module Glas.TestProgVal

open Expecto
open Glas
open Value
open ProgVal
open ProgEval
open RandVal

let randomSym () = symbol (randomLabel ())

let ofBitList l = List.foldBack consStemBit l unit

let mkSeq : List<Value> -> Value = 
    Rope.ofSeq >> Value.ofTerm >> PSeq



let doEval p io s0 = 
    match eval p io s0 with
    | Some s' -> s'
    | None -> failtestf "eval unsuccessful for program %s" (Value.prettyPrint p)

let failEval p io s0 =
    match eval p io s0 with
    | None -> () // pass - expected failure
    | Some _ -> 
        failtestf "eval unexpectedly successful for program %A stack %A" p (List.map Value.prettyPrint s0)

type ACV = 
    | Aborted of Value 
    | Committed of Value

// eff logger only accepts 'log:Value' effects and simply records values.
// Any other input will cause the effect request to fail. Intended for testing.
// (Note: a better logger would include aborted writes but distinguish them.)
type EffLogger =
    val private id : int
    val mutable private TXStack : List<Term>
    val mutable private CurrLog : Term
    new() = { id = rng.Next(); TXStack = []; CurrLog = Value.Rope.empty }

    // check whether we're mid transaction
    member x.InTX with get() = 
        not (List.isEmpty x.TXStack)

    // complete list of effects.
    member x.Outputs with get() = 
        let fn (acc : Value list) (t : Term) : Value list = 
            Seq.fold (fun l e -> (e::l)) acc (Value.Rope.toSeqRev t)
        List.fold fn (fn [] x.CurrLog) x.TXStack

    interface Effects.IEffHandler with
        member x.Eff v = 
            match v with
            | Value.Variant "log" vMsg ->
                x.CurrLog <- Value.Rope.snoc (x.CurrLog) vMsg
                ValueSome (Value.unit)
            | _ -> ValueNone
    interface Effects.ITransactional with
        member x.Try () = 
            //printf "try %d\n" x.id
            x.TXStack <- (x.CurrLog)::(x.TXStack)
            x.CurrLog <- Value.Rope.empty
        member x.Commit () = 
            //printf "commit %d\n" x.id
            match x.TXStack with
            | (tx::txs) ->
                x.CurrLog <- Value.Rope.append tx x.CurrLog
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

// programs to support testing?
let i2v = uint64 >> Value.ofNat


