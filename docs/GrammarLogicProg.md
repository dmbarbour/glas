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

I propose to build the grammar-logic entirely in terms of ordered choice, without direct use of unordered choice. This supports deterministic functions as the standard program type. Non-deterministic choice can still be supported indirectly via effects or partial inputs.

### Factoring Conditionals

The typical if-then-else or match-case syntax is awkward when multiple cases share a common prefix.

        if C1 and C2 then X else
        if C1 and C3 then Y else
        Z

This is also a challenge in many popular languages today. But this problem is exacerbated in context of ordered choice and backtracking. It's difficult to factor C1 in a way that avoids repetition and respects backtracking. 

        if C1 then
            if C2 then X else
            if C2 then Y else
            Z // oops, didn't backtrack C1
        else
            Z // oops, also repeated Z

        ...

        try
            if C1 then X else
            if C2 then Y else
            fail
        else
            Z   // oops, might backtrack from X or Y


These problems also appear in context of typical match-case syntax. I think I'll need a dedicated syntax for factoring conditionals. My first idea is to support subcases. Something like this:

        match arg with
        | C1 and
           | C2 -> X
           | C3 -> Y
        | _ -> Z

This seems like it might be adequate for factoring most conditionals locally. I can give it a try and see if I have new ideas.

Factoring is still limited more than I'd prefer. It isn't clear how to refactor partial function `C2 -> X | C3 -> Y` into a separate method call without conflating failures in X or Y with failures in C2 or C3. It seems feasible to use failure modes to specially support this factoring, but that feels like a hack. At least for now, I propose to allow only the final choice to be factored into a method call (without the `->` separator). It doesn't matter why we fail if there is no next choice. This avoids any syntactic impression of a perfect factoring in cases where factoring isn't perfect.

### Deterministic Functions

Methods in my grammar-logic language should represent deterministic functions, procedures, or processes by default. Non-deterministic computations will still be supported via effects or in contexts where we evaluate backwards. Mostly, this means programs cannot directly introduce unification variables or express unification. 

Unification variables can be introduced indirectly via effects or used in abstract by channels (and other primitives). Abstraction does impose constraints on the language: if we do not have static type checking to prove at compile time that channels are correctly used as channels, the runtime must instead track dynamic types (to at least distinguish channels from data).

### Non-Deterministic Choice

Non-deterministic choice can be expressed and controlled as an effect. Fair non-deterministic choice is useful for modeling concurrency in transaction loops. Biased choice is useful for modeling soft constraints and meta-stable reactive systems. To support biased choice, we might introduce scoring heuristics based on quality and stability. 

An intriguing possibility in context of grammar-logic programming is to model non-deterministic choice by returning a logic unification variable. This would make the choice implicitly adapt to the program. However, it would be difficult to implement fair choice from an infinite set.

### Extensible Namespaces

Instead of a monolithic grammar (or logic), I propose to model grammars as a collection of named rules. This allows us to express OOP-like inheritance and override. For example, we might want a new language the same as the old one, except with a new option to parse an integer.

        grammar foo extends bar . baz with
            integer = ...  

I propose to model all grammars as functions on a namespace. This allows for multiple inheritance, but see *Multiple Inheritance* for a discussion on conflict resolution.

Related ideas:

* *Hierarchical namespaces.* We might take `foo in f` to translate all names defined in foo to the `f/` namespace, such as `f/integer`. Hierarchical structure would help control name collisions while still allowing flexible extension to methods within that namespace.

* *Anonymous namespaces.* Our language compiler can logically rename methods that start with a specific prefix to a fresh anonymous namespace. This provides a space for private definitions and local refactoring. 

* *Explicit translations.* We could allow more precise renames, such as renaming 'integer' to 'int' in foo. This would be useful for adapting mixins to another target, for conflict avoidance, and for community localization. Ideally, renames can be abstracted for reuse.

* *Export Lists.* We could make it easy to restrict which symbols a grammar exports, perhaps renaming at the same time. All other symbols would be moved into an anonymous namespace. This could be useful to control conflicts.

* *Annotations by naming convention.* For example, annotate `integer` by defining `anno/integer/type` and `anno/integer/doc`. This makes annotations accessible and extensible, subject to the same refactoring and abstraction as all other data, and mitigates specialized syntax for annotations to a syntactic sugar. 

