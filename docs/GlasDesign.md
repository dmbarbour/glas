# Glas Language Design

## Module System

Large Glas programs are represented by multiple files and folders. Each file or folder deterministically computes a value.

To compute the value for a file `foo.ext`, search for a language module or package `language-ext`, which should include a program to process the file's binary. To compute a value for a folder `foo/`, use the value of the contained `public` module if one exists, otherwise a simple dictionary reflecting the folder's structure.

Language modules have access to limited compile-time effects, including to load values from external modules or packages. File extensions are elided. For example, loading `module:foo` could refer to a local file `foo.g` or `foo.xyz`, or a local subfolder `foo/`. Loading `package:foo` instead searches for folder `foo/` based on a `GLAS_PATH` environment variable.

Ambiguous references, directed dependency cycles, invalid language modules, and processing errors may cause a build to fail.

*Note:* Files and folders whose names start with `.` are hidden from the module system. A `.glas/` folder might support tooling, e.g. quotas, profiling, proxy cache. But, like annotations, should be semantically neutral.

## Data Model

Glas data is immutable. Basic data is composed of dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`.

Dictionaries are a set of `symbol:Value` pairs with unique symbols. Symbols are short binary strings. The empty dictionary `()` frequently serves as a unit value. Glas programs may iterate over symbols and test for presence of a symbol, thus flags and optional fields are supported.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. Symbol `foo` is shorthand for `foo:()`. A Boolean value is simply `type Boolean = true | false`. 

Glas uses lists for all sequential structure: arrays, binaries, deques, stacks, tables, tuples, queues. To efficiently support a gamut of applications with immutable data, Glas systems will represent large lists as [finger trees](https://en.wikipedia.org/wiki/Finger_tree). Logical reversal can be supported. Glas further uses [rope-style chunking](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) for large binaries to minimize overhead.

Glas natural numbers do not have a hard upper limit, and do support bignum arithmetic. Glas does not have built-in support for negative integers or rationals or floating point or other numeric types, but they could be modeled. Note that Glas is not suitable for high-performance numeric computing without *Acceleration*.

### Content-Addressed Storage

Glas is intended to work at very large scales, with data that may be larger than a computer's working memory. 

Glas will support big data structures using content-addressed storage: a subtree may be serialized for external storage on a large, high-latency medium such as disk or network. This binary representation should be referenced by secure hash. 

Use of secure hashes simplifies incremental and distributed computing:

* persistent data structures, structure sharing
* incremental upload and download by hash cache
* efficient memoization, matches on tree hashes
* provider-independent distribution, validation

A Glas runtime may heuristically store values to mitigate memory pressure, similar to virtual memory paging. However, programmers have a more holistic view of which values should be stored or speculatively loaded. Thus, Glas programs will support operators to guide use of storage. 

[Glas Object](GlasObject.md) is designed to serve as a standard representation for large Glas values with content-addressed storage.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries will include a cryptographic salt (among other metadata). To support incremental computing, this salt must be computed based on a convergence secret. However, it prevents global deduplication.

## Compilation Model

A subset of Glas modules compute externally useful binaries, perhaps representing music, images, documents, tarballs, or an executable. The Glas command-line tool provides methods to extract binary values from a module from a list or a program generating a stream of bytes.

To produce an executable binary, the static analysis, optimization, and code generation will be modeled as Glas programs, ultimately driven by a language module. To produce binaries specific to a system, a system-info package can describe the default compilation target.

As a convention, appname-code and appname-exe packages should be separated to simplify extension or composition of applications, model testing, experimentation with compiler parameters, etc..

*Note:* The Glas command-line tool may privately compile language modules for internal use, e.g. as plugins. However, this should be mostly hidden from the user.

### Staged Programming

Glas supports staged rogramming at two layers.

First, the module system supports ad-hoc metaprogramming. Language modules represent functions. By integrating an interpreter function, language modules can support macros or arbitrary staging.

Second, the Glas program model is amenable to partial evaluation. A programmer can annotate that certain inputs should be computable at compile-time. The compiler can raise warnings or errors.

The first approach is top-down, the second is bottom-up. They can help cover each other's weaknesses or limitations.

### Acceleration

Acceleration is a pattern to support high-performance computing.

The idea: We can develop a program that simulates an abstract processor with a static set of registers, binary memory, fixed-width arithmetic and floating-point, etc.. We annotate this subprogram for acceleration. A compiler can recognize the annotation, verify the subprogram, then substitute use of an actual processor.

Effective substitution benefits from a constraints such as: separation of behavior and data like a Harvard architecture, separation of data and pointer registers. We're essentially modeling a DSL for a safe, simplified, idealized processor.

Acceleration of an abstract processor would support problem domains such as compression and cryptography. This idea can be extended to acceleration of abstract GPGPUs or FPGAs, or even simple networks of processors.

Acceleration can fail for various reasons - unrecognized, deprecated, unsupported on target, resource constraints, etc.. The compiler should raise warnings or errors to resist invisible performance degradation.

*Note:* Accelerators are significant investments involving design and development, maintenance costs, portability challenges, and security hazards. Fixed functions have poor return on investment compared to abstract programmable hardware.

### Memoization

In Glas, [incremental computing](https://en.wikipedia.org/wiki/Incremental_computing) will be supported primarily by [memoization](https://en.wikipedia.org/wiki/Memoization). Content-addressed storage also contributes, enabling memoization over large value. 

Glas subprograms can be annotated for memoization. Annotations can include ad-hoc heuristic parameters such as how large a table to use, sharing, volatile vs persistent storage, expiration strategies, precomputation for specified inputs, etc.. 

Persistent memoization could use a secure hash of the memoized subprogram and its transitive dependencies as a content-addressed table. This can support incremental compilation or indexing involving similar data across multiple executions. Volatile memoization is more efficient and useful for fine-grained [dynamic programming](https://en.wikipedia.org/wiki/Dynamic_programming). 

Memoization and incremental computing are deep subjects with many potential optimization and implementation strategies. Fortunately, even naive implementation can be effective with precise application by programmers.

## Computation Model

The primary Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) to support scalable computation and expressive composition under a constraint of determinism.

KPNs consist of concurrent processes that communicate by reading and writing channels. Channels may be externally wired between processes, supporting open composition of cyclic dependencies. Use of channels is restricted to ensure a deterministic outcome. Deadlock is a potential outcome.

Variations from original KPNs:

Glas favors bounded-buffer channels, such that fast producers always wait on slow consumers. KPNs can model bounded-buffer channels using coupled `(ready, data)` pairs of unbuffered channels flowing in opposite directions. Writer reads ready token then writes data. Default is zero-buffer, which models a concurrent rendezvous pattern.

Glas channels may be 'closed' from either end. A writer may indicate there is no more data. A reader may also say that it's done. This feature supports expressive composition and short-circuiting computations.

Glas channels are second-class. A process has labeled data ports, which can be composed and wired externally.

## Program Model

Concretely, a Glas program is represented by dictionary containing `code:[List, Of, Operators]`. This dictionary might also include a namespace and annotations. 

Each operator is parameterized by static references to data ports. Operations on independent ports concurrently based on available input and readiness of readers. Operators that share ports are implicitly sequenced according to the list.

Data ports for external IO are referenced by `io:portname`. Variables are referenced by `var:varname`. A program may instantiate an abstract child process with a fresh prefix `foo` then reference its external ports via `foo:portname`. External data ports are not buffered.

Wires are an operator that repeatedly reads from one port and writes to another, with optional buffering. Compilers can compose and optimize wires to minimize abstraction overhead for dataflow across process boundaries. Wires serve the role of conventional channels.

By default, Glas programs implicitly 'close' unused ports. This signals termination, that a subsequent read or write will wait indefinitely. Optional inputs and lazy outputs can be modeled based on observing implicitly closed ports.

Operators are detailed in a later section.

### Variables

Logically, a variable `var:varname` represents an identity process that buffers one value. The buffer starts empty. Writing the variable fills its buffer. Reading the variable empties its buffer. Variables serve several roles in Glas programs: 

* state across loops
* communication between loops
* scratch space for calculations
* conditional behavior (empty or full)

Default use of the buffer is inconvenient for many use-cases, so Glas supports a few patterns via extended references:

A reader may use `var:varname:copy` to read a copy of the value. One copy remains in the buffer. As a special case, multiple operators may read-copy concurrently.

A writer may use `var:varname:send` to wait for a reader. This models a concurrent rendezvous, and simulates writing unbuffered data ports between processes.

## Namespace Model

Definition-level programing is compact, comprehensible, and composable compared to expanding a program to primitive operators. Glas supports definition-level programming with a higher-order namespace model.

A Glas program can instantiate abstract child processes by name. Namespaces are separate from runtime semantics: names are resolvable at compile-time, recursive definitions are rejected, no support for closures.

The envisioned use case is that Glas modules should compute namespace values. This supports a conventional programming style where programs are decomposed into reusable definitions with flexible scoping and export control, then composed as needed. 

The higher-order namespace features can support dependencies, defaults, generic programming, and separate compilation. Favoring namespace-layer dependency injection instead of directly loading modules can reduce duplication and improve flexibility.

Concretely, a namespace will be represented as `ns:[List, Of, Namespace, Operators]`. Namespace operators are much simpler than program operators - oriented around definitions, defaults, visibility. Glas does not support useful computation in the namespace. 

## Annotations

Annotations are concretely represented by `note:Content` in Glas programs or namespaces. Glas systems support annotations in most places, 

, e.g. as a special program or namespace operator, a request in the request-response channel for effects, the program dictionary also containing `code:` and `ns:`.

Logically, annotations must be *semantically neutral*. That is, ignoring or removing annotations must not affect the observable behavior of the system. Content of annotations is left to ad-hoc extension and de-facto standardization. If a system does not recognize an annotation, it should emit a warning if possible (to avoid silent failure), then ignore.

Annotations serve a valuable role in context of external tooling: documentation and comments, automatic testing, debug logging or breakpoints, profiling, acceleration and optimization hints, type annotations, theorem prover hints, anchors for reflection, recommended widgets for projectional editing or direct manipulation, etc..

## Effects Model

Most Glas programs will represent effects via request-response channel. The program writes `io:request` then reads `io:response` based on an API of supported requests. This models a single thread for procedural behavior, but with background computations while awaiting response.

A request-response channel becomes a bottleneck for effects. [Glas applications](GlasApps.md) mitigate this with API design - small transactions, asynchronous IO, fork requests. Acceleration, memoization, and content-addressed storage can displace effects for some use-cases. 

My vision for very large scales involves development of higher-order *deployment models* that represent how a set of applications bind to configurable distributions of sensors, actuators, displays, storage, networks, swarms, clients, and other resources. A deployment model would be continuously compiled and deployed.

*Aside:* Glas compilers could inject effectful operations via higher-order namespace. However. effects embedded within black-box processes are awkward to observe, control, or safely update at runtime. Explicit deployment models are a better fit for my larger vision of software systems.

## Language Modules

To process a file with extension `.xyz`, a Glas command-line utility will search for module or package `language-xyz`, favoring a local module. The exceptional case is bootstrapping, which requires built-in syntax. 

The language module should compute a namespace that defines a compile process. A minimal compile process must take source input, load modules, and compute a value. This could be implemented directly as a Glas program, with request-response for loading modules (and logging).

However, a direct implementation is opaque to external tooling. This can hinder provenance tracking, incremental computing, ambiguity detection, programming assistance, proposal of corrections, etc.. Thus, I'm exploring alternatives in the [Glas Syntax](GlasSyntax.md) document.

## Glas System Patterns

Miscellaneous high-level visions for Glas systems.

### Glas Application Model

This grew into its own page, which envisions a non-conventional application model for Glas systems and their integration with existing systems. See [Glas Application Model](GlasApps.md).

### Graphical Projection

After we have GUIs working, programs can be edited via graphical projections over files and folders. Glas language modules can optimize syntax for this purpose. The natural progression is to define a compile process for database files, then program by editing graphical projections of databases.

There are many potential benefits of programming in this medium: multiple views of code to support understanding and manipulation, lower barriers for tooling, extensibility with new projections and properties, and widgets specialized for different DSLs.

Of course, actualizing these benefits depends on design. Graphical programming can be awful if we aren't careful.

*Aside:* For best effect, graphical projection of programs should be combined with live coding and notebook-style apps. See *Glas Application Model*.

### Automated Testing

Within a Glas folder, any module with name `test-*` should be processed even if its value is not loaded by the `public` module. Language modules support ad-hoc expression of tests via files, logging `error:(...)` to report issues. Static analysis can be performed as normal tests.

Testing of computed executable binaries is theoretically feasible via accelerated simulation and fuzz-testing. However, accelerators won't be immediately accessible, so more conventional methods are required short-term.

### Automatic Buffering

Buffers reduce sensitivity to latency, which supports distributed computation. However, manual buffering is awkard and easy to get wrong. For example, extending one buffer is often useless without extending several other buffers.

Glas programs should specify minimal buffering for correct and convenient operation. An optimizer or compiler can later grow buffers as needed to support distributed computation. This could be supported by annotations.

### Program Search

We can develop higher-order program models that represent constraint systems and search spaces for programs. We can define packages that represent catalogs of other packages, with tags and summaries and indexes. Modules could experimentally compose program values, evaluate their fitness for a purpose. 

This would enable programs to be much more adaptive to changes in requirements, preferences, or the ecosystem.

*Note:* Type-driven programs, such as selecting functions based on inferred types, is essentially a limited form of program search. Type safety is another constraint guiding a choice from an implicit search space. 

### Abstract and Linear Types

Abstraction and linearity are not intrinsic properties of data. Instead, they are constraints on a subprogram that interacts with data.

A subprogram can treat data as abstract by never manipulating it directly. Instead, the data is processed through provided functions or channels. Abstraction extends to linearity when duplication and dropping of data are treated as protected manipulations.

Glas programmers can use annotations to assert that a subprogram should respect abstraction or linearity. This should be verified by static analysis.

Abstract and linear data types are useful for performance, especially in context of *acceleration*. Abstraction can protect optimized representations. Linear data can often be modified in-place, optimizing allocation and garbage-collection.

### Modal and Phantom Types

It is feasible for a type system to track metadata such as location, latency, units of measure, purpose and provenance. Programmers can then control composition based on this metadata in ad-hoc ways. 

For example, a type system might complain if we try to add apples to oranges, or if we attempt to multiply matrices logically located on separate GPGPUs. Tracking latency can make it feasible to constrain programs based on big-O complexity notations.

Modal types can be expressed and manipulated by annotations. Which manipulations are valid might be constrained by the type system (e.g. latency is monotonic). This pattern can be combined with abstract and linear types.

## OPERATOR DESIGN

I haven't found a solid set of guiding principals for choice of operators. But some goals: 

* widely useful
* easy to understand

I'm not entirely happy with unpredictable performance if we leverage referential equality. But use of caching, content-addressed storage, opportunistic parallel and distributed computation, live coding apps, etc. already limit predictability of performance.

### Deep Equality? Yes.

Glas can support structural equality comparisons over arbitrary values.

### Hashing? No.

Choosing any particular hash function is very awkward.

### Pattern Matching? No.

Structured pattern matching is very awkward to represent at semantics layer. Maybe try to introduce in syntax layers.

### Recursive Definitions? No.

It is feasible to locally rewrite let-rec groups to loops. However, it's too difficult to make recursion work nicely with the KPN model with incremental, partial outputs.

### Backtracking Operators? No.

Very awkward fit for Glas semantics. We can still do backtracking at the higher layers, e.g. hierarchical transactions, pattern matching.

### Arithmetic? Minimal.

There is no end to the number of arithmetic functions we could introduce. But sticking to add/sub/mul/div should be sufficient. 

### Break/Continue Loops? No.

An awkward fit for concurrent operators. I'm thinking we might end a loop by writing a certain loop variable.

### Logical List Reversal? Yes.

Easy to understand, widely useful. Awful performance predictivity - could be O(1) time or O(N) time.

### List Collections Processing? Probably.

I'm inclined to support a variety of list-processing operators such as zip/unzip, transpose, map, filter, scan, fold, flatmap, concat, sum, etc. I would like to have good support for optimizing structure-of-arrays vs array-of-structures.

It's less clear to me whether these operations should also apply to channels, or whether a different set of similar operations should apply.

### Dictionaries and Symbols? Full.

Glas programs will have full ability to inspect and construct dictionaries, e.g. iteration over symbols, composition of dictionaries. A compiler should use static analysis and annotations to determine when certain dictionaries should be represented as C-like structs. 

## Program Operators

### Arithmetic

Glas will support natural numbers and bignum arithmetic.

However, rather than have a large number of 

        add:(args:[list of ports], result:port)
        mul:(args:[list of ports], result:port)
        sub:(a:port, b:port, a-b:port, b-a:port)
        div:(dividend:port, divisor:port, quotient:port, remainder:port)

A tempting alternative is to create a generic 'math' operator wi

        math:(a:port, b:port, c:port, "a b c + +":port)








Subtraction and division may have one or two outputs. Subtraction can output both left-right and right-left, returning a minimum value of 0. Division outputs quotient and remainder. Arithmetic with non-numeric input or divide-by-zero will not have output.

Glas is not designed for high performance numeric computing. Glas systems should *accelerate* an abstract CPU or GPGPU with access to fixed-width integers, floating point, vectors and SSE, etc. then use that for low-level computations. 

### Dictionary Operations

### List Processing

### Channel Processing

### Fixpoint Loop

### Conditional Behavior

### Annotations

### Component System

### Synchronization

Ways we can synch:

* static - insist a value is statically computable
* latent - defer a value until there is demand for it
* 


* wait for a value before starting subprogram
* wait for channel to finish before producing value
* wait for multiple values before producing value
* 


        synch:[X->Y]   - wait for an unrelated input before continuing

### Content-Addressed Storage

### Memoization


*Note:* A relevant concern for Glas is to support memoization over large lists. This may require specialized support to align memoization the finger-tree structure, primarily a memoized flatmap or mapsum. 

### Distribution

## Namespace Operators



## Evaluation Strategies

Graph Rewriting vs Abstract Evaluation

A predictable graph-rewrite semantics might simplify presentation of evaluation. The simplest model of rewriting is erasure: when a process is finished making decisions, it might 'become' a simple rename operation.

Glas does not have a strong use-case for a graph rewrite semantics. However, it is useful and not difficult to ensure a rewrite semantics can be expressed: it is sufficient that primitive processes can be  expressed by local rewriting. 

A graph rewrite semantics will essentially allow Glas programs to represent 'interaction networks'. 

## Provenance Tracking

Glas modules hides the provenance of a value; the client of a module  only observes its computed value, not how it was derived. However, it is feasible to augment a Glas evaluator to trace the primary influences on values and produce corresponding files.

This could be augmented by annotations within Glas programs to explicitly support provenance tracking, e.g. to support user-defined equivalence regions, human meaningful names.

