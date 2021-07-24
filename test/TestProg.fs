module Glas.TestProg

open Expecto
open Glas
open Program

let rng = System.Random()

let randomRange lb ub =
    lb + (rng.Next() % (1 + ub - lb))

let randomBits len = 
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- (0 <> (rng.Next() &&& 1))
    Bits.ofArray arr

let opArray = Array.ofList op_list
let randomOp () = opArray.[rng.Next() % opArray.Length]
let randomSym = randomOp >> opStr >> Value.label

let randomRecord () =
    let symCt = randomRange 0 8
    let mutable r = Value.unit
    for _ in 1 .. symCt do
        let k = randomSym () // 27 ops to select from
        let v = Value.ofBits (randomBits (randomRange 0 32))
        r <- Value.record_insert k v r
    r

let randomBytes len =
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- byte (rng.Next())
    Value.ofBinary arr


// require a successful parse (or raises exception, fails test)
let doParse = Program.tryParse >> Option.get 



// random program suitable for parse and print tests, but 
// almost certainly invalid for interpretation.
let rec randomProg d =
    if (d < 1) then Op (randomOp ()) else
    match randomRange 1 10 with
    | 1 -> Dip (randomProg (d - 1))
    | 2 -> Data (randomRecord ())
    | 3 -> 
        let seqLen = randomRange 0 10
        Seq [ for _ in 1 .. seqLen do yield randomProg (d - 1)]
    | 4 -> Cond (Try=randomProg (d-1), Then=randomProg (d-1), Else=randomProg (d-1))
    | 5 -> Loop (While=randomProg (d-1), Do=randomProg (d-1))
    | 6 -> Env (With=randomProg (d-1), Do=randomProg (d-1))
    | 7 ->
        let notes = randomRecord () |> Value.record_delete (Value.label "do") 
        Prog (Do=randomProg (d-1), Note=notes)
    | _ -> randomProg (d-1)


// it is feasible to create a few useful Program values for manual testing.
// Some ideas:
//  fibonacci function
//  greatest common denominator
//  a g0 parser...

[<Tests>]
let test_ppp =
    testList "program parse and print" [
        testCase "symops" <| fun () ->
            for op in Program.op_list do
                //printf "op %s\n" (Program.opStr op)
                let opSym = Value.symbol (Program.opStr op)
                let printOp = Program.print (Op op) 
                Expect.equal opSym printOp "equal print"
                Expect.equal (Op op) (doParse opSym) "equal parse"

        testCase "print then parse" <| fun () ->
            for x in 1 .. 1000 do
                let p0 = randomProg 6
                let vp0 = Program.print p0
                let p1 = doParse vp0
                //if x < 10 then printf "PROGRAM\n%A\n\n" p1
                Expect.equal p1 p0 "equal programs after print and parse"

    ]

let dataStack (ds : Value list) : Interpreter.RTE  = 
    { DS = ds; ES = List.empty; IO = Effects.noEffects }

let doEval p e = 
    match Interpreter.interpret p e with
    | Some e' -> e'
    | None -> failtestf "eval unsuccessful for program %A" p

let failEval p e =
    match Interpreter.interpret p e with
    | None -> () // pass - expected failure
    | Some _ -> failtestf "eval unexpectedly successful for program %A stack %A" p (e.DS)

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


