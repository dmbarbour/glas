namespace Glas

module Effects = 
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
        val private LogOut : Value -> unit
        val private WrapFail : FTList<Value> -> FTList<Value>
        val mutable private TXStack : FTList<Value> list

        /// Set the committed output destination. 
        /// Wrap aborted messages with `fail:MessageList`.
        new (out) = 
            { LogOut = out; WrapFail = wrapFail; TXStack = [] }

        /// Set the commited output destination and the rewrite
        /// function for handling aborted aborted messages. 
        new (out, rewriteF) = 
            { LogOut = out; WrapFail = rewriteF; TXStack = [] }

        member self.Log(msg : Value) : unit =
            match self.TXStack with
            | (tx0::txs) ->
                self.TXStack <- (FTList.snoc tx0 msg)::txs
            | [] -> 
                self.LogOut msg // commit immediately

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
                        self.LogOut msg

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

    /// Common log messages are of form `warn:(msg:"warning string")`.
    /// The intention is that we could extend the message with metadata.
    let logMsg hdr msg =
        Value.variant hdr (Value.variant "msg" (Value.ofString msg))

    // common log headers
    let info = "info"
    let warn = "warn"
    let error = "error"
    
    // log:Msg effect. This sends the log message, ignores the result.
    // Thus, support for logging is optional.
    let log (ll:IEffHandler) (msg:Value) : unit =
        ignore <| ll.Eff(Value.variant "log" msg)


    /// Log Output to Console
    ///
    /// This is intended to be a good 'default' behavior for writing 
    /// log outputs. Currently only recognizes info, warn, error strings
    /// and 'fail' with a list of log messages. Console colors for each.
    /// 

