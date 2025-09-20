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

        ttt0 abc1 - 3 stem bits in header
        ttt0 ab10 - 2 stem bits in header
        ttt0 a100 - 1 stem bits in header
        ttt0 1000 - 0 stem bits in header
        ttt0 0000 - reserved 

        ttt1 fnnn . (bytes) - stems of 4 to 64 bits
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

This compactly encodes symbols, numbers, composite variants, radix trees, etc.. 

*Note:* I've dropped support for shared stems via `ttt0 0000` because it was messy and inefficient. Currently there is no option to share common stems. But templated data could possibly fill this role (see *Potential Future Extensions*). 

### Lists

In glas systems, lists are conventionally encoded in a binary tree as a right-spine of branch nodes (pairs), terminating in a leaf node (unit value). This is a Lisp-like encoding of lists.

          /\     type List = (Value * List) | ()
         a /\
          b /\   a list of 5 elements
           c /\      [a, b, c, d, e]
            d /\     
             e  ()

However, direct representation of lists is inefficient for many use-cases. Thus, glas runtimes support specialized representations for lists: binaries, arrays, and [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). To protect performance, Glas Object also offers specialized list nodes:

* *array* - header (0x0A) . (length - 1) . (array of offsets); represents a list of values of given length. Offsets are varnats all relative to the end of the array, denormalized to the same width. Width is determined by looking at the first varnat.
* *binary* - header (0x0B) . (length - 1) . (bytes); represents a list of bytes. Each byte represents a small positive integer, 0..255.
* *concat* - header (0x0C) . (offset to right value) . (left list); represents logical concatenation, substituting left list terminal with given right value (usually another list).
* *drop* and *take* - see *Accessors*, support sharing slices of a list 

See *Encoding Finger Tree Ropes* for a pattern to leverage concat effectively. Of course, the system is free to convert ropes into larger arrays or binaries if it doesn't need fine-grained structure sharing of list fragments within or between globs.

### External References

External references are primarily intended for references between globs.

* *external ref* - header (0x02) followed by a reference value. A reference value must be recognized as representing another value in context. We can logically substitute the external reference with the referenced value.

Reference values in context of content-addressed storage:
* *glob:SecureHash* - reference to content-addressed glob. SecureHash is usually a 64-byte binary representing the SHA3-512 of an external binary. 
* *bin:SecureHash* - reference to content-addressed binary data. Same SecureHash as for globs, but the referent is loaded as a binary instead of parsed as a glob.

External references generalize as a *contextual extension* mechanism for Glas Object. For example, in context of a module system, we might use *local:ModuleName* and *global:ModuleName* instead of content-addressed *glob* and *bin* references. In context of streaming data or templates, we might introduce *var:Nat* to represent data that will arrive later in the stream or perhaps upon demand. 

*Note:* Establishing and maintaining the context is rarely free. Effective support for external references may involve access tokens for a [CDN](https://en.wikipedia.org/wiki/Content_delivery_network), protocols for content negotiation (analogous to HTTP Accept header), reference validation overheads, and so on. 

### Internal References 

We can forward reference within a glob file. 

* *internal ref* - header (0x88) . (offset); i.e. the whole-value *accessor*.

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

### Accelerated Representations

We can extend *external references* to support logical representations of data. In this case, the reference contains all the information we need, but not in canonical form. For example, an unboxed floating point matrix might be represented as:

        (0x02) . matrix:(dim:[200,300], type:f32, data:Binary)

When translated to canonical form, this might translate to a list of lists of 32-bit bitstrings. But a runtime could potentially use the unboxed representation directly. 

We can potentially introduce many more variants to support graphs, sets, etc.. And even matrices might benefit from logical transposition, lazy multiplication, etc.. This complicates content negotiation and the runtime. If parties fail to agree to an accelerated representation, they can still construct the canonical representation and add *annotations* they know to read the data back as a matrix. Of course, if conversion is very expensive, the transaction might be aborted on quota constraints.

Eventually, as accelerated representations achieve status as de-facto standards, we can contemplate assigning dedicated headers in Glas Object to save a few bytes.

## Varnats, Lengths, and Offsets

A varnat is encoded with a prefix '1*0' encoding a length in bytes, followed by 7 data bits per prefix bit, msb to lsb order. For example:

        0nnnnnnn
        10nnnnnn nnnnnnnn
        110nnnnn nnnnnnnn nnnnnnnn 
        ...

In normal form, varnats use the smallest number of bytes to encode a value. It isn't an error to use more bytes, just not very useful within an immutable binary. In some where the minimum value is one instead of zero, we'll encode one less such that a single byte can encode 1 to 128.

## Summary of Node Headers

        0x00        (never used)
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

        CURRENTLY UNUSED:
        0x03-0x09
        0x0F-0x1F
        0xA0-0xFF

## Tentative Extensions

Support for LSM tree style updates could be useful, a notion of 'patching' a tree or a list without the overhead of a 'deep' update.

Support for templates, applying a template to an array of arguments.

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

This structure represents a 2-3 finger-tree rope, where '2-3' refers to the size of internal nodes. It is possible that wider nodes and more digits would offer superior performance, but most gains will likely be due to favoring larger binary or array fragments.

### Glob Headers

As a simple convention, a glob binary that starts with an internal reference (0x88) is considered to have a header. The header should also be glas data, typically a record of form `(field1:Value1, field2:Value2, ...)`. 

        0x88 (offset to data) (header) (data)

A header can be considered an annotation for the glob binary as a whole. Potential use cases include adding provenance metadata, glob extension or version information, or entropy for a convergence secret.

### Data Validation

Validation of glob binaries can be expensive in context of very large data or accelerated representations. Nonetheless, it should be performed before we commit potentially invalid data into a database. That's our last good opportunity to abort a transaction without risk of long-term corruption.

To mitigate validation overheads, a runtime might implicitly trust hashes it learns about from a trusted database or CDN. This trust would be expressed in the runtime configuration. Additionally, we can leverage glob headers or annotations to include proof hints or cryptographic signatures. Proof hints can reduce the cost to re-validate, while signatures might indicate a party you trust already performed the validation.

### Deduplication and Convergence Secret

It is possible for a glas system to 'compress' data by generating the same glob binaries, with the same secure hash. This is mostly a good thing, but there are subtle attacks and side-channels. These attacks can be greatly mitigated via controlled introduction of entropy, e.g. [Tahoe's convergence secret](https://tahoe-lafs.readthedocs.io/en/latest/convergence-secret.html).

## Proposed Extensions

### Small Arrays and Binaries

We could encode length for small arrays or binaries directly in the header, e.g. `0xA(len)` for arrays and `0xB(len)` for binaries of lengths 1 to 16. However, it isn't clear that this is worthwhile, especially for arrays. Perhaps it would be worthwhile for small binaries alone.

### Templated Structs

Encode structures as an array-like structure where the header describes labels separately from the data. This allows reuse of labels for a common use case.

