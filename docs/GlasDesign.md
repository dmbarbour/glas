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

However, glas systems will often encode data into stems. Dictionaries such as `(height:180, weight:100)` can be encoded as [radix trees](https://en.wikipedia.org/wiki/Radix_tree), while an open variant becomes a singleton dictionary. The naive binary tree encoding is inefficient for this role because it doesn't compact stem bits. In practice, we might use something closer to:

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

This allows for reasonably efficient representation of labeled data. We can also easily encode integers into stems. However, we might further extend the Node representation to more efficiently encode arrays and other useful types. 

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

### Abstract or Linear Data

Technically, data is abstract only in context of a subprogram that does not directly observe or construct that data. Abstract data may be linear insofar as the subprogram further does not directly copy or drop the data. These are extrinsic properties of context, not intrinsic properties of data. However, enforcing these features based on static analysis can be difficult. A little bit of metadata in the representation can support efficient enforcement at runtime.

To support abstract data is not difficult, involving a lightweight extension to the Node type:

        type Node =
            | ... # other Node types
            | Abstract of Key * Tree

The runtime may provides primitives or recognize annotations to wrap and unwrap data. Attempting to observe abstract data without first unwrapping it would be treated as divergence or type error. An optimizer can eliminate unnecessary wrap-unwrap pairs. Intriguingly, it is feasible to strongly enforce abstraction via encryption in cases where data is serialized across trust boundaries, though for performance and debuggability reasons we might limit this to specialized keys.

Linearity is a bit more difficult. I suggest [tagged pointers](https://en.wikipedia.org/wiki/Tagged_pointer) to efficiently encode a bit describing whether each node is transitively linear. At runtime, we can check this bit before we copy or drop the data. User-defined types would introduce linearity only upon wrapping a value as abstract. A runtime should mark open files, sockets, channels, etc. as linear at the API.

Linearity cannot be strongly enforced in an open system. Thus, we might conlate linearity with runtime scope, forbid serialization of linear data over remote procedure calls or into a persistent database. Even with this constraint, linear data can be very useful for safely integrating a glas system with external resources.

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

Instead of understanding a runtime as a process running on a specific machine, we can model a runtime as a distributed system overlay that implements a reasonable API for the application. This allows a single application to run on multiple nodes without explicitly struggling with serialization, network interfaces, network disruption. 

Such a runtime should have built-in support for latency-tolerant or partitioning-tolerant data types such as queues, channels, bags, and CRDTs. By integrating the application in terms of such resources, the application can be distributed across multiple nodes.

The transaction loop application model is an excellent fit for a distributed runtime. We can logically run the *same* repeating transaction on every node, yet heuristically filter based on non-deterministic choice and locality. There is no serializability conflict between one transaction reading a queue and another transaction blindly writing a queue. We can benefit from distributed transactions where needed, yet design our application such that *most* transactions run on a single node. Mirroring and distributed systems programming are neatly unified. 

Of course, in context of a distributed runtime, we no longer have an implicit host operating system. This has a significant impact on filesystem APIs, network APIs, FFI, and so on. If we aren't careful, reading a local clock can violate transactional isolation. The various APIs must be carefully reinvented in context of the distributed runtime.

## Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, automated testing, debugging, type-checking, optimizations, and other non-semantic features. As a general rule, annotations must not influence observable behavior except through reflection APIs. Thus, it is safe to ignore unrecognized annotations, though we'll report a warning to resist silent degradation of performance or consistency. 

In context of abstract assembly, annotations might be generally represented using `(%an AnnoAST ProgAST)`, scoping over a subprogram. The AnnoAST might be something like `(%log ChanExpr MessageExpr)`. Ignoring the annotation, this should be equivalent to ProgAST. If ProgAST is omitted, we might assume a no-op.

We can also support annotations in the namespace. For this, I propose prefix '#' for annotations. We might define 'foo.#doc' or 'foo.#type' to document 'foo'. 

Although annotations don't directly influence system behavior, their influence is indirectly visible through reflection APIs or external configurations. For example, annotations for profiling might result in statistics that can later be extracted via 'refl.prof.\*', or we might configure a runtime to serialize profiling metadata to file.

## Automated Testing

Automatic tests should be expressed using annotations. We might express this using static assertions within programs or a simple convention such as 'foo.#test.\*' within a namespace.

Most tests will run with a limited effects API to ensure reproducibility. This API may include non-deterministic choice to model fuzz testing and property testing, with the system searching for a sequence of choices that will fail the test. With some configuration, it is feasible to cache and share these restricted tests between users, and produce a system health report.

## Live Coding

Applications can provide a projectional editor for their own code and state. As we edit code or state, we can immediately observe updates in system behavior. In many cases, this feedback can be rendered in close visual proximity to the relevant code. We can design applications to leverage this. A useful metaphor for live coding is the [notebook application](GlasNotebooks.md), where we develop an application as a notebook that mixes source and output. 

To simplify live coding, we should avoid unnecessary sources of entanglement between code and state such as long-running procedural loops (where local vars become state), spawned threads, or first-class functions or objects. The transaction loop application model avoids those procedural loops and spawned threads, but we'll further avoid first-class functions in the program model.

## Performance

### Acceleration

I touch on the notion of acceleration when describing the *List* and *Number* types for glas data. More generally, a runtime can develop specialized data representations and functions to leverage them, then let programmers replace a reference implementation with a more efficient version via annotation. In addition to triggering this exchange, the annotation resists silent performance degradation by warning the user if the exchange fails. 

Intriguingly, it is feasible to accelerate simulation of an abstract CPU or GPGPU or process network. With some careful design and ahead-of-time safety checks, we can compile a simulation to run unchecked on actual hardware. This is a viable path to high performance computing in glas systems.

Developing an accelerator is a high risk endeavor due to potential for bugs or security holes. As a rule, we should introduce acceleration only where the reward is similarly great. Aside from lists, numbers, and abstract machines, effective support for graphs, bags, sets, and relational algebras may prove worthy.

### Laziness and Parallelism

We can introduce annotations to guide use of laziness and parallelism. If intelligently applied, parallelism can enhance utilization while laziness avoids wasted efforts. Both can keep transactions shorter, reducing risk of concurrent interference. However, laziness and parallelism introduce their own performance risks. This can be mitigated by heuristically 'forcing' computations. I suggest automatically forcing computations at RPC boundaries to isolate performance risks to each application.

### Memoization

Any computation that can run lazily or in parallel can potentially be memoized. Persistent memoization can be useful for compilation of applications or indexing of a database. We can introduce annotations to guide memoization. Memoization is most easily applied to tree structures, where we compute some monoidal property for a tree node based on the same property of each subtree. But memoizing computations on lists is also very valuable, and may need special attention.

In any case, the use of memoization is assumed in design of glas. Without it, many 'pure' computations such as compilation would require explicit use of state for manual caching.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization can be guided by program annotations, but should be transparent modulo use of runtime reflection APIs.

Compared to virtual memory backed by disk, content addressing has benefits for incremental computation, orthogonal persistence, and structure sharing. A lot of work doesn't need to be repeated when a hash is known. When communicating large values, it also works nicely with [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network).

## Thoughts

### Type System

We can support type annotations within the abstract assembly, and typechecking on the final namespace. To avoid harming extensibility, it should be feasible to express just fragments of a type, a subset of type assumptions we make locally, leaving details to be refined contextually. Gradual typing should be feasible.

In addition to structural types and data abstraction, I'm very interested in support for 'shadow' types that aren't represented in runtime data as a possible basis for unit types on numbers, staging types, logical locations or latency, and other contextual properties. Perhaps instead of shadow types per se, we could model an ad-hoc static computation that propagates bi-directionally through a call graph. This could feasibly be expressed as yet another namespace.

Aside from types, I like the idea of annotating other properties and developing a system of 'proof hints' and 'proof tactics'. Ideally, types would be expressed within this system instead of separately from it. In case of shadow types, we'd add 'proof assumptions'. But at the moment, I don't have concrete ideas on how to approach this idea.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard constraints and preferences while building a call graph. This might be expressed as path-dependent 'costs' based on non-deterministic choices. Costs could be described by emitting ad-hoc values at compile time, which are then translated to positive rational numbers, allowing users to explore different cost heuristics. 

To support this, of course, we need static bi-directional computation with a notion of weighted choice, and we'll need a caching model and modified A* search to support incremental compilation. Intriguingly, this search could be applied to both construction of namespaces and choices within a call graph.

I think it's best to solve the problem of static bi-directional computations first. That would be very useful even without search, e.g. to provide extra context over a call graph. Perhaps it could solve shadow types, or involve them, e.g. units for numbers.

### Provenance Tracking

I need to explore how to debug problems and trace them back to their original sources. In glas systems, this is complicated by metaprogramming at multiple layers, but also somewhat simplified by disfavoring first-class functions or other 'mobile' code abstractions. I like the idea of [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics.

