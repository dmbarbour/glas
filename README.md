# Glas Language

Glas is an object-oriented, imperative, concurrent, purely functional language. Programs are expressed imperatively with linear objects, single-assignment variables, and explicit threads. The resulting computations are deterministic, interactive, pure, and cacheable. Glas adapts [session types](https://groups.inf.ed.ac.uk/abcd/) to prevent deadlock.

Interactions in Glas are generally modeled as data structures containing unassigned variables. For example, it is possible to model a question-answer list where later questions are incrementally computed based on previous answers. This list would serve a similar role as a 'rendezvous'. A channel is simply an output-only or input-only list where the tail is deferred.

This model of interaction is very convenient for [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface). We can simply render the structure to the user. Alternative views can be supported via lenses (editable projections). Multiple users and background effects can be supported by partitioning a larger interactive data structure. Real-time systems can be supported using frames and timeouts.

Direct manipulation interfaces lower the artifical barriers between UI and API. For example, it is feasible for end-users to compose 'applications', wiring outputs from one as inputs to another. They can also peek within the application to observe what's happening, using similar graphical projections. 

Ultimately, one motive of Glas is to give more control of software to end users. Other aspects of Glas design also support this vision, especially the module system. However, Glas is also intended to be safe, efficient, and effective for programming low-level and mission-critical systems. To that end, Glas supports flexible static analysis, and types to construct statically-allocated hard real-time programs.

See [design](docs/GlasDesign.md) document for details.

## Project Goals

This project has several goals:

* define Glas syntax and semantics
* bootstrap interpreter or compiler
* self-hosting types and cross compiler 
* tooling: linter, doc-gen, package manager
* proof of concept for direct manipulation

Desiderata include keeping this project small and self-contained. 

## Status of Language

Glas was a newly envisioned language as of mid July 2019. It is now in late design phase, with most features and syntax settled. Features still being contemplated include support for generics and exceptions. Work on implementation will begin soon, hopefully.

