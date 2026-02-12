# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and the liquid-to-solid creation analogous to staged metaprogramming. It is also a backronym for 'general language system', something glas aspires to be. Design goals orient around compositionality, extensibility, scalability, live coding, staged metaprogramming, and distributed systems programming.

This document provides an overview of the main components of glas and how they fit together.

## Command Line Interface (CLI)

The [glas CLI](GlasCLI.md) is the initial user interface to glas systems. The primary use case is to load the user configuration then run an application defined within the configuration.

## Configuration

The `GLAS_CONF` environment variable may specify a configuration. If undefined, the default file location is `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows. 

The configuration file is loaded as a glas module. This module should define ad hoc 'glas.\*' runtime options and a configured environment 'env.\*'. The configured environment is fed back into the configuration as '%env.\*', serving as a pseudo-global namespace. Runtime configuration options may generally be runtime-version specific and generally receive access to runtime version info 'rt.\*' as algebraic effects.

The glas system supports user-defined syntax by defining a front-end compiler at '%env.lang.FileExt'. As a special case, we'll attempt bootstrap when a configuration defines its own compiler. That is, we initially apply the the built-in, then override via the definitions at 'env.lang.FileExt'. Bootstrap must reach a fixpoint within just a few cycles.

In my vision for glas systems, a small user configuration inherits from a much larger community or company configuration, applying a few overrides to account for the user's projects, preferences, or resources. The larger configuration is imported from DVCS, which may in turn link other DVCS repos. 

The configured environment may be large, defining a whole system of applications and libraries. There is an opportunity for whole-system versioning, transitively linking DVCS in terms of stable tags or content-addressed hashes. But performance for extracting a specific definition or application will depend on lazy loading and caching.

## User-Defined Syntax

By convention, when a front-end compiler loads a file, it will process that file via front-end compiler defined at '%env.lang.FileExt' then link '%\*' names into the returned module. To get things rolling, the glas CLI provides a built-in front-end compiler for at least [".glas"](GlasLang.md) files, subject to bootstrapping.

A syntax can be 'globally' installed via configuration. But project-local syntactic tweaks or even folder-local DSLs are possible by overriding '%env.lang.FileExt' in scope of performing imports. This offers a lot of flexibility. Importantly, it also mitigates potential issues that may arise as front-end languages are updated.

Syntax isn't only for humans. To simplify tooling, we may attempt to load JSON, CSV, even SQLite files as program sources. Technically, we don't need to compile every file we '%load', but it is convenient to do so.

A front-end compiler is modeled as an object (see below) that minimally defines a 'compile' function. The object can also implement interfaces for other tools, e.g. syntax highlighting or auto-formatting. And it may expose some internal structure for overrides, e.g. parse rules and AST generators.

## Modules

A front-end compiler returns a [closed-term namespace AST](GlasNamespaces.md) representing a module. A basic module is a "module"-tagged `Env -> Object` namespace term, and basic objects are described below. The AST is plain-old data, easy to cache. The module is parameterized by an abstract source then linked to '%\*' primitives as the object Base.

The front-end compiler does not directly load files. Instead, it generates an AST that includes '%load' operations. This stage separation simplifies lazy loading and caching. Loading another file also involves of '%src.file' constructors relative to an abstract source. Abstraction of sources supports two design goals: control of filesystem dependencies, and location-independence of compilation.

The glas system enforces two major constraints on dependencies: "../" and absolute file paths are forbidden, and a file cannot be loaded twice. These rules simplify refactoring, sharing code, metaprogramming, and live coding. Every folder becomes a package. Sharing is made explicit in the namespace, e.g. via '%env.\*'. 

Modules support some OOP-like features: inheritance, override, mixins. In practice, developers can usefully distinguish between 'importing' a module like an independent object versus 'including' it like a mixin. When a user configuration inherits and overrides definitions from a community configuration, that's achieved via logical inclusion.

## Namespace

The [glas namespace](GlasNamespaces.md) is expressed as an extended lambda calculus, supporting reification of the environment, and explicit rules to bind and translate environments. We rely on lazy evaluation of the namespace.

## Objects

The [glas namespace model](GlasNamespaces.md) supports stateless objects. The basic object model is an "obj"-tagged `Env -> Env -> Env` term, with roles `Base -> Self -> Instance`. The open fixpoint 'Self' argument provides a basis for overrides, and 'Base' supports mixins or ultimately linking an object to a host.

Objects offer a consistent mechanims for extensibility, but effective use requires deliberate design. The glas system uses objects for front-end compilers, modules, and applications.

*Note:* Although the namespace-layer supports objects, the runtime program model does not.

## Applications

Applications are whatever the CLI knows how to run. 

Basic applications are "app"-tagged objects that define a transactional 'step' method and recognized event handlers like 'http' or 'rpc'. The runtime will bind object Base to 'sys.\*' effects APIs and some registers, repeatedly evaluate 'step', and occasionally invoke the event handlers. This design is useful for live coding, reactivity, and distribution; see [glas applications](GlasApps.md) for details.

But users may develop alternative application models, using distinct tags and configuring the application adapter to 'compile' these apps to a basic application.

## Programs

Modules assume access to [primitive program constructors](GlasProg.md) such as '%do', '%loop', and '%swap'. These support simple, structured procedural programs, operating on registers and data stack. Note that constructing a program does not run the program, i.e. `(%do P1 P2)` is abstract data representing a program that performs two operations when run.

Compared to the namespace's extended lambda calculus, these runtime programs are less expressive. The intention is to simplify optimizations and to avoid complications with live coding. 

The set of program constructors is minimal. Performance relies on acceleration, where the runtime substitutes a reference implementation of a function with an equivalent, but much faster, built-in. To ensure robust performance, acceleration is explicit, guided by annotations.

## Data

All [glas data](GlasData.md) is logically encoded into binary trees with edges labeled 0 or 1. The actual under-the-hood representation may be specialized for performance, guided by annotations.

## Annotations

Annotations are essentially structured comments embedded in the namespace AST. Annotations should not affect formal behavior of a valid program. But they may influence performance, verification, instrumentation, and reflection. The glas system relies on annotations; there are more annotation constructors than program primitives.

A proposed set of annotation constructors are defined with the [program model](GlasProg.md), e.g. `(%an.log Chan Message)` to support debugging, `%an.lazy.spark` to guide parallelism, and `(%an.arity 1 2)` to roughly describe the stack effect of a subprogram. But a runtime is free to introduce new or deprecate old annotations. To resist silent degradation, we'll warn about undefined annotation constructors.

