# Abstract Assembly

Assume programs are defined in an [extensible namespace](ExtensibleNamespaces.md). The compiler will produce a representation of each method's behavior in an intermediate language, perhaps a bytecode or AST. This intermediate language includes references back into the namespace, such as the names of other methods called.

This document proposes a one-size-fits-all intermediate language. Candidate structure: 

        type AST  = Constructor Expr*
        and  Expr = Name | Data | ( AST )

When compiling methods, the compiler will represent abstract AST nodes as function calls to abstract AST constructors such as 'ast.addi' or 'ast.cond'. These methods are eventually provided by the runtime or a later-stage compiler. The AST node type will usually remain abstract.

A programmer may restrict or override AST constructors available to a subprogram. It is feasible to construct AST nodes programmatically to support staged metaprogramming or dynamic eval. It is possible to implement adapters to alternative ASTs. However, many optimizations must be deferred.

This structure probably feels familiar to Lisp or Scheme programmers. It is simple and sufficient. This does require restricting the 'type' of AST constructors, i.e. they are limited to positional parameters of just a few types, the return type must also be an AST node, and side-effects may be restricted. But the benefit is simplified representation.

*Aside:* If a large AST node would be copied many times, this can be mitigated by structure sharing or by separating that AST node into a private constructor. In any case, it isn't significant problem.

## Staged Arguments

I use 'ast.cond' as an example of a higher-order AST node. Its arguments are ASTs representing the condition and contingent behavior. When called, the return value is an AST that represents the composite conditional expression. In contrast, the second function parameter might be referenced as `(ast.arg 2)`. 

Essentially, arguments to AST operators represent static parameters, while dynamic parameters are always represented via AST nodes that reference the environment. Boundaries blur a bit in context of dynamic eval, but this stage separation should still be kept in mind.

## Data Arguments

It is feasible to represent data by constructing a program that will reconstruct that data. However, it's much more convenient to just support data arguments. The latter integrates nicely with content addressed storage and access to 'data' modules.

## Metaprogramming

The user language will compile an if/then/else expression to 'ast.cond'. Metaprogramming should allow users to go the other direction - write an expression that produces 'ast.cond' and embed it within their program in place of a conditional expression. 

To support this, the language must support static eval. Otherwise 'ast.cond' would not be subject to the same validation and optimization as the if/then/else expression it replaced. Additionally, the language must provide syntax or a keyword to apply the generated AST node.

## Hygiene

A relevant concern for metaprogramming is [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). This relates to how construction of an AST node gains access to parameters and local variables. 

The simplest option to implement is to ignore hygiene. AST nodes in the intermediate language reference parameters and locals by name or index, similar to a bytecode. Although this doesn't provide hygiene, this can be mitigated by annotations. For example, we could say that a subprogram should only read or write specific variables.

Alternatively, local scopes may be associated with the namespace. A local might be referenced by a `(name, index)` pair encapsulated into an abstract AST node. The language may include special syntax or keywords to obtain AST references. This would ensure hygiene by way of [object capability security](https://en.wikipedia.org/wiki/Object-capability_model).

## Dynamic Eval

The intermediate language or runtime may provide some means to evaluate an AST node at runtime. This could be expressed as a primitive operator 'ast.eval' that is parameterized by a variable, or as a non-standard runtime extension 'sys.refl.eval' that is called with an AST program parameter.

A built-in eval is often more convenient than writing an interpreter by hand. It implicitly integrates with the underlying JIT compiler, acceleration, and other optimizations. And it's feasible to eliminate the JIT subsystem if dead code analysis determines 'eval' isn't called or via use of dynamic loading.

## Annotations

An annotation is an AST node or argument that doesn't affect formal observable behavior of a well-behaved subprogram, instead adds some commentary on it. Annotations are very useful to guide optimizations, to check programmer assumptions, to integrate with external tools. As an important part of my vision for glas systems, glas languages should support annotations at every layer.

## Reflection

Abstract AST constructors such as 'ast.cond' are usually one way, but we could support reflective access to AST nodes, such as 'sys.refl.ast.is-cond' returning a boolean.

## Ephemerality and Locality

The abstract AST nodes should be ephemeral, albeit cacheable and often statically computed. That is, they cannot be stored or communicated between transactions. Users cannot cache them manually, but could ask their system to memoize dynamic construction of an AST from a script.

AST nodes are also local. That is, we cannot inspect or evaluate them from within another application's namespace. But in context of RPC and code distribution, even dynamic eval might construct a distributed program, i.e. where some code fragments are pushed to remote nodes to avoid unnecessary round-trips and network traffic. 

## Notes on Optimization

The primary feature of a namespace is that definitions are late bound. Although this is convenient for extension and patching, many optimizations must be deferred until definitions are finalized. To mitigate this, it would be best if the intermediate language defines AST nodes or annotations to robustly guide annotations or receive effective feedback when an optimization cannot be implemented.

## Notes on Adapters

Assume a subprogram is written in a DSL with a specialized AST, such as a language for regular expressions or matrix manipulations. This must be adapted to the host language. 

A direct solution can translate every individual operation in the DSL to a series of operations in the host language. However, this will miss out on potential optimizations, such as reordering the sequence of matrix multiplications based on matrix sizes, or merging common prefixes for regular expressions. 

If we want to maximize opportunities for optimization, we'll need to construct the full DSL program before adapting it to the client. This would require an extra step. We could manually override each method to apply the final optimization. With some language support, it might be feasible to apply the same override to every method in a namespace or interface.

