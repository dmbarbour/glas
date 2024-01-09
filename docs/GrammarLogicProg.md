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

This seems like it might be adequate for factoring most conditionals locally. My intuition is that it would generalize into a 'decision tree' structure under the hood. I can give it a try and see if I have new ideas.

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

* *Hierarchical namespaces.* We can model hierarchical structure of names, based on some conventions for directory-like paths (e.g. `dir/name`). Hierarchical structure would help control name collisions while still allowing flexible extension to methods within that namespace.

* *Anonymous or Private namespaces.* Our language compiler can reserve some prefixes for private use, then automatically rename private methods to avoid conflict. Compared to explicit 'private' methods, reserved prefixes would avoid conflict with public use of the same name in another context.

* *Prefix Based Rename and Move.* It is feasible to construct a single-pass index to move and rename things, and also specify the rename destination for private names. This can support both automatic and manual resolution of conflicts. Moving a definition might leave the original name 'declared'. Hiding and erasing names can be modeled in terms of rename or move to a fresh anonymous name. It is technically feasible to 'merge' names, e.g. rename foo to bar when bar is already defined or declared, but I'm not sure this is a good idea; it at least should be prevented from happening by accident.  

* *Higher Order Components and Containers.* We can support template-like abstraction or mixins, via something like: `import foo(a:x, b:y, c:z) as bar` or simply `from foo(a:x, b:y, c:z) import (importList)`. The first would create a namespace 'bar' based on grammar 'foo' where we override names 'a', 'b', 'c' with 'x', 'y', and 'z' in local context. The latter would allow directly binding some names into the local namespace from 'bar' into the local namespace. 

* *Annotations by naming convention.* We can bind names to annotations based on associated names, e.g. method `main` might be associated with `anno/main/type`, `anno/main/doc`, and others. When we specify renames or moves, annotations may also be renamed or moved. This design also makes annotations extensible.

* *Assertions by naming convention.* Similar to annotations, we might model assertions by defining `assert/property-name` to a method that is treated as a proposition. These assertions would be evaluated in context of extensions to the namespace.

* *Nominative types.* It is feasible to use names within within types, e.g. to index records or to tag variants. This could be leveraged as a basis for abstract data types, especially when combined with private namespaces.

* *Interfaces.* We can potentially declare methods and define their intended types and documentation separately from defining the implementation for a method. Default definitions can also be supported.

#### Multiple Inheritance

Multiple inheritance carries risk of ambiguity and confusion when a method is inherited from multiple sources. Ideally, most conflicts can be avoided or automatically resolved, and programmers have tools for explicit and concise resolution where needed.

I propose a lightweight structural approach to automatic conflict resolution. For each method we indicate whether we expect to introduce or override that definition. If we attempt to introduce a method twice, or override a method that hasn't been defined, we have an error. We might introduce an additional rule to support interfaces, based on unification of definitions (feasible if we can exclude private names). Anyhow, what I want to avoid is any form of deep or complicated analysis for conflict resolution. This should be something that users can easily reason about while supporting usable strategies to avoid conflict.

Users can avoid most conflicts by sticking to patterns such as single inheritance of the main program plus mixins, interfaces, and hierarchical components. When conflict does occur, it is feasible to resolve this via rename (suitable when names have two different meanings) or move. Access to prior definitions can be modeled as moving the prior definition into a private space, but we can give users more explicit control over moves.

#### Default Definitions

I could support defaults for arbitrary symbols. This might be modeled as a soft state between declared and introduced. When we 'override' default becomes an introduction, but if we 'introduce' default becomes declaration. Defaults would conflict with other defaults when they don't match definitions. 

Defaults might get complicated if we try to generalize things (priorities, overrides of defaults that preserve default priority, etc.) but I think we can get most benefits and avoid most complications by simply restricting defaults to interfaces. Perhaps support overrides of defaults within derived interfaces.

#### Access to Previous Definitions

When we override or shadow a definition, our language might automatically move the prior definition to a conventional private location, such as 'foo' to 'prior foo'. This should be implemented in terms of move, rename, and anonymous structure. The difference between override and shadow is whether existing references are also redirected to 'prior foo'.

#### Implementation of Namespaces

