# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef -- Args

This is intended for use with a lightweight syntactic sugar.

        glas opname a b c 
            # rewrites to
        glas --run glas-cli-opname -- a b c

This feature combines nicely with *staged* applications. A staged application can browse the module system and select or construct another application based on given arguments. Effectively, user-defined operations become command line languages, consistent with user-defined file extensions in modules. 

To better support command line languages, runtime configuration parameters (such as memory quotas or GC tuning) are not directly provided through command line arguments. Instead, such parameters may be provided via configuration, environment variables, or annotations. Indirectly, a staged application can arrange annotations on the returned application based on arguments.

Aside from '--run' the glas command line interface may define other built-in operations. But the intention is that most logic should be represented in the glas module system and not the command line executable.

## ModuleRef

A ModuleRef is a string that uniquely identifies a module. Initially, this may be a global module or a filename.  

        global-module
        ./FilePath

A FilePath is recognized by containing a directory separator (such as '/'). The glas command line interface will attempt to interpret any file or folder as a module, if requested. Otherwise, we'll search the configured distribution for a global module of the given name. Eventually, we might extend these options with URIs, but I think there isn't a strong use case.

## Configuration

All relevant configuration information is centralized to a profile, a ".prof" file. The `GLAS_PROFILE` environment variable can select the active configuration. If unspecified, GLAS_PROFILE will be set to an OS-specific default such as `"~/.config/glas/default.prof"` in Linux or `"%AppData%\glas\default.prof"` in Windows.

Minimally, the active distribution must be configured. I propose to initially use the [text tree syntax](TextTree.md) to express a filesystem search path where each directory contains global modules as subfolders. Any local paths are relative to the profile. Later we might allow logically renaming modules, reference to DVCS repositories, and separate ".dist" files.

        dist 
            dir Directory1
            dir Directory2
            ...

Aside from distribution, the profile will eventually support sections for logging, storage, app config, proxy compilers, content delivery networks, etc.. Ultimately, the profile is relatively ad-hoc: specific to the glas executable, subject to experimentation, deprecation, de-facto standardization. Users should receive a warning if a profile includes unrecognized or deprecated entries. 

### Distribution Files? Defer.

In context of glas, a distribution represents a set of named global modules that are maintained and versioned together. It is convenient to express large distributions in terms of inheriting from community or company distributions, adding popular patches and local overrides or new definitions. To support this pattern, distributions should support multiple inheritance and reference to DVCS repositories.

Full support for distributions will be deferred at least until after glas CLI bootstrap is completed.

## Running Applications

        glas --run ModuleRef -- Args

The glas executable first compiles the referenced module into a value. This value must be recognized as representing an application. Initially, the glas command line will only recognize the the type used in ".g" modules
Recognized run modes include:

* *step* - transaction loop step function. See [glas apps](GlasApps.md).
* *stage* - application has type `Args -> Application`.
  * effects to build app are *log* and *load* (global) 
  * arguments may be divided across stages using '--'.
  * `glas --run staged-op -- stage one args -- stage two args`
* *test* - application has a simple pass/fail `unit->unit` type
  * effects are limited to *log*, *load*, and *fork(Nat)*
  * fork is non-deterministic choice for fuzz testing
  * might extend to allow `any->any` type like assertions
* *text* - application function from `Args -> Binary`. 
  * binary is written to standard output once computed
  * limited access to *log* and *load* (global) effects
  * motive is to simplify bootstrap of glas executable 

## Scripting

Proposed operations:

        glas --script(.Ext)* (ScriptFile) (Args)
        glas --script(.Ext)*=(ScriptText) (Args)

Usage context:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips first line if it starts with shebang (#!), compiles remaining content based on specified extensions, then runs it as an application. Compilation ignores file extensions and location of the script file. Only global modules can be referenced. A variation with '--script.g=Text' can run a script without a separate script file.

In some cases, we might bind GLAS_PROFILE to the script instead of to the user. This can be supported indirectly via Linux 'env' command. Example:

        #!/usr/bin/env -S GLAS_PROFILE=/etc/glas.prof glas --script.g
        program goes here

In this case, the script should require special group permissions to execute, aligned with dependencies of the profile.

## Other Operations

Proposed operations:

* `--version` - print version to console
* `--help` - print basic usage to console 
* `--check ModuleRef` - compile module pass/fail, do not run
* `--list-modules` - report global modules in configured distribution

We should avoid adding too much logic to the command line interface, thus features such as a REPL or debug support would mostly be shifted to user-defined operations. But we can support a few built-ins. Ideally, the same operations can be supported via user-defined operations. For example, enable '`glas version`' to obtain information about runtime version as an effect.

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

Using log messages for detailed warnings or errors.

## Bootstrap

The bootstrap implementation of 'glas' might support only the *binary* run-mode for applications. This reduces need to implement a complete effects system up front. Assuming we can print a binary to standard output, the following can potentially work:

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

The target architecture could be provided as an argument or by defining a 'target' module.

*Note:* It is feasible to support early bootstrap via intermediate ".c" file or similar, to leverage a mature optimizing compiler. But I hope to eventually express all optimizations within the glas module system!

## Early Applications

The executable should have minimum logic, but where should I focus initial attention for user-defined apps?

* Bootstrap
* Browsing module system.
* Automated Testing
* Command line calculator
* REPLs
* [Language Server Protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol)
* FUSE filesystem support
* IDE projectional editors, programmable wikis
