# Glas Language Design

## Module System

Large Glas programs are represented by multiple files and folders. Each file or folder deterministically computes a value.

To compute the value for a file `foo.ext`, search for a language module or package `language-ext`, which should include a program to process the file's binary. To compute a value for a folder `foo/`, use the value of the contained `public` module if one exists, otherwise a simple dictionary reflecting the folder's structure.

Language modules have access to limited compile-time effects, including to load values from external modules or packages. File extensions are elided. For example, loading `module:foo` could refer to a local file `foo.g` or `foo.xyz`, or a local subfolder `foo/`. Loading `package:foo` instead searches for folder `foo/` based on a `GLAS_PATH` environment variable.

Ambiguous references, directed dependency cycles, invalid language modules, and processing errors may cause a build to fail.

To support a conventional style involving dictionaries of definitions with flexible dependencies and export control, a module should compute a value that represents a program namespace.

*Note:* Files and folders whose names start with `.` are hidden from the module system. A `.glas/` folder might be used for extra input to the Glas command-line utility, such as quotas.

## Data Model

Glas data is immutable. Basic data is composed of dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`.

Dictionaries are a set of `symbol:Value` pairs with unique symbols. Symbols are short binary strings. The empty dictionary `()` frequently serves as a unit value. Glas programs may iterate over symbols and test for presence of a symbol, thus flags and optional fields are supported.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. Symbol `foo` is often shorthand for `foo:()`.

Glas uses lists for all sequential structure: arrays, binaries, deques, stacks, tables, tuples, queues. To efficiently support a gamut of applications with immutable data, Glas systems will represent large lists as [finger trees](https://en.wikipedia.org/wiki/Finger_tree). Glas further uses [rope-style chunking](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) for large binaries to minimize overhead.

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

*Note:* The Glas command-line tool may privately compile and cache programs for performance, e.g. compile language modules into plugins specific to the command-line tool. This should be hidden from normal users of the tool.

### Staged Programming

Glas supports metaprogramming at two layers.

First, the module system supports ad-hoc metaprogramming. Language modules represent functions. By integrating an interpreter function, language modules can support macros or arbitrary staging.

Second, the Glas program model is amenable to partial evaluation. With annotations, a programmer can explicitly represent the intention that certain values are computable at compile-time. The compiler can raise a warning or type error in case of problems.

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

The Glas program model is an intermediate language represented by structured data. Surface syntax is provided by language-modules, and any syntactic sugar will need to be  little bit of 'compilation' might be performed by the language modules.

Concretely, a Glas program is a list of operators. Operators are variant data. Operators are interpreted as manipulating a program environment, while the list represents sequential composition.

The program environment consists of data stack, external ports, variables, tasks, wires, hierarchical processes, annotations, and a higher-order namespace.

1. The data stack serves as a scratch space for calculations and transitory space for reading and writing ports. Always starts empty, and should have statically computable size.
1. External ports are referenced by `io:portname`. Every program is a process, with these ports modeling parameters and results. No implicit buffering at this level.
1. Variables are referenced by `var:varname`. Logically, a variable is a trivial process that buffers a single value between read and write. Variables start empty.
1. Tasks are anonymous background computations. They share a program's ports, but have their own data stack. The task blocks subsequent use of the ports it uses until finished with them.
1. Wires are specialized tasks that should be easy for a compiler to optimize. The basic wire will repeatedly read from one port and write to another until source or destination is closed.
1. Hierarchical processes can be instantiated. The child process's external ports can be referenced via `proc:procname:portname`. Abstract subprograms must be integrated this way.
1. Annotations are embedded notes to support external tools, e.g. for performance, profiling, debugging, static analysis, or projectional editing. Annotations must be semantically neutral.
1. The higher-order namespace can define reusable subprograms and higher-order structures thereof. Glas names are resolvable at compile-time and do not affect runtime semantics.

The structure of a Glas program is stable and rigid. Names and ports are statically bound. There are no first-class functions. However, ports cover the common roles of continuations. And the higher-order namespace can support flexible composition.

## Namespace Model

The Glas namespace model is generic with respect to the type of object being named, and the structure of names. The Glas program model depends on this namespace model, not the other way around. 






## Effects Model

Request-response is a simple pattern for effects. This could be expressed by a process writing a message to `io:request` then reading `io:response`, repeating as desired. This pattern corresponds to single-threaded procedural programming. The request-response stream is a 'thread' of effects. 

However, for modeling concurrent systems, multiplexing requests through a central request-response channel is awkward, and too easily becomes a performance and synchronization bottleneck.

A viable solution: Model effectful programs as higher-order programs that depend on abstract, effectful subprograms. The compiler statically injects the dependencies. This has positive performance implications: effects can be locally integrated with generated machine code. 

Unfortunately, this design makes effects much more difficult to observe or control than a request-response channel.

This problem is mitigated by the namespace model: a program can wrap effectful operators before passing them onwards to a subprogram. This supports simple abstraction or attenuation patterns. Abstract and linear types might further ensure every effectful operation has unique identity and clear provenance to support hierarchical control.

## Abstract and Linear Types

Abstraction and linearity are not intrinsic properties of data. Instead, they are constraints on how a subprogram uses data.

A higher-order program can treat data as abstract by manipulating the data only through provided functions. Abstraction extends to linearity by considering duplication or dropping of data as manipulations.

Glas programmers can use annotations to insist that a subprogram should respect data abstraction or linearity. This causes the compiler to raise a warning or error in case the expectation is violated.

Abstract and linear data types are useful for performance, especially in context of *Acceleration*. Abstraction can preserve optimized under-the-hood representations between operations. Linear data can potentially be modified in-place, reducing memory management overheads.

Abstract and linear types are also useful for designing a robust, scalable effects API.

*Aside:* Linear types have a bad reputation due to awkward feature interactions with pattern matching, closures, exceptions. However, a suitable syntax can help, and there is less friction with KPNs compared to lambda calculus.

## Language Modules

To process a file with extension `.xyz`, a Glas system will search for module or package `language-xyz`, favoring a local module. The exceptional case is bootstrapping, which requires built-in syntax.

A language module should specify a namespace value that defines a 'compile' operation. The compile operation will take a source binary  and produce a result while performing limited compile-time effects.

Supported compile-time effects: 
* loading modules
* logging messages

Compilation will fail if no result value is produced, a dependency cycle is detected, or an `error:(...)` message is logged. On success, this produces a deterministic value. The set of log messagess is also deterministic, but may be emitted in non-deterministic order.

For expensive builds, logging should be used to report incremental progress. In the extreme case, we could log messages like `progress:(task:regalloc, step:37, max:100)` to support progress bars.

## Glas System Patterns

This section describes some high-level visions for how Glas systems are managed or used. These ideas indirectly influence Glas design.

### Automated Testing

Within a Glas folder, any module with name `test-*` should be processed even if its value is not loaded by the `public` module. Language modules support ad-hoc expression of tests via files, logging `error:(...)` to report issues. Static analysis can be performed as normal tests.

Testing of computed executable binaries is theoretically feasible via accelerated simulation and fuzz-testing. However, accelerators won't be immediately accessible, so more conventional methods are required short-term.

### Graphical Programming

Language modules enable Glas to bridge textual and graphical programming. Graphical programming can be supported by developing a specialized syntax, nodes annotated with graphical markup for layout and presentation.

Presentation might involve calendar widgets for date values, color pickers for color values, sliders for numbers, etc.. In the more general case, an entire file might be rendered as a big widget. Projections feasibly integrate multiple files, via embedding, portals, or links. At an extreme, it is theoretically feasible to project programs as video games.

Language modules could make the presentation more explicit by defining utility functions suitable for presentation of languages in various media, such as a web-app.

### Live Coding Applications

In a live programming system, programs represent active intentions of users. Changes to a program should immediately affect the real-world. However, this requires careful attention to how state and effects are modeled, to avoid losing information or breaking interactions.

A good model for live coding:

* Program is a deterministic transaction, repeating.
* Deterministic abort pauses program, awaits change.
* Request-response to support abstract state, queries.
* Program can be aborted and updated at any time.

Transactions via request-response can hide state such that writing to the end of a queue doesn't conflict with every other process that writes to the end of the same queue. Similarly, if a query returns some lossy observation on state, then state changes might not change the query result, avoiding conflicts.

Large transactions have high risk of conflict. To support large scale computations, we might need

To support larger scales, 

the effects model might support static partitioning of the application into several independent transactions.

So, it can be useful to partition transactions into smaller ones. This could be achieved via 'fork' requests.


 For example, `fork:[foo,bar,baz]` would logically split the transaction into three, responding respectively with `foo` or `bar` or `baz`. All three transactions would run concurrently, and even repeatedly if conditions prior to `fork` are stable.

This model is simple, accessible, extensible, composable, and scalable. It could be further supported by a syntax built around observation-action rules. This would make a good default model for building applications. 

### Program Search

As the Glas system matures, it might be useful to shove more decision making to expert systems encoded into the module system.

At a lower level, automate some data-plumbing. At a high level, describe programs with hard requirements, heuristic preferences, and a search space for potential solutions. A staged program can search for programs that achieve these goal, leveraging the limited intelligence we can integrate via rules or machine learning.

To support search, Glas programmers can define packages that catalog and curate other packages. Catalogs should include names and summaries of other package, and perhaps a reverse-lookup index. Summaries might include tags and types.

### Incremental Indexing



## ACTIVE OPERATOR DESIGN

I haven't found a solid set of guiding principals for choice of operators. But some hand-wavy goals: 

* operators are widely useful and easy to understand
* performance is robust, predictable, controllable
* scalable computations over distributed processors

Implicit representations should be avoided. 

### Deep Equality? No.

Glas could support a structural equality comparison for arbitrary values. However, this would have unpredictable performance when comparing channels.

### Pattern Matching? No.

I'd like to support pattern matching, including view patterns and pattern guards. However, it does not seem feasible to support pattern matching at the level of primitive operators. This will need to become a feature of the language modules.

### Namespace Conditionals? No.

A least-expressive option for 'defaults' is to apply a backup definition in case a particular symbol is undefined. However, this doesn't cover a lot of cases where we might wish to define our defaults based on which other elements are defined.

An interesting option is to support conditionals based on an arbitrary set of defined symbols. E.g. if `(x,y)` is defined, then apply a namespace operation, else another operation. This would limit namespaces to finite boolean logic, capable of expressing flags and defaults.

However, if we're capable of boolean logic over fields in a dictionary, we could indirectly represent natural numbers up to some arbitrary boundary via bitfields. So, perhaps we should support basic arithmetic and numeric operations, too. A similar argument applies to representation of lists using recursive record structures.

If we begin to model rich data structures in our namespace logic, we should be able to abstract this logic. At this point, we'd need another namespace to access the namespace. This is feasible, but is NOT a path towards simplicity or user comprehension.

So, my proposal is to limit namespace computations to unconditional defaults. In this case, we might support defaults for a set of symbols in terms of each other. Static analysis could then identify minimal 'sets' of definitions to avoid recursion.

### Recursive Definitions? No.

It is feasible to locally rewrite let-rec groups to loops. However, it's too difficult to make recursion work nicely with the KPN model with incremental, partial outputs.

### Staged Programming? Indirect.

I rejected staged computing within the namespace model above. It is possible to add an operation `stage:([] -> Prog))` to the program model. A benefit of this is that we could integrate data from the namespace to compute a program. However, this would make it very difficult to reason about *effectful* operators.

Fortunately, even without explicit staging in the program, we can support staging via the module system and partial-evaluation at the compiler. 

Partial evaluation could be augmented with static types to insist that certain values are compile-time computable, perhaps session types. 

### Backtracking Operators? No.

I could develop a `cond:(try, then, else)` and `fail` operators instead of an effectful model. However, it doesn't fit nicely with channels or effects.

### Arithmetic? Minimal.

There is no end to the number of arithmetic functions we could model. However, I'd prefer to keep this relatively minimal within the bounds of convenience.

### Annotations? On names.

Annotation of values is a form of hidden representation, a poor fit for Glas. So we should restrict annotations to names only.

### Named Loops? Not now.

I could name loops, e.g. such that I can later break/continue the loop. However, I'm uncertain that this is a good idea, doesn't fit combinator logic for example.

### Reuse of process names...

If a process is defined within a loop, it is scoped to that loop. I could also support shadowing of names, but I think it's better to not do so.

### Reuse of ports...

Conceptually, a feature I'd like is to *break* wires conditionally, e.g. based on a specific value over that wire. This would allow me to wait for a particular value to be observed. 

We might be able to model this by having some 'continuation' when wiring, i.e. we wire to a process, but then 









Breaking Wires...

We can model wires that break after transferring a large amount of data.

### Logical List Reversal? Maybe.

Reversing a list takes O(N) time. But, logically, it is feasible to reverse a list in O(1) time then operate on it with an extra check at the reference. 

A concern is that it becomes difficult to reason about how a particular implementation will represent the logical reversals, or how aggressively this is performed. Performance becomes unpredictable, which is something I'd prefer to avoid.

However, performance predictability is not the top priority for Glas, and mostly-predictable is acceptable. Comprehensible performance is the issue.

### List Collections Processing?

I'm inclined to support a variety of list-processing operators such as zip/unzip, transpose, map, filter, scan, fold, flatmap, concat, sum, etc. I would like to have good support for structure-of-arrays vs array-of-structures.

It's less clear to me whether these operations should also apply to channels, or whether a different set of similar operations should apply.

### Dictionaries and Symbols?

Glas programs will have full ability to inspect and construct dictionaries, e.g. iteration over symbols, composition of dictionaries. A compiler should use static analysis and annotations to determine when certain dictionaries should be represented as C-like structs. 

## Program Operators

### Stack Manipulation 

Glas uses a data stack for most primitive operators. This supports implicit source and destination. 

        data:X                      -- X
        copy                      X -- X X
        drop                      X --
        swap                    Y X -- X Y
        dip:(S. -- S'.)        S. X -- S'. X
        sip:(X -> Y)              X -- Y
        box                      X  -> [X]
        unbox                   [X] ->  X

* **data** - push constant onto stack
* **copy** - copy top stack value
* **drop** - remove top stack value
* **swap** - switch the top two stack values
* **sip** - apply subprogram to the top stack value
* **dip** - apply subprogram below top stack value
* **box** - capture environment onto singleton stack
* **unbox** - release environment from singleton stack

Data plumbing on a stack involves an ad-hoc combination of dip, swap, copy, and drop operations. Language modules can model syntax with  conventional variables and lambdas then compile to stack machine. Of course, some users might favor a Forth-like programming style.

The sip and dip operators support structural scoping of subprograms. The box and unbox operators enable programs to work with non-stack environments and capture of the current stack as a value on a new stack. 

A stack is concretely represented by heterogeneous list. Head of list is top of stack. However, use of data stacks should be constrained more than dynamic lists. Safety analysis should reject Glas programs where stack type or size is difficult to statically predict.

### Arithmetic

Glas supports natural numbers and bignum arithmetic.

        add                     N N -- N
        mul                     N N -- N
        sub                     N N -- N
        div                     N N -- N
        mod                     N N -- N

Subtraction will return 0 if the second parameter is equal to or greater than the first. Division and modulo fail if the second argument, divisor, is zero. We can consider adding new functions for convenience.

Glas is not designed for high performance numeric computing. The expectation is that Glas systems should *accelerate* an abstract CPU or GPGPU with access to fixed-width integers, floating point, vectors and SSE, etc. The Glas language then supports communication between abstract processors, and their reference model for fuzz-testing.

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

