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

The `GLAS_CONF` environment variable should reference a file expressed in the [glas initialization language](GlintLang.md), `".gin"` file extension. If undefined, we'll try an OS-specific default such as `"~/.config/glas/default.gin"` on Linux or `"%AppData%\glas\default.gin"` on Windows. If that does not exist, the glas command will fail and might suggest attempting `"glas --config init"`.

A typical glas configuration for an individual will import and inherit from community or company configuration then apply a few overrides for user-specific requirements, resources, and authorities.

Some features we'll configure:

* *Global modules.* Instead of search paths or package managers, every global module is explicitly specified in the configuration. We'll typically import community or company 'distributions' of packages into the configuration.
* *RPC registries.* Describe where to publish and subscribe to RPC objects, together with simple publish or subscribe filters. This serves as a content-addressed routing model for RPC.
* *Persistent data.* A transactional key-value database that can be shared between 'glas' commands. Often user or project specific. Glas systems will generally favor RPC over shared state.
* *Mirroring.* For performance and partitioning tolerance, some applications might want to run in a distributed runtime. We might describe multiple mirroring for those applications.
* *Logging and profiling.* We could configure where this information is stored.
* *Application configuration.* It can be convenient to supply applications some extra configuration or calibration data through the configuration, similar to environment variables. We might support packages of specialized configuration options that can be selected based on application `settings.*` methods.

A configuration can indirectly reference other environment variables, but this is easily sandboxed to support cross compilation. An application can potentially adjust runtime behavior dynamically via `sys.refl.*` methods. In any case, I specifically aim to avoid introducing a mess of command line options to configure the runtime.

## Naming Modules

A ModuleName is a direct reference to `module.ModuleName` in the configuration, and must meet the normal configuration syntax for a dotted path. The configuration must specify every global module as a local or DVCS folder, an inline value, or a staged computation.

The global module namespace isn't flat. That is, `sys.load(global:"math")` might not always refer to `module.math` when compiling a module. This is handled by including a module *Environment* in the specification of compiled global modules. 

This is a great boon for flexible integration and testing, but it can also be confusing for users to navigate. To mitigate the latter, we might configure the level of detail when compiling modules, or develop tools to detail the dependency graph for a module. 

## Running Applications

As mentioned in the start, the primary operation is to run an application.

        glas --run ModuleName Args

In this case, we first load the configuration, then we compile the module, then we attempt to interpret the module's compiled value as an application. Depending on the definition of `settings.mode` within the application, we might run in various modes. At the moment, only two modes are recognized:

* *step* - transaction loop, the default
* *stage* - compile arguments to another app (see below)

The run mode may influence which effects are provided, the application life cycle, and other things.

### Staged Applications

A staged application will be interpreted as a language module. That is, it should define `compile : SourceCode -> ModuleValue` and the only observable effect is to load modules (`sys.load`).

        glas --run StagedApp Args To App Constructor -- Args To Next Stage

In this case, the SourceCode is a list of command line arguments `["Args", "To", "App", "Constructor"]`. The returned value must be recognized as a compiled application module value. This is then run with remaining arguments `["Args", "To", "Next", "Stage"]`. The `"--"` separator is optional; if omitted, one is implicitly inserted as the final argument.

Staged applications are intended for use together with the `glas opname => glas --run cli.opname` syntactic sugar, allowing users to generally treat `opname` as specifying a language for the remainder of the glas command, with `--` as a consistent stage separator.

Use of `--cmd.FileExt (ScriptText) Args` can serve a similar role, using normal file language for the inline scripts, but it's inconvenient to directly edit a large ScriptText in most comand shells. See *Inline Scripting*. 

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

This allows familiar file-based languages to be used without a separate file. The most likely use case is if constructing ScriptText within another program. *Note:* There is no implicit erasure of an initial shebang line from ScriptText.

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

### Cache Tooling

        glas --cache Operation

Provide users some visibility and control for what is stored for performance. Useful operations:

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

Pre-bootstrap implementations of the glas executable might support only a limited subset of the effects API, such as console IO, an in-memory database, and perhaps the HTTP interface. To work within these limits, I propose to bootstrap by writing an executable binary to standard output then redirecting to a file.

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

It is feasible to support multiple targets through the configuration. Also, very early bootstrap might instead write to an intermediate C file or LLVM that we then compile further. However, I do hope to eventually bootstrap directly to OS-recognized executable binary.

