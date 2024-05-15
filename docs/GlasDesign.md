# Glas Design

Glas was named in allusion to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. There is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible through staged computing and definition of 'glas-cli-\*' modules. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Modules are represented by files and folders. Every valid module compiles to a glas value (see *Data*). In most cases these values represent *Programs and Applications*, but data modules are also useful and can be referenced as constants.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will first compile a global module named lang-ext, which must define a compilation function, then apply this compilation function to the file binary. To bootstrap this system, a compiler for ".g" files is built-in and we first compile lang-g if possible.

File extensions may be composed. For example, "example.json.m4.gz" would essentially apply a pipeline of three compilers: lang-gz then lang-m4 then lang-json. This might decompress the file binary, apply a text macro preprocessor, then parse the result as JSON. Conversely, if file extensions are elided, the compiled value is simply the file binary. 

A folder is compiled to the value of its contained "public" file of any extension. Folders serve as dependency boundaries: there is no access to arbitrary files within subfolders or the parent directory. Folders may contain local modules, test programs, and auxilliary content such as a readme, license, or a cryptographically signed manifest.

Global modules are represented by folders. The namespace of global modules is subject to configuration, and may compose community and company distributions, referencing the network in addition to the filesystem. 

## Data

Glas represents data using finite, immutable binary trees. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are simpler than graphs for incremental construction or reasoning about termination. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)`, either type `(Left a | Right b)`, or unit `()`. 

However, glas systems favor labeled data because labels are more meaningful and extensible. Labels are encoded into a *path* through a tree, favoring null-terminated UTF-8. For example, label 'data' would be encoded into the path `01100100 01100001 01110100 01100001 00000000` where '0' and '1' respectively represent following the left or right branch. A record such as `(height:180, weight:200)` may have two such paths with shared prefixes, forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). A variant would have exactly one label.

To efficiently represent labeled data, non-branching paths must be compactly encoded. A viable runtime representation is closer to:

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

It is feasible to could further extend Node with specialized representations for common data structures (see *Accelerated Representations* below). But the idea is that anything can be represented as a binary tree, and binary trees conveniently support indexed structure without introducing 'offsets' or 'pointers'. 

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

### Lists, Arrays, Queues, Binaries

Sequential structure is usually encoded as a list. A list is represented as a binary tree where the left nodes are elements and the right nodes form the spine of the tree, terminating with a leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access arrays, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) under-the-hood. This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special treatment because they are a popular data representation for filesystems and networks. Within glas systems, a binary is canonically represented as a list of small integers (0..255), but the finger-tree rope might allocate binary fragments of many kilobytes or megabytes. Program annotations can provide guidance, e.g. ask a runtime to 'flatten' a binary to control chunk size.

### Accelerated Representations

We can generalize the idea of representing lists as finger-tree ropes to support for unboxed floating point matrices, unordered data types (e.g. sets, unlabeled graphs), or even virtual machine states (with registers, memory, etc.).

In each case, we first define a canonical representation as a binary tree. For example, a matrix could be a list of lists. A set can be represented by an ordered list. An unlabeled graph might refer to [graph canonization](https://en.wikipedia.org/wiki/Graph_canonization). A VM state might use a simple record.

However, constructing, validating, manipulating, and maintaining the canonical representation is expensive and error-prone. Instead, the runtime provides specialized under-the-hood representations, and users manipulate that representation indirectly through built-in or accelerated functions. Use of abstract data types can guard against accidental conversion to the canonical representation.

The runtime can maintain accelerated representations even when serializing data for RPC communication or a key-value database. In case of [glas object](GlasObject.md), this leverages the external reference type. Use of runtime reflection APIs may allow programs to observe actual under-the-hood representations.

### Large Values

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to disk. This optimization is transparent to most functions, but can be guided by program annotations and is potentially visible through a reflection API. Content-addressed references simplify memoization, communication, and persistent storage of large structures. The proposed representation is [Glas Object (glob)](GlasObject.md) binaries. 

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration. The glas system specifies the [glas language](GlasLang.md) with the ".g" file extension, and a non-conventional [glas application](GlasApps.md) model. However, glas is very extensible through language modules, staging, and acceleration and is not limited to these initial models.

## Language Modules

As a simple naming convention, global module 'lang-xyz' should detail how to compile files with extension ".xyz". This detail should be represented as an 'app' namespace definining `compile : SourceCode -> ModuleValue` with limited effects. Source code is *usually* a binary, but composition of file extensions allows for structured source. Compilation must succeed in a single step. 

Effects are restricted to ensure deterministic, reproducible outcomes and to simplify integration of the compiler function into other contexts. The compiler is limited to loading modules and logging messages. When loading modules, dependency cycles or a failed download may cause compilation to logically diverge and report an error.

I hope to eventually support many 'associated' language tools by naming conventions in the module system, such as support for REPL, linters, syntax highlighting, [language server protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol), interactive tutorials, etc.. However, these could be defined in separate modules, e.g. 'repl-xyz'. 

## Automated Testing

As a simple naming convention, local modules whose names start with "test-" will be interpreted as tests. Tests can automatically be compiled and evaluated as part of building the owning module. The glas system can automatically maintain a 'system health' report based on which tests are passing, which are failing, and test coverage at the scope of a distribution.

Each test module may define multiple test methods as `test.*`. This has the same interface as application built-in tests. However, a test module has limited access to effects to guarantee tests are reproducible. Effects includes language module effects and 'fork' for non-deterministic choice and fuzz testing.

In case of fuzz testing with 'fork', how much testing should be performed will depend on quotas and heuristics. But a smarter test system can remember which tests are performed, focus on regression tests, and leverage fork to focus on edge cases and improve test coverage. 

## Performance

### Acceleration

If a function is giving us poor performance, it is feasible to replace that function with a compiler built-in or hardware. To resist silent performance degradation, this replacement should be explicit, guided by annotations. Usefully, this can be understood as an optimization instead of a semantic extension.

It is feasible to accelerate simulation of an abstract CPU or GPGPU, running on actual hardware. This simulation must be restricted to a memory-safe subset of behaviors, though this could be achieved through proof-carrying code and static analysis. When the code argument is static, much work can be performed ahead of time.

Acceleration influences under-the-hood representations of data. I describe representation of lists as finger-tree ropes and *accelerated representations* more generally under *Data*. Accelerated programs or built-in functions are necessary to take full advantage of accelerated representations.

### Laziness and Parallelism

Insofar as we can prove subcomputations are 'pure' calculations and will terminate in some reasonable period, we can transparently make that computation lazy or evaluate it in parallel. This allows users to set up expensive computations for evaluation in the background. 

In context of transactions, we can commit while computation is ongoing. A future transaction can later wait on the result to become observable. In general, we can chain further lazy and parallel operations, and it is feasible to observe partial results without waiting on the full thing. I think this would provide an effective basis for pipelining between transactions.

In a distributed computation, lazy data might be serialized as an external reference, to be provided later or upon demand. This reference could include a secure hash of the computation to be performed to better fit with memoization and content distribution.

### Content Addressed Data

Glas systems will leverage content-addressed storage (addressed by secure hash) to manage larger-than-memory data in context of orthogonal persistence and distributed evaluation. This might roughly be viewed as a variation on virtual memory paging, but it's simplified by immutability and hierarchical structure.

In a distributed system, content addressed data allows for incremental communication of large data structures, especially in case of structure sharing. Further, a [content distribution network (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network) can mitigate costs of repeatedly communicating large values.

Use of content addressing should be semi-transparent: guided by annotations, invisible except via controllable reflection APIs. 

### Memoization

Any computation that could run lazily or in parallel can potentially be memoized. Persistent memoization would be especially relevant for incremental compilation of the module system. Content addressing can support memoization by reducing the cost to compare persistent values.

Memoization is most easily applied to tree structures, where we can compute some monoidal value for each tree node based on the value in each child. Unfortunately, it does not easily apply to lists because the underlying finger-tree structure is not visible. This can be mitigated by applying some other stable chunking system to the list, cf. [prolly trees](https://docs.dolthub.com/architecture/storage-engine/prolly-tree).

## Thoughts

### Type Checking

Type annotations can be included in the application namespace and perhaps also within program definitions. Ideally, we can immediately begin to perform some checks on programmer assumptions and expectations based on these types. However, I hope for types to be 'partial' in the sense that we can leave them partially unspecified and incrementally refine them. Types with holes in them.

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

Glas uses a pattern of naming modules to extend the glas system, e.g. 'lang-ext' or 'glas-cli-opname'. This pattern hasn't exhausted its usefulness. It can be leveraged for projectional editors, internal wikis, module system browsers, and other features of an integrated development environment. I'd like to pursue this further.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

This will likely also require a specialized program model.

### Provenance Tracking

The glas module system currently hinders manual provenance tracking, thus any efforts in this direction should be automated, i.e. with the compiler automatically maintaining maps for traceback. I think this is an important direction for future tooling. The [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics also seems relevant here.

### Alternative Data

I've often considered extending glas data to support graph structures or unordered sets. I think these could give some benefits to users, but it isn't clear to me how to effectively and efficiently work with them yet. For now, perhaps keep it to accelerated models.
