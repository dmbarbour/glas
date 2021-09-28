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

    let symOpsList = 
        [ lCopy; lSwap; lDrop
        ; lEq; lFail; lEff
        ; lGet; lPut; lDel
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
    let (|Dip|_|) v =
        match v with
        | Stem lDip p -> Some p
        | _ -> None

    let Data v = lv lData v
    let (|Data|_|) v =
        match v with
        | Stem lData vData -> Some vData
        | _ -> None
    
    // adding 'P' prefix to avoid conflict with F# Seq
    let PSeq lV = lv lSeq (ofFTList lV)
    let (|PSeq|_|) v =
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
    let (|Cond|_|) v =
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
    let (|Loop|_|) v =
        match v with
        | Stem lLoop (RecL [lWhile;lDo] ([Some pWhile; optDo], U)) ->
            let pDo = Option.defaultValue Nop optDo 
            Some (pWhile, pDo)
        | _ -> None

    let Env (pWith, pDo) =
        unit |> record_insert lWith pWith
             |> record_insert lDo pDo
             |> lv lEnv

    let (|Env|_|) v =
        match v with
        | Stem lEnv (RecL [lWith; lDo] ([Some pWith; Some pDo], U)) ->
            Some (pWith, pDo)
        | _ -> None

    let Prog (vAnno, pDo) =
        vAnno |> record_insert_unless_nop lDo pDo
              |> lv lProg

    let (|Prog|_|) v =
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

    let private compSeqArity a b =
        match a, b with
        | Arity (ia, oa), Arity (ib, ob) ->
            let d = max 0 (ib - oa)
            let ia' = ia + d
            let oa' = oa + d
            Arity (ia', oa' + (ob - ib))
        | Arity _, _ -> b
        | _ -> a

    let private compCondArity c a b =
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

    let private compLoopArity c a =
        // dynamic if not stack invariant.
        match compSeqArity c a with
        | Arity (i,o) when (i = o) -> Arity(i,o)
        | ArityFail -> 
            match c with
            | ArityFail -> Arity(0,0)
            | _ -> ArityDyn
        | _ -> ArityDyn

    /// Computes arity of program. If there are 'arity:(i:Nat, o:Nat)' annotations
    /// under 'prog' operations, requires that annotations are consistent.
    let rec stackArity (ef0:StackArity) (p0:Value) : StackArity =
        match p0 with 
        | Stem lCopy U -> Arity (1,2)
        | Stem lSwap U -> Arity (2,2)
        | Stem lDrop U -> Arity (1,0)
        | Stem lEq U -> Arity (2,0)
        | Stem lGet U -> Arity (2,1)
        | Stem lPut U -> Arity (3,1)
        | Stem lDel U -> Arity (2,1)
        | Stem lEff U -> ef0
        | Stem lFail U -> ArityFail
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
        | Prog (anno, p) ->
            let arInfer = stackArity ef0 p
            let arAnno = 
                match anno with
                | FullRec ["arity"] ([FullRec ["i";"o"] ([Nat i; Nat o], _)], _) ->
                    Some(int i, int o)
                | _ -> None
            match arInfer, arAnno with
            | Arity (i,o), Some (i',o') when ((i' >= i) && ((o - i) = (o' - i'))) ->
                // annotated arity is compatible with inferred arity
                Arity(i',o')
            | _, None -> arInfer // no annotation, use inferred
            | _, _ -> ArityDyn // arity inconsistent with annotation
        | _ ->
            failwithf "not a valid program %s" (prettyPrint p0)

    let static_arity p =
        match stackArity (Arity (1,1)) p with
        | Arity (a,b) -> Some struct(a,b) 
        | _ -> None




