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

The [program model](GlasProg.md) provides a collection of primitive definitions under prefix '%'. The runtime will also bind '%env.\*' to the configuration's 'env.\*' via fixpoint, supporting shared libraries. The runtime also links '%src' to abstract data representing a user configuration.

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
        type TL = Map of Prefix to (Prefix | NULL) as radix-tree dict

This representation does not include closures, thunks, reified environments, etc. necessary for evaluation. Those must be constructed indirectly through evaluation.

## Evaluation

Evaluation of an AST is a lazy, substutive reduction in context of an environment that maps a subset of names to definitions (i.e. `Name -> optional AST`). 

* application, lambdas, and names: as lazy lambda calculus evaluation

* reification `e:()` - returns an abstract dictionary containing all names in scope, i.e. `{ "x" = x, "y" = y, ...}`. Trick is to make this lazy. 
  * Empty environment is expressed as `t:({ "" => NULL }, e:())`. 
  * Specified prefix is selected by `t:({ "" => Prefix }, e:())`.
* env binding - when applied `(b:(Prefix,Body), Arg)`, binds Arg to Prefix context of evaluating Body. All external names matching Prefix are shadowed in Body, much as lambdas shadow a single name.
* translation `t:(TL, Body)` - translates Body's view of the current environment through TL. Of semantic relevance, TL controls dictionary keys if the environment is reified in Body.

* annotations `a:(Anno, Target)` - Semantically inert: logically evaluates as Target. In practice, we evaluate Anno to an abstract Annotation using compiler-provided Annotation constructors - by convention `%an.name` or `(%an.ctor Args)`. We then use this Annotation to guide instrumentation, optimization, or verification of Target.
* data `d:Data` - evaluates to itself 
* ifdef `c:(Name, (Then, Else))` - evaluates to Then if Name is defined in current environment, otherwise Else. 
* fixpoint - built-in fixpoint for convenient expression and efficient evaluation

### Reference Implementation

TBD. Will write something up in Haskell or F# and get back on this.

Some requirements or desiderata:

* Lazy evaluation in general, necessary for lazy loading etc.
* Memoized lazy partial evaluation within function bodies.

Performance at this layer isn't essential for my use case, though no reason to avoid performance if it's a low-hanging fruit. But I'll be mostly focusing on the Program layer for performance of glas systems.

## Translation

TL is a finite map of form `{ Prefix => (Prefix' | NULL) }`. To translate a name via TL, we find the longest matching prefix, then rewrite that to the output prefix. Alternatively, if output is NULL, we'll treat the name as undefined.

The TL type works best with prefix-unique names, where no name is a prefix of another name. Consider that TL `{ "bar" => "foo" }` will convert `"bard"` to `"food"`, and it's awkward to target 'bar' alone. To mitigate, we logically add suffix `".."` to all names, and front-end syntax can strongly discourage `".."` within user-defined names. This logical suffix enables translation of 'bar' together with 'bar.\*' via `{ "bar." => "foo." }` or 'bar' alone via `{ "bar.." => "foo.." }`. If translation removes the suffix, raise an error. (Alternatively, introduce an `N:FullName` intermediate representation.)

Sequential translations can be composed into a single map. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that output prefixes in A match as many input prefixes in B as possible, then translate A's outuput prefixes as names (longest matching prefix). To normalize, optionally erase new redundancy.

## Loading Files

Modularity is supported by compile-time effects. To prevent compile-time effects from leaking accidentally into the runtime, they are provided indirectly as second-class algebraic effects via the %macro primitive. The primary effect is to load files, returning a binary. This binary may be processed within the macro to generate an AST. Lazy compilation may be expressed in terms of returning an AST that contains more macro nodes.

Viable API:

        ("%macro", b:("ct.", P))        # bind ct to effects in P
        ct.load(Src) : Binary option    # load glas data based on query
        ct.find(Src, FileRegex) : List of FilePath  # search near Src
        (%src.file FilePath Src) : Src
        (%src.dvcs.git Repo Ver FilePath Src) : Src
 
Src is an abstract data type with constructors in '%src.\*'. To support relative file paths and rendering dependency graphs, Src constructors always take a Src argument. By convention, each module receives a link to its own Src via '%src'. To support location-independent compilation, Src is opaque at compile time, but details are available at runtime via 'sys.refl.src.\*'.

We might eventually extend Src to stable HTTP queries, content-addressed data, and virtual filesystems. However, I believe this is enough to get started.

