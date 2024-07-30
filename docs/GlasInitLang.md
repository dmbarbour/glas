# Glas Initialization, Integration, Input, and Configuration Language

This is a language for modular configurations. It's also the bootstrap language for language modules. Preferred file extension: ".gin".  

Notable features:

* *Imports and Inheritance.* The language is highly modular, capable of working with very large configurations. In addition to importing local files, imports may refer to DVCS or HTTP resources. It is possible to compose and override definitions from multiple sources.
* *Object Capability Security.* The language carefully controls construction of locations and names to support robust reasoning about relationships and access control. For example, a remote file cannot directly reference a local file, imported definitions can be sandboxed, and dependencies between global modules are localized in the configuration namespace.
* *Grammar Inspired Expression.* Functions are expressed as grammar rules. Loops are expressed in terms of recursive pattern matching between functions. This is especially convenient for parsing and processing of texts and intermediate representations.
* *Termination Guarantee.* Computation is guaranteed to eventually terminate even without use of quotas. This is based on restricting recursion: recursion is supported only within the grammar match rule, and some data must be matched prior to recursion. The language is designed to simplify analysis of these constraints.
* *Pure and Deterministic.* The only 'effect' in this language is to import definitions or binary data. Computation is fully deterministic up to imported dependencies. 
* *Lazy Evaluation.* Most computation can be avoided if it isn't necessary for the configuration. This includes loading imports.
* *Block Structured Syntax.* The syntax avoids use of indentation for deep hierarchical structure. 

Glas systems will fully specify the global module namespace within the configuration. Compared to filesystem search paths or package managers and lockfiles, this supports many nice properties: Overriding dependencies supports abstraction of global modules. Hierarchical structure lets users work with multiple versions of global modules in the same system. Export control allows a package of global modules to share 'private' dependencies. However, this results in large configurations.

In my vision for glas systems, users will usually inherit from a community or company configuration then apply a few overrides representing the user's authorities, resources, and preferences. Although the full configuration may be very large, the user's configuration file can be small.

## Data

The language supports only a few data types. 

* numbers - integers, rationals, complex
* lists - texts, tables, vectors, matrices
* dictionaries with symbolic keys
* variants (as singleton dictionaries)
* locations - files, git repos, etc.
* localizations - for latent binding

The 'plain old data' is numbers, lists, dictionaries, and variants. These translates directly to glas data using the conventional representations, e.g. bitstrings for integers, dicts as tries with a null separator, etc.. The configuration language abstracts over representations, but built-in conversions are supported.

For security reasons, locations and localizations are constructed by keyword then left abstract. Viable representations under-the-hood:

        type Location =
            (origin:ConfigFileLocation    # from parser
            ,target:Data                  # from user
            )
        type Localization = 
            (tl:Map of Prefix to Prefix   # from renames
            ,df:Map of Name to Def        # from eval
            )

Locations capture the configuration file's location. This supports relative paths and also lets us easily recognize problems such as a remote DVCS configuration file referencing regular file paths outside the repository. 

Localization might capture a local scope of definitions, including name translations. Minimally, this consists of prefix-to-prefix name translations into the configuration namespace (see localizations in [abstract assembly](AbstractAssembly.md)). But in context of closures, we might need a more general solution.

## Config Namespace

A configuration is expressed as a [namespace](GlasNamespaces.md) of terminating functions and data expressions. A namespace is like an OOP 'class' without the state: it supports inheritance and override, hierarchical structure, and access control to names. A configuration file may define multiple namespaces as libraries of reusable components and mixins. The actual configuration is the namespace called 'config'.

To support adaptive configurations, the configuration namespace might reserve `sys.*` for late binding of system data into the configuration namespace, such as OS environment variables or host architecture. There may also be some limited support for implicit parameters, e.g. to access application settings.

## Toplevel Syntax

A configuration file consists of a header of import and export statements, followed by multiple block-structured namespace definitions. 

        # sample config
        import locations.math-file as m
        from "./oldconfig.g0" import locations, config as old-config
        from "../foobar.g0" import foo, bar
        export config, locations

        @ns config
        # syntax tbd
        include old-config
        :server.foo
        include foo                     # include into server.foo
        set address = "192.168.1.2"     # server.foo.address
        :server.bar
        include bar                     # include into server.bar
        set address = "192.168.1.3"     # server.bar.address

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
        Source <= 'file' InlineText | 'loc' Path 

Explicit imports forbid name shadowing and are always prioritized over implicit imports using 'open'. We can always determine *where* every import is coming from without searching outside the configuration file. This supports lazy loading and processing of imports.

The Source is currently limited to files or a dotted path that should evaluate to a Location. The Location type may specify computed file paths, DVCS resources with access tokens and version tags, and so on. 

## Implicit Parameters and Algebraic Effects



## Limited Higher Order Programming

The language will support limited higher order programming in terms of overriding functions within a namespace, and in terms of algebraic effects. These are structurally restricted to simplify termination analysis.

These are always static, thus won't interfere with termination analysis. Some higher order loops may be built-in to the syntax for convenience.

The Localization type isn't used for higher order programming in this language because dynamic . It is used only for translating global module names back into the configuration namespace.


## Function Definitions


## Namespace Blocks

The namespace will define data and functions. We might override definitions by default, providing access to the 'prior' definition, but we could support explicit introduction where we want to assume a name is previously unused.

We can support some local shadowing of implicit definitions in context, so long as we don't refer to those implicit definitions. 






Data can be modeled as a function that doesn't match any input. 

Name shadowing is only permitted for implicit definitions, and a namespace block must 

In general, a 'complete match' of input is required for a function, meanin



## Explicit Introduction

By default, definitions will be overrides. If we want to assume we 'introduce' a definition for the first time, we might specify 'intro ListOfNames'. I think this will better fit the normal use case.


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

### Configured Modules

Modules are specififed individually within the configuration namespace, usually under the `module.*` prefix. Each module definition should evaluate to a specification and extended description:

        type ModuleDesc 
              = (spec:ModuleSpec
                ,desc:TextBlurb
                ,... # ad-hoc annotations
                )

        type ModuleSpec 
              = file:(at:Location, ln:Localization)
              | stage:(lang:GlobalModuleDesc, src:GlobalModuleDesc, ln:Localization)
              | data:PlainOldData

The full description is there to support browsing, searching, debugging, and so on. Most modules will be specified as a folder, located in the filesystem or a DVCS repository. The staged and inline specifications are useful for small or simple modules. All global modules named during compilation will be translated into the configuration namespace via Localization. 

### RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

Fortunately, other than often including a Location in a registry, we don't need any additional support for registries.

### Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. Eventually, we might also want to support distributed databases with special handling of vars, queues, bags, etc..

*Note:* I've rejected the idea of 'mounting' databases as a basis for sharing because it complicates reasoning. Instead, we should focus on databases that are 'natively' distributed, and support access control to keys. 
