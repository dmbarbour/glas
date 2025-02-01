# Abstract Assembly

Proposed plain-old-data representation for abstract assembly as glas data:

        type AST = (Name, List of AST)      # constructor
                 | 0b0:Data                 # constant data
                 | 0b10:Name                # namespace ref
                 | 0b110:Localization       # reified scope

This encoding uses lists for constructor nodes and a compact tagged union for leaf nodes. The representation of Name and Localization will depend on the namespace model. Assuming [glas namespaces](GlasNamespaces.md), we can directly reuse the Name type, and the Localization would be a 'link' TLMap, allowing us to evaluate names in scope.

The system is assumed to define a set of primitive AST constructor functions. For example, a procedural intermediate language might specify '%i.add' for arithmetic and '%seq' for control. Primitives are prefixed with '%' by convention. This allows us to easily recognize and forward primitives when translating the namespace. 

Embedded data is arbitrary, and is not touched by namespace translations. However, to support flexible integration and acceleration, I recommend wrapping most embedded data within an AST node. For example, we might favor `(%i.const 42)` instead of directly using 42.  

By default, a compiler should forward primitives through a namespace unmodified. For example, when importing a module under prefix `foo.*` we could use rename `{ "" => "foo.", "%" => "%" }` to add a prefix to everything except the primitives. This indirection introduces an opportunity for a program to extend, restrict, override, route, and abstract AST primitives available to a subcomponent. This is the basis for the name *abstract assembly*.  

# Meta Thoughts

## Naming Variables

In context of metaprogramming, it is convenient if capture of variables is controlled, aka [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). One possibility here is to encode a namespace directly into the computation, use translations to control access. Alternatively, we could encode variable names with reference to an external namespace. In the latter case, instead of `(%local "x")`, we might use `(%local &privateScopeName "x")`. This allows for limited non-hygienic macros when they can guess the private scope name.

## Staged Eval

In some cases, we'll want to insist certain expressions, often including function parameters and results, are evaluated at compile time. Minimally, this should at least be supported by annotations and the type system. But it is useful to further support semantic forms of static eval, e.g. '%static-if' doesn't necessarily need to assume the same type or environment on both conditions, instead allowing the static type of an expression or function to vary conditionally.

In context of dead code elimination and lazy loading, static eval is limited in viable 'effects' API. We can permit 'safe' cacheable fetching of files or HTTP GET, or debug outputs such reporting a warning. In the latter case, we might be wiser to model the effect as an annotation and debugging as reflection.
