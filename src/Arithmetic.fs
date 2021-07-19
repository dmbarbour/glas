namespace Glas

/// Utilities to support bitstring arithmetic per the Glas program definition.
module Arithmetic =

    let private splitWidths w1 w2 b = 
        let wb = Bits.length b
        assert ((w1 + w2) >= wb)
        if (w1 >= wb) then
            let bw1 = Bits.addZeroesPrefix (w1 - wb) b
            let bw2 = Bits.addZeroesPrefix w2 (Bits.empty)
            struct(bw1, bw2)
        else
            let wc = wb - w1
            let bw1 = Bits.skip wc b
            let bw2 = Bits.addZeroesPrefix (w2 - wc) (Bits.take wc b)
            struct(bw1, bw2)

    /// N1 N2 -- SUM CARRY
    /// SUM preserves bit-width of N1
    /// CARRY preserves bit-width of N2
    let add (n1:Bits) (n2:Bits) : struct(Bits * Bits) =
        let carrySum = Bits.ofI (Bits.toI n1 + Bits.toI n2)
        splitWidths (Bits.length n1) (Bits.length n2) carrySum

    /// N1 N2 -- PROD OVERFLOW
    /// PROD preserves bit-width of N1
    /// OVERFLOW preserves bit-width of N2
    let mul (n1:Bits) (n2:Bits) : struct(Bits * Bits) =
        let overflowProd = Bits.ofI (Bits.toI n1 * Bits.toI n2)
        splitWidths (Bits.length n1) (Bits.length n2) overflowProd

    /// N1 N2 -- DIFF | FAILURE.  
    /// DIFF is (N1 - N2), preserving bit-width of N1.
    /// FAILURE in case DIFF would be negative.
    let sub (n1:Bits) (n2:Bits) : Bits option =
        let iDiff = Bits.toI n1 - Bits.toI n2
        if (iDiff.Sign < 0) then None else
        let diff = Bits.ofI iDiff
        let wDiff = Bits.length diff
        let wN1 = Bits.length n1
        assert (wN1 >= wDiff)
        Some (Bits.addZeroesPrefix (wN1 - wDiff) diff)

    /// DIVIDEND DIVISOR -- QUOTIENT REMAINDER | FAILURE
    /// QUOTIENT preserves bit-width of DIVIDEND
    /// REMAINDER preserves bit-width of DIVISOR
    /// FAILURE in case of zero divisor
    let div (dividend:Bits) (divisor:Bits) : struct(Bits * Bits) option =
        let iDivisor = Bits.toI divisor
        if(0 = iDivisor.Sign) then None else
        let iDividend = Bits.toI dividend
        let mutable iRemainder = Unchecked.defaultof<bigint> // assigned byref
        let iQuotient = bigint.DivRem(iDividend, iDivisor, &iRemainder)
        let quotient = Bits.ofI iQuotient
        let remainder = Bits.ofI iRemainder
        let wDividend = Bits.length dividend
        let wDivisor = Bits.length divisor
        let wQuotient = Bits.length quotient 
        let wRemainder = Bits.length remainder
        assert((wDividend >= wQuotient) && (wDivisor >= wRemainder))
        let quotient' = Bits.addZeroesPrefix (wDividend - wQuotient) quotient
        let remainder' = Bits.addZeroesPrefix (wDivisor - wRemainder) remainder
        Some struct(quotient', remainder')
