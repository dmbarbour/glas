
module Glas.TestVal

open Expecto
open Glas.Value

let rng = System.Random()

let rec mkRandomList sz acc =
    if (0 >= sz) then acc else
    mkRandomList (sz - 1) (rng.Next() :: acc)

let randomRange m n = 
    (min m n) + (rng.Next() % (1 + (abs (m - n)))) 

let randomList m n =
    mkRandomList (randomRange m n) (List.empty)

let randomBytes len =
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- byte (rng.Next())
    ofBinary arr

let randomSym len =
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- 0x61uy + byte (rng.Next() % 26)
    System.Text.Encoding.UTF8.GetString(arr)

[<Tests>]
let tests = 
    testList "Glas values" [
        testCase "unit is unit" <| fun () ->
            Expect.isTrue (isUnit unit) "isUnit unit"
        
        testCase "unit comparisons" <| fun () ->
            Expect.equal unit unit "unit eq unit"
            Expect.notEqual unit (left unit) "unit not-eq false"
            Expect.notEqual unit (right unit) "unit not-eq true"
            Expect.equal (compare unit unit) 0 "compare unit"
            Expect.isLessThan unit (left unit) "compare unit false"
            Expect.isLessThan unit (right unit) "compare unit true"

        testCase "bool comparisons" <| fun () ->
            let t = right unit
            let f = left unit
            Expect.equal t t "eq t t"
            Expect.equal f f "eq f f"
            Expect.notEqual t f "eq t f"
            Expect.notEqual f t "eq f t"
            Expect.equal (compare t t) 0 "cmp t t"
            Expect.equal (compare f f) 0 "cmp f f"
            Expect.equal (compare t f) 1 "cmp t f"
            Expect.equal (compare f t) -1 "cmp f t"
            Expect.isLessThan f t "lessThan f t"

        testCase "pair comparisons" <| fun () ->
            let t = right unit
            let f = left unit
            Expect.equal (compare (pair t f) (pair t t)) -1 "cmp ptf ptt"
            Expect.equal (compare (pair t t) (pair t f)) 1 "cmp ptt ptf"
            Expect.equal (compare unit (pair unit unit)) -1 "cmp pu puu"
            Expect.equal (compare t (pair unit unit)) 1 "cmp t puu"
            Expect.equal (compare f (pair unit unit)) 1 "cmp f puu"

        testCase "fixed-width number comparisons" <| fun () ->
            let l = List.map uint8 (randomList 20 30)
            for x in l do
                for y in l do
                    Expect.equal (compare (u8 x) (u8 y)) (compare x y) "compare numbers"
                    Expect.equal ((u8 x) = (u8 y)) (x = y) "eq numbers"

        testCase "number round trip" <| fun () ->
            let l = randomList 200 300
            for x in l do
                match u8 (uint8 x) with
                | U8 n -> Expect.equal n (uint8 x) "round trip U8"
                | _ -> failtest "failed to round-trip U8"


        testCase "pair of numbers" <| fun () ->
            let a = pair (nat 99UL) (nat 999UL)
            match vfst a with
            | Nat 99UL -> ()
            | _ -> failtest "incorrect fst"
            match vsnd a with
            | Nat 999UL -> ()
            | _ -> failtest "incorrect snd"

        testCase "deep match pair" <| fun () -> 
            let a = pair (nat 1234UL) (nat 2345UL)
            let b = pair (nat 4567UL) (nat 5678UL)
            match (pair a b) with
            | P (P (Nat w, Nat x), P (Nat y, Nat z)) ->
                Expect.equal w 1234UL "match pair ll"
                Expect.equal x 2345UL "match pair lr"
                Expect.equal y 4567UL "match pair rl"
                Expect.equal z 5678UL "match pair rr"
            | _ -> failtest "failed to match"

        testCase "matchLR" <| fun () ->
            match (left (u8 27uy)) with
            | L (U8 x) -> Expect.equal x 27uy "match left"
            | _ -> failtest "failed to match left"

            match (right (u8 42uy)) with
            | R (U8 x) -> Expect.equal x 42uy "match right"
            | _ -> failtest "failed to match right"

        testCase "ftlist round-trip" <| fun () ->
            let l = randomList 2000 3000 |> List.map uint64
            let v = ofFTList (FTList.map nat (FTList.ofList l))
            let toNat v = 
                match v with
                | Nat n -> n
                | _ -> failwithf "%s is not a nat" (prettyPrint v)
            let l' = v |> toFTList |> FTList.toList |> List.map toNat
            Expect.equal l' l "round trip"

        testCase "binary round trip" <| fun () ->
            let l = randomList 2000 3000 |> List.map uint8 
            let l' = ofBinary (Array.ofList l) |> toBinary |> List.ofArray
            Expect.equal l' l "round trip via binary"
            
        testCase "record lookup" <| fun () ->
            // singleton lookup
            match record_lookup (Bits.ofList [false; true]) (left (right (u8 27uy))) with
            | Some (U8 27uy) -> ()
            | x -> failtest (sprintf "singleton record lookup -> %A" x)

            match record_lookup (Bits.ofList [false; true]) (left (left (u8 27uy))) with
            | None -> ()
            | x -> failtest "singleton lookup should fail"


            // multi-value lookup
            let r = pair (pair (u8 27uy) (u8 42uy)) (pair (u8 108uy) (u8 22uy))
            match record_lookup (Bits.ofList [false; false]) r with
            | Some (U8 27uy) -> ()
            | x -> failtest (sprintf "saturated lookup 00 -> %A" x)
            match record_lookup (Bits.ofList [false; true]) r with
            | Some (U8 42uy) -> ()
            | x -> failtest (sprintf "saturated lookup 01 -> %A" x)
            match record_lookup (Bits.ofList [true; false]) r with
            | Some (U8 108uy) -> ()
            | x -> failtest (sprintf "saturated lookup 10 -> %A" x)
            match record_lookup (Bits.ofList [true; true]) r with
            | Some (U8 22uy) -> ()
            | x -> failtest (sprintf "saturated lookup 11 -> %A" x)
        
        testCase "record delete" <| fun () ->
            let r = left (right (pair unit unit))
            Expect.equal r (record_delete (Bits.ofList [true]) r) "delete non-present field"
            Expect.isTrue (isUnit (record_delete (Bits.empty) r)) "delete empty path is unit"
            Expect.isTrue (isUnit (record_delete (Bits.ofList [false]) r)) "delete l is unit"
            Expect.isTrue (isUnit (record_delete (Bits.ofList [false; true]) r)) "delete lr is unit"
            Expect.equal (left (right (right unit))) (record_delete (Bits.ofList [false; true; false]) r) "delete lrl is lrr"
            Expect.equal (left (right (left unit))) (record_delete (Bits.ofList [false; true; true]) r) "delete lrr is lrl"
            Expect.equal (left (right (right unit))) (record_delete (Bits.ofList [false; true; false; true]) r) "delete lrlr is lrr"

        testCase "record insert" <| fun () ->
            let checkElem k m r =
                match (record_lookup (Bits.ofByte k) r), (Map.tryFind k m) with
                | Some (Nat n), Some n' -> Expect.equal n n' "equal lookup"
                | None, None -> ()
                | _ -> failtest "mismatch"
            let mutable m = Map.empty
            let mutable r = unit
            for x in 1UL .. 4000UL do
                let k = uint8 (rng.Next())
                checkElem k m r
                m <- Map.add k x m
                r <- record_insert (Bits.ofByte k) (nat x) r
                checkElem k m r

        testCase "record insert delete" <| fun () ->
            let checkElem k m r =
                match (record_lookup (Bits.ofByte k) r), (Map.tryFind k m) with
                | Some (Nat n), Some n' -> Expect.equal n n' "equal lookup"
                | None, None -> ()
                | _ -> failtest "mismatch"
            let mutable m = Map.empty
            let mutable r = unit
            for x in 1UL .. 3000UL do
                // add a few items.
                let addCt = randomRange 5 10
                for _ in 1 .. addCt do
                    let k = uint8 (rng.Next())
                    checkElem k m r
                    m <- Map.add k x m
                    r <- record_insert (Bits.ofByte k) (nat x) r
                    checkElem k m r
                // remove a few items.
                let delCt = randomRange 10 20
                for _ in 1 .. delCt do
                    let k = uint8 (rng.Next())
                    checkElem k m r
                    m <- Map.remove k m
                    r <- record_delete (Bits.ofByte k) r
                    checkElem k m r

        testCase "asRecord" <| fun () ->
            let v = asRecord ["fe"; "fi"; "fo"; "fum"] (List.map u8 [1uy .. 4uy])
            match v with
            | Record ["fe"; "fi"; "fo"; "foo"] 
               ([Some (U8 1uy); Some (U8 2uy); Some (U8 3uy); None]
               ,Variant "fum" (U8 4uy)) -> ()
            | _ -> failwith "record match failed"
            Expect.isTrue (isRecord v) "v is a labeled record"


        testCase "recordSeq" <| fun () ->
            for _ in 1 .. 100 do
                let mutable m = Map.empty // ground truth rep
                let mutable v = unit // tested representation
                for ix in 1uy .. 100uy do
                    let k = randomSym (randomRange 1 4)
                    m <- Map.add k ix m
                    v <- record_insert (label k) (u8 ix) v
                let convert kvp = 
                    match kvp with
                    | (k, U8 ix) -> (k,ix)
                    | _ -> invalidArg (nameof kvp) "not a valid kvp"
                //printf "kvps=%A\n" m
                Expect.equal (v |> recordSeq |> Seq.map convert |> List.ofSeq) (Map.toList m) "equal maps"

        testCase "eq binaries" <| fun () ->
            for _ in 1 .. 100 do
                let a = randomBytes 6
                let b = randomBytes 7
                Expect.notEqual a b "bytes of different length a b"
                Expect.equal a a "equal a a"

        testCase "string round trip" <| fun () ->
            for _ in 1 .. 100 do
                let s = randomSym (randomRange 1 100)
                let v = ofString s
                let s' = toString v
                Expect.equal s s' "equal strings"
 
        testCase "toKey" <| fun () ->
            Expect.equal (toKey (pair unit (left unit))) (Bits.ofByte 0xC4uy) "pulu"
            Expect.equal (toKey (left (left (right unit)))) (Bits.ofByte 0x58uy) "llru"
            Expect.equal (toKey (left (right (left unit)))) (Bits.ofByte 0x64uy) "lrlu"
            Expect.equal (toKey (right (pair unit unit))) (Bits.ofByte 0xB0uy) "rpuu"
            Expect.equal (toKey (right (left (pair unit unit)))) (Bits.ofNat64 0x270UL) "rlpuu"
        
        testCase "ofKey" <| fun () ->
            Expect.equal (pair unit (left unit)) (ofKey (Bits.ofByte 0xC4uy)) "pulu"
            Expect.equal (left (left (right unit))) (ofKey (Bits.ofByte 0x58uy)) "llru"
            Expect.equal (left (right (left unit))) (ofKey (Bits.ofByte 0x64uy)) "lrlu"
            Expect.equal (right (pair unit unit)) (ofKey (Bits.ofByte 0xB0uy)) "rpuu"
            Expect.equal (right (left (pair unit unit))) (ofKey (Bits.ofNat64 0x270UL)) "rlpuu"

        // testing the pretty printer...
        testCase "escaploosion - printing strings" <| fun () ->
            let s0 = "\n\"Hello, world!\"\a\t\v\r\x1b\x7f"
            let v = ofString s0 
            let spp = prettyPrint v
            Expect.equal "\"\\n\\\"Hello, world!\\\"\\a\\t\\v\\r\\x1B\\x7F\"" spp "pretty printing ugly strings"

        testCase "printing numbers" <| fun () ->
            Expect.equal "23" (prettyPrint (nat 23UL)) "print numbers"
            Expect.equal "42" (prettyPrint (nat 42UL)) "printing more nats"

        testCase "printing basic pairs and sums data" <| fun () ->
            Expect.equal "(() . 0b0)" (prettyPrint (pair unit (left unit))) "pulu"
            Expect.equal "R[()]" (prettyPrint (right (pair unit unit))) "rpuu"
            Expect.equal "RL(() . 1)" (prettyPrint (right (left (pair unit (right unit))))) "rlpuru"

        testCase "printing variants" <| fun () ->
            Expect.equal "foo" (prettyPrint (symbol "foo")) "foo"
            Expect.equal "text:msg:\"hello, world!\"" (prettyPrint (variant "text" (variant "msg" (ofString "hello, world!")))) "text"

        testCase "printing lists" <| fun () -> 
            let l = [1;2;127;128;254;255] |> List.map (uint64 >> Value.nat) |> FTList.ofList |> Value.ofFTList
            Expect.equal "[1, 2, 127, 128, 254, 255]" (prettyPrint l) "list of numbers"
            Expect.equal "\"Ok\"" (prettyPrint (ofBinary [|byte 'O'; byte 'k'|])) "looks like a string"

        testCase "printing records" <| fun () ->
            let v = asRecord ["foo"; "bar"; "baz"; "qux"; "gort"] (List.map (uint64 >> Value.nat) [1 .. 5])
            let ppv = "(bar:2, baz:3, foo:1, gort:5, qux:4)"
            Expect.equal ppv (prettyPrint v) "record"

    ]


