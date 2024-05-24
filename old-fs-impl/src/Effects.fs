namespace Glas

// NOTE: I originally intended to support all effects needed to `--run` a
// simple application, such as network and filesystem access. But I'd rather
// not implement effects twice, so I'm deferring this until after bootstrap.
// We can bootstrap using `glas --extract` with no effects other than module
// system and basic logging.

module Effects = 

    /// Support for hierarchical transactions.
    type ITransactional = 
        interface
            /// The 'Try' operation is called to start a hierarchical transaction. This
            /// will implicitly be child of previously started, unconcluded transaction.
            abstract member Try : unit -> unit

            // To support a two-phase commit, we call Precommit before Commit. If the
            // Precommit returns false, we should Abort instead. This is necessary for
            // interacting with other transaction systems. 
            // abstract member Precommit : unit -> bool

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
            abstract member Eff : Value -> Value voption
        end

    // For 'pure' functions, we'll halt on first *attempt* to use effects.
    exception ForbiddenEffectException of Value
    let forbidEffects = {   
        new IEffHandler with
            member __.Eff v = 
                raise <| ForbiddenEffectException(v)
        interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
    }

    /// No effects. All requests fail, but it isn't treated as a type error.
    let noEffects =
        { new IEffHandler with
            member __.Eff _ = ValueNone
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
        }

    /// Try effect from 'b' only if effect from 'a' fails (ValueNone)
    let fallbackEffect (a:IEffHandler) (b:IEffHandler) : IEffHandler =
        { new IEffHandler with
            member __.Eff v =
                match a.Eff v with
                | ValueNone -> b.Eff v
                | aResult -> aResult 
          interface ITransactional with
            member __.Try () = a.Try(); b.Try()
            member __.Commit () = a.Commit(); b.Commit()
            member __.Abort () = a.Abort(); b.Abort()
        }

    /// Transactional Logging Support
    /// 
    /// Logging requires special attention in context of hierarchical transactions
    /// and backtracking. We want to keep log messages from the aborted path because
    /// they remain valuable for debugging. However, we should distinguish recanted
    /// messages, e.g. by wrapping or flagging them.
    ///
    /// A simple default option is to add a '~' flag to every message that is
    /// transactionally aborted.
    type TXLogSupport =
        val private WriteLog : Value -> unit
        val private Recant : Rope -> Rope
        val mutable private TXStack : Rope list

        /// Set the committed output destination. Default behavior for aborted
        /// transactions is to insert a '~' flag to every message.
        new (out) = 
            let recantMsg = Value.record_insert (Value.label "~") (Value.unit)
            { WriteLog = out
            ; Recant = Value.Rope.map recantMsg
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
                self.TXStack <- (Value.Rope.snoc tx0 msg)::txs
            | [] -> 
                // not in a transaction, forward to wrapped eff
                self.WriteLog msg

        member self.PushTX () : unit = 
            self.TXStack <- (Value.Rope.empty) :: self.TXStack

        member self.PopTX (bCommit : bool) : unit =
            match self.TXStack with
            | [] -> invalidOp "pop empty transaction stack"
            | (tx0::tx0Rem) ->
                let tx0' = if bCommit then tx0 else self.Recant tx0
                match tx0Rem with
                | (tx1::txs) -> // commit into lower transaction
                    self.TXStack <- (Value.Rope.append tx1 tx0')::txs 
                | [] -> // commit to output
                    self.TXStack <- List.empty
                    for msg in Value.Rope.toSeq tx0' do
                        self.WriteLog msg

        interface IEffHandler with
            member self.Eff v =
                match v with
                | Value.Variant "log" msg -> 
                    self.Log(msg)
                    ValueSome Value.unit
                | _ -> ValueNone
        interface ITransactional with
            member self.Try () = self.PushTX ()
            member self.Commit () = self.PopTX true
            member self.Abort () = self.PopTX false

    /// The convention for log messages is an ad-hoc record where fields
    /// are useful for routing, filtering, etc. and ad-hoc standardized.
    /// 
    ///   (lv:warn, text:"something bad happened")
    ///
    /// This design is intended for extensibility of text with new context
    /// and of log events with new variants.
    let logText lv msg =
        Value.asRecord ["lv"; "text"] [Value.symbol lv; Value.ofString msg]
    
    let logTextV lv txtMsg vMsg =
        Value.asRecord ["lv";"text";"msg"] [Value.symbol lv; Value.ofString txtMsg; vMsg]

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
        | Value.FullRec ["~"] ([_], _) -> 
            System.ConsoleColor.DarkMagenta
        | Value.FullRec ["lv"] ([lv],_) ->
            match lv with
            | Value.Variant "info" _ -> System.ConsoleColor.Green
            | Value.Variant "warn" _ -> System.ConsoleColor.Yellow
            | Value.Variant "error" _ -> System.ConsoleColor.Red
            | Value.Variant "prof" _ -> System.ConsoleColor.Blue
            | _ -> 
                // randomly associate color with non-standard level
                match (hash (lv.Stem)) % 6 with
                | 0 -> System.ConsoleColor.Magenta
                | 1 -> System.ConsoleColor.DarkGreen
                | 2 -> System.ConsoleColor.DarkBlue
                | 3 -> System.ConsoleColor.Blue
                | 4 -> System.ConsoleColor.DarkCyan
                | 5 -> System.ConsoleColor.DarkYellow
                | _ -> System.ConsoleColor.DarkRed
        | _ -> System.ConsoleColor.Cyan

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


    type BinaryWriterSupport =
        val private WriteFrag : uint8 array -> unit
        val mutable private TXStack : ((uint8 array) list) list

        /// Set the committed output destination. Default behavior for aborted
        /// transactions is to insert a '~' flag to every message.
        new (out) = 
            { WriteFrag = out
            ; TXStack = [] 
            }

        member self.Write(msg : uint8 array) : unit =
            match self.TXStack with
            | (tx0::txs) ->
                self.TXStack <- ((msg::tx0)::txs)
            | [] ->
                self.WriteFrag msg

        member self.PushTX () : unit = 
            self.TXStack <- [] :: self.TXStack

        member self.PopTX (bCommit : bool) : unit =
            match self.TXStack with
            | (_::txs) when not bCommit ->
                self.TXStack <- txs
            | (tx0::tx1::txs) ->
                self.TXStack <- (List.append tx0 tx1)::txs
            | (tx0::[]) ->
                self.TXStack <- []
                tx0 |> List.rev |> List.iter self.WriteFrag
            | [] -> invalidOp "pop empty transaction"

        interface IEffHandler with
            member self.Eff v =
                match v with
                | Value.Variant "write" (Value.BinaryArray msg) -> 
                    self.Write(msg)
                    ValueSome Value.unit
                | _ -> ValueNone
        interface ITransactional with
            member self.Try () = self.PushTX ()
            member self.Commit () = self.PopTX true
            member self.Abort () = self.PopTX false




    // given log+load effects, return full runtime effects
    //
    // For now, I've decided to mostly elide this to after bootstrap rather than
    // implement it twice (once in F# and once within the glas module system). 
    let runtimeEffects (ll : IEffHandler) : IEffHandler =
        ll // TODO!

    // given log+load effects, add a 'write' effect for extraction.
    let extractionEffects (ll : IEffHandler) : IEffHandler = 
        let stdout = System.Console.OpenStandardOutput()
        let writeFrag b = stdout.Write(b,0,b.Length)
        fallbackEffect (BinaryWriterSupport(writeFrag)) ll


(*
module Effects2 =
    // I'm thinking about an alternative effects API to support partial-evaluation
    // of effects. This can reduce runtime overheads related to routing of effects.
    // The cost is a significant increase in complexity.
    // 
    // Relatedly, transactions can be isolated insofar as we can precisely identify
    // which effects are in use. Worst case the effect is wholly opaque, in which 
    // case we'll add everything to the transaction same as a top-level composite of
    // IEffHandler objects.

    // Effects will need some internal state of arbitrary data types (buffers, etc.).
    // To simplify partial evaluation, we'll handle this at a fine granularity, work
    // with a set of transactional variables or components instead of a single global
    // handler.
    type ITransactional = 
        interface

            /// The 'Try' operation is called to start a hierarchical transaction. This
            /// will implicitly be child of previously started, unconcluded transaction.
            abstract member Try : unit -> unit

            /// The most recently started, unconcluded transaction is concluded by calling
            /// one of Commit or Abort. If Try has been called many times, each Try must be
            /// concluded independently and Commit adds to the parent transaction.
            abstract member Commit : unit -> unit
            abstract member Abort : unit -> unit
        end

    // The simplest transactional variable. No concurrency!
    // This can be useful to help build effects handlers.
    // Similar to a Ref<'a> except with ITransactional interface.
    type TXVar<'a> =
        val mutable Value  : 'a
        val mutable private TXHist : 'a list
        new(ini) = { Value = ini; TXHist = [] }
        interface ITransactional with
            member v.Try() =
                v.TXHist <- v.Value :: v.TXHist
            member v.Commit() =
                // keep Value.
                v.TXHist <- List.tail v.TXHist
            member v.Abort() =
                v.Value <- List.head v.TXHist
                v.TXHist <- List.tail v.TXHist

    let txvar a = 
        TXVar<_>(a)

    // A set of TXVars or other components with ITransactional interface.
    type TXVars = ITransactional list

    // this only exists due to limitation on IL that prevents direct
    // use of for..in loops within a constructor.
    let private onOpenTX (vars : TXVars) =
        for v in vars do
            v.Try()

    // RAII support for transactions, operating on a TXVars collection.
    // Note: it's best to apply `List.distinct` on TXVars ahead of time.
    type OpenTX =
        val private TXVars : TXVars
        val mutable private Closed : bool
        new(vars : TXVars) = 
            onOpenTX vars
            { TXVars = vars; Closed = false } 
        member private tx.Close() =
            if tx.Closed then invalidArg (nameof tx) "already committed or aborted" else
            tx.Closed <- true
        member tx.Commit () = 
            tx.Close()
            for v in tx.TXVars do
                v.Commit()
        member tx.Abort () = 
            tx.Close()
            for v in tx.TXVars do
                v.Abort()
        interface System.IDisposable with
            member tx.Dispose() = 
                if not tx.Closed then 
                    tx.Abort()

    // Partial values moved to another module.
    type PVal = PartialValue.AbsVal
    type VarId = PartialValue.VarId

    // Generic variable access. The input and output variables will be
    // in separate namespaces (i.e. writing does not affect reads).
    type IVarAccess = 
        interface 
            abstract member Read : VarId -> Value
            abstract member Write : VarId -> Value -> unit
        end

    // When we partially evaluate the effect, we'll produce a partial value
    // as output, a specialized effect handler, and a set of external vars 
    // to be protected transactionally such as TXVars.
    //
    // The handler only has access to virtualized internal memory, so we can
    // easily protect that via the abstract interface.
    //
    // The partial value may specify arbitrary output variables. These will
    // be mapped to memory with a level of indirection via hashtable. Any
    // writes to variables not in the PVal will fail. Missing writes have
    // undefined behavior.
    //
    // Note: Read-only variables for an effect don't need to be protected
    // because we currently aren't supporting concurrent transactions.
    type FutureEffect =
        { Output        : PVal                  // partial output (or maybe constant)
        ; Protect       : TXVars                // transaction variables to protect
        ; Handle        : IVarAccess -> bool     // bool returns success/fail.
        }

    // If an effect will *always* fail, we return ValueNone instead of the
    // future effect. This simplifies a few optimizations. Compiling the
    // effect should be idempotent and cacheable, but may allocate some
    // extra resources as needed to run the effect in the future. 
    type CompileEffect = PVal -> FutureEffect voption



    // Local memory access with separate reads and writes.
    //
    // Prior to handling effect, fill inputs via SetParam. 
    // After handling effect, access outputs via GetResult.
    //
    // Based on dictionaries to minimize allocations.
    type LocalMemAccess = 
        val private RTable : System.Collections.Generic.Dictionary<VarId, Value>
        val private WTable : System.Collections.Generic.Dictionary<VarId, Value>
        new() = 
            { RTable = new System.Collections.Generic.Dictionary<VarId, Value>()
            ; WTable = new System.Collections.Generic.Dictionary<VarId, Value>()
            }
        member m.SetParam ix v =
            m.RTable[ix] <- v
        member m.GetResult ix =
            m.WTable[ix]
        member m.Reset() =
            m.RTable.Clear()
            m.WTable.Clear()
        interface IVarAccess with
            member m.Read ix =
                m.RTable[ix]
            member m.Write ix v =
                m.WTable[ix] <- v

    // Prepare to run an effect as a one-off transaction. This version can
    // support a partial input, with the actual values provided in a later
    // argument. 
    let runPartialEffect (eh : CompileEffect) (pv : PVal) : (VarId -> Value) -> Value voption =
        match eh pv with
        | ValueNone -> 
            fun _ -> ValueNone
        | ValueSome fe ->
            let rdVars = pv |> PartialValue.listVars |> List.distinct |> List.sort
            let txVars = List.distinct (fe.Protect)
            fun fnReadVar ->
                let va = new LocalMemAccess()
                for ix in rdVars do
                    va.SetParam ix (fnReadVar ix)
                use tx = new OpenTX(txVars)
                let ok = fe.Handle va
                if not ok then ValueNone else
                let vResult = PartialValue.fill (va.GetResult) (fe.Output) 
                tx.Commit() // commit AFTER fill in case of exception.
                ValueSome vResult

    // Run effects filtered on a known label such as 'log'. The provided
    // value is whatever would follow the 'log' label.
    let runLabeledEffect (eh : CompileEffect) (label : Value) : Value -> Value voption =
        match eh (PartialValue.labelVar label 0) with
        | ValueNone -> 
            fun _ -> ValueNone
        | ValueSome fe ->
            let txVars = List.distinct (fe.Protect)
            fun arg ->
                let va = new LocalMemAccess()
                va.SetParam 0 arg
                use tx = new OpenTX(txVars)
                let ok = fe.Handle va
                if not ok then ValueNone else
                let vResult = PartialValue.fill (va.GetResult) (fe.Output)
                tx.Commit()
                ValueSome vResult

    // This compiles the effects with a fully abstract value. 
    let runEffect eh = 
        runLabeledEffect eh (Value.unit)

    // This compiles the effects with a constant argument. 
    let runConstantEffect (eh : CompileEffect) (arg : Value) : unit -> Value voption =
        match eh (PartialValue.Const arg) with
        | ValueNone ->
            fun () -> ValueNone
        | ValueSome fe ->
            let txVars = List.distinct (fe.Protect)
            fun () ->
                let va = new LocalMemAccess()
                use tx = new OpenTX(txVars)
                let ok = fe.Handle va
                if not ok then ValueNone else
                let vResult = PartialValue.fill (va.GetResult) (fe.Output)
                tx.Commit()
                ValueSome vResult


    // To support CompileEffects, I'll need the ability to easily 'match'
    // on partial values. This requires some careful attention, e.g. a 
    // 'log:(Var 0)' partial value can be a possible match against a 
    // '(Var 0)' input but not against a 'file:(...)' partial value input.
    //
    // I'll perhaps come back to this later.

*)