### Controlling Filesystem Entanglement

In my vision for glas systems, users can easily share code by copying folders between workspaces. Although DVCS bindings are preferred for stable dependencies, copying is suitable for notebook-style applications where live coding and projectional editing are integrated with the experience.

To support this vision, we can report warning or error in case of parent-relative (`"../"`) paths in Src constructors. Absolute paths are similarly restricted: users cannot reference an absolute path via relative-path Src in the same context (with DVCS becoming a new context). 

We might similarly report if a DVCS reached by hash links to other DVCS by branch. This would allow us to more robustly maintain horizontal versioning across bounaries.

## User-Defined Syntax

As a supported convention, users may define front-end compilers per file extension in '%env.lang.FileExt'. To bootstrap this process, the glas executable initialy injects a built-in definition for '%env.lang.glas' for long enough to build and extract the configured 'env.lang.glas'. 

A viable model for the front-end compiler is a Program implementing a pure function of type `Binary -> AST`, where AST represents a closed term of the module definition type. Lazy loads must be expressed as %macro nodes within the generated AST.

Compilation and linking are stage separated, which has some advantages and disadvantages. A compiler cannot express a closure, e.g. integrating definitions from its own environment. It can only arrange for the generated AST to later link shared libraries. OTOH, this ensures the client controls linking, and that we can share compilers without specializing them per link environment.

## Modules

The proposed module type is `Env -> Env`, albeit tagged for extensibility (see *Tags*). An Env is naturally very extensible, but we must reserve a few space to resist conflict. Prefix '%' is reserved for the system and front-end compilers.

A few reserved names deserve special attention:

* '%env.\*' - implicit parameters or context. Should propagate implicitly across most imports, but definitions may be shadowed contextually. This serves as the foundation for shared libraries, e.g. '%env.libname.op'. Initially binds to 'env.\*' in the user configuration.
* '%arg.\*' - explicit parameters. This allows a client to direct a module's behavior or specialize a module. It is feasible to import a module many times with different parameters.
* '%self.\*' - open recursion. By externalizing fixpoint to the client, we can express modules in terms of inheritance, override, and mixin composition. 
* '%src.\*' - abstract location '%src' and constructors. When importing a module, the front-end compiler shall temporarily shadow '%src' to that module's location.
* '%.\*' - reserved for the front-end compiler's private use; should start empty

The glas system provides the initial environment, including an initial '%env.\*', '%self.\*', '%src', and optionally providing runtime version info (or a method to query it) via '%arg.\*'. Front-end compilers must continue this pattern, though ideally the common functions (like wrapping an `Env -> Env` function to set '%src' and '%arg.\*', or common load steps) are shared between them.

Usefully, modules are first-class values within the glas namespace. We can define a name to the result of loading a module for convenient reuse. We can define namespace-level macros or mixins with the same type as loaded modules.

## Tags and Calling Conventions

A viable encoding for tags:

        f:("Adap", ((b:("", "Tag"), "Adap"), Body))

This function receives an Env of adapters, extracts adapter for Tag, applies to Body. Alternatively, if there is no adapter for Tag, raises an obvious error.

I propose to wrap nearly all modules and definitions in such tags. The overhead is negligible, but the resulting system will be far more extensible and adaptive. There is some risk of different communities overloading the same tags, but we're still better off due to the opportunity for resolution without module-level or call-site adapters.

The motivating use case for tags is similar to calling conventions. I would like to support calls to `Env -> Program` and `Program` in the same context. However, obvious use cases include integration with macros, modules, mixins. Tags can clearly distinguish definitions of 'types' from programs or data. And so on. Essentially all user definitions should be tagged.

## Controlling Shadows

Shadowing of names can be useful, especially in context of fixpoints. We can 'update' a name many times, yet clients bind the final definition via fixpoint. Shadow and update is a viable foundation for *Aggregation* patterns.

However, when users shadow a name *by accident*, it very easily leads to errors. These errors are obvious in many cases, but the subtle ones are a source of unnecessary frustration. To mitigate this, I propose to report name shadowing as an error by default. Then, we provide a simple means to suppress the error. 

The namespace AST has structures that can shadow names:

* `f:("x", Body)` - shadows prior name `"x"` in Body (if any)
* `b:("foo.", Body)` - shadows all prior names with prefix `"foo."` in Body (if any)
* `t:({ "foo." => "bar.", "bar." => "baz." }, Body)` - shadows prior names with prefix `"foo."` in body. Whether it shadows `"bar."` is more confusing - it's now available via `"foo."`.

