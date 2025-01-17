# Glas Namespaces

For glas configurations and applications, I want a deterministic, modular, simple, scalable, extensible namespace that supports namespace macros, late binding, lazy loading, incremental computing, and robust access control. Definitions may be mutually recursive in general, though static analysis may reject recursion in specific cases. I assume definitions and macros are represented using [abstract assembly](AbstractAssembly.md). 

## Representation

A namespace may define macros and be expressed in terms of those same macros. It is most consistent and convenient to represent the complete namespace as one big macro expression. That is, at the toplevel a namespace is expressed using abstract assembly, with the system providing primitive AST constructors such as `(%ns.union NS1 NS2 ...)`.

The namespace expression may depend upon terms that it defines, relying on flexible order of evaluation to resolve missing definitions. In our example, NS2 might define 'foo' then NS1 might call 'foo'. With some careful design, it is feasible to represent negotiations and handshake protocols, modular components collaborating to define a namespace. With lazy evaluation, we might stop after evaluating NS2 if we have all the definitions we need.

The namespace may support limited effects such as loading files, DVCS resources, perhaps HTTP GET. In general, these effects should be safe and cacheable. Lazy loading is then based on lazy evaluation of the namespace macro expression.

## Failure Modes

It's useful to ask "what could go wrong?" non-ironically. 

### Ambiguous Definitions

Use of `(%ns.union NS1 NS2 ...)` implies the idempotence and commutativity of sets. However, if a name is assigned distinct definitions by NS1 and NS2, it isn't immediately clear how this should be resolved. Picking one arbitrarily is non-deterministic. Selecting one based on relative order is non-commutative. Combining definitions would contradict lazy evaluation.

I propose to resolve this based on order, then to also raise a warning where ambiguity is detected. We might introduce a 'debug' evaluation mode that aggressively searches for ambiguities. We should encourage programmers to make their intentions explicit, e.g. by renaming or removing a conflicting definition, or favoring `%ns.seq` instead of union.

### Divergence

Some namespace expressions may fail to evaluate due to missing definitions or quota restrictions. The expression *might* provide a higher-priority version of a definition that we want, but we don't know because could not evaluate. When this happens, depending on context, the system can halt on error or raise a few warnings then continue, best-effort, with what might be a partial namespace and lower-priority definitions.

Another failure mode is that we're simply missing some definitions we need to proceed with evaluation of a namespace expression. In this case, we could emit a warning then continue evaluating a best-effort without any output from that expression. This introduces a risk that we'll use and output lower-priority definitions. 

### Dependency Cycles

In a dependency cycle, an NS expression depends (oft indirectly) on some definition it produces. In practice, a dependency cycle is distinguished from divergence only when a lower-priority definition breaks the cycle, and thus we discover the higher-priority definition after the lower-priority version is in use.

Proposed resolution: If the higher-priority version is identical to the lower-priority, there is no conflict. Otherwise, we report an error. This is simple to check.

In special cases, we might want to 'bootstrap' some definitions, reach a fixpoint without requiring the lower-priority definition is equivalent to the higher-priority. For this I propose `%ns.fix` or similar, with the user indicating a maximum number of cycles and substituting some definitions for just the first cycle. Making it explicit would help keep it simple.

## Behavior

### Conditional Definitions

We can support a simple form of conditional expressions at the namespace layer, i.e. if-then-else where the condition is independent of definitions produced by the 'then' or 'else' expressions. However, we cannot support 'ifdef' or non-monotonic observations.

### Constraint Namespaces? No.

If we relax the dependency constraints on conditional expressions, we'll effectively represent a constraint system and require a constraint solver. We can support 'ifdef' using the same solvers, akin to [answer set programming](https://en.wikipedia.org/wiki/Answer_set_programming). These features support hard constraint systems.

