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

    [<return: Struct>]
    let (|Op|_|) v =
        match v with
        | Bits b ->
            match record_lookup b symOpsRec with
            | ValueSome U -> ValueSome b
            | _ -> ValueNone
        | _ -> ValueNone

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
    let lUntil = label "until"
    let lDo = label "do"
    let lEnv = label "env"
    let lWith = label "with"
    let lProg = label "prog"
    let lTBD = label "tbd"

    let lv l v = withLabel l v

    let Nop = lv lSeq unit

    let Dip v = lv lDip v

    [<return: Struct>]
    let (|Dip|_|) v =
        match v with
        | Stem lDip p -> ValueSome p
        | _ -> ValueNone

    let Data vData = lv lData vData

    [<return: Struct>]
    let (|Data|_|) v =
        match v with
        | Stem lData vData -> ValueSome vData
        | _ -> ValueNone
    
    // adding 'P' prefix to avoid conflict with F# Seq
    let PSeq body = lv lSeq body

    [<return: Struct>]
    let (|PSeq|_|) v =
        match v with
        | Stem lSeq body -> ValueSome body
        | _ -> ValueNone

    let isNop v =
        match v with
        | Stem lSeq Unit -> true
        | _ -> false

    let record_insert_unless_nop lbl v r =
        if isNop v
            then record_delete lbl r 
            else record_insert lbl v r

    let Cond (pTry, pThen, pElse) =
        unit |> record_insert lTry pTry
             |> record_insert_unless_nop lThen pThen
             |> record_insert_unless_nop lElse pElse
             |> lv lCond

    [<return: Struct>]
    let (|Cond|_|) v =
        match v with
        | Stem lCond (RecL [lTry; lThen; lElse] ([ValueSome pTry; optThen; optElse], U)) ->
            let pThen = ValueOption.defaultValue Nop optThen
            let pElse = ValueOption.defaultValue Nop optElse
            ValueSome (pTry, pThen, pElse)
        | _ -> ValueNone


    let While (pWhile, pDo) =
        unit |> record_insert lWhile pWhile
             |> record_insert_unless_nop lDo pDo
             |> lv lLoop

    [<return: Struct>]
    let (|While|_|) v =
        match v with
        | Stem lLoop (RecL [lWhile;lDo] ([ValueSome pWhile; optDo], U)) ->
            let pDo = ValueOption.defaultValue Nop optDo 
            ValueSome (pWhile, pDo)
        | _ -> ValueNone

    let Until (pUntil, pDo) =
        unit |> record_insert lUntil pUntil
             |> record_insert_unless_nop lDo pDo
             |> lv lLoop

    [<return: Struct>]
    let (|Until|_|) v =
        match v with
        | Stem lLoop (RecL [lUntil; lDo] ([ValueSome pUntil; optDo], U)) ->
            let pDo = ValueOption.defaultValue Nop optDo
            ValueSome (pUntil, pDo)
        | _ -> ValueNone

    let Env (pWith, pDo) =
        unit |> record_insert lWith pWith
             |> record_insert lDo pDo
             |> lv lEnv

    [<return: Struct>]
    let (|Env|_|) v =
        match v with
        | Stem lEnv (RecL [lWith; lDo] ([ValueSome pWith; ValueSome pDo], U)) ->
            ValueSome (pWith, pDo)
        | _ -> ValueNone

    let Prog (vAnno, pDo) =
        vAnno |> record_insert_unless_nop lDo pDo
              |> lv lProg

    [<return: Struct>]
    let (|Prog|_|) v =
        match v with
        | Stem lProg (RecL [lDo] ([optDo], vAnno)) ->
            let pDo = ValueOption.defaultValue Nop optDo
            ValueSome (vAnno, pDo)
        | _ -> ValueNone

    let TBD vMsg = lv lTBD vMsg

    [<return: Struct>]
    let (|TBD|_|) v =
        match v with
        | Stem lTBD vMsg -> ValueSome vMsg
        | _ -> ValueNone

    /// Utility function. Add annotations to a program.
    let addAnno k v p =
        let struct(anno0, pDo) = 
            match p with
            | Prog(vAnno, pDo) -> struct(vAnno, pDo)
            | _ -> struct(Value.unit, p)
        let anno' = Value.record_insert k v anno0
        Prog(anno', pDo)

    let getAnno k p =
        match p with
        | Prog(anno, _) -> Value.record_lookup k anno
        | _ -> ValueNone

    /// Return a sequence of program component values that are invalid programs.
    let rec invalidProgramComponents v =
        seq {
            match v with
            | Op _ | Data _ | TBD _ -> 
                ()
            | Dip p -> 
                yield! invalidProgramComponents p
            | PSeq (List lP) -> 
                yield! lP |> Rope.toSeq |> Seq.collect invalidProgramComponents
            | Cond (pTry, pThen, pElse) ->
                yield! invalidProgramComponents pTry
                yield! invalidProgramComponents pThen
                yield! invalidProgramComponents pElse
            | While (pCond, pDo) | Until(pCond, pDo) ->
                yield! invalidProgramComponents pCond
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
        // valid subprograms
        | Arity of int * int
        // observes X stack items then always fails.
        | ArityFail of int 
        // inconsistent subprograms
        | ArityDyn 

    let private compSeqArity a b =
        match a, b with
        | Arity (ia, oa), Arity (ib, ob) ->
            let d = max 0 (ib - oa)
            let ia' = ia + d
            let oa' = oa + d
            Arity (ia', oa' + (ob - ib))
        | Arity (ia, oa), ArityFail ib -> 
            let d = max 0 (ib - oa)
            ArityFail (ia + d)
        | Arity _, ArityDyn -> ArityDyn
        | _ -> a

    let private compCondArity c a b =
        match c with
        | ArityFail ci ->
            match b with
            | Arity (bi, bo) ->
                let d = max 0 (ci - bi)
                Arity (bi + d, bo + d)
            | ArityFail bi ->
                ArityFail (max ci bi)
            | ArityDyn -> ArityDyn
        | Arity(ci, co) ->
            let ca = compSeqArity c a
            match ca, b with
            | Arity (li, lo), Arity (ri, ro) when ((li - lo) = (ri - ro)) ->
                Arity (max li ri, max lo ro)
            | Arity (li, lo), ArityFail ri ->
                let d = max 0 (ri - li)
                Arity (li + d, lo + d)
            | ArityFail li, Arity (ri, ro) ->
                let d = max 0 (li - ri)
                Arity (ri + d, ro + d)
            | ArityFail li, ArityFail ri ->
                ArityFail (max li ri)
            | _ -> ArityDyn
        | ArityDyn -> ArityDyn

    let private compWhileLoopArity c a =
        match c with
        | ArityFail ci -> Arity(ci,ci)
        | Arity (ci, co) ->
            let ca = compSeqArity c a 
            match ca with
            | Arity(li,lo) when (li = lo) -> Arity(li,li)
            | ArityFail li -> Arity(li, li)
            | Arity _ | ArityDyn -> ArityDyn
        | ArityDyn -> ArityDyn

    let private compUntilLoopArity c a =
        match c with
        | ArityFail _ | ArityDyn ->
            // if 'until' always fails, we have a forever loop 
            ArityDyn
        | Arity (ci, co) ->
            match a with
            | Arity (ai, ao) when (ai = ao) ->
                let d = max 0 (ai - ci)
                Arity (ci + d, co + d)
            | ArityFail ai ->
                let d = max 0 (ai - ci)
                Arity (ci + d, co + d)
            | Arity _ | ArityDyn -> ArityDyn

    /// Computes arity of program. If there are 'arity:(i:Nat, o:Nat)' annotations
    /// under 'prog' operations, requires that annotations are consistent.
    let rec stackArity (p0:Value) : StackArity =
        match p0 with 
        | Stem lCopy U -> Arity (1,2)
        | Stem lSwap U -> Arity (2,2)
        | Stem lDrop U -> Arity (1,0)
        | Stem lEq U -> Arity (2,0)
        | Stem lGet U -> Arity (2,1)
        | Stem lPut U -> Arity (3,1)
        | Stem lDel U -> Arity (2,1)
        | Stem lEff U -> Arity (1,1)
        | Stem lFail U | Stem lTBD _ -> ArityFail 0
        | Dip p ->
            match stackArity p with
            | Arity (a,b) -> Arity (a+1, b+1)
            | ar -> ar
        | Data _ -> Arity(0,1)
        | PSeq (List lP) -> 
            let fn ar op = compSeqArity ar (stackArity op)
            Rope.fold fn (Arity (0,0)) lP
        | Cond (c, a, b) -> 
            compCondArity (stackArity c) (stackArity a) (stackArity b)
        | While (c, a) -> 
            compWhileLoopArity (stackArity c) (stackArity a)
        | Until (c, a) ->
            compUntilLoopArity (stackArity c) (stackArity a)
        | Env (w, p) -> 
            let okHandlerArity =
                match stackArity w with
                | ArityFail i -> (i <= 2)
                | Arity (i,o) -> (i = o) && (i <= 2)
                | ArityDyn -> false
            if okHandlerArity then stackArity p else ArityDyn
        | Prog (anno, p) ->
            let arInfer = stackArity p
            let arAnno = 
                match anno with
                | FullRec ["arity"] ([FullRec ["i";"o"] ([Nat64 i; Nat64 o], _)], _) ->
                    Some(int i, int o)
                | _ -> None
            match arInfer, arAnno with
            | Arity (i,o), Some (i',o') when ((i' >= i) && ((o - i) = (o' - i'))) ->
                // annotated arity is compatible with inferred arity
                Arity(i',o')
            | _, None -> arInfer // no annotation, use inferred
            | _, _ -> ArityDyn // arity inconsistent with annotation
        | _ -> ArityDyn

    let static_arity p =
        match stackArity p with
        | Arity (a,b) -> Some struct(a,b) 
        | _ -> None

