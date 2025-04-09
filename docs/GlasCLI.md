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

My vision for early use of glas systems is that end users mostly operate through user-defined operations. To avoid cluttering the command line with runtime switches, we push runtime options into the configuration file, application settings, or (rarely) OS environment variables.

## Configuration

The glas executable will read a user configuration based on a `GLAS_CONF` environment variable, loading the specified file as a [namespace](GlasNamespaces.md). If unspecified, the default location is `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows.

A typical user configuration will inherit from a community or company configuration from DVCS, then override some definitions for the user's projects, preferences, and resources. Each DVCS repository becomes a boundary for curation, security, and version control. A community configuration will define hundreds of shared libraries and applications in 'env.\*', relying on lazy loading and shared caching for performance. 

To mitigate risk of naming conflict, the runtime will recognize configuration options under 'conf.\*'. The glas executable may expect definitions for shared heap storage, RPC registries, rooted trust for public key infrastructure, and so on. An extensible executable may support user-defined accelerators, optimizers, and typecheckers via the user configuration. We could maximize portability by asking a configuration to generate an adapter based on application settings and runtime version info.

## Running Applications

An application is expressed within a namespace, using 'app.\*' methods to simplify recognition, access control, extraction, and conflict avoidance. Users can reference and run applications in a few ways:

* **--run**: To run 'foo', we look for 'env.foo.app.\*' in the user configuration. This application is lazily downloaded and compiled on demand, usually caching to avoid redundant effort.
  * *note:*  Users may run hidden apps outside of 'env.\*' via '.' prefix, e.g. '.foo => foo.app.\*'. 
* **--script**: Compile indicated file, package folder, or URL. The generated namespace must define 'app.\*' at the toplevel.
  * **--script.FileExt**: Same as '--script' except we use the given file extension in place of the actual file extension for purpose of user-defined syntax. Mostly for use with shebang scripts in Linux, where file extensions may be elided.
* **--cmd.FileExt**: Treated as '--script.FileExt' for an anonymous, read-only file found in the caller's working directory. The main use case is shell scripts avoiding intermediate files.

Every application must at least define 'app.settings' to guide integration. The runtime does not observe settings directly. Instead, an 'app.settings' handler is passed when querying the configuration for application-specific options.

Among the application-specific configuration options, a glas executable may support multiple run modes. For example, a transaction-loop application uses 'app.start' and 'app.step', a threaded application defines 'app.main', a staged application could specifies another namespace procedure 'app.build'. Thus, exactly what happens when we run an application depends on 'app.settings', and is independent of application source. 

See [glas applications](GlasApps.md).

*Note:* Alternatively, we might model an application namespace as a set of handlers generated within a call graph. This would simplify modeling application state as a set of local vars on the stack. In any case, an application may have more than one associated method.

## Installing Applications

Installing applications - ensuring they're available for low-latency or offline use - can be understood as a form of manual cache management. A user configuration might recommend that a set of definitions is maintained locally. To simplify tooling, we might add a little indirection, perhaps referencing a local file or shared-heap variable. This is easily extended to installing scripts.

Ideally, 'installing' an application reduces to downloading an executable binary or whatever low-level JIT-compiled representation is cached by the glas executable. Or if not the that, then at least avoiding rework for the more expensive computations. This is feasible using an approach similar to Nix package manager, i.e. downloading from a shared cache based on transitive secure hashes of contributing sources. The user configuration could specify one or more trusted, shared caches.

## Initial Namespace

The glas executable provides an initial namespace containing only a few [program primitives](GlasProg.md) under '%\*'. The space of primitives is marked read-only. Scripts or staged applications are written into the same namespace as the user configuration, albeit in separate volumes for access control. Viable translations:

        # user configuration
        move: { "%" => WARN, "" => "u." }
        link: { "%" => "%", "%env." => "u.env.", "" => "u." }

        # script or staged app (at addr)
        move: { "%" => WARN, "" => "addr." }
        link: { "%" => "%", "%env." => "u.env.", "" => "addr." }

The front-end compiler will further introduce '@\*' compiler dataflow definitions to support automatic integration across module boundaries. This is also the case for a built-in front-end compiler. However, from the runtime's perspective, these are normal definitions and receive no special attention.

From a regular programmer's perspective, '%\*' and '@\*' are implementation details, and the initial namespace is effectively empty. However, regular users will also inherit from a community configuration

## Security

Not every application should be trusted with full access to FFI, shared state, and other sensitive resources. This is true within a curated community configuration, and even more so for external scripts of ambiguous provenance. What can be done?

Trust can be tracked for individual definitions based on contributing sources. This can be supported via public key infrastructure, with trusted developers signing manifests and having their own public keys signed in turn. The user configuration can specify the 'root' trust, or how to look it up. The glas executable can heuristically search `".pki/"` subfolders when loading sources for signed manifests and derived certificates. In context of DVCS, those certificates might propagate trust across DVCS repos.

Trust can be scoped. For example, when signing a manifest, a developer can indicate they trust code with GUI but not full FFI or network access. Unfortunately, we cannot count upon developers precisely scoping trust, nor will regular end-users be security savvy. Instead, communities scope trust of developers or public signatures by serving as certificate authorities. A developer might be trusted with GUI and 'public domain' network access, and this extends to code signed by that developer.

Trust can be attenuated via annotations. For example, a trusted shared library might implement a GUI using FFI. FFI requires a very high level of trust, but GUI does not. Based on annotations, the library could indicate that a subset of public methods only require the client is trusted with GUI access. A developer can voluntarily sandbox an application by interacting with the system only through such libraries.

Before running an application, the glas executable can analyze the call graph for trust violations. If there are any concerns, we might warn users and let them abort the application, extend trust, or run in a degraded mode where a subset of transactions is blocked for security reasons. Of course, exactly how this is handled should be configurable.

*Note:* This trust-based security model can be conveniently combined with security based on abstract data types and access control to definitions or effects handlers. However, the latter techniques are useful only insofar as we trust the toplevel application.

## Built-in Tooling

The glas executable may be extended with useful built-in tools. Some tools that might prove useful:

* **--conf** - inspect and debug the configuration, perhaps initialize one
* **--cache** - manage installed applications and resources, clean up
* **--db** - query, browse, watch, or edit persistent data in the shared heap
* **--rpc** - inspect RPC registries or issue RPC calls from command line
* **--dbg** - debug or manipulate running apps from the command line

Heuristics to guide tooling: First, where feasible, every function available via built-in CLI tools should be accessible through applications. This might involve introducing 'sys.refl.conf.\*', 'sys.refl.cache.\*', and similar methods. Even 'sys.refl.cli.help' and 'sys.refl.cli.version' can be included. Second, we should ultimately aim to keep the glas executable small, assigning code bloat a significant weight.

## Glas Shell

A user configuration may define 'app.\*' at the toplevel namespace. In context of [notebook applications](GlasNotebooks.md) this should represent a live-coding projectional editor for the configuration file and its transitive dependencies. Users can run this as a normal application via `"glas --run . Args To App"`. 

A community can feasibly extend this notebook application to serve as a [shell](https://en.wikipedia.org/wiki/Shell_(computing)) for a glas system. Instead of running individual glas applications within the OS, and occasionally managing OS configuration files, users can treat the glas system as one big live-coded environment composed of applications and configurations. This 'shell' application may support multiple user interfaces via CLI, HTTP, and GUI.

In practice, we'll often favor instanced shells. Instancing might be implemented by copying or logically overlaying the configuration folder. A glas executable could have built-in support for instancing with `"glas --shell ..."`.

## Implementation Roadmap

The initial implementation of the glas executable must be developed outside the glas system. This implementation will lack many features, especially the optimizations that would let transaction loops scale beyond a simple event dispatch loop. Fortunately, simple event dispatch loops are still very useful, and we can fully utilize a system between FFI, accelerators, and sparks. We also have access to conventional threaded applications.

Ideally, we'll eventually bootstrap the glas executable within the glas system. Early forms of bootstrap could generate C or LLVM, but I hope to swiftly eliminate external dependencies and integrate relevant optimizations into the glas libraries.


