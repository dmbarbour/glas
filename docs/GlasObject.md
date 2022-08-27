# Glas Object

Glas Object, or 'glob', is intended to be a compact, indexed binary representation for Glas values, with support for content-addressed reference to other binaries. The intended use case is persistence, serialization, and caching.

## Desiderata

* *indexed* - Data access to specific elements of lists and dicts does not need to parse or scan unrelated regions of the glob file. Dictionaries can be organized as radix trees. Lists can be organized as finger trees or ropes.
* *compact* - Binary data can be very efficiently embedded into globs. Tabular data can avoid repeating headers. Structured data can be embedded tighter than many in-memory representations. Can easily share common structure within a file.
* *scalable* - Larger-than-memory values can be loaded partially, leaving most data in content-addressed storage. All sizes use variable-sized numbers. Content-addressed storage is persistent, can support databases.
* *updatable* - Can model updates efficiently and incrementally, similar to log-structured merge-tree.
* *simple* - should not have a very complicated parser. A minimalist writer (no fancy compressio passes) should also be easy. 

It is assumed that Glas Object globs will undergo validation if they come from an untrusted source. 

## Nodes

A glob binary always starts with a data node, i.e. the first byte will be parsed as a data node header. A glob parser will always 

### Basic Nodes

The three basic nodes are stems, leaves, and branches. 

* *stem* - a tree node having exactly one child. 
* *leaf* - tree node with no children. 
* *branch* - tree node with two children. header is followed by offset to right child, then immediately by left child.

In Glas systems, symbols, numbers, and radix trees are all based on stems. It is essential to compact adjacent stem bits to require fewer bytes than bits. Additionally, it is convenient to merge stems into leaves to save one byte per symbol or number, and merge stems into branches to save one byte for every field in a record or dict after the first. This merge effectively multiplies the number of stem nodes. 

The proposed encoding for stem nodes is HD3 + PBC8. That is, we can encode up to 3 bits directly in the node header, or encode up to 64 bits in bytes after the node header. This design costs exactly 32 node header types for each of stem, leaf, and branch (14 from HD3, 16 from PBC8, 1 w/o stem, 1 reserved). 

*Aside:* The stem w/o stem option is included for symmetry and is effectively the whitespace operator of Glas Object.

### References

Glas Object supports internal references within a glob file, and external references between glob files.

* *internal ref* - header followed by varnat offset to value later in the current glob.
* *external ref* - header followed by a 64-byte secure hash (SHA3-512) of another glob.

External references refer start parsing from the first byte of a glob. However, in some cases it may be convenient to support larger dictionary globs then reference individual definitions. This is supported via path select node: 

* *path select* - header followed by byte count (varnat + 1), that many path bytes, then the target value (usually an external ref). Equivalent to the value reached by following the path to the target.
* *list select* - header followed by index (varnat) then by target list value. Equivalent to value reached by indexing into the list (0-based). 

Path select only supports 8-bit aligned paths. It is possible to work around limitation this by adding 1..7 prefix bits to a target value. However, in practice, definitions in a dictionary are usually 8-bit aligned. 

### Annotations

Annotations are intended to support tooling at the Glas Object layer. They do not affect the value, and there is no implicit guarantee that annotations are preserved by a given tool.  

* *annotation* - header is followed by offset to annotated value then immediately by annotation value. 

Potential use cases for annotations include projectional editing hints, metadata about a glob file or its provenance, or suggesting a specialized runtime representation for acceleration. However, annotations at the Glas Object layer generally cannot be observed at other layers and do not replace annotations at other layers.

### Lists

Lists are a simple data structure formed from a right-spine of pairs terminating in unit (leaf). 

                 /\     type List = (Value * List) | ()
                a /\
                 b /\   a list of 5 elements
                  c /\      a:b:c:d:e:[]
                   d /\     [a,b,c,d,e]
                    e  ()