* *Assertions by naming convention.* Similar to annotations, we might model assertions by defining `assert/property-name` to a method that is treated as a proposition. These assertions would be evaluated in context of extensions to the namespace.

* *Nominative types.* It is feasible to use names within types, to index records or tag variants. There are advantages to use of names instead of bitstring labels: open records or variants can leverage the hierarchical namespaces and renaming to avoid conflicts. Anonymous names model abstract data types (ADTs). 

* *Interfaces.* We could define interfaces as namespaces that declare methods and abstract types, then document them via annotations. Mixins could share an interface to help ensure they mean the same thing by any given symbol. Default definitions might be represented via interfaces.

*Note:* Different names are never truly equivalent in context of update or extension. A grammar can define `foo = bar` but later extension or a source code update to the grammar may cause 'foo' to diverge from 'bar'. Namespaces will not support strong aliasing.

#### Multiple Inheritance

I propose to keep multiple inheritance very simple: multiple inheritance is permitted when two grammars introduce different symbols, or if they introduce the same symbol with the same definition. This could be understood as unification-based multiple inheritance, combining the namespaces as a simple union of sets. 

With this strategy, [diamond inheritance](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem) is mostly limited to shared interfaces. Multiple inheritance will lean more heavily into *mixins*, which override definitions without introducing them. Compared to a genealogy-based inheritance algorithm, this is less flexible but easier to implement and reason about. 

Sketch of algorithm:

* A grammar tracks for each symbol whether it is declaring, introducing, or overriding a definition.
  * A symbol must be declared before use, and introduced before override; introduction implies declaration.
* When introducing a def, compiler saves current def and defers conflict detection.
* Conflict detection is performed at next introduction or after inheritance is processed.

This might need to be tweaked depending on how we compile definitions, and depending on how we implement anonymous namespaces. But the intention is to avoid comparing for equal definitions before overrides and extensions are applied. When overrides are applied, the entire history of prior definitions generally becomes part of that definition for comparison, though we might be more precise if an override doesn't reference its prior.

#### Default Definitions

I could support defaults for arbitrary symbols. This might be a soft state between declared and introduced. Perhaps when we attempt to 'override' a default, it becomes an introduction, but if we 'introduce' a symbol that has a default, the default becomes a mere declaration.

#### Access to Previous Definitions

I propose use of keyword prior to reference previous versions of the current definitions. To simplify reasoning and implementation, only the current definition can reference its own prior. This ensures the history of each word can be taken independently (no need to think about which words updated together). 

For convenient use of prior, we should provide a lightweight syntax to forward all arguments to another method. Would also be useful for delegation in general. Syntactically, something like `delegate to foo` or `foo(...)`. 

#### Prefix to Prefix Renaming

Ideally we can compose an indexed rename structure in a single pass while compiling grammars, such that all renames can be batched and easily cached.

The main challenge is overlapping renames. When I rewrite prefix `foo => x` in context of renaming `bar => fo`, the composite rename must imply both `baro => x` and `barn => fon`. Note that `fo` cannot be a complete name, otherwise rename of `foo` would be invalid, thus we can assume `bar` must also have a suffix. We can handle every `bar(X)` case. We only need one identity case branch per bit.

        bar(0b1) => fo(0b1)
        bar(0b00) => fo(0b00)
        bar(0b010) => fo(0b010)
        ... identity bitstrings of size 4, 5, 6, 7
        bar(0b01100110) => fo(0b01100110)   // barn => fon case
        bar(0b01100111) => x                // baro => x   case

This isn't pretty, but it should be within tolerable overheads for indexing these cases.

A secondary challenge is indexing things the renames themselves. Assuming I'm building from the outside in, my current index has `bar => fo` and then I'm presented with the `foo => x` case and must somehow locate `bar` as a potential prefix of `foo`. This requires some form of reverse lookup index. 

At the byte level, we would need to search for:

* all indices that produce `food` (or other suffixes of `foo`)
* all indices that produce `foo`
* all indices that produce `fo`
* all indices that produce `f`
* all indices that produce the empty prefix

Any of these could be a potential match for `foo => x` in context. And we'd need to generalize to bit-level matching. 

Although, maintaining this index is not trivial, it should be possible to support ad-hoc prefix-to-prefix renaming in a reasonably efficient, compositional, top-down manner. A simplistic, less scalable implementation remains an option when getting started.

