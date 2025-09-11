# Glas Namespaces

The glas namespace is the foundation for modularity, extensibility, metaprogramming, and version control of glas systems.

A user's view of a glas system is expressed as an enormous namespace importing from multiple files and DVCS repositories. In general, the user will import from some DVCS repositories representing a community, company, or specific projects of interest, then override some definitions for user-specific resources, preferences, or authorities. Working with a huge namespace is mitigated by lazy evaluation and caching.

## Procedurally Generated Namespaces

I propose to represent a glas namespace *procedurally*, i.e. a program iteratively writes definitions. The API is carefully restricted to simplify laziness, caching, and flexible evaluation order. The namespace supports metaprogramming and modularity via 'ns.eval' and 'ns.read' respectively.

Viable types:

        type Name = strings, binaries excluding NULL or C0
        type Prefix = any prefix of Name, empty to full name
        type TL = Map of Prefix to (Prefix | NULL | WARN), as radix tree dict
        type WARN = NULL 'w'                # 0x00 0x77
        type AST = List of AST              # constructor
                 | d:Data                   # embedded data
                 | n:Name                   # namespace ref
                 | z:List of TL             # localization
                 | s:(AST, List of TL)      # scoped AST, lazy translate

A viable algebraic effects API:

* `ns.*` - common prefix for namespace ops
  * `write(Name, AST)` - write a definition. This is modified by prior move and link translations. 
  * `move(TL)` - apply translation to future defined Names (for 'write').
  * `link(TL)` - apply translation to future ASTs (for 'write' or 'eval').
  * `read(Query) : Result` - access external environment with a cacheable Query, e.g. load a file.
  * `eval(AST) : [cb] Result` - tentative; interprets AST as the body of a procedure in the current 'link' context. Limited interaction with the caller through a callback handler.
    * Alternatively, we can feasibly support macro handlers with more flexible bindings. Depends on the program model.
  * `fork(N)` - tentative; non-deterministic choice if not supported via primitive. But this will probably be a primitive.

Aliasing can be expressed by defining one name to another. Those two names should then be equivalent under normal evaluation or execution contexts.

*Note:* The above provides a rough idea, but is subject to change to better integrate with the program model. For example, we might want something more flexible for binding 'eval' results and callbacks based on a static argument.

## Abstract Assembly AST

The AST representation is an intermediate structure used within the namespace. Essential requirements:

* Precise identification of names for robust rewrite and translate names. (n)
* Efficient embedding of data that does not contain names, i.e. constants. (d)
* Precise capture of context, e.g. to support staged metaprogramming. (z)
* Primitive constructor applications as names for restriction and extension.

The scope rule 's' is not essential, but is convenient for performance and to avoid repeating translation logic within front-end compilers. 

## Primitive AST Constructors

In practice, most AST constructors should start with a name. The [program model](GlasProg.md) shall specify a set of primitive constructor names, by convention prefixed with '%' such as '%seq' or '%i.add'. This convention supports efficient propagation of primitives via TL rules such as `{ "" => "foo.", "%" => "%" }`. Piggybacking on this convention, we'll often integrate shared libraries and script-like access to installed applications via '%env.\*'. See the *Shared Libraries* pattern, below.

The program model may support user-defined constructors. If the first AST in the constructor is not a name, we'll attempt to interpret it as the definition body of an anonymous user-defined constructor. An empty constructor, meanwhile, is treated as undefined. See note on *Regarding User-Defined Constructors* later.

## Notes on Evaluation

Non-deterministic choice serves as a basis for concurrency. Logically, we run the namespace procedure an infinite number of times, covering all possible sequences of non-deterministic choices. In practice, we clone a procedure's continuation at each choice and evaluate branches in parallel.

Use of 'ns.move' serves as the basis for laziness. Relevantly, 'ns.move' constrains future outputs from a given thread. Thus, if we do not need at least one of those outputs, we can capture the thread's continuation and defer execution until an output is needed. After we generate all definitions transitively needed by a given application, we can simply drop any remaining continuations.

Output is iterative. Logically, we '%yield' just before returning from 'ns.write'. Thus, even if a thread later diverges with an error or infinite loop, we can utilize definitions generated prior to that error.

