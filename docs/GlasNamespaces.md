# Glas Namespaces

The glas namespace is the foundation for modularity, extensibility, metaprogramming, and version control of glas systems.

A user's view of a glas system is expressed as an enormous namespace importing from multiple files and DVCS repositories. In general, the user will import from some DVCS repositories representing a community, company, or specific projects of interest, then override some definitions for user-specific resources, preferences, or authorities. Working with a huge namespace is mitigated by lazy evaluation and caching.

I propose to represent a glas namespace *procedurally*: a program that 'writes' definitions when evaluated in a suitable context. At the top level, this procedure is parameterized by a source file, also serving as a front-end compiler for a specific file extension. This delegates many representation details to the [glas program model](GlasProg.md).

## Proposed Representation

Some useful types:

        type Name = prefix-unique Binary, excluding NULL, as bitstring
        type Prefix = any prefix of Name (empty to full), byte aligned
        type TL = Map of Prefix to (Prefix | NULL) as radix tree dict
          # rewrites longest matching prefix, invalidates if NULL
        type NSDict = Map of Name to AST as radix tree dict
        type AST = (Name, List of AST)      # constructor
                 | d:Data                   # embedded data
                 | n:Name                   # namespace ref 
                 | s:(AST, List of TL)      # scoped AST
                 | z:List of TL             # localization

Compiler effects API (roughly):

* `def(NSDict)` - emit a batch of definitions
* `move(TL)` - apply translation to future assigned names (LHS of NSDict)
* `link(TL)` - apply translation to future definitions (RHS of NSDict, eval)
* `fork(N)` - returns non-deterministic choice of natural number 0..(N-1)
* `eval(AST)` - evaluate under current translation
* `load(URL)` - compile another source file
* `source` - abstract source (location, version, etc.) to support notebook apps and debugging.

## Abstract Assembly

I call the lisp-like AST encoding "abstract assembly" because every constructor node starts with a name, never a concrete value or bytecode. This ensures we can extend, restrict, or redirect constructors through the namespace. The system provides a set of 'primitive' constructor names, typically prefixed with '%' such as '%i.add' for arithmetic and '%seq' for procedural composition. The common prefix simplifies recognition and translation. Most TL maps will contain a `"%" => "%"` entry to forward primitives by default.

## Prefix Uniqueness and Name Mangling

Names are described as prefix-unique: no name should be a prefix of another name. A violation of prefix uniqueness is a minor issue, worthy only of a warning. However, prefix-uniqueness is what lets us translate "bar" without modifying "bard" and "barrel". To resolve this, a compiler should 'mangle' names, e.g. escaping special characters and adding a ".!" suffix. 

The proposed suffix ".!" serves a role beyond prefix uniqueness. Every definition "bar.!" is implicitly associated with larger composite "bar.\*" for purpose of renames. This allows every definition to be implicitly extended with subcomponents or metadata within the namespace. We'll usually translate "x." to "y." instead of "x" to "y".

*Note:* I don't write names in mangled form for aesthetic reasons, but even primitives would be mangled.

## Composing and Simplifying Translations

We can compose TL sequentially, i.e. assuming TL maps A and B, we can write 'A fby B' to indicate translation A is followed by (fby) translation B. Within the AST, we often express this lazily as a list of TL, head followed by tail. But it is feasible to reduce a list of TL to a singleton. This is achieved by extending suffixes on both sides of A such that the RHS for A matches every possible LHS prefix for B, then we apply B's rewrites to the RHS of A.

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start
          # NOTE: also extend suffixes of implicit "" => "" rule
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes 
            fby { "f" => "xy", "foo" => "z"}     
        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # end

However, if it's only for performance, compacting TL maps will often prove to be a wasted effort because we end up applying a large TL map to just a few names, and we have better structure sharing with the list of TL. We only save when applying the translation very widely. So, this should be left as a heuristic decision.

Aside from composition, a TL map can be simplified where rewrite rules are redundant, e.g. when a rule with a longer prefix is implied by a rule with the next shorter prefix. For example, we don't need a rule `"xy" => "zy"` if the rule `"x" => "z"` exists. And we don't need the rule `"xy" => NULL` if `"x" => NULL` exists.

### Scoped ASTs

The only computation represented at the AST layer is scoping, which applies a sequence of translations to the AST node. Scoping is convenient when composing large AST fragments, and supports lazy evaluation. A localization lets us record a scope for future use in multi-stage programming.

When constructing definitions, the stack of 'link' translations will essentially be applied as a scope to every introduced definition. The 'move' translation instead rewrites the defined symbols, thus aren't represented within the AST at all.

## Non-Deterministic Choice

We use non-deterministic choice as the basis for iteration, i.e. upon 'fork' we'll actually evaluate both options in separate iterations. To ensure a deterministic outcome, definitions from lower numbered forks have priority. However, evaluation order is flexible. It is sufficient to avoid creating a dependency cycle. 

## Lazy Evaluation

Independent of lazy 'thunks' to defer pure computations, we can lazily evaluate a namespace by deferring evaluation of forks that obviously won't define the names we need. This will be determined based on 'move' translations.

## Source Abstraction

To support notebook applications, the application must capture a reference to source so it can be edited from within the application. However, the source should be kept abstract to avoid influencing the compiled output. I currently propose a 'source' parameter that returns an abstract value that we'll be able to observe later through runtime reflection APIs.

The runtime can decide whether this value is an abstract reference or directly records all relevant details.

## Modularity and Resources

A large glas namespace will be represented across multiple files and multiple front-end languages. There are a few security considerations, e.g. a remote DVCS resource must not reference the user's local filesystem.



## Automatic Testing

