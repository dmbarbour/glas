# Language g0

The g0 (jee zero) language is a Forth variant with staged metaprogramming, algebraic effects, and immutable tree-structured data. The simple syntax and semantics of g0 are intended to remain close to the g0 program model and simplify bootstrap. This is the only bootstrapped language in the Glas system, so all other languages must be implemented (perhaps indirectly) using g0.

The g0 language is not suitable for all programs, nor for all programmers. It's certainly unsuitable for embedding large amounts of data. Fortunately, the Glas system makes it relatively easy to define and use other languages with different file extensions.

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

The g0 language compiles modules into dictionaries, a record such as `(word1:prog:(do:CompiledProgram, ...), word2:macro:CompiledProgram, word3:data:Value, ...)` mapping each word to its definition. 

If 'open' appears, it must be the first entry and refer to a module that outputs a dictionary. This becomes the initial dictionary; without open, a g0 module instead starts with an empty dictionary. If 'export' appears, it must be the final entry, and may rewrite or replace the final g0 dictionary, determining the module's final compiled output. This supports metaprogramming of dictionaries and output of non-dictionary values.

## Words

Words in g0 must match a simple regex: 

        WFrag: [a-z][a-z0-9]*
        Word:  WFrag('-'WFrag)*

Within a g0 file, each word may only have one definition. That is, a word may be defined only once, and cannot be defined if it has already been used (we'll assume its definition is via 'open'). 

The g0 language cannot directly import or reference definitions that don't have valid g0 words. This isn't a problem when working mostly within g0, but importing definitions from modules written in other languages might require using macros to extract the definition.

## Embedded Data

The g0 program can directly include numbers, symbols, strings, and programs.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (a min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 a list of ascii bytes; forbid C0, DEL, and '"'
        [Program]               program as a value, mostly for use with macros

The value `0` (or `""`) serves multiple roles as the empty bitstring, the empty list, the empty record, and the unit value. For clarity of intention, it's best to define something like `data unit [0]` for each of these cases, though. Program values are locally non-deterministic due to compiler optimizations and annotations.

There is no dedicated syntax for structured data. Clever word choice can mitigate this. For example, with suitable definitions, a multi-line text could be embedded as:

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

Despite mitigating techniques, the g0 syntax neither intended nor well suited for bulk data entry. Large texts should instead be embedded in a `.txt` file, with programmers defining `language-txt` module. For bulk structured data, we could define modules to read JSON, MsgPack, Cap'n Proto, SQLite, or other data formats. 

## Programs 

        [42 [foo] bar]

Language g0 programs are expressed as blocks of words and data delimited by square brackets. Programs themselves can be embedded as data, mostly for use in macros.

## Compile-Time Effects

Macro calls, assertions, data definitions, and the export function can use compile-time effects. This includes the 'log' and 'load' effects available to language modules. Additionally, the compiler can introduce effects for [compiler directives](https://en.wikipedia.org/wiki/Directive_%28programming%29). Directives could feasibly control warnings, tune quotas, or enable experimental optimizations. But they should be used sparingly to to minimize risk of complicating the g0 language.

## Definitions

### Macro Definitions

A 'macro' definition is a program that will be statically evaluated at each call site for staged metaprogramming. The first output (top of stack) from a macro must be a value representing a program, which is treated as if it were the definition of a prog call immediately following the macro call.

Most higher-order programming will be based on macros. Glas systems can also support higher-order programming via acceleration of interpreters, but macros are much more amenable to further optimization.

### Prog Definitions

A 'prog' definition is for normal runtime behavior. Evaluation of prog calls is normally deferred. However, a prog call must be evaluated immediately if blocks input to a macro. Immediate evaluation is possible if the called program has sufficient input and doesn't require external effects.

### Data Definitions

A 'data' definition is a program of arity 0--1 that is statically evaluated to produce a data value. At the call site, we will simply replace a data word by its already computed data.

## Primitive Definitions

The g0 language does not have any built-in definitions. Instead, we'll use macros and embedded data as the foundation for constructing arbitrary Glas programs, as follows:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 'dip put]
        macro while-do [0 'do put 'while put 0 'loop put]
        ...

Currently these are defined in the [prims module](../prims/public.g0). 

## Static Assertions

        assert [test or analysis code here]

Assertions are 0--Any static arity programs that are evaluated at compile-time. Primary output is pass/fail, with a failed assertion causing compilation of the module to fail. Compile-time effects can be used to generate log messages.

Static assertions serve a role in lightweight unit and integration tests. They can also support for user-defined static analysis. For example, with suitable definitions, `assert [[foo] type-check]` could analyze whether the behavior of foo is consistent with internal type annotations.

## Annotations

There is no special syntax for program annotations in g0. But programmers may manually add annotations to programs, and the compiler will preserve them and may verify any annotations it recognizes. For example, `prog foo [[P] 'builtin-foo anno-accel apply]` might result in a dictionary containing `foo:prog:(do:P, accel:builtin-foo)`. The compiler is free to verify any user-provided annotations that it recognizes, such as reporting an error if a program has an unrecognized static arity annotation.

## Export Function

The export function is a program of static arity 1--1. If present, it must be the final entry in the g0 file, otherwise is equivalent to `export []`. After compiling the export program, it is evaluated with the module's compiled dictionary value as the input, and the final output (not necessarily a g0 dictionary) becomes the module's compiled value.

This feature enables metaprogamming of the dictionary itself, though the metaprogrammed dictionary is not accessible within the same file that generates it. It also simplifies integration with non-g0 modules.

*Aside:* For export control, define `modulename/public.g0` with a sequence of `from ... import ...` entries for other files within the same folder. The export function is overkill for that purpose, and more difficult to understand.

## Compilation Strategy

After parse, the AST for g0 programs is essentially a list of `block:Program | call:Word | data:Value`, with data including all embedded data. Blocks compile recursively. How calls are handled depends on the word type - macro, prog, or data. Data compiles trivially, whether it's embedded data or calling a data word. This leaves macro and prog calls.

Macro calls are evaluated at compile-time. The first output of a macro call should be a program, which is treated as if it were the definition for a prog call immediately following the macro call. If a macro fails to evaluate, whether due to insufficient static input or failure during evaluation, compilation of the entire module will fail. 

Prog calls are evaluated at compile-time if there are sufficient inputs (based on program arity) and evaluation does not require any external effects. The intention here is that a program such as `[1 2 add my-macro]` will evaluate `add` such that data is available to `my-macro`. If a prog call cannot be evaluated at compile-time, the program definition is inlined and evaluation is deferred.

*Note:* Due to inlining of program definitions, compiled programs tend to grow exponentially larger than their g0 source with common code being repeated many times. Glas systems resolve this using structure sharing of common subtrees. Additionally, before we compile Glas programs into executable binaries, we can perform a compression pass.

## Compilation Quotas

It's convenient to know that the g0 compile will take a controlled amount of time. This can be supported by use of quotas, failing the compile if the quota is exhausted. The compile function cannot observe real-world CPU time, but we could introduce a step counter when interpreting programs.

If we do introduce quotas, we'll also want to support extra-long builds for known modules. This could be supported via annotations or compile-time effects.

## Bootstrap 

The glas command line executable will have a built-in g0 compiler that is used only for bootstrap. Bootstrap will be logically recomputed every time we use the executable, but the rework can be mitigated by caching.

The built-in g0 is used to compile module language-g0, producing a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same *behavior* but may differ structurally due to variations in optimizations or annotations. It's difficult to check that the behaviors are the same, but we can easily use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are exactly the same, we have successfully bootstrapped language-g0. Otherwise, time to debug!

## Proposed Language Extensions

Some ideas that I haven't rejected yet, but also haven't committed to.

### Multi-Stage Macros

Macros currently must produce a program value at the top of the stack. We could extend this to specially handle `macro:Program`, further evaluating the result as a macro. This is slightly more expressive than single-stage macros, but most use cases I can think of would also be dubious practices. I've decided to defer this extension at least until a strong use-case is discovered.

### Hierarchical Definitions

        from prims import dip
        import math as m
        prog dist [ [m.square] dip m.square m.add m.sqrt ]

The proposal here is to support hierarchical imports and dotted-path naming, similar to many languages. 

In this case, our dictionary should roughly consist of `(m:data:ModuleValue, dist:prog:(...), dip:macro:(...))`, and use of dotted paths such as 'm.sqrt' would extract a program from the data then apply it. Using 'data' for this role, we retain limited compositionality, i.e. 'm.sqrt' can be decomposed as two actions 'm .sqrt'. Use of 'import math' alone would be equivalent to 'import math as math'.

However, use of deep hierarchical references is a bad practice, and I find this aesthetically unpleasant. I probably won't ever need this feature in language-g0. 

### Private Definitions

We could use a simple convention for defining symbols that are implicitly removed from the exported dictionary, e.g. support for `private macro foo [...]`. I don't believe this is needed. For export control, I'd suggest using the `public.g0` module within a folder to explicitly import all definitions we want to share outside the module. 

