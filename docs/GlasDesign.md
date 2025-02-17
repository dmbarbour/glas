# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and the phased liquid-to-solid creation analogous to staged metaprogramming. It can also be read as a backronym for 'general language system', which is something glas aspires to be. Design goals orient around compositionality, extensibility, scalability, live coding, staged metaprogramming, and distributed systems programming. 

Interaction with the glas system is initially through a command line 'glas' executable. See [Glas CLI](GlasCLI.md).

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

### Abstract and Linear Data

Data is abstract in context of a subprogram that does not directly observe or construct that data. Abstract data may be linear insofar as the subprogram further does not directly copy or drop the data. Technically, these are extrinsic properties of context, not intrinsic properties of data. However, it can be useful to integrate some metadata to simplify runtime enforcement.

To support abstract data, we can simply extend the Node type:

        type Node =
            | ... # other Node types
            | Abstract of Key * Tree

The runtime may recognize annotations to wrap and unwrap data. Attempting to observe abstract data without first unwrapping it would be a runtime type error, which will typically abort the current transaction. An optimizer can eliminate unnecessary wrap-unwrap pairs. Intriguingly, it is feasible to cryptographically enforce abstractions across trust boundaries, though for performance and debugging this should be limited to specialized keys.

To support linearity, we could leverage [tagged pointers](https://en.wikipedia.org/wiki/Tagged_pointer) to efficiently encode a metadata bit for whether each node is transitively linear. At runtime, we can easily check this bit before we copy or drop data. Linear types are very convenient for modeling open files, sockets, channels, futures and promises, and so on - anything where we might want to enforce a protocol. Aside from runtime use, users could mark abstract data linear upon 'wrap'.

*Note:* To simplify reasoning in open systems, abstract linear data will also be scoped to each runtime. That is, it cannot be stored in a shared database or transferred via remote procedure calls.

## Programs and Applications

A glas system is expressed as an enormous [namespace](GlasNamespaces.md), customized per user. A typical user configuration inherits community or company configurations via import from DVCS, then integrates user-specific preferences, projects, resources, and authorizations. The namespace is essentially an intermediate representation, with definitions in the [glas program model](GlasProg.md).

The [glas command line interface](GlasCLI.md) can run applications defined within the user's namespace, or use 'languages' defined in the namespace to load scripts or command text as applications. Applications may also be 'installed' to make them available for offline use.

Most applications in glas systems will use the [transaction-loop application model](GlasApps.md), defining 'start', 'step', 'http', and other interfaces. The main loop is externalized, with the runtime repeatedly calling 'step', and calling 'http' between steps to handle occasional requests. This model is especially friendly for live coding, and it can be optimized in many useful ways. But even without those optimizations, we can express a single-threaded event dispatch loop.

Alternative run modes are also supported, such as staged applications. Every application should define 'settings' to influence application-specific configuration of the runtime including run mode, logging, profiling, quotas, and other features.

### Syntax

The glas system supports user-defined syntax aligned with file extensions. This is inextricably entangled with the namespace model: when we 'load' a namespace module, we'll select a procedure '%env.lang.FileExt' from the namespace to 'compile' the binary into even more definitions. Via translation rules, we could set '%env.\*' within a scope, influencing 'load'.

The glas executable include at least one built-in compiler for [glas language, ".glas" files](GlasLang.md). We'll use this if '%env.lang.glas' is undefined, or to bootstrap if self-referentially defined. Most other languages will ultimately be defined in terms of ".glas" code.

## Distributed Programming

Instead of running an application as an operating system process, we can configure a distributed runtime that overlays remote machines or processes. This is an especially good fit for the transaction loop application model. We can mirror the same 'step' and 'http' and other interfaces on every node, and due to the nature of repeating transactions, the result is one big application that can offer degraded service during network failure and recover resiliently when the network is re-established.

Of course, we still need to design the application to be partitioning tolerant, i.e. arranging for most transactions to run on a single node, and favoring queues, bags, or CRDTs for asynchronous communication between nodes. But the transaction loop simplifies the problem.

## Live Coding

We can update a transaction loop application atomically between steps. With most modules in DVCS, we can apply updates to multiple files atomically. We can also add some 'switch' behavior to control when updates are applied and help with any state or schema updates. 

To further simplify live coding, the glas program model avoids state-code entanglement, e.g. no first-class functions or objects, no spawned threads. Even channels are avoided because they're awkward for schema updates. Alternative solutions are supported for the roles these usually fulfill.

My long term vision is that our user-defined syntax will generate a [notebook view](GlasNotebooks.md) of the application, providing a projectional editor for every source file, with live coding as users make edits. But live coding through external tools is still useful.

## Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, testing, debugging, static analysis, caching, acceleration, parallelism, and other non-semantic features. As a general rule, annotations should not influence observable behavior except through reflection APIs. Thus, a runtime can safely ignore unrecognized annotations. 

A proposed representation (in the abstract assembly) is `(%an AnnoAST Operation)`, such as:

        (%an (%log ConfigChan Message) Operation)
        (%an (%assert ConfigChan Expr Message) Operation)
        (%an (%type TypeExpr) Operation)

In each case, we annotate Operation with a given AnnoAST. If Operation is omitted, it defaults to a no-op. The set of recognized annotations is extensible. If unrecognized, a simple warning once per tag is sufficient. Where users don't want a warning, we could disable warnings with: `(%an (%an.nowarn AnnoAST) Operation)`.

We can also express annotations in the namespace layer, e.g. 'foo.\#doc' or 'foo.\#test.\*'. Also in the data layer, e.g. the `Abstract of Key * Tree` mentioned prior is an annotation. 

### Automatic Testing

Tests can be expressed within programs using static or runtime assertions, and within the namespace via 'foo.\#test.\*'. Tests may leverage non-deterministic choice as a basis for fuzz testing.

        assert(Chan, Cond, Message) { Operation }

It is feasible to express invariants as assertions *over* an operation. We could test this at start and end, randomly in the middle, upon error for the stack trace, etc.. depending on how we configure assertions on Chan. In a debugging context, we could try to isolate where a condition went from true to false within the operation.

### Logging, Profiling, Recording

        log (Channel, Message) { Operation }
        prof (Channel, DynamicIndex) { Operation }

As with assertions, logging *over* an operation is convenient for many use cases. We could potentially 'watch' the log message over the course of the operation, or add it to a stack trace. For profiling, the operation is almost essential (unless we're just counting entries). Profiling can be understood as a specialized form of logging, e.g. we could include some profiling stats (like duration) in a log, but we can't usefully include diverse messages in a profile.

*Aside:* In context of transaction loop applications with incremental computing and branching upon non-deterministic choice, it might be more useful to render logs as a time-varying, scrubbable tree instead of as a text stream.

### Recording

        record (Channel, Cond) { Operation }

Instead of ad hoc user-defined messages, consider conditionally retaining data to replay certain computations in slow motion. Recording could be configured per Channel, e.g. how many records to keep, how often to record, how the condition is interpreted (true on entry, rising or falling edge), and so on. For debugging purposes, such records might prove more convenient than logging or breakpoints. 

## Debugging

The transaction loop applications often define 'http' and 'rpc', and a socket is opened to handle these requests. But the runtime will intercept some requests, e.g. HTTP requests to `"/sys"` (by default). This can provide a reflective view of the runtime - logs, profiles, disassembly, and so on. It can provide some generic administrative tools: pause, continue, update, restart, etc..

Both browsers and external debugger tools could attach interact with the application through this interface. Some applications might also support internal debuggers, which may use the same reflection interface from within the application, perhaps 'sys.refl.http'. Of course, reflective 'rpc' might prove more efficient.

## Performance

### Acceleration

Acceleration is a pattern that lets a runtime introduce 'performance' primitives separately from 'semantic' primitives. For example, instead of directly introducing a new primitive for '%matrix.mul', we might write `(%an (%accel.matrix.mul) ReferenceImpl)`. A subset of runtimes might recognize this annotation, optionally validate ReferenceImpl, then substitute the ReferenceImpl with the accelerator, e.g. '%internal.matrix.mul'. A simple link rule `{ "%internal." => NULL }` can block users from directly referencing internal primitives, leaving it for the internal optimizer only.

There is plenty of benefit in accelerating matrices, graphs, sets, relational databases, and other types, so long as they are widely useful. However, among the best use cases is accelerated simulation of an abstract CPU, GPGPU, or other low-level models. The runtime can 'compile' simplified code for the actual GPGPU (or CPU). This is much safer than embedded assembly or FFI, and can easily be used within a 'pure' function, which is convenient for memoization. If we want cryptography, physics simulations, or LLMs in glas systems, this is a good approach.

### Laziness and Parallelism

        (%an (%lazy.thunk) Expr)        # return thunk immediately; restricts type of Expr
        (%an (%lazy.force) Expr)        # if Expr returns shallow thunk, force evaluation
        (%an (%lazy.force.deep) Expr)   # force all thunks within data returned by Expr
        (%an (%lazy.spark) Expr)        # put thunk in a queue to be forced by worker thread

A subset of computations, especially pure functions, can be safely deferred. The deferred computation can later be forced, or sent to a background worker to handle in parallel. Laziness is very troublesome in open systems, so we might implicitly force computations prior to serialization for remote procedure calls or persistent storage.

### Memoization

When applying a pure function to immutable data, we can produce a secure hash as a lookup key the computation as a whole. A simple lookup table can let us find the result. In glas systems, we'll configure persistent memoization tables to support incremental compilation and many other use cases.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to higher-latency storage (e.g. disk or network). This optimization can be guided by program annotations. In context of serialization - distributed runtimes, database storage, or remote procedure calls - we can lazily fetch fragments of data as needed, supporting incremental upload and download. There are also benefits for memoization, allowing us to work with large values as 'keys'.

In some cases, we might want to distribute content-addressed data via [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network) to reduce the local network burden. 

## Thoughts

### Type System

The section on abstract and linear data provides a highly simplified dynamic type system. But I hope to gradually support something more sophisticated. I would hope to track unit types on numbers within applications, for example. And perhaps track ad hoc session types for channels, insofar as we use them. It is unclear to me how to best approach this, other than that it will involve annotations within programs or namespaces to guide static analysis. Beyond types, I hope to explore the more general idea of proof-carrying code, using annotations to integrate proof tactics that can adapt to smaller code changes.

*Note:* Because glas programs are mostly transactional in nature, it's relatively easy to treat dynamic type errors at runtime as divergence, same as an infinite loop, effectively aborting the transaction. 

### Program Search

I'm interested in a style of metaprogramming where programmers express both hard and soft constraints on the program, and the program includes sufficient code to adapt to contexts and user preferences. Depending on the design, this might also support working with 'ambiguous' definitions, allowing programmers to be more vague and still get a useful program.

Some ideas: We could introduce AST nodes for 'static' non-deterministic choice within a call graph. We can reject some solutions based on static asseertions, and prioritize others that have a lower 'weight'. Weights can be abstracted into arbitrary values that we 'interpret' into positive rational numbers according to user preference, e.g. an 'experimental' tag might weigh 0.01 for one user and 100 for another. By applying something similar to an A* search, it should be feasible to discover the 'best' solution by weight, and we can ensure this solution is deterministic.

But I'm not convinced that a one-size-fits-all solution at the toplevel is the right approach here. Perhaps more explicit staging of program search would be easier for programmers to isolate, control, and stabilize in context of incremental compilation. 
