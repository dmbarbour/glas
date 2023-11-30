# Programming with Grammars and Logic

## Overview

Grammars and logic are similar in semantics but tend to present distinct HCI and user experience in practice. Grammar-logic programming can be viewed as an experiment to make both more accessible and usable. I additionally draw inspiration from effect systems in FP, and inheritance-based extension in OOP. 

A grammar represents a set of values in a manner that ideally supports efficient recognition and generation of values. A grammar can easily represent a 'function' as a relation, a set of pairs. Computation is based on partial matching of values returning variables. For example, given a grammar representing `{("roses", "red"), ("apples", "red"), ("violets", "blue")}`, we could match `(Things, "red")` and get the answer set `{(Things="roses"), (Things="apples")}`, running the function backwards. In the general case, there might be more than one variable in each answer.

Logic programming reverse implication clauses `Proposition :- Derivation` can be represented in grammars with shared variables and guarded patterns. Shared variables must generate the same value at every location. Guarded patterns might have the form `Pattern when Guard` or `Pattern unless Guard`, allowing the Guard to constrain variables while remaining detached from the computed result.

Pattern-matching with grammars often represent 'recursion' on the pattern side. Within a rule such as `"while" Cond:C "do" Block:B -> loop:(while:C, do:B)` variables `C` and `B` might conveniently refer to computed AST values. We might view `->` as a syntactic sugar to make grammars more usable. The aforementioned example might desugar to a relation `("while" (V1 when (V1,C) in Cond) "do" (V2 when (V2,B) in Block), loop:(while:C, do:B))`, assuming 'Cond' and 'Block' also represent relations.

