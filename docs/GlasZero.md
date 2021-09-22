# The g0 Language

The g0 (jee zero) language is essentially a Forth variant with staged metaprogramming, algebraic effects, and immutable tree-structured data. The syntax and semantics are very simple. 

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

The g0 language assumes imported modules have compiled values of form `(qux:prog:Program, baz:macro:Program, ...)`. The default output from a g0 module is the dictionary visible at the bottom of the file, e.g. `(bar-baz:(...), word1:prog:(do:Program, ...), word2:macro:Program, word3:data:Value, qux:(...), other symbols from foo)`. 

If 'open' appears, it must be the first entry, inheriting an initial dictionary. If unspecified, the g0 module starts with an empty dictionary. Within the module, words may only be explicitly defined once, and a word that is explicitly defined cannot be used before its definition. If 'export' appears, it must be the final entry, describing a function to rewrite or replace the final dictionary.

In case of error, a g0 compiler should attempt to report other errors where feasible before failing compilation. Assertions can help programmers detect errors that the g0 compiler won't normally catch.

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

Currently, there is no dedicated syntax for structured data. We can only construct data on the stack, or process a string. Some clever word choice can mitigate this. For example, with suitable definitions, a multi-line text could be embedded as:

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

We could similarly use 'd0' and 'di' to embed dictionaries. However, despite mitigation, the g0 syntax is awkward for significant data embeddings. It is wise to embed any significant volume of data into other files using either a syntax suitable for the data in question, or a generic syntax similar to JSON or MessagePack (perhaps extended with module references), then develop a language module to extract the structed data.

## Compile-Time Effects

Macro calls, static assertions, data definitions, and the export function all have access to compile-time effects, such as log and load effects via the language module. A compiler may support additional effects - e.g. to disable a warning, enable experimental optimizations, manage quotas - but it shouldn't be anything that significantly affects the meaning or behavior of the program. 

## Definitions

### Prog Definitions

A 'prog' definition is a program with any static arity. Prog calls are evaluated at compile-time if they have sufficient arguments to fully evaluate and do not require top-level effects. Compile-time evaluation is needed to support macros. But in most cases, calls are deferred.

Most words in a g0 dictionaries will be 'prog' words.

### Macro Definitions

A 'macro' definition is a program that will be statically evaluated in context of its caller, using static inputs based on partial evaluation. The first result from a macro must be a value representing a program. This program is applied inline just after the macro call. 

When used together with g0's ability to embed programs as data, macros effectively support higher-order programming at compile-time. List processing, parser combinators, and many other program abstractions may be supported via macros.

Macros have access to compile-time effects, and are useful when defining g0 primitives.

### Defining Primitives

