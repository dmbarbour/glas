# Language g0

The g0 language is a Forth variant with staged metaprogramming, algebraic effects, and immutable tree-structured data. The simple syntax and semantics of g0 are intended to remain close to the g0 program model and simplify bootstrap. This is the primary bootstrapped language in the Glas system, so other languages must be implemented (perhaps indirectly) using g0.

The g0 syntax is not suitable for all programs or programmers. It can be awkward to track the data stack in one's head, and the syntax for embedding data doesn't scale well to structured data. Users should develop different language modules to cover more use cases.

## Top Level

The top-level of a g0 file consists of imports, definitions, assertions, and an export function. Simple line comments are also supported.

        # comments start with # and run to end of line.

        # import definitions from other modules
        open foo
        from bar import qux, baz as bar-baz
        import math as m

        # direct definitions
        prog w1 [ Program ]
        macro w2 [ Program ]
        data w3 [ Program ]

        # procedural definitions
        from [ Program ] import j, l as xyzzy

        # static assertions for lightweight verification
        assert [ Program ]

        # rewrite of module value
        export [ Program ]

Most entries may appear in any order and number. The exceptions are 'open' and 'export' which, if included, must respectively be the first and last entries. Use of 'open' inherits definitions from another module. Use of 'export' can rewrite or replace default module value. 

## Dictionary

By default, a g0 module compiles to a dictionary - a value of form `dict:(w1:prog:do:Program, w2:macro:Program, w3:data:Value, m:data:Dictionary, qux:(...), ...)`. Each symbol in the dictionary corresponds to a defined word, and each definition has a type header to allow specialized handling at the call site. The 'dict' header is intended to simplify future integration with or extension by other language modules.

A g0 program only recognizes words with the `prog | macro | data` definition types. Words with unrecognized definition types may be imported and exported but cannot be called from a g0 program.

## Imports

Imports provide convenient access to the module system. 

* **open ModuleName** - start with words defined in another module. The value of the named module must be a dictionary. Unused words from this module may be overridden by later imports or definitions.
* **from ModuleName import ImportList** - add selected words from a module into the local namespace. The import list is comma separated list of words with optional 'as' clauses for local renaming.
* **import ModuleName (as Word)** - this simply imports a module value as data. If the 'as' clause is elided, the ModuleName is used as the data word. See *Hierarchical Definitions*.

Import may fail if the module cannot be loaded or (in case of 'from') does not define an imported word. In case of failure, the full module will fail to compile. 

Modules can be programatically imported via 'load' effect in static evaluation contexts (such as data evaluation and macro calls). This allows for more flexible failure handling and inspection or manipulation of the loaded value. However, this is much less concise and convenient than import declarations.

## Words

Words in g0 must match a simple regex: 

        WFrag: [a-z][a-z0-9]*
        Word:  WFrag('-'WFrag)*
        HWord: Word('/'Word)*

In context of a g0 module, each word has exactly one definition. Words implicitly imported via 'open' may be redefined but only before their first use, i.e. such that the original definition is never observed within the g0 module. Hierarchical words are indirectly defined, i.e. we define 'm/sqrt' as part of defining data 'm'.

The g0 language cannot directly import or reference definitions that don't have valid g0 words. This isn't a problem when working mostly within g0, but importing definitions from modules written in other languages might require using macros to extract the definition.

## Embedded Data

The g0 program can directly include bitstrings, natural numbers, symbols, strings, and programs.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (min-width natural number)
        0x17                    0b00010111      (four bits per character)
        'word                   0x776f726400    (symbol for any valid word)
        "hello"                 a list of ascii bytes excluding C0, DEL, and "
        [Program]               program as a value, mostly for use with macros

The value `0` is the single-node tree. This can represent an empty bitstring, empty list, empty record, unit value, and natural number zero. For clarity of intention, it's best to define something like `data unit [0]` for each of these cases, though. Program values are locally non-deterministic due to compiler optimizations and annotations.

There is no dedicated syntax for bulk or structured data. Clever word choice can mitigate this. For example, with suitable definitions, a multi-line text could be embedded as:

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