The partial namespace may be observed through 'ns.eval'. When definitions are missing, further evaluation will implicitly wait for those definitions. It is possible to encode interactions between concurrent forks through the namespace, such that each thread waits on definitions and writes definitions in turn. See *Namespace Processes and Channels* for such a design pattern. However, best practice is to avoid unnecessary entanglements.

There are two troublesome failure modes: nontermination and ambiguity. For non-termination, resolution involves configured quotas and debuggers. Ambiguity requires more attention.

Writing the same definition more than once is idempotent. If a word receives multiple definitions, we'll report an ambiguity warning. A warning, not an error, because we don't want to break laziness just to search for ambiguous definitions. Instead, we'll aim to resolve ambiguity deterministically, favoring 'leftmost' choices and 'earliest' definitions from a thread. If we are forced to observe a definition (via 'ns.eval') before generating a higher-priority definition, we'll insist the observed and final definitions are identical.

*Note:* As definitions are generated, many more processes may apply such as typechecking and optimization. This can proceed concurrently with evaluation of the namespace.

## Translations

A translation is expressed as a prefix-to-prefix map. We'll find the longest matching prefix for a name on the LHS, and rewrite that to the RHS prefix. This allows for atomic swaps, e.g. `{ "a." => "b.", "b." => "a." }` would swap 'a.\*' and 'b.\*' in a single step. 

To cover a few additional cases, we permit NULL or WARN (NULL 'w') in place of the RHS prefix. These are context-dependent. For a move translation, NULL quietly removes names while WARN represents removal with a warning. For a link translation, NULL becomes a compile-time error, while WARN instead reports a compile-time warning and wraps the problematic code to raise an error at runtime. A user configuration can feasibly adjust this behavior.

Translations compose sequentially. Within a scoped AST a list of TL `[A, B, C]` will apply A, B, then C in sequence. However, we can 'simplify' this list into one larger translation. To compose 'A fby (followed-by) B' we first expand A to include redundant rules such that the RHS of A matches every possible LHS prefix in B. Then we apply B's rules to the RHS of A. For example:

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes
            fby { "f" => "xy", "foo" => "z"}
        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # rewrite
        # Note that NULL and WARN are never rewritten under 'fby'.

We finally recognize and remove redundant rules introduced by this rewrite. For example, we don't need rule `"xy" => "zy"` if `"x" => "z"` exists, and we don't need rule `"xy" => NULL` if `"x" => NULL` exists.

## Localizations

Localizations enable programs to capture the 'link' scope in context. Given a string and a localization, we can securely generate a name referenced by that string, excepting NULL or WARN. This can be useful for multi-stage metaprogramming with late binding to the namespace.

Conversely, given a localization and a full name, we can generate a (possibly empty) set of precursor strings by reversing the translation. This is useful mostly for reflection APIs. A reverse translation is inefficient by default, but it is feasible to cache a reverse-lookup index.

## Prefix Uniqueness and Name Mangling

Names *should* be prefix-unique, i.e. no name is a prefix of another name. This constraint exists to esnure prefix-to-prefix translations (the TL type) can always uniquely translate a name. A violation won't cause any issues for the namespace per se, but is worth a warning. In practice, prefix uniqueness will be enforced by the front-end compiler rewriting names, e.g. escaping reserved characters and appending a ".!" suffix.

The proposed ".!" suffix serves prefix uniqueness, and further ensures every definition "bar.!" is implicitly part of a composite "bar.\*", which is very convenient for namespace extension and associative definitions. For example, we might annotate definitions with "bar.\#type" and "bar.\#doc" and so on.

## Namespace Macros and Eval

The 'ns.eval' operation is the basis for namespace macros. This operation interprets an AST argument as the body of an anonymous procedure. In addition to returning a result, the 'ns.eval' operation may interact with the caller through a generic callback handler 'cb'.

In case of missing definitions, we can evaluate as far as possible without those definitions (allowing progress in cases where definitions are only conditionally required) then wait for missing definitions to be provided by other forks. This provides a basis for lazy evaluation or interactive computation between namespace 'threads'.

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

