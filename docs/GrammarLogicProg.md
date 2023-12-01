# Programming with Grammars and Logic

## Overview

Grammars and logic are similar in underlying semantics. Dataflow is based around [logic unification](https://en.wikipedia.org/wiki/Unification_(computer_science)#Application:_unification_in_logic_programming). Computation can 'run backwards', searching for inputs that lead to a specified output. However, logic programming usually has the form `Proposition :- Derivation`. This is awkward for many use cases, relevantly including the modeling of effects. Grammars are rarely explored as a complete basis for programming. I occasionally see `Rule = Pattern -> Action` inspired from grammars (for example Alessandro Warth's [OMeta](https://en.wikipedia.org/wiki/OMeta)), but the right hand action is rarely modeled as a proper part of the grammar, much less any side-effects.

Grammars trivially include logic programs the moment we introduce *guard patterns* - 'P when G' generates (or recognizes) a subset of values in P where G also generates a value. P and G typically share some variables, thus requiring G to search for at least one value that shares elements with P. Negative guards are also possible, 'P unless G'. 

Grammars can trivially model pure functions. A grammar generates or recognizes a set of values. Pure functions can be modeled as a set of ordered pairs, where no two distinct pairs have the same first element. We only need a little syntax to leverage this. 

Intriguingly, grammars can also model interactive computations. For example, instead of an ordered pair, we could generate a simplistic procedural trace of form `(arg:A, eff:[(Request1, Response1), (Request2, Response2)], return:R)`. We can view a procedure call as an interaction between two grammars - one representing the procedure, the other the caller. The caller writes the argument, reads requests, writes responses, then reads the result. The procedure inverts read and write. We can design our language such that read and write responsibilities are clear, or leverage [session types](https://en.wikipedia.org/wiki/Session_type) to typefully represent such responsibilities.

A runtime can bind effects to the real world. However, due to potential failure and backtracking, we should limit the API to requests that can be deferred, undone, or safely ignored. The [transaction loop application model](GlasApps.md) is a good fit. Alternatively, if we prove backtracking is unnecessary in a subset of grammars (e.g. because failures are converted to error results) then we could support a more direct effects API.

We aren't limited to procedural traces. We could instead model interactions around channels or pubsub, gaining significant benefits for scalability and compositionality. Channels and pubsub can be further enhanced and integrated via temporal semantics. 

As an independent point, grammars can benefit from OOP-inspired inheritance and extensibility. For example, we might tweak an existing programming language by extending the parse rules for integers and loops.

## Why Another Language?

A grammar-logic language should get me closer to immediately developing user-defined languages and completing bootstrap. Logic unification variables can provide more scalable foundation for effects. Ability to compute functions 'backwards' is convenient for a number of ideas - constraint based search, property testing, debugging and explaining computations.

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

*Aside:* Many OO languages support a 'final' annotation, to express that certain methods should not be overridden. I have mixed feelings about this. Why arbitrarily restrict extension? Some of the motives, such as partial evaluation during separate compilation, seem less applicable to my grammar-logic language in glas.

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

### Translation 

Translation (or rename) operations apply to a grammar or mixin definition. For example, if we translate 'int -> integer' in a mixin, then the translated mixin will override 'integer' in the target namespace, and leave 'int' alone. This design is consistent with hierarchical and anonymous namespaces, which are also scoped to grammar or mixin definitions. 

To support reuse and refactoring, renames can be defined independently of grammars.  

        translation aleph is 
            int -> integer
            xyzzy -> fizzbuzz
            fizzbuzz -> xyzzy

        grammar foo = aleph(bar) . baz

Multiple symbols may be renamed simultaneously. This allows for 'cyclic' renames, such as swapping 'xyzzy' and 'fizzbuzz' in the example above. However, it is an error if a rename accidentally combines two words. Thus, in the example above, it would be an error if 'integer' is already defined or used within grammar bar. A translation collision error would ideally be caught early, at module compile time, but it can also be caught later.

We could also define translations by composition, or apply a hierarchical namespace to a translation, or translate some names into private spaces 'int -> ~int', or rename hierarchical namespaces 'm/ -> math/'. These don't require much extra effort from the compiler.

### Grammar Interfaces

We might also benefit from something like an 'interface' or 'mask' where we explicitly list only the names we want to expose, and everything not exposed is moved into a fresh anonymous namespace. This is much simpler than a translation, and it fills a useful role similar to export lists. 

### Channel Based Interactions

My initial thought is to model interactions around channels. This gives me many features I want - compositionality, simplicity, and scalability. Extensibility is hindered by linear ownership of channels, but this can be mitigated by modeling a databus or other extensible architecture.

Channels can be modeled as partial lists, with the writer holding a cursor at the 'tail' of the list, and the reader holding the 'head'. To write a channel, we unify the tail variable with a pair, where the second element is the new tail. Then we update the local cursor to the new tail, ensuring we only write each location once. A written channel may be 'closed' by unifying with the empty list. (Closing a channel can be implicit based on scope.)

Channels aren't limited to moving data, they can also transfer channels. We can build useful patterns around this, modeling objects or remote functions or a TCP listener as a channel of channels. 

However, in context of deterministic computation, channels are essentially linear types. If there are two writers, writes will conflict. And even reading a channel must be linear if the reader might receive a writable channel. This can be mitigated - we can use *temporal semantics* to deterministically merge asynchronous writes. Or we could support non-deterministic merge based on arrival-order with runtime support (see *reflective race conditions*). With the ability to merge asynchronous events, channels can model openly extensible systems, such as a databus or router.

An inherent risk with channels is potential deadlock, with multiple channels are waiting on each other in a cycle. This can potentially be mitigated by lazy reads (aka ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot) in context of lazy evaluation). Or it could be avoided using [session types](https://en.wikipedia.org/wiki/Session_type) and static analysis.

### Channel-Based Objects and Functions

An object can be modeled as a process that accepts a subchannel for each 'method call'. The object reference would be the channel that delivers the method calls. The separate subchannel per call ensures responses are properly routed back to the specific caller, in case the reference is shared. (An object reference might be shared via *Temporal Semantics* or *Reflective Race Conditions*.)

Functions can be modeled similarly as a *stateless* object. If the runtime knows the object is stateless, it can optimize method calls to evaluate in parallel, interactions may be forgotten because they don't affect any external state, and the channel may be freely copied because there is no need to track order of writes. Effectively, the channel serves as a first-class function (albeit with restrictions on how it is passed around or observed).

Ideally, grammar methods and object methods via channels would have a consistent syntax and underlying semantics.

#### Temporal Semantics? Tentative.

Temporal semantics support deterministic merge of asynchronous events. This would allow modeling a databus or a publish-subscribe system simulate timeouts within a pure function. Without temporal semantics, we can abandon either deterministic merge (see *Reflective Race Conditions*) or asynchronous events (cf. [*Synchronous Reactive Programming*](https://en.wikipedia.org/wiki/Synchronous_programming_language)).

Temporal semantics can be implemented by introducing time step messages, adding a mutable time variable to every procedure and channel read cursor, and some extra processing. 

While reading, when process time is greater than or equal to channel time, we handle time step messages to advance the channel's time variable. Otherwise reads behave same as atemporal reads, waiting on future messages. If process time is less than channel time, read fails immediately. The process may observe a "wait for later" status. A process may explicitly 'wait' on multiple channels. Waiting will send time step messages to all writable channels held by the process. In practice, language support is required to precisely track held channels. 

When a new channel pair is initialized, a logical latency value can be introduced representing the time it takes a message to transfer. This can be leveraged to model timeouts, i.e. create the channel just so we may wait on it.

Temporal semantics can be mapped to real-world effects. Even with transaction loop applications, where transactions are logically instantaneous, we could treat each transaction as committing to a schedule of programmed events, mapping each time step to a nanosecond after an idealized time of commit. This future schedule would be most useful in time-sensitive domains such as music or robotics. Or time could be ignored beyond ordering effects - this would need to be documented as part of the effects API, and consistent.

Although there are many benefits, I hesitate because temporal semantics adds considerable complexity to the language. For example, we'll need to track which channels are held so we can write the time steps. For example, we'll need to more explicitly represent concurrent operations (fork or fork-join) because otherwise they'd use the same process time variable. My intention is to give this a try, and find out if it's as big a mess as I fear.

#### Reflective Race Conditions? Tentative. As Effect.

A runtime can merge events from multiple channels non-deterministically based on arrival order, aka race conditions. This is a potential alternative to temporal semantics, and is much easier to implement efficiently. 

Race conditions impose significant predictability costs. Arrival order is heavily influenced by context: parallel processing, memory cache sizes, compiler or runtime version, etc.. Applications that are developed and tested in context of observable race conditions often encounter undiscovered bugs when ported to new machines. Race conditions are worse than the normal challenges with non-determinism.

Predictability can be mitigated if we control where race conditions are introduced, limit it to clear boundaries. But I think it might be better to develop temporal semantics.

*Note:* It is feasible to combine race conditions with temporal semantics, i.e. race conditions within each time step. But I'm not aware of any strong use case, and I'm not convinced it's a good idea.

#### Bundling of Channels or Arguments

Duplex channels can be modeled as a simple read-write pair of cursors, `(r:RC, w:WC)`. A simplex channel could then be modeled as only half a duplex pair, such as `r:RC`, or as a duplex pair where one end or the other is explicitly 'closed'. 

We could extend bundles to named channels. In this case, we might use `d:(foo:(...), bar:(...))` or similar, where `foo` and `bar` may each contain the `r`, `w`, and even another `d` to allow hierarchical dictionaries. Reading channel `foo.baz` may translate to operating on `d:foo:d:baz:r`. (A compiler might further translate to offsets in a struct.)

Bundles can feasibly be extended with additional interaction models and common argument patterns for method calls. For example, pass-by-reference variables could be modeled as specialized channels, and simple data parameters could be understood as specialized read-only data channels. We could make 'bundle' the primary message type for both channels and grammar method calls, providing a basis for consistent interactions.

*Note:* Above, I assume structural protection of abstractions. That is, the language is aware of bundle structures and knows that 'r' values represents a read-only channels. The syntax would ensure correct use of 'r'. This is essentially a lightweight type system. With a proper type system, bundles could be simplified.

#### Broadcast Channels? As Optimization.

A broadcast channel is a read channel that receives only perfectly copyable content, such as data or channel-based functions. A broadcast channel is also perfectly copyable: no need temporal semantics or non-deterministic race conditions. This is very convenient for the common one-to-many broadcast pattern.

A compiler or runtime could recognize broadcast channels and use a more efficient copy mechanism. This recognition can be supported via static or dynamic types. In case of dynamic types, we may need to explicitly declare broadcast channels when we first create them (to reject non-copyable writes), and we might generalize 'simple copy' to be a flag on bundles.

*Aside:* Programmers should also be given some tools to verify their performance assumptions. 

### Pubsub Set Based Interactions? Defer.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. 

Writing to a partial set is commutative and idempotent. These are very convenient properties. However, reading a partial set is relatively complicated because, at least in context of deterministic computation, we must not accidentally *observe* write-order. 

Consequently, read operations are continuous: there is no order, no first, thus cannot read first three items then stop. Instead, read would fork the computation, with each fork operating concurrently on one element of the set. Sequential reads would effectively model relational joins. And the reader is also limited to commutative, idempotent effects, such as writing to a partial set. These constraints on readers result in a very declarative, reactive programming style. Indeed, it's similar to publish-subscribe systems, so I've decided to call this 'pubsub sets'.

Just as channels may transfer channels, partial sets may carry partial sets. This supports a useful request-response pattern: one program writes a request and includes a 'new' partial set just to receive the response. This response may include data and additional partial sets for further interaction. Also similar to channels, to structurally protect abstraction and dataflow, the grammar-logic language could use hierarchical 'bundles' of partial sets and data as the primary message type.

This request-response pattern can serve as a basis for function passing and integration of real-world effects. Many effects can be adapted. For example, although we cannot directly model streaming files, we could model requests to read file regions or to apply patches later. And we could potentially even abstract this to look like a stream.

Pubsub can potentially be adapted to *temporal semantics*. If successfully adapted, this should simplify integration with channel-based processes, which could maintain 'publish set' variables over time and read historical values of sets. However, there are still some theoretical and logistical challenges I'm working out, such as how to recognize when a process is done writing to a partial set. 

The idea needs more work. However, I think it's a very promising direction to pursue, and a good fit for my vision of glas systems. Meanwhile, we can still model a broadcast or databus via channels.

*Note:* Modeling partial sets in glas is non-trivial. But if we don't care about efficiency, we can encode values into bitstrings then model the set of bitstrings as a partial radix tree. To support pubsub, I assume partial sets are abstracted by the compiler and runtime, observed and manipulated only through keywords or built-in functions.

### Staged Programming

A staged program returns the next program, to be evaluated in a future context. Staged programming can be supported explicitly via first-class objects or closures, e.g. a two-stage function has type `A -> (B, C -> D)`. I think this would also work for most grammar-logic languages.

In some cases, it would be useful to represent the staged program as a value. This might involve a decompiler option, something to extract the generated function as a grammar. A similar feature would be useful in most languages.

Without first class functions or a decompiler, the remaining option is to manually build a value representing the next stage program. This option is effective, but integration can be awkward. For example, behavior shared between stages must be represented twice. Also, many host language features for extensibility or reasoning won't apply to the generated program.

### No-Fail Contexts? Defer.

We can develop an effects API that may only be called from a no-fail (or no-backsies) context, i.e. where we can guarantee there is no backtracking after the effect. This would support a more direct API, with direct OS calls or FFI. This isn't even very difficult - we could introduce a few simple types to track no-fail method calls and no-fail API channels, then restrict (via static analysis) where the no-fail API channel is written.

This seems like a useful feature to develop later, but I also feel it's relatively low priority.

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

One approach to annotate 'foo' with documentation is to define associated words. For example, we could define 'anno/foo/docs' to a value representing documentation for 'foo'. This approach is simple and has many nice properties. Annotations are easily extended or abstracted. They don't require unique syntax. Tools may warn if annotations are referenced from outside the 'anno' namespace, or if recognized annotations have non-standard types. A compiler can implicitly apply translations to annotations. 

I think this can be used for most cases where we want annotations on methods. 

### Static Assertions

A static assertions is a computation that should return 'true'. In context of grammar-logic, assertions can be modeled as methods where the system searches for an input that leads to computing a valid outcome. This is very expressive, capable of representing searches, negative assertions, and individual tests.

We could support assertions as a conventional use of annotations or namespaces 

        anno/foo/assert/basic-test = ...
        assert/foo-bar-integration = ...

A compiler may heuristically filter which assertions to test based on naming and transitive dependencies of the application. For example, if we use word 'a/foo' then we might test all assertions in 'a/assert/' plus all those in 'a/anno/foo/assert/'. 

### Module Structure

Modules may start with one `open Module` import, providing the initial namespace, then import individual definitions from other modules with optional aliasing (via `from ./xyzzy import foo, bar as baz, ...`). There are no qualified imports or hierarchical names at this layer. Modules may specify an optional export list. By default, everything is exported. 

Modules additionally define grammars and mixins, translations and interfaces. The compiler eliminates references between modules-layer definitions via inling or application; compiled grammar definitions are independent. However, most code should be at the grammar-layer. Grammars can be understood as a more extensible and composable modular form.

There are no top-level assertions, macros, or data definitions. Nothing that might require a full 'eval' by the module-level compiler.

### Application Model

As a convention, if a grammar namespace is referenced as a function, we may assume method 'main'. If present, the glas command line interface also evaluates a 'run-mode' annotation on 'main'. This can specify alternative run modes, or provide some extra parameters to the default run mode. Staged programs, such as selecting or configuring a program based on command line and environment parameters, can be supported via run mode.

### Acceleration

It is possible to support some accelerated functions in context of grammar-logic languages. This can be expressed using 'accel' annotations, which the runtime or compiler would know to check.

However, acceleration of higher-order functions is difficult to express. Also, we cannot necessarily accelerate running a function backwards or with partial inputs. Thus, relying on acceleration may hinder use of programs for non-deterministic search.

## Namespace Builder

I want an AST for namespaces that efficiently compiles to a 'flat' dictionary with (rn:renames, def:definition) pairs. This could be followed by a function to apply renames to definitions. To keep it simple, the compiler may reserve a prefix for internal use; can easily raise an error if users attempt to define a reserved symbol.

* *rename:(in:NS, map:\[(Prefix1, Prefix2), ...\])* - rename multiple names within NS. This also renames them within definitions in NS. Initial Prefixes must not overlap. Final prefixes may overlap, but it's an error if two names are combined. 
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


