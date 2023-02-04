# Glas Command Line Interface

The glas command line supports two primary operations:

        glas --extract ValueRef
        glas --run ValueRef -- Args

There are also a few secondary operations built-in, such as '--help' or '--version'. All built-in operations start with '--'. User-defined operations are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

In my vision for glas systems, most operations are user-defined, and all built-in operations can feasibly be implemented via '--run'. Behavior is primarily represented in the module system.

## Value References

The primary operations are parameterized by a reference to a value in the glas module system. This reference specifies a module (local to working directory or global) and may index into a dictionary via dotted path.

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

This syntax limits which modules can be referenced. For example, if a module name contained an emoji character or unusual punctuation, it cannot be directly referenced from glas command line '--run'. Such modules can still be referenced indirectly, e.g. via application macro. But I encourage glas system modules to keep module names simple.

## Configuration

The glas executable will centralize configuration files, cached computations, content-addressed storage, and a key-value database into a single folder. This folder may be specified by the GLAS_HOME environment variable or will be implicitly assigned a value based on OS convention such as `~/.config/glas` in Linux or `%AppData%/glas` in Windows.

The primary configuration file is "sources.tt". This uses the [text-tree](../glas-src/language-tt/README.md) format. Each entry in this file represents a location to search for global modules in priority order. Comments are supported with label '\rem'. Currently, this is limited to 'dir' entries for local filesystem directories.

        \rem example sources.tt
        dir ./src
        dir /home/username/glas
        dir C:\Users\username\glas
        dir ../../glas

There may be some secondary runtime configuration expressed via local glas modules, e.g. a module named "conf" within the GLAS_HOME directory. This might support some options for logging, caching, sandboxing, and so on. These modules could use any language defined in the module system.

Finally, any application-specific runtime configuration must be expressed using annotations. Annotations can be compiled into apps or abstractly manipulated by application macros. Profiling, tuning memory allocation and GC, even effects API versioning may be supported via annotations. 

Importantly, there are no built-in command line arguments for runtime configuration. This ensures configuration is always accessible for abstraction.

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

A basic application process is represented by a glas program with the 'prog' header.

        prog:(do:Program, ... Annotations ...) 

The program must have 1--1 arity and express a transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

In context of the command line, this process starts with `init:["List", "Of", "Args"]` and terminates by returning `halt:ExitCode`. The ExitCode should be a bitstring representing a small integer. If the step function returns `step:State` that value becomes the next step's input. After any successful step, effects are committed. On a failed step, effects are aborted and the step is retried.

*Note:* If annotations or inference indicate the program has another type, it might still be runnable via '--run' (depending on the glas command line executable) but it is not what I call a 'basic' application.

### Process Networks

I am developing another program model with the 'proc' header that should be easier to optimize for incremental computing, concurrency, distribution, and transaction fusion. Design of 'proc' is ongoing within the [glas applications](GlasApps.md) document. It is still based around the transaction machine concept. Once it is stable, we might add support for running 'proc' apps to 'glas --run'. 

### Application Macros

Application macros are distinguished by the 'macro' header.

        macro:prog:(do:Program, ...)

To simplify caching, arguments are explicitly staged via '--':

        glas --run MacroRef -- Static Args -- Dynamic Args

At the moment only 'prog' type macros are supported. The macro program must be a 1--1 arity function and here would receive list of strings `["Static", "Args"]` on the data stack. The returned value must represent another application (potentially another application macro), which then is run receiving `["Dynamic", "Args"]`. The '--' separator may be omitted if there are no dynamic args.

The macro program has access to the same effects as language modules, i.e. 'log' and 'load'. All other effects will fail. Application macros are usefully understood as user-defined languages for the command line interface. User-defined ops as macros can support a very flexible user interface.

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

The glas command line interface will favor log messages to report warnings or errors. Runnable applications may halt with a small integer exit code. But even for apps I recommend log messages instead of relying on informative exit codes.

## Secondary Operations

I'd prefer to avoid built-ins with sophisticated logic. But a few lightweight utilities to support early development or OS integration are acceptable.

Operations for early development:

* `--check ValueRef` - compile module and test that value is defined.
* `--print ValueRef` - build then pretty-print a value for debugging.

Operations for OS integration:

* `--version` - print executable version information
* `--help` - print information about options

Ideally, we should move ASAP from built-ins to user-defined operations such as 'glas-cli-print'.

## Thoughts

### Debug Mode

It is feasible for the glas executable to support debugging of an app. This could be expressed via annotations to build a special debug view. However, it is also feasible to build this debug view manually via metaprogramming, like a macro that explicitly rewrites the program. The latter option would move debugging logic from the glas command line executable into the module system, and is more to my preference.

### Profiling

Profiling will need some more consideration than I've given it so far. Some runtime support is needed to track the failed transactions efficiently. Annotations would guide profiling, e.g. enable or disable it for a subprogram, and give names to subprograms for profiling purposes.

### Application Macro access to Environment Variables

I could extend application macros with access to environment variables. However, I'm uncertain that I want to encourage use of the environment for interpreting the command line 'language'. Additionally, most use of the OS layer env is hindered without also having access to read files and other features. 

For now, decided to treat application macros a lightweight extension to language modules for command line arguments. This ensures that anything we express via command line can also be easily abstracted within a new module and shared with other users, which is a convenient property.

### Applications Objects

I've been contemplating a more object-based application model. The basic application program can roughly be viewed as a class where 'init' is the constructor and 'step' is the only method after construction. But we might envision an alternative model where there are multiple methods - methods to render GUI views, publish services, subscribe to resources, receive user events, advance through time, etc.. 

The benefit of having many methods is that we can more precisely control the effects API for each method. For example, we could guarantee that rendering views does not modify application state, or that active views are visible only in an idempotent manner. I'm not in a hurry to directly support this at the glas command line executable. It would be wiser to first explore it using an adapter, compiling to the simpler 'prog' or 'proc' application models.
