namespace Glas

module Effects = 
    open System.Threading.Tasks

    /// Support for hierarchical transactions. This mostly applies to the external
    /// effects handler in context of Glas programs.
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


    /// No effects. Requests fail and transactions are ignored.
    let noEffects =
        { new IEffHandler with
            member __.Eff _ = None
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
        }

    /// Apply effects to a, then fallback to b if a fails.
    /// This can be used for lightweight routing of effects.
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

    /// As wrapFail except we may change the `fail` label.
    let wrapFailTag lbl =
        let k = Value.label lbl
        let startsWithLbl v = Option.isSome (Value.tryMatchStem k v)
        fun msgList -> 
            if Seq.forall startsWithLbl (FTList.toSeq msgList) 
                then msgList // flatten if empty list or all log messages are failures
                else msgList |> Value.ofFTList |> Value.variant lbl |> FTList.singleton
    
    /// wrapFail is the default rewrite function for transactional logging support.
    /// Primarily, we'll rewrite a list of aborted log messages to a single message
    /// of form `fail:MsgList`. There is also logic to flatten a stack of failures. 
    let wrapFail = wrapFailTag "fail"

    /// Transactional Logging Support
    /// 
    /// This wraps a naive logging IEffHandler with some code to support
    /// transactional capture of logged messages. 
    /// 
    /// Logging requires special attention in context of hierarchical
    /// transactions and backtracking. We should keep the log messages
    /// because they are precious for debugging. However, which log
    /// messages are committed or aborted should also be clear to the
    /// client.
    ///
    /// The default method to group aborted log messages under 
    /// `fail:[MessageList]`. Users may override this behavior, 
    /// e.g. for contexts where label 'fail' is used.
    ///
    /// At the top-level, a function can be provided to receive the 
    /// logged messages when they become available.
    type TXLogSupport =
        val private WriteLog : Value -> unit
        val private WrapFail : FTList<Value> -> FTList<Value>
        val mutable private TXStack : FTList<Value> list

        /// Set the committed output destination. 
        /// Wrap aborted messages with `fail:MessageList`.
        new (out) = 
            { WriteLog = out; WrapFail = wrapFail; TXStack = [] }

        /// Set the commited output destination and the rewrite
        /// function for handling aborted aborted messages. 
        new (out, rewriteF) = 
            { WriteLog = out; WrapFail = rewriteF; TXStack = [] }

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
                let tx0' = if bCommit then tx0 else self.WrapFail tx0
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

    /// The convention for log messages is an ad-hoc record where fields
    /// are useful for routing, filtering, etc. and ad-hoc standardized.
    /// 
    ///   (lv:warn, text:"something happened")
    ///
    /// This design is intended for extensibility of text with new context
    /// and of log events with new variants.
    let logText lv msg =
        Value.asRecord ["lv"; "text"] [Value.symbol lv; Value.ofString msg]

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


    let private selectColor v =
        match v with
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
        | Value.Variant "fail" _ ->  System.ConsoleColor.DarkMagenta
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


    (*
    /// Transactional Stream (Wrapper)
    /// 
    /// Adds backtracking support to a dotnet Stream. Within a transaction, 
    /// writes are deferred and reads are recorded for undo. Reads are 
    type TXStream =
        // The stream being wrapped.
        val private Stream : System.IO.Stream

        // Bytes available for read due to prior, aborted transactions are
        // put back into the ReadBuffer for future reads.
        val mutable private ReadBuffer : FTList<byte>

        // Bytes currently being read by a background task are stored into
        // an intermediate buffer. 
        val mutable private BGReadBuffer : FTList<byte>

        // The stack of hierarchical transactions. Each transaction records 
        // bytes (written, read). 
        val mutable private TXStack : struct(FTList<byte> * FTList<byte>) list

        // A potential pending read operation, in case a prior read has timed out
        // or returned a partial result.
        val mutable private PendingRead : Task<FTList<byte>> option

        new(stream) =
            { Stream = stream
            ; ReadBuffer = FTList.empty
            ; TXStack = []
            ; PendingRead = None
            }

        member private self.AwaitPendingRead(wait_millis : int) : unit =
            match self.PendingRead with
            | Some task when task.Wait(wait_millis) ->
                self.PendingRead <- None
                self.ReadBuffer <- FTList.append (self.ReadBuffer) (task.Result)
            | _ -> ()

        member private self.SetPartialRead() : unit =
            // in case of a partial read, if there is no pending read we can
            // add a fake one to represent the partial read. This prevents 
            // further reads within the current transaction.
            if Option.isNone self.PendingRead then
                self.PendingRead <- Some (Task.FromResult (FTList.empty))

        member private self.TakeReadBuffer() =
            let result = self.ReadBuffer
            self.ReadBuffer <- FTList.empty
            result

        member private self.PartialRead(amt : uint64, wait_millis : int) : FTList<byte> =
            if List.isEmpty self.TXStack then
                // outside of a transaction, we can continue pending reads.
                self.AwaitPendingRead(wait_millis) 

            if (FTList.length self.ReadBuffer >= amt) then
                // sufficient data from prior aborted reads
                let (result,rem) = FTList.splitAt amt (self.ReadBuffer)
                self.ReadBuffer <- rem
                result
            elif (Option.isSome self.PendingRead) then
                // pending read, thus cannot start a new read
                self.TakeReadBuffer()
            else 
                // start a new read task.
                let rdAmt = amt - FTList.length self.ReadBuffer
                let readTask = 
                    async {
                        let arr = Array.zeroCreate (int rdAmt)
                        let rdCt = self.Stream.Read(arr,0,arr.Length)
                        let bytes = if (arr.Length = rdCt) then arr else Array.take rdCt arr
                        return bytes |> FTList.ofArray
                    } |> Async.StartAsTask 
                self.PendingRead <- Some readTask
                self.AwaitPendingRead(wait_millis)
                self.TakeReadBuffer()

        member self.Read(amt : int, ?wait_millis : int) : byte[] =
            if (amt < 0) then invalidArg (nameof amt) "read amt must be >= 0" else
            let millis = defaultArg wait_millis 10
            let bytesRead = self.PartialRead(uint64 amt, millis)
            if (FTList.length bytesRead < uint64 amt) then
                self.SetPartialRead() 
            match self.TXStack with
            | struct(wl,rl)::txs ->
                let rl' = FTList.append rl bytesRead
                self.TXStack <- struct(wl,rl')::txs
            | _ -> ()
            FTList.toArray bytesRead
            

        member self.Write(data : byte []) : unit =
            if not self.Stream.CanWrite then invalidOp "stream does not permit writes" else
            match self.TXStack with
            | struct(wl,rd)::txs ->
                let wl' = FTList.append wl (FTList.ofArray data)
                self.TXStack <- struct(wl',rd)::txs
            | [] -> // non-transactional write
                self.Stream.Write(data, 0, data.Length)

        interface ITransactional with
            member self.Try () =
                if List.isEmpty self.TXStack then
                    self.AwaitPendingRead(10)  // try to finish reads prior to transaction
                self.TXStack <- struct(FTList.empty, FTList.empty) :: self.TXStack
            member self.Commit () =
                match self.TXStack with
                | struct(w0,r0)::struct(ws,rs)::txs ->
                    // join hierarchical transaction
                    let ws' = FTList.append ws w0
                    let rs' = FTList.append rs r0
                    self.TXStack <- struct(ws',rs')::txs
                | [struct(wl,_)] ->
                    // commit transaction
                    self.TXStack <- []
                    let arr = FTList.toArray wl
                    self.Stream.Write(arr,0,arr.Length)
                | [] -> invalidOp "cannot commit, no active transaction."
            member self.Abort () = 
                match self.TXStack with
                | struct(_, r0)::txs ->
                    // putback reads performed by this transaction.
                    self.TXStack <- txs
                    self.ReadBuffer <- FTList.append r0 (self.ReadBuffer)
                | [] -> invalidOp "cannot abort, no active transaction."

    *)




