# Glas Design

Glas was named in allusion to transparency of glass, human mastery over glass as a material, and phased or 'staged' creation with glass (liquid and solid). 

Design goals for glas include compositionality, extensibility, reproducibility, modularity, metaprogramming, and live coding. Compared to conventional languages, there is much more focus on compile-time computation and staging. To support live coding, the default [application model](GlasApps.md) is also non-conventional.

## Command Line Interface (CLI)

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible through staged computing and definition of 'glas-cli-\*' modules. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Modules are represented by files and folders. Every valid module compiles to a glas value (see *Data*). In most cases these values represent *Programs and Applications*, but data modules are also useful and can be referenced as constants.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will first compile a global module named lang-ext, which must define a compilation function, then apply this compilation function to the file binary. To ensure a reproducible outcome, the compiler function has very limited access to effects. To bootstrap this system, a compiler for ".glas" files is built-in to the CLI, and we'll first compile lang-glas if possible.

File extensions may be composed. For example, "example.json.m4.gz" would essentially apply a pipeline of three compilers: lang-gz then lang-m4 then lang-json. This might decompress the file binary, apply a text macro preprocessor, then parse the result as JSON. Conversely, if file extensions are elided the compiled value is the unmodified file binary.

A folder is compiled to the value of its contained "main" file, which may have any recognized file extension. Folders are implicit dependency boundaries: a compiler can reference only local files or subfolders, or external modules referenced through the system configuration. Folders may also contain ad-hoc auxilliary content such as tests, readme, signed manifest, or license file.

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

See also *Rational Numbers* and *Floating Point*.

### Lists, Arrays, Queues, Binaries

Sequential structure in glas is usually encoded as a list. A list is as a binary tree where every left node is an element and every right node is a remaining list, and the empty list is a simple leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access arrays, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) under-the-hood. This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special treatment because they are a popular data representation for filesystems and networks. Within glas systems, a binary is canonically represented as a list of small integers (0..255), but the finger-tree rope might allocate binary fragments of many kilobytes or megabytes. Program annotations can provide guidance, e.g. ask a runtime to 'flatten' a binary to control chunk size.

### Accelerated Representations

