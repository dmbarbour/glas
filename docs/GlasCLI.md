# Glas Command Line Interface

The primary operation for the glas command line interface is to run an application:

        glas --run ModuleName Args To App

This combines with a lightweight syntactic sugar:

        glas opname a b c 
            # rewrites to
        glas --run cli.opname a b c

The referenced module may directly describe an application or may describe a staged compiler, treating the remaining arguments as a program in a user-defined language. Aside from `--run`, the glas executable will support a few alternative run modes and a few useful toolkits. 

## Runtime Configuration

The `GLAS_CONF` environment variable should reference a local file expressed in the [glas initialization language](GlasInitLang.md), favoring the `".gin"` file extension. If undefined, we'll try an OS-specific default such as `"~/.config/glas/default.gin"` on Linux or `"%AppData%\glas\default.gin"` on Windows. The actual configuration should be a namespace 'config' defined within this file. 

A typical configuration file will import and inherit from a community or company configuration, then apply a few overrides for user-specific authorities, resources, and preferences. Even if the full configuration is very large, the user's configuration file may be small.

Some features we'll configure:

* *Global modules.* Instead of search paths or package managers, every global module is explicitly specified in the configuration. We'll typically import community or company 'distributions' of packages into the configuration.
* *RPC registries.* Describe where to publish and subscribe to RPC objects, together with simple publish or subscribe filters. This serves as a content-addressed routing model for RPC.
* *Persistent database.* A transactional key-value database that can be shared between 'glas' commands. Databases are often user specific; we'll mostly rely on shared RPC services for shared state.
* *Logging and profiling.* We could configure where this information is stored.
* *Application configuration.* The configuration can override, mask, and extend the OS environment variables presented to an application, and usefully includes support for structured data. 
* *Mirroring.* For performance and partitioning tolerance, some applications might want to run in a distributed runtime. We might describe multiple mirroring for those applications.

To support adaptive configurations, OS environment variables and application specific settings may be late bound into a configuration namespace. This provides a layer of indirection and opportunity for user-defined abstraction for additional environment variables or command line input via staged applications. Additionally, a runtime may support dynamic configuration via reflection methods.

## Module Names

The ModuleName must match the normal configuration syntax for a dotted path, and is implicitly a reference to `module.ModuleName`. Depending on the configuration, some modules might be hidden from the user, but this is mitigated by *staged applications* and freedom to edit the configuration.

## Running Applications

As mentioned in the start, the primary operation is to run an application.

        glas --run ModuleName Args

In this case, we first load the configuration, then we compile the module, then we attempt to interpret the module's compiled value as an application. By default we'd run as a [transaction loop application](GlasApps.md) but other options might be indicated by `settings.mode`:

* *loop* - transaction loop application
* *stage* - staged applications (see below)
* *test* - run as a test application

In general, methods in `settings.*` will be visible to the configuration when evaluating specific configuration options, allowing for application specific settings.

### Staged Applications

A staged application will be interpreted as a language module. That is, it should define `compile : SourceCode -> ModuleValue` and the only observable effect is to load modules (`sys.load`).

        glas --run StagedApp Args To App Constructor -- Args To Next Stage

In this case, the SourceCode is a list of command line arguments `["Args", "To", "App", "Constructor"]`. The returned value must be recognized as a compiled application module value. This is then run with remaining arguments `["Args", "To", "Next", "Stage"]`. The `"--"` separator is optional; if omitted, one is implicitly added as the final argument.

Staged applications are intended for use with the `glas opname => glas --run cli.opname` syntactic sugar. This enables 'opname' to specify a language for the remainder of the command.

*Note:* The interface for *Inline Scripting* can serve a similar role.

### Scripting

In some cases, we want to run a file instead of a module. Proposed operations:

        glas --script (ScriptFile) (Args)
        glas --script.FileExt (ScriptFile) (Args)

In latter case, we'll use the provided `.FileExt` in place of the actual file extension. Scripts are intended usage context is a shebang script file within Linux:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips the first line if it starts with '#!' (shebang), then compiles the remaining content in accordance with the specified file extension. The script may reference local and global modules, with global modules implicitly localized as `module.*` and local modules being relative to the script file location.

By default, the script will use the user's `GLAS_CONF` configuration. If this is inappropriate, in Linux one might use 'env' with the '-S' option such as:

        #!/usr/bin/env -S GLAS_CONF=/etc/glas.conf /usr/bin/glas --script.g 

For Windows, there's probably something similar one could do with powershell.  

### Inline Scripting

An obvious variation on the above is to embed the script text into the command line. Proposed operation:

        glas --cmd.FileExt ScriptText Args

The ScriptText will be compiled under the languages indicated by the file extensions with access to global modules. The main differences from `--script`: there is no attempt to remove a shebang line, and local modules are relative to the caller's working directory.

## Tooling

The glas may contain a few built-in tools. Anything that would significantly increase executable size should be avoided as a built-in. By convention, we should also support an extended suite in the module system.

        glas --config Operation     # built-ins
        glas config Operation       # full suite

The two should be consistent in meaning and intention for all Operations that are implemented for both.

### Configuration Tooling

        glas --config Operation

Tools to debug the configuration in general, or to use a configuration as a simple language. Useful operations:

* *init* - help users create a configuration file, perhaps interactively
* *check* - look for obvious errors or concerns in the configuration file 
* *print Expr* - evaluate and pretty-print an expression in config namespace
* *debug Expr* - like print, but add verbose debugging and profiling information.
* *extract Expr* - require Expr evaluates to a binary, write it raw to stdout.
* *help* - describe available operations

### Module System Tooling

        glas --module Operation

This should be extra tooling to discover modules and to debug modules. Useful operations:

* *list Filters* - scan the configuration then print a list of available modules. The default might include the module name and a short text description. Filters could allow for restricting the scan and controlling which annotations are displayed.
* *check ModuleName* - pass/fail for compiling a module without running it. Will print any compile-time log messages.
* *deps ModuleName* - print transitive dependencies of a module in exacting detail.
* *test Filters* - run tests on modules in the module system
* *help* - describe available operations

### Other Ad-hoc Tooling

Some potential operations:

* `--cache` - summarize, invalidate (force re-validation), or clear the cache
* `--db` - browse the configured key-value database, possibly watch for changes
* `--rpc` - browse the configured RPC registry, manually call RPC methods
* `--repl` - perhaps starts a simple web service to view files as [REPL sessions](GlasREPL.md)

Basically, anything that would be very useful pre-bootstrap should be considered as a potential built-in, then filtered based on what can be implemented without overly enlarging the glas executable. 

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

We'll rely on log messages for detailed warnings or errors. But we might allow applications to set an exit code more generally.

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