#### Stable Anonymous Namespaces

A related issue is how to represent anonymous namespaces. This is a good fit for renaming: the compiler implicitly renames symbols that start with a specific prefix (perhaps `.`) to an anonymous namespace. Ideally, this rename is stable - cacheable for incremental computing. 

We could build stable identifiers for each included grammar based on inclusion path (using names in the original module). In the rare case we directly inherit from a mixin multiple times, we could we could introduce a numeric index and raise a stability warning.

We could restrict use of anonymous symbols within namespaces because it would interfere with equivalence ofnmaespaces.

*Note:* Content addressing doesn't work in context of renames unless we can guarantee there are no references from the private space back to public definitions. So we're stuck with path-based names. 

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

The cost of temporal semantics is complexity. The language runtime must clearly track 'held' channels. And it is relatively inefficient to push time steps individually, so a runtime must compress sequential time steps and accelerate operations that interact with time steps (e.g. polling). 

I feel this idea is worth exploring, but it might not be available immediately. Ideally, I should develop the language such that temporal semantics can be introduced later with full backwards compatibility.

#### Reflective Race Conditions? Tentative. As Effect.

A runtime can potentially merge events from multiple channels non-deterministically based on arrival order. Arrival order will vary based on under-the-hood features such as multi-processing, cache sizes, and processor preemption by the operating system. This is a potential alternative to temporal semantics, and is easier to implement efficiently. 

However, this allows for race conditions to influence observable program behavior. Race conditions due to arrival-order non-determinism are worse for reasoning and testing than most other forms of non-determinism because it's highly machine dependent. This easily results in difficult to reproduce "works on my machine" bugs.

Predictability can be mitigated insofar as we control where race conditions are introduced. But I think I would favor temporal semantics for most potential use cases. AFAIK, there isn't a strong use case for combining the two.

#### Bundling and Abstracting Channels

Duplex channels can be modeled as a read-write pair of partial lists. We could model a dictionary or list of channels, perhaps mixed with data and other abstract structure such as pass-by-ref vars. To protect abstractions, pattern matching must discriminate at least the general type, such as data vs. channels.

Our runtime would need to track which elements are channels or pass-by-refs in order to properly handle temporal semantics, automatic close upon scope exit, and so on. This could be implemented as keeping some parallel type information with every value, or perhaps adding annotations to values. Static type analysis can be favored where feasible.

Assuming we develop this, mixed bundles of channels and data become the normal type for method call arguments, environments, and results or channel messages. Channels, and dictionaries or lists thereof, are effectively first-class, albeit limiting copy operations.

#### Channel-Based Objects and Functions

An object can be modeled as a process that accepts a subchannel for each 'method call'. The object reference would be the channel that delivers the method calls. The separate subchannel per call ensures responses are properly routed back to the specific caller, in case the reference is shared. (An object reference might be shared via *Temporal Semantics* or *Reflective Race Conditions*.)

Functions can be modeled similarly as a *stateless* object. If the runtime knows the object is stateless, it can optimize method calls to evaluate in parallel, interactions may be forgotten because they don't affect any external state, and the channel may be freely copied because there is no need to track order of writes. Effectively, the channel serves as a first-class function (albeit with restrictions on how it is passed around or observed).

Ideally, grammar methods and object methods via channels would have a consistent syntax and underlying semantics.

#### Channels with Substructural Constraints

If we want to pass-by-ref through a channel, we cannot copy the read end of that channel. If we want to freely copy a channel, we cannot pass any messages through that channel that are not themselves freely copyable. It seems useful to support constraints like this via dynamic types, perhaps based on flags when a channel pair is constructed. Even better if issues are detected at compile time. 

#### Pushback Operations

A reader process can push data or messages backwards into an input channel, to be read locally. This is a very convenient operation in some use cases, reducing the complexity for conditioning inputs.

### Pubsub Set Based Interactions? Defer. Tentative.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. This makes writes commutative and idempotent, features we can easily leverage. 

Just as channels may transfer channels, values written into a partial set could contain a partial set for receiving the response. We might also need to track substructural types of pubsub sets to control copying. Readers could thus read a set of requests and write a set of responses back to the caller, forming an interaction. If the reader process leverages idempotence and commutativity, it is feasible to coalesce concurrent requests and responses, only forking computations where they observe different data. This would result in a highly declarative and reactive system, similar to publish-subscribe systems.

