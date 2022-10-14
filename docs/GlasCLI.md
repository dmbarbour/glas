# Glas Command Line Interface

The glas command line must support only a few built-in operations:

        glas --run ValueRef -- Args
        glas --extract ValueRef
        glas --version
        glas --help

Other operations may be introduced with the '--' prefix, but it's a better fit for my vision of Glas systems if most operations are user-defined. User-defined operations are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

User-defined operations can feasibly support pretty-printers, REPLs, linters, language-server support for IDE integration, and much more. But it is sufficient to use '--extract' to support basic compilation via the glas modle system.

## Bootstrap

The bootstrap implementation for glas command line only needs to support the '--extract' command. Assuming suitable module definitions, bootstrap can be expressed with just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, we need different binaries for different operating systems and machine architectures. This can be conveniently supported by defining a global 'target' module that describes our compilation target. (Alternatively, we could define specific modules such as 'glas-binary-linux-x64'.)

## Value References

The '--run' and '--extract' commands must reference values in the Glas module system. However, it isn't necessary to reference arbitrary values, just enough to support user-defined commands and early development.

        ValueRef = ModuleRef ComponentPath
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]
 
A value reference starts by identifying a specific module, local or global. Global modules are folders found by searching GLAS_PATH environment variable, while local modules identify files or subfolders within the current working directory. The specified module is compiled to a Glas value.

A subcomponent of the module's value may then be indicated by dotted path. This path is limited to simple null-terminated text labels. There is no attempt to generalize, at least not for the built-in commands. 

User-defined commands can feasibly extended the ValueRef mini-language. However, I think it's usually better to put any desired logic into the module system where it's accessible, rather than embedded in external scripts.

## Extracting Binaries

The glas command line can directly extract binary data to stdout.

        glas --extract ValueRef

The reference must evaluate to a binary value, a list of bytes. This binary is written to stdout, then the command halts. Binary extraction is primarily intended to reduce requirements for bootstrap: compile then extract of an executable binary without implementing a complete effects API. However, this feature may prove broadly useful in treating glas as a build system.

## Running Applications

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. See [glas applications](GlasApps.md).

        glas --run ValueRef -- List Of Args

The glas command line will support a few different application types, distinguished by header such as 'prog' or 'macro'. For performance, the glas command line may JIT compile and cache the application value rather than directly interpreting it. 

Arguments after '--' are passed to the application as a list of strings. This may be elided if there are no arguments. Other than command line arguments, applications may also be configured via a few environment variables.

### Basic Applications

Basic applications are distinguished by the 'prog' header, i.e. `prog:(do:Program, ... Annotations)`. The program should represent a transactional step function, which may evaluate an arbitrary number of steps.

        type Process = (init:Args | step:State) -- [Eff] (step:State | halt:Result) | Fail

This process starts with `init:["List", "Of", "Args"]` and ends with `halt:ExitCode`. The ExitCode should be a short bitstring, representing an integer. Annotations are more ad-hoc but might serve a useful role such as hints for tab completion. See *Basic Effects API*.

### Macro Applications

Macro applications are distinguished by the 'macro' header.

        macro:Program

We'll further separate static and dynamic arguments:

        glas --run MacroRef -- Static Args -- Dynamic Args

The macro program must be 1--1 arity and receives the `["Static", "Args"]` as input. It must return another application value. The returned application is then interpreted receiving dynamic arguments. If all arguments are static, the separator may be elided, but use of the separator may simplify caching.

Macro applications have access to language module effects: 'log' and 'load'. Effectively, macro applications extend the glas command line with a little DSL.

### Concurrency and Distribution (Incomplete)

It is difficult to optimize a glas process to fully leverage transaction machine features - incremental computing, replication on fork, etc.. An effective option to work around this is to model the concurrency and components more explicitly and declaratively. I'd like to eventually support an 'app' header that makes concurrency and logical distribution more visible to a compile-time analysis.

## Basic Effects API

Basic applications will support most effects described for [glas applications](GlasApps.md), plus a few effects for convenient access to Glas modules:

* **load:ModuleRef** - load current value of a module. Modules may update due to source changes, but this can only be observed between transactions.
* **rt:reload** - (runtime extension) rebuild and redeploy current application from the module system, if possible. Fails if application cannot be rebuilt. On success, applies to future transactions.

These effects support live coding on a per-app basis, although they aren't a complete, independent solution. Computations on the module system must implicitly be incremental, using cached results to avoid unnecessary rework.

## Environment Variables

Proposed environment variables:

* **GLAS_DATA** - a folder for content-addressed storage, persistent memoization cache, the shared database, or extended configurations. If unspecified, defaults to a user-specific folder such as `~/.glas`.
* **GLAS_PATH** - search path for global modules. Follows OS conventions for PATH environment variables. Global modules must be subfolders found on this path.

Configuration within GLAS_DATA may extend, override, and deprecate environment variables. But this is still useful as a list of features we might want to configure.

## Exit Codes

Keeping it simple.

         0  okay
        -1  not okay

Glas systems will rely on log messages more than exit codes to describe errors. No reason to think hard about this. 
