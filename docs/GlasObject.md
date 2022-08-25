# Glas Object

Glas Object, or 'glob', is intended to be a compact, indexed binary representation for Glas values, with support for content-addressed reference to other binaries. The intended use case is persistence, serialization, and caching.

## Desiderata

* *indexed* - Data access to specific elements of lists and dicts does not need to parse or scan unrelated regions of the glob file. Dictionaries can be organized as radix trees. Lists can be organized as finger trees or ropes.
* *compact* - Binary data can be very efficiently embedded into globs. Tabular data can avoid repeating headers. Structured data can be embedded tighter than many in-memory representations. Can easily share common structure within a file.
* *scalable* - Larger-than-memory values can be loaded partially, leaving most data in content-addressed storage. All sizes use variable-sized numbers. Content-addressed storage is persistent, can support databases.
* *updatable* - In a functional context, update includes producing new values that are slightly different from the original. This can be supported via lazy logical access, inserts, and deletes, similar to log-structured merge trees. 
* *simple* - should not have complicated parser

## Node Types

Tree nodes are aligned with bytes. Reading the first byte tells you what, if anything more, must be read. This gives us 256 base header types, more than enough for Glas Object even if we encode some data into common node types.

### Basic Nodes

The three basic nodes are stems, leaves, and branches.

A 'stem' is a non-branching sequence of tree nodes each having exactly one child. In context of symbols, numbers, and radix trees, it is essential to compact adjacent stem bits. Support for very long stems is not essential, and keep costs down within reason.

The current proposal:

* branch left fby value
* branch right fby value
* header bit len (3 .. 32) fby 1 to 4 bytes, value. 

Encoding efficiency is 12.5% (for 1 or 2 bits) up to 80% efficiency (for 32 bits). Consumes 32 header types. Conveniently aligns with bitmasks. If header bits is not a multiple of 8, then all non-coding bits are in the first byte are filled with 0s. 

A 'leaf' is a tree node with no children. The basic leaf can be encoded as a single choice. It is useful to combine stem-leaf sequences to save one byte per symbol and number value. This can be achieved using an additional 32 header types, aligning with stem choices.

A 'branch' is a tree node with two children. The basic branch can be encoded as a single choice, followed by an offset to the right child, then immediately by the left child. It is useful to combine stem-branch sequences to save a byte per field in any record/dict. This consumes an additional 32 header types. 

Altogether, basic nodes consume 98 header types, and are sufficient to encode arbitrary Glas data. This encoding is adequate for radix trees but not for lists.

### References

There are two basic references in Glas Object: internal and external. Consumes 2 header types.

* *internal* - followed by varnat offset to value later in the same binary.
* *external* - followed by value that contextually identifies another value.

An external reference is *usually* represented by a 32-byte binary containing the SHA3-256 of another glob file. 

However, to enhance flexibility, an external reference may be represented by an arbitrary Glas value. We could use Glas Object as a language module with module system access, treat it as a templating language with free variables, or model streaming data via future value references.

*Note:* Content-addressed references avoid cycles cryptographically - representing a cycle would require a secure hash collision. In other contexts, we may need to detect cycles or prevent them by other tactics, e.g. streaming data could involve monotonic references.

### Annotations

Annotations support tooling at the Glas Object layer. They do not affect the value. Consumes 1 header type.

* *annotation* - same rep as branch (no stem); left value is attached as comment on right value. 

Potential use cases for annotations include projectional editing hints, comments in a Glas Object module, or supporting specialized runtime representation for acceleration at load-time. These do not replace annotations in Glas Programs or elsewhere.

### Lists

Lists are common in Glas systems and serve roles for all sequential data structures. Formally, a list in Glas systems is a tree that always branches on the right spine until terminating in unit.

        type List = (Value * List) | ()

                 /\
                a /\
                 b /\
                  c /\
                   d /\
                    e

However, direct representation of lists is inefficient in the roles of arrays, sparse arrays, queues, and binaries. It also hinders structure sharing within and between lists, requiring a common tail. To resolve these issues, Glas systems will represent large lists as [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). This supports flexible, efficient operations on lists without relying on mutation. 

