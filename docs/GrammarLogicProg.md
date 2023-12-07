# Programming with Grammars and Logic

## Overview

Grammar and logic programs have similar semantics. Dataflow is based on [unification](https://en.wikipedia.org/wiki/Unification_(computer_science)#Application:_unification_in_logic_programming) and backtracking searches. Grammars generate and accept values. Logic programs generate or accept propositions. The two are easily unified: a guarded pattern (`Pattern when Guard`) in a grammar can essentially model logic `Proposition :- Derivation`. (Similarly, logical negation via `Pattern unless Guard`.)

A pure, deterministic function can be modeled as a grammar or logic that accepts a set of `(args, result)` pairs, where no two pairs have the same first element. An effectful function can be modeled as a grammar or logic that accepts a set of `(args, result, env)` triples, where the third element represents effectful interaction with the environment. A simplistic interaction model is a request-response list, `[(Request1, Response1), (Request2, Response2), ...]` where the environment accepts requests and generates responses. 

We can build a procedural language upon grammars and logic, where the environment parameter is implicit. Unification is expressive, not limited to simple request-response interactions. I propose to instead model concurrent channels and temporal semantics. Of course, this does limit us to deferred, safely ignorable, or undoable effects (see [transaction loop applications](GlasApps.md)) unless we guarantee commit. 

Intriguingly, non-determinism can be controlled effectfully by the environment. The language doesn't need to provide direct access to unification variables. Assuming such a language, we might model logic propositions as deterministic functions that either return unit or fail, i.e. `Proposition -> ()`. We could search for passing propositions.

As an unrelated point, grammars and logic both benefit from OOP-like inheritance. For example, we might inherit from a grammar that represents a programming language but extend the rules for parsing numbers. Or we might inherit a system of logic propositions, then override the propositions that represent initial data.

## Why Another Language?

A grammar-logic language should be convenient for developing user-defined languages. Logic unification variables can model concurrent or distributed effects, enhancing scalability. The ability to compute functions backwards is very nice in many contexts - constraint systems, property testing, explaining computations.

## Brainstorming

### Ordered Choice 

We can easily model ordered choice in terms of unordered choice and pattern guards.

        P or-else Q             =>      P or (Q unless P)
        if C then P else Q      =>      (P when C) or (Q unless C)

I propose to build the grammar-logic in terms of ordered choice, to support deterministic functions by default. No direct access to unordered choice. Indirectly, non-deterministic choice can be supported via effects and even prioritized via heuristic annotations.

### Deterministic Functions

Methods in my grammar-logic language should represent deterministic functions, procedures, or processes by default. Non-deterministic computations will still be supported via effects or in contexts where we evaluate backwards. Mostly, this means programs cannot directly introduce unification variables or express unification. 

Unification variables can be introduced indirectly via effects or used in abstract by channels (and other primitives). Abstraction does impose constraints on the language: if we do not have static type checking to prove at compile time that channels are correctly used as channels, the runtime must instead track dynamic types (to at least distinguish channels from data).

### Non-Deterministic Choice

Non-deterministic choice can be expressed and controlled as an effect. Fair non-deterministic choice is useful for modeling concurrency in transaction loops. Biased choice is useful for modeling soft constraints and meta-stable reactive systems. To support biased choice, we might introduce scoring heuristics based on quality and stability. 

An intriguing possibility in context of grammar-logic programming is to model non-deterministic choice by returning a logic unification variable. This would make the choice implicitly adapt to the program. However, it would be difficult to implement fair choice from an infinite set.

### Extensible Namespaces

Instead of a monolithic grammar (or logic), I propose to model grammars as a collection of named rules. This allows us to express OOP-like inheritance and override. For example, we might want a new language the same as the old one, except with a new option to parse an integer.

        grammar foo extends bar . baz with
            integer = ... | prior.integer 

We can generally support mixins and multiple inheritance. I propose to model all grammars as mixins, but use of the grammar keyword above would introduce an implicit constraint that `integer` must not be defined prior to `bar . baz`. Multiple inheritance is possible if two grammars don't overlap, or via the mixin keyword that does not have this constraint.

I don't expect to be needing first-class namespaces. That is, we don't instantiate grammars the way we instantiate objects. Grammars are stateless. There is also no performance need for final definitions.

Related ideas:

* *Hierarchical namespaces.* We might take `foo in f` to translate all names defined in foo to the `f/` namespace, such as `f/integer`. Hierarchical structure would help control name collisions while still allowing flexible extension to methods within that namespace.

* *Anonymous namespaces.* Our language might logically rename methods and hierarchical namespaces that start with '~' to a fresh anonymous namespace, providing a space for private definitions.

* *Explicit translations.* We could allow more precise renames, such as renaming 'integer' to 'int' in foo. This would be useful for adapting mixins to another target, for conflict avoidance, and for community localization. Ideally, renames can be abstracted for reuse.

* *Interfaces as incomplete namespaces.* We can define incomplete grammars where overrides are expected or required. These incomplete grammars may contain annotations describing expected properties or types. Those annotations could be checked when compiling the fully extended grammar, unless they too are explicitly overridden.

* *Annotations as conventional namespaces.* We can model most annotations as a lightweight convention on namespaces, e.g. annotate `integer` by defining `anno/integer/type`, `anno/integer/docs`, and so on. The compiler could know to rename `anno/integer/` when `integer` is renamed. Assertions might similarly be named and modeled via a conventional `assert/property` namespace. This design allows annotations to be extended or refactored like any other definition.

* *Nominative types.* Programs can potentially use arbitrary names from the namespace to wrap or unwrap data, or match wrapped data without unwrapping it. This would enable renames to also prevent some type conflicts. In context of anonymous namespaces, this would support user-defined abstract data types. Further, we could easily associate operations such as debug views through the namespace.




### Channel Based Interactions

My initial thought is to model interactions around channels. This gives me many features I want - compositionality, simplicity, and scalability. Procedural interaction can be modeled via request-response channel. Although extensibility is limited due to linear ownership of channels, this can be mitigated by modeling a databus or other extensible architecture.

Channels can be modeled as partial lists, with the writer holding a cursor at the 'tail' of the list, and the reader holding the 'head'. To write a channel, we unify the tail variable with a pair, where the second element is the new tail. Then we update the local cursor to the new tail, ensuring we only write each location once. A written channel may be 'closed' by unifying with the empty list. (Closing a channel can be implicit based on scope.)

Channels aren't limited to moving data, they can also transfer channels. We can build useful patterns around this, modeling objects or remote functions or a TCP listener as a channel of channels. 

However, in context of deterministic computation, channels are essentially linear types. If there are two writers, writes will conflict. And even reading a channel must be linear if the reader might receive a writable channel. This can be mitigated - we can use *temporal semantics* to deterministically merge asynchronous writes. Or we could support non-deterministic merge based on arrival-order with runtime support (see *reflective race conditions*). With the ability to merge asynchronous events, channels can model openly extensible systems, such as a databus or router.

An inherent risk with channels is potential deadlock with multiple channels are waiting on each other in a cycle. This is mitigated by temporal semantics: if at least one channel in the cycle has latency, deadlock is broken. It can also be avoided via static analysis (perhaps [session types](https://en.wikipedia.org/wiki/Session_type)).

#### Temporal Semantics? Tentative.

Temporal semantics support deterministic merge of asynchronous events. This significantly enhances compositionality and extensibility, e.g. we can model processes interacting through a databus or publish-subscribe system.

Temporal semantics can be implemented for channels by introducing time step messages. A reader blocks on time step messages. A process may wait, implicitly removing time steps from held input channels then writing time steps to held output channels. We might represent the balance of delay time steps and pending removals on input channels via an associated variable. 

In context of temporal semantics, a process can deterministically return that a read fails because waiting is required. A process can wait and poll a list of channels for the first channel with a data message. Logical timeouts are possible, where a process limits the number of time steps it waits for a message. Channel latency can be expressed by inserting a few time steps into a read channel when it is initially allocated.

Time steps can be mapped to real-world effects, e.g. one microsecond per time step. This would be useful for scheduling effects in time-sensitive contexts such as music or robotics. Intriguingly, transactional interactions might commit to a schedule of write-only effects relative to an idealized time of commit.

The cost of temporal semantics is complexity. The language runtime must clearly track 'held' channels. And it is relatively inefficient to push time steps individually, so a runtime might compress sequential time steps, e.g. `(time-step * Count)` and accelerate operations that interact with time steps (e.g. polling). 

I feel this idea is worth exploring, but it might not be available immediately. Ideally, I should develop the language such that temporal semantics can be introduced later with full backwards compatibility.

#### Reflective Race Conditions? Tentative. As Effect. Disfavored.

A runtime can potentially merge events from multiple channels non-deterministically based on arrival order. Arrival order will vary based on under-the-hood features such as multi-processing, cache sizes, and processor preemption by the operating system. This is a potential alternative to temporal semantics, and is much easier to implement efficiently. 

However, this allows for race conditions to influence observable program behavior. Race conditions due to arrival-order non-determinism are worse for reasoning and testing than most other forms of non-determinism because it's highly machine dependent. This easily results in difficult to reproduce "works on my machine" bugs.

Predictability can be mitigated insofar as we control where race conditions are introduced. But I think I would favor temporal semantics for most potential use cases. (AFAIK, there isn't a strong use case for combining the two.)

#### Bundling and Abstracting Channels

Duplex channels can be modeled as a read-write pair of partial lists. We could model a bundle as a dictionary of channels. These bundles could be mixed with data and pass-by-ref vars to serve as the primary type for arguments and environments. 

A runtime must precisely track which elements represent channels in order to protect certain abstractions - linear reads and writes, temporal semantics when copying, implicit closing of channels when they leaves scope, etc.. Exactly how a runtime tracks this is an implementation detail, but options include associated type information at runtime or static type analysis at compile time.

Assuming the runtime does an adequate job of tracking these things, then bundling isn't so different from normal data manipulation. Channels, including dictionaries or lists thereof, are effectively first-class. 

#### Broadcast Channels? As Optimization.

A broadcast channel is a read channel that receives only perfectly copyable content, such as data or channel-based functions. Consequently, a broadcast channel is also perfectly copyable. 

A compiler or runtime could recognize broadcast channels and use a more efficient copy mechanism. This recognition can be supported via types (static or dynamic) and explicit declaration of broadcast read-write pairs (where we'll copy the reader). Attempting to write a non-copyable value through a broadcast channel would be a type error.

The writer can still be copied imperfectly via temporal semantics or reflective race conditions. Copying the full reader-writer pair could essentially result in the databus pattern, which is also very useful.

#### Pushback Operations

A reader process can push data or messages backwards into an input channel, to be read locally. This is a very convenient operation in some use cases, reducing the complexity for conditioning inputs.

#### Channel-Based Objects and Functions

An object can be modeled as a process that accepts a subchannel for each 'method call'. The object reference would be the channel that delivers the method calls. The separate subchannel per call ensures responses are properly routed back to the specific caller, in case the reference is shared. (An object reference might be shared via *Temporal Semantics* or *Reflective Race Conditions*.)

Functions can be modeled similarly as a *stateless* object. If the runtime knows the object is stateless, it can optimize method calls to evaluate in parallel, interactions may be forgotten because they don't affect any external state, and the channel may be freely copied because there is no need to track order of writes. Effectively, the channel serves as a first-class function (albeit with restrictions on how it is passed around or observed).

Ideally, grammar methods and object methods via channels would have a consistent syntax and underlying semantics.

### Pubsub Set Based Interactions? Defer.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. 

Writing to a partial set is commutative and idempotent. These are very convenient properties. However, reading a partial set is relatively complicated because, at least in context of deterministic computation, we must not accidentally *observe* write-order. 

Consequently, read operations are continuous: there is no order, no first, thus cannot read first three items then stop. Instead, read would fork the computation, with each fork operating concurrently on one element of the set. Sequential reads would effectively model relational joins. And the reader is also limited to commutative, idempotent effects, such as writing to a partial set. These constraints on readers result in a very declarative, reactive programming style. Indeed, it's similar to publish-subscribe systems, so I've decided to call this 'pubsub sets'.

Just as channels may transfer channels, partial sets may carry partial sets. This supports a useful request-response pattern: one program writes a request and includes a 'new' partial set just to receive the response. This response may include data and additional partial sets for further interaction. Also similar to channels, to structurally protect abstraction and dataflow, the grammar-logic language could use hierarchical 'bundles' of partial sets and data as the primary message type.

This request-response pattern can serve as a basis for function passing and integration of real-world effects. Many effects can be adapted. For example, although we cannot directly model streaming files, we could model requests to read file regions or to apply patches later. And we could potentially even abstract this to look like a stream.

Pubsub can potentially be adapted to *temporal semantics*. If successfully adapted, this should simplify integration with channel-based processes, which could maintain 'publish set' variables over time and read historical values of sets. However, there are still some theoretical and logistical challenges I'm working out, such as how to recognize when a process is done writing to a partial set. 

The idea needs work. However, it's a very promising direction to pursue, and a good fit for my vision of glas systems. Meanwhile, we can still model a broadcast or databus via channels.

*Note:* Modeling partial sets in glas is non-trivial. But if we don't care about efficiency, we can encode values into bitstrings then model the set of bitstrings as a partial radix tree. To support pubsub, I assume partial sets are abstracted by the compiler and runtime, observed and manipulated only through keywords or built-in functions.


### No-Fail Contexts? Defer.

We can develop an effects API that may only be called from a no-fail (or no-backsies) context, i.e. where we can guarantee there is no backtracking after the effect. This would support a more direct API, with direct OS calls or FFI. This isn't even very difficult - we could introduce a few simple types to track no-fail method calls and no-fail API channels, then restrict (via static analysis) where the no-fail API channel is written.

This seems like a useful feature to develop later, but I also feel it's relatively low priority.

### Type Safety

We can use type annotations to describe expected types of methods, including arguments, the environment, and results. Data structure and channel protocols. Timing or latency assumptions in context of temporal semantics. Static parameter and result elements. Basically, anything we might want to check.

Ideally, users may be imprecise and we'll automatically infer missing properties based on context of use, specialized based on the current extensions. 

### Nominative Types

It is possible to model 

### Default Definitions? Tentative.

Support for 'default' definitions is convenient in some cases. A default definition would override or extend another default definition, but it won't override normal definitions. This would be understood as a special feature of the definition type.

### Weighted Grammars and Search

Methods can be instrumented with annotations to generate numbers estimating cost and fitness and other ad-hoc attributes. A runtime can implicitly compute and total these values, and use them heuristically to guide search in context of non-deterministic computations. This isn't a perfect solution, but I think it might be adequate for many use cases.

Because this can build on annotations, no special semantics are needed. But dedicated syntax can make this feature more accessible.

### Lazy Evaluation? Flexible.

As an optimization tactic, lazy evaluation is a good fit for grammar-logic and functional programming. We could support annotations within the language to guide its use. But I'd prefer to avoid lazy evaluation semantics. No lazy fixpoints ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot), for example. 

### Module Structure

Toplevel module structure can be similar to g0 - one open module declaration (single inheritance), several imports and definitions, finally an optional export list. Imports could be qualified or provided as a list. 

Definitions include grammars, mixins, perhaps translations. At least initially, I intend to avoid anything that requires compile-time eval: data definitions, macros, top-level assertions, export expressions, etc.. Those might be introduced later, after bootstrap and accelerated eval.

A grammar represents an entire namespace, but we can follow the convention of defining a 'main' method for applications. We could use annotations (perhaps via `anno/main/run-mode`) to guide integration of main, e.g. selecting a staged programming mode.

### Applications as Servers

Applications might operate on a tuple space or mailbox of messages to avoid the awkward composition challenges of establishing HTTP listeners and so on. This would also support OS signals and other events. I think this would be a much better default.

A compiler or runtime might also generate an administrative and debug interface for applications, providing access to state and configurations and so on. This might also be represented as an HTTP service.

Ideally, we provide data storage and configurations to applications at a higher level than 'files'. Let the runtime manage this difference in levels. 

### Extensible Syntax? Defer.

I like the idea of supporting DSLs or dialects within programs. But this should be handled by compile-time eval, e.g. so we can load local modules as part of our extensible syntax. So, it will be deferred until at least after bootstrap and accelerated eval.

### Fine-Grained Staged Programming

It might be useful to support 'static' annotations to indicate which expressions should be statically computable within the fully extended grammar. This may propagate to static parameters in some use cases. Ideally, we could also support types that describe static arguments and results.

### Method Call Syntax

The toplevel argument to a method shall always be a dictionary. This ensures space for an 'env' parameter for the implicit environment. The primary argument might be named 'args'. We might represent return values as 'result'. I favor labels in case we later extend the interaction model.

The 'args' parameter will typically be a list (positional parameters) or another dictionary (keyword parameters). Keyword parameters are advantageous for further extensibility, but sometimes a list is more concise and convenient. Grammars can potentially match on lists the same way they match on texts or source code, recognizing fragments of a list.

Channels, pass-by-ref vars, etc. would be modeled as first-class elements of these lists or dictionaries. However, the language abstractions and runtime may protect linearity assumptions. Attempting to recognize a channel as a regular list value may either fail or be treated as a type error depending on context.

It should be feasible to *fully abstract* over method arguments, e.g. such that even pass-by-ref vars might be included in the result of method calls, forwarding or providing views on inputs.

The 'env' parameter is what the language passes or threads tacitly through every computation. This might be wrapped and abstracted via dedicated syntax, such as `with (env) { ... }`. The language must also provide dedicated syntax to access the environment. Typically, the environment is also a dictionary, providing access to the runtime (perhaps as a channel-based object) and potentially to some read-only environment vars or threaded state vars. We might support 'pure' computations that are essentially `with () { ... }`.

### Pass By Reference Parameters

We can roughly model pass-by-reference as a pair `(Curr, Dest)` where programs update `Curr` in place (via purely functional state patterns) then write (unify) the final value of `Curr` with `Dest` when the reference is about to exit scope. As with channels, this pair should be abstracted by the language, such that `Dest` is never observed by a program.

There are a couple related challenges.

First, we cannot copy a pass-by-ref parameter for concurrent operation. The `Dest` cannot be copied because we may only write it once and there is no sensible solution to merge writes. Consequently, pass-by-ref must be considered a linear type. What we can do is continue to pass-by-ref the current value, or copy the contained value (unless that value also restricts copy).

Second, integration with temporal semantics needs careful attention. One idea is to model `Dest` as a write-once channel, in which case the channel might also include a number of implicit time step messages, and a process could query whether the referenced object has been returned yet. Conversely, `Curr` would need to include the read channel where the time steps are observed, and might be updated via pushback. The process could then potentially 'wait' on a pass-by-ref result. 

I suspect this idea needs careful formalization, especially in context of composition: What happens, exactly, when a pass-by-ref var is passed-by-ref? How should pass-by-ref interact with mutable local vars?

### Incremental Compilation

We can design a grammar compiler such that it memoizes and composes partial results, aligned with expression of grammar inheritance. This could help avoid some rework. 


## Namespace Builder

I want an AST for namespaces that efficiently compiles to a 'flat' dictionary with (rn:renames, def:definition) pairs. This could be followed by a function to apply renames to definitions. To keep it simple, the compiler may reserve a prefix for internal use; can easily raise an error if users attempt to define a reserved symbol.

* *rename:(in:NS, move:\[(Prefix1, Prefix2), ...\])* - rename multiple names within NS. This also renames them within definitions in NS. Initial Prefixes must not overlap. Final prefixes may overlap, but it's an error if two names are combined. 
* *scope:(in:NS, hide:\[Prefix1, Prefix2, ...\])* - move all names that start with the given prefixes into a fresh, anonymous namespace. The compiler may reserve a name prefix for all the anonymous namespaces. In practice, we'll mostly `hide:"~"`.

TODO:

* *define* new words or overrides, should specify an extra prefix to locally refer to 'prior' definitions. Prior definitions are moved into a new anonymous namespace, then the given prefix is redirected to that namespace.

* *initial* definitions - assert certain words are undefined (or only have default or abstract definitions). This cannot be a normal priority because it needs to wrap the grammar body *and* its extensions.
* *final* and *default* and *abstract* definitions - could model as priority values on definitions. Abstract definitions could omit the definition body, mostly serve as stand-ins to ensure renames don't merge names by accident, and to help discover spelling errors.
* *compose* namespace builders - apply namespace operations sequentially.

*Aside:* I could add explicit operations to move or undefine words, but I probably don't need them. No need to maximally generalize namespace operators. Provide what is needed, no more.

## Grammar Methods


## Misc

### Channels

### Method Interactions

### Method Protocols?

### Pass by Reference Parameters


