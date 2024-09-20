# Glas Namespaces

For glas configurations and applications, we need modular namespaces with support for overrides, name shadowing, and robust access control. Other useful features include lazy extraction of needed definitions, staged metaprogramming within the namespace, garbage collection of temporary names, and support for concurrent interactive computation and parallelism. 

This document proposes a simple namespace model to support these goals, based primarily on prefix-oriented translations of names. I assume definitions and import expressions are both represented using an [abstract assembly](AbstractAssembly.md). This enables us to precisely recognize and rewrite names within definitions, and also capture translations for localization or redirects. We can reduce translations to a single lazy pass to control overhead for rewrites.

## Proposed Model

We'll represent the namespace using plain old glas data, with support for deferred computations. This doesn't structurally prevent risk of name conflicts, type errors, staging errors, and divergence. But we'll generally evaluate NS at compile time, report warnings through logs, and also continue evaluation in presence of minor errors.

        type NS
            = df:(Map of Name to Expr)                      # define
            | mx:(TL, Set of NS)                            # mix
            | ld:(TL, Set of Expr)                          # load

        type TL =
            ( mv?(Map of Prefix to (Prefix | NULL | WARN))  # move
            , ln?(Map of Prefix to Prefix)                  # link
            )

        type Set = List                     # ignore order and dups
        type Name = Symbol                  # assumed prefix unique  
        type Prefix = Symbol                # empty up to full name
        type Symbol = Bitstring             # byte aligned, no NULL
        type WARN = NULL 'w' (0x00 0x77)    # remove with a warning
        type Map = Trie                     # a NULL byte separator
        type Expr = AST                     # via abstract assembly

Description of namespace constructors (NS):

* *define (df)* - Provide set of definitions as dictionary. Canonical form of evaluated NS.
* *mix (mx)* - Express a namespace as a set of smaller namespaces and a latent translation. 
* *load (ld)* - Express namespace as a macro to be expanded and integrated. Each Expr should evaluate to a `(Output, Continuation)` pair. We effectively expand this to `mx:(TL, Output), ld:(TL, Continuation)`.

Description of namespace translations (TLMap):

* *move (mv)* - rewrites keys of 'df' based on longest matching prefix. Special destinations: NULL to quietly remove a definition, WARN to remove with a warning.
* *link (ln)* - rewrites names and localizations within Exprs based on longest matching prefix.

## Prefix Unique Names

Prefix-based rewrites make it difficult to work with "pea" without also touching "pear" and "pearl". To resolve this problem, a front-end compiler can add a reserved suffix to ensure no full name is a prefix of another full name. For example, we can easily rename or remove "pea.!" without touching "pear.!" and "pearl.!". Also, we can easily detect when prefix uniqueness is violated and log a warning. 

*Note:* I propose ".!" to align with conventions for hierarchical structure. This effectively treats definition of "pea" as included in "pea.\*", allowing us to rewrite both with prefix "pea.". This will simplify import lists. I choose '!' simply as the first printable character for lexicographic sort, but we could use something else.

## Conflicting Definitions

A conflict occurs where two or more 'df' introduce the same name with different Exprs. There is no conflict when they agree on the Expr. When a conflict is detected, we should report a warning or error. In contexts where computation must continue, we can resolve the conflict by choosing one version, favoring the version we've already observed via load or lazy extraction.

*Note:* I considered a variation where I aggregate proposed definitions into a set. This seems useful for expressing into multimethods, tables, or constraint systems. However, it made it a lot more awkward to determine when we're done with a definition. I've decided to express these via design patterns instead; see *Composing Definitions*.

## Translation Patterns

Moves and links are applied based on longest matching prefix, and it's possible to swap or rotate names in a single step such as `{ "bar." => "foo.", "foo." => "bar." }`. WWith a few patterns, translations support shadowing, overrides, hierarchical composition, global definitions, and robust access control. 

