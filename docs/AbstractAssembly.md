# Abstract Assembly

The main idea for abstract assembly to use Names from a user-controlled namespace as constructors in an abstract syntax tree (AST) intermediate representation. This gives users an opportunity to control which language features a subprogram may use through the namespace, and potentially supports extension of or adaptation between ASTs.

Proposed plain-old-data representation for abstract assembly as glas data:

        type AST = c:(Name, List of AST)        # constructor
                 | d:Data                       # embedded data
                 | n:Name                       # name (as arg)
                 | z:Localization               # localization

The types for Name and Localization depend on the namespace model. In case of [glas namespaces](GlasNamespaces.md) we could use:

        type Name = Bitstring (byte aligned, no NULL)   # not prefix of another
        type Localization = Map of Prefix to Prefix     # rewrite longest match
        type Prefix = Bitstring (byte aligned, no NULL) # empty up to full name

Localization captures all rewrite rules that would apply to a Name. This can be useful in context of staged computing, where some names might be integrated in a later stage. In case of glas namespaces, we would capture link (ln) operations, aggregating via followed by (fby) composition.

As a convention, I propose primitive AST constructors are '%' prefixed. This enables easy recognition, resists accidental name collisions, supports prefix-based propagation of primitives into hierarchical components, and simplifies syntactic control of direct user access to primitives. A few primitive AST nodes for a procedural language might include %seq, %i.add, %cond, %call, and so on.

The abstraction overhead for abstract assembly is negligible and is paid entirely at compile time. To simulate a concrete assembly, it is sufficient to propagate '%' names into hierarchical components by default (i.e. `{ "" => "foo.", "%" => "%" }` for component 'foo'). The abstraction can still be useful in some contexts, such as when integrating components developed in multiple languages.

## Name Capture

In context of a Localization we could safely introduce a `Binary -> Name` computation that accounts for how the name would be rewritten in context. This preserves namespace-based access control. However, unless this operation is used only at compile-time, it easily interferes with dead code elimination.

In practice, a better solution is to construct an explicit table of all the names we might reference, e.g. `[(n:"foo", m:&foo), (n:"bar", m:&bar), ...]`. This allows for precise erasure of unused definitions. In context of live coding or orthogonal persistence, it is useful to further restrict `&foo` as an *ephemeral* type, usable only within a transaction (permitting partial evaluation at compile time).

*Note:* We can support the converse, `Name -> Binary`, as part of a reflection API. This exposes contextual details, reducing 'portability' of code within a namespace, so it can be useful to restrict access.

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
