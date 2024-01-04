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

A FilePath is recognized by containing at least one directory separator (such as '/'). The glas command line interface will attempt to interpret any file or folder as a module, if requested. Otherwise, we'll search the configured distribution for a global module of the given name. Eventually, we might extend these two options with URIs.

## Configuration

All relevant configuration information is centralized to a profile, a ".prof" file. The GLAS_PROFILE environment variable selects the active configuration. The default is OS-specific, such as `"~/.config/glas/default.prof"` in Linux or `"%AppData%\glas\default.prof"` in Windows.

Potential configuration information includes:

* active distribution
* storage locations 
* logging destinations
* ad-hoc performance tuning
* debug assertions or profiling
* trusted proxy compilers
* content delivery networks

No need for reference between profiles yet, but some lightweight inheritance mechanism might be useful later. Also, application-specific sub-profiles are feasible, by expressing 'rules' that are sensitive to annotations (or environment variables). 

As a short term scaffold, we might only support simplified definition of the distribution as a filesystem search path:

        dist 
            search Directory1
            search Directory2
            ...

More features can be added as needed. I propose the [text tree syntax](TextTree.md). 

## Distributions

In context of glas, a distribution represents a set of named global modules that are maintained and versioned together. It is often convenient to express distributions in terms of inheriting from a community distribution (perhaps a DVCS repository), mixing in a few patch-like distributions, then adding a few local applications or overrides. To allow this, distributions will support multiple inheritance. For consistency, I intend to reuse multiple inheritance similar to that proposed for grammar-logic programming.

Initially, we'll just use a search path in profile. After bootstrap, I intend to eventually support inheritance from ".dist" files with reference to DVCS branches and other repositories.

## Running Applications

        glas --run ModuleRef -- Args

The glas executable first compiles the referenced module into a value, which must be recognized as representing an application. Initially, [grammar-logic modules](GrammarLogicProg.md) are recognized, assuming the 'main' method of the 'app' grammar. This program may include annotations to configure runtime options. The run-mode annotation is especially relevant, influencing integration.

Recognized run modes include:

* *loop* - the default, a [transaction loop](GlasApps.md) step function
* *staged* - application has type `Args -> Application`.
  * arguments may be divided across stages using '--'.
  * `glas --run staged-op -- stage one args -- stage two args`
* *test* - application has type `() -> ()`, we're interested in pass/fail
  * effects are *log*, *load* (global), and *fork(Nat)*
  * fork is non-deterministic choice for parallel or fuzz testing
* *binary* - application function from `Args -> Binary`. 
  * binary is written to standard output once computed
  * limited access to *log* and *load* (global) effects
  * motive is to simplify bootstrap of glas executable 

The glas executable might also evaluate assertions. A lot of processing can be cached, including assertions and JIT-compilation, to reduce rework across command line operations.

## Other Operations

Guiding principles for introducing features: 

* Can support via user-defined operation. 
* Doesn't add too much logic to executable.
* Sufficient utility to justify built-in.  

These three points should hold simultaneously. In some cases we might add some effects or annotations to make the first point hold, e.g. to support '--version' as a user-defined op we can add a method to the runtime's reflection API.

Proposed Operations:

* `--version` - print version to console
* `--help` - print basic usage to console 
* `--check ModuleRef` - compile module pass/fail, do not run
* `--list-modules` - report global modules in configured distribution
* `--script(.ext)*` - extended scripting support (see *Scripting*)

### Scripting

Proposed built-in operation:

        glas --script(.ext)* (ScriptFile) (Args)

Usage context:

        #!/usr/bin/glas --script.g.m4
        program goes here

This operation loads the script file, removes the shebang line, compiles the script program (without local modules), then runs it as an application. 

## Exit Codes

Keeping it simple. 

         0  pass
         1  fail

Using log messages for detailed warnings or errors.

## Bootstrap

The bootstrap implementation of 'glas' might support only the *binary* run-mode for applications. This reduces need to implement a complete effects system up front. Assuming we can print a binary to standard output, the following can potentially work:

    # build
    /usr/bin/glas --run glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --run glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

The target architecture could be provided as an argument or by defining a 'target' module.

*Note:* It is feasible to support early bootstrap via intermediate ".c" file or similar, to leverage a mature optimizing compiler. But I hope to eventually express all optimizations within the glas module system!
