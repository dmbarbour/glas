# Glas Namespaces

The glas namespace is the foundation for modularity, extensibility, metaprogramming, and version control of glas systems.

A user's view of a glas system is expressed as an enormous namespace importing from multiple files and DVCS repositories. In general, the user will import from some DVCS repositories representing a community, company, or specific projects of interest, then override some definitions for user-specific resources, preferences, or authorities. Working with a huge namespace is mitigated by lazy evaluation and caching.

## Procedurally Generated Namespaces

I propose to represent a glas namespace *procedurally*, i.e. a program iteratively writes definitions. The API is carefully restricted to simplify laziness, caching, and flexible evaluation order. To support metaprogramming of the namespace, the namespace procedure receives access to 'eval' within scope of the generated namespace. To support modularity, this namespace procedure may read stable data from an external environment.

Useful types:

        type Name = prefix-unique Binary, excluding NULL, as bitstring
        type Prefix = any byte-aligned prefix of Name, empty to full
        type TL = Map of Prefix to (Prefix | NULL | WARN), as radix tree
        type WARN = NULL 'w'                # 0x00 0x77, as bitstring
        type AST = (Name, List of AST)      # constructor
                 | d:Data                   # embedded data
                 | n:Name                   # namespace ref
                 | s:(AST, List of TL)      # scoped AST
                 | z:List of TL             # localization

A viable algebraic effects API:

* `ns.*` - common prefix for namespace ops
  * `write(Name, AST)` - write a definition. This is modified by prior move and link translations. 
  * `move(TL)` - apply translation to future defined Names (for 'write').
  * `link(TL)` - apply translation to future ASTs (for 'write' or 'eval').
  * `fork(N)` - returns non-deterministic choice of natural number 0..(N-1), basis for iteration.
  * `eval(AST, List of Arg) : [cb] Result` - interprets AST as the body of an anonymous procedure, providing a list of arguments and permitting interaction with the caller via generic callback handler 'cb' 
    * `cb : List of Arg -> [cb] Result` - generic, recursive callback handler, like remote procedure calls.
  * `read(Query) : Result` - access external environment with a cacheable Query, e.g. load a file.

Aliasing is expressed by defining one name to another, e.g. 'ns.write(Name1, n:Name2)'. These two names should be equivalent under evaluation within the program model (modulo reflection APIs). I assume 'eval' will often just use 'n:Name' for binding a definition.

## Evaluation Strategy

The 'ns.fork' effect supports non-deterministic choice as a basis for iteration and laziness. Logically, we repeatedly evaluate a namespace procedure as many times as necessary, each in a separate transaction. However, the output is monotonic, idempotent, and deterministic. There is no need to recompute a branch after it commits or aborts for any reason other than missing definitions. In practice, we might evaluate multiple branches in parallel and pause computation when awaiting a missing definition.

However, to ensure a deterministic outcome in case of ambiguity, we prioritize definitions from lower-numbered forks. Thus, we must not accept definitions from higher-numbered forks before all lower-numbered forks are finished or paused. Further, 'ns.move' translations restrict which definitions a branch can produce: we can lazily defer computation of branches if we determine that they do not produce definitions we need.

In case of long-running computations, we might heuristically garbage collect intermediate definitions. This can be understood as an aggressive form of dead-code elimination: we can eliminate definitions if they are not reachable from a 'rooted' definition (e.g. a public def in user config, or 'app.\*' def in a script) or a pending computation. The 'ns.link' translation determines what is reachable from a pending namespace computation. We can also garbage collect 'dead' namespace threads if we determine they'll be waiting forever. See *Namespace Processes and Channels* for a design pattern that relies on garbage collection.

We can begin processing definitions before a namespace is fully evaluated. We can apply typecheckers, optimizers, staged computing, check static assertions, and further compile abstract assembly to executable binary code. In context of 'ns.eval', concurrent processing is expected, but we can also reduce latency for starting an application, or garbage collect 'app.start' or a prefix to 'app.main' after we've started.

## Abstract Assembly

I call the Lisp-like AST encoding 'abstract assembly' because every constructor node is abstracted in the namespace. In contrast to concrete encodings like bytecodes, it is very convenient to extend, restrict, or redirect these names via 'link' translations.

We assume the system defines a set of 'primitive' constructor names, such as '%i.add' for arithmetic and '%seq' for procedural composition. The '%' prefix will simplify recognition and translation. Usually, we'll forward primitives through the namespace unmodified, so `import ... as foo` might use a TL similar to `{ "" => "foo.", "%" => "%" }`. 

We can evaluate AST to a canonical form by applying scope translations then simplifying localizations. Evaluation of a scope node involves rewriting or invalidating names and appending the list of translations to an internal scope node or localization.

