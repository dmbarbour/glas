# Glas Object

Glas Object, or 'glob', is intended to be a compact, indexed binary representation for Glas values, with support for content-addressed reference to other binaries. The intended use case is persistence, serialization, and caching.

## Desiderata

* *indexed* - Data access to specific elements of lists and dicts does not need to parse or scan unrelated regions of the glob file. Dictionaries can be organized as radix trees. Lists can be organized as finger trees or ropes.
* *compact* - Binary data can be very efficiently embedded into globs. Tabular data can avoid repeating headers. Structured data can be embedded tighter than many in-memory representations. Can easily share common structure within a file.
* *scalable* - Larger-than-memory values can be loaded partially, leaving most data in content-addressed storage. All sizes use variable-sized numbers. Content-addressed storage is persistent, can support databases.
* *simple* - should not have a very complicated parser. No separate headers (but annotations are supported). A minimalist writer should also be easy, even if it doesn't result in the best compression.

## Encoding

A parser for Glas Object will first read a header byte that indicates how to read the remainder of a node. A glob binary starts with such a header byte, which represents the value for that binary. *Accessor* nodes support fine-grained structure sharing between globs.

### Basic Data

Glas data is binary trees. We could distinguish basic tree types as leaves, stems, and branches.

* *leaf* - tree node with no children. 
* *stem* - a sequence of tree nodes having exactly one child. 
* *branch* - tree node with two children. header is followed by offset to right child, then immediately by left child.

Glas Object uses stems heavily to encode bitstrings. Numbers, symbols, etc. are encoded into as bitstrings. Thus, compaction of stems is essential. Additionally, we could save some bytes (one byte per symbol, number, or branch within a radix tree) by merging stem-leaf and stem-branch.

The proposed encoding for Basic Nodes uses 96 header types:

        ttt0 abc1 - 3 bits in header (H3)
        ttt0 ab10 - 2 bits in header (H2)
        ttt0 a100 - 1 bits in header (H1)
        ttt0 1000 - 0 bits in header (H0)
 
        ttt1 fnnn (Bytes) - PBC 
            f - full (1) or partial (0) first byte  
            nnn - 1-8 byte.
 
        ttt0 0000 (ofnn nnnn) (Bytes or Offset) - Extended PBC 
            o - offset (1) or inline bytes (0)
            f - full (1) or partial (0) first byte
            nnn nnnn - 1-64 bytes.

        partial byte: (1 to 7 bits)
            abcdefg1 - 7 bits (msb first)
            abcdef10 - 6 bits
            ...
            a1000000 - 1 bits
            10000000 - 0 bits (unused in practice)
            00000000 - unused

        ttt:
            001 - Leaf and Stem-Leaf Nodes 
            010 - Stem Nodes 
            011 - Branch and Stem-Branch Nodes

        offset: varnat, bytes to skip after offset
        varnat: unary prefix (1*0) then 7 data bits per prefix bit.
            0nnnnnnn
            10nnnnnn nnnnnnnn
            110nnnnn nnnnnnnn nnnnnnnn
            ...
            standard glob impl will max at 63 data bits.

This compactly encodes symbols, numbers, composite variants, radix trees, etc. without spending bytes on expensive structure. Common stems can optionally share bytes via External PBC encoding, though this is only worthwhile for large, common stems. In practice, most stems will be relatively short.

*Aside:* The H0 Stem header (0x48 - 0100 1000) doesn't encode any stem bits, and is followed by a child node. This represents the no-op for Glas Object.

### References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x02) followed by 64-byte secure hash (SHA3-512) of another glob.
* *internal ref* - header (0x88) followed by offset to later value within current glob.

Internal ref nodes are useful for structure sharing within a glob, and external refs can control memory use and support structure sharing between globs. External refs must be combined with *Accessors* to support fine-grained structure sharing, i.e. sharing component values between globs.

### Accessors

Accessors support fine-grained structure sharing between globs. For example, we could define a common dictionary then use accessors to reference individual definitions.

* *path select* - headers use same encoding of path bits as stem nodes (ttt = 100), followed by a target value offset (perhaps to an external ref). Represents the value reached by following the path into the target.
* *list drop* - header (0x08) followed by count (varnat + 1) then by a list-like value. Represents value reached by following path of count '1' bits.
* *list take* - header (0x09) followed by count (varnat + 1) then by a list-like value. Represents value after replacing the node at count '1' bits with a leaf node (unit).

