# Glas Command Line Interface

The glas command line supports two primary operations:

        glas --run ValueRef -- Args
        glas --extract ValueRef

The '--run' operation starts an application loop capable of network and filesystem access. The '--extract' operation prints binary data to stdout and is intended to simplify bootstrap. Other built-in operations include '--help' and '--version'. However, most glas operations will be user-defined. This begins with a lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.run -- a b c

Behavior of 'run' depends on the program and may be influenced by annotations. As a rule, runtime parameters - such as memory quota, choice of garbage collector, or version of effects API - must be specified via annotation or effects.

## Value References

Both '--run' and '--extract' are parameterized by a reference to a value in the glas module system. This uses a simple dotted path such as `module-name.symbol.data`. The full syntax for a value reference:

        ValueRef = (ModuleRef)(ComponentPath)
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './'Word
        GlobalModule = Word
        ComponentPath = ('.'Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

This syntax limits what can be directly referenced. For example, it does not support modules named with emoji or indexing into a list. However, it is not difficult to work around these limitations with application macros.

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

The glas command line knows how to interpret some values as runnable applications, with access to ad-hoc effects including filesystem and network. See [glas applications](GlasApps.md).

        glas --run ValueRef -- Args To App

The referenced value must currently have a 'prog' or 'macro' type header. This will change as glas evolves. Arguments following the '--' separator are forwarded to the application. Any runtime tweaks (GC, JIT, quotas, profiling, etc.) must be expressed via annotations or effects instead of additional command line options. 

### Basic Applications

A basic application process is represented by a glas program with the 'prog' header.

        prog:(do:Program, ... Annotations ...)

Currently, the program should have 1--1 arity and express a transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

In context of the command line, this process starts with `init:["List", "Of", "Args"]` and terminates by returning `halt:ExitCode`. The ExitCode should represent a small integer, with zero representing success and anything else a failure. If the step function returns `step:State` then computation continues with the same value as input to the next step.

If the step fails, any effects are aborted then the step is retried. Ideally, it is retried after some change to the effectful inputs, to avoid predictably computing failure. This provides a lightweight basis for programming reactive systems.

### Application Macros

Application macros are distinguished by the 'macro' header. At the moment, only 'prog' type macros are supported.

        macro:prog:(do:Program, ...)

To simplify caching, arguments are explicitly staged via '--':

        glas --run MacroRef -- Macro Args -- Remaining Args

The macro program must be a 1--1 arity function. This function receives `["Macro", "Args"]` as input on the data stack and must return another value representing a runnable application (another macro is valid). The returned application is then run with `["Remaining", "Args"]` following the '--' separator; this separator may be omitted if there are no remaining arguments.

Macro evaluation has access to only language module effects, i.e. log and load. Essentially, application macros implement user-defined languages for the glas command line interface. Any program expressed via application macro can also be defined within a new module.

## Extended Effects API

In context of glas command line, I propose a few specialized extensions to the effects API:

* **load:ModuleRef** - load current value of a module. Value of module may update between transactions. 
* **reload** - rebuild and redeploy application from updated source while preserving application state. Fails if application cannot be rebuilt (e.g. if its source is in a bad state) or if redeployment is infeasible. Otherwise returns unit but the update is deferred until after commit.

These extend the effects API proposed in [glas apps](GlasApps.md), and would qualify as stable effects.

## Extracting Binaries

The glas command line can directly extract binary data to standard out.

        glas --extract ValueRef

The reference must evaluate to binary data, that is a list of bytes. After evaluation, the binary is simply written to stdout. The primary motive for this feature is to support bootstrap without implementing a complete runtime effects API, but it can be useful for obtaining value from the glas module system in general.

## Bootstrap

The bootstrap implementation for glas command line executable should be based around the '--extract' command. Assuming suitable module definitions, bootstrap could be expressed with just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, different binaries are needed for different operating systems and architectures. This is easily resolved. We could name modules for different targets, such as 'glas-binary-linux-x64'. Or we could introduce a 'target' module that serves a similar role as architecture-specific headers. We can override 'target' for cross compilation.

*Note:* During early bootstrap, we might favor an intermediate language, e.g. extract to ".c" file, then apply mature optimizing C compiler.

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

Currently I log everything to stderr with some colored text. This works alright for loading modules initially, but it isn't a great fit for transaction machine applications. Something closer to a tree of messages, with a scrubbable history, might be appropriate.