# Glas Command Line Interface

The glas command line interface supports one primary operation:

        glas --run ModuleRef -- Args

This is intended for use with a lightweight syntactic sugar.

        glas opname a b c 
            # rewrites to
        glas --run glas-cli-opname -- a b c

This feature combines nicely with *staged* applications. A staged application can browse the module system and select or construct another application based on given arguments. Effectively, user-defined operations become command line languages, consistent with user-defined file extensions in modules. 

To better support command line languages, runtime configuration parameters (such as memory quotas or GC tuning) are not directly provided through command line arguments. Instead, such parameters may be provided via configuration files in GLAS_HOME, environment variables, or annotations. Indirectly, a staged application can arrange annotations on the returned application based on arguments.

Aside from '--run' the glas command line interface may define other built-in operations. For example, '--help' and '--version' are Linux standard, and we might provide a method to list global modules, simplify shebang scripting, or support lightweight debugging. But, in principle, anything users can do with built-in arguments should be achievable with a user-defined op. This principle might influence support for runtime reflection in the effects API.

## ModuleRef

A ModuleRef is a string that uniquely identifies a module. Initially, this may be a global module or a filename.  

        global-module
        ./FilePath

A FilePath is recognized by containing at least one directory separator (such as '/'). The glas command line interface will attempt upon request to interpret any file or folder as a module. Otherwise, we'll search the active distribution for a global module of the given name. Eventually, we might extend these two options with URIs.

## Running Applications

        glas --run ModuleRef -- Args

Initially, the glas executable will only recognize grammar-logic modules, or modules that compile to a compatible value. This might have the form `g:(def:(app:gram:(...), MoreDefs), MetaData)`. A grammar-logic module potentially defines multiple independent grammars, each of which may define multiple interdependent methods. The grammars are represented in an intermediate language, thus further processing is necessary. Broadly, the glas executable will extract the 'app' grammar, JIT compile the 'main' method, then run it as an application. Ideally, many steps are memo-cached such that we aren't rebuilding the module and its transitive dependencies every time. 

The glas runtime will support multiple application types. This is indicated via 'run-mode' annotation on 'main'. 

Some feasible run modes:

* *loop* - the default, a [transaction loop](GlasApps.md) step function
* *staged* - application has type `Args -> Application`.
  * arguments may be divided across stages using '--'.
  * `glas --run staged-op -- stage one args -- stage two args`
  * otherwise, all args go to the first stage
* *binary* - application is a function from `Args -> Binary`. 
  * binary is written to standard output once computed
  * access to *log* and *load* (global) effects
  * primary motive is to simplify bootstrap
* *test* - application has type `() -> ()`, we're interested in pass/fail
  * effects are *log*, *load* (global), and *fork(Nat)*
  * fork is non-deterministic choice for parallel or fuzz testing

A more conventional application model is feasible if we control backtracking or extend glas executable to recognize non-grammar program types. But, at least initially, to develop a conventional application involves producing an independently executable binary.

## Configuration

I propose two environment variables to get started:

* GLAS_HOME - a folder path for ad-hoc configuration and default location storage. Default is based on OS, e.g. `"~/.config/glas"` for Linux or `"%AppData%\glas"` for Windows.
* GLAS_PROF - selects the active configuration *profile*. This enables users to efficiently change configuration for different roles or tasks. Default, is `"default"` referring to `"${GLAS_HOME}/default.prof"`. May be file path for profiles outside GLAS_HOME.

### Profiles

A profile may describe ad-hoc configuration features, such as:

* active distribution via ".dist" file
* storage locations for data and cache
* configuration of logging options
* binding external services, e.g. HTTP
* enable debug mode features in runtime
* ad-hoc performance tuning
* trusted proxy compilers
* content delivery networks

Of these, support for distributions is urgent. Everything else can be deferred.

        # default.prof
        dist default.dist

Other features can be developed later. Also, we might eventually support inheritance and composition of profiles, much as we do for distributions. But this is relatively low priority.

### Distributions

In context of glas, a distribution represents a set of global modules that are maintained and versioned together. Company or community distributions will often refer to a network repository instead of local filesystem. It is often convenient to express a user's distribution in terms of inheritance from a community distribution, perhaps adding a few clusters of modules from other sources, perhaps assigning defaults. Thus, distributions support multiple inheritance.

Multiple inheritance can result in accidental conflicts. To mitigate this, distributions clearly indicate whether they are expecting to introduce or override each module. This allows suitable warnings or errors (which could be disable in cases where you don't care), and also tweaks some useful behavior such as automatically moving 'foo' to a private 'prior-foo' upon override. Conflicts can be resolved via rename or move.

Private global modules are supported. This only affects inheritance, i.e. all private modules are hidden to the inheritance and cannot be extended further, but the glas command line interface still treats it as a normal global module. 

To support discovery, I propose a description of modules be directly included in distributions. This could include some short text and additional attributes to simplify filtering.

... TODO ...


The most important configuration file is perhaps "sources.tt". This file describes where to search for global modules. This file uses the [text-tree](../glas-src/language-tt/README.md) format - a lightweight alternative to XML or JSON. This file is currently limited to 'dir' entries indicating local filesystem, and entries that start with '#' for comments.

        # example sources.tt
        dir ./src
        dir /home/username/glas
        dir C:\Projects\glas\glas-src

Eventually, I intend to support network repositories as the primary source for global modules. However, short term a search path of the local filesystem will suffice. Relative paths are relative to the folder containing the configuration file. Order is relevant - locations are processed sequentially.

## Bootstrap

The bootstrap implementation for glas command line executable might be based around the 'stream' run-mode. 

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas
    # maybe also check size, performance, etc.

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, different binaries are needed for different operating systems and architectures. This can be resolved by introducing a 'target' module that specifies the intended architecture for executable outputs, or by explicitly naming each binary (e.g. 'glas-binary.linux-x64'). 

*Note:* It is feasible to support early bootstrap with an intermediate ".c" output or similar, to benefit from a mature optimizing compiler. But I hope to eventually express all optimizations within the glas module system!

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

Using log messages for detailed warnings or errors.


## Secondary Operations

Guiding principles for introducing more built-in operations: 

* Can implement as user-defined operation. 
* Doesn't add too much logic to executable.
* Sufficient utility to justify built-in.  

All three points should hold simultaneously. However, we can potentially extend the glas executable to make the first point true. For example, to support '--version' as a user-defined operation, we might add a method to the effects API accessible to applications. The question then becomes whether we're adding too much logic.

Proposed Operations:

* `--version` - print version to console
* `--help` - print basic usage to console 
* `--check ModuleRef` - compile module pass/fail, do not run
* `--list-modules` - report modules in current distribution

### Scripting

Proposed built-in operation:

        glas --script(.ext)* (ScriptFile) (Args)

Usage context:

        #!/usr/bin/glas --script.g.m4
        program goes here

This operation would load the script file, remove the shebang line, compile the script body based on the specified extensions, then run the resulting application. This is much more suitable than '--run' because the '--' separator doesn't fit and file extensions are frequently elided. 

As a user-defined op, we might need to use `#!/usr/bin/env -S glas script .g.m4` which is usable but relatively awkward. Additionally, we cannot read the script file within a staged app, so we might need accelerated evaluation instead.

## Thoughts

### System Health

Distributions support automatic testing in the form of "test-" modules, both local and global. Tests can potentially perform automatic type checking, unit tests, and integration tests. But there are some limits, such as they cannot perform linting. Distributions can potentially leverage more conventional tooling at the repository level to support linting and other features. But this is beyond the scope of glas command line interface.

