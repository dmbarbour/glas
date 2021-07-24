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

The output for a g0 program is a record of all defined words. There is no export control. Compiled program definitions in g0 will always have a 'prog' header, e.g. `prog:(do:CompiledProgram)`. This enables extension of namespaces to define data, types, macros, and other objects. Although g0 does not need this extension, it will simplify integration with other language modules.

## Bootstrap 

As a special case, the language-g0 module will go through a bootstrap process in Glas. The command-line utility provides a naive implementation for a sufficient subset of g0. This is used to build the module language-g0 to a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same behavior, but they may be structurally different due to differences in optimizations, annotations, etc.. To resolve this, we use P1 to rebuild language-g0, producing `(compile:P2, ...)` then verify P1 and P2 are structurally equivalent.

If bootstrap fails, we must debug both module language-g0 and the command-line tool. This bootstrap process requires that module language-g0 is carefully defined to achieve fixpoint after a single cycle. To simplify linking during bootstrap, the language-g0 module should be isolated to a folder with no outside module dependencies.

The next step is to bootstrap the command-line utility. This requires modeling the application and compiler to the target architecture, then extracting the binary to an executable file. This task is outside the scope of this document, but when completed allows us to escape the initial Glas command-line utility.

## Embedded Data

The g0 program syntax has built-in support for numbers, symbols, and strings.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (the min-width representation)
        0x17                    0b00010111      (always multiple of four bits)
        'word                   0x776f726400    (symbols for every valid word)
        "hello"                 (list of ascii bytes; forbids C0, DEL, and '"')

It is feasible to develop a series of words such that `23 u8` is equivalent to `0x17`. The g0 language compiler may evaluate such data statically. Strings do not have escape characters but can similarly be post-processed by a subsequent word. There is currently no support for embedded records or lists, but those may be constructed programmatically via put and pushr.

## Words

A word in g0 currently must match regex `[a-z]+('-'[a-z]+)*(0-9)*`.

Within a program, a word is immediately compiled by replacing it with the definition. This results in a form of static scoping and linking. If there is no definition for a word, a warning should be logged and the word is replaced by 'fail' operator.

## Keywords

The g0 syntax has keywords for every symbol in Glas programs (e.g. swap, dip, do, loop, seq), plus a few for namespace management (e.g. import, open, from, as). Explicitly attempting to define keywords will result in a parse failure. If an 'open' module's record would define keywords, a warning should be logged that those definitions are ignored.

For the basic symbolic operators like 'swap' or 'add', keywords compile to the program operator of the same name. Program structure uses a few specialized patterns:

        dip [ Program ]                                         (dip)
        while [ Program ] do [ Program ]                        (loop)
        try [ Program ] then [ Program ] else [ Program ]       (cond)
        with [ Program ] do [ Program ]                         (env)

## Extensions

A bootstrap syntax should not be casually extended with new features because doing so will inevitably leak into the bootstrap program, which complicates a fresh bootstrap from other languages. However, it is feasible to develop new language modules that preserve a similar Forth-like style while adding a variety of features suitable for staged computing, metaprogramming, static analysis, acceleration, etc..
