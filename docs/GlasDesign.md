# Glas Design

Glas was named in allusion to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. There is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible through staged computing and definition of 'glas-cli-\*' modules. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Modules are represented by files and folders. Every valid module compiles to a glas value (see *Data*). Program modules usually compile to a value that represents a [namespace](ExtensibleNamespaces.md), but data modules are also useful.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will first compile a global module named language-ext, which must define a compilation function, then apply this compilation function to the file binary. Composition of file extensions is supported. A gzip-compressed JSON file with a macro preprocessor might use file extension ".json.m4.gz". In this case, we apply language-gz, then language-m4, then language-json. Conversely, if file extensions are elided, the compiled value is simply the file binary. To bootstrap this system, a compiler for ".g" files is built-in (see *Programs*).

A folder must contain a 'public' module, represented by a file. The compiled value of a folder is the compiled value of its public module. A folder behaves as a boundary for dependencies: compilation functions only reference the public value of a folder, thus other files and subfolders are effectively private. A folder also serves as a container for local modules, test programs, and utility content such as a readme, license, or manifest.

Global modules are represented by folders, and are organized into *distributions*. In context of glas systems, a distribution is a set of global modules that are versioned and maintained together, and can be defined in terms of inheritance from other distributions. The glas command line interface supports configuration of the distribution.

## Data