*Note:* to translate FileExt to a glas name we lower case 'A-Z', replace ASCII punctuation by '-', add the '.!' suffix for prefix uniqueness. For example, file "foo.TAR.GZ" is processed by '%env.lang.tar-gz.!'. Names containing control characters are simply rejected.

### Folders as Packages

I propose to forbid parent-relative ("..") and absolute file paths in context of processing files. Files should reference other local files in the same folder or subfolders, or remote files via DVCS. Thus, each folder is effectively location independent. The same would apply within DVCS repositories.

Further, a folder containing a "package" file (of any extension) will be recognized as a package folder. Instead of directly loading individual files, users load the package folder and we'll implicitly search for the package file and load that. If clients attempt to bypass the package file, we'll report a warning because doing so can hinder refactoring and maintenance.

User-defined namespace procedures can implement this restriction by overriding 'ns.read' and checking for package files. Usefully, this is easily integrated with the front-end compiler support for *User-Defined Syntax*. The glas executable will apply these restrictions in context of the user configuration, scripts, and any built-in front-end compilers. 

*Note:* Filesystem-layer links can work around restrictions on relative or absolute paths. I don't recommend this in general because it complicates sharing and distribution of code. But it's convenient for adding local projects to a user configuration.

### Linear Files

Reading a file more than once is not an error at the namespace layer in context of lazy loading and distinct compilation environments. Even cyclic file dependencies are possible. However, it's *probably* an error at other layers, suggesting use of a shared library, namespace macro, or copy for independent editing in a notebook application. I propose that, by default, a runtime reports a warning if a file is read more than once for any reason. The runtime can also support annotations or configuration options to suppress this warning for specific files, folders, or DVCS repos.

## Late-Bound Sources

It is feasible to integrate command-line arguments or OS environment variables as late-bound sources that are determined very shortly before an application is run. For example, 'ns.read(os:env:Var)' could return the specified environment variable, but would also hinder separate compilation of an application namespace. The latter issue can be mitigated through annotations, thus late-bound sources may prove one of the more convenient and robust approaches to expressing staged applications.

Similarly, a program could access command-line arguments or OS environment variables as 'static' algebraic effects, allowing for staging in other layers. We can even support both options at the same time. There is a lot of flexibility in how we express staged programming of applications.

## Embedded Data

The 'd:Data' node within an AST allows for embedding data without further processing at the namespace layer. However, it is not recommended to use 'd:Data' directly as a parameter within most operations. Instead, a thin data wrapper such as `(%i d:42)` for integers should apply in most cases. This provides a convenient opportunity for the program to integrate type safety analysis, accelerated representations, and similar features. 

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

It is possible to model interactions through the namespace such that two or more threads define and await definitions in turn, but garbage collection is necessary to support long-running processes within the namespace. In general, we can garbage-collect private definitions (by convention, words containing "~") if they aren't reachable from a public definition or 'ns.link'.

However, removing definitions from scope of 'ns.link' involves adding link rules. It's fruitless if this overhead is linear, so we must arrange for the rules to simplify. This is easiest in case of sequential interactions, such as channels. 

Consider a numbering schema such as A000 to A999, B001000 to B999999, C001000000 to C999999999, and so on. Under simplification, rule `"A11" => NULL` replaces individual rules for `"A110" => NULL` up to `"A119" => NULL`. When processing item A123, six rules can remove A000 to A122 from scope: A0, A10, A11, A120, A121, A122. In general, the number of rules is the sum of digits plus the number of prior size headers (A, B, C, etc.). (Use of base2 or base4 would allow some tradeoffs.)

In practice, there is no use case for these channels. It's just an exercise in expressiveness. Best practice is to avoid unnecessary entanglements between namespace threads.

## Quotas

Divergence is a relevant concern. However, even if we could somehow guarantee termination in presence of 'eval', we can easily express computations that take far more time than we're willing to spend. Thus, we'll want quotas for evaluation of a namespace. Quotas could be expressed via user configuration and annotations.

