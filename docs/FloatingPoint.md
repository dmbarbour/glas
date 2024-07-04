# Floating Point

This document describes a tweak to [posits](https://en.wikipedia.org/wiki/Unum_(number_format)#Posit_(Type_III_Unum)) for arbitrary-width bitstrings. 

I'm unlikely to actually use this directly in glas systems. I probably won't even use it indirectly - there are better options for accelerated representations. However, I spent a few days thinking this up, and I'd like to keep it around a while longer.

## Encoding

Every bitstring encodes a unique rational number. Every rational number whose denominator is a power of two can be precisely represented.

The empty bitstring encodes zero. To interpret any non-empty bitstring, we'll first add logically a `1000...` suffix to a non-empty bitstring (that is a 1 bit followed by infinite 0 bits) then interpret the result as `(sign)(regime)(exponent)(fraction)`. 

Adding 0 bits to a fraction doesn't affect the result, so we don't actually need infinite bits. Just add enough zeroes to reach the fraction.

The regime is encoded as a sequence of '1' bits followed by a '0', or a sequence of '0' bits followed by a '1'. This determines whether the exponent is positive or negative. Unlike conventional posits, regime also determines exponent size (es) in a simple way. 

        regime  es      exponent
        10      2       0..3  
        110     2       4..7
        1110    3       8..15
        11110   4       16..31
        111110  5       32..63
        (1*N)0  N       (2^N)..(2^(N+1)-1)

        01      2       -4..-1
        001     2       -8..-5
        0001    3       -16..-9
        00001   4       -32..-17
        000001  5       -64..-33
        (0*N)1  N       -(2^(N+1))..-((2^N)+1)

Compared to regular posits, where regime is essentially a unary addition to exponent, this supports compact encoding for much larger exponents. However, it costs one bit for a few intermediate exponents. 

The final number is computed as `(-1)^(sign) * 2^(exponent) * (1.(binary fraction))`, same as normal posits. There is no encoding for not-a-real.

## Limitations

This encoding is exact for addition, subtraction, and multiplication. But division, square roots, etc. would need some additional parameters indicating how much precision to maintain in the result. Further, even encoding decimal `0.3` would require a precision parameter. This is a problem fixed width posits and floating point representations do not have.

There is no encoding for not-a-number. This isn't a problem for the intended context, glas systems, because we can simply fail operations and backtrack. But it might require some form of signaling failure in other contexts.

Obviously, there is no hardware support for this encoding. Why bother with floating point when there's no hardware support? Might as well use a bignum representation.
