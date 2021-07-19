
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

// this variation is intended to ensure an irregular branch structure
let randomFTList m n =
    let mutable ftl = FTList.empty
    while (int (FTList.length ftl) < n) do
        ftl <- FTList.append ftl (FTList.ofList (randomList 1 20))
        ftl <- FTList.append (FTList.ofList (randomList 1 20)) ftl
        let shuffle = randomRange 0 (int (FTList.length ftl))
        let (l,r) = FTList.splitAt (uint64 shuffle) ftl
        ftl <- FTList.append r l
    FTList.take (uint64 (randomRange m n)) ftl
    

[<Tests>]
let tests =
    testList "ftlist tests" [
        testCase "empty" <| fun () ->
            Expect.equal (FTList.toList FTList.empty) (List.empty) "empty is as empty does"

        testCase "list conversions" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal (FTList.toList (FTList.ofList l)) l "ofList toList"
            let f = randomFTList 2000 4000
            Expect.isTrue (FTList.eq f (FTList.ofList (FTList.toList f))) "toList ofList"

        testCase "reverse" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal (FTList.toList (FTList.rev (FTList.ofList l))) (List.rev l) "round trip reversed"
            let f = randomFTList 2000 4000
            Expect.equal (FTList.toList (FTList.rev f)) (List.rev (FTList.toList f)) "equal reverse irregular"

        testCase "map" <| fun () ->
            let l = randomList 2000 4000
            Expect.equal (FTList.toList (FTList.map hash (FTList.ofList l))) (List.map hash l)  "hashes to hashes"

        testCase "length" <| fun () ->
            let l = List.map (fun i -> randomList i (2 * i)) [0; 5; 10; 20; 100; 200; 400; 800; 1600]
            let lz = List.map (List.length) l
            let ftlz = List.map (FTList.ofList >> FTList.length >> int) l
            Expect.equal ftlz lz "equal list lengths"

            let f = randomFTList 2000 4000
            Expect.equal (List.length (FTList.toList f)) (int (FTList.length f)) "equal lengths irregular"

        testCase "split" <| fun () ->
            let len = 1000
            let l = randomList len len
            let f = FTList.ofList l
            for _ in 1 .. 100 do
                let xz = randomRange 0 len 
                let (ll, lr) = List.splitAt xz l
                let (fl, fr) = FTList.splitAt (uint64 xz) f
                Expect.equal (FTList.toList fl) ll "left equal"
                Expect.equal (FTList.toList fr) lr  "right equal"

        testCase "append" <| fun () ->
            for _ in 1 .. 100 do
                let l1 = randomFTList 0 1000
                let l2 = randomFTList 0 1000
                Expect.equal (FTList.toList (FTList.append l1 l2)) (List.append (FTList.toList l1) (FTList.toList l2)) "equal append"

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

        testCase "equal" <| fun () ->
            let l = randomList 100 200
            Expect.isTrue (FTList.eq (FTList.ofList l) (FTList.ofList l)) "eq"
            Expect.isFalse (FTList.eq (FTList.ofList l) (FTList.append (FTList.ofList l) (FTList.singleton 17))) "not eq"

        testCase "compare" <| fun () ->
            let l = randomList 100 200
            let f1 = FTList.ofList l
            let f2 = FTList.map id f1
            let fx1 = FTList.ofList [-1]
            let fx2 = FTList.ofList [0]
            Expect.equal (FTList.compare f1 f2) 0 "eq compare"
            Expect.equal (FTList.compare (FTList.append f1 fx1) f2) 1 "longer lists"
            Expect.equal (FTList.compare f1 (FTList.append f2 fx1)) -1 "shorter list"
            Expect.equal (FTList.compare (FTList.append f1 fx2) (FTList.append f2 fx1)) 1 "diff lists"
            Expect.equal (FTList.compare (FTList.append f1 fx1) (FTList.append f2 fx2)) -1 "diff lists 2"
            Expect.equal (FTList.compare (FTList.append f1 fx1) (FTList.append f2 fx1)) 0 "eq compare 2"

        testCase "index" <| fun () ->
            let fl = randomFTList 1000 2000
            let a = FTList.toArray fl
            for ix in 0 .. (int (FTList.length fl) - 1) do
                Expect.equal (FTList.item (uint64 ix) fl) (Array.item ix a) "equal list item"

    ]
