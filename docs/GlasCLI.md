# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef Args To App

This combines with a lightweight syntactic sugar:

        glas opname a b c 
            # rewrites to
        glas --run cli.opname a b c

The referenced module may directly describe an application or may describe a staged compiler, treating the remaining arguments as a program in a user-defined language. The intention is to shift most logic into the glas module system, and to keep the command line simple and free of clutter.

## Configuration

The glas executable will look for environment variable `GLAS_CONF`, which should name a configuration file. If this environment variable is undefined, the default file path is OS specific: `"~/.config/glas/default.conf"` on Linux or `"%AppData%\glas\default.conf"` on Windows. 

This configuration file should use the [glas configuration language](GlasConfigLang.md). Although this language aims to be simple, there is a lot to configure. For example, instead of search paths, every global module will be independently named in the configuration. Use of ModuleRef 'cli.opname' on the command line will map to 'module.cli.opname' in the configuration. To mitigate this, the configuration language supports imports, inheritance, and overrides. A user's configuration might inherit a distribution maintained by a company or community.

Some things we'll configure via file:

* *global modules* - Mapping global module names to locations. Also supports constants and staged modules. No support for search paths, but we can eventually import community 'distributions'.
* *key-value database* - For orthogonal persistence and asynchronous communication. We'll probably start with a file-based database, perhaps LMDB or LSM tree. We might eventually want distributed databases.
* *RPC registries* - Where to publish and discover RPC objects. In general will route to multiple registries with varying trust levels using tag-based filters.
* *application config vars* - an extended set of environment variables
* *ad-hoc runtime options* - such as logging and profiling
* *mirroring* - Automatically deploy applications to remote nodes for performance and network partitioning tolerance.
* *content delivery networks* - For large values, communicated frequently. Based around content addressed data.
* *proxy compiler and cache* - For large computations, often shared between processes.

However, file-based configuration is unsuitable for some properties. For example, the bindings for RPC and HTTP requests cannot be shared between concurrent OS processes. In these cases, we'll rely on application specific `settings.*` methods or dynamic configuration via `sys.refl.*` methods. 

We specifically won't add command line switches.

## Global Module Distribution



## Secondary Operations

Aside from '--run'


## Global Module Distribution


Every ModuleRef must be defined in the [glas runtime configuration file](GlasConfigLang.md). The simplest definition is a file path, but glas systems will eventually support remote modules via DVCS repositories.



 The configuration file is specified by the GLAS_CONF environment variable. 
 in the glas configuration. However, it may instead refer to a staged compiler  


The ModuleRef binds a name in the glas system configuration. Modules are generally included in the configuration namespace under 'distro.', so we'd look for 'distro.cli.opname' in the configuration, which must evaluate to  

 to the glas runtime configuration, a file referenced by environment variable GLAS_CONF. 

By combining this syntactic sugar with *Staged Applications* (see below), glas supports user defined command line languages.




## ModuleRef

A ModuleRef is a string that uniquely identifies a module. Initially, this may be a global module or a file path.  


A FilePath is heuristically recognized by containing a directory separator (such as '/'). The glas command line interface will attempt to interpret any file or folder as a module, as requested. Otherwise, we'll search the runtime configuration for a global module of the given name. 

## Configuration

To avoid clutter, I hope to keep runtime configuration options off the command line. This limits configuration to environment variables, configuration files, and the application itself. 

To simplify switching of configurations, I propose for `GLAS_CONF` environment variable to specify a configuration file. If unspecified, the default configuration file is OS specific, e.g.  This file uses the [glas configuration language](GlasConfigLang.md).

This configuration file should describe system-wide features such as global modules, a shared key-value database, the RPC registry. Additionally, it may describe defaults for instance specific features such as quotas, memory management, logging options, and so on. However, the latter may be subject to application tuning via `conf.*` or `sys.refl.conf.*` methods.

## Running Applications

        glas --run ModuleRef Args

The glas executable first compiles the referenced module into a structured value. Initially, we'll recognize `glas:(app:Application, ...)`, i.e. the compiled output of a ".glas" module that defines 'app'. The Application type is itself a structured value representing a [namespace](GlasNamespaces.md) of methods. At this point, the methods have been compiled to an intermediate language, a Lisp-like [abstract assembly](AbstractAssembly.md). AST constructor methods and system methods (`%*` and `sys.*` by convention) are left abstract to be implemented by the runtime.

The default interpretation is a [transaction loop](GlasApps.md). In addition to evaluating the 'step' transaction forever, the runtime may recognize interfaces such as 'http' or 'gui' and implicitly implement an HTTP service or GUI user agent.

An alternative interpretation may be indicated by defining `settings.mode`. The main alternative is a staged application. 

### Staged Applications

A staged application will be interpreted as a language module. That is, it should define `compile : SourceCode -> ModuleValue` and the only observable effect is to load modules (`sys.load`).

        glas --run StagedApp Args To App Constructor -- Args To Next Stage

In this case, the SourceCode is a list of command line arguments `["Args", "To", "App", "Constructor"]`. The returned value must be recognized as a compiled application module, e.g. `glas:app:Application`. This is then run with remaining arguments `["Args", "To", "Next", "Stage"]`. The `"--"` separator is optional; if omitted, it is inserted implicitly as the final argument.

Between staged applications and the syntactic sugar for user-defined operations, users can effectively extend the glas command line language just by defining modules. The main alternative is *Inline Scripting* (see below) but it is relatively awkward to work with large argument strings in most command shells.

## Scripting

Proposed operations:

        glas --script.FileExt (ScriptFile) (Args)

FileExt may be anything we can use as a file extension, including composite extensions. Intended usage context is a shebang script file within Linux:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips first line if it starts with shebang (#!), compiles remaining content based on specified file extension, then runs the result as an application with the remaining arguments.

*Aside:* If users want to configure for the specific script, they can leverage the `env` command in Linux to tweak environment variables or split multiple command line options.

### Inline Scripting

An obvious variation on the above is to embed the script text into the command line argument. Proposed operation:

        glas --cmd.FileExt ScriptText Args

This allows familiar file-based languages to be used without the file. Working with a large or multi-line text arguments is awkward in most command shells, so I favor staged applications in this role. However, inline scripting might prove more convenient when the glas command is embedded within a shell script.

## Other Operations

Proposed operations:

* `--version` - print version to console
* `--help` - print basic usage to console 
* `--check ModuleRef` - compile module pass/fail, do not run app
* `--init` - create initial configuration file, might prompt user

I hope to keep the glas executable small, pushing most logic into the module system. But if there is a strong argument for introducing new features as built-ins, we can do so. Built-in features can potentially be accessed through `sys.refl.*` APIs.

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



