# Glas Object

Glas Object, or 'glob', is a data format for representing Glas data within binaries or files. This is intended for use with stowage, and indexed for efficient lookups.

Glob represents tree nodes using less than one byte on average. The byte format is `nnoppppp` meaning we have two node-type bits `nn`, one offset bit `o`, and five path prefix bits `ppppp`. 

The path prefix bits can encode up to four bits in the 'path' leading to this node, or can indicate an extended prefix mode. The node type indicates the number of children and supports an escape option for stowage, finger-tree lists, and other structure that requires special interpretation. The offset bit determines use of indirection.

Node types: 

* 00 - leaf. This is the terminal node type. There is no following node.
* 01 - stem. This node just adds path prefix bits to the following node.
* 10 - branch. This node is followed by an offset to the left node, then the right node.
* 11 - escape. Following node has special interpretation, e.g. stowage or finger-tree.

Offset bit: 

* 0 - offset. The following node for stem or escape, or the right node of branch, is accessed indirectly via offset. 
* 1 - immmediate. The following node for stem or escape, or the right node of a branch, immediately follows the current node.

Leaf nodes use immediate mode; the leaf+offset combination, i.e. high bits `000`, is reserved for potential future extensions.

Prefix bits:

* 00001 - empty prefix
* 00010 - left
* 00011 - right
* 00100 - left left
* 00101 - right left
* 00110 - left right
* 00111 - right right
* ...
* 10111 - right right right left
* 00000 - extended binary prefix 

The extended prefix will encode a large number of prefix bits as a compact binary, but I haven't decided exact details. The basic structure of Glas is based around the leaf, stem, branch, prefix bits, and use of offsets. However, escape options are very ad-hoc and require careful standardization.



....

Some thoughts:

* require lightweight type and version header, e.g. `glob0\n`
* glob should preserve structure sharing within tree where feasible
* interning hints. When a subtree is likely to appear in many resources, we could suggest it be interned by the runtime when loaded. This is a simple annotation.
* deep references? A secure hash reference can be augmented with an offset into a resource. This would enable a stowage resource to aggregate a large number of related resources. It would also simplify partial reuse of stowage resources when constructing a tree.
* log-structured merges



 if a subtree is widely shared, but is too small for a separate stowage resource, an interning hint could be useful. This could be a lightweight hash of the value being interned.
* optimize for fast parse, fast query without 
* byte-oriented - compact encoding within limits of byte alignment
* 
* header - every Glas object should have an optional header value, a full record. This header would include the 'salt' for cryptographic uniqueness.
* 

# ........ 

My goal for Glas object is an efficient, robust, standard encoding for content-addressed storage.

 content-addressed storage.


To support distributed incremental computing and sharing of very large values, Glas specifies this Glas Object (`.glob`) representation. 

Some thoughts:
* Glas Object should be designed for [Glas Application](GlasApps.md) access and update patterns, because applications, because apps can easily produce very large structures over time.
* Binaries are useful for interaction. Whether a list is fully binary should perhaps be recorded in the secure hash references.

# MISC

Currently, this is a relatively low priority.



## Note on Unstable Hashes

Secure hashes for a value represented using Glas Object are 'unstable' in the sense that a single value may have a number of hashes. 



*Note:* Secure hashes in Glas Object are 'unstable' in the sense that a single value may have any number of hashes. This is true for many reasons: log-structured updates, finger-tree construction, heuristic node sizes, metadata, and potential [entropy added for security purposes](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html).



finger)

## Values as Path Sets

In some contexts, it is useful to view Glas values as homomorphic to a set of structured 'path' strings. In this view, the empty dictionary `()` maps to an empty set, while `(a:(x:(), y:()), b:())` might map to the set `{a/x/, a/y/, b/}`. This set may 

 Glas then supports bulk operations on the set based on shared prefixes.


...




Glas values can potentially be understood as sets of path strings. This set is represented in a manner that supports prefix sharing, implicitly forming the tree of dictionaries. Glas supports bulk data manipulations (copy, move, erasures, unions) based on common prefixes. 

