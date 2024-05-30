# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef -- Args To App

Other runtime options may be provided prior to the '--' separator. However, I hope to minimize the number of runtime options needed on the command line, and instead support user-defined commands via lightweight syntactic sugar.

        glas opname a b c 
            # rewrites to
        glas --run glas-cli-opname -- a b c

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

        glas --run ModuleRef -- Args

The glas executable first compiles the referenced module into a value. This value must be recognized as representing an application. 

Initially, we'll only recognize ".glas" and compatible modules, which evaluate to something like `glas:(Dict of (Namespace of AbstractAssembly))`. In this case, the dictionary should contain 'app', and its namespace should represent an abstract [application object](GlasApps.md). The application namespace leaves some methods undefined, such as system methods and [abstract assembly constructors](AbstractAssembly.md) and (`sys.*` and `%*`), to be provided by the runtime.

In addition to conventional apps, special run modes may be recognized and run differently. For example, an application that defines `run-mode-staged` might be interpreted as a staged application. The default run mode could be `run-mode-loop`.

### Staged Applications

Staged applications, indicated by 'run-mode-staged', support user-defined command line languages. 

        glas --run StagedApp -- Description Of App -- Args To Returned App

In this case, the application has the same interface as language modules, i.e. `compile : SourceCode -> ModuleValue` with limited effects (just `sys.load(ModuleRef)`), except our SourceCode is now `["Description", "Of", "App"]`. The returned ModuleValue is then interpreted as another application, receiving the remaining arguments.

## Scripting

Proposed operations:

        glas --script.FileExt (ScriptFile) (Args)

FileExt may be anything we can use as a file extension, including multiple extensions. Intended usage context is a shebang script file within Linux:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips first line if it starts with shebang (#!), compiles remaining content based on specified file extension, then runs the result as an application with the remaining arguments.

*Aside:* If users want to configure for the specific script, they can leverage the `env` command in Linux to tweak environment variables or split multiple command line options.

### Inline Scripting

An obvious variation on the above is to embed the script text into the command line argument. Proposed operation:

        glas --cmd.FileExt ScriptText -- Args

This can be convenient in some cases. It enables users to favor familiar file-based languages without requiring a separate file. That said, most file-based languages are probably an awkward fit for inline scripting, and users might ultimately be better off developing and learning a suitable command-line language (via staged app).

## Other Operations

Proposed operations:

* `--version` - print version to console
* `--help` - print basic usage to console 
* `--check ModuleRef` - compile module pass/fail, do not run
* `--list-modules` - report global modules in configured distribution
* `--init` - create initial configuration file, might prompt user

We should avoid adding too much logic to the command line interface, thus features such as a REPL or debug support would mostly be shifted to user-defined operations. But we can support a few built-ins. Ideally, the same operations can be supported via user-defined operations. For example, enable '`glas version`' to obtain information about runtime version as an effect.

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

Using log messages for detailed warnings or errors.

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



