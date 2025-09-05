# Floating Point

This document describes a tweak to [posits](https://en.wikipedia.org/wiki/Unum_(number_format)#Posit_(Type_III_Unum)) for arbitrary-width bitstrings. 

In context of glas systems, this may prove convenient as an accelerated representation of rational numbers. But I'm uncertain whether it is more useful than competing representations.

## Encoding

Every bitstring encodes a unique rational number. Every rational number whose denominator is a power of two can be precisely represented.

The empty bitstring encodes zero. To interpret any non-empty bitstring, we'll first add logically an infinite `1000...` suffix then interpret the result as `(sign)(regime)(exponent)(fraction)`. Adding 0 bits to the fraction doesn't further influence the result, so in practice it is always sufficient to read a finite number of bits. The '1' bit at the head of the suffix guarantees every bitstring encodes a unique number.

The regime is encoded as a sequence of '1' bits followed by a '0', or a sequence of '0' bits followed by a '1'. This determines whether the exponent is positive or negative. The main tweak compared to conventional posits is that exponent size is proportional to regime, allowing for logarithmic-space encoding of large exponents, at the cost of being one or two bits larger for a subset of smaller exponents. Also, there is no encoding for infinity or not-a-real. 

        regime  es      exponent
        10      2       0..3                0 + es (0..3)
        110     2       4..7                4 + es (0..3)
        1110    3       8..15               8 + es (0..7)
        11110   4       16..31              16 + es (0..15)
        111110  5       32..63              32 + es (0..31)
        (1*N)0  N       (2^N)..(2^(N+1)-1)  2^N + es (0..(2^N-1))

        01      2       -4..-1              -4 + es (0..3)
        001     2       -8..-5              -8 + es (0..3)
        0001    3       -16..-9             -16 + es (0..7)
        00001   4       -32..-17
        000001  5       -64..-33
        (0*N)1  N       -(2^(N+1))..-((2^N)+1)

The final number is computed as `(-1)^(sign) * 2^(exponent) * (1.(binary fraction))`, same as normal posits. Although encoding of 0 is a special case, if we interpreted it under this equation it would be `(-1)*2^(-inf)`, i.e. a negative transfinite number infinitely close to zero because the negative regime would never terminate.

## Samples

        Number              Encoding
        0                   (empty bitstring)
        0.5                 00
        1                   0
        1.5                 010001
        2                   0100

## Limitations

This encoding is exact for addition, subtraction, and multiplication. But division, square roots, etc. would need some additional parameters indicating how much precision to maintain in the result. Further, even encoding decimal `0.3` would require a precision parameter. This is a problem fixed width posits and floating point representations do not have.

There is no encoding for not-a-number. Thus, some other form of error handling is needed for divide-by-zero.

Obviously, there is no hardware support for this encoding. Why bother with floating point when there's no hardware support? It's essentially a bignum representation.

