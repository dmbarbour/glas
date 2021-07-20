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
    | 7 -> Prog (Do=randomProg (d-1), Note=(randomRecord ()))
    | 8 -> Note (randomRecord ())
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
                for _ in 1 .. 1000 do
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    failEval (Op Eq) (dataStack [v1;v2]) 
                    let e' = doEval (Op Eq) (dataStack [v1;v1;v2])
                    Expect.equal (e'.DS) [v1;v1;v2] "equal stacks"

            testCase "record ops" <| fun () ->
                for _ in 1 .. 1000 do
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
                for _ in 1 .. 10 do
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

                    let l12 = Value.ofFTList (FTList.append (Value.toFTList l1) (Value.toFTList l2)) 
                    let eJoin = doEval (Op Join) (dataStack [l2;l1]) 
                    Expect.equal (eJoin.DS) [l12] "equal joins"


        // bitstring ops
        // arithmetic ops
        // control ops

    ]