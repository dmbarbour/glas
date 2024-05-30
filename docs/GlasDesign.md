# Glas Design

Glas was named in allusion to transparency of glass, human mastery over glass as a material, and phased or 'staged' creation with glass (liquid and solid). 

Design goals for glas include compositionality, extensibility, reproducibility, modularity, metaprogramming, and live coding. Compared to conventional languages, there is much more focus on compile-time computation and staging. To support live coding, the default [application model](GlasApps.md) is also non-conventional.

## Command Line Interface (CLI)

The glas system starts with a command line tool 'glas'. This tool has built-in knowledge for compiling glas modules and running glas applications. Relevant to my design goals, the command line language is also very extensible through staged computing and definition of 'glas-cli-\*' modules. Users can effectively define their own command line languages. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax Extension

Modules are represented by files and folders. Every valid module compiles to a glas value (see *Data*). In most cases these values represent *Programs and Applications*, but data modules are also useful and can be referenced as constants.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will first compile a global module named lang-ext, which must define a compilation function, then apply this compilation function to the file binary. To ensure a reproducible outcome, the compiler function has very limited access to effects. To bootstrap this system, a compiler for ".glas" files is built-in to the CLI, and we'll first compile lang-glas if possible.

File extensions may be composed. For example, "example.json.m4.gz" would essentially apply a pipeline of three compilers: lang-gz then lang-m4 then lang-json. This might decompress the file binary, apply a text macro preprocessor, then parse the result as JSON. Conversely, if file extensions are elided the compiled value is the unmodified file binary.

