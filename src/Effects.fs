namespace Glas

module Effects = 
    // NOTE: I originally intended to support all effects needed to `--run` an
    // implementation of Glas command line from within this program. That would
    // mostly require filesystem effects and environment variables.
    //
    // Later, I decided to defer support for `glas --run` until after bootstrap.
    // I will use a specialized `glas --extract` operation to perform bootstrap.
    // Mostly, I don't want to implement this twice, nor work around the design
    // decisions of the .NET API that are awkward for transaction machines, such
    // as blocking reads.
    //
    // A consequence is that effects required in this implementation are just
    // log and load, which are used by language modules.

    /// Support for hierarchical transactions.
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


    /// No effects. All requests fail. Transactions are ignored.
    let noEffects =
        { new IEffHandler with
            member __.Eff _ = None
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
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
        val private Recant : FTList<Value> -> FTList<Value>
        val mutable private TXStack : FTList<Value> list

        /// Set the committed output destination. Default behavior for aborted
        /// transactions is to insert a '~' flag to every message.
        new (out) = 
            let recantMsg = Value.record_insert (Value.label "~") (Value.unit)
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
        | Value.FullRec ["~"] ([_], _) -> 
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

    /// This should be full effects for 'glas --run'. But it's
    /// just a tiny subset at the moment.
    let runEffects () : IEffHandler =
        consoleErrLogger ()