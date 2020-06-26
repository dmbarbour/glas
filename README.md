# Glas

Glas is a programming language system designed for scalable, reproducible, and extensible software systems.

## Glas Overview

Glas has several non-conventional features:

Glas supports **user-defined syntax**, guided by file extensions. To compute the module value for a file named `foo.xyz`, the compiler will use a program defined by the module named `language-xyz`.

Glas modules and programs have a **homoiconic representation** as structured data. A module can inspect or manipulate programs imported from other modules as data.

Glas focuses on **compile-time computation**. The Glas command-line tools know how to compute module values and extract binaries. Actual runtime behavior requires computing a binary executable. Type checkers, optimizers, and code generators become normal Glas functions.

Glas computation is based on [**Kahn Process Networks**](https://en.wikipedia.org/wiki/Kahn_process_networks) to support scalable, interactive computation without compromising determinism. Glas programs have a procedural style, but externally may be viewed as pure functions.

Glas has built-in support for [**content-addressed storage**](https://en.wikipedia.org/wiki/Content-addressable_storage) and explicit [**memoization**](https://en.wikipedia.org/wiki/Memoization) to support larger than memory values and incremental computing. Databases can be modeled as first-class values, and incrementally indexed.

Glas proposes explicit [**hardware acceleration**](https://en.wikipedia.org/wiki/Hardware_acceleration) for high-performance computing. A subprogram that simulates an abstract GPGPU can be annotated for substitution by an actual GPGPU. This requires careful design and compiler support, but can open new problem domains such as machine learning, physics simulation, or video rendering.

See the [design doc](docs/GlasDesign.md) for more detail.

## Project Goals

The concrete goal is to bootstrap a small (under 1MB) but feature rich command-line utility named `glas` with support for methods to evaluate modules incrementally and extract binary data. 

The bootstrap compiler should use a lightweight application model, perhaps with access to filesystem, console, and network effects (at least) that can easily be reused for other user-defined applications. It should be feasible to experiment with web servers and web apps, but native GUI is not my priority.

Finally, I'd like to figure out a good approach to leverage Nix or Guix packages within the Glas systems.

## Status of Language

Glas has been re-envisioned in June 2020 with the basis on KPN. The high-level design feels like it's finally coming together and stabilizing. 

Some detail work is still required, e.g. exact choice of operators for the stack programming language, and a concrete representation for Glas Object.
