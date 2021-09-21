# The g0 Language

The g0 (jee zero) language is essentially a Forth with algebraic effects, staged metaprogramming, and immutable data. The syntax and semantics are very simple. This language serves as the bootstrap language for Glas systems, i.e. the language-g0 module will be written using g0 files. Upon successful bootstrap, behavior of the Glas system will depend only on state of the module system instead of the versions of external tools.

Although g0 is a good Forth, Forth is not suitable for every program nor for every programmer. Manual management of dataflow can be awkward for large functions. The g0 language is notably awkward for features that require implicit global aggregation, such as lookup tables for multi-methods, constraint variables for adaptive metaprogramming, or asynchronous programs forming an implicit global state machine. The expectation is that higher level language modules will be developed as the Glas ecosystem matures, with g0 as the foundation.

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

Within a g0 file, programs may only reference words previously defined. All words are statically linked by the compiler, producing program values that do not contain symbolic references. A successful compilation produces a dictionary of definitions (a record of form `(word1:prog:(do:Program, ...), word2:macro:Program, word3:data:Value, ...)`) using Glas programs, then optionally rewrites (or replaces) this dictionary via export function.

If 'open' appears, it must be the first entry, specifying a module from which to load the initial dictionary. By default, a g0 module starts with the empty dictionary. If 'export' appears, it must be the final entry, describing a function to rewrite (or replace) the final dictionary. By default, the dictionary is exported unmodified.

*Note:* If there are errors, compilers should generally fail compilation of the entire module after printing error messages to the log. The intention is to catch errors early and encourage developers to fix them. It is not recommended to limp along while tolerating errors in the code. 

## Programs 

        [42 [foo] bar]

Programs are expressed as a sequence of words and data between square brackets. Programs within a program are a form of data. Programs are evaluated from left to right, each operation manipulating the data stack or potentially calling external algebraic effects. Data within the program is evaluated by pushing the value onto the data stack. A useful subset of operations are macros, which are evaluated statically and must produce a program on the data stack. 

## Words

A word in g0 must match regex: 

        WordFragment = [a-z][a-z0-9]*
        Word = WordFragment('-'WordFragment)*

Within a program, a word's meaning depends on its definition type. For example, prog and macro calls are treated differently. However, words always operate on the data stack, applying from left to right.

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

## Definitions

### Prog Definitions

A 'prog' definition is a program with any static arity. Prog calls are evaluated at compile-time if they have sufficient arguments to fully evaluate and do not require top-level effects. Compile-time evaluation is needed to support macros. But in most cases, calls are deferred.

Most words in a g0 dictionaries will be 'prog' words.

### Macro Definitions

A 'macro' definition is a program that will be statically evaluated in context of its caller, using static inputs. The first two results from a macro should be a value representing a program. This program is then applied inline just after the macro call. 

When used together with g0's ability to embed programs as data, macros effectively support higher-order programming at compile-time. List processing, parser combinators, and many other program abstractions can be supported via macros, so long as the programs can be resolved at compile-time.

Macros have access to compile-time effects, and are useful when defining g0 primitives.

### Defining Primitives

Macros are sufficient to eliminate built-in definitions. For example, we can define primitive operators using macros:

        macro apply [] # apply first parameter as program
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 swap 'dip put]
        macro while-do [[0 swap 'while put] dip 'do put 0 swap 'loop put]
        ...

The g0 language does not have any built-in definitions. It relies entirely on macros for this purpose.

### Data Definitions

Data is defined using a program of arity 0--1. This program is evaluated at compile-time, with access to compile-time effects same as macros. 

## Compile-Time Effects

Macro calls, static assertions, data definitions, and the export function all have access to compile-time effects, such as log and load effects via the language module. A compiler may support additional effects - e.g. to disable a warning, enable experimental optimizations, manage quotas - but it shouldn't be anything that significantly affects the meaning or behavior of the program. 

## Static Assertions

Assertions are evaluated after compilation with an empty data stack and access to compile-time effects. Assertions do not modify the dictionary and instead represent a pass/fail check. If evaluation of the assertion fails, compilation of the module will also fail. Static assertions within g0 programs are a convenient alternative to external unit tests.

## Export Function

The export function is a program of static arity 1--1, the final entry in a g0 file. If left unspecified, is equivalent to `export []`. After compiling the export program, it is evaluated with access to compile-time effects. The dictionary of definitions as inputs on the data stack. The value output from the export function becomes the compiled value of the g0 module.

Export functions enable g0 to define words programmatically, flexibly merge dictionaries, analyze dictionaries for errors, produce module values suitable for non-g0 import contexts, and extract or compile definitions from non-g0 sources. However, programmatic definitions won't be visible within the g0 module that constructs them, only to later modules that import these definitions.

*Aside:* It is not recommended to use export functions for export control. Instead, use a 'public.g0' file to manually import the exposed symbols.

## Annotations

There is currently no dedicated syntax for annotations in g0. However, if programs construct `prog:(do:P, Annotations)` programs, e.g. by adding annotations to a block before applying it, a g0 compiler should avoid burying the annotations below other prog headers. Instead, the compiler should check recognized annotations for consistency, and add its own annotations as needed, e.g. for stable partial evaluation. 

It is feasible to annotate programs after defining them via export function, e.g. based on searching for data definitions identified by `anno-word`. However, this is awkward and isn't recommended for the normal use case.

*Aside:* Use of compile-time effects or logging to specify annotations was rejected. Use of macros to wrap a block 

## Stable Partial Evaluation for Macros

For simplicity of comprehension and implementation, partial evaluation in g0 should have granularity of full words. Each word is either fully evaluated or not. This is simple to comprehend and to implement. But a concern remains regarding stability. Consider the following g0 program:

        macro apply []
        prog swap ['swap apply]
        prog await2 [swap swap]
        prog foo [[swap] await2 apply]

Naively, whether this program compiles depends on whether `swap swap` is eliminated by the optimizer. However, it is not a good thing for compilation to depend on the optimizer because it raises the burden for compiling g0. 

To solve this, a g0 optimizing compiler can introduce arity annotations into defined programs based on behavior prior to optimization. For example, the definition of 'await2' should be annotated with `arity:(i:2, o:2)`. Then, the input arity is checked prior to partial evaluation of the 'await2' word. Because there are not two items on the data stack prior to calling 'await2', we'd also have zero inputs to 'apply' and thus fail to compile foo. Programmers will be alerted to fix the problem.

*Aside:* Arity annotations are a convenient solution. In addition to providing stability, this reduces need to memoize computations during early development. There is a risk of users lying to the compiler, but this will eventually be caught.

## No Shadowing

A g0 compiler should report an error and fail if a word is called or explicitly defined before a later definition within the same file. This prevents observable shadowing; words from 'open' may be shadowed, but only if they are not used. This constraint simplifies reasoning and debugging because each word will have only one observable meaning within scope of a g0 file.

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
