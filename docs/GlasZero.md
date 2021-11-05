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
        macro dip [0 'dip put]
        macro while-do [0 'do put 'while put 0 'loop put]
        ...

The g0 ecosystem will usually start with a module to define the primitives and other useful functions.

### Data Definitions

Data is defined using a program of arity 0--1. After macro evaluation, this program is evaluated at compile-time with access to compile-time effects. The generated value on the data stack becomes a `data:Value` definition in the dictionary. Failure to evaluate a data definition causes the whole module to fail.

## Static Assertions

Assertions must be 0--Any static arity programs, favoring 0--0. After macro evaluation, assertions are evaluated with an empty data stack and access to compile-time effects. If 0--N arity where N is non-zero, an info message is implicitly written to the log describing the final data stack. On failure, an error is emitted and the entire module will fail to evaluate.

Static assertions within g0 programs are a convenient basis for lightweight unit tests. They will run every time the program is built, but memoization can  

## Export Function

The export function is a program of static arity 1--1, the final entry in a g0 file. If left unspecified, is equivalent to `export []`. After compiling the export program, it is evaluated with access to compile-time effects. The dictionary of definitions is input on the data stack. The value output from the export function becomes the compiled value of the g0 module.

Export functions enable g0 to define words programmatically, flexibly merge dictionaries, analyze dictionaries for errors, produce module values suitable for non-g0 import contexts, and extract or compile definitions from non-g0 module sources. However, the g0 module will not be able to directly use the words it is defining via export.

*Aside:* For export control, instead of using an export function, it will often be simpler and better to use a 'public.g0' module within a folder to explicitly whitelist the exposed symbols.

## Annotations

There is currently no dedicated syntax for annotations in g0. However, if programs construct `prog:(do:P, Annotations)` programs, e.g. by adding annotations to a block before applying it, a g0 compiler should avoid burying the annotations below other prog headers. Instead, the compiler should check recognized annotations for consistency, and add its own annotations as needed, e.g. for stable partial evaluation. 

It is also feasible to annotate programs via export function, post-hoc. However, if you're free to invasively modify code, it's usually better to annotate programs internally. 

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

Extensions accepted so far: data definitions, static assertions, export function.

### Variables? Rejected.

Variables are often more convenient than manual data-plumbing with swap, drop, etc.. However, full support for variables (mutation, pass by reference, etc.) is more sophisticated than I'd prefer to support in the g0 compiler. This is left to a future post-g0 language. 

### User-Defined Operators? Rejected.

Without variables or support for overloading, use of operators is relatively awkward. So, this should be added to a post-g0 language.

### Multi-Stage Macros? Tentative.

Macros currently must produce a `data:Program` value at the top of the stack. We could extend this to permit `data:macro:Program`, further evaluating the result as a macro. This is more expressive than single-stage macros, but also more difficult to reason about. I don't see a strong use-case at this time. I've decided to defer this extension until a strong use-case is discovered.

### Lists of Programs? Rejected.

Idea is `{ foo, bar, baz }` syntax produces a list of program blocks, which a macro could process. This could simplify concise expression of lists or dicts, e.g. as `{1, 2, 3}` or `{'x 1, 'y 2, 'z 3}`. However, it would interact awkwardly with data abstraction and complicates construction of data, so I've decided against this feature.

### Macro State? Rejected.

A compiler could enable macros to communicate via compile-time effects, e.g. through variables. This has many potential use-cases. However, I'd prefer to avoid coupling the idea of compile-time communication to module boundaries. Later, we might create modules based on soft constraint systems and assignment of constraint/logic variables, where even modules represent partial constraints and solutions. 

### Assertion Parameter? Rejected.

Similar to export, we could provide the dictionary as an input to assertions. This would allow more flexible analysis, but may also reasoning and refactoring insofar as our dictionary might expose symbols that will be shadowed later. I've decided to avoid this complication; assertions are still useful as is, but any special analysis on the whole dictionary may be deferred to export function.

### Aggregate Definitions? Rejected.

It is feasible to extend g0 with special symbols that support implicit aggregation on 'open' and 'from', preferably monotonic and idempotent such as set union or graph unification. However, this idea has the same problems as macro state, just at a different layer. It should probably be solved at the macro state layer, too.
