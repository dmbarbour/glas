# Extensible Namespaces

Namespaces are a very useful layer between modules and functions. Modules can define and compose namespace fragments in terms of inheritance, mixins, and interfaces. Namespaces are especially convenient for defining mutually recursive functions. Extensible namespaces support tweaking or tuning of large programs, and static higher-order programming. Hierarchical namespaces we can precisely manage access to names, providing a lightweight basis for secure composition.

In glas systems, I propose to express [applications](GlasApps.md) as partial namespaces. This allows applications to be viewed as objects, and for the runtime to provide some definitions that can potentially be partially evaluated by an optimizer.

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
* *Move and Combine.* Move at least one definition, then combine via override. Use when both symbols have the same contextual purpose. Override in general can be modeled in terms of moving the original 'foo' to 'prior_foo'.
* *Erase.* Can erase the conflicting definition from the sources you don't want. Implemented as move then hide. 
* *Rename and Separate.* Rename at least one definition. Use when symbols conflict due to reuse of words in multiple contexts.

Ideally, most conflicts are avoided so we don't need messy any syntax to resolve them. But the above covers most cases.

## Components

With just a little syntactic sugar, it is feasible to support lightweight components where we *parameterize* a namespace with a set of overrides.

        obj = foo(bar, baz=qux)

In this case, we might understand this as creating a new hierarchical namespace 'obj' (e.g. where we could access 'obj.baz'), then defining 'obj.bar=bar' and 'obj.baz=qux' in the caller's namespace. This binding could be supported via overrides or via renames, i.e. you're adding the 'obj.' prefix then renaming 'obj.bar -> bar' and 'obj.baz -> qux', perhaps erasing the original definitions of 'obj.bar' and 'obj.baz' first.

We could also support an import list variant:

        from foo(bar, baz=qux) import ...

This illustrates that hierarchical composition can be concise and flexible depending on syntax. But I think the syntax proposed here could use further tweaking.

## Aliasing? Not at this layer.

In many cases, we might want to reference the same resources from multiple names within the namespace. This is especially the case in context of state resources declared within the namespace. The tree-structured namespace becomes a directed acyclic graph.

If we aren't concerned with multiple names, we can achieve something close enough via renames: add prefix 'foo.' to component, rename 'foo.io.' to 'io.'. This would mean we cannot access 'foo.io.' externally, i.e. the client would need to directly work with 'io.'. Additionally, we could easily support definition-layer redirection, i.e. define one method as delegating to another, or declare one state resource as a reference to another (or a component within another). Between these, I think there isn't a strong use case for aliasing at the namespace layer.

And suppose we want to keep 'foo.io.' as mapping to 'io.' such that future extensions to 'foo.io.x' implicitly extend 'io.x' as well. In this case, a mixin that extends both 'foo.io.' and 'io.' would be very confusing. It is unclear which override should apply first. We'd probably need to add rules to check that overrides within a mixin refer to different methods. Programmers would generally need to be 'aware' of aliases when developing extensions, which isn't better than favoring rename or redirect.

Finally, I find it convenient to understand names as 'points of change'. Rename and redirection isn't a problem for this, but aliasing and unification would mean extensions cannot independently change some names.

## Annotations

Annotations can be supported by simple naming conventions. For example, to annotate method 'foo' we might define 'foo%type' and 'foo%doc'. This would make annotations subject to the same abstraction, extension, and overrides as everything else. The language compiler should be annotation aware, such that renaming 'foo' would implicitly rename prefix 'foo%'. 

Of course, the exact naming convention is subject to aesthetic concerns. I'll leave the bikeshed color problem to the language design doc.

## Assertions

Similar to annotations, assertions can be supported based on naming conventions. It is convenient for assertions to have names in any case because it simplifies reporting which assertion failed. 

Static assertions can be tested when building applications. Intriguingly, some that reference state might also be tested when committing transactions that write that state, ensuring transactional consistency. If an assertion would fail, the transaction is aborted. 

To ensure assertions don't influence observable behavior, the assertion itself might be aborted even on success.

## Interfaces and Defaults

Interfaces can be modeled as namespace components that have no private definitions, and don't actually define normal methods. But they can define annotations and assertions. Annotations would include type annotations and documentation. Assertions would describe conditions that should hold true once the namespace is fully defined and the application is initialized.

Interfaces are a good place for default definitions, too. Default definitions could allow for override or intro, but two defaults might conflict unless they have the same definition.

## Compressing Namespaces

After a namespace is fully written, it might be compressible. This includes dead code elimination and potential use of content addressing to combine programs that have the same behavior. However, this is non-trivial and quite beyond the scope of this document.

## Overloading or Multimethods? Responsibility of Definitions layer.

For some languages we might assign multiple meanings to one symbol contingent on context. This can be a typeful context or more ad-hoc and dynamic. Either way, we will want to extend a program both by introducing new contexts and overriding behavior in existing contexts. And we'll want good performance, and for this to not overly interfere with static analysis.

I think this mostly needs to be solved in the definitions layer. The issue is that choosing based on context is different from the conventional 'ordered' choice, i.e. the if/then/else composition doesn't work. Instead, we want something more nuanced based on matching contexts, and this requires that 'assumed context' is an explicit part of the definition or program fragment.

Of course, it isn't impossible to use ordered choice, so long as we're willing to explicitly order our definitions from least to most specific. But this is awkward when extending a program with new contexts, and it doesn't compose *efficiently* in any case. 

What sort of contexts could we model that do compose easily? Well, one option is to focus on *named contexts*, i.e. where a context is itself a *set of names* representing descriptors on the environment (with possible return values). This would allow for contexts to compose as a lattice, and would greatly simplify partial matching. More general matching on patterns seems troublesome to compose this way. But perhaps we could identify a useful subset of patterns that compose as lattices?

A compiler might leverage this namespace to partition static context for performance reasons, e.g. involving name mangling. But this should be invisible to the programmer and irrelevant to the semantics. Ultimately, focus needs to be how methods are expressed and compose.

## Nominative Types

Namespaces can support nominative types, i.e. where the name is included in the type. We could support name-indexed records, for example. In context of glas applications, we might insist that all nominative types are ephemeral. 
