# Glas Design

Glas was named in allusion to transparency of glass, human mastery over glass as a material, and staged creation with glass (liquid and solid). 

Design goals for glas include compositionality, extensibility, reproducibility, modularity, metaprogramming, and live coding. Compared to conventional languages, there is much more focus on compile-time computation and staging. 

Interaction with a glas system is initially through a command line 'glas' executable. See [Glas CLI](GlasCLI.md) for details.

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are relatively convenient for persistent data structures and content addressing of very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)`, either type `(a + b)`, or unit `()`. However, glas systems will favor labeled data as more human meaningful and extensible. We will encode labels as a left-right-left path into a binary tree. Words such as 'height' and 'weight' can be encoded into the path using UTF-8, allowing dictionaries such as `(height:180, weight:200)` to be encoded as [radix tree](https://en.wikipedia.org/wiki/Radix_tree). An open variant can be encoded as a singleton dictionary. 

To efficiently represent dictionaries and variants, we must compactly encode non-branching sequences. A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encodes 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     // branch point
            | Stem64 of uint64 * Node   // all 64 bits!

        Stem Encoding (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

This can also efficiently encode bitstrings of arbitrary length as a Stem terminating in a Leaf, which is useful when encoding integers. 

### Integers

Integers in glas systems are typically encoded as variable length bitstrings, msb to lsb, with negatives in one's complement:

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

See also *Numbers*, below.

### Lists, Arrays, Queues, Binaries

Sequential structure in glas is usually encoded as a list. A list is either a `(head, tail)` pair or a leaf node, similar in style to Lisp or Scheme lists.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special handling because they're a very popular type at system boundaries (reading files, network communication, etc.). Logically, a binary is a list of small integers (0..255). For byte 14, we'd use `0b1110` not `0b00001110`. But under the hood, binaries will be encoded as compact byte arrays.

### Numbers, Vectors, and Matrices

Arbitrary rational numbers can be encoded as a dict `(n, d)` of integers. The rational subset of complex or hypercomplex numbers can be modeled as dicts `(r, i)` or `(r, i, j, k)` of rationals or integers. A vector is encoded as a list of numbers. A matrix is encoded as list of vectors or matrices of identical dimensions. Arithmetic operators in glas systems should be overloaded to handle these different number types where it makes sense to do so. 

For performance, a runtime may support optimized internal representations. A useful subset of rationals could be efficiently represented and processed as floating point numbers. Matrices could be implemented as an array with dimension metadata.

*Note:* Arithmetic in glas is exact by default, but there will be workarounds for performance.

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

A glas program module should compile to `g:(ns:ProgramNamespace, ...)`. The 'g:ns:' header enables future variants and extensions. The [namespace model](GlasNamespaces.md) lets us compose, shadow, override, and metaprogram definitions. Definitions are represented in [abstract assembly](AbstractAssembly.md). The runtime will extend an application namespace with [primitive AST constructors](GlasProg.md) and `sys.*` effects APIs. 

The glas system proposes an unusual [application model](GlasApps.md) based on repeated evaluation of a transactional 'step' method, mixed with other transactional events such as 'http' requests. This has nice properties for live coding, concurrency, interruption, distribution, and reactive systems. However, to fully leverage this requires non-trivial optimizations. To simplify these optimizations, the primitive AST constructors are designed with care.

Semantics are specified for this structured intermediate representation because front-end syntax is user-defined. We assume module "lang.x" defines the compiler for ".x" files. A file with extension ".x.y" is pipelined to "lang.y" then "lang.x". To ensure cachability and portability, compilers are limited to loading modules and logging messages. As a special case, a ".g" compiler is built-in, and we attempt to bootstrap "lang.g" where it's defined.

## Staged Metaprogramming

The glas system favors static staged metaprogramming as the primary basis for higher order programming. In part, because first-class functions or objects interact awkwardly with live coding, orthogonal persistence, remote procedure calls, and network partitioning. For flexibility, we'll support static staging at several layers:

* User-defined syntax is the first layer. Users can extend their languages with problem-specific syntactic sugar or introduce text-to-text macro preprocessor languages. 

* The namespace is the second layer. Users can define and apply (via 'load') namespace macros that expand to multiple definitions, with late binding and overrides.

* The program AST is the third layer. We can introduce AST nodes that evaluate and expand at compile time. Algebraic effects can serve as a second-class function passing.

Where we need dynamic code, we'll want mechanisms that interact nicely with live coding and the other features. One option is to interpret program values, i.e. `g:(ns:ProgramNamespace, ...)`, in context of an ephemeral *localization*. For performance, the runtime can cache safety checks and JIT compilation. Another option is to introduce reflection APIs to statefully 'patch' the application namespace, modeling dynamic code as a form of live coding and self-modifying code. 

## Modularity

The glas module system has two layers: modular configurations, and modular applications. A typical user configuration will import a community or company configuration from DVCS, then override a few definitions to integrate user preferences, projects, or resources. 

At both layers, modules are mostly represented by files. To simplify sharing and reuse of code, application modules can only reference local files from the same folder or subfolder as the file being processed, or the configured compilation context. Dependencies between configuration files are less restrictive, though we do forbid remote configuration from using absolute file paths or "../" paths that would escape the DVCS repository.

There is no separate package system. Instead, application modules are manually defined within the configuration. This results in very large configuration namespaces, but is mitigated by lazy loading and evaluation. In return, the configuration has precise control over compilation context. Instead of a flat namespace of global modules, or awkward overrides based on first match on a search path, we have the full power of namespaces to manage dependencies. 

When compiling application modules, the configured compilation context must include module "lang.x" to process ".x" files. As previously noted, ".g" is a special exception for bootstrap. In any case, language modules can be localized to each community or project, which reduces risk for extension and experimentation.

Application modules are ultimately configuration values. The configuration language provides APIs or keywords to conveniently 'compile' application files. But a configuration could also directly compute its own applications or provide arbitrary data through a compilation context.

*Note:* For `module.foo` we might define associated metadata such as `module.foo#about` or `module.foo#author`. A tool to browse modules without building them should know this convention.

