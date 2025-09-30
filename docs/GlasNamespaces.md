# Glas Namespaces

In my vision for glas systems, huge namespaces define runtime configuration, shared libraries, and applications. Definitions can be distributed across dozens of DVCS repositories, referencing stable branches or hashes for horizontal version control. We rely on laziness to load and extract only what we need, and incremental compilation to reduce rework. 

## Design Overview

Lambda calculus can express namespaces, e.g. a `let x = X in E` becomes `((λx.E) X)`. However, such namespaces are second-class in the lambda calculus. We can extend the lambda calculus with reification of the environment, a means to integrate environments into the namespace, and more explicit control of a namespace.

Base Lambda Calculus:

* *application* - provide an argument 
* *arg binding* - bind argument to name in body
* *name* - substitute definition of name in scope

Namespace extensions:

* *reification* - capture current view of environment 
* *translation* - modify body's view of environment
* *env binding* - bind environment to prefix in body

Utility extensions:

* *annotations* - structured comments for instrumentation, optimization, verification.
* *data* - opaque to the calculus, but embedded for convenience
* *ifdef* - flexible expression of defaults, optional defs, merge-union, mixins
* *fixpoint* - a built-in fixpoint for lazy, recursive defs

The [program model](GlasProg.md) provides a collection of primitive definitions under prefix '%'. The runtime will also bind '%env.\*' to the configuration's 'env.\*' via fixpoint, supporting shared libraries. The runtime also links '%src' to abstract data representing a user configuration.

## Abstract Syntax Tree (AST)

The AST encoded as structured glas data. This serves as an intermediate representation for namespaces and programs.

        type AST =
            | Name                  # substitute definition
            | (AST, AST)            # application
            | f:(Name, AST)         # bind name in body (aka lambda)
            | e:()                  # reifies host environment
            | t:(TL, AST)           # modify body's view of environment
            | b:(Prefix, AST)       # bind argument to prefix in body
            | a:(AST, AST)          # annotation in lhs, target in rhs 
            | d:Data                # embedded glas data, opaque to AST
            | c:(Name,(AST,AST))    # ifdef conditional expression
            | y:AST                 # built-in fixpoint combinator 
        type Name = binary excluding NULL
        type Prefix = any binary prefix of Name
        type TL = Map of Prefix to (Prefix | NULL) as radix-tree dict

## Evaluation

Evaluation of an AST is a lazy, substutive reduction in context of an environment that maps a subset of names to definitions (i.e. `Name -> optional AST`). 

* application, lambdas, and names: as lazy lambda calculus evaluation

* reification `e:()` - returns an abstract dictionary containing all names in scope, i.e. `{ "x" = x, "y" = y, ...}`. An empty environment can be expressed as `t:({ "" => NULL }, e:())`.
* translation `t:(TL, Body)` - translates Body's view of the current environment through TL. Of semantic relevance, TL controls dictionary keys if the environment is reified in Body.
* env binding - when applied `(b:(Prefix,Body), Arg)`, binds Arg to Prefix context of evaluating Body.

* annotations `a:(Anno, Target)` - Semantically inert: logically evaluates as Target. In practice, we evaluate Anno to an abstract Annotation using compiler-provided Annotation constructors - by convention `%an.name` or `(%an.ctor Args)`. We then use this Annotation to guide instrumentation, optimization, or verification of Target.
* data `d:Data` - evaluates to itself 
* ifdef `c:(Name, (Then, Else))` - evaluates to Then if Name is defined in current environment, otherwise Else. 
* fixpoint - built-in fixpoint for convenient expression and efficient evaluation

### Evaluation Pseudocode

Notes:

* For lexical scope,  `f:(Name, Body)` we must bind Body to the current environment and 

* We can directly evaluate binders if they are introduced and applied in the same context, i.e. `(f:(Name, Body), Arg)` or `(b:(Prefix, Body), Arg)` can skip the intermediate closure.
 heavily optimized.



Intermediate Representations:

* Closures - We can directly evaluate binders if they are introduced and applied

When we evaluate binders such as `f:(Name, Body)`


The proposed AST type represent the intermediate states for evaluation, so we'll add a few:

        type EvalAST = 
            | AST
            | C:

Note: we'll likely need to introduce some intermediate representations, such as closures, closure-var refs, and thunks, to properly encode evaluation.

        # inline applications are common and easily optimized.
        # TBD: lexical binding to Env.
        eval Env (f:(Name, Body), Arg) =        # inline definition
            let argThunk = eval Env Arg
            let Env' = addEnv (def:(Name, argThunk)) Env
            eval Env' Body
        eval Env (b:(Prefix, Body), Arg) =      # inline binding
            let argThunk = eval Env Arg
            let Env' = 
                if "" = Prefix then argThunk else  # optimization
                addEnv (idx:(Prefix, argThunk)) Env
            eval Env' Body
        eval Env (t:(TL, Body)) =
            let Env' = addEnv (tl:TL) Env
            eval Env' Body


### Environment Representation 

