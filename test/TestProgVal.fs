module Glas.TestProgVal

open Expecto
open Glas
open ProgVal
open ProgEval

let rng = System.Random()

let randomRange lb ub =
    lb + (rng.Next() % (1 + ub - lb))

let randomBits len = 
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- (0 <> (rng.Next() &&& 1))
    Bits.ofArray arr

let opArray = Array.ofList symOpsList
let randomOp () = opArray.[rng.Next() % opArray.Length]
let randomSym = randomOp 

let randomRecord () =
    let symCt = randomRange 0 8
    let mutable r = Value.unit
    for _ in 1 .. symCt do
        let k = randomSym () // 27 ops to select from
        let v = Value.ofBits (randomBits (randomRange 0 32))
        r <- Value.record_insert k v r
    //printfn "%s" (Value.prettyPrint r) 
    r

let randomBytes len =
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- byte (rng.Next())
    Value.ofBinary arr

let mkSeq = FTList.ofList >> PSeq


let doEval p io s0 = 
    match interpret p io s0 with
    | Some s' -> s'
    | None -> failtestf "eval unsuccessful for program %A" p

let failEval p io s0 =
    match interpret p io s0 with
    | None -> () // pass - expected failure
    | Some _ -> failtestf "eval unexpectedly successful for program %A stack %A" p (List.map Value.prettyPrint s0)

type ACV = 
    | Aborted of Value 
    | Committed of Value

// eff logger only accepts 'log:Value' effects and simply records values.
// Any other input will cause the effect request to fail. Intended for testing.
// (Note: a better logger would include aborted writes but distinguish them.)
type EffLogger =
    val private id : int
    val mutable private TXStack : List<FTList<Value>>
    val mutable private CurrLog : FTList<Value>
    new() = { id = rng.Next(); TXStack = []; CurrLog = FTList.empty }

    // check whether we're mid transaction
    member x.InTX with get() = 
        not (List.isEmpty x.TXStack)

    // complete list of outputs if fully committed
    member x.Outputs with get() = 
        let fn o s = FTList.append s o
        List.fold (fun st vs -> FTList.append vs st) (x.CurrLog) (x.TXStack) 

    interface Effects.IEffHandler with
        member x.Eff v = 
            match v with
            | Value.Variant "log" vMsg ->
                x.CurrLog <- FTList.snoc (x.CurrLog) vMsg
                Some (Value.unit)
            | _ -> None
    interface Effects.ITransactional with
        member x.Try () = 
            //printf "try %d\n" x.id
            x.TXStack <- (x.CurrLog)::(x.TXStack)
            x.CurrLog <- FTList.empty
        member x.Commit () = 
            //printf "commit %d\n" x.id
            match x.TXStack with
            | (tx::txs) ->
                x.CurrLog <- FTList.append tx x.CurrLog
                x.TXStack <- txs
            | _ -> invalidOp "committed while not in a transaction"
        member x.Abort () = 
            //printf "abort %d\n" x.id
            match x.TXStack with
            | (tx::txs) ->
                x.CurrLog <- tx
                x.TXStack <- txs
            | _ -> invalidOp "aborted while not in a transaction" 

let noEff = Effects.noEffects


