# Program Model for Glas

In the intermediate representation, a program is an abstract [namespace](GlasNamespaces.md) of definitions using a Lisp-like intermediate language. An application namespace must define methods recognized by the runtime, e.g. start, step, switch, signal, settings, http, and rpc. The abstract assembly references undefined names, typically with a '%' prefix, as primitive AST constructors or keywords. 

 and ' an interface via this namespace. 

Assembly constructors are provided via names with a '%' prefix, e.g. `(%sum Expr1 Expr2)` might represent a primitive expression to compute and return the sum of two expressions. 

Abstraction through the namespace ensures extension, restriction, and intervention on subprograms is supported via systematic rewrites of names. However, such rewrites are expensive, resulting in a bloated and redundant namespace. Ideally, the abstract assembly should support additional layers of abstraction, e.g. parameters, external wiring, algebraic effects. 

In glas systems, programs are transactional and [applications](GlasApps.md) are expressed as transaction loops, leveraging non-deterministic choice and incremental computing as the basis for concurrency. Distributed transactions and remote procedure calls form a basis for larger systems. Live coding is assumed, so we should avoid entangling application state with the current codebase.

This document proposes and motivates a set of assembly constructors for glas systems.

To support incremental computing and loop fusion, we might need some careful design such that we can isolate dependencies from sequential computations. Ideally, our 'procedures' support a fair bit of parallelism internally, and incremental computing within each parallel fragment. Some sort of dataflow?

## Design Decisions

* Built-in Transactions.
  * Transactions independent of conditionals, but easy to compose.
  * Transactions should have useable return value even on abort.
  * Some sort of return value or transaction-scoped vars not reset.
  * Scoped as blocks. Perhaps set a transaction var to indicate commit.
* Algebraic Effects.
  * Implicit parameter namespace of algebraic effects independent of program namespace.
  * Second-class algebraic effects also serve limited roles of first-class functions.
  * No dynamic 'return' of a function, macros may abstract definition of effect handlers.
  * Should support higher-order handlers that access effects from caller, not just of host.
  * Support static parameters and partial eval of algebraic effects, flexible integration.
* Stack Objects.
  * A second-class way to 'return' functions, or abstract over algebraic effects handlers.
  * An effective basis for multi-step interaction and composition of staged subprograms.    
* Built-in Staging.
  * Explicit support for staged algebraic effects and operations.
  * Explicit support for partial evaluation with static parameters and results.
* Linear abstract data, runtime scoped.
  * Dynamic enforcement as needed, e.g. via metadata bit in packed pointers.
  * Viable basis for open files, network sockets, and FFI bindings. 
  * Generally conflate with runtime scoped data.
* Reject dynamic channel references.
  * Technically feasible, via linear reference understood by runtime or database.
  * Entangles connectivity with state, hindering live coding and open systems.
  * Difficult to simulate with parallelism via algebraic effects and local state.
  * Favor algebraic effects and remote procedure calls for interactions, instead.
  * Do support static second-class queues, distinct ops to read and write queue.


## Design Goals

Without first-class channels, we can still simulate second-class channels in terms of reading and writing a shared queue in the database. But without linear channel refs, it is also difficult to achieve parallelism for dynamic channels.

Perhaps the solution is closer to programmable switching fabrics. We could try to make it easy to 'optimize' stable routing, with mux and demux. Both within a parallel loop and between transactions. What would this look like? Static routing is easier, but can we extract stable conditionals for dynamic routing?

A static optimization is essentially that a predictable future series of data moves can be integrated into a single move. 



Could support channels within app more generally, but:
    * Results in inconsistency between apps.

* Reject channels or wormholes within apps. 
  * In theory, could support via linear abstract data. 
  * In theory, could support channels or wormholes for internal use in app.

  * 
  * Avoid first-class channels as linear objects (for both live coding and consistency with IPC)


  

## Design Thoughts

* Parallel and concurrent computation within a transaction is very convenient for incremental computing of distributed transactions. Ideally, opportunities for parallelism can be recognized statically, and concurrency is separate from non-deterministic choice, and concurrency allows for flexible staged computing. 
  * For parallelism, it would be convenient if we can statically distinguish operations that write to one end of a 'list' or queue variable from those that read at the other end. There are likely many similar specialized cases.
  * Concurrency can complicate the effects API. Perhaps solving this is the biggest issue: can we model concurrent, distributed effects without loss of determinism or confluence? How do we model a concurrent runtime object?