The *internal ref* node (0x88) is equivalent to encoding the 0-bit prefix for *path select*. 

Accessors are oriented around Glas Object list and radix-tree structures because that's what we index. List accessors work for any value with a contiguous right spine, i.e. list-like values. However, they are most optimally applied to list nodes.

### Annotations

Annotations can include rendering hints for projectional editing, metadata for provenance tracking, suggest in-memory representations for acceleration at load-time, and otherwise support tooling. Each value in Glas Object may have a list of annotations (usually empty) by applying annotation nodes.

* *annotation* - header (0x04) is followed by offset to annotated value, then by the annotation value.

Glas Object annotations emphatically *do not* replace annotations at other layers, such as the Glas program model. A runtime could feasibly drop annotations or provide access effectfully.

### Lists

Lists are a simple data structure formed from a right-spine of pairs terminating in unit (leaf). 

                 /\     type List = (Value * List) | ()
                a /\
                 b /\   a list of 5 elements
                  c /\      a:b:c:d:e:[]
                   d /\     [a,b,c,d,e]
                    e  ()

In Glas systems, lists are used for almost any sequential data structure - arrays, tuples, stacks, queues, deques, binaries, etc.. Sparse lists, where elements are optional, are also useful in some contexts. However, direct representation of lists is awkward and inefficient for most use-cases. Thus, Glas systems will often use specialized representations under-the-hood, such as [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

Glas Object provides a few specialized nodes to support serialization of indexed lists (or any sufficiently list-like structure). Binary data is explicitly supported because it's the most common data type for interfacing between Glas systems and other systems.

* *array* - header (0x0A) followed by length (varnat + 1) then by that many value offsets. The varnat encoding of the offsets is denormalized so they're all the same width. All offsets are relative to just after the last one. Represents a list of values.
* *binary* - header (0x0B) followed by length (varnat + 1) then by that many bytes. Represents a list containing the binary data. Each byte corresponds to an 8-bit bitstring, msb to lsb. 
* *concat* - header (0x0C) followed by an offset to remainder, then by a list-like value whose right spine terminates in a leaf (unit). Represents value formed by replacing that leaf with the remainder.

        concat (A:AS) B = A:concat(AS,B)
        concat () B = B

In context of concat, we can easily index past 'list take', array, and binary nodes because they record size information. Serialization of ropes should introduce 'list take' nodes as needed for efficient indexing, simply taking the full length of the list.

## Summary of Node Headers

        0x02        External Ref
        0x04        Annotation

        0x08        List Drop
        0x09        List Take
        0x0A        Array
        0x0B        Binary
        0x0C        Concat

        0x20-0x3F   Leaf and Stem-Leaf Nodes
        0x40-0x5F   Stem Nodes and No-op (0x48)
        0x60-0x7F   Branch and Stem-Branch Nodes
        0x80-0x9F   Path Select and Internal Ref (0x88)

## Potential Future Extensions?

### Lazy Patches

I could introduce an 'apply patch' node that applies a 'patch' to a data node.

* *apply patch* - header (0x05) is followed by offset to value, then by patch node.

Patch nodes must be carefully designed to support partial application, merging, and indexed lookup. They would largely mirror the structure of data, e.g. follow a stem then apply a patch, apply patch to each branch, split list at index then apply patches to left and right sublists (also tracking modified left sublist size). At their destination, patches would replace data, splice or trim branches, and insert or delete sublists.

Benefits of lazy patching include more efficient representation of 'deep' updates (no need to reference unmodified regions) and amortization of updates (aggregate at root, apply at heuristic threshold). Glas Object could be used as a [log-structured merge-tree](https://en.wikipedia.org/wiki/Log-structured_merge-tree). The cost is complexity.

Decision is to defer this feature. Revisit if update performance becomes major issue.

Even without patches, can amortize cost of replacing referenced globs based on heuristic thresholds (how much data is copied, how much is dropped, etc). Also, updates costs should align closely with eager in-memory updates of tries and finger-tree ropes; programmers can predict and work with this. Further, stowage annotations can provide some program control over serialization.

### Array of Structs

Support for logical transposition from array-of-structs as struct-of-arrays might be convenient for many use cases. However, I'm not convinced this would be best handled at the data layer. It might be better to support array of structs vs. struct of arrays at higher program layers.
