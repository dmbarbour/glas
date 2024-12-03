# Glas Namespaces

For glas configurations and applications, we need modular namespaces with support for overrides, name shadowing, and robust access control. Other useful features include lazy extraction of needed definitions and staged metaprogramming within the namespace. Namespaces can express recursive definitions, though the extent to which recursion is permitted may depend on the program model.

This document proposes a simple namespace model to support these goals, based primarily on prefix-oriented translations of names. I assume definitions are expressed using an [abstract assembly](AbstractAssembly.md), but we could substitute any representation that lets us precisely recognize and rewrite names. Abstract assembly can also capture future translations into a 'localization', which is valuable in context of 'eval'.

Some important features of this namespace model include monotonicity, idempotence, and the ability to compose translations into a single pass. I'm still contemplating built-in support for lazy loading and macros versus pushing those features to the compilation model. There are some potential errors

## Proposed Model

We'll represent the namespace using plain old glas data, with support for deferred computations. This doesn't structurally prevent risk of name conflicts, type errors, staging errors, and divergence. But we'll generally evaluate NS at compile time, report warnings through logs, and also continue evaluation in presence of minor errors.

        type NS
            = df:(Map of Name to (Set of Expr))             # define
            | ap:(NS, List of MX)                           # apply
            | lz:(TL, Set of NS)                            # lazy

        type MX 
            = mx:(TL, Set of NS)                            # mix or patch
            | at:(LN, List of MX)                           # translate MX

        type TL = (mv?MV, ln?LN)                            # translate
        type MV = Map of Prefix to (Prefix | DROP | WARN)   # move map
        type LN = Map of Prefix to (Prefix | WARN)          # link map

        type Set = List                     # ignore order and dups
        type Name = Symbol                  # assumed prefix unique  
        type Prefix = Symbol                # empty up to full name
        type Symbol = Bitstring             # byte aligned, no NULL
        type DROP = NULL DEL (0x00 0x7F)    # remove the definition
        type WARN = NULL 'w' (0x00 0x77)    # remove with a warning
        type Map = Trie                     # a NULL byte separator
        type Expr = AST                     # via abstract assembly

Namespace constructors (NS):

* *define (df)* - Provide set of definitions as dictionary. Canonical form of evaluated NS. We can represent a set of ambiguous definitions, but in most contexts this results in ambiguity errors or warnings.
* *apply (ap)* - Express namespace as a series of simple patches on another namespace.
* *lazy (lz)* - Express a namespace as a deferred translation and union of NS fragments. 

Patch constructors (MX):

* *mix (mx)* - This is the basic patch. It first applies a translation to the target namespace, e.g. to delete or override names, then inserts new definitions via NS.  
* *at (at)* - Translates a sequence of patches to apply to different locations within the namespace. This supports limited abstraction of patches.

Description of namespace translations (TL):

* *move (mv)* - rewrites keys of 'df' based on longest matching prefix. Based on special targets, we can also remove the name with or without warnings.
* *link (ln)* - rewrites names and localizations within Exprs based on longest matching prefix. Based on special targets, we can also raise a warning if a name or prefix is unexpectedly in use, and replace it by an invalid name.

## Prefix Unique Names

Prefix-based rewrites make it difficult to work with "pea" without also touching "pear" and "pearl". To resolve this problem, we use a simple name mangling scheme: add a reserved suffix to each name that guarantees names are not prefixes of other names. I propose ".!" for this role. We can easily rename or remove "pea.!" without touching "pear.!" and "pearl.!". Additionally, we can raise a warning if we discover any names are not prefix-unique when constructing the namespace.

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

In general, we'll want lazy evaluation of the NS because an NS may have hidden thunks at the data representation layer. This might further extend to 'load' expressions (see *Namespace Macros*). Laziness can be supported by filtering evaluation based on 'mv' translations in 'lz'. We can also rewrite 'ap' almost immediately to 'lz', and use thunks for composition or translation of TL.

Also, assuming ambiguous definitions are invalid, it is convenient to select one definition and stick with it as if it were the only definition. Ambiguity becomes a race condition, reported through warnings or error messages. To avoid redundant messages and simplify diagnosis, we can track the ambiguous definitions.

        type EvalContext =
            ( have:(Map of Name to Expr)            # selected definitions only
            , want:(Map of Name to (Set of Task))   # needed defs and pending tasks
            , todo:(Set of NS)                      # NS elements to be processed 
            , drop:(Map of Name to (Set of Expr))   # ambiguous defs for diagnosis
            )

In this case Task is from the context. We might capture tasks that cannot be processed immediately due to at least one missing definition. Or we could ignore Task and use an empty set (unit). .

Based on the wanted names, we select a potential candidate from the 'todo' list. Any invalid candidate gets pushed to the rear of the 'todo' choices, while other candidates are partially evaluated, often expanding into a larger set of 'todos'. In case of 'df', we immediately add items to 'have' (or 'drop' if we have a different definition). When we finally find the definition, we remove it from the 'want' list.

We'll process 'ap' and patches by rewriting. The list of patch operations will be processed eagerly, but 'mx' rewrites to a lazy operation. Use of 'at' to translate patches is a relatively sophisticated operation.

        ap:(NS, ()) => NS                           # ap done
        ap:(NS, (mx:(TL, Adds), Ops)) =>            # lazily apply mx
            ap:(lz:((), (lz:[TL, NS], Adds)), Ops)
        at:(LN, ()) =>                              # at done (empty seq)
        at:(LN, (mx:(TL, Adds), Ops)) =>            # translate patches
            mx:[TL at LN, lz:((mv:LN, ln:LN), Adds)], at:(LN, Ops)
        at:(LN, (at:(LN', Ops'), Ops)) =>           # compose patch translations
            at:(LN' fby LN, Ops'), at:(LN, Ops)

