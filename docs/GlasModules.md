# Glas Module and Package System

The Glas module and package system divides a programs into multiple files. Each module or package represents a first-class Glas value of arbitrary type. Glas defines keywords `module` and `package` to reference modules or packages by name.

## Files as Modules

Glas will flexibly interpret files as modules,  via the [Glas Language Extension](GlasLangExt.md) feature. The `.g` extension is used for the standard Glas language. Because file-extensions are non-invasive, Glas can be extended to process files produced by external tools, assuming they are not a bad fit for Glas semantics.

File extension is NOT part of the module name. That is, a `foo.x` file will be referenced as `module foo`. This ensures that the language used by a module is not visible to the client of that module.

## Folders as Modules or Packages

Any subfolder `foo/` is interpreted as a module. If a folder contains a `public` module, the value from that module becomes the value for the folder. Otherwise, the folder is interpreted as a closed record of contained modules, essentially reflecting the filesystem structure.

By design, folders cannot have external module dependencies. Folders depend only on internal structure and packages, thus are easy to share or review independently. Folders will also serve as the concrete representation for Glas packages.

## Filesystem Restrictions

Glas does not support arbitrary folder and file names. Names starting with `.` or containing `~` are explicitly elided to support common filesystem conventions. Otherwise, names should be suitable for use in Glas records - e.g. avoid use of spaces, upper-case characters, etc.. A Glas compiler may report a warning or error for problematic names, then leave the associated module or package undefined.

## GLAS_PATH

The simplest and most conventional approach to package management is the filesystem search path, defined by environment variable such as `GLAS_PATH`. This path will specify a sequence of locations to search for a package. To find a package, sequentially search each location. This also supports override and precedence.

Normally, locations are filesystem folders, and we'll simply search each for an appropriate subfolder. However, it is feasible to extend the concept of search paths with network locations and package distributions.

The weakness with this feature is that it becomes too difficult to control and manage as it scales.

## Nix Package Manager

A mature Glas system could easily have over ten thousand packages. Any given package may have several versions or variants. Some packages may update frequently, e.g. due to continuous deployment. Altogether, package systems present several challenges related to scale, caching, sharing, configuration management, and reproducible computation.

To solve these problems, we'll eventually want a package manager.

However, designing a good package manager is a lot of work and a significant project in its own right. Thus, until it proves inadequate, I propose to shove this effort to an existing system: the Nix package manager.

## Distributions

Another useful idea for managing packages is 'distributions'. A distribution contains one version of each package. This enables all packages in the distribution to be tested to work cohesively, and also provides a simple model for deployment: publishing to the distribution.

A distribution can be curated by a company or community. It's also a similar idea to Nix channels. A Nix channel might prove adequate for representing Glas distributions.

## Namespaces and Implementation Hiding

The Glas module system focuses on organization of a program into files, folders, and external packages. In contrast, conventional module systems cover additional responsibilities such as namespace management and implementation hiding.

In Glas, those responsibilities are pushed to the value and type system layers. The default Glas syntax will support ML-style signatures and structures. Abstraction is achieved through annotation and ascription, asserting values are observed through limited interfaces.
