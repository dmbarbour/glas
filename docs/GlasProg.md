# Program Model for Glas

A program is defined in context of a [namespace](GlasNamespaces.md). An [application](GlasApps.md) program is represented within a namespace by defining a few toplevel names with simple naming conventions. Other names are useful for modularization of programs, abstraction and reuse of program fragments across applications. A subset of names are primitives, defined by interpreters or compilers and standardized for consistency. The convention in glas is to favor the '%' prefix for primitive names, simplifying recognition and propagation of primitives through the namespace.

This document designs and defines an initial set of primitive names.

## Design Thoughts

### Extensible Intermediate Language

The user-defined front-end syntax is supported based via the namespace model. However, I also want the opportunity to evolve the intermediate language - as described in this document - in flexible ways (adding features, removing features, support for DSLs, isolation of subprograms, etc.).

Technically, this isn't difficult to support. We can introduce AST nodes such as `(%Lang AST)` to indicate how a subprogram should be interpreted. This language node can restrict or extend the set of AST primitives, and contextually adjust how any shared primitives are interpreted. This context can apply across call boundaries, insofar as the language permits calls within the namespace.

As a useful convention, we could encourage front-end compilers to use language nodes for most definitions. We may require a language specifier for top-level 'app.\*' definitions.

Ideally, we can also support user-extensible 'primitives', the ability for users to define the '%lang' or AST nodes. This may require a specialized sublanguage, perhaps something like templating, but it should be technically feasible.

### Application State

I propose to model programs as operating on a stable environment of stateful, named resources. Some of these resources may kept abstract to the program, including as open files or network sockets. Others may be simple cells or queues. 

The program should declare its expectations or assumptions about the external environment. The compiler will generate a schema based on integrating declarations. A program may also introduce temporary state resources for use within a subprogram. In context of recursive calls, these temporaries can serve the role of a data stack; we won't explicitly have a data stack. For flexibility, the program can also apply logical transforms to the environment for use in a subprogram. The simplest case is translation of names, but we can generalize to stable editable projections with lenses, getters and setters, etc..

To support dynamic state, the environment shall support tables. To keep it simple, I propose to start with arrays or key-value dicts. In theory, we can eventually extend this to stable, editable views of relational tables with support for a relational algebra.

In any case, the proposed state model excludes the conventions of a heap, pointers, first-class references. Users may indirectly model these features, but the intention is that the environment should be stable, extensible, and browseable.

#### Relational State

It is feasible to introduce access-controled relational states in terms of indexing one environmental resource through another. In case of tables, associations involving two entities implicitly serve as relations, maintaining some extra state about a relationship. Otherwise, associations effectively add hidden columns to a table.

Usefully, we could support transitively deletion of relations based on deletion of table entries. This would simplify cleanup similar to foreign key requirements in a relational database, avoiding reference-based garbage collection.

#### Scoped State

We can partition external state into a few more scopes: persistent, ephemeral, and transaction-local. 

* Persistent state is stored in a shared database. It is accessible to concurrent or future applications, including multiple instances of the current application. A good place for configurations or persistent data.
* Ephemeral state is local to the process or runtime instance, but shared across transactions. Runtime features such as open files or network sockets may bind to ephemeral state. For semi-transparent persistence, a program can translate a subprogram to use ephemeral state instead of persistent state (but not vice versa).
* Transaction-local state is ephemeral state that is cleared implicitly when a transaction commits. This is mostly useful in context of remote procedure calls or other cases where we might invoke multiple methods within a single transaction.

These scopes might be distinguished based on specialized declarations over the environment, or perhaps based on annotations. Either way, it should be feasible to determine inconsistencies in scope assumptions at compile time.

#### Accept States

Data types can feasibly support volatile states that cannot be committed and non-final states that cannot be reset. Method-local and transaction-local vars would be implicitly reset as they leave scope, and we might also block a transaction based on transitive reset for relational state. With integration, perhaps via annotations or declarations, the runtime can kill transactions that would violate assumptions about application state, allowing users to enforce protocols and ensure consistency.

#### Abstract State

Instead of abstract data types, I hope to manage most abstraction at the state layer. This can feasibly be expressed in terms of associative state without any additional features, relying on access control to the indexing resource.

Abstraction of vars is convenient for working with open files, network sockets, and user-defined abstract objects that are held in client-provided state. We could use annotations to logically 'seal' a volume of vars, such that the same seal must be used to access that state. Abstraction of state serves a similar role as abstract data types but is much easier to eliminate at compile-time and avoids potential schema update concerns.

#### Typed State

We might want something like dependently-typed regions of state, where the fields available may depend on other fields instead of only supporting value types. And we'll probably want to support refined types for state. e.g. limiting an integer field to a bounded range. These types could be checked with some ad hoc mix of static and dynamic analysis, rejecting transactions that would violate assumptions or observe a violated assumption.

#### Linear Move? 

For something like open files or network sockets, it might be useful to have 'move' operators within the environment. This could also respect relational state.

However, I don't have a good approach to a general solution that works nicely with aliasing and scoping. We could explicitly model which parts of the environment are movable, i.e. a sort of 'detachable' state boundary, but that instead becomes troublesome for schema change. One of the better options is to verify that moves are linear, avoiding any loss or duplication. This can probably be checked at compile-time in most cases.

Regardless, we will want APIs that let us move abstract open files, network sockets, FFI threads, etc., ideally while respecting associated resources.

### Parallelism and Concurrency

