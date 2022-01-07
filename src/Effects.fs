namespace Glas

module Effects = 

    /// Support for hierarchical transactions. This mostly applies to the external
    /// effects handler in context of Glas programs.
    ///
    /// Note: This API is incompatible with *parallel* transactions, which require
    /// multi-stage commit with potential failure so we can backtrack and retry. We
    /// ultimately use locks to control background effects. 
    type ITransactional = 
        interface
            /// The 'Try' operation is called to start a hierarchical transaction. This
            /// will implicitly be child of previously started, unconcluded transaction.
            abstract member Try : unit -> unit

            /// The most recently started, unconcluded transaction is concluded by calling
            /// one of Commit or Abort. If Try has been called many times, each Try must be
            /// concluded independently and Commit simply adds the child transaction's actions
            /// to its parent. 
            abstract member Commit : unit -> unit
            abstract member Abort : unit -> unit
        end

    /// RAII support for transactions, to ensure they're closed in case of exception.
    ///   USAGE:  use tx = withTX effHandler; ...; tx.Commit(); (or Abort)
    /// If not explicitly commited or aborted, is implicitly aborted upon Dispose.
    type OpenTX =
        val private TX : ITransactional
        val mutable private Closed : bool
        new(tx : ITransactional) = 
            tx.Try () 
            { TX = tx; Closed = false } 
        member private tx.Close() =
            if tx.Closed then invalidArg (nameof tx) "already committed or aborted" else
            tx.Closed <- true
        member tx.Commit () = 
            tx.Close()
            tx.TX.Commit()
        member tx.Abort () = 
            tx.Close()
            tx.TX.Abort()
        interface System.IDisposable with
            member tx.Dispose() = 
                if tx.Closed then () else 
                tx.Abort()

    let inline withTX (hasTX : 'T when 'T :> ITransactional) = 
        new OpenTX (hasTX :> ITransactional)

    /// For top-level effects, we must support both the effect requests and the
    /// conditional backtracking behavior (i.e. hierarchical transactions).
    type IEffHandler =
        interface
            inherit ITransactional

            /// Called with a value to represent a top-level side-effect. An effect
            /// request may be unrecognized or otherwise fail, in which case 'None'
            /// value should be returned. On success, a 'Some' value must be returned,
            /// though it may be unit.
            ///
            /// Glas effects are also transactional, via the ITransactional interface.
            ///  
            abstract member Eff : Value -> Value option
        end


    /// No effects. PRequests fail and transactions are ignored.
    let noEffects =
        { new IEffHandler with
            member __.Eff _ = None
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
        }

    /// Rewrite and filter effects functionally. Also defers transactions
    /// until an effect passes the filter.
    type RewriteEff =
        val private WrappedEff : IEffHandler
        val private RewriteReq : Value -> Value option
        val mutable private TXDepth : int 
        new(io, rw) = 
            { WrappedEff = io
            ; RewriteReq = rw
            ; TXDepth = 0
            }
        interface ITransactional with
            member self.Try () =
                self.TXDepth <- self.TXDepth + 1
            member self.Commit () =
                if (self.TXDepth > 0)
                    then self.TXDepth <- self.TXDepth - 1
                    else self.WrappedEff.Commit ()
            member self.Abort () =
                if (self.TXDepth > 0)
                    then self.TXDepth <- self.TXDepth - 1
                    else self.WrappedEff.Abort ()
        interface IEffHandler with
            member self.Eff req =
                match self.RewriteReq req with
                | None -> None
                | Some req' ->
                    while 0 < self.TXDepth do
                        self.TXDepth <- self.TXDepth - 1
                        self.WrappedEff.Try ()
                    self.WrappedEff.Eff req'

    /// Rewrite and/or filter effects functionally. Defers transactions until an 
    /// effect is processed. 
    let rewriteEffects (fn : Value -> Value option) (io:IEffHandler) : IEffHandler =
        RewriteEff(io,fn) :> IEffHandler

    /// Defer transactions until any effect is requested. This can help optimize
    /// performance in context of mostly-pure computations. However, it is not the
    /// best way to optimize (which should involve static analysis of effects).
    let deferTry io =
        rewriteEffects Some io

    /// Given a header, only accept effects of form `header:Request`, then
    /// remove the header before passing the request to the next effect handler.
    let selectHeader (header : string) =
        rewriteEffects (Value.(|Variant|_|) header)


    /// Apply effects to a, then fallback to b only if a fails.
    /// Transactions apply to both effect handlers.
    ///
    /// Note: this implementation doesn't scale nicely to many
    /// effects. Fine if it's just a few, though.
    let composeEff (a : IEffHandler) (b : IEffHandler)  =
        { new IEffHandler with
            member __.Eff msg =
                let aResult = a.Eff msg
                if Option.isSome aResult then aResult else
                b.Eff msg
          interface ITransactional with
            member __.Try () = try b.Try() finally a.Try() 
            member __.Commit () = try a.Commit() finally b.Commit()  
            member __.Abort () = try a.Abort() finally b.Abort()
        }


    /// Environment Variables (Read Only)
    ///
    /// Exposure of environment variables via 'env:get:"VARIABLE"' and 'env:list'.
    /// This is mostly useful for GLAS_PATH and HOME (e.g. to find config files). 
    ///
    /// This presents a read-only API, which avoids complicated questions such as
    /// how module lookup and caching should be affected by changes to GLAS_PATH. 
    /// I'm not aware of any strong use-case for mutation of environment variables.
    let envVarEff () =
        { new IEffHandler with
            member __.Eff req =
                match req with
                | Value.Variant "env" envOp ->
                    match envOp with
                    | Value.Variant "get" (Value.String k) ->
                        let v = System.Environment.GetEnvironmentVariable(k)
                        if isNull v then None else 
                        Some (Value.ofString v)
                    | Value.Variant "list" Value.U ->
                        System.Environment.GetEnvironmentVariables().Keys 
                        |> Seq.cast<string>
                        |> Seq.map (Value.ofString)
                        |> FTList.ofSeq
                        |> Value.ofFTList
                        |> Some
                    // Note: file path separator moved to 'dir:sep' effect.
                    // Note: could potentially support 
                    //  machine name
                    //  process ID
                    //  ...
                    | _ -> None
                | _ -> None
         interface ITransactional with
            member __.Try() = ()
            member __.Commit() = ()
            member __.Abort() = ()
        }

    /// Time Effects
    ///
    /// Transactions are logically instantaneous thus cannot sleep or wait. But they can
    /// observe time and abort if run too early or too late. This handler implements the
    /// 'time:now' and 'time:check' effects. After time is requested within a transaction,
    /// all further requests return the same value until the transaction completes.
    type TimeEff =
        val mutable private TXTime : uint64 option
        val mutable private TXDepth : int
        new () = 
            { TXTime = None
            ; TXDepth = 0
            }

        member private self.PushTX() =
            self.TXDepth <- self.TXDepth + 1
        member private self.PopTX() =
            assert(0 < self.TXDepth)
            self.TXDepth <- self.TXDepth - 1
            if(0 = self.TXDepth) then
                self.TXTime <- None
        member private self.Now() =
            match self.TXTime with
            | Some tNow -> tNow // time is frozen within transaction 
            | None -> 
                let tNow = System.DateTime.UtcNow.ToFileTime() |> uint64
                if (0 < self.TXDepth) then
                    self.TXTime <- Some tNow
                tNow

        interface ITransactional with
            member self.Try () = self.PushTX()
            member self.Commit () = self.PopTX()
            member self.Abort () = self.PopTX()
        
        interface IEffHandler with
            member self.Eff req =
                match req with
                | Value.Variant "time" timeReq ->
                    match timeReq with
                    | Value.Variant "now" Value.U ->
                        Some (self.Now() |> Value.nat)
                    | Value.Variant "check" (Value.Nat tMin) ->
                        if self.Now() >= tMin 
                            then Some Value.unit 
                            else None
                    | _ -> None
                | _ -> None

    let timeEff () = 
        TimeEff() :> IEffHandler

    /// Random bits, with fresh bits between transactions.
    type RandomBits =
        val mutable private Buffer : Bits       // bits currently available for reading
        val mutable private StaleBits : int     // count of recycled bits from backtracking
        val mutable private TXHist : Bits list  // bits remembered for future backtracking
        new () = 
            { Buffer = Bits.empty
            ; StaleBits = 0
            ; TXHist = List.empty
            }

        member inline private self.Remember b =
            match self.TXHist with
            | (bs::txs) -> 
                // remember choices in case of backtracking 
                self.TXHist <- ((Bits.cons b bs)::txs)
            | [] -> () // no need for memory outside of transactions

        // forget observed bits over time, keep the buffer fresh
        member inline private self.ExitTX () =
            self.TXHist <- []
            self.Buffer <- Bits.skip self.StaleBits self.Buffer
            self.StaleBits <- 0

        member self.ReadBit () =
            if Bits.isEmpty self.Buffer then
                // take random bits from dotnet
                self.Buffer <- Bits.random 4096
            let b = Bits.head self.Buffer
            self.Buffer <- Bits.tail self.Buffer
            self.StaleBits <- max 0 (self.StaleBits - 1)
            self.Remember b
            b

        member self.ReadBits n =
            let mutable bs = Bits.empty
            for _ in 1 .. n do
                bs <- Bits.cons (self.ReadBit()) bs
            bs

        interface ITransactional with
            member self.Try () =
                self.TXHist <- (Bits.empty :: self.TXHist)
            member self.Commit () =
                match self.TXHist with
                | [_] -> self.ExitTX()
                | (tx0::tx1::txs) -> 
                    // remember tx0 in parent transaction
                    self.TXHist <- ((Bits.append tx0 tx1)::txs)
                | [] ->
                    failwith "commit outside of transaction" 
            member self.Abort () =
                match self.TXHist with
                | [_] -> self.ExitTX()
                | (tx0::txs) ->
                    // backtrack tx0 in parent transaction
                    self.TXHist <- txs
                    self.Buffer <- Bits.append (Bits.rev tx0) self.Buffer
                    self.StaleBits <- Bits.length tx0 + self.StaleBits
                | [] ->
                    failwith "abort outside of transaction"

        interface IEffHandler with
            member self.Eff req =
                match req with
                | Value.Nat n when (n < uint64 System.Int32.MaxValue) ->
                    self.ReadBits (int n) |> Value.ofBits |> Some
                | _ -> None

    let randomEff () =
        selectHeader "random" (RandomBits())


    /// Non-Deterministic Fork Effect
    ///
    /// Ideally, fork is optimized with incremental computing and replication. This allows
    /// stable forks to represent multiple concurrent threads and tasks. Currently, just 
    /// returns random bits. This has correct formal behavior, but lacks the performance
    /// to effectively model concurrency.
    let forkEff () =
        let rw req =
            match req with
            | Value.Variant "fork" Value.U -> Some (Value.nat 1UL)
            | _ -> None
        rewriteEffects rw (RandomBits()) 

    /// Shared State Communications for Background Effects 
    ///
    /// We can use a value reference as a shared-state interchange between a program and a
    /// runtime's background task. The effects API will abstract state from the program to
    /// protect invariants. 
    ///
    /// Synchronization uses the dotnet Monitor class: the reference is locked (via Enter,
    /// Exit) when used within a transaction, and is Pulsed upon commit. The assumption is
    /// that a background thread occasionally interacts with the shared state to receive
    /// requests and provide responses.
    ///
    /// This design is simplistic. No backtracking is required, but scalability is limited.
    /// Despite some limits, this design is adequate for bootstrap of Glas systems and will
    /// be used for network and filesystem effects.
    ///
    /// A finalizer may be provided to halt the background thread. 
    type SharedStateEff<'I, 'O, 'S> =
        val private State       : 'S ref
        val private ParseReq    : Value -> 'I option
        val private Action      : 'I -> 'S -> ('O * 'S) option
        val private PrintResp   : 'O -> Value
        // The 'ParseReq' can filter some requests. Until a relevant request is observed, we
        // use an integer to track logical stack depth. Together, these allow locking to be
        // deferred until necessary.
        val mutable private TXStack : Choice<int, (struct('S * 'S list))>

        new(state, parser, action, writer) = 
            { State = state
            ; ParseReq = parser
            ; Action = action
            ; PrintResp = writer
            ; TXStack = Choice1Of2 0
            }

        member private self.ReadState () =
            match self.TXStack with
            | Choice1Of2 n when (n > 0) ->
                // unlock later, on transaction commit/abort
                System.Threading.Monitor.Enter(self.State) 
                let s = self.State.Value
                self.TXStack <- Choice2Of2 struct(s, List.replicate (n - 1) s)
                s
            | Choice2Of2 struct(s,_) ->
                s
            | _ -> 
                // internal error (should not occur)
                failwith "must read from inside a transaction"

        member private self.WriteState s =
            match self.TXStack with
            | Choice2Of2 struct(_, ss) ->
                self.TXStack <- Choice2Of2 struct(s,ss)
            | _ ->
                // internal error (should not occur)
                failwith "must write after read within transaction"


        interface ITransactional with
            member self.Try () =
                match self.TXStack with
                | Choice1Of2 n ->
                    self.TXStack <- Choice1Of2 (n + 1)
                | Choice2Of2 struct(s,ss) ->
                    self.TXStack <- Choice2Of2 struct(s,s::ss)
            member self.Commit () =
                match self.TXStack with
                | Choice1Of2 n ->
                    assert (n > 0)
                    self.TXStack <- Choice1Of2 (n - 1)
                | Choice2Of2 struct(s,(_::ss)) ->
                    self.TXStack <- Choice2Of2 struct(s,ss)
                | Choice2Of2 struct(s,[]) ->
                    self.State.Value <- s
                    System.Threading.Monitor.PulseAll(self.State)
                    System.Threading.Monitor.Exit(self.State)
                    self.TXStack <- Choice1Of2 0
            member self.Abort () =
                match self.TXStack with
                | Choice1Of2 n ->
                    assert (n > 0)
                    self.TXStack <- Choice1Of2 (n - 1)
                | Choice2Of2 struct(_, (s::ss)) ->
                    self.TXStack <- Choice2Of2 struct(s,ss)
                | Choice2Of2 struct(_, []) ->
                    System.Threading.Monitor.Exit(self.State)
                    self.TXStack <- Choice1Of2 0

        interface IEffHandler with
            member self.Eff v =
                match self.ParseReq v with
                | None -> None
                | Some i ->
                    use tx = withTX self
                    let s = self.ReadState()
                    match self.Action i s with
                    | Some (o, s') ->
                        self.WriteState s'
                        tx.Commit()
                        Some (self.PrintResp o)
                    | None ->
                        tx.Abort()
                        None

    /// Transactional Logging Support
    /// 
    /// Logging requires special attention in context of hierarchical transactions
    /// and backtracking. We want to keep log messages from the aborted path because
    /// they remain valuable for debugging. However, we should distinguish recanted
    /// messages, e.g. by wrapping or flagging them.
    type TXLogSupport =
        val private WriteLog : Value -> unit
        val private Recant : FTList<Value> -> FTList<Value>
        val mutable private TXStack : FTList<Value> list

        /// Set the committed output destination. Default behavior for aborted
        /// transactions is to add a 'recant' flag to every message.
        new (out) = 
            let recantMsg = Value.record_insert (Value.label "recant") (Value.unit)
            { WriteLog = out
            ; Recant = FTList.map recantMsg
            ; TXStack = [] 
            }

        /// Set the commited output destination and the function for
        /// rewriting logged messages upon abort. 
        new (out, recantFn) = 
            { WriteLog = out
            ; Recant = recantFn
            ; TXStack = [] 
            }

        member self.Log(msg : Value) : unit =
            match self.TXStack with
            | (tx0::txs) ->
                self.TXStack <- (FTList.snoc tx0 msg)::txs
            | [] -> 
                // not in a transaction, forward to wrapped eff
                self.WriteLog msg

        member self.PushTX () : unit = 
            self.TXStack <- (FTList.empty) :: self.TXStack

        member self.PopTX (bCommit : bool) : unit =
            match self.TXStack with
            | [] -> invalidOp "pop empty transaction stack"
            | (tx0::tx0Rem) ->
                let tx0' = if bCommit then tx0 else self.Recant tx0
                match tx0Rem with
                | (tx1::txs) -> // commit into lower transaction
                    self.TXStack <- (FTList.append tx1 tx0')::txs 
                | [] -> // commit to output
                    self.TXStack <- List.empty
                    for msg in FTList.toSeq tx0' do
                        self.WriteLog msg

        interface IEffHandler with
            member self.Eff v =
                match v with
                | Value.Variant "log" msg -> 
                    self.Log(msg)
                    Some Value.unit
                | _ -> None
        interface ITransactional with
            member self.Try () = self.PushTX ()
            member self.Commit () = self.PopTX true
            member self.Abort () = self.PopTX false

    /// Option for TXLogSupport in case we want to wrap messages instead of flagging them. 
    /// Wrapping is more efficient and preserves some information about transaction structure.
    /// OTOH, it's more difficult to read or process compared to a flat stream of messages.
    let recantWrap vs =
        let isRecanted = Value.record_lookup (Value.label "recant") >> Option.isSome 
        let allRecanted = not (Seq.exists (isRecanted >> not) (FTList.toSeq vs))
        if allRecanted then vs else
        FTList.singleton (Value.variant "recant" (Value.ofFTList vs))

    /// The convention for log messages is an ad-hoc record where fields
    /// are useful for routing, filtering, etc. and ad-hoc standardized.
    /// 
    ///   (lv:warn, text:"something bad happened")
    ///
    /// This design is intended for extensibility of text with new context
    /// and of log events with new variants.
    let logText lv msg =
        Value.asRecord ["lv"; "text"] [Value.symbol lv; Value.ofString msg]
    
    let logTextV lv msg v =
        Value.asRecord ["lv";"text";"val"] [Value.symbol lv; Value.ofString msg; v]

    // common log levels
    let info = "info"
    let warn = "warn"
    let error = "error"
    
    // log:Msg effect. This sends the log message, ignores the result.
    // Thus, IEffHandler support for logging is optional.
    let log (ll:IEffHandler) (msg:Value) : unit =
        ignore <| ll.Eff(Value.variant "log" msg)

    let logInfo ll msg = log ll (logText info msg)
    let logWarn ll msg = log ll (logText warn msg)
    let logError ll msg = log ll (logText error msg)

    let logInfoV ll msg v = log ll (logTextV info msg v)
    let logWarnV ll msg v = log ll (logTextV warn msg v)
    let logErrorV ll msg v = log ll (logTextV error msg v)

    let private selectColor v =
        match v with
        | Value.FullRec ["recant"] ([_], _) -> 
            System.ConsoleColor.DarkMagenta
        | Value.FullRec ["lv"] ([lv],_) ->
            match lv with
            | Value.Variant "info" _ -> System.ConsoleColor.Green
            | Value.Variant "warn" _ -> System.ConsoleColor.Yellow
            | Value.Variant "error" _ -> System.ConsoleColor.Red
            | _ -> 
                // randomly associate color with non-standard level
                // but keep it stable per level
                match (hash lv) % 8 with
                | 0 -> System.ConsoleColor.Magenta
                | 1 -> System.ConsoleColor.Cyan
                | 2 -> System.ConsoleColor.DarkGreen
                | 3 -> System.ConsoleColor.DarkBlue
                | 4 -> System.ConsoleColor.Blue
                | 5 -> System.ConsoleColor.DarkCyan
                | 6 -> System.ConsoleColor.DarkYellow
                | _ -> System.ConsoleColor.DarkRed
        | _ -> System.ConsoleColor.Blue

    /// Log Output to Console StdErr (with color!)
    let consoleErrLogOut (vMsg:Value) : unit =
        let cFG0 = System.Console.ForegroundColor
        System.Console.ForegroundColor <- selectColor vMsg
        try 
            let sMsg = Value.prettyPrint vMsg
            System.Console.Error.WriteLine(sMsg)
        finally
            System.Console.ForegroundColor <- cFG0

    let consoleErrLogger () : IEffHandler =
        TXLogSupport(consoleErrLogOut) :> IEffHandler






