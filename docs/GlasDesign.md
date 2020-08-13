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

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. Symbol `foo` is often shorthand for `foo:()`.

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

As a convention, appname-model and appname-exe packages should be separated to simplify extension or composition of applications, model testing, experimentation with compiler parameters, etc..

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

Glas channels are second-class. Instead of channel objects, a process has labeled data ports. Ports can be wired between subprocesses.

## Program Model

Concretely, a Glas program is represented by a list of operators. Operators are represented by variant data.

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

Default use of the buffer is inconvenient for many use-cases, so Glas supports two patterns via extended references:

A reader may use `var:varname:copy` to read a copy of the value. One copy remains in the buffer. As a special case, multiple operators may read-copy concurrently.

A writer may use `var:varname:send` to wait for a reader. This models a concurrent rendezvous, and simulates writing unbuffered data ports between processes.

## Namespace Model

Definition-level programing is compact, comprehensible, and composable compared to expanding a program to primitive operators. Glas supports definition-level programming with a higher-order namespace model.

A Glas program can instantiate abstract child processes by name. Namespaces are separate from runtime semantics: names are resolvable at compile-time, recursive definitions are rejected, no support for closures.

The envisioned use case is that Glas modules should compute namespace values. This supports a conventional programming style where programs are decomposed into reusable definitions with flexible scoping and export control, then composed as needed. 

The higher-order namespace features can support dependencies, defaults, generic programming, and separate compilation. Favoring namespace-layer dependency injection instead of directly loading modules can reduce duplication and improve flexibility.

Concretely, a namespace will be represented as a list of operators. However, namespace operators are much simpler than program operators, being oriented around definitions, defaults, visibility. Glas does not support useful computation in the namespace.

## Annotations

Annotations are concretely represented by a `note:Content` operator within a program or namespace.

Logically, annotations are *semantically neutral*. Ignoring or removing them should not affect the observable behavior of a program, as measured at the data ports. 

Annotations serve a valuable role in context of external tooling: documentation and comments, automatic testing, debug output or breakpoints, profiling, acceleration and optimization hints, type annotations, theorem prover hints, anchors for reflection, preferred widgets for projectional editing or direct manipulation, etc..

Content of annotations is left to ad-hoc extension and de-facto standardization. By default, if a compiler does not recognize an annotation, it should emit a warning to avoid silent failure, then ignore.

## Effects Model

A request-response channel can model single-threaded procedural code. A program would write `io:request` then read `io:response`. Background computation may continue while waiting on a response.

For larger systems, we could model a deployment layer that specifies how to bind component processes and ports to different locations and resources.

It is feasible for a compiler to inject effectful operators via the higher-order namespace. However, I'd prefer to avoid this solution because it hinders visibility, simulation, determinism, and distribution.

## Language Modules

To process a file with extension `.xyz`, a Glas system will search for module or package `language-xyz`, favoring a local module. The exceptional case is bootstrapping, which requires built-in syntax.

A language module must compute a value that represents a namespace that defines a 'compile' process. 

The compile process will receive a binary on the port `source`, perform limited compile-time effects via request-response channel (see *Effects Model*), and produce the module's value on `result`.

Supported requests: 
* `log:Message` - emit an arbitrary message for logging purposes. Response is always `ok`, never fails.
* `load:Module` - load value of module such as `module:foo` or `package:foo`. Response is `ok:Value | error`. 

Compilation fails if a program halts before producing a result, if a request is not unsupported, or if any `error:(...)` message is logged. Load errors won't necessarily break a compile. 

## Glas System Patterns

Miscellaneous high-level visions for Glas systems.

### Automated Testing

Within a Glas folder, any module with name `test-*` should be processed even if its value is not loaded by the `public` module. Language modules support ad-hoc expression of tests via files, logging `error:(...)` to report issues. Static analysis can be performed as normal tests.

Testing of computed executable binaries is theoretically feasible via accelerated simulation and fuzz-testing. However, accelerators won't be immediately accessible, so more conventional methods are required short-term.

### Automatic Buffering

Buffers reduce sensitivity to latency, which supports distributed computation. However, manual buffering is too awkard and easy to get wrong. For example, extending one buffer can be useless without also extending other buffers.

Glas programs should normally specify minimal buffering for correct and convenient operation. Buffers can be introduced by variables, wires, and wires between variables. An optimizer or compiler can later grow buffers as needed to support distributed computation. 

### Live Coding Applications

In a live programming system, programs represent active intentions. Changes to a program should affect the real-world. However, this requires careful attention to how state and effects are modeled, to avoid losing information or breaking interactions.

A good model for live coding:

* Program is a deterministic transaction, repeating.
* Deterministic abort pauses program, awaits change.
* Request-response to support abstract state, queries.
* Program can be aborted and updated at any time.

Abstraction of external state is convenient for precise conflict analysis, incremental computing, and scalability. 

For example, multiple transactions could write concurrently to an abstract queue or mailbox without conflict. Removing messages from a mailbox can be transparently non-deterministic, and run in parallel if there is no read-write conflict.

Large transactions have high risk of conflict. However, it is feasible to 'fork' a transaction, duplicating the transaction then running multiple paths. This also would indirectly support non-deterministic choice.

This model is simple, observable, extensible, composable, and scalable. It could be supported by dedicated syntax built around observation-action rules. Compilers might optimize for checkpointing. 

