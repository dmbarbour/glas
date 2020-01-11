# Glas Module System

A Glas module or package is a value defined externally.

## Terminology: Modules, Packages, Distributions, Projects

Glas distinguishes 'module' as a value defined within the same folder, while a 'package' is generally a value defined non-locally, e.g. from the network. 

Glas organizes packages into distributions. A distribution has only one version of each package, enabling packages to be typed, tested, and verified to work cohesively. A distribution will often be maintained by a community or company, using DVCS patterns such as forks and pull requests.

A project is a set of packages under development by a programmer or small team. A project is usually much smaller than a distribution, but will often be targeted at multiple distributions, and thus require concurrent testing.

## Module and Package References

Glas defines keywords `module` and `package` to access modules or packages by name. Thes keywords bind tightly, such that `module foo.bar` is equivalent to `(module foo).bar`. 

## Files as Modules

Glas will flexibly interpret most files as modules.

The interpretation of a file into a value depends on the file extension. The `.g` file extension is used for Glas language modules. The default behavior for other file extensions is to return the file's content as a binary array. This behavior is configurable via the [Glas Language Extension](GlasLangExt.md) features. 

Because file-extensions are non-invasive, Glas can often be extended to compile files produced by other tools into structured data or similar. Of course, not all file types are suitable.

## Folders as Modules

Glas will interpret most folders (aka directories) as modules.

The default interpretation of a folder is simply the closed record of contained modules. However, if the folder defines a `public` module, the value from this module becomes the value of the folder. This enables folders to hide implementation details like any other module.

*Note:* Files and folders whose names start with `.` or contain `~` are explicitly excluded as Glas modules. A compiler may warn or reject other names being awkward or ambiguous.

## Closed Modules

As another special case, a Glas folder may define a `packages` module. The compiler will recognize this, and use the module as the implicit distribution within the folder. That is, `package foo` refers to the local `module packages.foo` instead of an external distribution. 

This feature supports true stand-alone modules, having no external dependencies.

## Distributions

A Glas distribution is concretely represented by a closed module where the packages module is represented by a subfolder.

        dist/packages/packagename

The `dist/` directory serves as a convenient dumping ground for distribution metadata - authorship, licensing, readmes, discussion, digital signatures, index, cache, etc.. Distributions are also closed modules, and thus may define a `public` value in their role as a module.

## Distribution Package Managers

Mature distributions will grow very large, over ten thousand packages. However, for the normal user or programmer, downloading the entire distribution is unnecessary, a waste of network and storage.

So we can implement a package manager. This software will fetch user-selected packages and their transitive dependencies, alert users to updates, and automatically download updates in the background. The difference from a normal package manager is that packages would be managed in configured distributions, which simplifies configuration concerns.

*Note:* Filesystem support for deduplication would further improve support for distributions, especially in context of closed modules.

## Managing Namespace

Programmers are generally encouraged to declare a local name, e.g. `let f = module foo`, both for shorthand and to avoid repeating themselves. If there are many repetitive module and package imports, developing an aggregation module or language extension could reduce boilerplate.

