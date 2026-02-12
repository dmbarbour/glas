# Glas Namespaces

## Extended Lambda Calculus

A lambda calculus can construct environments, e.g. a `let x = Def in Expr` can desugar to `(λx.Expr Def)`. However, it is difficult to reuse this across many Exprs. To simplify reuse, I propose to extend lambda calculus with reification of environments, i.e. such that `(λx.__ENV__ Def)` returns a record like `{ "x":Def }`, containing all definitions in scope. Then we also introduce a mechanism to bind environments back into the namespace.

Base Lambda Calculus:

- names aka variables 
- application
- lambdas

Namespace Extensions:

- reify environment, abstract record
- translate or alias names 
- bind or integrate environment

A viable solution is prefix-based bindings, something like `(νPrefix.Body Env)` representing that `PrefixFoo` in `Body` links to `Foo` in `Env`. *Note:* There is no implicit fallback: if `Foo` is undefined in `Env`, then `PrefixFoo` is undefined in `Body`. If users want mixin-like behavior, they can model mixins explicitly: see *Objects* pattern, below.

## Concrete Representation

A proposed namespace AST encoded as structured [glas data](GlasData.md):

        type AST =
          # lambda calculus
            | Name              # substitute definition
            | (AST, AST)        # application
            | f:(Name, AST)     # bind name in body, aka lambda
          # namespace extensions
            | e:()              # reifies current environment
            | t:(TL, AST)       # modify body's view of environment
            | b:(Prefix, AST)   # binds environment to prefix in body
          # utility extensions
            | d:Data            # embedded glas data, opaque to AST
            | a:(AST, AST)      # annotation is LHS, content in RHS
            | y:AST             # built-in fixpoint combinator
        type Name = binary excluding NULL # implicit, infinite "..." suffix
        type Prefix = any finite prefix of Name
        type TL = Map of Prefix to (Optional Prefix) as glas dict

Beyond namespace extensions, I introduce embedded data and annotations for integration, and a built-in fixpoint for convenience and concision.

## Translations

The TL type is a finite map of form `{ Prefix => Optional Prefix }`. The longest matching LHS Prefix is selected then converted to the RHS Prefix, if specified. If the RHS is none, matching names are treated as undefined. In context of `t:(TL, AST)`, the TL translation is applied to free (unbound) names within the AST.

A problem with prefix-to-prefix translations is that natural language names are not always prefix-unique. For example, it can be difficult to translate "bar" without also affecting "barrel". To mitigate this, we logically add an infinite "..." suffix to every name. Thus, we can match "bar." or "bar.." without touching "barrel".

It is possible to compose TLs sequentially. Rough sketch: to compute A followed-by B, first extend A with redundant rules such that RHS prefixes in A match as many LHS prefixes in B as possible, then translate A's RHS prefixes via B as names. To normalize, erase redundant rules. Computing this composition can improve performance if we're translating enough names. 

## Design Patterns

### Tags and Adapters

The glas namespace easily encodes tagged terms, encoding tags as Names.

        tag TagName = f:("Body", (f:("Adapters", 
            ((t:({""=>NULL}, b:("", TagName)), "Adapters"), "Body"))))
        (tag "prog", ProgramBody)

The tag selects an Adapter and applies it to a given Body. This is essentially a Church-encoded sum types, albeit leveraging the reified namespace. 

In context of glas systems, I propose to tag all user definitions and compiled modules. This improves system extensibility, e.g. we can introduce tags for different calling conventions, alternative application models, etc..

Some tags currently in use:

* "data" - embedded data (`d:...`)
* "prog" - basic glas programs
* "call" - `Env -> Def`, Env is caller's environment for algebraic effects
* "obj" - a generic `Env -> Env -> Env` *Object* described below
* "module" - a basic `Env -> Object` *Module* as described below
* "app" - basic `Object` app, integrates object in specific manner

Developers may freely introduce new tags and deprecate old ones.

### Objects

As the initial object model for glas systems, I propose an "obj"-tagged `Env -> Env -> Env` namespace term, corresponding to `Base -> Self -> Instance`. This model is based on [Prototypes and Object Orientation, Functionally (2021)](http://webyrd.net/scheme_workshop_2021/scheme2021-final91.pdf).

The 'Base' argument supports mixin composition and may ultimately bind the host, e.g. 'sys.\*' system effects APIs for application objects. The 'Self' argument is an open fixpoint, supporting mutual recursion with inheritance and override. The "obj" tag ensures opportunity to develop alternative object models, e.g. to eventually support multiple inheritance.

The glas system uses objects for applications, modules, and front-end compilers. Objects offer an opportunity for extensibility, but effective use requires deliberate design. For example, a front-end compiler that exposes only 'compile' is less extensible than one that also exposes 'parse.int' for override.

## Integration

### Modules

Modules are modeled as a "module"-tagged `Env -> Object`. That `Env` is a parameter object, providing 'src', an abstract location of the module's file. The Base for a module object links '%\*' primitives and a '%env.\*' shared environment. Importantly, these should include means to load further modules:

- `(%src.file FilePath Src) : Src` - file in same folder or subfolder as another Src. 
- `(%src.git URL Version Src) : Src` - remote DVCS repo; access relative '%src.file' 
- `(%load Src) : d:Data` - load file, returns optional binary as embedded data 
  - diverges for errors other than file does not exist
- `(%macro P)` - P is 0--1 arity program that returns a closed-term namespace AST
- `%env.lang.FileExt` - see *User-Defined Syntax* below

To simplify sharing, refactoring, live coding, and metaprogramming, we impose some constraints:

- forbid parent-relative ("../") and absolute filepaths
- forbid files and subfolders whose names start with "."
- warn if any file or repo is loaded more than once

We might distinguish between 'importing' and 'including' a module. Importing a module will instantiate it, closing the fixpoint. Including a module is closer to applying a mixin, supporting inheritance and override of the module.

### User-Defined Syntax

A front-end compiler is an object that defines 'compile', a 1--1 arity program that takes an optional binary input and returns a closed-term namespace AST representing a module. Aside from 'compile', the object may provide ad hoc interfaces for tooling (e.g. syntax highlighting) or extension (e.g. override integer parser).

By convention, front-end compilers should be available at '%env.lang.FileExt'. We can introduce front-end compilers globally via configured environment or override them locally, in scope of project or subfolder. This provides a simple basis for user-defined syntax.

### Configuration

A configuration is a module that defines a configured environment 'env.\*' and ad hoc runtime options in 'glas.\*'. When linking, we bind '%env.\*' in Base to the configured environment via fixpoint.

A small, local user configuration will inherit from a large, curated community or company configuration in DVCS. The resulting environment may define hundreds of applications and libraries. Lazy evaluation and caching is essential for performance. Whole-system versioning is feasible if we use content-addressed hashes or stable tags when linking DVCS versions, or if our 'glas.\*' configuration options include rules to freeze DVCS links.

When initially compiling the configuration, we supply a built-in compiler for at least '%env.lang.glas'. If the configuration defines 'env.lang.glas', we perform a bootstrap cycle to transition to the user-defined compiler.

## Safety

### Shadows

Although shadowing names can be useful, automatic shadowing is a source of subtle errors. To mitigate this, I propose to report errors or warnings for shadowing by default, and introduce annotations to suppress the warning.
