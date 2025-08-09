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

#### Linear Move? Defer

It is feasible to introduce a generic operator for moving or swapping a volume of state within an environment. This could translate to moving a struct or struct pointer under the hood. This could respect relational structure, too. A cost is that the environment becomes much less stable, and thus more difficult to render or reason about. But this does offer some benefits for flexibility, modeling queues and such.

I currently intend to elide this feature, see how far we can go with stable structure and whether there is a strong need for linear move or swap. 

### Algebraic Effects and Handlers

Instead of directly defining effectful behaviors in the namespace, I propose to present access to state and effects as implicit parameters to a program. This essentially introduces another 'level' of namespace for the set of handlers in scope. I propose to support prefix-oriented translation and aliasing of handlers and state similar to translation of the host namespace, i.e. `{ Prefix => Prefix | NULL }` associative maps that apply to specific AST nodes such as `(%var "x")`, albeit across definition boundaries.

A handler receives access to *two* environments, host and client, and must robustly distinguish them. To avoid risk of conflict, the default calling convention may present handlers with with two distinct prefixes, e.g. prefix "." binding to host and "$" to client. We can later extend the set of calling conventions as needed.

Initially, we might restrict handlers to letrec-style cliques within scope of a subprogram. This will likely be sufficient for most use-cases. However, if there is a strong use case, we can eventually support full namespace procedures to define sets of handlers.

*Note:* I don't propose to capture continuations in handlers. But coroutines and concurrency could be introduced via separate primitives.

### Concurrency and Parallelism

Coroutines are a convenient way to introduce concurrency to a procedural model. Coroutines are non-preemptive: they yield voluntarily then continue on some condition. Logically, coroutines evaluate sequentially, but an opportunity exists for parallel evaluation consistent with a sequential schedule. However, this opportunity isn't easy to grasp, requiring analysis of a stable schedule and interference.

I propose to initially introduce coroutines that wait on a simple, stateful condition, such as for a variable to be non-zero. This enables open composition of anonymous coroutines and provides an opportunity to initiate parallel evaluation insofar as we determine the condition is stable and intervening or parallel operations will not conflict. We can feasibly extend this condition to support conjunctions, disjunctions, negations, and simple comparisons. It would be convenient to share a notion of simple conditions with branch and loop primitives.

Coroutines in glas will be fork-join structured. For example, 'seq (co P1 P2) P3' would not run P3 until both P1 and P2 are completed. If P1 or P2 is waiting, then the sequence may wait. Ideally, coroutines are associative, such that 'co A (co B C)' is equivalent to 'co (co A B) C'. This requires careful attention to the scheduler, e.g. always prioritize leftmost, or simple round robin. A fair non-deterministic scheduler is associative and commutative, but a deterministic scheduler is a better fit for my vision of glas systems. 

We can introduce primitives to control a scheduler. Among these, I propose to introduce an 'atomic P' structure that fully evaluates P without waiting on external coroutines. Internal waits are handled by introducing a local scheduler, thus P may be expressed using concurrency internally. In context of transaction loop applications, 'atomic' may be implicit for most toplevel application methods, excepting remote procedure calls.

*Note:* A useful validation of this concurrency model is whether we can effectively compile Kahn process networks (KPNs) to run subprocesses in parallel.

### User-Defined AST Constructors

Users should be able to define AST constructors that serve a role similar to macros or templates, albeit independent of front-end syntax. For example, it should be feasible to define an embedded DSL compiler for regular expressions or data formatting.

User-defined constructors receive *positional* AST arguments. However, presenting these arguments as first-class values makes it difficult to maintain context, resulting in [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro) challenges.

It seems too difficult to maintain contextual information with first-class AST values, binding those values back to algebraic effects or an environment. Instead, I propose to present the AST arguments as executable expressions and handlers of sorts. They, in turn, may receive some arguments in addition to operating within their host environment similar to fexprs.

This suggests a basic program model similar to call-by-push-value or vau calculus, something where positional arguments are easily captured with some prefix for naming handlers. This design does hinder reflection on the program, but we can feasibly introduce reflection on programs across namespace and handler boundaries as a separate feature.

### Partial and Static Eval

Instead of a structured approach to partial evaluation, I propose annotations to indicate specific variables are statically determined at specific steps. This is flexible enough to cover ad hoc dataflows, yet easily verified at compile time. Users can still develop front-end syntax and libraries to robustly compose code with structured partial evaluation, reducing risk of errors. But users may also accept risks and freely mix code that isn't designed or maintained with partial evaluation in mind.

Note that we only mark variables as static, not expressions. There is no strong notion of expressions at the level of the glas program model.

### Staged Metaprogramming

The namespace model supports metaprogramming of the namespace, but it isn't suitable for fine-grained code per call site. To support metaprogramming at the call site, we can introduce a primitive for 'eval' of an AST, taking at least two parameters: a namespace localization, and a variable containing the AST value. In most cases, we'll want to insist this is a static variable, i.e. that the AST is fully determined at compile-time.