[<Tests>]
let test_ops = 
    // note: focusing on type-safe behaviors of programs
    testList "program evaluation" [
            testCase "stack ops" <| fun () ->
                for _ in 1 .. 10 do 
                    let v1 = mkRandomVal 3
                    let v2 = mkRandomVal 3
                    let s0 = [v1;v2]
                    let sCopy = doEval (Op lCopy) noEff s0
                    Expect.equal (sCopy) [v1;v1;v2] "copied value"
                    let sDrop = doEval (Op lDrop) noEff s0 
                    Expect.equal (sDrop) [v2] "dropped value"
                    let sSwap = doEval (Op lSwap) noEff s0
                    Expect.equal (sSwap) [v2;v1] "swapped value"

            testCase "eq" <| fun () ->
                for _ in 1 .. 100 do
                    let v1 = mkRandomVal 3
                    let v2 = pair v1 (mkRandomVal 2)
                    failEval (Op lEq) noEff [v1;v2;v2] 
                    let s' = doEval (Op lEq) noEff [v1;v1;v2]
                    Expect.equal (s') [v2] "eq drops equal values from stack"

            testCase "record ops" <| fun () ->
                for _ in 1 .. 100 do
                    let k = randomSym ()
                    let r0 = mkRandomRecord 3 9
                    let v = mkRandomIntVal ()

                    let rwk = Value.record_insert k v r0
                    let rwo = Value.record_delete k r0

                    // Get from record.
                    failEval (Op lGet) noEff [k; rwo]
                    let sGet = doEval (Op lGet) noEff [k; rwk]
                    Expect.equal (sGet) [v] "equal get"

                    // Put into record.
                    let sPut1 = doEval (Op lPut) noEff [k; rwk; v]
                    let sPut2 = doEval (Op lPut) noEff [k; rwo; v]
                    Expect.equal (sPut1) [rwk] "equal put with key"
                    Expect.equal (sPut2) [rwk] "equal put without key"

                    // Delete from record.
                    let sDel1 = doEval (Op lDel) noEff [k; rwk]
                    let sDel2 = doEval (Op lDel) noEff [k; rwo]
                    Expect.equal (sDel1) [rwo] "equal delete label"
                    Expect.equal (sDel2) [rwo] "equal delete missing label"

            testCase "data" <| fun () ->
                for _ in 1 .. 1000 do
                    let v = mkRandomVal 5
                    let s' = doEval (Data v) noEff []
                    Expect.equal (s') [v] "eq data"

            testCase "seq" <| fun () ->
                let p = mkSeq [ Op lCopy
                              ; Data (Value.symbol "foo"); Op lGet
                              ; Op lSwap
                              ; Data (Value.symbol "bar"); Op lGet]
                for _ in 1 .. 1000 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let r0 = Value.asRecord ["foo";"bar"] [a; b]
                    let s' = doEval p noEff [r0]
                    let expected = [b; a]
                    Expect.equal (s') expected "expected seq result"



            testCase "dip" <| fun () ->
                for _ in 1 .. 10 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let c = mkRandomIntVal ()
                    let s0 = [c;b;a]
                    let sDip = doEval (Dip (Op lSwap)) noEff s0
                    Expect.equal (sDip) [c;a;b] "dip swap"

            testCase "cond" <| fun () ->
                // eq returning a boolean instead of pass/fail.
                let t = Value.symbol "true"
                let f = Value.symbol "false"
                let eqBool = Cond (Op lEq, Data t, mkSeq [Op lDrop; Op lDrop; Data f])

                for i in 1 .. 3 do
                    for j in 1 .. 3 do
                        let s0 = [i2v i; i2v j]
                        let s' = doEval eqBool noEff s0
                        let expect = if (i = j) then t else f
                        Expect.equal s' [expect] "boolean equality"

            testCase "loop while" <| fun () ->
                // test is to reverse a large random bitstring
                let b0 = ofBitList [false] 
                let b1 = ofBitList [true] 
                let pPopPrefix = mkSeq [Op lCopy; Dip(Op lGet)]
                let pPopBit = Cond(mkSeq [Data b0; pPopPrefix], Nop, mkSeq [Data b1; pPopPrefix])
                let pTag = mkSeq [Dip(Data Value.unit); Op lPut]
                let pRevBits = mkSeq [ Data (Value.unit)
                                     ; While( Dip(pPopBit), mkSeq [ Op lSwap; pTag ])
                                     ; Op lSwap
                                     ; Data (Value.unit); Op lEq
                                     ]
                let bits = Array.foldBack consStemByte (randomBytes 1250) unit
                let bitsRev = 
                    let rec loop acc bs =
                        if not (isStem bs) then acc else
                        let acc' = consStemBit (stemHead bs) acc
                        let bs' = stemTail bs
                        loop acc' bs' 
                    loop unit bits
                let s' = doEval pRevBits noEff [bits]
                Expect.equal s' [bitsRev] "reversed bits"

            testCase "loop until" <| fun () ->
                // test is to reverse a large random bitstring
                let b0 = ofBitList [false] |> Value.ofBits
                let b1 = ofBitList [true] |> Value.ofBits
                let pPopPrefix = mkSeq [Op lCopy; Dip(Op lGet)]
                let pPopBit = Cond(mkSeq [Data b0; pPopPrefix], Nop, mkSeq [Data b1; pPopPrefix])
                let pTag = mkSeq [Dip(Data Value.unit); Op lPut]
                let pRevBits = mkSeq [ Data (Value.unit)
                                     ; Until( Dip(mkSeq [Data Value.unit; Op lEq])
                                            , mkSeq [Dip(pPopBit); Op lSwap; pTag ])
                                     ]
                let bits = Array.foldBack consStemByte (randomBytes 1250) unit
                let bitsRev = 
                    let rec loop acc bs =
                        if not (isStem bs) then acc else
                        let acc' = consStemBit (stemHead bs) acc
                        let bs' = stemTail bs
                        loop acc' bs' 
                    loop unit bits
                let s' = doEval pRevBits noEff [bits]
                Expect.equal s' [bitsRev] "reversed bits"

            testCase "toplevel eff" <| fun () ->
                let varSym s = mkSeq [Data Value.unit; Data (Value.symbol s); Op lPut]
                let pLogMsg = mkSeq [varSym "log"; Op lEff ]
                failEval (Op lEff) (EffLogger()) [Value.symbol "oops"]
                for _ in 1 .. 100 do
                    let msg = mkRandomIntVal ()
                    let eff = EffLogger() 
                    let sLog = doEval pLogMsg eff [msg]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (eff.Outputs) [msg] "logged outputs"
                    Expect.equal (sLog) [Value.unit] "eff return val"

            testCase "transactional eff" <| fun () ->
                let varSym s = mkSeq [Data Value.unit; Data (Value.symbol s); Op lPut]
                let tryOp p = Cond (p, Nop, Nop)
                let tryEff s = tryOp (mkSeq [varSym s; Op lEff])
                let tryEff3 = mkSeq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                for _ in 1 .. 100 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let c = mkRandomIntVal ()
                    let eff = EffLogger()
                    let sLog = doEval tryEff3 eff [a;b;c]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (eff.Outputs) [a;c] "expected messages"
                    Expect.equal (sLog) [Value.unit; b; Value.unit] "expected results"

            testCase "env" <| fun () ->
                // behavior for 'env': 
                //  increment an effects counter
                //  rename "oops" effects to "log" and vice versa.
                let b1 = (ofBitList [true])
                let varSym s = mkSeq [Data Value.unit; Data (Value.symbol s); Op lPut]
                let pInc = mkSeq [Data Value.unit; Data b1; Op lPut] // unary counter
                let getSym s = mkSeq [Data (Value.symbol s); Op lGet]
                let pRN s1 s2 = Cond (getSym s1, varSym s2
                                     ,Cond(getSym s2, varSym s1, Nop))
                let withCRN s1 s2 p = mkSeq [ Data (Value.unit)
                                    ; Env (mkSeq [ pInc; Dip (mkSeq [pRN s1 s2; Op lEff])] 
                                          ,p)]
                let tryOp p = Cond (p, Nop, Nop)
                let tryEff s = tryOp (mkSeq [varSym s; Op lEff])
                let tryEff3 = mkSeq [tryEff "log"; Dip (tryEff "oops"); Dip (Dip (tryEff "log"))]
                let pTest = withCRN "log" "oops" tryEff3
                for _ in 1 .. 100 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let c = mkRandomIntVal ()
                    let eff = EffLogger()
                    let sLog = doEval pTest eff [a;b;c]
                    Expect.isFalse (eff.InTX) "completed transactions"
                    Expect.equal (eff.Outputs) [b] "expected messages"
                    Expect.equal (sLog) [b1; a; Value.unit; c] "expected results"

            testCase "prog" <| fun () ->
                for _ in 1 .. 10 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let s0 = [a;b]
                    let sProg = doEval (Prog (mkRandomRecord 2 6, Op lSwap)) noEff s0 
                    Expect.equal (sProg) [b;a] "prog"
    ]