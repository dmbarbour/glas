# Glas Command Line Interface

The primary operation for the glas command line interface is to run an application:

        glas --run ModuleName Args To App

This combines with a lightweight syntactic sugar:

        glas opname a b c 
            # rewrites to
        glas --run cli.opname a b c

The referenced module may directly describe an application or may describe a staged compiler, treating the remaining arguments as a program in a user-defined language. Aside from running a module, we'll provide a few alternative run modes and toolkits via commands other than `--run`. 

        # running files and inline scripts
        glas --script(.Lang)* ScriptFile Args
        glas --cmd(.Lang)+ ScriptText Args

        # built-in toolkits 
        glas --config (Operation)
        glas --module (Operation)
        glas --cache (Operation)

        # conventions
        glas --version
        glas --help

But the executable shouldn't have too much more built-in logic than is required to compile and run a module. Ideally, most logic is eventually shifted into the module system. For example, the `--module` toolkit should eventually be extended and improved by a `cli.module` application in the module system.

## Configuration

The glas executable will look for environment variable `GLAS_CONF`, which should name a configuration file. If this environment variable is undefined, the default file path is OS specific: `"~/.config/glas/default.conf"` on Linux or `"%AppData%\glas\default.conf"` on Windows. 

This configuration file should use the [glas configuration language](GlasConfigLang.md). Although this language aims to be simple, there is a lot to configure. For example, instead of search paths, every global module will be independently named in the configuration. Use of ModuleRef 'cli.opname' on the command line will map to 'module.cli.opname' in the configuration. To mitigate this, the configuration language supports imports, inheritance, and overrides. A user's configuration might inherit a distribution maintained by a company or community.

Some things we might configure via file:

* *global modules* - Mapping global module names to locations. Also supports constants and staged modules. No support for search paths, but we can eventually import community 'distributions'.
* *key-value database* - For orthogonal persistence and asynchronous communication. We'll probably start with a file-based database, perhaps LMDB or LSM tree. We might eventually want distributed databases.
* *RPC registries* - Where to publish and discover RPC objects. In general will route to multiple registries with varying trust levels using tag-based filters.
* *mirroring* - Automatically deploy applications to remote nodes for performance and network partitioning tolerance.
* *config vars* - environment vars via the config
* *ad-hoc runtime options* - such as logging and profiling
* *content delivery networks* - For large values, communicated frequently.
* *proxy compiler and cache* - For large computations, performed frequently.

However, the expected use case is to share the same configuration file between many glas commands line operations. Some properties cannot (or should not) be shared between operations. In these cases, applications may define ad-hoc `settings.*` methods to be evaluated by a late stage compiler or runtime, with limited access to effects. Alternatively, the application might call `sys.refl.*` methods to dynamically configure a runtime, potentially limited to `start()` and `switch()` operations.

Ultimately, all settings are provided either through the configuration file or through the application, and nothing is directly configured by command line switches or environment variables. If users want configure `settings.*` via command line, it is possible to develop a staged application for that role.

## Naming Modules

For the primary run operation, we must name a module:

        glas --run ModuleName Args

The ModuleName is restricted syntactically to a public name in the configuration. The glas executable will implicitly add a 'module.' prefix then evaluate this name within the configuration. The glas executable will report an error if the referenced module is undefined or its definition is not recognized as a global module.

## Running Applications

As mentioned in the start, the primary operation is to run an application.

        glas --run ModuleName Args

In this case, we first load the configuration, then we compile the module, then we attempt to interpret the module's compiled value as an application. Depending on the definition of `settings.mode` within the application, we might run in various modes:

* *step* - transaction loop, also the default
* *lang* - language module, 'compile' some arguments to another app (see below)

The run mode may influence which effects are provided, the application life cycle, and other things. I might eventually introduce a more conventional procedural mode, perhaps called *main*. 

### Staged Applications

A staged application will be interpreted as a language module. That is, it should define `compile : SourceCode -> ModuleValue` and the only observable effect is to load modules (`sys.load`).

        glas --run StagedApp Args To App Constructor -- Args To Next Stage