We can generalize the idea of representing lists as finger-tree ropes to other optimized representations. For example, a compiler could convert known dictionary and variant types into 'structs' and 'enums'. We could optimize vectors and matrices of unboxed floating point numbers. Unordered data types, such as sets or unlabeled graphs, can be assigned a canonical representation as a binary tree (see [graph canonization](https://en.wikipedia.org/wiki/Graph_canonization)) then we can develop accelerated representations that logically reduce to the canonical form.

The runtime can potentially maintain accelerated representations even when serializing data for RPC communication or a persistent key-value database. In case of [glas object](GlasObject.md), this leverages the external reference type. Use of runtime reflection APIs may allow programs to observe actual under-the-hood representations.

### Very Large Values

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization should be transparent modulo explicit use of runtime reflection effects. However, it can be guided by program annotations. 

Content-addressed references simplify memoization, communication, and orthogonal persistence with large values. By leveraging this, entire databases can be presented as massive values. The [glas object (glob)](GlasObject.md) representation is designed to serve as the primary serialization format.

### Abstract Data Types

Data abstraction is an extrinsic property of data, i.e. data is abstract *in context* of a given subprogram. In case of abstract runtime types (such as open file handles), the scope of abstraction might be an entire application. Ideally, data abstraction is enforced by static analysis, eliminating runtime overheads. However, static analysis is difficult in many cases. 

We can extend the data representation with barriers to efficiently detect violation of type assumptions at runtime. These barriers might be manipulated by 'annotations' that wrap and unwrap values with type headers at runtime. This has unavoidable overhead, but it can be acceptable depending on granularity. A compiler might eliminate many wrap-unwrap pairs based on static analysis.

In case of [substructural types](https://en.wikipedia.org/wiki/Substructural_type_system) we might further restrict a subprogram's ability to 'copy' or 'drop' data. Again, this is best enforced by static analysis, but we could 

### Nominative Types

A runtime can support [nominative types](https://en.wikipedia.org/wiki/Nominal_type_system) as an abstract type. Essentially, instead of using bitstrings as 'labels' for a dictionary or variant, we can use database keys or names from a [namespace](GlasNamespaces.md), abstracting over representation details. In context of live coding, orthogonal persistence, and distribution, nominative types must be carefully scoped.

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

The glas system specifies [glas language](GlasLang.md) with file extension ".glas". A glas module compiles to a dictionary of partial [namespaces](GlasNamespaces.md). An application module should define the 'app' namespace, representing an [application object](GlasApps.md). The app namespace should leave abstract a subset of methods `%*` and `sys.*` to be provided by the runtime, corresponding to [abstract assembly](AbstractAssembly.md) and system functions. Application state is bound to an external key-value database with support from the language.

The glas system is extensible via language modules, acceleration, and staging. The initial program and application model is intended to provide a good foundation, but I assume programmers will eventually use problem-specific languages for various parts of a large application.

### Language Modules

As a simple naming convention, global module `lang.xyz` should detail how to compile files with extension ".xyz". The 'app' namespace should define `compile : SourceCode -> ModuleValue`. The SourceCode is usually a file binary, but may in general may be any glas data. The general case appears easily in context of composing file extensions. The compiled module value may be any glas data, but is most often an intermediate representation for a compiled program. 

To ensure reproducible results, the compiler has very limited access to effects. The only observable effect is to is to 'load' compiled values from external modules. For convenience, this extends to staged compilation, treating it as a special module reference.

* `sys.load(ModuleRef)` - On success, returns compiled value of the indicated module. On error, diverges if observing failure would be non-deterministic (e.g. dependency cycle, network fault, resource quota), otherwise fails deterministically. We'll broadly distinguish a few kinds of ModuleRef:
  * *local:Text* - refers to a local file or subfolder, found within the same folder as whatever file is currently being compiled. The Text must not include the dot file extension or folder path separator.
  * *global:Text* - refers to an externally configured value by name. This may be localized within the global namespace, i.e. `global:"foo"` may mean different things to different global modules. However, all local modules would reference the same `global:"foo"`. 
  * *inline:Data* - trivially return Data. Equivalent to staging with an identity function. This is useful only in context of composite ModuleRef options, such as *stage*.
  * *stage:(LangRef, DataRef)* - here LangRef and DataRef are both ModuleRefs. In practice, LangRef might be `global:"lang.foo"` corresponding to file extension `".foo"`, while DataRef is `inline:Text` corresponding to a file body.

Annotations within the compiler can further support logging, profiling, tracing, caching, parallelism, quotas, and hardware acceleration. These effect-like features are not observable by the compiler but may help with performance and debugging.

Long term, I also want to support REPL, linter, syntax highlighting, [language server protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol), interactive tutorials, etc.. In context of staging, it is most convenient to support these features as additional interfaces on language modules. But I'm not in a hurry to develop these integration APIs.

### Automated Testing

As a simple naming convention, local modules whose names start with "test-" will be recognized as tests. Tests can be compiled and evaluated by the glas system to produce or maintain a health report. Each test application may define multiple test methods as `test.*`. A test method should have a simple `unit -> unit | FAIL` type. 

Effects in automated tests are restricted to ensure reproducibility and replayability. In addition to annotations for logging, profiling, caching, etc. we can use `sys.fork` for fuzz testing and search.

* `sys.fork(N)` - returns an integer in the range `0..(N-1)`. Diverges if N is not a positive integer.

The test system is free to leverage abstract interpretation, heuristics, and memory to focus on 'fork' choices that are more likely to result in test failure, such as edge cases or regression tests.

*Note:* Due to limited effects, the test system requires explicit simulation of test environments in many cases. Additionally, true integration testing must use test applications instead of regular test modules.

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

### Proof Carrying Code

I'd like to start supporting proof carrying code and tactics relatively early. This should involve annotations within code, e.g. `(%proof Property Tactics Subprog)`, where both properties and proof tactics can be abstracted via the namespace. Ideally, this is defined in a way that allows for namespace overrides to add proofs modularly to a library or application.

### Graphical Programming

We could develop a language where the file is essentially a database, and there are graphical tools to render and modify the database. This would simplify integration with tables and structured data, graphical representations of behavior, and so on. Incremental compilation over large databases is possible by leveraging *caching* carefully.

### Macro Preprocessing

We could develop generic Text->Text languages, perhaps based on a macro preprocessor with dedicated headers (at the preprocessor layer). I've hinted at this with the ".m4" extension previously. Developing a good preprocessor language could simplify embedded DSLs and syntactic sugars.

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

## Tentative Data Representations

Not really committed to anything here yet.

### Rational Numbers 

Ratios are easily represented as a pair of integers, not necessarily in reduced form. In many cases, such as within a program AST, it is convenient to model rational numbers precisely without conversion to floating point or imprecise types. Where needed, math can include explicit operations in our computations to 'round' rational numbers to another rational.

### Floating Point (Tentative)

This proposed floating point number representation for glas is based on [posits](https://en.wikipedia.org/wiki/Unum_(number_format)#Posit_(Type_III_Unum)) but adjusted for arbitrary length bitstrings. In this modified encoding, every bitstring encodes a unique rational number. Any rational number whose denominator is a power of two can be precisely represented. The zero value is conveniently represented by the empty bitstring. 

To interpret any non-empty bitstring, we'll first add logically a `1000..` suffix (that is a 1 bit followed by infinite 0 bits) then interpret the result as `(sign)(regime)(exponent)(fraction)`. Adding a 0 to the fraction doesn't modify the encoded number, thus in practice we need only add bits up to the fraction. 

Further, to improve how posits scale to much larger numbers, I increase exponent size (es) with regime using a simple pattern:

        regime  es      exponent
        10      2       0..3  
        110     2       4..7
        1110    3       8..15
        11110   4       16..31
        111110  5       32..63
        (1*N)0  N       (2^N)..(2^(N+1)-1)

        01      2       -4..-1
        001     2       -8..-5
        0001    3       -16..-9
        00001   4       -32..-17
        000001  5       -64..-33
        (0*N)1  N       -(2^(N+1))..-((2^N)+1)

The final number is computed as `(-1)^(sign) * 2^(exponent) * (1.(binary fraction))`.

However, I'm not convinced this is a good fit for my vision of glas systems. One significant weakness of arbitrary width floating point is that it becomes unclear how many bits should be kept at any given step. Even initial representation is troublesome: no matter how many bits, we cannot exactly represent decimal number `0.3`. We would need precision arguments on many operations. Also, floating point math is often expected to leverage hardware. 

My vision for glas systems might be better served by favoring rational numbers in glas data, then separately accelerate IEEE 754 floating point arithmetic and matrix computations. Leave conversions to the users.

*Aside:* Floating point arithmetic isn't fully deterministic in the sense that different hardware can have different results for the same operation. This complicates acceleration. We might instead introduce floating point operations as an abstract AST extension or an effect.
