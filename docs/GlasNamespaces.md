# Glas Namespaces

In my vision for glas systems, huge namespaces define runtime configuration, shared libraries, and applications. Definitions can be distributed across dozens of DVCS repositories, referencing stable branches or hashes for horizontal version control. We rely on laziness to load and extract only what we need, and incremental compilation to reduce rework. 

## Design Overview

Lambda calculus can express namespaces, e.g. a `let x = X in Expr` becomes `((λx.Expr) X)`. However, namespaces are second-class in the lambda calculus. I propose to extend lambda calculus with first-class environments. 

First, we can reify the environment. The program can generate an abstract record `{ "x" = x, "y" = y, ... }` for all names in scope. For binding, we can introduce `((ν"Prefix".Body) Env)`, binding "PrefixName" in Body to "Name" in Env. This shadows a prefix like lambdas shadow names. A simple record accessor can be expressed via empty prefix `((ν.name) Env)`. For control, we introduce translations that modify a subprogram's view of its current environment, also influencing the reified environment key names.

Lambda Calculus:

* *application* - provide an argument 
* *arg binding* - bind argument to name in body
* *name* - substitute definition of name in scope

Namespace extensions:

* *reification* - capture current view of environment 
* *env binding* - bind environment to prefix in body
* *translation* - modify body's view of environment

Utility extensions:

* *annotations* - structured comments for instrumentation, optimization, verification
* *data* - opaque to the calculus, but embedded for convenience
* *ifdef* - flexible expression of defaults, optional defs, merge-union, mixins
* *fixpoint* - a built-in fixpoint for lazy, recursive defs

The runtime provides an initial environment of names, supporting a [program model](GlasProg.md) and various *Module* conventions (e.g. %src, %env.\*, %self.\*).

## Abstract Syntax Tree (AST)

Namespaces encoded as structured glas data. This serves as an intermediate representation for namespaces and programs in glas systems.

        type AST =
            | Name                  # substitute definition
            | (AST, AST)            # application
            | f:(Name, AST)         # bind name in body, aka lambda
            | e:()                  # reifies current environment
            | b:(Prefix, AST)       # bind argument to prefix in body
            | t:(TL, AST)           # modify body's view of environment
            | a:(AST, AST)          # annotation in lhs, target in rhs 
            | d:Data                # embedded glas data, opaque to AST
            | c:(Name,(AST,AST))    # ifdef conditional expression
            | y:AST                 # built-in fixpoint combinator
        type Name = binary excluding NULL
        type Prefix = any binary prefix of Name
        type TL = Map of Prefix to (Optional Prefix) as radix-tree dict

The AST representation does not include closures, thunks, reified environments, etc. necessary for intermediate steps during evaluation. Those shall have ad hoc, abstract, runtime-specific representations.

## Evaluation

Evaluation of an AST is a lazy, substitutive reduction in context of an environment that maps a subset of names to definitions (i.e. `type Env = Name -> optional AST` with caching). In most context, the initial environment is empty, thus insisting that AST terms are 'closed' or combinators, often of type `Env -> Env`. 

* application, lambdas, and names: as lazy lambda calculus evaluation

* translation `t:(TL, Body)` - translates Body's view of names in the current environment through TL. Without reification, translation can serves a role for import aliases and access control. Translation becomes semantically significant insofar as it influences reification.
* reification `e:()` - returns an abstract `Env` representing all names in scope, i.e. `{ "x" = x, "y" = y, ...}`, albeit lazily constructed.
  * Empty environment can be expressed as `t:({ "" => NULL }, e:())`. 
  * Specified prefix can be selected by `t:({ "" => Prefix }, e:())`. 
* env binding - when applied `(b:(Prefix,Body), Env)`, binds Env to Prefix context of evaluating Body. That is, PrefixName now refers to Name in Env if defined.
  * Patch-based semantics: if Name is not defined in Env, fall back to prior definition.
  * Consider `t:({ Prefix => NULL }, b:(Prefix, Body))` to clear prefix before binding.
  * Record-selector pattern is `t:({""=>NULL}, b:("", Name))`, Env as first-class dict.

* annotations `a:(Anno, Target)` - Semantically inert: logically evaluates as Target. In practice, we evaluate Anno to an abstract Annotation using compiler-provided Annotation constructors - by convention `%an.name` or `(%an.ctor Args)`. We then use this Annotation to guide instrumentation, optimization, or verification of Target.
* data `d:Data` - evaluates to itself. Abstract to AST evaluation, but may be observed when applying primitive Names.
* ifdef `c:(Name, (L, R))` - evaluates to L if Name is defined in current environment, otherwise R. 
* fixpoint - built-in fixpoint for convenient expression and efficient evaluation


### Reference Implementation

TBD. Will write something up in Haskell or F# and get back on this.

