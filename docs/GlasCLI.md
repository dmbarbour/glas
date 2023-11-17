# Glas Command Line Interface

The glas command line supports one primary operation:

        glas --run ValueRef -- Args

This is intended for use with a lightweight syntactic sugar.

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.run -- a b c

The behavior of 'run' is usually to start an application loop. The referenced value is compiled then interpreted as a program. Any runtime parameters - such as memory quota, choice of garbage collector, or effects API version - must be specified via annotation or effects.

Other built-in methods may be provided, such as '--extract' to support bootstrap, '--test' to support automatic testing, or '--help' and '--version' as standard Linux options.

## Value References

A value reference is a simple dotted path, starting with a module name. A global module is referenced by default, but local modules (within the current folder) may be specified via './' prefix.

        global-module-name.foo
        ./local-module-name.bar.baz

This indicates compilation of the specified module to a glas value, then extraction of the value reached by following the indicated label. The syntax for a value reference:

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

This syntax limits which fields and even which module names can be directly referenced. For example, users cannot directly reference a module whose name includes emoji. It is possible to work around these limits via application macros, writing an intermediate module, or simply avoiding use of troublesome names.

## Configuration

There is a folder identified as GLAS_HOME, configurable via environment variable. If this variable is not set, a user-local default follows an OS convention such as `~/.config/glas` in Linux or `%AppData%/glas` in Windows. This folder will centralize glas system configurations. By default, a persistent key-value database for use by applications will also be stored in GLAS_HOME.

The most important configuration file is perhaps "sources.tt". This file describes where to search for global modules. This file uses the [text-tree](../glas-src/language-tt/README.md) format - a lightweight alternative to XML or JSON. This file is currently limited to 'dir' entries indicating local filesystem, and entries that start with '#' for comments.

        # example sources.tt
        dir ./src
        dir /home/username/glas
        dir C:\Projects\glas\glas-src

Eventually, I intend to support network repositories as the primary source for global modules. However, short term a search path of the local filesystem will suffice. Relative paths are relative to the folder containing the configuration file. Order is relevant - locations are processed sequentially.

The glas executable may use a few more files within GLAS_HOME for features such as where to save stowed data, cached computations, or the key-value database. By default, these will be stored within GLAS_HOME.

## Running Applications

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. 

        glas --run ValueRef -- Args To App

The referenced value must currently have a 'prog' or 'macro' type header. This will change as glas evolves. Arguments following the '--' separator are forwarded to the application. Any runtime tweaks (GC, JIT, quotas, profiling, etc.) must be expressed via annotations or effects instead of additional command line options. 

### Transaction Loop Applications

Transaction loop application is the default, represented by a glas 'prog:(...)'. This program should have 1--1 arity and express a transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

In context of the command line, this loop starts with `init:["Args", "To", "App"]` and terminates by returning `halt:ExitCode`. The ExitCode should represent a small integer, with zero representing success and anything else a failure. When the step function returns `step:State`, any effects are committed then computation proceeds with the same State value as input.

A failed step aborts the current transaction, then retries. The retry might succeed if new data is available on input channels, or if different non-deterministic choices are made (e.g. via 'fork' effect). Ideally, the runtime will optimize this to wait or search for relevant changes that enable progress.

See [glas applications](GlasApps.md).

### Application Macros

Application macros are programs that return a value representing another application program. Currently, this is mode is implied for a value of form 'macro:prog:(...)'. Arguments may be explicitly staged via '--':

        glas --run MacroRef -- Macro Args -- Remaining Args

The macro program must be a 1--1 arity function. This function receives `["Macro", "Args"]` as input on the data stack and must return another value representing a runnable application (another macro is valid). The returned application is then run with `["Remaining", "Args"]` following the '--' separator; this separator may be omitted if there are no remaining arguments.

Macro evaluation has access to only language module effects, i.e. log and load. Essentially, application macros implement user-defined languages for the glas command line interface. Any program expressed via application macro can also be defined within a new module.

