
module Glas.TestBits

open Expecto
open Glas

let inline randomBool (rng : System.Random) =
    (0 <> (1 &&& rng.Next()))

let rec randomListLoop rng n acc =
    if (0 >= n) then acc else
    let b = randomBool rng
    randomListLoop rng (n - 1) (b :: acc)

let randomList n =
    randomListLoop (System.Random()) n []

[<Tests>]
let tests =
    testList "bit tests" [
        testCase "23" <| fun () ->
            let b = Bits.ofByte 23uy
            let bl = [false; false; false; true; false; true; true; true]
            Expect.equal b (Bits.ofList bl) "equal lists"
            Expect.equal (Bits.length b) 8 "byte length"
            Expect.equal (Bits.rev b) (Bits.ofList (List.rev bl)) "reversed"
            Expect.equal 8 (Bits.sharedPrefixLen b b) "shared prefix"

        testCase "big bit lists" <| fun () ->
            let len = 223
            let b0 = randomList len
            let b1 = List.append b0 (true :: randomList 13)
            let b2 = List.append b0 (false :: randomList 29)
            Expect.equal (Bits.toList (Bits.ofList b0)) b0 "equal encoding via list"
            Expect.equal (Bits.toList (Bits.rev (Bits.ofList b0))) (List.rev b0) "equal reversed lists"
            Expect.equal (Bits.sharedPrefixLen (Bits.ofList b1) (Bits.ofList b2)) len "shared prefix"
            Expect.equal (Bits.ofList (List.append b1 b2)) (Bits.append (Bits.ofList b1) (Bits.ofList b2)) "appending"

        testCase "take and skip" <| fun () ->
            let len = 500
            let tk = 157
            let b = randomList len
            Expect.equal (Bits.toList (Bits.take tk (Bits.ofList b))) (List.take tk b) "equal take"
            Expect.equal (Bits.toList (Bits.skip tk (Bits.ofList b))) (List.skip tk b) "equal skip"

        testCase "bitwise negation" <| fun () ->
            let b0 = randomList 599
            Expect.equal (Bits.ofList b0 |> Bits.bneg |> Bits.toList) (List.map (not) b0) "equal negation"

        testCase "bitwise equality" <| fun () ->
            let len = 461
            let b0 = randomList len
            let b1 = randomList len
            Expect.equal (Bits.beq (Bits.ofList b0) (Bits.ofList b1) |> Bits.toList) (List.map2 (=) b0 b1) "bitwise equal"
        
        testCase "bitwise xor" <| fun () ->
            let len = 1373
            let b0 = randomList len
            let b1 = randomList len
            Expect.equal (Bits.bneq (Bits.ofList b0) (Bits.ofList b1) |> Bits.toList) (List.map2 (<>) b0 b1) "bitwise xor"

        testCase "bitwise and" <| fun () ->
            let len = 503
            let b0 = randomList len
            let b1 = randomList len
            Expect.equal (Bits.bmin (Bits.ofList b0) (Bits.ofList b1) |> Bits.toList) (List.map2 (&&) b0 b1) "bitwise and / min"

        testCase "bitwise or" <| fun () ->
            let len = 1231
            let b0 = randomList len
            let b1 = randomList len
            Expect.equal (Bits.bmax (Bits.ofList b0) (Bits.ofList b1) |> Bits.toList) (List.map2 (||) b0 b1) "bitwise or/max"

    ]
