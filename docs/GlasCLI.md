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

Initial arguments are the command-line arguments, `init:["List", "Of", "Args"]`. Final result should be a bitstring that we'll cast to an int. 

## Exit Codes

Mostly, we'll just return 0 for okay or -1 for errors. Details on why things failed should be in the log (stderr, by default). The application may use its own exit codes.

## Effects API

The effects API for Glas CLI is essentially everything listed in [Glas Apps](GlasApps.md) plus the 'load' effect described for language modules.

* **load:ModuleRef** - load current value of referenced module, or fail if the module cannot be compiled. References are the same as used by language modules.

The ability to update source code and load updated module values, together with accelerated evaluation, can provide an effective basis for live coding in Glas systems. In context of incremental computing, 'load' is potentially stable until the module is updated.
