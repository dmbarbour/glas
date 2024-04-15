# Abstract Assembly

This document proposes a one-size-fits-all intermediate language in context of a [namespace](ExtensibleNamespaces.md). This intermediate language might represent an assembly or bytecode. The candidate structure is reminiscent of Lisp or Scheme: 

        type AST = Constructor Arg*
        and  Arg = Data | Name | AST            

To support renames, names must be clearly distinguished in the intermediate language. Assuming represention as glas data, I propose one-bit header `0b0` for embedded data and `0b1` for names, while an AST argument is recognized as a pair, and constructor arguments as a list. Any extensibility concern can be addressed by introducing new AST constructors.

A constructor is a function name. The constructor function type is constrained to `List of Arg -> AST` with limited side effects. Primitive constructors might be named 'ast.addi', 'ast.cond', 'ast.seq', and declared in the namespace as abstract methods to be provided by the runtime or a later-stage compiler. Or perhaps we favor '%addi' for syntactic control and concision. The returned AST data type may be abstract, observable only through a system reflection API.

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

## Sandboxes and Adapters

It is feasible to intercept the AST used by a subprogram, introduce a user-defined AST type and optimization passes before finally compiling to the 'host' AST. Conversely, we might wrap 'host' methods before providing them to the  

and introduce a layer between the main application and subprogram. This provides an opportunity for the user to define their own concrete AST then inspect a program, performing ad-hoc analysis and optimizations.

However, we'll also need an adapter between ASTs. This would benefit from a namespace-level operation to slightly modify every definition in the namespace, e.g. adding an AST constructor that represents a wrapper for host methods or a final compilation step to the 'host' AST.

## Annotations

An annotation is an AST node or argument that doesn't affect formal observable behavior of a well-behaved subprogram, instead adds some commentary on it. Annotations are very useful to guide optimizations, to check programmer assumptions, to integrate with external tools. As an important part of my vision for glas systems, glas languages should support annotations at every layer.

