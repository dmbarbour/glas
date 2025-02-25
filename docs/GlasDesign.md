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

However, glas systems will often encode data into stems. Dictionaries such as `(height:180, weight:100)` can be encoded as [radix trees](https://en.wikipedia.org/wiki/Radix_tree). We can encode a zero bit as a left branch, a one bit as a right branch, and encode key text using UTF-8, separating it from data with a NULL byte. An open variant type can be represented as a singleton dictionary. To support these encodings, we must compact stem bits. We might favor something closer to:

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

This allows for reasonable representation of labeled data. We may similarly encode integers into stems. However, we will further extend the Node to efficiently encode embedded binaries and other useful types.

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

Direct representation of lists is inefficient for many use-cases, such as random access, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special handling because they're a very popular type at system boundaries (reading files, network communication, etc.). Logically, a binary is a list of small integers (0..255). For byte 14, we'd use `0b1110` not `0b00001110`. But under the hood, binaries will be encoded as compact byte arrays.

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

## Annotations

Programs in glas systems will generally embed annotations to support *instrumentation, validation, optimization*, and other non-functional features. As a general rule, annotations should not influence observable behavior except through reflection APIs and performance.

A proposed representation in the abstract assembly is `(%an AnnoAST Operation)`, such as:

        (%an (%an.log ConfigChan Message) Operation)
        (%an (%an.assert ConfigChan Expr Message) Operation)
        (%an (%an.type TypeExpr) Operation)

        (%an (%an.nowarn AnnoAST) Operation)

In each case, we annotate an Operation. We might view annotations as a flavored identity function. If an annotation constructor is not recognized by a runtime, we can warn on the first encounter. To suppress warnings in certain cases, we could support composite annotation constructors such as '%an.nowarn'.

Annotations can also be expressed at other layers. In the namespace layer, we might use 'foo.\#doc' for documentation or 'foo.\#test.\*' for a test suite. The runtime will ignore namespace-layer annotations, but they can be useful for external tools. In the data layer, a runtime might record some annotations to support acceleration or dynamic enforcement of abstract and linear types, but a user can only access this through reflection APIs.

### Instrumentation

We might annotate our programs to record some extra information to support debugging. This includes logging, profiling, or recording a computation.

        log (Chan, Message) { Operation }
        prof (Chan, DynamicIndex) { Operation }
        record (Chan, Cond) { Operation }

        (%an (%an.log Chan Message) Operation)
        (%an (%an.prof Chan DynamicIndex) Operation)
        (%an (%an.record Chan Cond) Operation)

For performance reasons, instrumentation can be enabled and disabled based on a static Chan. This could be via application settings and conditional compilation, or statefully via reflection APIs.

The log Message should either be a read-only computation or computable within a hierarchical transaction. Conditional logging is supported by returning an 'empty' message. Logging over an operation is interesting; depending on configuration for Chan, this could be taken as outputting random samples or adding Message to a stack trace. In context of transaction loop applications - with forks and incremental computing - we might render logs to a user as a time-varying tree instead of a message stream.

Profiling should record things useful for understanding performance. Entry and exit counts, time spent, memory allocated, etc.. In context of transaction loop applications, we might keep stats related to stability and incremental computing, aborting on read-write conflict, and so on.

A runtime can also be asked to record information to replay an Operation, and perhaps more based on configuration. Recording can be a convenient alternative to breakpoints in cases where users don't intend to interfere with the computation. We might keep the recording based on whether Cond was true at any point in Operation.

*Note:* Non-deterministic choice in a log message or recording condition might be interpreted as a composition, i.e. set of messages, all conditions are true. For profiling dynamic index, we might heuristically split costs.

### Validation

Annotations can express assertions, type annotations, even proofs. However, type systems and proof-carrying code are deep subjects and I won't touch them here. Assertions are a lot simpler:

        assert(Chan, Cond, Message) { Operation }
        (%an (%an.assert Chan Cond Message) Operation)

We might interpret an assertion over an operation as expressing an invariant. Based on configuration for Chan, this could be randomly sampled and tested again for stack traces, or tested continuously for every change that affects Cond (modulo hierarchical transactions). If Cond is non-deterministic, all conditions must be true.

Use of *static* assertions - plus a few conventions for static channel names - might prove a convenient way to express arbitrary unit tests within a program. Non-deterministic conditions could serve as a basis for fuzz testing.

### Optimization

Annotations will guide most performance features - acceleration, caching, laziness, parallelism, use of content-addressed storage. See *Performance* below. They might also guide JIT compilation and optimizers directly, such as inlining calls.

## Debugging

The runtime can be configured to listen on a TCP port for debugger interactions. The same port may be shared for 'http' and 'rpc' calls. By convention, we might reserve `"/sys/*"` for runtime use. The runtime could even integrate a browser-based debugger. Access to status, recent logs, profiling information, application state, etc. can be provided through this interface. Administrative tools - pause, continue, update, restart, etc. - are also feasible. Authorization for debugger access should be configurable, too.

## Performance

This isn't an exhaustive list, just a few ideas.

### Acceleration

Acceleration is a pattern that lets a runtime introduce 'performance' primitives separately from 'semantic' primitives. For example, instead of directly introducing a new primitive for '%matrix.mul', we might write `(%an %accel.matrix.mul ReferenceImpl)`. A subset of runtimes might recognize this annotation, optionally validate ReferenceImpl, then substitute the ReferenceImpl with a built-in implementation. 

There is plenty of benefit in accelerating matrices, graphs, sets, relational databases (i.e. a dict of lists of dicts), and other types, so long as they are widely useful. However, among the best use cases is accelerated eval of an abstract CPU or GPU. The accelerator can validate memory safety and 'compile' code for actual hardware. If we want cryptography, physics simulations, or LLMs available as 'pure functions' in glas systems, this would be a good approach.

There are caveats. Acceleration of floating point arithmetic is a hassle due to inconsistencies between processors, so we might stick with integers. If necessary, we can still access hardware through an effects API, perhaps indirectly via FFI.

### Laziness and Parallelism

A subset of computations, especially pure functions, can be safely deferred. The deferred computation can later be forced, or sent to a background worker to handle in parallel.

        (%an (%an.lazy.thunk) Expr)        # defer eval of type of Expr, immediately return thunk
        (%an (%an.lazy.force) Expr)        # force evaluation of a thunk, returning the data
        (%an (%an.lazy.spark) Expr)        # add thunk to a pool, eventually will be forced

I assume *explicit* laziness for glas systems. Under this assumption, it would be a type error to 'force' or 'spark' data that is not a thunk, and we can construct have lazy lazy values that must be forced twice. This discourages unnecessary use of laziness, and also reduces the burden for a superbly efficient implementation of laziness. 

Laziness is very troublesome in open systems. To keep it simple, lazy data will be runtime scoped. 

### Content Addressed Storage

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to higher-latency storage (e.g. disk or network). Programmers may use `(%an (%an.cas Hint) BigDataExpr)` annotations to guide this behavior, with Hint potentially suggesting minimum and maximum chunk sizes and other heuristic guidance. The actual conversion may be deferred, performed only when the garbage collector is looking for opportunities to recover memory, or when the hash might be useful for memoization.

In context of serialization - distributed runtimes, database storage, or remote procedure calls - we can lazily fetch fragments of data as needed, supporting incremental upload and download. If we find ourselves repeatedly communicating content-addressed data between runtimes (e.g. via remote procedure calls) we could use integrate content delivery networks to spread the network pressure.

### Memoization

When applying a pure function to immutable data, we can use a secure hash as a lookup key. A persistent memoization table allows sharing work between applications. In glas systems, we'll rely on persistent memoization as a basis for incremental compilation. To work with large data, we should use content-addressed data to reduce the effective size of the argument.

### Pre-Warming

We can evaluate application-layer 'start' and 'step' operations at compile time. The compiler would simulate state, non-deterministic choice, and other runtime features, pausing computation for anything it cannot handle. The compiler can decide based on heuristic space-time tradeoffs whether to include initialized state and partially evaluated transactions in a compiled image. With guidance from application settings, a compiler could also perform a series of 'http' requests and cache the results.

## Thoughts

### Type System

The section on abstract and linear data provides a highly simplified dynamic type system. But I hope to gradually support something more sophisticated. I would hope to track unit types on numbers within applications, for example. And perhaps track ad hoc session types for channels, insofar as we use them. It is unclear to me how to best approach this, other than that it will involve annotations within programs or namespaces to guide static analysis. Beyond types, I hope to explore the more general idea of proof-carrying code, using annotations to integrate proof tactics that can adapt to smaller code changes.

*Note:* Because glas programs are mostly transactional in nature, it's relatively easy to treat dynamic type errors at runtime as divergence, same as an infinite loop, effectively aborting the transaction. 

### Program Search

I'm interested in a style of metaprogramming where programmers express constraints on the program, both hard and soft, then we discover a program that meets these constraints. However, I don't have a good solution for this that ensures a deterministic outcome and supports incremental compilation. At the moment, probably best to leave this to a separate stage and isolate it within the namespace and call-graph?

### Dynamic Types

We can add a little metadata to our data representation to support enforcement of types at runtime. To support abstract data types or 'newtype'-like wrappers, we might extend the Node type:

        type Node =
            | ... # other Node types
            | Abstract of Key * Tree

An optimizer could avoid the extra wrap/unwrap step in cases where type-safety is proven statically. In special cases, some abstract types with certain keys recognized by a runtime could be encrypted when sent over RPC or stored to a shared database.

Abstract types may be scoped to the runtime, forbidding use in remote procedure calls or shared storage. This can be efficiently enforced via [tagged pointers](https://en.wikipedia.org/wiki/Tagged_pointer), with a bit that encodes whether the value is transitively scoped to the runtime. 

Some abstract types may be *linear*, which forbids copy and drop. Linear types can be useful for enforcing protocols, e.g. that open files are closed. As with scoping, linearity can use a tag bit. In practice, all linear types should be scoped because it's impossible to enforce or clean up linearity in an open system. That said, I'd prefer to avoid linearity in glas if there are good alternative.