* A program should be able to control (extend, sandbox, etc.) the 'abstract environment' exposed to its subprograms in a manner consistent with the environment presented to the program. That is, consistency across layers of abstraction is a priority.
  * All access to effects should be algebraic or otherwise provided implicitly through the call graph. It should be feasible to restrict a subprogram's access to effects, and also to rename or wrap effects visible to a subprogram.
  * In context of references or linear objects from the runtime, the user must be able to introduce similar references or linear objects connected to the environment exposed to subprograms. 
  * We should have effective support for unwind behavior when we exit a scope, e.g. to clean up abstract environment.
* Managing scope and aliasing of references is troublesome, requiring careful attention and sophisticated types in context of stack objects, live coding, orthogonal persistence, and remote procedure calls. Aliasing tends to hinder parallelism. I'd prefer to avoid the sophisticated type systems that needed to truly get references right.
* In most cases, it is feasible to replace references with abstract linear data. Users can introduce their abstract linear data types via annotations to wrap or unwrap data. Runtime APIs for filesystem or network can transparently wrap OS provided handles or sockets as abstract linear data, allowing for safe parallel file access.
  * Linearity can be encoded into data for efficient dynamic enforcement using a metadata bit in packed pointers.
  * OS-provided references wrapped as linear objects  - e.g. open files or sockets - also benefit from 'runtime' scope. This would block storing the object into a persistent database or passing it over RPC.
  * Runtime scope also makes linearity a lot more robust, easier to track and enforce. Consider conflating runtime scope and linearity into a single type to simplify the system. 
* Channels are convenient for modeling flexible dataflows in a concurrent system. A runtime could let users allocate channels, returning a connected pair of linear objects for one-way communication. Further, channels could also transfer linear objects. We might want something like session types for channels.
  * The inability to transfer linear channels across RPC boundaries would result in an inconsistency between inter-process communications and local concurrency.
  * Stateful entanglements of channels will likely hinder live coding, insofar as we interface through dynamic channels instead of code and stable state resources.
  * Might prefer to avoid channels for these reasons.
* Recursion is convenient for processing of tree-structured data and provides opportunity to represent extensible loops in the namespace. However, recursion will hinder some forms of static computing, i.e. it cannot be directly aligned with the call graph.
  * If we don't have recursion, we end up implementing it indirectly via lists and such. No reason to block recursion for memory reasons without also blocking allocation.
  * Perhaps annotations could constrain recursion for some subprograms.


* Support for hierarchical transactions is very convenient for modeling grammars. However, it should be separate from backtracking conditionals, otherwise we cannot easily refactor backtracking transactions that share a common prefix. We may also need some output that is dependent on observations within a transaction. Interaction with concurrency requires careful attention.
* I would like effective support for staging, including static parameters and partial results from a computation. Ideally, staging supports ad hoc interactive definitions rather than just a single call-return. An intriguing possibility is to support a fractal namespace aligned with the call graph as a medium for concurrent staging.
* For number types, I want unbounded integers, rationals, complex numbers, and vectors or matrices to be the default. But ideally the program model should make it easy to identify and isolate subprograms where we can use bounded number representations to optimize things. 
* I want to support unit types for numbers and other useful context, ideally propagating through a computation at compile time. And I'd like users to have flexible support to define similar context for other roles. I'm not sure how to approach this yet.
* I would like to automate support for indexed and editable relational database views. It is feasible to model the database as an accelerated data type or linear object, but it might be easier to support static analysis of views if the database model is built-in to the program model.
* Ideally, every program has a clear small-step rewrite semantics. This greatly simplifies debugging.

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.


# Old Stuff

The proposed program model is procedural, albeit extended with transactions and algebraic effects. I'm still exploring the possibility to reduce need for first-class references and support parallelism within the transaction. 

