namespace Glas

/// Represents a program as a value end-to-end. This avoids the issues from 
/// converting between value and program, which will simplify structure 
/// sharing.
module ProgVal =
    open Value
    type Program = Value
    type ProgramFunction = Effects.IEffHandler -> Value list -> Value list option

    // basic operators
    let lCopy = label "copy"
    let lDrop = label "drop"
    let lSwap = label "swap"
    let lEq = label "eq"
    let lFail = label "fail"
    let lEff = label "eff"
    let lGet = label "get"
    let lPut = label "put"
    let lDel = label "del"
    let lPushl = label "pushl"
    let lPopl = label "popl"
    let lPushr = label "pushr"
    let lPopr = label "popr"
    let lJoin = label "join"
    let lSplit = label "split"
    let lLen = label "len"
    let lBJoin = label "bjoin"
    let lBSplit = label "bsplit"
    let lBLen = label "blen"
    let lBNeg = label "bneg"
    let lBMax = label "bmax"
    let lBMin = label "bmin"
    let lBEq = label "beq"
    let lAdd = label "add"
    let lMul = label "mul"
    let lSub = label "sub"
    let lDiv = label "div"

    let symOpsList = 
        [ lCopy; lSwap; lDrop
        ; lEq; lFail
        ; lEff
        ; lGet; lPut; lDel
        ; lPushl; lPopl; lPushr; lPopr; lJoin; lSplit; lLen
        ; lBJoin; lBSplit; lBLen; lBNeg; lBMax; lBMin; lBEq
        ; lAdd; lMul; lSub; lDiv
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

    let private ar a b = Arity (a, b)

    let private opArityMap =
        [ (lCopy, ar 1 2)
        ; (lSwap, ar 2 2)
        ; (lDrop, ar 1 0)
        ; (lEq, ar 2 0)
        ; (lFail, Failure)
        ; (lEff, ar 1 1)
        ; (lGet, ar 2 1)
        ; (lPut, ar 3 1)
        ; (lDel, ar 2 1)
        ; (lPushl, ar 2 1)
        ; (lPopl, ar 1 2)
        ; (lPushr, ar 2 1)
        ; (lPopr, ar 1 2)
        ; (lJoin, ar 2 1)
        ; (lSplit, ar 2 2)
        ; (lLen, ar 1 1)
        ; (lBJoin, ar 2 1)
        ; (lBSplit, ar 2 2)
        ; (lBLen, ar 1 1)
        ; (lBNeg, ar 1 1)
        ; (lBMax, ar 2 1)
        ; (lBMin, ar 2 1)
        ; (lBEq, ar 2 1)
        ; (lAdd, ar 2 2)
        ; (lMul, ar 2 2)
        ; (lSub, ar 2 1)
        ; (lDiv, ar 2 2)
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
                Arity (max li ri, max lo ro)
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
                let d = max 0 (a - o) // p assumes deeper input stack?
                let i' = i + d
                let o' = o + d + (b - a)
                _stack_arity_seq i' o' ps'
            | ar -> ar

    // vestigial
    let static_arity p =
        match stack_arity p with
        | Arity (a,b) -> Some struct(a,b) 
        | _ -> None

