# Glas Namespaces

For glas configurations and applications, we need modular namespaces with support for overrides, name shadowing, and robust access control. Other desiderata include lazy evaluation with indexed access, canonical form as a flat dictionary, and flexible metaprogramming.

This document develops a simple namespace model to support these goals, based primarily on prefix-oriented translations of names. I assume definitions and import expressions are both represented using an [abstract assembly](AbstractAssembly.md). This enables us to precisely recognize and rewrite names within definitions, and also capture translations for localization or redirects. We can reduce translations to a single lazy pass to control overhead for rewrites.

## Proposed AST

        type NSDef
            = df:(Map of Name to (Set of Expr))             # define
            | mx:(TLMap, Set of NSDef)                      # mixin
            | ld:(TLMap, Expr)                              # load 
        type TLMap =
            ( mv?(Map of Prefix to (Prefix | NULL | WARN))  # move
            , ln?(Map of Prefix to Prefix)                  # link
            )

        type Name = Symbol                  # assumed prefix unique   
        type Prefix = Symbol                # empty up to full name
        type Symbol = Bitstring             # byte aligned, no NULL
        type WARN = NULL 'w' (0x00 0x77)    # remove with a warning
        type Map = Trie                     # a NULL byte separator
        type Set = List                     # ignore order and dups
        type Expr = abstract assembly       # something we evaluate

Description of namespace constructors:

* *define (df)* - Introduce a set of definitions as a dictionary. Also is the canonical form for a fully evaluated namespace. Each name may have a set of definitions, but this *usually* must be a singleton set. See *Multiple Definitions*. 
* *mixin (mx)* - Express a namespace as a set of smaller namespaces and some latent translations. This supports composition and deferred evaluation. 
* *load (ld)* - Evaluate an import expression (Expr), then apply an import list (TL) to the resulting namespace (NSDef). Supports modularity and metaprogramming. It's a staging error for load to influence its own evaluation. 

We'll generally want lazy partial evaluation for 'load' operations within a namespace. I develop a suitable *Evaluation Strategy* below. We might lazily guard against self definition via 'remove with warning' for the transitive dependencies of Expr.

Description of namespace translations:

* *link (ln)* - rewrites names and localizations within Exprs. This does not affect keys in 'df'. Default is empty.
* *move (mv)* - rewrites keys in 'df' without touching Exprs. Default is empty. Two special destinations:
  * move to NULL - quietly remove definitions
  * move to WARN (NULL 'w') - loudly remove definitions (warning or error)

All rewrites in a translation are applied atomically, based on longest matching prefix. Translations are the basis for name shadowing, overrides, hierarchical composition, and robust access control. We'll also look at translations to guide lazy evaluation. However, multiple translation passes over definitions can be too expensive. This is mitigated by *Composition of Translations*. That is, we can heuristically choose to compose the translations then apply a single, lazy translation pass.

## Prefix Unique Names

The proposed 'df' type makes it difficult to rename or delete 'food' without also renaming or deleting 'foodie'. To resist this problem, a front-end compiler should add a suffix to each name that ensures every name has its own unique prefix. For example, if we add '#' then we'd define 'food#' vs. 'foodie#'.

That said, this isn't a hard requirement. We might report some warnings if prefix uniqueness of names is violated, yet allow it under known naming conventions where renaming or deleting the whole group is acceptable (e.g. both 'food#' and 'food#type').

## Translation Patterns

* A rename involves 'mv' and 'ln' operations with the same Map. In practice, we'll usually use 'ln' only as part of a rename, but 'mv' may diverge.  
* To override `foo#` (see *Prefix Unique Names*), we can move `foo# => foo^#`, and simultaneously rename `foo^ => foo^^` to avoid introducing name conflicts. Then users can re-define `foo#` with reference to the prior `foo^#` (perhaps via keyword like 'prior' or 'super').
* To shadow `foo#`, it's essentially same as 'override' except we *rename* `foo# => foo^#` instead of moving it. This preserves existing relationships to the prior definition, where override binds existing references to the new definition.
* To model private definitions, we could reserve '~' for private names, use only rewrites that preserve privacy, and optionally warn if a name directly used in an Expr would violate privacy.
* To model hierarchical components, we can simply add a prefix to everything `{ "" => "foo." }` but also alias some shared components as needed, such as global definitions.
* To model global definitions, we could take a prefix such as "g." and propagate it by default into hierarchical components, e.g. component 'foo' might have a rewrite rule `{ "" => "foo.", "g." => "g." }`. This implicit rewrite is already assumed for AST constructors in abstract assembly.
* To express assumptions about where names are defined in a modular namespace, we can leverage remove with warnings, e.g. move `{ "" => WARN, "foo." => "foo." }` says that the target namespace only introduces `foo.*`, while a move `{ "bar." => WARN }` says the tacit namespace does not introduce `bar.*`. Lazy evaluation of namespaces can benefit from precise expression of assumptions.

