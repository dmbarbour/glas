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

Source files are compiled into modules by a front-end compiler. Basic modules are expressed as a "module"-tagged `Src -> Object` namespace terms. To import the module, we provide Src for the module's source file, then link object base with the host's '%\*' pseudo-global namespace. This must include functions to further load and compile more modules:

- `(%load Src) : optional Binary` - Load abstract file source at compile-time. 
  - If the file does not exist, returns none. Diverges for any other error. 
  - Warns if any file is loaded twice, or a repo is loaded through two paths.
- `(%macro P) : Any` - 0--1 arity program P must return closed-term namespace AST.
  - Useful for metaprogramming in general. In this case, for front-end compilers. 
- `%env.lang.FileExt : Compiler` - convention; see *User-Defined Syntax* below. 
- `(%src SrcArc Src) : Src` - Constructs an abstract source path. 
  - Abstracted for location independence, but accessible to reflection APIs.
  - Always relative to simplify tracing and control (e.g. DVCS redirects).
- `(%src.file FilePath) : SrcArc` - relative to prior file or repo root. 
  - Forbids parent-relative ("../") and absolute filepaths.
  - Forbids files and subfolders whose names start with ".".
- `(%src.dvcs RepoRef) : SrcArc` - ad hoc ref to DVCS repo, e.g. URL and branch.
  - RepoRef is plain old data; what is recognized depends on runtime.
- `(%src.dvcs.redirect P) : SrcArc` - program P is RepoRef--RepoRef function.
- `(%src.note Data) : SrcArc` - annotations for reflection APIs.

The proposed restrictions on filepaths ensure folders are location-independent, easy to package and share. If the warnings discouraging filesystem-layer sharing are heeded, we mitigate need for shotgun edits to perform filesystem-layer refactoring and restructuring. DVCS redirects are useful for whole-system versioning and curation: we can redirect an unstable branch to a stable tag, content-addressed hash, or community-controlled fork.

Other than 'importing' a module, instantiating the object, we can also 'include' a module to apply it like a mixin. The included module shares Self and performs a `Base -> Base` edit. This is useful for inheritance and override of user configurations, or patching a library before including it. However, include isn't friendly to lazy loading.

## Pseudo-Global Environment

By convention, '%\*' serves a role similar to a global namespace. This isn't truly a global namespace, but it is linked transitively through the module system by default, providing primitive program constructors like '%loop' and functions to support modularity like '%load'. The '%env.\*' volume is reserved for user-defined shared libraries and applications.

## User-Defined Syntax

A minimal front-end compiler is a namespace object that defines a 'compile' method: a "prog"-tagged program, 1--1 arity, that receives loaded file data (optional binary) and returns a closed-term module AST. We'll usually invoke this method via '%macro'. Compilers are encouraged to also provide ad-hoc interfaces for tooling and extension, e.g. syntax highlighting or customization of parse rules.

By convention, when loading a file we search for a front-end compiler at '%env.lang.FileExt'. This supports user-defined syntax per file extension. Users may define compilers locally for a project-specific DSL, or globally as part of the user configuration.

Users aren't limited to textual syntax. Binary formats are useful for graphical programming or tool-assisted development of embedded data. 

## Standard Syntax

The glas system specifies a [".glas" syntax](GlasLang.md). This is intended to be a primary textual syntax of glas systems, with user-defined syntax supporting DSLs, graphical programming, or user extensions to this syntax. 

## Programs

A glas module expects [primitive program constructors](GlasProg.md) when linked, such as '%do' and '%loop'. The program is an abstract data type, e.g. `(%do P1 P2)` returns an abstract program that can later be integrated into a '%loop'. The program model is relatively simple: structured procedures operating on a data stack and registers, and the data is all binary trees.

The program model is not wholly independent of the namespace model: registers are linked through the namespace, and we leverage the namespace for algebraic effects. But the namespace can be erased with simple compile-time optimizations.

## Annotation and Acceleration

Annotations are structured comments embedded in the namespace AST. Annotations should not affect formal behavior of a valid program, but they may influence performance, verification, instrumentation, and reflection. For example, we might use `(%an.log Chan Message)` for logging, `%an.lazy.spark` to guide parallelism, and `(%an.arity 1 2)` to verify data stack arity of a subprogram.

Performance of glas systems relies on annotation-guided *acceleration*. An annotation such as `%an.accel.list.concat` might ask a runtime to substitute an annotated reference implementation of a list concat function with a built-in. The built-in can leverage hidden implementation details, e.g. representing large lists into finger-tree ropes. The runtime is encouraged to validate the reference implementation before replacing.

By accelerating *interpreters*, e.g. for a memory-safe subset of Vulkan or OpenCL, we can integrate high-performance computing into glas systems while avoiding awkward semantics and safety concerns of FFI.

Runtimes are free to develop or deprecate annotations. Several ideas are proposed in the program model document.

## Applications

A basic [application](GlasApps.md) is an "app"-tagged object that defines transactional methods such as 'step', 'http', and 'rpc'. The application-object base is linked to runtime-provided state registers and 'sys.\*' effects APIs. The runtime repeatedly executes 'step' in separate transactions. Other methods, like 'http' and 'rpc', are event-driven. 

This transaction-loop model is very useful for live coding, reactivity, concurrency, and distribution, but requires sophisticated optimizations for performance: replication on non-deterministic choice, optimistic concurrency control, incremental computing, memoization of control flow. Before these optimizations are implemented, developers can still use 'step' as a single-threaded event dispatch loop and leverage sparks for parallelism.

## Configuration

In my vision of glas systems, a small, filesystem-local user configuration inherits from a large, curated community or company configuration in DVCS. The full configuration defines hundreds of shared libraries and applications. Performance for running a specific application depends on lazy evaluation and incremental compilation. System stability relies on whole-system versioning, leveraging DVCS redirects.

To compile the configuration file, we start a built-in compiler for at least the standard ".glas" syntax. The built-in is implicitly injected as a final override of '%env.lang.glas'. If the configuration attempts to define '%env.lang.glas', we'll attempt to bootstrap, recompiling with '%env.lang.glas' pointing to the user's definition.

recompiling the configuration with the compiler it defined for itself. This bootstrap cycle may run once or twice more to verify a stable fixpoint.

Primary outputs from a configuration include ad hoc runtime configuration options in 'glas.\*', and the final '%\*' namespace, especially '%env.\*' where the shared libraries and applications should be defined. Enough to select an application by name and run it, or to effectively run an external script within the configuration.

*Tentative:* I could insert a rule to capture 'env.\*' defined in the configuration as the initial '%env.\*'. Uncertain if this is useful, or just a likely source of confusion. 

## Scripts

A script is a glas module that does not provide its own environment. Instead, the script is logically imported into a configured environment by linking to the configuration's final '%\*' output. The script file may use any file extension or syntax supported by the configuration's '%env.lang.FileExt'. Typically, the script defines an application 'app'.

Scripts are easy to share, but they have a few significant weaknesses. First, it's too easy to accidentally share scripts between users with incompatible environments. This causes frustration and complicates portability. Second, scripts easily escape sight and reach, hindering maintenance. These concerns can be mitigated, but glas systems should generally favor building configurations instead of sharing scripts.

## Command Line Interface (CLI)

The [glas CLI](GlasCLI.md) is the initial user interface to glas systems. Primary behavior is to load a configuration then run a named application or script. But the CLI can also support debugging, cache management, etc.. 