Some requirements or desiderata:

* Lazy evaluation in general, necessary for lazy loading etc.
* Memoized lazy partial evaluation within function bodies.

Performance at this layer isn't essential for my use case, though no reason to avoid performance if it's a low-hanging fruit. But I'll be mostly focusing on the Program layer for performance of glas systems.

## Translation

TL is a finite map of form `{ Prefix => Optional Prefix }`. To translate a name via TL, we find the longest matching prefix, then rewrite that to the output prefix. Alternatively, if the rhs has no prefix, we'll treat the name as undefined.

The TL type works best with prefix-unique names, where no name is a prefix of another name. Consider that TL `{ "bar" => "foo" }` will convert `"bard"` to `"food"`, and it's awkward to target 'bar' alone. To mitigate, we logically add suffix `".."` to all names, and front-end syntax will discourage `".."` within user-defined names. The combination of logical suffix and front-end support allows translation of 'bar' together with 'bar.\*' via `{ "bar." => "foo." }` or 'bar' alone via `{ "bar.." => "foo.." }`. There is a possibility of translation *removing* the suffix that should be handled correctly by an evaluator's internal representation of names or environments. 

Sequential translations can be composed into a single map. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that output prefixes in A match as many input prefixes in B as possible, then translate A's outuput prefixes as names (longest matching prefix). To normalize, optionally erase new redundancy.

## Loading Files

Files are accessible through a few provided names. Loading files is staged, separate from runtime execution of the application program unless dynamic %eval is also enabled.

* `(%load Src) : d:Data` - loads external resources at compile-time, returning opaque data. This operation may diverge if Src is malformed or unreachable.
* `%src : Src` - by convention, this Src represents the file currently being compiled. It is expected that relative Src constructors take this Src as the root.
* `%src.*` - constructors for abstract Src data. All constructors are relative to another Src. 
  * `(%src.file FilePath Src) : Src` - evaluates to an abstract Src representing a FilePath relative to another Src. When loaded, returns an *optional binary*, treating 'does not exist' as a valid state. For other errors (e.g. unreachable, permissions issues) the loader diverges and logs a compile-time error.
    * Note: glas systems forbid `"../"` relative paths and absolute paths relative to relative paths. See *Controlling Filesystem Entanglement* for motivations.
    * Note: absolute paths are still contextually relative, e.g. absolute paths within DVCS repository are relative to repository root. However, initial 
  * `(%src.dvcs.git URI Ver Src) : Src` - returns a Src representing a DVCS git repository. If loaded, returns unit. Use '%src.file' to access files starting at repo root.
  * `(%src.an Annotation Src) : Src` - here Annotation is represented by embedded data. It is not observed by the loader, but is available later in context of runtime reflection on sources, e.g. `sys.refl.src.*`.
* `(%macro Program) : AST` - evaluate a pure, 0--1 arity program that returns an AST representation. The latter is validated, evaluated in an empty namespace (i.e. implicit `t:({ "" => NULL }, AST)` wrapper), then substituted for the macro node. Linking is stage-separated from macro eval, e.g. the returned AST typically has a type such as `Env -> Env` and expects a parameter for linking.

It is feasible to extend Src, e.g. to include HTTP resources or content-addressed data. I might add Mercurial and other DVCS sources. But the above should be adequate for my initial use cases.

It is feasible to relax determinism, e.g. %load and %macro can have non-deterministic outcomes in the general case. But it is difficult to efficiently evaluate a large, non-deterministic namespace. In practice, macros and sources should be deterministic, and we'll warn in most contexts if any non-determinism is detected.

### Controlling Filesystem Entanglement

Many programming languages allow messy, ad hoc relationships between module system and filesystem, e.g. with "../" paths, absolute file paths, etc.. Glas systems restrict these relationships in order to simplify refactoring, extension, and sharing of code.

First, we forbid parent-relative (`"../"`) paths in Src constructors, and absolute file paths may only be constructed relative to other absolute file paths (or DVCS repo root). This ensures folders can generally be treated as independent packages, easily shared by copying, excepting very few 'toplevel' folders (e.g. for the user configuration) which may reference other absolute paths within the local filesystem.

Second, we hide files and subfolders whose names start with `"."`. For example, if a front-end compiler requests file `".git/config"` from a DVCS repo, this file is treated as non-existent regardless of whether it exists. This matches conventions for hidden or associated structure in the filesystem. Although front-end compilers cannot see these files and folders, a runtime may recognize and utilize `".glas/"` and `".pki/"` and so on for incremental compilation, signed manifests and certificates, etc..

Third, a front-end compiler cannot browse folders, cannot query folder contents. Although ability to browse is convenient for several use cases, alignment of code to folder structure eventually becomes a source of entanglement and embrittlement that hinders refactoring and non-invasive extension. Instead, users may construct indices within files.

