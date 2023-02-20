# Glas

Glas is a general language system. Glas is designed for scalable, reproducible, and extensible software systems. Glas also reinvisions the application model to simplify concurrency, cache management, and live coding or continuous deployment.

## Glas Overview

Glas has several non-conventional features:

Glas supports **user-defined syntax**, guided by file extensions. To compute the module value for a file named `foo.xyz`, the compiler will use a program defined by the module named `language-xyz`. It is possible to develop alternative syntax, DSLs, integrate `.json` files as modules, or support projectional editing via specialized syntax.

Glas supports **user-defined compilers**. When modules compute binary values, those binaries can be extracted. Thus, it is possible to 'compile' meme images or documents. Compiling a program involves processing a homoiconic representation into an executable binary, then extracting.

Glas supports **large, incremental builds**. Large values support structure sharing across builds by content-addressed storage, i.e. using secure-hashes as value references. Work sharing across similar builds can be supported by explicit memoization. 

Glas will use explicit [**acceleration**](https://en.wikipedia.org/wiki/Hardware_acceleration) for high-performance computing. For example, we could simulate an abstract CPU, then replace by actual CPU to implement compression or cryptography algorithms. Acceleration of Kahn Process Networks could support distributed builds.

Glas favors a [**transaction machine application model**](docs/GlasApps.md) that is more amenable to live coding and distributed overlay programs than the conventional `int main(string[] args)` app model. This comes with some optimization challenges, but I'm optimistic that it can provide a better basis for applications.

See the [design doc](docs/GlasDesign.md) for more detail.

## Project Goals

The concrete goal is to bootstrap a command-line utility named `glas` with support for user-defined syntax, compilation of modules, extraction of binaries, content-addressed storage, incremental builds, continuous builds, and usable acceleration (ideally for CPU and KPN).

A minimal bootstrap implementation will be written in F#, with a motive to leverage JIT compilation for performance. The bootstrap will not support acceleration or stowage unless these prove necessary.

## Status of Language

Glas has been re-envisioned several times, so it's been a slow start. KPNs were dropped from the initial program model because they're too complicated. Backtracking was reintroduced to simplify conditional and loop combinators.

But at this point I'm ready to start programming.