In Glas systems, lists are used for almost any sequential data structure - arrays, sparse arrays, tuples, stacks, queues, deques, binaries, etc.. However, the direct representation of lists is awkward and inefficient for many of use-cases. Thus, Glas systems support several optimized representations of lists, especially arrays or [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

Glas Object provides a few nodes to support effective serialization of indexed, updateable lists.

* *binary* - header followed by length (varnat + 1), then by that many bytes. Represents a list containing the same sequence of data (representing bytes as 8-bit bitstrings, msb to lsb).
* *array* - header followed by length (varnat + 1) then by that many value offsets. The varnat encoding of offsets is denormalized so they share the same width. All offsets are relative to end of array. Represents a list containing the sequence of values reached by those offsets.
* *flatten* - followed by a varnat representing cached length, then by a list of lists. Represents the result of concatenating all the component lists. 

### Lazy Patches

Lazy patching enables Glas Object to be used similarly to a [log-structured merge-tree](https://en.wikipedia.org/wiki/Log-structured_merge-tree) database, depending on tooling. Updates can aggregate to a heuristic threshold, amortizing update costs. Frequently updated data is implicitly represented near the root of the glob dependency graph, i.e. in working memory. 

* *apply patch* - header followed by offset to target value then a patch node. Represents the outcome of applying the patch to another value. Application of patches is purely functional.

Patch nodes are logically distinct from data nodes, but overlap to simplify parsing. 

#### Patching Trees

Patches on trees support stem and branch nodes, but the meaning is tweaked to 'follow' the path before applying another patch, or apply a patch to each branch. Encodings are slightly reinterpreted so children are patch nodes instead of data nodes. (Leaf nodes do not modify the value, but could be interpreted as implicit assertions that a node exists.)

Upon reaching the indicated location in the tree, patches can modify the tree structure in several ways:

* *replace* - header is followed by value. Replaces value at location.
* *splice* - header includes direction (left or right), followed by value. Stem becomes a branch. Value is inserted in the indicated direction. (Direction is mostly necessary for merging patches.)
* *trim* - header includes direction (left or right). Branch becomes a stem. Removes indicated branch, keeping the other.
* *grow* - header is followed by stem bits, then another patch. Inserts stem bits.
* *shrink* - header is followed by stem bits, then another patch. Removes stem bits.

The most common patches will be 'replace' for updates, 'splice' for inserts, and 'trim' for deletes. 

The 'grow' and 'shrink' patches enable manipulation of tree bits without affecting tree leaves. I propose to encode 1 to 8 stem bits using a single byte and a flag bit in the header (similar to PBC encoding). This gives a 50% encoding which is adequate for a rare patch.

*Note:* Missing patches for permutation of data. Probably not essential.

#### References

Patches allow internal references to other patches. External references are not permitted.

#### Patching Annotations

The 'annotation' node is reinterpreted to apply a patch each to annotations and annotated value, similar to branching. Unlike branches, every node has a (usually empty) list of annotations, and we'll apply the patch to the whole list. For example, we can erase annotations by replacing the original annotations with the empty list.

#### Patching Lists

It is possible to patch short lists using branches directly. An 'array' node will be interpreted as a list of patches to apply, affecting the first several elements of a list. To access deep into a list, we can use the 'skip' operator.

* *skip* - header followed by count (varnat + 1) then by a patch. Follows right branch that many times then applies patch (i.e. to remaining list).

The skip node is useful for indexing into lists, but not so useful for indexing into the patch itself.

....


What are my options here?

* remove a range of elements
* insert a range of elements
* patch an array of elements
* replace a binary region

## Encodings

### Varnat Encoding

Glas Object encodes natural numbers for sizes and offsets. This encoding for natural numbers includes a unary prefix `1*0` to encode the total number of bytes, followed by 7 data bits per byte. Normal form is to use the fewest bytes possible, but varnats may be denormalized in some contexts (e.g. array nodes denormalize offsets to support random access). 

        0xxxxxxx
        10xxxxxx xxxxxxxx
        110xxxxx xxxxxxxx xxxxxxxx                  
        1110xxxx xxxxxxxx xxxxxxxx xxxxxxxx
        ...

Most implementations will limit varnats to 63 bits in practice, i.e. same as the maximum int64 value. But to avoid artificial limitations on scalability, this isn't imposed by Glas Object itself. Parsing varnats is considered O(1).

### Offsets

Offsets are relative forward-only pointers used within Glas Object binaries. They are represented by varnats. The number usually represents how many bytes to jump after the offset, i.e. 0 will point to the very next byte. In context of arrays, the offsets represent number of bytes to jump from end of the array.

### Path Byte Count (PBC) Encoding

Path Byte Count encoding is used for encoding larger stem sequences. The path byte count is encoded into the header type, along with a flag to indicate whether the first byte is full or partial. A partial first byte uses the lowest '1' bit to indicate number of encoded bits (1 to 7).

        Partial Byte
        abcdefg1    - 7 bits
        abcdef10    - 6 bits
        abcde100    - 5 bits
        abcd1000    - 4 bits
        abc10000    - 3 bits
        ab100000    - 2 bits
        a1000000    - 1 bits
        10000000    - 0 bits
        00000000    - unused

PBC costs 1/4th the number of header types compared to directly encoding a bitcount in the header. For example, using 16 header types we could support PBC of 8 bytes, which asymptotically approaches 89% efficiency for a long stem and is sufficient to represent most symbols and numbers we'll encode in Glas systems with a single node.

To mitigate the short paths, I'll also encode up to 3 bits directly into node headers (HD3). 

An 'extended' variation could use an extra byte to encode the number of bytes (and perhaps whether first byte is full) to provide high-efficiency encodings beyond the limit of 4 path bytes.

### Node Encodings

Basic Nodes

        0x20-0x3F   Leaf Nodes
        0x40-0x5F   Stem Nodes
        0x60-0x7F   Branch Nodes

        Stem HD3 (no prefix bytes)
            ttt0 1000 - zero prefix bits.
            ttt0 a100 - one prefix bit.
            ttt0 ab10 - two prefix bits.
            ttt0 abc1 - three prefix bits.

        Stem PBC (1 to 8 prefix bytes)
            ttt1 fnnn 
                f - first byte full? (1 full, 0 partial)
                nnn - number of prefix bytes

        Full Encoding:
            Leaf - Header PrefixBytes 
            Stem - Header PrefixBytes Value
            Branch - Header PrefixBytes Offset Value

Other Data Nodes:

        0x02        Internal Ref
        0x03        External Ref
        0x04        Annotation
        0x05        Apply Patch
        0x06        Path Select
        0x07        List Select

        0x0A        Array
        0x0B        Binary
        0x0C        Flatten

Patch Nodes:

        Common Nodes - reinterpreted

        0x20-0x3F   Leaf Nodes - Asserts that a node exists, do nothing.
        0x40-0x5F   Stem Nodes - Child node is another patch
        0x60-0x7F   Branch Nodes - Children both are patches
        0x02        Internal Ref - Ref to later patch in glob
        0x04        Annotation - Patch annotations list and annotated value.
        0x0A        Array - apply array of patches to list elements
        0x0B        Binary - Asserts match on binary (treat as Leaf nodes).

        Patch Specific

        0x81        Replace
        0x82        Splice Left
        0x83        Splice Right
        0x84        Trim Left
        0x85        Trim Right
        0x86        Grow Bits (1 to 7 bits)
        0x87        Grow Byte (8 bits)
        0x88        Shrink Bits (1 to 7 bits)
        0x89        Shrink Byte (8 bits)

        0x91        List Skip
        




## Ideas for Later

* Chunkification of lists into matrices.
* Transposition of matrics.
* Lightweight 'codec' logic for fixed-width binary data.
* Represent Array of Structures as Structure of Arrays.



