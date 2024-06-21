# Glas Configuration Language

The glas configuration language is lightweight and generic. Each configuration file represents a dictionary of [namespaces and mixins](GlasNamespaces.md). The primary configuration is the namespace 'config', but other namespaces may represent reusable components, mixins, or functions. The definitions within each namespace must be acyclic, and there are no general loops, thus termination is guaranteed.

Configured features are ultimately represented by names and data within the namespace. This must be interpreted by the application or runtime.

## Global Modules

Global modules will be individually defined under 'distro.' prefix, i.e. in case of `glas --run foo Args` the runtime will search for 'distro.foo' within the configuration namespace. The value of 'distro.foo' should be recognized as a module reference. Proposed representation of module references:

        type GlobalModuleRef 
              = remote:(at:Location, ln:Localization)
              | staged:(lang:GlobalModuleRef, src:GlobalModuleRef, ln:Localization)
              | inline:PlainOldData  

A remote module includes a Location and Localization. Both are abstract types for security reasons. The Location type may represent a file path on the local machine, or a URL to a remote DVCS repository. I intend to use the same Location type for importing configuration files. The Localization type relates to abstract assembly and the namespace model, and enables users to override dependencies of a module.

A staged module will first load the language module and source module values, then 'compile' the source via the language under a given localization. An inline module will trivially return a value that is computed entirely within the configuration.

## Security Concerns for Locations and Localizations

The main security threat related to configuration of glas systems involves unpredictable dependencies. For example, it's okay if a local file references another local file, or for a remote file to reference another remote file, but not for a remote file to reference a local file. We'll similarly want to control dependencies within the configuration namespace.

To resolve these concerns, the types for Location, Localization, and Names will be abstract within the configuration. These types can only be produced and composed by keyword, and would use specialized AST constructors in the intermediate [abstract assembly](AbstractAssembly.md).

That said, I don't see a use case for Names as data within a configuration. So, Locations and Localizations would be the primary abstract types in configurations.

## Locations


## Namespaces Layer Functions

A namespace of simple data expressions can represent a function. For example, we can develop a namespace where the client is expected to override 'x' and 'y' then read 'result'. To apply this function, we would dedicate a fresh hierarchical 'scratch' space for evaluation, while overriding a few arguments.

A direct encoding might look something like:

        import foo as tmp    # tl:(def(foo), { "" => "tmp." })
        override tmp.x = expr1
        override tmp.y = expr2
        rename tmp.result to bar
        hide tmp  # rename 'tmp' to a fresh private namespace

This is a bit bulky, so we'll need some syntactic sugar. Perhaps something like:

        import foo as tmp with
          x = expr1
          y = expr2
          result -> bar
        hide tmp

        eval foo with
          x = expr2
          y = expr2
          result -> bar

        apply foo(x=expr1, y=expr2, result->bar)

More generally, we could support shorthand 'z->z' as 'z', and perhaps prefix rewrites at '.' boundaries such as 'z. -> xyz.'. We could also understand 'eval' as a form of import and `(x = expr1, y = expr2, result->bar)` as similar to an import list. This would extend to qualified imports, `import as with`. 

I propose namespace layer functions as the primary way to to abstract over both configurations and data. This allows for a relatively simple data expression language. 

## Configuring RPC Registries, Databases, Etc..

We'll need to publish and discover RPC interfaces via intermediate matchmaking service, or to a shared database. Initially, this might only support one case, such as publishing through a local folder in the filesystem. Similar for the key-value database.

Ultimately, a lot of configuration will be more ad-hoc, with de-facto standardization.

## Application Specific Configuration

Some properties cannot be shared between applications. One obvious case is the TCP port we open to receive HTTP and RPC requests. In these cases, it's best if we can leave configuration to the application itself. 

Dynamic configuration is possible through a reflection API. For example, upon `start()` the application might configure the runtime through operations such as `sys.refl.bind("127.0.0.1:8080")`. Static configuration is feasible by extending the application's interface with a `config.*` section. For example, an application might define `config.bind` returning a list of TCP listen targets. In the latter case, we might constrain the effect type. 

## Data Expression Language

I want to keep this language very simple. There are no user-defined functions at this layer, but we might, only user-defined data. We'll freely use keywords and dedicated syntax for arithmetic, conditions, etc.


### Conditional Expressions

We could support `iflet x = Expr1 then Expr2 else Expr3` where only Expr2 has `x` in scope as a local variable. This corresponds to `try/then/else`, allowing us to 'commit' to Expr2 after evaluating Expr1. A simple variant `Expr1 | Expr2` might correspond to `iflet x = Expr1 in x else Expr2`. And we could have `if Expr1 then Expr2 else Expr3` where we don't actually capture Expr1, only test whether it is well defined.

For an expression to be well defined, it must depend only on well-defined names, and it must evaluate successfully. We might have some comparison expressions, e.g. where `("a" = "a")` evaluates to `"a"` while `("a" = "b")` simply fails. Also, any name that is part of a dependency cycle is considered undefined; we won't even attempt to evaluate the definitions.




## Structure

Toplevel: A dictionary that may contain [namespaces](GlasNamespaces.md). 

This is consistent with the initial glas language. However, there would be some significant differences regarding how 

This simplifies abstraction and mixins compared to directly modeling a toplevel namespace. It is also consistent with my initial glas language. The main differences are that locations in the configuration language must be more concrete, and the language should guarantee termination. 

*Aside:* We could still use an [abstract assembly](AbstractAssembly.md) when compiling the configuration, even if we don't take any advantage of this abstraction.

## Locations

The configuration language layer deal with filesystem and network locations. Locations are used in many cases: import of configuration files, reference to global modules, database persistence, etc..

I propose to model locations as an abstract data type. We won't manipulate locations as strings or as structured namespace components. Instead, locations are manipulated through a handful of abstract AST constructors.









Locations are ne includes for import of other configuration files. 

The simplest location is a file path. A more sophisticated location might name a remote DVCS repository, including access tokens and version tags, together with a relative file path. 

Although it is feasible to model location as a structured namespace component, I think it would be more convenient to model locations as an abstract data type. This might involve explicit support from the abstract assembly layer.
It would probably be convenient if a 'location' is modeled as an abstract data type - a feature supported by the abstract assembly - instead of modeling a location as a st


* composing texts and including named values within a text
* 'nameof(name)' expands to a name's text representation (after rename)
* distinguish whether a name is a text, a number, a dict, or undefined
* simple conditional expressions
* possible arithmetic on texts that represent decimal numbers. 
* uncertain: support for lists, looping over lists, list names in namespace

A runtime system and application could contribute some ad-hoc variables prior to computing some parts of the configuration, but not for others.

I think this is quite doable. 

## Misc

### RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

### Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. Eventually, we might also want to support distributed databases with special handling of vars, queues, bags, etc..

*Note:* I've rejected the idea of 'mounting' databases as a basis for sharing because it complicates reasoning. Instead, we should focus on databases that are 'natively' distributed, and support access control to keys. 
