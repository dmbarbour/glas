
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
            Expect.isTrue (eq unit unit) "unit eq unit"
            Expect.isFalse (eq unit (left unit)) "unit not-eq false"
            Expect.isFalse (eq unit (right unit)) "unit not-eq true"
            Expect.equal (cmp unit unit) 0 "compare unit"
            Expect.equal (cmp unit (left unit)) -1 "compare unit false"
            Expect.equal (cmp unit (right unit)) -1 "compare unit true"

        testCase "bool comparisons" <| fun () ->
            let t = right unit
            let f = left unit
            Expect.isTrue (eq t t) "eq t t"
            Expect.isTrue (eq f f) "eq f f"
            Expect.isFalse (eq t f) "eq t f"
            Expect.isFalse (eq f t) "eq f t"
            Expect.equal (cmp t t) 0 "cmp t t"
            Expect.equal (cmp f f) 0 "cmp f f"
            Expect.equal (cmp t f) 1 "cmp t f"
            Expect.equal (cmp f t) -1 "cmp f t"

        testCase "number comparisons" <| fun () ->
            let l = List.map uint32 (randomList 20 30)
            for x in l do
                for y in l do
                    Expect.equal (cmp (u32 x) (u32 y)) (compare x y) "compare numbers"
                    Expect.equal (eq (u32 x) (u32 y)) (x = y) "eq numbers"

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
            | Pair (Pair (U8 w, U16 x), Pair (U32 y, U64 z)) ->
                Expect.equal w 111uy "match U8"
                Expect.equal x 2222us "match U16"
                Expect.equal y 33333ul "match U32"
                Expect.equal z 444444UL "match U64"
            | _ -> failtest "failed to match"

        testCase "matchLR" <| fun () ->
            match (left (u8 27uy)) with
            | Left (U8 x) -> Expect.equal x 27uy "match left"
            | _ -> failtest "failed to match left"

            match (right (u16 22222us)) with
            | Right (U16 x) -> Expect.equal x 22222us "match right"
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
    ]


