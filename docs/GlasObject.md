# Glas Object

## Status

The core of Glas Object is feature-complete, but it hasn't been performance-tested yet and standardization of escapes still needs a lot of work.

## Overview

Glas Object, or 'glob', is a data format for representing Glas data within files or binaries. Glob is intended for use together with content-addressed storage (which I call 'stowage') to support structure sharing at a larger scale. 

A naive encoding of a Glas value is a bitstream representing a tree traversal, e.g. 00 leaf, 01 right, 10 left, 11 branch left then right. However, this encoding is not good with respect to various desiderata:

* indexing - avoid scan of large structures for element lookup
* sharing - reuse common subtrees, locally and globally
* lists - support the finger-tree representation
* tables - options for column-structured data encodings 
* binaries - embedding of large and structured binary data
* annotations - mix metadata such as provenance within data
* extension - clear mechanism to introduce new features
* laziness - options for deferred procedural generation
* modules - adequate as a bootstrap language module
* memory - adequate for direct use by interpreter

Glas Object should be efficient and also support these desiderata to a reasonable degree.

## Proposed Representation

The current proposal is an 8-bit byte-aligned representation where the most common node header is represented by a `"pppppnnn"` format: five bits to encode path prefix and three bits to encode node type.

### Path Prefix

Five path prefix bits encode a sequence of zero to four non-branching edges preceding a node:

* 1zyxw - four edge path prefix `wxyz`, 
* 01zyx - three edge path prefix `xyz`
* 001zy - two edge prefix prefix `yz`
* 0001z - one edge path prefix `z`
* 00001 - no path prefix
* 00000 - extended path prefix 

Integrated path prefix bits are useful for short paths with many branches, but has 50% efficiency maximum. The extended path prefix can encode large, non-branching paths at near 100% efficiency. An extended path prefix immediately follows a node (before child nodes) and is encoded as a varnat (see below) followed by that number of bytes.

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

### Varnat Encoding

The varnat encoding favored by Glas Object uses a prefix `(1)*0` to encode the total number of bytes, reminiscent of UTF-8 but . For example:

        0xxxxxxx
        10xxxxxx xxxxxxxx
        110xxxxx xxxxxxxx xxxxxxxx
        1110xxxx xxxxxxxx xxxxxxxx xxxxxxxx

This gives us 7 data bits per byte. To ensure a lexicographic order of numbers, overlong encodings aren't permitted. Glas Object varnats are interpreted to start at 1 (we don't use zeroes).

        00000000    1
        00000001    2
        00000010    3
        ...
        01111111    128

In practice, varnats are unlikely to use more than four bytes in Glas Objects. At some point in the range above a few megabytes, use of stowage references becomes superior to using ever larger binaries.

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
