namespace Glas

/// Represents a program as a value end-to-end. This avoids the issues from 
/// converting between value and program, which will simplify structure 
/// sharing.
module ProgVal =
    open Value
    type Program = Value

    // basic operators
    let Copy = label "copy"
    let Drop = label "drop"
    let Swap = label "swap"
    let Eq = label "eq"
    let Fail = label "fail"
    let Eff = label "eff"
    let Get = label "get"
    let Put = label "put"
    let Del = label "del"
    let Pushl = label "pushl"
    let Popl = label "popl"
    let Pushr = label "pushr"
    let Popr = label "popr"
    let Join = label "join"
    let Split = label "split"
    let Len = label "len"
    let BJoin = label "bjoin"
    let BSplit = label "bsplit"
    let BLen = label "blen"
    let BNeg = label "bneg"
    let BMax = label "bmax"
    let BMin = label "bmin"
    let BEq = label "beq"
    let Add = label "add"
    let Mul = label "mul"
    let Sub = label "sub"
    let Div = label "div"

    let symOpsList = 
        [ Copy; Swap; Drop
        ; Eq; Fail
        ; Eff
        ; Get; Put; Del
        ; Pushl; Popl; Pushr; Popr; Join; Split; Len
        ; BJoin; BSplit; BLen; BNeg; BMax; BMin; BEq
        ; Add; Mul; Sub; Div
        ]

    let symOpsRec =
        List.fold (fun r op -> record_insert op unit r) unit symOpsList

    let inline Op b =
        Value.ofBits b
    let (|Op|_|) v =
        match v with
        | Bits b ->
            match record_lookup b symOpsRec with
            | Some U -> Some b
            | _ -> None
        | _ -> None

    // structured subprograms
    let lDip = label "dip"
    let lData = label "data"
    let lSeq = label "seq"
    let lCond = label "cond"
    let lTry = label "try"
    let lThen = label "then"
    let lElse = label "else"
    let lLoop = label "loop"
    let lWhile = label "while"
    let lDo = label "do"
    let lEnv = label "env"
    let lWith = label "with"
    let lProg = label "prog"

    let lv l v =  
        { v with Stem = Bits.append l (v.Stem) }

    let Nop = lv lSeq unit

    let Dip v = lv lDip v
    let inline (|Dip|_|) v =
        match v with
        | Stem lDip p -> Some p
        | _ -> None

    let Data v = lv lData v
    let inline (|Data|_|) v =
        match v with
        | Stem lData vData -> Some vData
        | _ -> None
    
    // adding 'P' prefix to avoid conflict with F# Seq
    let PSeq lV = lv lSeq (ofFTList lV)
    let inline (|PSeq|_|) v =
        match v with
        | Stem lSeq (FTList lV) -> Some lV
        | _ -> None

    let private record_insert_unless_nop lbl v r =
        if v = Nop 
            then record_delete lbl r 
            else record_insert lbl v r

    let Cond (pTry, pThen, pElse) =
        unit |> record_insert lTry pTry
             |> record_insert_unless_nop lThen pThen
             |> record_insert_unless_nop lElse pElse
             |> lv lCond
    let inline (|Cond|_|) v =
        match v with
        | Stem lCond (RecL [lTry; lThen; lElse] ([Some pTry; optThen; optElse], U)) ->
            let pThen = Option.defaultValue Nop optThen
            let pElse = Option.defaultValue Nop optElse
            Some (pTry, pThen, pElse)
        | _ -> None



    let Loop (pWhile, pDo) =
        unit |> record_insert lWhile pWhile
             |> record_insert_unless_nop lDo pDo
             |> lv lLoop
    let inline (|Loop|_|) v =
        match v with
        | Stem lLoop (RecL [lWhile;lDo] ([Some pWhile; optDo], U)) ->
            let pDo = Option.defaultValue Nop optDo 
            Some (pWhile, pDo)
        | _ -> None

    let Env (pWith, pDo) =
        unit |> record_insert lWith pWith
             |> record_insert lDo pDo
             |> lv lEnv

    let inline (|Env|_|) v =
        match v with
        | Stem lEnv (RecL [lWith; lDo] ([Some pWith; Some pDo], U)) ->
            Some (pWith, pDo)
        | _ -> None

    let Prog (vAnno, pDo) =
        vAnno |> record_insert_unless_nop lDo pDo
              |> lv lProg

    let inline (|Prog|_|) v =
        match v with
        | Stem lProg (RecL [lDo] ([optDo], vAnno)) ->
            let pDo = Option.defaultValue Nop optDo
            Some (vAnno, pDo)
        | _ -> None

    /// Return a sequence of program component values that are invalid programs.
    let rec invalidProgramComponents v =
        seq {
            match v with
            | Op _ -> 
                ()
            | Dip p -> 
                yield! invalidProgramComponents p
            | Data _ -> 
                ()
            | PSeq lP -> 
                yield! lP |> FTList.toSeq |> Seq.collect invalidProgramComponents
            | Cond (pTry, pThen, pElse) ->
                yield! invalidProgramComponents pTry
                yield! invalidProgramComponents pThen
                yield! invalidProgramComponents pElse
            | Loop (pWhile, pDo) ->
                yield! invalidProgramComponents pWhile
                yield! invalidProgramComponents pDo
            | Env (pWith, pDo) ->
                yield! invalidProgramComponents pWith
                yield! invalidProgramComponents pDo
            | Prog (_, pDo) ->
                yield! invalidProgramComponents pDo
            | _ -> 
                yield v
        }

    /// Return whether the program has a valid AST. This does not check
    /// for valid static arity or other properties.
    let rec isValidProgramAST v =
        Seq.isEmpty (invalidProgramComponents v)
        
    type StackArity =
        | Arity of int * int
        | Failure // arity of subprogram that always Fails
        | Dynamic // arity of inconsistent subprogram

    let private opArityMap =
        [ (Copy, Arity(1,2))
        ; (Swap, Arity(2,2))
        ; (Drop, Arity(1,0))
        ; (Eq, Arity(2,0))
        ; (Fail, Failure)
        ; (Eff, Arity(1,1))
        ; (Get, Arity(2,1))
        ; (Put, Arity(3,1))
        ; (Del, Arity(2,1))
        ; (Pushl, Arity(2,1))
        ; (Popl, Arity(1,2))
        ; (Pushr, Arity(2,1))
        ; (Popr, Arity(1,2))
        ; (Join, Arity(2,1))
        ; (Split, Arity(2,2))
        ; (Len, Arity(1,1))
        ; (BJoin, Arity(2,1))
        ; (BSplit, Arity(2,2))
        ; (BLen, Arity(1,1))
        ; (BNeg, Arity(1,1))
        ; (BMax, Arity(2,1))
        ; (BMin, Arity(2,1))
        ; (BEq, Arity(2,1))
        ; (Add, Arity(2,2))
        ; (Mul, Arity(2,2))
        ; (Sub, Arity(2,1))
        ; (Div, Arity(2,2))
        ] |> Map.ofList

    let rec stack_arity p =
        match p with 
        | Op op ->
            match Map.tryFind op opArityMap with
            | Some arity -> arity
            | None -> failwithf "missing op %s in arity map" (prettyPrint p)
        | Dip p ->
            match stack_arity p with
            | Arity (a,b) -> Arity (a+1, b+1)
            | Failure -> Failure
            | Dynamic -> Dynamic
        | Data _ -> Arity(0,1)
        | PSeq lP  -> stack_arity_seq (FTList.toList lP)
        | Cond (c, a, b) ->
            let l = stack_arity_seq [c;a]
            let r = stack_arity b
            match l,r with
            | Arity (li,lo), Arity(ri,ro) when ((li - lo) = (ri - ro)) ->
                if (li > ri) then l else r
            | Failure, Arity (ri, ro) -> 
                match stack_arity c with
                | Failure -> r
                | Arity (ci, _) ->
                    let d = (max ci ri) - ri
                    Arity (ri + d, ro + d)
                | Dynamic -> Dynamic
            | _, Failure -> l
            | _, _ -> Dynamic
        | Loop (c, a) ->
            // seq:[c,a] must be stack invariant.
            match stack_arity_seq [c;a] with
            | Arity (i,o) when (i = o) -> Arity(i,o)
            | Failure -> 
                match stack_arity c with
                | Failure -> Arity(0,0)
                | _ -> Dynamic
            | _ -> Dynamic
        | Env (e, p) -> 
            // constraining bootstrap eff handlers to be 2-2 including state.
            // i.e. forall S . ((S * Request) * St) -> ((S * Response) * St)
            match stack_arity e with
            | Arity(i,o) when ((i = o) && (2 >= i)) -> 
                stack_arity (Dip p)
            | _ -> Dynamic
        | Prog (_, p) -> stack_arity p
        | _ ->
            // not a valid program. 
            Failure

    and stack_arity_seq ps =
        _stack_arity_seq 0 0 ps
    and private _stack_arity_seq i o ps =
        match ps with
        | [] -> Arity(i,o)
        | (p::ps') ->
            match stack_arity p with
            | Arity (a,b) -> 
                let d = max 0 (a - o) 
                let i' = i + d
                let o' = o + d + (b - a) 
                _stack_arity_seq i' o' ps'
            | ar -> ar

    // vestigial
    let static_arity p =
        match stack_arity p with
        | Arity (a,b) -> Some struct(a,b) 
        | _ -> None

    // Desired Interpreter:
    //  defunctionalized continuation passing
    //  finally tagless with stack memory allocation
    //  support for quotas
    //  avoid try/commit/abort effects based on escape analysis
    //
    // I currently have a direct style interpreter, but it's
    // not very useful for incremental computing or performance.
    // I think finally tagless could be over 15x faster.

    //module InterpreterCPS =
        // A continuation is essentially a cursor into the program.
        // Relevantly, we must track:
        //   continuation, data stack, failure stack, effect stack
        //   continuation op for commit
        //   continuation op for restoring env
        //   continuation op for popping env



    // TODO:
    //  - rewrite program for continuation-passing style
    //  - or compile a program into a composition of F# functions for JIT

    /// A lightweight, direct-style interpreter for the Glas program model.
    /// Perhaps useful as a reference, and for getting started. However, this
    /// is really awkward for incremental computing, stack traces, quotas, etc..
    module Interpreter =
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
            [ (Copy, copy)
            ; (Drop, drop)
            ; (Swap, swap)
            ; (Eq, eq)
            ; (Fail, fun _ -> None)
            ; (Eff, eff)
            ; (Get, get)
            ; (Put, put)
            ; (Del, del)
            ; (Pushl, pushl)
            ; (Popl, popl)
            ; (Pushr, pushr)
            ; (Popr, popr)
            ; (Join, join)
            ; (Split, split)
            ; (Len, len)
            ; (BJoin, bjoin)
            ; (BSplit, bsplit)
            ; (BLen, blen)
            ; (BNeg, bneg)
            ; (BMax, bmax)
            ; (BMin, bmin)
            ; (BEq, beq)
            ; (Add, add)
            ; (Mul, mul)
            ; (Sub, sub)
            ; (Div, div)
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

    type ProgramFunction = Effects.IEffHandler -> Value list -> Value list option

    let interpret (p : Program) : ProgramFunction =
        fun eff s0 ->  
            let e0 : Interpreter.RTE = { DS = s0; ES = []; IO = eff }
            match Interpreter.interpret p e0 with
            | Some e' -> Some (e'.DS)
            | None -> None

