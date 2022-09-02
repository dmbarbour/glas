# Glas Object

Glas Object, or 'glob', is a compact, indexed binary representation for tree-structured data. Primary use cases for Glas Object are data storage, communication, and caching. The focus is representation of dictionaries (radix trees) and lists (arrays, binaries, finger tree ropes), and structure sharing. 

Structure sharing is supported within a glob via offsets and between globs via content-addressed references and accessors. We can reference globs by their secure hash (SHA3-512), and follow a path to a component item.

Although Glas Object is designed for Glas, it should be a very good representation for acyclic structured data in general. 

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

        ttt1 fnnn . (Bytes) - PBC 
            f - full (1) or partial (0) first byte  
            nnn - 1-8 byte.

        ttt0 0000 . ofnn nnnn . (Bytes or Offset) - Extended PBC 
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

*Aside:* The H0 Stem header (0x58 - 0101 1000) doesn't encode any stem bits, and is followed by a child node. This represents the no-op for Glas Object.

### References

Glas Object supports internal references within a glob file, and external references between glob files.

* *external ref* - header (0x03) followed by 64-byte secure hash (SHA3-512) of another glob.
* *internal ref* - header (0x88) followed by offset to later value within current glob.

Internal ref nodes are useful for structure sharing within a glob, and external refs can control memory use and support structure sharing between globs. External refs must be combined with *Accessors* to support fine-grained structure sharing, i.e. sharing component values between globs.

*Note:* I originally wanted to support an offset into external refs, but that greatly complicates precise GC analysis and other tooling. Instead, we can support a pattern for random access if we want one.

### Accessors

Accessors support fine-grained structure sharing between globs. For example, we may define a common dictionary then use accessors to reference individual definitions.

* *path select* - headers use same encoding of path bits as stem nodes (ttt = 100), followed by an offset to the target value. Represents the value reached by following the path into target.
* *list drop* - header (0x08) followed by count (varnat + 1) then by offset to list-like value. Represents value reached by following path of count '1' bits.
* *list take* - header (0x09) followed by count (varnat + 1) then by immediate list-like value. Represents value after replacing the node immediately after count '1' bits with a leaf node (unit). In practice, mostly used to cache information about list length.

The 'path select' nodes include 'internal reference' (node 0x88 - select an empty path). All path select nodes use a reference to the target value, but an inline value can effectively be expressed via zero offset.

The 'list-like' values for the two list accessors only require a contiguous right spine of given length. But these accessors will be much more efficient if applied to an indexed list (see *Lists* below). 

### Annotations

Annotations can include rendering hints for projectional editing, metadata for provenance tracking, or recommend specialized runtime representation for acceleration at load-time. Their role is to support tooling. Each value in Glas Object may have a list of annotations (usually empty) by applying annotation nodes.

* *annotation* - header (0x04) is followed by offset to annotation value (the metadata), then by the annotated value.

A runtime may feasibly drop these annotations or provide effectful access to read and write certain annotations. Glas Object annotations emphatically *do not* replace annotations at other layers, such as the Glas program model. 

