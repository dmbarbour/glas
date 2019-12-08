# Glas Language

Glas is a purely functional language with transparent futures, programmed in an imperative and object-oriented style. Glas leverages futures to model pass-by-reference parameters and interactive computations. Objects build upon pass-by-reference with a dash of syntactic sugar.

Glas is designed to support [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface). Transparent futures are an essential aspect of this, enabling interactions to be modeled as simple data structures. Glas is ultimately intended for end-user programming and lightweight sharing of software artifacts. These features would build upon the direct manipulation interfaces and functional purity.

Glas is intended to be a full-spectrum language, with good performance and access to hardware acceleration via FPGA, GPGPU, and cloud computing. To support this without violating purity, Glas will support rich static analysis and flexible annotations. 

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

