# Glas Command Line Interface

The glas command line should support only a few built-in operations:

        glas --extract ValueRef
        glas --run ValueRef -- Args
        glas --version
        glas --help

User-defined operations are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

My vision for glas systems is that most operations will be user-defined. Logic is visible and accessible via the module system. Different versions of 'glas' mostly affect performance. However, more built-ins may be introduced to support bootstrap development or system configuration.

## Value References

The '--run' and '--extract' commands reference values in the Glas module system. 

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

A local module is identified within the current working directory. A global module is found based on prior configuration of glas command line, such as via the GLAS_PATH environment variable. The component path allows limited dictionary access.

Value references are limited, e.g. there is no option to index a list or use emoji in the component path. If more flexible value references are desired, use *Application Macros*.

## Environment Variables

Proposed environment variables:

* **GLAS_DATA** - a folder for content-addressed storage, cached computations, the shared database, and extended configurations. If unspecified, defaults to a user-specific folder such as `~/.glas`.
* **GLAS_PATH** - search path for global modules. Follows OS conventions for PATH environment variables. Global modules must be subfolders found on this path.

Configurations managed under GLAS_DATA may later extend, override, or deprecate environment variables.

## Extracting Binaries

The glas command line can directly extract binary data to standard out.

        glas --extract ValueRef

The reference must evaluate to a binary value (a list of bytes). The binary is written to stdout, then the command halts. The primary motive for this feature is to support bootstrap without implementing a complete effects API.

## Running Applications

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. See [glas applications](GlasApps.md).

        glas --run ValueRef -- Args To App

The application model is extensible: interpretation is based on value header such as 'prog' or 'macro' (see below). For performance, the glas command line may implicitly compile and cache application behavior, but that won't be directly exposed to the user. Args following '--' are passed to the application.

### Process

A basic application process uses the 'prog' header, and is represented by a glas program.

        prog:(do:Program, ...) 

This program represents an application process as a transactional step function.

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

In context of the console application, this process starts with `init:["List", "Of", "Args"]` and finishes with `halt:ExitCode`. The ExitCode should be a short bitstring representing a 32-bit integer. Between init and halt, the process may commit any number of intermediate steps, forwarding the State value.

### Macros

Application macros are distinguished by the 'macro' header.

        macro:Program

To simplify caching, arguments are implicitly staged via '--':

        glas --run MacroRef -- Static Args -- Dynamic Args

The program must be 1--1 arity and will receive `["Static", "Args"]` on the data stack. The returned value must represent an application, which then is run receiving `["Dynamic", "Args"]`. Returning another application macro is permitted.

The macro program has access to the same effects API as language modules, i.e. 'log' and 'load'. Application macros can usefully be viewed as user-defined language extensions for glas command line arguments. The expectation is that they'll usually be combined with user-defined operations.

### Process Networks

Process networks use the 'proc' header.

        proc:(do:Process, Annotations)

The process network also represents a transactional step function (it could be compiled into one) but in a manner that makes sequences, concurrency, partitioning, incremental computing, communication, etc. much more obvious to an optimizer. The glas command line can take advantage of this to improve performance.

Design of process networks is ongoing, within the [glas applications](GlasApps.md) document.

## Extended Effects API

In context of glas command line, I propose a few specialized extensions to the effects API:

* **load:ModuleRef** - load current value of a module. Value of module may update between transactions. 
* **reload** - rebuild and redeploy application from source while preserving application state. Fails if application cannot be rebuilt or if redeployment is infeasible. Otherwise returns unit and applies after commit.
* **help:Effect** - access to integrated documentation. The Effect here may represent an effect or part of one (e.g. namespace 'db' or operation 'db:put' or parameter 'db:put:k'). Response is an ad-hoc record such as `(text:"Description Here", class:op, related:[List, Of, Values])`, or failure.

## Bootstrap

The bootstrap implementation for glas command line only needs to support the '--extract' command. Assuming suitable module definitions, bootstrap can be expressed with just a few lines of bash:

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
         1  fail

The glas command line interface will favor log messages to report warnings or errors. Runnable applications may halt with an arbitrary integer exit code (up to 31 bits) but even for those I'd suggest generally sticking to error logs and only signaling failure with the exit code.
