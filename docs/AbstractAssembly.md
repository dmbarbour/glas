# Abstract Assembly

Proposed plain-old-data representation for abstract assembly as glas data:

        type AST = (Name, List of AST)      # constructor
                 | 0b0:Data                 # embedded data
                 | 0b10:Name                # namespace ref
                 | 0b11:Localization        # namespace scope

This encoding uses lists for constructor nodes and a compact tagged union for leaf nodes. Names and localizations are clearly distinguished from data to simplify renames, allowing us to translate definitions as they are introduced into a namespace. Embedded data is arbitrary; if very large, it might be represented using content-addressed storage.

Every constructor starts with a name. A compiler assumes the system defines a known, abstract set of primitive AST constructor names. For example, a procedural intermediate language might assume '%i.add' and '%seq'. To simplify processing and user recognition, by convention these primitives are prefixed by '%'.

By default, a compiler should forward primitive '%' names unmodified through the namespace, e.g. when a module might be imported under prefix `foo.*` using prefix-to-prefix rename `{ "" => "foo.", "%" => "%" }`. However, we aren't limited to the default. With a suitable syntax, a program might extend, restrict, override, route, and abstract AST primitives available to its subprograms. This indirection is the basis for the name *abstract assembly*.

The representation of Name and Localization depend on the namespace model, especially which names and renames are legal. For [glas namespaces](GlasNamespaces.md), which support byte-aligned, prefix-to-prefix renames, a viable solution is:

        type Name = Bitstring (byte aligned, no NULL)   # not prefix of another
        type Localization = Map of Prefix to Prefix     # rewrite longest match
        type Prefix = Bitstring (byte aligned, no NULL) # empty up to full name

Localizations capture a program's local view of its context. This can be useful for dynamic 'eval', embedded DSLs, or format strings where we might want to bind names to the environment. However, use of localizations at runtime will hinder dead-code elimination and static safety analysis. In glas systems, we might use localizations only for staged computing at compile time.

# Meta Thoughts

## Capture of Stack Variables

Instead of `(%local "x")`, we could associate local variable names with the namespace. This could be name per variable or per scope, the latter involving `(%local &privateScopeName "x")`. This provides a simple basis for [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). A macro would be unable to 'forge' references into the context.

## Switchable Primitives? Not in glas systems.

It is feasible to introduce `%debug-assert` and `%debug-assert.unchecked` and let users switch between them via namespace manipulation without the ability to define AST primitives. I don't believe I'll use this for glas systems because it's deprecated by proper support for metaprogramming. However, it's an interesting idea. 

## Dynamic Eval

The intermediate language can define a primitive '%eval' operator, or a runtime could define a 'sys.refl.eval' method. Either way, it might be convenient to express this in terms of evaluating an abstract, ephemeral AST node. This would let users leverage the runtime's JIT compiler and integrate effectively with logging, acceleration, and transaction loop optimizations.

A relevant challenge with 'eval' is interaction with a static type analysis. We could introduce some explicit type assumptions at the site of eval that can be checked on the dynamic AST parameter, i.e. `(%eval StaticTypeExpr DynamicASTExpr)`. This shifts the challenge to how to *infer* a StaticTypeExpr.

Conveniently, we can easily optimize if eval is applied to a static AST expression, and a we can eliminate the expensive JIT subsystem from a runtime if an optimizer erases all reference to '%eval'. 

## Static Eval

In some cases, we'll want to insist certain expressions, often including function parameters and results, are evaluated at compile time. Minimally, this should at least be supported by annotations and the type system. But it is useful to further support semantic forms of static eval, e.g. '%static-if' doesn't necessarily need to have the same type on both conditions, instead allowing the static type of an expression or function to vary conditionally.

*Note:* In theory, static eval could also support compile-time effects. But I think it's best to avoid such effects because it doesn't play nicely with lazy compilation and dead code elimination. Users can still support logging and profiling at compile-time, because those are annotations not effects. 
