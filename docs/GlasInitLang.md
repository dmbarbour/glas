# Glas Initialization, Integration, Input, and Configuration Language

This is a language for modular configurations. Preferred file extension: ".gin" or maybe ".g" if I unify layers.  

An unusual feature of glas system configuration is that we'll represent the entire 'package system' within the configuration. This results in very large configurations, thus we require ample support for modularity. Notable features:

* *Modularity and Abstraction.* The language is designed to support *very large* configurations with multiple files and abstraction via late binding and override.
* *Laziness and Caching.* The configuration language is designed to support lazy loading of imports and implicit memoization of computations, to reduce rework. 
* *Grammar Inspired Functions* Functions are expressed as deterministic grammar rules, based loosely on [parsing expression grammars (PEGs)](https://en.wikipedia.org/wiki/Parsing_expression_grammar). 
* *Termination Guarantee.* Computation is ideally restricted to [primitive recursive functions](https://en.wikipedia.org/wiki/Primitive_recursive_function). This should be enforced by analysis of mutually recursive definitions.
* *Simple Syntax.* The toplevel syntax is inspired from [toml](https://toml.io/en/), with minimal extensions for modularity and functions. 

In general, user configurations will inherit from much larger community or company configurations, which include base distributions of modules and conventions for integrating application settings and host resources. In glas systems, the configuration will abstract the host layer such module locations and authorization, OS environment variables, foreign function interfaces, filesystem folders, and network interfaces

*Note:* The ".g" application language should be syntactically very similar to ".gin", albeit with a few differences in how modules are referenced, support for metaprogramming, and access to effects. 

## Data

The language supports only a few data types. 

* numbers - integers, rationals, complex
* lists - texts, tables, vectors, matrices
* dictionaries with symbolic keys
* variants (as singleton dictionaries)

* locations - files, git repos, etc.
* localizations - for late binding

The 'plain old data' is numbers, lists, dictionaries, and variants. These translates directly to glas data using the conventional representations, e.g. bitstrings for integers, dicts as tries with a null separator, rational and complex numbers as dicts, etc.. The configuration language abstracts over representations, but built-in conversions are supported.

For security reasons, locations, localizations, and names are constructed by keyword then left abstract. Viable representations under-the-hood:

        type Location =
            (origin:ConfigFileLocation    # from parser
            ,target:Data                  # from user
            )
        type Localization = Map of Prefix to Prefix

Locations capture the configuration file's location. This supports relative paths and also lets us easily recognize problems such as a remote DVCS configuration file referencing regular file paths outside the repository. Localization might capture a scope of definitions, including name translations. Minimally, this consists of prefix-to-prefix name translations into the configuration namespace (see localizations in [abstract assembly](AbstractAssembly.md)). 

## Config Namespace

A configuration file defines one [namespace](GlasNamespaces.md) of functions and data expressions. This can inherit and override other configuration files. It is possible to develop template-like abstract configuration files where overrides are expected. For example, by convention definitions under `sys.*` are left abstract for later system-provided overrides, such as access to OS environment variables.

## Import Expressions

A configuration will generally contain some import statements that introduce definitions from other configuration files. In general, it is possible to import the same file multiple times in different translation contexts, or with different overrides. 

Import expressions may be arbitrary expressions that evaluate to a glas input file Location. This might be wrapped and represented as an 'ld' operation when compiling the configuration file. Obviously, an import expression must not depend on any definitions provided by the imported resource, but it may depend on definitions later provided or overridden by the *client* of a module.

A relevant design goal for glas configuration is *lazy* imports. Instead of multiple inheritance and conflict detection, we'll generally restrict imports to one 'open' import (e.g. to inherit from another config) plus any number of non-overlapping explicit or qualified imports. This ensures it is always locally obvious within a file where a given name is defined, which import expressions must be evaluated.

*Note:* There is no support for 'mixins' for higher-order abstraction.

## Toplevel Syntax

I'm aiming for a syntax similar to [toml](https://toml.io/en/), though not an exact match. This is complicated a little by support for import expressions and functions.


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