### Other Modes?

I'm currently developing [Grammar Logic Programming](GrammarLogicProg.md), which would indicate run-mode (transaction loop, macro, etc.) via annotation instead of based on headers. I might end up deprecating the current 'prog' model.

## Effects API

Specialized effects for transaction loop apps in context of the glas CLI:

* *load* and *log* - same as language modules, though module values may vary over time.
* *reload* - asks runtime to update future application loops based on updated sources.

Additionally, we might want other effects proposed in [glas apps](GlasApps.md). Ideally, 'load' and 'reload' should be stable effects, usable in the stable prefix of a forking application.

## Extracting Binaries

The glas command line can directly extract binary data to standard out.

        glas --extract ValueRef

The referenced value may currently have two forms:

* *data:Binary* - writes the binary to standard output
* *prog:(...)* - a 0--0 arity program is evaluated with limited effects:
  * *write:Binary* - writes the binary to standard output
  * *load* and *log* - same behavior as language modules

The primary motive for extract is to simplify bootstrap, by mitigating the need for a complete effects API in early versions of the glas executable. But this operation mode may prove convenient for extracting value from the glas module system in other use cases.

## Bootstrap

The bootstrap implementation for glas command line executable should be based around the '--extract' command. Assuming suitable module definitions, bootstrap could be expressed with just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

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

I'd prefer to avoid built-ins with sophisticated logic. But a few lightweight utilities to support early development or OS integration are acceptable.

Operations for early development:

* `--check ValueRef` - compile module and test that value is defined.
* `--print ValueRef` - build then pretty-print a value for debugging.

Operations for OS integration:

* `--version` - print executable version information
* `--help` - print information about options

In addition, we might benefit from some built-in options for manipulation of the glas system, e.g. to configure the global module search path ($GLAS_HOME/sources.tt). But this is low priority. I hope to focus early on developing user-defined operations.

## Thoughts

### Automatic Testing

I wouldn't mind a `glas --test` operation for running all test programs (based on 'test-*' modules) either globally or locally. I do hesitate because the best solutions involve a lot of caching and remembering which forks lead to failure. I expect we'll get a user-defined `glas test` first, then integrate it as a built-in later.

### Debug Mode

It is feasible for the glas executable to support debugging of an app. This could be expressed via annotations to build a special debug view. However, it is also feasible to build this debug view manually via metaprogramming, like a macro that explicitly rewrites the program. The latter option would move debugging logic from the glas command line executable into the module system, and is more to my preference.

### Profiling

Profiling will need some more consideration than I've given it so far. Some runtime support is needed to track the failed transactions efficiently. Annotations would guide profiling, e.g. enable or disable it for a subprogram, and give names to subprograms for profiling purposes.

### Application Macro access to Environment Variables

I could extend application macros with access to environment variables. However, I'm uncertain that I want to encourage use of the environment for interpreting the command line 'language'. Additionally, most use of the OS layer env is hindered without also having access to read files and other features. 

For now, decided to treat application macros a lightweight extension to language modules for command line arguments. This ensures that anything we express via command line can also be easily abstracted within a new module and shared with other users, which is a convenient property.

### Applications Objects

I've been contemplating a more object-based application model. Instead of a global step function that conflates handling of GUI, data feeds, etc. the app could provide fine-grained methods that still follow the transaction machine model but are more specialized (i.e. per-method effect, parameter, and return types). 

With some careful design, we could (for example) ensure that rendering an app doesn't affect application state, or that data subscriptions are very robust to network issues.

However, this requires a lot of careful design work that I'd prefer to avoid introducing as a prerequisite to a working glas system. My current decision on this is to develop it later. Perhaps start with implementing applet/objects by compiling to a more conventional global main-loop app.

### Log Options

Currently I log everything to stderr with some colored text. This works alright for loading modules initially, but it isn't a great fit for transaction machine applications. Something closer to a tree of messages, with a scrubbable history, might be appropriate. Anyhow, this area could use a lot of work.


