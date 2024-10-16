# Glas Namespaces

For glas configurations and applications, we need modular namespaces with support for overrides, name shadowing, and robust access control. Other useful features include lazy extraction of needed definitions and staged metaprogramming within the namespace. Namespaces can express recursive definitions, but whether recursion is permitted also depends on the program model.

This document proposes a simple namespace model to support these goals, based primarily on prefix-oriented translations of names. I assume definitions and lazy load expressions are both represented using an [abstract assembly](AbstractAssembly.md). This enables us to precisely recognize and rewrite names within definitions, and also capture translations for localization or redirects. We can reduce translations to a single lazy pass to control overhead for rewrites.

## Proposed Model

We'll represent the namespace using plain old glas data, with support for deferred computations. This doesn't structurally prevent risk of name conflicts, type errors, staging errors, and divergence. But we'll generally evaluate NS at compile time, report warnings through logs, and also continue evaluation in presence of minor errors.

        type NS
            = df:(Map of Name to Expr)                      # define
            | ap:(NS, List of MX)                           # apply
            | lz:(TL, Set of NS)                            # lazy
            | ld:(TL, Set of Expr)                          # load

        type MX 
            = mx:(TL, Set of NS)                            # mix or patch
            | at:(LN, List of MX)                           # translate MX

        type TL = (mv?MV, ln?LN)                            # translate
        type MV = Map of Prefix to (Prefix | DROP)          # move map
        type LN = Map of Prefix to Prefix                   # link map

        type Set = List                     # ignore order and dups
        type Name = Symbol                  # assumed prefix unique  
        type Prefix = Symbol                # empty up to full name
        type Symbol = Bitstring             # byte aligned, no NULL
        type DROP = NULL (0x00)             # remove the definition
        type Map = Trie                     # a NULL byte separator
        type Expr = AST                     # via abstract assembly

Namespace constructors (NS):

* *define (df)* - Provide set of definitions as dictionary. Canonical form of evaluated NS.
* *apply (ap)* - Express namespace as a series of patches on another namespace. This is convenient when abstracting patches as operations on a namespace.
* *lazy (lz)* - Express a namespace as a lazy translation on a lazy union of namespaces. This is useful to defer computation, and as an intermediate state in namespace evaluation.
* *load (ld)* - Express namespace as a macro to be expanded and integrated. Each Expr should evaluate *within the partial namespace* to a `(Set of NS, Set of Expr)` pair. The output is the set of NS, while the continuation is the set of Expr. Essentially evaluates to `lz:(TL, Set of NS), ld:(TL, Set of Expr)`. 

Patch constructors (MX):

* *mix (mx)* - This is the basic concrete patch. It first applies a translation to the target namespace, e.g. to delete or override names, then inserts new definitions. 
* *at (at)* - Translate a patch to apply to modified representation of the namespace. This provides a basis for abstraction and reuse of patches, e.g. applying a patch to an abstract hierarchical component, or applying a patch as a function on a namespace computing from abstract 'lhs.' to 'rhs.'. 

Description of namespace translations (TL):

* *move (mv)* - rewrites keys of 'df' based on longest matching prefix. Can also remove with special target NULL.
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

To clarify, by composition of translations, I mean to compose the 'TL' type, producing a new TL. 

        (mv:MVA, ln:LNA) fby (mv:MVB, ln:LNB) => (mv:MVAB, ln:LNAB)

The 'mv' and 'ln' components of a TL can be composed independently. The basic approach for 'A fby B' is to extend implicit suffixes on both sides of A so the output for A matches input for B, then apply B's rules to the RHS of of the modified A, using the longest matching prefix in B in each case.

        { "bar" => "fo" } fby { "f" => "xy", "foo" => "z"  }                    # start

        # note that we also extend suffixes of the implicit "" => "" rule
        { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }          # extend suffixes 
            fby { "f" => "xy", "foo" => "z"}     

        { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }            # end

A suffix cannot be extended if the longer prefix would be matched by another rule. Conversely, we can simplify a translation map by erasing implied rewrites, e.g. we don't need `"fad" => "bed"` if our next longest matching prefix rule is `"fa" => "be"`. Similarly, we don't need `"xyz" => NULL` if we also have `"xy" => NULL`. Adding suffixes and simplifying are identity transforms on the rewrite rules.

Conveniently, the NULL and WARN cases in 'mv' don't need any special handling because they cannot be matched by any valid prefix. Thus, we won't extend any suffixes, and we'll simply preserve remove rules from MVA, and potentially add new remove rules based on what MVB does match in the expanded MVA. The only thing we might do is simplify remove rules that are implied by a shorter rule in the composition.

Due to the structure of NSDef, this is the only composition rule we need for TL. The translation closer to a leaf node is followed by a translation closer to the root node. 

## Translation of Patches

The 'at' constructor for patches will modify where a patch is applied. This will compose 'fby' with contained 'at' operations, but handlng 'mx' is more interesting. We must rewrite the patch to apply to a translated view of the namespace. This does operate as a rename for definitions introduced within the patch, but it doesn't rewrite the tacit namespace. Instead, we'll rewrite the TL element of 'mx', modifying the lhs and rhs of the move and link maps. 

For example, if our 'at' translation is `{ "" => "foo.", "g." => "g." }`, then a move from `{ "a." => "b.", "c." => "g.d.", "g.e." => "f." }` will become a move `{ "foo.a." => "foo.b.", "foo.c." => "g.d.", "g.e." => "foo.f." }`. Removes need special recognition when rewriting the rhs, and as usually we'll want to un-simplify to expand prefixes and suffixes of rules to match all possible rewrites.

## Evaluation Strategy

