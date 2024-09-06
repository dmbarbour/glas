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

The `GLAS_CONF` environment variable should specify a configuration file, usually in the [glas init language (".gin")](GlasInitLang.md). If undefined, we'll use an OS-specific default such as `"~/.config/glas/conf.gin"` in Linux or `"%AppData%\glas\conf.gin"` on Windows. If this configuration file doesn't exist, users may be asked to create one via `glas --config init` or similar.

Configurations are modular. A user's configuration will generally reference a community or company 'distribution' then apply overrides relevant to user-specific preferences, authorities, and resources. The full configuration namespace might be very large, but this can be mitigated by lazy loading and evaluation. 

In addition to defining a massive application-layer module system, a configuration will define ad-hoc properties to configure the runtime. 

* *persistent database and cache* - Glas applications are well suited for orthogonal persistence, but if we want persistence we'll also need to configure storage locations. This must be runtime specific, because a runtime only recognizes some databases.
* *access to host resources* - Applications won't directly reference host resources, but instead reference configured filesystem 'roots', network interfaces by name, clocks, and FFI libraries. This is useful for both mirroring and sandboxing.
* *mirroring* - It is convenient to configure a distributed runtime to run distributed applications. This will also impact how host resources are referenced. 
* *logging, profiling, assertions* - Applications may contain annotations for features with static 'channels' that can be enabled, disabled, or otherwise configured. Obviously, the configuration would determine what's actually enabled. This should be application specific.
* *application environment* - instead of a runtime directly providing access to OS environment variables, it can let the configuration intervene.
* *RPC registries* - Applications may publish and subscribe to remote procedure call (RPC) 'objects'. The configuration specifies a registry where objects are published or discovered. A composite registry can filter and route RPC objects based on metadata, and can also rewrite metadata, providing an effective basis for security.

Many configuration options will be application specific. This is supported by allowing the configuration to access ad-hoc `settings.*` defined in the application namespace when evaluating these options. The configuration may also have access to OS environment variables and other cacheable properties.

*Note:* There is a tradeoff between flexibility and tooling. For example, it is more convenient to develop `glas --db` or `glas --cache` tools if we don't need to know application settings. 

## Application Module Namespace

At the toplevel, applications and application-layer modules will generally be defined under `module.*`. In case of `glas --run cli.opname` we'd be looking for `module.cli.opname` in the configuration. The 'module' prefix is mostly to prevent name collisions between modules and configuration options. 

However, this isn't a 'flat' namespace. Users will have sufficient means to control scope of a module's dependencies, i.e. such that "foo" might refer to `module.foo` in one case and `module.xyzzy.bar` in another. It is feasible to support 'private' utility modules shared between a subset of public modules. In addition to simplifying integration of modules from multiple communities, this lets us parameterize modules, or compile the same modules under multiple conditions.

### RPC Registry Configuration

tbd

Basic registry might be a shared service or distributed hashtable. Composite registries should support filtering and routing on RPC object metadata, and also rewriting of this metadata.

### Database Configuration

tbd

Possibly just specify a folder in the filesystem, let the runtime decide file format (e.g. LMDB or LSM tree).

## Running Applications

The `--run`, `--cmd`, and `--script` operations each compile an application module then run that application with provided command line arguments. The main difference for these three is how the module is introduced:

* `--run ConfiguredApp Args` - look for `module.ConfiguredApp` in the configuration, which should evaluate to an application namespace.
* `--cmd(.FileExt)+ ScriptText Args` - module is provided as script text, and we compile it as if it were a file with the given file extensions using `module.lang.ext` to process each extension. The script may load global modules. 
* `--script(.FileExt)* ScriptFile Args` - module is provided as a file outside the module system, interpreted based on given file extension (or actual extension if unspecified). The script may load global modules or local files.

Regardless of how the module is introduced, it should compile into an application program. This is usually a [transaction loop application](GlasApps.md), but it may be a *Staged Application* (see below), or some alternative mode recognized by the runtime. The run mode will be modeled as an application specific configuration option, perhaps via `settings.mode`.

*Note:* For performance, compilation may be cached by the runtime, subject to heuristics.

### Arguments Processing

Command line arguments are conventionally provided to an application as a list of strings. However, I find this awkward, hindering compositionality of applications. In many cases, it would be more convenient to separate arguments processing from the application logic. We can support this in the configuration, e.g. an application-specific 'compiler' for Args. If approached carefully, it is feasible to integrate with tab completion.

### Staged Applications

        glas --run StagedApp Args To Staged App -- Args To Next Stage

Staged applications allow us to treat the command-line as a user-defined language. A staged application might define `compile : Args -> App` similar to a language module, and perhaps `settings.mode = "staged"` to support runtime configuration. In this case, argument processing might also be expected to return a pair, representing which arguments are visible in each stage. The default arguments processor might split on `"--"`.

In context of live coding (via `sys.reload()`), staged applications can recompute all the stages. 

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

## Glas Command Shell

I envision users eventually 'living within' a live coded glas system. Instead of running individual applications, the user might (via command line or GUI) define `run.PID = App` and maintain a set of active applications via live coding. Instead of a GUI for a single app, we model a composite GUI for the composite app that renders subwindows for each `run.PID` app. 

I believe such a command shell can be modeled as an application with clever use of a reflection API. For example, we might model self-modifying code in terms of `sys.refl.patch.*` methods, adding a final bit of code to adjust the application namespace. Usefully, a patch could influence `settings.*` and integration, and automatically apply to modified source code in context of live coding.

Anyhow, this is a long term future goal, a direction to pursue after the command line interface is mature.
