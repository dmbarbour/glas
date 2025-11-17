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

The [program model](GlasProg.md) provides an initial collection of definitions. By convention, these are bound under prefix '%'. Front-end compilers further support conventions for %src, %self.\*, %env.\*, and %arg.\*.

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
  * `(%src.dir FileRegex Src) : Src` - search for files matching a pattern, relative to another Src. When loaded, returns a lexicographically sorted list of FilePath.
  * `(%src.dvcs.git URI Ver Src) : Src` - returns a Src representing a DVCS git repository. If loaded, returns unit. Use '%src.file' to access files within the repository.
  * `(%src.an Annotation Src) : Src` - here Annotation is represented by embedded data. It is not observed by the loader, but is available later in context of runtime reflection on sources, e.g. `sys.refl.src.*`.
* `(%macro Program) : AST` - evaluate a pure, 0--1 arity program that returns an AST representation. The latter is validated, evaluated in an empty namespace (i.e. implicit `t:({ "" => NULL }, AST)` wrapper), then substituted for the macro node. Linking is stage-separated from macro eval, e.g. the returned AST typically has a type such as `Env -> Env` and expects a parameter for linking.

It is feasible to extend Src, e.g. to include HTTP resources or content-addressed data. I might add Mercurial and other DVCS sources. But the above should be adequate for my initial use cases.

It is feasible to relax determinism, e.g. %load and %macro can have non-deterministic outcomes in the general case. But it is difficult to efficiently evaluate a large, non-deterministic namespace. In practice, macros and sources should be deterministic, and we'll warn in most contexts if any non-determinism is detected.

### Controlling Filesystem Entanglement

In my vision for glas systems, users can easily share code by copying folders between workspaces. Although DVCS bindings are preferred for stable dependencies, copying is suitable for notebook-style applications where live coding and projectional editing are integrated with the experience.

To support this vision, we forbid parent-relative (`"../"`) paths in Src constructors, and absolute file paths may only be constructed relative to other absolute file paths or a DVCS source.

### Linear File Dependencies

In general, glas systems shall report a warning if a file is noticed to be loaded twice, even if acyclic or via independent paths. The motive is to favor the *shared library* design pattern (via '%env.\*') in case of shared files, and to structurally simplify live coding.

## User-Defined Syntax

As a supported convention, users may define front-end compilers per file extension in '%env.lang.FileExt'. To bootstrap this process, the glas executable initialy injects a built-in definition for '%env.lang.glas' for long enough to build and extract the configured 'env.lang.glas'. 

The front-end compiler is a 1--1 arity Program implementing a pure function of type `Binary -> AST`, where AST generally represents a closed term of the *Module* type. Compilation is performed by %macro nodes, while fetching sources is handled separately, via %load.

Compilation and linking are stage separated, which has some advantages and disadvantages. A compiler cannot express a closure, e.g. integrating definitions from its own environment. It can only arrange for the generated AST to later link shared libraries. OTOH, this ensures the client controls linking, and that we can share compilers without specializing them per link environment.

## Modules

The basic module type is `Env -> Env`, albeit tagged for future extensibility (see *Tags*). We further reserve prefix '%' for system use, e.g. primitive definitions and front-end compilers. A few reserved names deserve special attention:

* '%env.\*' - implicit parameters or context. Should propagate implicitly across most imports, but definitions may be shadowed contextually. This serves as the foundation for shared libraries, e.g. '%env.libname.op'. Binds to 'env.\*' in the user configuration via fixpoint.
* '%arg.\*' - explicit parameters. This allows a client to direct a module's behavior or specialize a module. It is feasible to import a module many times with different parameters.
* '%self.\*' - open recursion. By externalizing fixpoint to the client, we can express modules in terms of inheritance, override, and mixin composition. 
* '%src.\*' - abstract location '%src' and constructors. When linking a module, the front-end compiler will shadow '%src' in scope.
* '%.\*' - implicit 'private' space for the front-end compiler; starts empty

The glas system provides the initial environment, including an initial '%env.\*', '%self.\*', '%src', and optionally providing runtime version info (or a method to query it) via '%arg.\*'. Front-end compilers must continue this pattern, though ideally the common functions (like wrapping an `Env -> Env` function to set '%src' and '%arg.\*', or common load steps) are shared between them.

Usefully, modules are first-class within the namespace. We can define names to the result of loading a module, for example. 

## Calling Conventions, Adapters, and Tags

It is useful to tag definitions, modules, etc. to support more flexible interpretation and integration. I propose to model tags as a Church-encoded variant, such as:

        f:("Adapters", ((b:("", "Tag"), "Adapters"), Body))

This receives an environment of Adapters, selects Tag, then applies to Body. This generalizes to inspecting adapters and picking one, or selecting multiple adapters non-deterministically. It is not difficult to inverse this structure, i.e. such that adapter inspects component, and some tags may compose that pattern. This design seems convenient for most use cases.

All definitions, modules, and other components should be tagged. Tags should roughly indicate integration, e.g. types and assumptions. A few proposed tags to get started:

* "module" - `Env -> Env`. In this context, input Env is expected to have the '%\*' definitions described in *Modules*. 
* "prog" - `Env -> Program`. Input Env is per call-site, representing access to caller-provided registers, algebraic effects handlers, etc.. Even with an empty Env, the Program type supports input-output via data stack. The caller does not provide program primitives: those are provided at the module layer. 
* "data" - constant embedded data.
* "app" - `Env -> Env`. In this case, the input Env provides system-level runtime effects APIs, e.g. 'sys.tty.\*' for console IO, 'g.\*' for global registers, 'app.\*' as a fixpoint. The returned Env has methods such as 'settings', 'main', and 'rpc'. See [Glas Applications](GlasApps.md).

Beyond these, we could tag Church-encoded lists for *Aggregation*, inheritance graphs for *Multiple Inheritance*, or support other patterns as they are developed. There is some risk of independent communities overloading the same tag, resulting in conflicts. Resolution in that case is left to convention, communication, and de facto standardization.

## Inheritance

By externalizing module fixpoints to '%self', we can express one module as inheriting another, sharing the same self. The inheriting module can override definitions, integrating with any mutual recursion through self. 

To support this implicitly, a front-end compiler rewrites names based on usage context:

* defining name - use name directly
* keyword 'prior' name - use name directly
* otherwise - instead use '%self.name'

Aside:* I favor the term 'prior' instead of the more conventional 'super'. I believe it has better connotations for most use cases.

### Multiple Inheritance

It is awkward to model multiple inheritance using `Env->Env`. Direct composition results in shared ancestors being invoked many times. Instead, we'll want an intermediate representation of an inheritance graph, to which we apply a linearization algorithm such as C3 to eliminate shared ancestors and merge adjacents as priors. This is feasible, though we'll need a tag to distinguish inheritance graphs from the basic module type.

*Note:* The namespace does not provide multiple inheritance as a built-in because linearization algorithms are ultimately heuristic in nature. Instead, linearization is left to %macro nodes.

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

The proposed convention in glas is to represent hierarchical structure in a 'flat' namespace. There may be dotted paths in names, such as "foo.bar", but it's ultimately just a bigger name. The main alternative is to define "foo" as an Env, then treat syntax "foo.bar" as extracting "bar" from that Env. However, the flat namespace greatly simplifies access control and aliasing via translations, or updating 'deep' definitions.

## Indexed Modularity

An interesting opportunity is to model modules as indexing other modules. This could be supported at multiple layers, e.g. a module that knows where all the good DVCS sources are, or one that provides access to a searchable collection of partially-evaluated shared libraries in in '%env.\*'. This is possible due to the first-class nature of glas modules and sources.
