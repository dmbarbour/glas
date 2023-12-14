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
            integer = ... | proto.integer 

I propose to model all grammars as functions on a namespace. This allows for multiple inheritance. There are some issues that must be mitigated (see *Namespace Genealogy*).

Related ideas:

* *Hierarchical namespaces.* We might take `foo in f` to translate all names defined in foo to the `f/` namespace, such as `f/integer`. Hierarchical structure would help control name collisions while still allowing flexible extension to methods within that namespace.

* *Anonymous namespaces.* Our language compiler can logically rename methods that start with a specific prefix, perhaps '.', to a fresh anonymous namespace. This provides a space for private definitions and local refactoring. 

* *Explicit translations.* We could allow more precise renames, such as renaming 'integer' to 'int' in foo. This would be useful for adapting mixins to another target, for conflict avoidance, and for community localization. Ideally, renames can be abstracted for reuse.

* *Annotations by naming convention.* For example, annotate `integer` by defining `anno/integer/type` and `anno/integer/doc`. This makes annotations accessible and extensible, subject to the same refactoring and abstraction as all other data, and mitigates specialized syntax for annotations to a syntactic sugar. 

* *Assertions by naming convention.* Similar to annotations, we might model assertions by defining `assert/property-name` to a method that is treated as a proposition. These assertions would be evaluated in context of extensions to the namespace.

* *Interfaces as partial namespaces.* An interface is essentially a grammar where the methods are declared but undefined, where we instead focus on annotations - documentation, types, assertions about how methods interact, etc.. Useful for both mixins and grammars. Testi

* *Nominative types.* It is feasible to allow programs to use names to organize data, e.g. to wrap and unwrap data, or to index data. This can model open variant types and open records that are guaranteed to not conflict, or abstract data types via anonymous namespaces. However, nominative types are relatively awkward in context of open distributed systems.

#### Namespace Genealogy? Tentative.

Multiple inheritance has known issues that must be mitigated, such as [the diamond inheritance problem](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem). Consider a diamond inheritance example:

            A
           / \
          B   C
           \ /
            D

This diamond might be constructed from four grammars.

        grammar A with ...
        grammar B extends A with ...
        grammar C extends A with ...
        grammar D extends B . C with ...

One reasonable interpretation is that B and C represent extensions to meanings from A, and there is no conflict unless they independently attempt to extend the same symbol with two different meanings. To implement this solution in context of building the namespace, we might track provenance for every symbol.

        to apply G to NS:
            local NS' = apply G's prototype to NS
            for each symbol defined in G:
                if symbol is defined in NS and is not derived from G:
                    raise an error
                else:
                    update symbol and its genealogy in NS'
            return NS'

The grammar `(B . C)` can be interpreted as functional composition, compiling to `λ NS . apply B to (apply C to NS)`. In context, applying A twice would have no effect the second time, but `apply B` would raise an error if C and B each explicitly override the same symbol.

In context of glas systems, grammars are represented as plain old data at the glas module layer. They don't have an extrinsic identity, but we can reasonably assume grammars with the same value have the same meaning (especially when grammars include annotations to document meaning). Recording the entire history of grammar values into genealogy might be a bit expensive, but a compiler could instead record a set of secure hashes, perhaps `hash((hash(G), symbol))`. (*Note:* unique genealogy per symbol is useful in context of renames.)

Although resolving the multiple inheritance issue in this manner is feasible, I'm not convinced it's the right solution for glas systems. It requires mental gymnastics to understand which compositions of grammars are acceptable, order of application. 

#### Alternatives to Genealogy

I currently favor a simple solution: Single inheritance per method. That is, multiple inheritance of grammars is permitted only when the intersection of defined symbols is empty. More generally, we might specify for each symbol whether it it is assumed by the grammar (or mixin) to have already be defined or undefined.

#### Access to Previous Definitions

A keyword 'prior' might reference prior versions of definitions when extending definitions. This could reference the current method, or potentially reference other methods in context of multiple definitions updated atomically (e.g. foo = prior bar and bar = prior foo). The latter option is a little bit more flexible, though it may hinder refactoring of definition groups into mixins. OTOH, it does align with other atomic ops such as renames.

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

### Pubsub Set Based Interactions? Defer. Tentative.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. This would make writes commutative and idempotent - features we can potentially leverage. 

Just as channels may transfer channels, partial sets may provide access to partial sets. A remote process might process a set of requests that include locations to write responses, and those responses would be routed back to the initial caller. If the process leverages idempotence and commutativity, it is feasible to coalesce concurrent requests and responses, only forking computations where they observe different data. This would result in a highly declarative and reactive system, similar to publish-subscribe systems.

Unfortunately, it significantly complicates reads because there is no clear starting point. It also complicates termination because no individual writer can take responsibility to say "that's it, no more, we're done". Temporal semantics might help, e.g. we might require a time step between writing and reading (read past, write future). But this idea still needs a lot of work before it becomes viable.

