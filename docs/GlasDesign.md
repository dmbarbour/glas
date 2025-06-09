# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and the phased liquid-to-solid creation analogous to staged metaprogramming. It can also be read as a backronym for 'general language system', which is something glas aspires to be. Design goals orient around compositionality, extensibility, scalability, live coding, staged metaprogramming, and distributed systems programming. 

Interaction with the glas system is initially through a [command line interface](GlasCLI.md).

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data and align well with needs for parsing and processing languages. They are convenient for persistent data structures via structure sharing, and content addressing for very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

This can generally encode a pair `(a, b)`, a choice `(a + b)`, or a leaf `()`. Alternatively, we could encode these options more directly as a sum type:

        type Tree = 
            | Branch of Tree * Tree
            | Stem of (bool * Tree)  # bool is left/right label
            | Leaf

However, glas systems will often encode data into stems. Dictionaries such as `(height:180, weight:100)` can be encoded as [radix trees](https://en.wikipedia.org/wiki/Radix_tree), encoding the symbol into stem bits with a NULL separator from the data. An open variant type can be represented as a singleton dictionary. To support these encodings, we must compact stem bits. In practice, a runtime may represent arbitrary trees using something closer to:

        type Tree = (Stem * Node)       # as struct
        type Stem = uint64              # encodes 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     # branch point
            | Stem64 of uint64 * Node   # all 64 bits!

        Stem Encoding (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

This allows for reasonable representation of labeled data. We may similarly encode integers into stems. However, we can further extend the Node to efficiently encode text or binary data, struct-like data, and other useful types.

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

Sequential structure in glas is usually encoded as a list. A list is either a `(head, tail)` pair or a leaf node, a non-algebraic encoding similar in style to Lisp or Scheme lists.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()

Direct representation of lists is inefficient for many use-cases, such as random access, double-ended queues, or binaries. To enable lists to serve many roles, lists are often represented under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special handling because they're a very popular type at system boundaries (reading files, network communication, etc.). Logically, a binary is a list of small integers (0..255). For byte 14, we'd use `0b1110` not `0b00001110`. But under the hood, binaries will be encoded as compact byte arrays.

### Optional Data and Booleans

The convention for encoding 'optional' data in glas is to use an empty list for no data, and a singleton list for some data. The convention for encoding Boolean is optional unit, i.e. empty list for 'false' and a singleton list containing the empty list for 'true'.

For 'Either' types, we'll usually switch to symbolic data like `ok:Result | error:(text:Message, ...)`. 

### Numbers, Vectors, and Matrices

Arbitrary rational numbers can be encoded as a dict `(n, d)` of integers. The rational subset of complex or hypercomplex numbers can be modeled as dicts `(r, i)` or `(r, i, j, k)` of rationals or integers. A vector is encoded as a list of numbers. A matrix is encoded as list of vectors or matrices of identical dimensions. Arithmetic operators in glas systems should be overloaded to handle these different number types where it makes sense to do so. 

For performance, a runtime may support optimized internal representations. A useful subset of rationals could be efficiently represented and processed as floating point numbers. Matrices could be implemented as an array with dimension metadata.

*Note:* Arithmetic in glas is exact by default, but there will be workarounds for performance.

## Namespaces and Programs 

A glas system is expressed as a modular [namespace](GlasNamespaces.md) defining *languages, libraries, applications, and adapters*. Typically, a user imports a community or company configuration from DVCS, then overrides definitions to integrate user-specific projects, preferences, and resources. A community configuration may be enormous, defining hundreds of applications; this is mitigated by lazy loading and caching.

The [programs](GlasProg.md) are procedural in nature, but takes inspiration from functional and object-oriented programming. However, to simplify optimizations and live coding, we rely on algebraic effects and metaprogramming instead of first-class functions or objects.

### Languages

The namespace supports user-defined syntax: to load a ".xyz" file, we'll search for '%env.lang.xyz' in the current scope. This serves as a front-end compiler, writing an intermediate representation for programs into the namespace. To get started, the glas executable provides at least one built-in compiler, usually for [".glas" files](GlasLang.md). The built-in compiler is used to bootstrap the user definition if possible.

### Libraries

Shared libraries are a design pattern within the namespace. An application can assume '%env.lib.math.whatever' is already defined. If wrong, the error message is clear and the fix is easy: install the library. By convention, names with prefix '%' are implicitly propagated across imports, and we'll apply a default translation `"%env." => "env."` to the configuration namespace. Thus, we might install a library via import into 'env.lib.math' to share utility code with most applications.

The main advantage of shared libraries is performance, avoiding redundant work across applications. The main disadvantage is customization: the application cannot override library definitions or change its links to other libraries. The disadvantage can be mitigated by translating links to alternative versions of specific libraries within some scope. 

### Applications

A [transaction-loop application](GlasApps.md) can be implemented by defining procedures for 'start', 'step', 'http', etc.. The main loop involves repeatedly running 'step' in separate transactions. The direct implementation is a single-threaded event loop. However, support for incremental computing and a few other optimizations lets this model express concurrent, distributed, reactive systems. The transaction loop is also very convenient for live coding.

Although the transaction loop is intended to be the default for glas systems, a runtime may support alternative modes. For example, staged applications would compile the next-stage application based on command-line arguments and OS environment variables. Applications should define 'settings' to guide integration, including run mode.

### Adapters

Instead of directly observing an application, a runtime should first ask the configuration for an adapter. The configuration can query application settings and runtime version info, then return wrappers for the runtime's algebraic effects, the application interface, perhaps annotations. Use cases include porting applications across runtimes, exploring alternative application models, sandboxing untrusted applications, and overriding application settings based on configuration policies.

Adapters at other layers are still useful. Runtimes can recognize multiple conventions. Applications and libraries can support conditional compilation based on '%env.\*' context or static parameters. But the configuration is the final opportunity to influence integration, and the most convenient opportunity for end users.

*Aside:* We could define some applications with 'settings' alone, treating the adapter as a compiler.

## Distributed Systems Programming

The transaction loop application model greatly simplifies programming of distributed systems. The application can be mirrored on every node, i.e. repeating the same 'step' function, handling 'http' and 'rpc' requests locally, caching or migrating state. The runtime can apply a heuristic optimization: abort a 'step' that is better initiated on another node. Similarly, repeating 'rpc' calls can be redirected to the relevant node.

To fully leverage the distributed system, applications must be architected such that *most* transactions involve only one or two runtime nodes. During a network disruption, the application continues running locally, but some distributed transactions are blocked. This supports graceful degradation as the network fails, and resilient recovery as communication is restored. If necessary, an application may observe disruption indirectly via timeouts or reflection. 

Although transaction loops don't eliminate the need for design, they are flexible and forgiving of mistakes. We can always force a few distributed transactions rather than re-architecting.

## Live Coding

To support live coding, a runtime can be configured to scan for source updates periodically or upon trigger. Sources include local files and remote DVCS resources. After compiling everything, we switch to the new code. For a transaction loop application, 'switch' is the first transaction evaluated in the new code. If switch fails, it can be retried; we continue running the old code until it succeeds. The switch operation is responsible for schema updates if necessary.

In a distributed runtime, one node is be responsible for updates. This role can be configured or determined by consensus algorithms. For isolation of transactions, old code must not observe messages or states produced by new code. However, we can propagate software updates together with the messages and states. In case of network disruption, some nodes might be slower to receive the update.

*Note:* Between incremental computing and acceleration, programmers can also model live coding *within* applications. This may be more convenient in some cases to control the scope and cost of the updates.

## Debugging

The runtime can be configured to listen on a TCP port for debugger interactions. The same port may be shared for 'http' and 'rpc' calls. By convention, we might reserve `"/sys/*"` for runtime use. The runtime could even integrate a browser-based debugger. Access to status, recent logs, profiling information, application state, etc. can be provided through this interface. Administrative tools - pause, continue, update, restart, etc. - are also feasible. Authorization for debugger access should be configurable, too.

## Annotations

Programs in glas systems will integrate annotations to support *instrumentation, validation, optimization*, and other non-functional features. As a general rule, annotations should not influence observable behavior except through reflection APIs and performance.

Proposed representation in abstract assembly: 

        (%an (%an.dbg.log Chan Message) Operation)

We always annotate an Operation. But, within a sequence, we'll often annotate a no-op. If an annotation like '%an.dbg.log' is not recognized, we can report a warning then simply evaluate Operation. To suppress warnings, we could recognize something like `(%an.nowarn (...))`, or suppress control warnings through the configuration.

Annotations can also be expressed at other layers:

* A runtime may encode metadata within data. This metadata should be serializable, e.g. via [glas object](GlasObject.md), thus may be integrated in cache or persistent state. 
* A namespace may encode metadata based on naming conventions, e.g. 'foo.\#doc', and 'foo.\#type'. These annotations would be visible to tools that browse or analyze a namespace.
* The module system may recognize hidden subfolders like ".pki/" or ".glas/". This can contain metadata such as signed manifests and public key certificates, stable GUIDs for location-independence of folders, and cached hints for a theorem prover.

## Instrumentation

Instrumentation is expressed using annotations. This includes logging, profiling, or tracing a computation.

        log (Chan, Message) { Operation }
        profile (Chan, DynamicIndex) { Operation }
        trace (Chan, Cond) { Operation }

        (%an (%an.dbg.log Chan Message) Operation)
        (%an (%an.dbg.prof Chan DynamicIndex) Operation)
        (%an (%an.dbg.trace Chan Cond) Operation)

The log Message should either be a read-only computation or computable within a hierarchical transaction. Conditional logging is supported by returning an 'empty' message. Logging over an operation is interesting; depending on configuration for Chan, this can support random samples or adding Message to a stack trace. In context of transaction loop applications - with forks and incremental computing - we might render logs to a user as a time-varying tree instead of a message stream.

Profiling should record things useful for understanding performance. Entry and exit counts, time spent, memory allocated, etc.. In context of transaction loop applications, we might keep stats related to stability and incremental computing, aborting on read-write conflict, and so on.

If asked to 'trace' an Operation, the runtime may conditionally record enough information to replay that operation in slow motion. This serves as a convenient alternative to breakpoints in many use cases.

In each case 'Chan' is a statically evaluated such that we can selectively disable log messages or assertions, or configure how they are handled over a complex Operation. For even more flexible and precise configuration of debugging, we can also rewrite Chan:

        debug-scope (ChanRewrite) { Operation }
        (%an (%an.dbg.scope ChanRewrite) Operation)

In this case, ChanRewrite should be a `Chan -> Chan` function that we apply at compile time.

*Note:* Non-deterministic choice in a log message or recording condition will be heuristically interpreted as a composition: all conditions, a set of messages, etc.. Depending on configuration of Chan, we could try to evaluate every case or just one choice randomly.

## Performance

Annotations guide performance features - acceleration, caching, laziness, parallelism, JIT compilation, tail-call optimization, use of content-addressed storage, etc..

### Acceleration

There are many functions that are difficult to implement efficiently within the glas program model due to lack of static types or suitable 'primitive' operations. In these cases, we can provide a slower reference implementation, then use an annotation to ask a runtime to replace the reference implementation with a high-performance built-in. Example:

        (%an (%an.accel.matrix.mul "double") ReferenceImpl)

In practice, the reference implementation will alias a separate definition. This allows for users to define automatic tests that compare the reference implementation with the accelerated version and verify consistency. A runtime may also integrate built-in verification, but it will often be limited due to concerns of performance or bloat.

As needed, the runtime shall leverage specialized under-the-hood representations to support accelerated functions. For example, a matrix of floating point numbers might be represented as one large binary together with some metadata for dimensions. An accelerated list might be represented using finger-tree ropes. We could also accelerate structs, arrays of structs, and virtual machine states.

Acceleration adds a fair bit of implementation and verification overhead. It is best to accelerate widely useful types - matrices, graphs, sets, relational databases, etc.. We can accelerate a few specialized functions (e.g. "sha512") to support bootstrap. But ideally we eventually replace any specialized functions with widely useful accelerated 'eval' of memory-safe operations on an abstract CPU or GPGPU.

A relevant concern with acceleration is that not all hardware-supported operations are portable. This is especially the case for floating point computations, e.g. some processors use 80-bit internal representations. In this case, either our ReferenceImpl must account for the target processor (perhaps configured via '%env.arch.\*') or the accelerated implementation must trade some performance for portability.

*Aside:* We may need logical [graph canonization](https://en.wikipedia.org/wiki/Graph_canonization) to accelerate unlabeled graph structures. The under-the-hood representation would not be canonical.

### Laziness and Parallelism

A subset of expressions are purely functional or read their environment without modifying it. In these cases, we can capture a snapshot of the relevant environment, then defer the computation until it is needed, or evaluate in parallel between transactions. Of course, the snapshot and indirection does introduce some overhead, so this is most suitable for relatively expensive computations.

        (%an (%an.lazy.thunk) Expr)     # defer eval of type of Expr, returns abstract thunk
        (%an (%an.lazy.force) Thunk)    # force evaluation of a thunk, returning the data
        (%an (%an.lazy.spark) Thunk)    # adds thunk to a thread pool, force in background

This API makes thunks explicit. Alternatively, we could support thunks that are implicitly forced when we attempt to observe a value. However, for my vision of glas systems, it's more convenient if laziness is explicit and well-integrated with the type system. Thunks can be runtime-scoped, and it's an error to thunk an Expr that has observable effects.

### Content-Addressed Storage

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to higher-latency storage (e.g. disk or network). 

        (%an (%an.cas.stow OptionalHints) BigDataExpr)
        (%an (%an.cas.load) StowedData)

Use of 'stow' doesn't immediately write the value to disk. It may wrap or associate the data with a little runtime metadata and cache, deferring actual storage guided by hints and heuristics. For example, we could wait for actual memory pressure, letting the garbage collector decide when to store the data and remove it from memory. Data below a size threshold may be kept in local memory regardless.

Use of 'load' is provides access previously stowed data. It is feasible to support transparent stowage with implicit 'load', but it's inefficient to check for content-addressed representations on every arithmetic operation. That said, we could support specialized variants for implicit load of finger-tree ropes and such, integrating with accelerated representations.

Content-addressed data interacts very nicely with memoization, orthogonal persistence, and distributed programs where large but infrequently-updated data structures are passed around (e.g. audio or video media, context dictionaries). This also integrates very easily with content delivery networks. 

### Caching

When applying a pure function to immutable data, we can use a secure hash as a lookup key. A persistent memoization table allows sharing work between applications. This isn't optimal - we could be including features of the data that aren't observed by the function - but it's a very simple basis for work sharing, and users can apply some extra processing to isolate relevant input prior to memoization.

In glas systems, we'll rely on persistent memoization as a primary basis for incremental compilation. To work with large data, we should use content-addressed data to reduce the effective size of the argument.

### Program Rewrites? Defer.

Mapping two *pure* functions over a list, e.g. `map f . map g`, is equivalent to mapping the composite function, `map (f . g)`. For map-reduce over a list, we can parallelize and distribute computation if the sum operation is associative. There are many similar observations on programs. However, it's difficult to *prove* such optimizations are safe. 

What can be done? One viable option is metaprogramming, letting users build a DSL that performs the optimizations when compiled further. Another is proof-carrying code, extending the program with proof hints. We could also use annotations as a sort of "trust me, bro" to the compiler, insisting a function is associative or commutative or monotonic or whatever without a proof. In the latter case, trust may be contingent on PKI, similar to application access to FFI.

At the moment, I won't pursue these optimizations too far at the runtime layer, leaving it to DSLs and metaprogramming. But it's certainly an area where we could obtain some significant returns on investment as the glas system matures.

### Warmed Applications

We can evaluate application-layer 'start' and 'step' operations at compile time, insofar as they don't immediately await response from FFI or other external sources. The compiler would simulate state, non-deterministic choice, and other runtime features, pausing computation for anything it cannot handle. The compiler can decide based on heuristic space-time tradeoffs whether to include initialized state and partially evaluated transactions in a compiled image. With guidance from application settings, a compiler could also perform a series of 'http' requests and cache the results.

## Validation

Annotations can express assertions, type annotations, even proofs. I'll explore some of our opportunities here.

### Assertions and Automatic Testing

Assertions are by far the simplest form of validation.

        assert(Chan, Cond, Message) { Operation }
        (%an (%an.dbg.assert Chan Cond Message) Operation)

We might interpret an assertion over an operation as expressing an invariant. Based on configuration for Chan, Cond can be randomly sampled, automatically tested upon stack trace, or tested continuously for every relevant change in state. To avoid influencing observable behavior, Cond can be a read-only function or evaluated within a hierarchical transaction. When an assertion fails, we log the message and halt the transaction.

If Cond is non-deterministic, we'll interpret that as conjunction: every possible condition *should* hold true. However, for performance reasons, we might not evaluate them all every time: we could randomly or heuristically sample several conditions each time we encounter the assertion. This may depend on configuration of Chan.

In context of staged computing or partial evaluation, we can express static assertions. These may be evaluated before the application runs and effectively serve as unit tests. With non-deterministic conditions, we also get fuzz testing. In the glas program model, some staging and partial evaluation is aligned with the call graph via static parameters. This allows for some custom testing specific to the integration.

### Abstract Data Types

Annotations can express that data should be abstract within a computation. However, it isn't always convenient to enforce types via static analysis. To support dynamic enforcement of abstract data types, we can extend the Node type:

        type Node =
            | ... # other Node types
            | Abstract of Key * Tree

Based on type annotations, a runtime can wrap and unwrap data with this 'Abstract' node. Any attempt to observe abstract data without unwrapping, or an attempt to unwrap with the wrong key, will will raise a runtime type error. This would be treated similar to an assertion failure.

Based on static analysis, an optimizer can eliminate many of these wrap/unwrap actions, or at least skip some key comparisons. Based on further annotations, we could insist that some abstract types are fully eliminated within some scope, raising a compile-time error if this is not true. This provides a lightweight basis for gradual typing.

### Scope Control Types

In context of remote procedure calls and shared databases, it is often useful to control the scope of data. For example, we don't want open file handles escaping the runtime boundary. Scopes are most easily expressed as an extension to abstract types, such as files. For dynamic enforcement, we might represent runtime scope as an extra flag on the Abstract node's Key.

However, efficient dynamic enforcement of scopes benefits from a O(1) lookup. Every node should cache metadata for whether it transitively includes runtime-scoped data. To support this efficiently, we could use [tagged pointers](https://en.wikipedia.org/wiki/Tagged_pointer), albeit only for a very small number of scopes.

Fortunately, we don't need many scopes to cover most use-cases in glas systems. A useful hierarchy of scopes:

* global scope - can send or receive over RPC
* shared scope - can read or store to shared database
* runtime scope - open files, network sockets
* transaction scope - RPC objects, namespace refs

Whether we need all these scopes depends on the application API. For example, database scope is necessary only if we want abstract database references as first-class values within the database. Note that *transaction scope* is compatible with incremental computing and partial evaluation. It may be computed before a transaction, but it cannot leave a transaction.

### Linear Types

Linear data is abstract data that cannot be arbitrarily copied or dropped. This is useful when modeling resources, protocols, or promises. Linear types are potentially useful to ensure a transaction is 'complete' upon commit, i.e. to check there are no unfulfilled promises. Like scope, linear types can be expressed as a flag on abstract data then enforced efficiently using tagged pointers. 

Linear types are extremely awkward in open systems: they cannot be enforced, and they shouldn't be enforced - it's unclear how to clean up after an application dies mid-protocol. At runtime scope, linear types interact awkwardly with transaction-loop optimizations, such as incremental computing: we're forced to repeatedly read the linear data from state, observe or manipulate it, write it back to state. The best opportunity for linear types is at transaction scope, to enforce that transaction-local protocols are completed before the transaction commits.

### Units on Numbers?

I want to express physical units on numbers - kilograms, newtons, meters, joules, etc. - and enforce safe use of units. However, I'm not certain of the best approach. Some options:

* *staged computing* - model units as a static parameter and result. Likely to be awkward syntactically, but perhaps front-end language support can mitigate this. A big advantage compared to annotations is that this makes units accessible for 'print' statements and such.
* *enum in accelerated number rep* - we're likely to accelerate our number types. It isn't too expensive to add an enum to this representation for units, covering most units encountered in practice, and verify across the basic arithmetic operations. This is probably the simplest short-term solution, though units would only be visible through a reflection API.
* *static analysis* - add units to our type annotations, analyze at compile time. I'm reluctant on this option, mostly because I want to put off static analysis, but I want support for units relatively early.

I'll need to think on this further. 

### Proof-Carrying Code?

I'm curious how well proofs can be supported via systematic annotations within programs.

A reasonable question is what a 'proof' should look like. We could support some sort of user-defined reflective judgement over an AST and call graph, ideally while abstracting names and the namespace. No need to prove the prover works or terminates in general. We can let users define ever more provers to their own satisfaction.

These judgements might be extended with an opportunity to annotate the AST or call graph for future passes or future proofs.

## Misc. Thoughts

### Program Search

I'm interested in a style of metaprogramming where programmers express constraints on the program, both hard and soft, then we discover a program that meets these constraints. However, I don't have a good solution for this that ensures a deterministic outcome and supports incremental compilation. At the moment, probably best to leave this to a separate stage and isolate it within the namespace and call-graph?
