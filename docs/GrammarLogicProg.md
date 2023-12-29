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

* *Anonymous namespaces.* Our language compiler can logically rename methods that start with a prefix such as './' to a fresh anonymous namespace. This provides a space for private definitions and local refactoring. 

* *Explicit translations.* We could allow more precise renames, such as renaming 'integer' to 'int' in foo. This would be useful for adapting mixins to another target, for conflict avoidance, and for community localization. Ideally, renames can be abstracted for reuse.

* *Containers.* A hierarchical namespace can be treated as a record-like container if we can easily rewrite things based on prefix: move `f/` to the root, simultaneously move everything else into an anonymous namespace.

* *Export Lists.* We could make it easy to restrict which symbols a grammar exports, perhaps renaming at the same time. All other symbols would be moved into an anonymous namespace. This could be useful to control conflicts.

* *Annotations by naming convention.* For example, annotate `integer` by defining `anno/integer/type` and `anno/integer/doc`. This makes annotations accessible and extensible, subject to the same refactoring and abstraction as all other data, and mitigates specialized syntax for annotations to a syntactic sugar. 

* *Assertions by naming convention.* Similar to annotations, we might model assertions by defining `assert/property-name` to a method that is treated as a proposition. These assertions would be evaluated in context of extensions to the namespace.

* *Nominative types.* It is feasible to use names within types, to index records or tag variants. There are advantages to use of names instead of bitstring labels: open records or variants can leverage the hierarchical namespaces and renaming to avoid conflicts. Anonymous names model abstract data types (ADTs). 

* *Interfaces.* We could define interfaces as namespaces that declare methods and abstract types, then document them via annotations. Mixins could share an interface to help ensure they mean the same thing by any given symbol. Default definitions might be represented via interfaces. Interface ascription could apply an export list based on an interface.

*Note:* Different names are never truly equivalent in context of update or extension. A grammar can define `foo = bar` but later extension or a source code update to the grammar may cause 'foo' to diverge from 'bar'. Namespaces will not support strong aliasing.

#### Multiple Inheritance

