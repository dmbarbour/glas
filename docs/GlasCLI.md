# Glas Command Line Interface

The glas command line supports two primary operations:

        glas --extract ValueRef
        glas --run ValueRef -- Args

User-defined operations are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

Most operations used in practice should be user-defined. This ensures logic is visible and accessible in the module system. New versions of the glas command line executable may improve performance or update the effects API, but should not significantly affect behavior of applications that rely on stable APIs.

## Value References

The primary operations are parameterized by a reference to a value in the glas module system. This reference specifies a module (local to working directory or global) and may index into a dictionary via dotted path.

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

This syntax restricts references in several cases, e.g. a component path cannot include emoji characters, and we cannot slice or index a list. This can be mitigated by avoidance or resolved by a little indirection, e.g. application macros can support more flexible value references.

## Configuration

The glas executable will centralize configuration files, cached computations, content-addressed storage, and the shared key-value database into a single folder. This folder may be specified by the GLAS_HOME environment variable or will be implicitly assigned a value based on OS convention such as `~/.config/glas` in Linux or `%AppData%/glas` in Windows.

The primary configuration file is "sources.txt". Each entry in this file represents a location to search for global modules in priority order (value of first match 'wins'). Line comments are permitted starting with '#'. Currently, this is limited to 'dir' entries for local filesystem directories. Future extensions can support network resources or logical renaming of modules.

        # example sources.txt
        dir ./src
        dir /home/username/glas
        dir C:/Users/username/glas
        dir ../../glas

Secondary configuration will be expressed via local glas module named "conf". This module would compile to a record value that includes elements for logging, caching, sandboxing, and other configurable features recognized by the glas executable.

Finally, application-specific configuration should be expressed using annotations. Annotations can be compiled into apps or adjusted by application macros. Profiling, tuning memory allocation and GC, even effects API versioning can be supported via annotations.

## Extracting Binaries

The glas command line can directly extract binary data to standard out.

        glas --extract ValueRef

The reference must evaluate to a binary value (a list of bytes). This binary is written to stdout, then the command halts. The motive for this feature is to support bootstrap without implementing or maintaining a runtime effects API.

## Running Applications

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. See [glas applications](GlasApps.md).

        glas --run ValueRef -- Args To App

Interpretation depends on the value header, currently recognizing 'prog', 'proc', or 'macro' (see below). This may be extended over time. To improve performance, the glas command line may privately compile and cache a representation of application behavior. Args following '--' are passed to the application. The '--' separator may be omitted if would be final argument.

*Note:* Additional arguments prior to '--' would interfere with aesthetics and abstraction. Annotations are favored instead. Annotations can be adjusted by application macros.

### Basic Applications

A basic application process uses the 'prog' header.

        prog:(do:Program, ... Annotations ...) 

This program must have 1--1 arity and be typed as a transactional process step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

In context, this process starts with `init:["List", "Of", "Args"]` and finishes with `halt:ExitCode`. The ExitCode should be a bitstring representing a small integer. The process may commit one or more intermediate 'steps' before halting, carrying application private state to the next step. Input and output is expressed effectfully and applied transactionally between steps.

### Process Networks

Applications expressed via 'prog' header are difficult to statically optimize for incremental computing, concurrency, and distribution. So I'm currently developing another program model with the 'proc' header that is easier to optimize.

        proc:(do:Process, ... Annotations ...)

Design of 'proc' is ongoing within the [glas applications](GlasApps.md) document. The core idea is to expose and manage control flow and communication to support robust optimizations. Every proc is equivalent to a 'prog' app step function, so use of 'proc' should primarily impact performance and scalability.

### Application Macros

Application macros are distinguished by the 'macro' header.

        macro:Program

To simplify caching, arguments are implicitly staged via '--':

        glas --run MacroRef -- Static Args -- Dynamic Args

The macro program must be a 1--1 arity function and here would receive list of strings `["Static", "Args"]` on the data stack. The returned value must represent another application (potentially another application macro), which then is run receiving `["Dynamic", "Args"]`. The '--' separator may be omitted if would be final argument.

The macro program has access to the same effects API as language modules, i.e. 'log' and 'load'. Application macros are usefully viewed as user-defined languages for the command line interface.  and combine nicely with the syntactic sugar for user-defined operations.

## Extended Effects API

In context of glas command line, I propose a few specialized extensions to the effects API:

* **load:ModuleRef** - load current value of a module. Value of module may update between transactions. 
* **reload** - rebuild and redeploy application from source while preserving application state. Fails if application cannot be rebuilt or if redeployment is infeasible. Otherwise returns unit and applies after commit.
* **help:Effect** - access to integrated documentation. The Effect here may represent an effect or part of one (e.g. namespace 'db' or operation 'db:put' or parameter 'db:put:k'). Response is an ad-hoc record such as `(text:"Description Here", class:op, related:[List, Of, Values])`, or failure.

These extend the effects API proposed in [glas apps](GlasApps.md), and would qualify as stable effects.

## Bootstrap

The bootstrap implementation for glas command line only needs to support the '--extract' command. Assuming suitable module definitions, bootstrap could be expressed with just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, we need different binaries for different operating systems and machine architectures. This can be conveniently supported by defining a global 'target' module that describes our compilation target, then switch targets via configuring global module search. Alternatively, we could define target-specific modules such as 'glas-binary-linux-x64'.

## Exit Codes

Keeping it simple. 

         0  okay
        -1  fail

The glas command line interface will favor log messages to report warnings or errors. Runnable applications may halt with a small integer exit code. But even for apps I would favor log messages over informative exit codes.

## Secondary Operations

I'd prefer to avoid built-ins with sophisticated logic. But a few lightweight utilities to support early development or OS integration are acceptable.

* `--check ValueRef` - compile module and test that value is defined.
* `--print ValueRef` - build then pretty-print a value for debugging.
 * uses same printer as for log messages (same configuration, too)
* `--version` - print executable version information
* `--help` - print information about options

Ideally, we should move ASAP from built-ins to user-defined operations such as 'glas-cli-print'. 

## Thoughts

### Debug Mode

It is feasible for the glas executable to support debugging of an app, e.g. via annotation to build a debug view. However, it is also feasible to build this debug view manually via metaprogramming, e.g. add a web service just for debugging. I favor the latter option, and will explore it first.

### Profiling

Profiling will need some more consideration than I've given it so far. Some runtime support is needed to track the failed transactions efficiently. Annotations would guide profiling, e.g. enable or disable it for a subprogram, and give names to subprograms for profiling purposes.

### Application Macro access to Environment Variables

I could extend application macros with access to environment variables. However, I'm uncertain that I want to encourage use of the environment for interpreting the command line 'language'. Additionally, most use of the OS layer env is hindered without also having access to read files and other features. 

For now, decided to treat application macros a lightweight extension to language modules for command line arguments. This ensures that anything we express via command line can also be easily abstracted within a new module and shared with other users, which is a convenient property.