Soft constraint systems might be expressed by adding a notion of weighted choices then searching for a 'solution' to the namespace with the lowest total weight. In the general case, weights may depend on definitions introduced, so this might require hill climbing algorithms to find answers.

These features would make it difficult to support laziness or ensure determinism. I'd prefer to focus on a simpler namespace model for now, but it might be worth returning to these ideas in some other context.

#




evaluation of the macro should be deferred until dependencies are computed and we need the output. There is a possibility to express a dependency cycle, but this is relatively easy to detect and debug. For efficient lazy loading, we can use translations to scope generated definitions.  

Order of evaluation for namespace macros is flexible, independent of order of expressions within a file and decomposition into modules. However, in case a name is defined more than once, we must deterministically select one definition based on priority. To support flexibe priority, namespace macros could emit monotonic 'weights' to guide search, or fall back on order of expression where weights are equal. 

Evaluation of namespace macros may use limited effects, e.g. for loading files or DVCS resources. This allows us to align lazy loading with lazy evaluation. However, these effects should be safe and cacheable, like HTTP GET.



## Namespaces As Macros

I propose to represent a namespace as one big macro expression in abstract assembly. This would initially assume several primitive AST constructors, conventionally prefixed with `%`. This should include a union constructor where definitions introduced by one component namespace may be used in construction of another.

This allows the namespace to define reusable components, perhaps abstracted via parameters or algebraic effects. Avoiding a concrete intermediate representation is useful for lazy evaluation across multiple 'layers'. Later, we might also embed namespaces within procedures, e.g. as a basis for objects, and we'd still want lazy static evaluation of object methods.

## Translations, Localizations, and Laziness

When composing a namespace, we might translate the generated definitions:

* We can move or remove definitions generated by that component.
* We can rewrite names used within generated definitions.

It may also prove convenient to support translation of inline abstract assembly fragments as though translating a definition.



When expressing a namespace in terms of namespace components, there are several useful translations we might apply:

## Weighted Definitions and Resolving Ambiguity

Intriguingly, weights can be abstracted, e.g. expressing metadata like 'experimental' that get converted to weights through algebraic effects. This would allow a namespace to express a set of definitions that varies conditionally in limited ways based on context and user preferences.


## Rough Evaluation Strategy

A compiler might partially evaluate a namespace, generating a 'flat' dictionary based on translations.

, containing the required definitions. In addition to definitions, this dictionary might contain weights and a flag to indicate which definitions are used in 


Abstract assembly can be extended or constrained for a subprogram, and could easily support something akin to algebraic effects. The generated namespace representation remains abstract, which is convenient for laziness and flexible order of evaluation. There is no need to wrap or localize a data representation of a namespace when defining namespace components. It is also more convenient to define local namespaces within procedures as something like a lightweight object model.


allows us to fully l benefits

 complications of embedding abstract assembly for namespace macros within data AST expression, and allows us to robustly support some features similar to algebraic effects. 



* 
* Namespace fragments can be directly defined within the namespace.
* We can robustly embed namespace ASTs within a procedure.

convenient than a mixed representati


This design has several benefits. 

a union of two namespace expressions would allow one subset to depend on definitions provided by other subsets. 

 but may refer to macros defined within the namespace.

To avoid a dependency cycle, a toplevel module namespace should depend only on primitive AST constructors for namespaces, i.e. conventionally prefixed with `%` and provided by the runtime. However, in context of a namespace union constructors, one subset of definitions may depend on definitions provided by another subset.

In concrete terms, this means expressing the namespace using abstract assembly, and providing any primitive AST constructors for namespaces under the `%*` naming convention. The toplevel intermediate language representation of a module or program must depend on these primitive AST constructors for namespaces because use of anything else would form a dependency cycle. 

This design is convenient in many ways. 

This design is both more convenient and extensible than designing another AST type just for namespaces. It allows us to directly define namespace fragments within the namespace, and makes it relatively easy to integrate namespace fragments into procedures, too, e.g. for modeling 'objects' of some form.