In practice, I think we'll want at least one more parameter for eval: an additional translation to redirect or restrict the  AST's access to algebraic effects and application state. Although a separate scoped environment translation primitive can solve most issues, it awkwardly leaves the AST variable itself in scope, and it's very convenient to combine the two.

### Tail Call Optimization

The program model may have some built-in loops, but it's convenient to also support efficient tail recursion, especially in context of live coding. There is an implicit stack of local application state. In order to find the opportunity for tail call optimization, we might need to unroll a loop a little to determine which variable allocations may be recycled.

We can still support recursion with a conventional data stack for 'locals', but it might be a better fit for glas systems to insist on tail recursion as the default, emitting a warning if a recursive loop does not optimize.

### Non-Deterministic Choice

It is feasible to provide non-deterministic as a primitive effect (through the namespace) or controlled through algebraic effects. An advantage of primitive non-determinism is that we can more directly represent stable choices and more locally optimize non-deterministic code without full access to effects. As a primitive, we could also introduce an operator to restrict use of non-determinism within a scope, insisting that a subprogram reduces to a single choice.

We might also bind non-deterministic choice to abstract state, allowing for multiple 'streams' of choices.

### Parameters and Results

It seems feasible to present most 'parameters' as algebraic effects. This includes both application state and implicit parameters, but also AST parameters in context of user-defined AST constructors. Presenting those as handlers, instead of first-class AST representations, simplifies precise management of context. We can also separate evaluation of 'expressions'.

If users define a new loop constructor, they could use handlers to access the condition and body of that loop, similar to macros or fexprs but with handlers providing implicit context management and macro hygiene. We could introduce primitive constructors to conveniently 'evaluate' AST arguments as expressions for function calls.

Results would also be modeled as algebraic effects or writing to some 'result' state. Expressions could be understood as programs that write a common 'result' state (or environment structure) before they are fully computed. This would allow for some simple composition based on dataflow conventions without conflating a notion that AST fragments themselves are expressions that evaluate.

#### Keyword Parameters

In the abstract AST, we have positional parameters such as `(Name Expr1 Expr2 Expr3)`. However, keyword parameters are often preferable for extensibility reasons. To support this, we could develop a convention where Expr1 is often a list of keywords. This might not apply to every constructor, but at least to most user-defined constructors.

### Automatic Cleanup? Not at this layer.

We can easily support a 'defer Op' feature, e.g. performing Op before we exit the current scope. This can be implemented by a front-end compiler. It does not seem feasible to tie cleanup back to shared or runtime state in any consistent way. I don't believe primitive support is appropriate.

### JIT Compilation

The runtime should support a JIT compiler. However, one of my design goals for glas is to push most logic into the configuration. An intriguing possibility is to define the JIT compiler, or at least extend it (stages, optimizations, etc.) within the user configuration. This could be subject to runtime version info, making the compiler function configurable and portable. Application settings could also influence JIT, though it might be best to keep this indirect and focus on annotations.

We can first JIT the JIT compiler, then use it to compile parts of the application as needed.

### Unit Types?

I'm still uncertain how to approach unit types for numbers. One idea is to explicitly thread some static metadata through a computation, but it seems difficult to route this in context of conditions and loops. Perhaps we can support something like a unification logic as one of the computation modes?

### Memoization

such that code can be organized as a composition of generators and consumers, without any first-class structures. Most interaction models won't play nicely with hierarchical transactions. However, this *might* be achieved via capturing some local variables into 'objects' for algebraic effects. Perhaps there is something simple we can use, based on explicit yield and continue.

### Implicit Data Stack? No.

It might be convenient to introduce an implicit, local data stack for pushing and popping data. Threads would require 0--0 arity, and we could insist on static arity in general. It's useful for anonymous parameters. However, it's awkward for stuff like open files and such, and it doesn't extend well with keyword arguments and similar.

### Keyword Arguments and Results

As a calling convention, we could provide a static parameter for most method calls that represents the set of arguments provided and also names the results. It might also be useful to make this a primitive feature, a special static parameter to 'call' of sorts, allowing for more flexible integration. 

We could feasibly support explicit arguments to a call in addition to the implicit environment. But I'm not convinced it's a good idea to complicate calls with multiple forms of argument, and it doesn't extend nicely to many use cases.

## Rough Sketch

A tacit concatenative programming model operating on named vars and handlers. Except we do have 'arguments' to AST constructors. We'll present those as handlers, too. It is possible to express concurrent computations via 'thread' sections with 'await' conditions. It is possible to isolate a subset of threads with 'atomic' sections.

Support for locals and resource or handler names is primitive.

## Basic Operations

Everything is an expression and has a value. For procedural operations, that value is often unit.

* `(%seq Op1 Op2 ...)` - 

## Design Thoughts

* For number types, I want unbounded integers, rationals, complex numbers, and vectors or matrices to be the default. But ideally the program model should make it easy to identify and isolate subprograms where we can use bounded number representations to optimize things. 
* Ideally, every program has a clear small-step rewrite semantics. This greatly simplifies debugging.

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.