Pubsub has some challenges. If we read and write within the same time-step, it isn't clear how we'd know the reader is done reading and hence done writing. This could be mitigated by temporal semantics - read the past, write the future - but it's also unclear how to stabilize temporal semantics and avoid high-frequency update cycles.  

*Note:* The glas data type does not directly support sets. Indirectly, we could encode values into bitstrings and model a set as a radix tree, or we could model a set as an ordered list. To support pubsub, I assume the language would abstract and accelerate sets. 

### Substructural Types

In context of user-defined abstract data types, via nominative types, it might be useful to mark certain objects with substructural properties similar to pass-by-ref or channels. This would be based on the premise that these types *might* have certain properties, and should be treated thusly, even if their current implementation does not. This might be expressed via flags when constructing the nominative type data.

### Transaction Loop Applications

Grammar-logic programs can be used to express [transaction loop applications](GlasApps.md). However, first-class channels would be troublesome at the loop boundary. We could restrict state to plain old data then rebuild channels within each loop. This might be acceptable if we're constructing stable channels as part of the stable transaction prefix.

Distributed computations could be based on distributing code that communicates via stable channels. Each step just does a little work then commits then logically rebuilds all the channels to await the next interaction, but the logical rebuild could be almost instant if we don't destabilize anything. Threads based on fair non-deterministic choice could keep each transaction small and specialized.

### Control of Backtracking and Incremental Commit? Tentative.

It is possible to design computations that won't fail and backtrack beyond some scope. This might be modeled in terms of returning an error and moving on instead of backtracking.

Insofar as we can ensure this property, we can support a more conventional effects API, e.g. sending a request to a remote service then awaiting a response. At the very least, we could support a more conventional application model where we can commit transactions without logically rebuilding channels every cycle.

The main issue is that I'm uncertain how to make this easy to express and use in context of channels and process networks. I suspect some very sophisticated types would be needed, which is something I don't want to deal with up front.

### Type Safety

We can use type annotations to describe expected types of methods, including arguments, the environment, and results. Data structure and channel protocols. Timing or latency assumptions in context of temporal semantics. Static parameter and result elements. Basically, anything we might want to check.

Ideally, users may be imprecise and we'll automatically infer missing properties based on context of use, specialized based on the current extensions. 

### Weighted Grammars and Search

We could introduce annotations on methods that assign heuristic scores based on their inputs or results. The glas system could use these scores to guide non-deterministic search where fair choice is not required. 

### Lazy Evaluation? Flexible.

As an optimization tactic, lazy evaluation is a good fit for grammar-logic and functional programming. I could potentially support a few annotations within the language to guide laziness. But I'd prefer to avoid lazy evaluation semantics. No lazy fixpoints ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot), for example. And preferably no reasoning about `_|_` in context of laziness.

### Module Structure

Toplevel module structure can be similar to g0 - one open module declaration (single inheritance), several imports and definitions, finally an optional export list. Imports could be qualified or provided as a list. 

Definitions include grammars (mixins, interfaces), perhaps separate renames and control lists. At least initially, I intend to avoid anything that requires compile-time eval: data definitions, macros, top-level assertions, export expressions, etc.. Those might be introduced later, after bootstrap and accelerated eval.

When used as an application, we'll refer to the 'main' method within a grammar. We could use annotations (perhaps via `anno/main/run`) to select application modes or configurations.

### Applications as Servers

The environment can provide access to a mailbox or tuple space of requests which can be searched by topic and support responses. This would provide a more flexible approach to applications providing services, including HTTP-based HCI, without need to explicitly build TCP connections or similar. I think this would be very nice in context of live coding or transaction loop applications.

### Extensible Syntax? Defer.

One reasonable approach involves compile-time eval, compiling a local language to the structured AST. This is necessary so our embedded languages can load local modules and build subprograms, and also to stabilize meaning (extensions change definitions, but not the meaning of the original def). This approach requires compile-time eval and thus should be deferred to post-bootstrap.

We could use indentation or braces to delimit the language (and perhaps only permit language extensions that respect pairing of braces, parens, brackets). To simplify indentation-sensitive languages, we might convert texts with indentation to use braces or control chars (e.g. STX/ETX).

