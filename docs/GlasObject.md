# Glas Object

Glas Object, or 'glob', is a compact, indexed binary representation for tree-structured data. Primary use cases are data storage, communication, and caching. The focus is representation of dictionaries (radix trees) and lists (arrays, binaries, finger tree ropes), and structure sharing. 

Glas Object is intended to work well with content-addressed storage. A glob can reference other globs by secure hash (SHA3-512), and can work together with proxy caching via content delivery networks. However, it is also feasible to use globs within the glas module system, with access to other modules and procedural generation. This is supported by abstracting the *external reference* type.

## Desiderata

* *indexed* - Data access to specific elements of lists and dicts does not need to parse or scan unrelated regions of the glob file. Dictionaries can be organized as radix trees. Lists can be organized as finger trees or ropes.
* *compact* - Common data and binaries can be efficiently embedded. Can easily share common structure within a glob. 
* *scalable* - Larger-than-memory values can be loaded partially, leaving most data in content-addressed storage. All sizes use variable-sized numbers. Content-addressed storage is persistent, can support databases.
* *simple* - Should not have a complicated parser. Composition and decomposition should be easy. A basic writer (i.e. without optimization passes) should be easy.
* *extensible* - Space for new data types or representations to meet future needs.

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

* *array* - header (0x0A) . (length - 1) . (array of offsets); represents a list of values of given length. Offsets are varnats all relative to the end of the array, denormalized to the same width. Width is determined by looking at the first varnat.
* *binary* - header (0x0B) . (length - 1) . (bytes); represents a list of bytes. Each byte represents an 8-bit stem-leaf bitstring, msb to lsb.
* *concat* - header (0x0C) . (offset to right value) . (left list); represents logical concatenation, substituting left list terminal with given right value (usually another list).
* *drop* and *take* - see *Accessors*, support sharing slices of a list 

See *Encoding Finger Tree Ropes* for a pattern to leverage concat effectively. Of course, the system is free to convert ropes into larger arrays or binaries if it doesn't need fine-grained structure sharing of list fragments within or between globs.

### External References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x02) followed by a value. This value must be contextually recognized as referencing another value. The reference is logically substituted by the referent.

I know of at least two relevant 'contexts' for external references: content-addressed storage for stowage and content-delivery networks, and the glas module system in the likely event users begin using ".glob" file extension as a module type..

References in context of content-addressed storage:
* *glob:SecureHash* - reference to content-addressed glob. SecureHash is usually a 64-byte binary representing the SHA3-512 of an external binary. 
* *bin:SecureHash* - reference to content-addressed binary data. Same SecureHash as for globs, but the referent is loaded as a binary instead of parsed as a glob.

References in context of glas module system:
* *local:ModuleName* - value of local module
* *global:ModuleName* - value of global module
* *eval:prog:(do:Program, ...)* - Program must have arity 0--1 and may use 'log' and 'load' effects. This supports procedural generation, static assertions, and handling of 'load' failure.

Content-addressed storage should not use full eval due to denial-of-service risks, but can still leverage *Accessors* and *Templates* for flexible sharing.

### Internal References 

We can forward reference within a glob file. 

* *internal ref* - header (0x88) . (offset); This is just the *path accessor* with an empty path, i.e. a whole-value accessor.

Internal references are mostly useful to improve structure sharing or compression of data. Also useful for the *Glob Headers* pattern, where a glob binary starts with an internal ref.

### Accessors

Accessors support fine-grained structure sharing that preserves indexability and works in context of content-addressed storage. Essentially, we support slicing lists and indexing into records. 

* *path* - headers (0x80-0x9F) . (offset); uses stem-bit header (ttt=100) to encode a bitstring path. Equivalent to following that path into the target value as a radix tree. 
* *drop*  - header (0x0D) . (length) . (offset to list); equivalent to path of length '1' bits. 
* *take* - header (0x0E) . (length) . (inline list value); equivalent to sublist of first length items from list. Although useful to slice lists, this is heavily used to cache list lengths for ropes.

Indexing a list is possible via composition of path and drop, but it shouldn't be needed frequently, so it isn't optimized.

### Annotations

Support for ad-hoc comments within Glas Object.

* *annotation* - header (0x01) . (offset to data) . (metadata); the metadata may be an arbitrary value.

In practice, annotations at the glas object layer are written by a runtime when it's storing data then read by the runtime when it's loading data. Potential use cases include hints for accelerated runtime representations and tracking dataflow while debugging. User programs can potentially access these annotations via runtime reflection APIs. However, it's usually wiser to model annotations in the data layer if possible.

## Varnats, Lengths, and Offsets

