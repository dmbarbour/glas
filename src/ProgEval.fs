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
        // However, the current implementation is checking for arity errors and manipulating 
        // a stack representation at runtime instead of preallocating memory. We currently 
        // do not optimize the program. Acceleration, memoization, and stowage are not yet 
        // supported. There is significant room for improvement.
        //
        // If performance proves adequate for bootstrap, I'll avoid touching this further.
        // But if needed, I'll explore other performance enhancements.

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

        /// simplest continuation
        type CC<'A> = RTE -> 'A 

        /// CTE - compile-time environment (excluding primary continuation)
        [<Struct>]
        type CTE<'R> = 
            { FK : CC<'R>           // on failure
            ; EH : Op<'R>           // effects handler
            ; TX : ITransactional   // top-level transaction interface.
            }
        and Op<'A> = CTE<'A> -> CC<'A> -> CC<'A> 


        let copy cte cc rte =
            match rte.DataStack with
            | (a::_) as ds -> cc { rte with DataStack = (a::ds) }
            | _ -> cte.FK rte

        let drop cte cc rte =
            match rte.DataStack with
            | (_::ds') -> cc { rte with DataStack = ds' }
            | _ -> cte.FK rte

        let swap cte cc rte = 
            match rte.DataStack with
            | (a::b::ds') -> cc { rte with DataStack = (b::a::ds') }
            | _ -> cte.FK rte

        let eq cte cc rte =  
            match rte.DataStack with
            | (a::b::ds') when (a = b) -> cc { rte with DataStack = ds' }
            | _ -> cte.FK rte

        let get cte cc rte =
            match rte.DataStack with
            | ((Bits k)::r::ds') ->
                match record_lookup k r with
                | Some v -> cc { rte with DataStack = (v::ds') }
                | _ -> cte.FK rte
            | _ -> cte.FK rte

        let put cte cc rte = 
            match rte.DataStack with
            | ((Bits k)::v::r::ds') -> 
                cc { rte with DataStack = ((record_insert k v r)::ds') }
            | _ -> cte.FK rte

        let del cte cc rte = 
            match rte.DataStack with
            | ((Bits k)::r::ds') ->
                cc { rte with DataStack = ((record_delete k r)::ds') }
            | _ -> cte.FK rte

        let pushl cte cc rte = 
            match rte.DataStack with
            | (v::(FTList l)::ds') ->
                cc { rte with DataStack = ((ofFTList (FTList.cons v l))::ds') }
            | _ -> cte.FK rte
        
        let popl cte cc rte = 
            match rte.DataStack with
            | (FTList (FTList.ViewL (v, l'))::ds') ->
                cc { rte with DataStack = (v::(ofFTList l')::ds') }
            | _ -> cte.FK rte

        let pushr cte cc rte = 
            match rte.DataStack with
            | (v::(FTList l)::ds') ->
                cc { rte with DataStack = ((ofFTList (FTList.snoc l v))::ds') }
            | _ -> cte.FK rte

        let popr cte cc rte =  
            match rte.DataStack with
            | ((FTList (FTList.ViewR (l', v)))::ds') ->
                cc { rte with DataStack = (v::(ofFTList l')::ds') }
            | _ -> cte.FK rte
        
        let join cte cc rte = 
            match rte.DataStack with 
            | ((FTList rhs)::(FTList lhs)::ds') ->
                cc { rte with DataStack = ((ofFTList (FTList.append lhs rhs))::ds') }
            | _ -> cte.FK rte

        let split cte cc rte =
            match rte.DataStack with
            | ((Nat n)::(FTList l)::ds') when (FTList.length l >= n) ->
                let (l1, l2) = FTList.splitAt n l
                cc { rte with DataStack = ((ofFTList l2)::(ofFTList l1)::ds') }
            | _ -> cte.FK rte

        let len cte cc rte =  
            match rte.DataStack with
            | ((FTList l)::ds') ->
                cc { rte with DataStack = ((nat (FTList.length l))::ds') }
            | _ -> cte.FK rte 

        let bjoin cte cc rte =
            match rte.DataStack with
            | ((Bits b)::(Bits a)::ds') ->
                cc { rte with DataStack = ((ofBits (Bits.append a b))::ds') }
            | _ -> cte.FK rte

        let bsplit cte cc rte = 
            match rte.DataStack with
            | ((Nat n)::(Bits ab)::ds') when (uint64 (Bits.length ab) >= n) ->
                let (a,b) = Bits.splitAt (int n) ab
                cc { rte with DataStack = ((ofBits b)::(ofBits a)::ds') }
            | _ -> cte.FK rte

        let blen cte cc rte = 
            match rte.DataStack with
            | ((Bits b)::ds') ->
                cc { rte with DataStack = ((nat (uint64 (Bits.length b)))::ds') }
            | _ -> cte.FK rte

        let bneg cte cc rte = 
            match rte.DataStack with
            | ((Bits b)::ds') ->
                cc { rte with DataStack = ((ofBits (Bits.bneg b))::ds') }
            | _ -> cte.FK rte

        let bmax cte cc rte = 
            match rte.DataStack with
            | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                cc { rte with DataStack = ((ofBits (Bits.bmax a b))::ds') }
            | _ -> cte.FK rte

        let bmin cte cc rte = 
            match rte.DataStack with
            | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                cc { rte with DataStack = ((ofBits (Bits.bmin a b))::ds') }
            | _ -> cte.FK rte
        
        let beq cte cc rte = 
            match rte.DataStack with
            | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                cc { rte with DataStack = ((ofBits (Bits.beq a b))::ds') }
            | _ -> cte.FK rte

        let add cte cc rte = 
            match rte.DataStack with
            | ((Bits n2)::(Bits n1)::ds') ->
                let struct(sum,carry) = Arithmetic.add n1 n2
                cc { rte with DataStack = ((ofBits carry)::(ofBits sum)::ds') }
            | _ -> cte.FK rte

        let mul cte cc rte = 
            match rte.DataStack with
            | ((Bits n2)::(Bits n1)::ds') ->
                let struct(prod,overflow) = Arithmetic.mul n1 n2
                cc { rte with DataStack = ((ofBits overflow)::(ofBits prod)::ds') }
            | _ -> cte.FK rte

        let sub cte cc rte = 
            match rte.DataStack with
            | ((Bits n2)::(Bits n1)::ds') ->
                match Arithmetic.sub n1 n2 with
                | Some diff -> cc { rte with DataStack = ((ofBits diff)::ds') }
                | None -> cte.FK rte
            | _ -> cte.FK rte

        let div cte cc rte = 
            match rte.DataStack with
            | ((Bits divisor)::(Bits dividend)::ds') ->
                match Arithmetic.div dividend divisor with
                | Some struct(quotient, remainder) ->
                    cc { rte with DataStack = ((ofBits remainder)::(ofBits quotient)::ds') }
                | None -> cte.FK rte
            | _ -> cte.FK rte

        let fail cte _ = // rte implicit
            cte.FK 

        let eff cte = // cc rte implicit
            (cte.EH) cte

        // symbolic ops except for 'eff' are covered here
        let opMap<'A> : Map<Bits,Op<'A>> =
            [ (lCopy, copy); (lDrop, drop); (lSwap, swap)
            ; (lEq, eq); (lFail, fail); (lEff, eff)
            ; (lGet, get); (lPut, put); (lDel, del)
            ; (lPushl, pushl); (lPopl, popl); (lPushr, pushr); (lPopr, popr)
            ; (lJoin, join); (lSplit, split); (lLen, len)
            ; (lBJoin, bjoin); (lBSplit, bsplit); (lBLen, blen)
            ; (lBNeg, bneg); (lBMax, bmax); (lBMin, bmin); (lBEq, beq)
            ; (lAdd, add); (lMul, mul); (lSub, sub); (lDiv, div)
            ] |> Map.ofList

        // We can logically flatten 'dip:P' into the sequence:
        //   dipBegin P dipEnd
        // We use the extra runtime dip stack to temporarily store the
        // top item from the data stack.
        let dipBegin cte cc rte =  
            match rte.DataStack with
            | (a::ds') -> 
                cc { rte with DataStack = ds'; DipStack = (a::(rte.DipStack)) }
            | _ -> cte.FK rte // error in program

        let dipEnd _ cc rte =
            match rte.DipStack with
            | (a::dip') ->
                cc { rte with DataStack = (a::(rte.DataStack)); DipStack = dip' }
            | _ -> failwith "(internal compile error) imbalanced dip stack"

        let dip (opDip : Op<'A>) cte cc =
            dipBegin cte (opDip cte (dipEnd cte cc))

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
            | None -> failwith "(internal compile error) imbalanced transaction"

        let commitTX cte cc rte = 
            match rte.FailureStack with
            | Some priorTX ->
                cte.TX.Commit()
                cc { rte with FailureStack = priorTX.FailureStack }
            | None -> failwith "(internal compile error) imbalanced transaction"

        let beginTX cte cc rte = 
            cte.TX.Try()
            cc { rte with FailureStack = Some rte }
                
        let cond<'A> (opTry:Op<'A>) (opThen:Op<'A>) (opElse:Op<'A>) cte cc =
            let ccElse = abortTX cte (opElse cte cc)
            let ccThen = commitTX cte (opThen cte cc)
            beginTX cte (opTry { cte with FK = ccElse } ccThen)

        let loop<'A> (opWhile:Op<'A>) (opDo:Op<'A>) cte cc0 =
            let ccHalt = abortTX cte cc0
            let cycleRef = ref ccHalt
            let ccRepeat rte = (!cycleRef) rte
            let ccDo = commitTX cte (opDo cte ccRepeat) 
            let ccWhile = beginTX cte (opWhile { cte with FK = ccHalt } ccDo)
            cycleRef := ccWhile // close the loop
            ccWhile

        // special operator to retrieve data from the eff-state stack.
        let effStatePop cte cc rte =
            match rte.EffStateStack with
            | (a::es') ->
                cc { rte with DataStack = (a::(rte.DataStack)); EffStateStack = es' }
            | _ -> failwith "(internal compile error) imbalanced effects handler stack"
        
        // special operator to push data to the eff-state stack.
        let effStatePush cte cc rte =
            match rte.DataStack with
            | (a::ds') ->
                cc { rte with DataStack = ds'; EffStateStack = (a::(rte.EffStateStack)) }
            | _ -> cte.FK rte // arity error in program

        let env<'A> (opWith:Op<'A>) (opDo:Op<'A>) cte0 cc0 =
            let eh0 = cte0.EH // restore parent effect in context of opWith
            let eh' cte cc = (effStatePop cte (opWith { cte with EH = eh0 } (effStatePush cte cc)))
            effStatePush cte0 (opDo { cte0 with EH = eh'} (effStatePop cte0 cc0))

        let pseq<'A> (ops:FTList<Op<'A>>) cte cc0 =
            FTList.foldBack (fun op cc -> op cte cc) ops cc0

        let rec compile<'A> (p:Program) : Op<'A> =
            match p with
            | Op opSym -> 
                match Map.tryFind opSym opMap with
                | Some op -> op
                | None -> failwithf "(internal compile error) unhandled op %s" (prettyPrint p)
            | Dip p' -> dip (compile p') 
            | Data v -> data v 
            | PSeq s -> pseq (FTList.map compile s)
            | Cond (pTry, pThen, pElse) -> cond (compile pTry) (compile pThen) (compile pElse)
            | Loop (pWhile, pDo) -> loop (compile pWhile) (compile pDo) 
            | Env (pWith, pDo) -> env (compile pWith) (compile pDo) 
            | Prog (_, p') -> 
                // memoization, stowage, or acceleration could be annotated here.
                compile p' 
            | _ -> failwithf "unrecognized program %s" (prettyPrint p)

        let ioEff (io:IEffHandler) cte cc rte =
            match rte.DataStack with
            | (request::ds') ->
                match io.Eff request with
                | Some response ->
                    cc { rte with DataStack = (response::ds') }
                | _ -> cte.FK rte
            | _ -> cte.FK rte

        let dataStack ds = 
            { DataStack = ds
            ; DipStack = []
            ; EffStateStack = []
            ; FailureStack = None
            }

        let eval (p:Program) (io:IEffHandler) =
            let cc rte = 
                assert((List.isEmpty rte.DipStack)
                    && (List.isEmpty rte.EffStateStack)
                    && (Option.isNone rte.FailureStack)) 
                Some (rte.DataStack)
            let cte = { FK = (fun _ -> None); EH = ioEff io; TX = io }
            let run = compile p cte cc
            run << dataStack

    /// The current favored implementation of eval.
    let eval : Program -> Effects.IEffHandler -> Value list -> Value list option = 
        FTI.eval

