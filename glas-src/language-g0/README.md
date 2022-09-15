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

A valid g0 module initially compiles to a dictionary, represented as a record (radix tree) of form `(word:deftype:Value, ...)`. The g0 language handles 'prog', 'macro', and 'data' deftypes. The deftype determines how a word will be applied. Words of other deftypes are possible via import but cannot be called from a g0 program.

The 'prog' and 'macro' deftypes are statically linked at compile time, and 'data' is statically evaluated. Thus, there are no symbolic references between definitions of these types. Structure sharing and memoization can serve a performance role similar to symbolic references, e.g. memoize the inferred type for a subprogram.

*Note:* An export function can rewrite or replace this dictionary. This enables g0 modules to compile to arbitrary values.

## Words

Words in g0 must match a simple regex: 

        WFrag: [a-z][a-z0-9]*
        Word:  WFrag('-'WFrag)*
        HWord: Word('/'Word)*

In context of a g0 module, a word will have exactly one definition. That is, a word cannot be defined twice or be defined after use. However, words implicitly imported via 'open' may be overridden before first used.

## Imports

Imports provide convenient access to the module system. A design goal is that definition and provenance of every word must be unambiguous at g0 file scope. 

* **open ModuleRef** - start with words defined in another module. The value of the referenced module must be a dictionary. Unused words from this module may be overridden by later imports or definitions. 
* **from ModuleRef import ImportList** - add selected words from a module into the local namespace. The import list has form 'x, y as foo-y, z' - a comma separated list of words with optional 'as' clauses for local renaming. It is an error if any word in the import list is undefined.
* **import ModuleRef as Word** - import value of any module as a 'data' word. Mostly intended for hierarchical definitions, e.g. after 'import math as m' we can use 'm/sqrt'. The 'as' clause is required in this case.

### ModuleRef 

A ModuleRef may be one of:

        Word            # load  global:"Word"
        './'Word        # load  local:"Word"

The g0 language cannot directly import modules whose names are not valid g0 words. Local and global module namespaces are entirely separate, no fallbacks or defaults. A failure to load a module will be a compile time error. In the rare case that more flexible behavior is required, compile-time evaluation can directly use 'load' effects.

### ImportList 

An ImportList has the form:

        ImportWord = Word ('as' Word)?
        ImportList = ImportWord (',' ImportWord)*

Each word in our import list must be found in the source dictionary, and is added to the current dictionary, optionally using a new name via the 'as' clause.


## Embedded Data

The g0 program can directly include bitstrings, natural numbers, symbols, strings, and programs.

        0b010111                (becomes identical bitstring)
        23                      0b10111         (min-width natural number)
        0x17                    0b00010111      (four bits per character)
        'word                   0x776f726400    (symbol for any valid word)
        "hello"                 a list of ascii bytes excluding C0, DEL, and "
        [Program]               program as a value, mostly for use with macros

Depending on context, value `0` might represent an empty bitstring, empty list, empty record, unit value, and natural number zero. For clarity of intention, it's best to define something like `data unit [0]` for each case.

Program values are compiled. Their representation may vary due to which optimizations are applied, but behavior should be preserved and compilation is deterministic within context of the language-g0 module. In most cases, program values are parameters to macros.

Other than programs values, there is no syntax for structured data. Clever word choice can mitigate this. For example, with suitable definitions, a multi-line text could be embedded as:

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

However, the intended approach for embedding data is to define another language, e.g. define a language-txt module that can convert most texts to UTF-8 and run a spellchecker. Also define generic language modules to parse JSON, XML, SQLite, and other structured data. 

## Programs 

        [0x2A [foo] bar]

Language g0 programs are expressed as blocks of words and data delimited by square brackets. Programs themselves can be embedded as data within programs, mostly for use in macros. Embedded programs may be optimized by a g0 compiler, thus do not have a locally deterministic representation as data.

## Definitions

### Macro Definitions

Macro definitions support staged metaprogramming. Each call to a macro will be evaluated at compile-time, taking inputs from the data stack based on partial evaluation. The top data stack result must represent a Glas program, which is subsequently applied. If any macro call fails, that is a compile-time error and the entire g0 module will fail to compile.

Compiles to a 'word:macro:GlasProgram' entry in the dictionary. 

