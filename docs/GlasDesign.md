# Glas Design

## Motive (Why Another Language?)

Purely functional programming has nice properties - easy to reason about, easy to test, easy to share and reuse. 

But the conventional approaches, based on lambda calculus or combinatory logic, are flawed. It is difficult in those models to explicitly represent structure sharing, or efficiently support concurrent decompositions (such as Kahn Process Networks). Partial evaluation is awkward. Also, these models are unsuitable for [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface), which benefit from stable structure over evaluation and the ability to typefully arrange inputs and outputs into the same region of the screen.

Further, most functional programming languages are unsuitable for distributed computing at the large scale, or embedded real-time and FPGA programming at the small scale. This is largely due to inadequate type systems, ad-hoc escape hatches for mutability or FFI, type-indexed generics, security concerns, or the problems of managing versioned libraries at scale.

Glas is a purely functional language based on confluent graph rewriting. The graph provides an explicit, formal basis for structure sharing. Unification, one of two rewrite rules, offers a convenient basis for partial and concurrent evaluations. The graph is stable even with garbage-collection. Meanwhile, Glas leverages content-addressed dependencies to support robust computing at scale. 

## 








are relatively awkward for representing interactive computations, deterministic concurrency (such as Kahn Process Networks), structure sharing, and partial evaluations.

Glas solves these problems by taking confluent graph rewriting as an alternative basis for functional programming. The graph structure can represent sharing. Unification is good for partial evaluation, interaction, and deterministic concurrency. 

Further, I'm interested in . Today, every user-interface is a walled garden, difficult to compose or integrate, with many hand-written layers of indirection to the program logic. Ideally, we could automatically produce a good user interface from an interface type and a few style annotations. But lambdas and combinators are unsuitable for direct manipulation due to how they partition inputs from outputs, and the unstable structure over incremental evaluations.

Glas supports direct manipulation


Another interest is computing at large scales (distributed computing) and small scales (embedded real-time or FPGA targets).


by rejecting lambda calculus and combinatory logic. Instead, Glas is based on confluent graph rewriting, with rules for unification and application. 




Evaluation destabilizes user input.  

Elements are being replaced or renamed during evaluation. 

 This instability hinders treating the f

 and tend to separate inputs and outputs spatially.  elements being replaced or renamed during evaluation. This instability hinders a lot of external tooling
 lambdas are unstable due to su

Glas is based instead on graph rewriting via unification and application.


A Glas program is a structured graph with labeled, directed edges. There ar


Another interest is distributed computing. For computing at large scales, structure sharing is 


## Programs as Stable, Structured Graphs




## Session Types