## Multiple Definitions

The namespace model maintains a set of definitions for each name. In many cases, this must be a singleton set, but larger sets are potentially convenient when modeling multimethods, constraint systems, or logic-relational tables. If a name has multiple distinct definitions where one is expected, we'll generally raise an ambiguity error at compile time.

Although the set of definitions is represented as a list, we're free to sort and eliminate duplicates at any step. I recommend doing so at reflection API boundaries to ensure a deterministic outcome when the set is observed by arbitrary user code.

*Note:* Even without multiple definitions, we can support something similar via careful use of overrides. But overrides are awkward to integrate from multiple sources. Use of multiple definitions may prove more convenient in some use cases.

## Composition of Translations



## Computation

Aside from 'load', which is left to the program semantics, the most complicated computation we need is translation of move and link. However, for performance it is also useful to compose adjacent moves and links, or to translate translations, i.e. 'fby' composition. We might also simplify by removing redundant operations.

Composing 'link' operations rather than walking large definitions many times is especially useful for performance.

### Simplifying Move, Link, and Translate Maps

We can eliminate redundant rewrites. For example, if we have a rewrite `fa => ba` then we don't need a longer rewrite `fad => bad` because that is already implied. In general, we can remove a rule that's implied by the next longest matching prefix.

Ignoring this simplification won't hurt performance much, but it isn't difficult to implement. Importantly, the simplification can also be reversed, i.e. we can add redundant rules to a rewrite map. This is used when composing rewrites with fby and translation.

### Followed By (fby) Composition of Move, Link, Translate Maps

The prefix maps in 'mv', 'ln', and 'mx' represent an atomic sets of rewrites. This atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step to avoid name collisions. For every name, we find the longest matching prefix in the map then apply it. This does imply we cannot casually separate operations into smaller steps.

However, it is feasible to compose sequential rewrites and moves. For example, `{ bar => fo } fby { f => xy, foo => z }` can compose to `{ bar => xyo, baro => z, f => xy, foo => z }`. 

To implement this, we first extend `{ bar => fo }` with redundant rules such that the rhs contains all possible prefixes matched in `{ f => xy, foo => z }`: `{ bar => fo, baro => foo, f => f, foo => foo }`. Then we apply `{ f => xy, foo => z }` to the rhs, resulting in `{ bar => xyo, baro => z, f => xy, foo => z }`.  Effectively, we unsimplify, rewrite some rules, then simplify again.

*Note:* No special rules for removes because we won't match a NULL.

### Translation of Move and Link

Assume translation rule `{ "" => "scratch." , "src." => "foo." , "dst." => "bar." }`, representing that we apply a mixin to a scratch space with specified regions for input or output. In this case, we must translate a move or link to operate on the translated namespace locations. For example, moving `{ src.x => dst.y }` becomes `{ foo.x => bar.y }`. And moving `{ x => dst.z }` becomes `{ scratch.x => bar.z }`. Essentially, we must apply the translation independently to lhs and rhs of move and link.

As with fby composition, in general we must 'un-simplify' our move or link maps to include longer matching prefixes where possible. For example, moving `{ sr => ds }` will first un-simplify to `{ sr => ds , "src." => "dsc." , "srt." => "dst." }`, then we can translate to `{ scratch.sr => scratch.ds , "foo." => "scratch.dsc." , "scratch.srt." => "bar." }`. As a special case, 'unsimplify' of removes doesn't add a suffix to the rhs.

