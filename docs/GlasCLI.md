# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef Args To App

Other runtime options may be provided prior to the '--' separator. However, I hope to minimize the number of runtime options needed on the command line, and instead support user-defined commands via lightweight syntactic sugar.

        glas opname a b c 
            # rewrites to
        glas --run glas-cli-opname a b c

By combining this syntactic sugar with *Staged Applications* (see below), glas supports user defined command line languages.

## ModuleRef

A ModuleRef is a string that uniquely identifies a module. Initially, this may be a global module or a file path.  

        global-module
        ./FilePath

A FilePath is heuristically recognized by containing a directory separator (such as '/'). The glas command line interface will attempt to interpret any file or folder as a module, as requested. Otherwise, we'll search the runtime configuration for a global module of the given name. 

## Configuration

To avoid clutter, I hope to keep runtime configuration options off the command line. This limits configuration to environment variables, configuration files, and the application itself. 

To simplify switching of configurations, I propose for `GLAS_CONF` environment variable to specify a configuration file. If unspecified, the default configuration file is OS specific, e.g. `"~/.config/glas/default.conf"` on Linux or `"%AppData%\glas\default.cfg"` on Windows. This file uses the [glas configuration language](GlasConfigLang.md).

This configuration file should describe system-wide features such as global modules, a shared key-value database, the RPC registry. Additionally, it may describe defaults for instance specific features such as quotas, memory management, logging options, and so on. However, the latter may be subject to application tuning via `conf.*` or `sys.refl.conf.*` methods.

## Running Applications

        glas --run ModuleRef Args

The glas executable first compiles the referenced module into a structured value. Initially, we'll recognize `glas:(app:Application, ...)`, i.e. the compiled output of a ".glas" module that defines 'app'. The Application type is itself a structured value representing a [namespace](GlasNamespaces.md) of methods. At this point, the methods have been compiled to an intermediate language, a Lisp-like [abstract assembly](AbstractAssembly.md). AST constructor methods and system methods (`%*` and `sys.*` by convention) are left abstract to be implemented by the runtime.

The default interpretation is a [transaction loop](GlasApps.md). The runtime will evaluate 'start' once, then 'step' repeatedly until the application halts, each in a separate transaction. The runtime may also recognize interfaces such as 'http' or 'gui' and implicitly implement an HTTP service or GUI user agent.

Alternative interpretations may be indicated by defining 'flag' methods in the namespace. Initially, we'll recognize 'run-mode-loop' which indicates the default interpretation, and 'run-mode-staged' to support user-defined languages on the command line. Any unrecognized 'run-mode-\*' flag should raise an error.

### Staged Applications

Staged applications are indicated by defining 'run-mode-staged'. The definition is irrelevant. When recognized, the runtime will instead interpret the application as a language module. That is, it should define `compile : SourceCode -> ModuleValue` and the only available effect is to load modules (`sys.load`).

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



