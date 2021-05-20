# Glas Object

## Status

The core of Glas Object is feature-complete, but it hasn't been performance-tested yet and design of escapes still needs a lot of work.

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
* escape - 000 - interpret following inline node

Glas values can be encoded using just inline branch, stem, and leaf nodes. Offsets support structure sharing, indexing, and organization within the binary. Offsets are encoded as a varnat (see below) and are always positive, thus it's impossible to directly represent a cycle.

Escaped nodes are parsed as normal data but require an extra interpretation step. For example, we might interpret stowage or module references by loading the appropriate resource. Design of escapes requires careful attention, but does not require new node types.

### Varnat Encoding

The varnat encoding favored by Glas Object uses a prefix in the msb of the first byte `1*0` to encode the total number of bytes (equal to number of bits in prefix). For example:

        0xxxxxxx
        10xxxxxx xxxxxxxx
        110xxxxx xxxxxxxx xxxxxxxx
        1110xxxx xxxxxxxx xxxxxxxx xxxxxxxx

This gives us 7 bits per byte, and preserves lexicographic order. In the interpretation for offsets or extended prefix size fields, Glas varnats start at 1. 

        00000000    1
        00000001    2
        00000010    3
        ...
        01111111    128

Although varnats are extensible, at a certain point we'll likely favor stowage references (secure hashes of binaries) instead of offsets within the binary. 

### Escapes

Escape nodes tell a Glas Object parser to interpret the following node. For extensibility, the following node should be an open variant. For efficiency, there should be low overhead for common escapes. A viable design is to use a varnat encoding for the variant prefix following the node. 

In practice, we're unlikely to ever have 128 escape codes, so escape headers will cost three bytes: one for the escape node (which might have its own prefix bits), then two for the varnat selector (at 4 bits per byte). The body of the escape code can also be reasonably compact.

Useful Escapes:

* references - stowage and modules could be combined
* annotations - pair any value with an ignored value
* failure - explicit error options
* fallback - if one data option has error, use other
* binary data - interpret bitstring as binary list
* finger tree - efficient representation of lists

We can use annotations on the top-level node to provide a header of sorts.

Possibilities:

* unboxed - as binary data, but with a type parameter
* variant - bind label to value; enables shared labels
* record - bind a list of labels to a list of values
* table - bind a list of labels to a list of record elements
* transpose - logically reorient rows and columns
* reshape - logically reshape a matrix
* patch - logically update a value, useful with stowage 
* path - logically select value, useful with modularity

We could feasibly use escapes to support arbitrary computation in Glas Object.

### Memory

Glas Object is not primarily designed for use in-memory, but it could potentially work with 2-space nursery garbage collector and a shared region for older generation data. 

The main issue is that it's awkward to have variable-width offsets when estimating memory consumption. This issue can be mitigated by specializing the in-memory representation, e.g. using overlong offset varnats or switching from offsets to absolute pointers.
