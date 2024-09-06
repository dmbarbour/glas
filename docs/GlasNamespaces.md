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

* *define (df)* - Introduce a set of definitions as a dictionary. This is the canonical form for a fully evaluated namespace. Each name may have *Multiple Definitions*, but is often restricted to the singleton set. 
* *mixin (mx)* - Express a namespace as a set of smaller namespaces and latent translations. This supports composition and deferred evaluation of the namespace.
* *load (ld)* - Express a namespace as an import expression (Expr) and import list (as TLMap). Evaluate Expr then apply TLMap to the returned NSDef. This supports modularity and metaprogramming, but is subject to potential evaluation and staging errors.

We'll generally want lazy partial evaluation for 'load' operations within a namespace. I develop a suitable *Evaluation Strategy* below. We might lazily guard against self definition via 'remove with warning' for the transitive dependencies of Expr.

Description of namespace translations:

* *link (ln)* - rewrites names and localizations within Exprs. This does not affect keys in 'df'. Default is empty.
* *move (mv)* - rewrites keys in 'df' without touching Exprs. Default is empty. Special destinations:
  * NULL - quietly remove definitions. 
  * WARN (NULL 'w') - loudly remove definitions, emitting warning or error. This is useful to enforce assumptions about where definitions are introduced in a mixin.

All rewrites in a translation are applied atomically, favoring the longest matching prefix. Translations are the basis for name shadowing, overrides, hierarchical composition, and robust access control. We'll also look at translations to guide lazy evaluation. However, multiple translation passes over definitions can be too expensive. This is mitigated by *Composition of Translations*. That is, we can heuristically choose to compose the translations then apply a single, lazy translation pass.

## Prefix Unique Names

The proposed namespace model makes it difficult to translate 'food' without also translating 'foodie'. To resist this problem, a front-end compiler should slightly modify names, e.g. adding a suffix that ensures a unique prefix. For example, if we add '#' then we'd define 'food#' vs. 'foodie#', and the two names can now be translated independently. 

However, this isn't a hard requirement of the namespace model. There may be special circumstances where it makes sense to translate a cluster of names together by default, e.g. 'food#' and 'food#type#' or 'food#doc#' where the later items represent associated metadata.

## Translation Patterns

* A rename involves 'mv' and 'ln' operations with the same Map. In practice, we'll usually use 'ln' only as part of a rename, but 'mv' may diverge.  
* To override `foo#` (see *Prefix Unique Names*), we can move `foo# => foo^#`, and simultaneously rename `foo^ => foo^^` to avoid introducing name conflicts. Then users can re-define `foo#` with reference to the prior `foo^#` (perhaps via keyword like 'prior' or 'super').
* To shadow `foo#`, it's essentially same as 'override' except we *rename* `foo# => foo^#` instead of moving it. This preserves existing relationships to the prior definition, where override binds existing references to the new definition.
* To model private definitions, we could reserve '~' for private names, use only rewrites that preserve privacy, and optionally warn if a name directly used in an Expr would violate privacy.
* To model hierarchical components, we can simply add a prefix to everything `{ "" => "foo." }` but also alias some shared components as needed, such as global definitions.
* To model global definitions, we could take a prefix such as "g." and propagate it by default into hierarchical components, e.g. component 'foo' might have a rewrite rule `{ "" => "foo.", "g." => "g." }`. This implicit rewrite is already assumed for AST constructors in abstract assembly.
* To express assumptions about where names are defined in a modular namespace, we can leverage remove with warnings, e.g. move `{ "" => WARN, "foo." => "foo." }` says that the target namespace only introduces `foo.*`, while a move `{ "bar." => WARN }` says the tacit namespace does not introduce `bar.*`. Lazy evaluation of namespaces can benefit from precise expression of assumptions.

## Multiple Definitions

The namespace model maintains a set of definitions for each name. Larger sets are potentially useful for modeling multimethods, tables, or constraint systems. If a program expects a single definition for a name with multiple definitions, we might raise an ambiguity error at compile time. Or we could try to disambiguate based on type and context. This is left to program semantics. 