However, instead of awkwardly embedding data in g0, it is better to develop language modules for data entry. Text could be provided in a `.txt` file. Structured data could use JSON, XML, MsgPack, SQLite, etc.. A `.g0` file may then simply `import` the module data, or load it via macro.

## Programs 

        [0x2A [foo] bar]

Language g0 programs are expressed as blocks of words and data delimited by square brackets. Programs themselves can be embedded as data within programs, mostly for use in macros. Embedded programs may be optimized by a g0 compiler, thus do not have a locally deterministic representation as data.

## Definitions

### Macro Definitions

Macro definitions support staged metaprogramming. Each call to a macro will be evaluated at compile-time, taking inputs from the data stack based on partial evaluation. The top data stack result must represent a Glas program, which is subsequently applied. If any macro call fails, that is a compile-time error and the entire g0 module will fail to compile.

### Prog Definitions

A 'prog' definition is for normal runtime behavior. A call to a prog word will simply apply the word's behavior, albeit subject to partial evaluation and other optimizations.

### Data Definitions

A 'data' definition is expressed by a program of arity 0--1 that is evaluated at compile-time to produce the data value. A call to a data word is trivially replaced by the computed data.

### Procedural Definitions

        from [ Program ] import foo, bar as baz, qux

A 'from' entry may be parameterized by a 0--1 arity program instead of a module name. This program is evaluated at compile-time to return a dictionary from which we integrate definitions into the toplevel namespace. It is an error if the imported word is not defined or has an unrecognized type.

### Hierarchical Definitions

Within a program, `m/sqrt` requires that 'm' is dictionary data defining 'sqrt'. That is, 'm' is a data word whose value is of form `(dict:(sqrt:(...)), ...)`. This is usually achieved via 'import' declaration or 'data' definition. A benefit of hierarchical definitions is that it reduces need to import words individually, and provides clear provenance of words. A disadvantage is that it's often more verbose.

Hierarchical definitions can be arbitrarily deep, e.g. `m/trig/sine` requires that `m/trig` is also dictionary data that defines sine. However, programmers may receive a style warning for going more than one level deep.

### Primitive Definitions

The g0 language does not have any implicit definitions. Instead, we can use macros and embedded data as the foundation for constructing arbitrary Glas programs, as follows:

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

## Export Function

The final step when compiling a g0 module is to evaluate the export function. The dictionary value for the module is input to the export function, and the output is taken as the module's compiled value. Export functions are potentially useful for procedural generation of definitions, or compiling values for consumption outside the g0 context. If the 'export' entry is elided, is equivalent to `export []`, returning the dictionary unmodified.

### Export Word List

Alternatively, g0 modules may export an inclusion list of defined words. For example, `export foo, bar, qux` filters the dictionary to contain just those three words. This provides lightweight support for simple export control, but anything more sophisticated will need to use the export function.

## Static Evaluation

