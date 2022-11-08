
module Glas.FTList

open Expecto
open Glas
open RandVal

let randomList m n =
    List.init (randomRange m n) (fun _ -> mkRandomIntVal ())

let ropeOfList l =
    Value.Rope.ofSeq l

let ropeToList t =
    Value.Rope.foldBack (fun v l -> (v::l)) t []

let ropeToArray t =
    match Value.ofTerm t with
    | Value.ValueArray a -> a
    | _ -> failtest "could not convert term to array"

// goal here is to produce a messy rope value
let randomRope m n =
    let mutable t = Value.Leaf
    while((Value.Rope.len t) <= uint64 n) do
        // add content
        let l = randomList 10 20
        let r = randomList 10 20
        t <- Value.Rope.append (ropeOfList l) (Value.Rope.append t (ropeOfList r))
        for _ in 1 .. 4 do 
            // cut and switch
            let ixCut = uint64 <| randomRange 0 (int (Value.Rope.len t))
            let tl = Value.Rope.take ixCut t
            let tr = Value.Rope.drop ixCut t
            t <- Value.Rope.append tr tl 
    Value.Rope.take (uint64 (randomRange m n)) t


[<Tests>]
let tests =
    testList "rope tests" [
        testCase "empty" <| fun () ->
            Expect.equal (ropeToList Value.Rope.empty) (List.empty) "empty is as empty does"

        testCase "list conversions" <| fun () ->
            let l = randomList 200 400
            Expect.equal (ropeToList (ropeOfList l)) l "ofList toList"
            let f = randomRope 200 400
            Expect.equal f (ropeOfList (ropeToList f)) "toList ofList"

        testCase "map" <| fun () ->
            let l = randomList 200 400
            let fn v = Value.pair Value.unit (Value.pair v Value.unit)
            Expect.equal (ropeToList (Value.Rope.map fn (ropeOfList l))) (List.map fn l) "equality over map"


        testCase "length" <| fun () ->
            let l = List.map (fun i -> randomList i (2 * i)) [0; 5; 10; 20; 100; 200; 400; 800; 1600]
            let lz = List.map (List.length) l
            let tlz = List.map (ropeOfList >> Value.Rope.len >> int) l
            Expect.equal tlz lz "equal list lengths"

            let f = randomRope 2000 4000
            Expect.equal (List.length (ropeToList f)) (int (Value.Rope.len f)) "equal lengths irregular"

        testCase "split" <| fun () ->
            let len = 1000
            let f = randomRope len len
            let l = ropeToList f
            for _ in 1 .. 100 do
                let xz = randomRange 0 len 
                let (ll, lr) = List.splitAt xz l
                let (fl, fr) = (Value.Rope.take (uint64 xz) f, Value.Rope.drop (uint64 xz) f)
                Expect.equal (ropeToList fl) ll "left equal"
                Expect.equal (ropeToList fr) lr "right equal"

        testCase "append" <| fun () ->
            for _ in 1 .. 100 do
                let l1 = randomRope 0 1000
                let l2 = randomRope 0 1000
                Expect.equal (ropeToList (Value.Rope.append l1 l2)) 
                             (List.append (ropeToList l1) (ropeToList l2)) "equal append"

        testCase "arrays" <| fun () ->
            let t = randomRope 1000 2000
            let a = ropeToArray t
            Expect.equal (a.Length) (int (Value.Rope.len t)) "length preserved"
            Expect.equal (Value.Rope.ofSeq a) t "round trip through array"
            Expect.equal (List.ofArray a) (ropeToList t) "indirect through array"

        testCase "seqs" <| fun () ->
            let l = randomList 1000 2000
            Expect.equal (List.ofSeq (Value.Rope.toSeq (ropeOfList l))) l "equal via toSeq"
            Expect.equal (ropeToList (Value.Rope.ofSeq (List.toSeq l))) l "equal via ofSeq"

        testCase "equal" <| fun () ->
            let t = randomRope 1000 2000
            Expect.equal t t "eq"
            Expect.equal t (Value.Rope.drop 0UL t) "equal drop nothing"
            Expect.equal t (Value.Rope.take (Value.Rope.len t) t) "equal take everything"
            Expect.notEqual t (Value.Rope.drop 1UL t) "not eq drop"
            Expect.notEqual t (Value.Rope.cons (Value.unit) t) "not eq cons"
            Expect.notEqual t (Value.Rope.snoc t (Value.unit)) "not eq snoc"

        testCase "index" <| fun () ->
            let t = randomRope 1000 2000
            let a = ropeToArray t
            Expect.equal (int (Value.Rope.len t)) (a.Length) "length preserved"
            for ix in 0 .. (a.Length - 1) do
                Expect.equal (Value.Rope.item (uint64 ix) t) (Array.item ix a) "equal list item"
    ]
