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

I intend to eventually support reference to remote repositories, multiple inheritance with separate ".dist" files, and [namespace operations](ExtensibleNamespaces.md).

The profile will eventually support sections for logging, storage, app config, proxy compilers, content delivery networks, variables, etc.. Ultimately, the profile is relatively ad-hoc, specific to the glas executable and subject to deprecation and de-facto standardization. Users should receive a warning when a profile includes unrecognized or deprecated entries.

### Application Specific Configuration

Some runtime options such as GC tuning or logging might vary across apps. To support this, I propose to define subconfigurations within the profile. These are labeled arbitrarily, e.g. 'server', 'game', 'myapp.mydomain.com'. Applications may define a 'runtime-config' method which specifies a sequence of subconfigurations to apply. A default configuration is applied first.

This allows apps to tune runtime configurations based on their role or a unique application name, without knowing too much about the details. Though, it is feasible to extend 'runtime-config' to also support details.

### Distribution Files? Defer.

In context of glas, a distribution represents a set of named global modules that are maintained and versioned together. It is convenient to express large distributions in terms of inheriting from community or company distributions, adding popular patches and local overrides or new definitions. To support this pattern, distributions should support multiple inheritance and reference to DVCS repositories.

Full support for distributions will be deferred at least until after glas CLI bootstrap is completed.

## Running Applications

        glas --run ModuleRef -- Args

The glas executable first compiles the referenced module into a value. This value must be recognized as representing an application. Initially, we'll recognize ".g" modules that define an 'app' namespace, or generally any module that compiles to `g:(app:Namespace, ...)`. A [namespace](ExtensibleNamespaces.md) is more sophisticated than a simple dictionary, allowing overrides, renames, and late binding.

The application namespace provides interfaces that will be called by the runtime. For example, background tasks are represented by a runtime repeatedly calling a transactional 'step' method. Other methods may be called to support RPC, HTTP, GUI, publish-subscribe, or OS events. Conversely, a runtime may bind or override names representing the application's access to the environment. See [Glas Applications](GlasApps.md) for details.

The glas executable may support specialized application types such as staged applications or binary stream processing, influencing interfaces and integration. This might be indicated by declaring an abstract method such as 'run-mode-staged' or 'run-mode-bsp'.

### Staged Applications

Staged applications, indicated by 'run-mode-staged', support user-defined command line languages. 

        glas --run StagedApp -- Description Of App -- Args To Returned App

Staged applications essentially have the same interface as language modules, except input is the tokenized `["Description", "Of", "App"]` instead of a file binary. The compiled value must also have a type recognized as an application by 'glas --run'. The returned application is run with the list of arguments following an optional '--' separator. 

### Binary Stream Processing Applications

A binary stream processing application (BSP app), indicated by 'run-mode-bsp', will incrementally read from standard input and write to standard output. The main advantage of binary stream processing is that it's easily implemented and immediately useful, especially suitable for early development and bootstrap.

I propose to express the BSP app as a transaction loop application with a restricted effects API - e.g. read, write, log, and load. To ensure deterministic behavior, 'read' will logically diverge (wait indefinitely) if buffered input data is insufficient. Writes are generally buffered until the current transactional step commits. With this model, it is trivial to adapt a BSP app to a normal glas app.

BSP apps can support limited interaction such as a REPLs, especially if embedded in a suitable environment (e.g. with [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or [terminal graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/)). Of course, we may need to introduce configuration options to control features such as input echo and line buffering.

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
* Web apps? (Compile to JavaScript or WebAssembly + DOM)
* Notebook apps? (something oriented around reactive, live coding with graphics?)
