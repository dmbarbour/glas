# Language g0

The g0 language is essentially a Forth-inspired macro-assembly for the glas program model, with relatively direct compilation. The glas program model is oriented around immutable data and algebraic effects; g0 adds modularity and metaprogramming. 

The g0 notation has many known weaknesses: embedding data is awkward, data plumbing requires mental gymnastics, lack of recursive loops is inconvenient, annotations are ugly, and there is no typeful or contextual overloading of meaning. These issues cannot be resolved without complicating compilation, which would interfere with g0's primary role as a bootstrap language. To mitigate this, glas programmers are not limited to using g0. The g0 language can be used to implement itself and other language modules.

## Top Level

The top-level of a g0 file consists of imports, definitions, assertions, and export declarations. Simple line comments are also supported but skipped by the parser.

        # comments start with # and run to end of line.
        # there are no multi-line comments.

        # we can import definitions from other modules
        open foo
        from bar import qux, baz as bar-baz
        import math as m

        # direct definitions
        prog w1 [ Program ]
        data w2 [ Program ]
        macro w3 [ Program ]

        # procedural definitions. Data must be a dictionary.
        from [ Program ] import j, l as xyzzy

        # static assertions for lightweight verification
        assert [ Program ]

        # optionally control compiled value of module
        export [ Program ] 

        # alternatively an export list
        #   export w1, w2 as wd, w3

Most declarations may appear in any order and quantity, albeit with a restriction that words are defined only once and are defined before use. The 'open' and 'export' declarations are special cases; if present, they must respectively be the first and last declarations. 

## Dictionary

A valid g0 module initially compiles to a dictionary, represented as a record (a radix tree) of form `(word:deftype:Value, ...)`. The g0 language supports the 'prog', 'macro', and 'data' deftypes. The deftype determines how the Value will be handled at the call site for a word. Other deftypes may be imported or exported, and are accessible via static *load:dict* effects, but cannot be directly called within a g0 program.

Hierarchical dictionaries are supported via the 'data' deftype. See *Hierarchical Definitions*.

The prog, macro, and data deftypes do not include symbolic references back to the dictionary. That is, each definition in g0 is effectively stand-alone. This won't necessarily be the case for all glas system languages.

## Words

Words in g0 must match a simple regex: 

        WFrag: [a-z][a-z0-9]*
        Word:  WFrag('-'WFrag)*
        HWord: Word('/'Word)*

In context of a single g0 file, each word has exactly one definition, and must be defined before use.

## Imports

Imports provide convenient access to the module system. 

* **open ModuleRef** - start with dictionary defined in another module, but immediately delete all words defined later within the current file (for purpose of 'load:dict' effects). If present, must be first declaration in file.
* **from ModuleRef import ImportList** - add selected words from another module into the local namespace. Assumes compiled value of referenced module is a dictionary. Fails if any words from import list are undefined.
* **import ModuleRef as Word** - import the compiled value of the referenced module as data. This is primarily intended for use with hierarchical definitions.

If a module fails to load in the above import statements, compilation of the g0 module will fail. If more flexible handling of load failure is required, use compile-time 'load' effects.

### ModuleRef 

A ModuleRef may be one of:

        Word            # loads  global:"Word"
        './'Word        # loads  local:"Word"

It is possible that not all glas modules can be referenced this way, where module names are not valid g0 words. Those modules can still be accessed, albeit less conveniently, via compile-time 'load' effect (see *Procedural Definitions*).

### ImportList 

An ImportList applies to a dictionary and has the form:

        ImportWord = Word ('as' Word)?
        ImportList = ImportWord (',' ImportWord)*

Each word may optionally be renamed via the 'as' clause upon import; if elided, is equivalent to 'foo as foo'. It is an error to import a word that is not defined in the source dictionary. 

## Embedded Data

