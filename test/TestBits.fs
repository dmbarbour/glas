
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

let foldMSB (s : seq<bool>) : uint64 =
    let addBit n b =
        let e = if b then 1UL else 0UL
        (n <<< 1) ||| e
    Seq.fold addBit 0UL s

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

        testCase "n64" <| fun () ->
            let bl = randomList 64
            let n64 = foldMSB (List.toSeq bl)
            Expect.equal (Bits.ofList bl) (Bits.ofU64 n64) "equal n64"

        testCase "n32" <| fun () ->
            let bl = randomList 32
            let n32 = foldMSB (List.toSeq bl) |> uint32
            Expect.equal (Bits.ofList bl) (Bits.ofU32 n32) "equal n32"

        testCase "n16" <| fun () ->
            let bl = randomList 16
            let n16 = foldMSB (List.toSeq bl) |> uint16
            Expect.equal (Bits.ofList bl) (Bits.ofU16 n16) "equal n16"

        testCase "match sizes" <| fun () ->
            for sz in 0 .. 128 do
                let l = randomList sz
                let c = foldMSB (List.toSeq l) 
                match (Bits.ofList l) with
                | Bits.U64 n64 -> 
                    Expect.equal sz 64 "size 64" 
                    Expect.equal (uint64 c) n64 "equal n64"
                | _ when sz = 64 -> failtest "failed to match U64"
                | Bits.U32 n32 -> 
                    Expect.equal sz 32 "size 32"
                    Expect.equal (uint32 c) n32 "equal n32"
                | _ when sz = 32 -> failtest "failed to match U32"
                | Bits.U16 n16 -> 
                    Expect.equal sz 16 "size 16"
                    Expect.equal (uint16 c) n16 "equal n16"
                | _ when sz = 16 -> failtest "failed to match U32"
                | Bits.Byte n8 -> 
                    Expect.equal sz 8 "size 8"
                    Expect.equal (uint8 c) n8 "equal n8"
                | _ when sz = 8 -> failtest "failed to match Byte"
                | _ -> ()

        testCase "nat" <| fun () ->
            for x in 1 .. 64 do
                let n = foldMSB (List.toSeq (true::randomList (x-1)))  
                let b = Bits.ofNat64 n
                Expect.equal (Bits.length b) x "expected length"
                match b with
                | Bits.Nat64 n' -> Expect.equal n' n "match values"
                | _ -> failtest "did not match Nat64"

            match Bits.empty with
            | Bits.Nat64 n -> Expect.equal n 0UL "match empty bits"
            | _ -> failtest "did not match empty bits"

            match Bits.ofList (randomList 65) with
            | Bits.Nat64 _ -> failtest "match oversized list"
            | _ -> ()

        testCase "bits to bigint to bits" <| fun () ->
            Expect.equal (Bits.empty) (Bits.ofI 0I) "zero bits"
            for x in 1 .. 400 do
                let b = Bits.ofList (true::randomList (x - 1)) 
                let i = Bits.toI b
                let b' = Bits.ofI i
                Expect.equal b b' "equal round trip conversion"

        testCase "bigint to bits to bigint" <| fun () ->
            let rng = System.Random()
            for sz in 1 .. 50 do
                let arr = Array.zeroCreate (1 + sz)
                for ix in 1 .. sz do
                    arr.[ix - 1] <- byte (rng.Next())
                let i = bigint(arr)
                let b = Bits.ofI i
                let i' = Bits.toI b
                Expect.equal i i' "equal round trip conversion"

        testCase "list length" <| fun () ->
            for n in 0 .. 256 do
                let l = randomList n
                Expect.equal (Bits.length (Bits.ofList l)) n "compute length correctly"

        testCase "big bit lists" <| fun () ->
            let len = 223
            let b0 = randomList len
            let b1 = List.append b0 (true :: randomList 13)
            let b2 = List.append b0 (false :: randomList 29)
            Expect.equal (Bits.length (Bits.ofList b0)) len "expected length"
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

    ]
