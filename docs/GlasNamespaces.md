# Glas Namespaces

The glas namespace is the foundation for modularity, extensibility, metaprogramming, and version control of glas systems.

A user's view of a glas system is expressed as an enormous namespace importing from multiple files and DVCS repositories. In general, the user will import from some DVCS repositories representing a community, company, or specific projects of interest, then override some definitions for user-specific resources, preferences, or authorities. Working with a huge namespace is mitigated by lazy evaluation and caching.

## Procedurally Generated Namespaces

I propose to represent a glas namespace *procedurally*, i.e. as a program that iteratively writes definitions. The effects API is restricted to simplify laziness, caching, and flexible evaluation order. To support metaprogramming, the procedure receives access to 'eval' within scope of the generated namespace.

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

Algebraic effects API:

* `define(Name, AST)` - write a definition, modified by prior move and link
* `move(TL)` - apply translation to future defined names
* `link(TL)` - apply translation to future definition body (scopes AST)
* `fork(N)` - returns non-deterministic choice of natural number 0..(N-1)
* `eval(AST)` - returns result of evaluating anonymous procedure
* `eval.eff` - used by eval; default implementation raises an error
* `load(SourceRef)` - compile module in scope of current translation 
* `source` - (tentative) returns stable, abstract reference to the current source location

## Evaluation Strategy

The 'fork' effect supports non-deterministic choice as a basis for iteration and laziness. Logically, we repeatedly evaluate a namespace procedure as many times as necessary, each in a separate transaction. However, the output is monotonic, idempotent, and deterministic. There is no need to recompute a branch after it commits or aborts for any reason other than missing definitions. In practice, we might evaluate multiple branches in parallel and pause computation when awaiting a missing definition.

However, to ensure a deterministic outcome in case of ambiguity, we prioritize definitions from lower-numbered forks. Thus, we must not accept definitions from higher-numbered forks before all lower-numbered forks are finished or paused. Further, 'move' translations restrict which definitions a branch can produce: we can lazily defer computation of branches if we determine that they do not produce definitions we need.

In case of long-running computations, we might heuristically garbage-collect intermediate definitions. This can be understood as an aggressive form of dead-code elimination. We can forget 'private' definitions if they are not reachable from 'public' definitions or any pending computation. The 'link' translation influences what is reachable from a namespace procedure. We can also collect computations that wait on definitions not in scope of any 'move' translation. See *Namespace Processes and Channels* for a design pattern that relies on garbage-collection.

The 'eval' and 'load' effects both invoke the current namespace. If necessary, they wait for definitions. Although these can be implemented via interpreter, for performance we might run post-processing of definitions concurrently with computation of the namespace. This post-processing may include type-checking, testing, optimizations, JIT compilation to lower-level code, and so on.

## Abstract Assembly

I call the Lisp-like AST encoding 'abstract assembly' because every constructor node is abstracted by a name. In contrast to concrete encodings like bytecodes, it is very convenient to extend, restrict, or redirect these names via 'link' translations.

We assume the system defines a set of 'primitive' constructor names, such as '%i.add' for arithmetic and '%seq' for procedural composition. The '%' prefix will simplify recognition and translation. Usually, we'll forward primitives through the namespace unmodified, so `import ... as foo` might use a TL similar to `{ "" => "foo.", "%" => "%" }`. 

We can evaluate AST to a canonical form by applying scope translations then simplifying localizations. Evaluation of a scope node involves rewriting or invalidating names and appending the list of translations to an internal scope node or localization.

## Aliasing

Aliasing is expressed by defining one name to another, e.g. 'define(Name1, n:Name2)'. These two names should then be equivalent under evaluation within the program model, excepting reflection APIs. 

## Translations

A translation is expressed as a prefix-to-prefix map. We'll find the longest matching prefix for a name on the LHS, and rewrite that to the RHS prefix. This allows for atomic swaps, e.g. `{ "a." => "b.", "b." => "a." }` would swap 'a.\*' and 'b.\*' in a single step. 

