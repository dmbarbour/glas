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

The main challenge is: distributions are HUGE.

Envision the mature distribution: Communities and companies have been pushing and pulling packages for a couple decades now. There are somewhere over ten thousand packages. Old versions of many packages are stuck in closed modules. There are wikis and websites, music and memes, with the Glas repository being used by some projects as a 'smart' filesystem.

## Distribution Filesystem (GlaDFS)

Glas distributions eventually require a suitable filesystem. This is a relatively low priority while Glas is young, but it might be a useful project by its own merits.

Some Desiderata:

* decentralized storage
* incremental downloads
* provider independence
* aggressive structure sharing
* efficient diffs and edits
* atomic updates and snapshots
* deep history and metadata

Common filesystems are missing all of these features. And they often have other features that aren't particularly useful for Glas distributions and DVCS development patterns, such as access-control security.

I believe that a suitable filesystem can be developed based on content-addressed log-structured merge trie, with a rope data structure for file content.

Proposed representation:

        HEADERS - choice of the following
        /prefix     - define prefix, followed by BODY
        :symbol1    - define symbol1, followed by BODY
        ~symbol2    - delete symbol2

        BODY - sequence of following lines
        %secureHash - include fragment as binary
        @secureHash - include fragment as BODY
        .text       - include text including LF
        !text       - include text excluding LF

This representation is human readable for accessibility and debugging. Secure hashes would be represented in base64url. For binary files, we'll need to use `%secureHash` fragments. 

This representation is managed by machine. If there are too many definitions in the fragment, we use `/prefix` to move them. If definitions are oversized, we use `@secureHash` to build a rope or finger-tree. We assume prefixes won't be too long.

This representation would be leveraged with a FUSE or WinFSP service to recover the conventional filesystem interface and support the network layer.

## Managing Namespace

Programmers are generally encouraged to declare a local name, e.g. `let f = module foo`, both for shorthand and to avoid repeating themselves. If there are many repetitive module and package imports, developing an aggregation module or language extension could reduce boilerplate.

