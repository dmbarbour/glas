# Glas Object

Glas Object, or 'glob', is a compact, indexed binary representation for tree-structured data. Primary use cases for Glas Object are data storage, communication, and caching. The focus is representation of dictionaries (radix trees) and lists (arrays, binaries, finger tree ropes), and structure sharing. 

Structure sharing is supported within a glob via offsets and between globs via content-addressed references and accessors. We can reference globs by their secure hash (SHA3-512), and follow a path to a component item.

Although Glas Object is designed for Glas, it should be a very good representation for acyclic structured data in general. 

## Desiderata

* *indexed* - Data access to specific elements of lists and dicts does not need to parse or scan unrelated regions of the glob file. Dictionaries can be organized as radix trees. Lists can be organized as finger trees or ropes.
* *compact* - Common data and binaries can be efficiently embedded. Can easily share common structure within a glob. 
* *scalable* - Larger-than-memory values can be loaded partially, leaving most data in content-addressed storage. All sizes use variable-sized numbers. Content-addressed storage is persistent, can support databases.
* *simple* - Should not have a complicated parser. Composition and decomposition should be easy. A minimalist writer (i.e. without compression passes) should also be easy. Header is optional.

## Encoding

A parser for Glas Object will first read a header byte that indicates how to read the remainder of a node. The first byte of a glob binary is the 'root value' for the glob, and all relevant data in the glob must be accessible from the root.

### Basic Data

Glas data is binary trees. Glas Object distinguishes leaves, stems, and branches.

* *leaf* - a terminal node in the tree. 
* *stem* - a tree node with one child.
* *branch* - a tree node with two children.

Glas Object uses stems heavily to encode bitstrings. Numbers, symbols, etc. are encoded into as bitstrings. Thus, compaction of stems is essential. Additionally, we could save some bytes (one byte per symbol, number, or branch within a radix tree) by merging stem-leaf and stem-branch.

The proposed encoding for Basic Nodes consumes 96 header types, and supports flexible encoding of large bitstring fragments. 

        ttt0 abc1 - 3 stem bits in header (H3)
        ttt0 ab10 - 2 stem bits in header (H2)
        ttt0 a100 - 1 stem bits in header (H1)
        ttt0 1000 - 0 stem bits in header (H0)

        ttt0 0000 . (length) . (offset to bytes) - large stem
            exact multiple of 8 bits
            stem sharing is feasible

        ttt1 fnnn . (bytes) - medium stem
            f - full or partial first byte (0 - partial)  
            nnn - 1-8 bytes, msb to lsb

        partial first byte - encodes 1 to 7 bits
            abcdefg1 - 7 bits
            abcdef10 - 6 bits
            ...
            a1000000 - 1 bits
            10000000 - 0 bits (unused in practice)
            00000000 - unused

        ttt values:
            001 - Stem-Leaf and Leaf (0x28) Nodes 
            010 - Stem Nodes and Nop (0x48) 
                header . (child value)
            011 - Stem-Branch and Branch (0x68) Nodes
                header . (offset to right child) . (left child)

This can compactly encode symbols, numbers, composite variants, radix trees, etc.. 

### Lists

Lists are a simple data structure formed from a right-spine of pairs (branch nodes), terminating in unit value (a leaf node).

                 /\     type List = (Value * List) | ()
                a /\
                 b /\   a list of 5 elements
                  c /\      [a, b, c, d, e]
                   d /\     
                    e  ()

In Glas systems, lists are used for almost any sequential data structure - arrays, tuples, stacks, queues, deques, binaries, etc.. However, the direct representation of lists is awkward and inefficient for most use-cases. Thus, Glas systems use specialized representations under-the-hood, such as [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). Rope structures must be preserved for efficient serialization.

Specialized List Nodes:

* *array* - header (0x0A) . (length) . (offset to array of offsets); represents a list of values of given length. To support indexing of the array, encoding of offsets within array is denormalized such that all have same length. 
* *binary* - header (0x0B) . (length) . (offset to bytes); represents a list of bytes. Each byte represents an 8-bit stem-leaf bitstring, msb to lsb.
* *inline array* - header (0xA0 - 0xAF) . (array of 1 to 16 offsets); same as array of size 1 to 16, zero offset. 
* *inline binary* - header (0xB0 - 0xBF) . (1 to 16 bytes); same as binary of size 1 to 16, zero offset. 
* *concat* - header (0x0C) . (offset to right value) . (left list); represents logical concatenation of left list to right value (usually another list).

        concat () y = y
        concat (x,xs) y = (x, concat xs y)

See *Encoding Finger Tree Ropes* for a pattern to leverage concat effectively. Of course, the system is free to convert ropes into larger arrays or binaries if it doesn't need fine-grained structure sharing of list fragments within or between globs.

### References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x02) followed by 64-byte secure hash (SHA3-512) of another glob.
* *external bin* - header (0x03) followed by 64-byte secure hash (SHA3-512) of binary value.
* *internal ref* - header (0x88) followed by offset to later value within current glob.

Internal ref nodes are useful for structure sharing within a glob; header is implicit in *path* accessor nodes (see below). External ref nodes can access an external glob binary, or raw binary (treated as a list of bytes). Fine-grained sharing of referenced data is feasible via *Accessor* nodes to slice a list or follow a path into a record.

### Accessors

Accessors support fine-grained structure sharing between globs. For example, we may define a common dictionary then use accessors to reference individual definitions.