## Translations

A translation is expressed as a prefix-to-prefix map. We'll find the longest matching prefix for a name on the LHS, and rewrite that to the RHS prefix. This allows for atomic swaps, e.g. `{ "a." => "b.", "b." => "a." }` would swap 'a.\*' and 'b.\*' in a single step. 

To cover a few additional cases, we permit NULL or WARN (NULL 'w') in place of the RHS prefix. These are context-dependent. For a move translation, NULL quietly removes names while WARN represents removal with a warning. For a link translation, NULL becomes a compile-time error, while WARN instead reports a compile-time warning and wraps the problematic code to raise an error at runtime. A user configuration can feasibly adjust this behavior.

Translations compose sequentially. Within a scoped AST, list of TL `[A, B, C]` will apply A, B, then C in sequence. However, we can evaluate this to one large translation. To compose 'A fby (followed-by) B' we first expand A to include redundant rules with longer suffixes on both sides such that the RHS of A matches every possible LHS prefix in B. Then we apply B's rules to the RHS of A. For example:

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes
            fby { "f" => "xy", "foo" => "z"}
        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # rewrite
        # Note that NULL and WARN are never rewritten under 'fby'.

We can further simplify the resulting TL by recognizing and removing redundant rules. For example, we don't need rule `"xy" => "zy"` if `"x" => "z"` exists. We don't need rule `"xy" => NULL` if `"x" => NULL` exists.

## Localizations

Localizations allow programs to capture a 'link' scope for deferred use in multi-stage programming. For example, we could support a primitive like `(%ast.eval Localization ASTDataExpr)` that will evaluate an AST under a specific view of the namespace. 

*Note:* Localizations are best used in context of compile-time computations. Any potential use of localizations at runtime will hinder dead-code elimination, lazy evaluation, static analysis, and convenient nice features.

## Prefix Uniqueness and Name Mangling

Names *should* be prefix-unique, i.e. no name is a prefix of another name. This constraint exists to esnure prefix-to-prefix translations (the TL type) can always uniquely translate a name. A violation won't cause any issues for the namespace per se, but is worth a warning. In practice, prefix uniqueness will be enforced by the front-end compiler rewriting names, e.g. escaping reserved characters and appending a ".!" suffix.

The proposed ".!" suffix serves prefix uniqueness, and further ensures every definition "bar.!" is implicitly part of a composite "bar.\*", which is very convenient for namespace extension and associative definitions. For example, we might annotate definitions with "bar.\#type" and "bar.\#doc" and so on.

## Namespace Macros and Eval

The 'ns.eval' operation is the basis for namespace macros. This operation interprets an AST argument as the body of an anonymous procedure. In addition to returning a result, the 'ns.eval' operation may interact with the caller through a generic callback handler.

In case of missing definitions, we can evaluate as far as possible without those definitions (in some cases, definitions are only conditionally required) then wait for missing definitions to be provided by other forks. This provides a basis for lazy evaluation or interactive computation between namespace 'threads'.

*Note:* I assume, for performance, that this interpreter is augmented with a just-in-time compiler.

## Modularity

The 'ns.read' operation provides a basis for modularity. All queries should be idempotent, commutative, and cacheable. For example, reading a local file, browsing a folder, access to a remote DVCS, or implicit parameters, is a viable basis for such a query.

Sample queries:

* `ns.read(file:Path) : opt Binary` - ask for content of a file. Optional result in case file does not exist. Diverges with a suitable error message for troublesome cases like permissions errors or irregular files.
* `ns.read(dir:Pattern) : List of Path` - ask for a list of files and subfolders matching a given pattern.
* `ns.read(dvcs:(repo:URL, read:Query)) : Result` - apply a query in context of a DVCS repo. Diverges if the DVCS cannot be accessed.
* `ns.read(env:Name) : opt Data` - extended set of implicit parameters. Use of '%env.\*' and 'ns.eval' more flexibly serves this role, but 'ns.read' may prove more convenient in many cases. Could bind to OS environment or runtime version info.

This API pushes most responsibility for managing relative file paths to the client, though this can be mitigated by systematic override of 'ns.read' within a controlled scope. I propose to enforce a *Folders as Packages* pattern in the syntax layer, as a default.

The user configuration can feasibly define an adapter for 'ns.read', especially useful for implicit parameters. This should be subject to bootstrap together with user-defined syntax, verifying a stable fixpoint is reached after a few iterations. 

*Note:* It is technically feasible to treat a configured shared heap database or HTTP GET as read sources. However, I'm not convinced it's a good idea. For now, I propose to stick with files and DVCS, and a few implicit parameters.

### User-Defined Syntax

