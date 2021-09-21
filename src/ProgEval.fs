namespace Glas

module ProgEval =
    open Value
    open ProgVal

    module DSI =
        // A lightweight, direct-style interpreter for the Glas program model.
        // Perhaps useful as a reference and for getting started. However, this
        // interpreter is inefficient and awkward for extensions such as incremental
        // computing or debugging with stack traces.
        open Effects

        /// The interpreter's runtime environment. 
        [<Struct>]
        type RTE =
            { DS : Value list                     //< data stack
            ; ES : struct(Value * Program) list   //< env/eff stack
            ; IO : IEffHandler                    //< top-level effect
            }

        let inline copy e = 
            match e.DS with
            | x::_ -> Some { e with DS = x::(e.DS) }
            | _ -> None

        let inline drop e =
            match e.DS with
            | _::ds -> Some { e with DS = ds }
            | _ -> None

        let inline swap e = 
            match e.DS with
            | x::y::ds -> Some { e with DS = y::x::ds }
            | _ -> None

        let inline eq e = 
            match e.DS with 
            | x::y::ds when (x = y) -> Some { e with DS = ds }
            | _ -> None

        let inline get e = 
            match e.DS with
            | ((Bits k)::r::ds) ->
                match record_lookup k r with
                | Some v -> Some { e with DS = (v::ds) } 
                | None -> None
            | _ -> None

        let inline put e = 
            match e.DS with
            | ((Bits k)::v::r::ds) ->
                let r' = record_insert k v r
                Some { e with DS = (r'::ds) }
            | _ -> None

        let inline del e = 
            match e.DS with
            | ((Bits k)::r::ds) ->
                let r' = record_delete k r
                Some { e with DS = (r'::ds) }
            | _ -> None

        let inline pushl e = 
            match e.DS with
            | (v::(FTList l)::ds) ->
                let l' = FTList.cons v l
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline popl e = 
            match e.DS with
            | (FTList (FTList.ViewL (v,l')))::ds ->
                Some { e with DS = (v::(ofFTList l')::ds) }
            | _ -> None

        let inline pushr e = 
            match e.DS with
            | (v::(FTList l)::ds) -> 
                let l' = FTList.snoc l v
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline popr e = 
            match e.DS with
            | ((FTList (FTList.ViewR (l',v)))::ds) ->
                Some { e with DS = (v::(ofFTList l')::ds) }
            | _ -> None

        let inline join e = 
            match e.DS with
            | ((FTList l2)::(FTList l1)::ds) ->
                let l' = FTList.append l1 l2
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline split e =
            match e.DS with
            | ((Nat n)::(FTList l)::ds) when (FTList.length l >= n) ->
                let (l1,l2) = FTList.splitAt n l
                Some { e with DS = ((ofFTList l2)::(ofFTList l1)::ds) }
            | _ -> None
            
        let inline len e = 
            match e.DS with
            | ((FTList l)::ds) ->
                let len = nat (FTList.length l)
                Some { e with DS = (len::ds) }
            | _ -> None

        let inline bjoin e = 
            match e.DS with
            | ((Bits b)::(Bits a)::ds) ->
                let ab = Bits.append a b
                Some { e with DS = ((ofBits ab)::ds) }
            | _ -> None

        let inline bsplit e = 
            match e.DS with
            | ((Nat n)::(Bits ab)::ds) when (uint64 (Bits.length ab) >= n) ->
                let (a,b) = Bits.splitAt (int n) ab
                Some { e with DS = ((ofBits b)::(ofBits a)::ds) }
            | _ -> None

        let inline blen e = 
            match e.DS with
            | ((Bits b)::ds) -> 
                let len = nat (uint64 (Bits.length b))
                Some { e with DS = (len::ds) }
            | _ -> None

        let inline bneg e = 
            match e.DS with
            | ((Bits b)::ds) ->
                let b' = Bits.bneg b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline bmax e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.bmax a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline bmin e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.bmin a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline beq e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.beq a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline add e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                let struct(sum,carry) = Arithmetic.add n1 n2
                Some { e with DS = ((ofBits carry)::(ofBits sum)::ds) }
            | _ -> None

        let inline mul e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                let struct(prod,overflow) = Arithmetic.mul n1 n2
                Some { e with DS = ((ofBits overflow)::(ofBits prod)::ds) }
            | _ -> None

        let inline sub e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                match Arithmetic.sub n1 n2 with
                | Some diff -> Some { e with DS = ((ofBits diff)::ds) }
                | None -> None
            | _ -> None

        let inline div e = 
            match e.DS with
            | ((Bits divisor)::(Bits dividend)::ds) ->
                match Arithmetic.div dividend divisor with
                | Some struct(quotient,remainder) ->
                    Some { e with DS = ((ofBits remainder)::(ofBits quotient)::ds) }
                | None -> None
            | _ -> None

        let inline data v e = 
            Some { e with DS = (v::e.DS) }

        let rec eff e =
            match e.ES with
            | struct(v,p)::es ->
                let ep = { e with DS = (v::e.DS); ES = es; IO = e.IO }
                match interpret p ep with 
                | Some { DS = (v'::ds'); ES = es'; IO = io' } ->
                    Some { DS = ds'; ES = struct(v',p)::es'; IO = io' }
                | _ -> None
            | [] -> 
                match e.DS with
                | request::ds ->
                    match e.IO.Eff request with
                    | Some response ->
                        Some { e with DS = (response::ds) }
                    | None -> None
                | [] -> None
        and interpretOpMap =
            [ (lCopy, copy)
            ; (lDrop, drop)
            ; (lSwap, swap)
            ; (lEq, eq)
            ; (lFail, fun _ -> None)
            ; (lEff, eff)
            ; (lGet, get)
            ; (lPut, put)
            ; (lDel, del)
            ; (lPushl, pushl)
            ; (lPopl, popl)
            ; (lPushr, pushr)
            ; (lPopr, popr)
            ; (lJoin, join)
            ; (lSplit, split)
            ; (lLen, len)
            ; (lBJoin, bjoin)
            ; (lBSplit, bsplit)
            ; (lBLen, blen)
            ; (lBNeg, bneg)
            ; (lBMax, bmax)
            ; (lBMin, bmin)
            ; (lBEq, beq)
            ; (lAdd, add)
            ; (lMul, mul)
            ; (lSub, sub)
            ; (lDiv, div)
            ] |> Map.ofList
        and interpretOp (op:Bits) (e:RTE) : RTE option =
            match Map.tryFind op interpretOpMap with
            | Some fn -> fn e
            | None -> failwithf "unhandled op %s" (prettyPrint (ofBits op))
        and dip p e = 
            match e.DS with
            | (x::ds) -> 
                match interpret p { e with DS = ds } with
                | Some e' -> Some { e' with DS = (x::e'.DS) } 
                | None -> None
            | [] -> None
        and seq s e =
            match s with
            | FTList.ViewL (p, s') ->
                match interpret p e with
                | Some e' -> seq s' e'
                | None -> None
            | _ -> Some e
        and cond pTry pThen pElse e =
            use tx = withTX (e.IO) // backtrack effects in pTry
            match interpret pTry e with
            | Some e' -> tx.Commit(); interpret pThen e'
            | None -> tx.Abort(); interpret pElse e
        and loop pWhile pDo e = 
            use tx = withTX (e.IO) // backtrack effects in pWhile
            match interpret pWhile e with
            | Some e' -> 
                tx.Commit(); 
                match interpret pDo e' with
                | Some ef -> loop pWhile pDo ef
                | None -> None // failure in main loop body is elevated.
            | None -> tx.Abort(); Some e  // failure in loop condition ends loop.
        and env pWith pDo e = 
            match e.DS with
            | (v::ds) ->
                let eWith = { DS = ds; ES = struct(v,pWith)::e.ES; IO = e.IO }
                match interpret pDo eWith with
                | Some { DS = ds'; ES= struct(v',_)::es'; IO=io' } ->
                    Some { DS = (v'::ds'); ES=es'; IO=io' }
                | _ -> None
            | [] -> None
        and interpret (p:Program) (e:RTE) : RTE option =
            match p with
            | Op op -> interpretOp op e 
            | Dip p' -> dip p' e 
            | Data v -> data v e
            | PSeq s -> seq s e
            | Cond (pTry, pThen, pElse) -> cond pTry pThen pElse e
            | Loop (pWhile, pDo) -> loop pWhile pDo e 
            | Env (pWith, pDo) -> env pWith pDo e 
            | Prog (_, p') -> interpret p' e 
            | _ -> None

    module FTI =
        open Effects

        // A simple finally tagless interpreter.
        //
        // The main benefit is avoiding repeated parse actions within loops. Also, the
        // program structure is more accessible to .NET JIT. I'm hoping this will run
        // over an order of magnitude faster than direct-style interpretation.
        //
        // Potential variations:
        //  We could allocate stack locations in memory ahead of time. This would remove
        //  runtime checks for arity errors.


        // runtime environment
        [<Struct>]
        type RTE =
            { DS : Value list
            } 
        // compile-time environment
        [<Struct>]
        type CTE<'R> =
            { KS : RTE -> 'R    // on success
            ; KF : RTE -> 'R    // on failure
            }

        // Let's start with a continuation-passing approach to composing operations. I
        // use CTE and RTE to support extensibility of the environments. Currently, the
        // continuations should be compile-time, while data-stack is runtime.

        let copy cte =
            fun rte ->
                match rte.DS with
                | (a::_) as ds -> cte.KS { rte with DS = (a::ds) }
                | _ -> cte.KF rte

        let drop cte =
            fun rte ->
                match rte.DS with
                | (_::ds') -> cte.KS { rte with DS = ds' }
                | _ -> cte.KF rte

        let swap cte =
            fun rte ->
                match rte.DS with
                | (a::b::ds') -> cte.KS { rte with DS = (b::a::ds') }
                | _ -> cte.KF rte

        let eq cte =
            fun rte ->
                match rte.DS with
                | (a::b::ds') when (a = b) -> cte.KS { rte with DS = ds' }
                | _ -> cte.KF rte

        let get cte =
            fun rte ->
                match rte.DS with
                | ((Bits k)::r::ds') ->
                    match record_lookup k r with
                    | Some v -> cte.KS { rte with DS = (v::ds') }
                    | _ -> cte.KF rte
                | _ -> cte.KF rte

        let put cte =
            fun rte ->
                match rte.DS with
                | ((Bits k)::v::r::ds') -> 
                    cte.KS { rte with DS = ((record_insert k v r)::ds') }
                | _ -> cte.KF rte

        let del cte =
            fun rte ->
                match rte.DS with
                | ((Bits k)::r::ds') ->
                    cte.KS { rte with DS = ((record_delete k r)::ds') }
                | _ -> cte.KF rte

        let pushl cte =
            fun rte ->
                match rte.DS with
                | (v::(FTList l)::ds') ->
                    cte.KS { rte with DS = ((ofFTList (FTList.cons v l))::ds') }
                | _ -> cte.KF rte
        
        let popl cte =
            fun rte ->
                match rte.DS with
                | (FTList (FTList.ViewL (v, l'))::ds') ->
                    cte.KS { rte with DS = (v::(ofFTList l')::ds') }
                | _ -> cte.KF rte

        let pushr cte =
            fun rte ->
                match rte.DS with
                | (v::(FTList l)::ds') ->
                    cte.KS { rte with DS = ((ofFTList (FTList.snoc l v))::ds') }
                | _ -> cte.KF rte

        let popr cte =
            fun rte ->
                match rte.DS with
                | ((FTList (FTList.ViewR (l', v)))::ds') ->
                    cte.KS { rte with DS = (v::(ofFTList l')::ds') }
                | _ -> cte.KF rte
        
        let join cte =
            fun rte ->
                match rte.DS with 
                | ((FTList rhs)::(FTList lhs)::ds') ->
                    cte.KS { rte with DS = ((ofFTList (FTList.append lhs rhs))::ds') }
                | _ -> cte.KF rte

        let split cte =
            fun rte ->
                match rte.DS with
                | ((Nat n)::(FTList l)::ds') when (FTList.length l >= n) ->
                    let (l1, l2) = FTList.splitAt n l
                    cte.KS { rte with DS = ((ofFTList l2)::(ofFTList l1)::ds') }
                | _ -> cte.KF rte

        let len cte =
            fun rte ->
                match rte.DS with
                | ((FTList l)::ds') ->
                    cte.KS { rte with DS = ((nat (FTList.length l))::ds') }
                | _ -> cte.KF rte 

        let bjoin cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::(Bits a)::ds') ->
                    cte.KS { rte with DS = ((ofBits (Bits.append a b))::ds') }
                | _ -> cte.KF rte

        let bsplit cte =
            fun rte ->
                match rte.DS with
                | ((Nat n)::(Bits ab)::ds') when (uint64 (Bits.length ab) >= n) ->
                    let (a,b) = Bits.splitAt (int n) ab
                    cte.KS { rte with DS = ((ofBits b)::(ofBits a)::ds') }
                | _ -> cte.KF rte

        let blen cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::ds') ->
                    cte.KS { rte with DS = ((nat (uint64 (Bits.length b)))::ds') }
                | _ -> cte.KF rte

        let bneg cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::ds') ->
                    cte.KS { rte with DS = ((ofBits (Bits.bneg b))::ds') }
                | _ -> cte.KF rte

        let bmax cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                    cte.KS { rte with DS = ((ofBits (Bits.bmax a b))::ds') }
                | _ -> cte.KF rte

        let bmin cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                    cte.KS { rte with DS = ((ofBits (Bits.bmin a b))::ds') }
                | _ -> cte.KF rte
        
        let beq cte =
            fun rte ->
                match rte.DS with
                | ((Bits b)::(Bits a)::ds') when (Bits.length a = Bits.length b) ->
                    cte.KS { rte with DS = ((ofBits (Bits.beq a b))::ds') }
                | _ -> cte.KF rte

        let add cte =
            fun rte ->
                match rte.DS with
                | ((Bits n2)::(Bits n1)::ds') ->
                    let struct(sum,carry) = Arithmetic.add n1 n2
                    cte.KS { rte with DS = ((ofBits carry)::(ofBits sum)::ds') }
                | _ -> cte.KF rte

        let mul cte =
            fun rte ->
                match rte.DS with
                | ((Bits n2)::(Bits n1)::ds') ->
                    let struct(prod,overflow) = Arithmetic.mul n1 n2
                    cte.KS { rte with DS = ((ofBits overflow)::(ofBits prod)::ds') }
                | _ -> cte.KF rte

        let sub cte =
            fun rte ->
                match rte.DS with
                | ((Bits n2)::(Bits n1)::ds') ->
                    match Arithmetic.sub n1 n2 with
                    | Some diff -> cte.KS { rte with DS = ((ofBits diff)::ds') }
                    | None -> cte.KF rte
                | _ -> cte.KF rte

        let div cte =
            fun rte ->
                match rte.DS with
                | ((Bits divisor)::(Bits dividend)::ds') ->
                    match Arithmetic.div dividend divisor with
                    | Some struct(quotient, remainder) ->
                        cte.KS { rte with DS = ((ofBits remainder)::(ofBits quotient)::ds') }
                    | None -> cte.KF rte
                | _ -> cte.KF rte

        let fail cte = 
            cte.KF

        // symbolic ops except for 'eff' are covered here
        let opMap () =
            [ (lCopy, copy)
            ; (lDrop, drop)
            ; (lSwap, swap)
            ; (lEq, eq)
            ; (lFail, fail)
            ; (lGet, get)
            ; (lPut, put)
            ; (lDel, del)
            ; (lPushl, pushl)
            ; (lPopl, popl)
            ; (lPushr, pushr)
            ; (lPopr, popr)
            ; (lJoin, join)
            ; (lSplit, split)
            ; (lLen, len)
            ; (lBJoin, bjoin)
            ; (lBSplit, bsplit)
            ; (lBLen, blen)
            ; (lBNeg, bneg)
            ; (lBMax, bmax)
            ; (lBMin, bmin)
            ; (lBEq, beq)
            ; (lAdd, add)
            ; (lMul, mul)
            ; (lSub, sub)
            ; (lDiv, div)
            ] |> Map.ofList



        // eff
        // data
        // dip
        // cond
        // loop
        // seq
        // prog

        


        // how do we capture








    // TODO: Consider a continuation passing interpreter to simplify incremental computing,
    // or a finally tagless compilation for performance. However, this can be defered until
    // needed, possibly until much later - bootstrap implementation of Glas command-line.

    /// Default Interpreter for Glas Programs.
    let interpret p eff =
        fun s0 ->  
            match DSI.interpret p { DS = s0; ES = []; IO = eff } with
            | Some e' -> Some (e'.DS)
            | None -> None

