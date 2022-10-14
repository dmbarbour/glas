# Glas Command Line Interface

The glas command line must support only a few built-in operations:

        glas --run ValueRef -- Args
        glas --extract ValueRef
        glas --version
        glas --help

User-defined operations are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

The vision for glas systems is that most operations will be user-defined. Logic for features such as REPLs or pretty-printing should be represented in the module system instead of adding many built-in operations.

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

The '--run' and '--extract' commands reference values in the Glas module system. 

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

A global module is a subfolder found based on configuration of the glas command line, e.g. via GLAS_PATH environment variable. A local module is a file or subfolder in the current working directory. The component path allows limited selection of fields from record values.

These value references are highly constrained, e.g. there is no option to slice a list. However, this concern is greatly mitigated by *Application Macros*.

## Environment Variables

Proposed environment variables:

* **GLAS_DATA** - a folder for content-addressed storage, persistent memoization cache, the shared database, or extended configurations. If unspecified, defaults to a user-specific folder such as `~/.glas`.
* **GLAS_PATH** - search path for global modules. Follows OS conventions for PATH environment variables. Global modules must be subfolders found on this path.

Configuration within GLAS_DATA may extend, override, and deprecate environment variables. But this is still useful as a list of features we might want to configure.

## Extracting Binaries

The glas command line can directly extract binary data to stdout.

        glas --extract ValueRef

The reference must evaluate to a binary value, a list of bytes. This binary is written to stdout, then the command halts. Binary extraction is primarily intended to reduce requirements for bootstrap: compile then extract of an executable binary without implementing a complete effects API. However, this feature may prove broadly useful in treating glas as a build system.

## Running Applications

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. See [glas applications](GlasApps.md).

        glas --run ValueRef -- List Of Args

The glas command line will support a few different application types, distinguished by header such as 'prog' or 'macro'. For performance, the glas command line may JIT compile and cache the application value rather than directly interpreting it. 

Arguments after the ValueRef are passed to the application as a list of strings. Other than command line arguments, some behavior can be configured via environment variables.

### Application Processes

Process applications are distinguished by the 'prog' header, i.e. `prog:(do:Program, ... Annotations)`. The program should represent a transactional step function. The runtime will repeatedly evaluate this function so long as it returns 'step' or fails. Failure implicitly waits for external changes.

        type Process = (init:Args | step:State) -- [Eff] (step:State | halt:Result) | Fail

In context of the console application, this process starts with `init:["List", "Of", "Args"]` and ends with `halt:ExitCode`. The ExitCode should be a short bitstring, representing an integer. IO is based around the full *Effects API*.

*Aside:* At the application layer, we could recognize a few annotations to support tab completion and related tooling.

### Application Macros

Application macros are distinguished by the 'macro' header.

        macro:Program

To simplify caching, arguments are optionally staged via '--':

        glas --run MacroRef -- Static Args -- Dynamic Args

The program must be 1--1 arity and will receive `["Static", "Args"]` on the data stack. The returned value must represent an application, which then is run receiving dynamic arguments. Returning another application macro is permitted but usually unnecessary.

The program has access to language module effects, i.e. 'log' and 'load'. Application macros might usefully be viewed as language extensions for the command line interface. For best aesthetics, combine with user-defined operations.

### Process Networks (Defer)

For robust transaction machine optimizations (incremental computing, replication on fork, transaction fusion, distribution of cliques, etc.) it is useful to develop an expanded model that makes these opportunities obvious and controls effects as needed. I propose to develop this later under an different header (perhaps 'app' or 'net').

## Effects API

Applications will support most effects described for [glas applications](GlasApps.md), plus effects for convenient access to the glas module system, live coding, and interactive development that might be difficult in separately compiled applications. Proposed extensions:

* **load:ModuleRef** - load current value of a module. Modules may update due to source changes, but this can only be observed between transactions.
* **rt:reload** - (a runtime extension) rebuild and redeploy current application from the module system. Rebuild includes recomputing application macros. Fails if application cannot be rebuilt.
* **help:Value** - the Value here may represent an effect or part of one (e.g. namespace 'db' or operation 'db:put' or parameter 'db:put:k'). Response is a record such as `(text:"Description Here", related:[List, Of, Values])`.

Access to the module system should be stable with respect to incremental computing.

## Exit Codes

Keeping it simple.

         0  okay
        -1  not okay

Glas systems will rely on log messages more than exit codes to describe errors. No reason to think hard about this. 