* Transactional - In general, we assume toplevel application methods like 'start', 'step', or 'http' are evaluated in implicit transactions. The constructors include further support for hierarchical transactions. Transactions are separated from backtracking, and it is permitted to return limited information from an aborted transaction.
* Algebraic Effects - Conventional procedural languages provide effects through abstract definitions in the application namespace. This hinders intervention and sandboxing. I propose to instead model the abstract environment as an implicit object with methods, subject to override when presenting parts of the environment to subprograms.
* Explicit Context - I would like to track some flexible ad hoc static context across operations, both for safety checks and comprehension. One use case includes tracking units for numbers across a computation. TBD: still working out how this should be modeled. Ideas include: constraint model with namespace or overlay of abstract environment.
* Stable Graph - As much as possible, we should stabilize and control interaction with the environment both within and between procedures. This provides a basis for unchecked parallel evaluation. Ideally, we can reduce most cases to 'static' connections, but there may be some cases (e.g. 'eval' or dynamic references) where we must bind to the environment dynamically. In those cases, we should still be able to reason about and restrict which references are used.
* Pervasive Parallelism - It should be possible to evaluate different parts of a large procedure in parallel, especially including loops (so we can start processing the next loop before the last cycle finishes), such that we can model concurrency both within and between procedures. In addition to the stable graph of relationships, this may require restricting the effects on each resource, such as writing versus reading a queue or network socket, potential support for CRDTs.

These features have a widespread impact on program expression. For example, a request-response pattern over the network will require at least two transactions (so we can commit the request). We'll avoid first-class references like sockets or file handles, instead binding responses to a stable environment. With careful design, it should be feasible to model parallel interaction with multiple network connections and open files even within a single procedure.

## Resources, Registers, and References?

A procedure operates on an abstract environment and may restrict or extend the abstract environment presented to subprograms (i.e. via algebraic effects). This should be mostly independent from the program namespace (modulo reflection or 'eval'), yet is similar in nature insofar as it is convenient to 'name' features or methods of the abstract environment for purpose of operations, extensions, and restrictions. To support parallelism, restrictions must be precise and amenable to static analysis. Extending this environment is a simple basis for algebraic effects.

For open files, network sockets, dynamic channels, etc.. I intend to avoid first-class references (i.e. references as data) because they make it difficult to reason statically about sharing and parallelism. In theory, this can be solved using substructural types for linearity and lifespan (e.g. runtime or ephemeral). However, I prefer a robust structural solution instead of an optional analytic solution.

A viable structure is to introduce named 'registers' for linear objects in the environment, separate from the normal data registers. A procedure can then operate on objects through these registers, and potentially move objects between registers. In theory, we could generalize registers to model named channels or stacks, allowing them to contain a simple sequence of values and support a higher level of parallelism based on how read and write access are used.

Recursive functions need some careful attention. One option is to forbid recursion, but that is awkward for a lot of use cases. Instead, I would prefer to carefully design recursion to use conventional stacks of ephemeral resources, and support parallel operations across past, present, and future stacks. 

## Dynamic Collections

A program might need to open multiple files or sockets, iterate through them or process them concurrently. Each resource might be associated with some working state and metadata. This will be difficult to express if we're directly binding resources to registers, i.e. we'd also need all the associated data to be presented as registers. It would be more convenient if we have linear reference types for the open file or socket.



 without adequate support from the environment. 

However, expressing it will hinder static computation. 



Dynamic resources and eval also need some attention. It is feasible to model 'scoped' dynamic references, i.e. where register names are runtime expressions but subject to static translations. It is feasible to statically analyze for sharing and parallelism at the level of scopes instead of individual names. However, this makes the 'pointers' very dependent on context of translation, and some users would inevitably attempt to translate pointers between scopes. This complicates the system.

A viable alternative is instead to model a static set of dynamic collections in the environment. Perhaps we could model 'channels' of data and linear objects. And some databases. However, if we start trying to support logical 'slices' and editable projections or views, I think we won't have much benefit compared to scoped access to a dynamic namespace of registers.

Anyhow, I feel that the solution to dynamic collections should not be arbitrary. It should be justified as either an optimization of what we could (in theory) do with static collections - e.g. avoiding big dispatch tables, awkward views and slicing - or we should go the other way and take static environments as an optimizable subset of dynamic. 

...

In any case, we can then arrange for different parts of a large procedure to operate in parallel on different registers with minimal synchronization, providing a basis for parallelism that is much simpler compared to extracting dataflow just one stack. It might also be feasible to also model deterministic concurrent interaction directly in the semantics, e.g. based on Kahn process networks instead of parallel evaluation of loops.

*Note:* It isn't necessary to discriminate stacks and channels and bags. They could all be the same thing with different access flags or methods. But it might be useful to ensure that loops are either invariant on a channel or stack, or explicitly test for empty.

## Pseudo Concurrency