* A rename involves 'mv' and 'ln' operations with the same Map. In practice, we'll usually use 'ln' only as part of a rename, but 'mv' may diverge.  
* To override `foo.!` , we can move `foo.! => foo^.!`, and simultaneously rename `foo^ => foo^^` to avoid introducing name conflicts. Then users can re-define `foo.!` with reference to the prior `foo^.!` (perhaps via keyword like 'prior' or 'super').
* To shadow `foo.!`, it's essentially same as 'override' except we *rename* `foo.! => foo^.!` instead of moving it. This preserves existing relationships to the prior definition, where override binds existing references to the new definition.
* To model private definitions, we could reserve '~' for private names, use only rewrites that preserve privacy, and optionally warn if a name directly used in an Expr before any translations will violate privacy.
* To model hierarchical components, we can simply add a prefix to everything `{ "" => "foo." }` but also alias some shared components as needed, such as global definitions.
* To model global definitions, we could take a prefix such as "g." and propagate it by default into hierarchical components, e.g. component 'foo' might have a rewrite rule `{ "" => "foo.", "g." => "g." }`. This implicit rewrite is already assumed for AST constructors in abstract assembly.
* To express assumptions about where names are defined in a modular namespace, we can leverage remove with warnings, e.g. move `{ "" => WARN, "foo." => "foo." }` says that the target namespace only introduces `foo.*`, while a move `{ "bar." => WARN }` says the tacit namespace does not introduce `bar.*`. Lazy evaluation of namespaces can benefit from precise expression of assumptions.

## Composition of Translations

For performance, it's useful to compose translations before applying them to Exprs. 

We can compose translations sequentially, where one translation is followed by another. It can be useful for performance to compose translations instead of applying multiple sequential translations to Exprs, especially when the Exprs are large or when the composed translation is applied to many Exprs. Of course, composition of translations also has a cost, so there are some heuristic decisions involved.

To clarify, by composition of translations, I mean to compose the 'TLMap' type, producing a new TLMap. 

        (mv:MVA, ln:LNA) fby (mv:MVB, ln:LNB) => (mv:MVAB, ln:LNAB)

The 'mv' and 'ln' components of a TLMap can be composed independently. The basic approach for 'A fby B' is to extend implicit suffixes on both sides of A so the output for A matches input for B, then apply B's rules to the RHS of of the modified A, using the longest matching prefix in B in each case.

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start

        # note that we also extend suffixes of the implicit "" => "" rule
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes 
            fby { "f" => "xy", "foo" => "z"}     

        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # end

A suffix cannot be extended if the longer prefix would be matched by another rule. Conversely, we can simplify a translation map by erasing implied rewrites, e.g. we don't need `"fad" => "bed"` if our next longest matching prefix rule is `"fa" => "be"`. Similarly, we don't need `"xyz" => NULL` if we also have `"xy" => NULL`. Adding suffixes and simplifying are identity transforms on the rewrite rules.

Conveniently, the NULL and WARN cases in 'mv' don't need any special handling because they cannot be matched by any valid prefix. Thus, we won't extend any suffixes, and we'll simply preserve remove rules from MVA, and potentially add new remove rules based on what MVB does match in the expanded MVA. The only thing we might do is simplify remove rules that are implied by a shorter rule in the composition.

Due to the structure of NSDef, this is the only composition rule we need for TLMap. The translation closer to a leaf node is followed by a translation closer to the root node. 

### Alt: List of Translations

Instead of properly composing TLMap, we could introduce an intermediate representation that maintains a lists of rewrite rules.

        type TLMapExt = ( lns:List of (Map of Prefix to Prefix)
                        , mvs:List of (Map of Prefix to (Prefix | NULL | WARN))
                        )

With this, we can still walk each Expr only once to perform the rewrite, it's only the individual name and localization elements in the abstract assembly that must iterate through the list of link rewrites. Localizations could also maintain the list, with runtime support. This should perform adequately if our transforms aren't too deep. That said, I don't believe that proper composition is so complicated or expensive as to make this worthwhile.

