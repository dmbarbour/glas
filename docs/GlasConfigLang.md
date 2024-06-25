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

Locations need special attention for security reasons. This mostly applies later, when we start using remote locations (e.g. a configuration file in a DVCS repository). We can get started with just local files, keeping extensions in mind.

I imagine a grammar similar to this for parsing Locations:

        expr Location 
                = file Path                     # absolute or relative to current file
                | eval Expr in Namespace        # for abstraction, libraries of locations
                | git ...                       # extensions as needed

        expr Path = StringLiteral

We'll use locations in context of configuration file imports:

        open Location                           # single inheritance
        from Location import ImportList         # selective imports
        import Location as LocalName            # qualified import (hierarchical)

When embedding locations as data in more general expressions, I propose prefix '@' to indicate that we should parse a location.

        ns foo {
          fooFile = @file "./foo"
        }
        from (eval fooFile in foo) import ...

The 'file' location will implicitly add the configuration file's location to the intermediate AST at parse time. This is very useful for relative paths, ensuring they are relative to the correct file even across imports. We can also detect problematic paths, such as a file path that steps outside a remote file's repository.

The 'eval' location is useful for abstracting locations, or developing libraries of locations. It lets us describe locations before they are used or even in a separate file.

## Toplevel Syntax



## Namespaces as Functions

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

### Arithmetic

I'm torn a bit on how much support for arithmetic in configurations should be provided. Integers? Rationals? Vectors and matrices? I'm leaning towards support for ad-hoc polymorphism based on the shape of data.

### Lists and Tables

I don't plan to support full loops, but it might be convenient to support some operations to filter, join, zip, and summarize lists similar to a relational algebra. 

### Structured Data

Support for pairs, lists, and and labeled variants or dictionaries is essential. We could also make it feasible to lift a configuration namespace into data.

## RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

Fortunately, other than often including a Location in a registry, we don't need any additional support for registries.

## Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. Eventually, we might also want to support distributed databases with special handling of vars, queues, bags, etc..

*Note:* I've rejected the idea of 'mounting' databases as a basis for sharing because it complicates reasoning. Instead, we should focus on databases that are 'natively' distributed, and support access control to keys. 
