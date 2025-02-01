# Glas Namespaces

The glas namespace model is the foundation for modularity, extensibility, scalability, and version control of glas systems. A glas system, including runtime configuration and applications, is expressed as one enormous namespace across multiple files and DVCS repositories. 




 glas namespace will define both applications and runtime configuration options, albeit in a structured way to support lazy loading and caching. 

 There is no strong separation of configurations and applications.



A configuration may import other files, supporting file-based modularity. Instead of a separate package manager, applications and programs are defined in the configuration namespace. This results in a very large namespace, and performance must be mitigated by lazy loading and caching.

aggregate definitions and overrides across multiple DVCS repositories. It also serves the role of package, also serve the role of package manager

A glas namespace  nice systemic features: modular, extensible, scalable, predictable, and programmable.

For modularity, I want the ability to define a namespace across multiple files and DVCS resources. Extensibility means I can introduce new definitions or modify existing definitions without invasively modifying files. Scalability requires parallelism, lazy evaluation, caching of definitions, and access control such that I can work effectively with very large namespaces. Predictability requires a deterministic outcome, automated testing, and effective control of relationships within the namespace. And programmability involves robust procedural generation of a namespace via definitions within the same namespace.


## Representations

I propose to represent a glas namespace *procedurally*: a program that 'writes' definitions when evaluated in a suitable context. At the top level, this procedure will be parameterized by a source file, also serving as a front-end compiler for a specific file extension. This design delegates many representation details to the [glas program model](GlasProg.md), but we must further specify an algebraic effects API and other contextual features.

Some useful types:

        type Name = Binary excluding NULL, prefix-unique, as a bitstring, 
        type Definition = Abstract Assembly (separate doc)
        type NSDict = Map of Name to Definition, as dict 
        type Prefix = Binary excluding NULL, empty up to a full name
        type TLMap = Map of Prefix to (Prefix | NULL), as dict  
           # match longest prefix; NULL in RHS is a deletion or restriction

Algebraic effects API (roughly):

* `define(NSDict)` - emit a batch of definitions
* `move(TLMap)` - apply translation to future assigned names (LHS of NSDict)
* `link(TLMap)` - apply translation to future definitions (RHS of NSDict, eval)
* `fork(N)` - returns non-deterministic choice of natural number 0..(N-1)
* `eval(Definition)` - evaluate under current translation
* `load(URL)` - compile another source file
* `source` - abstract source file (location, version, content, etc.) to support live coding.

This will surely need some tweaking as things are developed, but it's sufficient to discuss implications.

### Prefix Uniqueness and Name Mangling

Names are described as prefix-unique: no name should be a prefix of another name. A violation of prefix uniqueness is a minor issue, worthy only of a warning. However, prefix-uniqueness is what lets us translate "bar" without modifying "bard" and "barrel". To resolve this, a compiler should 'mangle' names, e.g. escaping special characters and adding a ".!" suffix. 

The proposed suffix ".!" serves a role beyond prefix uniqueness. Every definition "bar.!" is implicitly associated with larger composite "bar.\*" for purpose of renames. This allows every definition to be implicitly extended with subcomponents or metadata within the namespace. We'll usually translate "x." to "y." instead of "x" to "y".

*Note:* I don't write names in mangled form for aesthetic reasons, but in practice even primitives would be mangled.

### Composing and Simplifying Translations

We can compose TLMaps sequentially, i.e. `A fby B` to indicate translation A is followed by (fby) translation B. We can evaluate this by adding rules with extended suffixes on both sides of A such that the RHS for A matches every possible LHS prefix for B, then we apply B's rewrites to the RHS of A. Note NULL never appears in the LHS of B, thus erasures are preserved.

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start
          # NOTE: also extend suffixes of implicit "" => "" rule
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes 
            fby { "f" => "xy", "foo" => "z"}     
        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # end

Of course, a namespace evaluator could just as easily just maintain a list of translations without composing it. Composition into one 'flat' TLMap can potentially save a little space or time compared to iterating through a sequence of translations, but in practice the benefit is often negligible or negative.

A TLMap can be simplified insofar as rewrite rules are redundant, e.g. when a rule with a longer prefix is implied by a rule with the next shorter prefix. For example, we don't need a rule `"xy" => "zy"` if the rule `"x" => "z"` exists. And we don't need the rule `"xy" => NULL` if `"x" => NULL` exists.

### Alternative Designs

I could express translations as 'scoped' to definitions generated by a subprogram, instead of a stateful algebraic effect applying to the remaining continuation. This would leave it to 

This would hinder laziness, but it could be resolved by encouraging compilers to scope entire continuations. 

 translations to be 'scoped' to expressions, not applying to the full remaining sequence. 

## Non-Deterministic Choice

The procedural namespace supports non-deterministic choice via 'fork'. In context, this evaluates all choices, and may be implemented by cloning the computation. This is similar to a [transaction loop](GlasApps.md), but simplified because there is no mutable state shared between steps. Use of fork serves as a foundation for iterative procedural generation, flexible evaluation order, and lazy evaluation.

## Lazy Evaluation

To support lazy evaluation, we must have robust metadata regarding which subcomputations are unnecessary. In this role, we'll primarily rely on 'move' translations. For example, while we're looking for the definition of 'foo', we can safely ignore the fork that adds a 'bar.\*' prefix to all newly defined names.

*Note:* Aside from laziness based on definitions, the program model may support laziness at the data layer. This would mostly be limited to purely functional calculations.

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







# Old Content


