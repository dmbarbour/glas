# Glas Design

Glas was named in allusion to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. There is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible through staged computing and definition of 'glas-cli-\*' modules. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Modules are represented by files and folders. Every valid module compiles to a glas value (see *Data*). Modules may have any value type, subject to user-defined compilation functions. Those same compilation functions might check the types of values that are imported, but in some cases we might import data or integrate modules of multiple types.

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

Integers in glas systems are usually encoded as variable length bitstrings, msb to lsb, with negatives in one's complement:

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

Bytes and fixed-size words are instead encoded as bitstrings of exact size, e.g. 8 bits per byte, msb to lsb. For example, byte 4 would be '00000100'. 

Other than bitstrings, sequential structure is usually encoded as a list. A list is represented as a binary tree where the left nodes are elements and the right nodes form the spine of the tree, terminating with a leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access or double-ended queues. To enable lists to serve most roles, lists will often be represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the earlier 'Node' type with array and binary fragments and logical concatenation, then accelerating list operations to slice or append large lists.

To support larger-than-memory data, glas systems will also leverage content-addressed storage to offload volumes of data to disk. I call this pattern *Stowage*, and it will be heavily guided by program annotations. Stowage simplifies efficient memoization, and network communication in context of large data and structure-sharing update patterns. Stowage also helps separate the concerns of data size and persistence.

## Applications and Programs

This is rather involved (see [Glas Applications](GlasApps.md)). The general idea is that we define modules that define namespaces that describe second-class objects. The runtime adds effectful operations to the namespace, then integrates the application based on standard methods it defines, e.g. repeatedly evaluating a transactional 'step' method to represent background processing. The transaction loop has very nice systemic properties, albeit contingent on an optimizer.

To support this, we must also develop a data type that effectively represents the modules, namespaces, and procedures. The proposed type for normal ".g" modules is a simple record of definitions with a trivial header. The application should be defined under 'app'.

        g:(app:Namespace, MoreDefs)

[Representation of the namespace](ExtensibleNamespaces.md) is sophisticated, so I'm developing it in a dedicated document. This is based on lazy rewriting of prefixes of names. Modulo constraints that names are represented by prefix-unique bitstrings and can be precisely identified, the namespace structure is effectively independent from what is defined within the namespace.

The main remaining question is what a program or procedure should look like. I'm experiencing a bit of analysis paralysis on this matter. Some interesting possibilities include the [interaction calculus](https://github.com/VictorTaelin/Interaction-Calculus) or [grammar logic programming](GrammarLogic.md). 

## Language Modules

Language modules have a simple naming convention, e.g. global module language-xyz provides the compiler function for files with extension ".xyz". The language module must represent an application with a simple `Data -> Data` function with limited effects:

* *load:ModuleRef* - We can request the runtime to provide the compiled value from another module. This may fail, in which case reason for failure is implicitly reported via the log. Dependency graph must be acyclic (this is checked). ModuleRef can be:
  * *global:String* - a global module, i.e. based on a configured search path
  * *local:String* - a local module, i.e. same folder as file being compiled
* *log:Message* - We can write messages for the developer while we compile, e.g. to indicate progress, warnings, or errors. Returns unit. Messages should be dictionaries, not plain text, and the runtime might implicitly add some debug context to messages.

Languages benefit from associated tools, such as REPLs, linters, intellisense, decompilers, etc.. We will need to develop some conventions around this, perhaps association of global modules based on names like repl-xyz or exporting a cluster of functions from language-xyz.

## Automated Testing

As a simple convention, modules whose names start with "test-" are treated as test programs. This includes local modules within a folder module, even if they don't contribute to the public result. We can develop tools to automatically compile and run tests and maintain a distribution health report. 

A test program can be expressed as a `unit -> unit` pass/fail function with limited effects:

* *log* and *load* - same as language modules
* *fork(N)* - return a non-deterministic choice of natural numbers in range 1..N.

Use of *fork* supports fuzz testing and expressing multiple tests as a single program. To support multi-tests, a simple convention might be that the first several forks up to some quota are exhaustively tested. The quota can be adjusted via annotation. 

Due to limited effects, testing an interactive application may involve simulating the environment (filesystem, network, user, etc.). Ideally we'll swiftly develop a reusable, tunable sandbox environments for testing. Of course, conventional integration testing is still feasible, but wouldn't be automated in the above sense.

## Applications

The [transaction loop](GlasApps.md) is the default application model for glas systems. This model has nice properties for live coding, reactive systems, extensibility, and scaling. It is also aligns well with [grammar-logic programming](GrammarLogicProg.md), which implicitly requires transaction-like backtracking of effects. But it does complicate performance. 

Other useful application models include staged applications and binary extraction. These are roughly described with the [glas command line interface](GlasCLI.md).

## Performance

### Acceleration

Acceleration is an optimization pattern where we annotate a subprogram to be replaced by a more efficient or scalable implementation known to the interpreter or compiler. This may also involve use of specialized representations. For example, if we accelerate list-append, the runtime might use append of finger-tree ropes. Acceleration effectively extends a language with new performance primitives without extending semantics, which is convenient for a minimalist language.

As a more sophisticated use case, our function might 'accelerate' evaluation of code on an abstract CPU or GPGPU. This code would support a memory-safe, deterministic, portable subset of programs. The accelerator would translate it for evaluation on the actual CPU or GPGPU. Ideally, the code is provided as a static parameter, allowing compile-time translation. This technique can extend glas systems to many problem domains while avoiding FFI.

To resist silent performance degradation, the runtime or compiler should report an error where requested acceleration is unrecognized or unsupported. But we can support additional annotations to suppress this error or reduce it to a warning for certain functions.

*Note:* Accelerators should be verified to match the subprogram they replace, where possible. Fuzz testing could be automated.

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
