# The g0 Syntax

The g0 syntax is a lightweight layer above the Glas program model, similar to a Forth, and is intended to serve a bootstrap role in the Glas system. Associated file extension is `.g0`. 

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

Programs are expressed as a sequence of words and embedded data between square brackets, generally separated by spaces (though square brackets are also okay separators). All programs in g0 must have static arity; some, such as data, must have a specific static arity.

The g0 compiler will opportunistically partially evaluate programs while compiling them. This includes embedded program blocks. However, after compilation of individual programs, the g0 compiler will not further optimize them.

### Words

A word in g0 must match regex:

        WordFragment = [a-z][a-z0-9]*
        Word = WordFragment('-'WordFragment)*

Within a program, a word's meaning depends on the definition type: prog or data is immediately linked and opportunistically partial-evaluated. A macro is evaluated using static data on the stack to produce a program value, which is then linked. 

In case of error, a warning should be logged then the word is replaced by 'fail' within the program. This does not necessarily result in failure to compile a module.

*Aside:* Shadowing of words should be discouraged. If encountered, a warning should be logged.

### Embedded Data

The g0 program syntax has built-in support for numbers, symbols, strings, and programs as values. 

        0b010111                (becomes identical bitstring)
        23                      0b10111         (a min-width representation)
        23u7                    0b0010111       (numbers of specified width)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbid C0, DEL, and '"')
        [Program]               program on the data stack, for metaprogramming

The value `0` or `0u0` is the empty bitstring, which also corresponds to the empty list, empty record, and 'unit' value for Glas programs. 

The g0 syntax doesn't support structured data or string escapes, but it is possible to write programs that compose or manipulate data, and a compiler may optimize by static partial evaluation.

## Assertions

Assertions provide a lightweight basis for unit tests. Assertions are evaluated with 0 inputs. If evaluation fails, the assertion fails and the entire g0 module is considered to fail compilation (i.e. loading the module will fail). Evaluation of assertions has access to language module effects (log and load), so it's feasible for internal 'log' messages to clarify issues.

## Prog Definitions

A 'prog' definition is a program with any static arity. Programs will be partially evaluated and optimized where feasible. Any top-level 'eff' calls are not evaluated. If a prog happens to have 0--1 arity and does not use effects, it may implicitly optimize to 'data' in the dictionary output. 

## Data Definitions

A 'data' definition is specified by a program with 0--1 arity. This program is evaluated with access to language-module log and load effects.

## Macro Definitions

A 'macro' entry is bound to a program that is evaluated statically using partially evaluated inputs from the caller. The top stack output from a macro should be a program, which is then inlined into the caller. A macro may have more than one stack output, but only the top item is treated specially. 

The purpose of macros is to provide a basis for static higher order programming, e.g. to support list processing and parser combinators. They can support metaprogramming in general. Macros also provide a simple basis to avoid built-in definitions. For example, we can define the Glas program primitive operators using a few macros and progs:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 swap 'dip put]
        macro while-do [[0 swap 'while put] dip 'do put 0 swap 'loop put]
        macro try-then-else ...
        ...

If the g0 compiler fails to statically compute parameters to a macro, or if macro evaluation fails, these are logged as errors in the g0 module. Usefully, programmers can use macros to make explicit some assumptions about partial evaluation:

        ; A -- [A]
        prog quote [0 swap 'data put]

        ; [P1] [P2] -- [P1 P2]
        prog compose [0 swap pushl swap pushl 0 swap 'seq put]

        ; A [B] -- [A B]
        prog curry [[quote] dip compose]

        macro static-data [[] curry]
        macro static-data2 [[] curry curry]
        macro static-data3 [[] curry curry curry] 
        ...

Macros also have access to language-module log and load effects at compile-time. For example, we could support static logging of messages and loading of arbitrary module values.

        macro static-load [0 swap 'load put eff quote]
        macro static-error ...
        macro static-warn ...
        macro static-info ...

These features make g0 much more useful for static debugging. It is also feasible to support static typechecks and user-defined optimizations as macros.

The intended use-case for macros is static higher-order programming to support list processing and parser combinators.

*Aside:* Feature interaction between macros and local variables is awkward and complicated. I'm dropping local vars in favor of macros. 

## Local Variables

Local variables within a program are sometimes more convenient than manual manipulation of the stack using dip, drop, copy, swap. This is especially true for larger, more complicated programs, or if writing a closure that deeply embeds a variable. Local variables can support lambda-like expressions as `[\x y . E]` or let-like expressions as `[Expr1 \x . Expr2]`, with 'x' capturing the intermediate result from Expr1. This can be implemented by rewriting the subprogram that introduces the variable.

    \x y z. E => \z. \y. \x. E
    \w. E | E introduces no vars => T(w,E)
    T(w,E) | E does not contain w => drop E
    T(w,w) =>
    T(w,[E]) => [T(w,E)] curry
    T(w,F G)
        | only F contains w => T(w,F) G
        | only G contains w => [F] dip T(w,G)
        | otherwise => copy [T(w,F)] dip T(w,G)

In context of macros, we'll also need a compilation strategy such that static 'curry' or 'dip' in the parent program can be performed *before* we attempt to evaluate macros within a subprogram. 

## No Keywords!

The g0 language has no keywords. As described earlier, macros fulfill this role. 

## Bootstrap 

As a special case, the language-g0 module will go through a bootstrap process in Glas. The command-line utility provides a naive implementation for a sufficient subset of g0. This is used to build the module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior but will often be structurally different due to differences in optimizations or annotations. To resolve this, we use P1 to rebuild language-g0, producing `(compile:P2, ...)` then verify P1 and P2 are structurally equivalent.

This bootstrap process requires that module language-g0 is carefully defined to achieve fixpoint in just one cycle, verified by the second compilation. If P1 and P2 are structurally distinct, bootstrap fails and we should debug our definitions.

After bootstrap of g0, the next major step is to bootstrap the command-line application, then eventually support compilation of the app to a binary executable that we extract, divorcing from the initial implementation. However, these steps are outside the scope of this document.

## Extensions

The g0 syntax should not be extended much because doing so will hinder bootstrap implementations. It's already pushing the limits for complexity by requiring compile-time evaluation. Of course, if there are any features that greatly simplify bootstrap and do not overly complicate the model, we can consider them. Most extensions should be deferred to development of new language modules after bootstrap.
