# Record Utilities

Records are a common data type in glas systems and are represented as radix trees. Labels are UTF-8 null-terminated text (or null-terminated binary) encoded into the bitstring path to each value.

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

A null byte (0x00, or 8 sequential zero bits) separates the label from the embedded data. 

This module provides a few utilities for working with records, such as obtaining the list of labels, or converting between records and key-value lists. It also supports working with labels as values.

Some related types:

* *variant* - essentially a record with only a single label from a known set. This serves as the labeled sum type, where records are the labeled product type.
* *dictionary* - represented as a record, but with dynamic labels and homogeneous data
* *wordmaps* - uses fixed-length labels (e.g. 32-bit words) instead of null terminators; useful as a basis for hashmaps
* *symbols* - just the utf-8 null-terminated bitstring representing a single label

## Runtime Encoding

Due to compact encoding of bitstring fragments (see [bits](../bits/README.md)), records are radix trees by default. This representation is reasonably efficient but still involves significant allocation and access overheads. 

A compiler can potentially do much better, translating static record types into a 'struct' representation with a single allocation and efficient, offset-based access. Further, in-place updates are possible if the compiler knows a record is uniquely referenced. These performance features can be guided by annotations. 

Annotations can potentially elevate records into first-class data types in glas runtimes.

## Regarding Key-Value Maps

It is feasible to support arbitrary key-value maps by encoding arbitrary glas values into bitstrings, but I'm not convinced this is a good idea. Viable alternatives include constructing a hashmap or a balanced binary search tree.