*Note:* We can awkwardly and inefficiently model partial sets in glas data by encoding values into bitstrings then modeling a partial radix tree. I assume that if this feature is developed, partial sets would instead be abstracted by the language and efficiently supported by the compiler and runtime.

### No-Fail Contexts? Defer.

We can develop an effects API that may only be called from a no-fail (or no-backsies) context, i.e. where we can guarantee there is no backtracking after the effect. This would support a more direct API, with direct OS calls or FFI. This isn't even very difficult - we could introduce a few simple types to track no-fail method calls and no-fail API channels, then restrict (via static analysis) where the no-fail API channel is written.

This seems like a useful feature to develop later, but I also feel it's relatively low priority.

### Type Safety

We can use type annotations to describe expected types of methods, including arguments, the environment, and results. Data structure and channel protocols. Timing or latency assumptions in context of temporal semantics. Static parameter and result elements. Basically, anything we might want to check.

Ideally, users may be imprecise and we'll automatically infer missing properties based on context of use, specialized based on the current extensions. 

### Default Definitions? Tentative.

Support for 'default' definitions is convenient in some cases. Defaults might be represented by defining `anno/foo/default` in context of compiler awareness of defaults. The compiler would know to search for default definitions when the primary definition is not provided.

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

### Method Calls

I propose the initial language focus on the styles of procedural programming and pattern matching.

A conventional procedure call has at least three elements - argument, environment, and result. This might be concretely represented by an `(env, arg, ret)` triple, called an activation record. Each may contain an ad-hoc structured mix of data, channels, and pass-by-refs. Support for pass-by-refs in result is convenient for refactoring arguments.

To improve pattern matching, I propose to introduce two more elements: remaining arguments (output) and an optional adjunct argument (input). This extends our activation record to `(env, adj, arg, rem, ret)`.

* Remaining arguments supports incremental pattern matching. For example, when we parse an integer out of text, we would implicitly return the remaining text to the caller for further processing. This generalizes easily to matching on lists and records. 
* The adjunct argument is provided *within* a pattern, distinct from the argument we're trying to matching against. It serves the same role as curried parameters in F# active patterns, i.e. `let (|P|_|) adj arg = ...` allows P to explicitly adjust via adj how and whether a match occurs on arg. 

I will consider further extensions as needed, but this gives a good start at least. 

*Note:* Normal programs won't extend the activation record. That's the domain of language design. But a useful level of extensibility for normal programs is assured by the ability to match flexibly on ad-hoc structure. For example, a method might accept both list and dict arg (i.e. positional and keyword arguments).

#### Pass-by-Ref

Pass-by-reference is supported for all cases, including returned values. Returning pass-by-ref is useful for refactoring and for symmetric control over created processes. Output-only can be supported typefully. We always need an initial value to handle extension with new unhandled elements, but we could provide an input that's an invalid output to require handling.

Pass by ref can be modeled via implicit unification of a current value with a destination variable after we know we won't be updating the current value further. For write channels, this unification might be reversed, with the destination being an empty list, and unification instead closing the channel. A separate process could sequence the closed channel with a sequential copy. 

#### Environment Manipulation

The environment is passed to and returned from each subprogram. Some elements of the environment are read-only, others might be read-write or pass-by-ref. Returns should happen ASAP after we know an element of the environment isn't used. Structure of the environment is stable within a context. This stability simplifies local reasoning, extension, and composition.

Access to manipulate the environment might also be provided by implicit keyword variable named env. We could also provide structured access such as `with (newEnv) do { ... }` or a variation that runs to end of scope. But I'm uncertain anything special is needed. Perhaps even `let env = (newEnv) in { ... }` would be sufficient, with the idea being that there is an implcit argument named 'env' that is threaded everywhere but may be extended or overridden in lexical contexts.

*Aside:* I could require explicit syntax to refer to prior versions of a symbols, such as `let env = ... | prior env`. This could even be similar to referencing grammar namespaces, though we might generally need to distinguish namespace refs from local variables.


### Incremental Compilation

We can design a grammar compiler such that it memoizes and composes partial results, aligned with expression of grammar inheritance. This could help avoid some rework. 


## Namespace Builder

I want an AST for namespaces that efficiently compiles to a 'flat' dictionary with (rn:renames, def:definition) pairs. This could be followed by a function to apply renames to definitions. To keep it simple, the compiler may reserve a prefix for internal use; can easily raise an error if users attempt to define a reserved symbol.

* *rename:(in:NS, move:\[(Prefix1, Prefix2), ...\])* - rename multiple names within NS. This also renames them within definitions in NS. Initial Prefixes must not overlap. Final prefixes may overlap, but it's an error if two names are combined. 
* *scope:(in:NS, hide:\[Prefix1, Prefix2, ...\])* - move all names that start with the given prefixes into a fresh, anonymous namespace. The compiler may reserve a name prefix for all the anonymous namespaces. In practice, we'll mostly `hide:"~"`.

TODO:

* *define* new words or overrides, should specify an extra prefix to locally refer to prior definitions. Prior definitions are moved into a new anonymous namespace, then the given prefix is redirected to that namespace.

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


