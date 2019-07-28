# Glas Language

Glas programming language combines two ideas: session types and pure functions.

## Session Types for Pure Functions

In context of pure functions, session types represent a dependency graph between function inputs and outputs. Session types can achieve this with greater convenience and precision than conventional functional programming languages. 

For example, in a conventional language we might have a function of type `(A,B,C) -> (X,Y)`. If we want `X` as an earlier output, we might change the type to `A -> (X, (B,C) -> Y)`. Unfortunately, there is no implicit subtype relationship between these function types, and we'll often need to restructure both caller and the implementation to support this change. In contrast, with session types, we have something closer to `?A !X ?B ?C !Y`, and this is a subtype of `?A ?B ?C !X !Y` or even of `!X ?A !Y ?B !Z`. Further, these programs can be written in a direct style: the types are based on data dependencies, not program structure.

## Interactions and Effects

Intermediate outputs can be observed by the caller then influence future inputs. This is a simple interaction. 

Session types enable convenient expression of *interactive* computations. We can adapt choice and recursive session types to pure functions. A choice of sessions can model variants or object-oriented interfaces or remote server APIs. A recursive session can model unbounded request-response streams or collaborative decision trees.

With session types, we can model streaming data, or streams of request-response interactions. The latter might serve as an effects model, a viable alternative to monads or algebraic effects.

## Process Networks and Deterministic Concurrency

Although interactions start with the caller, they may be delegated to another function call. By abstracting or translating how interactions are handled through other function calls, we essentialy model a concurrent process network. However, unlike most process models, the determinism of pure functions is preserved.

*Aside:* Use of session types with pure functions subsumes [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks): we can model streaming data channels, but we aren't constrained by them. A streaming interaction can effectively model cooperative concurrency between coroutines.

## Other Glas Features or Goals

Glas aims to become a robust, high-performance, systems-level programming language. Glas has many low-level numeric types. Programmers may declare that a program must be statically allocated for embedded or mission-critical systems. Termination up to input should be guaranteed by static analysis. External modules or packages will be robustly referenced, shared, and versioned by secure hash (although development modules may use normal filenames).

## Language Status

Glas is a newly envisioned language as of mid July 2019. 

Formalization of a syntax, semantics, and type system has barely started. Many challenges remain, e.g. I'm still struggling with how to support generic programming (traits? implicit parameters?). I intend to brainstorm, develop, and refine details over the next few months. I'll try to keep potential readers up-to-date via the project wiki.

Glas has potential to become a powerful, performant, and popular language due to its unusual combination of expressive concurrency, determinism, and intended support for static allocation and systems targets. 