*Note:* In practice, it should be rare that we must significantly expand rewrites. Most examples I can think of either require weird alignment with meaningful name fragments (like `sr => ds` above) or violate the [law of Demeter](https://en.wikipedia.org/wiki/Law_of_Demeter). 

### Translation of Definitions and Namespaces

When applied to a final 'ns', we can trivially convert a translate to a rename. This might be represented by addending move + link operators to ns with the translation map. For example, if we define `dst.xyzzy` and our translation is `"dst." => "bar."` then we actually defined `bar.xyzzy`. If the definition depends on `src.qux` and we translate `"src." => "foo."` then we actually depend on `foo.qux`. We're always rewriting full names on the ns.

### Lazy Evaluation

An NSOp that isn't observed can be lazily evaluated depending on evaluation context. This is useful for lazy loading of files or lazy evaluation of namespace layer metaprogramming. So, a useful consideration is how to extract a subset of definitions from a namespace without observing every NSOp. 

The 'ns' operator can be evaluated backwards, extracting 'ns' defs from end of list. If we recognize a move ('mv') that would prevent us from introducing the relevant definition, we don't need to evaluate further. Similarly, a subset of mixin 'mx' operators can be skipped depending on the translation. If they cannot be skipped, we can lazily apply the translation to every mixin operator then flatten into the parent scope.

Of course, definitions can also be lazily evaluated.

## Some Useful Patterns

### Private Definitions

In context of modular components, a private definition is an implementation detail and should not be referenced from outside the component. Effective support for private definitions can simplify local reasoning and future changes. 

Assume byte '~', used anywhere in a name, marks it private. The front-end syntax introduces access control features that result in private definitions. For an accept list we might compile `export foo*, bar, qux as q` into translation `{ "" => "~", "foo." => "foo.", "bar#" => "bar#", "qux#" => "q#" }`. For a deny list we might compile `hide foo, bar.*` into translation `{ "foo#" => "~foo^#", "~foo^" => "~foo^^", "bar." => "~bar^.", "~bar^" => "~bar^^" }`. Users never directly write a private name.

Syntactic protections are relatively weak in context of user-defined syntax. We can strengthen privacy protections by analyzing a namespace before it is evaluated: check that 'ns' defs don't initially reference private names, and that all rewrites (prefix to prefix maps) preserve privacy: if '~' is in lhs, it must also appear in rhs. 

### Global Definitions

A global namespace is modeled by implicitly forwarding a namespace such as `g.*` into hierarchical components. For example, if we have hierarchical component 'foo' we might use translation `{ "" => "foo.", "g." => "g." }`. This allows components to share and access definitions with without manual threading, albeit at greater risk of conflict. By explicitly overriding the default behavior, users can sandbox globals or resolve conflicts.

Abstract assembly proposes global namespace `%*` for AST constructors. I am tempted to reserve a region under '%' for user-defined globals, but doing so dilutes purity of purpose and the performance benefits are negligible.

### Abstraction of Call Graphs

For first-order procedural programming, we can hard-code names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. The namespace model makes this elastic, allowing users to translate names to support hierarchical structure and overrides names. We can leverage this elasticity for limited abstraction, e.g. to redirect or sandbox a name. However, this approach to abstraction is very expensive where we want many variations of a large call graph. 

To solve this, we can support staged higher order programming directly in the program definition layer. Further, it is feasible to restrict higher order programming to static parameters. An optimizer can heuristically perform partial evaluation and inlining.

An intriguing opportunity is to apply indirection to all call targets, i.e. let all edges in a call graph become arcs through an implicit 'call context' parameter, possibly static. Instead of calling a name directly, we might use `(%call "foo" ArgExpr)` then the call context maps `"foo"` to a function name or other callable AST. To support bindings to an open namespace (without breaking namespace security assumptions), the call context can can leverage *Localizations* as described in [abstract assembly](AbstractAssembly.md). To support logical overlays and overrides with 'super' calls, call context can be structured as a list of layers.

There is probably some awkward feature interaction, e.g. private utility definitions probably shouldn't be subject to override, so we might need some special attention to that. But we don't need a new NSOp for this role, just careful design of the front-end syntax and intermediate AST.

## Potential Extensions

### Copy Operator? Unlikely.

It is feasible to introduce operations to copy all or part of the tacit namespace into a new location. This might be expressed as a `cp:(Map of Prefix to Prefix)` operation, copying definitions based on longest matching prefix, and also moving them to the target prefix and perhaps also performing a link on the copied defs with the same map (for internal consistency). 

However, I don't have a strong use case for this. For the potential use cases I've found it seems better to either abstract construction of a namespace then directly instantiate multiple variations (already supported via 'ld'), or use the layers of overlays mentioned for call graph abstraction. 

### Load Operator? Tentative Redesign.

We could introduce a 'load' operator `ld:Def` that evaluates Def to an NSOp within the namespace context (subject to late binding and overrides) then rewrites to the returned NSOp. The challenge is ensuring this Def doesn't affect any names that it depends upon. This feature would be very convenient for lazy loading of dependencies and metaprogramming subject to overrides and parameterization via the host namespace.

I might need a different namespace model to make this feasible.

## Alternative Namespace Model

Some desired areas of improvement: 

* assume syntactic avoidance of name conflicts but detect errors early
* stable structure: don't 'modify' the tacit namespace, only add to it 
* support for load expressions that are evaluated within the namespace

I think this is doable, and it should even be simpler than the older model. Since we aren't touching the namespace layer, we don't need 'mv' or 'ln', but we do need to translate our load expressions. 

We do lose the 'remove with warnings' feature to help resist conflicts and guide lazy evaluation, but I that's a minor issue given it never really helped with user-defined globals in any case, and we don't need to consider it for `%*` or `sys.*` when we're implicitly receiving those from the runtime. The problem before had a lot to do with composing multiple definitions per word, and we're assuming that's a non-issue.


