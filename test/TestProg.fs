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


[<Tests>]
let test_ops = 
    // note: focusing on type-safe behaviors of programs
    testList "unit test of interpreter" [
            testCase "stack ops" <| fun () ->
                for _ in 1 .. 10 do 
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7
                    let e0 = dataStack [v1;v2]
                    match Interpreter.interpret (Op Copy) e0 with
                    | Some e' -> Expect.equal e'.DS [v1;v1;v2] "copied value"
                    | None -> failtest "copy failed"

                    match Interpreter.interpret (Op Drop) e0 with
                    | Some e' -> Expect.equal (e'.DS) [v2] "dropped value"
                    | None -> failtest "drop failed"

                    match Interpreter.interpret (Op Swap) e0 with
                    | Some e' -> Expect.equal (e'.DS) [v2;v1] "swapped value"
                    | None -> failtest "swap failed"


            testCase "eq" <| fun () ->
                for _ in 1 .. 10 do
                    let v1 = randomBytes 6
                    let v2 = randomBytes 7

                    match Interpreter.interpret (Op Eq) (dataStack [v1;v2]) with
                    | None -> () // pass
                    | Some _ -> failtest "eq succeeded incorrectly"

                    match Interpreter.interpret (Op Eq) (dataStack [v1;v1;v2]) with
                    | Some e' -> Expect.equal (e'.DS) [v1;v1;v2] "eq passed"
                    | None -> failtest "eq failed incorrectly"

            testCase "record ops" <| fun () ->
                for _ in 1 .. 1000 do
                    let k = randomSym ()
                    let r0 = randomRecord ()
                    let v = randomBytes 6

                    let rwk = Value.record_insert k v r0
                    let rwo = Value.record_delete k r0

                    // Get from record.
                    match Interpreter.interpret (Op Get) (dataStack [Value.ofBits k; rwk] ) with
                    | Some e' -> Expect.equal e'.DS [v] "expected value get"
                    | None -> failtest "did not get value"

                    match Interpreter.interpret (Op Get) (dataStack [Value.ofBits k; rwo]) with
                    | None -> () // pass
                    | Some _ -> failtest "get succeeds on field not in record"

                    // Put into record.
                    match Interpreter.interpret (Op Put) (dataStack [Value.ofBits k; v; rwk]) with
                    | Some e' -> Expect.equal (e'.DS) [rwk] "equal put"
                    | None -> failtest "failed to put"

                    match Interpreter.interpret (Op Put) (dataStack [Value.ofBits k; v; rwo]) with
                    | Some e' -> Expect.equal (e'.DS) [rwk] "equal put"
                    | None -> failtest "failed to put"

                    // Delete from record.
                    match Interpreter.interpret (Op Del) (dataStack [Value.ofBits k; rwk]) with
                    | Some e' -> Expect.equal (e'.DS) [rwo] "equal delete"
                    | None -> failtest "failed to delete"

                    match Interpreter.interpret (Op Del) (dataStack [Value.ofBits k; rwo]) with
                    | Some e' -> Expect.equal (e'.DS) [rwo] "equal delete"
                    | None -> failtest "failed to delete"

        // list ops
        // bitstring ops
        // arithmetic ops
        // control ops

    ]