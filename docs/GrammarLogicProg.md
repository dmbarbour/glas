# Programming with Grammars and Logic

## Overview

Grammars and logic are similar in semantics but tend to present distinct HCI and user experience in practice. Grammar-logic programming can be viewed as an experiment to make both more accessible and usable. I additionally draw inspiration from effect systems in FP, and inheritance-based extension in OOP. 

A grammar represents a set of values in a manner that ideally supports efficient recognition and generation of values. A grammar can easily represent a 'function' as a relation, a set of pairs. Computation is based on partial matching of values returning variables. For example, given a grammar representing `{("roses", "red"), ("apples", "red"), ("violets", "blue")}`, we could match `(Things, "red")` and get the answer set `{(Things="roses"), (Things="apples")}`, running the function backwards. In the general case, there might be more than one variable in each answer.

Logic programming reverse implication clauses `Proposition :- Derivation` can be represented in grammars with shared variables and guarded patterns. Shared variables must generate the same value at every location. Guarded patterns might have the form `Pattern when Guard` or `Pattern unless Guard`, allowing the Guard to constrain variables while remaining detached from the computed result.

Pattern-matching with grammars often represent 'recursion' on the pattern side. Within a rule such as `"while" Cond:C "do" Block:B -> loop:(while:C, do:B)` variables `C` and `B` might conveniently refer to computed AST values. We might view `->` as a syntactic sugar to make grammars more usable. The aforementioned example might desugar to a relation `("while" (V1 when (V1,C) in Cond) "do" (V2 when (V2,B) in Block), loop:(while:C, do:B))`, assuming 'Cond' and 'Block' also represent relations.

Interactive computation is implicit when two or more grammars constrain each other, but we can make it explicit. For example, a procedural 'interaction trace' might be modeled as `(args:Arguments, eff:[(Request1, Response1), (Request2, Response2), ...], return:Result)`. A 'procedure' can then be represented as a grammar that generates a trace by reading Arguments, writing Requests, reading Responses, and writing a Result. Similarly, the 'caller' would write Arguments and read Result. A 'effects handler' would read Requests and write Responses. In context of grammars, we can understand 'reading' as matching a wide range of values, and 'writing' as generating a small range of values. It is feasible to adapt [session types](https://en.wikipedia.org/wiki/Session_type) to grammars to precisely control interactions. 

A runtime can bind interactions to the real-world. However, potential backtracking constrains the effects API and interferes with long-running computations. The [transaction machine application model](GlasApps.md) is a good match for grammar-logic programming with backtracking. Alternatively, it is feasible to prove (perhaps with type-system support) that backtracking is unnecessary for certain grammar-logic programs.

A gramar-logic language can modify the 'syntactic sugar' for functions (`->`) to represent interaction traces instead of relations. This would significantly enhance extensibility and expressiveness of the language, reducing need for programmers to manually thread interaction variables through a program. But procedural request-response is awkward for modeling concurrent interaction, so I'm exploring alternatives.

Grammar-logic languges have potential to be modular and extensible. OOP solved the problem of extending systems of mutually recursive definitions via inheritance and mixin patterns. A grammar-logic language can build upon the idea of grammar 'classes' that implement named grammars, enabling extension or override or of specific elements. Alessandro Warth's [OMeta](https://en.wikipedia.org/wiki/OMeta) develops this idea effectively, though it misses some features of a grammar-logic.

## Desiderata

Specific features I want for glas systems.

* Support for annotation and acceleration of grammars.
* Flexible extension and composition of grammars.
* Expressive and scalable standard interaction model.
* Convenient testing and verification extensions.
* Program behavior is static at compile time.
* Termination guarantee, if I can manage it.

A staged model, where we build a simpler program that builds grammars and extensions and tests and so on, is a good approach to my goals.

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

Users might explicitly mark 'abstract' methods that should be overridden, or mark 'final' methods that should not be. These assumptions can also be checked.

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

We can model interactions as a structured value that is mutually constrained by multiple component grammars. The simplest case is a procedure interacting with the environment, producing a request-response sequence where the procedure determines requests and the environment determines responses.

I propose to build interaction primarily around forkable, duplex, temporal channels. Every event on such a channel either message data, a subchannel, or a time step. Relevantly, reading data or accepting a subchannel involve distinct keywords or syntax. The temporal aspect allows waiting to be separated from reading, to model asynchronous interaction. When a procedure or process 'waits', this is ultimately modeled as a time step that propagates to connected channels.

Most methods are 'procedural' and interact with their environment through a standard request-response channel. But we can model processes, binding different subprograms to operate on disjoint subsets of channel names. Channel names can be masked or aliased for method calls within scope. We could enforce 'pure' functions by masking all the channels. 

Beyond channels, support for implicit parameters (such as environment variables) is also a useful feature. We might also borrow writer semantics in special cases. I'll insist the interaction model is an extensible data structure. 

### Default Definitions? Defer

Default definitions can serve a useful role in many cases, improving concision in cases where the defaults are acceptable. But how should we model defaults? 

* We could mark some definitions with a 'default' priority. Defaults can override and extend other defaults, but will not override a higher priority definition.
* We could to introduce a 'default' namespace. If 'foo' is undefined, we can implicitly fall back to 'default/foo'. This is predictable and accessible, yet poorly integrated.
* We could introduce a 'default' method, such that `default(foo:FooArgs)` represents the default behavior for 'foo'. This is difficult to reason about, yet very expressive.

I hesitate to commit. Perhaps I should return to ths question later, after I start to experience the need for it in practice and can experiment with various solutions. 

### Static Assertions

We could introduce static assertions within grammars, which are assertions that a particular grammar produces a value. These static assertions would be computed after all extensions are in-place. This would allow some flexible testing of programs.

Static assertions might be expressed as annotations. Type annotations are also possible, and would serve a similar role.

### Weighted Grammars and Search

Methods, instrumented by annotations, can generate natural numbers representing 'cost' and 'quality' and other ad-hoc attributes. The runtime can implicitly total these values and use them heuristically to guide search in context of non-deterministic computations. 

Although this isn't a perfect solution, I think it might be adequate for guiding search algorithms. Conveniently, no semantics are required beyond flexible annotations.

### Staged Metaprogramming

Extension provides an effective approach to higher-order programming. Interaction via second-class channels provides another, supporting communication between remote regions of code. A third solution is to model *staged interactions*, i.e. design the interaction model and method syntax such that a predictable subset of inputs and outputs can be computed at compile-time.

I would prefer to avoid the scenario where I'm explicitly generating grammar values at the end of a stage. This would be poorly integrated with extensibility and other features.

### Acceleration

It is possible to support accelerated functions in context of grammar-logic languages. But they might not always be accelerated in every direction, e.g. it's easier to accelerate functions from input to output than vice versa.

## Program Model