We'll compile a namespace into a flat dictionary of definitions. This involves prefix-oriented renaming of things. A set of renames might be expressed as an associative map such as `{ f => xy, foo => x }` where the longest matching prefix wins. We can compose rewrites from root to leaf, i.e. if the root applies the aforementioned rewrite and the child nodes `{ bar => fo }` we can compute a composite map `{ bar => fo, baro => x, f => xy, foo => x }`. This is based on the longest matching prefix including all possible suffixes of `fo`. It is feasible to apply renames in a single pass.

Anonymous namespaces need special attention. The simplest implementation of anonymous namespaces is to reserve a privacy prefix for each component grammar, but this results in too many redundant definitions. One potential alternative is content addressing: we compute a secure hash for each 'clique' of mutually recursive definitions, then use content addressing in the compiled name for a method. This could be done for public methods, too, in which case the compiled dictionary would include this and a an association binding public names to content address. Common definitions can be shared. This would essentially add some extra passes to identify cliques and add clique-specific renames, but it could be incremental.

### Channel Based Interactions

My initial thought is to model interactions around channels. This gives me many features I want - compositionality, simplicity, scalability. Procedural interaction can be modeled via request-response channel. Although extensibility is limited due to linear ownership of channels, this can be mitigated by modeling a databus or other extensible architecture.

Channels can be modeled as partial lists, with the writer holding a cursor at the 'tail' of the list, and the reader holding the 'head'. To write a channel, we unify the tail variable with a pair, where the second element is the new tail. Then we update the local cursor to the new tail, ensuring we only write each location once. A written channel may be 'closed' by unifying with the empty list. (Closing a channel can be implicit based on scope.)

Channels aren't limited to moving data, they can also transfer channels. We can build useful patterns around this, modeling objects or remote functions or a TCP listener as a channel of channels. 

However, in context of deterministic computation, channels are essentially linear types. If there are two writers, writes will conflict. And even reading a channel must be linear if the reader might receive a writable channel. This can be mitigated - we can use *temporal semantics* to deterministically merge asynchronous writes. Or we could support non-deterministic merge based on arrival-order with runtime support (see *reflective race conditions*). With the ability to merge asynchronous events, channels can model openly extensible systems, such as a databus or router.