To apply a TL to another 'lz', we can use fby composition. Application of TL to 'df', and composition of TL, may also benefit from implicit laziness via thunks.

## Design Patterns

### Private Names

It can be useful to mark some names private, only for internal use within a subprogram. It is infeasible to strongly protect privacy in context of user-defined syntax, namespace macros, and staged metaprogramming with programs as values. However, even weak protections resist against accidental violations. We can weakly enforce privacy with a few conventions:

* Reserve using '~' (0x7E) anywhere in name to mark private names.
* Warn when definitions contain private names before translations.
* Insist translations preserve privacy: '~' in lhs implies in rhs.
* Reject '~' in lhs for Data to Name conversions via Localization.
* Provide syntactic support for privacy, including 'export' lists.

An export list such as `export foo, bar, qux as q` might compile to a rename translation `{ "" => "~", "foo." => "foo.", "bar." => "bar.", "qux." => "q.", "%" => "%" }`. In this case, '%' is preserved by default as a global namespace.

### Global Names

We can simulate a global namespace by automatically forwarding names into hierarchical components by default. For example, a language might implicitly translate `{ "g." => "g.", "%" => "%", "" => "foo." }`, treating `g.*` and `%*` as global namespaces for hierarchical component `foo.*`. The `%*` is generally for primitive AST constructors, and might be read-only via 'move' to NULL. But we could permit introducing global names under `g.*` or some other prefix. 

Usefully, because this isn't a true global space, it is subject to further translations. We could resolve conflicts by renaming globals for certain subprograms. We could also sandbox or 'chroot' the globals used in subprograms, e.g. routing `{ "g." => "g.foo." }`.

I'm not convinced that `g.*` is a good prefix for globals, however. A tempting approach is to simply use `%` as the only global namespace, permit writes in a subregion of `%` (e.g. via move `{ "%" => NULL, "%." => "%." }` at toplevel), and require keywords when defining or referencing globals (i.e. `global foo =` versus `foo =`).

### Shared Libraries

Large namespaces will often contain many redundant definitions as we compose variations of similar components. An optimizer can mitigate this with a compression pass, recognizing and rewriting common definitions or AST fragments to a content-addressed namespace. However, for better compile-time performance, it is convenient to keep the namespace small to start with. 

It is possible to manually share common utility code between components through a global namespace. This can be supported with manual abstraction of the components (manually injecting the shared code), or by each component redundantly writing into the global namespace, relying on unification of redundant definitions. However, abstract is awkward in many cases, and reundant writes can still be very expensive due to overhead of computing, translating, and comparing each definition.

To avoid the overheads for redundant writes, we must efficiently recognize when two namespace values will be the same despite some variation in the outermost translation. This requires a non-trivial evaluation and memoization strategy, and annotations to guide its use. We might leverage annotations in 'ld' expressions for this role.

### Aggregate Definitions

Our namespace model restricts us to a single definition per name. However, in context of multimethods, constraint systems, or constructing tables, it is convenient if multiple namespace components contribute to a shared definition. 

We can leverage overrides in this role, manually composing definitions from various components. Other than overrides, our main option is to support composition and integration in a higher model, i.e. making some assumptions about which names are defined, or using annotations in the program value layer to indicate which definitions are implemented.

### Algebraic Effects and Abstraction of Call Graphs

For first-order procedural programming, we hard-code names in the call-graph between functions. Our abstract assembly would contain structures like `(%call Name ArgExpr)`. Despite being 'hard coded', the namespace translations make this elastic, rewriting names within the assembly. We can support hierarchical components, overrides, and shadowing, and even limited abstraction by intentionally leaving names for override.

However, encoding many small variations on a large call graph at the namespace level is very expensive in terms of memory overhead and rework by an optimizer. To solve this, we should support abstraction of calls at the program layer. For example, we could support algebraic effects where a program introduces an effects handler that may be invoked from a subprogram. With careful design, this effects handler may still be 'static' for inline optimizations and partial evaluation, similar to namespace overrides.

By leveraging *Localizations* (from [abstract assembly](AbstractAssembly.md)), we can also interpret data to names in the host namespace without loss of namespace layer locality or security properties. It is feasible to leverage layers of localizations to model overlays on the call graph, where most names can be overridden in a given call context.

## Tentative Extensions

### Namespace Macros

It is feasible to extend the namespace type with 'load' expressions to support namespace macros, something like `ld:(TL, Expr)` where the TL is applied to the result, and the Expr is translated based on 'ln' rules. Iterative compilation can be based on non-deterministic choice within Expr.

A significant weakness of this design is that it separates evaluation of Expr from any particular context of algebraic effects. It might be better to push this sort of 'eval' logic into the compiler to ensure consistent context. In the latter case, a compiler would recursively compile files or evaluate expressions within the current context of algebraic effects, relying on non-deterministic choice for iterative compilation (e.g. to wait on dependencies for 'eval', or wait on a file).

### Mapping or Wrapping Definitions

It is feasible to add a translation step to modify definitions, e.g. to wrap them with a series of constructors. This might be expressed as an extra `ap:[F, G, H]` translation, such that our translated definition is `F(G(H(Def)))`, and F, G, H are expressions (names or partial constructors) subject to link (ln) translations.

This would simplify sandboxing, translation, and reflection on definitions. However, it's semantically awkward, and I'm not convinced that the use case is strong enough. A viable alternative is to design the AST and intermediate language such that it's friendly to sandboxing via integration of hooks or careful use of context (e.g. algebraic effects) instead of ad-hoc wrappers. This allows code to control its own integration more precisely.
