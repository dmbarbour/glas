# Extensible Namespaces

This document describes an AST for a purely functional expression of an extensible namespace model that supports defining names, renames and overrides, declarations and late binding of definitions, recursive definitions and graph structures, access control, hierarchical names and component structure, multiple inheritance and mixins, and higher order templates.

This namespace model assumes definitions are represented in [abstract assembly](AbstractAssembly.md), which offers additional benefits for metaprogramming and extensibility. 

Performance is also a significant concern. I expect namespaces to grow very large and contain many redundant definitions tucked away in private namespaces. Thus, careful attention is needed towards indexing, lazy evaluation, and caching.

## Prefix Oriented Rewrites

There are two basic prefix-oriented rewrite operations:

* rename - rewrite a prefix for all methods AND update all links. 
* move - rewrite a prefix, breaking links; supports overrides.

Many other features such as overrides and private definitions can be expressed in terms of renames, moves, and a few conventions.

The scope of a rewrite is a namespace component. For example, we could rewrite a mixin to change which methods it overrides without actually modifying the namespace to which the mixin is later applied.

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. By default, the syntax might not permit access to private names of subcomponents. 

This form of privacy is weak, being based on conventions and syntactic control. In glas systems, syntactic control can be bypassed via user-defined syntax, so it's better to immediately provide an explicit bypass that is easy to detect via search or linter.

Where true privacy is required, the namespace can easily restrict which names are visible to hierarchical application components. Controlling access to names is essentially a form of [object capability security](https://en.wikipedia.org/wiki/Object-capability_model). But leveraging this might require architecting an application such that hierarchical components are externally wired together by sharing of names. 

*Note:* Other than controlling names, we could look into homomorphic encryption or zero-knowledge proofs. But such things are far outside the scope of the namespace model.

## Overrides

When we override a definition, we *move* the original definition then redefine it.

A potential question is to where the original definition is moved. We could introduce a convention of moving 'foo => foo^'. With this, we might also first rename 'foo^ => foo^^', such that we maintain a complete chain of older instances of 'foo' locally in the namespace (instead of moving them into private spaces). Similar to private definitions, the syntax might strongly discourage direct use of 'foo^^'.

Of course, if we don't actually use the prior version in the override, we might instead delete the history.

## Multiple Inheritance

There is an ambiguity risk when the same name is inherited from two sources. The proposed namespace model doesn't have a sophisticated solution to resolve ambiguity, but it does include a few annotations to help check user assumptions about which names are defined. In particular, this does not solve [the diamond problem](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem), but the namespace can support import and export control, mixins, and composition of non-overlapping dictionaries.

## Interfaces

I assume the same interface will be defined many times, i.e. defining type annotations and documentation for declared methods. We'll want to check that it truly is the same interface by comparing definitions. To support this, we'll also allow some overlap when inheriting from multiple sources, but under a constraint that the same definition is inherited.

This design restricts interfaces: they cannot contain any 'private' definitions, because those would be rewritten differently per instance. Override of interface methods would also be limited if the prior definition is in the private namespace.

I think interfaces would be useful even with these restrictions.

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




