# Glas Namespaces

In my vision for glas systems, huge namespaces define runtime configuration, shared libraries, and applications. Definitions can be distributed across dozens of DVCS repositories, referencing stable branches or hashes for horizontal version control. We rely on laziness to load and extract only what we need, and incremental compilation to reduce rework. 

Applications should be expressed as OO class-like namespaces: methods with open recursion, supporting inheritance and overrides. The final step is binding an application to a namespace of system APIs and runtime state. 

Ideally, all namespace-level computation should be completed at compile-time. Though we might permit staged apps that compile just a bit further after receiving access to command-line args and environment variables.

## Design Overview

The lambda calculus awkwardly supports namespaces, e.g. `let x = X in Expr` as syntactic sugar for `((λx.Expr) X)`. With fixpoints, we can also support recursive definitions.

We can extend the lambda calculus with reified environments. A keyword, say `__env__`, lazily extracts visible definitions into an abstract environment record. To utilize this environment, we can also introduce a prefix binding: `((νPrefix.Expr) Env)`. This makes names in Env available through Prefix, and implicitly shadows all existing names with Prefix. Prefix may be the empty string, thus we might express dotted-path access as `((ν.foo) Env)`.

To support scoping and interface adapters, we introduce a scoping rule consisting of a set of prefix-to-prefix rewrites, e.g. `{ "a" => "b", "b" => "c", "c" => "a", "d" => NULL }` rotates prefixes a, b, c and hides prefix d. In general, we apply the rule with the longest matching prefix (if any) to each name in the current environment, and we'll support full-name matches too. We can apply scopes to first-class environments through `(ν.(apply scope to __env__) Env)`.

Other useful namespace computation primitives include ifdef and union. With ifdef, we can more easily implement default definitions or compose partial interfaces. With union, we can conveniently integrate definitions into a shared namespace without giving everything a unique prefix. Union does risk ambiguity, but we can raise an error when we detect a name has multiple definitions, forcing developers to resolve the conflict.

Each file should compile to a parameterized namespace as an intermediate representation, i.e. `ν%.(Env Expr)`. By convention, '%' is our pseudo-global namespace and includes [program-model primitives](GlasProg.md), compile-time effects such as loading files, and access to 'env.\*' defined in the user configuration via fixpoint binding to '%env.\*'. The latter supports shared libraries and applications.

The glas system supports user-defined syntax. The operation to load a file selects a front-end compiler '%env.lang.FileExt' from the provided environment (usually the current environment) based on file extension. If everything goes well, we then bind the provided environment and return the Env. Otherwise, we'll raise suitable errors. To bootstrap this, '%env.lang.glas' (and perhaps '%env.lang.glob' for serialized data) may initially link to a built-in.

Applications should be defined within the user configuration, typically 'env.appname.app', or a separate script file, which we'll load via '%env.lang.FileExt' then access 'app'. Applications are also defined as parameterized namespaces, and are expected to have the form `λsys.λself.(AppEnv Expr)`. We bind 'sys' to a runtime effects API, e.g. 'sys.file.\*', then we fixpoint 'self' to support open recursion when composing applications. The resulting AppEnv should define 'settings', 'main', and common, composable event handlers such as 'http' and 'rpc'.

Program methods generally are frequently defined with form `λargs.(Program AST)`. This allows callers to provide a controlled view of their environment. In context of the full program model, this args environment may include local algebraic-effects handlers or pass-by-ref registers. 

