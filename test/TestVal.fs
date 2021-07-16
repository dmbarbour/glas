
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

        testCase "number comparisons" <| fun () ->
            let l = List.map uint32 (randomList 20 30)
            for x in l do
                for y in l do
                    Expect.equal (compare (u32 x) (u32 y)) (compare x y) "compare numbers"
                    Expect.equal ((u32 x) = (u32 y)) (x = y) "eq numbers"

        testCase "number round trip" <| fun () ->
            let l = randomList 200 300
            for x in l do
                match u8 (uint8 x) with
                | U16 _ | U32 _ | U64 _ -> failtest "matched wrong type U8"
                | U8 n -> Expect.equal n (uint8 x) "round trip U8"
                | _ -> failtest "failed to round-trip U8"

                match u16 (uint16 x) with
                | U8 _ | U32 _ | U64 _ -> failtest "matched wrong type U16"
                | U16 n -> Expect.equal n (uint16 x) "round trip U16"
                | _ -> failtest "failed to round-trip U16"

                match u32 (uint32 x) with
                | U8 _ | U16 _ | U64 _ -> failtest "matched wrong type U32"
                | U32 n -> Expect.equal n (uint32 x) "round trip U32"
                | _ -> failtest "failed to round-trip U32"

                match u64 (uint64 x) with
                | U8 _ | U16 _ | U32 _ -> failtest "matched wrong type U64"
                | U64 n -> Expect.equal n (uint64 x) "round trip U64"
                | _ -> failtest "failed to round-trip U64"



        testCase "pair of numbers" <| fun () ->
            let a = pair (u32 99ul) (u64 999UL)
            match fst a with
            | U32 99ul -> ()
            | _ -> failtest "incorrect fst"
            match snd a with
            | U64 999UL -> ()
            | _ -> failtest "incorrect snd"

        testCase "deep match pair" <| fun () -> 
            let a = pair (u8 111uy) (u16 2222us)
            let b = pair (u32 33333ul) (u64 444444UL)
            match (pair a b) with
            | P (P (U8 w, U16 x), P (U32 y, U64 z)) ->
                Expect.equal w 111uy "match U8"
                Expect.equal x 2222us "match U16"
                Expect.equal y 33333ul "match U32"
                Expect.equal z 444444UL "match U64"
            | _ -> failtest "failed to match"

        testCase "matchLR" <| fun () ->
            match (left (u8 27uy)) with
            | L (U8 x) -> Expect.equal x 27uy "match left"
            | _ -> failtest "failed to match left"

            match (right (u16 22222us)) with
            | R (U16 x) -> Expect.equal x 22222us "match right"
            | _ -> failtest "failed to match right"

        testCase "list round-trip" <| fun () ->
            let l = randomList 2000 3000 |> List.map uint32
            let v = ofFTList (FTList.map u32 (FTList.ofList l))
            let toU32 x = 
                match x with
                | (U32 n) -> n
                | _ -> invalidArg (nameof x) "not a U32"
            let l' = v |> toFTList |> FTList.toList |>  List.map toU32
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
            let r = pair (pair (u8 27uy) (u16 333us)) (pair (u32 1234ul) (u64 12345UL))
            match record_lookup (Bits.ofList [false; false]) r with
            | Some (U8 27uy) -> ()
            | x -> failtest (sprintf "saturated lookup 00 -> %A" x)
            match record_lookup (Bits.ofList [false; true]) r with
            | Some (U16 333us) -> ()
            | x -> failtest (sprintf "saturated lookup 01 -> %A" x)
            match record_lookup (Bits.ofList [true; false]) r with
            | Some (U32 1234ul) -> ()
            | x -> failtest (sprintf "saturated lookup 10 -> %A" x)
            match record_lookup (Bits.ofList [true; true]) r with
            | Some (U64 12345UL) -> ()
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
                | Some (U32 n), Some n' -> Expect.equal n n' "equal lookup"
                | None, None -> ()
                | _ -> failtest "mismatch"
            let mutable m = Map.empty
            let mutable r = unit
            for x in 1ul .. 4000ul do
                let k = uint8 (rng.Next())
                checkElem k m r
                m <- Map.add k x m
                r <- record_insert (Bits.ofByte k) (u32 x) r
                checkElem k m r

        testCase "record insert delete" <| fun () ->
            let checkElem k m r =
                match (record_lookup (Bits.ofByte k) r), (Map.tryFind k m) with
                | Some (U32 n), Some n' -> Expect.equal n n' "equal lookup"
                | None, None -> ()
                | _ -> failtest "mismatch"
            let mutable m = Map.empty
            let mutable r = unit
            for x in 1ul .. 3000ul do
                // add a few items.
                let addCt = randomRange 5 10
                for _ in 1 .. addCt do
                    let k = uint8 (rng.Next())
                    checkElem k m r
                    m <- Map.add k x m
                    r <- record_insert (Bits.ofByte k) (u32 x) r
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


        testCase "toKey" <| fun () ->
            Expect.equal (toKey (pair unit (left unit))) (Bits.ofByte 0xC4uy) "pulu"
            Expect.equal (toKey (left (left (right unit)))) (Bits.ofByte 0x58uy) "llru"
            Expect.equal (toKey (left (right (left unit)))) (Bits.ofByte 0x64uy) "lrlu"
            Expect.equal (toKey (right (pair unit unit))) (Bits.ofByte 0xB0uy) "rpuu"
            Expect.equal (toKey (right (left (pair unit unit)))) (Bits.ofNat64 0x270UL) "lrpuu"
        
        testCase "ofKey" <| fun () ->
            Expect.equal (pair unit (left unit)) (ofKey (Bits.ofByte 0xC4uy)) "pulu"
            Expect.equal (left (left (right unit))) (ofKey (Bits.ofByte 0x58uy)) "llru"
            Expect.equal (left (right (left unit))) (ofKey (Bits.ofByte 0x64uy)) "lrlu"
            Expect.equal (right (pair unit unit)) (ofKey (Bits.ofByte 0xB0uy)) "rpuu"
            Expect.equal (right (left (pair unit unit))) (ofKey (Bits.ofNat64 0x270UL)) "lrpuu"

    ]