A folder is compiled to the value of its contained "public" file, which may have any extension. Folders are implicit dependency boundaries: due to limitations on effects, the compiler can only reference local files or subfolders and global modules configured with the CLI. Further, folders may contain ad-hoc auxilliary content such as tests, readme, license, or a cryptographically signed manifest. All global modules are represented by folders. 

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are relatively convenient for persistent data structures and content addressing of very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)`, either type `(a + b)`, or unit `()`. 

However, glas systems often favor labeled data as more human meaningful and openly extensible. Labels can be encoded into a *path* into the tree, where each path encodes a symbol using null-terminated UTF-8. For example, label 'data' would be encoded into the path `01100100 01100001 01110100 01100001 00000000` where '0' and '1' respectively represent following the left or right branch. 

A dictionary such as `(height:180, weight:200)` may have many such paths, implementing a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). An open variant would instead have exactly one label.

To efficiently represent labeled data, non-branching path fragments must be compactly encoded. A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encoding 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     // branch point
            | Stem64 of uint64 * Node   // all 64 bits

        Stem Encoding (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

We will further extend the Node type to support efficient Lists and other *Accelerated Representations* as described below. Further, a programming language may abstract over some representations, and enforce abstractions. However, the general idea is that data is represented as a binary tree.

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

We can generalize the idea of representing lists as finger-tree ropes to support for unboxed floating point matrices, unordered data types (e.g. sets, unlabeled graphs), or even virtual machine states (with registers, memory, etc.).

In each case, we first define a canonical representation as a binary tree. For example, a matrix could be a list of lists. A set can be represented by an ordered list. An unlabeled graph might refer to [graph canonization](https://en.wikipedia.org/wiki/Graph_canonization). A VM state might use a simple record.

However, constructing, validating, manipulating, and maintaining the canonical representation is expensive and error-prone. Instead, the runtime provides specialized under-the-hood representations, and users manipulate that representation indirectly through built-in or accelerated functions. Use of abstract data types can guard against accidental conversion to the canonical representation.

The runtime can maintain accelerated representations even when serializing data for RPC communication or a key-value database. In case of [glas object](GlasObject.md), this leverages the external reference type. Use of runtime reflection APIs may allow programs to observe actual under-the-hood representations.

### Very Large Values

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization should be transparent modulo explicit use of runtime reflection effects. However, it can be guided by program annotations. 

Content-addressed references simplify memoization, communication, and orthogonal persistence with large values. By leveraging this, entire databases can be presented as massive values. The [glas object (glob)](GlasObject.md) representation is designed to serve as the primary serialization format.

### Abstract Data Types

Data abstraction is an extrinsic property of data, i.e. data is abstract *in context* of a given subprogram or even an entire application. Ideally, data abstraction should be enforced via static analysis and have zero runtime overhead. For example, we could check that a subprogram only observes or constructs specific data structures indirectly through provided methods. 

Unfortunately, static analysis isn't always feasible. In these cases, a runtime might dynamically enforce data abstraction, guided by program annotations. Efficient dynamic enforcement may involve integration with the runtime data representation, e.g. to 'wrap' data with a type header (perhaps a name or database key). Similarly, [substructural types](https://en.wikipedia.org/wiki/Substructural_type_system) might be enforced using 'copyable' and 'droppable' flags in the data.

### Nominative Types

It is feasible to support [nominative types](https://en.wikipedia.org/wiki/Nominal_type_system) in context of a namespace. Instead of indexing binary trees via bitstrings, we can index abstract types using names. To integrate with live coding, orthogonal persistence, and distributed systems we should favor database keys as the foundation for these names instead of the program namespace (see *Programs and Applications*). 

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

The glas system specifies [glas language](GlasLang.md) with file extension ".glas". A glas module compiles to a dictionary of partial [namespaces](GlasNamespaces.md). An application module should define the 'app' namespace, representing an [application object](GlasApps.md). The app namespace should leave abstract a subset of methods `%*` and `sys.*` to be provided by the runtime, corresponding to [abstract assembly](AbstractAssembly.md) and system functions. Application state is bound to an external key-value database with support from the language.

The glas system is extensible via language modules, acceleration, and staging. The initial program and application model is intended to provide a good foundation, but I assume programmers will eventually use problem-specific languages for various parts of a large application.

### Language Modules

As a simple naming convention, global module 'lang-xyz' should detail how to compile files with extension ".xyz". The 'app' namespace should define `compile : SourceCode -> ModuleValue`. The SourceCode is often a file binary, though this may vary when composing file extensions or reusing the language module in other contexts. The compiled ModuleValue can be any plain old glas data.

To ensure reproducible results, the only observable effect a compiler has access to is to 'load' modules.

* `sys.load(ModuleRef)` - On success, returns compiled value of the indicated module. On failure, may diverge or fail on error. Divergence is used only where observing failure would be non-deterministic, e.g. in case of a dependency cycle or network fault. ModuleRef:
  * *local:Text* - identifies a subfolder or file (modulo extensions). 
  * *global:Text* - identifies a global module from the configuration.

Of course, the compiler also has full access to logging, loading, caching, acceleration, and other useful 'effects' that are accessible via annotations.

Long term, we'll also want support for REPL, linter, syntax highlighting, [language server protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol), interactive tutorials, etc.. These might be introduced with similar naming conventions.

### Automated Testing

As a simple naming convention, local modules whose names start with "test-" will be recognized as tests. Tests can be compiled and evaluated by the glas system to produce or maintain a health report. Each test application may define multiple test methods as `test.*`. A test method should have a simple `unit -> unit | FAIL` type. 

Effects in automated tests are restricted to ensure reproducibility and replayability. This includes the effects from language modules, plus `sys.fork` for fuzz testing and search.

* `sys.fork(N)` - returns an integer in the range `0..(N-1)`. Diverges if N is not a positive integer.

The test system is free to leverage abstract interpretation, heuristics, and memory to search for 'fork' choices that are more likely to result in test failure, such as edge cases or regression tests.

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

Ratios are easily represented as a pair of integers, not always in reduced form. In many cases, such as within a program AST, it is convenient to model rational numbers precisely without conversion to floating point or imprecise types. Where needed, math can include explicit operations in our computations to 'round' rational numbers to another rational. 

### Floating Point

This proposed 'default' floating point number representation for glas is based on [posits](https://en.wikipedia.org/wiki/Unum_(number_format)#Posit_(Type_III_Unum)) but adjusted for arbitrary length bitstrings. In this modified encoding, every bitstring encodes a unique rational number. There is no support for not-a-number. Any rational number whose denominator is a power of two can be precisely represented. The zero value is conveniently represented by the empty bitstring. 

As with posits, we interpret the bitstring as `(sign)(regime)(exponent)(fraction)`, with negatives using sign bit '1'. Unlike posits, there is no limit on regime or fraction size, and we logically add a `1000..0` suffix to the bitstring before interpreting it. That is a '1' bit followed by however many '0' bits are needed to reach the fraction. Further, to scale efficiently to large exponents, I increase exponent size (es) with regime:

        regime  es      exponent
        10      2       0..3        
        110     2       4..7
        1110    3       8..15
        11110   4       16..31
        111110  5       32..63
        ...

        01      2       -4..-1
        001     2       -8..-5
        0001    3       -16..-9
        00001   4       -32..-17
        000001  5       -64..-33
        ...

The final number is computed as `(-1)^(sign) * 2^(exponent) * (1.(binary fraction))`.

I hesitate to standardize this mostly because I don't see a strong use case for floating point at the 'glas' program layer. I suspect we'll mostly favor rational numbers and ratios, and when we do want floating point it will be in context of an accelerator, in which case we'll need to favor a format supported by the hardware.
