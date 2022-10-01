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

Glas data is binary trees. We could distinguish basic tree types as leaves, stems, and branches.

* *leaf* - tree node with no children. 
* *stem* - a sequence of tree nodes having exactly one child. 
* *branch* - tree node with two children. header is followed by offset to right child, then immediately by left child.

Glas Object uses stems heavily to encode bitstrings. Numbers, symbols, etc. are encoded into as bitstrings. Thus, compaction of stems is essential. Additionally, we could save some bytes (one byte per symbol, number, or branch within a radix tree) by merging stem-leaf and stem-branch.

The proposed encoding for Basic Nodes consumes 96 header types:

        ttt0 abc1 - 3 bits in header (H3)
        ttt0 ab10 - 2 bits in header (H2)
        ttt0 a100 - 1 bits in header (H1)
        ttt0 1000 - 0 bits in header (H0)

        ttt1 fnnn . (Bytes) - PBC 
            f - full (1) or partial (0) first byte  
            nnn - 1-8 byte.

        ttt0 0000 . ofnn nnnn . (Bytes or Offset) - Extended PBC 
            o - offset (1) or inline path bytes (0)
            f - full (1) or partial (0) first byte
            nn nnnn - 1-64 bytes.

        partial byte: (1 to 7 bits)
            abcdefg1 - 7 bits (msb first)
            abcdef10 - 6 bits
            ...
            a1000000 - 1 bits
            10000000 - 0 bits (unused in practice)
            00000000 - unused

        ttt values:
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

This compactly encodes symbols, numbers, composite variants, radix trees, etc. without spending bytes on expensive structure. It is feasible to share stem bits within a glob via External PBC encoding, potentially useful for compressing large, common stems. In practice, most stems should be short.

*Aside:* The H0 Stem header (0x58 - 0101 1000) doesn't encode any stem bits, and is followed by a child node. This represents the no-op for Glas Object.

### Lists

Lists are a simple data structure formed from a right-spine of pairs (branch nodes), terminating in unit value (a leaf node).

                 /\     type List = (Value * List) | ()
                a /\
                 b /\   a list of 5 elements
                  c /\      a:b:c:d:e:[]
                   d /\     [a,b,c,d,e]
                    e  ()

In Glas systems, lists are used for almost any sequential data structure - arrays, tuples, stacks, queues, deques, binaries, etc.. However, the direct representation of lists is awkward and inefficient for many use-cases. Thus, Glas systems use specialized representations under-the-hood, such as [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). These structures must be preserved for serialization.

To preserve performance in context of serialization, Glas Object will also support common optimized representations of lists. 

* *array* - header (0x0A) followed by length (varnat + 1) then by that many value offsets. The varnat encoding of offsets must be denormalized so they're all the same width. All offsets are relative to the final offset, i.e. a '0' offset would mean a value immediately following the array of offsets. Represents a list of values.
* *binary* - header (0x0B) followed by length (varnat + 1) then by that many bytes. Represents a list containing the binary data. Each byte corresponds to an 8-bit bitstring, msb to lsb. 
* *short array* - header (0xA0-0xAF) encodes a length of 1 to 16 items. Short for '0x0A (length)'. 
* *short binary* - header (0xB0-0xBF) encodes length of 1 to 16 bytes. Short for '0x0B (length)'.
* *concat* - header (0x0C) is followed by offset to left list, then by right value (usually a list). Logically concatenates left list onto right value.

        concat () y = y
        concat (x,xs) y = (x, concat xs y)

A rope structure is constructed from concat nodes and list-take nodes (to cache size info) with array or binary binary fragments (instead of individual list elements) at the leaves. A finger-tree rope is a rope structure that is balanced in such a way that access to the first and last items requires constant time. See *Encoding Finger Tree Ropes*.

### References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x02) followed by 64-byte secure hash (SHA3-512) of another glob.
* *external bin* - header (0x03) followed by 64-byte secure hash (SHA3-512) of binary value. 
* *internal ref* - header (0x88) followed by offset to later value within current glob.

Internal ref nodes are useful for structure sharing within a glob, while external refs can control memory use and support structure sharing between globs. Binary references are intended to simplify reflection and integration with external tools. References can be combined with *Accessor* nodes for fine-grained sharing.

### Accessors

Accessors support fine-grained structure sharing between globs. For example, we may define a common dictionary then use accessors to reference individual definitions.

* *follow* - headers (0x80-0x9F) use same encoding of path bits as stem nodes (except ttt = 100), followed by an offset to the target value. Represents the value reached by following the path into target.
* *drop*  - header (0x08) followed by count (varnat + 1) then by offset ot value. Equivalent to following a path of count '1' bits. 
* *take* - header (0x09) followed by count (varnat + 1) then by a right-associative sequence of pairs. Equivalent to a sublist from taking the first count items.

        take 0 _ = ()
        take n (x,xs) = (x, take (n-1) xs)

The 'path select' nodes include the internal reference (0x88) via selecting the empty path. All path select nodes use a reference to the target value, but an inline value can effectively be expressed via zero offset. 

