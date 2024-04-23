# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef -- Args To App

This is intended for use with a lightweight syntactic sugar.

        glas opname a b c 
            # rewrites to
        glas --run glas-cli-opname -- a b c

When this syntactic sugar is combined with *Staged Applications* (see below), we can effectively support user-defined command line languages.

To keep the command line clean, runtime configuration parameters (e.g. memory and GC tuning) are represented in a configuration file instead of on the command line. There are simple conventions to support options specific to an application or class of applications.

## ModuleRef

A ModuleRef is a string that uniquely identifies a module. Initially, this may be a global module or a file path.  

        global-module
        ./FilePath

A FilePath is heuristically recognized by containing a directory separator (such as '/'). The glas command line interface will attempt to interpret any file or folder as a module, as requested. Otherwise, we'll search for a global module of the given name. 

## Configuration

Most configuration information will be centralized to a ".prof" file. This file may be selected by the `GLAS_PROFILE` environment variable, but has an OS-specific default such as `"~/.config/glas/default.prof"` in Linux or `"%AppData%\glas\default.prof"` in Windows. I propose a lightweight [text tree syntax](TextTree.md) to express this profile. 

A minimal configuration must specify a distribution. Initially, we'll support a simple filesystem search path, with local paths relative to the profile. Every global module must be represented as a subfolder within this path. In case of ambiguity, the first path 'wins'.

        dist 
            dir Directory1
            dir Directory2
            ...

I intend to eventually support reference to remote repositories, multiple inheritance with separate ".dist" files, and [namespace operations](GlasProgNamespaces.md).

The profile will eventually support sections for logging, storage, app config, proxy compilers, content delivery networks, variables, etc.. Ultimately, the profile is relatively ad-hoc, specific to the glas executable and subject to deprecation and de-facto standardization. Users should receive a warning when a profile includes unrecognized or deprecated entries.

### Application Specific Configuration

Runtime features such as GC tuning, logging, and persistence may vary between apps. 

To support this, the profile will define labeled subconfigurations. An application can define 'config.class' to return a simple priority list such as `["myapp.mydomain.com", "server"]`. The runtime would prioritize the subconfiguration labeled 'myapp.mydomain.com' if it is defined, falling back to 'server' and then (implicitly) 'default'. 

Selecting a configuration class is adequate for most use cases, but in some cases it's more convenient for the application to provide some details. To support this, we might eventually introduce 'config.gc', 'config.log', and other ad-hoc configuration attributes. These options may be developed as needed, subject to de-facto standardization. But everything should first be configurable via profile.

### Distribution Files? Defer.

In context of glas, a distribution represents a set of named global modules that are maintained and versioned together. It is convenient to express large distributions in terms of inheriting from community or company distributions, adding popular patches and local overrides or new definitions. To support this pattern, distributions should support multiple inheritance and reference to ".dist" files in remote DVCS repositories.

However, this is complicated and I'm not in a hurry. Full support for distributions can be deferred until after glas CLI bootstrap is completed.

### Reload Config

A runtime could follow OS conventions and automatically reload configuration and sources based on OS signal, such as SIGHUP. Additionally, this might be available through runtime reflection, including the reflective HTTP interface.

## Running Applications

        glas --run ModuleRef -- Args

The glas executable first compiles the referenced module into a value. This value must be recognized as representing an application. Initially, we'll only recognize ".g" modules that define a namespace of methods, or modules in user-defined languages that compile to the same representation. This namespace must implement interfaces recognized by the runtime for integration; see [glas application](GlasApps.md).

In addition to conventional apps, some special run modes may be recognized and run differently, perhaps based on whether a 'run-mode-staged' or 'run-mode-bsp' method is defined.

### Staged Applications

Staged applications, indicated by 'run-mode-staged', support user-defined command line languages. 

        glas --run StagedApp -- Description Of App -- Args To Returned App

Staged applications essentially have the same interface as language modules, except input is the tokenized `["Description", "Of", "App"]` instead of a file binary. The compiled value must also have a type recognized as an application by 'glas --run'. The returned application is run with the list of arguments following an optional '--' separator. 

*Note:* In this case, 'load' might also be able to access configuration parameters from the runtime configuration, perhaps via `load(config:Var)`.

### Binary Stream Processing Applications

A binary stream processing application (BSP app), indicated by 'run-mode-bsp', will incrementally read from standard input and write to standard output, plus access to language module effects (log and load). The advantage of BSP is that it's trivially implemented, immediately useful, suitable for early development and bootstrap. 

We can leverage it to extract binaries from the module system or to implement REPLs. If we're feeling clever, we can try [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or a [terminal graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/).

## Scripting

Proposed operations:

        glas --script.FileExt (ScriptFile) (Args)

FileExt may be anything we can use as a file extension, including multiple extensions. Intended usage context is a shebang script file within Linux:

        #!/usr/bin/glas --script.g
        program goes here

This operation loads the script file, skips first line if it starts with shebang (#!), compiles remaining content based on specified file extension, then runs the result as an application with the remaining arguments.

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

We should avoid adding too much logic to the command line interface, thus features such as a REPL or debug support would mostly be shifted to user-defined operations. But we can support a few built-ins. Ideally, the same operations can be supported via user-defined operations. For example, enable '`glas version`' to obtain information about runtime version as an effect.

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

Using log messages for detailed warnings or errors.

## Bootstrap

The bootstrap implementation of 'glas' might support only run-mode-bsp. This reduces the need to reimplement a full runtime in multiple languages. We can bootstrap by writing the executable and redirecting to a file.

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

### RPC Registry Configuration

The simplest registry might be configured as a remote service (URL and access tokens), shared database, or distributed hashtable. We also need composite registries, and support for filtering and editing 'tags' for both publish and subscribe.

### Database Configuration

At least one database should be configured to support persistent data. We might initially use LMDB or RocksDB, which would require configuring a filesystem location. 

Eventually, we might also want to support distributed databases. And it might be useful to compose databases flexibly, e.g. based on logical mounting or overlay of key-value databases. However, I think such features very low priority until the distant future. 

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



