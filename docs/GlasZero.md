# The g0 Language

The g0 syntax is Forth-like, optimized for simplicity and directness of compilation to Glas programs. Like many Forths, a subset of g0 words are macros, evaluated at compile-time to support metaprogramming. 

In context of Glas system, g0 is the bootstrapped language. After successfully bootstrapped, extracted binaries will depend only on state of the module system and not on versions of tools. Though, tools do affect performance.

The g0 language will be used as a foundation to define other language modules, which may trade simplicity and directness for greater readability or usability. 

## Top Level

The top-level of a g0 program consists of namespace management, i.e. imports and new definitions. Line comments are also permitted, treated as whitespace. The g0 language supports static assertions for lightweight testing.

        open foo
        from bar import baz, word as bar-word

        ; this is a line comment
        prog word1 [ Program ]
        macro word2 [ Program ]
        data word3 [ Program ]
        assert [ Program ]

Use of open can inherit definitions from another module. If used, it must be the first entry. Following optional open is an ad-hoc mix of explicit imports, definitions, and assertions. Programs can only refer to words defined previously in the file. 

When compilation succeeds, output is a dictionary of program and macro definitions, such that opening this module would logically extend the file. Defined programs and macros are statically linked, thus are independent of this dictionary. Compilation may fail due to parse errors, failure to evaluate a macro, assertion failures, or static arity checks. 

There is no direct support for export control. Indirectly, a folder may contain a 'public.g0' file that whitelists exported words.

## Programs 

Programs are expressed as a sequence of words and data between square brackets. All programs in g0 must have static arity; some, such as data, must have a specific static arity. The g0 compiler will partially evaluate programs while compiling them, and may perform other optimizations. Annotations can be supported indirectly, via compile-time effects.

## Words

A word in g0 must match regex:

        WordFragment = [a-z][a-z0-9]*
        Word = WordFragment('-'WordFragment)*

Within a program, a word's meaning depends on the definition type: prog or data is immediately linked and opportunistically partial-evaluated. A macro is evaluated using static data on the stack to produce a program value, which is then linked. 

In case of error, a warning should be logged then the word is replaced by 'fail' within the program. This does not necessarily result in failure to compile a module.

*Aside:* Shadowing of words should be discouraged. If encountered, a warning should be logged.

## Data

The g0 program syntax has built-in support for numbers, symbols, strings, and programs as values. 

        0b010111                (becomes identical bitstring)
        23                      0b10111         (a min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbid C0, DEL, and '"')
        [Program]               program 'block' on data stack; use with macros

The value `0` (or `""`) is the empty bitstring. 

Aside from program blocks, the g0 syntax does not directly support structured data. This can be mitigated by some conventions and careful definition of words. For example, multi-line text might be expressed as:

        l0 "Anatomy Class"
        li "  by Betsy Franco"
        li ""
        li "The chair has"
        li "arms."
        li "The clock,"
        li "a face."
        li "..."
        li unlines

That is, build a list of strings using some simple words, then combine that list of strings into a text. A similar construction can build dictionaries. When these techniques are too awkward or verbose, consider developing another language module.

## Assertions

Assertions provide a lightweight basis for unit tests. Assertions are evaluated with 0 inputs. If evaluation fails, compilation of the g0 module also fails, in which case loading this module will also fail. Evaluation of assertions has access to language module effects (log and load) and any compiler-provided effects.

## Prog Definitions

A 'prog' definition is a program with any static arity. Programs will be partially evaluated and optimized where feasible. Any top-level 'eff' calls are not evaluated. If a prog happens to have 0--1 arity and does not use effects, it may implicitly optimize to 'data' in the dictionary output. 

## Macro Definitions

A 'macro' is a program that is statically evaluated using partially evaluated input. The primary motive for macros is to support higher order programming and metaprogramming (within the staging limitation), e.g. so we can express list processing, parser combinators, or abstract common loops. The top stack output from a macro call should be a program, which is then applied inline. 

Conveniently, macros also eliminate the need for built-in definitions. For example, we can define all the Glas program primitive operators using macros and progs:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 swap 'dip put]
        macro while-do [[0 swap 'while put] dip 'do put 0 swap 'loop put]
        macro try-then-else ...
        ...

Macros are statically evaluated by the g0 language module's compile program and at least have access to log and load effects. The compiler may handle additional effects, e.g. to manage quotas, disable a warning, enable an experimental optimization, or apply the compiler's built-in optimizer to a program value.

Macros can improve concision for regular expressions, or support a lightweight embedded bytecode.

## Annotations

The g0 syntax does not directly support program annotations. However, macros may manually construct annotated programs or subprograms of form `prog:(do:Behavior, ...Annotations)`. If a program consists of a single 'prog' operator, the annotations should be lifted (no need to add another `prog:do` layer).

In some cases a g0 compiler will provide its own annotations, e.g. to stabilize partial evaluation or support debugging. In these cases, the compiler should detect conflict between inferred and explicit annotations. In case of contradiction, errors or warnings should be emitted to the log, and compilation may fail. This provides a lightweight basis for typechecks.

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

Without shadowing, words have at most one definition within scope of a file. This constraint simplifies reasoning and debugging.

The g0 compiler should report a shadowing error and fail if a word is called or explicitly defined before a later definition within the same file. Thus, programs may shadow implicit word definitions from 'open', but only if those definitions are not called. 

Access to words shadowed from 'open' may still be imported manually with renaming.

        open foo
        from foo import bar as foo-bar
        prog bar [wrapper around foo-bar]

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