To load a file into a namespace, we can 'ns.eval' a user-defined front-end compiler based on file extension. I propose '%env.lang.FileExt'. The generated namespace and abstract assembly serve as an [intermediate representation](https://en.wikipedia.org/wiki/Intermediate_representation). This supports user-defined syntax aligned with file extensions.

Aligning with file extensions simplifies integration with external tools. Graphical and textual programming can be freely mixed. A file-based database can generously be viewed as syntax. Arbitrary ".txt" or ".json" files can be treated as sources, perhaps defining 'data' yet subject to staging, partial evaluation, compile-time assertions. DSLs can be developed. Experimental language extensions can be scoped to a community or project through the namespace.

The glas executable provides at a built-in front-end compiler for [".glas" files](GlasLang.md) and perhaps others. Initially, 'env.lang.glas' will use the built-in until we can bootstrap a user-provided definition. Multiple built-ins compilers may be mutually bootstrapped, with the executable verifying a fixpoint after a few iterations.

*Note:* to translate FileExt to a glas name we lower case 'A-Z', replace ASCII punctuation by '-', add the '.!' suffix for prefix uniqueness. For example, file "foo.TAR.GZ" is processed by '%env.lang.tar-gz.!'. 

### Folders as Packages

I propose to forbid parent-relative ("..") and absolute file paths in context of processing files. Files should reference other local files in the same folder or subfolders, or remote files via DVCS. Thus, each folder is effectively location independent. The same would apply within DVCS repositories.

Further, a folder containing a "package" file (of any extension) will be recognized as a package folder. Instead of directly loading individual files, users load the package folder and we'll implicitly search for the package file and load that. If clients attempt to bypass the package file, we'll report a warning because doing so can hinder refactoring and maintenance.

User-defined namespace procedures can implement this restriction by overriding 'ns.read' and checking for package files. Usefully, this is easily integrated with the front-end compiler support for *User-Defined Syntax*. The glas executable will apply these restrictions in context of the user configuration, scripts, and any built-in front-end compilers. 

*Note:* Filesystem-layer links can work around restrictions on relative or absolute paths. I don't recommend this in general because it complicates sharing and distribution of code. But it's convenient for adding local projects to a user configuration.

### Linear Files

Reading a file more than once is not an error at the namespace layer in context of lazy loading and distinct compilation environments. Even cyclic file dependencies are possible. However, it's *probably* an error at other layers, suggesting use of a shared library, namespace macro, or copy for independent editing in a notebook application. I propose that, by default, a runtime reports a warning if a file is read more than once for any reason. The runtime can also support annotations or configuration options to suppress this warning for specific files, folders, or DVCS repos.

## Design Patterns

### Private Definitions

As a simple convention, we might assume the "~" prefix is reserved for private definitions used within the current file or other compilation unit. When importing files, a compiler might implicitly 'allocate' regions within its own private space, and translate private names from imports via renames such as `{ "~" => "~(Allocated)." }`. 

Translation based on a privacy prefix doesn't prevent a client from accessing private definitions of a module, but it does allow the client to more precisely control which definitions are shared between different subprograms. 

We might further add a weak enforcement mechanism: raise an error when a name containing "~" is directly used in a definition, or when a translation is not privacy-preserving. A translation is privacy-preserving if "~" in LHS prefix implies "~" or NULL in RHS. A compiler can arrange to introduce privacy only via translations.

### Implicit Environment

I propose to reserve "%env.\*" to serve as a read-only context. This piggybacks on the default rules for propagating primitives. To support this pattern, the glas executable will apply a default link rule `{ "%env." => "env." }`. The '%\*' space is read-only via move rule `{ "%" => WARN }`, but users can populate the initial environment by defining 'env.\*'. Beyond this, they may redirect the environment within a scope, e.g. adding linke rules such as `{ "%env.foo." => "my.foo." }`.

The implicit environment serves as the basis for shared libraries, user-defined syntax, efficient composition of applications, and user-provided feature flags to influence compilation. 

### Shared Libraries

Common utility code should be provided through the implicit environment, such as "%env.lib.math.\*". If a symbol isn't defined, the user would receive a clear error such as "no definition for env.lib.math.whatever". This is easily corrected by importing the library into the user configuration, or by loading it locally and redirecting links within a local scope.

Shared libraries do introduce versioning and maintenance challenges, i.e. an update to a library may break some applications and not others. This can be mitigated by localizing updates or by whole-system automated testing.

*Note:* Even without shared libraries, a compression pass can feasibly eliminate runtime overhead of loading the same utility code many times. But the shared library pattern avoids compile-time overheads and risk of accidental variation, e.g. via feature flags.

### Compiler Dataflow

Multimethods let users describe specialized implementations of generic operations across multiple modules, heuristically integrating them. A soft constraint-logic program might declare assumptions and preferences across multiple modules, then solve constraints holistically. Notebook applications should build a table of contents across multiple modules and robustly route proposed updates to their sources. Implementing these patterns manually is awkward and error-prone. Instead, I propose to push this to front-end compilers and libraries shared between them. 

As a simple convention, we reserve '@\*' names for compiler-supported dataflow between modules and their clients. Compilers can implement ad hoc, fine-grained dataflows through the namespace. In contrast, '%env.\*' is controlled by the user and supports only one dataflow pattern. The glas executable does not need to be aware of specific compiler dataflow patterns except insofar as they are embedded into built-in front-end compilers.

In some cases, dataflow computations require closure via specialized processing at the root source. Ideally, this is separate from the source code, such that we can compile any module as a root or further compose it. This can be supported by letting 'ns.read(env:"@")' return a set of flags (as a dict) describing compiler dataflows closed by the client.

A relevant concern with compiler dataflow is that it easily interferes with lazy loading. This is unavoidable, given we are integrating content orthogonally to loading modules. This can be mitigated by syntactic support for scoping and translating dataflows, and avoiding automation of dataflow where it confuses most users.

### Namespace Processes and Channels

*Note:* I don't have a practical use case in mind. At the moment, this pattern serves only as a demonstration of theoretical expressiveness.

A namespace process can be modeled in terms of a namespace procedure that 'yields' incremental output by forking to commit one branch and continue the other. A process may also fork to 'spawn' a subprocess. Processes interact by writing or awaiting definitions in turn, with 'eval' implicitly waiting. Although the outcome is deterministic, composition is more flexible than call-return, able to model protocols and negotiations.

Interaction of processes introduces many intermediate definitions. It is possible to garbage-collect these definitions, but a long-running process must *add* link rules to *remove* items from scope. Unless these rules simplify, this incurs linear overhead, thus is useful only for coarse-grained collection.

Link rules most easily simplify for *sequential* interactions, such as channels. Consider a variable-width numbering schema such as A000 to A999, B001000 to B999999, C001000000 to C999999999, and so on. Under simplification, rule `"A11" => NULL` replaces rules for A110 to A119. When processing item A123, six rules can remove A000 to A122 from scope: A0, A10, A11, A120, A121, A122. In general, the number of rules is the sum of digits plus the number of prior size headers (A, B, C, etc.). A binary encoding results in fewer rules.

Dynamic channels are readily supported. Message 'mA123' could inform the reader that the next subchannel, 'cA003.\*', is now in play for reading or writing. A few caveats: Simplifying requires tracking extra metadata because cA003 may remain in use long after cA009 is released. Depth is a concern: as we establish subchannels over subchannels over channels, names grow linearly like 'cA003.cA042.cA001.mA042'. Channels are second-class: the closest thing to passing a channel around is spawning a subprocess to lazily alias inputs to outputs, a 'wiring' pattern.

## Quotas

Divergence is a relevant concern. However, even if we could somehow guarantee termination in presence of 'eval', we can easily express computations that take far more time than we're willing to spend. Thus, we'll want quotas for evaluation of a namespace. Quotas could be expressed via user configuration and annotations.

To ensure a reproducible outcome, quotas must be based on a heuristic cost function. We might accumulate costs in a register, then check for overruns periodically (e.g. upon GC) and just before commit. But it should be deterministic whether or not any given fork of the namespace procedure commits.

## Failure Modes

Errors that abort a namespace procedure - assertion failures, quota constraints, etc. - are reduced to a warning, with evaluation proceeding on other 'ns.fork' branches. In case of invalid definitions - malformed AST, links to NULL or WARN - we can report appropriate compile-time warnings and arrange to diverge at runtime. After namespace computation halts (due to return and commit, abort on error, 'ns.eval' waiting on definitions, or lazily paused after 'ns.move') we implicitly invalidate missing definitions.

Ambiguous definitions are possible if the same name is assigned multiple distinct definitions. (Assigning the same definition many times is idempotent.) In context of lazy evaluation, it is awkward to treat ambiguity as an error. Instead, we raise a warning then deterministically prioritize the first definition from the lowest-numbered fork. If a lower-priority definition must be observed (via 'ns.eval') to compute the higher-priority version, we can report an additional warning (or error) for the priority inversion, then deterministically favor the observed definition for consistency. Either way, the outcome should be deterministic, and programmers should manually resolve ambiguity.

The heuristic policy for which namespace-layer errors or warnings should block applications from running should be configurable and application specific. That is, the runtime evaluates a configured function with access to the list of namespace errors and 'app.settings' to decide whether we abandon the effort. If we do run the application, this list of issues may also be visible through a 'sys.refl.ns.\*' API.
