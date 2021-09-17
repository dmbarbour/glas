# The g0 Language

The g0 syntax is Forth-like, optimized for simplicity and directness of compilation to Glas programs. Like many Forths, a subset of g0 words are macros, evaluated at compile-time to support metaprogramming. 

In context of Glas system, g0 is the bootstrapped language. After successfully bootstrapped, extracted binaries will depend only on state of the module system and not on versions of tools. Though, tools do affect performance.

The g0 language will be used as a foundation to define other language modules, which may trade simplicity and directness for greater readability or usability. 

## Top Level

The top-level of a g0 file consists of namespace management, i.e. imports and new definitions. Line comments are also permitted, treated as whitespace. The g0 language supports static assertions for lightweight testing.

        open foo
        from bar import qux, baz as bar-baz

        ; this is a line comment
        prog word1 [ Program ]
        macro word2 [ Program ]
        data word3 [ Program ]
        assert [ Program ]

        export [ Program ]

The g0 programs may only reference words previously defined in the file. Definitions are statically linked and compiled into Glas programs, thus there are no symbolic references after compilation. A successful compilation produces a dictionary of definitions (a record of form `(word1:prog:(do:Program, ...), word2:macro:Program, ...)`) then rewrites it via export function.

If 'open' appears, it must be the first entry, specifying a module for the initial dictionary. By default, g0 starts with the empty dictionary. If 'export' appears, it must be the final entry, describing an arbitrary rewrite on the generated dictionary. By default, we export the full dictionary unmodified.

*Note:* If there are errors, compilers should generally fail compilation of the entire module after printing suitable error messages to the log. The intention is to catch errors early and encourage developers to fix them. 

## Metaprogramming

In g0, assert, data, export, and called macros are evaluated statically and have access to compile-time effects including 'log' and 'load'. Of these, export and macros support metaprogramming. Macros support metaprogramming at the call site within other programs. Export supports metaprogramming of the top-level by programmatically rewriting the dictionary.

Usefully, 'export' allows g0 modules to masquerade as other language module types by producing suitable values, though it might be more awkward.

Macros support metaprogramming within a program, and export supports metaprogramming of the top-level, freely rewriting the produced dictionary into the module's compiled value. 

Assertions are mostly for 

## Programs 

        [42 foo bar]

Programs are expressed as a sequence of words and data between square brackets. Programs are evaluated from left to right, each operation manipulating the data stack or potentially calling external algebraic effects. Data within the program is evaluated by pushing the value onto the data stack. 

A useful subset of operations are macros, which evaluate statically with access to compiler-provided effects (such as logging messages and loading modules, via language module effects API). Macros often take program data as static input, and this is supported by the data embedding. For example:

        [[op1] [op2] [op3] try-then-else]

In this case, try-then-else should be a macro that constructs a program given values representing the condition and branch behaviors. Macro calls are not distinguished syntactically, but rather at point of definition. Programs are partially evaluated to support the macro calls.

A g0 compiler may perform various static analyses on programs, including arity and type checks. It's generally acceptable to have a more restrictive compiler.

## Words

A word in g0 must match regex:

        WordFragment = [a-z][a-z0-9]*
        Word = WordFragment('-'WordFragment)*

Within a program, a word's meaning depends on the definition type: prog or data is immediately linked and opportunistically partial-evaluated. A macro is evaluated using static data on the stack to produce a program value, which is then linked. 

In case of error, a warning should be logged then the word is replaced by 'fail' within the program. This does not necessarily result in failure to compile a module.

*Aside:* Shadowing of words should be discouraged. If encountered, a warning should be logged.

## Data

The g0 program syntax has built-in support for numbers, symbols, strings, and programs.  

        0b010111                (becomes identical bitstring)
        23                      0b10111         (a min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbid C0, DEL, and '"')
        [Program]               program 'block' on data stack; use with macros

The value `0` (or `""`) is the empty bitstring. 

The g0 syntax does not directly support structured data (other than programs). However, it is feasible to leverage partial evaluation, macros, strings, and some concise words to serve the role. For example, multi-line text might be expressed as:

        l0 "Anatomy Class"
        li "  by Betsy Franco"
        li ""
        li "The chair has"
        li "arms."
        li "The clock,"
        li "a face."
        li "..."
        li unlines

This is acceptable, barely. However, rather than struggle with awkward data embeddings, programmers should develop suitable language modules for any data that needs to be embedded in a project.

## Prog Definitions

        prog word [ Program ]

A 'prog' definition is a program with any static arity. Prog calls are evaluated at compile-time if they have sufficient arguments to fully evaluate and do not require top-level effects. Compile-time evaluation is needed to support macros. But in most cases, calls are deferred.

Most words in a g0 dictionaries will be 'prog' words.

## Macro Definitions

        macro word [ Program ]

A 'macro' definition is a program that will be statically evaluated in context of its caller, using static inputs. The first result from the macro must be a program. This program is inlined by the caller, effectively becoming a 'prog' call for purpose of further compile-time evaluation. It is an error if a macro call cannot be evaluated at compile-time.

Macros support staged metaprogramming. To support staged higher-order programming, the g0 syntax supports embedding programs as first-class values. List processing, parser combinators, and many other program abstractions can be supported via macros. However, unlike conventional functional PLs, higher order programming in g0 is static and does not support closures (modulo providing your own eval implementation).

