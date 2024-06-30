
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for overrides and recursion. Defined elements may reference each other, and composition generally requires translation of names to avoid conflict or integrate names abstracted by a component. The namespace model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md), but it can be adapted to any type where names are precisely recognized and efficiently rewritten.

## Proposed AST

        type NSOp 
              = ns:(Map of Name to Def, List of NSOp)   # namespace
              | mx:(List of NSOp)                       # mixin 
              | ln:(Map of Prefix to Prefix)            # link defs
              | mv:(Map of Prefix to Prefix)            # move defs
              | rm:(Map of Prefix to unit)              # remove defs
              | tl:(Map of Prefix to Prefix, List of NSOp) # translate
              
        type Name = Symbol                          # assumed prefix unique   
        type Prefix = Symbol                        # empty up to full name
        type Symbol = Bitstring                     # byte aligned, no NULL
        type Map = Dictionary                       # trie; NULL separators
        type Def = abstract assembly                

* namespace (ns) - describes a dictionary as a set of definitions (df) together with a sequence of operations to modify that set. When applied as an operator, we'll evaluate the final dictionary then unify it with the tacit namespace.
* mixin (mx) - a sequence of operations that we can apply to a tacit namespace. This is our primary joiner for NSOps, i.e. it's how we express a sequence. Also encodes the identity operation (no-op) as `mx:[]`. 
* link (ln) - modify names within definitions in tacit namespace, based on longest matching prefix.
* move (mv) - move definitions in tacit namespace, based on longest matching prefix, without modifying the actual definition.
* remove (rm) - remove (delete) definitions from the tacit namespace.
* translate (tl) - a variation of mixin that also rewrites a sequence of operations to apply to different locations. This includes renames on introduced definitions, but the trickier bit is translating moves, links, and removes (see *Translation of Move, Link, Remove*). The primary use case for translation is to abstract application of mixins.

I have some tentative extensions in mind, such as copy and mapping a function over definitions. But I believe the above is sufficient for glas systems.

## Prefix Unique Names

The current NSOp type makes it difficult to rename or delete 'food' without also renaming or deleting 'foodie'. To prevent this problem, we'll assume 'prefix unique' names: no full name is a prefix of another name. An evaluator of NSOp might issue a warning when it detects names are not prefix unique. A front-end compiler might reserve '#' then implicitly define 'food#' and 'foodie#' under the hood, ensuring prefix uniqueness by default.

For the remainder of this document, the compiler added suffix is implicit.

## Common Usage Patterns

* A 'rename' involves both 'mv' and 'ln' operations with the same Map. We'll almost never use 'ln' except as part of a full rename, but it's separated to simplify optimizations and rewrite rules.
* To 'override' `foo`, we rename prefix `foo^ => foo^^` to open space, move `foo => foo^` so we can access the prior (aka 'super') definition, then define `foo` with optional reference to `foo^`. Alternatively, we could remove then redefine `foo` if we don't need the old version.
* To 'shadow' `foo`, we rename prefix `foo^ => foo^^` to open space, *rename* `foo => foo^` so existing references to `foo` are preserved, then define `foo` with optional reference to `foo^`. 
* To model 'private' definitions, we prefix private definitions with '~', then we systematically rename '~' in context of inheritance. The syntax doesn't need to provide direct access to '~'.
* To model hierarchical composition, add a prefix to everything (e.g. via 'tl' of empty prefix) then provide missing dependencies via rename or delegation. This is object capability secure, i.e. the hierarchical component cannot access anything that is not provided to it.
* To treat mixins as functions, we can define the mixin against abstract 'components' such as 'arg' and 'result', then apply a translation map the mixin to its context. We might translate the empty prefix to a fresh scratch space to lock down what a mixin can touch.

## Unambiguous Definitions and Multiple Inheritance

To detect accidental name collisions in context of moves, renames, and multiple inheritance, we'll treat it as an ambiguity error when a name is assigned two or more different definitions. To override, the prior definition must first be explicitly moved or removed. To shadow a word, the prior definition must be renamed. 

However, it is unambiguous if the same definition is assigned to a name many times. And we can leverage redundant expression as a lightweight verification of interfaces, i.e. that multiple components sharing an interface have the same expectations based on matching documentation or type annotations.