Glas represents data using finite, immutable binary trees. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are simpler than graphs for incremental construction or reasoning about termination. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)` or either type `(Left a | Right b)`. However, glas systems favor labeled data because labels are more meaningful and extensible. Labels are encoded into a *path* through a tree, favoring null-terminated UTF-8. For example, label 'data' would be encoded into the path `01100100 01100001 01110100 01100001 00000000` where '0' and '1' respectively represent following the left or right branch. A record such as `(height:180, weight:200)` may have many such paths with shared prefixes, forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). A variant would have exactly one label.

To efficiently represent labeled data, non-branching paths are compactly encoded by the glas runtime system or [glas object serialization format](GlasObject.md). A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encoding 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     // branch point
            | Stem64 of uint64 * Node   // all 64 bits

        Stem Encoding
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits

It is feasible to could further extend Node with specialized variants to support efficient binary data, list processing, records as structs instead of radix trees, etc.. The above offers a reasonable starting point, but the intention is that data in practice should be represented much more efficiently than the naive encoding of binary trees.

### Integers

Integers in glas systems are encoded as variable length bitstrings, msb to lsb, with negatives in one's complement:

        Integer  Bitstring
         4       100
         3        11
         2        10
         1         1
         0               // empty   
        -1         0
        -2        01
        -3        00
        -4       011

### Lists        

Sequential structure is usually encoded as a list. A list is represented as a binary tree where the left nodes are elements and the right nodes form the spine of the tree, terminating with a leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access or double-ended queues or binaries. Binary data is represented as a list of small integers (0..255). To enable lists to serve most sequential data roles, lists will often be represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the earlier 'Node' type with logical concatenation and array or binary fragments. Then we rely on a few built-ins or accelerated functions to slice, append, and index lists.

### Unordered Collections

To support unordered data types - such as finite sets, bags, or graphs - we must define a canonical representation as a binary tree. For example, a finite set can be modeled as a trie where every bitstring is a prefix-unique encoding of a binary tree. It doesn't need to be an efficient canonical representation because we'll avoid constructing this representation at runtime, instead relying on accelerated operations to construct or query the set and typeful abstraction to resist accidents.

### Large Values

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to disk. This optimization is transparent to most functions, but can be guided by program annotations and is potentially visible through a reflection API. Content-addressed references simplify memoization, communication, and persistent storage of large structures. The proposed representation is [glas object (glob)](GlasObject.md) binaries. 

## Applications and Programs

A program is a value with a known interpretation. An application is a program with a known integration. In this context, the relevant knowledge must be embedded in the glas command line interface executable. I'm still exploring various design aspects - see [glas applications](GlasApps.md). But the general design direction of glas application model is:

* *Applications as objects.* Apps may declare state and define methods, and they provide interfaces for integration.
* *Incremental definition.* Apps can be defined in terms of tweaks to existing apps. Can abstract tweaks as mixins.
* *Transactional operation.* Transactions may involve many methods and apps. Many effects are deferred until commit.
* *Transaction loops.* Repeating transactions *optimize* into incremental computing, reactivity, and concurrency.
* *Live coding.* Application code can be maintained at runtime by leveraging transactions and ephemeral references.
* *Distributed computing.* Applications can model and maintain robust, resilient, distributed network overlays.

The first points take inspiration from [object-oriented programming](https://en.wikipedia.org/wiki/Object-oriented_programming), but first-class objects aren't implied. Any object references are *ephemeral* - scoped to a transaction - to simplify live coding and distributed computing. Transactional operation and distributed computing also have a significant effect on effects APIs and state models.

I'm still exploring how to express methods. Procedural expression of behavior is an awkward fit for incremental computing of distributed transactions, e.g. requiring careful attention to reentrancy. This can be mitigated, but I'm exploring alternative paradigms. 

## Language Modules

Language modules follow a simple naming convention: the global module 'language-xyz' must describe how to compile files with extension ".xyz". This module should define application 'app' that defines `compile : SourceCode -> ModuleValue`, performing compilation as a single transactional step. 

To ensure a deterministic, reproducible outcome and support predictable refactoring, the compiler has limited access to effects. Available effects:

* load compiled module values from module system
  * *f:String* - local file or folder, extensions elided
  * *g:String* - global module name, via runtime config
* log output for a human observer

Loading a module may fail if the module isn't defined or if compilation of it fails. The exact reason for error might be logged but wouldn't be visible to the compiler. Dependency cycles or a failed download instead cause load to logically diverge. 

We might eventually develop naming conventions to support REPLs, linters, syntax highlighting support, intellisense, decompilers, [language server protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol), interactive tutorials, etc.. However, it might prove more convenient to separate these into separate modules, e.g. 'langrepl-xyz'. 

## Automated Testing

As a simple convention, local modules whose names start with "test-" will be interpreted as tests. Tests may be compiled and evaluated even when they aren't required by a folder's 'public' definition. It is feasible to run all tests in a distribution as a health check.

The test module should define an application 'app' that defines a 'run' method. Tests have limited access to effects:

* log and load - same as language modules
* fork - non-deterministic choice to support fuzz testing or multiple tests

The idea is that we're searching for failing tests. Based on analysis and feedback, a sufficiently smart test system might focus on some forks and mostly ignore others.

*Note:* Of course, our applications may also include tests, such as the built-in-tests for consistency.

## Performance

### Acceleration

If a function is giving us poor performance, it is feasible to replace that function with a compiler built-in or hardware. However, it is useful to maintain the reference definition for analysis, debugging, and because it allows us we can treat this replacement as an 'optimization' instead of a semantic extension to the language.

Acceleration can influence under-the-hood representations of data. For example, large lists and binaries might be encoded as finger-tree ropes to support efficient slices, concatenation, and indexing. In these cases, it may be useful to treat the data as abstract to avoid accidental conversion between representations.

It is feasible to accelerate simulation of an abstract CPU or GPGPU. This simulation would be restricted to a memory-safe subset of behaviors, perhaps checking the code before 'running' it. An accelerated implementation would compile the code argument for a physical CPU or GPGPU target. When the code argument is static, this compilation can be performed ahead of time.

To resist silent performance degradation, annotations requesting an unrecognized or unsupported accelerator should be reported at compile time. We can indicate 'optional' acceleration in the annotation. Ideally, accelerators will also be verified through automatic testing. 

### Stowage

Glas systems will use content-addressed storage (addressed by secure hash) to manage larger-than-memory data. Stowage can be viewed as a variation on virtual memory paging, i.e. large subtrees can be heuristically moved to and from local memory to a higher-latency. Content addressing simplifies compression, memoization, incremental computing, and content delivery networks. In context of glas systems, stowage would be almost invisible modulo runtime reflection and use of annotations to guide stowage.

### Memoization

It should be feasible to annotate at least purely functional subprograms for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. Persistent or even shared (via trusted proxy) memoization tables are feasible. Stowage simplifies memoization with very large inputs and outputs.

*Note:* Memoization on lists requires special attention to leverage the tree structure under the hood. Generically, we could feasibly accelerate conversion of lists to something like [prolly trees](https://docs.dolthub.com/architecture/storage-engine/prolly-tree) for stable chunking, then memoize map-reduce on the tree.

### Content Distribution

Networked glas systems can potentially support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve network performance in context of *Stowage*. Content on an untrusted network could be encrypted and decrypted based on the configuration and hashes.

### Compression Pass

When compiling glas programs, a useful optimization pass is to identify common subprograms and translate those to reusable function calls. This pass may be guided by annotations and is not necessarily aligned with user defined functions. Usefully, compression may occur after partial evaluation and other specialization passes. 

## Reflection

A runtime can directly provide a reflection API as part of the application effects API. This would enable an application to review its resource consumption, performance profile, or debug outputs then adjust behavior appropriately. With sufficient runtime support, an application might also be able to update application code at runtime, e.g. requesting reload from source to support live coding.

Additionally, a runtime could support annotations that provide limited access to a reflection API, e.g. the 'refl' annotation described earlier. This would also be capable of debug outputs and performance tuning. But annotations must not directly affect formal behavior of a program. At most, annotations could halt a problematic program early or provide useful hooks for the effectful reflection API.

## Thoughts

### Type Checking

The initial language will support annotations, and may develop a dedicated syntax for type annotations. I hope to eventually include typecheck tools that are applied automatically. But it's a low priority for now.

### Useful Languages

Aside from the initial language, we might want specialized languages for:

* lightweight embedded data, tables, databases as modules, etc.
* generic text preprocessing, text-layer macros, e.g. ".json.m4"
* graphical programming, perhaps based on a database format

### Abstract and Linear Data

Abstraction is a property of a subprogram. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. Substructural types, such as linear types, extend this by also restricting copy and drop operations except via provided functions.

It is feasible to annotate subprograms with linear types and leverage this to support in-place updates and reduce garbage collection. However, this optimization is difficult to achieve in context of backtracking conditionals or debugger views. The more general motive for linear types is to typefully enforce protocols, such as closing a channel when done.

### Databases as Modules

It is feasible to design language modules that parse binary database formats (LMDB, MessagePack, [Glas Object](GlasObject.md), MySQL or SQLite files, etc.). Doing so should, in theory, simplify development of visual or graphical programming environment. My vision for glas systems is that code should be a flexible mix of text and structured input, using diagrams (boxes and wires, Kripke state machines, etc.) where convenient.

Incremental compilation over large databases is feasible via memoization or partitioning into multiple files. But I think it would be better if we favor support small, composable, modular databases for most use cases.

### Extensible IDEs

Glas uses a pattern of naming modules to extend the glas system, e.g. 'language-ext' or 'glas-cli-opname'. This pattern hasn't exhausted its usefulness. It can be leveraged for projectional editors, internal wikis, module system browsers, and other features of an integrated development environment. I'd like to pursue this further.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

This will likely also require a specialized program model.

### Provenance Tracking

The glas module system currently hinders manual provenance tracking, thus any efforts in this direction should be automated, i.e. with the compiler automatically maintaining maps for traceback. I think this is an important direction for future tooling. The [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics also seems relevant here.

### Alternative Data

I've often considered extending glas data to support graph structures or unordered sets. I think these could give some benefits to users, but it isn't clear to me how to effectively and efficiently work with them yet. For now, perhaps keep it to accelerated models.