Node types:

* *binary* - header includes length (1 to 32). Followed by that many bytes. Efficiency 50% to 97%. 
* *array* - header includes length (1 to 32). Followed by that many value offsets. Offsets relative to end of array. 
* *flatten* - followed by varnat caching total size, then by a list of lists. Value is logical result of concatenating the component lists. In practice, the tree structure of 'flatten' nodes is preserved to simplify indexing.
* *kilobyte* - header is followed by 1024 bytes. Represents a large binary - same as flattening a full array of full binaries. Purpose is to reduce overhead for embedding large binaries (from almost 10% to below 1%).

Glas Object doesn't structurally enforce use of finger-tree structure, but indirectly supports it via 'flatten' nodes. Any required logic is left to the runtime or tools. 

*Aside:* Tempting to use 512-byte blocks instead of kilobyte. Still get almost 99% embedding, but might be easier to find opportunities to condense a block.

## Logical Manipulation

### Updates

Logical updates can provide performance benefits similar to a [log-structured merge-tree](https://en.wikipedia.org/wiki/Log-structured_merge-tree). Consider a few useful operations:

* *put* - parameterized by new value, path, original value. Returns original value updated with new value at path. This covers record insert and modifying a single element of a list.
* *record delete* - parameterized by bitstring, path, original value. Returns original value indicated bitstring removed from path (per record deletion).
* *list replace* - parameterized by index, length, inserted list, path to list, original value. Inserts original value with list at path modified replacing elements from [index..index+length) with the inserted list. Used for insert or removal.

One concern is representing 'paths'. A path is something like `.foo.bar[42].qux` - a dotted path through structs and arrays. Essentially, a path is a list of bitstrings and list indexes. Another concern is optimizing operations. Instead of referring to `.foo.bar` repeatedly, it might be desirable to say `.foo.bar{ [42].qux = ...; [43].baz = ...}` or similar, combining operations at a given point on the path.

### Access

Access can improve modularity, allowing partial reuse from very large modules such as shared dictionaries. Mostly, this means access to list and record values, including partial lists.

* *get* - parameterized by path, original value. Value is whatever we get from following path into the indicated value.
* *slice* - parameterized by index, length, path to list. Returns a sublist of given length from the given index.

Support for partial records might also be useful, e.g. specifying a subset of labels to preserve. But it isn't essential - we could manually construct a record based on getting values from another record. The main reason 'slice' is needed is that we cannot reasonably do the same with potentially very large lists.

## Encodings

### Varnat Encoding

Glas Object encodes natural numbers for sizes and offsets. This encoding for natural numbers includes a unary prefix `1*0` to encode the total number of bytes, followed by 7 data bits per byte. Normal form is to use the fewest bytes possible. For example:

        0xxxxxxx
        10xxxxxx xxxxxxxx
        110xxxxx xxxxxxxx xxxxxxxx                  
        1110xxxx xxxxxxxx xxxxxxxx xxxxxxxx
        ...

Most implementations will limit varnats to 63 bits in practice, i.e. same as the maximum int64 value. But to ensure future scalability, this limit isn't imposed by Glas Object itself.

### Offsets

Offsets are encoded as a varnat count of bytes to skip, but what this is relative to is context dependent. Usually, a 0 offset means a value starts with the next byte after the offset. For array nodes, this is relative to the last offset.

### Node Type Encodings

Individual Nodes:

                Leaf
                Branch
                Internal Ref
                External Ref
                Annotation
                Flatten
                Kilobyte

Group Nodes:

        Stem Nodes (L, R, Len 3 to 32)
        Stem-Leaf Nodes (as Stem)
        Stem-Branch Nodes (as Stem)
        Binary Nodes (Length 1 to 32)
        Array Nodes (Length 1 to 32)

There is some room for expansion, if 


## Ideas for Later

* Chunkification of lists into matrices.
* Transposition of matrics.
* Lightweight 'codec' logic for fixed-width binary data.
* Represent Array of Structures as Structure of Arrays.



