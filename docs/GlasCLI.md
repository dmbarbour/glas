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

The command-line tool will always include an option to directly extract binary data. 

        glas --extract ValueRef

The reference must evaluate to a binary (a list of bytes). This binary is written to stdout, then the command halts.

## Running Applications

        glas --run ValueRef -- List Of Args

The referenced value must be a Glas program, representing a process with a 'prog' header. This might be extended later, for performance reasons. See the [application model](GlasApps.md) for general information.

        type Process = (init:Args | step:State) -- [Eff] (step:State | halt:Result) | Fail

Initial arguments are the command-line arguments, `init:["List", "Of", "Args"]`. Final result should be `halt:ExitCode` where ExitCode is a bitstring interpreted as a signed integer. 

## Environment Variables

Currently, using only two environment variables:

* **GLAS_DATA** - a folder for content-addressed storage, persistent memoization cache, shared database, and extended configuration. If unspecified, defaults to a user-specific folder such as `~/.glas`.
* **GLAS_PATH** - search path for global modules. Follows OS conventions for PATH environment variables. Potentially extended or overridden by configuration files in GLAS_DATA.

## Effects API

The effects API for runnable applications is essentially everything listed in [Glas Apps](GlasApps.md) plus the 'load' effect used by language modules. This is slightly tweaked for context:

* **load:ModuleRef** - load current value of the referenced module, or fail if the module cannot be compiled. A 'local' module is relative to the working directory.

The ability to observe updates to a module between transactions can be useful for live coding if paired with accelerated evaluation.

## Exit Codes

Keeping it simple.

         0  okay
        -1  not okay

Glas systems will rely on log messages more than exit codes to describe errors. No reason to think hard about this.
