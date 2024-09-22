# Glas Command Line Interface

As a general rule, the glas executable takes the first argument to determine the 'language' for remaining arguments. There are a few different options for running programs, depending on where the program is sourced. 

        glas --run ConfiguredApp Args To App
        glas --script(.FileExt)* FileName Args To App
        glas --cmd(.FileExt)+ "Command Text" Args To App 
        glas --config ConfigOp ...
        # and so on, extensible

To support user defined extensions, we also have a simple syntactic sugar:

        glas opname Args To App
          # rewrites to
        glas --run cli.opname Args To App

We avoid command line switches for runtime configuration options. Instead, configuration is handled via combination of configuration file and application settings.

## Configuration

The `GLAS_CONF` environment variable should specify an initial configuration file with a file extension and associated syntax understood by the 'glas' executable. If the environment variable is not defined, we'll try an OS-specific default such as `"~/.config/glas/conf.g"` in Linux or `"%AppData%\glas\conf.g"` on Windows. If the configuration file doesn't exist, users may be asked to create one, perhaps via `glas --config init`.

Configurations are modular. A typical user's configuration will generally extend a community or company configuration from DVCS, overriding a few definitions for user preferences, projects, authorities, or resources. The community configuration may define a massive library of applications and reusable application components. This is mitigated by lazy loading and caching.

In addition to defining a applications, the configuration will define runtime options. 

* *persistent database and cache* - Glas applications are well suited for orthogonal persistence, but we'll need to configure this.
* *reference host resources* - most effects APIs (network, filesystem, clock, FFI, etc.) will have some indirection through the configuration when binding resources. 
* *mirroring* - it's easiest to define a distributed application in context of a distributed runtime environment.
* *logging, profiling, assertions* - enable and disable channels, redirect streams, random sampling, max counts, etc.. 
* *application environment* - instead of directly providing access to OS environment variables, it can let the configuration intervene.
* *RPC registries* - publish and subscribe to remote 'objects', routing and filtering smaller registries for security.

Many configuration options, such as logging, will be application specific. This is supported indirectly: the application defines `settings.*`, and these are made accessible as implicit parameters when evaluating some expressions in the configuration. Similarly, configuration options such as network interfaces and clocks may be mirror specific.

All configuration options are ultimately runtime specific. Runtimes must document which configuration names they recognize, their expected types, and their meaning. Of course, in practice we'll develop de-facto standards across 'glas' implementations. To support a more adaptive configuration, a runtime can potentially supply some stable information about itself through the `sys.*` namespace. 

*Note:* There is a tradeoff between flexibility and tooling. For example, it is inconvenient to develop general `glas --db` or `glas --rpc` tools if persistence and RPC are application specific.

## Application Module Namespace

At the toplevel, applications and application-layer modules will generally be defined under `module.*`. In case of `glas --run cli.opname` we'd be looking for `module.cli.opname` in the configuration. The 'module' prefix prevents accidental name collisions between modules and configuration options. Dependencies can be localized, i.e. `"import math"` might refer to different modules in context of compiling different applications. But aside from conventions for compilation flags, we'll usually favor consistent names within a community.

### RPC Registry Configuration

tbd

Basic registry might be a shared service or distributed hashtable. Composite registries should support filtering and routing on RPC object metadata, and also rewriting of this metadata.

### Database Configuration

tbd

Possibly just specify a folder in the filesystem, let the runtime decide file format (e.g. LMDB or LSM tree).

## Running Applications

The `--run`, `--cmd`, and `--script` operations each compile an application module then run that application with provided command line arguments. The main difference for these three is how the module is introduced:

* `--run ConfiguredApp Args` - look up `module.ConfiguredApp` in the configuration. This should evaluate to an application namespace.
* `--cmd.FileExt ScriptText Args` - module is provided as script text. We'll compile it using the default `module.*` compilation context, then run. 
* `--script(.FileExt)? ScriptFile Args` - module is a script file outside the module system. If this file starts with a shebang line, it's logically removed before processing. If a file extension is given, the actual file extension is ignored.

Regardless of how the module is introduced, it should compile into a recognized program value. This usually represents a [transaction loop application](GlasApps.md), but the configuration can look at `settings.*` and decide it's a *Staged Application* or something else.

Many computations may be cached such that running the same application a second time is a lot more efficient.

### Arguments Processing 

Command line arguments are conventionally provided to an application as a list of strings. We could easily do the same here, but I've always found this awkward, especially when we begin to compose applications. I think it would be better if arguments were parsed before the application sees them. Even better if this parsing supports tab completions, help documentation, and so on such before the application is run.

A viable solution is to extend the configuration with an application specific argument parser.

### Staged Applications

        glas --run StagedApp Args To Staged App -- Args To Next Stage

Staged applications allow us to treat the command-line as a user-defined language. A staged application might define `compile : Args -> App` similar to a language module, and perhaps `settings.mode = "staged"` to support runtime configuration. In this case, argument processing might also be expected to return a pair, representing which arguments are visible in each stage. The default arguments processor might split on `"--"`.

In context of live coding (via `sys.reload()`), staged applications can recompute all the stages. 

## Command Shells and Interactive Development

I envision users mostly 'living within' a live-coded glas system instead of using lots of 'glas' commands on a command line. Instead of running specific applications on the command line, user actions would manipulate a live coding environment, adding and removing active 'applications' at runtime. This seems feasible via [REPL or Notebook interface](GlasNotebooks.md). 



## Built-in Tooling

The glas executable might provide a variety of associated tools:

* `--config` - initialize, inspect, or rewrite a configuration. Might support debug evaluation of configuration properties, pretty-printing results, or writing binary output to stdout.
* `--module` - operations to inspect the module system, e.g. browse modules, or ensure a module compiles or passes tests without running it.
* `--db` - browse the persistent key-value database, continuously watch for changes, or even perform changes manually.
* `--cache` - summarize, inspect, invalidate, or clear the cache.
* `--rpc` - inspect the RPC registry, perhaps manually invoke some RPC methods.

Built-in tooling should be balanced against bloat. A feature that requires more logic should have a stronger justification to provide as a built-in. But there are many useful features that won't cost much.

## Bootstrap

Pre-bootstrap implementations of the glas executable might support only a limited subset of the effects API, such as console IO, an in-memory database, and perhaps the HTTP interface. To work within these limits, I propose to bootstrap by writing an executable binary to standard output then redirecting to a file.

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

It is feasible to support multiple targets through the configuration. Also, very early bootstrap might instead write to an intermediate C file or LLVM that we then compile further. However, I do hope to eventually bootstrap directly to OS-recognized executable binary.
