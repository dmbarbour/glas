# Glas Command Line Interface

The glas executable generally takes the first command line argument as a switch to interpret the remainder of the arguments. There are a few options for running applications from different sources, and some ad-hoc operations to help users understand and control the glas system. 

        glas --run AppName Args To App
        glas --script SourceRef Args To App
        glas --script.FileExt FilePath Args To App
        glas --cmd.FileExt "Source Text" Args To App 
        glas --shell Args To App
        glas --cache CacheOp
        glas --conf ConfigOp
        ... etc. ...

A simple syntactic sugar supports user-defined operations:

        glas opname Args
          # implicitly rewrites to
        glas --run cli.opname Args

My vision and intention is that end users mostly operate through user-defined operations. To avoid cluttering the command line with runtime switches, we'll push all configuration options into application 'settings' and the configuration file.

## Configuration

The glas executable will read a user configuration based on a `GLAS_CONF` environment variable, loading the specified file as a [namespace](GlasNamespaces.md). If unspecified, the default location is `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows.

A typical user configuration will inherit from a community or company configuration from DVCS, then override some definitions for the user's projects, preferences, and resources. The community configuration may define hundreds of applications and libraries. For performance, we rely on lazy loading and caching. The DVCS repository becomes the basis for curation and version control. For precise control, maintainers could link repositories by version hash instead of branch names.

Shared libraries, languages, and applications are typically defined under 'env.\*'. We'll link '%env.\*' to 'env.\*' when loading the configuration, scripts, and staged applications. The user configuration may also define a toplevel application under 'app.\*', which can serve as the basis for a *Glas Shell* (described later).

Outside of 'env.\*' and 'app.\*', definitions are ad hoc, runtime-specific, subject to de facto standardization. The glas executable may expect definitions for shared heap storage locations, RPC registries, trusted certification authorities, and so on. An extensible executable may support user-defined accelerators, optimizers, and typecheckers, also defined in the user configuration.

Applications define 'app.settings' to guide configuration of application-specific options, such as where to write log files or which ports to open for HTTP and RPC. The glas executable does not observe settings directly, instead enabling the configuration to query and interpret application settings when deciding the configured value. For maximimum portability, the runtime may ask the configuration to generate an adapter based on application settings and runtime version info.

## Running Applications

Users can reference applications in a few ways:

* **--run**: To run 'foo', we look for 'env.foo.app.\*' in the configuration namespace. A community configuration might define hundreds of applications to be lazily downloaded and compiled on demand, or installed by name.
  * To reference outside 'env.\*' users may add a '.' prefix, e.g. '.foo' binds to 'foo.app.\*'.
* **--script**: Load the indicated file, package folder, or URL as a namespace. This namespace defines 'app.\*'.
  * **--script.FileExt**: Same as '--script' except we use the given file extension in place of the actual file extension for purpose of user-defined syntax. Mostly for use with shebang scripts in Linux, where file extensions may be elided.
* **--cmd.FileExt**: Treated as '--script.FileExt' for an anonymous, read-only file found in the caller's working directory. The assumed motive is to avoid writing a temporary file.

The glas executable may support more than one run mode as an application-specific configuration option. For example, the executable might expect 'app.start' and 'app.step' for a transaction-loop application, 'app.main' for a threaded application, or 'app.build' for a staged application. The only method every application needs is 'app.settings' to guide configuration. See [glas applications](GlasApps.md).

## Installing Applications

Installing applications - ensuring they're available for low-latency or offline use - can be understood as a form of manual cache management. A user configuration might recommend that a set of definitions is maintained locally. To simplify tooling, we might add a little indirection, perhaps referencing a local file or shared-heap variable. This is easily extended to installing scripts.

Ideally, 'installing' an application reduces to downloading an executable binary or whatever low-level JIT-compiled representation is cached by the glas executable. Or if not the that, then at least avoiding rework for the more expensive computations. This is feasible using an approach similar to Nix package manager, i.e. downloading from a shared cache based on transitive secure hashes of contributing sources. The user configuration could specify one or more trusted, shared caches.

## Security

Not every application should be trusted with full access to FFI, shared state, and other sensitive resources. This is true within a curated community configuration, and for external scripts of ambiguous provenance. What can be done?

A configuration describes who the user trusts and in which roles, or how to lookup this information. The glas executable can build an extended cache of signed manifests and certified public keys, e.g. by searching `".glas/"` subfolders when loading sources. Trust can be scoped by signatures and extended by annotations, and tracked at a fine granularity based on contributing sources.

A trusted library might use FFI to implement a GUI framework. Based on annotations, that library can extend trust for specific methods in the library's public interface to a client trusted only with GUI. Of course, specific features such as a web-engine view may require greater trust. An application that anticipates running without much trust can voluntarily sandbox itself by restricting which interfaces it uses.

Before running an application, the glas executable will analyze the call graph for violations of the configured security policy. If there are any concerns, we can warn users then let them abort the application, extend trust, or run in a degraded mode where some transactions are blocked for security reasons. Of course, exactly how this is handled will also be configurable.

## Glas Shell

A user configuration may also be an application, defining 'app.\*' at the toplevel. In context of [notebook applications](GlasNotebooks.md) this application will often represent a live-coding projectional editor for the configuration and its transitive dependencies. Users can run this application via `"glas --run . Args To App"` without any special features. 

We can feasibly present this notebook as the main [shell](https://en.wikipedia.org/wiki/Shell_(computing)) for a glas system. Instead of running applications as separate OS processes, user actions can compose applications into the notebook, integrating with 'app.step', 'app.http', 'app.gui', and so on. This shell could support both graphical and command-line interfaces, the latter via 'sys.tty.\*'.

In practice, we'll want instanced shells. Instancing can be implemented by copying a configuration folder, logically overlaying part of the filesystem, or introducing an intermediate configuration file that inherits and overrides definitions. A glas executable might have built-in support for instanced shells via `"glas --shell ..."`, perhaps naming the shell for persistence.

## Built-in Tooling

The glas executable may be extended with useful built-in tools. Some tools that might prove useful:

* **--conf** - inspect and debug the configuration, perhaps initialize one
* **--cache** - manage installed applications and resources, clean up
* **--db** - query, watch, or edit persistent data.
* **--rpc** - inspect RPC registries, perhaps issue some RPC calls directly from command line
* **--debug** - debug or manipulate running apps from the command line
* **--trust** - tools to manage trust of scripts or app providers, adding, removing, or inspecting roles

Heuristics to guide tooling: First, any function available via CLI tools must also be accessible within glas applications. This might involve introducing 'sys.refl.conf.\*' or similar methods. Even 'sys.refl.cli.help' and 'sys.refl.cli.version' may be included. Second, we should aim to keep glas executables small, assigning code bloat a significant weight.

## Implementation Roadmap

The initial implementation of the glas executable must be developed outside the glas system. This implementation will lack many features, especially all the optimizations that would let transaction loops scale beyond a simple event dispatch loop. But it should be feasible to provide basic file system access and console IO, HTTP integration, simple persistent state, lazy evaluation of namespaces and simplistic caching, and loading of DVCS resources.

Ideally, we'll eventually bootstrap the glas executable by extracting another executable binary. Something like:

    # build
    /usr/bin/glas --run glas-bin > ~/.glas/tmp/glas
    chmod +x ~/.glas/tmp/glas

    # verify
    ~/.glas/tmp/glas --run glas-bin | cmp ~/.glas/tmp/glas

    # install
    sudo mv ~/.glas/tmp/glas /usr/bin/glas

Early forms of bootstrap could be adjusted to instead generate C or LLVM or similar to be further compiled instead of directly to executable format. But I hope to eliminate such dependencies.
