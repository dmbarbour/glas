# Extensible Namespaces

This document describes a set of [abstract assembly](AbstractAssembly.md) constructors for namespaces. 

The proposed namespace model will support recursive definitions, access control, renames and overrides, declarations and late binding of definitions, hierarchical namespaces and software components, and flexible mixins and abstraction of namespaces. All definitions within the namespace are also represented via abstract assembly, albeit with different AST constructors.

Relating to glas systems:

* an application is a namespace where names represent program behavior, such as the start, step, and stop methods for the application life cycle
* a glas program module is a non-recursive namespace where every name represents an application or part of one, such as reusable components or mixin
* to simplify integration, the main application defined in a program module is named 'app', e.g. in case of `glas --run ModuleName` by the [CLI](GlasCLI.md)

Performance is a significant concern. Namespaces can grow very large and will often have a lot of redundant structure. It should be feasible to process only the subset of names we actually need to run the application and ignore the remainder. Thus, this document also pays attention to indexing and lazy evaluation.






## Prefix Oriented Rewrites

There are two basic prefix-oriented rewrite operations:

* rename - rewrite a prefix for all methods AND update all links. 
* move - rewrite a prefix, breaking links; supports overrides.

The scope of a rewrite is a namespace component. For example, we could rewrite a mixin to change which methods it overrides. Hierarchical namespaces are supported by *renaming* the empty prefix. An override can be represented by *moving* the original name, e.g. move 'foo' to '^foo', then redefining 'foo'. With some conventions for private definitions, we can also hide names or support export lists.

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

## Private Definitions

As a convention, a language might indicate private symbols via special prefix such as '~'. The language syntax can restrict reference to private definitions of component applications. The language compiler can automatically rename '~' when inheriting to avoid conflicts. Ideally, this rename is stable for debugging purposes.

We can easily end up with many copies of private definitions. This is mitigated by structure sharing, but an optimizer could potentially recombine private definitions. In any case, it's outside the scope of namespaces, because the namespace layer itself doesn't know '~' means private.

## Multiple Inheritance

We can compose namespaces, rewriting as we do so. However, there is an ambiguity risk when the same name is inherited from two sources. The namespace should include some annotations about assumptions - e.g. whether a given symbol is already defined or not. To support interfaces, the namespace might also treat the same definition from two sources as unambiguous.

With conflict detection, avoidance, and manual resolution, we can support mixin inheritance, and we can merge dictionaries that define different words. But it would be too much manual editing to directly merge dictionaries that share a lot of words. Instead, users would tend to favor hierarchical namespaces or explicit import lists.

## Non-Recursive Namespaces



## Annotations

Similar to private definitions, annotations can be supported via naming conventions. For example, to annotate method 'foo' we might define 'foo?type' and 'foo?doc'. The language compiler should be annotation aware. 

## Nominative Types

It is feasible to support limited reflection from the language into the namespace. This may include access to names as abstract values or abstract types. However, it would probably be best to treat names as ephemeral types.

## Metaprogramming

It is feasible to 'map' an operation to every definition in a namespace. Moreover, it is feasible to do so lazily or as part of the single-pass rewrite. The main issue is how we would represent the operation when representing the namespace. I'd prefer to avoid defining a complicated language for this role!

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

## Abstract Assembly

I propose a one-size-fits-all [abstract assembly](AbstractAssembly.md) for defining application methods. Further, we can use the same assembly - but with different AST constructors - to represent construction of the namespaces. 

This would simplify meta-namespaces, where every definition is a namespace. This also simplifies metaprogramming, which might effectively reduce to adding a prefix to every definition.

## Meta-Namespaces

A glas module could compile into a 'dict' of namespaces. Alternatively, we could potentially compile it to a meta-namespace, a namespace where the definitions are namespaces. The latter would be interesting insofar as it allows users to abstract and compose dependencies more flexibly. Though, there is signficant risk of confusion.

## Thoughts

I'll need to control scope of renames. For example, if we rename an override method in a mixin, it should override the new method on its eventual target rather than renaming anything within its target. But a mixin must also perform renames as part of expressing the override. 

I'll want annotations on namespace constructors to help distinguish 'mixins' and other uses of namespaces to simplify integration with the language compiler, i.e. so we can raise a warning or error if the user attempts to apply as a 'mixin' something declared as an initial 'namespace'. 