In this case, the SourceCode is a list of command line arguments `["Args", "To", "App", "Constructor"]`. The returned value must be recognized as a compiled application module, e.g. `glas:app:Application`. This is then run with remaining arguments `["Args", "To", "Next", "Stage"]`. The `"--"` separator is optional; if omitted, it is inserted implicitly as the final argument.

Between staged applications and the syntactic sugar for user-defined operations, users can effectively extend the glas command line language just by defining modules. The main alternative is *Inline Scripting* (see below) but it is relatively awkward to work with large argument strings in most command shells.

### Scripting

In some cases, we want to run a file instead of a module. Proposed operations:

        glas --script (ScriptFile) (Args)
        glas --script.FileExt (ScriptFile) (Args)

In latter case, we'll use the provided `.FileExt` in place of the actual file extension. Scripts are intended usage context is a shebang script file within Linux:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips the first line if it starts with '#!', then compiles the remaining content in accordance with the specified file extension. The script may reference global modules, implicitly localizing as `{ "" => "module." }`. Attempts to load a local module will fail.

By default, the script will use the user's `GLAS_CONF` configuration. If this is inappropriate, in Linux one might use 'env' with the '-S' option such as:

        #!/usr/bin/env -S GLAS_CONF=/etc/glas.conf /usr/bin/glas --script.g 

For Windows, there's probably something similar one could do with powershell.  

### Inline Scripting

An obvious variation on the above is to embed the script text into the command line. Proposed operation:

        glas --cmd.FileExt ScriptText Args

This allows familiar file-based languages to be used without a separate file. The most likely use case is if constructing ScriptText within another program. In this case, there is no automatic removal of a shebang line.

## Tooling

The glas executable may contain a few built-in tools, but anything that would significantly increase executable size should instead represented in the module system. For example, we might have a `glas config` tool (defined by 'module.cli.config') that extends operations available via `glas --config`. 

In practice, the glas command line executable will support a fair bit of debugging logic to support `sys.refl.http` and similar APIs. It would be convenient if tooling can provide access to much of this.

### Configuration Tooling

Tools to debug the configuration in general, or to use a configuration as a simple language.

        glas --config Operation

Useful operations:

* *init* - help users create a configuration file, perhaps interactively
* *check* - look for obvious errors or concerns in the configuration file 
* *print Expr* - evaluate and pretty-print an expression in config namespace
* *debug Expr* - like print, but add verbose debugging and profiling information.
* *extract Expr* - require Expr evaluates to a binary, write it raw to stdout.
* *help* - describe available operations

### Module System Tooling

This should be extra tooling to discover modules and to debug modules.

        glas --module Operation

Useful operations:

* *list Filters* - scan the configuration then print a list of available modules. The default might include the module name and a short text description. Filters could allow for restricting the scan and controlling which annotations are displayed.
* *check ModuleName* - pass/fail for compiling a module without running it. Will print any compile-time log messages.
* *deps ModuleName* - print transitive dependencies of a module in detail.
* *test Filters* - run tests on modules in the module system
* *help* - describe available operations

### Cache Tooling

Provide some user access and control to what is being stored for performance.

        glas --cache Operation

Useful operations:

* *debug Filters* - describe what is held in cache
* *clear Filters* - delete the cache or perhaps parts of it (e.g. DVCS, files)
* *help* - describe available operations

Other operations will likely depend on exactly how cache is represented.

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

We'll rely on log messages for detailed warnings or errors. But we might allow applications to set an exit code more generally.

## Bootstrap

Pre-bootstrap implementations of the glas executable might support only a limited subset of the effects API, such as read-write to console and access to an in-memory database. To work within these limits, I propose to bootstrap by writing an executable binary to standard output then redirecting to a file.

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

It is feasible to support multiple targets through the configuration. Also, very early bootstrap might instead write to an intermediate C file or LLVM that we then compile further. However, I do hope to eventually bootstrap directly to OS-recognized executable binary.