* *path* - headers (0x80-0x9F) . (offset to target); uses stem-bit header (ttt=100) to encode a bitstring path. Equivalent to following that path into the target value.
* *drop*  - header (0x08) . (length) . (offset); equivalent to path of length '1' bits. 
* *take* - header (0x09) . (length) . (list value); equivalent to sublist of first length items from list. 

        take 0 _ = ()
        take n (x,xs) = (x, take (n-1) xs)

Essentially, we support slicing lists and indexing into records. 

### Annotations

Support for ad-hoc comments within Glas Object.

* *annotation* - header (0x01) . (offset to data) . (metadata); the metadata may be an arbitrary value.

The main role of annotations is to support external tooling at the data layer. For example, if we develop a projectional editor over a Glas Object database, annotations may provide rendering hints. If a runtime wants to automatically rebuild accelerated representations when data is loaded, annotations could provide acceleration hints.

## Varnats, Lengths, and Offsets

A varnat is encoded with a prefix '1*0' encoding length in bytes, followed by 7 data bits per prefix bit, msb to lsb order. For example:

        0nnnnnnn
        10nnnnnn nnnnnnnn
        110nnnnn nnnnnnnn nnnnnnnn 
        ...

In normal form, varnats use the smallest number of bytes to encode a value. However, varnats may be denormalized in some cases. For example, for an array of offsets, offsets are denormalized to ensure have the same length in order to simplify indexing. 

A length value is encoded as a varnat+1, for example 01111111 encodes varnat 127 but length 128. Offsets encode a number of bytes to skip *after* the offset, thus '0' means 'next byte'. 

*Note:* Most implementations won't want to use bigints for varnats. In practice, 63 data bits is the maximum. This won't affect most use-cases, but does constrain size of 'sparse' arrays represented as ropes with shared zeroes.

## Bytes

Byte values are readily encoded in Glas Object.

        0x38 (byte)     - single byte value
        0xA0 (byte)     - list of single byte

In general, bytes represent 8-bit stem-leaf nodes, msb to lsb.

## Summary of Node Headers

        0x00        Void (never valid data)
        0x01        Annotation
        0x02        External Ref
        0x03        External Bin

        0x08        Drop
        0x09        Take
        0x0A        Array
        0x0B        Binary
        0x0C        Concat

        0x20-0x3F   Stem-Leaf and Leaf (0x28) 
        0x40-0x5F   Stem Nodes (and Nop - 0x48)
        0x60-0x7F   Stem-Branch and Branch (0x68)
        0x80-0x9F   Path and Internal Ref (0x88)
        0xA0-0xAF   Short Arrays (length 1 to 16)
        0xB0-0xBF   Short Binaries (length 1 to 16)

        UNUSED:
        0x04-0x07
        0x0D-0x0F
        0xC0-0xFF

## Conventions and Patterns 

Some ideas about how we might leverage Glas Object for more use cases.

### Encoding Finger Tree Ropes

It is feasible to combine list-take (Size) and concatenation nodes in a way that provides hints of finger-tree structure to support tree-balancing manipulations.

    Concat  (L1 ++ L2)
    Take    (Size . List)

    Digit(k)
        k = 0
            Array
            Binary
        k > 0
            Larger Array or Binary (via heuristics)
            Size . Node(k-1)
    Node(k) - two or three concatenated Digit(k)
    Digits(k) - one to four concatenated Digit(k)
        LDigits(k) - right assoc, e.g. (A ++ (B ++ C))
        RDigits(k) - left assoc, e.g. ((A ++ B) ++ C)
    Rope(k)
        Empty               Leaf
        Single              Array | Binary | Node(k-1)
        Many                Size . (LDigits(k) ++ (Rope(k+1) ++ RDigits(k)))

This structure represents a 2-3 finger-tree rope. The finger-tree rope is a convenient default for most use cases for large lists. And it won't hurt too much if other rope structures are blindly manipulated as finger-tree ropes.

### Glob Headers

If a glob binary starts with an internal reference node, the skipped data cannot be part of the glob value. I propose to use this space to instead store a glob header.

        0x88 (offset to data) (header) (data)

The header value should be a record of form `(field1:Value1, field2:Value2, ...)` with ad-hoc symbolic labels. Unlike annotations, this header does not remain associated with the data. Instead, it should represent attributes specific to the glob binary, such as metadata useful for the glob encoder, or a reverse lookup index to improve structure sharing.

### Shared Dictionary

Build a dictionary of useful values then share and reference as needed (via external refs and accessor nodes). In some cases, this can greatly improve compression while avoiding any explicit stateful communication context. 

## Potential Future Extensions

### Matrices

Matrices are useful across many problem domains, and it might be useful to optimize them in Glas Object. However, I'm not convinced the Glas data layer is the right place to do so due to all the ad-hoc boxing/unboxing and safety constraints. It might be more convenient to represent common unboxed floating point matrices as binaries in Glas systems (perhaps with structured headers), then develop accelerated operations on them. 

### Structs and Tables

Support for applying a list of labels to a vector or matrix of data. This could reduce repetition within a glob by a great deal, and is a good option for structure sharing of the header bits. But like matrices, taking a wait and see approach.

### Lenses, Codecs, Patches

It is feasible to extend Glas Object with a lightweight language supporting 'views' of data. This language should be designed such that it guarantees termination and still supports indexed access to data (i.e. partial evaluation aligns with indexing). It might be feasible to integrate matrices, structs, and tables into this language in some manner. 

This would require a lot of design work. It could make Glas Object a lot more flexible. But I'm uncertain that it would be worth the extra complexity. Something to consider later.