An inherent risk with channels is potential deadlock with multiple channels are waiting on each other in a cycle. This is mitigated by temporal semantics: if at least one channel in the cycle has latency, deadlock is broken. It can also be avoided via static analysis (perhaps [session types](https://en.wikipedia.org/wiki/Session_type)).

#### Temporal Semantics? Tentative.

Temporal semantics support deterministic merge of asynchronous events. This significantly enhances compositionality and extensibility, e.g. we can model processes interacting through a databus or publish-subscribe system.

Temporal semantics can be implemented for channels by introducing time step messages. A process may wait for a time step, which implicitly writes a time step to every held output channel, and increments a counter var for every input channel. When a process reads a channel, if the next message is a 'time step' it will try to decrement the counter and remove that message. If the counter is already zero, read fails with a 'try again later' status, i.e. to require that the process wait a time step. Any number of messages may be delivered within a time step.

Logical time steps are potentially aligned with fine-grained schedules in the real-world, such as nanoseconds. Thus, the system needs to optimize a few cases: compress sequential time-steps in the channel, and allow waiting on a set of input channels to model efficient polling. This feature can support programming of time-sensitive systems. Even with transaction loop applications, we could schedule output events relative to an idealized commit time.

The cost of temporal semantics is complexity. It must be clear which channels are 'held' by a process.

I feel this idea is worth exploring. Ideally, I should develop the language such that temporal semantics can be introduced later.

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

### Speech Act Based Interactions? Defer.

I once read about an interesting language proposal called [Elephant](https://www-formal.stanford.edu/jmc/elephant/elephant.html) by the creator of Lisp. In this language, interactions are based on speech acts. Various speech acts are distinguished, such as assertions, questions, answers, offers, acceptances, commitments, and promises. But essentially we have an IO semantics beyond mere structured exchange of data.

I'm uncertain where I'd want to go with this. But it does remind me of the HTTP distinctions between GET, PUT, and POST operations, where some interactions can be systematically cached by intermediate proxies while others cannot. We could try to augment interactions or specific exchanges with promises of idempotence, commutativity, monotonicity, cacheability, etc.. OTOH, structured approaches to guarantee monotonicity or idempotence, such as pubsub sets or abstract CRDTs, might offer a more robust foundation than mere promises by programmers.

### Substructural Types

In context of user-defined abstract data types, via nominative types, it might be useful to mark certain objects with substructural properties similar to pass-by-ref or channels. This would be based on the premise that these types *might* have certain properties, and should be treated thusly, even if their current implementation does not. This might be expressed via flags when constructing the nominative type data.

### Transaction Loop Applications

Grammar-logic programs are very suitable for [transaction loop applications](GlasApps.md). 

Integration tweaks relative to my earlier prog model:

* Step state should be plain old data; no holding refs to prior steps or timeline. 
* In effects API, use named refs only to introduce channels; robust ocap security.
* Consider logic unification vars for *search* instead of (just) bools and ratios.
* Overlay networks with distributed transactions can be modeled via stable prefix. 

Overlay networks would be based on establishing channels between distributed processes within the stable prefix of a transaction. This would benefit from location-specific effects channels. Before we commit the transaction, all of these processes terminate and the channels will be closed; but in context of incremental computing, we'll rebuild those channels and processes, effectively representing long-lived channels and processes. 

### Procedural Applications

If we can guarantee that certain contexts never backtrack, we can support more flexible APIs such as synchronous request-response. The disadvantage is that we'll very often be dealing explicitly with error values, undo, etc.. But it'd be nice to have the option. We can consider supporting this as a special run-mode if we develop a type or proof system that supports the guarantee.

### Type Safety

We can use type annotations to describe expected types of methods, including arguments, environment, protocols, and results. I would like to begin developing a type system relatively early, preferably with a lot more flexibility than most languages to only partially describe types.

### Weighted Grammars and Search

We could introduce annotations on methods that assign heuristic scores based on their inputs or results. The glas system could use these scores to guide non-deterministic search where fair choice is not required. 

### Flexible Evaluation Order

Method calls in grammar-logic are opportunistic in terms of computation order, constrained only by dataflow. But we could support some performance and analysis hints representing programmer intentions, e.g. annotate subprograms as eager, parallel, or lazy.

Lazy evaluation is a good fit for grammar-logic and functional programming, at least for pure functions. Effectful functions would effectively be eager until they're finished writing requests. However, I'd prefer to avoid lazy evaluation semantics: no lazy fixpoints ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot), no divergence semantics. In general, computation should be opportunistic, i.e. anything that can be computed is computed, and any specific tactic is an optimization.

### Extensible Syntax? Defer.

The most reasonable approach involves compile-time eval, so our embedded languages can load local modules, log messages, and build subprograms with the full capabilities of the language module.

Applying a macro or DSL would involve referencing a module-layer grammar definition. By default we'd call the 'main' method but we could permit the user to specify method. In any case, the grammar would be fully known at point of call, and it could be compiled and cached for reuse. The output from the macro might be an appropriate AST fragment, or potentially a function we can decompile into an AST. 

The range of input to a DSL-like reader macro might be limited based on an assumption for common use of braces, brackets, parentheses, and indentation. That is, we should assume a compatible tokenizer and directly raise an error if the input is not fully parsed by the macro (i.e. if the pass-by-ref input returns anything other than unit).

### Fine-Grained Staged Programming

It might be useful to support 'static' annotations to indicate which expressions should be statically computable within the fully extended grammar. This may propagate to static parameters in some use cases. Ideally, we could also support types that describe static arguments and results.

### Method Calls

A conventional procedure call has at least three elements - argument, environment, and result. This might be concretely represented by a `((env:Env, arg:Arg), Result)` pair, consistent with modeling functions as pairs. Effectful channels could be provided through env. The Env, Arg, and Result may each contain an ad-hoc mix of data, channels, pass-by-refs, etc.. 

To better support a grammar-logic language, I propose to tune method calls a bit:

A grammar-logic method is typically applied in context of pattern matching. For example, we might parse an integer from a text, returning both the computed integer and the remaining input. I think we need to support this 'remaining input' in a general way that generalizes to input types other than lists, such as a pass-by-ref variable.Intriguingly, we could also use this to return a conditioned input or represent pushback. 

Auxilliary inputs could be used for lightweight abstraction or refinement of patterns. For example, we might express a ranged integer parse as `integer(min:-1, max:10)` method call as a pattern, with 'input' and 'env' both as tacit arguments. To support procedural programming, we might enable method calls to focus on these paramaters. One option is to treat pass-by-ref as an implicit argument in certain contexts. In general, we might simply treat 'input' as a standard parameter to method calls alongside 'env'. In any case, if a method doesn't use it, the pass-by-ref input would be returned immediately. 

We can model 'input' and 'env' as otherwise normal parameters supported with some syntactic sugar. 

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

Patterns are syntactic sugar over procedural fragments. We will support procedure fragments within the pattern to guarantee patterns have full flexibility of procedures. We can use method calls within patterns outside of a procedure fragment, in which case the 'input' parameter is implicitly threaded. Constants and lists have some special support, mostly operating on 'input' and returning themselves. 

In some cases an exact match is needed, in which case we'll insist that the 'remaining' input is unit. Our syntax can support both `parse &var with ...` and `match Expr with ...`, with the latter requiring an exact match but allowing any expression for initial input. A normal method call might be defined in context of an implicit `parse &input with ...`. And we can explicitly provide an 'input' argument in contexts where one is not implicitly provided.

Thoughts:

* Constant Patterns
  * Integer - match an integer (variable width bitstring) exactly, return matched integer. This includes all integer encodings such as `0xAB` and `0b101` and `42`.
  * Symbol - match a symbol exactly, return matched symbol.
  * Text - match start of text, return matched text, remaining text returned via input. Binaries as special encoding for text, maybe `x"0123456789ABCDEF"`
* List Patterns
  * `[A, B, C]` - match `(A, (B, (C, Rem)))` returning Rem as remaining input. A, B, C must be exact matches.
* Record or Variant Patterns
  * label:Pattern - match a specific label, removing it from input and returning remaining record. 
  * typename of Pattern - similar but for nominative type indexed data
* Meta Patterns
  * P opt - optional match
  * P rep - match P multiple times
  * P until Q - match P repeatedly until Q is matched once.
  * P or Q - match P or Q

An important concern is how to return values from meta-patterns. A repeating pattern P has multiple variables the pattern P, but now we need a variable for the repetition. This could be supported by treating the variables as fields in a record of the metapattern. We could have special support for converting the array of structs to a structure of arrays if the meta-pattern variable isn't named. 

### Module Layer

        g:(def:(app:gram:(...), MoreDefs),  MetaData)

A grammar-logic module will compile into a simple dictionary structure and ad-hoc metadata. The 'g' header will become useful when integrating multiple glas languages. A grammar-logic module that represents an application will define an 'app' grammar containing a 'main' method. 

I propose compiled definitions be represented by independent values. For example, in case of `grammar foo extends bar` we integrate the compiled value of bar within the compiled value of foo rather than 'foo' referring to 'bar' by name. This simplifies module-layer imports, exports, and local reasoning. But it limits overrides to lower and higher layers: methods in a grammar, or modules in a distribution.

        import Module                   # toplevel import
        from Module import ImportList   # explicit import
        import Module as LocalName      # qualified import
        Definitions
        export ImportList

To avoid ambiguity, I propose we limit modules to a single toplevel import followed by any number of explicit and qualified imports, followed by module definitions. Further, we forbid shadowing of anything that is previously defined or assumed. We might extend the only toplevel import with an optional ImportList to support aliasing and assertions. For example, `import my-module with bar, foo as prior-foo` would import everything from my-module but would assert 'bar' is defined and rename 'foo' to 'prior-foo'.

Definitions within a grammar-logic module would include grammars of various forms (including mixins or interfaces) and other program fragments that we might find reusable (such as macros or embedded DSLs, renames). However, anything requiring compile-time eval should be deferred until after bootstrap.

### Incremental Compilation

The module layer compiles grammars to an AST or intermediate language. A lot of processing is still needed: partial evaluation, verification of assertions, analysis of types, translation to machine code and accelerated representations, compression of similar definitions, and so on. Ideally, all of these steps are 'incremental' in context of persistent memoization tables, such that a change to a source file rarely requires rebuilding an entire application. But how do we design for incremental compilation?

Identifying 'cliques' - subsets of mutually recursive definitions - is likely an important part of incremental compilation. Additionally, we might need to rely on secure-hash content addressing instead of allocation of anonymous namespaces. 


