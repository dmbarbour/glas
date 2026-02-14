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

In this case, the configuration will define some rule under 'glas.\*' to decide what to do, perhaps returning an application to run based on opname and Args. It should be feasible to reimplement all the built-in operations as apps, though perhaps requiring a little reflection ('sys.refl.\*').

## Configuration Files

See the [design doc](GlasDesign.md) for general overview of configurations. 

The CLI expects `GLAS_CONF` environment variable to specify a configuration file. If undefined, default file location is `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows. 

*Note:* Configurations have the same restrictions against importing parent-relative ("../") and absolute file paths as other glas modules. On one hand, this makes it easy to backup or share configurations. On the other, configurations cannot integrate local user projects without moving them to the configuration folder or an intermediate DVCS repo.

## Running Applications

Applications can be defined in the user configuration or as separate script files. See *Configuration* in [glas design](GlasDesign.md). See [glas applications](GlasApps.md) for details on how applications are defined. 

* **--run AppName**: Refers to '%env.AppName.app' as defined in the configuration.
  * **--run-config AppName**: refers to 'AppName', i.e. full name is required
* **--script FilePath**: Loads file as a glas module in context of the configuration's final '%\*' environment. This module should define an application at 'app'.
  * **--script.FileExt FilePath**: as '--script' except we select the front-end compiler based on a given file extension, ignoring actual extension. Mostly intended for Linux shebang lines so we can elide file extension.
* **--cmd.FileExt SourceText**: as '--script.FileExt' except we directly provide script text on the command line. In this case, we treat the current working directory as our 'location' for relative file paths.

## Runtime Tuning

I intend to avoid command-line clutter from runtime tuning, such as number of worker threads, GC triggers, debug verbosity, or experimental optimizations. Instead, these should be supported through ad hoc 'glas.\*' configuration options and `GLAS_*` environment variables.

## Installing Applications

In the general case, running an application may load and compile files from remote DVCS repositories. Performance is mitigated by laziness, parallelism, and caching. Eventually, we may also support proxy compilation and caching, sharing work within a community. But we also need a robust solution for offline use. This might be expressed in terms of configuring a list of applications and libraries that should remain in cache. Then we can develop tools to manage this list and apply changes.

## Built-in Tooling

Other than running applications, some tools I want from the CLI include:

- reflection on configurations (and scripts), e.g. listing definitions and types.
- cache management. How much storage, and by whom? Time spent vs storage size?
- access to browse and manipulate the configured persistent data layer
- browsing available RPC APIs

Ideally, any tool users can develop with built-ins should also be something users can implement via 'sys.refl.\*' APIs. And it's preferable to implement tools within the configuration if they would significantly increase executable size.

## Fine-Grained Security

I have an idea for fine-grained security that I'd like to explore. We can use conventional security mechanisms in the meanwhile.

When loading files, we can heuristically peek into `".pki/"` subfolders, collecting signed manifests and certificates. The user configuration may specify trusted signators and with which features (like FFI vs GUI) they're trusted. Annotations within glas programs (and within certificates) may attenuate trust. For example, a GUI API can be implemented using FFI. We must check that the code using FFI is trusted to do so, but it might permit use the API to any caller that has GUI authority.

Further, we can integrate patterns based on object-capability security. For example, instead of requiring GUI authority for every GUI API call, a method to 'open' GUI may return abstract data that serves as an unforgeable bearer token. Other GUI API methods then allow anyone to call, regardless of PKI trust, contingent on providing the bearer token. In theory, this could still be enforced statically by a type system.

This design applies to both configured applications and external scripts. That is, we don't trust an application merely because it has infiltrated a curated community configuration; we still authenticate signatures. But even untrusted scripts can do useful work if they limit themselves to a trusted sandbox.
