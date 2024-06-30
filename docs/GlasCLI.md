# Glas Command Line Interface

The primary operation for the glas command line interface is to run an application:

        glas --run ModuleName Args To App

This combines with a lightweight syntactic sugar:

        glas opname a b c 
            # rewrites to
        glas --run cli.opname a b c

The referenced module may directly describe an application or may describe a staged compiler, treating the remaining arguments as a program in a user-defined language. The intention is to shift most logic into the glas module system, and to keep the command line simple and free of clutter.

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

### Configuration Tooling

The glas command line interface can provide a toolbox under `--config`. 

        glas --config Operation

A few potentially useful operations include:

* *init* - help users create a configuration file, perhaps interactively
* *check* - look for obvious errors or concerns in the configuration file 
* *print Expr* - evaluate and pretty-print an expression in config namespace
* *debug Expr* - like print, but add verbose debugging information.
* *extract Expr* - like print, but limited to strings Expr, direct to stdout
* *help* - describe available operations

However, I hope to avoid built-in features that would significantly grow the 'glas' executable. In my vision for glas systems, users should push most logic into the module system itself, including the logic for an extended toolbox.

        glas config Operation
            # rewrites to
        glas --run cli.config Operation

This can support flexible, user-defined extensions to the tooling.

## Naming Modules

For the primary run operation, we must name a module:

        glas --run ModuleName Args

For simplicity, ModuleName is syntactically restricted:

        ModuleName <= (Word)('.'Word)*
        Word <= [a-z][a-z0-9]+

This module must be defined within the configuration as 'module.ModuleName', otherwise the operation will fail. Due to localization, the same ModuleName might refer to a different global module when loaded from a module. Effectively, we're applying implicit localization `{ "" => "module." }` to the user. 

*Note:* Files cannot be referenced by '--run', but you can use '--script' instead.

## Module System Tooling

Every global module is explicitly defined within the configuration. In addition to a specification, which roughly describes how to compile a module into a value, each definition may include ad-hoc annotations, such as a text blurb describing the module, or a list of tags to support search.

To help users work with this module system, and debug problems, we might introduce various command line tools to list, search, and filter modules, to examine dependencies, check whether a module compiles, and so on. 

        glas --module Operation

Some useful operations:

* *list ModuleNames* - print list of toplevel modules and descriptions (if defined) matching given names.
* *check ModuleNames* - try to compile the listed modules, reporting pass/fail for each, including any logged warnings or errors.
* *deps ModuleNames* - compile modules and produce a summary of transitive dependencies, down to the file granularity. Maybe include secure hashes.

Here ModuleNames is a plural of ModuleName. It might be expressed as a non-empty list of ModuleName extended with '*' wildcards. We might eventually want variations that can filter on tags and other properties.

As with '--config', I don't want to add too much logic to the glas executable to support this, but if it's just a little extra on top of what is already needed for '--run' then that's fine.

## Cache Tooling

We might also want a `--cache` toolkit for examining how much space we're using, clearing out stuff we haven't used in a while, forcing review of DVCS repositories, and so on. This would apply to both compiling modules and processing configuration files.

## Testing

By convention, automated tests are added to the system by defining `test-*` local modules within the folders used by global modules. Each test application may define multiple tests and also use `sys.fork` for fuzz testing. Ideally, we'll have some command line tools to help run tests, and also maintain a database of test results. We might need a `--test` option to cover some basics, but I hope to push most logic for testing into the module system.

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

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

We'll rely on log messages for detailed warnings or errors. But we might allow applications to set an exit code more generally.

## Bootstrap

The bootstrap implementation of 'glas' might support only a limited subset of effects, such as CLI output and process local state. We can bootstrap by writing the executable and redirecting to a file.

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

The target architecture could be provided as an argument or by defining a 'target' module.

*Note:* It is feasible to support early bootstrap via intermediate ".c" file or similar, to leverage a mature optimizing compiler. But I hope to eventually express all optimizations within the glas module system!

## Misc

### Early Applications

The executable should have minimum logic, but where should I focus initial attention for user-defined apps?

* Bootstrap
* Browsing module system.
* Automated Testing
* Command line calculator
* REPLs
* [Language Server Protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol)
* FUSE filesystem support
* IDE projectional editors, programmable wikis
* Web apps? (Compile to JavaScript or WebAssembly + DOM)
* Notebook apps? (something oriented around reactive, live coding with graphics?)