### Affine Files

Instead of cyclic dependendencies or even directed-acyclic graphs, I propose *affine* file dependencies: each file is loaded at most once in context of an application or user configuration. When a glas compiler or runtime notices a file is loaded twice, it raises a warning or error.

This restriction simplifies local reasoning, live coding, and metaprogramming. It eliminates risk that updating a file has hidden non-local consequences, and ensures the dependency graph is fully represented within the configuration namespace. The file binary can be replaced transparently by a generated binary by updating a single location, which mitigates need for metaprogramming at the filesystem layer.

Instead of sharing at the filesystem layer, shared libraries are supported by conventions and patterns at the namespace layer. See *Shared Libraries* below.

## Shared Libraries

A user configuration is a module that defines `env.*`. The namespace module type supports open fixpoint, such that modules can be defined in terms of inheriting, overriding, and extending other modules. The glas compiler or runtime further performs a toplevel fixpoint, linking `%env.*` to the configured `env.*`. This providies the configured environment as pseudo-primitives, piggybacking common `{ ..., "%" => "%" }` translation rules for propagating primitives across scopes.

A consequence of this design is that the user configuration can easily inherit from a community or company configuration that defines most shared libraries. The user is free to tweak a few shared libraries via overrides.

### Applications as Components

In addition to libraries, applications are frequently defined within `env.*`. A naming convention `env.Appname.app` is imposed by the [glas CLI](GlasCLI.md). The CLI also supports defining applications as external scripts, outside the configuration file. But, in my vision for glas systems, most applications will ultimately be named under `env.*`.

The motive for this is to support inheritance, override, extension, and composition of applications. To support inheritance and override, the application type supports yet another latent fixpoint step, much like modules. Indeed, we'll essentially define the application type as a module with a few conventions on entry points and staging.

By providing applications as shared library components, and developing at least one front-end syntax for ergonomic composition of applications, glas systems should become very 'scriptable'.

## User-Defined Syntax

As a supported convention, users may define front-end compilers per file extension in '%env.lang.FileExt'. To bootstrap this process, the glas executable initialy injects a built-in definition for '%env.lang.glas' for long enough to build and extract the configured 'env.lang.glas'. 

The front-end compiler is a 1--1 arity Program implementing a pure function of type `Binary -> Module`, where *Module* is described below. This compilation is performed via %macro nodes, while fetching sources is handled separately via %load.

Importantly, output of a front-end compiler is plain old data. This prevents the compiler from linking its own environment into the compiled module, forcing a stage separation between compilation and linking. Any shared definitions must either be integrated into the compiler output (per module) or provided through shared libraries in '%env.\*'.

## Modules

The basic module value returned by a front-end compiler is a namespace AST representing a closed term of type `Env -> Env`. The module is 'linked' by providing the input `Env`. The returned `Env` represents exported definitions. For clarity and extensibility, the basic module is further tagged "module" (see *Tagged Definitions*), and input `Env` is structured under a few conventions:

* '%\*' - primitive definitions are provided ad hoc prefix '%' prefix. The motive is to simplify both visual recognition of primitives and concise default propagation via `{ "%" => "%" }` translation rules.
  * '%env.\*' - initially bound to 'env.\*' in the user configuration (via fixpoint), then propagated by default alongside primitive definitions. This pseudo-global namespace provides the foundation for *Shared Libraries*.
* '$\*' - inputs to be specialized per 'import', not propagated implicitly
  * '$self' - open fixpoint of module exports, supports inheritance and override.
  * '$src' - abstract data representing a source path for the module being linked. 
  * '$args' - in case a front-end compiler introduces a syntax to explicitly parameterize imports, e.g. `import foo(A,B,C) as f`, I propose to input a Church-encoded list of namespace terms via '$args' (and named args via '$kwargs').

Similarly, it is feasible to reserve export prefixes to support *Aggregation* patterns, such as generating a table of contents for notebook applications. However, module-layer aggregation is not recommended because it conflicts with lazy loading. Instead, I anticipate aggregation at the application layer.

### Inheritance and Override

The '$self' argument supports expressing modules in terms of inheritance and overrides. This can be useful in many cases, though to effectively leverage it requires explicit design of modules with overrides and extensions in mind, and syntactic support from the front-end compiler (e.g. bind to '$self' names in definition bodies by default, require a keyword to refer to 'prior' definitions).

### Import vs. Include

We'll usually interpret `Env -> Env` as linking a module to its context. But another useful interpretation is an *environment rewrite*, where the input `Env` may directly reference and rewrite definitions in scope. A front-end compiler could easily support both integrations, perhaps distinguishing keywords 'import' (link, local fixpoint) versus 'include' (rewrite current environment). 

