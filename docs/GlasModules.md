# Glas Module System

The module system is an important part of a language's user experience.

A Glas 'module' is a value defined externally, usually in another file. Glas modules do not export symbols or definitions in the general case, e.g. the value defined could be a function or binary. But it is feasible to import symbols from closed records into lexical scope, like a conventional module system.

*Note:* Futures or open records in a module's value cannot be assigned by the client of the module. They simply remain opaque and undefined.

## Binary File Modules

For a subset of files, their 'value' is simply their content as a binary array. This ability to modularize binary data, without relying on filesystem effects, is convenient for partial evaluation, caching, DSLs, and distributing resources such as documentation or training sets for machine learning.

Which files are treated as binary data is determined by file extension. Files with a `.g` suffix use the Glas language, while all other files are valued by their binary content. To keep it simple, this is not configurable. However, it is feasible to process binary data at compile-time (via [Glas Language Extensions](GlasLangExt.md)).

File extensions are not included in module references. Thus, a developer may switch transparently between directly including a binary or computing it.

## Filesystem Directories

A Glas module is often represented by a directory containing files and subdirectories. 

The default value for a directory maps contained files and subdirectories into an open record. However, if the directory contains a 'public' module (usually a `public.g` file), the value for the directory becomes the value defined by this module, and other directory content is implicitly private.

*Note:* If filesystem names are problematic, e.g. if ambiguous or not portable, the compiler should issue a suitable warning or error. File and directory names starting with `.` are hidden and not considered part of a module. 

## Filesystem Dependencies

Glas restricts filesystem dependencies: a file may only depend on modules defined in the same filesystem directory, or distribution packages. Relevantly, Glas does not permit reference to arbitrary directories, and there is no filesystem search path like very many languages use.

This restriction simplifies reuse and sharing. A directory may easily be copied between projects, or evaluated as a stand-alone project, assuming compatible distributions.

## Distribution Packages

Glas packages are essentially modules from the network instead of the filesystem. A package will be concretely represented by filesystem directory. Packages should include documentation, unit tests, and other metadata in addition to the public value.

However, there are many problems with conventional package managers. It is challenging to find a set of package versions that work together. A package update can silently break other packages. It also can be difficult to work around an unresponsive package owner.

To mitigate these issues, Glas groups packages into 'distributions'. A distribution has one version of each package. The packages in a distribution can be typed, tested, and verified to work cohesively. If there are any problems, they will be visible. The developer can repair or remove broken packages after a breaking change.

A Glas development environment can be configured with more than one target distribution, enabling a project to be verified across multiple configurations, or even committed to multiple distributions.

*Note:* Because packages are shared by many distributions, naming conflicts are possible. Glas assumes these conflicts will be solved socially, e.g. by staking claim to a name or prefix in a popular community distribution.

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

I'm still contemplating syntax and aesthetics for module and package references. In the language extension layer, we can treat module and package access as methods.

One simple option is to treat it as a keyword, e.g. `module foo` would reference the local filesystem module, while `package foo` would reference the distribution package. The parser should bind these keywords with high precedence, such that `module foo.bar.baz` is equivalent to `(module foo).bar.baz`.

Another viable option is to model package and module references as an open record in lexical scope, e.g. `extern.package.foo` vs. `extern.module.foo`. This approach has an advantage of being close to the semantics - the values are simply out there, defined outside module scope. However, this would complicate parse-time analysis of module dependencies, because of stuff like `let m = extern.module; ... m.foo ...`.

For now, I'm inclined to favor `module foo` and `package foo`.