Macros additionally eliminate the need for built-in definitions. For example, we can define primitive operators using macros:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 swap 'dip put]
        macro while-do [[0 swap 'while put] dip 'do put 0 swap 'loop put]
        macro try-then-else ...
        ...

Macros also have access to compile-time effects.

## Compile-Time Effects

Macro calls, static assertions, data definitions, and the export function all have access to compile-time effects, such as log and load effects via the language module. A compiler may support additional effects - e.g. to disable a warning, enable experimental optimizations, manage quotas - but it shouldn't be anything that significantly affects the meaning or behavior of the program. 

## Data Definitions

        data word [ Program ]

A 'data' definition is a program of static arity 0--1. After compiling this program normally (e.g. evaluating the macros), the program is evaluated into data. Evaluation of this program has access to compile-time effects. 

## Static Assertions

        assert [ Program ]

Assertions are programs of static arity 0--Any. After compiling this program normally, it is evaluated with access to compile-time effects. In this case, the main result is pass or fail; if evaluation of the assertion fails, that is a compile-time error. 

Static assertions within g0 programs are a convenient alternative to unit tests in many cases. 

## Export Function

        export [ Program ]

The export function is a program of static arity 1--1. After compiling this program normally, it will be evaluated with access to compile-time effects and the module's dictionary of definitions on the data stack. The output from the export function, which may be a value of any type, becomes the module's compiled value. The export function supports metaprogramming at the definitions layer, and enables computation of non-dictionary values for use outside the g0 context.

*Note:* Although the export function can be used for export control, a more convenient alternative is to leverage a 'public.g0' file within the folder to import only the subset of words that should be visible to clients of the module.

## Annotations

There is no dedicated syntax for annotations in g0. However, if macros construct `prog:(do:P, Annotations)` programs, a g0 compiler should avoid burying it below another prog header. Instead, the compiler should check annotations for consistency and add its own annotations as needed, e.g. for stable partial evaluation. 

It is also feasible to annotate programs after defining them via export function. However, this is quite awkward, so isn't recommended unless you intend to non-invasively add annotations to imported definitions.

## Stable Partial Evaluation for Macros

For simplicity of comprehension and implementation, partial evaluation in g0 should have granularity of full words. Each word is either fully evaluated or not. This is simple to comprehend and to implement. But a concern remains regarding stability. Consider the following g0 program:

        macro apply []
        prog swap ['swap apply]
        prog await2 [swap swap]
        prog foo [[swap] await2 apply]

Naively, whether this program compiles depends on whether `swap swap` is eliminated by the optimizer. However, it is not a good thing for compilation to depend on the optimizer because it raises the burden for compiling g0. 

To solve this, a g0 optimizing compiler can introduce arity annotations into defined programs based on behavior prior to optimization. For example, the definition of 'await2' should be annotated with `arity:(i:2, o:2)`. Then, the input arity is checked prior to partial evaluation of the 'await2' word. Because there are not two items on the data stack prior to calling 'await2', we'd also have zero inputs to 'apply' and thus fail to compile foo. Programmers will be alerted to fix the problem.

*Aside:* Arity annotations are a convenient solution. In addition to providing stability, this reduces need to memoize computations during early development. There is a risk of users lying to the compiler, but this will eventually be caught.

## Detect and Forbid Shadowing

Without shadowing, words have at most one definition within scope of a file. This constraint simplifies reasoning, debugging, and compilation.

By default, a g0 compiler should report shadowing as an error and fail if a word is called or explicitly defined before a later definition within the same file. Thus, programs may shadow implicit word definitions from 'open', but only if the old definition is not used within the file. 

## Compilation Strategy for Programs

The AST for g0 programs after parse is a simple list of 'call:word', 'block:G0', and 'data:Value' operators. 

To compile a program, we'll find definitions of called words, partially evaluate each word when feasible (always whole words), if not capture the current data stack and the word's definition into the program. A macro must be evaluated, or compilation fails. Static arity should be computed for each program and included in the program annotations to stabilize partial evaluation. Then, programs may freely be optimized.

A g0 compiler may use quotas to guard against non-termination. Quotas will necessarily be deterministic, e.g. based on loop counters instead of CPU time. 

## Bootstrap 

Assume an implementation of g0 is built-in to the command-line utility. This implementation is used to build module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior but may differ structurally due to optimizations or annotations. To resolve this, we use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. 

If P1 and P2 are structurally equal, we have successfully bootstrapped g0. If not, bootstrap has failed and we must debug the built-in definition and language-g0 module. Essentially, we require language-g0 to reach a fixpoint almost immediately.

A remaining goal is bootstrap of the command-line utility. This requires extracting a binary executable from the Glas module system that can replace the original command-line utility.

## Extensions

The g0 syntax should not be extended much because doing so will hinder bootstrap implementations. It's already pushing the limits for complexity with compile-time evaluation. Of course, if there are any features that greatly simplify bootstrap and do not overly complicate the model, we can consider them. Most extensions should be deferred to development of new language modules after bootstrap.

### Variables? Rejected!

Variables are often more convenient than manual data-plumbing with swap, drop, etc.. However, full support for variables (e.g. controlling variable capture within macros, mutation of variables within a loop, pass-by-reference) is somewhat more complicated than I'd prefer to address prior to bootstrap of g0. Future language modules may support variables, but g0 will not.