## Evaluation Strategy

In addition to explicit laziness via 'load', we might assume NS has implicit laziness via thunks in the runtime data representation. Our default evaluation strategy should be lazy, avoiding unnecessary evaluation of load or observation of thunks. This is easily augmented with parallelism, operating on full sets at a time.

        type EvalContext =
            ( have:(Map of Name to Expr)            # extracted definitions
            , want:(Map of Name to (Set of NS))     # needed defs and pending tasks
            , todo:(Set of NS)                      # NS elements to be processed 
            , fail:(Set of NS)                      # failed tasks for diagnosis
            , drop:(Map of Name to (Set of Expr))   # conflicting defs for diagnosis
            )

In each step, we can scan 'todo' for tasks that might contribute to what we 'want'. The primary basis for laziness is to filter 'mx' and 'ld' tasks: a name is a potential output of the NS if it doesn't have a prefix in the LHS of 'mv', or if it has a prefix in the RHS of 'mv'. For indexed performance, we might first extend TL with an index - a trie containing only the prefixes from RHS of 'mv'.

To process 'df', we add the names to 'have' if possible, and remove those names from 'want', adding pending tasks back to 'todo'. If there are name conflicts, we instead add the definition to 'drop' to support debugging, and we report the conflict at most once per name.

To process 'mx', we extract at least one NS, apply the TL, and expand translated NS and remaining mx (if any) into 'todo'. To apply TL to another 'mx', we compose TL. To apply TL to 'ld', we compose TL and also apply the 'ln' translation to the Set of Expr. To apply TL to 'df', we apply the 'ln' rules to the Exprs and 'mv' rules to the names. The latter may result in name conflicts if 'mv' merges two prefixes.

To process 'ld', we try to evaluate an Expr to a `(Set of NS, Set of Expr)` pair, then produce an `mx:(TL, Set of NS), ld:(TL, Set of Expr)` pair with the TL from 'ld'. But we must also handle evaluation failures. If evaluation fails due to a missing definition, we can add `ld:[TL, Expr]` under 'want' for the missing name, otherwise report the problem and add it to 'fail' to support further debugging. Most complexity is related to developing the evaluator for Exprs in context of a partial namespace.

## Design Patterns

### Forbidden Names

Assume we forbid DEL (0x7F) for use in names. Then, by including DEL in rhs of a translation, we can guard against accidental use of the prefix within a namespace component. We can report if we accidentally do define names containing DEL, and easily verify that all translations are DEL-preserving (DEL in lhs implies DEL in rhs). 

This would serve a symmetric role of 'move to WARN' controlling introduction of names. For example, if we want to insist a given subcomponent only uses names prefixed 'foo' or 'bar' we might use a link rule `{ "" => DEL, DEL => DEL, "foo." => "foo.", "bar." => "bar." }`. If we want to say the component *does not* use 'foo', the rule instead is `{ "foo." => DEL+"foo." }`. (We don't need shadowing since these names shouldn't be defined.)

### Private Names

Similar to DEL, we can reserve '~' (0x7E) for use in private names. We could warn if '~' appears in initial 'df' or load expressions, require it is only introduced in the rhs of a translation. We can also verify that translations are privacy preserving ('~' in lhs implies '~' in rhs). Further, we must also reject names containing '~' even before they are translated via *Localization*. 

Privacy would be introduced by rename, symmetric move and link, based on access control expressions. For example, we might compile `export foo, bar, qux as q` (accept list) to rename `{ "" => "~", "foo." => "foo.", "bar." => "bar.", "qux." => "q." }`. Or we could compile `hide foo, bar` (deny list) to rename `{ "foo." => "~foo.", "bar." => "~bar.", "~foo." => "~foo.^", "~bar." => "~bar.^" }`, including operations to shadowing the prior `~foo.`.

