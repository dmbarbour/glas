# The g0 Language

The g0 (jee zero) language is essentially a Forth variant oriented around staged metaprogramming, algebraic effects, and immutable tree-structured data. The syntax and semantics are very simple. 

This language serves as the bootstrap language for Glas systems, i.e. the language-g0 module will be written using g0 files. Upon successful bootstrap, behavior of the Glas system will depend only on state of the module system instead of the versions of external tools.

The g0 language is not suitable for every program. Other language modules should be developed to cover cases where g0 becomes awkward. 

## Top Level

The top-level of a g0 file consists of imports, definitions, assertions, and an optional export function. Line comments starting with '#' are supported.

        open foo
        from bar import qux, baz as bar-baz

        # this is a line comment
        prog word1 [ Program ]
        macro word2 [ Program ]
        assert [ Program ]
        data word3 [ Program ]

        export [ Program ]

The g0 language assumes opened and imported modules compile into dictionary values of form `(word1:prog:(do:GlasProgram, ...), word2:macro:GlasProgram, word3:data:Value, ...)`. A g0 module produces a similar dictionary value by default. All definitions are already linked into the GlasProgram, with no latent reference to the g0 namespace. This design relies on structure sharing of common subtrees to compress the representation.

If 'open' appears, it must be the first entry, inheriting an initial dictionary. If unspecified, a g0 module starts with an empty dictionary. Each word may only be explicitly defined once, and a word may not be used until after its final definition within the module. If 'export' appears, it must be the final entry, describing a function to rewrite or replace the final dictionary. 

In case of obvious error, a g0 compiler should attempt to report other errors where feasible then fail compilation. Assertions can help programmers detect errors that the g0 compiler won't normally catch.

## Words

Words in g0 must match regex: 

        WordFragment = [a-z][a-z0-9]*
        Word = WordFragment('-'WordFragment)*

All top-level imported modules and defined words must be valid words. If a module is intended for use in g0, we can assume it has a compatible name and symbols. If necessary, compile-time load effects can access non-g0 modules.

## Programs 

        [42 [foo] bar]

Programs are expressed as a sequence of words and data between square brackets. To support metaprogramming, programs are readily available as a form of data. Programs are evaluated from left to right, each word applying a program to the data stack, each data adding to the data stack. At compile-time, program words are partially evaluated when feasible, and macros must be successfully evaluated to produce a program.

## Data

The g0 program syntax has built-in support for numbers, symbols, strings, and programs.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (a min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbid C0, DEL, and '"')
        [Program]               program as a value, mostly for use with macros

The value `0` (or `""`) is the empty bitstring. 

Currently, there is no dedicated syntax for structured data. Some clever word choice can mitigate this. For example, with suitable definitions, a multi-line text could be embedded as:

        l0 "Blow, blow, thou winter wind,"
        li "Thou art not so unkind"
        li " As man’s ingratitude;"
        li "Thy tooth is not so keen,"
        li "Because thou art not seen,"
        li " Although thy breath be rude."
        li ""
        li "Freeze, freeze, thou bitter sky,"
        li "That dost not bite so nigh"
        li " As benefits forgot:"
        li "Though thou the waters warp,"
        li "Thy sting is not so sharp"
        li " As friend remembered not."
        li ""
        li "  -- William Shakespeare"
        li unlines

However, despite mitigation, the g0 syntax is unsuitable for embedding large data. Programmers should develop dedicated language modules for bulk data entry. 

## Compile-Time Effects

Macro calls, static assertions, data definitions, and the export function all have access to compile-time effects, such as log and load effects via the language module. A compiler may support additional effects - e.g. to disable a warning, enable experimental optimizations, manage quotas - but it shouldn't be anything that significantly affects the meaning or behavior of the program. 

## Definitions

### Prog Definitions

A 'prog' definition is a program with any static arity. Prog calls are evaluated at compile-time if they have sufficient arguments to fully evaluate and do not require top-level effects. Compile-time evaluation is needed to support macros. But in most cases, calls are deferred.

Most words in a g0 dictionaries will be 'prog' words.

