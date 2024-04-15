# Abstract Assembly

This document proposes a one-size-fits-all intermediate language in context of a [namespace](ExtensibleNamespaces.md). This intermediate language might represent an assembly or bytecode. The candidate structure is reminiscent of Lisp or Scheme: 

        type AST = Constructor Arg*
        and  Arg = Data | Name | AST            

To support renames, names must be clearly distinguished in the intermediate language. Assuming represention as glas data, I propose one-bit header `0b0` for embedded data and `0b1` for names, while an AST argument is recognized as a pair, and constructor arguments as a list. Any extensibility concern can be addressed by introducing new AST constructors.

A constructor is a function name. The constructor function type is constrained to `List of Arg -> AST` with limited side effects. Primitive constructors might be named '%addi', '%cond', '%seq', and so on. I propose prefix '%' for syntactic control, concision, and clarity by convention. Primitive constructors are abstract, to be provided by a runtime or later-stage compiler. The returned AST data type is also abstract but may be observable through a reflection API.

A programming language can support metaprogramming based around the abstract AST, e.g. involving user-defined constructors, or support for JIT compilation and dynamic evaluation of AST nodes. It is also feasible to sandbox a subprogram, introducing user-deined AST abstractions, adapters, and optimizations. Also, an optimizer can refactor large AST nodes by defining private constructor functions in the namespace.

## Staged Arguments

I use 'ast.cond' as an example of a higher-order AST node. Its arguments are ASTs representing the condition and contingent behavior. When called, the return value is an AST that represents the composite conditional expression. In contrast, the second function parameter might be referenced as something like `(ast.arg 2)`. 

Essentially, arguments to AST operators represent static parameters, while dynamic parameters are represented via AST nodes that reference the environment. Boundaries blur a bit in context of dynamic eval, but this stage separation should still be kept in mind.

## Macros

It is easy for a language to support macro-like metaprogramming by enabling users to define and invoke their own AST constructors. This invocation would be distinguished by special syntax or keyword. The function would need the appropriate constructor type.

A relevant concern for metaprogramming is [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). This relates to how construction of an AST node gains access to parameters and local variables.

AST nodes and names should be treated as abstract types within the host language. It is feasible to design the AST such that access to locals or parameters is securely encapsulated within an abstract AST node and cannot be 'forged' by the macro, e.g. by including a reference to a private 'scope' name. A macro could be granted access to a local via special syntax or keyword. This would give us hygiene by way of [object capability security](https://en.wikipedia.org/wiki/Object-capability_model), while allowing access

## Dynamic Eval

A runtime could provide an `sys.refl.eval : AST -> Any` method, or the intermediate language itself might support an `ast.eval` as a primitive, identifying a dynamic AST in the environment. Either way, runtime support for dynamic evaluation is very convenient because it enables the user to leverage the existing machinery for JIT compilation, memoization, acceleration, and other optimizations.

## Annotations

An annotation is an AST node or argument that doesn't affect formal observable behavior of a well-behaved subprogram, and instead adds commentary. Annotations are very useful to check programmer assumptions, guide optimizations, and integrate with debuggers, profilers, and other external tools. In glas systems, every AST should have built-in support for annotations.

