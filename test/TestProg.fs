module Glas.TestProg

open Expecto
open Glas

let rng = System.Random()

let randomRange lb ub =
    lb + (rng.Next() % (1 + ub - lb))

// require a successful parse (or raises exception, fails test)
let doParse = Program.tryParse >> Option.get 


let opArray = Array.ofList Program.op_list
let randomOp () = opArray.[rng.Next() % opArray.Length]

// random program suitable for parse and print tests, but 
// almost certainly invalid for interpretation.
let rec randomProg d =
    if (d < 1) then Op (randomOp ()) else
    match randomRange 1 10 with
    | 1 -> Dip (randomProg (d - 1))
    | 2 -> Data (Program.print (randomProg (d-1)))
    | 3 -> 
        let seqLen = randomRange 0 10
        Seq [ for _ in 1 .. seqLen do yield randomProg (d - 1)]
    | 4 -> Cond (Try=randomProg (d-1), Then=randomProg (d-1), Else=randomProg (d-1))
    | 5 -> Loop (While=randomProg (d-1), Do=randomProg (d-1))
    | 6 -> Env (With=randomProg (d-1), Do=randomProg (d-1))
    | 7 -> Prog (Do=randomProg (d-1), Note=(Program.print <| randomProg (d-1)))
    | 8 -> Note (Program.print (randomProg (d-1)))
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
