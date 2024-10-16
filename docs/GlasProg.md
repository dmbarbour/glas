# Program Model for Glas

The initial program model is `g:(ns:Namespace, ...)`. The 'g' variant header and 'ns' dict layer exist for extensibility and to support concise integration based on metadata. The main body of the program is a [namespace](GlasNamespaces.md) containing [abstract assembly](AbstractAssembly.md) definitions. Abstract assembly uses *named* AST constructors, generally `%*`, within definitions. Thus, it is feasible to restrict or extend the intermediate language through the namespace.

This document focuses on a specific choice of `%*` primitives, something relatively easy to interpret or further compile yet suitable for my vision glas systems - live coding, orthogonal persistence, incremental computing, transactions. 

The initial semantics are procedural in nature, albeit with algebraic effects, hierarchical transactions, and careful attention to non-determinism. However, unlike conventional procedural languages with a 'main' procedure, a [glas application](GlasApps.md) namespace instead defines a transactional 'step' method for background processing, an 'http' method to handle ad-hoc HTTP requests, among others. Also, the effects API is modeled using algebraic effects instead of special extensions to the application namespace.

staging and partial evaluation, parsing and backtracking, concurrency and parallelism, effective control over non-determinism, and stable rendering of computations for live coding and [gui](GlasGUI.md).

The initial program model is procedural in nature, i.e. programs will generally describe a sequence of operations on the abstract environment. There is support 

 This is limiting in some ways, but it's also relatively simple to reason about

## Semantic Foundation

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

The procedural foundation is simpler than a process network: a simple sequence of operations on an abstract environment, generally including arguments and a working space and a limited procedural interface (methods or algebraic effects) to the rest. We could support an unbounded data stack and ad-hoc recursive computations, or restrict procedural programs to a bounded data stack, limited to non-recursive computations or tail recursion.

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

