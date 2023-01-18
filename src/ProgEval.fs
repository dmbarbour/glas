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

        let type_error rte vType =
            raise <| RTError(rte, Value.variant "type-error" vType)

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
            // profiling support?
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

        let halt msg cte cc rte =
            raise <| RTError(rte, ProgVal.Halt msg)

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

        // stow (first item on data stack)
        let stow vOpts cte cc =  
            // not yet implemented
            // value type currently does not include stowage
            cc

        let memoize prog vOpts (compiledProg : Op) : Op =
            // bootstrap might benefit from memoization. But we'll need
            // stowage, first. Without stowage, we cannot efficiently
            // memoize on large values such as subprogram fragments.
            compiledProg


        let profile prog vOpts (lzOp : Lazy<Op>) : Op =
            // todo: add some profiling support
            lzOp.Force()

        module Accel =

            // List accelerators are the most important for bootstrap.
            // However, a few arithmetic accelerators are also useful.

            let rec bits_negate_term t =
                match t with
                | Leaf -> Leaf
                | Stem64(bits, t') ->
                    Stem64(~~~bits, bits_negate_term t')
                | _ -> failwith "input is not bits"
            
            let bits_negate_stem n =
                let lb = StemBits.lenbit (StemBits.len n)
                let ndb = (lb ||| (lb - 1UL))
                ((~~~n) &&& (~~~ndb)) ||| (n &&& ndb)

            let bits_negate bits =
                { Stem = bits_negate_stem bits.Stem 
                ; Term = bits_negate_term bits.Term
                }

            let accel_bits_negate cte cc rte =
                match rte.DataStack with
                | ((Bits b)::ds') ->
                    let b' = bits_negate b
                    cc { rte with DataStack = ((ofBits b')::ds') }
                | (_::_) -> type_error rte (Value.symbol "bits")
                | _ -> underflow rte

            let rec bits_reverse_append acc bits =
                if not (isStem bits) then acc else
                let acc' = consStemBit (stemHead bits) acc
                let bits' = stemTail bits
                bits_reverse_append acc' bits'

            let accel_bits_reverse_append cte cc rte =
                match rte.DataStack with
                | ((Bits bits)::(Bits acc)::ds') -> 
                    let result = bits_reverse_append acc bits
                    cc { rte with DataStack = ((ofBits result)::ds') }
                | (_::_::_) -> type_error rte (Value.symbol "bits")
                | _ -> underflow rte
            
            let accel_bits_verify cte cc rte =
                match rte.DataStack with
                | (v::ds') -> if isBits v then cc rte else cte.FK rte
                | _ -> underflow rte

            let accel_nat_add (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                |  ((Nat64 n)::(Nat64 m)::ds') when ((System.UInt64.MaxValue - n) >= m) ->
                    cc { rte with DataStack = ((Value.ofNat (m + n))::ds') }
                | _ -> lzOp.Force() cte cc rte

            let accel_nat_sub (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (n > m) then cte.FK rte else
                    cc { rte with DataStack = ((Value.ofNat (m - n))::ds') }
                | _ -> lzOp.Force() cte cc rte

            let accel_nat_mul (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    try 
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) n m
                        //printfn "accel_nat_mul %d*%d=%d" m n prod
                        cc { rte with DataStack = ((Value.ofNat prod)::ds') }
                    with
                    | :? System.OverflowException ->
                        lzOp.Force() cte cc rte
                | _ -> lzOp.Force() cte cc rte

            let accel_nat_divmod (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 divisor)::(Nat64 dividend)::ds') ->
                    if (0UL = divisor) then cte.FK rte else
                    let struct(quot,rem) = System.Math.DivRem(dividend,divisor)
                    // printfn "accel_nat_divmod %d %d => %d %d" dividend divisor quot rem
                    let dsResult = (Value.ofNat rem)::(Value.ofNat quot)::ds'
                    cc { rte with DataStack = dsResult }
                | _ -> lzOp.Force() cte cc rte

            let accel_nat_gt (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m > n) then cc rte else cte.FK rte
                | _ -> lzOp.Force() cte cc rte

            let accel_nat_gte (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m >= n) then cc rte else cte.FK rte
                | _ -> lzOp.Force() cte cc rte 

            let accel_int_increment (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::ds') when (n <> System.Int64.MaxValue) ->
                    cc { rte with DataStack = ((Value.ofInt (n + 1L))::ds') }
                | _ -> lzOp.Force() cte cc rte

            let accel_int_decrement (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::ds') when (n <> System.Int64.MinValue) ->
                    cc { rte with DataStack = ((Value.ofInt (n - 1L))::ds') }
                | _ -> lzOp.Force() cte cc rte
            
            let accel_int_add (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try 
                        let sum = Microsoft.FSharp.Core.Operators.Checked.(+) m n
                        cc { rte with DataStack = ((Value.ofInt sum)::ds') }
                    with
                    | :? System.OverflowException ->
                        lzOp.Force() cte cc rte
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_int_mul (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) m n
                        cc { rte with DataStack = ((Value.ofInt prod)::ds') }
                    with 
                    | :? System.OverflowException ->
                        lzOp.Force() cte cc rte
                | _ -> 
                    lzOp.Force() cte cc rte

            let accel_int_gt (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m > n) then cc rte else cte.FK rte
                | _ -> lzOp.Force() cte cc rte

            let accel_int_gte (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m >= n) then cc rte else cte.FK rte
                | _ -> lzOp.Force() cte cc rte 

            let accel_list_verify cte cc rte =
                match rte.DataStack with
                | (v::_) -> if Value.isList v then cc rte else cte.FK rte
                | _ -> underflow rte
            
            let accel_list_length (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((List l)::ds') ->
                    let nLen = Value.Rope.len l
                    cc { rte with DataStack = (Value.ofNat nLen)::ds' }
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_list_append (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((List r)::(List l)::ds') ->
                    let result = Rope.append l r
                    cc { rte with DataStack = (Value.ofTerm result)::ds' }
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_list_take (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(List l)::ds') ->
                    if (n > (Rope.len l)) then cte.FK rte else
                    let result = Rope.take n l
                    cc { rte with DataStack = (Value.ofTerm result)::ds' }
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_list_skip (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(List l)::ds') ->
                    if (n > (Rope.len l)) then cte.FK rte else
                    let result = Rope.drop n l
                    cc { rte with DataStack = (Value.ofTerm result)::ds' }
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_list_item (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((Nat64 n)::(List l)::ds') ->
                    if (n >= (Rope.len l)) then cte.FK rte else
                    let result = Rope.item n l
                    cc { rte with DataStack = (result::ds') }
                | _ ->
                    lzOp.Force() cte cc rte

            let accel_list_cons (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | ((List l)::v::ds') ->
                    let result = Rope.cons v l
                    cc { rte with DataStack = ((Value.ofTerm result)::ds') }
                | _ ->
                    lzOp.Force() cte cc rte
            
            let accel_list_snoc (lzOp : Lazy<Op>) cte cc rte =
                match rte.DataStack with
                | (v::(List l)::ds') ->
                    let result = Rope.snoc l v 
                    cc { rte with DataStack = ((Value.ofTerm result)::ds') }
                | _ ->
                    lzOp.Force() cte cc rte


            let tryAccel (prog : Program) (vModel : Value) (lzOp : Lazy<Op>) : Op option  =
                match vModel with
                | Variant "bits-verify" U ->
                    Some accel_bits_verify
                | Variant "bits-negate" U -> 
                    Some accel_bits_negate
                | Variant "bits-reverse-append" U -> 
                    Some accel_bits_reverse_append
                | Variant "bits-length" U ->
                    None 
                | Variant "bits-take" U ->
                    None 
                | Variant "bits-skip" U ->
                    None
                // could also add bits-and, bits-xor, etc.
                | Variant "nat-add" U -> 
                    Some (accel_nat_add lzOp)
                | Variant "nat-sub" U -> 
                    Some (accel_nat_sub lzOp)
                | Variant "nat-mul" U -> 
                    Some (accel_nat_mul lzOp)
                | Variant "nat-divmod" U -> 
                    Some (accel_nat_divmod lzOp)
                | Variant "nat-gte" U ->
                    Some (accel_nat_gte lzOp)
                | Variant "nat-gt" U ->
                    Some (accel_nat_gt lzOp)
                | Variant "int-increment" U -> 
                    Some (accel_int_increment lzOp)
                | Variant "int-decrement" U -> 
                    Some (accel_int_decrement lzOp)
                | Variant "int-add" U -> 
                    Some (accel_int_add lzOp)
                | Variant "int-sub" U -> 
                    None 
                | Variant "int-mul" U -> 
                    Some (accel_int_mul lzOp)
                | Variant "int-divmod" U -> 
                    None 
                | Variant "int-gte" U ->
                    Some (accel_int_gte lzOp)
                | Variant "int-gt" U ->
                    Some (accel_int_gt lzOp)
                | Variant "list-verify" U ->
                    Some (accel_list_verify)
                | Variant "list-length" U ->
                    Some (accel_list_length lzOp)
                | Variant "list-append" U ->
                    Some (accel_list_append lzOp)
                | Variant "list-take" U ->
                    Some (accel_list_take lzOp)
                | Variant "list-skip" U ->
                    Some (accel_list_skip lzOp)
                | Variant "list-item" U ->
                    Some (accel_list_item lzOp)
                | Variant "list-cons" U ->
                    Some (accel_list_cons lzOp)
                | Variant "list-snoc" U ->
                    Some (accel_list_snoc lzOp)
                | _ -> 
                    None

        let accelerate (p : Program) (vModel : Value) (lazyOp : Lazy<Op>) : Op =
            match vModel with
            | Variant "opt" vModel' ->
                // optional acceleration
                match Accel.tryAccel p vModel' lazyOp with
                | Some op -> op
                | None -> lazyOp.Force()
            | _ ->
                // require acceleration
                match Accel.tryAccel p vModel lazyOp with
                | Some op -> op
                | _ -> halt (Value.variant "accel" vModel)

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
            | Prog (anno, p') -> 
                // annotations may specify acceleration, memoization, stowage, or
                // other performance features. 
                //
                // Thoughts: It might also be useful to cache compilation at 'prog'
                // boundaries. However, I'll need another intermediate stage to make
                // that work well.
                let lazyCompile = 
                    let addAccel (lzOp : Lazy<Op>) = 
                        match anno with
                        | (Record ["accel"] ([ValueSome vModel], _)) ->
                            lazy (accelerate p' vModel lzOp) 
                        | _ -> lzOp 
                    let addMemo (lzOp : Lazy<Op>) = 
                        match anno with
                        | (Record ["memo"] ([ValueSome vOpts], _)) ->
                            lazy (memoize p' vOpts (lzOp.Force()))
                        | _ -> lzOp
                    let addStow (lzOp : Lazy<Op>) =
                        match anno with
                        | (Record ["stow"] ([ValueSome vOpts], _)) ->
                            // assumption: stow usually annotates a nop program.
                            // for now, just sequence stowage after the program.
                            lazy (pseq [| lzOp.Force(); stow vOpts |])
                        | _ -> lzOp
                    let addProf (lzOp : Lazy<Op>) =
                        match anno with
                        | (Record ["prof"] ([ValueSome vOpts], _)) ->
                            lazy (profile p' vOpts lzOp)
                        | _ -> lzOp
                    lazy (compile p') |> addAccel |> addMemo |> addStow |> addProf
                lazyCompile.Force()
            | Stem lHalt msg -> halt msg
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