[Session types](https://groups.inf.ed.ac.uk/abcd/) essentially describe patterns of input and output

input-output patterns. In Glas, we apply session types to input-output parameters of pure functions. For example, a session type of form `(?A . !B . ?C . !D)` might say that `A` is required as an input, then `B` is available as an output, followed by `C` is an input and `D` as an output. With conventional FP, we might represent this type using continuation passing, e.g. `A -> (B, (C -> D))`. 

More sophisticated session types further describe *recursion* and *choice* patterns, enabling expression of long-lived, branching interactions. Conventional data types are easily subsumed by session types: they're effectively output-only (or input-only) sessions.

There are several significant benefits to using session types. First, session types enable interactive computations to be expressed in a direct style, without explicit closures. Second, session types are easily *subtyped*, e.g. the above type is compatible with `(!D . ?A . !B)`. This enables wide compatibility between programs and reduces need for explicit glue code. Third, by more precisely representing data dependencies, session types greatly simplify partial evaluation optimizations.

## Allocation Types

Static allocation and linking is a valuable property in domains of embedded or real-time systems, FPGAs and hardware synthesis. However, this constraint is unsuitable for higher-level applications or metaprogramming.

Glas language will enable developers to assert within session types that functions are 'static' after a subset of input parameters are provided. Glas might also support a weaker constraint of 'stack' allocation, to simplify reasoning about performance and memory management for many higher applications.

## Commutative Monoids

A commutative monoid is any type together with a commutative composition operator and an identity element. Examples of commutative monoids:

* sums of integers
* maximum of natural numbers
* unions of sets or multi-sets
* all [conflict-free replicated data types (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type)

Commutative monoids are intriguing for two reasons. First, they can model open systems with contributions from multiple sources.


 Second, they 

 purely functional, deterministic computation while reducing the need

 support

 unordered contributions while producing a deterministic result, and thus fit nicely within the determinism constraints of 

multiple unknown sources, and we can compute an incremental result




With session types, it is not difficult to model interactive construction of commutative monoids. Essentially, we have a tree of operations








## Allocation Types








 by envisioning functions as having an unbounded set of input and output parameters. At the lowest level, we have simple 

named input and output parameters. 



* **Session Types** to model interaction, concurrency, and effects
* **Graph Rewriting** to support partial evaluation and rendering
* **Projectional Editing** for extensible visualization and editing
* **Allocation Types** to control GC, support hardware synthesis
* **Content-Addressed** dependencies for large-scale systems, block-chains

Glas solves weaknesses of conventional functional programming. As a consequence, it is very non-conventional, and will benefit from a different programming environment.

## Interactive Computation

We can define an interactive computation as one that produces some outputs before all inputs are provided. Most FP languages use an applicative syntax of form `outputs = fn(inputs)`, and a semantics where all inputs are provided before any outputs are observed. Thus, interaction must be explicitly and indirectly modeled via continuation passing style or monadic types or algebraic effects. 

In contrast, a Glas program enables interaction directly between pure functions. This requires a non-conventional syntax, and opportunistic semantics for internal evaluations. A Glas program or function is represented by a subgraph, with a bundle of labeled edges for inputs and outputs. This graph reduces by opportunistic rewriting. 

Start with a trivial example:


        (6)-(inc)--x              (7)----x
           \a
            (mul)---y     =>      (42)---y
           /b
        (7)

We can take a 'subgraph' by separating the inputs:

        ---a--(inc)----x--
            \
             (mul)-----y--
            /        
        ---b

A client of this subgraph could feed `(6)` to the `a` input, read the `x` output, increment it, then feed backwards to the `b` input in order to compute `y`. Although this interaction is trivial, it's surprisingly awkward to represent in conventional FP languages.

A question is how to 'type' this subgraph and reason about it. Trivially, we can observe the basic types of the edges: `{a:int, b:int, x:int, y:int}`. We can augment this with directionality: `{a?int, b?int, x!int, y!int}`. Further, we can observe that `x` is available as an output even before `b` is provided as input, so we could augment our type with a sequential ordering: `{a?int . x!int . b?int . y!int}`. At this point, we have reinvented a primordial version of session types.

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

## Effects System

Recursive session types easily support a simple effects system:

        type Effects =
            &{ eff1: ... . more:Effects
             | eff2: ... . more:Effects
             | quit: status!int 
             }

This would have a linear structure and would roughly correspond to an algebraic or monadic effects system. Further, it is also feasible to support forks and threads via `fork: left:Effects * right:Effects` or similar, assuming `*` here represents commutative sessions (whereas `.` represents sequential sessions).

l



## Deterministic Concurrency

We can reasonably define a 'concurrent' system as one that interacts with multiple agents and services. Importantly, concurrent systems introduce the 'problem' where events may arrive from

 two independent sources

 Importantly, we shouldn't need a grand central dispatch or any form of 



 between independently defined subcomputations. This is very convenient for expression of complex systems. However, Glas is purely functional and deterministic: modulo debuggers, we cannot observe race-conditions, which sub-computations finish first.



 Deterministic concurrency is convenient for large-scale computations. We can easily leverage cloud computation, for example. Kahn Process Ne

 concurrency preserves observable determinism - i.e. we cannot observe race conditions between computations, but we can see which computations finish 'first'.

 Further, a great deal of task-parallel computation is feasible insofar as these subcomputations are expensive compared to the communications.



 However, for purity, this in this case concurrency is fully deterministic, similar to Kahn Process 


With sophisticated session types, we can independently define and maintain subprograms that cooperate to compute a result. Hence, we can model basic forms of concurrency. Additionally, a great deal of parallelism is feasible, albeit only insofar as loops are designed for relatively coarse-grained communications.








A reasonable question is: what does it even mean to adapt these session types to 


In this case, we have a function that effectively supports a stream of `Mul` or `Add` methods, terminated by a `Quit` method. 


















Use of session types can enable us to track which interactions are 'safe' against problems like deadlock (where we have a loop of dataflow dependencies). Further, they offer an intriguin




        
        



        






with some directionality information: `{a?int, b?int, x!int, y!int}`. Here, `?` represents input while `!` represents output. However, this type still doesn't capture that `x` output only requires the `a` input. So, we augment our type with some ordering: `{a?int, x!int, b?int, y!int}`. At this



In this case, we could understand our 'bundle' as a record of three edges like `{a:int, b:int, x:int, y:int}`, except we should further augment this with directionality like `{x?int, y?int, r!int}` where `?` represents an input and `!` an output. We could give `mul` a type based on this bundle. For a convenient textual projection of the graph, we could even call `mul` based on it:

        g : {x?int, y?int, r!int}

        var p = mul()
        p.x = 6
        p.y = 7
        ... do something with p.r ...

As a slightly more complicated example, we could have two outputs



        





        

Computatio



        


 with a bundle of labeled edges for inputs and outputs. Computation is opportunistic, via rewriting. I'll eventuall

This requires a different syntax

 partial outputs to be provided based on partial input. 



Thus, it is difficult to represent interaction syntactically, so we're forced to work around

instead of `output = fn(inputs)`, which requires providing the inputs up front before any outputs

 This is achieved by requiring all inputs to a function are produced before any outputs

 interaction between subprograms written in a direct style. This idea is two-fold: first, instead of requiring all inputs to a function before producing any outputs, we allow 

outputs from a function may be computed before all inputs are provided, thus we 


First, support for interaction, effects, and concurrency remains awkward. As a community, we have experimented with monadic composition, algebraic effects, linear types. Yet, it remains difficult to model separate loops interacting with messages to produce a result. Further, the models often hinder the forms of reasoning and refactoring that functional purity promises in the first place. 

Second, it is difficult to visualize computation in these languages. The original lambda calculus has a rewriting semantics, which enables rendering intermediate steps and final results. But FP languages today rarely offer any means to even render first-class functions, much less the intermediate computations. This weakness hinders debugging, explanation of code, and creates a barrier between programming and user-interfaces.



[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) offer an intriguing alternative, preserving the locality of  but don't really fit the type systems for functional 

 Second, it is difficult to visualize and debug computation - lambda calculus has a rewriting semantics, but most functional programming languages have a s

 We have developed some usable models - based for example on monadic composition, algebraic effects. But these solutions introduce some rigid structure on computation. 

Conventional F



* **Session Types** support interaction, effects, futures, and concurrency.
* **Term Rewriting** interpretation supports partial evaluation and caching.
* **Projectional Editing** for extensible visualization of code and results.
* **Static Allocation** types support real-time systems or hardware synthesis.
* **Concatenative Style** simplifies composition, refactoring, and streaming.
* **Content-Addressed** dependencies support large-scale systems, block-chains.

As a concatenative language, all functions in Glas operate on an implicit environment. In Glas, this environment includes a record of labeled data and futures. This record includes a data stack, which serves as a convenient intermediate location for literals and computations. Meanwhile, the labeled data is a more convenient route for partial evaluation and fine-grained interactions: we can statically determine or constrain which labels a subprogram touches. 

In Glas, the record environment includes the 'dictionary' or codebase. Hence, the entire codebase is subject to programmatic manipulation. For convenience, Glas supports a lexically scoped environment as a standard access pattern.

Concurrency in Glas is based on fine-grained interactions and partial evaluation. With session types, we can track whether an output from a function is available before all inputs are provided.






Glas is a low-level programming language: common fixed-width numeric types and arrays are built-in, and have predictable representation. Glas also supports generic 




## Adapting Session Types t Pure Functions

Session types were or


In context of pure functions, session types represent a dependency graph between function inputs and outputs. Session types can achieve this with greater convenience and precision than conventional functional programming languages. 

For example, in a conventional language we might have a function of type `(A,B,C) -> (X,Y)`. If we want `X` as an earlier output, we might change the type to `A -> (X, (B,C) -> Y)`. Unfortunately, there is no implicit subtype relationship between these function types, and we'll often need to restructure both caller and the implementation to support this change. In contrast, with session types, we have something closer to `?A !X ?B ?C !Y`, and this is a subtype of `?A ?B ?C !X !Y` or even of `!X ?A !Y ?B !Z`. Further, these programs can be written in a direct style: the types are based on data dependencies, not program structure.

## Interactions and Effects

Intermediate outputs can be observed by the caller then influence future inputs. This is a simple interaction. 

Session types enable convenient expression of *interactive* computations. We can adapt choice and recursive session types to pure functions. A choice of sessions can model variants or object-oriented interfaces or remote server APIs. A recursive session can model unbounded request-response streams or collaborative decision trees.

With session types, we can model streaming data, or streams of request-response interactions. The latter might serve as an effects model, a viable alternative to monads or algebraic effects.

## Process Networks and Deterministic Concurrency

Although interactions start with the caller, they may be delegated to another function call. By abstracting or translating how interactions are handled through other function calls, we essentialy model a concurrent process network. However, unlike most process models, the determinism of pure functions is preserved.

*Aside:* Use of session types with pure functions subsumes [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks): we can model streaming data channels, but we aren't constrained by them. A streaming interaction can effectively model cooperative concurrency between coroutines.

## Other Glas Features or Goals

Glas aims to become a robust, high-performance, systems-level programming language. Glas has many low-level numeric types. Programmers may declare that a program must be statically allocated for embedded or mission-critical systems. Termination up to input should be guaranteed by static analysis. External modules or packages will be robustly referenced, shared, and versioned by secure hash (although development modules may use normal filenames).


