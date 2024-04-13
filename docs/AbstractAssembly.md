# Abstract Assembly

Assume programs are defined in an [extensible namespace](ExtensibleNamespaces.md). The compiler will produce a representation of each method's behavior in an intermediate language, perhaps a bytecode or AST. This intermediate language includes references back into the namespace, such as the names of other methods called.

This document proposes a one-size-fits-all intermediate language. Candidate structure: 

        type AST  = Type Expr*
        and  Expr = Name | Data | AST

This probably feels familiar to Lisp or Scheme programmers. I'll probably tweak this to also support keyword arguments. Anyhow, of greater concern is how we represent AST node Type. There are effectively two relevant options:

* Type is glas data, probably a symbol or string. 
* Type is a name, an abstract constructor, subject to access control, overrides, and renames through the namespace layer.

I propose to represent AST node type as a name. The user-defined language compiler will produce definitions referring to 'ast.addi' and 'ast.cond' and similar. Or perhaps '%addi' and '%cond' to save a few bytes and discourage direct use of these names in the user syntax. 

This use of names provides many benefits. A later-stage compiler can directly perform local error analysis, peephole optimizations, and cache compilation as the AST nodes are constructed. Metaprogramming is more accessible. Users can define adapter layers for software components or DSLs that target alternative intermediate languages. The namespace can be leveraged to control a subprogram's access to troublesome operations, such as a non-deterministic 'ast.amb'.

The cost of this design is that many optimizations are not accessible to incremental compilation, and we might need to typecheck user-defined AST constructors to control use of side-effects, and glas languages will need to handle threading of 'ast' methods into hierarchical application components. 

## AST args vs Function args

I've used 'ast.cond' type as an example of a higher-order operation. Its arguments are ASTs, and when called as a function it returns an AST that composes its arguments to abstractly represent a conditional behavior. This is an entirely different layer from using `(ast.arg 2)` to refer to the second argument of a function call at runtime. 

Essentially, any arguments to AST operators are static similar to macro or template arguments. These arguments are provided by a compiler. We can use static AST nodes to reference runtime arguments. This can get a little confusing in context of dynamic eval, but each AST node is staged.

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

To support intermediate optimizations, e.g. to optimize a sequence of matrix multiplications based on matrix sizes, or to merge common prefixes for regular expressions, we must first introduce an intermediate representation of the DSL AST then compile this to the host AST later.

The extra step of compiling to the host AST seems awkward to express manually. It is feasible to add some indirection when calling methods of the component DSL, add the optimization pass as a static eval at the call site. 

To systematically wrap every method with an optimization may benefit from a namespace-layer operation.

## Embedded Data

Most intermediate languages for glas systems should include nodes that take glas data and (if needed) validate and translate it for use in the intermediate language. The ability to directly represent data in the AST simplifies integration with content addressed storage and data modules (simple compared to using AST nodes to construct data).

