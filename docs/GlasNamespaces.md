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

## Translation

TL is a finite map of form `{ Prefix => Optional Prefix }`. To translate a name via TL, we find the longest matching prefix, then rewrite that to the output prefix. Alternatively, if the rhs has no prefix, we'll treat the name as undefined.

The TL type works best with prefix-unique names, where no name is a prefix of another name. Consider that TL `{ "bar" => "foo" }` will convert `"bard"` to `"food"`, and it's awkward to target 'bar' alone. To mitigate, we logically add suffix `".."` to all names, and front-end syntax will discourage `".."` within user-defined names. The combination of logical suffix and front-end support allows translation of 'bar' together with 'bar.\*' via `{ "bar." => "foo." }` or 'bar' alone via `{ "bar.." => "foo.." }`. There is a possibility of translation *removing* the suffix that should be handled correctly by an evaluator's internal representation of names or environments. 

Sequential translations can be composed into a single map. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that output prefixes in A match as many input prefixes in B as possible, then translate A's outuput prefixes as names (longest matching prefix). To normalize, optionally erase new redundancy.

## Loading Files

A file is loaded from an abstract source. This is supported through a few primitive definitions and conventions:

* `(%load Src) : d:Data` - loads external resources at compile-time, returning as embedded data. This operation may diverge if Src is malformed or unreachable.
* `%src.*` - constructors for abstract Src data. All constructors are relative to another Src. A module must be parameterized by its Src.
  * `(%src.file FilePath Src) : Src` - evaluates to an abstract Src representing a FilePath relative to another Src. When loaded, returns an *optional binary*, treating 'does not exist' as a valid state. For other errors (e.g. unreachable, permissions issues) the loader diverges with a compile-time error.
    * Note: glas systems forbid relative paths, absolute paths, and files or subfolders whose names start with ".". See *Controlling Filesystem Entanglement*.
  * `(%src.dvcs.git URI Ver Src) : Src` - returns a Src representing a DVCS git repository. If loaded, returns a boolean (whether the DVCS is found). Use '%src.file' to access files relative to repo root.
  * `(%src.an Annotation Src) : Src` - Annotation is ignored in most contexts, but is available later when reflecting on sources for debug output or projectional editing.
  * `(%src.an.local Src) : Src` - enforces locality, forbids restricts constructing remote sources relative to returned source.

It is feasible to extend Src, e.g. Mercurial, content-addressed globs, HTTP. Perhaps we'll eventually support extension of '%src.\*' constructors through the runtime configuration. But, for now, git and files are adequate.

After loading the file, it must be compiled. This is also supported through primitives and conventions:

* `(%macro Builder)` - Builder must be a deterministic 0--1 arity program that returns a closed-term namespace AST on the data stack. Returns the evaluated namespace term.
* `%env.lang.FileExt` - a front-end compiler; see *User-Defined Syntax* below

An 'import' operation might construct an AST roughly of form `(Link (%macro (%do (%data (%load (%src.file FileName $src))) (Adapt %env.lang.FileExt))))`. 

### Controlling Filesystem Entanglement

Many popular programming languages allow messy, ad hoc relationships within the filesystem with "../" paths, absolute file paths, etc.. These are convenient in the short term. Unfortunately, such dependencies complicate long-term sharing, refactoring, and local reasoning.

To avoid these issues in glas, I propose to forbid absolute and parent-relative file paths in Src constructors. References outside the folder are possible only via remote DVCS links. *Note:* This slightly inconveniences local development: users must either include their projects within their configuration folder or treat the project as external scripts.

I further forbid files and subfolders whose names start with ".". These are reserved for tooling and metadata, such as ".git/config", PKI-signed manifests and certificates, cached proof hints for dependent types, or scratch spaces. This is already a popular convention; glas merely enforces it.

A tentative third constraint is to forbid browsing of filesystems: there is no '%src.dir' constructor returning a list of files. Browsing folders and iterating files unfortunately entangles meaning with *location*, which hinders refactorings that would move or share structure between locations. Without browsing, users instead introduce intermediate files for indices, and this indirection provides an opportunity to abstract location.

### Affine File Dependencies

This restriction says that no file may be loaded twice. Files may be loaded at most once within a glas context. Otherwise we report an error or warning. This restriction offers many benefits for reasoning, refactoring, live coding, and metaprogramming:

- simplifies local reasoning about how edits propagate
- ensures files can be refactored locally
- transparently replace files with generated code
  - no need for metaprogramming to create intermediate files
- all sharing is visible within configuration for analysis
- can leverage '$src' and line number as uniqueness source

Instead of sharing through the filesystem, sharing is through the namespace. See *Shared Libraries and Applications*. However, we may provide a annotations to disable the warning on a case-by-case basis.

## Shared Libraries and Applications

