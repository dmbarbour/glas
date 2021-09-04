# The g0 Syntax

The g0 syntax is a lightweight layer above the Glas program model, similar to a Forth or Scheme, and is intended to serve a bootstrap role in the Glas system. Associated file extension is `.g0`. 

The g0 syntax has limited support for staged metaprogramming, local variables for convenient data plumbing, and assumptions/assertions as a simple basis for tests and types. However, the intention is that g0 is a transitory syntax and that users will develop more sophisticated language modules after bootstrap.

## Top Level

The top-level of a g0 program consists of namespace management, i.e. imports and new definitions. Line comments are also permitted, treated as whitespace within the g0 text. The g0 language also supports static assertions for lightweight testing.

        open foo
        from bar import baz, word as bar-word

        ; this is a line comment
        prog word1 [ Program ]
        macro word2 [ Program ]
        data word3 [ Program ]
        assert [ Program ]

A single 'open' is permitted to inherit a dictionary from a prior module. If open is used, it must be the first entry. This may be followed by explicit imports, definitions, and assertions. Definitions may only use words defined previously. Recursion is not permitted.

Shadowing of definitions, i.e. using any word that is defined later within the same file, is permitted but discouraged. The compiler should log a warning where encountered. It is more convenient for comprehension when words have consistent and stable definitions.

The final output is a record of program, macro, and data definitions representing the dictionary visible at the bottom of the g0 file. Definitions are statically linked, thus do not depend on the dictionary. The above example might reduce to `(word1:prog:Program, word2:macro:Program, word3:data:ComputedValue, foo-x:(...), baz:(...), bar-word:(...))`. Assertions are not included in the dictionary, but may cause compilation of the module to fail.

There is no direct support for export control. Indirectly, a folder may contain a 'public.g0' file that whitelists exported words.

## Programs 

Programs are expressed as a sequence of words and data between square brackets. All programs in g0 must have static arity; some, such as data, must have a specific static arity.

The g0 compiler will opportunistically partially evaluate programs while compiling them. This includes embedded program blocks. However, after compilation of individual programs, the g0 compiler will not further optimize them.

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
        23u7                    0b0010111       (numbers of specified width)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbid C0, DEL, and '"')
        [Program]               program 'block' on data stack; use with macros

The value `0` (or `0u0` or `""`) is the empty bitstring. 

Aside from program blocks, the g0 syntax does not directly support structured data. Programmers should define words as needed for comfortable construction and composition of complex values. For example, multi-line text might be expressed as a list that we concatenate, and large lists could be supported via a few words:

        list-begin
        li "Anatomy Class"
        li "  by Betsy Franco"
        li ""
        li "The chair has"
        li "arms."
        li "The clock,"
        li "a face."
        li "..."
        list-end unlines

Use of list-begin etc. together with a dict-begin could cover most variable-size embedded data in g0. For a short list of static size where values fit neatly on one line, we might favor `1 2 3 list3`. Of course, if a data structure is verbose or awkward to express in g0, programmers should consider developing a language module that makes expression more concise and convenient.

## Assertions

Assertions provide a lightweight basis for unit tests. Assertions are evaluated with 0 inputs. If evaluation fails, the assertion fails and the entire g0 module is considered to fail compilation (i.e. loading the module will fail). Evaluation of assertions has access to language module effects (log and load), plus any compiler-provided effects (e.g. quota control)

## Prog Definitions

A 'prog' definition is a program with any static arity. Programs will be partially evaluated and optimized where feasible. Any top-level 'eff' calls are not evaluated. If a prog happens to have 0--1 arity and does not use effects, it may implicitly optimize to 'data' in the dictionary output. 

## Data Definitions

A 'data' definition is specified by a program with 0--1 static arity. This program is evaluated statically, similar to a macro or assertion. The computed value becomes the defined data.

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

Macros are statically evaluated by the g0 language module's compile program and at least have access to log and load effects. The compiler may handle additional effects internally, e.g. to manage quotas, disable a warning, enable an experimental optimization, or even to apply a compiler's internal optimizer to an arbitrary program value.

## Local Variables

Local variables in a program are often more convenient than manual stack manipulation using dip, drop, copy, swap. This is especially true for larger programs. Local variables support lambda-like expressions as `[\x y. E]` and let-like expressions as `[E1 \x. E2]`. Local variables can be implemented by rewriting the subprogram that introduces the variable.

    \x y z. E => \z. \y. \x. E
    \w. E | E introduces no vars => T(w,E)
    T(w,E) | E does not contain w => drop E
    T(w,w) =>
    T(w,[E]) => [T(w,E)] curry
    T(w,F G)
        | only F contains w => T(w,F) G
        | only G contains w => [F] dip T(w,G)
        | otherwise => copy [T(w,F)] dip T(w,G)
    where 
        copy : A -- A A
        drop : A --
        dip : A [F] -- F A
        curry : A [F] -- [A F]

Local variables complicate compilation in context of macros where we must specially handle static variables. Refactoring is hindered by entanglement between macros and static variables.

## Compilation Strategy for Programs

The intial AST for g0 programs is a simple list of 'var:word', 'call:word', 'block:G0', and 'data:Value' operators. 

The first pass is to eliminate variables. This pass runs right-to-left and applies hierarchically to blocks. It eliminates 'var' elements from the AST, but introduces 'drop', 'dip', 'curry', and 'copy' operators.

The second pass performs linking and partial evaluation. This pass runs left-to-right. Each word is linked and applied to partial data. Failed application of any macro call will cause module compilation to fail. To solve interaction of local vars and macros, this pass must specially rewrite `V [F] curry => [V F]` or `V [F] dip => F V` prior to linking and partially evaluating `F`. 

After the second pass, we have a Glas program. We can immediately apply the Glas program optimizer to ensure block program values are optimized by default. Of course, programmers may manually optimize blocks via developing an optimizer function, or a compiler could expose its own optimizer function via macro effect (e.g. **optimize:Program**). Manual optimization is useful when we start manually composing program values.

*Note:* A g0 compiler is free to impose quotas for how much static evaluation is performed, e.g. we might limit the number of times a loop is processed during partial evaluation. Quotas could be controlled via compile-time macro effects to explicitly increase the quota where needed.

## Bootstrap 

Unlike most language modules, g0 is bootstrapped. All other language modules can use g0 as a foundation. 

An initial implementation of g0 provided by the command-line utility. This is then used to build the module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior but will often be structurally different due to differences in optimizations or annotations. To resolve this, we use P1 to rebuild language-g0, producing `(compile:P2, ...)` then verify P1 and P2 are structurally equivalent.

This bootstrap process requires that module language-g0 is carefully defined to achieve fixpoint in just one cycle, verified by the second compilation. If P1 and P2 are structurally distinct, bootstrap fails and we should debug our definitions.

After bootstrap of g0, the next major step is to bootstrap the command-line application, then eventually support compilation of the app to a binary executable that we extract, divorcing from the initial implementation. However, these steps are outside the scope of this document.

## Extensions

The g0 syntax should not be extended much because doing so will hinder bootstrap implementations. It's already pushing the limits for complexity by requiring compile-time evaluation. Of course, if there are any features that greatly simplify bootstrap and do not overly complicate the model, we can consider them. Most extensions should be deferred to development of new language modules after bootstrap.
