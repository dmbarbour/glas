# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and the liquid-to-solid creation analogous to staged metaprogramming. It is also a backronym for 'general language system', something glas aspires to be. Design goals orient around compositionality, extensibility, scalability, live coding, staged metaprogramming, and distributed systems programming.

This document provides an overview of the main components of glas and how they fit together. Order is roughly bottom-up.

## Data

All [glas data](GlasData.md) is logically encoded into binary trees with edges labeled 0 or 1. The actual under-the-hood representation may be specialized for performance, guided by annotations.

## Namespace

The [glas namespace](GlasNamespaces.md) is modeled as an extended lambda calculus supporting reification of environments and other useful features. The namespace AST is plain old glas data. Linking is modeled in terms of applying functions instead of ad hoc compiler magic. Namespaces may be very large, relying on lazy evaluation and modularity for performance. 

The namespace can model tagged definitions and stateless objects.

- Tags are modeled in terms of selecting a name from a case adapter.
- Basic objects are `Env -> Env -> Env` in roles `Base -> Self -> Instance`. 
  - Base supports mixin composition or linking to host.
  - Overrides are based on open recursion through Self.
  - Instantiation involves fixpoint to close recursion.

Modularity and extensibility of glas systems relies on tags and objects. These patterns are further detailed in the namespace document.

## Modules and Modularity

Source files are compiled into modules by a front-end compiler. Modules are expressed as a "module"-tagged `Src -> Object` namespace term. To import a module, we provide the Src used to load the file and link object base to the host's '%\*' pseudo-global namespace. This should includes program constructors and functions to transitively load and compile more modules:

- `(%load Src) : optional Binary` - Load abstract source file at compile-time. 
  - If the source does not exist, returns none. Diverges for any other error. 
  - Warns if any file is loaded twice, or a repo is loaded through two paths.
- `(%macro P) : Any` - 0--1 arity program P must return closed-term namespace AST.
  - Useful for metaprogramming in general. In this case, for front-end compilers. 
- `%env.lang.FileExt : Compiler` - convention; see *User-Defined Syntax* below. 
- `(%src SrcPath Src) : Src` - Constructs an abstract source path. 
  - Abstracted for location independence, but accessible to reflection APIs.
  - Always relative to simplify tracing and control (e.g. DVCS redirects).
- `(%src.file FilePath) : SrcPath` - relative to prior file or repo root. 
  - Forbids parent-relative ("../") and absolute filepaths.
  - Forbids files and subfolders whose names start with ".".
- `(%src.dvcs (url:..., ref:...)) : SrcPath` - ref to DVCS repo.
- `(%src.dvcs.redirect P) : SrcPath` - program to rewrite DVCS refs.
- `(%src.note Data) : SrcPath` - annotations for reflection APIs.

The proposed restrictions on filepaths ensure folders are location-independent, easy to package and share. If the warnings discouraging filesystem-layer sharing are heeded, we mitigate need for shotgun edits to perform filesystem-layer refactoring and restructuring. DVCS redirects are useful for whole-system versioning and curation: we can redirect an unstable branch to a stable tag, content-addressed hash, or community-controlled fork.

Other than 'importing' a module, instantiating the object, we can also 'include' a module to apply it like a mixin. The included module shares Self and performs a `Base -> Base` edit. This is useful for inheritance and override of user configurations, or patching a library before including it. However, include isn't friendly to lazy loading.

## Pseudo-Global Environment

By convention, '%\*' serves a role similar to a global namespace. This isn't truly a global namespace, but it is linked transitively through the module system by default, providing primitive program constructors like '%loop' and functions to support modularity like '%load'. The '%env.\*' volume is reserved for user-defined shared libraries and applications.

## User-Defined Syntax

A minimal front-end compiler is a namespace object that defines a 'compile' method: a "prog"-tagged program, 1--1 arity, that receives loaded file data (optional binary) and returns a closed-term module AST. We'll usually invoke this method via '%macro'. Compilers are encouraged to also provide ad-hoc interfaces for tooling and extension, e.g. syntax highlighting or customization of parse rules.