Note that 'include' must modify '$src' in scope, but arguments such as '$self' would bind to the host's '$self'. We can conveniently express inheritance and override in terms of 'including' the module into the initially empty namespace at the start of the inheriting module. 

*Note:* Beyond import and include, we could introduce specialized or user-defined link keywords to support *Aggregation* patterns, to determine which exports are implicitly integrated and how to integrate them. 

## Tagged Definitions and Adapters

Tags are extremely convenient for extensibility. The community can easily introduce new tags or deprecate old ones, providing a foundation for evolving systems. Tags can readily support evolving calling conventions, program types, even application models. Although less precise than type systems, it is easy to detect, report, and comprehend errors due to unhandled or incompatible tags.

In my vision for glas systems, every user definition is tagged. Modules generated by front-end compilers are also tagged. (Primitives and compiler-internal definitions are not necessarily tagged.) I propose to represent tags via Church-encoding that leverages namespace extensions:

        template tag<"Tag"> = 
            f:("Body", (f:("Adapters", 
                ((b:("", "Tag"), "Adapters"), "Body"))))
        tag<"prog">(Definition)

Basic tags select and apply an adapter function. If there is no adapter for a given tag, we can raise an appropriate error at compile time. Intriguingly, this formalization supports adaptive definitions, e.g. we could support a tag that examines the adapter then heuristically selects a 'best' definition in context. But, in practice, I'd prefer to push sophisticated analysis of context to another stage, e.g. partial evaluation with algebraic effects.

A useful initial set of tags:

* "data" - embedded data (`d:...`)
* "prog" - abstract program
* "cond" - selector, body of '%cond' or '%loop' (not a valid program by itself)
* "call" - `Env -> Def` - receive caller's environment, return another tagged definition
* "module" - the basic `Env -> Env` module 
* "app" - wrap a module that defines recognized names like 'settings' and 'start' 
  * the module is also tagged, e.g. `"app":"module":(Env -> Env)`

Beyond these tags, I already anticipate a few more to support *Aggregation* patterns, *Multiple Inheritance*, or composing alternative expressions of behavior such as hardware description language, process networks, or interaction nets. Tags may be user-defined, though if only user-defined the definition will need some explicit post-processing to 'run' it. 

In my vision for glas systems, tags are concise and locally meaningful such as "prog" and "app". Developers can introduce GUID or URL tags if they fear naming conflicts, but I hope communication with the community will be sufficient to mitigate conflict in most cases.

I'll eventually want tags to support *Aggregation* patterns via Church-encoded lists, *Multiple Inheritance* via linearization and deduplication of inheritance graphs, and other useful features. Tags make it easy to introduce and integrate new types as needed, subject to de facto standardization.

## Multiple Inheritance

The basic `Env->Env` module type with '$self' easily models single inheritance, and can awkwardly model mixins. However, for complicated cases, manual linearization is painful and error-prone. Automatic linearization is feasible if we construct an intermediate 'inheritance graph' that can be composed, deduplicated, reduced, and ultimately 'compiled' to the `Env -> Env` type. This would require support from front-end compilers, but it seems technically feasible.

## Controlling Shadows

Shadowing of names can be useful, especially in context of *Aggregation* (see below). However, accidental shadowing can be a source of subtle errors. To mitigate this, I propose to report warnings or errors upon shadowing by default, then allow annotations to suppress warnings locally.

## Incremental Compilation

Lazy evaluation can simplify incremental computing. Each thunk serves as a memo cell and tracks which thunks must be recomputed if its input ever changes. We can especially build a few thunks around %load and %macro nodes.

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. Like 42. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Aggregation

Language features such as multimethods, typeclasses, declaring HTTP routes, etc. require constructing and sharing tables. In context of functional programming, we can express iterative construction in terms of repeatedly shadowing a definition with an updated version. We can express sharing via fixpoint if we're careful to avoid datalock. 

An interesting opportunity is to use Church-encoded lists to aggregate tagged ASTs for later processing. This can be very flexible, and is directly analogous to the Writer monad.

Aggregation across modules is hostile to lazy loading. But we could allow aggressive, automatic aggregation at the *application* layer.

## Hierarchy

The proposed convention in glas is to represent hierarchical structure in a 'flat' namespace. There may be dotted paths in names, such as "foo.bar", but it's just one big name. The main alternative is to define "foo" as an Env, then treat syntax "foo.bar" as extracting "bar" from that Env. However, the flat namespace greatly simplifies access control and aliasing via translations, or updating 'deep' definitions.

## Indexed Modularity

An interesting opportunity is to model modules as indexing other modules. This could be supported at multiple layers, e.g. a module that knows where all the good DVCS sources are, or one that provides access to a searchable collection of partially-evaluated shared libraries in in '%env.\*'. This is possible due to the first-class nature of glas modules and sources.
