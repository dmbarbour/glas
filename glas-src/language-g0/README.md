# Language g0

The g0 language is essentially a Forth-inspired macro-assembly for the Glas program model. The Glas program model is oriented around immutable data and algebraic effects. To this, the g0 layer adds limited support for modularity and staged metaprogramming. 

The g0 syntax isn't suitable for all programs or programmers. It's especially awkward for embedding data. The main use case for g0 is very early development and bootstrap of the Glas language system. Thus, a primary design goal is simplicity of implementation. The g0 language can later be used to implement other Glas language modules.

## Top Level

The top-level of a g0 file consists of imports, definitions, assertions, and export declarations. Simple line comments are also supported.

        # comments start with # and run to end of line.

        # import definitions from other modules
        open foo
        from bar import qux, baz as bar-baz
        import math as m

        # direct definitions
        prog w1 [ Program ]
        data w2 [ Program ]
        macro w3 [ Program ]

        # procedural definitions
        from [ Program ] import j, l as xyzzy

        # static assertions for lightweight verification
        assert [ Program ]

        # control final compiled value of module
        export [ Program ] # or an import list
        # alternatively may specify export word list

Most declarations may appear in any order and quantity. The exceptions are 'open' and 'export' which, if included, must respectively be first and last. 

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
* **import ModuleRef as Word** - import the compiled value of the referenced module as data. This is primarily intended for use with hierarchical definitions, e.g. 'import math as m' then later call 'm/sqrt'. However, this can also be convenient for import of arbitrary data.

If a module fails to load in the above import statements, compilation of the g0 module will fail. If more flexible handling of load failure is required, use compile-time 'load' effects.

### ModuleRef 

A ModuleRef may be one of:

        Word            # loads  global:"Word"
        './'Word        # loads  local:"Word"

The g0 language can only conveniently reference modules whose names are valid g0 words. Use of compile-time 'load' effects don't have this naming constraint.

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

Compiles to 'w1:prog:(do:GlasProgram, ...)' entry in the dictionary. (Does not reduce to a 'data' definition even if the optimized program is just 'data:Value'.)

### Data Definitions

        data w2 [Program]

A 'data' definition is expressed by a program of arity 0--1 that is evaluated at compile-time to produce the data value. A call to a data word is trivially replaced by the computed data.

Compiles to a 'w2:data:Value' entry in the dictionary.

### Macro Definitions

        macro w3 [Program]

Macro definitions support staged metaprogramming. The program must have static stack arity. A macro call will be evaluated at compile-time, taking inputs from the data stack. This relies on partial evaluation. The top data stack result must represent a valid Glas program, which is then applied.

Compiles to 'w3:macro:GlasProgram' entry in the dictionary. (Does not reduce to a 'prog' definition even if the macro is equivalent to a program.)

### Procedural Definitions

        from [ Program ] import ImportList

This supports procedural construction of a module value for imports, instead of directly naming a module. This program is evaluated at compile-time and should return a dictionary containing every word in the import list.

### Hierarchical Definitions

If word 'm' is data representing a dictionary, then 'm/sqrt' is the equivalent to applying a definition from that dictionary.

        import math as m
        prog dist [ [m/square] dip m/square m/add m/sqrt ]

The primary use case is to enable imports to act as lightweight namespaces. However, this also works with computed dictionaries, e.g. via 'data' definitions.

### Primitive Definitions

