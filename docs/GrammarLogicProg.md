# Programming with Grammars and Logic

## Overview

Grammars and logic are similar in semantics but tend to present distinct HCI and user experience in practice. Grammar-logic programming can be viewed as an experiment to make both more accessible and usable. I additionally draw inspiration from effect systems in FP, and inheritance-based extension in OOP. 

A grammar represents a set of values in a manner that ideally supports efficient recognition and generation of values. A grammar can easily represent a 'function' as a relation, a set of pairs. Computation is based on partial matching of values returning variables. For example, given a grammar representing `{("roses", "red"), ("apples", "red"), ("violets", "blue")}`, we could match `(Things, "red")` and get the answer set `{(Things="roses"), (Things="apples")}`, running the function backwards. In the general case, there might be more than one variable in each answer.

Logic programming reverse implication clauses `Proposition :- Derivation` can be represented in grammars with shared variables and guarded patterns. Shared variables must generate the same value at every location. Guarded patterns might have the form `Pattern when Guard` or `Pattern unless Guard`, allowing the Guard to constrain variables while remaining detached from the computed result.

Pattern-matching with grammars often represent 'recursion' on the pattern side. Within a rule such as `"while" Cond:C "do" Block:B -> loop:(while:C, do:B)` variables `C` and `B` might conveniently refer to computed AST values. We might view `->` as a syntactic sugar to make grammars more usable. The aforementioned example might desugar to a relation `("while" (V1 when (V1,C) in Cond) "do" (V2 when (V2,B) in Block), loop:(while:C, do:B))`, assuming 'Cond' and 'Block' also represent relations.