### Fine-Grained Staged Programming

It might be useful to support 'static' annotations to indicate which expressions should be statically computable within the fully extended grammar. This may propagate to static parameters in some use cases. Ideally, we could also support types that describe static arguments and results.

### Method Calls

A conventional procedure call has at least three elements - argument, environment, and result. This might be concretely represented by an `(env, arg, ret)` triple, called an activation record. Each may contain an ad-hoc structured mix of data, channels, and pass-by-refs. Support for pass-by-refs in result is convenient for refactoring arguments.

To better support a grammar-logic language, I propose to tune method calls a bit:

A grammar-logic method is typically applied in context of pattern matching. For example, we might apply integer within a pattern to parse an integer from a text, returning the computed integer. But, in addition to that integer result, we must return any remaining, unparsed text. The remaining input can be returned via pass-by-ref. This mechanism generalizes to returning the remainder of a structured list or dictionary. Intriguingly, this can also model conditioning input for further pattern matching, assuming users have sufficient access to rewrite the input.

In addition to the main input for pattern matching, I want auxilliary inputs that could be used for lightweight abstraction or refinement of patterns. For example, we might express a ranged integer parse as `integer(min:-1, max:10)` as a pattern. To support procedural programming, we might allow normal method calls to focus on these other paramaters. One option is to treat pass-by-ref input as an implicit argument in certain contexts. 

We could similarly treat env as an implicit parameter. The return value might be understood as an output-only pass-by-ref parameter, albeit structurally enforced by the language. In general, the idea is that the caller exchanges data with a method through an activation record with keyword parameters, with a few contextually implicit parameters. Implicit parameters can potentially be adjusted in context of DSLs or macros, once I'm ready to support those.

*Note:* In contrast to conventional procedural languages, method calls in grammar-logic are concurrent by default, constrained by data dependencies. An optimizing compiler might use a call stack in cases where synchronous call-return aligns with dataflow, or may use lazy evaluation if we don't need the return value immediately, but must generally use a flexible evaluation order.

#### Pass-by-Ref

In context of a grammar-logic language, a pass-by-ref mutable var might be modeled as an abstract pair `(Curr, Dest)` where our runtime will implicitly unify Curr and Dest after the final write to the var in scope. The program can update Curr as a functional state variable. Curr may have any type, including data, channels, and other pass-by-refs.

A pass-by-ref cannot be copied but it can be borrowed, i.e. we could further pass-by-ref the current value, replacing Curr with another free variable. The restriction on copying limits some patterns, but this is mitigated by the ability to return pass-by-refs from functions, or pass them through channels, to freely abstract patterns of writing into structures.

I would prefer to avoid depending on partial evaluation of Dest, at least implicitly. But an optimizing compiler could potentially determine the structure and some data in Dest before the final value is computed.

#### Environment Manipulation

The environment is passed to each subprogram. It isn't returned from a subprogram, but it might contain pass-by-ref elements or channels that are threaded between subprograms. The intention here is that env has a stable toplevel structure controlled by the caller, and any immutable data in env can be processed in parallel by each subprogram.

A program can control the environment exposed to its own subprograms. The syntax for this might be something like `with (newEnv) do ...`. Users would not directly define 'env', and it isn't directly mutable, but it can be controlled within a scope.

### Incremental Compilation

Ideally, the grammar logic language should be designed such that some clever memoization and caching can reduce rework when building the same or similar grammars repeatedly.

## Namespace Builder

I want an AST for namespaces that efficiently compiles to a 'flat' dictionary with (rn:renames, def:definition) pairs. This could be followed by a function to apply renames to definitions. To keep it simple, the compiler may reserve a prefix for internal use; can easily raise an error if users attempt to define a reserved symbol.

* *rename:(in:NS, move:\[(Prefix1, Prefix2), ...\])* - rename multiple names within NS. This also renames them within definitions in NS. Initial Prefixes must not overlap. Final prefixes may overlap, but it's an error if two names are combined. 
* *scope:(in:NS, hide:\[Prefix1, Prefix2, ...\])* - move all names that start with the given prefixes into a fresh, anonymous namespace. The compiler may reserve a name prefix for all the anonymous namespaces. In practice, we'll mostly `hide:"~"`.

TODO:

* *define* new words or overrides

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