I hope to make live coding applications the default for Glas systems, trading a little performance for a lot of convenience. 

### Wikis and Web Applications

I have a vision for modeling a wiki in Glas where every page is a module and defines a live coding web-app (and perhaps other symbols in a namespace).

The live-coding web-app is represented as a Glas program that uses request-response effects to operate on an idealized document object model (DOM), client-side storage, and asynchronous access to server resources. 

This program is compiled to JavaScript, and perhaps some static resources based on partial evaluation from empty DOM. It might also be compiled to native applications.

This could be used for presentation of data, little applications, full games, etc.. If nothing else, it should be a good sandbox for experimenting with Glas.

### Notebook Applications

The conventional notion of fire-and-forget REPL is a poor fit for Glas. However, Jupyter-style notebook applications - where every line remains active - could be a relatively good fit. 

This might be achieved by annotating each logical line, and modifying the compiler to report the affected variables. Further, we might implicitly bind a document object model frame to each line to support explicit user-interaction.

Notebooks are a good fit in context of live coding applications, continuously replaying the notebook. Changes in live data or code should immediately affect the outputs.

### Graphical Programming

Language modules enable Glas to bridge textual and graphical programming. Graphical programming can be supported by developing a specialized syntax, with annotations for graphical markup, layout and presentation.

Presentation might involve calendar widgets for date values, color pickers for color values, sliders for numbers, etc.. In the more general case, an entire file might be rendered as a big widget. Projections feasibly integrate multiple files, via embedding, portals, or links. At an extreme, it is theoretically feasible to project programs as video games.

If coupled with the notion of live notebook applications, then we have a GUI which can reserve space to compute another GUI based on data. 

### Stack Programming

I'm fond of stack programming, though I do understand why most people aren't. 

In Glas, this could be supported by a user-defined syntax that allocates variables for stack positions. To avoid accidental interference with concurrency, we might use a fresh variable for everything added to the stack, then let the compiler optimize.

### Program Search

As the Glas system matures, it might be useful to shove more decision making to expert systems encoded in the module system. 

We can develop higher-order program models that represent constraint systems and search spaces for programs. We can define packages that represent catalogs of other packages, with tags and summaries and indexes. Modules could experimentally compose program values, evaluate their fitness for a purpose. 

This would enable programs to be much more adaptive to changes in requirements or preferences.

### Abstract and Linear Types

Abstraction and linearity are not intrinsic properties of data. Instead, they are constraints on how a subprogram interacts with certain data.

A higher-order program can treat data as abstract by manipulating the data only through the provided functions. Abstraction extends to linearity if we consider duplication or dropping of data to be manipulations. Glas programmers can use annotations to assert that a subprogram should respect abstraction or linearity.

Abstract and linear data types are useful for performance, especially in context of *Acceleration*. Abstraction can preserve optimized under-the-hood representations between operations. Linear data can potentially be modified in-place, optimizing away some allocation, copy, and garbage-collection steps.

Abstract and linear types are also useful for designing a robust, scalable effects API.

*Note:* Linear types have a bad reputation due to awkward feature interactions with pattern matching, closures, exceptions. However, a suitable syntax can help a lot, and there should be much less friction with process networks compared to lambda calculus.

## OPERATOR DESIGN

I haven't found a solid set of guiding principals for choice of operators. But some goals: 

* widely useful
* easy to understand

I'm not entirely happy with unpredictable performance if we leverage referential equality. But use of caching, content-addressed storage, opportunistic parallel and distributed computation, live coding apps, etc. already limit predictability of performance.

### Deep Equality? Yes.

Glas can support structural equality comparisons over arbitrary values.

### Hashing? No.

I don't want to choose any particular hash function.

### Pattern Matching? No.

It does not seem convenient to support pattern matching at the level of primitive operators.

### Recursive Definitions? No.

It is feasible to locally rewrite let-rec groups to loops. However, it's too difficult to make recursion work nicely with the KPN model with incremental, partial outputs.

### Backtracking Operators? No.

Very awkward fit for Glas.

### Arithmetic? Minimal.

There is no end to the number of arithmetic functions we could introduce. But sticking to add/sub/mul/div should be sufficient.

### Break/Continue Loops? No.

Awkward fit for concurrent operators.

### Logical List Reversal? Yes.

Easy to understand, widely useful. Awful performance predictivity - O(1) time or O(N) time - but the O(N) is only paid lazily when accessing the list, and doesn't apply to repeated reversals.

### List Collections Processing? Probably.

I'm inclined to support a variety of list-processing operators such as zip/unzip, transpose, map, filter, scan, fold, flatmap, concat, sum, etc. I would like to have good support for optimizing structure-of-arrays vs array-of-structures.

It's less clear to me whether these operations should also apply to channels, or whether a different set of similar operations should apply.

### Dictionaries and Symbols? Full.

Glas programs will have full ability to inspect and construct dictionaries, e.g. iteration over symbols, composition of dictionaries. A compiler should use static analysis and annotations to determine when certain dictionaries should be represented as C-like structs. 

## Program Operators

### Arithmetic

Glas supports natural numbers and bignum arithmetic. 

        add:(args:[list of ports], result:port)
        mul:(args:[list of ports], result:port)
        sub:(left:port, right:port, left-right:port, right-left:port)
        div:(dividend:port, divisor:port, quotient:port, remainder:port)

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