A varnat is encoded with a prefix '1*0' encoding a length in bytes, followed by 7 data bits per prefix bit, msb to lsb order. For example:

        0nnnnnnn
        10nnnnnn nnnnnnnn
        110nnnnn nnnnnnnn nnnnnnnn 
        ...

In normal form, varnats use the smallest number of bytes to encode a value. It isn't an error to use more bytes, just not very useful within an immutable binary. In some where the minimum value is one instead of zero, we'll encode one less such that a single byte can encode 1 to 128.

## Summary of Node Headers

        0x00        Void (never used)
        0x01        Annotation
        0x02        External Ref

        0x0A        Array
        0x0B        Binary
        0x0C        Concat
        0x0D        Drop
        0x0E        Take 

        0x20-0x3F   Stem-Leaf and Leaf (0x28) 
        0x40-0x5F   Stem Nodes (and Nop - 0x48)
        0x60-0x7F   Stem-Branch and Branch (0x68)
        0x80-0x9F   Index Path and Internal Ref (0x88)

        PROPOSED EXTENSIONS (see below):

        0x03        Var
        0x04        App

        CURRENTLY UNUSED:
        0x05-0x09
        0x0F-0x1F
        0xA0-0xFF

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
            Larger Array or Binary (heuristic)
            Size . Node(k-1)
    Node(k) - two or three concatenated Digit(k)
    Digits(k) - one to four concatenated Digit(k)
        LDigits(k) - right assoc, e.g. (A ++ (B ++ C))
        RDigits(k) - left assoc, e.g. ((A ++ B) ++ C)
    Rope(k)
        Empty               Leaf
        Single              Array | Binary | Node(k-1)
        Many                Size . (LDigits(k) ++ (Rope(k+1) ++ RDigits(k)))

This structure represents a 2-3 finger-tree rope, where '2-3' refers to the size of internal nodes. It is possible that wider nodes and more digits would offer superior performance. In any case, the finger-tree rope is effective for many use cases for larger lists. 

The ability to force a sublist into a flat array or binary is also useful.

### Glob Headers

Headers can be directly encoded as part of a glob value then bypassed via 'accessor'. Alternatively, it could be encoded as an annotation. However, these options make it difficult to reason locally about whether the header is referenced externally and must be preserved by tools.

An alternative is to use a simple convention where a glob binary that starts with an internal reference (0x88 (offset)) is leaving space for the header. Usually 0x88 supports structure sharing, but this wouldn't normally apply to the root element of the glob binary.

        0x88 (offset to data) (header) (data)

A header value might, by convention, be a record of form `(field1:Value1, field2:Value2, ...)` with ad-hoc symbolic labels as header fields.

The difference between a header and annotations is scope. The header should describe the glob binary as a whole rather than any specific data within. This could include version info, cryptographic signatures for trust, etc..

### Shared Dictionary

Build a dictionary of useful values then share and reference as needed (via external refs and accessor nodes). In some cases, this can greatly improve compression while avoiding any explicit stateful communication context. 

## Potential Future Extensions

Many ideas won't be worthwhile without enough end-to-end support from compilers and runtimes. I'll defer them until we have a better idea of what is needed and whether the benefits are worth the complications.

### Unboxed Structures

To represent unboxed vectors or matrices, glas systems can use an explicit unboxed view such as `matrix:(type:float64,rows:32, cols:48, data:Binary)` or a structured view involving a list of lists of numbers. Either way, accelerators can manipulate the matrix efficiently. However, glas object will (currently) serialize the structured view inefficiently.

It is feasible to extend glas object with some instructions to perform some simple operations on lists - i.e. to chunkify the list, to reinterpret binaries as bitstrings, perhaps even to apply a template to every element of a list.

Whether this is worthwhile in practice would depend on whether glas systems favor the structured view of the matrix, and how much of a hassle converting to binary for serialization and stowage proves to be in practice. No need to bother if we just directly use the binary view as inputs to accelerators and storage in program state.

### Partial Data and Templates (Proposed Extension)

Glas Object is readily extended to model partial data by introducing labeled variables within data. This is useful insofar as it enables sharing of templates - stable tree roots with variable leaf elements. Viable extension:

* *var* - header (0x03) . (varnat) - represents a variable or unknown, labeled by a natural number.
* *app* - header (0x04) . (template offset) . (varnat K) . (array of args) - substitute sequentially labeled variables (var K, var K+1, etc.) with corresponding arguments from array. 

This isn't a full lambda calculus, i.e. the parameters to 'app' cannot be deferred or abstracted contextually. Evaluation is local. Termination is guaranteed. The resulting 'app' could feasibly be indexed as a value with just a little extra context.

But I'm not convinced this feature is useful in practice. The added complexity isn't trivial. A runtime would need significant hints for when and where to use templates. Aside from contrived circumstances, I suspect it will be difficult to achieve significant benefits.

