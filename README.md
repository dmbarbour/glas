# Glas Language System

Glas is a programming language system designed for reflective, scalable, deterministic metaprogramming and software build systems. 

## Glas Overview

Glas has several non-conventional features:

Glas supports **user-defined syntax** guided by file extensions. To process a file named `foo.xyzzy`, the compiler will load a module named `language_xyzzy`. This module will specify a process that receives the file binary and computes the module value, with effectful access to other module values. Glas can readily support DSLs, integrate external languages, or be optimized for projectional editing.

Glas programs have a **homoiconic representation** as structured data, which is evaluated as a program only in certain contexts (such as language modules). This enables Glas to compile functions to JavaScript or to write its own type-checkers. Although modules do not hide behavior from clients, it is still possible to enforce assumptions about parametric polymorphism and other nice properties. 

Glas focuses almost entirely on **compile-time computation**. Instead of specifying one standard application model, a subset of modules should compute externally useful binary values. This binary could represent an executable program or docker image. Essentially, the true 'compiler' is a normal Glas function, free to support ad-hoc static safety analysis and optimizations, and is not limited to compiling Glas programs.

Glas computation is based on [**Kahn Process Networks**](https://en.wikipedia.org/wiki/Kahn_process_networks), which provides an effective basis for scalable and interactive computation. The process network is constructed and abstracted procedurally, using a carefully designed [stack-based language](https://en.wikipedia.org/wiki/Stack-oriented_programming). This design simplifies composition and dynamic evolution of the process network, supporting procedural and functional programming styles.

Glas supports [**incremental computing**](https://en.wikipedia.org/wiki/Incremental_computing) via [**content-addressed data**](https://en.wikipedia.org/wiki/Content-addressable_storage) and [**memoization**](https://en.wikipedia.org/wiki/Memoization). Large tree-structured values - including programs - can be partitioned into nodes and referenced by secure hash. Large lists will use a finger-tree representation. Content-addressed data supports incremental distribution and enables memoization to effectively support incremental indexing of large values such as key-value databases.

Glas proposes explicit [**hardware acceleration**](https://en.wikipedia.org/wiki/Hardware_acceleration) to extend computation with new performance primitives. For example, a program that correctly simulates programmable hardware - such as a GPGPU, FPGA, or a generic CPU - can be annotated for the compiler to replace it by direct use of available hardware. The hardware implementation can be fuzz-tested against the reference implementation. The explicit annotation resists performance rot when accelerators are deprecated.

See the [design doc](docs/GlasDesign.md) for more detail.

## Project Goals

The concrete goal is to bootstrap a small (under 1MB) but feature rich command-line utility named `glas` with support for methods to evaluate modules to [Glas Object](docs/GlasObject.md) and extract binary data. 

The bootstrap compiler should use a lightweight application model with access to filesystem, console, and network effects (at least) that can be reused for other user-defined applications. It should be feasible to experiment with web servers, web apps, and interactive consoles. Native GUI is a lower priority.

Finally, I'd like to figure out the right approach to leverage Nix or Guix packages within the Glas systems.

## Status of Language

Glas has been re-envisioned in June 2020 with the basis on KPN. However, this solves several of the remaining design concerns for scalability of the initial computation model.
