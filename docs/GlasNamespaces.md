
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for overrides and recursion. Defined elements may reference each other, and composition generally requires translation of names to avoid conflict or integrate names abstracted by a component. The namespace model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md), but it can be adapted to any type where names are precisely recognized and efficiently rewritten.

## Proposed AST

        type NSOp 
              = ns:(Map of Name to Defs, List of NSOp)  # namespace
              | mx:(Map of Prefix to Prefix, List of NSOp) # mixin 
              | ln:(Map of Prefix to Prefix)            # link defs
              | mv:(Map of Prefix to (Prefix|NULL))     # move or delete
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
* move (mv) - move definitions in tacit namespace, based on longest matching prefix, without modifying the actual definition. We can express 'remove' by using NULL byte as destination.
* mixin (mx) - apply a sequence of operations to the tacit namespace, with logical translation of each operation based on longest matching prefix. See *Translation of Move and Link*. 
* load (ld) - The Def must evaluate to an 'ns' NSOp at compile time. Evaluation must be acyclic in the sense that it doesn't depend on any definitions it introduces, and this should be checked. Load is useful for modularity, staged metaprogramming, and higher order abstraction of namespaces, but is also subject to ad-hoc evaluation errors and potential divergence.

This current choice of operations combines move and remove, namespace and defs, mixin and translate. I originally had separated these, but conflating them simplifies evaluation. Access control is robustly supported via translation.

## Prefix Unique Names

The current NSOp type makes it difficult to rename or delete 'food' without also renaming or deleting 'foodie'. To prevent this problem, we'll assume 'prefix unique' names: no full name is a prefix of another name. An evaluator of NSOp might issue a warning when it detects names are not prefix unique. A front-end compiler might reserve '#' then implicitly define 'food#' and 'foodie#' under the hood, ensuring prefix uniqueness by default.

For the remainder of this document, the compiler added suffix is implicit.

## Common Usage Patterns

* A 'rename' involves both 'mv' and 'ln' operations with the same Map. We'll almost never use 'ln' except as part of a full rename, but it's separated to simplify optimizations and rewrite rules.
* To 'override' `foo#` (see *Prefix Unique Names*), we might rename prefix `foo# => foo^#` so we can access the prior (aka 'super') definition, then redefine `foo#` with optional reference to `foo^#`. We must also rename `foo^ => foo^^` to shadow prior overrides.
* To 'shadow' `foo#`, it's essentially same as 'override' except we *rename* `foo# => foo^#` instead of moving it. This preserves existing relationships to the prior definition, where override binds existing references to the new definition.
* To model 'private' definitions, we could prefix private definitions with '~', then systematically rename '~' to a fresh namespace in context of inheritance. 
* To model hierarchical composition, add a prefix to everything (e.g. via 'tl' of empty prefix) then provide missing dependencies via rename or delegation. This is object capability secure, i.e. the hierarchical component cannot access anything that is not provided to it.
* To treat mixins as functions, we can define the mixin against abstract 'components' such as 'arg' and 'result', then apply a translation map the mixin to its context. We might translate the empty prefix to a fresh scratch space to lock down what a mixin can touch.

## Multiple Definitions

The namespace model maintains a list of definitions for each name. 

To simplify declarative reasoning, the list of definitions is usually processed as a set: we might sort the list and drop duplicates before further processing. After removing duplicates, most names should have a single, unambiguous definition. The front-end syntax can resist ambiguity via name shadowing and access control.

However, it is feasible to leverage a larger set of definitions to express tables, constraints, or multimethods. Some use cases might eventually be built-in, but glas system can also support ad-hoc use cases via `(%defs Name)` (in abstract assembly) evaluating at compile time to set of AST nodes. Meanwhile, an empty set is useful to declare a name without defining it.

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

*Note:* Although moves have a special case with a NULL byte in the rhs, this doesn't impact implementation because we'll never match a symbol starting with NULL on a subsequent move.

### Translation of Move and Link

Assume translation rule `{ "" => "scratch." , "src." => "foo." , "dst." => "bar." }`, representing that we apply a mixin to a scratch space with specified regions for input or output. In this case, we must translate a move or link to operate on the translated namespace locations. For example, moving `{ src.x => dst.y }` becomes `{ foo.x => bar.y }`. And moving `{ x => dst.z }` becomes `{ scratch.x => bar.z }`. Essentially, we apply the translation independently to each prefix in the move and link. 

