# Proposed Representations of Numbers

The proposed representation for variable-width natural numbers is a bitstring, msb to lsb, with no leading zeroes.

        0       (empty bitstring)
        1       1
        2       10
        3       11                          
        23      10111
        42      101010

This encoding has the advantage of being simple, conventional, easily explained and understood. It has several disadvantages, too.

## Integers

Natural numbers can be transparently extended with negative integers by simply inverting all the bits. This corresponds to a one's complement encoding with no leading ones. Conveniently, we cannot encode a negative zero, and this gives a complete bijection between bitstrings and integers.

        -1      0
        -2      01
        -3      00             
        -23     01000          
        -42     010101

Alternatively, we could negate only the msb. However, I believe the complement is easier to explain and understand. 

## Rational Numbers

A lightweight encoding for an exact rational number is perhaps an integer, natural pair where the natural has an implicit '1' prefix.

        (Integer * NZNatural)

However, I'm uncertain we'll use this much. Might favor floating point. Or just use `ratio:(n:Int, d:Nat)`. 

## Floating Point

We could directly encode floating point bitstrings. Whether IEEE floating point or something closer to Posits/Unums. 

OTOH, in practice we'll often want to accelerate vector and matrix operations with many floating point values. In this context, it might be better to encode vectors and matrices of floating point numbers directly into binaries. Some header information (such as matrix rank, or choice of numeric encoding) could be separated from the binary.

*Aside:* For probabilistic programming, I'm interested in developing a variant of floating point optimized for the range (0.0,1.0), with greatest, symmetric precision very near one and zero. But this is somewhat beyond the scope of Glas.

## Vectors and Matrices