## Language Modules

A language module is a program module whose namespace defines `compile : SourceCode -> CompiledValue` assuming limited access to limited effects such as `sys.load` to access local files or the configured compilation context. 

* `sys.load(ModuleRef)` - returns the compiled value of the referenced resource. May fail if the file does not exist or if compilation fails deterministically. May diverge if compilation involves a dependency cycle or failure would be non-deterministic, e.g. due to network connectivity. Default ModuleRefs:
  * *file:Name* - local file dependencies, processed based on configured 'lang.ext'.
  * *conf:Name* - access the compilation context provided by the configuration

Other effects of compilation include logging, profiling, and persistent caching. However, these are modeled as annotations via primitive AST nodes, not as observable effects. Nonetheless, we'll rely on logging as the primary basis to inform developers about specific problems in code.

*Note:* Lazy loading can improve performance or stabilize caching in many cases. It is feasible to wrap load within a lazy computation that always returns or diverges (no observable failure), then defer load until necessary. 

## Automated Testing

We can develop a few conventions for recognizing tests in the module system. For example, we can search for `module.foo#test` or similar by associated name. We might also recognize `test.*` modules in the compilation context as potential tests. 

In any case, once we have a pile of test programs, we can run pass-fail `test.*` methods in the program namespace with limited access to effects. Supported effects should include `sys.fork(N)` to support fuzz testing, and ephemeral state to simplify simulations and reporting. 

Ideally, we will share and coordinate test results to avoid unnecessary rework. This might configured with guidance from `settings.*` options in the test program.

## Performance

### Acceleration

I touch on the notion of acceleration when describing the *List* and *Number* types for glas data. More generally, a runtime can develop specialized data representations and functions to leverage them, then let programmers replace a reference implementation with a more efficient version via annotation. In addition to triggering this exchange, the annotation resists silent performance degradation. 

Intriguingly, it is feasible to accelerate simulation of an abstract CPU or GPGPU or cluster. With careful design, including a safety check, we can compile the simulation to run on actual hardware. This is a possible path to high performance computing in glas systems.

As a rule, we should favor acceleration of types that are useful across a wide range of problem domains, e.g. lists, numbers, sets, graphs, matrices, and abstract machines.

### Laziness and Parallelism

We can introduce annotations to guide use of laziness and parallelism. If intelligently applied, parallelism can enhance utilization while laziness avoids wasted efforts. Both can keep transactions shorter, reducing risk of concurrent interference. However, laziness and parallelism introduce their own performance risks. This can be mitigated by heuristically 'forcing' computations. I suggest automatically forcing computations at RPC boundaries to isolate performance risks to each application.

### Memoization

