# Glas Configuration Language

The glas configuration language is lightweight and generic. Computation is limited, but there is ample support for modular composition and extension based on [namespaces](GlasNamespaces.md).

Configurations can grow very large, especially with how the glas system uses them. Large configuration files replace the conventional package managers and search paths, explicitly defining every global module. A user configuration will usually inherit and extend a large configuration representing community distributions, company mixins, project specific overlays. This might be represented as referring to a remote file maintained in a DVCS repository. 

An unusual feature of the glas configuration language is that it reifies *localizations*. This lets us preserve scope during compilation of global modules, which in turn supports parameterization, overrides, and working with multiple versions of the system. 

## Data

The data language is dynamically typed, and supports very few data types. 

* integers
* lists - including texts and tables 
* dictionaries with symbolic keys
* variants as singleton dictionaries
* locations - files, git repos, etc.
* localizations - for latent binding

The 'plain old data' is integers, lists, dictionaries, and variants. This translates directly to glas data via the conventional representation, i.e. bitstrings for integers and dicts as tries. 

Locations and localizations require keywords to construct and are abstract after construction. I'm not committed to this, but a viable syntax:

        loc Expr                # create a location
        link local              # basic localization
        link with Aliases       # translated localization

Under the hood, the abstract types might look like:

        type Location 
                = (origin:ConfigFileLocation
                  ,target:Expr
                  )
        type Localization 
                = (env:Namespace
                  ,tl:Map of Prefix to Prefix
                  )

Locations capture the configuration file's location to support relative paths. This also guards against problematic locations, such as a remote configuration file referencing an absolute file path. Localizations capture an environment so we can later evaluate some names. This includes an entire namespace in context of *Namespaces as Functions*.

## Namespaces as Functions

Namespaces are the basis for functions in the configuration language. At the namespace level, we can define 'mixins' that operate on a tacit namespace, producing a modified namespace. At the data level, a function can be expressed as a namespace where the caller overrides 'arg.x' and 'arg.y' then evaluates 'result'.

Namespaces are not first-class within the configuration language. However, composition of namespaces is more flexible than basic function composition. This can support some limited examples of higher order programming.

Because we're using namespaces as functions, localizations must capture the 'closure', i.e. the captured namespace must include overrides to arguments.

## Configuration File Structure

The toplevel of a configuration file consists of imports and namespace definitions. The actual configuration is represented by the namespace called 'config'. Other namespaces may serve as a library of components, mixins, and functions.

Imports are carefully designed to be unambiguous. It is locally possible to determine where an imported namespace should be defined before loading anything. This allows for lazy loading, which is convenient for performance. This is achieved by permitting only one 'unqualified' import, while all other imports are explicit import lists or hierarchical. Imports may reference namespaces to express configuration file locations. 

Each namespace contains definitions that should evaluate to data. The data expression language is not Turing complete, but is capable of simple arithmetic, pattern matching, and [primitive recursion](https://en.wikipedia.org/wiki/Primitive_recursive_function). Data definitions must be acyclic, forming a directed acyclic graph.

## Adaptive Configurations

As a convention, `sys.*` in the configuration namespace should be reserved for system parameters. For example, the system might introduce operating system environment variables as `sys.env.*`. This enables flexible adaptation of the configuration to the host.

The extent to which this environment is exposed to the module system is left to the user. The system arguments are not accessible within hierarchical configuration components unless explicitly propagated.

## Configuration Syntax

I take some inspiration from [TOML](https://toml.io/en/), which I find more readable than most configuration languages. However, compared to TOML the glas configuration language is complicated by need to support imports, inheritance, and overrides.

A viable presentation is to use a distinct form of section header for the file level versus the 


However, I do need some extra syntax to work with

However, the glas configuration language must also deal with imports and overrides, which complicates things a little. 








Imports, namespace definitions. I'd like to minimize indentation, so we might need to signal structure by other means - blank spaces, visually obvious boundaries (perhaps `[name]` or `@name`?), and so on.

## Data Expression Language


There is a sublanguage for 

I want to keep this language very simple. There are no user-defined functions at this layer, but we might, only user-defined data. We'll freely use keywords and dedicated syntax for arithmetic, conditions, etc.

### Conditional Expression

Might be based mostly on pattern matching.

### Arithmetic

I'm torn a bit on how much support for arithmetic in configurations should be provided. Integers? Rationals? Vectors and matrices? I'm leaning towards support for ad-hoc polymorphism based on the shape of data.

### Lists and Tables

I don't plan to support full loops, but it might be convenient to support some operations to filter, join, zip, and summarize lists similar to a relational algebra. 

### Structured Data

Support for pairs, lists, and and labeled variants or dictionaries is essential. We could also make it feasible to lift a configuration namespace into data.



## Glas Configuration Details

### Global Modules

Global modules are defined individually within the configuration namespace, usually under the `module.*` prefix. Each module definition should evaluate to a specification and extended description:

        type GlobalModuleDesc 
              = (spec:GlobalModuleSpec
                ,desc:TextBlurb
                ,... # ad-hoc annotations
                )

        type GlobalModuleSpec 
              = folder:(at:Location, ln:Localization)
              | staged:(lang:GlobalModuleDesc, src:GlobalModuleDesc, ln:Localization)
              | inline:PlainOldData

The full description is there to support browsing, searching, debugging, and so on. Most modules will be specified as a folder, located in the filesystem or a DVCS repository. The staged and inline specifications are useful for small or simple modules. All global modules referenced during compilation will be localized, including language modules implicitly referenced by file extension.

### RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

Fortunately, other than often including a Location in a registry, we don't need any additional support for registries.

### Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. Eventually, we might also want to support distributed databases with special handling of vars, queues, bags, etc..

*Note:* I've rejected the idea of 'mounting' databases as a basis for sharing because it complicates reasoning. Instead, we should focus on databases that are 'natively' distributed, and support access control to keys. 
