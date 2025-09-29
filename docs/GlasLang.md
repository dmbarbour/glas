# Goals for Glas Syntax

* First, this should be a syntax I want to program in. 
* Implicit fixpoint per-module for module-local recursive definitions.
* Lightweight declaration of definition 'tags' for adapters.
* Easy access to low-level code when I want some. 



# DEPRECATED

Major overhauls to namespace model, prog model, etc.. Need to rewrite a lot.


# Initial Language for Glas (TODO!)

The glas system can support multiple programming languages, but there 

## Convenient Access to Implicit Environment

Perhaps something like this:

        foo.x       # local foo.x
        :foo.x      # %env.foo.x

## Overrides and Hyperstatic Env

When we import from a source, we might want to hide some definitions and override others. This could be expressed as part of the import alias list (e.g. 'overriding' clauses on load), or alternatively by syntactically discriminating '=' (let, hide prior) vs. ':=' (assign, update in place) or similar. Need to find a solution that makes both comfortable.

## Outcome

The [namespace model](GlasNamespaces.md) describes a process of iteratively writing definitions into an abstract, monotonic, partial namespace. The namespace is never expressed as a concrete value.


 containing [abstract assembly](AbstractAssembly.md) definitions. This compiles 
To simplify extensibility, dictionary definitions are initially limited to 'ns' and 'mx' headers, and the 'g' header can help integrate ".g" modules into other languages.



## Desiderata

This is a procedural language without a separate heap. Objects are second-class, in the sense they can be allocated on the stack but not directly returned from a subroutine. This is mitigated by a macro-like system leveraging [abstract assembly](AbstractAssembly.md). A basic procedure is compiled into two parts: a procedure body, plus a simple wrapper macro that evaluates arguments in the caller then calls the procedure body. Other macros may abstract over declaration of local vars, closures, and construction of stack objects.

The language should also support static eval. Users can express assumptions about which parameters and results should be determined at compile time. It should be feasible to support lightweight DSLs, e.g. a syntactic sugar around different monads or GADTs (though true syntax extensions are left to language modules). 

I would like to support *units* on numbers in some sensible way. Phantom types? Associated metadata? Ideally something that doesn't cost too much for serialization and makes sense for basic arithmetic. 

 And perhaps similar on other values. These units should be subject to compile-time analysis, and could provide a lightweight basis as a type system.

. Some lightweight DSLs might be supported in terms of compile-time p of data that represents subprograms. Of course, the glas system fully supports user-defined syntax (by defining language modules), 

, including true front-end extensions by defining lang-g. 


 The glas system also supports staging via 
although ".g" syntax is not directly extensible (indirectly, users could extend lang-g or de

Some lightweight DSLs may be feasible based on this (i.e. a macro could parse a const value representing a program)., though glas also allows users to define other file extensions with ad-hoc syntax.

Ideally, the ".g" language should also support lightweight user-defined DSLs. However, this is inherently limited to structures we can parse without 

I hope to also support lightweight DSLs based on macros. 

## Macros For All


## Caching

## Types

## Logging

## Profiling

## Proof Carrying Code


## Process Networks



it isn't clear how to make a process networks integrate nicely with effects and transactions.

To support distributed computations at larger scales, it might be convenient to support some model of process networks within glas programs. However, 



I believe Glas systems would benefit heavily from good support for Kahn Process Networks, especially temporal KPNs (where processes, channels, and messages have abstract time). 

I would like the KPN language to produce KPN representations without any specific assumption about the target platform, i.e. so it could be compiled to multiple targets - including, but not limited to, Glas processes.

Instead of dynamic channels, might aim for labeled ports with external wiring. This would be a closer fit for the static-after-metaprogramming structure of glas systems.

## Soft Constraint Search

This is a big one. I'd like to support search for flexible models, especially including metaprogramming. The main thing here would be to express and compose search models, and also develop the optimizers and evaluators for the search.

## Logic Language


## Glas Lisp 

Lightweight support for recursion might be convenient.

# Glas Initialization, Integration, Input, and Configuration Language

This is a language for modular configurations. Preferred file extension: ".gin" or maybe ".g" if I unify layers.  

An unusual feature of glas system configuration is that we'll represent the entire 'package system' within the configuration. This results in very large configurations, thus we require ample support for modularity. Notable features:

* *Modularity and Abstraction.* The language is designed to support *very large* configurations with multiple files and abstraction via late binding and override.
* *Laziness and Caching.* To support the expected use cases, the configuration language must support lazy loading of imports and persistent caching of computations. 
* *Grammar Inspired Functions* Functions are expressed as deterministic grammar rules, based loosely on [parsing expression grammars (PEGs)](https://en.wikipedia.org/wiki/Parsing_expression_grammar). 
* *Termination Guarantee.* Computation is ideally restricted to [primitive recursive functions](https://en.wikipedia.org/wiki/Primitive_recursive_function). This should be enforced by analysis of mutually recursive definitions.
* *Simple Syntax.* The toplevel syntax is inspired from [toml](https://toml.io/en/), with minimal extensions for modularity and functions. 

In general, user configurations will inherit from much larger community or company configurations, which include base distributions of modules and conventions for integrating application settings and host resources. In glas systems, the configuration will abstract the host layer such module locations and authorization, OS environment variables, foreign function interfaces, filesystem folders, and network interfaces

*Note:* The ".g" application language should be syntactically very similar to ".gin", albeit with a few differences in how modules are referenced, support for metaprogramming, and access to effects. 

## Data

We'll support only plain old glas data. Syntactic support will focus on lists, numbers, and labeled data. Computation may also involve some tacit parameters for abstract locations, localizations, or higher-order functions. But these won't be presented as first-class values.

## Config Namespace

A configuration file defines one [namespace](GlasNamespaces.md) of functions and data expressions. This can inherit and override other configuration files. It is possible to develop template-like abstract configuration files where overrides are expected. For example, by convention definitions under `sys.*` are left abstract for later system-provided overrides, such as access to OS environment variables.

Regarding dependencies when loading a file as a module, we might apply a call context to represent the logical environment for the compiler. 

## Import Expressions?

A configuration file may reference other configuration files or remote resources. The question is how much computation is needed to represent these files or resources. We could permit arbitrary expressions, to be evaluated at parse time. This would allow for late binding and overrides, but it greatly complicates processing of the configuration. Alternatively, we could restrict to inline expressions, e.g. relative file paths or URLs. This simplifies processing but is a bit less flexible.

I'm leaning towards the static inline expressions. It isn't clear to me where flexibility of locations would be useful for configurations, instead I suspect it would be confusing if we used it at all.

To simplify lazy imports, we'll ensure in the syntax layer that we never have more than one definition for a word, at most one 'open' dependency for implicit imports, and that we only use one definition for a word within a configuration file.

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

*Note:* When importing definitions, we might want the option to override instead of shadow definitions. This might need to be represented explicitly in the import list, and is ideally consistent with how we distinguish override versus shadowing outside the list. Of course, this is a non-issue if we omit 'open' imports.

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


