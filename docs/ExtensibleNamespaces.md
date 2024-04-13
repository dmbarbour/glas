# Extensible Namespaces

Namespaces are a very useful layer between modules and functions. Modules can define and compose namespace fragments in terms of inheritance, mixins, and interfaces. Namespaces are especially convenient for defining mutually recursive functions. Extensible namespaces support tweaking or tuning of large programs, and static higher-order programming. Hierarchical namespaces we can precisely manage access to names, providing a lightweight basis for secure composition.

In glas systems, I propose to express [applications](GlasApps.md) as partial namespaces. This allows applications to be viewed as objects, and for the runtime to provide some definitions that can potentially be partially evaluated by an optimizer.

This document describes how to implement namespaces with a single-pass rewrite of names. I assume names are represented by prefix-unique bitstrings (such as null-terminated UTF-8). We'll support multiple inheritance, mixins, interfaces, private definitions, hierarchical namespaces, annotations, assertions, renames, and overrides. This is more or less independent of the definition type, modulo that it must be possible to precisely identify all names used within a definition.

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

## Annotations

Similar to private definitions, annotations can be supported via naming conventions. For example, to annotate method 'foo' we might define 'foo?type' and 'foo?doc'. The language compiler should be annotation aware. 

## Nominative Types

It is feasible to support limited reflection from the language into the namespace. This may include access to names as abstract values or abstract types. However, it would probably be best to treat names as ephemeral types.

## Design Goals

* A namespace is represented by glas data. No extrinsic identity.
* A namespace may declare abstract names to be defined via extension.
* A namespace represents a mixin, applying to abstract namespace.
* A mixin may rename, move, and override definitions.
* A mixin may restrict or require some names are defined or declared.
* Namespace semantics can be expressed as local rewrite rules.
* Namespace semantics are efficient and guarantee termination.
* Efficiently discover which symbols are defined or declared.

## Thoughts

I assume a glas module compiles to a dictionary of independent namespaces, perhaps `(foo:ns:(...), bar:ns:(...), app:ns:(...))`. However, these compiled namespace values do not reference each other by name. If we write `namespace foo extends bar`, the compiler can directly copy the compiled representation of bar into the compiled representation of foo. Optionally, the compiler could rewrite and optimize the composition a little.

I'll need to control scope of renames. For example, if we rename an override method in a mixin, it should override the new method on its eventual target rather than renaming anything within its target. But a mixin must also perform renames as part of expressing the override. 

I'll want annotations on namespace constructors to help distinguish 'mixins' and other uses of namespaces to simplify integration with the language compiler, i.e. so we can raise a warning or error if the user attempts to apply as a 'mixin' something declared as an initial 'namespace'. 