By convention, when loading a file we search for a front-end compiler at '%env.lang.FileExt'. This supports user-defined syntax per file extension. Users may define compilers locally for a project-specific DSL, or globally as part of the user configuration.

Users aren't limited to textual syntax. Binary formats are useful for graphical programming or tool-assisted development of embedded data. 

## Standard Syntax

To get started, the glas system specifies a [".glas" syntax](GlasLang.md). This is intended to be the primary textual syntax of glas systems, with user-defined syntax mostly supporting DSLs, graphical programming, or user extensions to this syntax. 

## Programs

A glas module expects [primitive program constructors](GlasProg.md) when linked, such as '%do' and '%loop'. The program is an abstract data type, e.g. `(%do P1 P2)` returns an abstract program that can later be integrated into a '%loop'. The program model is relatively simple: structured procedures operating on a data stack and registers, and the data is all binary trees.

The program model is not wholly independent of the namespace model: registers are linked through the namespace, and we leverage the namespace for algebraic effects. But the namespace can be erased with simple compile-time optimizations.

## Annotation and Acceleration

Annotations are structured comments embedded in the namespace AST. Annotations should not affect formal behavior of a valid program, but they may influence performance, verification, instrumentation, and reflection. For example, we might use `(%an.log Chan Message)` for logging, `%an.lazy.spark` to guide parallelism, and `(%an.arity 1 2)` to verify data stack arity of a subprogram.

Performance of glas systems relies on annotation-guided *acceleration*. An annotation such as `(%an.accel %accel.list.concat)` asks a runtime to substitute an annotated reference implementation with a built-in. The built-in can leverage hidden implementation details, e.g. representing large lists into finger-tree ropes. The runtime is encouraged to validate the reference implementation before replacing.

By accelerating *interpreters*, e.g. for a memory-safe subset of Vulkan or OpenCL, we can integrate high-performance computing into glas systems while avoiding awkward semantics and safety concerns of FFI.

Runtimes are free to develop or deprecate annotations. Several ideas are proposed in the program model document.

## Configuration

A configuration is a root module in a glas system. In my vision, a small, filesystem-local user configuration inherits from a large, curated community or company configuration in DVCS. The final environment may define hundreds of applications and shared libraries. Performance depends on lazy loading and incremental compilation. Correctness relies on whole-system versioning, supported by user discipline and via DVCS redirects.

To initially compile the configuration file, we need a built-in compiler. To support modularity, we also link '%env.lang.glas' to the built-in. But the configuration may define its own compiler. If so, we'll attempt to bootstrap and verify a stable fixpoint is reached within a few cycles.

The primary outputs from a configuration are the final '%env.\*' environment and 'glas.\*' runtime configuration options. We can configure the runtime then run an application by name.

## Applications

A basic [application](GlasApps.md) is an "app"-tagged object that defines transactional methods such as 'step', 'http', 'rpc'. When instantiated, the application object is linked to state registers and 'sys.\*' effects APIs. The runtime repeatedly executes 'step', perhaps concurrently (on non-deterministic choice), but other methods are driven by based on external events.

The basic application model is designed to support live coding, reactivity, concurrency, and distribution. However, it performs poorly without sophisticated transaction-loop optimizations. In the short term, developers should use 'step' as a single-threaded event dispatch loop and leverage sparks for parallelism.

Applications are often defined in the '%env.\*' environment. This is useful because we'll often want to compose applications (like shell scripts or widgets) or extend them (mixins, flavors, overrides). 

## Scripts

A script is a glas module that defines one application, 'app'. Unlike configurations, scripts do not provide their own environments. They link the configuration's final '%\*' environment.

*Note:* Script files are easy to share but a hassle to maintain. A tool to conveniently add DVCS repos to user configurations may prove a more robust solution.

## Command Line Interface (CLI)

The [glas CLI](GlasCLI.md) is the initial user interface to glas systems. Primary behavior is to load a configuration then run a named application or script. But the CLI can also support debugging, cache management, etc.. 