A viable environment representation?

        type Env = List of EnvOp
        type EnvOp =
            | def:(Name, AST)       # definition
            | idx:(Prefix, AST)     # env binding
            | tl:TL                 # translated scope

        lookup N (def:(Name, Def), Env') =
            if N = Name then 
                Just Def 
            else
                lookup N Env'
        lookup N (idx:(Prefix, NS), Env') =
            if Prefix matches N then
                let N' = skip (len Prefix) N
                lookup N' NS
            else
                lookup N Env'
        lookup N (tl:TL, Env') =
            let N' = translate TL N
            lookup N' Env'
        lookup N () = None

There are many optimizations we can perform on Env: move names into matching prefixes, merge similar prefixes, compose translations, remove unreachable definitions, etc.. When these optimizations are applied, we get something similar to radix-tree indexing between translations, but we also pay significant up-front costs and lose structure sharing. Thus, the decision must be heuristic.

This is just an idea at the moment. We might extend the environment with something like closures, argument offsets, and thunks before we're truly ready to flesh things out.

But something like this can potentially work in context of a lexical scope. We might need special support closures once we begin to integrate things.

### Translation

TL is a finite map of form `{ Prefix => (Prefix' | NULL) }`. To translate a name via TL, we find the longest matching prefix, then rewrite that to the output prefix. Alternatively, if output is NULL, we'll treat the name as undefined.

The TL type works best with prefix-unique names, where no name is a prefix of another name. Consider that TL `{ "bar" => "foo" }` will convert `"bard"` to `"food"`, and it's awkward to target 'bar' alone. To mitigate, we logically add suffix `".."` to all names, and front-end syntax can strongly discourage `".."` within user-defined names. This logical suffix enables translation of 'bar' together with 'bar.\*' via `{ "bar." => "foo." }` or 'bar' alone via `{ "bar.." => "foo.." }`. If translation removes the suffix, raise an error. (Alternatively, introduce an `N:FullName` intermediate representation.)

Sequential translations can be composed into a single map. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that output prefixes in A match as many input prefixes in B as possible, then translate A's outuput prefixes as names (longest matching prefix). To normalize, optionally erase new redundancy.

## Modularity

Modularity is supported by compile-time effects. To prevent compile-time effects from leaking into the runtime, they are provided indirectly as algebraic effects handlers to the %macro primitive. The primary effect is to load files, returning a binary. This binary may be processed within the macro to generate an AST. Lazy compilation may be expressed in terms of returning an AST that contains more macro nodes.

Viable API:

        ("%macro", b:("ct.", P))        # bind ct to effects in P
        ct.load(Src) : Binary option    # load glas data based on query
        (%src.file FilePath Src) : Src
        (%src.dvcs.git Repo Ver FilePath Src) : Src
 
Src is an abstract data type with constructors in '%src.\*'. To support relative file paths and rendering dependency graphs, Src constructors always take a Src argument. By convention, each module receives a link to its own Src via '%src'. To support location-independent compilation, Src is opaque at compile time, but details are available at runtime via 'sys.refl.src.\*'.

We can later extend Src to stable HTTP queries, content-addressed data, and virtual filesystems. However, I believe this is enough to get started.

## Module Overview

A proposed module type is `Env -> Env`, albeit tagged for extensibility (see *Tags*).

By convention, the input environment is bound to prefix '%', i.e. `b:("%", ModuleBody)`. This space provides access to program primitives, a user-configurable environment '%env.\*' for shared libraries, and '%src' for a module's abstract location. The module will define more names and eventually return its reified environment, optionally hiding '%' and private utility definitions.

To support mutually-recursive definitions, we must wrap the module body with a fixpoint. Although we could do so within each module, there is a significant benefit to keeping recursion 'open': it allows us to express modules in terms of inheritance and override. Thus, I propose to move the fixpoint to the client 'import', binding a module's definition to '%self.\*'. A module may still close recursion locally where it makes sense.

We can also support a notion of parameterized modules, analogous to OO constructors (for immutable objects). We effectively use '%env.\*' for implicit parameters. I propose to reserve '%arg.\*' for more conventional arguments that should not be implicitly propagated. If there are no explicit user parameters to a module, this volume should be empty.

Altogether, a client module will propagate its own '%\*' after shadowing '%src', '%self', and '%arg.\*'. For '%self' the client will usually fixpoint the module, but an opportunity exists for inheritance or mixins.

## User-Defined Syntax

As a convention, users may define front-end compilers per file extension in '%env.lang.FileExt'. To bootstrap this process, the glas executable initialy injects a built-in definition for '%env.lang.glas'. *Aside:* We should normalize file extensions, e.g. lower case ascii, '.' to '-'. 

A viable model for the front-end compiler is a Program implementing a pure function of type `Binary -> AST`, where AST represents a closed term of the module definition type. Lazy loads must be expressed as %macro nodes within the generated AST.

Compilation and linking are stage separated, which has some advantages and disadvantages. A compiler cannot express a closure, e.g. integrating definitions from its own environment. It can only arrange for the generated AST to later link shared libraries. OTOH, this ensures the client controls linking, and that we can share compilers without specializing them per link environment.

## Tags and Calling Conventions

A viable encoding for tags:

        f:("E", ((b:("", "T"), "E"), Body))

This function receives an Env of adapters E, extracts the T adapter, then applies to Body. Alternatively, if E does not contain T, this raises a clear error when applied.

I propose to wrap nearly all modules and definitions in such tags. The overhead is negligible, but the resulting system will be far more extensible and adaptive. There is some risk of different communities overloading the same tags, but we're still better off due to the opportunity for resolution without module-level or call-site adapters.

The motivating use case for tags is something like calling conventions. I would like to support calls to `Env -> Program` and `Program` within the same language without explicit adapters or syntactic distinctions. I would also like to support macro expansions without a separate syntax. We would also benefit from distinguishing embedded data, for use in wider contexts. And so on.

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

## Controlling Entanglement

In my vision for glas systems, users can easily share code by copying folders. Although DVCS bindings are preferred for stable dependencies, copying is suitable for notebook-style applications where live coding and projectional editing are integrated with the experience.

To support this vision, we can report warning or error in case of parent-relative (`"../"`) paths in Src constructors. Absolute paths are similarly restricted: users cannot reference an absolute path via relative-path Src in the same context (with DVCS becoming a new context). 

We might similarly report if a DVCS reached by hash links to other DVCS by branch. This would allow us to more robustly maintain horizontal versioning across bounaries.