Namespace expressions must be composable, such that a large namespace can be composed from smaller namespaces and evaluated incrementally. To support laziness, abstraction, and determinism without too much ordering, each component namespace might be logically translated (with an import/export list) and have some monotonic weight.

# Old Content

In a prior version, I didn't handle conflict very well due to working with 'Sets'. It was difficult to identify conflicts without evaluating the full namespace. By ordering things, we can resolve conflicts, and reported conflict may be deterministic based on which definitions we required. We can still evaluate many components in parallel.  

*Note:* I'm uncertain whether I'll use the full namespace type. The important take-aways seem to be support for composable prefix-based translations, restricting names a little (for prefix uniqueness, private names, invalid names), and the opportunity to capture translations via localizations.



An acceptable failure mode is a dependency cycle, where two macros each rely on definitions provided by the other macro - we can recognize these issues then resolve them manually.

## Dependency Cycles and Bootstrap

A dependency cycle exists when a namespace macro depends on the same definitions it outputs. Although easy to avoid locally, it is difficult to avoid dependency cycles in context of latent overrides. Nobody will grok the entire graph of namespace dependencies. 

In case of actual dependency cycles, we can only insist that programmers resolve them manually. Programmers can apply a number of ad-hoc strategies, e.g. staging the definitions more clearly, or restructuring the modules a little. In any case, the problematic behavior should be deterministic and very debuggable.

However, prioritized definitions introduce a special case: we might optimistically evaluate a macro using a lower-priority definition, but then discover the macro outputs a higher-priority definition for the same name. If the definitions are identical, there is obviously no conflict. Intriguingly, the definitions might instead be behaviorally equivalent, or only equivalent within the space observed. Thus, we might retry evaluation using the higher-priority definition, and check one final time for identical results. 

Note that we are not computing a fixpoint! Instead, we're attempting to verify an assumption that the difference in definitions was irrelevant to evaluation of the namespace. However, this effectively implements lightweight bootstrap, and programmers might rely on this lightweight bootstrap for manually resolving some dependency cycles.












We've reached a fixpoint. If the two aren't the same, we could try to re-evaluate the macro using the higher-priority definition, then test again whether a fixpoint is reached. If no fixpoint is achieved after a few rounds, we might report a warning to let the programmer know. 

Support for resolving fixpoints is essentially a bootstrap strategy for the namespace. The challenge is to ensure bootstrap is deterministic, i.e. independent of the 





. For example, we could make some assumptions about the definition that will be output, and check whether those assumptions are true.


because we don't know precisely which definitions a macro will output before it's evaluated. Instead, we only know scope and priority. In this case, 

 because we don't know exactly which definitions a macro will output before evaluation. We only have a rough idea based on scope and priority. In this case, we can optimistically evaluate the macros, assuming there will be no conflicts, then check our assumptions. It is feasible to heuristically attempt evaluation under a few different sets of assumptions to finally resolve the conflict.


In this case, we can optimistically evaluate the macros, then check our assumptions afterwards. In practice, there might be no conflict. 

Usefully, if a macro outputs *the same* definition we used optimistically - even if the version we used was from a lower-priority source - we

there is also no conflict.


 In this case, a macro may depend on definitions *potentially* generated by another macro, and vice versa.


One viabl


 then resolved manually.  

 isn't easy to avoid cycles

it can be difficult for a programmer to grok the entire graph of namespace dependencies when performing overrides. 

 cycles in context of overrides. 

 a cycle can be introduced by a downstream programmer. The question

 generated by the same namespace macro. 

evaluation of a namespace macro might depend on a definition also provided by that macro, perhaps indirectly through another ma

 or two or more macros might be mutually



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

## Alternatives

Instead of sets, we could support ordered namespaces with deterministic outcomes upon conflict. We might still report conflicts as errors, but this would ensure that conflicts don't result in a non-deterministic system. 

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
