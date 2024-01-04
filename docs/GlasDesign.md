# Glas Design

As a backronym, 'General LAnguage System'. Glas was named in allusion to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. 

Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. However, there is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Every module compiles to a glas value (see *Data*). A program module usually compiles into a structure representing an AST or intermediate language. Other modules might compile into binary or structured data intended for import by an application. Modules may have any value type, subject to user-defined compilation functions.

Modules are represented by files and folders. 

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will first load a global module named language-ext, which must define a compiler function. Most languages are community defined, but to bootstrap this system, glas CLI will have a built-in compiler for ".g" files. This is first used to compile language-g, and other glas languages ultimately build upon language-g. 

Composition of file extensions is supported. A gzip-compressed JSON file with an m4 text-to-text macro preprocessor might use file extension ".json.m4.gz". In this case, we apply language-gz, then language-m4, then language-json to compute the compiled value for module 'foo'. Conversely, if file extension is elided, the compiled value is the file binary. File extensions are not considered part of the module name.

A folder must contain a 'main' module, represented by a file. The value of a folder is simply the value of its main module. A folder serves as a container for local modules, test programs, and utility content such as a readme, license file, or signed manifest. A folder also serves as a boundary for dependencies. A compiler function can only reference local modules within the same folder, and global modules.

Global modules are represented by folders and organized into distributions. In context of glas systems, a *distribution* is a set of modules that are versioned and maintained together, often defined in terms of inheritance from other distributions. The glas command line interface supports flexible description of distributions.

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

Direct representation of lists is inefficient for many use-cases, such as tuples or arrays or double-ended queues. To enable lists to serve most roles, lists will often be represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the earlier 'Node' type with array and binary fragments and logical concatenation, then accelerating list operations to slice or append large lists.

To support larger-than-memory data, glas systems will also leverage content-addressed storage to offload volumes of data to disk. I call this pattern *Stowage*, and it will be heavily guided by program annotations. Stowage simplifies efficient memoization, and network communication in context of large data and structure-sharing update patterns. Stowage also helps separate the concerns of data size and persistence.

## Programs

Programs are values with known interpretation. Although glas systems can support more than one program model, the quality and character of glas systems, and simplicity of bootstrap, will be deeply influenced by the initial language. The initial language will use the ".g" file extension, and I am currently developing [grammar logic programming](GrammarLogicProg.md) as a basis for the initial language.

## Language Modules

Language modules are global modules with a simple naming convention: module language-xyz provides the compiler function for files with extension ".xyz". A language module must compile into a program that represents via simple `Data -> Data` function with limited effects:

* *load(ModuleRef)* - We can request the runtime to provide the compiled value from another module. This may fail, in which case reason for failure is implicitly reported via the log. Dependency graph must be acyclic (this is checked). ModuleRef can be:
  * *global:String* - a global module, i.e. based on a configured search path
  * *local:String* - a local module, i.e. same folder as file being compiled
* *log(Message)* - We can write messages for the developer while we compile, e.g. to indicate progress, warnings, or errors. Returns unit. Messages should be dictionaries, not plain text, and the runtime might implicitly add some debug context to messages.

Languages also benefit from associated tools, such as REPLs, linters, intellisense, decompilers, etc.. We will need to develop some conventions around this, perhaps association of global modules based on names like repl-xyz or exporting additional functions from language-xyz.

## Automated Testing

As a simple convention, any module whose name starts with "test-" is assumed by the glas system to represent a test program. This includes local and global modules. We can develop glas system tools to discover, compile, evaluate, and cache results for test programs either local to a folder or for the active distribution. This could help us maintain a distribution health report.

A test program can be expressed as a `unit -> unit` pass/fail function with simple effects:

* *log* and *load* - same as language modules
* *fork(N)* - return non-deterministic choice of natural number in range 1..N.

Use of *fork* would support fuzz testing, parallel testing, and expressing many tests with a single program. Aside from evaluating the tests, tools might also evaluate assertions, types, and other useful properties. This approach to testing does limit access to the real world, so integration testing requires a more conventional solution. But we could feasibly simulate many environments.

## Applications

The [transaction loop](GlasApps.md) is the default application model for glas systems. This model has nice properties for live coding, reactive systems, extensibility, and scaling. It is also aligns well with [grammar-logic programming](GrammarLogicProg.md), which implicitly requires transaction-like backtracking of effects. But it does complicate performance.

Other useful application models include staged applications based on command line arguments and extraction of binary data. It is feasible to extract an executable binary, using glas as a build system.

## Performance

### Acceleration

Acceleration is an optimization pattern where we annotate a subprogram to be replaced by a more efficient or scalable implementation known to the interpreter or compiler. 

