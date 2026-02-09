# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and the phased liquid-to-solid creation analogous to staged metaprogramming. It can also be read as a backronym for 'general language system', which is something glas aspires to be. Design goals orient around compositionality, extensibility, scalability, live coding, staged metaprogramming, and distributed systems programming. 

Interaction with the glas system is initially through a [command line interface](GlasCLI.md).

## Data

The plain old data type in glas is the immutable binary tree. Trees are very convenient for modeling structured data without pointers. The runtime representation is assumed to compact non-branching sequences like a [radix trees](https://en.wikipedia.org/wiki/Radix_tree): 

        type Tree = (Stem * Node)       # as struct
        type Stem = uint64              # encodes 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     # branch point
            | Stem64 of uint64 * Node   # all 64 bits

        Stem (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

A dictionary such as `(height:180, weight:100)` is directly represented as a radix tree. Labels are encoded in UTF-8, separated from data by a NULL byte. A labeled variant (aka tagged union) is encoded as a singleton dictionary with a choice of labels. The convention in glas systems is to favor labeled data for anything more sophisticated than integers and lists. 

### Integers

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

### Lists

Lists are logically encoded as simple `(head, tail)` pairs (branch nodes), terminating in a leaf node `()`. 

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()

For performance, glas systems will optimize encodings of lists, and especially of binaries (lists of 0..255). The favored representation for large lists is [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29).

        type Node = 
            | ...
            | Arr of Array<Tree>
            | Bin of Binary
            | Concat of LeftLen * LeftNode * RightNode

This is an example of accelerated data representations. Instead of a program primitive to concatenate two lists, we annotate a reference implementation with `(%an.accel %accel.list.concat)`, then the runtime substitutes a built-in that uses Concat nodes.

### Optional Data and Booleans

Optional data is encoded as a list of zero or one items. Booleans are encoded as an optional unit value.

### Data Abstraction

The glas runtime may introduce special nodes to enforce data abstraction:

        type Node = 
            | ...
            | Sealed of Key * Tree

Use of data abstraction is guided by program annotations, e.g. `a:((%an.data.seal Key), %pass)` could seal the top value on the data stack, and a corresponding `%an.data.unseal` is necessary to view the data again. Otherwise, we raise a runtime type error. Ideally, a compiler will eliminate unnecessary seal and unseal operations to reduce the number of allocations.

## Behavior

The glas CLI provides built-in front-end compilers for [".glas"](GlasLang.md) and [".glob"](GlasObject.md) formats. Initially, we compile a user configuration to a [namespace AST](GlasNamespaces.md) representing a module. This module is linked against [primitive program constructors](GlasProg.md), such as '%cond' for conditional behavior. To support modularity, the linker provides '%macro' and '%load'.

The configuration defines runtime options under 'glas.\*' and an environment under 'env.\*'. The latter is fed back into the configuration as '%env.\*'. Shared libraries and applications are linked through '%env.\*'. By convention, '%\*' names are propagated transitively across all modules, thus serves as a pseudo-global namespace. The configured environment becomes the basis for sharing instead of the filesystem. Performance depends on lazy loading and caching.

Modules support inheritance and override similar to OOP. Typically, a small, local user configuration will inherit from a much larger community or company configuration in DVCS then override a few definitions as needed. Inheritance is also supported at the application layer.

Applications are modeled as namespace objects that define a transactional 'step' method. Instead of a long-running 'main' procedure, the 'step' method is called repeatedly in separate transactions. This greatly simplifies live coding and distribution, but it requires sophisticated optimizations such as incremental computing and concurrency on non-deterministic choice. Aside from 'step' the runtime can directly support 'http' or 'rpc' events. See [applications](GlasApps.md).

User-defined syntax is supported by conventions. When importing a module, we select the front-end compiler based on file extension, '%env.lang.FileExt'. If a configuration defines 'env.lang.glas' or 'env.lang.glob', we'll attempt a bootstrap.

## Annotations

The namespace AST supports annotation nodes. The runtime provides annotation constructors supporting a variety of features, such as `(%an.log Chan Message)` for logging, `%an.lazy.spark` for parallel evaluation of thunks, or `(%an.arity 1 2)` to control a subprogram's access to the data stack. 

Annotations are useful for validation, instrumentation, debugging, performance. But they must not affect semantics, modulo use of reflection APIs. It must be safe to ignore unrecognized annotations.

Performance of glas systems depends heavily on annotation-guided acceleration, where a reference function is replaced by a built-in. Acceleration provides performance primitives while preserving simple semantics.