Multiple inheritance carries risk of ambiguity and confusion when a method is inherited from multiple sources. Ideally, we report issues to the user, but also resolve negligible conflicts without attention from the user. Two useful resolution strategies include genealogy (merge if edit history doesn't conflict) or unification (merge if definitions are the same). However, after exploring these I feel both are more complicated and expensive than I want, especially in context of renames, allocation of anonymous namespaces, and representing grammars as values (no extrinsic identity).

I propose a simple alternative: programs will make certain assumptions explicit, such as whether we are *introducing* or *overriding* a definition. These assumptions are trivially verified when we construct the namespace. If we introduce a definition from two sources, we'll raise a conflict. This hinders use of multiple inheritance in general, but it does support a useful multiple inheritance pattern: single inheritance plus mixins.

Interfaces specifically would benefit from a unification tactic. An interface will declare some methods (with optional defaults) while defining several annotations that document methods, add type declarations, and perhaps a few assertions. These annotations represent the meaning of the interface. Unification would allow multiple mixins to inherit the same interface while verifying that it is, indeed, using the same meaning for referenced methods.

I think single inheritance augmented with mixins and interfaces covers most useful multiple inheritance patterns while avoiding conflict. I'll need to see how this works out in practice.

When conflict does occur, it is usually one of two scenarios: 

First, a word is used with two meanings, for example artist 'draw' image versus pump 'draw' water. In this case, the correct resolution is to *rename* one or both words, i.e. to 'artist-draw' and 'pump-draw'. Alternatively, we could *hide* the version we won't be using, essentially renaming it to an anonymous namespace.

Second, a word is used with the same meaning, but has been implemented twice by accident. In this case, the correct resolution is to *move* at least one word, leaving references to the original word. We can override the original word with reference to the moved word. Indeed, use of 'move' is how we implement overrides with reference to the prior definition, we just have two prior definitions in case of conflicts. Alternatively we could *erase* the version we won't be using, essentially moving it to an anonymous namespace.

Conflict resolution is feasible based on flexible move and rename operations, but it still adds friction to development. It is much preferable to avoid conflict. Hierarchical namespaces can also help users avoid conflict.

#### Default Definitions

I could support defaults for arbitrary symbols. This might be modeled as a soft state between declared and introduced. When we 'override' default becomes an introduction, but if we 'introduce' default becomes declaration. Defaults would conflict with other defaults when they don't match definitions. 

Defaults might get complicated if we try to generalize things (priorities, overrides of defaults that preserve default priority, etc.) but I think we can get most benefits and avoid most complications by simply restricting defaults to interfaces. Perhaps support overrides of defaults within derived interfaces.

#### Access to Previous Definitions

If we override 'foo' within a grammar, we might reference the prior definition. This could be implemented in terms of 'moving' the inherited foo to a new name (such as '^foo') that we make private to the current grammar. 

I'm uncertain exactly what syntax I'd want for this. Perhaps a keyword 'prior foo' instead of a sigil.

#### Prefix to Prefix Renaming

I propose renames are applied root to child in a single pass and support ad-hoc prefix-to-prefix rewriting. 

Assuming the root grammar has a `foo => x` prefix rewrite, and the child adds a `bar => fo` rewrite, we'll logically apply the root rewrite *after* the child rewrite. Thus, we must compose rewrites such as `baro => foo => x`. It is convenient to model this as an index containing both `bar => fo` and `baro => x`, with a rule where the longest matching prefix applies.

By building the root index before the child rename index, we simplify the problem of finding all suffixes of `fo` that must be rewritten again. Similarly, if our parent rule was `f => xy` then the child rule `bar => fo` would compose into the index as `bar => xyo`. The parent rename could be fully applied without any additional cases.

This design can easily support hierarchical namespaces (rewrite empty prefix), anonymous namespaces (rewrite '.' prefix to something top-down path-dependent), and individual renames (by definition, symbols have unique prefixes). With the longest matching prefix rule, we can easily model export lists via renames: rename empty prefix to the anonymous namespace (see below), rename everything in the list to itself (or specified alias, if any).

*Note:* Support for 'move' separate from 'rename' might involve separating the index for renaming at the method scope versus method-body scope. A normal rename applies to both, but 'move' only to one the method layer. 

*Aside:* It is feasible to implement 'prior' by means of move then rename, but I'd need to either improve conflict analysis or immediately rewrite private methods with content addressing (difficult!).

#### Implementing Anonymous Namespaces

The compiler can pass a prefix representing the anonymous namespace together with the index for renames. This would be used for hiding definitions. Where a grammar inherits from other grammars, this anonymous namespace can be partitioned for each component grammar. The partitioning function should be stable enough for incremental compilation, but can be simple, e.g. a varnat index for each component.

A deep inheritance hierarchy might result in long private names. But that shouldn't become a significant concern in practice. This design does hinder use of private names within interfaces or similar diamond pattern inheritance. But that seems like an acceptable limitation. 

### Lifting Interfaces

We'll likely need lightweight syntax to delegate entire interfaces across hierarchical namespaces. For example, to provide the local effects interface to a component namespace. 

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

If we want to pass-by-ref through a channel, we cannot copy the read end of that channel. If we want to freely copy a channel, we cannot pass any messages through that channel that are not themselves freely copyable. It seems useful to support this via dynamic types, perhaps adding flags when a connected read-write channel pair is constructed. Even better if issues are detected at compile time. 

#### Pushback Operations

A reader process can push data or messages backwards into an input channel, to be read locally. This is a very convenient operation in some use cases, reducing the complexity for conditioning inputs.

### Pubsub Set Based Interactions? Defer. Tentative.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. This makes writes commutative and idempotent, features we can easily leverage. 

Just as channels may transfer channels, values written into a partial set could contain a partial set for receiving the response. We might also track substructural types based on whether sets may include pass-by-refs or channels, i.e. to control copying. Readers could thus read a set of requests and write a set of responses back to the caller, forming an interaction. If the reader process leverages idempotence and commutativity, it is feasible to coalesce concurrent requests and responses, only forking computations where they observe different data. This would result in a highly declarative and reactive system, similar to publish-subscribe systems.

Pubsub has some challenges. If we read and write within the same time-step, it isn't clear how we'd know the reader is done reading and hence done writing. This could be mitigated by temporal semantics - read the past, write the future - but it's also unclear how to stabilize temporal semantics and avoid high-frequency update cycles.  

*Note:* The glas data type does not directly support sets. Indirectly, we could encode values into bitstrings and model a set as a radix tree, or we could model a set as an ordered list. To support pubsub, I assume the language would abstract and accelerate sets. 

### Substructural Types

In context of user-defined abstract data types, via nominative types, it might be useful to mark certain objects with substructural properties similar to pass-by-ref or channels. This would be based on the premise that these types *might* have certain properties, and should be treated thusly, even if their current implementation does not. This might be expressed via flags when constructing the nominative type data.

### Transaction Loop Applications

Grammar-logic programs can be used to express [transaction loop applications](GlasApps.md). However, first-class channels would be troublesome at the loop boundary. We could restrict state to plain old data then rebuild channels within each loop. This might be acceptable if we're constructing stable channels as part of the stable transaction prefix.

Distributed computations could be based on distributing code that communicates via stable channels. Each step just does a little work then commits then logically rebuilds all the channels to await the next interaction, but the logical rebuild could be almost instant if we don't destabilize anything. Threads based on fair non-deterministic choice could keep each transaction small and specialized.

### No-Fail Contexts and Effects? Defer.

It is feasible to restrict certain effects to contexts where we can fully commit to the effect, i.e. where no backtracking is needed after the effect. The benefit is that we can directly use use synchronous request-response effects with the environment, without the trappings of transactions. The disadvantage is that we must deal with errors locally, rather than simply aborting and undoing.

I think this feature would be very difficult to implement without support from a type system and static analysis. But we could support it as a specialized run-mode for applications after the type system is sufficiently developed.

### Type Safety

We can use type annotations to describe expected types of methods, including arguments, environment, protocols, and results. I would like to begin developing a type system relatively early, preferably with a lot more flexibility than most languages to only partially describe types.

### Weighted Grammars and Search

We could introduce annotations on methods that assign heuristic scores based on their inputs or results. The glas system could use these scores to guide non-deterministic search where fair choice is not required. 

### Lazy Evaluation? Flexible.

As an optimization tactic, lazy evaluation is a good fit for grammar-logic and functional programming. I could potentially support a few annotations within the language to guide laziness. But I'd prefer to avoid lazy evaluation semantics. No lazy fixpoints ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot), for example. And preferably no reasoning about `_|_` in context of laziness.

### Module Structure

A grammar-logic module will compile into a structure such as:

        g:(def:(app:gram:(...), MoreDefs),  MetaData)

Currently, it is mostly just a dictionary. Aside from grammars, we might introduce definition types for rename rules, or name sets for efficient export and import, etc.. Perhaps even macros, in the future. The module may have some module-level metadata and annotations, e.g. to record a secure hash of the original text, or top-level comments. The 'g' header can help distinguish and integrate multiple module types in the future.

Within the module, we'll support imports. Imports can be understood as a form of inheritance, and we could support multiple inheritance with simple unification semantics (it's much easier in this context). We could also support references to 'prior foo' and similar at the grammar level. We can support qualified imports, where a definition is another 'g' type. 