As with fby composition, in general we must 'un-simplify' our move or link maps to include longer matching prefixes where possible. For example, moving `{ sr => ds }` will first un-simplify to `{ sr => ds , "src." => "dsc." , "srt." => "dst." }`, then we can translate to `{ scratch.sr => scratch.ds , "foo." => "scratch.dsc." , "scratch.srt." => "bar." }`. 

*Note:* In practice, it should be rare that we must significantly expand rewrites. Most examples I can think of either require weird alignment with meaningful name fragments (like `sr => ds` above) or violate the [law of Demeter](https://en.wikipedia.org/wiki/Law_of_Demeter). 

### Translation of Definitions and Namespaces

When applied to final definitions we can simply convert translate to a rename, i.e. move and link with same map. For example, if we define `dst.xyzzy` and our translation is `"dst." => "bar."` then we actually defined `bar.xyzzy`. If this depends on `src.qux` and we translate `"src." => "foo."` then we actually depend on `foo.qux`. 

## Private Definitions

In context of modular subcomponents, a private definition is an implementation detail and should not be referenced from outside the module. Protection of private definitions can simplify future changes. Although the proposed namespace model doesn't have a built-in notion of privacy, it can be supported between simple conventions and lightweight static analysis:

* We reserve a byte, proposed '~', to indicate private symbols.
* We introduce '~' only in the rhs of link, move, or translate.
* If '~' appears in lhs of rewrite, it must also appear in rhs.
* We analyze for improper '~' prior to evaluation of namespace.
* We may also analyze to forbid ambiguity between private defs.

Effectively, these rules enforce a contagion model: a symbol never starts private, but becomes private through a rewrite then remains private indefinitely. 

In addition to the analysis, we need front-end-syntax for introducing privacy. Two useful patterns for access control are an accept list and a deny list. For an accept list we might compile `export foo*, bar, qux as q` into translation `{ "" => "~", "foo." => "foo.", "bar#" => "bar#", "qux#" => "q#" }`. For a deny list we might compile `hide foo, bar.*` into translation `{ "foo#" => "~foo^#", "~foo^" => "~foo^^", "bar." => "~bar^.", "~bar^" => "~bar^^" }`.

*Note:* In practice, we can resist accidental privacy violations by simply forbidding '~' in the front-end syntax for names. Analysis at the namespace layer offers additional protection in context of metaprogramming, but we can easily do without.

## Global Definitions

A global namespace can be modeled by implicitly forwarding a namespace such as `g.*` into hierarchical components. For example, if we have hierarchical component 'foo' we might use translation `{ "" => "foo.", "g." => "g." }`. This allows components to share and access definitions with without manual threading, albeit at greater risk of conflict. Usefully, this isn't a true global namespace. By overriding the default behavior, users can sandbox globals or resolve conflicts.

Abstract assembly proposes global namespace `%*` for AST constructors. We can reduce overheads a little by reserving space under '%' for user-defined globals. This would work best with some specialized syntax for globals. 

## Potential Extensions

### Mapping over Definitions? Tentative.

Currently, our only operation that touches existing definitions is link (ln). I'm contemplating an extension that would modify definitions in some way, e.g. to apply a function to every definition in a namespace, or perhaps rewrite the tacit namespace as a whole using only definitions from that namespace.

I've decided to hold off on this feature because I don't have a strong use case. Also, I think 'ld' adequately covers most use cases I'm interested in, even if it's a little awkward.

### Conditional Definitions? Rejected.

It is feasible to extend namespaces with conditional definitions, i.e. some equivalent of 'ifdef' that depends only on the set of defined names. But this complicates local reasoning about the namespace, making the order of definitions relevant. I'd prefer to avoid using this directly. That said, we can still support an 'ifdef' within definitions in the abstract assembly, assuming we're careful about interaction with 'ld'. 

### Annotated Operations? Rejected.

It is feasible to introduce an NSOp for annotations, but I don't see any need for it. The simplicity and guaranteed termination when evaluating NSOp reduces need for annotations to control, precisely optimize, or debug the intermediate states. Annotations are instead represented within definition or namespace layers.

