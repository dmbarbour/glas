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
* *ifdef* - supports expression of defaults, optional defs, flexible mixins
* *fixpoint* - a built-in fixpoint for lazy, recursive defs

The [program model](GlasProg.md) provides a collection of primitive definitions under prefix '%'. Additionally, the runtime will bind '%env.\*' to the configuration's 'env.\*' via fixpoint. This provides a foundation for shared libraries.

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

### Translation

TL is a finite map of form `{ Prefix => (Prefix' | NULL) }`. To translate a name via TL, we find the longest matching prefix, rewrite that to the output prefix. Or, if output is NULL, we'll treat the name as undefined. 

An obvious weakness of prefix-to-prefix translation is that natural language names are not prefix-unique, e.g. we cannot translate `"bar"` to `"foo"` without also converting `"bard"` to `"food"`. To mitigate this, front-end compilers implicitly extend names with a `".!"` suffix, such that we can use `{ "bar.!" => "foo.!" }` to match 'bar' alone, or `{ "bar." => "foo." }` to modify 'bar' together with 'bar.\*'.

Sequential translations can be composed into a single map. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that output prefixes in A match as many input prefixes in B as possible, then translate A's outuput prefixes as names (longest matching prefix). To normalize, optionally erase new redundancy.

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

### Eval Rules

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

## Modularity

Modularity is supported by compile-time effects. To prevent compile-time effects from leaking into the runtime, they are provided indirectly as algebraic effects handlers to the %macro primitive. The primary effect is to load files, returning a binary. This binary may be processed within the macro to generate an AST. Lazy compilation may be expressed in terms of returning an AST that contains more macro nodes.

Viable API:

        ("%macro", b:("ct.", P))                # bind ct to compile-time effects in P
        ct.load : Src<Type> -> (Type | FAIL)    # load glas data based on query

Src is an abstract data type. By convention, '%src' carries Src for the current module, while '%src.\*' provides constructors. All Src constructors are relative to enhance control over transitive dependencies and support rendering a dependency graph. To support location-independent compilation and cache sharing, location details only become visible at runtime via 'sys.refl.src.\*' APIs.

Possible initial constructors:

        (%src.file FilePath Src) : Src<Binary> 
        (%src.dir FileRegex Src) : Src<List of FilePath>
        (%src.dvcs.git Repo Ver Src) : Src<unit>
          # Repo - e.g. a URL
          # Ver - branch, tag, or hash

In this API, a DVCS file is relative to a DVCS source, and we'll support treat absolute file paths as relative to DVCS root. This seems sufficient to get started. We might eventually extend to support other DVCS, database or HTTP queries, and content-addressed data.

### Folders as Packages

In my vision for glas systems, users can easily share code by copying folders. Although DVCS bindings are preferred for stable dependencies, copying is suitable for notebook-style applications where live coding and projectional editing are integrated with the experience.

To support this vision, I forbid parent-relative (`"../"`) paths in Src constructors. Absolute paths are also restricted: users cannot reference an absolute path via relative-path Src. Upon entry to DVCS, we treat absolute paths as referring to DVCS root.

As a related convention, we might leverage %src.dir to let users 'load' a folder as a source after searching for a 'package.\*' file.

### User-Defined Syntax

As a convention, users may define front-end compilers per file extension in '%env.lang.FileExt'. To bootstrap this process, the glas executable initialy injects a built-in definition for '%env.lang.glas'. The front-end compiler should be a purely functional Program of type `Binary -> AST` for use within a macro but separate from operations to load the binary or link the AST.

### Tagged Definitions and Calling Conventions

A program may work with many types of definitions, e.g. `Env -> Program` vs. `Program` vs. Env -> Env`. This isn't a problem when used in different contexts. However, when they share a context, the caller must syntactically distinguish each call type, which is ugly and annoying.

My proposed solution is to tag every user definition and let front-end compilers provide an adapter. For example, we can easily adapt `Program` into `Env -> Program`. Perhaps we can also express macro expansions as normal program expansions.

A viable encoding for tags:

        f:("w", ((b:("", "TAG"), "w"), Definition))

This function receives an Env of adapters, extracts the TAG adapter, then applies to Definition. If there is no such adapter, we'll get an obvious error, usually at compile time.

### Modules and Mixins

The proposed type for module AST is `Env -> Env`. The input Env should at least provide primitives, including access to '%env.\*' and an updated '%src'. But this may be extended to access definitions in client's environment, essentially parameterizing modules. We could support in-out parameters, where a module both receives and returns a definition. And at the extreme, we could 'include' a module into the current namespace.

Of course, we aren't limited to modules that do this. We can also define `Env -> Env` functions for use as namespace macros, or even `Env -> (Env -> Env)` for parameterization.

The glas namespace model doesn't support a union of definitions, but it isn't difficult to develop modules that support a sort of union-merge of definitions via the ifdef primitive.

## Incremental Compilation

Lazy evaluation can simplify incremental computing. Each thunk serves as a memo cell and tracks which thunks must be recomputed if its input ever changes. 

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. Like 42. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Aggregation

Language features such as multimethods, typeclasses, declaring HTTP routes, etc. require constructing and sharing tables. In context of functional programming, we can express iterative construction in terms of repeatedly shadowing a definition with an updated version. We can express sharing via fixpoint if we're careful to avoid datalock. *Caveat:* aggregation across module boundaries hinders lazy loading. 

An interesting opportunity is to use Church-encoded lists or free monads to aggregate tagged ASTs. This provides a more flexible structure for postprocessing.

## Integration

By convention, modules receive a pseudo-global namespace '%\*'. This provides access to [program-model primitives](GlasProg.md), a method to load more modules, and a user-configurable '%env.\*' that via fixpoint to the user-configuration's 'env.\*'. The latter supports definition of shared libraries and applications.

Applications are typically defined within a configuration, e.g. 'env.AppName.app'. Users may also load scripts that define 'app' (still using the configured '%env.\*'). Like modules, the application type is also `Env -> Env`. However, in this case, the input environment contains 'sys.\*' definitions for runtime effects APIs, 'db.\*' and 'g.\*' registers, and 'app.\*' via fixpoint. The delayed fixpoint supports flexible inheritance and override when composing applications.

Applications define 'settings' to guide final configuration of the runtime, a 'main' method to represent program behavior, 'http' and 'rpc' methods to handle events. See [glas apps](GlasApps.md). Application methods are generally `Env -> Program`, allowing the program to interact with the runtime via callbacks.

