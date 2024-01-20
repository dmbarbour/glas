# Extensible Namespaces

Namespaces are a very useful layer between modules and functions. Modules will define and compose namespace fragments, e.g. in terms of inheritance, mixins, and interfaces. Namespaces can contain methods, state resources, annotations, assertions. Namespaces are convenient for expressing mutually recursive definitions. Extensible namespaces support tweaking or tuning of large programs, and static higher-order programming. With hierarchical namespaces we can precisely manage access to names, providing a lightweight basis for secure composition.

This document describes how to implement namespaces with a single-pass rewrite of names. I assume names are represented by prefix-unique bitstrings (such as null-terminated UTF-8). We'll support multiple inheritance, mixins, interfaces, private definitions, hierarchical namespaces, annotations, assertions, renames, and overrides. This is more or less independent of the definition type, modulo that it must be possible to precisely identify all names used within a definition.

## Prefix Oriented Rewrites

There are essentially three prefix-oriented rewrite operations:

* rename - rewrite a prefix for all methods AND update all links. 
* move - rewrite a prefix, breaking links; supports overrides.
* hide - rename prefix a fresh 'anonymous' namespace. Supports private defs.

The scope of a rewrite is a namespace component. For example, we could rewrite a mixin to change which methods it overrides. Hierarchical namespaces are supported by *renaming* the empty prefix. An override can be represented by *moving* the original name, e.g. move 'foo' to '^foo', then redefining 'foo'. Private definitions can be supported by using a common prefix for private names (perhaps '~') then hiding that prefix. Import or export lists can be supported by renaming the empty prefix to '~', renaming every exported name back out of '~', then hiding '~'. 

Aside from actually providing the definitions and detecting conflict risks, these few operations cover all manipulations we need on namespaces.

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

Additionally, the pass will include a prefix for hiding names. This location will be partitioned in context of hierarchical components.

## Multiple Inheritance

We can support namespaces deriving from multiple sources. However, there is a risk of ambiguity or conflict when the same name is inherited from two sources. To mitigate this, we can allow namespace operations to assert specific names are defined or not. In general, namespace components can indicate whether they expect to initially introduce or override a definition. If we try to introduce a definition that already exists, or override a definition that does not exist, we will raise an error for the programmer to resolve. This is adequate for a primary inheritance plus mixins or multiple inheritance of disjoint definitions.

To further support *interfaces* I propose two additional rules. First is 'unify', which introduces the definition or verifies the existing definition is the same. The second is support for 'default' definitions, which can be overridden or introduced without conflict (but two defaults must unify). In general, we might have mixins inherit from interfaces to provide some extra context. When a namespace applies multiple mixins, we'll test whether they're intended for the same interface.

This covers a few useful and common inheritance patterns while remaining simple to understand and implement.

## Conflict Resolution

When users inherit definitions from multiple sources, name conflicts are possible. Conflict resolution tactics include:

* *Import Lists.* Be more precise about what you're grabbing.
* *Qualified Imports.* Consider hierarchical namespaces.
* *Move and Combine.* Move at least one definition, then combine via override. Use when both symbols have the same contextual purpose. 
* *Erase.* Can erase the conflicting definition from the sources you don't want. Implemented as move then hide. 
* *Rename and Separate.* Rename at least one definition. Use when symbols conflict due to reuse of words in multiple contexts.

Ideally, most conflicts are avoided so we don't need messy any syntax to resolve them. But the above covers most cases.

## Components

With just a little syntactic sugar, it is feasible to support lightweight components where we *parameterize* a namespace with a set of overrides.

        obj = foo(bar, baz=qux)

In this case, we might understand this as creating a new hierarchical namespace 'obj' (e.g. where we could access 'obj.baz'), then defining 'obj.bar=bar' and 'obj.baz=qux' in the caller's namespace. We could also support an import list variant:

        from foo(bar, baz=qux) import ...

This illustrates that hierarchical composition can be concise and flexible depending on syntax. But I think the syntax proposed here could use further tweaking.

## Annotations

Annotations can be supported by associated names with a simple naming convention. For example, to annotate method 'foo' we could define 'anno.foo.type' and 'anno.foo.doc'. This would make annotations subject to the same abstraction, extension, and overrides as everything else. The language compiler should be annotation aware, such that renaming or explicitly moving 'foo' would also rename 'anno.foo.'. 

## Assertions

Assertions can be supported based on naming conventions. Assertions having names is convenient for for reporting in any case. We could simply have a convention where 'assert.whatever = computation' represents a test that should pass after the app is initialized (and even before, if the assertion doesn't reference app state).

In context of transaction loop applications, we could potentially evaluate assertions prior to commit. If an assertion fails, the transaction is aborted. If an assertion obviously isn't affected, it could be omitted. This might be too heavyweight for normal use, though; might use annotations on assertions to configure usage.

## Interfaces and Defaults

Interfaces can be modeled as namespace components that have no private definitions, and don't actually define normal methods. But they can define annotations and assertions. Annotations would include type annotations and documentation. Assertions would describe conditions that should hold true once the namespace is fully defined and the application is initialized.

Interfaces are a good place for default definitions, too. Default definitions could allow for override or intro, but two defaults might conflict unless they have the same definition.

## Compressing Namespaces

After a namespace is fully written, it might be compressible. This includes dead code elimination and potential use of content addressing to combine programs that have the same behavior. However, this is non-trivial and quite beyond the scope of this document.