The g0 language does not have built-in definitions. Instead, macros and embedded data are the foundation for constructing arbitrary Glas programs, as follows:

        macro apply []
        prog swap ['swap apply]
        prog drop ['drop apply]
        ...
        macro dip [0 'dip put]
        macro while-do [0 'do put 'while put 0 'loop put]
        ...

Currently these are defined in the [prims module](../prims/public.g0).

## Static Assertions

        assert [test or analysis code here]

Assertions are 0--Any static arity programs that are evaluated at compile-time. Primary output is pass/fail, with a failed assertion causing compilation of the module to fail. Compile-time effects can be used to generate log messages. Assertions are not added to the dictionary, and thus are anonymous or identified by line number.

Static assertions serve a role in lightweight unit and integration tests. They can also support user-defined static analysis, e.g. we could assert that a program typechecks according to some function, though it's more convenient to leave regular analysis to the g0 compiler.

## Export

If present, must be final declaration ignoring comments and whitespace. Export may take two forms - list or function. If elided, the final dictionary is exported unmodified.

        export ImportList
        export [ Program ]

In case of a list, only words in the list are exported. They may optionally be renamed via 'as' clauses. The function option is more flexible: it's handled like a data definition except the computed value becomes the module's compiled value. This enables g0 modules to have any value type, not limited to dictionaries. If rewriting the dictionary, use the 'load:dict' effect. 

## Static Evaluation

Static evaluation is performed in context of macro calls, data definitions, procedural definitions, static assertions, and the export function. In all cases, static evaluation has access to compile-time effects. The basic effects:

* *log:Message* - same as language modules. Message is assumed to be a record value. Compiler may implicitly add some metadata about location.
* *load:Ref* - as language modules, but extended with references to the current dictionary.
 * *dict* - return a dictionary value that includes words defined earlier in file. Excludes any words that are defined later within the file (i.e. words implicitly imported via 'open' are deleted if defined later).
 * *dict:Path* - equivalent to 'load:dict' effect followed by 'get' on Path. This can theoretically simplify incremental computing and precise dependency tracking compared to 'load:dict'.

These effects may be extended by the compiler, e.g. introduce [compiler directives](https://en.wikipedia.org/wiki/Directive_%28programming%29) to manage warnings, guide optimizations, tune static analysis, or manage quotas.

## Partial Evaluation

The g0 language performs limited partial evaluation for static data during macro expansion. For consistency and comprehensibility, this partial evaluation is handled at granularity of words. For example, in context of `prog foo [1 swap 2]` the program `[foo swap]` cannot be further evaluated because each word, taken individually, lacks sufficient static data arguments. But `[3 foo swap]` should partially evaluate to `[1 2 3]`.

After macro expansion, a g0 compiler may apply a more general optimizer to Glas programs, including partial evaluation with a much finer granularity. However, some optimizations can affect static arity, such as eliminating `swap swap` changes `2--2` to `0--0`. To stabilize partial evaluation during macro expansion, arity annotations should be inserted in cases where optimization affects arity.

## Annotations

There is no special syntax for program annotations. Annotations can be introduced indirectly by wrapping program data before applying it, e.g. `prog list-len [[list-len-body] 'list-len anno-accel apply]` might tell a compiler or interpreter to use the accelerated implementation for list length computations, while also providing the manual implementation.

## Static Analysis

One design goal for Glas systems is gradual typing. Compared to use of static assertions, it is more convenient if this depends mostly on annotations and inference. Thus, a g0 compiler may evolve to recognize more annotations, perform more analyses, and reject more programs. 

If a new analysis would suddenly reject many existing programs (even for legitimate reasons), that requires a softer touch to avoid breaking the Glas ecosystem. This might be achieved by starting with the analysis in a 'future-error' mode, introduce compiler directives or to opt-in early or opt-out, and give developers time to clean up or deprecate projects. Later, with consensus, we could switch the analysis to reject problematic programs.

*Note:* A 'future-error' should be distinguished from a 'warning' in certain contexts, such as understanding ecosystem health, or when using compiler directives to convert errors to warnings. Use of a to-be-deprecated API would similarly warrant a 'future-error' message.

## Compilation Strategy for Blocks

This section describes a strategy for compiling a block of g0 code into a program value. I find it convenient to separate parser and linker passes.

        type AST = List of (data:Value | call:Ref | block:AST)
        type LinkedAST = List of (data:Value | prog:Program | macro:Program | block:LinkedAST)

        parse   : Text -> AST                   # parser combinators?
        link    : Dict -> AST -> LinkedAST      # mostly dict lookups
        compile : LinkedAST -> Program          # partial and static eval, blocks to data 

The g0 language performs linking very eagerly compared to most programming languages. A consequence is that g0 programs are non-polymorphic and rely on structure sharing at the data layer for memory performance.

The compilation pass can be a simple left-to-right pass that evaluates each 'prog' call if possible (i.e. sufficient 'data' input based on arity, no runtime effects attempted) and must successfully evaluate every 'macro' call (with access to compile-time effects). Each block is compiled recursively, then compiles to 'data:Program'.

The compile step may optimize the returned program. For stability of partial evaluation, if any optimization would affect arity of the program, an arity annotation should also be inserted.

## Bootstrap 

The glas command line executable will have a built-in g0 compiler that is used only for bootstrap. Bootstrap will be logically recomputed every time we use the executable, but the rework can be mitigated by caching.

The built-in g0 is used to compile module language-g0, producing a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same *behavior* but may differ structurally due to variations in optimizations or annotations. It's difficult to check that the behaviors are the same, but we can easily use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are exactly the same, we have successfully bootstrapped language-g0. Otherwise, time to debug!

## Thoughts on Language Extensions

Some ideas that come to mind repeatedly.

### Multi-Stage Macros

I could allow macro calls to return `macro:Program`, to be evaluated as another macro call. This would enable macros to have variable static arity based on data, e.g. scan the static data stack for a sentinel value to build a list. 

However, I'm uncertain that I want to enable or encourage ad-hoc scanning of the data stack. It creates a huge inconsistency between compile-time and runtime programming. Further, this feature complicates local reasoning and refactoring because programmers cannot just look at arity. Decided to reject unless I find a very strong use-case. 

### Local Variables

To make variables work well, e.g. such that we can access and update a local variable from deep within a loop, I need macros that operate on an abstract g0 AST instead of program values. Although this is feasible, it isn't a complication I want for the g0 language. My decision is to support variables in higher-level language modules.

### Negative Assertions 

Currently, it is feasible to express a negative assertion with a macro that verifies a program halts with a backtracking failure.

        assert [ [test code] verify-not ]

I've found this pattern is common in practice. It is tempting to provice a specialized syntax for the same behavior.

        reject [ test code ]

However, I hesitate to do so. I feel it might be a bit confusing if compiler directive effects within 'reject' are backtracked.