[<Tests>]
let test_ops = 
    // note: focusing on type-safe behaviors of programs
    testList "unit test of interpreter" [
            testCase "stack ops" <| fun () ->
                for _ in 1 .. 10 do 
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    let e0 = dataStack [v1;v2]
                    let eCopy = doEval (Op Copy) e0
                    Expect.equal (eCopy.DS) [v1;v1;v2] "copied value"
                    let eDrop = doEval (Op Drop) e0 
                    Expect.equal (eDrop.DS) [v2] "dropped value"
                    let eSwap = doEval (Op Swap) e0
                    Expect.equal (eSwap.DS) [v2;v1] "swapped value"

            testCase "eq" <| fun () ->
                for _ in 1 .. 100 do
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    failEval (Op Eq) (dataStack [v1;v2]) 
                    let e' = doEval (Op Eq) (dataStack [v1;v1;v2])
                    Expect.equal (e'.DS) [v1;v1;v2] "equal stacks"

            testCase "record ops" <| fun () ->
                for _ in 1 .. 100 do
                    let k = randomSym ()
                    let r0 = randomRecord ()
                    let v = randomBytes 6

                    let rwk = Value.record_insert k v r0
                    let rwo = Value.record_delete k r0

                    // Get from record.
                    failEval (Op Get) (dataStack [Value.ofBits k; rwo])
                    let eGet = doEval (Op Get) (dataStack [Value.ofBits k; rwk])
                    Expect.equal (eGet.DS) [v] "equal get"

                    // Put into record.
                    let ePut1 = doEval (Op Put) (dataStack [Value.ofBits k; v; rwk])
                    let ePut2 = doEval (Op Put) (dataStack [Value.ofBits k; v; rwo])
                    Expect.equal (ePut1.DS) [rwk] "equal put with key"
                    Expect.equal (ePut2.DS) [rwk] "equal put without key"

                    // Delete from record.
                    let eDel1 = doEval (Op Del) (dataStack [Value.ofBits k; rwk])
                    let eDel2 = doEval (Op Del) (dataStack [Value.ofBits k; rwo])
                    Expect.equal (eDel1.DS) [rwo] "equal delete label"
                    Expect.equal (eDel2.DS) [rwo] "equal delete missing label"

            testCase "list ops" <| fun () ->
                for _ in 1 .. 100 do
                    let l1 = randomBytes (randomRange 0 30)
                    let l2 = randomBytes (randomRange 0 30)

                    // push left
                    let ePushl = doEval (Op Pushl) (dataStack [l2;l1])
                    let lPushl = FTList.cons l2 (Value.toFTList l1)
                    Expect.equal (ePushl.DS) [Value.ofFTList lPushl] "match pushl 1"

                    // push right
                    let ePushr = doEval (Op Pushr) (dataStack [l2;l1]) 
                    let lPushr = FTList.snoc (Value.toFTList l1) l2
                    Expect.equal (ePushr.DS) [Value.ofFTList lPushr] "match pushr"

                    // pop left
                    match Interpreter.interpret (Op Popl) (dataStack [l1]) with
                    | Some e' -> 
                        match l1 with
                        | Value.FTList (FTList.ViewL (v,l')) ->
                            Expect.equal (e'.DS) [v;Value.ofFTList l'] "match popl"
                        | _ -> failtest "popl has unexpected result structure"
                    | None -> Expect.equal l1 Value.unit "popl from empty list"

                    // pop right
                    match Interpreter.interpret (Op Popr) (dataStack [l1]) with 
                    | Some e' -> 
                        match l1 with
                        | Value.FTList (FTList.ViewR (l',v)) ->
                            Expect.equal (e'.DS) [v;Value.ofFTList l'] "match popr"
                        | _ -> failtest "popl has unexpected result structure"
                    | None -> Expect.equal l1 Value.unit "popl from empty list"

                    // joins
                    let l12 = Value.ofFTList (FTList.append (Value.toFTList l1) (Value.toFTList l2)) 
                    let eJoin = doEval (Op Join) (dataStack [l2;l1]) 
                    Expect.equal (eJoin.DS) [l12] "equal joins"

                    // splits
                    let wl1 = FTList.length (Value.toFTList l1)
                    let eSplit = doEval (Op Split) (dataStack [Value.nat wl1; l12])
                    Expect.equal (eSplit.DS) [l2;l1] "split list into components"

                    // lengths
                    let eLen = doEval (Op Len) (dataStack [l1]) 
                    Expect.equal (eLen.DS) [Value.nat wl1] "length computations"
 
            testCase "bitstring ops" <| fun () ->
                // not sure how to test bitstring ops without reimplementing them...
                for _ in 1 .. 1000 do
                    let n = randomRange 0 100
                    let a = randomBits n
                    let b = randomBits n

                    let e0 = dataStack [Value.ofBits b; Value.ofBits a]
                    let eNeg = doEval (Op BNeg) e0
                    Expect.equal (eNeg.DS) [Value.ofBits (Bits.bneg b); Value.ofBits a] "negated"

                    let ab = Bits.append a b
                    let eJoin = doEval (Op BJoin) e0
                    Expect.equal (eJoin.DS) [Value.ofBits ab] "joined"

                    let eLen = doEval (Op BLen) e0
                    Expect.equal (eLen.DS) [Value.nat (uint64 n); Value.ofBits a] "length"

                    let eSplit = doEval (Op BSplit) (dataStack [Value.nat (uint64 n); Value.ofBits ab])
                    Expect.equal (eSplit.DS) [Value.ofBits b; Value.ofBits a] "split"

                    let eMax = doEval (Op BMax) e0
                    Expect.equal (eMax.DS) [Value.ofBits (Bits.bmax a b)] "max"

                    let eMin = doEval (Op BMin) e0
                    Expect.equal (eMin.DS) [Value.ofBits (Bits.bmin a b)] "min"

                    let eBEq = doEval (Op BEq) e0
                    Expect.equal (eBEq.DS) [Value.ofBits (Bits.beq a b)] "beq"

                    // ensure that ops properly fail if given bitstrings of non-equal lengths
                    let c = randomBits (n + 1)
                    let l = randomBytes 2
                    let ebc = dataStack [Value.ofBits b; Value.ofBits c]
                    failEval (Op BMax) ebc
                    failEval (Op BMin) ebc
                    failEval (Op BEq) ebc

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

                    let e0 = dataStack [Value.ofBits b; Value.ofBits a]
                    let eAdd = doEval (Op Add) e0
                    let eProd = doEval (Op Mul) e0
                    let eSubOpt = Interpreter.interpret (Op Sub) e0
                    let eDivOpt = Interpreter.interpret (Op Div) e0

                    Expect.equal (eAdd.DS) [Value.ofBits carry; Value.ofBits sum] "eq add"
                    Expect.equal (eProd.DS) [Value.ofBits overflow; Value.ofBits prod] "eq prod"
                    match eSubOpt, subOpt with
                    | Some eSub, Some diff -> 
                        Expect.equal (eSub.DS) [Value.ofBits diff] "eq sub"
                    | None, None -> 
                        Expect.isLessThan (Bits.toI a) (Bits.toI b) "negative diff"
                    | _, _ -> 
                        failtest "inconsistent subtract success" 

                    match eDivOpt, divOpt with
                    | Some eDiv, Some struct(q,r) ->
                        Expect.equal (eDiv.DS) [Value.ofBits r; Value.ofBits q] "eq div"
                    | None, None -> 
                        Expect.equal (Bits.toNat64 b) 0UL "div by zero"
                    | _, _ -> 
                        failtest "inconsistent div success"


            testCase "data" <| fun () ->
                for _ in 1 .. 1000 do
                    let v = randomRecord ()
                    let e' = doEval (Data v) (dataStack [])
                    Expect.equal (e'.DS) [v] "eq data"

            testCase "seq" <| fun () ->
                let p = Seq [Op Copy; Data (Value.symbol "foo"); Op Get; 
                             Op Swap; Data (Value.symbol "bar"); Op Get; 
                             Op Mul; Op Swap; Op BJoin]
                for _ in 1 .. 1000 do
                    let a = randomBits (5 * randomRange 0 20)
                    let b = randomBits (5 * randomRange 0 20)
                    let r0 = Value.asRecord ["foo";"bar"] [Value.ofBits a; Value.ofBits b]
                    let e' = doEval p (dataStack [r0])
                    let struct(prod, overflow) = Arithmetic.mul a b
                    Expect.equal (e'.DS) [Value.ofBits (Bits.append overflow prod)] "expected seq result"

            testCase "dip" <| fun () ->
                for _ in 1 .. 10 do
                    let a = randomBytes 4
                    let b = randomBytes 4
                    let c = randomBytes 4
                    let e0 = dataStack [c;b;a]
                    let eDip = doEval (Dip (Op Swap)) e0
                    Expect.equal (eDip.DS) [c;a;b] "dip swap"

            testCase "cond" <| fun () ->
                let abs = Cond (Try = Op Sub, Then = Seq [], Else = Seq [Op Swap; Op Sub])
                for _ in 1 .. 1000 do
                    let a = byte <| rng.Next ()
                    let b = byte <| rng.Next ()
                    let c = if (a > b) then (a - b) else (b - a)
                    //printf "a=%d, b=%d, c=%d\n" a b c
                    let eAbs = doEval abs (dataStack [Value.u8 b; Value.u8 a])
                    Expect.equal (eAbs.DS) [Value.u8 c] "expected abs diff"


            testCase "loop" <| fun () ->
                // loop program: filter a list of bytes for range 32..126.
                // this sort of behavior is quite awkward to express w/o dedicated syntax.

                let pFilterMap pFN =
                    Seq [Data Value.unit
                        ;Loop (While = Dip (Op Popl)
                              ,Do=Cond(Try=Dip pFN
                                      ,Then=Seq[Op Swap; Op Pushr]
                                      ,Else=Dip (Op Drop)))
                        ;Dip (Op Drop)
                        ]

                // verify the filterMap loop has some expected behaviors
                let lTestFM = randomBytes 10
                let eAll = doEval (pFilterMap (Seq [])) (dataStack [lTestFM])
                Expect.equal (eAll.DS) [lTestFM] "filter accepts everything"
                let eNone = doEval (pFilterMap (Op Fail)) (dataStack [lTestFM])
                Expect.equal (eNone.DS) [Value.unit] "filter accepts nothing"

                let inRange lb ub = 
                    Seq [Op Copy; Data (Value.u8 lb); Op Sub; Op Drop
                        ;Cond(Try=Seq [Data (Value.u8 ub); Op Sub], Then=Op Fail, Else=Seq [])]
                let pOkByte = inRange 32uy 127uy
                let okByte b =
                    match b with
                    | Value.U8 n -> (32uy <= n) && (n <= 126uy)
                    | _ -> false 
                for x in 0uy .. 255uy do
                    // check our byte predicate
                    match Interpreter.interpret pOkByte (dataStack [Value.u8 x]) with
                    | Some eP -> 
                        //printfn "byte %d accepted\n" x 
                        Expect.isTrue (okByte (Value.u8 x)) "accepted byte"
                        Expect.equal (eP.DS) [Value.u8 x] "identity behavior"
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
                    let eLoop = doEval pLoop (dataStack [l0])
                    Expect.equal (eLoop.DS) [lF] "equal filtered lists"

            testCase "toplevel eff" <| fun () ->
                let varSym s = Seq [Dip (Data Value.unit); Data (Value.symbol s); Op Put]
                let pLogMsg = Seq [varSym "log"; Op Eff ]
                failEval (Op Eff) { DS = [Value.symbol "oops"]; ES = List.empty; IO = new EffLogger() }
                for _ in 1 .. 100 do
                    let msg = randomBytes 10
                    let eff = EffLogger() 
                    let eLog = doEval pLogMsg { DS = [msg]; ES = []; IO = eff }
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [msg] "logged outputs"
                    Expect.equal (eLog.DS) [Value.unit] "eff return val"

            testCase "transactional eff" <| fun () ->
                let varSym s = Seq [Dip (Data Value.unit); Data (Value.symbol s); Op Put]
                let tryOp p = Cond (Try=p, Then=nop, Else=nop)
                let tryEff s = tryOp (Seq [varSym s; Op Eff])
                let tryEff3 = Seq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                for _ in 1 .. 100 do
                    let a = randomBytes 10
                    let b = randomBytes 10
                    let c = randomBytes 10
                    let eff = EffLogger()
                    let eLog = doEval tryEff3 { DS = [a;b;c]; ES = []; IO = eff }
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [a;c] "expected messages"
                    Expect.equal (eLog.DS) [Value.unit; b; Value.unit] "expected results"

            testCase "env" <| fun () ->
                // behavior for 'env': 
                //  increment an effects counter
                //  rename "oops" effects to "log" and vice versa.
                let pInc = Seq [Data (Value.nat 1UL); Op Add; Op Drop] // fixed-width increment
                let varSym s = Seq [Dip (Data Value.unit); Data (Value.symbol s); Op Put]
                let getSym s = Seq [Data (Value.symbol s); Op Get]
                let pRN s1 s2 = Cond (Try = getSym s1, Then = varSym s2
                               ,Else = Cond(Try = getSym s2, Then = varSym s1, Else = nop))
                let withCRN s1 s2 p = Seq [ Data (Value.u8 0uy)
                                    ; Env (With=Seq [ pInc; Dip (Seq [pRN s1 s2; Op Eff])] 
                                          ,Do=p)]
                let tryOp p = Cond (Try=p, Then=nop, Else=nop)
                let tryEff s = tryOp (Seq [varSym s; Op Eff])
                let tryEff3 = Seq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                let pTest = withCRN "log" "oops" tryEff3
                for _ in 1 .. 100 do
                    let a = randomBytes 10
                    let b = randomBytes 10
                    let c = randomBytes 10
                    let eff = EffLogger()
                    let eLog = doEval pTest { DS = [a;b;c]; ES = []; IO = eff }
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (FTList.toList eff.Outputs) [b] "expected messages"
                    Expect.equal (eLog.DS) [Value.u8 1uy; a; Value.unit; c] "expected results"

            testCase "prog" <| fun () ->
                for _ in 1 .. 10 do
                    let a = randomBytes 4
                    let b = randomBytes 3
                    let e0 = dataStack [a;b]

                    let eProg = doEval (Prog (Do=Op Swap, Note=randomRecord())) e0 
                    Expect.equal (eProg.DS) [b;a] "prog"


(*
    | Env of Do:Program * With:Program
*)            
    ]