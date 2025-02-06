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

### Abstract, Linear, and Scoped Data

Data is abstract in context of a subprogram that does not directly observe or construct that data. Abstract data may be linear insofar as the subprogram further does not directly copy or drop the data. Technically, these are extrinsic properties of context, not intrinsic properties of data. However, it can be useful to integrate some metadata to simplify runtime enforcement.

To support abstract data, we can simply extend the Node type:

        type Node =
            | ... # other Node types
            | Abstract of Key * Tree

The runtime may recognize annotations to wrap and unwrap data. Attempting to observe abstract data without first unwrapping it would be a runtime type error, which will typically abort the current transaction. An optimizer can eliminate unnecessary wrap-unwrap pairs. Intriguingly, it is feasible to cryptographically enforce abstractions across trust boundaries, though for performance and debugging this should be limited to specialized keys.

To support linearity, we could leverage [tagged pointers](https://en.wikipedia.org/wiki/Tagged_pointer) to efficiently encode a metadata bit for whether each node is transitively linear. At runtime, we can easily check this bit before we copy or drop data. Linear types are very convenient for modeling open files, sockets, channels, futures and promises, and so on - anything where we might want to enforce a protocol. Aside from runtime use, users could mark abstract data linear upon 'wrap'.

I propose to conflate linear types and runtime scope. That is, linear data cannot be stored in a persistent database or communicated through remote procedure calls. This neatly avoids the troublesome challenges of enforcing linearity in open systems and cleanup after a foreign source of linear objects vanishes from the open system.

## Programs and Applications

A glas system is expressed as one enormous namespace, customized per user. A typical user configuration composes community and company configurations via import from DVCS, then integrates user-specific preferences, projects, resources, and authorizations. Clever binding to DVCS version tags or hashes serves roles in version control, community curation, package managers, and software update. To effectively work with such large namespaces, the [namespace model](GlasNamespaces.md) is carefully designed to support lazy loading and caching.

Within the namespace, definitions are represented using a Lisp-like intermediate representation called abstract assembly, where every AST constructor is a name. This enables extension and restriction of AST nodes through the namespace. Primitive AST constructors are ultimately provided by the runtime, prefixed with '%' for easy recognition and routing. For example, the runtime might define '%i.add' for integer arithmetic, '%cond' for conditional behavior, and '%seq' for procedural composition. See the [glas program model](GlasProg.md).

The [glas CLI](GlasCLI.md) can run applications defined within the configuration namespace, and also compile a file or command-line text into an application. Thus, to 'install' applications, users may choose flexibly between extending the namespace or maintaining a folder of scripts. Aside from directly running applications, it is feasible to reflect on the namespace to extract an executable binary.

To support my vision for glas systems, I develop a [transaction-loop application model](GlasApps.md) as the default for glas applications. In this model, an application defines a transactional 'step' method that we'll evaluate repeatedly. The application will handle 'http' requests and other transactional events between steps. Performance of a transaction loop application relies on relatively sophisticated optimizations: incremental computing of a stable prefix, parallel evaluation of stable non-deterministic choice. Without these optimizations, we're limited to a simple event dispatch loop.

The glas CLI will support at least two run modes: staged applications and transaction loop applications. Instead of directly running applications, we'll first configure the runtime. Runtime options are defined in the user's configuration, but application-specific options may be contingent upon a query to application settings. For example, we might log to stderr by default, yet configure logging differently for a self-described "console app". Run mode is configured the similarly.

## User-Defined Syntax

The glas system supports user-defined syntax. Whenever we 'import' a file into our namespace, we'll automatically select a compiler from the current namespace based on file extension. The namespace schema might be simple "%.lang.xyzzy" for file extension ".xyzzy". This assumes a convention using "%." prefix for implicit parameters within the namespace. 

To get this system started, the glas executable provides a built-in compiler for at least one 'initial' syntax. The root namespace file must be written using an initial syntax. I propose [glas language, ".glas" files](GlasLang.md) in this role. However, initial syntax will be bootstrapped as part of runtime configuration, if possible, replacing with an implementation defined in the user's namespace and verifying a fixpoint is reached after a few iterations.

Because access to the compiler is routed through the namespace, it is feasible to locally extend languages within scope community or project. This is convenient for experimental features, project specific DSLs, or precise control of versions. However, it complicates sharing of code, requiring a compatible environment. Thus, it's preferable that file extensions are relatively stable.

*Note:* Compilers are modeled as applications with a staged run mode. In addition to providing a procedure for compiling source code, this application may implement ad-hoc interfaces to support syntax highlighting, auto-complete, interactive development and debugging, browsable documentation, or even an interactive tutorial on the language.

*Note:* To simplify integration with syntax highlighting, auto-complete, live coding, and so on, a file with multiple extensions such as ".tar.gz" is treated as one large extension. Attempting to compose interfaces automatically seems doomed to fail!

## Distributed Runtimes

My vision for glas systems involves live coding of distributed systems. The distributed runtime is how I propose to support this. A runtime can be configured to run on multiple machines. In context of the transaction loop application model, a distributed transaction may start anywhere. We can mirror an application, repeat the same transaction everywhere, then - as a performance heuristic - abort a repeating transaction that starts by accessing a remote resource. 

A queue can be written by one transaction and concurrently read by another without serializability conflict. We can use queues, bags, CRDTs, and similar intermediate state for asynchronous communication within an application. With some design effort, an application can arrange for most transactions to evaluate on a single node, yet distributed transactions remain available where convenient.

The runtime must tweak some effects APIs - filesystem, network, clock, FFI, etc. - in context of distribution. This might involve implicit parameters, allowing us to reuse some APIs. The compiler, optimizers, and runtime should also support features such as automatically mirroring or migrating state for performance.

## Live Coding

Transaction loop applications make it relatively simple to update program behavior between transactions. Logically, we can understand this as the application reading and rebuilding its own code in the stable prefix of each transaction. A glas runtime can be configured to check for updates in files or DVCS automatically, or we might let applications trigger updates via reflection API. To further control live coding, applications may define a 'switch' method that must succeed as the first call on the updated code.

To further simplify live coding, the glas program model avoids state-code entanglement where feasible. There are no first-class functions or objects. Instead, for dynamic code programs should rely on defunctionalization or accelerated eval. Instead of 'spawning' threads with a start function, concurrency is based on stable repetition of a non-deterministic choice. Caching is declarative, guided by annotations, allowing for precise invalidation by a runtime. Transactional remote procedure calls are favored over long-lived connections such as channels, ensuring reactivity of relationships in the open system.

Rather than rely entirely on external tools, I hope for applications to participate in their own live coding. For example, we could define [notebooks](GlasNotebooks.md) where the compiler provides at least one editable projection of source code mixed with rendering computed values, graphs, graphics, even a few widgets for users to tweak application state. When we 'import' modules, it might automatically integrate pages or chapters into the notebook, and construct a composite table of contents. In the general case, this allows for graphical programming, e.g. with boxes and wires or some other metaphor. Ideally, this view should be easy for users to wrap within a user-defined GUI or remove to save space.

## Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, testing, debugging, static analysis, caching, acceleration, parallelism, and other non-semantic features. As a general rule, annotations should not influence observable behavior except as observed through reflection APIs. That is, it must be safe for a runtime to ignore unrecognized annotations. Instead of raising an error, we'll report a warning per unrecognized annotation to resist silent degradation of program integrity or performance.

In context of the abstract assembly intermediate representation, I propose that annotations are represented using a uniform structure `(%an AnnoAST Operation)`, such as:

        (%an (%log ConfigChan Message) Operation)
        (%an (%assert ConfigChan Expr Message) Operation)
        (%an (%type TypeExpr) Operation)

In each case, we annotate Operation with a given AnnoAST. If Operation is omitted, it defaults to a no-op. The set of recognized annotations is ad-hoc extensible via abstract assembly. We can easily warn about unrecognized annotations only once per name. To let developers selectively disable warnings, we might generically support `(%an (%an.nowarn AnnoAST) Operation)`.

We can also support annotations at other layers. For the namespace layer, we might use prefix '\#' such as 'foo.\#type' or 'foo.\#test.\*' to define annotations on 'foo' or 'foo.\*'. At the data layer, we might annotate data to enforce types at runtime, to support acceleration, or to trace data flow through a system.

### Automatic Testing

Automatic tests can be expressed within a program using assertions, or within a namespace via ad-hoc conventions such as 'foo.\#test.\*'. Assertions can be static or dynamic. In general, we might leverage non-deterministic choice as a basis for fuzz testing, and tests would be evaluated within a transaction that we abort after determining pass or fail.

Conventionally, assertions are treated as independent statements. However, in some cases it might be useful to express some assertions as 'invariants' over a subprogram, such as:

        assert(Chan, Cond, Message) { Operation }
          # compile to
        (%an (%assert Chan Cond Message) Operation)

In this case, we might evaluate Cond at multiple points during Operation. Or, depending on configuration, we might test Cond once randomly during Operation, or just randomly in general allowing for multiple Operations per test. Regardless, we might test the condition whenever something else goes wrong during Operation and it's time to generate a stack trace.

### Logging and Profiling

As with assertions, I propose to support logging over an operation. Depending on the configuration, logging over an operation could include some performance metadata in addition to messages. Profiling can be understood as a specialized log, where the runtime efficiently aggregates performance statistics, treating each 'message' as a dynamic aggregation index.

        log (Channel, Message) { Operation }
        prof (Channel, DynamicIndex) { Operation }
          # compile to
        (%an (%log Channel Message) Operation)
        (%an (%prof Channel DynamicIndex) Operation)

We could support configuration of random sampling during an operation, or 'animation' of a log to support rendering of Operation. Or perhaps we only compute Message as part of a stack trace to support debugging.

*Note:* In context of transaction loop applications, with incremental computing and non-deterministic choice, we might want to render logs as something like a stable tree of messages that is also animated across repeating transactions.

### Recording

Where logging produces a user-defined message, recording instead captures some data to slowly replay a computation in the future. This recording initial states, parameters, and results from calling runtime APIs, but only insofar as these are observed within the computation. Though, we could record some outputs to help 'validate' the recording upon replay.

        # perhaps expressed as
        record (Channel, Cond) { Operation }

I think that such records would prove convenient for debugging many programs without interfering quite so severely as breakpoints. 

## Debugging

Depending on configuration, a glas runtime may implicitly intercept some `"/sys"` HTTP requests, and similarly for RPC, with configurable authorization. Through these interfaces the runtime can provide access to logs, profiles, recordings, an attached database, source and version information, disassembly of code, manipulation of breakpoints, and generic administrative controls: pause, continue, update, restart, etc.. 

An integrated development environment can implement scripts or plugins to interact with the glas runtime through these APIs. The runtime may also peek at the 'Accept:' header and let users browse logs, profiles, etc. with a few hyperlinks, insofar as this view doesn't add overly much overhead.

An application will receive access to these features via reflection APIs. Some reflection APIs may be fine-grained and structured for performance and efficiency, but should also support aggregate views via 'sys.refl.http' and similar. This enables an application to implement its own ad-hoc debug views. This is especially convenient in context of notebook applications where an application implements its own projectional editor.

## Performance

### Acceleration

In context of community and portability concerns, it is socially awkward to unilaterally extend a glas program model with new semantic primitives. Acceleration is a pattern that bridges this gap, letting runtimes introduce 'performance' primitives separately from 'semantic' primitives. 

For example, instead of directly introducing a new primitive for '%matrix.mul', we might write `(%an (%accel.matrix.mul) ReferenceImpl)`. A subset of runtimes might recognize this annotation, optionally validate ReferenceImpl, then replace the entire expression by '%matrix.mul'. Other runtimes could raise a warning then use ReferenceImpl. The warning guards against silent performance degradation.

One of the best use cases is accelerated 'eval' of an abstract CPU, GPGPU, or other low-level model. With some careful design, we can efficiently validate code is safe then 'compile' to run upon an actual CPU (or GPGPU, etc.). This can replace most performance-motivated of FFI or embedded assembly, and let glas systems effectively enter new problem domains. But there are also plenty benefits for extending 'data types' to graphs, bags, sets, relational algebras and databases, etc..

### Laziness and Parallelism

Insofar as we determine certain expressions are read-only, we might ask the runtime to defer computation, immediately returning a 'thunk' that captures code and context. Later, we can implicitly force evaluation by observing the value, or explicitly do so via other annotations. We can even ask a runtime to put the thunk into a queue for a worker thread to 'force', which supports lightweight parallelism.

Viable API:

        (%an (%lazy.thunk) Expr)        # return thunk immediately; restricts type of Expr
        (%an (%lazy.force) Expr)        # if Expr returns shallow thunk, force evaluation
        (%an (%lazy.force.deep) Expr)   # force all thunks within data returned by Expr
        (%an (%lazy.spark) Expr)        # put thunk in a queue to be forced by worker thread

Laziness does introduce a risk of capturing divergent computations. Or, even if we guarantee termination, some computations might simply take too long and hinder reasoning about performance. To control this problem, we'll generally scope thunks to the runtime, i.e. implicitly forcing thunks when serializing data in context of remote procedure calls or persistent storage. 

Aside from runtime scope, we might benefit from 'transaction' scope, i.e. thunks that will be forced just prior to commit if they would escape the transaction. Indeed, this might be a better default than runtime-scoped thunks. One viable expression: a runtime could recognize both `%lazy.thunk.rt` and `%lazy.thunk.tn`, with `%lazy.thunk` translating to a configurable default.

Anyhow, I don't expect glas systems to pursue Haskell levels of laziness, but it does seem a useful feature for guiding performance.

### Memoization

Any computation that can run lazily or in parallel can potentially be memoized. Persistent memoization can be useful for compilation of applications or indexing of a database. We can introduce annotations to guide memoization. Memoization is most easily applied to tree structures, where we compute some monoidal property for a tree node based on the same property of each subtree. But memoizing computations on lists is also very valuable, and may need special attention.

In any case, the use of memoization is assumed in design of glas. Without it, many 'pure' computations such as compilation would require explicit use of state for manual caching.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to higher-latency storage (e.g. disk or network). This optimization can be guided by program annotations, but should be transparent modulo use of runtime reflection APIs. In context of serialization - distributed runtimes, database storage, or remote procedure calls - we can support protocols to lazily fetch fragments of data as needed, supporting incremental upload and download for large values. 

There are also benefits for memoization, allowing us to work with larger values as 'keys'. And it is feasible to offload hashed content to a [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network), reducing the network burden for distributing very large values.

## Thoughts

### Type System

The section on abstract and linear data provides a highly simplified dynamic type system. But I hope to gradually support something more sophisticated. I would hope to track unit types on numbers within applications, for example. And perhaps track ad-hoc session types for channels, insofar as we use them. It is unclear to me how to best approach this, other than that it will involve annotations within programs or namespaces to guide static analysis. Beyond types, I hope to explore the more general idea of proof-carrying code, using annotations to integrate proof tactics that can adapt to smaller code changes.

*Note:* Because glas programs are mostly transactional in nature, it's relatively easy to treat dynamic type errors at runtime as divergence, same as an infinite loop, effectively aborting the transaction. 

### Program Search

I'm interested in a style of metaprogramming where programmers express both hard and soft constraints on the program, and the program includes sufficient code to adapt to contexts and user preferences. Depending on the design, this might also support working with 'ambiguous' definitions, allowing programmers to be more vague and still get a useful program.

Some ideas: We could introduce AST nodes for 'static' non-deterministic choice within a call graph. We can reject some solutions based on static asseertions, and prioritize others that have a lower 'weight'. Weights can be abstracted into arbitrary values that we 'interpret' into positive rational numbers according to user preference, e.g. an 'experimental' tag might weigh 0.01 for one user and 100 for another. By applying something similar to an A* search, it should be feasible to discover the 'best' solution by weight, and we can ensure this solution is deterministic.

But I'm not convinced that a one-size-fits-all solution at the toplevel is the right approach here. Perhaps more explicit staging of program search would be easier for programmers to isolate, control, and stabilize in context of incremental compilation. 

### Tracing and Recording

I would love to trace both code and runtime data to its sources despite the challenges of multi-stage metaprogramming, optimizers, and open distributed systems. But what can be achieved without too much overhead or added system complexity?

One feasible idea is to add model something like medical radioactive tracers for dataflow. We can attach tracer annotations to data, then observe these annotations as they propagate through a system. By default, we could log the tracer when the value is observed or destroyed. Then we could annotate some expressions to capture and repply tracers to the return value instead of logging them.

 and such. 

 state and log external events (and non-deterministic choices) so we can slowly replay some computations after the fact. This could also be guided by annotations.




I'd love the ability to trace computed definitions back to source code at a very fine granularity. And, similarly, to trace outputs from a program back to specific inputs and conditional code. But this seems difficult to achieve, at least without enormous overheads. Some ideas: Perhaps we could leverage something like  And instead of fine-grained data annotations, we could support bulkier 'overlay' annotations. But this seems a pipe dream at the moment. 

What we can achieve in is support something like explicitly tagging data with
 
 annotations that we can then inspect as the data flows through the system. 

By default, we could *log* a message whenever a tag is removed from data and not explicitly handled. And we cou



For example, we could annotate expressio

 arrange for certain operations to capture tags 'removed' when processing data, then applying a new tag that may depend on all the tags removed. 