I don't intend to support concurrency directly within a procedure. But, it should be feasible to model concurrency in terms of running multiple iterations of a top-level loop in parallel. Basically, the top-level loop becomes a simulated 'time-step', and steps in each iteration should receive input both from earlier steps in the same loop and later steps in the prior iteration, threaded through these named registers and linear objects. The difficulty is identifying how many loops can usefully run in parallel, and evaluating the conditions to start the next loop before the prior loop completes.

# Older Stuff

## Parallelism Within Programs

Although the program model is procedural, there is an opportunity for parallelism between mostly independent subprograms. This can be augmented by careful attention to the nature of effects, e.g. multiple reads or multiple writes can occur in parallel, but stateful read-write cannot. Ideally, we can analyze our programs statically to extract a lot of useful parallelism at compile time, or with minimal overhead at runtime.

Even better if we can model concurrent interaction between subprograms with some parallel evaluation. This might be feasible with some attention to channels and parallel loop unrolling, i.e. such that we can determine conditions and start the next few loops before the prior loop completes. Interactions between subprograms can be modeled across loops.

However, use of dynamic references for the filesystem or network APIs will likely hinder implicit parallelism. Is there a good alternative or solution for this? Perhaps we could restrict such resource references to be linear, such that dataflow and parallelism can be aligned.

## Semantic Foundation

I've decided on a simple procedural foundation, albeit with scoped hierarchical transactions. 

At the moment, I'm still deciding the semantic foundation for this program model. A few approaches that seem good to me include [static process networks](GlasKPN.md) and [grammar logic](GrammarLogicProg.md). The simple procedural foundation is also a good choice, but might be a little too simple without mitigation strategies.

### Static Process Networks

A program can be modeled as a sequence of operations that read and write a finite set of named channels, including some arithmetic operations. However, operations are only ordered if they both read (or unread) the same channel, or both write the same channel. Sequential writes can generally be buffered to immediately support further progress. Reads can potentially proceed immediately after setting a dynamic forwarding rule.

When composing programs, we'll apply a static *translation* to channel names. To support intervention, we could translate read channels and write channels separately. To express usage assumptions and access control, we could use 'failed' translations to raise an error if a name is unexpectedly used in a subprogram.

It is possible to explicitly model a bounded data stack as a finite set of named channels, e.g. `s.h, s.th, s.tth, s.ttth, ...`, that we read, write, and translate as needed, each channel holding exactly one value. However, it is more convenient and efficient to let the compiler allocate specialized channels. The data stack can serves as the implicit destination for reads, source for writes, and working space for arithmetic. Concurrent subprograms must operate on different stack elements. A loop must have static stack arity. 

Compared to plain stack-based programming, channels add a lot of flexibility and extensibility, e.g. for optional arguments, higher order programming (via wiring a loop to an input and output channel), bi-directional computing, and concurrency.

An effects API could be modeled in terms of interaction with special channels bound to the runtime. However, it may prove awkward to multiplex a 'filesystem' channel between concurrent processes within a larger computation. This could be mitigated with temporal semantics for logical latency and time-share of channels. Temporal semantics would essentially treat every program as a loop over time. Or perhaps we could let users manually loop over time.

It is feasible to encode static metadata into our channel names. This would support the equivalent of 'templated' channels, or encoding operations that might interact with multiple base channels after translation. For example, the equivalent to `sys.ffi.call(StaticRef, DynamicArgs)` could be encoded as sending a message to a channel named `sys.ffi.call:StaticRef`, subject to flexible translation. And translations could generalize to producing a subprogram that involve multiple channels. This would greatly improve flexibility of the process network and simplify the effects APIs.

It is feasible to tweak channels to always perform an *exchange* of information, i.e. write plus read, with basic writes and reads exchanging unit values. This should make it more convenient to use channels as procedures for effects.

It might be best to avoid backtracking with process networks because we would need to propagate the behavior dynamically across the process network. This is feasible in theory, but difficult to implement and reason about.

### Procedural

The procedural foundation is simpler than a process network: a simple sequence of operations on an abstract environment, generally including arguments and a working space and a limited procedural interface (methods or algebraic effects) to the rest. We could support an unbounded data stack and ad hoc recursive computations, or restrict procedural programs to a bounded data stack, limited to non-recursive computations or tail recursion.

Compared to process networks, procedural programs do not have any built-in notion of interaction. However, it is feasible to model concurrent processes using an event loop. With suitable syntax, we can compile procedures into state machines that proceed and yield over multiple steps. Algebraic effects can abstract some interactions with the environment. Ideally, all effects are modeled as algebraic effects, i.e. the `sys.*` runtime API might be modeled as algebraic effects in the runtime environment instead of namespace extensions to the program definition. This provides opportunity for intervention and reflection.