In the simplest use case, users reference built-in functions such as 'accel:list-append' or 'accel:i64-add'. The runtime can specialize data representations to support these operations, e.g. use a rope structure to represent larger lists to ensure list-append is efficient. Relatedly, we can manually guide representation via 'accel:array-type' or similar, to indicate that a list should be represented under-the-hood by an array. This pattern extends glas systems with new performance primitives (without extending semantics).

As a more sophisticated use case, we might accelerate evaluation of code on an abstract, simulated GPGPU, CPU, Kahn Process Network. This might be expressed via 'accel:gpu:GpuCode'. The runtime compiles GpuCode to run on an actual GPGPU, so the GpuCode type should be easily translated, reasonably portable, memory-safe, and have other nice qualities. This allows acceleration to handle roles that would otherwise require FFI.

Further, dynamic code can be indirectly supported via 'accel:prog-eval' and 'accel:prog-type'. Here 'accel:prog-type' simplifies reuse of a prog value by caching arity analysis, JIT compilation, and other one-off steps. 

To resist silent performance degradation (across ports, runtime versions, etc.), the runtime or compiler should report an error where requested acceleration is unrecognized or unsupported. We might use 'accel:opt:Model' to explicitly indicate that acceleration is optional. 

Ideally, the glas system will automatically verify that the program and accelerator have the same behavior. Although full verification via static analysis or exhaustive testing is often difficult, we can at least introduce unit tests and perhaps some fuzz testing or random sample testing.

### Stowage

I use the word 'stowage' to describe systematic use of content-addressed storage (addressed by secure hash) to manage larger-than-memory data. Stowage is a variation on virtual memory paging, i.e. large subtrees can be moved from local memory to disk or a remote service if not immediately needed. Stowage simplifies support for large persistent variables, memoization, and incremental communication.

In context of glas systems, stowage is semi-transparent - invisible to pure functions and most effects, but guided by annotations and potentially accessible via runtime reflection effects. [Glas Object](GlasObject.md) is intended to be an efficient representation for stowage and serialization of glas data.

### Memoization

Purely functional subprograms in glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds. 

In combination with stowage, it is possible to incrementally process large data by memoizing recursive computations. This includes indexing of data, insofar as indexing can be expressed in terms of merging the indices of each component. Lists require special attention for stable chunking but it is possible to align memoization with the underlying finger-tree if we annotate a reducing function as associative (this annotation would be subject to proof or random testing).

Indexing large data via memoization and stowage is an underlying assumption for my vision of glas systems. Without this, we would rely entirely on stateful indices, which are locally efficient but error prone and difficult to share, compose, extend, or update at runtime.

### Content Distribution

Networked glas systems can potentially support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve performance when repeatedly communicating large stowed data values (see *Stowage*). A CDN service is not fully trusted, but it is feasible to derive a decryption and lookup key from the original secure hash of content. 

Usefully, we might support garbage collection of the CDN. With encrypted data, it cannot directly read the data to find dependencies. But we could upload some metadata such as a list of lookup keys that should be present together with the data.

### Compression Pass

When compiling glas programs, a useful optimization pass is to identify common subprograms and translate those to reusable function calls. This pass may be guided by annotations and is not necessarily aligned with user defined functions. Usefully, compression may occur after partial evaluation and other specialization passes. 

## Reflection

A runtime can directly provide a reflection API as part of the application effects API. This would enable an application to review its resource consumption, performance profile, or debug outputs then adjust behavior appropriately. With sufficient runtime support, an application might also be able to update application code at runtime, e.g. requesting reload from source to support live coding.

Additionally, a runtime could support annotations that provide limited access to a reflection API, e.g. the 'refl' annotation described earlier. This would also be capable of debug outputs and performance tuning. But annotations must not directly affect formal behavior of a program. At most, annotations could halt a problematic program early or provide useful hooks for the effectful reflection API.

## Thoughts

### Type Checking

The initial language-g will support annotations, and may develop a dedicated syntax for type annotations. I hope to eventually include typecheck tools that are applied automatically. But it's a low priority for now.

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

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

This will likely also require a specialized program model.

### Provenance Tracking

The glas module system currently hinders manual provenance tracking, e.g. we cannot access module names or file paths from the 'compile' function. Also, metaprogramming is widespread so we'd need to trace influence through macros. 

A partial mitigation strategy is that log messages can be associated with each file as it compiles. This is likely the only option short-term. A more complete solution will require tracing compiled output back to the inputs that influenced it, preferably to the precision of binary ranges within files. This is probably too much to trace efficiently, but we might try some heuristics around the notion of spreading and diluting blame similar to [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/). 

### Alternative Data

I've often considered extending glas data to support graph structures or unordered sets. I think these could give some benefits to users, but it isn't clear to me how to effectively and efficiently work with them yet. For now, perhaps keep it to accelerated models.
