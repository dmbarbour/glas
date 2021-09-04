# Glas Object

## Overview

Glas Object, aka 'glob', will be a standard data format for representing Glas data in files, binaries, and stream processing. Glob will also support content-addressed storage for structure sharing at large scales. 

Desiderata:

* indexing - avoid scan of large structures for components
* sharing - reuse common subtrees, locally and globally
* streaming - monotonically grow partial lists and trees
* lists - use finger-tree representation for large lists 
* binaries - efficient embedding of binary data and texts
* extension - clear mechanism to introduce new features

## Design Observations

### Streaming is Partial Data

The basic requirements for streaming data are monotonicity and forward-only references. This ensures we can process partial data as it becomes available, and also forget data that has been received and processed. For Glas Object, we must also prevent representation of cyclic data. 

A viable approach to streaming is to model the top-level of Glas Object as a sequence of definitions where each definition fills a named hole that is referenced in prior definitions. Unlike conventional dictionaries, we immediately forget the definition and recycle the name for future use. 

*Note:* Forward references can support limited structure sharing within a group of updates, but reuse of values will depend more heavily on content-addressed storage.

### Indexing of Streams

Holes are not filled in any particular order, and we cannot provide an immediate offset to where the hole is filled. However, it is feasible to independently index definitions within the Glas Object binary so we aren't repeatedly scanning definitions. Optionally, each definition header could also include a size field so we can efficiently skip to the next header.

### Naming of Holes

It is reasonable to name holes by simple natural numbers (0, 1, 2, ...). The Glob stream can specify hole 0 as the top-level value. In case of content-addressed storage, a referenced Glob binary might have its own holes. These must be incremented to an unused part of the namespace. 

*Aside:* Use of content-addressed storage with holes can essentially model templated data.

## Varnat Encoding

The varnat encoding used within Glas Object uses a unary prefix `(1)*0` to encode the total number of bytes, followed by 7 data bits per byte. For example:

        0xxxxxxx
        10xxxxxx xxxxxxxx
        110xxxxx xxxxxxxx xxxxxxxx
        1110xxxx xxxxxxxx xxxxxxxx xxxxxxxx

Numbers will normally use the shortest encoding possible. 

These numbers can identify holes, represent sizes, etc.

## Data Representation

The current proposal is a byte-aligned representation where the common node header is represented by a `"pppppnnn"` format: five bits to encode path prefix and three bits to encode node type.

### Path Prefix

Five path prefix bits encode a sequence of zero to four non-branching edges preceding the node. 

* 1abcd - four edge path prefix 
* 01abc - three edge path prefix
* 001ab - two edge prefix prefix
* 0001a - one edge path prefix 
* 00001 - no path prefix
* 00000 - extended path prefix 

Integrated path prefix bits are useful for short paths with many branches, but is inefficient for large bitstrings. The extended path prefix is suitable for long non-branching bitstrings. 

An extended path prefix immediately follows a node (before child nodes) and is encoded as a varnat (see below) followed by that number of bytes.

Glas Object can support binary data by including escape nodes (see node types) to interpret large bitstrings as binary lists or other unboxed structures. Escape nodes can also potentially support reuse of common prefixes, e.g. interpreting a record trie from a paired list of labels and list of values.

### Node Types

Three node type bits determine how the elements following a node are interpreted:

* branch - 1bb - lower two bits are inline vs. offset for children:
 * 00 - offset left then offset right
 * 01 - offset left then inline right
 * 10 - offset right then inline left (offset before inline!)
 * 11 - inline left then inline right (left tree should be small!)
* stem - 01b - lower bit is inline (1) vs. offset (0) for child.
* leaf - 001 - terminates a tree.
* escape - 000 - varnat operator then inline node.

Glas values can be encoded using just inline branch, stem, and leaf nodes. Offsets support structure sharing, indexing, and organization within the binary. Offsets are encoded as a varnat (see below) and are always positive, thus it's impossible to directly represent a cycle.

Escaped nodes are always parsed the same way: a varnat operator directly followed by the inline argument node. The operator indicates how to interpret the argument. For example, an escape might interpret an argument as a stowage or module reference, logically replacing the escape node by the referent's value.

### Escapes

Escape nodes are how Glas Object supports stowage, finger trees, binary embeddings via interpreting extended path prefix, and extension with new features. Escapes should be carefully designed and standardized, but it's still feasible to parse an escape node even if the operator is not recognized.

Useful Escapes:

* annotations - pair value with notes or comments.
* references - stowage, modules, perhaps futures.
* binary data - interpret bitstring as binary list
* finger tree - efficient representation of lists

Glas Object does not have a 'header', but annotation of a top-level node can easily serve that role.

Possibilities:

* label - bind labels to values, e.g. share a record's symbol table
* unbox - binary data with a type parameter
* matrix - logically transpose or reshape structures
* patch - logically update values, useful with references
* select - extract one value from another with simple queries
* failure and fallback - if first option has error, try another

Glas Object is intended to be simple. Integrating too many dynamic features into a data model will easily grow out of control, so it's best to defer standardization of new features until we have a strong use case and clear implementation.

### Memory

Glas Object is not primarily designed for use in-memory, but it could potentially work as an in-memory representation. I think the main issue would be that variable-width and unidirectional offsets are awkward in context of a migrating garbage collector and deferred computation of escapes. We could specialize the in-memory representation to use pointers instead, so it isn't Glas Object but has a simple translation to Glas Object.