### Annotations

Annotations can include rendering hints for projectional editing, metadata for provenance tracking, or recommend specialized runtime representation for acceleration at load-time. Their role is to support tooling. Each value in Glas Object may have a list of annotations (usually empty) by applying annotation nodes.

* *annotation* - header (0x04) is followed by offset to annotation value (the metadata), then immediately by the annotated value.

A runtime may feasibly drop these annotations or provide effectful access to read and write certain annotations. Glas Object annotations emphatically *do not* replace annotations at other layers, such as the Glas program model. 

*Security Note:* Sensitive data should be marked by annotation and paired with some random entropy bits to resist certain attacks (cf. [Tahoe's convergence secret](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html)). This would make it easier for Glas system tools and runtimes to recognize and erase sensitive data, rather than amortizing updates for performance reasons.

## Glob Headers

Metadata about a glob is supported by a simple convention of starting any glob binary with an internal reference, then encoding header immediately following the offset.

        0x88 (offset to root data) (header data) (root data)

Header data would an extensible record, i.e. `(attribute:Value, ...)`, encoded using Glas Object. These header attributes should be meaningful at the representation layer, i.e. describing properties of the glob container. In contrast, *annotations* apply to the represented values.

Some use cases for glob headers include indicating glob version, adding hints for *Amortized Update* or *Random Access* patterns (see below), and so on.

## Summary of Node Headers

        0x02        External Ref
        0x03        External Bin
        0x04        Annotation

        0x08        Drop
        0x09        Take
        0x0A        Array
        0x0B        Binary
        0x0C        Concat

        0x20-0x3F   Stem-Leaf and Leaf (0x28) 
        0x40-0x5F   Stem Nodes and No-op (0x48)
        0x60-0x7F   Stem-Branch and Branch (0x68)
        0x80-0x9F   Follow and Internal Ref (0x88)
        0xA0-0xAF   Short Arrays (length 1 to 16)
        0xB0-0xBF   Short Binaries (length 1 to 16)

        UNUSED:
        0x00-0x01
        0x05-0x09
        0x0D-0x0F
        0xC0-0xFF

## Patterns

Some ideas about how we might leverage Glas Object for more use cases.

### Amortized Updates

When we update a radix tree or finger tree, the result is a new value with some shared structure. If the original tree is represented in a separate glob, shared structure can be represented via accessor nodes. Heuristically, we can copy data that is small enough that referencing it isn't worthwhile (adjusting for what we've already copied).

If we later serialize data including the updated tree, we can make a decision to either drop the separate glob (after copying the content we need) or continue referencing it. This decision can be based on a heuristic threshold, e.g. keep the old glob if we're still referencing at least 30% of its content.

Essentially, we can heuristically defer replacement of globs until enough data is being replaced. Upon replacing a node, the same pattern will apply to the next level of dependencies. This pattern can amortize update costs at cost of some vestigial data in content-addressed storage.

### Random Access

We can construct globs that have short paths to many data nodes, e.g. the root node is an array (or dictionary) that contains references to both large values and several deeper components. We could also arrange that the array is sorted so we can efficiently perform reverse lookup (via binary search), or separately compute and cache a reverse lookup index. Using this index, we can replace long accessors by short ones, potentially saving time and space. 

This technique is unlikely to pay for its overheads in all cases. However, I expect we'll find enough cases where it is useful to explicitly support the pattern.

### Dictionary Based Communication

[Glas Channels](GlasChannels.md) will serialize messages between messages using the glob format. It is feasible for every message to reference a shared dictionary - this costs about 65 bytes per message to reference an external dictionary, then ~6 bytes for each access to the dictionary. The dictionary can be accessed upon request.

This allows messages to carry their full 'meaning' unlike most communication today that references an implicit dictionary. It also ensures the meaning is immutable, except insofar as we explicitly model references within our values.

### Encoding Finger Tree Ropes

It is feasible to combine list-take (Size) and concatenation nodes in a way that provides enough hints of finger-tree structure to support balanced operations. 

    Concat  (L1 ++ L2)
    Take    (Size . List)

    Digits(k) - up to four digits, logically concatenated
        1Dk     Dk
        2Dk     Dk ++ 1Dk
        3Dk     Dk ++ 2Dk
        4Dk     Dk ++ 3Dk

    Digit(k) (or Dk)
        k=0 # primary data!
            Array
            Binary
        k>0 # 2-3 nodes
            Size . (2Dk | 3Dk)

    FTRope(k)
        Empty   Unit
        Small   Digits(k)
        Full    Size . (Digits(k) ++ (FTRope(k+1) ++ Digits(k)))

    List 
        FTRope(0)
        Pair(elem, List)

If applied to other ropes, this won't result in a balanced finger-tree, but operations such as enqueue/dequeue/split/append will incrementally shift towards a finger-tree!

## Potential Future Extensions

### Lazy Patches

This was originally part of my Glas Object definition, so the design is close to complete. But I've decided to elide it because I'm not convinced by the performance-complexity tradeoff in general, and programmers can obtain similar performance benefits by explicitly modeling patches in the data layer.

The foundation for patches is introducing apply-patch nodes:

* *apply patch* - header is followed by target value and representation of patch

To make them efficient and useful, patches should be carefully designed with several properties:

* incremental (lazy) application. e.g. `(apply-patch (patch-branch a b) (branch x y))` can be rewritten to `(branch (apply-patch a x) (apply-patch b y))`, then we only need to consider half.
* indexable, e.g. given `(patch-branch (patch-stem stem-bits (replace y)))` we could efficiently lookup the value 'y' without observing the original data. This enables patches to serve as working memory.
* composable / mergable, e.g. `(apply-patch a (apply-patch b x))` can rewrite to `(apply-patch (merge a b) x)`, where the evaluated merge is usually smaller than the sum of its components.

To acheive these properties, we can mirror representation of indexed data, and keep enough extra information to merge patches as part of each patch. A viable model for patches on radix trees and lists: 

* *nop* - encoded similar to data leaf. Patch has no effect, though implicitly asserts that the data node is reachable. Usually erased if feasible.
* *patch stem* - encoded similar to data stem, followed by patch. Follow stem then applies patch.
* *patch branch* - encoded similar to data branch, with left and right patches. Applies patch to both branches.
* *replace* - header followed by data, replaces value at patched location.
* *trim* - header includes direction (left or right). Applies to branch. Removes branch in indicated direction, resulting in a stem.
* *splice* - header includes direction (left or right). Applies to stem. Adds branch in indicated direction, resulting in a branch. (Direction supports merging and indexing.)
* *internal ref* - header followed by offset to patch, supports structure sharing of patches within a glob
* *patch annotation* - encoded similar to data annotation, but the patch on the annotation applies a patch to the entire list of annotations. For example, we can erase all annotations on a value by replacing annotations with an empty list.
* *list split update* - header followed by final left length (varnat), initial left length (varnat), offset to right patch, then left patch. Will:
  * split list at initial left length
  * apply left and right patches respectively to left and right sublists
  * record final left list length (supports merging and indexing)
  * concat patched lists 
* *patch array* - encoded similar to data arrays, except with offsets to patches. Applies a list of patches to a list of data. (List of patches may be shorter than list of data.) 

If patches were explicitly modeled in the data layer, they would not be limited to radix trees and lists, and programmers can more precisely align patches with problem domain. It becomes impossible to directly observe external refs, but annotations for stowage and memoization would provide sufficient control to model a persistent [log-structured merge-tree](https://en.wikipedia.org/wiki/Log-structured_merge-tree). 

### Matrices

A matrix might be represented as a row-list of column-lists (or vice versa). This is acceptable for many use cases, but is a bit inefficient - it requires repeating list header overheads, and extra work to verify that every column-list has the same length.

An alternative is to represent a matrix within a single list via [row-major or column-major order](https://en.wikipedia.org/wiki/Matrix_representation). This could feasibly be supported in Glas with header such as:

* *chunkify* - header is followed by length K (varnat+1) then by a list value whose total length must be an exact multiple of K. Outcome is a logical list of lists of length K, formed by taking the first K (0..K-1), second K (K..2K-1), etc. items from the original list.
* *transpose* - header is followed by a matrix (a list of lists of constant length). Logically transposes this matrix.

However, before introducing this feature to Glas Object, it is necessary to observe how things are handled in practice. I suspect that we're mostly interested in 'unboxed' matrices - e.g. a binary value, together with some header info, representing a matrix of floating point numbers. We might choose to operate directly on the binary instead of trying to form a list of lists.

### Structs and Tables

Radix trees are great but have an obvious flaw: the labels are repeated with every value. An alternative is to apply a list-of-labels to a list-of-values. That list of labels could feasibly be shared by multiple instances. This could also be mapped to a table.

* *struct* - header is followed by an offset to a list of distinct bitstrings representing labels, then by a list of values of same length. Logically computes a record of labeled values.
* *table* - header is followed by an offset to a list of labels, then by a list of lists (a matrix). Logically applies the 'struct' header to every element of the list.

This feature is tempting. For example, between 'table' and 'transpose' we can support both [array-of-structs and struct-of-arrays](https://en.wikipedia.org/wiki/AoS_and_SoA) at the Glas Object layer. However, like matrix support this is a premature optimization. I should first observe how Glas systems handle large tables in practice. I won't be very surprised if programmers prefer to explicitly model tables using an array-of-structures.

### User Defined Codecs

It is feasible to make Glas Object extensible with arbitrary encodings. Consider:

* *codec* - header is followed by offset to value representing a codec, then immediately by the encoded value.

A codec might be represented as 'codec:(encode:Program, decode:Program, ...)', and usually referenced via content-addressed storage. A runtime could recognize certain codecs to support accelerated data representations.

However, codecs don't fit my vision for Glas Object. They aren't simple and would complicate reasoning about termination and performance. The data will not be directly indexable. That said, some working on the program model could potentially simplify reasoning and support limited indexing via lazy partial evaluation.



