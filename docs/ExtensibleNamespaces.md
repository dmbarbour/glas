# Extensible Namespaces

Namespaces are a very useful layer between modules and functions. Modules can define and compose namespace fragments in terms of inheritance, mixins, and interfaces. Namespaces are especially convenient for defining mutually recursive functions. Extensible namespaces support tweaking or tuning of large programs, and static higher-order programming. Hierarchical namespaces we can precisely manage access to names, providing a lightweight basis for secure composition.

In glas systems, I propose to express [applications](GlasApps.md) as partial namespaces. This allows applications to be viewed as objects, and for the runtime to provide some definitions that can potentially be partially evaluated by an optimizer.

This document describes how to implement namespaces with a single-pass rewrite of names. I assume names are represented by prefix-unique bitstrings (such as null-terminated UTF-8). We'll support multiple inheritance, mixins, interfaces, private definitions, hierarchical namespaces, annotations, assertions, renames, and overrides. This is more or less independent of the definition type, modulo that it must be possible to precisely identify all names used within a definition.

## Prefix Oriented Rewrites

There are two basic prefix-oriented rewrite operations:

* rename - rewrite a prefix for all methods AND update all links. 
* move - rewrite a prefix, breaking links; supports overrides.

The scope of a rewrite is a namespace component. For example, we could rewrite a mixin to change which methods it overrides. Hierarchical namespaces are supported by *renaming* the empty prefix. An override can be represented by *moving* the original name, e.g. move 'foo' to '^foo', then redefining 'foo'. 

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

Additionally, the pass will include a prefix for hiding names. This location will be partitioned in context of hierarchical components.

## Private Definitions

A language might indicate private symbols via special prefix such as '~'. Use of a standard prefix in this role is convenient because otherwise there is risk of shadowing public symbols. To keep it simple, the same language might forbid use of '~' within symbols except as a prefix character. To implement private definitions under these assumptions, the language compiler can systematically rename prefix '~' to 'fresh-prefix~', with 'fresh-prefix' uniquely allocated per inherited or component namespace.

Stability of private definitions is desirable in context of declared state and live coding or orthogonal persistence, or debug views and reflection. Insofar as definitions are stable, we can bind state by name across multiple versions of code. Relevantly, if the language compiler is allocating a fresh privacy prefix based on incrementing a counter, it would be unstable to many insertions and deletions. The privacy prefix should instead be derived based on symbols or aliases used in code, preferably with sufficient programmer control to manually stabilize across manual name changes.

Anyhow, this feature can be left mostly to the language definitions and conventions. No need for special support from the namespace model.

## Multiple Inheritance

We can support namespaces deriving from multiple sources. However, there is a risk of ambiguity or conflict when the same name is inherited from two sources. To mitigate this, we might allow namespace definitions to indicate assumptions about whether certain prefixes (generally including full symbols) are already in use or not. We can easily detect whether such assumptions are violated without peering into definition details. 

To support interfaces, we might also allow for inheriting 'the same' definition from two sources as not being ambiguous, a limited unification semantics.

Anyhow, we can readily support interfaces, mixins, hierarchical components, and composition of dictionaries in terms of multiple inheritance. But users would either manage [diamond pattern inheritance](https://en.wikipedia.org/wiki/Multiple_inheritance#The_diamond_problem) manually or (more likely!) avoid it.

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

## Conditionals? Language Layer

Conditionals that may vary due to late binding, such as testing whether a given symbol is defined, too easily leads to inconsistency in context of extensions and late binding. But we can easily test whether a static namespace component has a property.

        # can easily support:
        if (A defines x,y) then B else C 

        # cannot easily support:
        if x,y is defined then B else C

However, if we're testing only static properties, we can easily shift the test to the language layer, e.g. into keywords or macros that are evaluated at compile time. Further, the language layer has great flexibility regarding which properties are observable, not limited to testing whether a symbol is defined.

The primary motive for conditional construction of namespaces is mostly to automate 'glue' code, e.g. automate construction of an adapter for hierarchical application components. This needs a lot of flexibility for the general case.

## Aliasing? Not at this layer.

For simplicity, different names are fundamentally different with respect to late binding. But languages may leverage renames and moves to 'merge' names when composing namespaces. Also, it is very easy to support and optimize delegation at the definitions layer.

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