Interactive computation is implicit when two or more grammars constrain each other, but we can make it explicit. For example, a procedural 'interaction trace' might be modeled as `(args:Arguments, eff:[(Request1, Response1), (Request2, Response2), ...], return:Result)`. A 'procedure' can then be represented as a grammar that generates a trace by reading Arguments, writing Requests, reading Responses, and writing a Result. Similarly, the 'caller' would write Arguments and read Result. A 'effects handler' would read Requests and write Responses. In context of grammars, we can understand 'reading' as matching a wide range of values, and 'writing' as generating a small range of values. It is feasible to adapt [session types](https://en.wikipedia.org/wiki/Session_type) to grammars to precisely control interactions. 

A runtime can bind interactions to the real-world. However, potential backtracking constrains the effects API and interferes with long-running computations. The [transaction machine application model](GlasApps.md) is a good match for grammar-logic programming with backtracking. Alternatively, it is feasible to prove (perhaps with type-system support) that backtracking is unnecessary for certain grammar-logic programs.

A gramar-logic language can modify the 'syntactic sugar' for functions (`->`) to represent interaction traces instead of relations. This would significantly enhance extensibility and expressiveness of the language, reducing need for programmers to manually thread interaction variables through a program. But procedural request-response is awkward for modeling concurrent interaction, so I'm exploring alternatives.

Grammar-logic languges have potential to be modular and extensible. OOP solved the problem of extending systems of mutually recursive definitions via inheritance and mixin patterns. A grammar-logic language can build upon the idea of grammar 'classes' that implement named grammars, enabling extension or override or of specific elements. Alessandro Warth's [OMeta](https://en.wikipedia.org/wiki/OMeta) develops this idea effectively.

Functional programming, procedural programming, object-oriented programs, and process networks can be directly modeled as grammars. But without adequate language support, manually encoding complex interactive behavior into grammars inevitably results in an inefficient, incomprehensible, fragile mess. So the goal here is to design a viable PL upon pure grammar-logic semantics.

## Brainstorming

### Ordered Choice 

We can easily model ordered choice in terms of unordered choice and pattern guards.

        P or-else Q             =>      P or (Q unless P)
        if C then P else Q      =>      (P when C) or (Q unless C)

The if-then-else form is more general and more widely useful. We could extend this to pattern matching functions without much difficulty. 

Rewriting from ordered to unordered will hinder optimizations, so it's better to have ordered choice as a built-in. Also, ordered choice cannot represent unordered choice, so it provides a decent basis for deterministic computation.

*Todo:* Prove or-else is associative and idempotent. 

### Deterministic Functions

Deterministic computations are useful in many contexts. I propose to design my grammar-logic language such that grammars always represent deterministic functions. That is, results are fully deterministic up to arguments and interactions. 

Non-deterministic computations can still be modeled contextually by running a function with partial inputs, or even running backwards. The right-hand side of a match must still be valid 'grammar' pattern. 

*Aside:* In special cases, it might be feasible to also take advantage of 'confluent' computations. But I don't know how to leverage this outside of accelerated functions. 

### Extensions

The simplest extension is similar to OOP single inheritance.

        grammar foo extends bar with
            integer = ... | prior.integer

Here 'prior.integer' refers to bar's definition of integer, albeit with any reference to 'integer' within that definition referring instead to foo's definition. This allows foo to flexibly extend or override bar's original definitions. Of course, syntax for 'prior.' may vary. Any words not explicitly defined in 'foo' would be inherited from 'bar'. 

Annotations could help represent usage assumptions, such as cases where certain methods should or should not be overridden. 

Multiple inheritance can be modeled in terms of templated single inheritance. I'm not convinced to support templates in general, but it seems useful to explain my meaning here. 

        mixin foo with ... => grammar foo<G> extends G with ...
        mixin foo extends bar with ... => grammar foo<G> extends bar<G> with ...
        mixin foo extends bar . baz with ... => grammar foo<G> extends bar<baz<G>> with ...

The '.' operator here represents function composition of mixins. Syntax may vary. 

The 'grammar' and 'mixin' declarations may conveniently compile to the same underlying model, leveraging annotations to describe distinct usage assumptions. For example, we might insist mixins may extend grammars but not vice versa. We can check that multiple inheritance of grammars does not introduce conflicting definitions - that the G parameter and grammar contain no overlapping definitions. A few simple rules can mitigate known problems with multiple inheritance.

Users can explicitly mark 'abstract' the methods that should be overridden, or mark 'final' the methods that should not be. These assumptions can also be checked. Abstract methods provide a simple basis for higher-order programming of grammar-logic systems, albeit second-class.

### Components and Namespaces

In some cases, I want to work with more than one version of a grammar, or simply organize my grammar into something like objects. The contained grammars or objects should remain extensible.

One possibility is to develop a concise syntax for shifting a grammar (or mixin) into a namespace. We could specify that 'foo in f' means we add a prefix 'f/' to every method name used in grammar 'foo' (not applying to the final mixin parameter!). With this, we could model a grammar containing a component as something like the following:

        grammar foo extends (bar in b) with
            b/integer = int
            int = ... | prior.b/integer

Obviously the syntax needs serious work here! The name 'prior.b/integer' is verbose and ugly. Adding a dozen '(bar in b)' extensions in the header line would quickly grow out of control. It is unclear how we'd express private namespaces.

A more procedural approach to grammar description is a viable solution. Mixins would become functions that directly manipulate a grammar. And if we define 'integer' more than once, each could refer to a different 'prior.integer' to support incremental definition. Conveniently, this grammar builder procedure shouldn't need loops or conditions.

Syntax aside, namespaces and components can significantly improve scalability and expressiveness of the grammar-logic language. It allows grammars themselves to be used as higher-order functions or objects by designating a namespace for each 'instance'. 

### Scope

Grammars in a grammar-logic languages can support private methods similar to OOP. Private methods can be usefully understood in terms of introducing anonymous namespaces for local scratch work. Within this namespace, there is no need to worry about name conflicts, accidental overrides, etc.. This simplifies local reasoning and refactoring.

A simple and effective concrete representation is to reserve a prefix character for private names, perhaps '\_'. This represents the anonymous namespace prefix within a given privacy scope, and structurally avoids conflicts between public and private names.

### Interaction Model

I propose to model interactions around transferable, temporal, duplex channels. In a given step, a channel may transfer data or a subchannel, or indicate a time step - that 'logical time' must advance before more inputs are available. A process can wait upon multiple channels until one has data, and waiting will implicitly propagate to all channels that the process holds. 

The above implies a clear understanding of which channels a process 'holds'. To support this, channels are represented in a program model as special variables. If a variable is not explicitly referenced, it is not held by that procedure. We can introduce an explicit 'touch Channel' behavior for the very rare case that we want a loop to hold onto a channel for timing purposes only.

I propose to model program code as 'procedural' by default. Calls to grammar methods will implicitly thread a single, standard 'io' channel for procedural effects. It is possible to alias io via an 'effects handler'. It is possible to provide other channels to a method call as in-out parameters.

Aside from channels, I propose an implicit 'env' data parameter representing the implicit environment, similar to a reader monad. This specialized effect can offer superior performance than channels for some common use-cases. The data type representing interactions should be extensible for new ideas - in-out state parameters, CRDTs, etc..

*Note:* A procedure cannot explicitly close a channel. Channels will be closed automatically when they leave scope. This simplifies wrapping of protocols within other protocols.

### Channel-Based Objects and Functions

An object can be modeled as a process that receives a subchannel for each 'object method call'. The caller would fill this subchannel with inputs representing an operation or query, then the object process would respond via the same subchannel, routing data back to the caller. Simple request-response patterns may generalize to flexible session-typed protocols.

An object reference is modeled by a channel that delivers method call subchannels. Without temporal semantics, we could model 'linear' objects (no aliasing, still useful) or non-deterministic ordering of method-calls (if we allowed non-determinism). Temporal semantics allow us to 'copy' the object reference, merging method calls deterministically within each logical time step.

Functions can be modeled as method calls with known special properties. For example, if a runtime knows certain method calls are commutative and idempotent, then the runtime could relax the sequencing requirement and even cache results of prior computations. But we may need support from the program syntax and type system to robustly recognize the opportunity for optimization.

Anyhow, channels can effectively support first-class functions and objects without 'mobile code' or 'shared codespace' semantics. This has some benefits in context of concurrency, distribution, and security reasoning.

### Consistency of Grammars and Objects

Methods in a grammar represent procedure calls as grammars generating interaction traces. Methods of a channel-based object represent interactions through a channel. In either case, a method call may require multiple data and channel inputs, and return multiple data and channel outputs. 

Consistent syntax and behavior for these cases would be convenient, insofar as it is possible.

We'll certainly need special attention to implicit parameters such as 'env' and 'io'. We could allow these to be implicitly bound even through channel sessions. This would be consistent, and would shift any complexity regarding binding of local 'env' or local 'io' to a special syntax for object construction.

If session types are used for object methods, we should also be able to assign a useable session type to the grammar methods. Even better if grammar methods can support the same multi-step protocols as object methods.

### Staged Programming

A staged program returns the next program, to be evaluated in another context. This pattern is useful where performance is relevant and we don't want to recompute the initial stage repeatedly. Staged programming generalizes to more than two stages.

A simple approach to staging is to return a value that represents the next program as text or abstract syntax tree. This value can be stored and communicated, evaluated independently. This option is effective, but integration can be awkward:

* A staged program cannot easily extract its own behavior as a value, thus behavior is often represented twice and consistency of representations is difficult to guarantee.
* It can be difficult to reason about behavior or correctness of the returned program. Sophisticated types can help, such as Haskell's GADTs. 
* It can be difficult to ensure behavior and performance is preserved. The client must provide the effectful environment and efficient eval function. 

To simplify integration, a staged program may instead return an object or function that represents the next program. In a typical FP language, a two-stage function might be represented as `A -> (B, C -> D)`. In context of grammar-logic, the returned object or function would instead be modeled as a channel.

### No-Fail Contexts? 

In some contexts we don't want backtracking. This is especially the case for binding effects to the real-world, i.e. we might want to insist that certain 'io' requests can only be issued in a 'no-fail context', such that there is no risk of trying to 'undo' the request.

A no-fail context can call functions that may fail, under certain conditions, i.e. if the call is wrapped and the fallback behavior is no-fail. But a may-fail function cannot call functions that assume a no-fail context. This could be supported as something like an effect type, except that 'no-fail' is the effect instead of failure as an effect.

A 'no-fail' effect is possible only in context of a type system that can guarantee exhaustive matching of potential inputs. If invalid inputs are provided, failure is inevitable. This requires dependent types in the general case, and is likely to interfere with ad-hoc extensions. It isn't a lightweight feature.

### Type Safety

It is feasible to augment methods with type annotations, which describe:

* the data types for input and return values
* the effects protocol used via 'io' channel
* effects protocols for other bound channels
* if method needs no-fail context assumption

Protocols can potentially build on the notion of session types. Session types would be adapted to work within the consistency constraints for grammar methods and limits of duplex channels and the need for implicit 'io' and 'env' arguments. That is, it might not be as powerful as session types in general, so long as it's expressive enough for our purposes.

Partial evaluation is implicit insofar as a program produces partial outputs as a consequence of receiving partial inputs. Logic unification variables and channel-based interaction are very convenient for partial evaluation.

Session types can help make partial evaluation 'robust' by describing assumptions in a machine-checkable format, and enabling analysis of potential datalock.

### Default Definitions? Defer

Default definitions can serve a useful role in many cases, improving concision in cases where the defaults are acceptable. But how should we model defaults? 

* We could mark some definitions with a 'default' priority. Defaults can override and extend other defaults, but will not override a higher priority definition.
* We could to introduce a 'default' namespace. If 'foo' is undefined, we can implicitly fall back to 'default/foo'. This is more extensible and accessible than priority.
* We could introduce a 'default' method, such that `default(foo:FooArgs)` represents the default behavior for 'foo'. This is difficult to reason about, yet very expressive.

I hesitate to commit. Perhaps I should return to ths question later, after I start to experience the need for it in practice and can experiment with various solutions. 

### Weighted Grammars and Search

Methods can be instrumented with annotations to generate numbers estimating 'cost' and 'fitness' and other ad-hoc numeric attributes. A runtime can implicitly compute and total these values, and use them heuristically to guide search in context of non-deterministic computations. This isn't a perfect solution, but I think it might be adequate for many use cases.

Because this can build on annotations, no special semantics are needed. But dedicated syntax can make this feature more accessible.

### Module Structure

A glas module will compile to a dictionary of independent grammars, of form `(foo:gram:(ns:NS, ...), bar:gram:(...))`. The 'gram' header indicates the program model to the glas command line interface and to other language modules. The header also provides a space for definition-layer annotations. Formal behavior of the grammar is represented as a namespace of grammar-logic methods under 'ns'.

Where a 'gram' is referenced as an application program (e.g. in context of language module 'compile', glas CLI 'run', or automatic 'test' programs), the default entry method is 'main'. An alternative entry can be specified via `entry:Name` annotation.

*Note:* I intend to avoid need for 'eval' within the language module 'compile' function. Thus, the proposed grammar-logic language does not support top-level assertions, static data, or macros. All definitions have type 'gram'.

### Staged Applications

Staged programming is especially useful in context of glas CLI '--run' and the transaction machine application loop. But there is no 'macro' option in my proposed grammar-logic language. Some options:

* annotate a specific run mode for an entry method
* introduce associated method to report a run mode
* infer run mode based on type of the entry method

Inferring run mode seems like unnecessary complexity. Explicit annotation of run mode under 'gram' seems a bit awkward. An annotation bound to the method seems acceptable to me. Regarding 'associated method' - that is an intriguing approach to method-layer annotations!

### Annotations Namespace

One approach to annotate 'foo' with an author is to define 'anno/foo/author'.

This approach is simple and has some nice properties. The annotations are easily extended or abstracted. They don't require unique syntax. Tools may warn when annotations are referenced from outside the 'anno' namespace or have unexpected types. Renaming requires a little extra work, but it's easily resolved at the syntax layer assuming awareness of the convention.

I haven't discovered any significant disadvantages. I think this idea is worth trying in practice, albeit only for annotations on specific names.

### Static Assertions

A static assertions is a computation that should return 'true'. 

In context of grammar-logic, an assertion is easily modeled as a method where the system evaluates whether the method accepts at least one input. This generally involves non-deterministic search for a passing input. Negative assertions are easily modeled, e.g. `() unless P`. 

Within the grammar-logic language, we could express annotations in several ways:

* annotations on names, e.g. `anno/foo/assert/everything-is-awesome = ...`
* shared namespace of assertions, e.g. `assert/everything-is-awesome = ...`
* anonymous annotations on namespace, e.g. `assert "everything is awesome" ...`

I'm inclined to support both namespace options as a system convention. Assertions would be assigned unique and hopefully meaningful names, which are included in a failure report. Assertions bound to 'foo' may be skipped when 'foo' is not used within an application. Anonymous assertions are implicitly supported via private namespaces.

### Acceleration

It is possible to support accelerated functions in context of grammar-logic languages. But they might not always be accelerated in every direction, e.g. it's easier to accelerate functions from input to output than vice versa.

## Namespace Builder

The namespace builder procedurally constructs a namespace of mutually recursive 'methods'. Each operation manipulates a tacit prior namespace. Operations include method overrides, hierarchical namespace structure, scoped anonymous namespaces, and renaming of methods and namespaces.

*Note:* I could support for anonymous annotations within a namespace. But it smells like unnecessary complexity in context of conventions such as *Annotations Namespace* and *Static Assertions*. 

## Grammar Methods


## Misc

### Channels

### Method Interactions

### Method Protocols?

### Pass by Reference Parameters