*Note:* I think it's best if the programming language explicitly represents user expectations, i.e. whether we are introducing, overriding, or shadowing a definition.

## Computation

I think that basic application of link, move, and remove is obvious enough. Unification of definitions

### Simplifying Move, Link, and Translate Maps

We can eliminate redundant rewrites. For example, if we have a rewrite `fa => ba` then we don't need another rewrite `fad => bad`. 

We can potentially recognize and eliminate redundant rewrites as a map is constructed, i.e. when adding a rule we can first check whether it's redundant with the next shortest prefix, then we can check and eliminate any redundant longer prefixes. That said, ignoring simplification is unlikely to significantly hurt performance.

More usefully, we can 'un'-simplify maps and introduce redundant rewrites as part of composing or translating a map. This is a useful way to understand compositions and translations.

### Followed By (fby) Composition of Move, Link, Translate Maps

The maps in 'mv', 'ln', and 'tl' represent an atomic sets of rewrites. This atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step to avoid name collisions. For every name, we find the longest matching prefix in the map then apply it. This does imply we cannot casually separate operations into smaller steps.

However, it is feasible to compose sequential rewrites and moves. For example, `{ bar => fo } fby { f => xy, foo => z }` can compose to `{ bar => xyo, baro => z, f => xy, foo => z }`. 

To implement this, we first extend `{ bar => fo }` with redundant rules such that the right-hand side contains all possible prefixes matched in `{ f => xy, foo => z }`: `{ bar => fo, baro => foo, f => f, foo => foo }`. Then we apply `{ f => xy, foo => z }`, resulting in `{ bar => xyo, baro => z, f => xy, foo => z }`.  Un-simplify, rewrite, then simplify again (as needed).

The motive for this composition is performance, especially for 'ln' where we can reduce how often we walk large definitions. With a little laziness, we can reduce rewrites to a single pass per definition. 

### Translation of Move, Link, Remove

Assume translation rule `{ => scratch. , src. => foo. , dst. => bar. }`. This is a prototypical example for mixins as functions. In this case, we can translate a move, link, or remove to operate on the translated namespace locations. For example, moving `{ src.x => dst.y }` becomes `{ foo.x => bar.y }`. And moving `{ x => dst.z }` becomes `{ scratch.x => bar.z }`. Essentially, we apply the translation independently to each prefix in the move, link, or remove. 

However, as with the followed-by composition, there are cases where we must first 'un-simplify' the move to include longer prefixes. For example, moving `{ sr => ds }` will first un-simplify to `{ sr => ds , src. => dsc. , srt. => dst. }`, then we can translate to `{ scratch.sr => scratch.ds , foo. => scratch.dsc. , scratch.srt. => bar. }`. This should be rare in practice: usually operations such as move, rename, remove, or translate will all be aligned to similar hierarchical application components.

### Translation of Definitions and Namespaces 

When applied to final definitions we can simply convert translate to a rename (move and link) on those definitions.

That is, if we define `dst.xyzzy` and our translation is `dst. => bar.` then we actually defined `bar.xyzzy`. If `dst.xyzzy` depends on `src.qux` and we translate `src. => foo.` then `dst.xyzzy` actually depends on `foo.qux`. 

### Composition and Simplification of Removes

Composition of removes is relatively trivial: take the union of prefixes. We can simplify at the same time: we only need to keep the shortest prefix for each remove.

### Pushing Removes ahead of Moves

It is feasible to push removes ahead of moves. In this case, our maps are asymmetric: `mv:{ bar => fo } fby rm:{ foo }`. As with the previous forms, we would first un-simplify the move map to include prefixes that we'll be removing: `{ bar => fo, baro => foo, foo => foo }`. Then we identify which prefixes are removed from the right-hand side. The main difference is that we'll have two maps at the end, one for removes and one for moves: `rm:{ baro, foo } fby mv:{ bar => fo }`. 

This is potentially useful as a simplification. By performing removes ahead of other operations, especially ahead of link (ln), we can reduce the number of definitions we'll eventually walk. 

### Identifying Definitions

If we only want to determine whether a namespace defines a specific name, or produce the list of defined names under a given prefix, we can develop a much more efficient evaluator for just these roles. In particular, we can avoid 'ln' and we might simply assume definitions are unambiguous rather than testing for it.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. The syntax resists accidental reference to private symbols. However, syntactic protection is weak and easily bypassed in glas systems, where user-defined syntax is supported. Better to provide a bypass that is easily discovered by search or linter.

