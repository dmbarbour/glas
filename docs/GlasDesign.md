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

For performance, a runtime may support optimized internal representations then convert to a dict or list only where the encoding is directly observed. For example, a useful subset of rationals might be represented and processed as floating point numbers, or matrices could be represented as an array with dimension metadata.

*Note:* Arithmetic in glas should be exact by default, but there should also be options to trade precision for performance. This is a problem left to language design.

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

The glas system proposes an unconventional [application model](GlasApps.md) based on repeated evaluation of a 'step' transaction between other events. This has nice properties for live coding, concurrency, distribution, and reactive systems. However, to fully leverage this requires non-trivial optimizations. To simplify these optimizations, we must constrain the program model.

To support user-defined syntax and expressive metaprogramming, the glas program model consists primarily of a generic intermediate representation. We define [namespaces](GlasNamespaces.md), an [abstract assembly](AbstractAssembly.md) for definitions, and a [program model](GlasProg.md)


 specific program model

 (the `%*` constructors) in a manner conducive to modularity, extension, staging, caching, and the desired optimizations.

In general, users will define a namespace while leaving those 


A subset of names in `%*` and `sys.*` should be left undefined, to be provided by the runtime.

### Modularity

Modules in the glas system are aligned with the filesystem. Instead of convent

Modularity starts with configuration. In my vision for glas systems, each user will typically inherit from a community or company configuration via DVCS then override definitions representing user-specific authorizations, resources, preferences, and extensions.

To simplify tooling and support user-defined syntax, processing of files is aligned with file extensions by default. 


To simplify packaging and sharing, glas systems forbid use of "../" and absolute file paths for dependencies between files. A file may refer only to other files in the same folder, remote files via DVCS, or the local namespace of the cli


To process a file with extension ".x.y" we'll search the scope for functions "lang.y" and "lang.x" and apply them as compilers. As a special case, we assume a built-in compiler for ".g" files, and we'll implicitly attempt to bootstrap "lang.g" if it depends on itself.

ACTIVE DESIGN DECISION:

* *layered namespace* - Configuration and applications are separated structurally. Applications might be specified concretely by `file:(Location, Localization)` within the configuration, with localization determining language modules and access to configured modules. Reference to a configured module is by value, which constrains dependency structure.
* *one big namespace* - Application definitions are visible in the configuration namespace. An application is a hierarchical component with its own 'step' and 'start' definitions and so on. This relies heavily on lazy evaluation.
* *eval to namespace* - An application is an expression that evaluates to a namespace 'object'. Depending on the expression language, this may include binding definitions in scope, expressing applications as closures, etc.

Initially, I pursued a layered namespace. It's simple, but inflexible. I suspect that one big namespace is too far in the other direction.


The entire 'package system' is encoded in the configuration namespace.





 file may only reference files contained in the same folder, remote files via DVCS. 

for a file, we may only load that file



At this point, I have an important design choice: 

* *one big namespace* - The configuration namespace directly contains application definitions. This leans heavily on lazy evaluation and caching of the configuration namespace. User-defined syntax must be expressed in terms of metaprogramming.
* *layered namespace* - The configuration 

the configuration directly contains application definitions, or 

, or applications are indirectly specified in the configuration (e.g. as `file:(Location, Localization)`). 

The configuration is responsible for integrating modules. 

 between modules. 






Configurations are initially expressed using the [glas init language](GlasInitLang.md) with the ".gin" file extension, but this language provides some opportunities to integrate user-defined syntax.






Modularity starts at the configuration layer. In my vision for glas systems, each user will typically inherit from a community or company configuration (via DVCS), then override a few definitions representing authorizations, resources, or preferences. 






Based on command line arguments, the 'glas' executable will lazily evaluate the user's configured namespace, logically extend it with `sys.*` methods for effects, then compile or interpret the definitions.





 extend this namespace with standard `sys.*` method


and `%*` methods to bind the 



 of a few generic  together with some assumptions

proposes a generic []













The glas system specifies encodings for [namespaces](GlasNamespaces.md) and [abstract assembly](AbstractAssembly.md), in addition to a *program model*, which is essentially 

 as an intermediate representations for glas programs. In general, a module that represents a program will compile into a namespace. The namespace model supports hierarchical composition, inheritance, overrides, and 'load' expressions for staged computation and integration with the module system.