It is feasible to integrate tests or assertions with expression of the namespace. I would hope to do so in a manner that allows us to flexibly perform or ignore testing, e.g. with tests running on separate forks from definitions. In context of testing, use non-deterministic choice could serve as a basis for fuzz testing. 

This might be expressed by adding a `test(Name)` effect that treats the remainder of the current fork as a test. New definitions in this context might only apply for 'eval' within the test environment.

## Live Coding

The proposed `source` parameter or effect would capture the source location and its content (or a hash thereof) to support live coding. Capture of content would be useful for display of the original source even when disconnected from it, or to diff a compiled version with the current file.

## Design Patterns

### Private Definitions

As a simple convention, we might assume the "~" prefix is reserved for private definitions used within the current file or other compilation unit. When loading files, a compiler might implicitly 'allocate' regions within its own private space, and translate private names from imports via renames such as `{ "~" => "~(Allocated)." }`. 

Translation based on a privacy prefix doesn't prevent a client from accessing private definitions of a module, but it does allow the client to more precisely control which definitions are shared between different subprograms. 

We might further add a weak enforcement mechanism: raise a warning when a name containing "~" is directly used in a definition or if a translation would not be privacy-preserving. A compiler would then arrange to introduce privacy only via translations, e.g. based on an export list.

### Implicit Parameters

I propose a simple convention: we reserve "%.\*" to serve as a read-only context. This piggybacks on the default rules for propagating abstract assembly primitives. I assume "%" is read-only via implicit 'move' rule `{ "%" => NULL }`. Instead of introducing definitions, we use translations to logically rewrite names like `{ "%.x." => "my.x." }` within a controlled scope. This might also serve as a flexible alternative to global definitions in some cases.

### Aggregate Definitions

There are various cases where we might want to combine definitions from multiple subcomponents. For example, in context of live coding we might render each 'import' as a page or chapter that merges into a final notebook. To support shared libraries, we might want to build a list of dependencies that the client is expected to provide in the end.

Ultimately, this will be a language-layer feature, handled by the front-end compiler. It isn't possible to directly check whether a symbol is defined, but a compiler may assume specific definitions are provided by every import and automatically integrate them. Where this isn't the case, or where the feature should be disabled, specialized import syntax may let users intervene and manage integration manually.

### Shared Libraries

Instead of directly loading common utility code such as math libraries, we could insist on receiving these definitions as an implicit parameter, e.g. via "%.lib.math.\*". Doing so can potentially avoid a lot of rework and reduce need for a separate compression pass. Instead of manually loading, we might use aggregate definitions to compose a list of shared libraries needed, then process this list automatically. 

However, this does require every an extra step to explicitly load these resources. This could be mitigated by adding the shared library load step to the standard application life cycle, such that it can be handled by the runtime without wrapping the application.

## Failure Modes

### Ambiguous Definitions

If a namespace outputs the same name with two distinct definitions, we can raise an ambiguity warning. However, we can also resolve this predictably based on order, prioritizing the first definition, or the leftmost in context of 'fork'.

Note that defining a name many times with the same definition is idempotent and doesn't merit a warning. We can define pi to 3.14 as many times as we wish. It's only ambiguous if we define pi as 3.14 in some places and 3.1415926 in others. However, this would be limited to the most obvious structural equivalence. If we define pi as 3.14 here and 3+0.14 there, we'll consider them non-equivalent. The assumption of idempotence can be useful to help 'check' definitions.

I expect users would manually resolve most ambiguity warnings by tweaking their import/export lists to be more precise. 

### Divergence

An '%ns.proc' program might enter an infinite loop until a configurable quota is hit, or be blocked indefinitely on a missing definition. In these cases, we can raise warnings and continue best-effort, despite risk of accepting lower-priority definitions.

Ideally, failure on quota should be deterministic. That is, it should be based on some deterministic property that can be evaluated independently of runtime or host details like CPU time. A simple option is to base this on a loop counter or similar, perhaps augmented by annotations.

### Dependency Cycles

A dependency cycle will only be distinguished from a missing definition where a dependency for evaluating the high-priority source is 'resolved' using a lower-priority source for the same definition. In this case, we can accept the resolution if there is no ambiguity, otherwise we will raise a cycle error. Essentially, detecting this requires tracking priority of definitions and which definitions were 'used' during evaluation of the namespace.

### Invalid Names

If a 'link' TLMap contains a NULL in the RHS, it means the LHS prefix or name shouldn't be used. If that name is used regardless, we will at least emit a warning and 'compile' any call to that name to instead raise an error at runtime. However, this is one of those warnings we might want to treat as an error, forcing developers to fix the problem immediately.

## Performance

### Incremental Computing


## Incremental Computing

The compiler lacks APIs to directly store computations into cache. Instead, we'll rely on memoization via annotations and other features. How far can we take this?





## Naming Variables

In context of metaprogramming, it is convenient if capture of variables is controlled, aka [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). One possibility here is to encode a namespace directly into the computation, use translations to control access. Alternatively, we could encode variable names with reference to an external namespace. In the latter case, instead of `(%local "x")`, we might use `(%local &privateScopeName "x")`. This allows for limited non-hygienic macros when they can guess the private scope name.

## Staged Eval

In some cases, we'll want to insist certain expressions, often including function parameters and results, are evaluated at compile time. Minimally, this should at least be supported by annotations and the type system. But it is useful to further support semantic forms of static eval, e.g. '%static-if' doesn't necessarily need to assume the same type or environment on both conditions, instead allowing the static type of an expression or function to vary conditionally.

In context of dead code elimination and lazy loading, static eval is limited in viable 'effects' API. We can permit 'safe' cacheable fetching of files or HTTP GET, or debug outputs such reporting a warning. In the latter case, we might be wiser to model the effect as an annotation and debugging as reflection.