[<Tests>]
let test_ops = 
    // note: focusing on type-safe behaviors of programs
    testList "progval interpreter" [
            testCase "stack ops" <| fun () ->
                for _ in 1 .. 10 do 
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    let s0 = [v1;v2]
                    let sCopy = doEval (Op lCopy) noEff s0
                    Expect.equal (sCopy) [v1;v1;v2] "copied value"
                    let sDrop = doEval (Op lDrop) noEff s0 
                    Expect.equal (sDrop) [v2] "dropped value"
                    let sSwap = doEval (Op lSwap) noEff s0
                    Expect.equal (sSwap) [v2;v1] "swapped value"

            testCase "eq" <| fun () ->
                for _ in 1 .. 100 do
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    failEval (Op lEq) noEff [v1;v2;v2] 
                    let s' = doEval (Op lEq) noEff [v1;v1;v2]
                    Expect.equal (s') [v2] "eq drops equal values from stack"

            testCase "record ops" <| fun () ->
                for _ in 1 .. 100 do
                    let k = randomSym ()
                    let r0 = randomRecord ()
                    let v = randomBytes 6

                    let rwk = Value.record_insert k v r0
                    let rwo = Value.record_delete k r0

                    // Get from record.
                    failEval (Op lGet) noEff [Value.ofBits k; rwo]
                    let sGet = doEval (Op lGet) noEff [Value.ofBits k; rwk]
                    Expect.equal (sGet) [v] "equal get"

                    // Put into record.
                    let sPut1 = doEval (Op lPut) noEff [Value.ofBits k; v; rwk]
                    let sPut2 = doEval (Op lPut) noEff [Value.ofBits k; v; rwo]
                    Expect.equal (sPut1) [rwk] "equal put with key"
                    Expect.equal (sPut2) [rwk] "equal put without key"

                    // Delete from record.
                    let sDel1 = doEval (Op lDel) noEff [Value.ofBits k; rwk]
                    let sDel2 = doEval (Op lDel) noEff [Value.ofBits k; rwo]
                    Expect.equal (sDel1) [rwo] "equal delete label"
                    Expect.equal (sDel2) [rwo] "equal delete missing label"

            testCase "list ops" <| fun () ->
                for _ in 1 .. 100 do
                    let l1 = randomBytes (randomRange 0 30)
                    let l2 = randomBytes (randomRange 0 30)

                    // push left
                    let sPushl = doEval (Op lPushl) noEff [l2;l1]
                    let lPushl = FTList.cons l2 (Value.toFTList l1)
                    Expect.equal (sPushl) [Value.ofFTList lPushl] "match pushl 1"

                    // push right
                    let sPushr = doEval (Op lPushr) noEff [l2;l1] 
                    let lPushr = FTList.snoc (Value.toFTList l1) l2
                    Expect.equal (sPushr) [Value.ofFTList lPushr] "match pushr"

                    // pop left
                    match interpret (Op lPopl) noEff [l1] with
                    | Some s' -> 
                        match l1 with
                        | Value.FTList (FTList.ViewL (v,l')) ->
                            Expect.equal (s') [v; Value.ofFTList l'] "match popl"
                        | _ -> failtest "popl has unexpected result structure"
                    | None -> Expect.equal l1 Value.unit "popl from empty list"

                    // pop right
                    match interpret (Op lPopr) noEff [l1] with 
                    | Some s' -> 
                        match l1 with
                        | Value.FTList (FTList.ViewR (l',v)) ->
                            Expect.equal (s') [v;Value.ofFTList l'] "match popr"
                        | _ -> failtest "popl has unexpected result structure"
                    | None -> Expect.equal l1 Value.unit "popl from empty list"

                    // joins
                    let l12 = Value.ofFTList (FTList.append (Value.toFTList l1) (Value.toFTList l2)) 
                    let sJoin = doEval (Op lJoin) noEff [l2;l1] 
                    Expect.equal (sJoin) [l12] "equal joins"

                    // splits
                    let wl1 = FTList.length (Value.toFTList l1)
                    let sSplit = doEval (Op lSplit) noEff [Value.nat wl1; l12]
                    Expect.equal (sSplit) [l2;l1] "split list into components"

                    // lengths
                    let sLen = doEval (Op lLen) noEff [l1] 
                    Expect.equal (sLen) [Value.nat wl1] "length computations"
 
            testCase "bitstring ops" <| fun () ->
                // not sure how to test bitstring ops without reimplementing them...
                for _ in 1 .. 1000 do
                    let n = randomRange 0 100
                    let a = randomBits n
                    let b = randomBits n

                    let s0 = [Value.ofBits b; Value.ofBits a]
                    let sNeg = doEval (Op lBNeg) noEff s0
                    Expect.equal (sNeg) [Value.ofBits (Bits.bneg b); Value.ofBits a] "negated"

                    let ab = Bits.append a b
                    let sJoin = doEval (Op lBJoin) noEff s0
                    Expect.equal (sJoin) [Value.ofBits ab] "joined"

                    let sLen = doEval (Op lBLen) noEff s0
                    Expect.equal (sLen) [Value.nat (uint64 n); Value.ofBits a] "length"

                    let sSplit = doEval (Op lBSplit) noEff [Value.nat (uint64 n); Value.ofBits ab]
                    Expect.equal (sSplit) [Value.ofBits b; Value.ofBits a] "split"

                    let sMax = doEval (Op lBMax) noEff s0
                    Expect.equal (sMax) [Value.ofBits (Bits.bmax a b)] "max"

                    let sMin = doEval (Op lBMin) noEff s0
                    Expect.equal (sMin) [Value.ofBits (Bits.bmin a b)] "min"

                    let sBEq = doEval (Op lBEq) noEff s0
                    Expect.equal (sBEq) [Value.ofBits (Bits.beq a b)] "beq"

                    // ensure that ops properly fail if given bitstrings of non-equal lengths
                    let c = randomBits (n + 1)
                    let ebc = [Value.ofBits b; Value.ofBits c]
                    failEval (Op lBMax) noEff ebc
                    failEval (Op lBMin) noEff ebc
                    failEval (Op lBEq) noEff ebc

            testCase "arithmetic" <| fun () ->
                for _ in 1 .. 1000 do
                    let a = randomBits (5 * randomRange 0 20)
                    let b = randomBits (5 * randomRange 0 20)

                    // the expected results. We aren't testing Arithmetic module here.
                    // mostly need to check that stack order is right.
                    let struct(sum,carry) = Arithmetic.add a b
                    let struct(prod,overflow) = Arithmetic.mul a b
                    let subOpt = Arithmetic.sub a b
                    let divOpt = Arithmetic.div a b

                    let s0 = [Value.ofBits b; Value.ofBits a]
                    let sAdd = doEval (Op lAdd) noEff s0
                    let sProd = doEval (Op lMul) noEff s0
                    let sSubOpt = interpret (Op lSub) noEff s0
                    let sDivOpt = interpret (Op lDiv) noEff s0

                    Expect.equal (sAdd) [Value.ofBits carry; Value.ofBits sum] "eq add"
                    Expect.equal (sProd) [Value.ofBits overflow; Value.ofBits prod] "eq prod"
                    match sSubOpt, subOpt with
                    | Some sSub, Some diff -> 
                        Expect.equal (sSub) [Value.ofBits diff] "eq sub"
                    | None, None -> 
                        Expect.isLessThan (Bits.toI a) (Bits.toI b) "negative diff"
                    | _, _ -> 
                        failtest "inconsistent subtract success" 

                    match sDivOpt, divOpt with
                    | Some sDiv, Some struct(q,r) ->
                        Expect.equal (sDiv) [Value.ofBits r; Value.ofBits q] "eq div"
                    | None, None -> 
                        Expect.equal (Bits.toNat64 b) 0UL "div by zero"
                    | _, _ -> 
                        failtest "inconsistent div success"


            testCase "data" <| fun () ->
                for _ in 1 .. 1000 do
                    let v = randomRecord ()
                    let s' = doEval (Data v) noEff []
                    Expect.equal (s') [v] "eq data"

            testCase "seq" <| fun () ->
                let p = mkSeq [Op lCopy; Data (Value.symbol "foo"); Op lGet; 
                               Op lSwap; Data (Value.symbol "bar"); Op lGet; 
                               Op lMul; Op lSwap; Op lBJoin]
                for _ in 1 .. 1000 do
                    let a = randomBits (5 * randomRange 0 20)
                    let b = randomBits (5 * randomRange 0 20)
                    let r0 = Value.asRecord ["foo";"bar"] [Value.ofBits a; Value.ofBits b]
                    let s' = doEval p noEff [r0]
                    let struct(prod, overflow) = Arithmetic.mul a b
                    Expect.equal (s') [Value.ofBits (Bits.append overflow prod)] "expected seq result"

            testCase "dip" <| fun () ->
                for _ in 1 .. 10 do
                    let a = randomBytes 4
                    let b = randomBytes 4
                    let c = randomBytes 4
                    let s0 = [c;b;a]
                    let sDip = doEval (Dip (Op lSwap)) noEff s0
                    Expect.equal (sDip) [c;a;b] "dip swap"

            testCase "cond" <| fun () ->
                let abs = Cond (Op lSub, mkSeq [], mkSeq [Op lSwap; Op lSub])
                for _ in 1 .. 1000 do
                    let a = byte <| rng.Next ()
                    let b = byte <| rng.Next ()
                    let c = if (a > b) then (a - b) else (b - a)
                    //printf "a=%d, b=%d, c=%d\n" a b c
                    let sAbs = doEval abs noEff [Value.u8 b; Value.u8 a]
                    Expect.equal (sAbs) [Value.u8 c] "expected abs diff"


            testCase "loop" <| fun () ->
                // loop program: filter a list of bytes for range 32..126.
                // this sort of behavior is quite awkward to express w/o dedicated syntax.

                let pFilterMap pFN =
                    mkSeq [Data Value.unit
                          ;Loop (Dip (Op lPopl)
                                ,Cond(Dip pFN
                                ,mkSeq[Op lSwap; Op lPushr]
                                ,Dip (Op lDrop)))
                          ;Dip (Op lDrop)
                          ]

                // verify the filterMap loop has some expected behaviors
                let lTestFM = randomBytes 10
                let sAll = doEval (pFilterMap Nop) noEff [lTestFM]
                Expect.equal (sAll) [lTestFM] "filter accepts everything"
                let sNone = doEval (pFilterMap (Op lFail)) noEff [lTestFM]
                Expect.equal (sNone) [Value.unit] "filter accepts nothing"

                let inRange lb ub = 
                    mkSeq [Op lCopy; Data (Value.u8 lb); Op lSub; Op lDrop
                          ;Cond(mkSeq [Data (Value.u8 ub); Op lSub], Op lFail, Nop)]
                let pOkByte = inRange 32uy 127uy
                let okByte b =
                    match b with
                    | Value.U8 n -> (32uy <= n) && (n <= 126uy)
                    | _ -> false 
                for x in 0uy .. 255uy do
                    // check our byte predicate
                    match interpret pOkByte noEff [Value.u8 x] with
                    | Some sP -> 
                        //printfn "byte %d accepted\n" x 
                        Expect.isTrue (okByte (Value.u8 x)) "accepted byte"
                        Expect.equal (sP) [Value.u8 x] "identity behavior"
                    | None ->
                        //printf "byte %d rejected\n" x
                        Expect.isFalse (okByte (Value.u8 x)) "rejected byte"

                let pLoop = pFilterMap pOkByte
                for _ in 1 .. 100 do
                    let l0 = randomBytes 100
                    // our filtered list (via F#)
                    let lF = l0 |> Value.toFTList 
                                |> FTList.toList 
                                |> List.filter okByte
                                |> FTList.ofList 
                                |> Value.ofFTList    
                    let sLoop = doEval pLoop noEff [l0]
                    Expect.equal (sLoop) [lF] "equal filtered lists"

            testCase "toplevel eff" <| fun () ->
                let varSym s = mkSeq [Dip (Data Value.unit); Data (Value.symbol s); Op lPut]
                let pLogMsg = mkSeq [varSym "log"; Op lEff ]
                failEval (Op lEff) (EffLogger()) [Value.symbol "oops"]
                for _ in 1 .. 100 do
                    let msg = randomBytes 10
                    let eff = EffLogger() 
                    let sLog = doEval pLogMsg eff [msg]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [msg] "logged outputs"
                    Expect.equal (sLog) [Value.unit] "eff return val"

            testCase "transactional eff" <| fun () ->
                let varSym s = mkSeq [Dip (Data Value.unit); Data (Value.symbol s); Op lPut]
                let tryOp p = Cond (p, Nop, Nop)
                let tryEff s = tryOp (mkSeq [varSym s; Op lEff])
                let tryEff3 = mkSeq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                for _ in 1 .. 100 do
                    let a = randomBytes 10
                    let b = randomBytes 10
                    let c = randomBytes 10
                    let eff = EffLogger()
                    let sLog = doEval tryEff3 eff [a;b;c]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [a;c] "expected messages"
                    Expect.equal (sLog) [Value.unit; b; Value.unit] "expected results"

            testCase "env" <| fun () ->
                // behavior for 'env': 
                //  increment an effects counter
                //  rename "oops" effects to "log" and vice versa.
                let pInc = mkSeq [Data (Value.nat 1UL); Op lAdd; Op lDrop] // fixed-width increment
                let varSym s = mkSeq [Dip (Data Value.unit); Data (Value.symbol s); Op lPut]
                let getSym s = mkSeq [Data (Value.symbol s); Op lGet]
                let pRN s1 s2 = Cond (getSym s1, varSym s2
                                     ,Cond(getSym s2, varSym s1, Nop))
                let withCRN s1 s2 p = mkSeq [ Data (Value.u8 0uy)
                                    ; Env (mkSeq [ pInc; Dip (mkSeq [pRN s1 s2; Op lEff])] 
                                          ,p)]
                let tryOp p = Cond (p, Nop, Nop)
                let tryEff s = tryOp (mkSeq [varSym s; Op lEff])
                let tryEff3 = mkSeq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                let pTest = withCRN "log" "oops" tryEff3
                for _ in 1 .. 100 do
                    let a = randomBytes 10
                    let b = randomBytes 10
                    let c = randomBytes 10
                    let eff = EffLogger()
                    let sLog = doEval pTest eff [a;b;c]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [b] "expected messages"
                    Expect.equal (sLog) [Value.u8 1uy; a; Value.unit; c] "expected results"

            testCase "prog" <| fun () ->
                for _ in 1 .. 10 do
                    let a = randomBytes 4
                    let b = randomBytes 3
                    let s0 = [a;b]
                    let sProg = doEval (Prog (randomRecord(), Op lSwap)) noEff s0 
                    Expect.equal (sProg) [b;a] "prog"

    ]