The glas system specifies the [glas init language](GlasInitLang.md) for modular configurations, a primary [glas language](GlasLang.md) for the program layer, and a [glas object language](GlasObject.md) for serialization of structured data. These favor file extensions ".gin", ".g", and ".glob" respectively. Users may define additional languages with user-defined file extensions in the program layer.

### User Namespace

Each user has a `GLAS_CONF` configuration 


## Module System

The module system is an another area where glas in

The 'glas' executable implicitly extends the configuration namespace with some definitions under `sys.*` to support effects. 

 The configuration may reference OS environment variables


 The total configuration namespace may be very large, but this is mitigated by memoization and lazy evaluation.

The c

The glas system proposes a two level module system:

* *modular configurations* - Configuration files may import from the filesystem and remote DVCS repositories.
* *modular applications* - Applications may refer to local files or modules defined within the configuration.


The final configuration namespace may be very large even if the user's personal configuration file is very small.

 . To simplify sharing, application layer modules are organized into folders that depend only on contained files and configured modules (no "../" or absolute file paths), and cannot observe their own location.

The configuration will typically define application modules under `module.*`, with each module definition including a description and specification recognized by the 'glas' executable. Perhaps:

        type ModuleDesc 
              = (spec:ModuleSpec
                ,desc:"text blurb"
                ,... # ad-hoc annotations
                )

        type ModuleSpec
              = file:(at:Location, ln:Localization)
              | data:PlainOldData

In most cases, application modules will specify files in the filesystem or remote DVCS with a simple localization. The localization allows us to specify the same file multiple times with different dependency contexts. 

parameterize the same file with multi

