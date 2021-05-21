
module Glas.FTList

open Expecto
open Glas

let rng = System.Random()

let rec mkRandomList sz acc =
    if (0 >= sz) then acc else
    mkRandomList (sz - 1) (rng.Next() :: acc)

let randomRange m n = 
    (min m n) + (rng.Next() % (1 + (abs (m - n)))) 

let randomList m n =
    mkRandomList (randomRange m n) (List.empty)

[<Tests>]
let tests =
    testList "ftlist tests" [
        testCase "empty" <| fun () ->
            Expect.equal (List.empty) (FTList.toList FTList.empty) "empty is as empty does"

        testCase "list conversions" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal l (FTList.toList (FTList.ofList l)) "ofList toList"

        testCase "reverse" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal (List.rev l) (FTList.toList (FTList.rev (FTList.ofList l))) "round trip reversed"

        testCase "map" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal (List.map hash l) (FTList.toList (FTList.map hash (FTList.ofList l))) "hashes to hashes"

        testCase "length" <| fun () ->
            let l = List.map (fun i -> randomList i (2 * i)) [0; 5; 10; 20; 100; 200; 400; 800; 1600]
            let lz = List.map (fun x -> List.length x) l
            let ftlz = List.map (fun x -> FTList.length (FTList.ofList x)) l
            Expect.equal ftlz lz "equal list lengths"

        testCase "split" <| fun () ->
            let len = 1000
            let l = randomList len len
            let f = FTList.ofList l
            for _ in 1 .. 100 do
                let xz = randomRange 0 len 
                let (ll, lr) = List.splitAt xz l
                let (fl, fr) = FTList.splitAt xz f
                Expect.equal (FTList.toList fl) ll "left equal"
                Expect.equal (FTList.toList fr) lr  "right equal"

        testCase "append" <| fun () ->
            for _ in 1 .. 100 do
                let l1 = randomList 0 1000
                let l2 = randomList 0 1000
                Expect.equal (FTList.toList (FTList.append (FTList.ofList l1) (FTList.ofList l2))) (List.append l1 l2) "equal append"

        testCase "toArray" <| fun () ->
            let l = randomList 1000 2000
            Expect.equal (List.ofArray (FTList.toArray (FTList.ofList l))) l "equal round trip through array"

        testCase "toSeq" <| fun () ->
            let l = randomList 1000 2000
            Expect.equal (List.ofSeq (FTList.toSeq (FTList.ofList l))) l "round trip via seq"

        testCase "ofArray" <| fun () ->
            let l = randomList 1000 2000
            Expect.equal (FTList.toList (FTList.ofArray (List.toArray l))) l "equal via ofArray"

        testCase "ofSeq" <| fun () ->
            let l = randomList 1000 2000
            Expect.equal (FTList.toList (FTList.ofSeq (List.toSeq l))) l "equal via ofSeq"

    ]
