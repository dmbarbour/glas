# Glas

Glas is a programming language system designed for scalable, reproducible, and extensible software systems.

## Glas Overview

Glas has several non-conventional features:

Glas supports **user-defined syntax**, guided by file extensions. To compute the module value for a file named `foo.xyz`, the compiler will use a program defined by the module named `language-xyz`. It is possible to develop alternative syntax, DSLs, integrate `.json` files as modules, or support projectional editing via specialized syntax.

Programs have a **homoiconic representation** as structured data. Modules compute arbitrary values, including programs. Loading a module's value is distinct from composition of namespaces.

Glas supports **user-defined compilers**. Modules compute arbitrary values. Computed binaries can be extracted to file. To compile a program, compute an executable binary based on the program's homoiconic representation, then extract. It is also possible to compile documents, images, docker containers, etc..

Glas is designed for **large, incremental builds**. The Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) to support deterministic, concurrent, scalable computation. Larger-than-memory data is supported by content-addressed storage. Incremental computing is supported by module caching and explicit memoization. 

Glas excludes first-class functions. Higher-order programming and effects are supported using **static dependency injection**. This supports abstraction, attenuation, and defaults. Avoiding first-class functions simplifies generation of efficient code.

Glas will use explicit [**hardware acceleration**](https://en.wikipedia.org/wiki/Hardware_acceleration) for high-performance computing. For example, we could simulate an abstract register machine then annotate for acceleration. The compiler could replace by actual CPU. This would support efficient representation of algorithms in domains such as compression or cryptography.

See the [design doc](docs/GlasDesign.md) for more detail.

## Project Goals

The concrete goal is to bootstrap a small, feature-rich command-line utility named `glas` with support for methods to evaluate modules incrementally and extract binary data. 

The bootstrap compiler should support the live coding application model with access to filesystem, console, and network, perhaps an idealized document object model for GUI.

Finally, I'd like to figure out a good approach to leverage Nix or Guix packages within the Glas systems.

## Status of Language

Glas has been re-envisioned in June 2020 with the basis on KPN. The high-level design feels like it's finally coming together and stabilizing. 

Some detail work is still required, e.g. exact choice of operators for the stack programming language, and a concrete representation for Glas Object.
