# Glas Module System

A Glas 'module' is a value defined externally, usually in another file. Glas modules do not export symbols or definitions in the general case, e.g. the value defined could be a function, a record of values, or binary.

*Note:* Futures or open records in a module's value cannot be assigned by the client of the module. They simply remain opaque and undefined.

## Files as Modules

Glas will flexibly interpret most files as modules.

The interpretation of a file into a value will depend on the file extension. The `.g` file extension is used for Glas language modules. The default behavior for all other file extensions is to return file content as a binary array. 

This behavior is configurable as part of the [Glas Language Extension](GlasLangExt.md) features. By defining a language module, developers may report errors, return structured data, or implement domain-specific languages. Because file-extensions are non-invasive, it's relatively easy to work with ad-hoc files written using other tools.

## Folders as Modules

Glas will also interpret most folders (aka directories) as modules.

The default interpretation of a folder is simply the closed record of contained modules. However, if the folder contains a 'public' module (e.g. a `public.g` file), the value from this module becomes the value of the folder. This enables folders to hide implementation details like any other module.

## Filesystem Names

Glas compilers should reject files and folders that are awkwardly or ambiguously named, with a suitable warning or error. This is heuristic. Developers are encouraged to favor short, simple names that would be good as symbols in a record value.

Files and folders whose names start with `.` are explicitly excluded as Glas modules. These can be leveraged for extraneous features - DVCS, caching, project management, ad-hoc compiler options, etc..

## Filesystem Dependencies

Glas does not support filesystem search paths, and forbids upwards references into parent directories. Thus, a file may only depend on modules defined the same folder, or upon distribution packages (see below).

One consequence of this design is that folders are effectively stand-alone projects. This is even true for subdirectories within a project, which may easily be copied into another project as a form of sharing and reuse outside of the package distribution model.

## Distribution Packages

Glas packages are essentially modules from the network instead of the filesystem. A package should be represented by a folder, and should contain documentation, tests, and other metadata - not just a public value.

Glas groups packages into distributions. A distribution has only one version of each package. Packages in a distribution should generally be typed, tested, and verified to work cohesively. After breaking changes, problems can be made visible and a developer could repair or remove broken packages.

This design simplifies configuration management and avoids many problems of conventional package managers.

Distributions will generally be maintained by a community or company, and favor DVCS-style tactics - fork, merge, pull request, etc. - for sharing and distribution. A Glas project might edit multiple packages and target multiple distributions, enabling verification of compatibility with multiple configurations.

*Note:* Glas assumes naming conflicts will be solved socially, e.g. by staking claim to a name or prefix within a popular community distribution.

## Distribution Journal

Desiderata for distributions:

* incremental downloads
* provider independence
* verifiable downloads
* immutable versioning
* efficient, atomic updates
* efficient fork and diff
* easy structure sharing
* easy to sign and secure

To achieve these properties, we could represent distributions via content-addressed, log-structured merge trie, similar to what I had developed for Awelon. But we can also use content-addressed (secure hash) references for file content, instead of inlining file content.

It seems to worthwhile to develop distribution metadata for robust sharing independent of current ad-hoc services and systems.

## Module and Package References

The syntax for module and package reference may vary based on language extensions. However, the Glas language syntax introduces `module` and `package` as keywords, binding a single symbol such that `module foo.bar(y)` is equivalent to `(module foo).bar(y)`. Modules and packages are separate namespaces.

## Namespace Management

Conventional language support importing symbols from a module into the lexical scope. For Glas, the equivalent is `let (a:, b:, c:, _) = module foo`, leveraging patterns (here `a:`  is short for `a:a`).

*Note:* Glas does not support direct import of symbols from a record value because it's too complicated to reason about the state of lexical scope without static evaluation or in the presence of optional record fields.