### Prog Definitions

A 'prog' definition is for normal runtime behavior. A call to a prog word will apply the word's behavior at runtime, albeit subject to partial evaluation if there is sufficient input on the data stack and no effects are required. 

Compiles to a 'word:prog:(do:GlasProgram, ...)' entry in the dictionary. Potentially annotated.

### Data Definitions

A 'data' definition is expressed by a program of arity 0--1 that is evaluated at compile-time to produce the data value. A call to a data word is trivially replaced by the computed data.

Compiles to a 'word:data:Value' entry in the dictionary.

### Procedural Definitions

        from [ Program ] import ImportList

A 'from' entry may be parameterized by a data program instead of a module name. This program is evaluated at compile-time and must return a dictionary. We then import definitions from this dictionary same as we would from a module's compiled value.

### Hierarchical Definitions

A dictionary may contain other dictionaries as 'data'. The g0 language provides access to words from these hierarchical dictionaries via 'm/sqrt' syntax. This would essentially apply the definition reached by following path 'm.data.sqrt'. We can define the hierarchical dictionary via 'import math as m' or by other means.

*Aside:* I'm not fond of the aesthetic when a program uses too many calls to hierarchical definitions. However, it is at least very conventional to organize definitions into bundles.

### Primitive Definitions

The g0 language does not have any built-in definitions. Instead, we can use macros and embedded data as the foundation for constructing arbitrary Glas programs, as follows:

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

        export ImportList
        export [ Program ]

In case of an import word list, we'll export only the final words represented by this list. In case of a function, the function must be 1--1 arity, receiving the dictionary as input and returning the compiled module value. If absent, is equivalent to `export []`, returning the unmodified dictionary.

The export function may return any Glas value, not limited to a dictionary. Thus, the export function can potentially be useful for adaptation between Glas system languages, or procedural generation of data modules.

## Static Evaluation

Static evaluation is performed in context of macro calls, data definitions, procedural definitions, static assertions, and the export function. In all cases, static evaluation has access to compile-time effects. This include the 'log' and 'load' effects available to language modules, but may be extended with [compiler directives](https://en.wikipedia.org/wiki/Directive_%28programming%29) that influence compiler state, such as controlling compiler warnings or setting quotas.

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

*Note:* A 'prog' definition always results in 'prog' header in the dictionary, even if it could be optimized to 'data'. Similarly, 'macro' is not optimized to 'prog' even if it could be. The idea here is to preserve programmer intentions.

## Compilation Quotas

With static evaluation, we can easily have compilations take arbitrary amounts of time. To guard against infinite loops, a g0 compiler could use quotas, and support compiler directives (via compile-time effects) to tune quotas in case they are too small for a specific module. 

## Bootstrap 

The glas command line executable will have a built-in g0 compiler that is used only for bootstrap. Bootstrap will be logically recomputed every time we use the executable, but the rework can be mitigated by caching.

The built-in g0 is used to compile module language-g0, producing a value of form `(compile:P0, ...)`. Program P0 is then used to rebuild language-g0, producing `(compile:P1, ...)`. P0 and P1 should have the same *behavior* but may differ structurally due to variations in optimizations or annotations. It's difficult to check that the behaviors are the same, but we can easily use P1 to rebuild language-g0 once more, producing `(compile:P2, ...)`. If P1 and P2 are exactly the same, we have successfully bootstrapped language-g0. Otherwise, time to debug!

## Thoughts on Language Extensions

Some ideas that come to mind repeatedly.

### Multi-Stage Macros? Reject.

I could allow macro calls to return `macro:Program`, to be evaluated as another macro call. This would enable macros to have variable static arity based on data, or to scan the static data stack for sentinel values. However, I'm uncertain that I want to enable or encourage ad-hoc scanning of the data stack. It creates a huge inconsistency between compile-time and runtime programming. Further, this feature complicates local reasoning and refactoring because programmers cannot just look at arity. Decided to reject unless I find a very strong use-case. 

### Local Variables? Reject.

To make variables work well, e.g. such that we can access and update a local variable from deep within a loop, I need macros that operate on an abstract g0 AST instead of program values. Although this is feasible, it isn't a complication I want for the g0 language. My decision is to support variables in higher-level language modules.
