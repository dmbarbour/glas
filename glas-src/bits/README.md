# Bit String Manipulations

            /       0       Tree represents bitstring
            \       1           0b011010001
             \      1
             /      0
             \      1
             /      0
            /       0
           /        0
           \        1

A bitstring in glas is a subset of binary trees where every node has exactly one or zero children, with edges labeled 0 or 1. Glas runtimes optimize bitstring representations similar to optimizing radix trees.

Within a glas runtime, bitstrings are reasonably well compacted, i.e. a sequence 63 bits might be represented in a 64-bit field together with a

This module defines common operations on bitstrings. 


These bitstring operations are not accelerated. I intend to eventually develop an accelerated virtual machine model suitable for bitstring manipulation to support compression, encryption, etc.. The functions in this module are slow, but usable.
