# Glas Command Line Interface

The glas executable always takes the first argument to determine the 'language' for remaining arguments. 

        glas --run AppName Args To App
        glas --cmd(.FileExt)+ "Command Text" Args To App 
        glas --script(.FileExt)* FileName Args To App
        glas --config ConfigOp ...
        # and so on, extensible!

        glas opname Args To App
          # rewrites to
        glas --run cli.opname Args To App

Instead of command-line switches for runtime options (GC, JIT, logging, profiling, etc.), glas pushes these to the configuration and application settings.

## Configuration

The glas configuration is specified by the `GLAS_CONF` environment variable and is expressed in the [glas init language](GlasInitLang.md). If `GLAS_CONF` is undefined, we'll use a reasonable OS-specific default, e.g. `"~/.config/glas/conf.g"` in Linux or `"%AppData%\glas\conf.g"` on Windows. If this file doesn't exist, users might be asked to run `glas --config init` with some good defaults.

In my vision for glas systems, the user's configuration file will usually reference a community or company configuration then apply a few overrides to integrate user-specific preferences, authorities, or resources. Insofar as users are also developers, they might redirect some modules to the user's system or a private DVCS.

The configuration namespace ultimately defines both runtime options and applications. Unlike most conventional systems, there is no strong boundary between configuration and application code. Instead, we'll develop conventions and best practices. Although it is feasible to define an application wholly within a configuration, we'll usually reference separate files to be loaded when evaluating an application namespace.





The configuration is modular and evaluates to a namespace that defines not only runtime settings, but also a namespace of program modules and applications. It is feasible to directly define apps within the configuration, but we'll usually develop applications in separate files then integrate them. 







* *global modules* - Instead of a separate package manager, the glas configuration directly defines module, translating module names into specifications. 
* *RPC registries* - Applications may publish and subscribe to remote procedure call (RPC) 'objects'. The configuration specifies a registry where objects are published or discovered. A composite registry can filter and route RPC objects based on metadata, and can also rewrite metadata, providing an effective basis for security.
* *persistent database and cache* - Glas applications are well suited for orthogonal persistence, but if we want persistence we'll also need to configure storage locations. This must be runtime specific, because a runtime only recognizes some databases.
* *logging and profiling* - An application program may contain annotations for logging and profiling with static, labeled 'channels'. The configuration may configure each channel, e.g. enable or disable, direct to file, log to stderr, etc..
* *mirroring* - For transaction loop applications, mirroring is best modeled in terms of implementing a distributed runtime, such that a single application is running on multiple remote nodes.
* *application environment* - instead of a runtime directly providing access to OS environment variables, it can let the configuration intervene.

Ultimately, configuration options are runtime specific. Different versions of the `glas` executable may recognize different configuration options. However, configuration options are also subject to de-facto standardization by popular runtime implementations and shouldn't vary wildly in practice. I'll describe some proposed conventions below.

### Application Settings

A subset of runtime configuration options will be application specific. This is supported by letting a configuration call `settings.*` methods defined by the application module. The runtime never directly observes application settings, ensuring the configuration has an opportunity to interpret and override settings. Application settings are ad-hoc and subject to de-facto standardization within a community.

There are known tradeoffs with tooling. For example, if persistent storage is application specific, it becomes difficult to develop `glas --db` or `glas --cache` tools to inspect and manipulate storage. A runtime may sacrifice flexibility to simplify tooling in some cases.

### Configured Modules

I propose that global modules are defined under `module.*` in the configuration, ensuring the set of modules is finite. Each module is defined by a specification paired with ad-hoc annotations, such as a text blurb that might be printed when browsing modules. 

        type ModuleDesc 
              = (spec:ModuleSpec
                ,desc:TextBlurb
                ,... # ad-hoc annotations
                )

        type ModuleSpec 
              = file:(at:Location, ln:Localization)
              | data:PlainOldData
              | eval:(lang:FileExt, src:ModuleName, ln:Localization)

Most modules will be specified as files. The location is a file in the local filesystem or a remote DVCS with a URL and access tokens. The localization is a rewrite applied when searching for configured modules, e.g. `sys.load(conf:"foo")` might rewrite to load `module.foo`. Files are processed based on extension, e.g. a file with extension ".x.y" would be compiled by "lang.y" then "lang.x", also subject to localization. As a special case, a standard implementation for "lang.g" is built-in and will be bootstrapped if possible.

Locations and localizations are special types in the configuration language. Locations are special types so we can preserve relative file paths across imports. Localizations are special types so we can preserve relative location within the configuration namespace in context of translations and renames.

Support for inline 'data' is convenient for configuration parameters or to avoid small files. It is feasible to further extend ModuleSpec, e.g. for staged computing.

### RPC Registry Configuration

tbd

Basic registry might be a shared service or distributed hashtable. Composite registries should support filtering and routing on RPC object metadata, and also rewriting of this metadata.

### Database Configuration

tbd

Possibly just specify a folder in the filesystem, let the runtime decide file format (e.g. LMDB or LSM tree).

## Running Applications

The `--run`, `--cmd`, and `--script` operations each compile an application module then run that application with provided command line arguments. The main difference for these three is how the module is introduced:

* `--run AppName Args` - AppName refers to a configured definition (or collection thereof), which must be recognized as a module specification by the runtime. 
* `--cmd(.FileExt)+ ScriptText Args` - module is provided as script text, and we compile it as if it were a file with the given file extensions. The script may load global modules but cannot reference files.
* `--script(.FileExt)* ScriptFile Args` - module is provided as a file outside the module system, interpreted based on given file extension (or actual extension if unspecified). The script may load global modules and local files.

Regardless of how the module is introduced, it should compile into an application program. This is usually a [transaction loop application](GlasApps.md), but it may be a *Staged Application* (see below), or some alternative mode recognized by the runtime. The run mode will be modeled as an application specific configuration option, perhaps via `settings.mode`.

### Staged Applications

A staged application might define `settings.mode = "staged"` and `compile : Args -> App` like a language module. The runtime never directly observes settings, but may read a configured 'run-mode' that depends on `settings.mode`. After recognizing a staged application, the runtime compiles command line arguments into another application, then runs it.

        glas --run StagedApp Args To Staged App -- Args To Next Stage

By default, we could support a '--' separator for staged arguments. Thus, in the example we might evaluate `compile ["Args", "To", "Staged", "App"]` then pass `["Args", "To", "Next", "Stage"]` as arguments to the returned application (which might also be staged, in general). Alternative handling of arguments could also be configurable based on settings.

Staged applications serve at least two roles in glas systems. First, they support user-defined command line languages, especially when combined with the `glas opname => glas --run cli.opname` syntactic sugar. Second, staged applications enable users to modify runtime configuration options via the command line: `settings.*` of the next stage may depend on arguments to the current stage. 

## Built-in Tooling

The glas executable might provide a variety of associated tools:

* `--config` - initialize, inspect, or rewrite a configuration. Might support debug evaluation of configuration properties, pretty-printing results, or writing binary output to stdout.
* `--module` - operations to inspect the module system and typecheck, test, or validate modules without running an app.
* `--db` - browse the persistent key-value database, perhaps create a long-running process that prints values as they change over time.
* `--cache` - summarize, inspect, invalidate, or clear the cache.
* `--rpc` - inspect the RPC registry, perhaps manually invoke some RPC methods.
* `--repl` - support for file-based [REPL sessions](GlasREPL.md).

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

