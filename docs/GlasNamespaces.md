
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for overrides and recursion. Defined elements may reference each other, and composition generally requires translation of names to avoid conflict or integrate names abstracted by a component. The namespace model described in this document supports mixin inheritance, hierarchical components, and robust access control to names. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md), but it can be adapted to any type where names are precisely recognized and efficiently rewritten.

## Proposed AST

        type NSOp 
              = ns:(Map of Name to Defs, List of NSOp)  # namespace
              | mx:(Map of Prefix to Prefix, List of NSOp) # mixin 
              | ln:(Map of Prefix to Prefix)            # link defs
              | mv:(Map of Prefix to (Prefix|NULL))     # move or remove
              | ld:Def                                  # load
              
        type Name = Symbol                          # assumed prefix unique   
        type Prefix = Symbol                        # empty up to full name
        type Symbol = Bitstring                     # byte aligned, no NULL
        type Map = Dictionary                       # trie; NULL separators
        type Defs = List of Def                     # accept ambiguous defs 
        type Def = abstract assembly

I originally had a few more operations, but I eventually chose to conflate namespace+defs, mixin+translate, and move+remove.

* namespace (ns) - introduce definitions, expressed as a dictionary of definitions together with a sequence of operations to this dictionary. As an operation on a tacit namespace, extends lists of definitions for every defined word.
* link (ln) - modify names within definitions in tacit namespace, based on longest matching prefix.
* move (mv) - move definitions in tacit namespace, based on longest matching prefix, without modifying the actual definition. We express 'remove' by using a NULL byte as the destination; this isn't permitted in a valid Prefix.
* mixin (mx) - apply a sequence of operations to the tacit namespace, with logical translation of each operation based on longest matching prefix. See *Translation of Move and Link*. 
* load (ld) - The Def must evaluate to an NSOp at compile time, and is replaced by this NSOp. This supports staged computing, metaprogramming, and latent modularity (e.g. literal 'load file') of the namespace. It's a staging error if the resulting NSOp touches any definitions its evaluation relied upon.

This current choice of operations combines move and remove, namespace and defs, mixin and translate. I originally had separated these, but conflating them simplifies evaluation. 

## Prefix Unique Names

The current NSOp type makes it difficult to rename or delete 'food' without also renaming or deleting 'foodie'. To resist this problem, a front-end compiler might reserve '#' then implicitly define 'food#' and 'foodie#' under the hood. During evaluation of the namespace, we might also warn when we notice names aren't prefix unique.

That said, it isn't a problem that affects evaluation of the namespace. We can relax prefix uniqueness in some cases. For example, to support associative 'slots' on a name like `food#x`, we might heuristically warn for names that exclude '#' instead of actually checking prefix uniqueness.

## Common Usage Patterns

* A 'rename' involves both 'mv' and 'ln' operations with the same Map. We'll almost never use 'ln' except as part of a full rename, but it's separated to simplify optimizations and rewrite rules.
* To 'override' `foo#` (see *Prefix Unique Names*), we might rename prefix `foo# => foo^#` so we can access the prior (aka 'super') definition, then redefine `foo#` with optional reference to `foo^#`. We must also rename `foo^ => foo^^` to shadow prior overrides.
* To 'shadow' `foo#`, it's essentially same as 'override' except we *rename* `foo# => foo^#` instead of moving it. This preserves existing relationships to the prior definition, where override binds existing references to the new definition.
* To model 'private' definitions, we could prefix private definitions with '~', then systematically rename '~' to a fresh namespace in context of inheritance. 
* To model hierarchical composition, add a prefix to everything (e.g. via 'tl' of empty prefix) then provide missing dependencies via rename or delegation. This is object capability secure, i.e. the hierarchical component cannot access anything that is not provided to it.
* To treat mixins as functions, we can define the mixin against abstract 'components' such as 'arg' and 'result', then apply a translation map the mixin to its context. We might translate the empty prefix to a fresh scratch space to lock down what a mixin can touch.

## Multiple Definitions

The namespace model maintains a list of definitions for each name. To simplify declarative reasoning, this list of definitions is usually processed as a set, i.e. sort and drop duplicates before further processing. After dropping duplicates, most names should have a single, unambiguous definition. The front-end syntax should help users avoid ambiguity via name shadowing and qualified imports.

However, we aren't restricted to singleton sets with unambiguous definitions. It is feasible to leverage a larger set of definitions to express tables, constraints, or multimethods. Some use cases might eventually be built-in, but glas system can also support ad-hoc use cases via `(%defs Name)` returning a set of definitions (as abstract AST nodes). Meanwhile, an empty set is useful to declare a name without defining it.

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