Most computation - even metaprogramming - will be handled through the program model. The use of lambda calculus for namespaces is intended to mostly be static computation. The namespace AST may embed glas data, but treats it as fully opaque. However, it is possible to [encode data into lambdas](https://en.wikipedia.org/wiki/Church_encoding). Perhaps, one day, it may even prove useful.

Lazy evaluation is essential for this design to work as intended, applying both to lazy loading of definitions and metaprogramming through fixpoint definitions. However, assuming computation is mostly performed at compile-time and is highly amenable to incremental computing, I shouldn't need to break my brain understanding Simon Peyton Jones's STG machine.

### Incremental Compilation

Lazy evaluation can simplify tracking of dependencies: for each thunk, we can track its dependents. Each thunk may double as a persistent memo cell and reference many other cells.

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. Like 42. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Representation



# Deprecated Content

I'm pretty happy with the new direction!

## Relevant Types

        type AST = List of AST      # constructor
                 | d:Data           # embedded data
                 | n:Name           # namespace ref
                 | s:(AST, TLL)     # scoped AST for lazy translations
                 | z:TLL            # localization for metaprogramming
        type Data is any plain old glas data.
        type Name = Binary excluding NULL (0x00) 
        type TLL = List of TL (applied left to right)
        type Prefix = any binary prefix of (Name + ".!")
        type TL = Map of Prefix to (Prefix | NULL | WARN) as radix tree
        type NULL = 0x00
        type WARN = 0x00 0x77 (NULL 'w')

This AST type supports precise recognition of names, deferred translation of names, and arbitrary embedded data. In general, the first element of a constructor should be a name or another constructor.

Translations will rewrite a longest matching matching prefix of a name. Names containing "/" receive special attention. Except where "/" is explicitly matched, we repeat translation on the suffix following "/". This allows us to construct a name as a sequence of names, and thus to support more flexible access control.

A problem with prefix-to-prefix translations is that translating "bar" accidentally affects "bard" and "barrel". To mitigate, we logically add a ".!" suffix to names or "/" components for matching purposes. We raise an error if this suffix isn't preserved. Thus, we could match "bar.!" specifically, or "bar." to translate "bar" together with "bar.\*".

*Aside:* It is possible to compose a list of TLs into a single TL, but non-trivial and not always more efficient. 

## Procedurally Generated Namespaces? Tentative.

A viable representation of namespaces is to run a program that iteratively writes names. We could interpret non-deterministic choice as branching the program, forming a union. Non-deterministic coroutines would be awkward here, but we can use a deterministic scheduler, e.g. left-to-right (always evaluate leftmost coroutine that doesn't fail).

However, I'm not entirely satisfied with this solution. It doesn't unify nicely with method namespaces. It requires a different evaluation of programs. The ambiguity of definitions, and the inconsistency of ifdef, are troublesome.

I think we can do better. Just need to figure out how.

Anyhow, a viable API:

* `ns.*` - common prefix for namespace ops
  * `write(Name, AST)` - write a definition. This is modified by prior move and link translations. 
  * `move(TL)` - apply translation to future defined Names (for write).
  * `link(TL)` - apply translation to future ASTs (for write or eval).
  * `read(Query) : Result` - cacheable queries, runtime-specific. May fail.
  * `eval(AST, Arg) : [cb] Result` - evaluate AST with 1--1 arity and callbacks. 
    * `cb(Arg) : [cb] Result` - a generic 1--1 arity callback handler.

There is no built-in support for aliasing, but I assume the program model will support aliasing indirectly via `ns.write("foo", n:"bar")`, i.e. that a name is equivalent to its definition in most contexts.

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

### Implicit Translation Suffix

An inherent problem with prefix-to-prefix translations is that they cannot translate "bar" without accidentally affecting "bard" and "barrel".

To mitigate this, I propose to implicitly extend names with a ".!"  suffix for translation purposes. That is, given the name "bar", we actually apply the translation rule to "bar.!". If the translated name does not also terminate in ".!", we simply raise a link error. To further resist potential problems, we might raise warnings when ".!" appears within user-defined names.

The specific choice of ".!" is based on my vision for glas naming conventions. Relevantly, it allows us to translate "bar." to include "bar" together with "bar.method" and "bar.\#docs" and so on, while "bar.!" translates "bar" specifically. In other contexts, alternatives suffixes may prove more suitable or a little name mangling can serve the same end.

## Localizations

Localizations enable programs to capture the 'link' scope in context. Given a string and a localization, we can securely generate a name referenced by that string, excepting NULL or WARN. This can be useful for multi-stage metaprogramming with late binding to the namespace.

Conversely, given a localization and a full name, we can generate a (possibly empty) set of precursor strings by reversing the translation. This is useful mostly for reflection APIs. A reverse translation is inefficient by default, but it is feasible to cache a reverse-lookup index.

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

A namespace procedure can serve as a front-end compiler, generating a namespace as an intermediate representation. I propose to support user-defined syntax in coordination with the runtime.

When importing a file, the front-end compiler should use 'ns.eval' with '%env.lang.FileExt' to process that file. To support a configurable environment, the runtime aliases '%env.\*' to 'env.\*' in the user configuration. The glas executable includes a built-in front-end compiler for at least one syntax, e.g. [".glas" files](GlasLang.md), but will attempt to bootstrap and switch to 'env.lang.glas' if defined. (It is feasible to mutually bootstrap several front-end languages.)

User-defined syntax can support DSLs or data integration, e.g. even a ".json" file can be treated as a source for staged metaprogramming. Also, syntax doesn't need to be textual, e.g. we could process a database file rendered as boxes and wires as a 'syntax'. Alignment with file extensions simplifies integration with external tooling.

*Note:* As a convention when translating FileExt to a glas name, we might lower case 'A-Z' and replace punctuation by '-'. For example, "foo.TAR.GZ" is processed by '%env.lang.tar-gz'.

### Folders as Packages

I intend to discourage parent-relative ("..") and absolute file paths to simplify refactoring and sharing of code. The only absolute paths should be reference to DVCS URLs. Constraints on locality can be enforced through override of 'ns.read'. As a backup, a runtime could warn if a namespace thread ever backtracks a sequence of 'ns.read' requests.

In many cases it might be convenient to treat 'folders' as files. We can support this by convention of scanning the folder for a 'package.\*' file that is then loaded. The runtime may also warn if the 'package' file isn't the first file loaded from a folder.

*Note:* The toplevel user-configuration may support absolute paths. This is convenient for integration of user-local projects that are in some user-specified filesystem location. The restriction on locality might kick in only after the first relative or DVCS file reference in a given thread.

### Read Files Once 

Reading a file more than once for compiling an application is not an error per se, but it is concerning in context of rework or live coding. In most cases, if we read a file twice, we should either be moving it into a shared library or similar, or copying the file so the loads may be edited or independently, aligning structure of loads with structure of filesystem. It seems feasible to detect when a file is read twice then raise a warning.

## Design Patterns

### Private Definitions

As a simple convention, we might assume the "~" character (anywhere in a name) is reserved for private definitions. When importing files, a compiler might implicitly 'allocate' regions within its own private space, and translate private names from imports via renames such as `{ "~" => "~(Allocated)." }`. 

Translation based on a privacy prefix doesn't prevent a client from accessing private definitions of a module, but it does allow the client to more precisely control which definitions are shared between different subprograms. 

We might further add a weak enforcement mechanism: warn or raise an error when a name containing "~" is directly used in a definition, or when a translation is not privacy-preserving. A translation is privacy-preserving if "~" in LHS prefix implies "~" in RHS (also permitting NULL or WARN in RHS). We introduce privacy when we see "~" in RHS without it in the LHS. A compiler can insist that privacy is introduced only through namespace translations.

### Implicit Environment

I propose to reserve "%env.\*" to serve as a read-only context. This piggybacks on the default rules for propagating primitives. To support this pattern, the glas executable will apply a default link rule `{ "%env." => "env." }`. The '%\*' space is read-only via move rule `{ "%" => WARN }`, but users can populate the initial environment by defining 'env.\*'. Beyond this, they may redirect the environment within a scope, e.g. adding linke rules such as `{ "%env.foo." => "my.foo." }`.

The implicit environment serves as the basis for shared libraries, user-defined syntax, efficient composition of applications, and user-provided feature flags to influence compilation. 

### Shared Libraries

Common utility code should be provided through the implicit environment, such as "%env.lib.math.\*". If a symbol isn't defined, the user would receive a clear error such as "no definition for env.lib.math.whatever". This is easily corrected by importing the library into the user configuration, or by loading it locally and redirecting links within a local scope.

Shared libraries do introduce versioning and maintenance challenges, i.e. an update to a library may break some applications and not others. This can be mitigated by localizing updates or by whole-system automated testing.

*Note:* Even without shared libraries, a compression pass can feasibly eliminate runtime overhead of loading the same utility code many times. But the shared library pattern avoids compile-time overheads and risk of accidental variation, e.g. via feature flags.

### Compiler Dataflow

Multimethods let users describe specialized implementations of generic operations across multiple modules, heuristically integrating them. A soft constraint-logic program might declare assumptions and preferences across multiple modules, then solve constraints holistically. Notebook applications should build a table of contents across multiple modules and robustly route proposed updates to their sources. Implementing these patterns manually is awkward and error-prone. Instead, I propose to push this to front-end compilers and libraries shared between them. 

As a simple convention, we could reserve '$\*' names for compiler-supported dataflow between modules and their clients. Compilers can implement ad hoc, fine-grained dataflows through the namespace. In contrast, '%env.\*' is controlled by the user and supports only one dataflow pattern. The glas executable does not need to be aware of specific compiler dataflow patterns except insofar as they are embedded into built-in front-end compilers.

A relevant concern with compiler dataflow is that it easily interferes with lazy loading. This is unavoidable, given we are integrating content orthogonally to loading modules. This can be mitigated by syntactic support for scoping and translating dataflows, and avoiding automation of dataflow where it confuses most users.

### Namespace Processes and Channels

It is possible to model interactions through the namespace such that two or more threads define and await definitions in turn, but garbage collection is necessary to support long-running processes within the namespace. In general, we can garbage-collect private definitions (by convention, words containing "~") if they aren't reachable from a public definition or 'ns.link'.

However, removing definitions from scope of 'ns.link' involves adding link rules. It's fruitless if this overhead is linear, so we must arrange for the rules to simplify. This is easiest in case of sequential interactions, such as channels. 

Consider a numbering schema such as A000 to A999, B001000 to B999999, C001000000 to C999999999, and so on. Under simplification, rule `"A11" => NULL` replaces individual rules for `"A110" => NULL` up to `"A119" => NULL`. When processing item A123, six rules can remove A000 to A122 from scope: A0, A10, A11, A120, A121, A122. In general, the number of rules is the sum of digits plus the number of prior size headers (A, B, C, etc.). (Use of base2 or base4 would allow some tradeoffs.)

In practice, there is no use case for these channels. It's just an exercise in expressiveness. Best practice is to avoid unnecessary entanglements between namespace threads.

## Quotas

Divergence and performance are relevant concerns. We'll want quotas for evaluation of the namespace. Quotas can be expressed via user configuration and annotations.

Ideally, quotas should pass or fail deterministically, independent of CPU time or optional optimizations. This requires developing a heuristic cost function and, in general, checking for overruns before accepting generated output.

## Declarative Namespaces

Observations:

* If applications are defined as namespaces, then namespaces and definitions have the same 'type'. That is, if definitions are represented by AST, then so are namespaces.
* If ASTs are namespaces, then at least some ASTs must define things.
* Most definitions benefit from utility definitions. It seems worthwhile to support a notion of introducing definitions at least locally within other definitions.
* The method 'args' as a client-controlled view of the client's namespace seems widely useful. We should make this universal in the namespace model.
* Ideally, definitions can be inlined without changing their meaning. The definition boundary shouldn't be semantically significant. Thus, to *receive* the arguments, we may need something explicit like a lambda or template. 
* A localization is a reified translation. Localizations offer a first-class way to represent an environment of names. They can be passed through multiple layers. Of course, the construction of a localization needs some attention.
* We can unify lambdas binding definitions and localizations if we forbid definitions on the 'empty'. Lambdas can bind entire prefixes, if a localization is bound, use beyond the immediate prefix then binds the other names. But I fear this might reduce usability. Alternative of two distinct structures may prove clearer and more usable.
* Idea: if an AST can 'evaluate' to a localization that binds introduced local definitions, that could be a very declarative approach to defining a namespace. TBD: representing a localization as a prefix-to-prefix translation seems incompatible with 'local' definitions. Overrides may also prove troublesome. 
* Idea: if we have something like lambdas that can return lambdas, we can (via currying) support user-defined constructors of multiple arguments. Lambdas would receive a localization by name instead of expanding it under a prefix, of course. TBD: support for var-args?
* Support for constructors with a variable number of arguments seems problematic. It seems possible to process them, albeit awkward, e.g. lambda with optional arg and two bodies. It seems much more difficult to *generate* var-arg structures as part of an AST. And it's a problem that I'd prefer to avoid entirely.
* Perhaps it's better to not use var-args in the first place? This is an intermediate language. It isn't a problem to ask a front-end compiler to manage arity.
* The notion of 'local definitions' needs attention. If we have lambdas, we get local definitions implicitly, e.g. `let x = X in Expr` can be translated into `(λx.Expr X)`, and within Expr we'll we'll be translating 'x' to something that was previously anonymous. We might even capture this within a localization.
* We can feasibly represent anonymous definitions in a localization by extending a TL type.
* Perhaps mixing prefix-to-prefix translations with name-to-definition (and maybe prefix-to-localization?) would be an effective way to integrate things? Not sure about this. 
* Normally, a localization is paired with a global dict. I'd really like to get back to that! A global dict is very nice for rendering and debugging.
* So, we have one idea of 'evaluating' a localization that defines an internal view of a flexible namespace, and another that involves 'writing' definitions. The problem with writing definitions is that we can easily write the same name twice. The lambda-like approach can structurally guarantee a single definition per name, but it does not directly support inheritance and override of names.
* To support inheritance and override, my prior approach involved separating the 'move' and 'link' translations. We could move a prior definition then invoke it. Not great, but usable and it maintained the simple TL type.
* For overriding, we need some way to re-link definitions. This is often through a 'self' argument or similar. Could we do similar, providing a 'self' localization to some code instead of referencing things directly? Can we robustly support something similar to multiple inheritance or mixins?
* I think we could support 'ifdef' more broadly with this lambdas approach.
