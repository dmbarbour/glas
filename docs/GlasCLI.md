# Glas Command Line Interface

The CLI shall support built-in operations distinguished by '--' prefix:

        glas --run AppName Args To App
        glas --script SourceRef Args To App
        glas --script.FileExt FilePath Args To App
        glas --cmd.FileExt "Source Text" Args To App 
        glas --conf ConfigOp
        glas --cache CacheOp
        ... etc. ...

Additionally, the CLI shall support ad hoc user-defined interfaces:

        glas opname Args

This simply asks the user configuration what to do. It should be feasible to reimplement all the built-ins as user-defined interfaces. But the built-ins are there even when the configuration is broken or confused.

## Running Applications

Applications can be defined in the user configuration or as separate script files. See *Configuration* in [glas design](GlasDesign.md). See [glas applications](GlasApps.md) for details on how applications are defined. 

* **--run AppName**: Refers to 'env.AppName.app' defined in the configuration namespace.
* **--script FilePath**: Loads specified file as a glas module in context of the configured environment. This module should define an application at 'app'.
  * **--script.FileExt FilePath**: as '--script' except we select the front-end compiler based on a given file extension, ignoring actual extension. Mostly intended for Linux shebang lines so we can elide file extension.
* **--cmd.FileExt SourceText**: as '--script.FileExt' except we directly provide script text on the command line. In this case, we treat the current working directory as our 'location' for relative file paths.

## Installing Applications

In the general case, running an application may load and compile files from remote DVCS repositories. Performance is mitigated by laziness, parallelism, and caching. Eventually, we may also support proxy compilation and caching, sharing work within a community. But we also need a robust solution for offline use. This might be expressed in terms of configuring a list of applications and libraries that should remain in cache. Then we can develop tools to manage this list and apply changes.

## Fine-Grained Security

I have an idea for fine-grained (function-level) security that I'd like to explore. Of course, we can use the more conventional, coarse-grained security mechanisms in the meanwhile.

When loading a configuration (or script), we can heuristically peek into `".pki/"` subfolders, searching for signed manifests and certificates. The user configuration may specify trusted root sources and with which authorities (like FFI vs GUI) they're trusted. Annotations within glas programs may attenuate trust. For example, a GUI API can be implemented using FFI. We must check that the code using FFI is trusted to do so, but it might permit use the API to any caller that has GUI authority.

Further, we can integrate patterns based on object-capability security. For example, instead of requiring GUI authority for every GUI API call, a method to 'open' GUI may return abstract data that serves as an unforgeable bearer token. Other GUI API methods then allow anyone to call, regardless of PKI trust, contingent on providing the bearer token. In theory, this could still be enforced statically by a type system.

This design applies to both configured applications and external scripts. That is, we don't trust an application merely because it has infiltrated a curated community configuration; we still authenticate signatures. But even untrusted scripts can do useful work if they limit themselves to a trusted sandbox.

## Built-in Tooling

Other than running applications, some tools I want from the CLI include:

- reflection on configurations (and scripts), e.g. listing definitions and types.
- cache management. How much storage, and by whom? Time spent vs storage size?
- access to browse and manipulate the configured persistent data layer
- browsing available RPC APIs

Ideally, any tool users can develop with built-ins should also be something users can implement via 'sys.refl.\*' APIs. And it's preferable to implement tools within the configuration if they would significantly increase executable size.

## Dynamic Runtime Options

I aim to avoid any command-line clutter from runtime options. Instead, the glas CLI executable may recognize ad hoc `GLAS_*` environment variables for dynamic runtime configuration. Use cases might include configuring the number of worker threads, tweaking GC triggers, adjusting debug verbosity, enabling an experimental optimization.

