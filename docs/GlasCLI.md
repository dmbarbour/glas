# Glas Command Line Interface

The glas executable generally takes the first command line argument as a switch to interpret the remainder of the arguments. There are a few options for running applications from different sources, and some ad-hoc operations to help users understand and control the glas system. 

        glas --run AppName Args To App
        glas --script SourceRef Args To App
        glas --script.FileExt FilePath Args To App
        glas --cmd.FileExt "Source Text" Args To App 
        glas --cache CacheOp
        glas --conf ConfigOp
        ... etc. ...

A simple syntactic sugar supports user-defined operations:

        glas opname Args
          # implicitly rewrites to
        glas --run cli.opname Args

My vision for early use of glas systems is that end users mostly operate through user-defined operations. To avoid cluttering the command line with runtime switches, we push runtime options into the configuration file, application settings, or (rarely) OS environment variables.

## Configuration

The `GLAS_CONF` environment variable may specify a configuration file. If undefined, the default locations are `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows. This configuration file is compiled and loaded, based on file extension and built-in front-end compilers. A configuration may redefine these under `env.lang.FileExt`, in which case we bootstrap: rebuild via configured compiler, repeat, verify stable fixpoint after a few cycles.

The main content of a configuration is an environment, 'env.\*', of shared libraries and applications. This environment is fed back into the configuration as '%env.\*' then propagates to most modules, serving as a global namespace. This environment may be very large, relying on lazy loading. See [glas namespaces](GlasNamespaces.md) for how this works.

Secondary outputs include 'glas.\*' CLI or runtime configuration options, and 'app' which by convention defines an application that implements a projectional editor and command shell.

Typically, a user or project configuration inherits a community or company configuration via DVCS. The user configuration has a root file, but may expand beyond that, e.g. integrating some CSV or JSON files for convenient tooling.

## Running Applications

Applications can be defined within the user configuration or separate script files. See [glas applications](GlasApps.md) for details. 

* **--run AppName**: Refers to 'env.AppName.app' defined in the configuration namespace. As a special case, '--run .' refers to toplevel configuration 'app'.
* **--script FilePath**: Process indicated file in context of configured front-end compiler (based on file extension). The expected result is a module that defines 'app'. Link this module in context of the configured environment.
  * **--script.FileExt FilePath**: as '--script' except we select a front-end compiler based on a given file extension, ignoring the actual extension. Useful in context of Linux shebang lines.
* **--cmd.FileExt SourceText**: as '--script.FileExt' except we also provide the script text as a command-line argument. We might present this as a read-only virtual file.

## Installing Applications

In context of lazy loading and DVCS sources, we must generally be 'online' to run glas applications. Further, even if sources are local, there is latency on first run due to compilation. To mitigate, we can support 'installing' applications ahead of time, maintaining a local cache of compiled code.

This might be expressed with a few command-line options to maintain a `".config/glas/installs"` file or folder, listing installs and checking for updates, much like apt and similar tools.

Relatedly, compilation should eventually be separated and shared, e.g. by configuring proxy compilers and trusted PKI signatures.

## Trusting Application

I propose to assign trust to providers of source code, leveraging PKI infrastructure. User, company, or community configurations may trust individual developers or certificate authorities with access to security-sensitive features such as FFI. Source folders shall include signed manifests and certificates within `".pki/"` subfolders.

In theory, trust can be scoped. For example, when signing a manifest or certificate, a developer might indicate they trust code with GUI but not full FFI or network access. Unfortunately, I cannot imagine developers precisely scoping trust to individual sources. It seems more feasible, in practice, to trust providers, e.g. a given developer is trusted with GUI.

Attenuation of trust is achieved through annotation of trusted libraries or frameworks. For example, a user trusts the provider of library X with FFI access, but annotations for some definitions within library X may relax the requirement that the caller is trusted with FFI, optionally introducing a requirement that the caller is instead trusted with GUI. Leveraging abstract data types, we might distinguish trust requirements for *opening* a file from further operations on the abstract file.

A compiler can validate trust annotations and attenuations much like type annotations. Trusted providers can develop trusted sandboxes for untrusted applications. Applications can be developed with various trust assumptions, e.g. building upon code from popularly trusted providers versus using 'sys.ffi.\*' directly. Trust is relatively fine-grained compared to trusting full applications.

## Built-in Tooling

The glas executable may be extended with useful built-in tools. Some tools that might prove useful:

* **--conf** - inspect and debug the configuration, perhaps initialize one
* **--apt** - install, uninstall, update defined applications and libraries
* **--cache** - inspect cached resources, manual management options
* **--db** - query, browse, watch, or edit persistent data in the shared heap

Functions available via built-in CLI tools should be accessible through applications, even if only through 'sys.refl.\*' reflection APIs. Also, I hope to keep the glas executable relatively small, enough to bootstrap but pushing most logic into the module system and cached compiled code.

## Glas Shell

In context of live coding and projectional editing, applications may provide their own IDE. See [glas notebooks](GlasNotebooks.md). The user configuration is no exception, it could provide 'app' for self editing. 

We could feasibly leverage this as a basis for a [shell](https://en.wikipedia.org/wiki/Shell_(computing)) in glas systems. Instead of running individual glas applications from the command line, run the shell and modify it. To 'run' an application would then be to integrate it with the shell. 

In practice, we'll often want instanced shells, such that common edits like running the application are local to an instance. We could feasibly introduce command-line tools to run instances, or rely on conventions for instancing internally within the shell.

## Implementation Roadmap

The initial implementation of the glas executable must be developed outside the glas system. This implementation will lack many features, especially the optimizations that would let transaction loops scale beyond a simple event dispatch loop. Fortunately, simple event dispatch loops are still very useful, and we can fully utilize a system between FFI, accelerators, and sparks. We also have access to conventional threaded applications.

Ideally, we'll eventually bootstrap the glas executable within the glas system. Early forms of bootstrap could generate C or LLVM, but I hope to swiftly eliminate external dependencies and integrate relevant optimizations into the glas libraries.


