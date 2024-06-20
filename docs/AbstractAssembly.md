# Abstract Assembly

The main idea for abstract assembly to use Names from a *user-controlled namespace* as constructors in an abstract syntax tree (AST) intermediate representation. This gives users an opportunity to control through the namespace which language features a subprogram may use, to override language features, to adapt between languages, and potentially to extend a language with new features. 

Proposed representation for abstract assembly as glas data:

        type AST = c:(Name, List of AST)        # constructor
                 | d:Data                       # embedded data
                 | n:Name                       # name (as arg)
                 | z:Localization               # localization

The types for Name and Localization depend on the namespace model. In case of [glas namespaces](GlasNamespaces.md) we could use:

        type Name = Binary                              # not prefix of another
        type Localization = Map of Prefix to Prefix     # rewrite longest match
        type Prefix = Binary                            # empty up to full Name

Localization captures all rewrite rules that would apply to a Name. This can be useful in context of staged computing, where some names might be integrated in a later stage. In case of glas namespaces, we would capture link (ln) operations, aggregating via followed by (fby) composition.

As a convention, I propose primitive AST constructors are '%' prefixed. This enables easy recognition, resists accidental name collisions, supports prefix-based propagation of primitives into hierarchical components, and simplifies syntactic control of direct user access to primitives. A few primitive AST nodes for a procedural language might include %seq, %i.add, %cond, %call, and so on.

The abstraction overhead for abstract assembly is negligible and is paid entirely at compile time. To simulate a concrete assembly, it is sufficient to propagate '%' names into hierarchical components by default (i.e. `{ "" => "foo.", "%" => "%" }` for component 'foo'). The abstraction can still be useful in some contexts, such as when integrating components developed in multiple languages.

## Name Capture

By leveraging Localization, it is feasible to introduce operations such as `Binary -> Name` that account for how the name would be rewritten in context. This preserves namespace-based access control. However, unless this operation is used only at compile-time, it very easily interferes with dead code elimination.

In practice, a better solution is to construct an explicit table of all the names we might reference, e.g. `[["foo", &foo], ["bar", &bar], ...]`. This would allow for more precise erasure of unused definitions.

*Note:* We can easily support the converse, `Name -> Binary`. But this should be part of a reflection API because we don't always want components to know their own embedding.

## Capture of Stack Variables

Instead of `(%local "x")`, we could associate local variable names with the namespace. This could be name per variable or per scope, the latter involving `(%local &privateScopeName "x")`. This provides a simple basis for [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). A macro would be unable to 'forge' a local reference unless specifically implemented for use in a known scope.

## Switchable Primitives

Even without overrides and staged computing, it is feasible to introduce sets of related operations then use the namespace to switch between them. For example, `%debug-assert` might implicitly be checked, but for a given subprogram we might redirect to `%debug-assert.unchecked`. That said, this is awkward and inflexible compared to user-defined, staged operations. Still, I wonder if we'll find use for the idea somewhere.

## Dynamic Eval

The intermediate language can define a primitive '%eval' operator, or a runtime could define a 'sys.refl.eval' method. Either way, it might be convenient to express this in terms of evaluating an AST node, implicitly leveraging the runtime's built-in JIT compiler and directly integrating runtime features such as logging and acceleration.

A relevant challenge with 'eval' is interaction with a static type checker. This seems more approachable as a primitive, i.e. `(%eval StaticTypeExpr DynamicASTExpr)` would allow for integration assumptions to be locally verified prior to evaluation. Ideally, the StaticTypeExpr can be partially or fully inferred from context. This might require specialized AST nodes for type variables or 'holes' in type expressions.

Conveniently, we can easily optimize if eval is applied to a static AST expression, and a late stage compiler can remove the expensive JIT subsystem if an optimizer erases all reference to '%eval'. 

## Static Eval

In some cases, we'll want to insist certain expressions are evaluated at compile time. This should be supported by annotations and the type system. Ideally, a static expression may read static function parameters, and a subset of function return values (or other outputs) may also be static. If we support static implicit parameters, we can effectively model an effectful compile-time environment. 

It is feasible to also support some *semantic* forms of static evaluation as AST primitives. This might be useful for '%static-if' and '%ifdef' and similar, allowing us to more precisely control static evaluation. I would prefer to avoid true compile-time effects because those would hinder dead code elimination and JIT compilation, but static conditionals and static loop expansions might prove convenient.