The main difficulty I've had with this view is how to deal with the empty set and empty path. In particular, I want to avoid the scenario where `(a:())` is equivalent to `()`. However, if we distinguish empty sets, then it does make sense for `(a:(~))` to be equivalent to `()`.

But it's unclear it should mean for a dictionary that contains other paths to not also contain the empty path. Like, what is the result of cutting `p` from `(prefix:(), posix:())`. Is it some form of partial dictionary `(~ refix:(), osix:())` that excludes the empty path?

Or should we simply elide the empty path in all cases? If so, 




Desiderata:

* Log-structured merge tree updates, for efficient deep writes.
* Distributed structure sharing and value identification, via secure hashes. 
* Bit-level branching of nodes, to avoid problems with 'wide' dictionaries.
* Encoding of keys has a [self synchronizing](https://en.wikipedia.org/wiki/Self-synchronizing_code) property, at the bit level.
* Keys are lexicographically sorted by default.
* Keys containing numbers are sorted numerically.


, and favors file suffix `.glob`.. This representation is based on log-structured merge trees (for O(1) amortized update), radix trees (for prefix-oriented indexing and manipulations), and content-addressed Merkle trees (for modularity, distributed structure sharing, provider-independent distribution, incremental diffs and downloads). This standard representation is called [

Glas Object has specialized support for large lists/arrays based on finger-trees, building on the Glas dictionaries. This provides constant-time access to the endpoints (e.g. enqueue, dequeue) and logarithmic-time manipulations in the middle (random access, split, composition). Binaries are further specialized as rope data structures, with compact encoding of binary fragments near the leaves.

This design makes Glas Object suitable for large-scale computations, e.g. modeling large key-value databases, logs, queues. But it can still be used for small values, e.g. as an alternative to JSON or MessagePack.

Glas Object also maintains a few tag-bits for indexing purposes. 

Another valuable feature of Glas Object is that values may have tag bits for indexing purposes. The parent node will a union of these tag bits.

Thoughts: Lists should probably support ranged inserts, deletions, and reversals.


Another important feature of Glas Object is some built-in support for deferred/static computation, warnings, and errors. Within the Glas Object, deferred computation and errors are indexed, allowing for efficient search and processing.


Finally, Glas Objects will support deferred compuation, using a simple homoiconic representation for computations (see below!). Deferred computation is indexed for efficient processing. 

This is indexed, allowing efficient filtering discovery of elements may require further evaluation, and enabling programmers to quickly discover








 This allows for flexible templating and abstraction.

Ultimately, Glas Object is suitable for modeling very large key-value databases, logs and queues and streams, and filesystem-like structures. Further, it can do so on a distributed system scale, with incremental upload and download via content-addressed references.





via Glas dictionaries, thus supports constant-time manipulations of the ends and logarithmic-time manipulations of the middle.

The intention is that Glas Object can scale from minor use cases up to massive databases or filesystems. 




 using a representation based on finger-trees, plus ropes for binary sequences. This supports O(lg(N)) random access and manipulations, with O(1) near head or tail.



l
For uniformity, Glas encodes terminal values such as `42`, `[1,2,3]`, and `True` as dictionaries. Standard encodings will be detailed later. The only terminal value in Glas is the empty dictionary, `()`.

Glas will standardize a representation for serializing, sharing, and efficiently manipulating its dictionary values at a large scale, called [Glas object](GlasObject.md). Glas object uses concepts from Merkle trees and log-structured merge trees to support structure sharing, provider-independent distribution, and incremental updates.

Because Glas values are dictionaries, they are relatively easy to extend with new labels, though this requires writing computations in a row-polymorphic style.

## Note: Unstable Hashes

Glas Object does not attempt to ensure stable hashes for values. In particular, Glas supports log-structured updates and heuristic partitioning of nodes below the level of 

 partitioning of nodes

The [Unison project](https://www.unisonweb.org/docs/tour) focuses on identifying objects by secure hash, with a projectional editor. 


https://www.unisonweb.org/docs/tour
 favors a more conventional file-based module system.

Glas Object is primarily intended for back-end systems, not for direct human manipulations. 

A consequence of this design is that secure hashes are not directly aligned with Glas values, nor is there any effort to ensure a deterministic hash for a value.

