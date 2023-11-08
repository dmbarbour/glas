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

* Structural termination guarantee for most computations. 
* Support for annotation and acceleration of grammars.
* Flexible extension and composition of grammars.
* Expressive and scalable standard interaction model.
* Convenient testing and verification extensions.
* Simplicity - avoid first-class grammars and eval.

A staged model, where we build a simpler program that builds grammars and extensions and tests and so on, is a good approach to my goals.

## Brainstorming

### Ordered Choice 

It is feasible to model a ternary `if C then P else Q` structure that means something close to `(P when C) or (Q unless C)`. Specialized, we could also model a binary `P else Q` that means `P or (Q unless P)`.

This gives a robust basis for ordered choice. If we build grammars entirely with ordered choice, we can easily reason about determinism. However, ordered choice does complicate expression and semantics insofar as we prioritize 'outer' choices over 'inner' choices.

### Deterministic Computation

I need deterministic functions in many contexts. 

One potential approach: develop a grammar language for constructing deterministic functions, e.g. in terms of ordered choices and dataflows. This allows the language to ensure deterministic computation when programs are run in functional or procedural contexts, yet allows non-deterministic computation when evaluated with partial inputs. Logic programs can be represented by functions that return unit, focusing on the partial input. 

I would prefer to support confluent computation, i.e. determinism with non-deterministic choices internally (behind pattern guards). But it isn't clear to me how to identify confluence. Unless this is solved, it isn't a viable solution.

### Staged Metaprogramming

I would like the ability to express staged programs, a constrained basis for higher-order programming.

The most direct option is to develop functions that explicitly construct values that represent the next stage of computation. This approach is simple and flexible, but it's very poorly integrated. We cannot easily predict properties of the generated program or how they will be influenced by program extensions. Though dependent types and GADTs could offer a good start.

Less directly, we can model staging as partial evaluation. In the simplest case, a two-stage pure function `A -> (B, C -> D)` can be modeled as a grammar that represents a pair of inputs and outputs `((A,C), (B, D))` with a staging constraint where `B` depends deterministically on `A`. This would be complicated by interaction models and more flexible staging. In practice, dedicated syntax or session types may be necessary to protect staging constraints.

I think the partial evaluation solution is worth exploring. If simple syntax or types are sufficient, perhaps we could base the bootstrap language on partial evaluation. 

### Proposed Interaction Models

It is feasible for a grammar-logic language to support *multiple* interaction models for different functions, but I think it would be more convenient to develop a one-size-fits-all interaction trace. Preferably something that can be efficiently processed and partially evaluated based on which features are actually used.

Procedural request-response is a simple and effective interaction model, but it isn't very concurrency friendly. Ignoring dependent types a 'handler' might be represented by an effectful function from `(Request, State) -> (Response, State)`. The requests and responses would become part of the interaction trace. However, it might be useful to support labeled effects - multiple independent request-response streams.

Reader monad, or implicit parameters, is essentially an extra parameter or dictionary thereof that is implicitly input into function calls and accessed via keyword. This represents an 'environment' that is not specific to the call. This effect is trivial to implement, does not interfere with parallelism, and can be very useful in modeling 'context'.

Writer monad. Each subprogram may write a list of outputs, and these lists are implicitly concatenated. More generally, any monoidal structure would work, but lists are a good option if we can't prove monoidal laws. A 'handler' would return the aggregate value generated by a subprogram. This is awkward for most use cases, and channels would cover all the exceptions, so I'm not inclined to pursue Writer.

We could represent a State monad, essentially modeling a dictionary of mutable variables. I doubt I would want this for the top level interaction - that would model 'global' variables. But something like State might prove convenient for modeling local mutable variables within a 'loop' or similar. I should figure out how to express most conventional procedural patterns with grammars, and I expect this would be useful there.

Process networks offer a potential basis for a concurrency-friendly interaction model. First-class channels are more troublesome, with significant risk of accidental aliasing or dropping a channel without 'closing' it. But second-class channels have potential, and can even be dynamic (e.g. if we use a separate 'chan' stack, or a separate environment containing only named channels). I'll break this out into a separate section.

If we do model process networks with dynamic channels, it might be useful to model a procedural request-response stream as a standard channel. We could automatically construct a dynamic subchannel when we spawn a process.

### Modeling Process Networks and Channels

We can model channels with clever use of lists and logic unification variables. Essentially, we model partially specified list variables. Duplex channels, subchannels, and time steps can all be supported. However, modeling automatic aggregation of time steps isn't trivial, which might hinder performance of temporal process networks.

It is possible but not very convenient to introduce 'race conditions' into the channel model. A race condition would essentially be a non-deterministic choice in the grammar.

The challenge is to ensure channels are used correctly, i.e. that each 'write' is to a distinct location, that the list is terminated when we won't write any further, and so on. This requires syntactic support. First-class channels would also require abundant type support, so I think second-class channels might prove more convenient.

One viable option is to extend each 'function call' with a section for keyword channel parameters and results. 