The g0 language does not have any built-in definitions. Macros are sufficient to define the Glas program operators:

        macro apply [] # apply first parameter as program
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 swap 'dip put]
        prog tag [[0 swap] dip put]
        macro while-do [['while tag] dip 'do put 'loop tag]
        ...

The g0 ecosystem will usually start with a module to define the primitives and other useful functions.

### Data Definitions

Data is defined using a program of arity 0--1. This program is evaluated at compile-time, with access to compile-time effects same as macros. 

## Static Assertions

Assertions are evaluated after compilation with an empty data stack and access to compile-time effects. Assertions do not modify the dictionary and instead represent a pass/fail check. If evaluation of the assertion fails, compilation of the module will also fail. Static assertions within g0 programs are a convenient alternative to external unit tests.

## Export Function

The export function is a program of static arity 1--1, the final entry in a g0 file. If left unspecified, is equivalent to `export []`. After compiling the export program, it is evaluated with access to compile-time effects. The dictionary of definitions is input on the data stack. The value output from the export function becomes the compiled value of the g0 module.

Export functions enable g0 to define words programmatically, flexibly merge dictionaries, analyze dictionaries for errors, produce module values suitable for non-g0 import contexts, and extract or compile definitions from non-g0 module sources. However, the g0 module will not be able to directly use the words it is defining via export.

*Aside:* For export control, instead of using an export function, it will often be simpler and better to use a 'public.g0' module within a folder to explicitly whitelist the exposed symbols.

## Annotations

There is currently no dedicated syntax for annotations in g0. However, if programs construct `prog:(do:P, Annotations)` programs, e.g. by adding annotations to a block before applying it, a g0 compiler should avoid burying the annotations below other prog headers. Instead, the compiler should check recognized annotations for consistency, and add its own annotations as needed, e.g. for stable partial evaluation. 

It is also feasible to annotate programs via export function, post-hoc. However, if you're free to invasively modify code, it's usually better to annotate programs internally. 

## Stable Partial Evaluation for Macros

For simplicity of comprehension and implementation, partial evaluation in g0 should have granularity of full words. Each word is either fully evaluated or not. This is simple to comprehend and to implement. But a concern remains regarding stability. Consider the following g0 program:

        macro apply []
        prog swap ['swap apply]
        prog await2 [swap swap]
        prog foo [[swap] await2 apply]

Naively, whether this program compiles depends on whether `swap swap` is eliminated by the optimizer. However, it is not a good thing for compilation to depend on the optimizer because it raises the burden for compiling g0. 

To solve this, a g0 optimizing compiler should introduce arity annotations into defined programs based on behavior prior to optimizations. For example, the definition of 'await2' might be annotated with `arity:(i:2, o:2)`. Then, input arity is checked prior to partial evaluation of the 'await2' word. Because there are not two items on the data stack prior to calling 'await2', we'd also have zero inputs to 'apply' and thus consistently fail to compile foo regardless of optimizations.

## Compilation Strategy for Programs

The AST for g0 programs after parse is a simple list of 'call:word', 'block:G0', and 'data:Value' operators. 

To compile a program, we'll find definitions of called words, partially evaluate each word when feasible (always whole words), if not capture the current data stack and the word's definition into the program. A macro must be evaluated, or compilation fails. Static arity should be computed for each program and included in the program annotations to stabilize partial evaluation. Then, programs may freely be optimized.

A g0 compiler may use quotas to guard against non-termination. Quotas will necessarily be deterministic, e.g. based on loop counters instead of CPU time. 

## Bootstrap 

Assume an implementation of g0 is built-in to the command-line tool. This implementation is used to build module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior but may differ structurally due to optimizations or annotations. 

To resolve this, we use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are structurally equivalent, we have successfully bootstrapped g0. If not, bootstrap fails and we must debug. Essentially, we require language-g0 to reach a fixpoint almost immediately.

A remaining goal is bootstrap of the executable for the command-line tool. This requires extracting a binary executable from the Glas module system that can adequately replace the original command-line utility.

## Extension

The g0 syntax should not be extended very much because a significant goal is to preserve simplicity of bootstrap. However, if there are any features that simplify bootstrap or at least do not significantly complicate compilation, we can consider them. In many cases, it is wiser to design an alternative language module that implements the desired features.

Extensions accepted so far: data definitions, static assertions, export function.

### Variables? Rejected.

Variables are often more convenient than manual data-plumbing with swap, drop, etc.. However, full support for variables requires the ability to use variables from within loops or conditionals, mutate variables, pass by reference, etc.. Abstracting the data stack and adjusting the calling convention for functions within the language (e.g. supporting keyword parameters and results) would be a useful step. My decision is to defer this feature for a later language module.

### User-Defined Operators? Rejected.

We could implicitly rewrite '=>' to 'op-x3d-x3e' based on the ascii hexadecimal, and similar for other ops. Then we could define this word normally. However, I think ops won't fit the aesthetic of a postfix language, especially without type-driven overloads. Of course, ops should be supported by other language modules.

### Staged Macros? Tentative.

Macros currently produce a program as the first result. However, we could extend macros to permit a `macro:Program` result such that the result may be another macro call. The main use-case for this would be support for variable-arity macros; however, I'm not convinced this is a good idea or necessary. I've decided to defer this extension; if needed, adding it later won't break existing code.

### Lists of Programs? Rejected.

Idea is `{ foo, bar, baz }` syntax produces a list of program blocks, which a macro could process. This could simplify concise expression of lists or dicts, e.g. as `{1, 2, 3}` or `{'x 1, 'y 2, 'z 3}`. However, it would interact awkwardly with data abstraction and complicates construction of data, so I've decided against this feature.

### Macro State? Rejected.

A compiler could enable macros to communicate via compile-time effects, e.g. through variables. This has many potential use-cases. However, I'd prefer flexible communication models when I eventually pursue this concept - bidirectional within a file, and even communications between modules, likely based on soft constraint systems and assignment of constraint/logic variables. Essentially, the complete version of this feature should be deferred to a higher 'layer' of programming languages than g0 is intended to serve or represent.

### Assertion Parameter? Rejected.

Similar to export, we could provide the dictionary as an input to assertions. This would allow more flexible analysis, but may also complicate reasoning and refactoring insofar as our dictionary exposes symbols that will be shadowed later. I've decided to avoid this complication; assertions are still useful, and any special analysis on the whole dictionary may be deferred to export function.

### Aggregate Definitions? Rejected.

It is feasible to extend g0 with special symbols that support implicit aggregation on 'open' and 'from', preferably monotonic and idempotent such as set union or graph unification. However, this idea has the same problems as macro state, just at a different layer. Similarly, it should be left to higher layer language modules.