Static evaluation is performed in context of macro calls, data definitions, procedural definitions, static assertions, and the export function. In all cases, static evaluation has access to compile-time effects. This include the 'log' and 'load' effects available to language modules, but may be extended with [compiler directives](https://en.wikipedia.org/wiki/Directive_%28programming%29) that influence or observe compiler state, perhaps increasing a quota or disabling a warning or requesting compiler version info. However, directives should be carefully considered to with regards to portability or complication.

## Partial Evaluation

The g0 language performs limited partial evaluation for static data during macro expansion. For consistency and comprehensibility, this partial evaluation is handled at granularity of words. For example, in context of `prog foo [1 swap 2]` the program `[foo swap]` cannot be further evaluated because each word, taken individually, lacks sufficient static data arguments. But `[3 foo swap]` should partially evaluate to `[1 2 3]`.

After macro expansion, a g0 compiler may apply a more general optimizer to Glas programs, including partial evaluation with a much finer granularity. However, some optimizations can affect static arity, such as eliminating `swap swap` changes `2--2` to `0--0`. To stabilize partial evaluation during macro expansion, arity annotations should be inserted in cases where optimization affects arity.

## Annotations

There is no special syntax for program annotations. Annotations can be introduced indirectly by wrapping program data before applying it, e.g. `prog list-len [[list-len-body] 'list-len anno-accel apply]` might tell a compiler or interpreter to use the accelerated implementation for list length computations, while also providing the manual implementation.

## Static Analysis

One design goal for Glas systems is gradual typing. Compared to use of static assertions, it is more convenient if this depends mostly on annotations and inference. Thus, a g0 compiler may evolve to recognize more annotations, perform more analyses, and reject more programs. 

If a new analysis would suddenly reject many existing programs (even for legitimate reasons), that requires a softer touch to avoid breaking the Glas ecosystem. This might be achieved by starting with the analysis in a 'future-error' mode, introduce compiler directives or to opt-in early or opt-out, and give developers time to clean up or deprecate projects. Later, with consensus, we could switch the analysis to reject problematic programs.

*Note:* A 'future-error' should be distinguished from a 'warning' in certain contexts, such as understanding ecosystem health, or when using compiler directives to convert errors to warnings. Use of a to-be-deprecated API would similarly warrant a 'future-error' message.

## Compilation Strategy

After parse, the AST for g0 programs is essentially a list of `data:Value | block:AST | call:Word`, perhaps extended with location data to support debugging.

A first compilation pass walks left to right over the g0 AST, linking words and compiling blocks, producing a list of `data:Value | prog:Program | macro:Program`. Embedded data and calls to data words trivially evaluate to 'data'. A block compiles a g0 AST recursively (including any optimization passes), then provides the program as data. A prog call evaluates to 'prog', linking the program definition. A macro call evaluates to 'macro', linking the macro definition.

A second compilation pass performs partial evaluation of 'prog' calls and eliminates all 'macro' calls, producing a list of `data:Value | prog:Program`. Each 'prog' is partially evaluated if there is sufficient 'data' and evaluation returns successfully without requiring any runtime effects. Each 'macro' is statically evaluated in context of available data, then the top data result is rewritten to a 'prog', which may be partially evaluated. Compilation of the g0 module fails if any macro call cannot be evaluated.

After the second pass, we wrap the resulting list with 'seq' then may pass the program to a Glas program optimizer. The optimizer is free to perform ad-hoc program to program rewrites and partial evaluations, e.g. based on abstract interpretation. Ideally, the optimizer should preserve static arity via annotations in case of optimizations that affect arity, otherwise we may have inconsistent partial evaluation behavior depending on optimizer version.

*Note:* A 'prog' definition always results in 'prog' header in the dictionary, even if it could be optimized to 'data'. The idea here is to preserve programmer intentions against optimizations.

## Compilation Quotas

With static evaluation, we can easily have compilations take arbitrary amounts of time. To guard against this, a g0 compiler could use quotas, tuning them such that they're sufficiently large that most modules do not have a problem. A compiler function cannot observe CPU time, but a quota could be based on number of operations for example. Quotas could be increased via compiler directives (static effects) for modules that are expected to take a long time to compile.

## Bootstrap 

The glas command line executable will have a built-in g0 compiler that is used only for bootstrap. Bootstrap will be logically recomputed every time we use the executable, but the rework can be mitigated by caching.

The built-in g0 is used to compile module language-g0, producing a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same *behavior* but may differ structurally due to variations in optimizations or annotations. It's difficult to check that the behaviors are the same, but we can easily use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are exactly the same, we have successfully bootstrapped language-g0. Otherwise, time to debug!

## Thoughts on Language Extensions

Some ideas that come to mind repeatedly.

### Multi-Stage Macros? Reject.

I could allow macro calls to return `macro:Program`, to be evaluated as another macro call. This would enable macros to have variable static arity based on data, or to scan the static data stack for sentinel values. However, I'm uncertain that I want to enable or encourage ad-hoc scanning of the data stack. It creates a huge inconsistency between compile-time and runtime programming. Further, this feature complicates local reasoning and refactoring because programmers cannot just look at arity. Decided to reject unless I find a very strong use-case. 

### Local Variables? Reject.

To make variables work well, e.g. such that we can access and update a local variable from deep within a loop, I need macros that operate on an abstract g0 AST instead of program values. Although this is feasible, it isn't a complication I want for the g0 language. My decision is to support variables in higher-level language modules.