In context of transactions, procedural code avoids one of its greater weaknesses: no time between observation and action, no risk of concurrent interference. However, some procedural patterns must be divided across multiple transactions. We cannot have synchronous request-response with non-transactional systems (such as filesystem or network) because the request must be committed before the response becomes available.

The biggest loss for procedural code is bi-directional computing. This could be mitigated with some explicit staging and support for 'static' context, such that a later call in the call graph can influence prior behavior. 

Procedural code could support backtracking conditionals, but I'm not certain it's worthwhile for all conditionals. Perhaps instead explicitly support transactions and conditions separately, such that they can be paired if desired.

### Grammar Logic

In grammar logic programming, a program expresses a set of structured values (sentences), and composition can partially overlap sentences to both filter and relate them. Usefully, the program is both generative (producing values) and analytical (recognizing values). But this requires a lot of complicated optimizations upon composition to make efficient.

Support for grammar logic as the underlying program model could enhance flexibility of what we can do with a glas program. However, we must awkwardly represent interactions as part of the generated sentence, and we probably want determinism up to input by default. For this, we could restrict the grammar to generate interactive sentences of a specific form, perhaps aligned with the procedural semantics or process networks by default. This interaction may result in very 'large' sentences that must be garbage collected as they are constructed in general. 

In theory, refinement on the set of sentences can model bidirectional computation. We could also add heuristics for search via annotations. But this doesn't apply to a deterministic computation, where search is finding only one or zero sentences. Those refinements and search heuristics would apply only when running programs backwards. Unless we can squeeze out some optimizations, the extra potential has costs but no immediate benefits.

Although there are many theoretical benefits to grammar-logic as a foundation, I think it might be too difficult to implement efficiently in practice. At least as a starting point. 

## Thoughts

### Fractal Namespaces

Algebraic effects can be modeled as a dictionary of opaque definitions. However, an intriguing alternative is to model algebraic effects as a namespace that we 'patch' in scope, i.e. translate (move, link) then add some definitions for effects visible to a subprogram. This would allow 'override' of some algebraic effects names.

I'm not convinced this is a useful idea. Insofar as I want higher-order effects, it might be better modeled as providing some AST arguments to fexpr-like algebraic effects. Those AST arguments could include *localizations* and access to other effects as needed. We could exclude 'move' from this translation. 

But the idea of prefix-oriented translations of the 'effects' namespace at least for linking purposes.



 allows for the effects namespace to designate some names for overridde by the client, providing a medium for interactive definitions of effects. We can also restrict which effects are visible to a subprogram, and optionally model *localizations* at the effects layer, not just the namespace layer.



This also simplifies restriction of effects to a subprogram based on translation of prefixes. 





With the opaque definitions, we can still support interactive definitions

 pass higher-order functions to an effect, e.g. to filter a result. The algebraic effect would need access to both the scope of effects within which it was defined and 

 its own scope of effects, but also some methods defined by the caller.

We would need to pass in some implicit arguments from the caller of the algebraic effect, and the

We could resolve this by awkwardly introducing some mechanism to reference methods provided by the caller, 

, e.g. if we want to get some extra feedback about what an effect is doing under-the-hood we would need to somehow pass in a function from the caller. 

If we assume hierarchical algebraic effects are patching a runtime-provided namespace, it is be feasible for a subprogram to tweak or override

 fine-grained behaviors within provided effects, essentially support higher-order programming of the effects API. In general, programs could also introduce intermediate namespaces for 


We might view algebraic effects as a dictionary of methods being passed from a program to a subprogram. However, this view is somewhat inflexible. An intriguing alternative is to view algebraic effects as a namespace constructed within a call graph, with coordination and contributions from multiple sources.





## Thoughts



A program operates on an abstract environment. In part, this environment is abstracted to support effects (limited knowledge about the world), and in part to simplify modularity (control dependencies between components).

I want an opportunity for bi-directional computation, such that 'later' code or context can influence 'earlier' decisions. Most functional and procedural languages don't offer this feature. Constraint models or grammar-logic would allow limited influence through further refinement of constraints. Channels and concurrency could more explicitly move data in multiple directions. Ideally, bi-directional computing can contribute to partial evaluation or staging at compile-time computations, e.g. so we can compute units on numbers before the number is available.

