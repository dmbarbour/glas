# Glas Language

Glas is a purely functional language with transparent futures and rich metaprogramming. In the default syntax, Glas is programmed in a procedural-functional style, with signatures and structures similar to ML. However, Glas supports package-defined syntax which can support other styles.

Glas is designed to support [direct manipulation interfaces](https://en.wikipedia.org/wiki/Direct_manipulation_interface). Transparent futures are an essential aspect of this, enabling interactive sessions and protocols to be modeled as data structures, which can be rendered and manipulated directly.

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

