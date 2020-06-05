# Glas Language Design

Glas is a language system designed for large-scale reflective metaprogramming.

## Module System

A Glas module computes a value. A parse-time reference to a module will return that module's value, to support further computation by other modules. Glas values are simple, structured data formed of lists, dictionaries, and natural numbers, so modules have no hidden data or behavior. However, modules do hide their representation and dependencies.

Within a filesystem, modules are concretely represented by local files and subfolders, or external packages discovered by searching a `GLAS_PATH` environment variable. Glas does not specify a package manager at this time. The current intention is to leverage the Nix or Guix package managers, at least until they prove deficient.

Files support user-defined syntax or binary representations. To parse a file with the `.xyz` extension, the Glas tool utility will search for a local module or external package named `language_xyz` whose value describes how to process this file and ultimately produce a value. Exceptions exist for bootstrapping.

Language modules are designed to standardize syntax error handling, simplify optimizations, and support other common language features; this is detailed in a later section. To hide choice of language representation, the file extension is excluded from the module name.

The value of a folder is the value of its contained 'public' module, if defined, otherwise a simple dictionary of values for contained modules. Use of a public module enables folders to hide their representation or compute non-dictionary values. Glas packages are always represented by folders.

Glas forbids direct references across folder boundaries. Files may only reference other modules within the same folder, or external packages. This constraint simplifies reasoning, refactoring, and organization of large Glas programs. Further, Glas also forbids cyclic dependencies: module references must form a directed acyclic graph. A module reference is equivalent to a module's value.

Glas does not specify a package manager. The current intention is to leverage the Nix or Guix package managers.

## Data Model

Glas values are trees composed of immutable dictionaries, variants, lists, and natural numbers such as `(arbid:42, data:[1,2,3], extid:true)`. Keys within a dictionary are short, symbolic strings. The empty dictionary `()` can serve as a unit value.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsv:(...)` would be represented by a dictionary exclusively containing either `rgb` or `hsv`. Symbolic values such as booleans and enumerations are encoded as variants with a unit value.

Glas lists and natural numbers are logically encoded by recursive tree structures similar to `type Nat = succ:Nat | zero` and `type List a = cons:(head:a, tail:List a) | nil`. However, for performance reasons, the concrete representation is abstracted.

Glas lists are used for stacks, queues, arrays, matrices, binaries, and stream processing. To efficiently support a broad variety of use cases in context of copy-on-write updates, lists are usually represented by some variation of finger-tree ropes. Stream processing is a special case: to support pipeline parallel computation over arbitrarily long lists, intermediate lists will often be implicitly represented using futures or bounded-buffer channels.

Glas natural numbers have no hard upper limit, but performance may suffer at the threshold for bignum arithmetic. Negative, rational, and other useful number types must be modeled explicitly within Glas.

Glas specifies a [Glas Object](GlasObject.md) (aka 'glob') representation for storage, distribution, manipulations, and caching of Glas values. Glas Object features use of content-addressed references and log-structured updates to work efficiently with very large values.

## Computation Model


## Compilation via Binary Extraction

A binary is simply a list of natural numbers between 0 and 255. 

A subset of Glas modules will compute binary values that are externally meaningful or useful, perhaps representing an executable file, a tarball, or a docker container. A Glas command line tool should provide methods to extract binary values for external use.

The envisioned pattern: an application behavior package should sufficiently describe application behavior. A separate package will take the value from the application behavior package and provide it as input to the compiler package to compute a binary executable. This binary can be extracted to a file then installed on the user's path.

Compilers in Glas become normal, user-defined functions.

Separating the application behavior from the compiler function ensures the application behavior is accessible for analysis, extension, or integration with other applications. It also simplifies potential for cross-compilation or tweaking configurable parameters.

In the end, Glas does not specify a runtime behavior model. However, de-facto standard behavior models should emerge when the community develops compilers and common intermediate representations of runtime behavior.

## Accelerated Computation (Accelerator Pattern)

Glas does not directly support floating-point arithmetic, SSE, GPGPU, FPGA, and similar hardware-accelerated computation features. However, performance is always a useful feature whether at compile-time or runtime, and it's upsetting to waste capacity. So, Glas provides an indirect mechanism to fully utilize hardware resources.

The idea is that a compiler or interpreter can recognize a subprogram by label or code, and replace it by a high-performance built-in implementation that leverages available hardware resources. Making this pattern explicit has significant benefits: the recognition task becomes much simpler and more efficient, and the Glas system can alert developers when acceleration fails or is deprecated to resist silent performance degradation. The reference implementation remains valuable for fallback, fuzz-testing, and debugging.

Accelerators cannot introduce new value types or behaviors, only performance extensions. But a compiler or interpreter can use optimized internal representations for values produced and consumed by accelerators. For example, if we accelerate computations over floating-point matrices, a specialized matrix representation could reduce data conversion overheads.

Glas supports staged accelerators with static parameters. Instead of independently accelerating a dozen matrix manipulation functions, we could develop a unified matrix accelerator that takes as input a static input representing a matrix processing pipeline. This design simplifies organization of accelerators, and also reduces complications for compiling a matrix pipeline to a GPGPU.

This accelerator pattern does have a few weaknesses.

First, development of new accelerators often involves rapid changes in vision, behavior, and scope. In this context, maintaining both a reference implementation and the built-in is intolerable. This can be mitigated by temporarily eliding the reference implementation, matching only on accelerator label. The accelerator pattern becomes is compiler's pseudo-operator in this case.

Second, it can be difficult to achieve deterministic behavior across general hardware resources. For example, some but not all CPUs might invisibly use extended precision for floating-point arithmetic. To handle this, either the built-in must ensure deterministic results (e.g. disable or simulate extended precision) or we should favor target-specific accelerators with program switches and late-binding. Analogous to OpenGL vs. Direct3D philosophy of acceleration.

Despite a few weaknesses, the accelerator pattern can significantly improve the utility and extensibility of Glas software systems for high-performance computing, both for compile-time and runtime computation.

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

Glas does not enable a module to hide behavior from its client. However, Glas does allow for error during static computation. It is possible for a module to raise an error if it notices problems. It is feasible to enforce type safety within Glas, or to require that certain functions do not directly observe certain inputs. 

## Provenance Metadata

In Glas, modularity hides the dependencies and representation of each module. However, for debugging purposes, we might still wish to track values to their origin. It is feasible for a Glas utility to compute not just a binary, but also an associative structure that traces every byte to its contributors. 

Glas also includes some specific operators to help 'name' regions of code to simplify and stabilize provenance metadata across changes in code, or allow further source mapping in case a module is auto-generated by other tools.


## Language Modules





## Glas Command Line Utility

### 

