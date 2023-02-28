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

// arrange these locks to run sequentially
type LockHolder =
    val private LockObj : obj
    new(lockObj) =
        System.Threading.Monitor.Enter(lockObj)
        { LockObj = lockObj }
    interface System.IDisposable with
        member lh.Dispose() =
            System.Console.Out.Flush()
            System.Console.Error.Flush()
            System.Threading.Thread.Sleep(100)
            System.Threading.Monitor.Exit(lh.LockObj)

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

let testLock = ref 0

[<Tests>]
let test_ops = 
    // note: focusing on type-safe behaviors of programs
    // note: needs more precision for compiled interpreters
    testList "program evaluation" [

            testCase "nop" <| fun () ->
                let v = mkRandomVal 3 
                let s' = doEval (Nop) noEff [v]
                Expect.equal s' [v] "eq nop"

            testCase "data" <| fun () ->
                let v = mkRandomVal 3
                let s' = doEval (Data v) noEff []
                Expect.equal (s') [v] "eq data"

            testCase "seq" <| fun () ->
                let v1 = mkRandomVal 3
                let v2 = Value.pair v1 (mkRandomVal 2)
                let p = [Data v1; Data v2] |> Value.ofList |> PSeq
                let s' = doEval p noEff []
                Expect.equal (s') [v2; v1] "eq data seq"

            testCase "dip" <| fun () ->
                let v1 = mkRandomVal 3
                let v2 = Value.pair v1 (mkRandomVal 2)
                let s' = doEval (Dip (Data v1)) noEff [v2]
                Expect.equal (s') [v2; v1] "dip data" 
            
            testCase "dip seq" <| fun () ->
                // compiles multiple locals into program
                let a = mkRandomVal 3
                let b = Value.pair a (mkRandomVal 2)
                let c = Value.pair b (mkRandomVal 1)
                let p = [Data a; Dip (Data b); Dip (Dip (Data c))] |> Value.ofList |> PSeq
                let s' = doEval p noEff []
                Expect.equal s' [a; b; c] "dip seq data"

            testCase "copy" <| fun () ->
                let a = mkRandomVal 3
                let b = Value.pair a (mkRandomVal 2)
                let s' = doEval (Op lCopy) noEff [a;b]
                Expect.equal s' [a;a;b] "copy data"

            testCase "drop" <| fun () ->
                let a = mkRandomVal 3
                let b = Value.pair a (mkRandomVal 2)
                let s' = doEval (Op lDrop) noEff [a;b]
                Expect.equal s' [b] "drop data"

            testCase "swap" <| fun () ->
                // compiles multiple locals into program
                let a = mkRandomVal 3
                let b = Value.pair a (mkRandomVal 2)
                let c = Value.pair b (mkRandomVal 1)
                let s' = doEval (Op lSwap) noEff [a;b;c]
                Expect.equal s' [b;a;c] "swap data"

            testCase "eq" <| fun () ->
                let v1 = mkRandomVal 3
                let v2 = pair v1 (mkRandomVal 2)
                failEval (Op lEq) noEff [v1;v2;v2] 
                let s' = doEval (Op lEq) noEff [v1;v1;v2]
                Expect.equal (s') [v2] "eq drops equal values from stack"


            testCase "record ops" <| fun () ->
                for _ in 1 .. 10 do
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

            testCase "seq2" <| fun () ->
                let p = mkSeq [ Op lCopy
                              ; Data (Value.symbol "foo"); Op lGet
                              ; Op lSwap
                              ; Data (Value.symbol "bar"); Op lGet]
                for _ in 1 .. 10 do
                    let a = mkRandomIntVal ()
                    let b = mkRandomIntVal ()
                    let r0 = Value.asRecord ["foo";"bar"] [a; b]
                    let s' = doEval p noEff [r0]
                    let expected = [b; a]
                    Expect.equal (s') expected "expected seq result"

            testCase "dip2" <| fun () ->
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

            testCase "fail" <| fun () ->
                failEval (Op lFail) noEff [mkRandomVal 1]

            testCase "halt" <| fun () ->
                let eMsg = Value.symbol "fail-test"
                failEval (Halt eMsg) noEff [mkRandomVal 1]

            testCase "halt uncaught" <| fun () ->
                let eMsg = Value.symbol "try-test"
                let p = Cond ((Halt eMsg), Nop, Nop)
                failEval p noEff [mkRandomVal 1]

            testCase "eff fail" <| fun () ->
                failEval (Op lEff) (EffLogger()) [Value.symbol "oops"]

            testCase "eff ok" <| fun () ->
                let effLog = EffLogger()
                let msg = mkRandomVal 3
                let s' = doEval (Op lEff) effLog [Value.variant "log" msg]
                Expect.equal s' [Value.unit] "log output is unit"
                Expect.equal (effLog.Outputs) [msg] "logged outputs match"

            testCase "toplevel eff" <| fun () ->
                let varSym s = mkSeq [Data Value.unit; Data (Value.symbol s); Op lPut]
                let pLogMsg = mkSeq [varSym "log"; Op lEff ]
                failEval (Op lEff) (EffLogger()) [Value.symbol "oops"]
                for _ in 1 .. 10 do
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


            testCase "env dip" <| fun () ->
                // in this test the env is not called, but acts as a dip
                let v1 = mkRandomVal 3
                let v2 = Value.pair v1 (mkRandomVal 2)
                let v3 = Value.pair v2 (mkRandomVal 1)
                let p = Env((Op lFail), (Op lSwap))
                let s' = doEval p noEff [v1;v2;v3]
                Expect.equal s' [v1;v3;v2] "swap under env state"

            testCase "env swap" <| fun () ->
                // in this test case, env is called and swaps args.
                let v1 = mkRandomVal 3
                let v2 = Value.pair v1 (mkRandomVal 2)
                let v3 = Value.pair v2 (mkRandomVal 1)
                let p = Env((Op lSwap), (Op lEff))
                let s' = doEval p noEff [v1;v2;v3]
                Expect.equal s' [v2;v1;v3] "swap with eff state"

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
                for _ in 1 .. 10 do
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