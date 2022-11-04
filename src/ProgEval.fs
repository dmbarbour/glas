namespace Glas

module ProgEval =
    open Value
    open ProgVal

    module FTI =
        open Effects

        // A simple finally tagless interpreter.
        //
        // The benefit of FTI over direct style is that we avoid runtime parsing within loops,
        // and the resulting eval function is stable and accessible to .NET JIT compiler. 
        //
        // This implementation uses lazy compilation for conditional behavior. This ensures that
        // time-complexity of FTI compilation is not worse than direct style interpretation when
        // runtime evaluation covers a small portion of a large program.
        //
        // However, the current implementation is checking for arity errors and manipulating 
        // a stack representation at runtime instead of preallocating memory. We currently 
        // do not optimize the program. Acceleration, memoization, and stowage are not yet 
        // supported. There is much room for improvement.
        //
        // Additionally, I currently do not attempt to share structure within the compiled
        // program, e.g. by recognizing common subprograms. So, we're doing a lot of rework
        // here. Cached compilation could help with this.
        //
        // I intend to accelerate the list ops and maybe arithmetic ops for bootstrap. I'm still
        // uncertain how and whether I should check validity of the accelerators. It isn't the
        // case that the compiler must check this - it could be checked via explicit testing vs.
        // the non-accelerated implementation.
        //

        /// runtime environment
        ///
        /// Most operations focus on the data stack.
        /// The 'Dip' and 'EffState' stacks temporarily hide data.
        /// The FailureStack records snapshots of the RTE for recovery.
        [<Struct>]
        type RTE =
            { DataStack : Value list
            ; DipStack : Value list
            ; EffStateStack : Value list
            ; FailureStack : RTE option     
            } 

        // For halting on runtime errors of any sort. The value should carry some
        // extra information about cause.
        exception RTError of RTE * Value

        let underflow rte = 
            raise <| RTError(rte, Value.symbol "underflow")

        /// simplest continuation.
        type CC = RTE -> obj 

        // Note: I removed generics from this to keep the code simpler. Doesn't make
        // a big difference in any case, only need to box/unbox the final result.

        /// CTE - compile-time environment (excluding primary continuation)
        [<Struct>]
        type CTE = 
            { FK : CC               // on failure
            ; EH : Op               // effects handler
            ; TX : ITransactional   // top-level transaction interface.
            }
        and Op = CTE -> CC -> CC 

        let copy cte cc rte =
            match rte.DataStack with
            | (a::_) as ds -> cc { rte with DataStack = (a::ds) }
            | _ -> underflow rte


        let drop cte cc rte =
            match rte.DataStack with
            | (_::ds') -> cc { rte with DataStack = ds' }
            | _ -> underflow rte

        let swap cte cc rte =
            match rte.DataStack with
            | (a::b::ds') -> cc { rte with DataStack = (b::a::ds') }
            | _ -> underflow rte

        let eq cte cc rte =
            match rte.DataStack with
            | (a::b::ds') ->
                if Value.eq a b 
                    then cc { rte with DataStack = ds' }
                    else (cte.FK) rte
            | _ -> underflow rte

        
        let badLabel rte k =
            raise <| RTError(rte, Value.variant "invalid-label" k)

        let get cte cc rte =
            match rte.DataStack with
            | (kv::r::ds') ->
                match kv with
                | Bits k ->
                    match record_lookup k r with
                    | ValueSome v -> cc { rte with DataStack = (v::ds') }
                    | ValueNone -> (cte.FK) rte
                | _ -> badLabel rte kv
            | _ -> underflow rte

        let put cte cc rte =
            match rte.DataStack with
            | (kv::r::v::ds') -> 
                match kv with
                | Bits k -> cc { rte with DataStack = ((record_insert k v r)::ds') }
                | _ -> badLabel rte kv
            | _ -> underflow rte

        let del cte cc rte =
            match rte.DataStack with
            | (kv::r::ds') ->
                match kv with
                | Bits k -> cc { rte with DataStack = ((record_delete k r)::ds') }
                | _ -> badLabel rte kv
            | _ -> underflow rte

        let fail cte cc = // rte implicit
            (cte.FK) 

        let eff cte cc = // rte implicit
            (cte.EH) cte cc

        let tbd msg cte cc rte =
            raise <| RTError(rte, ProgVal.TBD msg)

        // We can logically flatten 'dip:P' into the sequence:
        //   dipBegin P dipEnd
        // We use the extra runtime dip stack to temporarily store the
        // top item from the data stack.
        let dipBegin cte cc rte =
            match rte.DataStack with
            | (a::ds') -> 
                cc { rte with DataStack = ds'; DipStack = (a::(rte.DipStack)) }
            | _ -> underflow rte

        let dipEnd _ cc rte =
            match rte.DipStack with
            | (a::dip') ->
                cc { rte with DataStack = (a::(rte.DataStack)); DipStack = dip' }
            | _ -> 
                // this should be unreachable
                failwith "(internal compile error) imbalanced dip stack"

        let dip (opDip : Op) cte cc0 =
            dipBegin cte (opDip cte (dipEnd cte cc0))

        let data (v:Value) cte cc rte =
            cc { rte with DataStack = (v::(rte.DataStack)) }

        // for conditional behaviors, we modify the failure handler within
        // the 'try' block. We also use the FailureStack to keep a snapshot
        // of the runtime environment to support backtracking.
        //
        // Finally, we'll use Try/Commit/Abort on the toplevel transactions
        // to support transactional effects. 

        let abortTX cte cc rte =
            match rte.FailureStack with
            | Some rte' ->
                cte.TX.Abort()
                cc rte'
            | None -> 
                // should be unreachable
                failwith "(internal compile error) imbalanced transaction"

        let commitTX cte cc rte =
            match rte.FailureStack with
            | Some priorTX ->
                cte.TX.Commit()
                cc { rte with FailureStack = priorTX.FailureStack }
            | None ->
                // should be unreachable 
                failwith "(internal compile error) imbalanced transaction"

        let beginTX cte cc rte =
            cte.TX.Try()
            cc { rte with FailureStack = Some rte }
                
        let cond (opTry:Op) (opThenLazy:Lazy<Op>) (opElseLazy:Lazy<Op>) cte cc =
            let ccCondThenLazy = lazy (commitTX cte ((opThenLazy.Force()) cte cc))
            let ccCondElseLazy = lazy (abortTX  cte ((opElseLazy.Force()) cte cc))
            let ccCondThen rte = (ccCondThenLazy.Force()) rte
            let ccCondElse rte = (ccCondElseLazy.Force()) rte
            beginTX cte (opTry { cte with FK = ccCondElse } ccCondThen)

        // Note for potential future headaches reduction:
        // 
        // F# doesn't do tail-call optimization by default in Debug mode!
        //
        // This gave me quite some trouble, trying to trace down why tailcalls were not
        // working as expected. I eventually solved by adding <Tailcalls>True</Tailcalls>
        // to the property group in the fsproj.
        let loopWhile (opWhile:Op) (opDoLazy:Lazy<Op>) cte cc0 =
            let cycleRef = ref cc0
            let ccWhileRepeat rte = (cycleRef.Value) rte
            let ccWhileDoLazy = lazy (commitTX cte ((opDoLazy.Force()) cte ccWhileRepeat))
            let ccWhileDo rte = (ccWhileDoLazy.Force()) rte
            let ccWhileHalt = abortTX cte cc0
            let ccWhileLoop = beginTX cte (opWhile { cte with FK = ccWhileHalt } ccWhileDo)
            cycleRef.Value <- ccWhileLoop // close the loop
            ccWhileLoop

        let loopUntil (opUntil:Op) (opDoLazy:Lazy<Op>) cte cc0 =
            let cycleRef = ref cc0
            let ccUntilRepeat rte = (cycleRef.Value) rte
            let ccUntilDoLazy = lazy (abortTX cte ((opDoLazy.Force()) cte ccUntilRepeat))
            let ccUntilDo rte = (ccUntilDoLazy.Force()) rte
            let ccUntilHalt = commitTX cte cc0
            let ccUntilLoop = beginTX cte (opUntil { cte with FK = ccUntilDo } ccUntilHalt)
            cycleRef.Value <- ccUntilLoop // close the loop
            ccUntilLoop

        // special operator to retrieve data from the eff-state stack.
        let effStatePop cte cc rte =
            match rte.EffStateStack with
            | (a::es') ->
                cc { rte with DataStack = (a::(rte.DataStack)); EffStateStack = es' }
            | _ -> 
                // should be unreachable
                failwith "(internal compile error) imbalanced effects handler stack"
        
        // special operator to push data to the eff-state stack.
        let effStatePush cte cc rte =
            match rte.DataStack with
            | (a::ds') ->
                cc { rte with DataStack = ds'; EffStateStack = (a::(rte.EffStateStack)) }
            | _ -> underflow rte

        let env (opWith:Op) (opDo:Op) cte0 cc0 =
            let eh0 = cte0.EH // restore parent effect in context of opWith
            let eh' cte cc = (effStatePop cte (opWith { cte with EH = eh0 } (effStatePush cte cc)))
            effStatePush cte0 (opDo { cte0 with EH = eh'} (effStatePop cte0 cc0))

        let pseq (ops : Op array) cte cc0 =
            let fn op cc = op cte cc
            Array.foldBack fn ops cc0

        let rec compile (p:Program) : Op =
            match p with
            | Stem lCopy U -> copy
            | Stem lDrop U -> drop
            | Stem lSwap U -> swap
            | Stem lEq U -> eq
            | Stem lFail U -> fail
            | Stem lEff U -> eff
            | Stem lGet U -> get
            | Stem lPut U -> put
            | Stem lDel U -> del
            | Dip p' -> dip (compile p')
            | Data v -> data v 
            | PSeq (ValueArray ops) -> 
                pseq (Array.map compile ops) 
            | Cond (pTry, pThen, pElse) ->
                cond (compile pTry) (lazy (compile pThen)) (lazy (compile pElse))
            | While (pWhile, pDo) ->
                loopWhile (compile pWhile) (lazy (compile pDo))
            | Until (pUntil, pDo) ->
                loopUntil (compile pUntil) (lazy (compile pDo))
            | Env (pWith, pDo) ->
                env (compile pWith) (compile pDo) 
            | Prog (_, p') -> 
                // memoization, stowage, or acceleration could be annotated here.
                // TODO: acceleration of several useful functions!
                compile p' 
            | Stem lTBD msg -> tbd msg
            | _ -> 
                // not a valid program. This could be detected by analysis. But
                // if we skip analysis, it will be reported at runtime.
                fun cte cc rte ->
                    raise <| RTError(rte, Value.variant "invalid-subprogram" p)

        let ioEff (io:IEffHandler) cte cc rte =
            match rte.DataStack with
            | (request::ds') ->
                match io.Eff request with
                | ValueSome response ->
                    cc { rte with DataStack = (response::ds') }
                | ValueNone -> (cte.FK) rte
            | [] -> underflow rte

        let dataStack ds = 
            { DataStack = ds
            ; DipStack = []
            ; EffStateStack = []
            ; FailureStack = None
            }

        // cancel all active transactions, using RTE FailureStack as cue.
        let rec unwindTX (io:ITransactional) (rte:RTE) =
            match rte.FailureStack with
            | None -> ()
            | Some rte' ->
                io.Abort()
                unwindTX io rte'

        let rec rteVal rte =
            let dataStack = rte.DataStack |> Value.ofList
            let dipStack = rte.DataStack |> Value.ofList
            let effStack = rte.EffStateStack |> Value.ofList
            let failStack = 
                match rte.FailureStack with
                | Some rte' -> rteVal rte'
                | None -> Value.symbol "none"
            Value.asRecord ["data";"dip";"eff";"back"] [dataStack;dipStack;effStack;failStack]

        let rec rtErrMsg rte msg =
            Value.variant "rte" <| Value.asRecord ["state";"event"] [rteVal rte; msg]

        // Note that 'eval' does not check the program for validity up front.
        let eval (p:Program) (io:IEffHandler) =
            let ccEvalOK rte = 
                assert((List.isEmpty rte.DipStack)
                    && (List.isEmpty rte.EffStateStack)
                    && (Option.isNone rte.FailureStack)) 
                box (Some (rte.DataStack))
            let ccEvalFail rte = box None
            let cte = { FK = ccEvalFail; EH = ioEff io; TX = io }
            let runLazy = lazy ((compile p) cte ccEvalOK)
            fun ds ->
                try ds |> dataStack |> (runLazy.Force()) |> unbox<Value list option>
                with
                | RTError(rte,msg) ->
                    let v = rtErrMsg rte msg
                    unwindTX io rte
                    logErrorV io "runtime error" (rtErrMsg rte msg)
                    None

        // For 'pure' functions, we'll also halt on first *attempt* to use effects.
        exception ForbiddenEffectException of Value
        let forbidEffects = 
            { new IEffHandler with
                member __.Eff v = 
                    raise <| ForbiddenEffectException(v)
              interface ITransactional with
                member __.Try () = ()
                member __.Commit () = ()
                member __.Abort () = ()
            }
        let pureEval (p : Program) =
            let evalNoEff = eval p forbidEffects
            fun ds -> 
                try evalNoEff ds
                with 
                | ForbiddenEffectException _ -> None


    /// The current favored implementation of eval.
    let eval : Program -> Effects.IEffHandler -> Value list -> Value list option = 
        FTI.eval

    /// Evaluate except with any effect halting the application. 
    let pureEval : Program -> Value list -> Value list option =
        FTI.pureEval
