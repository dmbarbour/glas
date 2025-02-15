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

My vision and intention is that end users mostly operate through user-defined operations. To fit the aesthetic, we must avoid cluttering the command line with runtime switches. Instead, we'll push most configuration options into application 'settings', a configuration file, and environment variables.

## Configuration

The glas executable starts by reading a `GLAS_CONF` environment variable and loading the specified file as a [namespace](GlasNamespaces.md). If unspecified, the default location is `"~/.config/glas/conf.g"` in Linux or `"%AppData%\glas\conf.g"` on Windows. 

The configuration namespace defines functions to evaluate runtime options such as persistent storage, caching, filesystem access, logging, etc.. The set of configurable features is ad-hoc and runtime-specific, albeit subject to de-facto standardization. Many options, such as logging, may be application-specific: the configuration can query application 'settings' when evaluating application-specific options. To simplify portability and security, application settings are never directly observed by the glas executable.

The configuration namespace may also define applications and shared libraries. A typical user configuration will import a community or company configuration from DVCS, then apply a few overrides for user-specific preferences, projects, and resources. A community configuration can define thousands of applications, mitigated by lazy loading and caching. Importing by specific tags or hashes in remote DVCS repositories serves as the foundation for curation, version control, and package management.

*Note:* The initial configuration file must use a recognized file extension with a built-in syntax, such as ".g" files. However, this is subject to the normal rules for user-defined syntax. If the configuration defines "env.lang.g", we'll attempt to bootstrap the language and reload the configuration. 

## Running Applications

The choice of '--run', '--script' and '--cmd' lets users reference applications in a few ways:

* **--run**: The application is already defined in the configuration namespace. To run "foo" we might access definitions such as "app.foo.settings", "app.foo.start", "app.foo.step", and so on. This translation may be configurable. A user can inherit from a community configuration that defines thousands of useful applications, loading them on demand.
* **--script**: The namespace model defines a 'load' operation. We 'load' the specified source (e.g. a file, package folder, or URL) in scope of a configurable '%env.\*' environment and primitive '%\*' AST constructors. The generated namespace should define application methods such as 'settings', 'start', 'step', and so on. 
  * **--script.FileExt**: Same as '--script' except we'll assume the given file extension in place of the actual file extension. Intended for use with shebang scripts in Linux, where file extensions are frequently elided.
* **--cmd.FileExt**: Treated as '--script.FileExt' for an anonymous, read-only file found in the caller's working directory. The assumed motive is to avoid writing a temporary file.

Prior to running the application, the glas executable might download sources, analyze types, evaluate tests, optimize, JIT-compile, and apply application-specific configuration options based on 'settings'. However, these efforts can be cached such that running the same application in the future is a lot faster.

To run a [transaction loop application](GlasApps.md), we'll evaluate first 'start', repeatedly evaluate 'step', and occasionally handle 'http' requests or other events in separate transactions. This model is especially friendly for live coding and distributed programming. 

However, the glas executable may support alternative run modes, e.g. staged applications, binary extraction, or a conventional 'main' procedure. The run mode should be expressed as an application-specific configuration option.

### Installing Applications

Users will inevitably want to 'install' applications to ensure they're available for offline use. 

        glas --install AppName*
        glas --uninstall AppName*
        glas --update

To simplify tooling, the configuration will specify a separate text file where installs are managed by 'glas --install' and 'glas --uninstall'. This can be paired with a computed list of installs defined within the configuration. When the user runs 'glas --update', the cache is maintained according to the current list of installs.

The proposed interface only supports applications defined within the configuration and referenced by name. It is feasible to extend this to scripts, referencing file paths or URLs. (I would not recommend installing '--cmd' text!)

### Shared Memo-Cache

Instead of manual caching, glas systems rely on cache annotations. We can easily [memoize](https://en.wikipedia.org/wiki/Memoization) application of pure functions to immutable data, e.g. using secure hashes as keys in a lookup table. We can design compilers to leverage memoization as a basis for incremental computing, and configure a persistent memoization to share work. This isn't restricted to user code, even JIT-compiled fragments of executable code can be memoized. 

It is feasible to share the memo-cache between users. Sharing introduces concerns related to authorization, authentication, and accounting. We might sign our results and specify whose signatures we trust. We could employ a [content delivery network](https://en.wikipedia.org/wiki/Content_delivery_network) to distribute larger results across many users. All of this amounts to extra configuration.

The glas system does not directly distribute executable code to users for running or installing applications. However, a shared memo-cache can serve a similar role. To fully leverage this, we might carefully control the implicit environment (shared libraries, compilers, etc.) visible to applications that we want to install easily, similar to Nix package manager.

## Command Shells and Interactive Development

I envision users eventually 'living within' a live-coded glas system. Instead of running fine-grained commands or applications, users would have a [projectional editor](GlasNotebooks.md) for a 'shell' that is running multiple component applications. Through the live coding interface, users could directly manipulate what is running.

## Built-in Tooling

The glas executable may be extended with useful built-in tools, insofar as they don't add much bloat. Some tools that might prove useful:

* `--conf` - inspect and debug the configuration, perhaps initialize one
* `--cache` - manage storage used for persistent memoization and incremental compilation
* `--db` - query, watch, or edit persistent data
* `--rpc` - inspect RPC registries, perhaps issue some RPC calls directly from command line
* `--app` - debug or manipulate running apps through runtime provided HTTP or RPC APIs

However, it is awkward to provide built-in tooling for application-specific resources, especially in context of '--cmd' and staged applications. To mitigate this, we might limit exactly how much choice applications have, e.g. select between three configured database options.

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
