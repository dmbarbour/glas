# Bitstring Manipulations

A bitstring in glas is encoded as a binary tree where every node has one or zero children. Each edge is labeled 0 or 1, representing a left or right child. A bitstring is encoded into the path through these edges.

            /       0       Tree represents bitstring
            \       1           0b011010001
             \      1
             /      0
             \      1
             /      0
            /       0
           /        0
           \        1

Bitstrings are useful for representing integers, bytes, symbols, and other simple data. However, bitstrings in glas are not optimized for random access and should be relatively short. A binary (a list of bytes) is favored for large texts or binaries.

This module defines many useful operations on bitstrings. 

## Compact Encoding Assumption

One assumption for glas systems is that non-branching bitstring fragments are encoded in a compact manner. One viable encoding that I've used in a few implementations:

        type Stem = Word64 // encodes 0..63 bits
        type Node = Leaf | Branch of Val * Val | Stem64 of Word64 * Node
        type Val = (Stem * Node) // struct 

With a K-bit stem word we can encode up to K-1 bits and stem length. For example, with a 4-bit word, we can encode up to 3 stem bits:

        1000        0 bits
        a100        1 bit
        ab10        2 bits
        abc1        3 bits
        0000        unused

In this case, short bitstring values (unit, small integers or symbols, etc. with a final Leaf node) will be non-allocating, which is very convenient.

## Operations Performance

My vision for glas systems does not emphasize bitstring manipulations. Common operations can be accelerated, but we shouldn't depend on it. Binaries (lists of bytes) should be favored over large bitstrings for serialization, and will be compacted by another mechanism.

Eventually, to enter domains such as compression and cryptography, glas systems will accelerate virtual machines that support low-level 'bit banging' operations. However, this is a task for another module.