Where robust privacy is required, we should instead rely on the namespace to control access to names. The namespace supports [ocap security](https://en.wikipedia.org/wiki/Object-capability_model) for hierarchical components. For example, if we rename `'' => 'foo.'` in a hierarchical component then it cannot access any names outside of `'foo.'` unless they are provided by the host. Providing methods to subcomponents can be expressed via another prefix rename (e.g. `'foo.sys.' => 'sys.'`) or abstracted via mixins (perhaps translating `'dst.' => 'foo.'`).

Nominative data types, implicit parameters, and algebraic effects may also be tied to the namespace, providing an effective basis to control access.

## Annotations and Associated Definitions

For each user-defined method, there might be several 'slots' defined in the namespace - e.g. representing declared types, the function code, and perhaps even a macro for invoking that code (to unify methods and macros, similar to fexprs). This is left entirely to the language design.

## Global Namespace

This namespace model doesn't directly support global names, but by convention we could implicitly propagate access to a namespace of globals into every hierarchical subcomponent. For every component 'foo', the implicit rename rule might be `{ "" => "foo.", "globals." => "globals." }`. With this, the hierarchical component can both reference and define globals. Further, we could still support translate globals to support intervention, overrides, and integration.

Of course, 'globals.' is a long prefix for this role. A single character is sufficient. In context of [abstract assembly](AbstractAssembly.md) the front-end syntax might propagate '%' as a global namespace. To avoid additional rewrite rules, we could reserve '%.' or similar for user-defined globals.

Support for globals is familiar and can improve concision when integrating components with their environments. Similar to conventional 'imports' of libraries, we could load and define the same globals many times, leveraging the ability to merge identical definitions. We would only need to resolve actual conflicts. That said, I'm not convinced this is a feature I want to encourage for glas systems.

## Potential Extensions

### Copy

We could introduce a copy operation, `cp:NSOp`, that will operate on a copy of the tacit namespace.

        ns:(df:Defs, mx:(cp:Op, ...)) =>
            ns:(df:Defs, mx:(ns:(df:Defs, mx:[Op])))


* copy (cp) *(tentative)* - apply an operation to a *copy* of the tacit namespace, then unify with the tacit namespace. This would provide a basis for copy-modify patterns within a namespace. I think most models could avoid it, instead preferring to directly re-apply the namespace values.

I'm also contemplating some ways to modify definitions other than 'link'. For example, it might be useful to have some way to rewrite definitions, or at least apply a named function to the definitions. This could feasibly be applied to the tacit namespace, or similar to a translation.


### Mapping over Definitions? Tentative.

Currently, our only operation that touches definitions is link (ln). I'm contemplating an extension that would modify definitions in some way, e.g. to apply a function to every definition in a namespace, or perhaps more similar to a translation.

I've decided to hold off on this feature because I don't have a clear use case. It might potentially be useful for 'sandboxing' definitions. But it might prove more convenient and precise to simply design an AST with hooks to be sandboxing friendly.

### Conditional Definitions? Rejected.

It is feasible to extend namespaces with conditional definitions, i.e. some equivalent of 'ifdef' that depends only on the set of defined names. But this complicates local reasoning about the namespace, making the order of definitions relevant. I'd prefer to avoid it. That said, we can still support an 'ifdef' within definitions. All use cases I've found can be solved by moving this to the definition layer.

### Annotated Operations? Rejected.

It is feasible to introduce an NSOp for annotations, but I don't see any need for it. The simplicity and guaranteed termination when evaluating NSOp reduces need for annotations to control, precisely optimize, or debug the intermediate states. Annotations are instead represented within definition or namespace layers.

### Lists? Other Layers.

We can model lists as namespace components that define 'head' and 'tail', where 'tail' is either empty or another list. Access to to the third element in the list would be `.tail.tail.head`. An underlying data representation can feasibly compress long bitstring paths such as `(.tail)^N`.

That said, modeling lists in at the namespace layer seems awkward. We cannot locally reason about list size for purpose of indexing or overriding individual elements. In most use cases, it seems better to represent the list in the definition layer, then use overrides to incrementally extend or rewrite the list.