*Security Note:* Security or privacy sensitive data should paired with some random bits to resist guesswork (cf. [Tahoe's convergence secret](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html)). Further, it should be marked as sensitive via annotation so runtimes and tools know to aggressively expunge that data and avoid sharing it as part of a larger glob that should is logically unreachable due to use of accessors.

### Lists

Lists are a simple data structure formed from a right-spine of pairs terminating in unit (leaf). 

                 /\     type List = (Value * List) | ()
                a /\
                 b /\   a list of 5 elements
                  c /\      a:b:c:d:e:[]
                   d /\     [a,b,c,d,e]
                    e  ()

In Glas systems, lists are used for almost any sequential data structure - arrays, tuples, stacks, queues, deques, binaries, etc.. Sparse lists, where elements are optional, are also useful in some contexts. However, direct representation of lists is awkward and inefficient for most use-cases. Thus, Glas systems will often use specialized representations under-the-hood, such as arrays or[finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

Glas Object provides a few specialized nodes to support serialization of indexed lists (or any sufficiently list-like structure). Binary data is explicitly supported because it's the most common data type for interfacing between Glas systems and other systems.

* *array* - header (0x0A) followed by length (varnat + 1) then by that many value offsets. The varnat encoding of the offsets is denormalized so they're all the same width. All offsets are relative to just after the last one. Represents a list of values.
* *binary* - header (0x0B) followed by length (varnat + 1) then by that many bytes. Represents a list containing the binary data. Each byte corresponds to an 8-bit bitstring, msb to lsb. 
* *short array* - header (0xA0-0xAF) encodes length, 1 to 16 items, otherwise as array.
* *short binary* - header (0xB0-0xBF) encodes length, 1 to 16 items, otherwise as binary.
* *concat* - header (0x0C) followed by offset to list-like value, then immediately by the remainder value. A list-like value has a right spine that terminates in a leaf node. Logically equivalent to replacing that leaf with the remainder value.

        concat (A,AS) B = (A,concat AS B)
        concat () B = B

Indexing of concat nodes relies on inserting some 'list take' nodes to cache size information, controlling the number of nodes we must examine to compute length.

*Note:* One idea is to extend concat for 3 to 5 items (i.e. 0x0D-0x0F). This might align nicely with nodes for balancing of ropes. But it's also unnecessary - we can support such nodes in-memory even with just regular 'concat', and saving a few bytes on concat is negligible for space savings.

## Summary of Node Headers

        0x03        External Ref
        0x04        Annotation

        0x08        List Drop
        0x09        List Take
        0x0A        Array
        0x0B        Binary
        0x0C        Concat

        0x20-0x3F   Stem-Leaf and Leaf (0x28) 
        0x40-0x5F   Stem Nodes and No-op (0x48)
        0x60-0x7F   Stem-Branch and Branch (0x68)
        0x80-0x9F   Path Select and Internal Ref (0x88)
        0xA0-0xAF   Short Arrays (lengths 1 to 16)
        0xB0-0xBF   Short Binaries (lengths 1 to 16)

        UNUSED:
        0x00-0x02
        0x05-0x07
        0x0D-0x0F
        0xC0-0xFF

## Patterns

Some ideas about how we might leverage Glas Object for more use cases.

### Amortized Update

When we update a radix tree or finger tree, the result is a new value with some shared structure. If the original tree is represented in a separate glob, shared structure can be represented via accessor nodes. Heuristically, we can copy data that is small enough that referencing it isn't worthwhile (adjusting for what we've already copied).

If we later serialize data including the updated tree, we can make a decision to either drop the separate glob (after copying the content we need) or continue referencing it. This decision can be based on a heuristic threshold, e.g. keep the old glob if we're still referencing at least 30% of its content.

Essentially, we can heuristically defer replacement of globs until enough data is being replaced. And upon replacing a node, the same pattern will apply to the next level of dependencies. This pattern can amortize update costs, keeping most updates close to the 'root' of a glob dependency graph, vaguely similar to a [log-structured merge-tree](https://en.wikipedia.org/wiki/Log-structured_merge-tree) or a generational garbage collector.

### Random Access

We can construct globs that have short paths to many data nodes, e.g. the root node is an array (or dictionary) that contains references to both large values and several deeper components. We could also arrange that the array is sorted so we can efficiently perform reverse lookup (via binary search), or separately compute and cache a reverse lookup index. Using this index, we can replace long accessors by short ones, potentially saving time and space. 

This technique is unlikely to pay for its overheads in all cases. However, I expect we'll find enough cases where it is useful to explicitly support the pattern.

### Dictionary Based Communication

We can serialize messages between machines as globs. Referencing a common dictionary with every message is quite affordable, i.e. 65 bytes for the external ref plus ~6 bytes per dictionary word. The dictionary would be available upon request.

This allows messages to carry their full 'meaning' unlike most communication today that references an implicit dictionary. It also ensures the meaning is immutable, except insofar as we explicitly model references within our values.

## Potential Future Extensions

### Lazy Patches

This was originally part of my Glas Object definition, so the design is almost complete. But I've decided to elide it for now because I'm not convinced by the performance-complexity tradeoff. May reconsider depending on experimental evidence of benefits.

The core idea is to introduce an 'apply patch' node that applies a 'patch' to a data node.

* *apply patch* - header (0x05) is followed by offset to target value, then by patch node.

Patches should be carefully designed with several properties:

* incremental (lazy) application. e.g. `(apply-patch (patch-branch a b) (branch x y))` can be rewritten to `(branch (apply-patch a x) (apply-patch b y))`.
* indexable, e.g. given `(patch-branch (patch-stem stem-bits (replace y)))` we could efficiently lookup the value 'y' without observing the original data. This enables patches to serve as working memory.
* composable, e.g. `(apply-patch a (apply-patch b x))` can rewrite to `(apply-patch (merged a b) x)`, where the merged patch combines the index and is usually smaller than the sum of separate patches. 

To acheive these properties, we can mirror representation of data, and cache some information that would otherwise be derived by peeking at data. A viable model for patches:

* *nop* - encoded similar to data leaf. Patch has no effect, though implicitly asserts that the data node is reachable. Will usually be erased if feasible.
* *stem* - encoded similar to data stem, followed by patch. Follow stem then applies patch.
* *branch* - encoded similar to data branch, with left and right patches. Applies patch to both branches.
* *replace* - header followed by data, replaces value at patched location.
* *trim* - header includes direction (left or right). Applies to branch. Removes branch in indicated direction, resulting in a stem.
* *splice* - header includes direction (left or right). Applies to stem. Adds branch in indicated direction, resulting in a branch. (Direction supports merging and indexing.)
* *internal ref* - header followed by offset to patch, supports structure sharing of patches within a glob
* *annotation* - encoded similar to data annotation, but the patch on the annotation applies a patch to the entire list of annotations. For example, we can erase all annotations on a value by replacing annotations with an empty list.
* *list split update* - header followed by final left length (varnat), initial left length (varnat), offset to right patch, then left patch. Will:
  * split list at initial left length
  * apply left and right patches respectively to left and right sublists
  * record final left list length (supports merging and indexing)
  * concat patched lists 
* *array* - encoded similar to data array, except with offsets to patches. Applies a list of patches to a list of data. (List of patches may be shorter than list of data.) 

For 'deep' updates, patches are more compact than structure-sharing updates because patches don't need to represent or reference unmodified components. Patches also support amortized update directly at the data layer, whereas we depend on glob replacement heuristics for similar benefits at the content-addressed storage layer. I'm not convinced these performance benefits are worthy of the added complexity costs.
