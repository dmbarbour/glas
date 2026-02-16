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

The glas namespace can essentially Church-encode tagged terms as Names, with adapters as reified environments. The tag selects an Adapter and applies it to a given Body.

        tag TagName = f:("Body", f:("Adapter", 
           ((b:("", TagName), "Adapter"), "Body")))
        (tag "prog", ProgramBody)

In context of glas systems, I propose to tag all user definitions and compiled modules. This improves system extensibility, e.g. we can introduce tags for different calling conventions, alternative application models, etc..

Some tags currently in use:

* "data" - embedded data (`d:...`)
* "prog" - basic glas programs
* "call" - `Object -> Def` for algebraic effects; returns tagged Def
* "env" - an `Env` definition
* "obj" - a generic `Env -> Env -> Env` *Object* described below
* "module" - a basic `Env -> Object` *Module* as described below
* "app" - interprets `Object` as a basic application

Developers can gradually introduce new tags and deprecate old ones. We might develop tags to work with DSLs like grammars, logic programs, constraint systems, process networks, hardware descriptions, etc.. Tags aren't always structural, e.g. with "app" we express intended interpretation of an object.

### Objects

As the initial object model for glas systems, I propose an "obj"-tagged `Env -> Env -> Env` namespace term, corresponding to `Base -> Self -> Instance`. This model is based on [Prototypes and Object Orientation, Functionally (2021)](http://webyrd.net/scheme_workshop_2021/scheme2021-final91.pdf).

The 'Base' argument supports mixin composition and may ultimately bind the host, e.g. 'sys.\*' system effects APIs for application objects. The 'Self' argument is an open fixpoint, supporting mutual recursion with inheritance and override. The "obj" tag ensures opportunity to develop alternative object models, e.g. to eventually support multiple inheritance.

The glas system uses objects for applications, modules, and front-end compilers. Objects offer an opportunity for extensibility, but effective use requires deliberate design. For example, a front-end compiler that exposes only 'compile' is less extensible than one that also exposes 'parse.int' for override.

## Safety

### Shadows

Although shadowing names can be useful, automatic shadowing is a source of subtle errors. To mitigate this, I propose to report errors or warnings for shadowing by default, and introduce annotations to suppress the warning.