Modules and grammars can be statically referenced as data from within a method. This might use a syntax such as `quote-def foo` versus `quote-module foo`. 

When interpreted as a program, we'll assume the 'main' method of the 'app' module is our entry.

### Extensible Syntax? Defer.

The most reasonable approach involves compile-time eval, so our embedded languages can load local modules, log messages, and build subprograms with the full capabilities of the language module.

Applying a macro or DSL would involve referencing a module-layer grammar definition. By default we'd call the 'main' method but we could permit the user to specify method. In any case, the grammar would be fully known at point of call, and it could be compiled and cached for reuse. The output from the macro might be an appropriate AST fragment, or potentially a function we can decompile into an AST. 

The range of input to a DSL-like reader macro might be limited based on an assumption for common use of braces, brackets, parentheses, and indentation. That is, we should assume a compatible tokenizer and directly raise an error if the input is not fully parsed by the macro (i.e. if the pass-by-ref input returns anything other than unit).

### Fine-Grained Staged Programming

It might be useful to support 'static' annotations to indicate which expressions should be statically computable within the fully extended grammar. This may propagate to static parameters in some use cases. Ideally, we could also support types that describe static arguments and results.

### Method Calls

A conventional procedure call has at least three elements - argument, environment, and result. This might be concretely represented by a `((env:Env, arg:Arg), Result)` pair, consistent with modeling functions as pairs. Effectful channels could be provided through env. The Env, Arg, and Result may each contain an ad-hoc mix of data, channels, pass-by-refs, etc.. 

To better support a grammar-logic language, I propose to tune method calls a bit:

A grammar-logic method is typically applied in context of pattern matching. For example, we might apply integer within a pattern to parse an integer from a text, returning the computed integer. But, in addition to that integer result, we must return any remaining, unparsed text. The remaining input can be returned via pass-by-ref. This mechanism generalizes to returning the remainder of a structured list or dictionary. Intriguingly, this can also model conditioning input for further pattern matching, assuming users have sufficient access to rewrite the input.

