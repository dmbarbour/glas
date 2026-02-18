
# Glas Data

The plain old data type in glas is the immutable binary tree. Trees are very convenient for modeling structured data without pointers. The runtime representation is assumed to compact non-branching sequences like a [radix trees](https://en.wikipedia.org/wiki/Radix_tree): 

        type Data = (Stem * Node)       # as struct
        type Stem = uint64              # encodes 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Data * Data     # branch point
            | Stem64 of uint64 * Node   # all 64 bits

        Stem (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

A dictionary such as `(height:180, weight:100)` is directly represented as a radix tree. Labels are encoded in UTF-8, separated from data by a NULL byte. A labeled variant (aka tagged union) is just a singleton dictionary with a choice of labels.

The convention in glas systems is to favor labeled data for anything more sophisticated than integers and lists.

## Integers

Integers are encoded as variable-length bitstrings, msb to lsb. Negative integers use ones' complement.

        Integer  Bitstring
         4       100
         3        11
         2        10
         1         1
         0               // no bits
        -1         0
        -2        01
        -3        00
        -4       011

The zero value is 'punned' with the empty list or dict. It's a convenient initializer.

## Lists, Arrays, Queues

Lists are logically encoded as simple `(head, tail)` pairs (branch nodes), terminating in a leaf node `()`. 

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()

There are many patterns for using lists and list-like structures (arrays, queues, etc.), but the direct encoding is inefficient for most of them. For performance, glas systems shall heavily optimize lists. Especially binaries - lists of small integers (0..255). A convenient representation for most use cases is [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). 

        type Node = 
            | ...
            | Arr of Array<Tree>
            | Bin of Binary
            | Concat of LeftLen * LeftNode * RightNode

To support list optimizations, the glas runtime can extend its internal data representation and implement a few built-in functions for list indexing, slicing, concatenation, etc.. These built-ins are integrated via annotation-guided acceleration, substituting a reference implementation.

## Optional Data and Booleans

Optional data is encoded as a list of zero or one items. This supports a smooth transition between options and lists.

## Booleans

Booleans are encoded as an optional unit value. This supports a smooth transition between 'blind' booleans and those that constructively detail how something is true like options.

## Extended Numbers

A proposed encoding for rational numbers is a dict of form `(n:Integer, d:Integer)`, corresponding to numerator and denominator. Most operations on rationals should reduce to normal form. 

The glas system shall favor exact arithmetic by default, with explicit rounding steps as needed. One motive for this is ensuring portable, deterministic arithmetic. Floating point has troublesome variation across hardware. There is a performance hit for exact rational numbers, of course. But I imagine that, for high-performance computing, we'll ultimately rely on acceleration of an abstract CPU or GPGPU.

Complex and hypercomplex numbers may similarly be supported, e.g. with `(r, i)` or `(r, i, j, k)` dictionaries of rational numbers. Vectors can be modeled as a list of numbers, and matrices as a list of vectors.

This design is intended to support ad hoc polymorphic operators that work on mixed types such as `Integer | Rational | Complex | Vector`, distinguished based on structure instead of variant label.

## Data Abstraction

The glas runtime may introduce special nodes to enforce data abstraction:

        type Node = 
            | ...
            | Sealed of Key * Tree

Use of data abstraction is guided by program annotations, e.g. `a:((%an.data.seal Key), %pass)` could seal the top value on the data stack, and a corresponding `%an.data.unseal` is necessary to view the data again. Otherwise, we raise a runtime type error. Ideally, a compiler will eliminate unnecessary seal and unseal operations to reduce the number of allocations.

## Content-Addressed Storage

With guidance from annotations, we can offload large subtrees from RAM to disk or other cheaper storage. The [glas object](GlasObject.md) serialization format is designed for this role.

