# Program Model for Glas

The intermediate language for glas systems is structured as a [namespace](GlasNamespaces.md) with definitions written in [abstract assembly](AbstractAssembly.md), but an essential remaining detail is the specific set of `%*` AST constructors recognized by a glas runtime. This document focuses on that final facet. Like everything else, the program model must be carefully designed to support my vision for glas systems.

## Miscellaneous Design Thoughts

* AST constructors should evaluate to abstract AST nodes. The primitive `%*` constructors are provided by the runtime. This provides a useful staging layer, and permits user-defined metaprogramming of AST constructors in a simple way. Also, in case of multiple definitions for a word, this provides another opportunity to eliminate duplicate definitions.

* Support for local closures and overlays of the application namespace within a given method call. I wonder if we can override methods for purpose of a specific call? That would require applying a logical translation to the host namespace to generate the method's local version. Seems awkward, but viable. Or we could build logical overlays in terms of translating data to names via localization.

* Call graphs should be adaptive, in the sense that we can coordinate construction of a call based on downstream context. This might be achieved based on explicit coordination, grammar-logic, or constraint systems. Use of namespace-like 'translations' per `(%call ... TL)` could bind shared names between calls. 

* I want support for units or shadow types, some extra computation that can be threaded statically through a computation. This could feasibly be integrated with the adaptive call graph mechanism. 

* Support for annotations in general, but also specifically for type abstraction, logging, profiling, and type checks. 

* A flexible system for expressing proof tactics and assumptions about code that are tied to intermediate code and can be roughly checked by a late stage compiler after all overrides are applied.

* I want to unify the configuration and application layers, such that users live within one big namespace and can use the namespace and call contexts to support sandboxing and portability of applications to different user environments. Modularity is supported at the namespace layer via 'ld' in addition to staging. In addition to Localizations, I must handle Locations carefully (and abstractly).

* I want termination by default, if feasible. In context of transactional steps, there is no need for non-terminating loops within the program itself. I have an idea to pursue this with a default proof of termination based on mutually recursive grammar rules without left recursions, while users could replace this with another proof in certain contexts via annotations. We could simply assume termination for remote procedure calls. Not sure of feasibility in practice.

* I like the idea of functions based on grammars, similar to OMeta. This is a good fit for glas systems because we need a lot of support for metaprogramming. Also, grammars can be run backwards to generate sentences. This is both convenient for debugging and understanding code, and for deterministic concurrency based on recognizing and generating the same sentence in different parts of code. 

* I'm interested in code that adapts to context, not just to the obvious parameters but also intentions, assumptions, expected types or outcomes, etc.. The main requirement here is a more flexible, bi-directional dataflow between upstream and downstream calls. This dataflow should be staged, evaluating prior to function arguments. We might try grammar-logic unification or constraint variables in this role. I'm uncertain what is needed, so will keep an open mind for ideas and opportunities here.

