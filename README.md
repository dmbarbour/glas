# Glas Language

Syntactically, Glas looks and feels like a conventional procedural or object-oriented language, albeit limited to linear objects. Semantically, Glas is a purely functional programming language based on *graph unification*.

Unification expresses interactive, concurrent computations. Graphs can directly represent shared, labeled structure. Graph unification is stable, monotonic, well suited for [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface).

Glas supports robust configuration management, concurrent versions, incremental processing, and separate proxy compilation. This is achieved by a simple feature: before compilation or deployment, program source is 'frozen' by transitively rewriting named dependencies to content-addressed secure hashes. The program can be thawed for editing.

Glas is a richly typed language. [Session types](https://groups.inf.ed.ac.uk/abcd/) are adapted to the Glas computation model to prevent deadlock. Existential types support modularity. [GADTs](https://en.wikipedia.org/wiki/Generalized_algebraic_data_type) will support embedded languages. Dependent types and model checking are also goals. However, Glas does not have typeful semantics - no type-indexed generics or `typeof` operation.

See [design](docs/GlasDesign.md) document for details.

## Project Goals

This project has several goals:

* fully define Glas syntax and semantics
* bootstrap untyped interpreter or compiler
* self-hosting type-checker and compiler
* effective error analysis and reporting
* compilation targeting JavaScript + DOM
* develop IDE with projectional editing
* proof of concept for direct manipulation

To produce applications, a Glas compiler will integrate an effectful interpretation of the program's session type. The bootstrap will support only filesystem and console. The self-hosting compiler should support a more flexible effects model. This may involve an annotation specifying how requests are translated to the compiler's intermediate language, which should be sufficiently low-level to support OS calls.

Desiderata are to keep this project *small* and *self-contained*. I take considerable inspiration from [Red language](https://www.red-lang.org/p/about.html) that this can be achieved. Modulo JavaScript+DOM, which is a mess.

## Status of Language

Glas was a newly envisioned language as of mid July 2019. It is now in late design phase, with most features and syntax settled. Features still being contemplated include support for generics and exceptions. Work on implementation will begin soon, hopefully.

