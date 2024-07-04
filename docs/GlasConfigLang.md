# Glas Configuration Language

The glas configuration language, preferred file extension ".g0", has limited support for computation and ample support for modular composition and extension.

The glas configuration is responsible for defining the global module namespace. Thus, configuration imports are instead based around reference to the local filesystem or remote DVCS repositories. 

In my vision, users will import configuration files representing community distributions, company overlays, project-specific mixins and merge these elements into a 'config' namespace. The user might apply a few final tweaks, e.g. overriding a global module to reference a local filesystem workspace. The final configuration namespace may be very large, but the configuration file as maintained by the user can be small.

## Compatibility

It is feasible to evaluate a hierarchical configuration into one very large JSON object, but there is no attempt at full compatibility. Relevantly, the configuration language doesn't support all JSON types such as floating point numbers and 

 (notably, floating point numbers aren't supported)


## Data

The data expression language is dynamically typed and supports very few data types. 

* numbers - integers, rationals, complex
* lists - texts, tables, vectors, matrices
* dictionaries with symbolic keys
* variants as singleton dictionaries
* locations - files, git repos, etc.
* environments - for latent binding

The 'plain old data' is numbers, lists, dictionaries, and variants. These translates directly to glas data using the conventional representations, e.g. bitstrings for integers, dicts as tries with a null separator, etc.. The configuration language abstracts over representations, but it is possible to convert between a rational and an `(n, d)` dict of integers.

Locations and environments are constructed by keywords then treated as abstract types. Viable representations under the hood:

        type Location =
            (origin:ConfigFileLocation    # from parser
            ,target:Data                  # from user
            )
        type Environment =
            (df:Map of Name to Def        # from runtime
            ,tl:Map of Prefix to Prefix   # from renames
            )

Locations capture the configuration file's location. This supports relative paths. It also lets us easily detect some problems, such as a remote DVCS configuration file referencing regular files outside the repository.

Environments capture enough information to evalute a name as understood within some scope. Environments require support across multiple layers: the name localization map is maintained in [abstract assembly](AbstractAssembly.md) across renames, while a namespace must be captured as a closure in context of *Namespaces as Functions*.

## Namespaces as Functions

At the data level, a function might be expressed as an namespace where a caller overrides 'arg.x' and 'arg.y' then evaluates 'result'. At the namespace level, a function may be expressed as a mixin: a sequence of namespace operations on an abstract, tacit namespace. The configuration language will fully leverage namespaces as functions.

Namespaces are not first-class within the configuration language. This is mitigated by the relatively flexible composition of namespaces, but it still limits higher order programming.

## Sketch of Syntax

A configuration file consists of a header of import and export statements, followed by multiple block-structured namespace definitions. The actual configuration is expressed by the namespace called 'config', while others represent functions, mixins, and reusable components.

        # sample config
        import locations.math-file as m
        from "./oldconfig.g0" import locations, config as old-config
        from "../foobar.g0" import foo, bar
        export config, locations

        @ns config
        include old-config
        :server.foo
        include foo                 # include into server.foo
        address = "192.168.1.2"     # server.foo.address
        :server.bar
        include bar                 # include into server.bar
        address = "192.168.1.3"     # server.bar.address

The relative order of imports and definitions is ignored. Instead, we'll enforce that definitions are unambiguous and that dependencies are acyclic. This gives a 'declarative' feel to the configuration language.

The use of block structure at multiple layers ('@' blocks in toplevel, ':' blocks in namespace definition) is intended to reduce need for indentation. We'll still use a little indentation in some larger data expressions (pattern matching, loops, multi-line texts, etc.) but it should be kept shallow.

Line comments start with '#' and are generally permitted where whitespace is insignificant, which is most places whitespace is accepted outside of text literals. In addition to line comments, there is explicit support for ad-hoc annotations within namespaces.

## Imports and Exports

Imports and exports must be placed at the head the configuration file, prior to the first '@' block separator. Proposed syntax:

        # statements (commutative, logical lines, '#' line comments)
        open Source                     # implicit defs (limit one)
        from Source import Aliases      # explicit defs
        import Source as Word           # hierarchical defs
        export Aliases                  # export control (limit one)

        # grammars
        Aliases <= (Word|(Path 'as' Word))(',' Aliases)?
        Word <= ([a-z][a-z0-9]*)('-'Word)?
        Path <= Word('.'Path)?
        Source <= InlineText | Path 
        # see data language for InlineText

Explicit definitions and imports must be unambiguous and are prioritized over implicit imports via 'open'. Because there is at most one implicit import, we can locally determine where to search for a definition. This supports lazy loading of imports.

Regarding import sources, it is common to use a text such as `"../foobar.g0"` to indicate a local file. We might also recognize some URLs for DVCS resources. In the more general case, we can use a dotted path into a namespace of Locations. This indirection is potentially convenient for maintenance purposes. 

### Data Imports

As a special case, we can also express limited import of file binaries at the data expression layer. This is a convenient alternative to embedding a file's content as a multi-line string.

## Computation Limits

I intend to restrict the configuration language to [primitive recursion](https://en.wikipedia.org/wiki/Primitive_recursive_function). Within that restriction, we can support a fair degree of flexibility: pattern matching, arithmetic, etc..

This doesn't guarantee reasonable performance, but it greatly simplifies reasoning and debugging of the configuration, including for performance problems.

### Adaptive Configurations

As a convention, `sys.*` within the configuration namespace is reserved for system parameters. For example, the system might introduce operating system environment variables as `sys.env.*`. This enables flexible adaptation of the configuration to the host.

The extent to which this environment is exposed to the module system is left to the user. The system arguments are not accessible within hierarchical configuration components unless explicitly propagated.

## Mixins? Defer.

We can potentially define 'mixins' as functions that apply to a namespace. I'd prefer to keep namespace definitions mostly order-independent, but we could apply a mixin to a namespace just prior to its inclusion within another namespace.

That said, I don't have a strong use case for mixins at this time. I'll defer support until it becomes a priority.

## Data Expression Language

Definitions within each namespace allow for limited computation of data. The language is not Turing complete, but is capable of simple arithmetic, pattern matching, and [primitive recursion](https://en.wikipedia.org/wiki/Primitive_recursive_function). Data definitions must be acyclic, forming a directed acyclic graph.

There is a sublanguage for 

I want to keep this language very simple. There are no user-defined functions at this layer, but we might, only user-defined data. We'll freely use keywords and dedicated syntax for arithmetic, conditions, etc.

### Multi-Line Texts

### Importing File Data 

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
              = folder:(at:Location, ln:Environment)
              | staged:(lang:GlobalModuleDesc, src:GlobalModuleDesc, ln:Environment)
              | inline:PlainOldData

The full description is there to support browsing, searching, debugging, and so on. Most modules will be specified as a folder, located in the filesystem or a DVCS repository. The staged and inline specifications are useful for small or simple modules. All global modules referenced during compilation will be localized to the Environment, including the language modules implicitly loaded based on file extensions.

### RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

Fortunately, other than often including a Location in a registry, we don't need any additional support for registries.

### Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. Eventually, we might also want to support distributed databases with special handling of vars, queues, bags, etc..

*Note:* I've rejected the idea of 'mounting' databases as a basis for sharing because it complicates reasoning. Instead, we should focus on databases that are 'natively' distributed, and support access control to keys. 
