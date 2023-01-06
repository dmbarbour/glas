# Glas Object

Glas Object, or 'glob', is a compact, indexed binary representation for tree-structured data. Primary use cases are data storage, communication, and caching. The focus is representation of dictionaries (radix trees) and lists (arrays, binaries, finger tree ropes), and structure sharing. 

Glas Object is intended to work well with content-addressed storage. A glob can reference other globs by secure hash (SHA3-512), and can work together with proxy caching via content delivery networks. However, it is also feasible to use globs within the glas module system, with access to other modules and procedural generation. This is supported by abstracting the *external reference* type.

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

### External References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x02) followed by a value. This value must be recognized as referencing another value in context, and is logically substituted by the referenced value.

There are currently two main contexts for external references: the glas module system (i.e. ".glob" files as modules) and content-addressed storage (i.e. for stowage or content-delivery networks). 

Proposed reference model: 

* In context of glas module system:
 * *local:ModuleName* - value of local module
 * *global:ModuleName* - value of global module
 * *eval:prog:(do:Program, ...)* - Program must have arity 0--1 and may use 'log' and 'load' effects. This supports procedural generation, embedded checks, and handling of 'load' failure.
* In context of content-addressed storage:
 * *glob:SecureHash* - reference to content-addressed glob. SecureHash is usually a 64-byte binary representing the SHA3-512 of an external binary. 
 * *bin:SecureHash* - reference to content-addressed binary data. Same SecureHash as for globs.

*Note:* Content-addressed storage does not have access to full eval due to denial-of-service risks, but can still use *Accessors* for fine-grained sharing of referenced data.

### Internal References 

We can forward reference within a glob file. 

* *internal ref* - header (0x88) followed by offset to value within current glob.

Internal references are mostly useful to improve structure sharing or compression of data. Internal references are encoded as path *Accessors* with the empty path. Also used in the *Glob Headers* pattern.

### Accessors

Accessors support fine-grained structure sharing that preserves indexability and works in context of content-addressed storage. Essentially, we support slicing lists and indexing into records. 

* *path* - headers (0x80-0x9F) . (offset); uses stem-bit header (ttt=100) to encode a bitstring path. Equivalent to following that path into the target value. 
* *drop*  - header (0x08) . (length) . (offset); equivalent to path of length '1' bits. 
* *take* - header (0x09) . (length) . (list value); equivalent to sublist of first length items from list. In addition to slicing lists, this is useful to cache list length for ropes.

### Annotations

Support for ad-hoc comments within Glas Object.

* *annotation* - header (0x01) . (offset to data) . (metadata); the metadata may be an arbitrary value.

The main role of annotations is to support external tooling at the data layer. For example, if we develop a projectional editor over a Glas Object database, annotations may provide rendering hints. If a runtime wants to automatically rebuild accelerated representations when data is loaded, annotations could provide acceleration hints.

## Varnats, Lengths, and Offsets

A varnat is encoded with a prefix '1*0' encoding a length in bytes, followed by 7 data bits per prefix bit, msb to lsb order. For example:

        0nnnnnnn
        10nnnnnn nnnnnnnn
        110nnnnn nnnnnnnn nnnnnnnn 
        ...

In normal form, varnats use the smallest number of bytes to encode a value. However, varnats may be denormalized in some cases. For example, for an array of offsets, offsets are denormalized to ensure have the same length in order to simplify indexing. 

A length value is encoded as a varnat+1, for example 01111111 encodes varnat 127 but length 128. Offsets encode a number of bytes to skip *after* the offset, thus '0' means 'next byte'. 

*Note:* Most implementations won't want to use bigints for varnats. In practice, 63 data bits is the maximum. This won't affect most use-cases, but does constrain size of 'sparse' arrays represented as ropes with shared zeroes.

## Summary of Node Headers

        0x00        Void (never used)
        0x01        Annotation
        0x02        External Ref

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
        0x03-0x07
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

This structure represents a 2-3 finger-tree rope. The finger-tree rope is a convenient default for most use cases for large lists. It won't hurt too much if other balanced rope structures are blindly manipulated as finger-tree ropes.

### Glob Headers

Headers can be directly encoded as part of a glob value then bypassed via 'accessor'. Alternatively, it could be encoded as an annotation. However, these options make it difficult to reason locally about whether the header is referenced externally and must be preserved by tools.

An alternative is to start the glob binary with an internal reference node. The skipped data cannot be observed as part of the glob data. I propose to use this space to instead store a glob header.

        0x88 (offset to data) (header) (data)

The header value will usually be a record of form `(field1:Value1, field2:Value2, ...)` with ad-hoc symbolic labels as header fields. Unlike annotations, this header does not remain associated with the data. Instead, it should represent attributes specific to the glob binary, such as metadata useful for the glob encoder, or a reverse lookup index to improve structure sharing.

### Shared Dictionary

Build a dictionary of useful values then share and reference as needed (via external refs and accessor nodes). In some cases, this can greatly improve compression while avoiding any explicit stateful communication context. 

## Potential Future Extensions

### Matrices

Matrices are useful across many problem domains. However, I'm uncertain about trying to specialize Glas Object to include matrix support. For example, it might prove more effective to represent matrices mostly as binary data (perhaps adding a small header), with accelerators that can manipulate it as a matrix of floats or other 'unboxed' data. Deferred for now.

If I do pursue matrices within Glas Object, I think we'd need some sort of chunking function. I.e. given a list of M*N size, a chunkify op could produce a list of length M of lists of length N. A logical transpose operation might also be useful. (I doubt we can support Z-order easily.)

### Tables

Support for applying a list of labels to a vector or matrix of data. This could reduce repetition within a glob by a great deal, and is a good option for structure sharing of the header bits. But like matrices, I'm taking a wait and see approach.
