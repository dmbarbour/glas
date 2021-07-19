module Glas.TestArith

open Expecto
open Glas

let rng = System.Random()

let randomRange lb ub =
    lb + (rng.Next() % (1 + ub - lb))

let randomBits len =
    let arr = Array.zeroCreate len
    for ix in 1 .. arr.Length do
        arr.[ix - 1] <- (0 <> (rng.Next() &&& 1))
    Bits.ofArray arr



[<Tests>]
let test_arith =
    testList "program arithmetic" [

        testCase "bitstring addition" <| fun () ->
            for x in 1 .. 1000 do
                let w1 = randomRange 0 100
                let w2 = randomRange 0 100
                let n1 = randomBits w1
                let n2 = randomBits w2
                let struct(sum, carry) = Arithmetic.add n1 n2
                let struct(sum2, carry2) = Arithmetic.add n2 n1
                //printf "%A + %A => sum %A carry %A\n" (Bits.toI n1) (Bits.toI n2) (Bits.toI sum) (Bits.toI carry)
                Expect.equal w1 (Bits.length sum) "preserve field size (sum)"
                Expect.equal w2 (Bits.length carry) "preserve field size (carry)"
                Expect.equal w2 (Bits.length sum2) "preserve field size (sum2)"
                Expect.equal w1 (Bits.length carry2) "preserve field size (carry2)"
                Expect.equal (Bits.toI (Bits.append carry sum)) (Bits.toI n1 + Bits.toI n2) "equal sum"
                Expect.equal (Bits.append carry sum) (Bits.append carry2 sum2) "equal joins"

        testCase "bitstring multiplication" <| fun () ->
            for x in 1 .. 1000 do
                let w1 = randomRange 0 100
                let w2 = randomRange 0 100
                let n1 = randomBits w1
                let n2 = randomBits w2
                let struct(prod, overflow) = Arithmetic.mul n1 n2
                let struct(prod2, overflow2) = Arithmetic.mul n2 n1
                //printf "%A * %A => prod %A overflow %A\n" (Bits.toI n1) (Bits.toI n2) (Bits.toI prod) (Bits.toI overflow)
                Expect.equal w1 (Bits.length prod) "preserve field size (prod)"
                Expect.equal w2 (Bits.length overflow) "preserve field size (overflow)"
                Expect.equal w2 (Bits.length prod2) "preserves field size (prod2)"
                Expect.equal w1 (Bits.length overflow2) "Preserves field size (overflow2)"
                Expect.equal (Bits.toI (Bits.append overflow prod)) (Bits.toI n1 * Bits.toI n2) "equal product"
                Expect.equal (Bits.append overflow prod) (Bits.append overflow2 prod2) "equal joins"

        testCase "bitstring subtraction" <| fun () ->
            for x in 1 .. 1000 do
                let w1 = randomRange 0 100
                let w2 = randomRange 0 100
                let n1 = randomBits w1
                let n2 = randomBits w2
                let i1 = Bits.toI n1
                let i2 = Bits.toI n2

                match Arithmetic.sub n1 n2 with
                | Some ndiff -> 
                    Expect.equal w1 (Bits.length ndiff) "preserve field size (diff)"
                    Expect.equal (i1 - i2) (Bits.toI ndiff) "equal diff"
                | None -> 
                    Expect.isLessThan i1 i2 "negative diff"

                match Arithmetic.sub n2 n1 with
                | Some ndiff ->
                    Expect.equal w2 (Bits.length ndiff) "preserve field size (diff)"
                    Expect.equal (i2 - i1) (Bits.toI ndiff) "equal diff"
                | None ->
                    Expect.isLessThan i2 i1 "negative diff"

        testCase "bitstring division" <| fun () ->
            for x in 1 .. 1000 do 
                let w1 = randomRange 0 100
                let w2 = randomRange 0 100
                let n1 = randomBits w1
                let n2 = randomBits w2
                let i1 = Bits.toI n1
                let i2 = Bits.toI n2
                
                match Arithmetic.div n1 n2 with
                | Some struct(q,r) ->
                    Expect.equal w1 (Bits.length q) "preserve field size (quotient)"
                    Expect.equal w2 (Bits.length r) "preserve field size (remainder)"
                    let mutable iRem = 0I
                    let iQuot = bigint.DivRem(i1,i2,&iRem)
                    Expect.equal (Bits.toI q) iQuot "equal quotient"
                    Expect.equal (Bits.toI r) iRem "equal remainder"
                | None ->
                    Expect.equal i2 0I "zero divisor" 

                match Arithmetic.div n2 n1 with
                | Some struct(q,r) ->
                    Expect.equal w2 (Bits.length q) "preserve field size (quotient)"
                    Expect.equal w1 (Bits.length r) "preserve field size (remainder)"
                    let mutable iRem = 0I
                    let iQuot = bigint.DivRem(i2,i1,&iRem)
                    Expect.equal (Bits.toI q) iQuot "equal quotient"
                    Expect.equal (Bits.toI r) iRem "equal remainder"
                | None ->
                    Expect.equal i1 0I "zero divisor" 

    ]
