
module Glas.TestVal

open Expecto
open RandVal
open Glas.Value

let ofBitList l = List.foldBack consStemBit l unit


[<Tests>]
let tests = 
    testList "Glas values" [
        testCase "unit is unit" <| fun () ->
            Expect.isTrue (isUnit unit) "isUnit unit"
        
        testCase "unit equality" <| fun () ->
            Expect.equal unit unit "unit eq unit"
            Expect.notEqual unit (left unit) "unit not-eq false"
            Expect.notEqual unit (right unit) "unit not-eq true"
            Expect.notEqual unit (pair unit unit) "unit not-eq pair"

        testCase "stem equality" <| fun () ->
            let t = right unit
            let f = left unit
            Expect.equal t t "eq t t"
            Expect.equal f f "eq f f"
            Expect.notEqual t f "eq t f"
            Expect.notEqual f t "eq f t"

        testCase "pair equality" <| fun () ->
            let t = right unit
            let f = left unit
            Expect.equal (pair t t) (pair t t) "eq pair 1"
            Expect.equal (pair f t) (pair f t) "eq pair 2"
            Expect.equal (pair t f) (pair t f) "eq pair 3"
            Expect.equal (pair f f) (pair f f) "eq pair 4"
            Expect.notEqual (pair t t) (pair t f) "neq pair 1"
            Expect.notEqual (pair t t) (pair f t) "neq pair 2"
            Expect.notEqual (pair t t) (pair f f) "neq pair 3"

        testCase "integer round trip" <| fun () ->
            let testVal n =
                match ofInt n with
                | Int64(n') ->
                    Expect.equal n n' "equal round trop int through value"
                | _ -> failtestf "int round trip failed for %A" n
            let lForceTests = [System.Int64.MinValue; -4L; -3L; -2L; -1L; 
                               0L; 1L; 2L; 3L; 4L; System.Int64.MaxValue]
            for n in lForceTests do
                testVal n
            for _ in 1 .. 1000 do
                testVal (mkRandomInt ())

        testCase "deep match pair" <| fun () -> 
            let a = pair (ofNat 1234UL) (ofNat 2345UL)
            let b = pair (ofNat 4567UL) (ofNat 5678UL)
            match (pair a b) with
            | P (P (Nat64 w, Nat64 x), P (Nat64 y, Nat64 z)) ->
                Expect.equal w 1234UL "match pair ll"
                Expect.equal x 2345UL "match pair lr"
                Expect.equal y 4567UL "match pair rl"
                Expect.equal z 5678UL "match pair rr"
            | _ -> failtest "failed to match"

        testCase "record lookup" <| fun () ->
            // singleton lookup
            match record_lookup (ofBitList [false; true]) (left (right (ofByte 27uy))) with
            | ValueSome (Byte 27uy) -> ()
            | x -> failtest (sprintf "singleton record lookup -> %A" x)

            match record_lookup (ofBitList [false; true]) (left (left (ofByte 27uy))) with
            | ValueNone -> ()
            | x -> failtest "singleton lookup should fail"


            // multi-value lookup
            let r = pair (pair (ofByte 27uy) (ofByte 42uy)) (pair (ofByte 108uy) (ofByte 22uy))
            match record_lookup (ofBitList [false; false]) r with
            | ValueSome (Byte 27uy) -> ()
            | x -> failtest (sprintf "saturated lookup 00 -> %A" x)
            match record_lookup (ofBitList [false; true]) r with
            | ValueSome (Byte 42uy) -> ()
            | x -> failtest (sprintf "saturated lookup 01 -> %A" x)
            match record_lookup (ofBitList [true; false]) r with
            | ValueSome (Byte 108uy) -> ()
            | x -> failtest (sprintf "saturated lookup 10 -> %A" x)
            match record_lookup (ofBitList [true; true]) r with
            | ValueSome (Byte 22uy) -> ()
            | x -> failtest (sprintf "saturated lookup 11 -> %A" x)
        
        testCase "record delete" <| fun () ->
            let r = left (right (pair unit unit))
            Expect.equal r (record_delete (ofBitList [true]) r) "delete non-present field"
            Expect.isTrue (isUnit (record_delete (ofBitList []) r)) "delete empty path is unit"
            Expect.isTrue (isUnit (record_delete (ofBitList [false]) r)) "delete l is unit"
            Expect.isTrue (isUnit (record_delete (ofBitList [false; true]) r)) "delete lr is unit"
            Expect.equal (left (right (right unit))) (record_delete (ofBitList [false; true; false]) r) "delete lrl is lrr"
            Expect.equal (left (right (left unit))) (record_delete (ofBitList [false; true; true]) r) "delete lrr is lrl"
            Expect.equal (left (right (right unit))) (record_delete (ofBitList [false; true; false; true]) r) "delete lrlr is lrr"

        testCase "record insert" <| fun () ->
            let checkElem k m r =
                match (record_lookup (ofByte k) r), (Map.tryFind k m) with
                | ValueSome (Nat64 n), Some n' -> Expect.equal n n' "equal lookup"
                | ValueNone, None -> ()
                | _ -> failtest "mismatch"
            let mutable m = Map.empty
            let mutable r = unit
            for x in 1UL .. 4000UL do
                let k = uint8 (rng.Next())
                checkElem k m r
                m <- Map.add k x m
                r <- record_insert (ofByte k) (ofNat x) r
                checkElem k m r

        testCase "record insert delete" <| fun () ->
            let checkElem k m r =
                match (record_lookup (ofByte k) r), (Map.tryFind k m) with
                | ValueSome (Nat64 n), Some n' -> Expect.equal n n' "equal lookup"
                | ValueNone, None -> ()
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
                    r <- record_insert (ofByte k) (ofNat x) r
                    checkElem k m r
                // remove a few items.
                let delCt = randomRange 10 20
                for _ in 1 .. delCt do
                    let k = uint8 (rng.Next())
                    checkElem k m r
                    m <- Map.remove k m
                    r <- record_delete (ofByte k) r
                    checkElem k m r

        testCase "asRecord" <| fun () ->
            let v = asRecord ["fe"; "fi"; "fo"; "fum"] (List.map ofByte [1uy .. 4uy])
            match v with
            | Record ["fe"; "fi"; "fo"; "foo"] 
               ([ValueSome (Byte 1uy); ValueSome (Byte 2uy); ValueSome (Byte 3uy); ValueNone]
               ,Variant "fum" (Byte 4uy)) -> ()
            | _ -> failwith "record match failed"
            Expect.isTrue (isRecord v) "v is a labeled record"


        testCase "recordSeq" <| fun () ->
            for _ in 1 .. 100 do
                let mutable m = Map.empty // ground truth rep
                let mutable v = unit // tested representation
                for ix in 1uy .. 100uy do
                    let k = randomLabel ()
                    m <- Map.add k ix m
                    v <- record_insert (label k) (ofByte ix) v
                let convert kvp = 
                    match kvp with
                    | (k, Byte ix) -> (k,ix)
                    | _ -> invalidArg (nameof kvp) "not a valid kvp"
                //printf "kvps=%A\n" m
                Expect.equal (v |> recordSeq |> Seq.map convert |> List.ofSeq) (Map.toList m) "equal maps"

        testCase "string round trip" <| fun () ->
            for _ in 1 .. 100 do
                let s = randomLabel ()
                let v = ofString s
                let s' = toString v
                Expect.equal s s' "equal strings"

        // testing the pretty printer...
        testCase "escaploosion - printing strings" <| fun () ->
            let s0 = "\n\"Hello, world!\"\a\t\v\r\x1b\x7f"
            let v = ofString s0 
            let spp = prettyPrint v
            Expect.equal "\"\\n\\\"Hello, world!\\\"\\a\\t\\v\\r\\x1B\\x7F\"" spp "pretty printing ugly strings"

        testCase "printing numbers" <| fun () ->
            Expect.equal "23" (prettyPrint (ofNat 23UL)) "print numbers"
            Expect.equal "42" (prettyPrint (ofNat 42UL)) "printing more nats"
            Expect.equal "-108" (prettyPrint (ofInt -108L)) "printing integers"

        testCase "printing basic pairs and sums data" <| fun () ->
            Expect.equal "((), -1)" (prettyPrint (pair unit (left unit))) "pulu"
            Expect.equal "~1[()]" (prettyPrint (right (pair unit unit))) "rpuu"
            Expect.equal "~10((), 1)" (prettyPrint (right (left (pair unit (right unit))))) "rlpuru"

        testCase "printing variants" <| fun () ->
            Expect.equal "foo" (prettyPrint (symbol "foo")) "foo"
            Expect.equal "text:msg:\"hello, world!\"" (prettyPrint (variant "text" (variant "msg" (ofString "hello, world!")))) "text"

        testCase "printing lists" <| fun () -> 
            let l = [1;2;127;128;254;255] |> List.map (uint64 >> Value.ofNat) |> Value.Rope.ofSeq |> Value.ofTerm
            Expect.equal "[1, 2, 127, 128, 254, 255]" (prettyPrint l) "list of numbers"
            Expect.equal "\"Ok\"" (prettyPrint (ofBinary [|byte 'O'; byte 'k'|])) "looks like a string"

        testCase "printing records" <| fun () ->
            let v = asRecord ["foo"; "bar"; "baz"; "qux"; "gort"] (List.map (uint64 >> Value.ofNat) [1 .. 5])
            let ppv = "(bar:2, baz:3, foo:1, gort:5, qux:4)"
            Expect.equal ppv (prettyPrint v) "record"
    ]