My vision for glas systems is that we start with a simple procedural programming model. Unfortunately, simple call-return structure is awkward for many use cases. A common alternative is to express a program as multiple 'threads' that interact through shared state. However, this introduces its own issues - race conditions and pervasive non-determinism make it difficult to reason about system behavior or ensure a deterministic outcome.

A viable solution is to introduce 'await Cond' and a fair deterministic scheduler. To express concurrent operations, we could support something like 'thread P1 P2 P3' as a procedural operation that returns only when all three subprocesses return, and 'awaits' when all non-returned subprocesses await. Ideally, 'thread' is associative; this might be achieved by having the scheduler always prioritize the leftmost process that can continue. It might also be convenient to also support an 'await.case' that supports multiple (Cond, Op) pairs, and 'yield' as essentially 'await True'. 

To control concurrency, we can introduce an 'atomic Op' operator. Evaluation from await to the next await is implicitly atomic, but this would further influence the scheduler to handle all 'awaits' within Op before returning. It becomes an error if 'atomic' halts on 'await' instead of returning.

This design simplifies expression of concurrency but leaves parallelism as a challenge for the optimizer. It seems feasible to extract many opportunities for parallelism via static analysis, especially if programs are designed for it (avoiding shared memory between threads, using different ends of queues). But there are also dynamic approaches, e.g. running steps optimistically in parallel hierarchical transactions then ordering operations according to the fair deterministic schedule. Of course, there are many other opportunities for parallelism based on dataflow analysis, accelerators, or sparks.

*Note:* The runtime will implicitly wrap transactional application interfaces such as 'app.step' with 'atomic'. A threaded application defining 'app.main' would not be implicitly atomic.

### Partial and Static Eval

Explicit support for static evaluation seems difficult to get right, especially in context of composition, conditionals, and concurrency. It is much less difficult to annotate an assumption that a compiler will perform certain computations at compile-time, then simply raise a fuss if that is not achieved. We can also leverage namespace macros for more explicit control of static eval in many cases.

This might be expressed in a program as 'static Expr' or 'static.warn Expr', with an annotation in the intermediate representation of the program. Some primitive AST nodes or effects may also require partial static evaluation.

### Metaprogramming and Handlers

In addition to namespace layer metaprogramming, the program layer should include at least one primitive for integrating a computed AST based on a localization. However, it's awkward to integrate just an AST fragment. We often want helper subroutines even in metaprogramming. This suggests introducing a namespace for metaprogramming, ideally with controlled access to the local context. This could align well with effects handlers.

One viable solution is to introduce algebraic effects handlers through a namespace procedure. This should be evaluated at compile time in most cases.

### Non-Deterministic Choice

It is feasible to provide non-deterministic as a primitive or controlled through algebraic effects. An advantage of primitive non-determinism is that we can more directly represent stable choices and more locally optimize non-deterministic code without full access to effects. As a primitive, we could also introduce an operator to restrict use of non-determinism within a scope, insisting that a subprogram reduces to a single choice.

We might also bind non-deterministic choice to abstract state, allowing for multiple 'streams' of choices.

### Unit Types

I would like good support for unit types early on when developing glas systems. But I don't have a good approach for this yet. Annotations seem the simplest option to start, perhaps bound to the vars. 

### Stowage

### Memoization

such that code can be organized as a composition of generators and consumers, without any first-class structures. Most interaction models won't play nicely with hierarchical transactions. However, this *might* be achieved via capturing some local variables into 'objects' for algebraic effects. Perhaps there is something simple we can use, based on explicit yield and continue.

### Tail Call Optimization?

I would like to support tail-call optimization as a basis for loops without expanding a data stack. Short term, we don't need to worry about this. And I have some suspicion that TCO will prove infeasible or useless in context of transactions and incremental computing. But it should be feasible, in controlled circumstances.

One viable approach is to have the compiler 'recycle' local var slots as they leave scope, at compile time. For tail recursion, we might need to allocate a few steps at compile-time before we find an allocation that was used previously in the cycle. We can then compile each allocation separately, or treat a list of indices as an added static parameter.

### Implicit Data Stack? No.

It might be convenient to introduce an implicit, local data stack for pushing and popping data. Threads would require 0--0 arity, and we could insist on static arity in general. It's useful for anonymous parameters. However, it's awkward for stuff like open files and such, and it doesn't extend well with keyword arguments and similar.

### Keyword Arguments and Results

As a calling convention, we could provide a static parameter for most method calls that represents the set of arguments provided and also names the results. It might also be useful to make this a primitive feature, a special static parameter to 'call' of sorts, allowing for more flexible integration. 

We could feasibly support explicit arguments to a call in addition to the implicit environment. But I'm not convinced it's a good idea to complicate calls with multiple forms of argument, and it doesn't extend nicely to many use cases.

## Rough Sketch

A tacit concatenative programming model operating on named vars and handlers. It is possible to express threads or waits for concurrency, albeit scoped by atomic. 

Support for locals and resource or handler names is primitive.



## Basic Operations

Everything is an expression and has a value. For procedural operations, that value is often unit.

* `(%seq Op1 Op2 ...)` - 

## Design Thoughts

* For number types, I want unbounded integers, rationals, complex numbers, and vectors or matrices to be the default. But ideally the program model should make it easy to identify and isolate subprograms where we can use bounded number representations to optimize things. 
* Ideally, every program has a clear small-step rewrite semantics. This greatly simplifies debugging.

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.