### Global Names

We can simulate a global namespace by automatically forwarding names into hierarchical components by default. For example, a language might implicitly translate `{ "" => "foo.", "$" => "$", "%" => "%" }` for hierarchical component 'foo'. This would effectively treat `$*` and `%*` as global namespaces. We use `%*` for primitive AST constructors, and it might be read-only (via move `{ "%" => WARN }`), whereas `$*` could be the user writable global namespace.

Usefully, because this namespace is still subject to translations, we can easily translate components to resolve name conflicts in the global namespace, or sandbox the global namespace for a subset of components. We only need some syntax to influence the default translation.

A weakness of global names is that it hinders lazy evaluation. It isn't obvious which constructors might contribute to any specific global name. Without hints, we'll be blindly searching until we find the right component. To mitigate this, users could introduce manual hints where they notice performance issues, or a compiler could record some metadata to support search during incremental compilation.

### Namespace as Function or Macro

With a few simple conventions, a namespace is easily be interpreted as a function. For example, the client overrides `args.*` then evaluates names in `result.*`, and ignores anything else as internal to the function. Further, this extends to fexprs or macro-like behavior because the arguments is easily presented as an expression in another namespace, not necessarily raw data.

### Load as a Process

We can understand 'load' as a process that observes some definitions via evaluation within a load expression, writes some definitions, then continues. The load process can wait indefinitely if it observes are undefined. With a clever encoding, a set of load processes can interact and communicate through the namespace, taking turns between writing and observing definitions.

The semantics for load processes in this view are similar to [futures and promises](https://en.wikipedia.org/wiki/Futures_and_promises) or concurrency with [session types](https://en.wikipedia.org/wiki/Session_type). There are some limitations because we cannot 'unify' names or prefixes, and we cannot directly observe definitions or undefined things. Nonetheless, many interaction patterns can be modeled.

There is some risk of producing a very large namespace of one-off definitions. This namespace model isn't designed to support garbage collection: it's difficult to predict what might be referenced in the output of a load expression. If necessary, a compiler can heuristically garbage collect private names, leveraging bloom filters to detect potential issues. Discriminating false positives, or continuing after a premature GC, would require recomputing. Alternatively, we could add some conventions to ensure certain names are only used once, letting those names be used for channels.

*Note:* In context of *Namespace as a Function*, use of *Load as a Process* allows for partial outputs based on partial inputs. Some feedback is possible, but only if the caller wires things up such that some inputs are dependent on partial outputs.

### Aggregate Definitions

Our namespace model restricts us to a single definition per name. However, in context of multimethods, constraint systems, or constructing tables, it is convenient if multiple namespace components contribute to a shared definition. 

We can leverage overrides in this role, manually composing definitions from various components. But it might prove more convenient to have language support for composing definitions from multiple components. Perhaps this could build upon *Load as a Process*, threading channels or possibly staged namespace-layer algebraic effects.

### Abstraction of Call Graphs

For first-order procedural programming, we can hard-code names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. Despite being 'hard coded', the namespace translations make this elastic, rewriting names within the assembly. We can support hierarchical components, overrides, and shadowing, and even limited abstraction by intentionally leaving names for override.

However, encoding many small variations on a large call graph is very expensive in terms of memory overhead and rework by a late-stage compiler. To solve this, it's best to support some abstraction of calls in the program layer. It is not difficult to also support `(%call CallableExpr ArgExpr)`. Although my vision for glas systems eschews first-class functions (due to awkward interaction with live coding, orthogonal persistence, remote procedure calls, etc.), we could easily restrict this with static parameters, or use a special variation of 'call' for algebraic effects.

Use of *Localizations* from abstract assembly can let us safely convert data to an open set of names. Instead of calling names directly or building abstract callables from a closed set, we might favor indirect calls through a localization. Intriguingly, we could also build a namespace within a method, and bind a localization to a component like `ext.*` within that namespace.