To cover a few additional cases, we permit NULL or WARN (NULL 'w') in place of the RHS prefix. These are context-dependent. For a move translation, NULL quietly removes names before they're added to a dictionary, while WARN represents removal with a warning. For a link translation, NULL raises a compile-time error, while WARN emits a compile-time warning then arranges for a runtime error in case that code is evaluated. Based on user configuration and local annotations, we may treat all link errors as warnings or vice versa.

Translations compose sequentially. Within the AST, list of TL `[A, B, C]` will apply A, B, then C in sequence. However, we can evaluate this to one large translation. To compose 'A fby (followed-by) B' we first expand A to include redundant rules with longer suffixes on both sides such that the RHS of A matches every possible LHS prefix in B. Then we apply B's rules to the RHS of A. For example:

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

Names should be prefix-unique, i.e. no name is a prefix of another name. This constraint exists to esnure prefix-to-prefix translations (the TL type) can always uniquely translate a name. If we notice this property is violated, it is sufficient to report a warning. In practice, prefix uniqueness will be enforced by a compiler rewriting names, e.g. escaping reserved characters and appending a ".!" suffix.

The proposed ".!" suffix further ensures every definition "bar.!" is implicitly be part of a composite "bar.\*". This supports ad-hoc extension. For example, we might annotate definitions with "bar.\#type" and "bar.\#doc" and so on.

## Namespace Macros and Eval

The 'eval' operation is the basis for namespace macros. This operation interprets an AST argument as the body of an anonymous procedure. Although I say 'interpret', I assume a just-in-time compiler is involved for performance. The return value is returned to the caller, while failure will abort the caller. A special exception is failure due to a missing definition: we can arrange the namespace procedure to wait for the definition settle before continuing.

The AST may interact with the caller via algebraic effects. To keep integration simple, the interpreter routes all requests to `eval.eff : (HandlerName, List of Arg) -> Result`. The caller is expected to override this handler in scope of calling 'eval', with the default raising an error.

## Modularity

The 'load' operation is the basis for modular namespaces. This call returns immediately, merely arranging to compile and integrate the module after the caller returns. Only the caller's 'link' and 'move' translations are inherited, and the module is compiled in an otherwise fresh environment of algebraic effects.

### User-Defined Syntax

