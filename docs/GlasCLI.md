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

The glas executable will read a user configuration based on a `GLAS_CONF` environment variable, loading the specified file as a [namespace](GlasNamespaces.md). If unspecified, the default location is `"~/.config/glas/conf.glas"` in Linux or `"%AppData%\glas\conf.glas"` on Windows.

The configuration may directly define applications, shared libraries, and user-defined syntax under 'env.\*'. This configured environment is effectively the community and user space of the configuration. We'll translate '%env.\*' to 'env.\*' when compiling the configuration or scripts. This ensures scripts have access to shared libraries and can efficiently express composition of common applications.

A typical user configuration will import a community or company configuration from DVCS, which may define hundreds of applications and libraries under 'env.\*'. For performance, we rely on lazy loading, caching, and explicit cache control in terms of 'installing' applications or libraries. Effectively, a community configuration serves as a package distribution, while DVCS branches become the basis for curation and version control.

Definitions outside of 'env.\*' may be read by a runtime to determine where to store persistent state, where to publish RPC APIs, which ports to open for HTTP requests, which log channels to enable and where to record logs, and so on. A subset of these features may be application specific via opportunity to query application 'settings' when evaluating the option. For maximum portability, a runtime might first generate an application adapter based on application settings and runtime version info.

*Note:* The user configuration must be expressed in terms of a built-in syntax. However, if the configuration defines 'env.lang.FileExt' we'll attempt to bootstrap and reload the configuration under the user's definition. 

## Extension

The glas executable may also read a default configuration, e.g. looking for `"/etc/glas/conf.glas"`. This can provide default options for runtime features, or extend the set of built-in compilers by defining 'env.lang.FileExt'. However, there should be no direct entanglement with the user configuration: the user configuration cannot *reference* definitions in the default configuration.

Akin to extending the set of built-in compilers, we can feasibly treat this default configuration as a sort of extension to the 'glas' executable, perhaps defining new '--operations' for the command line in a runtime-specific manner.

## Running Applications

The choice of '--run', '--script' and '--cmd' lets users reference applications in a few ways:

* **--run**: To run 'foo', the runtime will compile 'env.foo.app.settings' and 'env.foo.app.step' and related definitions in the configuration namespace. A community configuration might define hundreds of applications to be lazily downloaded, cached, and compiled on demand.
* **--script**: We 'load' an indicated file, folder, or URL as a namespace (in context of '%\*'). This script should define 'app.settings', 'app.step', and related methods.
  * **--script.FileExt**: Same as '--script' except we use the given file extension in place of the actual file extension for purpose of user-defined syntax. Mostly for use with shebang scripts in Linux, where file extensions may be elided.
* **--cmd.FileExt**: Treated as '--script.FileExt' for an anonymous, read-only file found in the caller's working directory. The assumed motive is to avoid writing a temporary file.

A runtime can support multiple run modes. A [transaction loop application](GlasApps.md) will define transactional methods such as 'start', 'step', and 'http'. A staged application might express 'build' to compile another application based on command line arguments, OS environment variables, and access to the runtime filesystem.

Every application should define 'settings'. This should mostly be used as a pure `Data -> Data` function that accepts ad hoc queries to guide integration. The runtime does not observe settings directly. Instead, it asks the configuration for an adapter based on application settings and runtime version info. As an extreme case, an adapter can generate definitions entirely from settings.

## Installing Applications

Users will inevitably want to install some applications to ensure they're available for offline use. This might be supported through an installer extension on the CLI. Perhaps we take inspiration from Debian's 'apt':

        glas --apt install AppName*
        glas --apt remove AppName*
        glas --apt update
        glas --apt upgrade

To simplify tooling, the configuration might specify a separate file to track which applications are installed. This allows us to easily modify and share installs separate from the main configuration sources. The configuration may also specify a set of default installs that must be explicitly removed. 

*Note:* It is feasible to extend this API to install scripts, referencing file paths or URLs.

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
* `--trust` - tools to manage trust of scripts or app providers, adding, removing, or inspecting roles

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
