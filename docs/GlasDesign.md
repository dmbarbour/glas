# Glas Language Design

## Motive (Why Another Language?)

Purely functional programming has many nice properties.

Unfortunately, the two most popular foundations for functional programming - lambda calculus and combinatory logic - are not convenient for modeling interaction, concurrency, partial evaluation, or structure sharing. Further, evaluation of these models is based on in-place rewriting, e.g. `multiply(6,7)` rewrites to `42`. This destabilizes references into the program, hindering development of [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface).

Further, most FP languages do not scale nicely for a mess of reasons. FFI escape hatches or other features often tie computation to an OS process. Ability to serialize functions and closures is often sacrificed for performance. Named modules or dependencies are a form of mutable reference, which undermines concise representation of large immutable functions. Closed-source modules optimize for separate compilation or protection of intellectual-property at cost of optimizations and ad-hoc model-checking. Nominal types are convenient for application-local type-indexed generic overloads, but don't work nicely with pluggable architectures, mobile code, and open distributed computing.

Glas is purely functional language based on unification. Unification easily represents interactive concurrent computation, and supports fine-grained partial evaluation without special effort. Further, it is monotonic and easily stabilized for direct manipulation. Session types are adapted to prevent conflict or deadlock. Further, Glas is designed with careful attention to both upwards and downward scalability. 

## Semantics Overview

A Glas program is a pure function represented by a structured, directed graph with labeled edges. Evaluation rewrites this graph, using two primary rewrite rules: *unification* and *application*.

*Application* is the basis for computation. A function is applied to a node, called the applicand. Parameters and results are represented as labeled edges. For example, if we apply a `multiply` function to a node, it may read edges `arg1 -> 6` and `arg2 -> 7` then write edge `result -> 42`. Glas defines a usable set of primitive functions and terminal values.

*Unification* is the basis for dataflow. Unification merges two nodes. For example, the node at `result` may be unified with the argument of another applied function. For terminal types, such as numbers or functions, Glas enforces a single-assignment semantics. For inner nodes, unification implicitly propagates over outbound edges with matching labels. Logically, a node has all labels, but unused labels aren't shown.

Glas restricts the structure of the graph to simplify syntax, composition, extension, visualization, and other features. There are many entangled concerns with this aspect, so it is detailed in later sections.

## Effects Model

Glas programs are pure functions. To produce effects, output must be interpreted by external agents, and input fed back into the computation. Fortunately, unification-based dataflow makes this interface easy. For example, a program can directly output a stream of request-response pairs. Responses, when written into the stream, would reach the correct location within the computation.

A Glas compiler will integrate a simple effects interpreter, parameterized by annotations or compiler flags. At this layer, the effects models will be designed for wide utility, performance, and ease of implementation. For example, requests might be translated to C calls based on a declarative FFI description, and the compiler could inline the calls based on static analysis of dataflow.

Effects also need attention within the program. Without implicit parameters, threading the tail of a request-response stream through a program too easily becomes a chore. And we'll generally want to abstract over the stream, to support application-layer effects.

## Modules and Binaries

Glas programs may contain references to external modules and binary data. 

During development, these references will be symbolic, corresponding to file-paths. This allows for conventional file-based development environments. 

Before compilation or package distribution, Glas systems will 'freeze' references via transitive rewrite to content-addressed secure-hashes. The modules and binaries are copied into a content-addressed storage space. Use of content-addressed references simplifies concurrent versions, configuration management, incremental compilation, separate compilation, distributed computing, and many related features.

Frozen modules should include annotations to support a 'thaw' operation, which reverses freeze. 

In practice, Glas modules should have shallow dependencies. Modules are functions, and may instead be parameterized with their dependencies. The same module instantiated twice will have incompatible existential types. External references should be isolated to aggregator modules where feasible.

