# Glas Language Design

## Module System

Large Glas programs are represented by multiple files and folders. Each file or folder deterministically computes a value.

To compute the value for a file `foo.ext`, search for a language module or package `language-ext`, which defines a program to compile a binary. To compute a value for a folder `foo/`, use the value of the contained `public` module if one exists, otherwise a simple dictionary reflecting the folder's structure.

It is permitted to compose file extensions, e.g. `foo.xyz.json` will first pass a binary to `language-json` then pass the resulting structured value as source input to `language-xyz`. Similarly, for folder `foo.xyz/` we'd apply `language-xyz` to whatever value is normally computed for the folder. Conversely, a file `foo` without an extension is just valued as a binary.

The language module can load other modules while compiling. Loading `module:foo` can find a local file or subfolder whose name is `foo` after excluding extensions. Loading `package:foo` would search for a folder (packages are always folders) based on the `GLAS_PATH` environment variable or other external configuration.

A build may fail due to ambiguous names, dependency cycles, bad language modules, etc..

*Note:* Files and folders whose names start with `.` are hidden from the Glas module system, but can be useful for quotas, caching, profiling, etc..

## Data Model

Glas data is composed of immutable dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`.

Dictionaries are a set of `symbol:Value` pairs with unique symbols. Symbols are short binary strings. The empty dictionary `()` serves as a unit value. Glas programs have full reflective access to dictionaries, e.g. to iterate over symbols or check for presence of a symbol. Flags and optional fields can be supported.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. Symbol `foo` is shorthand for `foo:()`. A Boolean value is `type Boolean = true | false`. 

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

The primary Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs). KPNs consist of concurrent loops and processes that communicate by reading and writing channels. Reading a channel waits indefinitely to ensure a deterministic outcome. Channels buffer writes, which is suitable for high-latency communications. 

Glas processes and channels are second-class. A program reads and writes statically labeled ports, and may externally wire ports between statically declared subprograms. Glas deliberately trades potential expressiveness for simplicity, stability, locality, and performance.

Glas will generally rely on static analysis for type safety, dead code elimination, bounded buffers, and deadlock prevention. Metaprogramming can potentially support laziness, temporal semantics, and other features, using patterns above KPNs. Acceleration is necessary for dynamic programs.

## Program Model

Concretely, a Glas program is represented by dictionary with `code:[List, Of, Operations]`. Each operator is parameterized by statically labeled ports, representing input sources and output destinations. 

External IO ports are accessed via `io:portname`. Local variables, scoped to the program, use `var:varname`. Programs compose hierarchically and statically: for subprogram foo, the external port `foo:portname` maps to `io:portname` within foo. Input and output ports may share the same name (for variables, this is always the case) but are implicitly distinguished by usage context.

Operations on separate ports compute concurrently, but operations modifying the same port are sequenced according to the list. Variables are really two separate ports for writing and reading. A program will typically have concurrent loops that interact via variables and declared subprograms. 

Reading a port is non-modifying. There is a separate operator to advance a read port to its next value, affecting subsequent reads. It is possible possible to push data onto a read port to affect the next read. It is possible to detect end of input. Attempting to read a port after end of input will wait indefinitely.

Writing a port is buffered until read. Although there are no hard limits on buffer size, Glas systems will normally ensure bounded buffers via static analysis.

Wires are primitive loops that read from one port and write to other ports. Wires should be composed and compiled for direct communications across subprogram boundaries instead of routing data indirectly through a parent program.

## Namespace Model

Glas will support definition-level programming with a static, higher-order namespace model. Definitions are resolvable at compile time and acyclic. This ensures namespaces do not influence runtime semantics: we can transitively expand names to primitives, thus namespaces are essentially a compression model.

The envisioned use case is that Glas modules should compute namespace values. This supports a conventional programming style where programs are decomposed into reusable definitions with flexible scoping and export control, then composed as needed. 

The higher-order namespace features can support dependencies, defaults, generic programming, and separate compilation. Favoring namespace-layer dependency injection instead of directly loading modules can reduce duplication and improve flexibility.

Concretely, a namespace will be represented as `ns:[List, Of, Namespace, Operators]`. Namespace operators are much simpler than program operators - oriented around definitions, defaults, visibility. Glas does not support useful computation in the namespace. 

## Annotations

Annotations are concretely represented by `note:Content` in Glas programs or namespaces. Glas systems support annotations in most places, e.g. as a special program or namespace operator, a request in the request-response channel for effects.

Logically, annotations must be *semantically neutral*. That is, ignoring or removing annotations must not affect the observable behavior of the system. Instead, annotations support optimization, static analysis, model testing, debugging.

Annotations serve a valuable role in context of external tooling: documentation and comments, automatic testing, debug logging or breakpoints, profiling, acceleration and optimization hints, type annotations, theorem prover hints, anchors for reflection, recommended widgets for projectional editing or direct manipulation, etc..

## Effects Model

Glas programs will represent procedural effects via request-response channel. The program writes `io:request` then reads `io:response` based on an API of supported requests. This models a single thread for procedural behavior, albeit with background computations while awaiting response.

A request-response channel becomes a bottleneck for effects. This can be mitigated with compiler support and API design for asynchronous effects, fusion with effect handler loops, etc.. Acceleration, memoization, and content-addressed storage can displace effects for some use-cases. At larger scales, it is feasible to introduce fork effects or deployment models.

*Aside:* Injecting effectful operators at compile-time via the namespace is feasible, but hinders external extension and control of code. I think it's wiser to model effects explicitly at every layer.

## Language Modules

To process a file with extension `.xyz`, a Glas command-line utility will search for module or package `language-xyz`, favoring a local module. The exceptional case is bootstrapping, which requires built-in syntax. 

The language module should compute a namespace that defines a compile process. This will use a request-response channel for loading and logging, and also have an `io:source` input and `io:result` output. A viable request API:

* **note:Message** - response is unit; outputs a message for debug logs, progress reports, proposed code changes, etc.. Message have any type, subject to de-facto standardization.
* **load:ModuleID** - A module ID is typically `module:symbol` or `package:symbol`. Response is the result of compiling that module, or a lazy placeholder (requires runtime support).
* **exists:ModuleID** - response is boolean, `true` iff the specified module can be compiled. This can be useful for modeling defaults or flags at the module system layer.

A load error can occur if a module is ambiguous, not found, fails to produce a result, or forms a dependency cycle. Lazy placeholders can defer load errors, and also simplify incremental computing and dynamic linking.

Compile succeeds if `io:result` is output, though logged errors (`note:error:Message`) should be reported. Partial success is possible in presence of lazy placeholders. Because effects are severely restricted, we can guarantee that compilation is deterministic based on the input source and state of the module system.

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

Glas programs should specify minimal buffering for correct and convenient operation. This might involve using `send` instead of `write` in many cases, or occasional use of wires for extended buffers.

Manual buffering is awkard and easy to get wrong. For example, extending one buffer is often useless without extending several other buffers. So, a Glas compiler should grow buffers based on analysis of opportunity and deployment. 

### Program Search

We can develop higher-order program models that represent constraint systems and search spaces for programs. We can define packages that represent catalogs of other packages, with tags and summaries and indexes. Modules could experimentally compose program values, evaluate their fitness for a purpose. 

This would enable programs to be much more adaptive to changes in requirements, preferences, or the ecosystem.

*Note:* Type-driven programs, such as selecting functions based on inferred types, is essentially a limited form of program search. Type safety is another constraint guiding a choice from an implicit search space. 

### Abstract and Linear Types

A subprogram can treat data as abstract by never manipulating it directly. Instead, data is processed through provided functions. Abstraction extends to linearity when duplication and dropping of data are treated as protected manipulations. Abstraction and linearity are not intrinsic properties of data, but are instead constraints on a subprogram that manipulates data.

Glas programmers can use annotations to assert that a subprogram should respect abstraction or linearity. Ideally, this should be verified by static analysis. However, it is also feasible to check these properties dynamically, with sufficient compiler or runtime support.

Abstract and linear data types are useful for performance, especially in context of *acceleration*. Abstraction can protect optimized representations. Linear data can often be modified in-place, optimizing allocation and garbage-collection.

### Modal and Phantom Types

It is feasible for a type system to track metadata such as units of measure, location, and lifespan. Programmers can then control composition based on this metadata in ad-hoc ways. For example, the type system might complain if we try to add apples to oranges, or if we attempt to multiply matrices logically located on separate GPGPUs, or if we attempt to write an abstract ephemeral reference into persistent storage.

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

### Cheap Singleton Lists?

We could feasibly track 'singleton lists' as a special case in the type analysis, or via  a pointer tag bits. Under this assumption, we don't need as many list operations.

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

*Aside:* In Glas systems, a 'bitfield' should normally be represented by keys in a dictionary instead of interpreting a number. It's more expensive, much more extensible and comprehensible.


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

