# Abstract Assembly

This document proposes a one-size-fits-most intermediate language in context of a [namespace](ExtensibleNamespaces.md). This intermediate language might model an assembly or bytecode. The candidate structure is reminiscent of Lisp or Scheme: 

        type AST = Name Arg*
        and  Arg = Data | Name | AST

The big idea for abstract assembly is that AST constructors are declared in the namespace, subject to rename, access control, abstraction, and override. Primitive constructors might be named '%addi', '%cond', '%seq'. These methods and the AST data type are abstract, determined by the runtime or late stage compiler.

## How is this useful? 

Even without user-defined AST constructors, we could use the namespace to hide '%amb' from a subprogram that *should be* deterministic, or we could substitute '%debug-assert' for '%pass' to locally disable runtime tests. An AST could be designed to support a few useful substitutions.

User-defined AST constructors are feasible, perhaps expressed as pure functions of type `List of Arg -> AST`. These would serve a role similar to macros or EDSL compilers. Of course, glas also supports user-defined syntax via language modules. User-defined AST constructors can offer an advantage where we might want late binding, abstraction, or override through the namespace.

Users might also override all primitive AST constructors forwarded to a subprogram. This is difficult to leverage without a reflection API and bulk namespace operators. But, after everything is in place, user programs could implement ad-hoc language adapters, sandboxes, profilers, debuggers, and optimizers.

## What is the cost?

If we were to use abstract assembly under the hood but otherwise ignore it for language design purposes, there would be no syntactic or runtime overhead, and only negligible compile-time overhead. Mostly, we would need an additional rename for hierarchical components. For example, if we add a prefix 'foo.' to all names defined in component 'foo', we would need to follow that with a rename 'foo.% => %'. A compiler can do this by default.

But to effectively leverage abstract assembly does introduce some design overheads - new syntax, tweaks to AST constructors to support macro hygiene, and so on.

To leverage abstract assembly, we'll need some dedicated syntax, and we might introduce extra AST constructors to support common overrides. This mostly has some design costs.

## Concrete Representation

We must precisely distinguish between data and names. 

I propose a simple non-algebraic representation:

        type Expr = 0b0:Value            // Data
                  | 0b1:Binary           // Name
                  | (Expr, List of Expr) // AST 

Extensibility depends entirely on defining new AST constructors. This representation can be reused for a concrete assembly where constructors are represented by data.

## Staged Arguments

Under normal circumstances, arguments to an AST constructor are static, but they may represent access to the future evaluation environment. For example, we use `(%arg 2)` as a static AST parameter to '%addi' but this represents reading the second argument to the local method call at runtime. This is common for any intermediate language, but it can get confusing without the occasional reminder.

## Names and Name Capture

I propose to represent names as arbitrary binaries, restricted only by program syntax. Any hierarchical structure for names is also a syntactic convention. 

However, names cannot be constructed from a binary at runtime. Runtime construction of names interacts too awkwardly with local renames and dead-code elimination. This also hurts security because access control to names is ultimately based on renames and is part of our [security assumptions](https://en.wikipedia.org/wiki/Object-capability_model).

Insofar as users need access to names at runtime, the language should provide keywords or special syntax. For example, `&foo` might compile to AST node `(%data foo)`, which evaluates to an abstract Name. Usefully, 'foo' would be subject to renames and dead code elimination.

If users need a table of names to support dynamic eval, they can create one manually, e.g. `[("foo", &foo), ("bar", &bar), ...]`. It is feasible to construct such tables automatically based on compile-time inspection of a namespace.

Anyhow, the language module compiler will simply output names as binaries in the abstract assembly. It's only a program's access to its own names that needs special attention.

## Macro Hygiene

A user-defined AST constructor can serve a similar role as a macro. A relevant concern for macro-like metaprogramming is [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro). This concerns how much access a macro is granted to the program environment. For example, if the returned AST node can access arbitrary variables such as `(%arg 2)` then that might be an issue.

It is feasible to leverage name capture in this role. Parameters and local variables could be associated with *named* scopes. For example, `(%arg 2 &scopeName)` would still refer to the second argument, but the macro program cannot forge the scope name, and use of the AST in the wrong scope is an obvious error. Instead we'd provide the abstract AST node a parameter to the macro. This would ensure hygiene by means of ocap security.

## Dynamic Eval

The intermediate language can define a primitive '%eval' operator whose argument represents an AST expression. The runtime would evaluate the expression then run the returned AST. This provides implicit access to the runtime's JIT compiler, memoization, acceleration, and other optimizations. It's very convenient.

Abstract assembly can help make dynamic eval syntax-independent, secure, and integrated. User code will generate the intermediate language AST based on the available AST constructors, and is subject to the name capture and macro hygiene described above. Further, we can easily restrict a subprogram's access to '%eval' to simplify static reasoning about a program. 

## Static Eval

Static eval can help us fully leverage abstract assembly for metaprogramming. Annotations can indicate assumptions about static evaluation and guide a compiler. A language could introduce AST constructors like '%static-if' to make static evaluation explicit, allowing for compile-time effects. 

Intriguingly, it is feasible for a type system to track and control which parameters and results in function calls are statically computable. A simple example might be `(Static<A>, B) -> (Static<X>, Y)`, while more sophisticated examples might involve session types. Use of static parameters is similar to template metaprogramming.

## Annotations

An annotation is an AST node or argument that doesn't affect formal observable behavior of a well-behaved subprogram, and instead adds commentary. Annotations are very useful to:

* check programmer assumptions - types, contracts, quotas, proofs
* guide optimizations - acceleration, memoization, content-addressed storage, distribution, JIT hints
* integrate external tools - debuggers, profilers, reporting errors, inferring blame 

Abstract assembly doesn't really change the nature of annotations, but it does make it easier to work with fine-grained AST constructors for each use case.

## Keyword Arguments

The `List of Arg -> AST` function type can be awkward when developing user-defined AST constructors. We can express keyword arguments by introducing a parameter object, and there may even be primitive support for this in the AST. But it's still awkward to receive a list containing the parameter object.

To properly support keyword arguments, we might leverage '%static-eval' to produce an AST node that is then passed as a constant to '%eval'.

