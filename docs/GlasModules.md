# Glas Module and Package System

Like many languages, Glas partitions large programs into files and folders, and larger projects into shared packages. Glas treats local files and folders as 'modules' while external values are 'packages'.

## Module and Package Values

In Glas, modules and packages represent first-class values of arbitrary type. Typically, this will be a structure with existential types. But there is nothing to prevent a module from representing a number, binary, or other value.

Namespaces within each module will be modeled explicitly by the Glas parser combinators. Thus, modules are entirely separate from the namespace concept.

## Files as Modules

Glas will flexibly interpret files as modules, using the [Glas Language Extension](GlasLangExt.md) feature to parse each file into a value. Because syntax is determined by non-invasive file-extension, Glas can reference many resources produced by external tools (e.g. treat a `.txt` file as a string value), though there are limitations.

The file extension is excluded from the module name. Thus, a `foo.x` file will be referenced as `foo` in the appropriate context (e.g. `module foo`). This supports a limited form of implementation hiding: the syntax of a module is hidden from its clients.

## Folders as Modules

A folder `foo/` is interpreted as a module. If a folder contains a `public` module, the output from that module becomes output for the folder. Otherwise, the folder's value will be a record of modules defined within the folder, effectively reflecting the structure of the folder.

*Note:* Module references within the filesystem are always limited to the same folder. Consequently, it is easy to copy a folder for reuse in another project. 

## Filesystem Restrictions

File or folder names starting with `.` or containing `~` are excluded as potential modules or packages. Module names must be unambiguous: if we have both a `foo.x` file and a `foo/` folder, that will result in a compile-time error. For portability, file names also must not be case-sensitive. Awkward names, e.g. containing spaces or problematic punctuation, may require quotes or escapes to reference and will raise a warning.

## Package Dependencies

Packages represent external dependencies shared between projects. Packages in the filesystem will generally be represented by folders discovered via `GLAS_PATH` environment variable search.

At this time, Glas is not trying to reinvent the package manager. However, use of Nix or Guix package managers, or another package manager designed for scalability and reproducible software, is very strongly recommended. 

## Acyclic Dependencies

The Glas compiler shall reject cyclic dependencies between modules or packages.

Glas is a graph-based language. Support for cycles would fit nicely with semantics. However, limiting cycles makes it easier to extract and reuse behaviors in a new context, or edit behavior for experimentation, and greatly simplifies incremental compilation. To improve user control, cycles are discouraged at these layers.
