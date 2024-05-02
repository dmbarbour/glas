# Abstract Assembly

The big idea for abstract assembly is that a front-end compiler expresses an intermediate-language in terms of abstract methods within the same [namespace](GlasProgNamespaces.md) as user-defined methods. This subjects compiled definitions to namespace-layer access control, extension, and override. The proposed representation is reminiscent of Lisp or Scheme:

        type App = (0b1:Name, List of Arg)
        type Arg = 0b0:Data
                 | 0b1:Name
                 | App

Primitive AST constructors might be named '%addi', '%cond', '%seq', etc.. A front-end compiler can forward to hierarchical components by default via prefix rename 'foo.% => %'. With language support, users could control forwarding, e.g. to hide '%amb' from a deterministic subprogram or substitute '%debug-assert' with '%pass' to disable debug testing for a specific component.

User-defined AST constructors are possible contingent on context, perhaps expressed as pure functions of type `List of Arg -> AST`. The AST data type is usually abstract, requiring construction in terms of primitives. User-defined constructors can serve a role similar to macros or EDSL compilers, but with potential benefits from evaluation in a later stage.

Intriguingly, it is feasible to *sandbox* a subprogram, implementing user-defined 'primitive' constructors for a subprogram then applying an adapter to 'compile' back into the host AST. This sandbox can support emulation or adaptation between intermediate languages and provides opportunity for user-defined profiling, debugging, and optimizations.

## Names and Name Capture

There should be no `Binary -> Name` function at runtime. Why not? Because such a function would complicate reasoning about scope of names, rename context, and dead-code elimination. This would further impact [security assumptions](https://en.wikipedia.org/wiki/Object-capability_model).

However, it is feasible for the front-end compiler to help capture access to names, i.e. syntax `&foo` might compile to `(%data 0b1:"foo")`. To support some form of 'eval', users can still create lookup tables manually, e.g. `[("foo", &foo), ("bar", &bar), ...]`, and this could be automated by metaprogramming in the front-end language.

## Capture of Stack Variables

We could make locals unforgeable by associating them with names, e.g. `(%local &privateScopeName 2)`. A late stage compiler could easily detect which names are in scope. This would give us [macro hygiene](https://en.wikipedia.org/wiki/Hygienic_macro) via object capability security.

## Dynamic Eval

The intermediate language can define a primitive '%eval' operator, or a runtime could supply 'sys.refl.eval' as an effect. Either way, it might be convenient to express this in terms of evaluating an AST node, implicitly leveraging the runtime's built-in JIT compiler, memoization, acceleration, and other optimizations. This avoids tying 'eval' to any particular front-end syntax.

The main challenge with 'eval' is interaction with a type system, i.e. ensuring the AST has behavior that is compatible in context. Safe eval might require tracking type information in the intermediate language, and perhaps restricting the types of expressions we may evaluate.

*Note:* Intriguingly, if '%eval' proves to be unused in the application, a late-stage compiler could drop expensive JIT support. 

## Static Eval

Static evaluation can easily be guided by annotations and independent of semantics. Alternatively, the intermediate language could support '%static-if' to make static evaluation explicit. The main difference regards staging. With annotations, we're limited to effects where it doesn't matter when they evaluate. With explicit static eval, we can explicitly support compile-time effects.

Effective support for static eval should enhance metaprogramming with abstract assembly. Ideally, we should be able to abstract over 'const' arguments to methods when constructing AST nodes. 

## AST Layer Effects? Tentative.

With abstract assembly, we could push some effects to the AST layer without losing namespace based access control. This is tempting for higher-order effects, e.g. `%amb` for non-deterministic choice of statements, where we want close integration with the syntax, type system, and optimizer.

However, I currently favor procedural effects even where it's a little awkward. For example, instead of `%amb`, we use `sys.fork` returns an integer that we then use in a conditional expression. It would take a very strong use case for me to favor AST layer effects.

## Concrete Assembly? No.

Abstract assembly insists on a Name argument in the App constructor field, but I left the '0b1' header so we can parse this as `type App = (Arg, List of Arg)`. This supports reuse as a concrete assembly, where the constructor argument is represented by data. It also allows use of App in constructor position, which might be interpreted as an inline user-defined constructor.

That said, I don't see any strong use case for introducing concrete or inline user-defined constructors into the abstract assembly. Even assuming we discover one, we can use `(%app Arg Args)` or a more precise abstract primitive constructor per use case. 
