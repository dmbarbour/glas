# Glas Module System

A Glas module or package is a value defined outside the current file. Glas distinguishes 'module' as a value defined within the same folder, while a 'package' is a value defined non-locally, e.g. from the network.

Glas defines keywords `module` and `package` to access modules or packages by name. Thes keywords bind tightly, such that `module foo.bar` is equivalent to `(module foo).bar`. 

## Files as Modules

Glas will flexibly interpret most files as modules.

The interpretation of a file into a value depends on the file extension. The `.g` file extension is used for Glas language modules. Behavior for other extensions is configurable via [Glas Language Extension](GlasLangExt.md) features. 

Because file-extensions are non-invasive, Glas can be extended to compile files produced by other tools into structured data or similar. Of course, not all file types are suitable for Glas semantics. But returning the binary is also a valid option.

## Folders as Modules

Glas will interpret most folders (aka directories) as modules.

The default interpretation of a folder is simply the closed record of contained modules. However, if the folder defines a `public` module, the value from this module becomes the value of the folder. This enables folders to hide implementation details like any other module.

*Note:* Files and folders whose names start with `.` or contain `~` are explicitly excluded as Glas modules. A compiler may warn or reject other names being awkward or ambiguous.

## Package Managers

A mature Glas system will have thousands of packages, with multiple versions of each package. This environment presents a significant challenge for configuration management, reproducible computation, and effective sharing between users. 

The Nix package manager seems a good fit for Glas. Like Glas, Nix views packages as values with purely functional computation. I propose to adapt Nix as the initial package manager for Glas. If necessary, we can develop tooling to make this more convenient for the user.

An advantage of the Nix package manager is that we can version not just a Glas package but also the entire ecosystem for bootstrapping and compiling it.

## Distributions

A distribution contains one version of each package. Distibutions simplify deployment, maintenance, and provide a basis for community. Users can subscribe to a distribution for updates, and developers can verify by types and tests that all packages within a distribution work cohesively.

The challenge of distributions is their massive scale. There are too many packages for a user or developer to download. At most, it is feasible to download metadata about the available packages.

In context of Nix, distributions correspond roughly to Nix channels, git repositories of package metadata and associated with cached binaries. It may be feasible to build on Nix channels directly. 

## Managing Namespace

Programmers are generally encouraged to declare a local name, e.g. `let f = module foo`, both for shorthand and to avoid repeating themselves. If there are many repetitive module and package imports, developing an aggregation module or language extension could reduce boilerplate.

