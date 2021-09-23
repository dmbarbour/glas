namespace Glas

/// Represents a program as a value end-to-end. This avoids the issues from 
/// converting between value and program, which will simplify structure 
/// sharing.
module ProgVal =
    open Value
    type Program = Value

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
    let lAdd = label "add"
    let lMul = label "mul"
    let lSub = label "sub"
    let lDiv = label "div"

    let symOpsList = 
        [ lCopy; lSwap; lDrop
        ; lEq; lFail; lEff
        ; lGet; lPut; lDel
        ; lPushl; lPopl; lPushr; lPopr
        ; lJoin; lSplit; lLen
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
        | ArityFail // arity of subprogram that always Fails
        | ArityDyn // arity of inconsistent subprogram

    let compSeqArity a b =
        match a, b with
        | Arity (ia, oa), Arity (ib, ob) ->
            let d = max 0 (ib - oa)
            let ia' = ia + d
            let oa' = oa + d
            Arity (ia', oa' + (ob - ib))
        | Arity _, _ -> b
        | _ -> a

    let compCondArity c a b =
        let ca = compSeqArity c a
        match ca, b with
        | Arity (li, lo), Arity (ri, ro) when ((li - lo) = (ri - ro)) ->
            Arity (max li ri, max lo ro)
        | _, ArityFail -> ca
        | ArityFail, Arity (ri, ro) ->
            match c with
            | ArityFail -> b
            | Arity (ci, _) ->
                let d = (max ci ri) - ri
                Arity (ri + d, ro + d)
            | ArityDyn -> ArityDyn
        | _ -> ArityDyn

    let compLoopArity c a =
        // dynamic if not stack invariant.
        match compSeqArity c a with
        | Arity (i,o) when (i = o) -> Arity(i,o)
        | ArityFail -> 
            match c with
            | ArityFail -> Arity(0,0)
            | _ -> ArityDyn
        | _ -> ArityDyn

    let private opArityMap =
        let inline ar a b = struct(a, b)
        [ (lCopy, ar 1 2)
        ; (lSwap, ar 2 2)
        ; (lDrop, ar 1 0)
        ; (lEq, ar 2 0)
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
        ; (lAdd, ar 2 1)
        ; (lMul, ar 2 1)
        ; (lSub, ar 2 1)
        ; (lDiv, ar 2 2)
        ] |> Map.ofList

    let rec stackArity (ef0:StackArity) (p0:Value) : StackArity =
        match p0 with 
        | Op op ->
            if op = lEff then ef0 else
            if op = lFail then ArityFail else
            match Map.tryFind op opArityMap with
            | Some (struct(a,b)) -> Arity (a,b)
            | None -> failwithf "missing op %s in arity map" (prettyPrint p0)
        | Dip p ->
            match stackArity ef0 p with
            | Arity (a,b) -> Arity (a+1, b+1)
            | ar -> ar
        | Data _ -> Arity(0,1)
        | PSeq lP  -> 
            let fn ar op = compSeqArity ar (stackArity ef0 op)
            FTList.fold fn (Arity (0,0)) lP
        | Cond (c, a, b) -> 
            compCondArity (stackArity ef0 c) (stackArity ef0 a) (stackArity ef0 b)
        | Loop (c, a) -> 
            compLoopArity (stackArity ef0 c) (stackArity ef0 a)
        | Env (e, p) -> 
            // we can allow imbalanced effects, but e must have one output 
            // for handler state.
            let efArity = compSeqArity (Arity(1,1)) (stackArity ef0 e)
            let p' = Dip p
            match efArity with
            | Arity(i,o) when ((i > 0) && (o > 0)) -> 
                stackArity (Arity(i-1,o-1)) p'
            | ArityFail -> 
                stackArity ArityFail p'
            | _ ->
                stackArity ArityDyn p'
        | Prog (_, p) -> 
            stackArity ef0 p
        | _ ->
            failwithf "not a valid program %s" (prettyPrint p0)

    // vestigial
    let static_arity p =
        match stackArity (Arity (1,1)) p with
        | Arity (a,b) -> Some struct(a,b) 
        | _ -> None




