# Record Utilities

Records are a very common data type in glas systems, and are represented as radix trees, with labels encoded using ASCII (or UTF-8) into a null-terminated bitstring path. 

         'c'  0x63
         /     0
         \     1
          \    1
          /    0
         /     0
        /      0
        \      1
         \     1
       <next>

The value reached by following the null terminator (0x00, or 8 zero bits) represents the data recorded within the record at a given label.

This module provides a few utilities for working with records, such as converting between records and lists of key-value pairs, or converting between strings (lists of bytes) and symbols.

## Intmaps

As a variation, records can support fixed-width indices. This might be useful as a representation of sparse arrays or matrices, for example. Conversely, we can view records as being constructed using 8-bit intmaps.

## Compact Encoding

Non-branching bit sequences are encoded in a compact manner. Given a word of K bits, we can encode up to K-1 bits.

        1000000..0      no bits
        a100000..0      1 bit
        ab10000..0      2 bits
        abc1000..0      3 bits
        ..
        abcdef1..0      6 bits
        etc.

For bitstrings that require multiple words, we can arrange to use the full word in all except the first item. Something like this:

        type Node = Leaf | Branch of Val * Val | Stem32 of Word32 * Node
        type Val = (Word32 * Node) // encodes 0..31 stem bits

These are implementation details and may vary from one implementation to another. But it does provide a hint about how a runtime is expected to represent bitstrings and records. 




