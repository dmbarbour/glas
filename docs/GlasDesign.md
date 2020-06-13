# Glas Language Design

This document aims to be relatively concrete in providing design details.

## Module System

Glas modules are concretely represented by files and folders in a filesystem. 

To find a module `foo`, a compiler will search for a file `foo.*` or folder `foo/` in the same directory as the file it's currently compiling. To find a module `pkg:foo`, also called a package, the compiler will search for a folder `foo/` based on the `GLAS_PATH` environment variable. 

Every module computes a Glas value. The value should be heuristically cached to support incremental computation, favoring the [Glas Object](GlasObject.md) representation for sharing.

To compute a value for a file `foo.xyz`, the Glas compiler will implicitly search for a module or package `language-xyz`, favoring a local module. The language module will specify a process that receives the file binary as input, loads module dependencies, and computes a value, among other things. (See *Language Modules*.)

To compute a value for a folder `foo/`, the Glas compiler will use the value of the contained `public` module, if it exists. Otherwise, the value is a record reflecting the module structure. Use of public modules gives more control over what a folder module exposes. Folders cannot reference their parent directory, so are self-contained.

Ambiguous file names or directed dependency cycles will raise errors.

## Data Model

Glas values are simple structured data, composed of immutable records, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], extid:true)`. Strings and binaries would be represented as lists of small numbers (0-255). Keys within a record are also strings.

Variant data is typically encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsv:(...)` would be represented by a dictionary exclusively containing either `rgb` or `hsv`. Symbolic values such as booleans and enumerations are encoded as variants with a unit value.

The empty dictionary `()` can serve as a unit value.

Glas systems are expected to represent large lists using [finger trees](https://en.wikipedia.org/wiki/Finger_tree), i.e. supporting amortized O(1) deque operations (push and pop from either end) and logarithmic time for almost everything else. For binaries, the list structure should also leverage [rope-style](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) chunking.

Glas natural numbers do not have a hard upper limit, and bignum arithmetic is supported. Programmers can model rational numbers and other types. However, high-performance numeric computing with Glas will require use of *Acceleration*.

To support working with large values, Glas specifies a [Glas Object](GlasObject.md) encoding and use of content-addressed references to component values.

## Compilation via Binary Extraction

A subset of Glas modules will compute binaries that are externally meaningful, perhaps representing an executable file or docker container. The Glas compiler must provide methods to extract these binary values for external use. 

The type-checker, optimizer, and code generator would be represented as normal Glas functions. Glas does not specify a runtime behavior model, but de-facto standards will certainly emerge based on which models are well supported.

Glas applications should specify application behavior in a separate package from compilation to binary. For example, we might have `pkg:appname` and `pkg:appname-exe`. This separation enables embedding the application behavior into other applications, simulation or model testing, and alternative compilation modes.

*Note:* Glas modules cannot look outside the filesystem. Glas systems can easily support host-package compilation by installing a host-specific `pkg:system-info`. However, cross compilation is also very useful.

## Computation Model

### Modeling Effects



## Accelerated Computation (Accelerator Pattern)

Glas does not directly support floating-point arithmetic, SSE, GPGPU, FPGA, and similar hardware-accelerated computation features. However, performance is always a useful feature whether at compile-time or runtime, and it's upsetting to waste capacity. So, Glas provides an indirect mechanism to fully utilize hardware resources.

The idea is that a compiler or interpreter can recognize a subprogram by label or code, and replace it by a high-performance built-in implementation that leverages available hardware resources. Making this pattern explicit has significant benefits: the recognition task becomes simpler and more efficient, and the Glas system can alert developers when acceleration fails or is deprecated to resist silent performance degradation. The reference implementation remains valuable for fallback, fuzz-testing, and debugging.

Because Glas supports processes, one of the simpler approaches to acceleration is to model a processor that can be programmed with some initial inputs then interactively queried and updated with further requests. It can also be useful to provide the 'program' statically, allowing accelerator code to be specialized at compile time. This is feasible, though it requires some flexibility in how accelerators are recognized or optimized, perhaps tweaking the annotations.

It can be awkward to maintain both the compiler built-in and the reference implementation during experimental phases, but we could work around that by using a dummy reference implementation. It can be difficult to achieve deterministic behavior across general hardware resources, e.g. floating-point with or without extended precision. So, it might be useful to late-bind platform-specific accelerators.

Despite a few weaknesses, the accelerator pattern can significantly improve the utility and extensibility of Glas for high-performance computing.

## Parallelism, Distribution, Concurrency

Even without acceleration, Glas can support a few useful patterns for scaling to parallel or distributed computation.

* transparent futures - values may be computed lazily or in parallel when a function is applied to modify a component value, configurable by annotations
* list stream processing - lists can easily be streamed as futures between list processing functions such as map, traverse, zip, concat
* content-addressed structure - large programs or values can be cached on remote nodes and referenced, which can save bandwidth for distributed processing.

The limitation of these patterns is that communication between parallel components must be explicitly routed by the parent program. For dynamic or interactive communication patterns, this becomes a synchronization and performance bottleneck.

The solution is to accelerate a deterministic concurrency model, such as Kahn Process Networks. An accelerated implementation can non-deterministic scheduling and routing to avoid the bottleneck, yet produce the same deterministic result as the reference implementation.

## Incremental Computation

A rapid edit-compute-feedback cycle is convenient, enabling observation and refinement of the produced artifact. To achieve this, it is necessary to reuse most computation from one cycle to another. 

Glas can support reuse in a couple layers:

* computed module values can be cached
* functions can explicitly be memoized

Purely functional computations cannot observe cache state, but it is acceptable to declaratively annotate that specific subprograms should use caching. With suitable design patterns, together with content-addressing for large persistent values, memoization can be leveraged to reuse much computation from one edit cycle to the next.

Incremental computing is complicated somewhat because large values generally have a non-deterministic representations: the organization of finger-tree nodes for large lists, buffering of log-structured updates, and ad-hoc size heuristics for partitioning large values into content-addressed binary nodes.

## Types and Parametricity 

Glas does not enable a module to hide behavior from its client. However, Glas does allow for error during static computation, so it is possible for a module to analyze the values it constructs or imports and to raise an error if there is a problem. It is feasible to enforce type safety and parametricity within Glas.

To simplify this, Glas supports annotation of subprograms, which can include type annotations. It is also feasible to accelerate type safety analysis.

## Language Modules

Language modules are designed to standardize syntax error handling, simplify optimizations, and support other common language features; this is detailed in a later section. To hide choice of language representation, the file extension is excluded from the module name.


Language modules must define a parser that can process a binary input into a value, reference external modules based on the input, and handle errors robustly and in a standard way. 

Imports needs special attention. They don't need to be a true 'effect' because imports are commutative, idempotent, read-only. However, we must to be cautious about cyclic dependencies, ad-hoc reflection. We could model parsers as taking a dictionary representing available resources, then using types to forbid this value from being captured or used in ad-hoc reflection.

Another major feature that language modules require is the ability to turn binaries into dictionary paths. This isn't a problem for pure functions, but it's a form of reflection and thus difficult to typecheck. 

## Provenance Metadata

In Glas, modularity hides the dependencies and representation of each module. However, for debugging purposes, we might still wish to track values to their origin. It is feasible for a Glas utility to compute not just a binary, but also an associative structure that traces every byte to its contributors. 

Glas also includes some specific operators to help 'name' regions of code to simplify and stabilize provenance metadata across changes in code, or allow further source mapping in case a module is auto-generated by other tools.



## Glas Command Line Utility

### 