The g0 language supports embedded bitstring data in several forms.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (min-width natural number)
        -23                     0b01000         (one's complement of nats)
        0x17                    0b00010111      (four bits per character)
        'word                   0x776f726400    (null-terminated ascii symbol)

Beyond bitstrings, g0 supports strings and embedded programs as data. 

        "hello"                 a list of ascii bytes excluding C0, DEL, and "
        [Program]               data represents behavior as a Glas program

There are no escape characters. This is mitigated by ability to postprocess strings at compile time. Exact representation of programs is not specified because the g0 compiler may optimize or annotate programs, but represented behavior should be consistent.

The g0 language currently does not provide syntax optimized for constructing arbitrary dicts or lists. This can be mitigated by clever definitions, but expectation is that users will embed data in other modules using specialized data languages.

## Definitions

### Prog Definitions

        prog w1 [Program]

A 'prog' definition defines a normal runtime behavior. Any call to a prog word will apply the word's behavior at runtime, albeit subject to partial evaluation if there is sufficient input on the data stack and no effects are required. 

Compiles to 'w1:prog:(do:GlasProgram, ...)' entry in the dictionary. 

### Data Definitions

        data w2 [Program]

A 'data' definition is expressed by a program of arity 0--1 that is evaluated at compile-time to produce the data value. A call to a data word is trivially replaced by the computed data.

Compiles to a 'w2:data:Value' entry in the dictionary.

### Macro Definitions

        macro w3 [Program]

Macro definitions support staged metaprogramming. The program must have static stack arity. A macro call will be evaluated at compile-time, taking inputs from the data stack. This relies on partial evaluation. The top data stack result must represent a valid Glas program, which is then applied.

Compiles to 'w3:macro:prog:(do:GlasProgram, ...)' entry in the dictionary. 

*Aside:* The 'prog' header is required under 'macro' within g0. This is intended to simplify extension of macro types in context of other glas languages.

### Procedural Definitions

        from [ Program ] import ImportList

This supports procedural construction of a module value for imports, instead of directly naming a module. This program is evaluated at compile-time and should return a dictionary containing every word in the import list.

### Hierarchical Definitions

If word 'm' is defined as data that represents a dictionary, then 'm/sqrt' is the equivalent to applying a definition from that dictionary. This is intended mostly as a lightweight namespace model for use with qualified imports.

        import math as m
        prog dist [ [m/square] dip m/square m/add m/sqrt ]

However, this also works with dictionaries computed via 'data' definitions, for example.

### Primitive Definitions

The g0 language does not have any built-in definitions. Instead, macros and embedded data are the foundation for constructing arbitrary Glas programs. This is achieved as follows:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 'dip put]
        macro while-do [0 'do put 'while put 0 'loop put]
        ...

The [prims module](../prims/public.g0) will define many useful low-level functions.

## Static Assertions

        assert [test or analysis code here]

Assertions are 0--Any static arity programs that are evaluated at compile-time. Primary output is pass/fail, with a failed assertion causing compilation of the module to fail. Compile-time effects can be used to generate log messages. Assertions are not added to the dictionary, and thus are anonymous or identified by line number.

Static assertions serve a role in lightweight unit and integration tests. They can also support user-defined static analysis, e.g. we could assert that a program typechecks according to some function, though it's more convenient to leave regular analysis to the g0 compiler.

## Export

If present, export must be final declaration in a g0 file. Export may take two forms - list or function. If elided, the final dictionary is exported with all defined words.

        export ImportList
        export [ Program ]

In case of an import list, the output is a dictionary that includes only words in the list. Use of 'as' clauses is permitted to rename words upon export. In case of an export function, the program is evaluated as a data definition, then that data becomes the compiled module value. The 'load:dict' effect can access the final dictionary as data.

## Static Evaluation

Static evaluation is performed in context of macro calls, data definitions, procedural definitions, static assertions, and the export function. In all cases, static evaluation has access to compile-time effects. The basic effects:

* *log:Message* - same as language modules. Message is assumed to be a record value. Compiler may implicitly add some metadata about location.
* *load:Ref* - as language modules, but extended with references to the current dictionary.
 * *dict* - return a dictionary value that includes words defined earlier in file. Excludes any words that are defined later within the file (i.e. words implicitly imported via 'open' are deleted if defined later).
 * *dict:Path* - equivalent to 'load:dict' effect followed by 'get' on bitstring Path (usually a word). This can theoretically simplify incremental computing and precise dependency tracking compared to 'load:dict'.

## Partial Evaluation

The g0 language performs limited partial evaluation for static data during macro expansion. For consistency and comprehensibility, this partial evaluation is handled at granularity of words. For example, in context of `prog foo [1 swap 2]` the program `[foo swap]` cannot be further evaluated because each word, taken individually, lacks sufficient static data arguments. But `[3 foo swap]` should partially evaluate to `[1 2 3]`.

After macro expansion, a g0 compiler may apply a more general optimizer to Glas programs, including partial evaluation with a much finer granularity. See *Optimization* later.

## Annotations

The g0 language has no dedicated syntax for program annotations. 

A useful subset of annotations can be expressed on the identity behavior using plain prog or macro definitions. For example, we could annotate the current type of the data stack or effects API. We can use annotations to trigger garbage collection, or stow a large value from the data stack into content-addressed storage.

Where needed, such as for acceleration or memoization, annotations can be added to a program value prior to embedding, e.g. `[[Program] Annotate apply]`. This pattern isn't pretty, but it's adequate for infrequent use, and it could be integrated into macros in many cases.

## Static Analysis

To simplify bootstrap, g0 language does not specify any required static analysis. Despite this, g0 implementations are encouraged perform ad-hoc analyses and raise warnings or errors to improve or resist degradation of glas systems. Annotations and compiler directives can tune strictness. De-facto standards will likely develop over time. 

## Optimization

The g0 compiler is free to perform optimization when compiling g0 programs to glas 'prog' values. Observable behavior of valid programs should not directly be affected. Indirectly, optimization may be observed if analyzing compiled program values.

*Note:* To stabilize partial evaluation, program arity must be treated as an aspect of a program's observable behavior. The g0 compiler can use arity annotations to preserve arity.

## Compiler Directives

The compiler function can receive [directives](https://en.wikipedia.org/wiki/Directive_%28programming%29) from a program to tune static analysis, optimizations, quotas, and other implementation-specific features (where compiler variation is permitted). These are flexibly expressed as formatted comments, e.g. `#warn ...`. 

## Compilation Strategy for Blocks

This section describes a strategy for compiling a block of g0 code into a program value. I find it convenient to separate parser and linker passes.

        type AST = List of (data:Value | call:Ref | block:AST)
        type LinkedAST = List of (data:Value | prog:Program | macro:Program | block:LinkedAST)

        parse   : Text -> AST                   # parser combinators?
        link    : Dict -> AST -> LinkedAST      # mostly dict lookups
        compile : LinkedAST -> Program          # static eval; blocks to data 

The g0 language performs linking very eagerly compared to most programming languages. A consequence is that g0 programs are non-polymorphic and rely on structure sharing at the data layer for memory performance.

The compilation pass can be a simple left-to-right pass that evaluates each 'prog' call if possible (i.e. sufficient 'data' input based on arity, no runtime effects attempted) and must successfully evaluate every 'macro' call (with access to compile-time effects). Each block is compiled recursively, then compiles to 'data:Program'.

The compile step may optimize the returned program. For stability of partial evaluation, if any optimization would affect arity of the program, an arity annotation should also be inserted.

## Bootstrap 

The glas command line interface will have a built-in g0 compiler that is used for bootstrap. Bootstrap will be logically performed every time we use the interface, though rework may implicitly be mitigated by caching.

The built-in g0 is used to compile module language-g0, producing a value of form `(compile:P0, ...)`. Program P0 is then interpreted to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same *behavior* but may differ structurally due to variation in optimizations or annotations. To prove equivalent behavior, we'll simply rebuild language-g0 using P1, producing `(compile:P2, ...)`. Bootstrap is considered successful if P1 and P2 are the same.

## Thoughts on Language Extensions

Idle thoughts that insist on idling. Mostly reminders for why I rejected them. 

### Multi-Stage Macros. Rejected.

I could permit macro calls to return `macro:(...)`, to be evaluated as another macro call. This would enable macros to have variable static arity based on data. For example, a macro could search for a sentinel value on the data stack. However, I'm not convinced this is a good idea. It hinders local reasoning and refactoring, and introduces an inconsistency between compile-time and runtime stack behavior.

### Local Variables. Rejected.

Interaction of variables and macros introduces a lot of complexity that I'd prefer to avoid. Something to leave for future language modules.

### Negative Assertions. Rejected.

The `assert [[Test Code] reject]` pattern isn't uncommon. I could specialize this, e.g. introducing `reject [Test Code]` directly. But I decided against this because there is an asymmetry for compile-time effects.