Favored hash: [BLAKE2b](https://blake2.net/), 512-bit, encoded as 128 base-16 characters with alphabet `bcdfghjkmnlpqrst`. Hashes are not normally seen while editing, so we don't compromise length. The base-16 consonant encoding will resist accidental spelling of offensive words.

Security Note: Content-address can be understood as an object capability for lookup, and it should be protected. To resist time-leaks, content-address should not directly be used as a lookup key. However, preserving a few bytes is convenient for manual lookup when debugging. I propose to use `take(8,content-address) + take((KeyLen - 8),hash(content-address))` as a lookup key.

## Annotations

Glas models 'annotations' as special functions with identity semantics. Annotations use a distinct symbol prefix such as `#author` or `#origin` or `#type`. Annotations may influence static safety analysis, performance optimizations, program visualization, automatic testing, debugger output, and etc.. However, annotations shall not affect observable behavior within the program. 

## Records and Variants

Records in Glas can be modeled by an inner node. Each label serves as a field. To update a record requires a primitive function that computes a record the same as the original except at one label. Update naturally includes erasure: to erase a label, update to a fresh node.

Variants in Glas can be modeled as specialized, dependently-typed pairs: an inner node has a special edge to indicate the choice of label, then the second label depends on the choice. We can use variants as a proxy to working with labels.

Variants can records are normally typed based on their structure. But with suitable type annotations, it is feasible to support GADTs, where we type a structure based on its interpretation. 

Note: Glas cannot meaningfully 'copy' nodes. No shallow copy, no deep copy. Due to unification semantics, a copy would be equivalent to the original because we would also copying all 'unused' edges.

## First-Class Codebase



## Loops

## Failure and Error Handling

## Tacit Programming

Why not lambdas? (E.g. with `$` refs to public node). 


## (Topics)

* Syntax
* Concurrency, KPNs
* Projectional Editing
* Embedded DSLs and GADTs
* Direct Manipulation
* Reactive Streams
* Type System Details


## Type System

Glas will heavily emphasize static analysis. Glas must support universal types, existential types, row-polymorphic types, multi-party session types, and linear types. Beyond these, desiderata include dependent types, exception types, performance and allocation types, and general model checking.

Advanced types cannot be fully inferred. Type annotations are required.

Glas does not have any semantic dependency on the type system: no type-indexed generics, no `typeof`. Thus, types primarily support safety and performance. 


## ....



## Session Types

[Session types](https://groups.inf.ed.ac.uk/abcd/) essentially describe patterns of input and output

input-output patterns. In Glas, we apply session types to input-output parameters of pure functions. For example, a session type of form `(?A . !B . ?C . !D)` might say that `A` is required as an input, then `B` is available as an output, followed by `C` is an input and `D` as an output. With conventional FP, we might represent this type using continuation passing, e.g. `A -> (B, (C -> D))`. 

More sophisticated session types further describe *recursion* and *choice* patterns, enabling expression of long-lived, branching interactions. Conventional data types are easily subsumed by session types: they're effectively output-only (or input-only) sessions.

There are several significant benefits to using session types. First, session types enable interactive computations to be expressed in a direct style, without explicit closures. Second, session types are easily *subtyped*, e.g. the above type is compatible with `(!D . ?A . !B)`. This enables wide compatibility between programs and reduces need for explicit glue code. Third, by more precisely representing data dependencies, session types greatly simplify partial evaluation optimizations.

## Allocation Types

Static allocation and linking is a valuable property in domains of embedded or real-time systems, FPGAs and hardware synthesis. However, this constraint is unsuitable for higher-level applications or metaprogramming.

Glas language will enable developers to assert within session types that functions are 'static' after a subset of input parameters are provided. Glas might also support a weaker constraint of 'stack' allocation, to simplify reasoning about performance and memory management for many higher applications.

## Session Types

The [simple session](http://simonjf.com/2016/05/28/session-type-implementations.html) type `{a?int . x!int . b?int . y!int}` tells us that it's perfectly safe to increment output `x` then feed it as input `b`. That is, we can safely compose interactions without risk of deadlock, without deep knowledge of subgraph implementation details. But for complex or long-lived interactions, this is far from sufficient for systems programming. Fortunately, session types offer a viable path forward, with *choice* and *recursion*:

        type ArithServer =
            &{ mul_int: a?int . b?int . x!int . more:ArithServer
             | add_dbl: a?double . x!double . more:ArithServer
             | quit: status!int 
             }

In session type systems, we often distinguish between 'external' choice `&` vs 'internal' choice `⊕`. External choice is analogous to a method call: the caller chooses the method. Internal choice is like a variant or tagged union: the provider chooses the value. In the example above, we have an external choice of methods including `add_dbl` or `mul_int`. Recursive session types can support unbounded interactions with remote loops.

We can interpret sophisticated session types for purely functional programming. A 'choice' is a promise to use only a subset of a function's input and output parameters. For example, if we choose `add_dbl`, we'll use `add_dbl.a` and `add_dbl.x`. But we won't use the `mul_int.a` or `quit.status`. A recursive session corresponds roughly to a function with unbounded (variable-args) input-output parameter lists.

Intriguingly, session types fully subsume conventional data types. A conventional FP tree is simply a pure-output (or pure-input) recursive session type. We could process that tree lazily or in parallel. We can represent structure-preserving maps as a function that receives inputs then provides outputs at each node in a tree. We can represent push-back behavior for slow consumers by requiring a `sync?unit` input at certain steps. 

Glas adapts session types to support interactive computations between functions, and to improve expressiveness over conventional FP languages.

## Deterministic Concurrency

