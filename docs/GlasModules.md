# Glas Module System

A Glas module or package is a value defined outside the current file. Glas distinguishes 'module' as a value defined within the same folder, while a 'package' is a value defined non-locally, e.g. from the network.

Glas defines keywords `module` and `package` to access modules or packages by name. Thes keywords bind tightly, such that `module foo.bar` is equivalent to `(module foo).bar`. 

## Files as Modules

Glas will flexibly interpret most files as modules.

The interpretation of a file into a value depends on the file extension. The `.g` file extension is used for Glas language modules. The default behavior for other file extensions is to return the file's content as a binary array. This behavior is configurable via the [Glas Language Extension](GlasLangExt.md) features. 

Because file-extensions are non-invasive, Glas can often be extended to compile files produced by other tools into structured data or similar. Of course, not all file types are suitable.

## Folders as Modules

Glas will interpret most folders (aka directories) as modules.

The default interpretation of a folder is simply the closed record of contained modules. However, if the folder defines a `public` module, the value from this module becomes the value of the folder. This enables folders to hide implementation details like any other module.

*Note:* Files and folders whose names start with `.` or contain `~` are explicitly excluded as Glas modules. A compiler may warn or reject other names being awkward or ambiguous.

## Package Managers

A mature Glas system is likely to involve thousands of packages, with many versions per package. Thus, we must share dependencies where feasible, avoid unnecessary downloads, and ensure all the packages work together - compatible types and successful integration tests.

The Nix package manager seems a good fit for Glas. Like Glas, Nix views packages as values. I would like to support use of Nix with Glas. This may benefit from some compiler support regarding how packages are located within the filesystem.

## Distributions

A distribution is a collection of packages, containing one version of each package. A benefit of distibutions is that they greatly simplify health metrics: all packages within the distribution can be typed and tested to work cohesively, and developers can test proposed updates against multiple target distributions.

The primary challenge of distributions is their massive scale. A mature distribution with ten thousand packages is too large for a normal user to download. It may be feasible to model distributions as metadata over versioned packages.

## Managing Namespace

Programmers are generally encouraged to declare a local name, e.g. `let f = module foo`, both for shorthand and to avoid repeating themselves. If there are many repetitive module and package imports, developing an aggregation module or language extension could reduce boilerplate.

