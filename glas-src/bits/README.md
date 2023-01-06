# Bitstring Manipulations

A bitstring in glas is a binary tree where every node has exactly one or zero children. Each edge is labeled 0 or 1, respectively representing the left or right child. The bitstring is encoded into the path through these edges.

            /       0       Tree represents bitstring
            \       1           0b011010001
             \      1
             /      0
             \      1
             /      0
            /       0
           /        0
           \        1

Bitstrings are useful for representing integers, symbols, and other simple data. However, large bitstrings are discouraged. For text or binary data, glas systems favor a list of bytes (8-bit bitstrings). 

This module defines several useful operations on bitstrings. 

## Compact Encoding Assumption

I assume glas runtimes efficiently represent bitstrings and radix trees, storing multiple bits per allocation. One convenient mechanism is to encode bitstring length using the lowest '1' bit within a word:

        10000..0    0 bits
        a1000..0    1 bits
        ab100..0    2 bits
        abc10..0    3 bits

A viable data encoding:

        type Value = struct 
            { uint64 stem                   // 0..63 bits encoded
            ; Term   term           
            }

        type Term =
            | Leaf
            | ExtStem of uint64 * Term      // full 64 bits encoded
            | Branch of Value * Value
            | ... (accelerated lists, binaries, etc.) ...

With this compact encoding, short, simple data including symbols and nearly all int64 values (except int64 min), can be encoded without heap allocations. Further, binaries - lists of 8-bit bitstrings - can be specially optimized to support more efficient file and socket IO. 

*Note:* The Glas Object encoding of glas data will similarly compact bitstrings and binaries for efficient storage and serialization.

## Operations Performance

My vision for glas systems does not emphasize bitstring manipulations. Most operations on bitstrings will be O(N). Common operations may be optionally accelerated (via 'accel:opt:...' annotations) to improve performance, but we shouldn't depend on that. Glas systems should favor operating on binaries instead of very large bitstrings.

Eventually, to enter domains such as compression and cryptography, glas systems should accelerate a virtual machine oriented around manipulation of bitstrings or binaries. However, this is a task for another module.