apply aliases (e.g. so we could load a file with `math => my-math`.

alias

parameterize the file with a namespace of configured dependencies. 

 configured module

 allows us to treat the configuration namespace as the module namespace. 

" to "module

determines which configured modules loaded when compiling the file. 

Although most global modules are specified as files, support for inline 'data' is convenient when parameterizing modules through a configuration. For files, Location might specify local filesystem or a remote DVCS, while Localization modifies which configured files are loaded. 

The glas system supports user-defined syntax for application modules. To compile a file with extension ".x.y" we'll first load modules "lang.y" and "lang.x", subject to localization.  


## Language Modules




Compilation of a file is based on file extension. To compile a file with extension ".ext", we first load *Language Module* 'lang.ext', which should define a 'compile' function (see below). Support for ".g" files is built-in, but the glas system will nonetheless attempt to bootstrap 'lang.g' if defined. A file with multiple extensions such as ".json.gz" is implicitly pipelined as 'lang.gz' then 'lang.json'. Conversely, a file without extensions trivially 'compiles' to its binary content. 


To support user-defined syntax, application modules implicitly depend on configured *language modules* based on file extensions. To compile a file with extension ".x.y" we first load modules "lang.y" and "lang.x", each of which should define a `compile : Source -> ModuleValue` function. 

To bootstrap this system, support for ".g" files is built-in. If "lang.g" is defined

A language module should compile to a program namespace that defines `compile : SourceCode -> ModuleValue`. In most cases, the ModuleValue should represent another program namespace. However, it may represent any structured value, which can be useful when working with 'data' modules or composing multiple file extensions.

To support a more deterministically reproducible outcome, language modules have limited access to effects. Mostly, we can 'load' other compiled module values. For convenience, this extends to staged compilation. Effects API:

* `sys.load(ModuleRef)` - On success, returns compiled value of the indicated module. On error, diverges if observing failure would be non-deterministic (e.g. dependency cycle, network fault, resource quota), otherwise fails observably (e.g. backtracking or exception). We'll broadly distinguish a few kinds of ModuleRef:
  * *file:Name* - Load a local file from the same folder as the file currently being compiled. Uses the same localization as the origin file. 
  * *dvcs:Resource* - Load a remote resource.
  * *conf:Name* - Load by name from the configured environment. Depending on conventions, this might represent a specific parameter or localized access to a configured module namespace.
  * *data:Data* - returns given Data. Intended for use with composite module refs like *eval*.
  * *eval:(lang:ModuleRef, src:ModuleRef)* - staged compilation, for embedding user-defined languages or DSLs.

In addition to loading dependencies, a language module can use annotations to implement logging, profiling, tracing, quotas, caching, parallelism, and hardware acceleration.

*Note:* I might eventually let compilers capture module localizations to explicitly support lazy load. I hesitate because localizations capture non-local configuration details, which hurts reproducibility. That said, it is still feasible for `sys.load()` to implicitly be lazy, leveraging external references when caching compilation to [glas object](GlasObject.md).

## Automated Testing

As a simple convention, modules named `test.*` in scope when compiling a module (relative to Localization) could be treated as a test suite. These modules might export any number of namespaces that define pass-fail `test.*` methods. 

These test methods have limited access to effects: non-deterministic choice for fuzz testing or property testing, ephemeral state for environment simulation. To support detailed output and structured test reports, the tests may include logging or profiling annotations, and test namespaces may also implement 'http' views of final test state.

It should be feasible to share test efforts in many cases via configuring a cache.

## Performance

### Acceleration

I touch on the notion of acceleration when describing the *List* and *Number* types for glas data. For example, we might represent lists as finger-tree ropes to support efficient slicing and appending of large lists. This generalizes.

When a function is giving us poor performance, it is feasible to replace that function with a compiler built-in or hardware. When a concrete data representation is giving poor performance, we can introduce logical representations that support more efficient operation.

To resist silent performance degradation, acceleration should be explicit, guided by annotations. Ideally, this can be understood as an optimization instead of a semantic extension.

Intriguingly, it is feasible to accelerate simulation of an abstract CPU or GPGPU, compiling the simulation to run on actual hardware. There may be some memory safety constraints, but those can be checked statically if we're careful in our designs. This is a primary direction to pursue for high-performance computation in glas systems.

In general, we should avoid any application-specific acceleration in favor of types (sets, graphs, matrices, abstract machines) that are likely to be useful across a wide range of problem domains.

### Laziness and Parallelism

Insofar as we can prove subcomputations are 'pure' calculations and will terminate in some reasonable period, we can transparently make that computation lazy or evaluate it in parallel. This allows users to set up expensive computations for evaluation in the background. 

In context of transactions, we can commit while computation is ongoing. A future transaction can later wait on the result to become observable. In general, we can chain further lazy and parallel operations, and it is feasible to observe partial results without waiting on the full thing. I think this would provide an effective basis for pipelining between transactions.

In a distributed computation, lazy data might be serialized as an external reference, to be provided later or upon demand. This reference could include a secure hash of the computation to be performed to better fit with memoization and content distribution.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization can be guided by program annotations, but should be transparent modulo use of runtime reflection APIs.

Compared to virtual memory backed by disk, content addressing has side benefits for incremental computation, orthogonal persistence, and structure sharing. A lot of work doesn't need to be repeated when a hash is known. When communicating large values, it also works nicely with [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network).

The [glas object (glob)](GlasObject.md) representation is designed to serve as the primary serialization format.

### Memoization

Any computation that could run lazily or in parallel can potentially be memoized. Persistent memoization would be especially relevant for incremental compilation of the module system. Content addressing can support memoization by reducing the cost to compare persistent values.

Memoization is most easily applied to tree structures, where we can compute some monoidal value for each tree node based on the value in each child. To memoize over lists, we might take inspiration from [prolly trees](https://docs.dolthub.com/architecture/storage-engine/prolly-tree) where a list can be 'chunked' in a stable way based on a rolling hash.

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

Type annotations can be represented in [abstract assembly](AbstractAssembly.md). Front-end languages can have built-in syntax and naming conventions to further support such annotations. We can potentially analyze namespaces to verify some types ahead of time, while others might be enforced dynamically using runtime annotations in the data representation. For performance, we might also be explicit about which types we assume to be statically checkable.

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

I like the idea of units for numbers, i.e. such that we know it's `3.0 Volts` instead of `3.0 kilograms`. But I haven't found a nice way to represent this dynamically. Perhaps it could be supported via shadow types, or generally a concept of tagging numeric types with logical metadata via annotations, and also checking this metadata via annotations.

### Compact Dictionaries

Use of UTF-8 for encoding dictionaries is awkward in many ways. An alternative is to use a 32-bit encoding for dictionary labels with 'a'-'z' and six extra characters, perhaps: data separator (':'), hyphen, dot, privacy separator ('~'), a number escape (with sign and length to preserve lexicographic order), and a user escape. 

        lexicograph order:
           x, x-t, x1, x1-t, x2, x10, xa, xb, x.a

        00000               data sep (':')
        00001               word joiner ('-')
        00010               number escape
        00011 to 11100      characters 'a' - 'z'
        11101               path separator ('.')         
        11110               privacy separator ('~')
        11111               reserved for user extension

Although this is feasible, I think the savings won't be very significant. The specification overhead must be considered. A better solution for saving space is to have a compiler recognize static types and use a specialized representation closer to 'structs' and tagged unions.