Although the set of definitions is represented as a list, we're free to sort and eliminate duplicates at any step. I recommend doing so at reflection API boundaries to ensure a deterministic outcome when the set is observed by arbitrary user code.

*Note:* Even without multiple definitions, we can support something similar via careful use of overrides. But overrides are awkward to integrate from multiple sources. Use of multiple definitions may prove more convenient in some use cases.

## Composition of Translations

We can compose translations sequentially, where one translation is followed by another. It can be useful for performance to compose translations instead of applying multiple sequential translations to Exprs, especially when the Exprs are large or when the composed translation is applied to many Exprs. Of course, composition of translations also has a cost, so there are some heuristic decisions involved.

To clarify, by composition of translations, I mean to compose the 'TLMap' type, producing a new TLMap. 

             TLMapA      fby     TLMapB       =>      TLMapAB
        (mv:MVA, ln:LNA) fby (mv:MVB, ln:LNB) => (mv:MVAB, ln:LNAB)

The 'mv' and 'ln' components of a TLMap can be composed independently. The basic approach for 'A fby B' is to extend implicit suffixes on both sides of A so the output for A matches input for B, then apply B's rules to the RHS of of the modified A, using the longest matching prefix in B in each case.

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start

        # note that we also extend suffixes of the implicit "" => "" rule
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes 
            fby { "f" => "xy", "foo" => "z"}     

        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # end

Suffixes may be extended insofar as the resulting rule is implied by a rewrite rule on a shorter prefix, and doesn't conflict with an existing rewrite on a longer prefix. Conversely, we can simplify a translation map by erasing implied rewrites, e.g. we don't need `"fad" => "bad"` if our next longest matching prefix rule is `"fa" => "ba"`, and we don't need `"xyz" => NULL` if we have `"xy" => NULL`. So what we're really doing by extending suffixes is un-simplifying the left hand map.

Conveniently, the NULL and WARN cases in 'mv' don't need any special handling because they cannot be matched by any valid prefix. Thus, we won't extend any suffixes, and we'll simply preserve remove rules from MVA, and potentially add new remove rules based on what MVB does match in the expanded MVA. The only thing we might do is simplify remove rules that are implied by a shorter rule in the composition.

*Note:* In context of the NSDef, the translation closer to the leaf node is 'followed by' the translation closer to the root node. 

### Alt: List of Translations

Instead of properly composing TLMap, we could produce an intermediate representation that maintains lists of rewrite rules.

        type TLMapExt = ( lln:List of (Map of Prefix to Prefix)
                        , lmv:List of (Map of Prefix to (Prefix | NULL | WARN))
                        )

With this, we can still walk each Expr only once to perform the rewrite, it's only the individual `n:Name` elements in the abstract assembly that must iterate through the list of link rewrites. (Localizations could also maintain the list of link rules, with runtime support.) The 

This should perform adeq

This avoids the cost and complexity of composition and still prevents the overhead of walking Exprs multiple times. Instead, when rewriting a name, we'll need to iterate through every TLMap in the list. This should perform well enough if our chain of dependencies isn't too large. 

 unless our translations are absurdly deep. But I think proper composition of TLMap isn't so complicated or expensive as to justify this.

## Computation

Aside from 'load', which is left to the program semantics, the most complicated computation we need is translation of move and link. However, for performance it is also useful to compose adjacent moves and links, or to translate translations, i.e. 'fby' composition. We might also simplify by removing redundant operations.

Composing 'link' operations rather than walking large definitions many times is especially useful for performance.


### Followed By (fby) Composition of Move, Link, Translate Maps

The prefix maps in 'mv', 'ln', and 'mx' represent an atomic sets of rewrites. This atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step to avoid name collisions. For every name, we find the longest matching prefix in the map then apply it. This does imply we cannot casually separate operations into smaller steps.

However, it is feasible to compose sequential rewrites and moves. For example, `

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