## Private Definitions

In context of modular components, a private definition is an implementation detail and should not be referenced from outside the component. Effective support for private definitions can simplify local reasoning and future changes. 

Assume byte '~', used anywhere in a name, marks it private. The front-end syntax introduces access control features that result in private definitions. For an accept list we might compile `export foo*, bar, qux as q` into translation `{ "" => "~", "foo." => "foo.", "bar#" => "bar#", "qux#" => "q#" }`. For a deny list we might compile `hide foo, bar.*` into translation `{ "foo#" => "~foo^#", "~foo^" => "~foo^^", "bar." => "~bar^.", "~bar^" => "~bar^^" }`. Users never directly write a private name.

Syntactic protections are relatively weak in context of user-defined syntax. We can strengthen privacy protections by analyzing a namespace before it is evaluated: check that 'ns' defs don't initially reference private names, and that all rewrites (prefix to prefix maps) preserve privacy: if '~' is in lhs, it must also appear in rhs. 

## Global Definitions

A global namespace is modeled by implicitly forwarding a namespace such as `g.*` into hierarchical components. For example, if we have hierarchical component 'foo' we might use translation `{ "" => "foo.", "g." => "g." }`. This allows components to share and access definitions with without manual threading, albeit at greater risk of conflict. By explicitly overriding the default behavior, users can sandbox globals or resolve conflicts.

Abstract assembly proposes global namespace `%*` for AST constructors. I am tempted to reserve a region under '%' for user-defined globals, but doing so dilutes purity of purpose and the performance benefits are negligible.

## Abstraction of Call Graphs

For conventional procedural programming, we can hard-code most names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. The namespace model makes this elastic, allowing users to translate names to support hierarchical structure and overrides names. We can leverage this elasticity for limited abstraction, e.g. to redirect or sandbox a name. However, this approach to abstraction is very expensive in cases where we need multiple variations deep within a call graph.

A more efficient solution is to introduce higher order programming at the definition layer. Even if we don't want functions as first-class values (due to awkward feature interaction with live coding, orthogonal persistence, remote procedure calls, etc.) we can support function arguments as implicit parameters (e.g. algebraic effects), or function results via staged programming. In any case, this reduces need for duplication in the namespace layer. Insofar as the higher order program is static or stable, an optimizer can heuristically trade space for speed via partial evaluation. 

Taken to an extreme, we can abstract all the calls. We might allow `(%call StaticDataExpr ArgExpr)` where `%call` looks at an implicit parameter to determine how data is converted to a function name (or callable AST). This supports ad-hoc late binding similar to OOP inheritance and overrides. This implicit parameter could be a function that returns a name or callable AST, likely involving *localizations* (see abstract assembly) so we can securely translate data to an open-ended set of names. Alternatively, a list of functions so we can model layers of logical overlays with 'super' calls in context of overrides.

In any case, the proposed namespace model and abstract assembly are adequate foundations for abstraction, and provide means for the program model to take it further without violating security or performance assumptions.

## Potential Extensions

### Improving Lazy Eval

At the moment, the 'ld' operator hinders lazy evaluation of the namespace because we cannot easily determine or restrict which names 'ld' introduces. Translation can help, but it doesn't prevent 'ld' from introducing names into `sys.*` or `%`. We'll need some form of annotations on namespaces to express assumptions about where definitions are introduced. 

One viable solution is to extend the 'remove' encoding (move to NULL) to support a test assumption, i.e. that nothing was actually removed. This could be encoded as a special remove destination, with a NULL byte. Then we could express that `sys.*` is not defined by 'ld' by adding a remove `sys.` prefix and also warning if something was removed. Lazy eval would easily notice the remove and wouldn't need to include `sys.*` in its considerations for introduction of definitions. Still, this is awkward to encode at every point where it might be needed. 

It may be better to explicitly constrain which definitions may be introduced than to specify where definitions are not introduced. This is feasible, but may require an extra operator to annotate the assumption.

### Copy Operator? Unlikely.

It is feasible to introduce operations to copy all or part of the tacit namespace into a new location. This might be expressed as a `cp:(Map of Prefix to Prefix)` operation, copying definitions based on longest matching prefix, and also moving them to the target prefix and perhaps also performing a link on the copied defs with the same map (for internal consistency). 

However, I don't have a strong use case for this. For the potential use cases I've found it seems better to either abstract construction of a namespace then directly instantiate multiple variations (already supported via 'ld'), or use the layers of overlays mentioned for call graph abstraction. 
