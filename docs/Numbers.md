# Proposed Representations of Numbers

## Naturals and Integers

Basic natural numbers and integers. Integers are one's complement of natural numbers. There is no negative zero, and values have a rather nice symmetry.

        42  101010
        23  10111
        4   100
        3   11
        2   10
        1   1
        0   (empty)
        -1  0
        -2  01
        -3  00
        -4  011
        -23 01000
        -42 010101

In some contexts, if we know we have a positive integer, we could erase the first '1' bit prefix then implicitly add it as needed. Saving a bit isn't very important, but it would resist representing invalid numbers.

## Rational Numbers

A compact encoding for an exact rational number:

        NZNatural - positive integer but drop the first '1' bit.
        (Integer * NZNatural)

In some contexts, we might favor something closer to 'ratio:(n:Int, d:Nat)'

## Floating Point

We could directly encode floating point bitstrings. Whether IEEE floating point or something closer to Posits/Unums. 

OTOH, in practice we'll often want to accelerate vector and matrix operations with many floating point values. In this context, it might be better to encode vectors and matrices of floating point numbers directly into binaries. Some header information (such as matrix rank, or choice of numeric encoding) could be separated from the binary.

*Aside:* For probabilistic programming, I'm interested in developing a variant of floating point optimized for the range (0.0,1.0), with greatest, symmetric precision very near one and zero. But this is somewhat beyond the scope of Glas.

## Vectors and Matrices

Might be more convenient to work directly with binary data, perhaps add some metadata headers about type (float32, posit32, etc.) and layout (row-major, column-major, z-order, etc..). 