I want the ability for a program to non-invasively observe and influence a subprogram's computations. This is useful for rendering, explaining, and debugging computations in context. This feature requires careful attention to the effects APIs, private state, and concurrency models. For example, binding abstracting algebraic effects to names from the program namespace will hinder intervention, but it may be feasible to bind to a separate runtime environment namespace.

It is feasible to model concurrent subprocesses within a transaction, similar to Kahn process networks. Perhaps algebraic effects can be modeled in terms of operations on channels, assuming we can statically optimize routing and multiplexing. This would improve flexibility of the system.

I would like support for staged higher-order programming without first-class functions or dynamic 'eval'. Algebraic effects or channels can effectively pass functions to a subprogram, but the reverse - 'returning' a function - may require semantics closer to fexprs, macros, or explicit staging so we instead abstract construction of the effects handlers binding to a client's environment.

I don't want partial data in the formal semantics, not even for this intermediate AST. This limits use of explicit futures and promises or unification. 

I like the idea of program search and the opportunity to explore multiple possible outcomes. However, I'm uncertain how much this will conflict with performance goals. This might benefit from semantics around constraint systems or grammar logic programming, with non-deterministic solutions to some programs. In addition to refining or intervening on those solutions, developers could potentially express heuristic fitness or cost as annotations to guide search.

The need for precise garbage collection is mitigated in context of the transaction loop and incremental computing. We could potentially avoid GC within transactions, or focus on static GC opportunities within the transaction. 

Non-determinism might be better modeled as a built-in than an effect, but it should still be feasible to control access, e.g. by restricting access to unordered choice AST constructors for deterministic programs, or by restricting unordered choice to something that only happens when we run programs backwards.

## Miscellaneous Design Thoughts

* factored conditionals - avoid a repeating 'Cond AND' prefix across multiple sequential, ordered choice conditions. 

* I want support for units or shadow types, some extra context and metadata that can be threaded and calculated statically through a call graph. This may require special support from the environment model.

* Support for annotations in general, but also specifically for type abstraction, logging, profiling, and type checks. 

* A flexible system for expressing proof tactics and assumptions about code that are tied to intermediate code and can be roughly checked by a late stage compiler after all overrides are applied.

* I want to unify the configuration and application layers, such that users live within one big namespace and can use the namespace and call contexts to support sandboxing and portability of applications to different user environments. Modularity is supported at the namespace layer via 'ld' in addition to staging. In addition to Localizations, I must handle Locations carefully (and abstractly).

* I want termination by default, if feasible. In context of transactional steps, there is no need for non-terminating loops within the program itself. I have an idea to pursue this with a default proof of termination based on mutually recursive grammar rules without left recursions, while users could replace this with another proof in certain contexts via annotations. We could simply assume termination for remote procedure calls. Not sure of feasibility in practice.

* I like the idea of functions based on grammars, similar to OMeta. This is a good fit for glas systems because we need a lot of support for metaprogramming. Also, grammars can be run backwards to generate sentences. This is both convenient for debugging and understanding code, and for deterministic concurrency based on recognizing and generating the same sentence in different parts of code. 

* I'm interested in code that adapts to context, not just to the obvious parameters but also intentions, assumptions, expected types or outcomes, etc.. The main requirement here is a more flexible, bi-directional dataflow between upstream and downstream calls. This dataflow should be staged, evaluating prior to function arguments. We might try grammar-logic unification or constraint variables in this role. I'm uncertain what is needed, so will keep an open mind for ideas and opportunities here.


# Originally From Namespaces: Algebraic Effects and Abstraction of Call Graphs

For first-order procedural programming, we hard-code names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. Despite being 'hard coded', the namespace translations make this elastic, rewriting names within the assembly. We can support hierarchical components, overrides, and shadowing, and even limited abstraction by intentionally leaving names for override.

However, encoding many small variations on a large call graph at the namespace level is very expensive in terms of memory overhead and rework by an optimizer. To solve this, we should support abstraction of calls at the program layer. For example, we could support algebraic effects where a program introduces an effects handler that may be invoked from a subprogram. With careful design, this effects handler may still be 'static' for inline optimizations and partial evaluation, similar to namespace overrides.

By leveraging *Localizations*, we can also interpret data to names in the host namespace without loss of namespace layer locality or security properties. It is feasible to leverage layers of localizations to model overlays on the call graph, where most names can be overridden in a given call context.
