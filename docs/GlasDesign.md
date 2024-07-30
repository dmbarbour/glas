# Glas Design

Glas was named in allusion to transparency of glass, human mastery over glass as a material, and staged creation with glass (liquid and solid). 

Design goals for glas include compositionality, extensibility, reproducibility, modularity, metaprogramming, and live coding. Compared to conventional languages, there is much more focus on compile-time computation and staging. 

Interaction with a glas system is initially through a command line 'glas' executable. See [Glas CLI](GlasCLI.md) for details.

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are relatively convenient for persistent data structures and content addressing of very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)`, either type `(a + b)`, or unit `()`. 

However, glas systems often favor labeled data as more human meaningful and openly extensible. Labels can be encoded into a *path* into the tree. For example, 'data' might be encoded as UTF-8 into the path '01100100 01100001 01110100 01100001' where '0' and '1' represent left and right branches into a binary tree. 

A dictionary such as `(height:180, weight:200, ...)` contains many labeled values, implementing a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). To support iteration over dictionary keys, we'll add a NULL separator '00000000' between the label and data. That is, the ':' is actually encoded as a NULL, and NULL should not appear within labels. A labeled variant type is effectively a dictionary that contains exactly one label from a known set.

To efficiently represent dictionaries and variants, we must compactly encode non-branching sequences of bits. A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encoding 0..63 bits
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

This provides a base encoding. We'll further extend the Node type to detect violation of runtime type assumptions, to support debug tracing, and to enhance performance (see *Accelerated Representations* below). But glas data should generally be understood as binary trees.

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

Sequential structure in glas is usually encoded as a list. A list is as a binary tree where every left node is an element and every right node is a remaining list, and the empty list is a simple leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access arrays, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) under-the-hood. This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special handling because they're a very popular type at system boundaries (reading files, network communication, etc.). Logically, a binary is a list of small integers (0..255). For byte 14, we'd use `0b1110` not `0b00001110`. But under the hood, binaries will be encoded as compact byte arrays.

### Numbers, Vectors, and Matrices

A rational can be modeled as a dict `(n, d)` of integers with non-zero denominator 'd'. The rational subset of complex or hypercomplex numbers can be modeled as dicts `(r, i)` or `(r, i, j, k)` of integers or rationals. A vector is a list of numbers. A matrix is a list of vectors of identical lengths.

Arithmetic operators on glas data will generally be overloaded to support these number representations, at least where it makes sense to do so. Implicit conversions between number types would be performed as needed, restricted to lossless conversions.

Under the hood, the runtime can potentially introduce optimized representations. For example, it is feasible to introduce a bignum floating point representation for rationals whose denominator is a power of two (or ten, or sixty). Or represent a matrix as a large array with logical dimension tracking.

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

The glas system proposes an unconventional [application model](GlasApps.md) that is very suitable for live coding, reactive systems, distributed network overlays, and projectional editors. This is based on a transaction loop: run the same atomic, isolated 'step' transaction repeatedly, with non-deterministic choice as a basis for multi-threading. A few other methods may support HTTP, RPC, and live code switch.

The glas system develops general purpose models for [namespaces](GlasNamespaces.md) and an [abstract assembly](AbstractAssembly.md) as structured intermediate representations for glas programs. Program modules generally compile into a dict of namespaces (or mixins) of abstract assembly definitions. The namespace model supports hierarchical composition, inheritance, and overrides. The abstract assembly can control language features and support late binding of embedded DSLs and macros.

The glas system specifies the [glas init language](GlasInitLang.md) for modular configurations, a primary [glas language](GlasLang.md) for the program layer, and a [glas object language](GlasObject.md) for serialization of structured data. These favor file extensions ".gin", ".g", and ".glob" respectively. Users can define additional languages with user-defined file extensions in the program layer.

## Modules

The glas system uses a two level module system:

* *modular configurations* - Configuration files may import from the local filesystem, remote DVCS repositories, and perhaps HTTP resources.  
* *modular programs* - Program files may access abstract names from a configured environment, or local files from the same folder or subfolders.

This separation isolates many concerns to the configuration layer including authorization, migration, and version control. The locality constraint between program files ensures folders are self-contained, which simplifies refactoring and sharing. The configured environment for a program module may represent both namespaces and specific parameters, subject to convention and configuration.

In my vision for glas systems, a user will typically inherit from a community or company configuration then override a few definitions representing authorizations, resources, or preferences. The community configuration will specify individual modules, eliminating need for a separate package manager. A configured module might be described as:

        type ModuleDesc 
              = (spec:ModuleSpec
                ,desc:"text blurb"
                ,... # ad-hoc annotations
                )

        type ModuleSpec
              = file:(at:Location, ln:Localization)
              | data:PlainOldData

Most global modules would be specified as files. This may refer to remote DVCS resources depending on Location. The 'data' option is convenient for parameterizing modules.

Compilation of a file is based on file extension. To compile a file with extension ".ext", we first load *Language Module* 'lang.ext', which should define a 'compile' function (see below). Support for ".g" files is built-in, but the glas system will nonetheless attempt to bootstrap 'lang.g' if defined. A file with multiple extensions such as ".json.gz" is implicitly pipelined as 'lang.gz' then 'lang.json'. Conversely, a file without extensions trivially 'compiles' to its binary content. 

Dependencies between modules must form a directed acyclic graph. Dependency cycles will be detected when compiling modules and treated as an error.

## Language Modules

A language module defines a 'lang' namespace that defines `compile : SourceCode -> ModuleValue`. To ensure a reproducible outcome, language modules have limited access to effects: they can only ask the system to 'load' compiled data from other modules. However, to simplify embedding of user-defined languages or development of macros, this extends to staged evaluation.

* `sys.load(ModuleRef)` - On success, returns compiled value of the indicated module. On error, diverges if observing failure would be non-deterministic (e.g. dependency cycle, network fault, resource quota), otherwise fails observably (e.g. backtracking or exception). We'll broadly distinguish a few kinds of ModuleRef:
  * *file:Name* - Reference a local file in the current folder or subfolder. No "../" paths! Compilation is based on file extension, selecting "lang.ext" from the configured environment. 
  * *env:Name* - Load data by name from the configured environment. Depending on conventions, this might represent a specific parameter or localized access to a configured module namespace.
  * *data:Data* - returns given Data. Intended for use with composite module refs like *eval*.
  * *eval:(lang:ModuleRef, src:ModuleRef)* - staged compilation, useful for embedding user-defined languages or DSLs.

Although there are no other formal effects, annotations can support logging, profiling, tracing, caching, parallelism, and hardware acceleration.

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

### Abstract Data 

Data abstraction is formally a property of a program, not of data. But dynamic enforcement of abstraction assumptions does benefit from including some annotations in the data representation. We could extend the Node type to support abstract data:

        type Node =
            | ...
            | Abstract of TypeName * Tree

A TypeName would need to be stable in context of orthogonal persistence or live coding, but we could feasibly bind to database paths.

Aside from abstract structure, we might consider substructural types such as scope (e.g. don't let open file handles be stored in the persistent database or pass through RPC calls), or linearity (enforce that file handles are closed exactly once after open, require explicit dup). For glas systems, I'm inclined to conflate runtime scope and linearity, then restrict the database to plain old data. Thus, we would only need one extra bit per node or value, perhaps via packed pointers or reserving a stem bit.

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