In addition to the main input for pattern matching, I want auxilliary inputs that could be used for lightweight abstraction or refinement of patterns. For example, we might express a ranged integer parse as `integer(min:-1, max:10)` as a pattern. To support procedural programming, we might allow normal method calls to focus on these other paramaters. One option is to treat pass-by-ref input as an implicit argument in certain contexts. 

We can understand 'env' as an implicit parameter that, other than being implicit, is no different from declared parameters. The pass-by-ref 'input' would be an implicit parameter only in pattern matching contexts. Input would be explicit when used outside of a pattern. Other arguments, such as 'min' and 'max' above, would be siblings to 'env' and 'input'. It would be an error to explicitly assign 'env' or 'input' in contexts where they are assigned implicitly. DSLs might introduce other implicit parameters for various roles.

*Note:* Method calls in grammar-logic are concurrent by default, constrained only by dataflow. But the implicit sequential threading of env will often constrain dataflow. Thus, concurrency in practice will largely revolve around controlling env.

#### Pass-by-Ref

In context of a grammar-logic language, a pass-by-ref mutable var might be modeled as an abstract pair `(Curr, Dest)` where our runtime will implicitly unify Curr and Dest after the final write to the var in scope. The program can update Curr as a functional state variable. Curr may have any type, including data, channels, and other pass-by-refs.

A pass-by-ref cannot be copied but it can be borrowed, i.e. we could further pass-by-ref the current value, replacing Curr with another free variable. The restriction on copying limits some patterns, but this is mitigated by the ability to return pass-by-refs from functions, or pass them through channels, to freely abstract patterns of writing into structures.

I would prefer to avoid depending on partial evaluation of Dest, at least implicitly. But an optimizing compiler could potentially determine the structure and some data in Dest before the final value is computed.

*Note:* Because method calls are concurrent, there is no specific issue with including refs in the return value. It is potentially very useful for refactoring method call arguments. But complicated use of pass-by-refs does increase risk of accidental deadlock. I'm interested in a lightweight static dataflow analysis to resist most issues.

#### Environment, Effects, and Concurrency

The 'env' parameter is implicitly passed via linear copy to called methods, threading any bundled data, channels, or pass-by-refs in a procedural style. Users cannot directly manipulate env, but the language will include keyword syntax for scoped manipulation of env, e.g. `with (newEnv) do { ... }` might shadow env within scope of the subprogram, and a simple variation might run to end of the current scope.

Procedural style implicit effects can be supported through channels in the env. For example, to access the filesystem, we might use a channel-based object where the runtime handles requests. But env will often be abstracted to simplify extension. Access to the abstract env might be provided through an interface of methods, and the abstraction might be protected via nominative types.

Dataflow of env will implicitly constrain concurrent computation in most cases. Users might work around this limitation via `with () { ... }` to reduce the environment to a trivial unit value, or perhaps `with (forkEnv()) { ... }` to explicitly construct an environment for concurrent computations. But these concurrent computations must interact through channels instead of shared memory.

#### Local Variables

It is possible to model local vars as something like arguments or pass-by-refs at the scope of individual operations. Importantly, a loop should only capture pass-by-refs that it potentially modifies. Pass-by-refs should be returned ASAP if aren't furhther modified within a scope.

### Pattern Matching

I need a concrete syntax and an understanding of semantics for patterns.

A string "Hello" should match any list (or perhaps list-like structure) that *starts with* the exact prefix, "Hello". We could try to generalize from "Hello": any list structure matches on a list prefix, and each element of the list should be fully matched in the sense that the remaining element is unit. Alternatively, embedding strings into patterns could be taken as a special case. I think generalizing is better, if feasible.





Unlike grammars on texts, I cannot simply assume I'm parsing a list input. We might primarily distinguish matching on lists versus records.

* List Patterns
  * `[A, B, C]` - match a list prefix
  * `[P]*` - repeatedly match a list prefix.
* Matching on `symbol:Pattern` will extract that symbol from a record, returning the remainder of the record.
* We must fully match each element of
* Matching a simple bitstring will 
* Matching on a record

Matching on a list


### Incremental Compilation

Modules compile separately into grammars. Large, composite grammars should compile incrementally into executables. That is, I should be able to cache many of the compilation steps in a manner that aligns with composition of the grammar. Prefix based renames already have this property, but there are a lot of other areas that need attention - incremental optimizations, etc..




# OLD STUFF

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


