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

### Application Processes

Process applications are distinguished by the 'prog' header.

        prog:(do:Program, ...) 

This program represents an application process as a transactional step function.

        type Process = (init:Args | step:State) -- [Eff] (step:State | halt:Result) | Fail

In context of the console application, this process starts with `init:["List", "Of", "Args"]`, and finishes with `halt:ExitCode`. The exit code should be a short bitstring (0 to 31 bits) representing a signed integer. The State is private to the app (modulo extensions or debugging). The process function is repeatedly evaluated until 'halt' is returned, committing successful steps and retrying failed steps.

The transactional nature has a significant influence on design of the effects API. See also *Effects API* and [Glas Apps](GlasApps.md). 

### Application Macros

Application macros are distinguished by the 'macro' header.

        macro:Program

To simplify caching, arguments are implicitly staged via '--':

        glas --run MacroRef -- Static Args -- Dynamic Args

The program must be 1--1 arity and will receive `["Static", "Args"]` on the data stack. The returned value must represent an application, which then is run receiving `["Dynamic", "Args"]`. Returning another application macro is permitted.

The macro program has access to the same effects API as language modules, i.e. 'log' and 'load'. Application macros can usefully be viewed as user-defined language extensions for glas command line arguments. The expectation is that they'll usually be combined with user-defined operations.

### Brainstorming

Potential future extensions to application model:

* *declarative process network* - make it easy to recognize and optimize for incremental computing, concurrency, and distribution.
* *virtual machine bytecode* - develop a representation that is closer to the machine, perhaps an intermediate representation.

Extensions are low priority for now, but are a viable path to enhance glas system flexibility, scalability, and performance.

## Effects API

Applications will support most effects described in [glas apps](GlasApps.md), plus a few effects to support live coding and integrate the glas module system. Proposed extensions:

* **load:ModuleRef** - load current value of a module. Value may update between transactions.
* **rt:reload** - (a runtime extension) rebuild and redeploy current application, i.e. the original ValueRef and any staged macros, while preserving application state. Fails if application cannot be rebuilt. Otherwise returns unit and applies to future transactions after commit.
* **help:Effect** - access to integrated documentation. The Effect here may represent an effect or part of one (e.g. namespace 'db' or operation 'db:put' or parameter 'db:put:k'). Response is an ad-hoc record such as `(text:"Description Here", class:op, related:[List, Of, Values])`, or failure.

The module system can be considered stable with respect to incremental computing, i.e. 'load' and 'rt:reload' may be evaluated continuously.

## Exit Codes

Keeping it simple. 

         0  okay
         1  fail

The glas command line interface will favor log messages to report warnings or errors. Runnable applications may halt with an arbitrary integer exit code (up to 31 bits) but even for those I'd suggest generally sticking to error logs and only signaling failure with the exit code.