Support for modularity is entangled with support for user-defined syntax. Instead of a standard syntax, 'load' selects a namespace procedure from the current scope based on file extension, typically '%env.lang.FileExt'. This namespace procedure is parameterized by the file binary and evaluated. Effectively, this implements a front-end compiler, while the generated definitions serve as an [intermediate representation](https://en.wikipedia.org/wiki/Intermediate_representation).

We assume built-in support for a few file extensions, such as ".glas" files. However, '%env.lang.glas' is favored. If this is self-referentially defined, we'll attempt a bootstrap via the built-in then verify a fixpoint. Multiple built-in languages can be mutually bootstrapped in one step. 

By aligning user-defined syntax with file extensions, we can easily integrate with external tools. A file-based database or a ".zip" file can generously be viewed as 'syntax'. Graphical and textual programming can be freely mixed. Users can develop DSLs for a project. Simple ".txt" or ".json" files might merely define 'data', but we can warn for spelling errors or report compile-time errors for structure errors, and we can easily apply partial evaluation optimizations.

*Note:* File extensions will be rewritten a little in translation to a name: utf-8, lower-case ('A-Z' only), replace '.' with '-'. We'll also add the ".!" suffix for prefix uniqueness.

### Folders as Packages

To simplify sharing of folders, we forbid loading of parent-relative and absolute file paths. Files within a folder may only reference other files in the same folder or subfolders, or remote DVCS resources. Thus, a folder is effectively location-independent.

Users may refer to a folder as the target for 'load'. In this case, we search for the 'package' file of any extension. If the package file exists, it is loaded. Otherwise, we generate a simple namespace that mimics the hierarchical structure of the folder content, albeit hiding file extensions. Files or subfolders whose names start with '.' would be fully hidden.

Reference to specific files within a subfolder can bypass interface abstractions and hinder refactoring of code. We might raise a linter-level warning to encourage developers to properly treat folders as packages.

### Ad-hoc SourceRef

A glas runtime may interpret source references in implementation-specific ways. For example, a runtime might heuristically recognize some texts as file paths or DVCS URLs. It may also support 'file:(path:Text, as:FileExt)' and 'dvcs:(...)' with multiple parts clearly describing where to fetch, which file to load, mirrors, authorization hints, etc..

A user configuration might define a `SourceRef -> SourceRef` function to translate and sanitize this reference between 'load(SourceRef)' and the runtime observing the value. For portability, this method might query the runtime. Unfortunately, this configured method is not available for the first few loads. The runtime will retrospectively review whether those initial loads are consistent with the configuration, i.e. whether we're within a fixpoint. If not, raise an error.

This design supports gradual integration with remote sources, and gradual standardization of SourceRef across runtime implementations. Initially, we might aim to support only a few remote DVCS resource sites, such as github and gitlab. We can grow from there.

## Abstract Source and Live Coding

The 'source' in the API for namespace procedures is mostly intended to support notebook applications, where every application is its own live coding projectional editor. It could be used for self-modifying code in general. The front-end compiler is parameterized with a *binary*, thus this source is the only clue where that binary comes from. However, glas code should not behave differently based on where it comes from, thus this source is left abstract. 

At runtime, the API for access to source should be abstracted around cooperative work though a DVCS - feature branches, pull requests, comments, blame, diffs, etc.. Details about where the source is located would also be available. A runtime could simulate a useful subset of these features for local files.

*Note:* Ideally, the abstract source is stable across common reorganizations of code, such that it doesn't interfere with incremental compilation or shared memo-cache. However, the developers responsible for writing the incremental compiler should assume it's unstable.

## Design Patterns

### Private Definitions

As a simple convention, we might assume the "~" prefix is reserved for private definitions used within the current file or other compilation unit. When loading files, a compiler might implicitly 'allocate' regions within its own private space, and translate private names from imports via renames such as `{ "~" => "~(Allocated)." }`. 

Translation based on a privacy prefix doesn't prevent a client from accessing private definitions of a module, but it does allow the client to more precisely control which definitions are shared between different subprograms. 

We might further add a weak enforcement mechanism: raise an error when a name containing "~" is directly used in a definition, or when a translation is not privacy-preserving. A translation is privacy-preserving if "~" in LHS prefix implies "~" or NULL in RHS. A compiler can arrange to introduce privacy only via translations.

### Implicit Environment

I propose to reserve "%env.\*" to serve as a read-only context. This piggybacks on the default rules for propagating primitives. I assume "%" is read-only via implicit move rule `{ "%" => NULL }`. Instead of defining '%' words, we use link rules to redirect them within a given scope, such as `{ "%env.x." => "my.x." }`. The runtime may apply a default link rule `{ "%env." => "env." }` to close the loop.

### Shared Libraries

Instead of directly loading common utility code such as math libraries, we can insist these definitions are provided through the implicit environment, such as "%env.lib.math.\*". If the symbol isn't defined, the user would receive a clear error such as "no definition for env.lib.math.whatever". This is easily corrected by importing the library into the client's environment. It isn't a problem to import many shared libraries: they'll be lazily loaded, and the cached code will be shared between apps.

Shared libraries do introduce versioning and maintenance challenges. This can be mitigated by controlling the scope of sharing, e.g. loading shared libraries at the scope of a curated community or project. In general, sharing will restrict overloads, i.e. we cannot move and replace a shared definition, but we can redirect "%env.lib.abc.xyz" to "my.xyz" within scope of defining a subprogram.

*Note:* Even without shared libraries, an optimizer could apply a compression pass to merge redundant code. But that optimization is relatively expensive, and it's convenient to start with 'hand-optimized' code that shares utility code.

### Aggregate Definitions

Some language features involve aggregating content across multiple modules. For example, notebook applications will import pages or chapters into view and compose a table of contents. A constraint-logic program might declare assumptions across multiple modules, with compilers gathering them to discover conflicts ASAP. Multimethods let users describe specialized implementations of a generic operation across multiple modules, then access them from anywhere.

It is awkward and error-prone for users to manually gather and integrate definitions. Instead, we'll rely on the front-end compiler to automate things. However, in context of extension with new syntax and new aggregates, we'll want a generic solution. There are limitations to work around: we cannot query whether a definition exists, we should not pass arbitrary names between scopes, and there is risk of interference with lazy loading and caching.

At the moment, I lack a solid generic solution. However, I do have an intuition: Reserve prefix "@" for public, compiler-generated definitions. Separate the logic from the compilers; provide it via '%env.\*'. At a 'leaf' node, use a call to write compiler metadata and bind to local definitions in scope. Align most aggregation of metadata with forks, such that each fork defines its own metadata then we locally compose them. A compiler can optimize a little, skipping a few writes or aggregations where obviously unnecessary.

### Namespace Processes and Channels

*Note:* I don't have a practical use case in mind. At the moment, this pattern serves only as a demonstration of theoretical expressiveness.

A namespace process can be modeled in terms of a namespace procedure that 'yields' incremental output by forking to commit one branch and continue the other. A process may also fork to 'spawn' a subprocess. Processes interact by writing or awaiting definitions in turn, with 'eval' implicitly waiting. Although the outcome is deterministic, composition is more flexible than call-return, able to model protocols and negotiations.

Interaction of processes introduces many intermediate definitions. It is possible to garbage-collect these definitions, but a long-running process must *add* link rules to *remove* items from scope. Unless these rules simplify, this incurs linear overhead, thus is useful only for coarse-grained collection.

Link rules most easily simplify for *sequential* interactions, such as channels. Consider a variable-width numbering schema such as A000 to A999, B001000 to B999999, C001000000 to C999999999, and so on. Under simplification, rule `"A11" => NULL` replaces rules for A110 to A119. When processing item A123, six rules can remove A000 to A122 from scope: A0, A10, A11, A120, A121, A122. In general, the number of rules is the sum of digits plus the number of prior size headers (A, B, C, etc.). A binary encoding results in fewer rules.

Dynamic channels are readily supported. Message 'mA123' could inform the reader that the next subchannel, 'cA003.\*', is now in play for reading or writing. A few caveats: Simplifying requires tracking extra metadata because cA003 may remain in use long after cA009 is released. Depth is a concern: as we establish subchannels over subchannels over channels, names grow linearly like 'cA003.cA042.cA001.mA042'. Channels are second-class: the closest thing to passing a channel around is spawning a subprocess to lazily alias inputs to outputs, a 'wiring' pattern.

## Quotas

Divergence is a relevant concern. However, even if we could guarantee termination in presence of 'eval', we can easily express computations that take far more time than we're willing to spend. Thus, we'll want quotas for evaluation of a namespace. Quotas could be expressed via configurations and annotations.

To ensure a reproducible outcome, quotas must be based on a heuristic cost function. We might accumulate costs in a register, then check for overruns periodically (e.g. upon GC) and just before commit.

## Failure Modes

Errors that abort a namespace procedure - dynamic type errors, assertion failures, quota constraints, etc. - can be reduced to a warning, with evaluation continuing on other branches. Errors that are localized to specific definitions - malformed AST, link violations, missing definitions, etc. - might raise a warning then arrange for a runtime error when the erroneous code is evaluated later. The choice between compile-time errors and warnings is subject to user configuration and guidance by annotations.

Ambiguous definitions are possible if the same name is assigned multiple distinct definitions. (Assigning the same definition many times is idempotent.) In context of lazy evaluation, it is awkward to treat ambiguity as an error. Instead, we raise a warning then deterministically favor the 'first' definition from the lowest-numbered fork. When ambiguity errors are noticed, users should resolve them by tweaking import or export lists, or considering use of the *Shared Libraries* pattern.

A cyclic dependency error is observed when a higher-priority 'ambiguous' definition can only be computed after observing the lower-priority version. If the definitions were the same, there is no error. If the cycle was not resolved, we did not observe the error. Essentially, this error is observed when a namespace macro should depend on its own output. Cyclic dependencies are perhaps the only errors at the namespace layer where we'll firmly insist on a resolution by programmers.
