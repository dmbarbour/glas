# Extensible Namespaces

This document describes AST constructors for namespaces, intended for encoding namespaces in [abstract assembly](AbstractAssembly.md). 

The proposed namespace model supports defining names, renames and overrides, declarations and late binding of definitions, recursive definitions and graph structures, access control, hierarchical names and component structure, multiple inheritance and mixins. Definitions also use *abstract assembly*, albeit with different AST constructors.

In context of glas systems, a program module compiles to a namespace of application components, and an application component is a namespace of methods. Thus, we have at least two layers of namespaces with different AST types. Arguably, runtime configuration of the global module distribution is a third namespace layer, albeit more restrictive.

Performance is a significant concern. I expect namespaces to grow very large and contain many redundant definitions. Thus, careful attention is needed towards indexing, lazy evaluation, and caching.

## Prefix Oriented Rewrites

There are two basic prefix-oriented rewrite operations:

* rename - rewrite a prefix for all methods AND update all links. 
* move - rewrite a prefix, breaking links; supports overrides.

The scope of a rewrite is a namespace component. For example, we could rewrite a mixin to change which methods it overrides. Hierarchical namespaces are supported by *renaming* the empty prefix. An override can be represented by *moving* the original name, e.g. move 'foo' to '^foo', then redefining 'foo'. With some conventions for private definitions, we can also hide names or support export lists.

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. With just a little syntactic support, this generated prefix can be stable to simplify orthogonal persistence, live coding, and debugging.

A potential issue is that we'll end up with many 'copies' of private definitions. An optimizer can potentially mitigate this by performing a compression pass on namespaces.

## Multiple Inheritance

We can compose namespaces. However, there is an ambiguity risk when the same name is inherited from two sources. The proposed namespace model will help users represent assumptions so they can detect issues at compile time and resolve them manually, e.g. by moving or renaming a conflicting method. This does not solve [the diamond problem](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem), but it's sufficient to support mixins, import and export control, and inheritance of multiple non-overlapping dictionaries.

## Annotations

For namespace-layer annotations, we'll initially need to describe our assumptions for multiple inheritance. It might also be useful to introduce some assumptions relating to recursion, or minimal subsets of overrides in context of defaults.

Annotations about names might be represented within the namespace, e.g. to annotate method 'foo' we might define 'foo?type' and 'foo?doc'. The language compiler should be annotation aware such that we can rename annotations on 'foo' when we rename 'foo'. 

## Nominative Types

It is feasible to support limited reflection from the language into the namespace. This may include access to names as abstract values or abstract types. However, it would probably be best to treat names as ephemeral types.

## Namespace Level Metaprogramming

To sandbox a subcomponent we might override its primitive AST constructors, providing the host an opportunity to observe and rewrite a representation of subprogram behavior. However, to invoke a sandboxed method, we first need to recompile the sandbox AST into the host AST. 

Instead of performing this step manually, we can support a few namespace-level operators for bulk rewrites. However, I'd prefer to avoid introducing a program model at the namespace layer. 

My intuition is that a few standard bulk operations should be sufficient in practice. For example, we could wrap a user-defined constructor to everything defined in a namespace component, i.e. `lift (OriginalAST)`, or perhaps express bulk delegation via `foo.xyzzy = wrap bar.xyzzy`. If these are sufficient, no ned to complicate the namespace type.

## AST Constructors



## Design Goals

* A namespace is represented by glas data. No extrinsic identity.
* A namespace may declare abstract names to be defined via extension.
* A namespace represents a mixin, applying to abstract namespace.
* A mixin may rename, move, and override definitions.
* A mixin may restrict or require some names are defined or declared.
* Namespace semantics can be expressed as local rewrite rules.
* Namespace semantics are efficient and guarantee termination.
* Efficiently discover which symbols are defined or declared.
* Effective support for metaprogramming.

I'll need to control scope of renames. For example, if we rename an override method in a mixin, it should override the new method on its eventual target rather than renaming anything within its target. But a mixin must also perform renames as part of expressing the override. 

I'll want annotations on namespace constructors to help distinguish 'mixins' and other uses of namespaces to simplify integration with the language compiler, i.e. so we can raise a warning or error if the user attempts to apply as a 'mixin' something declared as an initial 'namespace'. 