One option is to report shadowing only for `f:` and `b:` binders. Thus, `t:({ "foo." => NULL }, b:("foo.", Body))` will not report because there is no `"foo."` in context of binder `b:`. Alternatively, we could support shadowing annotations.

AST representations from modules, macros, etc. are implicitly evaluated in an empty environment, stand-alone, implicitly `t:({ "" => NULL }, AST)`. Hence, we can evaluate AST for potential shadowing in a context-independent manner. However, we might raise errors only for actual shadowing.

## Inheritance

By externalizing module fixpoints to '%self', we can express one module as inheriting another, sharing the same self. The inheriting module can override definitions, integrating with any mutual recursion through self. 

To support this implicitly, a front-end compiler rewrites names based on usage context:

* defining name - use name directly
* keyword 'prior' name - use name directly
* otherwise - instead use '%self.name'

Beyond single inheritance, we can support mixins on modules. Mixins may introduce or override definitions, but generally cannot drop definitions without breaking things. 

*Aside:* I feel keyword 'prior' offers greater clarity than 'super' in most use cases, e.g. `x := 1 + prior x` makes contextual sense whether it's local shadowing, mixins, or inheritance and overrides.

### Open Composition

It is possible to compose hierarchically without first closing the fixpoint. This provides an opportunity for the composite to override component definitions while controlling risk of naming conflicts. To support open composition requires translating each component's view of %self and the user namespace.

        # To translate component to Prefix:
        modifyInput = b:("", 
            t:({ "%self." => "%self.Prefix", 
                 "%" => "%", 
                 "" => "Prefix"}, e:()))
        moveOutput = b:("Dest.", b:("Output.",  
            t:({ "Prefix" => "Output.",
                 "" => Dest." }, e:())))
        translateOp = f:("EnvOp", f:("InputEnv", 
            ((moveOutput, "InputEnv"),("EnvOp", (modifyInput, "InputEnv")))))

Due to the second-class nature of translations, this should be constructed either by the compiler or via macro. 

### Multiple Inheritance? Defer.

Effective support for multiple inheritance will require another level of indirection. Each module must represent its dependency structure without immediately applying it, and must support robust identification to recognize shared dependency structure. Then an algorithm such as C3 identifies incompatible inheritance structures and determines a merge order.

This extension seems feasible: Generate module IDs based on a secure hash of generated module AST representations, prior to linking. Front-end compiler builds inheritance lists instead of directly applying dependencies. Introduce a new tag for modules where the `Env -> Env` mixin is only element.

See also [Prototypes: Object-Orientation, Functionally](http://webyrd.net/scheme_workshop_2021/scheme2021-final91.pdf); section 4.3 discusses multiple inheritance.

## Incremental Compilation

Lazy evaluation can simplify incremental computing. Each thunk serves as a memo cell and tracks which thunks must be recomputed if its input ever changes. 

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. Like 42. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Aggregation

Language features such as multimethods, typeclasses, declaring HTTP routes, etc. require constructing and sharing tables. In context of functional programming, we can express iterative construction in terms of repeatedly shadowing a definition with an updated version. We can express sharing via fixpoint if we're careful to avoid datalock. 

An interesting opportunity is to use Church-encoded lists to aggregate tagged ASTs for later processing. This can be very flexible, and is directly analogous to the Writer monad.

Aggregation across modules is hostile to lazy loading. But we could allow aggressive, automatic aggregation at the *application* layer.

## Integration

By convention, modules receive a pseudo-global namespace '%\*'. This provides access to [program-model primitives](GlasProg.md), a method to load more modules, and a user-configurable '%env.\*' that via fixpoint to the user-configuration's 'env.\*'. The latter supports definition of shared libraries and applications.

Applications are typically defined within a configuration, e.g. 'env.AppName.app'. Users may also load scripts that define 'app' (still using the configured '%env.\*'). Like modules, the application type is also `Env -> Env`. However, in this case, the input environment contains 'sys.\*' definitions for runtime effects APIs, 'db.\*' and 'g.\*' registers, and 'app.\*' via fixpoint. The delayed fixpoint supports flexible inheritance and override when composing applications.

Applications define 'settings' to guide final configuration of the runtime, a 'main' method to represent program behavior, 'http' and 'rpc' methods to handle events. See [glas apps](GlasApps.md). Application methods are generally `Env -> Program`, allowing the program to interact with the runtime via callbacks.