The user configuration defines 'env.\*'. The glas runtime feeds this back into the configuration as '%env.\*'. Front-end compilers treat '%\*' (which also contains primitives) as a pseudo-global namespace, transitively linking these definitions into every imported module by default.

Instead of filesystem install paths, users 'install' shared libraries and applications by defining them within 'env.\*' in the user configuration. The exception is scripts, which are invoked by filepath instead of configured name.

*Aside:* Sharing [applications](GlasApps.md) is convenient when we define new applications in terms of extending or composing existing applications.

## User-Defined Syntax

As a convention, users can define a front-end compiler per file extension under '%env.lang.FileExt'. The glas runtime provides built-in definitions for "glas" and "glob" extensions. If the user configuration redefines these, the runtime performs a short bootstrap cycle and verifies a fixpoint.

The front-end compiler is a tagged object that defines 'compile'. And 'compile' is a deterministic, 1--1 arity program that receives loaded data and returns a module AST. See below for *Tags*, *Objects*, and *Modules*, and separate document for [programs](GlasProg.md). The compiler will be invoked in context of a '%macro' node. 

Aside from 'compile', the front-end compiler at `%env.lang.FileExt` may implement interfaces to support syntax highlighting, language servers, and other tools. Further, it should expose features such as 'parser.int' for easy override, so developers can easily tweak a language. The 'Base' environment for the compiler is the empty environment.

For concision, a front-end compiler can make reasonable assumptions about the module's link environment. For example, instead of directly writing out a full AST to import and link another file, the generated AST might alias `{ "." => "%env.lang.FileExt.utils." }` then later invoke language-specific `".import"` macros.

## Tags and Adapters

The glas namespace easily encodes tagged terms, where tags are represented by names.

        tag TagName = f:("Body", (f:("Adapters", 
            ((b:("", TagName), "Adapters"), "Body"))))
        tag "prog" Program

Here, the tagged term extracts and applies an adapter based on TagName. This is analogous to Church-encoded sum types.

In context of glas systems, I propose to tag all user definitions and compiled modules. This significantly improves system extensibility, allowing for multiple calling conventions, new application types, etc.. It is also feasible to support tags with fallback definitions.

Sample tags:

* "data" - embedded data (`d:...`)
* "prog" - basic glas programs
* "env" - an environment term
* "call" - `Env -> Def` - for programs with algebraic effects; receive caller's environment, return another tagged definition
* "obj" - a generic `Env -> Env -> Env` *Object* described below
* "module" - a basic *Module* as described below

Developers can introduce new tags, extend adapters, and deprecate old tags as the system develops.

## Objects

As the initial object model for glas systems, I propose an "obj"-tagged `Env -> Env -> Env` namespace term, corresponding to `Self -> Base -> Instance`. This model is based on [Prototypes and Object Orientation, Functionally (2021)](http://webyrd.net/scheme_workshop_2021/scheme2021-final91.pdf).

The 'Self' argument represents an open fixpoint. The 'Base' argument supports mixin composition or late binding to a host environment, e.g. link application to 'sys.\*' effects APIs. The "obj" tag opens space for alternative models, e.g. to eventually support multiple inheritance.

The glas system uses objects for applications, modules, and front-end compilers. Objects offer an opportunity for extensibility, but effective use requires deliberate design. For example, a front-end compiler that exposes only 'compile' is much less extensible than one that also exposes 'parse.int' for override.

*Aside:* Despite wide use of objects, I hesitate to describe glas as object oriented because these objects are available only at compile-time.

## Modules

The proposed module type is a "module"-tagged `Env -> Object`. The argument is a parameter object that should provide at least 'src', the abstract file location for the file being compiled. Front-end compilers may freely provide additional parameters to the module.

Assuming an "obj"-tagged Object is returned, we apply the '%\*' primitives as Base. The '%' prefix is pre-inserted, and includes the configured environment via '%env.\*'.

*Note:* It is useful for a front-end syntax to distinguish between importing and including a module (perhaps by another name), such that 'include' applies a module object like a mixin and 'import' is closer to instantiating the module object. Inheritance and override of a community configuration is then expressed in terms of including the community configuration.

## Controlling Shadows

Shadowing of names can be useful, but automatic shadowing can be a source of subtle errors. To mitigate this, I propose to report errors or warnings for shadowing by default, then introduce annotations for deliberate shadowing.

## Incremental Compilation

Lazy evaluation can simplify incremental computing. Each thunk serves as a memo cell and tracks which thunks must be recomputed if its input ever changes. We can especially build a few thunks around %load and %macro nodes.

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Hierarchy and Naming Conventions

The proposed convention in glas systems is that dotted path names like 'foo.bar' are simply long names. That '.' is meaningful to the user and perhaps to tools, but it's just one big flat namespace.

As for use of names to poke into environments (or even objects), we could feasibly provide syntax for that. But with '.' taken, not sure what it should look like. It might be better to insist users explicitly bind environments or objects into the current namespace to access them, e.g. with a 'using' statement.