Interactive computation is implicit when two or more grammars constrain each other, but we can make it explicit. For example, a procedural 'interaction trace' might be modeled as `(args:Arguments, eff:[(Request1, Response1), (Request2, Response2), ...], return:Result)`. A 'procedure' can then be represented as a grammar that generates a trace by reading Arguments, writing Requests, reading Responses, and writing a Result. Similarly, the 'caller' would write Arguments and read Result. A 'effects handler' would read Requests and write Responses. In context of grammars, we can understand 'reading' as matching a wide range of values, and 'writing' as generating a small range of values. It is feasible to adapt [session types](https://en.wikipedia.org/wiki/Session_type) to grammars to precisely control interactions. 

A runtime can bind interactions to the real-world. However, potential backtracking constrains the effects API and interferes with long-running computations. The [transaction loop application model](GlasApps.md) is a good match for grammar-logic programming, with backtracking also aborting associated effects. Alternatively, it is feasible to prove that certain programs do not backtrack - a 'no fail context' - in which case we could use more conventional effects.

A gramar-logic language can modify the 'syntactic sugar' for functions (`->`) to represent interaction traces instead of relations. This would significantly enhance extensibility and expressiveness of the language, reducing need for programmers to manually thread interaction variables through a program. But procedural request-response is awkward for modeling concurrent interaction, so I'm exploring alternatives.

Grammar-logic languges have potential to be modular and extensible. OOP solved the problem of extending systems of mutually recursive definitions via inheritance and mixin patterns. A grammar-logic language can build upon the idea of grammar 'classes' that implement named grammars, enabling extension or override or of specific elements. Alessandro Warth's [OMeta](https://en.wikipedia.org/wiki/OMeta) develops this idea effectively.

## Brainstorming

### Ordered Choice 

We can easily model ordered choice in terms of unordered choice and pattern guards.

        P or-else Q             =>      P or (Q unless P)
        if C then P else Q      =>      (P when C) or (Q unless C)

The if-then-else form is more general and more widely useful. We could extend this to pattern matching functions without much difficulty. 

Rewriting from ordered to unordered will hinder optimizations, so it's better to have ordered choice as a built-in. Also, ordered choice cannot represent unordered choice, so it provides a decent basis for deterministic computation.

*Todo:* Prove or-else is associative and idempotent. 

### Deterministic Functions

Deterministic computations are useful in many contexts. I propose to design my grammar-logic language such that grammars always represent deterministic functions. That is, results are fully deterministic up to 'inputs', whether those inputs are initial arguments or deferred via channels. 

This will limit how we introduce variables to something closer to functional languages. Though, support for second-class channels can still model deferred variable assignments.

Non-deterministic computations can still be modeled contextually by running a function with partial inputs, or even running backwards. The right-hand side of a match must still be valid 'grammar' pattern. 

*Aside:* In special cases, it might be feasible to also take advantage of 'confluent' computations. But I don't know how to leverage this outside of accelerated functions. 

### Extensions

The simplest extension is similar to OOP single inheritance.

        grammar foo extends bar with
            integer = ... | prior.integer

Here 'prior.integer' refers to bar's definition of integer, albeit with any reference to 'integer' within that definition referring instead to foo's definition. This allows foo to flexibly extend or override bar's original definitions. Of course, syntax for 'prior.' may vary. Any words not explicitly defined in 'foo' would be inherited from 'bar'. 

Multiple inheritance can be modeled in terms of templated single inheritance. I don't intend to support templates in general, but it seems useful to explain my meaning here. 

        mixin foo with ... => grammar foo<G> extends G with ...
        mixin foo extends bar with ... => grammar foo<G> extends bar<G> with ...
        mixin foo extends bar . baz with ... => grammar foo<G> extends bar<baz<G>> with ...

Grammars and mixins could use the same underlying model to allow consistent '.' composition. However, to mitigate [the diamond inheritance ambiguity problem](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem) I'd need to indicate that certain names should be undefined (or identical) in the G parameter.

*Aside:* Many OO languages support a 'final' annotation, to express that certain methods should not be overridden. This is primarily motivated by performance in context of separate compilation. But it's also useful to simplify assumptions for local reasoning. Some form of 'final' annotation might be useful in our namespace model.

### Hierarchical Namespaces

In some cases, I want to work with more than one version of a grammar.

One possibility is to develop a concise syntax for shifting a grammar into a subordinate namespace. We could specify that 'foo in f' means we add a prefix 'f/' to every name defined and used in grammar 'foo' (not affecting the mixin parameter). With this, we could model a grammar containing a component as something like the following:

        grammar foo extends (bar in b) with
            b/integer = int
            int = ... | prior.b/integer

Obviously the syntax needs serious work here! The name 'prior.b/integer' is verbose and ugly. Adding a dozen '(bar in b)' extensions in the header line would quickly grow out of control. A potentially useful observation is that `(bar in b) . (baz in b)` should be equivalent to `((bar . baz) in b)`, and `(bar in b) . (baz in z)` should commute.

### Anonymous Namespaces

An anonymous namespace can conveniently be represented as a standard prefix on names, such as '~'. Use of a special prefix reduces risk of name shadowing, i.e. there should be no public names that start with '~'. An anonymous namespace can serve as a private scratch space for intermediate utility definitions.

We might implement private scope as rewriting '~' to a freshly allocated anonymous prefix. This is easiest if the language also reserves a prefix for all compiler-provided definitions, perhaps '\_'. Then '~' might be rewritten to `_anon123/` or similar. 

We can apply '~' to hierarchical namespaces, not just individual definitions. This does increase risk of repetition, with nearly identical anonymous namespaces found in multiple grammars. A compiler could later mitigate this by combining or compressing common definitions.

### Renaming 

Renaming individual symbols is useful for adapting a grammar to a context, or eliminating conflicts in context of multiple inheritance. 

Renames apply to the grammar or mixin definition. For example, if we apply rename 'int => integer' to a mixin, then the resulting mixin will override 'integer' and would neither reference nor modify 'int'. The compiler would know to also rename associated 'anno/int/' to 'anno/integer/', in case the mixin defines annotations, and to make private any symbols renamed to start with '~'. Rename operations can be defined and composed separately from grammars.

This design is consistent with hierarchical and anonymous namespaces, which are also scoped to grammar definitions. This consistency, and the precise scope of renames, can help prevent subtle surprises.

### Channel Based Interactions

My initial thought is to model interactions around channels. This gives me many features I want - compositionality, simplicity, and scalability. Extensibility is hindered by linear ownership of channels, but this can be mitigated by modeling a databus or other extensible architecture.

Channels can be modeled as partial lists, with the writer holding a cursor at the 'tail' of the list, and the reader holding the 'head'. To write a channel, we unify the tail variable with a pair, where the second element is the new tail. Then we update the local cursor to the new tail, ensuring we only write each location once. A written channel may be 'closed' by unifying with the empty list. (Closing a channel can be implicit based on scope.)

Basic channels can only support a single writer. Channels can be extended with *temporal semantics* to support merging of events from multiple writers. Alternatively, with runtime support we can introduce non-deterministic merge via *reflective race conditions*. Either mechanism would allow modeling a router, databus, or publish-subscribe. 

Channels aren't limited to sending data, they can also transfer channels. We can build useful patterns around this, such as modeling objects or remote functions. We can also simplify by organizing channels into duplex pairs or hierarchical bundles. Potentially, we could extend these 'bundles' to support all common interaction structures (e.g. also including pass-by-reference variables).

An inherent risk with channels is potential deadlock where multiple channels are waiting on each other in a cycle. This can potentially be mitigated by lazy reads (aka ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot) in context of lazy evaluation). Or it could be avoided using [session types](https://en.wikipedia.org/wiki/Session_type) and static analysis.

### Channel-Based Objects and Functions

An object can be modeled as a process that accepts a subchannel for each 'method call'. The object reference would be the channel that delivers the method calls. The separate subchannel per call ensures responses are properly routed back to the specific caller, in case the reference is shared. (An object reference might be shared via *Temporal Semantics* or *Reflective Race Conditions*.)

Functions can be modeled similarly as a *stateless* object. If the runtime knows the object is stateless, it can optimize method calls to evaluate in parallel, interactions may be forgotten because they don't affect any external state, and the channel may be freely copied because there is no need to track order of writes. Effectively, the channel serves as a first-class function (albeit with restrictions on how it is passed around or observed).

Ideally, grammar methods and object methods via channels would have a consistent syntax and underlying semantics.

### Temporal Semantics? Tentative.

Temporal semantics support deterministic merge of asynchronous events. This would allow modeling a databus or a publish-subscribe system simulate timeouts within a pure function. Without temporal semantics, we can abandon either deterministic merge (see *Reflective Race Conditions*) or asynchronous events (cf. [*Synchronous Reactive Programming*](https://en.wikipedia.org/wiki/Synchronous_programming_language)).

Temporal semantics can be implemented by introducing time step messages, adding a mutable time variable to every procedure and channel read cursor, and some extra processing. 

While reading, when process time is greater than or equal to channel time, we handle time step messages to advance the channel's time variable. Otherwise reads behave same as atemporal reads, waiting on future messages. If process time is less than channel time, read fails immediately. The process may observe a "wait for later" status. A process may explicitly 'wait' on multiple channels. Waiting will send time step messages to all writable channels held by the process. In practice, language support is required to precisely track held channels. 

When a new channel pair is initialized, a logical latency value can be introduced representing the time it takes a message to transfer. This can be leveraged to model timeouts, i.e. create the channel just so we may wait on it.

Temporal semantics can be mapped to real-world effects. Even with transaction loop applications, where transactions are logically instantaneous, we could treat each transaction as committing to a schedule of programmed events, mapping each time step to a nanosecond after an idealized time of commit. This future schedule would be most useful in time-sensitive domains such as music or robotics. Or time could be ignored beyond ordering effects - this would need to be documented as part of the effects API, and consistent.

Although there are many benefits, I hesitate because temporal semantics adds considerable complexity to the language. For example, we'll need to track which channels are held so we can write the time steps. For example, we'll need to more explicitly represent concurrent operations (fork or fork-join) because otherwise they'd use the same process time variable. My intention is to give this a try, and find out if it's as big a mess as I fear.

### Reflective Race Conditions? As Effect.

A runtime can provide effects (via 'io') for copying channels that reflect on runtime state to decide order of events. This allows observation of 'race conditions'. Observing race conditions trades predictability for performance. Reflective Race Conditions can be used in context of temporal semantics. In this case, non-deterministic merge could be limited to within each time step, or perhaps a slightly larger bounded window of K time steps.

### Bundling of Channels or Arguments

Duplex channels can be modeled as a simple read-write pair of cursors, `(r:RC, w:WC)`. A simplex channel could then be modeled as only half a duplex pair, such as `r:RC`, or as a duplex pair where one end or the other is explicitly 'closed'. 

We could extend bundles to named channels. In this case, we might use `d:(foo:(...), bar:(...))` or similar, where `foo` and `bar` may each contain the `r`, `w`, and even another `d` to allow hierarchical dictionaries. Reading channel `foo.baz` may translate to operating on `d:foo:d:baz:r`. (A compiler might further translate to offsets in a struct.)

Bundles can feasibly be extended with additional interaction models and common argument patterns for method calls. For example, pass-by-reference variables could be modeled as specialized channels, and simple data parameters could be understood as specialized read-only data channels. We could make 'bundle' the primary message type for both channels and grammar method calls, providing a basis for consistent interactions.

### Pubsub Based Interactions? Tentative.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. 

Writing to a partial set is commutative and idempotent. This is very convenient, avoiding the ordering and ownership constraints of writing to a channel. However, reading a partial set is more complicated because (in context of deterministic computations) we must not accidentally *observe* write-order. Consequently, 'read' operations on partial sets are continuous and limited to commutative, idempotent effects - such as writing to a partial set. This results in a very declarative and reactive style, similar to publish-subscribe or 'pubsub'.

Many real-world effects can be adapted, assuming we're willing to abort in case of multi-writer conflicts. For example, we can model a set of buffered writes or patches to a file. There is no problem if the patches do not overlap, or if overlaps are consistent (e.g. based on a partial ordering or priority of patches). [Conflict-free replicated datatypes (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are applicable.

Just as channels may transfer channels, partial sets may carry partial sets. This supports a useful request-response pattern: one program writes a request and includes a 'new' partial set just to receive the response. Of course, this response may include a new space for writing another response, allowing multi-step protocols. Like duplex channels, we could structurally distinguish 'read sets' and 'write sets' to control direction of dataflow. The reader of the request writes any response into the provided partial set. This ensures the response is correctly routed back to the writer. 

A reader can potentially read multiple partial sets. This effectively performs a relational join, albeit with fine-grained conditional logic regarding which sets to read and filter. Keeping the sets relatively 'small' will be important for performance in many cases. Intriguingly, a compiler or runtime can potentially coalesce computations that read or observe the same data (even from different sets) and automatically distribute writes to multiple response sites.

Pubsub can be adapted to *temporal semantics*. One major benefit of doing so is flexible integration with conventional, stateful computations. For example, channel-based processes could be provided a limited API to access the pubsub layer, with ability to configure published values over time and observe complete responses with a little latency. Supporting both pubsub and channels in one language may involve specialized sublanguages with careful interaction points.

*Note 1:* It is possible to model publish-subscribe systems using channel streams. But partial sets can avoid accidental complexity from ordering and repetition. Further, many potential optimizations depending on commutativity and idempotence are accessible, based robustly on program structure rather than ad-hoc proof.

*Note 2:* Modeling partial sets in glas is non-trivial. But if we don't care about efficiency, we can encode values into bitstrings then model the set of bitstrings as a partial radix tree. To support pubsub, I assume partial sets are abstracted by the compiler and runtime, observed and manipulated only through keywords or built-in functions.

### Staged Programming

A staged program returns the next program, to be evaluated in a future context. Staged programming can be supported explicitly via first-class objects or closures, e.g. a two-stage function has type `A -> (B, C -> D)`. I think this would also work for most grammar-logic languages.

In some cases, it would be useful to represent the staged program as a value. This might involve a decompiler option, something to extract the generated function as a grammar. A similar feature would be useful in most languages.

Without first class functions or a decompiler, the remaining option is to manually build a value representing the next stage program. This option is effective, but integration can be awkward. For example, behavior shared between stages must be represented twice. Also, many host language features for extensibility or reasoning won't apply to the generated program.

### No-Fail Contexts? Defer.

It is possible to systemically catch failures locally then convert them into failure results, such that "computation halted in a failure state after some request-response steps" is a valid result in the grammar. This would more closely model conventional procedural programming where there is no backtracking on failure, only exception handling or a query to the environment for advice.

It is feasible to design a program structure around no-fail contexts, where only no-fail operations may be directly executed and any possibly-failing operation must be explicitly lifted so it cannot propagate failure. We can also add some types, such that certain channels may only be passed into no-fail contexts. This way, we know those channels are never backtracked.

Later, we can develop no-fail effects APIs for our no-fail applications. No-fail effects could directly call the OS or a foreign function without need to defer effects until 'commit' or track information for robust 'undo'. The application could potentially provide its own runtime and backtracking.

This seems like a worthy pursuit. But it's also very low priority. And I suspect we'd need at least some static analysis to make it safely usable in practice.

### Type Safety

It is feasible to augment methods with type annotations, which describe:

* the data types for input and return values
* the effects protocol used via 'io' channel
* effects protocols for other bound channels
* if method needs no-fail context assumption

Protocols can potentially build on the notion of session types. Session types would be adapted to work within the consistency constraints for grammar methods and limits of duplex channels and the need for implicit 'io' and 'env' arguments. That is, it might not be as powerful as session types in general, so long as it's expressive enough for our purposes.

Partial evaluation is implicit insofar as a program produces partial outputs as a consequence of receiving partial inputs. Logic unification variables and channel-based interaction are very convenient for partial evaluation.

Session types can help make partial evaluation 'robust' by describing assumptions in a machine-checkable format, and enabling analysis of potential datalock.

### Default Definitions? Tentative.

Support for 'default' definitions is convenient in some cases. A default definition may override or extend another default definition, but it won't override normal definitions. We could model this as a priority on definitions. We could also model 'final' definitions as a priority: it's an error to even try to override a final definition with a normal or final definition, but a default definition would be ignored.

I don't want subtle, fine-grained priorities. But if we limit to just a few priorities, I think that's acceptable. We might also add 'abstract' definitions as a priority.

### Weighted Grammars and Search

Methods can be instrumented with annotations to generate numbers estimating 'cost' and 'fitness' and other ad-hoc numeric attributes. A runtime can implicitly compute and total these values, and use them heuristically to guide search in context of non-deterministic computations. This isn't a perfect solution, but I think it might be adequate for many use cases.

Because this can build on annotations, no special semantics are needed. But dedicated syntax can make this feature more accessible.

### Annotations Namespace

One approach to annotate 'foo' with an documentation is to define 'anno/foo/docs'.

This approach is simple and has many nice properties. The annotations are easily extended or abstracted. They don't require unique syntax. Tools may warn when annotations are referenced from outside the 'anno' namespace or have unexpected types. With syntax aware of this convention, it is easy to automatically rename 'anno/foo/' when we rename 'foo'.

I think this can be used for most cases where we want annotations on methods. 

### Static Assertions

A static assertions is a computation that should return 'true'. In context of grammar-logic, assertions can be modeled as methods where the system searches for an input that leads to computing a valid outcome. This is very expressive, capable of representing searches, negative assertions, and individual tests.

I propose to express assertions as normal definitions with simple namespace conventions.

        anno/foo/assert/basic-test = ...
        assert/foo-bar-integration = ...

A compiler may heuristically filter which assertions to test based on naming and transitive dependencies of the application. For example, if we use word 'a/foo' then we might test all assertions in 'a/assert/' plus all those in 'a/anno/foo/assert/'. 

### Module Structure

A glas module produces a dictionary of grammars. Each grammar is represented as a value (perhaps 'gram' header). There is no reference between grammars by name - such references are inlined by the compiler. Only names *within* grammars are mutually recursive or subject to extension. In addition to grammars, modules may include definition of reusable rename operations (perhaps 'alias' header).

Modules may import other modules, which are assumed to have the same structure. There may be some lightweight support for  aliasing names on import (`from xyzzy import foo, bar as baz, ...`) and qualified names `import xyzzy as x` and export lists, and to `open` a single module (avoiding ambiguous top-level names). The g0 syntax can be adopted here almost directly.

There are no top-level assertions, macros, or data definitions. A compiler may optionally perform some evaluation, e.g. to optimize based on final or private methods, but 'eval' is not *required* at this stage.

The compiler should aggressively warn for name shadowing, and may require explicit 'abstract' declarations for any undefined methods used in a definition. I would also like to support type annotations early on, with partial type descriptions that can usefully be refined (e.g. by unification).

### Application Model

As a convention, if a grammar namespace is referenced as a function, we may assume method 'main'. 

If present, the glas command line interface also evaluates 'anno/main/run-mode' to select an alternative run mode or provide runtime configuration options. Staged programs, such as selecting or configuring a program based on command line and environment parameters, can be supported via run mode.

### Acceleration

It is possible to support some accelerated functions in context of grammar-logic languages. This can be expressed using 'accel' annotations, which the runtime knows to check.

        anno/list-append/accel = ...

However, acceleration of higher-order functions is difficult to express. Also, we cannot necessarily accelerate running a function backwards or with partial inputs. Thus, relying on acceleration may hinder use of programs for non-deterministic search.

## Namespace Builder

I want an AST for namespaces that efficiently compiles to a 'flat' dictionary with (rn:renames, def:definition) pairs. This could be followed by a function to apply renames to definitions. To keep it simple, the compiler may reserve a prefix for internal use; can easily raise an error if users attempt to define a reserved symbol.

* *rename:(in:NS, map:\[(Prefix1, Prefix2), ...\])* - rename multiple names within NS. This also renames them within definitions in NS. Initial Prefixes must not overlap. Final prefixes may overlap, but it's an error if two defined names are combined. 

 It's an error if any two initial prefixes overlap, or if any two target prefixes result in a name collision. 
* *scope:(in:NS, hide:\[Prefix1, Prefix2, ...\])* - move all names that start with the given prefixes into a new, anonymous namespace.

TODO:

* *define* new words or overrides, should specify an extra prefix to locally refer to 'prior' definitions. Prior definitions are moved into a new anonymous namespace, then the given prefix is redirected to that namespace.
* *initial* definitions - assert certain words are undefined (or only have default or abstract definitions). This cannot be a normal priority because it needs to wrap the grammar body *and* its extensions.
* *final* and *default* definitions - likely represent as a priority on definitions. We could omit the priority field for normal definitions.
* *abstract* definitions - possibly model as a priority, if only to guard against renames merging abstract names.
* *compose* namespace builders - apply namespace operations sequentially.


*Aside:* I could add explicit operations to move or undefine words, but I probably don't need them. No need to maximally generalize namespace operators. Provide what is needed, no more.

## Grammar Methods


## Misc

### Channels

### Method Interactions

### Method Protocols?

### Pass by Reference Parameters


