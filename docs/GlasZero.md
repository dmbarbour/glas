# The g0 Syntax

The g0 syntax looks and feels similar to a Forth, and is intended to serve a bootstrap role in the Glas system. Associated file extension is `.g0`. 

The g0 syntax is limited in what it can express: it does not support metaprogramming, automatic data plumbing, recursive function groups, extensibility, type annotations, export control, and many other convenient features. The intention is that g0 is transitory; more sophisticated language modules will be developed beyond bootstrap.

## Top Level

The top-level of a g0 program consists of namespace management, i.e. imports and new definitions, plus ad-hoc  line comments.

        open foo
        from bar import baz, word as bar-word

        ; this is a line comment
        prog word [ Program ]

A single 'open' is permitted to inherit words defined by a prior module. This may be followed by multiple imports, then by multiple definitions. Shadowing any explicitly imported or defined word will result in a warning.

The g0 namespace is flat, i.e. there is no support in g0 for qualified imports or dotted paths into a hierarchical namespace. Also, module names and words used within g0 are restricted by syntax: if a file or function has an awkward name, a g0 program cannot even refer to it. This is a non-issue for bootstrap because we control the dependencies.

## Compiled Output

The output for a g0 program is a dictionary of all defined words, including imported words. Most definitions will have `prog:(do:Program,...)` format, likely with no annotations. An optimizer might reduce some programs to `prog:do:data:Value` and rewrite to `data:Value`. Additionally, definitions of form `prog:do:fail` (after optimization) are equivalent to undefined words in g0, and may implicitly be erased from the output dictionary.

The g0 compiler may optionally optimize or annotate programs. This may introduce some variability in structure with the bootstrap implementation, but it shouldn't affect program behavior.

## Bootstrap 

As a special case, the language-g0 module will go through a bootstrap process in Glas. The command-line utility provides a naive implementation for a sufficient subset of g0. This is used to build the module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior, but they may be structurally different due to differences in optimizations and annotations. To resolve this, we use P1 to rebuild language-g0, producing `(compile:P2, ...)` then verify P1 and P2 are structurally equivalent.

This bootstrap process requires that module language-g0 is carefully defined to achieve fixpoint after two cycles, verified by the third cycle. If P1 and P2 are structurally distinct, bootstrap fails and we should debug our definitions.

After bootstrap of g0, the next steps are to bootstrap the command-line application, then eventually support compilation of the app to a binary executable that we extract, fully divorcing from the initial implementation. However, these steps are outside the scope of this document.

*Aside:* To confine bootstrap, it is preferable that the language-g0 module is isolated to a folder with no external module dependencies. 

## Embedded Data

The g0 program syntax has built-in support for numbers, symbols, and strings. 

        0b010111                (becomes identical bitstring)
        23                      0b10111         (the min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbids C0, DEL, and '"')

The g0 syntax doesn't support structured data or string escapes, but it is possible to write programs that compose or manipulate data, and a compiler may optimize by static partial evaluation.

## Words

A word in g0 currently must match regex `[a-z][a-z0-9]*('-'[a-z][a-z0-9]*)*`. 

Within a program, a word is immediately compiled by replacing it with the definition. This results in a form of static scoping and linking. If there is no definition for a word, a warning should be logged and the word is replaced by 'fail' operator.

## Keywords

The g0 syntax has keywords for every basic operator in Glas programs (e.g. swap, copy) and also for namespace ops (import, open, from, as) and structural keywords (dip, while, do, try, then, else, with). Attempting to define keywords will result in compilation failure. Implicit imports of keywords via 'open' modules will be dropped with a warning.

For the basic symbolic operators like 'swap' or 'add', keywords compile to the program operator of the same name. Other keywords are simply left undefined. Program structure uses a few specialized patterns:

        dip [ Program ]                                         (dip)
        while [ Program ] do [ Program ]                        (loop)
        try [ Program ] then [ Program ] else [ Program ]       (cond)
        with [ Program ] do [ Program ]                         (env)

### Binary Extraction

The g0 syntax defines programs, and only defines data as an optional optimization. Binaries intended for extraction must be represented as streaming binaries - programs that write binary fragments.

## Extensions

The g0 syntax should not be extended with anything that complicates the parser or compilation. The idea is to instead escape g0 by implementing language modules.

I would like to develop more languages in Glas with a Forth-like syntax, albeit with much better support for staged higher order programmng, metaprogramming, type annotations, local variables, etc.. But we can also support lambda calculus, Kahn Process Networks, and many other program models.