Any computation that can run lazily or in parallel can potentially be memoized. Persistent memoization can be useful for compilation of applications or indexing of a database. We can introduce annotations to guide memoization. Memoization is most easily applied to tree structures, where we compute some monoidal property for a tree node based on the same property of each subtree. But memoizing computations on lists is also very valuable, and may need special attention.

In any case, the use of memoization is assumed in design of glas. Without it, many 'pure' computations such as compilation would require explicit use of state for manual caching.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization can be guided by program annotations, but should be transparent modulo use of runtime reflection APIs.

Compared to virtual memory backed by disk, content addressing has benefits for incremental computation, orthogonal persistence, and structure sharing. A lot of work doesn't need to be repeated when a hash is known. When communicating large values, it also works nicely with [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network).

## Thoughts

### Abstract Data and Substructural Types 

Data abstraction is formally a property of a program, not of data. But dynamic enforcement of abstraction assumptions does benefit from including some annotations in the data representation. We could extend the Node type to support abstract data:

        type Node =
            | ...
            | Abstract of TypeName * Tree

A TypeName would need to be stable in context of orthogonal persistence or live coding, but we could feasibly bind to database paths.

Abstract data might further be restricted in how and where it is used. Some useful restrictions include:

* linearity - The data cannot be duplicated or dropped like normal data. It can only be created or destroyed through provided interfaces. This is useful when data represents resources such as open files or network channels, ensuring that protocols are implemented correctly.
* scope - The data is only meaningful in a given context and cannot be arbitrarily shared. For example, we could have 'runtime' types that cannot be shared via RPC or stored in the persistent database, and 'ephemeral' types that cannot be stored between transactions.

As with abstract types, it's best if these substructural types are enforced by static analysis, but we can tweak representations to support dynamic enforcement. In this case, a few metadata bits per node could be represented using packed pointers, or we could reserve a few Stem bits.

*Note:* Data abstraction introduces some risk of path dependence and opportunity costs, e.g. schema update can be hindered. To mitigate this, we might generally restrict abstract types to runtime or ephemeral scope.

### Type Annotations and Checking

Type annotations should be represented in [abstract assembly](AbstractAssembly.md). Front-end languages can have built-in syntax and naming conventions to further support such annotations. We can potentially analyze namespaces to verify some types ahead of time, while others might be enforced dynamically using runtime annotations in the data representation. For performance, we might also be explicit about which types we assume to be statically checkable.

Due to the namespace context, it is feasible to wrap existing modules, adding type annotations, and it is feasible to override or refine type annotations for a software component. 

Further, type annotations should not directly contribute to formal behavior of glas programs, which enables gradual typing. In my vision for glas systems, partial typing should be the common case, with users expressing rough assumptions about the shapes of things without always providing the fine details.

### Proof Carrying Code

In addition to types, we could support expression of more ad-hoc properties about code within the abstract assembly, along with a proof or adequate proof tactics. Like types, these proofs could be supported via front-end syntax and naming conventions, and would be subject to tweaks or overrides through the namespace. Proofs and types might ultimately be the same thing, just with types favoring implicit proof tactics.

Similar to logging and profiling, proofs could be associated with static 'channels' to simplify enabling or disabling specific proofs. 

### Graphical Programming

We could develop a language where the file is essentially a database, and there are graphical tools to render and modify the database. This would simplify integration with tables and structured data, graphical representations of behavior, and so on. Incremental compilation over large databases is possible by leveraging *caching* carefully.

### Extensible IDEs

Glas uses a pattern of naming modules to extend the glas system, e.g. 'lang-ext' or 'glas-cli-opname'. This pattern could further be leveraged to support projectional editors, REPLs, internal wikis, module system browsers, and other features of an integrated development environment. 

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

This will likely also require a specialized program model or extensions to the abstract assembly for constraints across the entire namespace.

### Provenance Tracking

The glas module system currently hinders manual provenance tracking, thus any efforts in this direction should be automated, i.e. with the compiler automatically maintaining maps for traceback. I think this is an important direction for future tooling. The [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics also seems relevant here.

### Alternative Data

I've often considered extending glas data to support graph structures or unordered sets. I think these could give some benefits to users, but it isn't clear to me how to effectively and efficiently work with them yet. For now, perhaps keep it to accelerated models.

### Number Units

I like the idea of units for numbers, i.e. such that we know it's `3.0 Volts` instead of `3.0 kilograms`. But I haven't found a nice way to represent this dynamically. My best idea is to support units as a 'shadow type', i.e. some extra metadata about a number that is carried statically between computations. Perhaps I can find a way to let users conveniently manage shadow types in general?