In addition to explicit laziness via 'load', we might assume NS has implicit laziness via thunks in the runtime data representation. Our default evaluation strategy should be lazy, avoiding unnecessary evaluation of load or observation of thunks. This is easily augmented with parallelism, operating on full sets at a time.

        type EvalContext =
            ( have:(Map of Name to Expr)            # extracted definitions
            , want:(Map of Name to (Set of NS))     # needed defs and pending tasks
            , todo:(Set of NS)                      # NS elements to be processed 
            , fail:(Set of NS)                      # failed tasks for diagnosis
            , drop:(Map of Name to (Set of Expr))   # conflicting defs for diagnosis
            )

When extracting some definitions, if they aren't already defined we add them to 'want'. In each evaluation step, we can scan 'todo' for tasks that might contribute to what we 'want'. The primary basis for laziness is to filter 'lz' and 'ld' tasks based on output prefixes from 'mv' translations. It is feasible to index and cache the output prefixes for each 'mv' map, perhaps extending the TL type. 

To process 'df', we add the names to 'have' if they are previously undefined, otherwise add to 'drop' and log a warning. When we add a name to 'have' we can remove the same name from 'want' and return any pending tasks to the 'todo' set.

We'll process 'ap' and patches by rewriting. The list of patch operations will be processed eagerly, but 'mx' rewrites to a lazy operation. Use of 'at' to translate patches is a relatively sophisticated operation. 

        ap:(NS, ()) => NS                           # ap done
        ap:(NS, (mx:(TL, Adds), Ops)) =>            # lazily apply mx
            ap:(lz:((), (lz:[TL, NS], Adds)), Ops)
        at:(LN, ()) =>                              # at done (empty seq)
        at:(LN, (mx:(TL, Adds), Ops)) =>            # translate patches
            mx:[TL at LN, lz:((mv:LN, ln:LN), Adds)], at:(LN, Ops)
        at:(LN, (at:(LN', Ops'), Ops)) =>           # compose patch translations
            at:(LN' fby LN, Ops'), at:(LN, Ops)

To process 'lz', we extract at each NS, apply the TL, then return it to the todo pool. To apply a TL to another 'lz', we can use fby composition. To apply TL to 'ld', we fby compose TL and also apply the 'ln' translation to the Set of Expr. To apply TL to 'df' is essentially how TL was defined to start with. There is some risk of name collisions by 'mv' moving two names together, but this is resolved non-deterministically with a warning.

To process `ld:(TL, Set of Expr)`, we try to evaluate each Expr to a `(Set of NS, Set of Expr)` pair. For successful evaluations, we produce an `lz:(TL, Set of NS), ld:(TL, Set of Expr)` pairs, using TL from 'ld'. On failure, we diagnose the cause. If the failure is due to a missing definition, we add the name and `ld:[TL, Expr]` to 'want'. For any non-recoverable cause, we instead add this to 'fail'.

## Design Patterns

### Invalid Names

I propose to forbid byte DEL (0x7F) in names. This allows us to introduce DEL in translations to indicate that certain names or prefixes shouldn't be used, e.g. `{ "" => DEL, "foo." => "foo." }` would indicate that a given namespace component should only define or use `foo.*` names (depending on move or link rule), while `{ "foo." => DEL+"foo." }` would indicate that a namespace component must not define or use `foo.*`. If there are any issues, we can render the translated name as part of the warning.

We can also check that all translations are DEL preserving, i.e. such that DEL in lhs implies DEL in rhs.

### Private Names

Similar to invalid names, I propose use of '~' (0x7E) for private names. To guard against most accidental privacy violations, we can raise a warning if '~' appears in initial definitions or link expressions before translation or if translations do not preserve '~'. However, in context of user-defined syntax, it is possible to bypass this privacy protection; it only resists accidents. More robust protection would be based on using translations to control where names are used and introduced. 

Anyhow, with this convention we might compile `export foo, bar, qux as q` to rename `{ "" => "~", "foo." => "foo.", "bar." => "bar.", "qux." => "q." }`. Or we could compile `hide foo, bar` to rename `{ "foo." => "~foo.", "bar." => "~bar.", "~foo." => "~foo.^", "~bar." => "~bar.^" }`, including operations to shadowing the prior `~foo.`.

### Global Names

We can simulate a global namespace by automatically forwarding names into hierarchical components by default. For example, a language might implicitly translate `{ "g." => "g.", "%" => "%", "" => "foo." }`, treating `g.*` and `%*` as global namespaces for hierarchical component `foo.*`. The `%*` is generally for primitive AST constructors, and is read-only, thus we might also 'move' introduced `%*` to DEL. But we could permit introducing names under `g.*`.

Usefully, because this isn't a true global space, it is subject to further translations. We could resolve conflicts by renaming globals for certain subprograms. We could also sandbox or 'chroot' the globals used in subprograms, e.g. routing `{ "g." => "g.foo." }`.

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

### Algebraic Effects and Abstraction of Call Graphs

For first-order procedural programming, we hard-code names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. Despite being 'hard coded', the namespace translations make this elastic, rewriting names within the assembly. We can support hierarchical components, overrides, and shadowing, and even limited abstraction by intentionally leaving names for override.

However, encoding many small variations on a large call graph at the namespace level is very expensive in terms of memory overhead and rework by an optimizer. To solve this, we should support abstraction of calls at the program layer. For example, we could support algebraic effects where a program introduces an effects handler that may be invoked from a subprogram. With careful design, this effects handler may still be 'static' for inline optimizations and partial evaluation, similar to namespace overrides.

By leveraging *Localizations* (from [abstract assembly](AbstractAssembly.md)), we can also interpret data to names in the host namespace without loss of namespace layer locality or security properties. It is feasible to leverage layers of localizations to model overlays on the call graph, where most names can be overridden in a given call context.