### Macro Definitions

A 'macro' definition is a program that will be statically evaluated in context of its caller, using static inputs based on partial evaluation. The first result from a macro must be a value representing a program. This program is applied inline just after the macro call. 

When used together with g0's ability to embed programs as data, macros support higher-order programming limited to compile-time. List processing, parser combinators, and many other program abstractions may be supported via macros.

### Defining Primitives

The g0 language does not have any built-in definitions. Macros and embedded data are sufficient to express all Glas programs, though we'll usually favor 'prog' definitions for performance reasons.

        macro apply [] # apply first parameter as program
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 'dip put]
        macro while-do [0 'do put 'while put 0 'loop put]
        ...

The g0 ecosystem includes a module that defines primitives and other useful low-level functions.

### Data Definitions

Data is defined using a program of arity 0--1. After macro evaluation, this program is evaluated at  compile-time, with access to compile-time effects. The generated value on the data stack becomes a `data:Value` definition in the dictionary. Failure to evaluate a data definition causes the whole module to fail.

## Static Assertions

Assertions are 0--Any static arity programs, favoring 0--0. After macro evaluation, assertions are evaluated with an empty data stack and access to compile-time effects. The primary output is pass/fail; if evaluation of an assertion fails, the module also fails to compile. However, secondary output includes the compile-time log. If an assertion has a non-empty stack output, that is logged implicitly.

Static assertions within g0 programs are a convenient basis for lightweight unit and integration tests. They can also be used for user-defined static analysis. For example, with suitable definitions, `assert [[foo] type-check]` could analyze whether `foo` has a type consistent with internal annotations. 

*Note:* We don't want assertions for typechecks to become boilerplate. It is preferable if the compiler verifies easily checked *Annotations* implicitly. Assertions should mostly be used where annotations or implicit compiler checks are lacking for whatever reason.

## Annotations

There is no dedicated syntax for annotations in g0. However, it is feasible to programmatically build and macro-apply annotations, and a compiler should not erase or bury user-provided annotations. With suitable definitions, `prog foo [[P] Annotations annotate apply]` might result in a dictionary with `(foo:prog:(do:P, Annotations), ...)`, perhaps extending with compiler-provided annotations.

The compiler is free to verify user-provided annotations that it recognizes, perhaps failing a module based on an invalid stack-arity or type annotation. 

*Aside:* External annotations are also feasible, e.g. one module could wrap another with annotated definitions, either manually or programmatically via 'export'.

## Export Function

The export function is a program of static arity 1--1. It is either the final entry in a g0 file or, if left unspecified, is equivalent to `export []`. After compiling the export program, it is evaluated with access to compile-time effects. The dictionary of definitions is input on the data stack. The value output from the export function becomes the compiled value of the g0 module.

Export functions enable g0 to define words programmatically, merge dictionaries, or produce values suitable for non-g0 contexts. Or when accessing non-g0 programs from g0, we could use an intermediate module with an export function to compile the target into something g0 can use.

*Aside:* For export control, a more convenient option in many cases is treat the 'public' module within a folder as the whitelist for exported symbols, a file with only `from ... import ...` entries. 

## Compilation Strategy for Programs

The AST for g0 programs after parse is essentially a simple list of 'call:word', 'block:G0', and 'data:Value' operators. 

A call is replaced by the associated prog or macro. A macro is evaluated, with access to prior data entries and compile-time effects, then the top data entry becomes a prog. A prog is evaluated if there is sufficient input data and evaluation does require any top-level effects, otherwise it is not further compiled. A block is recursively compiled then becomes a 'data:Program' entry. Data is not further compiled.

The compiler may further optimize programs, but should carefully stabilize how compilation interacts with macro expansion. Relevantly, with respect to subsequent macro evaluation, prior calls are either fully evaluated or not at all, and arity should be stable. For example, `[swap swap]` might optimize to `[]` but the compiler could add an annotation to preserve original 2--2 arity.

A g0 compiler might impose quotas for compile-time computation. Any such quotas must be deterministic, e.g. based on loop counters instead of CPU time. However, quotas could be tunable via annotation.

## Bootstrap 

Assume an implementation of g0 is built-in to the command-line tool. This implementation is used to build module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior but may differ structurally due to optimizations or annotations. 

To resolve this, we use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are structurally equivalent, we have successfully bootstrapped g0. If not, bootstrap fails and we must debug. Essentially, we require language-g0 to reach a fixpoint almost immediately.

A remaining goal is bootstrap of the executable for the command-line tool. This requires extracting a binary executable from the Glas module system that can adequately replace the original command-line utility.

## Extension

The g0 syntax should not be extended very much because a significant goal is to preserve simplicity of bootstrap. However, if there are any features that simplify bootstrap or at least do not significantly complicate compilation, we can consider them. In many cases, it is wiser to design an alternative language module that implements the desired features.

Proposed extensions accepted so far: data definitions, static assertions, export function.

### Variables? Rejected.

Variables are often more convenient than manual data-plumbing with swap, drop, etc.. However, proper support for variables (mutation, pass by reference, safe interaction with hygienic macros, etc.) is more sophisticated than I'd prefer to support in the g0 compiler. This is left to a future post-g0 language. 

### User-Defined Operators? Rejected.

Without variables or support for overloading, use of operators is relatively awkward. So, this should be added to a post-g0 language.

### Multi-Stage Macros? Tentative.

Macros currently must produce a `data:Program` value at the top of the stack. We could extend this to specially handle `data:macro:Program`, further evaluating the result as a macro. This is slightly more expressive than single-stage macros, but most use cases I can think of would also be bad practices. I've decided to defer this extension at least until a strong, sensible use-case is discovered.

### Lists of Programs? Rejected.

Idea is `{ foo, bar, baz }` syntax produces a list of program blocks, which a macro could process. This could simplify concise expression of lists or dicts, e.g. as `{1, 2, 3}` or `{'x 1, 'y 2, 'z 3}`. However, it would interact awkwardly with data abstraction and complicates construction of data, so I've decided against this feature.

### Macro State or Aggregation? Rejected.

A compiler could enable macros to communicate via compile-time effects, e.g. through shared compile-time tables or variables. This has many potential use-cases. However, I'd prefer to avoid coupling the concept of compile-time communication to local module boundaries. Further, I want to preserve a very declarative style at the higher program layers, e.g. where order of imports and definitions is irrelevant, and this requires some careful API design.

I think it's better to defer this feature to after g0. We might build a syntax from ground up to work well with overloading definitions, ambiguity and probable meaning, preferences, and constraints via compile-time search and shared constraint models.

### Assertion Parameter? Rejected.

Similar to export, we could provide the dictionary as an input to assertions. This would allow more flexible analysis, but will also complicate reasoning because the dictionary would be specific to each assert (depending on following definitions) instead of a stable value. 

I've decided to avoid this complication. Assert serves a useful role as-is. Any assertions on the full dictionary are better performed by 'export'. 

### Hierarchical Definitions? Tentative.

        from basics import dip
        import math as m
        prog dist [ [m.square] dip m.square m.add m.sqrt ]

The proposal here is to support hierarchical imports and dotted-path naming. In this case, our dictionary should roughly consist of `(m:data:ModuleValue, dist:prog:(...), dip:macro:(...))`, and use of dotted paths such as 'm.sqrt' would extract a program from the data. Using 'data' for this role, we retain limited compositionality, i.e. 'm.sqrt' can be decomposed as two actions 'm .sqrt'. Use of 'import math' alone would be equivalent to 'import math as math'. 

However, use of deep hierarchical references is a bad practice. It violates Law of Demeter. Access to 'sqrt' using different prefixes from different files, such as 'm.sqrt' and 'math.sqrt', is an awkward user experience compared to flat dictionary per project. Readability takes a hit when trying to parse dotted paths.

I hope to fill this role with other solutions, such as developing an intermediate module that aggregates external and utility definitions into a flat dictionary. However, I might revisit use of hierarchical references depending on how things work in practice.
