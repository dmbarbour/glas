# Glas Module and Package System

The Glas module and package system divides a programs into multiple files. Each module or package represents a first-class Glas value of arbitrary type. Glas defines keywords `module` and `package` to reference modules or packages by name.

## Files as Modules

Glas will flexibly interpret files as modules,  via the [Glas Language Extension](GlasLangExt.md) feature. The `.g` extension is used for the standard Glas language. Because file-extensions are non-invasive, Glas can be extended to process files produced by external tools, assuming they are not a bad fit for Glas semantics.

File extension is NOT part of the module name. That is, a `foo.x` file will be referenced as `module foo`. This ensures that the language used by a module is not visible to the client of that module.

## Folders as Modules or Packages

Any subfolder `foo/` is interpreted as a module. If a folder contains a `public` module, the value from that module becomes the value for the folder. Otherwise, the folder is interpreted as a closed record of contained modules, essentially reflecting the filesystem structure.

Folders cannot have external *module* dependencies. Folders depend only on internal structure and external packages, thus are easy to share or review independently. 

Folders also serve as the concrete representation for Glas packages. That is, even if a package is a singular file, it must be represented as `pkgname/public.g` or similar. A package folder should include licensing, documentation, and other metadata.

## Filesystem Restrictions

File or folder names starting with `.` or containing `~` are excluded as potential modules or packages. Awkward names, e.g. containing spaces or unusual punctuation, may require special quotation or escapes, and might raise a warning.

## Package Search Path

A simple and very conventional approach to package management is a filesystem search path. This path could be defined by environment variable such as `GLAS_PATH`, a configuration file such as `~/.glas/packages.g`, or a command line parameter to the compiler.

The search path will specify a sequence of locations to search for a package. To find the package, each location is checked sequentially and the first instance of a named package has precedence. These locations may include filesystem locations and network repositories.

In Glas, a package is represented as a folder of the same name. That is, a `package foo` shall correspond to a folder `foo/` somewhere in this search path.

## Package Manager

Mature languages easily scale to over ten thousand packages, which may vary wildly in size from a few kilobytes to hundreds of megabytes. This will require a good package system to manage.

At this time, I don't intend to reinvent the package manager. Instead, I will recommend Nix or Guix package managers, which are designed to support reproducible rebuilds, per-user configurations, caching, and sharing between similar configurations. Further, Nix and Guix feasibly capture the entire bootstrap process.

## Live Data Packages

Unlike conventional systems, a goal of Glas is to support a 'living' system where packages may include a great deal of data - almanacs, encyclopedias, open street maps, sounds and materials, etc.. It is feasible to distribute databases across multiple files or packages.

To support this, we'll need a package manager with effective support for incremental uploads and downloads, lazy loading of package dependencies (perhaps even individual files) based on partial evaluation, and effective support for caching between continuous builds.

I'm uncertain whether the Nix and Guix package managers are suitable for these roles. If not, it may be feasible to either extend them further, or eventually develop a package manager more suitable for this vision.

## Cyclic Dependencies

Cyclic dependencies between packages or modules are currently disallowed and will raise an error. However, if there is a very strong use case, which isn't better solved by explicit use of open recursion and fixpoints, this stance may be reconsidered in the future.

## Namespaces and Implementation Hiding

Glas modules and packages represent arbitrary values. All other features of conventional module systems, such as implementation hiding or namespace management, must be achieved through the value and type-system layers. Glas will support ML-style signatures and structures as values, which can provide an initial basis for first-class modularity within the program.