To ensure a reproducible outcome, quotas must be based on a heuristic cost function. We might accumulate costs in a register, then check for overruns periodically (e.g. upon GC) and just before commit. But it should be deterministic whether or not any given fork of the namespace procedure commits.

## Failure Modes

Errors that abort a namespace procedure - assertion failures, quota constraints, etc. - are reduced to a warning, with evaluation proceeding on other 'ns.fork' branches. In case of invalid definitions - malformed AST, links to NULL or WARN - we can report appropriate compile-time warnings and arrange to diverge at runtime. After namespace computation halts (due to return and commit, abort on error, 'ns.eval' waiting on definitions, or lazily paused after 'ns.move') we implicitly invalidate missing definitions.

Ambiguous definitions are possible if the same name is assigned multiple distinct definitions. (Assigning the same definition many times is idempotent.) In context of lazy evaluation, it is awkward to treat ambiguity as an error. Instead, we raise a warning then deterministically prioritize the first definition from the lowest-numbered fork. If a lower-priority definition must be observed (via 'ns.eval') to compute the higher-priority version, we can report an additional warning (or error) for the priority inversion, then deterministically favor the observed definition for consistency. Either way, the outcome should be deterministic, and programmers should manually resolve ambiguity.

The heuristic policy for which namespace-layer errors or warnings should block applications from running should be configurable and application specific. That is, the runtime evaluates a configured function with access to the list of namespace errors and 'app.settings' to decide whether we abandon the effort. If we do run the application, this list of issues may also be visible through a 'sys.refl.ns.\*' API.

## Regarding User-Defined Constructors

User-defined constructors can feasibly support macros, templates, and embedded DSLs independent of front-end syntax. However, there are concerns similar to [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). In context of this document on namespaces, the emphasis is abstracting names and localizations.

Names may be constructed only based on a (Localization, Binary) pair. We could feasibly permit converting names back to data, or at least comparison of names. Localizations cannot be constructed, composed, or computed, only captured based on 'ns.link' context. Lazy scope translations could be made implicit, never directly observed by the macro. 

These features aren't especially difficult to implement. We could support these features via abstract data types, or macros could operate on a tacit data stack of ASTs and avoid direct capture of AST values. But this is an important constraint on design of user-defined constructors in the program model.

## A Note on Namespace Reflection

A reflection API may provide a program access to its own definitions. This can be useful for metaprogramming or debugging. Arguably, the simplest approach to reflection is to present a global namespace and present fully-translated AST values as definitions. With a localization, users can manually translate AST fragments to or from local names. However, this hinders local reasoning regarding scope, access control, dead code elimination, and position-independent code.

A viable alternative is to present full names and localizations as abstract data types, at least when accessed through the reflection API. A subset of global names will be unlinkable (link to NULL or WARN) or unreachable (no preceding string), but a localization can provide a robust, local, partial view of a namespace.

It is feasible to support both global and local reflection APIs at different trust levels.

## Regarding Hierarchical Namespaces

The glas program model will introduce local variables and algebraic effects handlers in context of a subprogram. These are namespace-like things. Ideally, we should avoid reinventing namespaces in multiple layers. However, at the program model layer, the design challenges are very different - far fewer definitions, far tighter integration with state in scope. The program model may permit use of a namespace procedure, as described in this document, to define a set of handlers. But, in practice, a much simpler solution will likely prove sufficient.

## Compact AST? Rejected.

It is tempting to compact the AST a little, e.g. we could instead use:

        type AST = List of AST              # constructor
                 | 0b0.Data                 # embedded data
                 | 0b10.Name                # namespace ref
                 | 0b110.List of TL         # localization
                 | 0b1110.(AST, List of TL) # scoped AST, lazy translate

In practice, this won't help much with Names due to prefix uniqueness and name mangling. But it will save a few bytes for embedded data, especially in case of small integers and labeled variants. But a cost is that we cannot casually render AST values without heuristically recognizing them as AST values.

In the end, I don't believe saving a few bytes is worth much, or at least that this isn't the best place to pursue it. In context of memo-caching compilation, we might use [glas object](GlasObject.md) together with a separate compression pass, or attempt to extend glas object with systematic compression of repetitive structure.
