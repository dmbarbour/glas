# Glas Language

Glas is a purely functional programming language with several major ideas:

* **Session Typed Functions**. Functions are conventionally typed as `Arguments -> Results`. This separation of arguments and results makes it difficult to represent that partial results are available early, or that outputs can be structurally aligned with input. Glas adapts [session types](https://groups.inf.ed.ac.uk/abcd/) to purely functional programming, combining arguments and results into a collective tree of single-assignment input and output parameters.

* **Concurrent, Interactive Unification** as basis for dataflow. Multiple functions may share branches of the input and output parameter tree. Output from one function becomes input to many other functions. This naturally expresses an interactive system with deterministic concurrency, and conveniently supports fine-grained partial evaluation. An effectful program can be modeled as a function with a suitable session type, such as a stream of request-response pairs.

* **Direct Manipulation Interfaces**. If rendered into a user-interface, session types are also suitable for human-computer interaction. A tree of mixed inputs and outputs can be rendered as a stable, interactive document. A list can be rendered as a streaming video, allowing user-input (with timeouts) at each step. We can model a multi-user session by partitioning into a sub-session for each users. User input can be conveniently represented as unification with a patch. Effects are essentially integrated as a software agent handling a role in the multi-user session. 

* **Content-Addressed** modules and binaries. Programmers may use symbolic file-path imports during development, but the Glas compiler or package manager will 'freeze' the program by transitively rewriting symbolic references to secure hashes. This design supports robust versioning, structure sharing between versions, configuration management, incremental processing (type-checking, compilation, upload, download, indexing, etc.), shared processing (e.g. via proxy type-checker or compiler), and distributed computing (ad-hoc database queries, mobile agents, etc.). 

Beyond session types, Glas intends support exceptions, existential types, GADTs, dependent types, model checking, heterogeneous computing, and other useful properties such as static allocation or real-time properties. Glas is intended to be a high-performance language suitable for both low-level and high-level computing.

See [syntax](docs/GlasSyntax.md) and [design](docs/GlasDesign.md) documents for details. 

## Project Goals

This project has several goals:

* define Glas in [documents](docs/)
* bootstrap untyped interpreter or compiler
* self-hosting type-checker and compiler
* effective error analysis and reporting
* develop IDE with projectional editing
* proof of concept for direct manipulation

To produce stand-alone applications, the Glas compiler must integrate an effectful interpretation of the program's session type. The intended interpretation should be described by annotation within the program. For an easy and effective start, the Glas compiler will initially support an effectful interpretation that translates streaming requests to C FFI calls.

Desiderata for this project include keeping it relatively small and simple.

## Status of Language

Glas was a newly envisioned language as of mid July 2019. It is now in late design phase, with most features and syntax settled. Features still being contemplated include support for generics and exceptions. Work on implementation will begin soon, hopefully.

