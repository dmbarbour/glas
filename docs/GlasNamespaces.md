
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for extension and recursion. The model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. However, support for 'new' or first-class objects is not implied. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md) or a concrete variant, but it can be adapted to any type where names are easily rewritten.

## Proposed AST

        type NSOp 
              = mx:(List of NSOp)                   # mixin 
              | ns:NSOp                             # namespace
              | df:(Map of Name to Def)             # definitions
              | ln:(Map of Prefix to Prefix)        # link defs
              | mv:(Map of Prefix to Prefix)        # move defs
              | rm:(Map of Prefix to unit)          # remove defs
              | tl:(NSOp, Map of Prefix to Prefix)  # translate

        type Name = Binary                          # assumed prefix unique
        type Prefix = Binary                        # empty up to full Name
        type Map = specialized trie                 # essential! see below!
        type Opt T = List of T, max length one      # keeping it simple
        type Def = abstract assembly                # or similar

* mixin (mx) - apply a sequence of operations to the tacit namespace
* namespace (ns) - evaluate NSOp (usually mx) in context of empty namespace, evaluating to definitions (df). This could be evaluated eagerly at AST construction time, so this operation is mostly about deferring costs and lazy evaluation.
* definitions (df) - add definitions to tacit namespace. It's an error if a name has two different definitions, but it's okay to assign the same definition many times.
* link (ln) - modify names within definitions in tacit namespace, based on longest matching prefix  
* move (mv) - move definitions in tacit namespace, based on longest matching prefix
* remove (rm) - remove definitions in tacit namespace, essentially a move to `/dev/null`.
* translate (tl) - modify an NSOp (often mx) to operate on a different prefix. 

## Common Usage Patterns

* A 'rename' involves both 'mv' and 'ln' operations with the same Map. We'll almost never use 'ln' except as part of a full rename, but it's separated to simplify optimizations and rewrite rules.
* To 'override' `foo`, we rename prefix `foo^ => foo^^` to open space, move `foo => foo^` so we can access the prior (aka 'super') definition, then define `foo` with optional reference to `foo^`. Alternatively, we could remove then redefine `foo` if we don't need the old version.
* To 'shadow' `foo`, we rename prefix `foo^ => foo^^` to open space, *rename* `foo => foo^` so existing references to `foo` are preserved, then define `foo` with optional reference to `foo^`. 
* To model 'private' definitions, we prefix private definitions with '~', then we systematically rename '~' in context of inheritance. The syntax doesn't need to provide direct access to '~'.
* To model hierarchical composition, add a prefix to everything (e.g. via 'tl' of empty prefix) then provide missing dependencies via rename or delegation. This is object capability secure, i.e. the hierarchical component cannot access anything that is not provided to it.
* To treat mixins as functions, we can define the mixin against abstract 'components' such as 'arg' and 'result', then apply a translation map the mixin to its context. We might translate the empty prefix to a fresh scratch space to lock down what a mixin can touch.

## Specialized Map Type

I propose to encode the map as a trie, expanding the binary key to a prefix-unique trie key in a simple way:

        toTrieKey (Byte, Bytes) = 0b1:(toOctet Byte):(toTrieKey Bytes)
        toTrieKey ()            = 0b00

The `toOctet` method will expand small integers to eight bits with a zeroes prefix, e.g. byte 5 as an octet is `0b00000101`. I reserve the key terminating in `0b01` for future extensions, such as compression of repeating sequences in a name, or keeping some metadata within the tree. The value follows the trie key, similar to how we encode dictionaries in glas.  

## Prefix Unique Names

This AST assumes names are prefix unique, meaning no name is a prefix of another name. Prefix uniqueness is trivial to achieve in practice, e.g. the front-end compiler can add a suffix (such as the NULL character) to defined names

This assumption simplifies the namespace AST because the prefix-oriented operations can also be applied to specific names. We can detect prefix uniqueness violations when merging definitions (df), or when applying any prefix operation (ln, rm, mv, tl with prefix longer than a name).

## Unambiguous Definitions and Multiple Inheritance

To detect accidental name collisions in context of moves, renames, and multiple inheritance, we'll treat it as an ambiguity error when a name is assigned two or more different definitions. To override, the prior definition must first be explicitly moved or removed. To shadow a word, the prior definition must be renamed. 

However, it is unambiguous if the same definition is assigned to a name many times. And we can leverage redundant expression as a lightweight verification of interfaces, i.e. that multiple components sharing an interface have the same expectations based on matching documentation or type annotations.

*Note:* I think it's best if the programming language explicitly represents user expectations, i.e. whether we are introducing, overriding, or shadowing a definition.

## Computation

### Simplifying Move, Link, and Translate Maps

We can eliminate redundant rewrites. For example, if we have a rewrite `fa => ba` then we don't need another rewrite `fad => bad`. 

We can potentially recognize and eliminate redundant rewrites as a map is constructed, i.e. when adding a rule we can first check whether it's redundant with the next shortest prefix, then we can check and eliminate any redundant longer prefixes. That said, ignoring simplification is unlikely to significantly hurt performance.

More usefully, we can 'un'-simplify maps and introduce redundant rewrites as part of composing or translating a map. This is a useful way to understand compositions and translations.

### Followed By (fby) Composition of Move, Link, Translate Maps

The maps in 'mv', 'ln', and 'tl' represent an atomic sets of rewrites. This atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step to avoid name collisions. For every name, we find the longest matching prefix in the map then apply it. This does imply we cannot casually separate operations into smaller steps.

However, it is feasible to compose sequential rewrites and moves. For example, `{ bar => fo } fby { f => xy, foo => z }` can compose to `{ bar => xyo, baro => z, f => xy, foo => z }`. 

To implement this, we first extend `{ bar => fo }` with redundant rules such that the right-hand side contains all possible prefixes matched in `{ f => xy, foo => z }`: `{ bar => fo, baro => foo, f => f, foo => foo }`. Then we apply `{ f => xy, foo => z }`, resulting in `{ bar => xyo, baro => z, f => xy, foo => z }`.  Un-simplify, rewrite, then simplify again (as needed).

The motive for this composition is performance, especially for 'ln' where we can reduce how often we walk large definitions. With a little laziness, we can reduce rewrites to a single pass per definition. 

### Translation of Move, Link, Remove

Assume translation rule `{ => scratch. , src. => foo. , dst. => bar. }`. This is a prototypical example for mixins as functions. In this case, we can translate a move, link, or remove to operate on the translated namespace locations. For example, moving `{ src.x => dst.y }` becomes `{ foo.x => bar.y }`. And moving `{ x => dst.z }` becomes `{ scratch.x => bar.z }`. Essentially, we apply the translation independently to each prefix in the move, link, or remove. 

However, as with the followed-by composition, there are cases where we must first 'un-simplify' the move to include longer prefixes. For example, moving `{ sr => ds }` will first un-simplify to `{ sr => ds , src. => dsc. , srt. => dst. }`, then we can translate to `{ scratch.sr => scratch.ds , foo. => scratch.dsc. , scratch.srt. => bar. }`. This should be rare in practice: usually operations such as move, rename, remove, or translate will all be aligned to similar hierarchical application components.

### Translation of Definitions and Namespaces 

When applied to final definitions (or a namespace, which eventually evaluates to such definitions) we can simply convert translate to a rename. 

        tl:(df:Defs, TLMap) => tl:(ns:df:Defs, TLMap)       
        tl:(ns:Op, TLMap) => ns:mx:[Op, ln:TLMap, mv:TLMap]

That is, if we define `dst.xyzzy` and our translation is `dst. => bar.` then we actually defined `bar.xyzzy`. If `dst.xyzzy` depends on `src.qux` and we translate `src. => foo.` then `dst.xyzzy` actually depends on `foo.qux`. 

### Composition and Simplification of Removes

Composition of removes is relatively trivial: take the union of prefixes. We can simplify at the same time: we only need to keep the shortest prefix for each remove. 

### Pushing Removes ahead of Moves

It is feasible to push removes ahead of moves. In this case, our maps are asymmetric: `mv:{ bar => fo } fby rm:{ foo }`. As with the previous forms, we would first un-simplify the move map to include prefixes that we'll be removing: `{ bar => fo, baro => foo, foo => foo }`. Then we identify which prefixes are removed from the right-hand side. The main difference is that we'll have two maps at the end, one for removes and one for moves: `rm:{ baro, foo } fby mv:{ bar => fo }`. 

This is potentially useful as a simplification, allowing us to reduce the total number of operations in a mixin. 

### Rewrite Semantics

We can simplify a mixin or evaluate a namespace based on a rewrite semantics. All rewrites are bidirectional, but are written in a direction that leads towards simplifications or evaluations.

    mx:(A ++ (mx:Ops, B)) => mx:(A ++ (Ops ++ B))     # flatten mx
    mx:[Op] => Op                                     # singleton mx

    ns:mx:(ln:_, Ops) => ns:mx:Ops                    # rewrite on empty
    ns:mx:(rm:_, Ops) => ns:mx:Ops                    # remove on empty
    ns:mx:(mv:_, Ops) => ns:mx:Ops                    # move on empty
    ns:df:Defs => df:Defs                             # eval ns

    df:{} => mx:[]                                    # empty df
    ns:mx:[] => mx:[]                                 # empty ns
    rm:{} => mx:[]                                    # no-op rm
    ln:{} => mx:[]                                    # no-op ln
    mv:{} => mx:[]                                    # no-op mv
    tl:(Op, {}) => Op                                 # no-op tl
 
    # translations
    tl:(mx:(Op, Ops), TLMap) =>                                 # distribute tl
        mx:[ tl:(Op, TLMap), tl:(mx:Ops, TLMap)]
    tl:(mx:[], _) => mx:[]                                      # empty tl
    tl:(df:Defs, TLMap) => ns:mx:[df:Defs, mv:TLMap, ln:TLMap]  # translate def
    tl:(ns:Op, TLMap) => ns:mx:[Op, mv:TLMap, ln:TLMap]         # translate lazy def
    tl:(tl:(Op, A), B) => tl:(Op, A `fby` B)                    # sequence tl
        # see *Followed By (fby) Composition of Move, Link, Translate Maps*
    tl:(rm:M, TLMap) => rm:M'                                   # translate rm
    tl:(mv:M, TLMap) => mv:M'                                   # translate mv
    tl:(ln:M, TLMap) => ln:M'                                   # translate ln
        # for M' in each case see *Translation of Move, Link, Remove*       

    # in context of mx
      # shorthand 'A B C' = 'mx:(LHS++[A,B,C]++RHS)'

      # namespace partial eval
      ns:(Ops ++ [df:Defs]) => ns:Ops df:Defs
      ns:(Ops ++ [ns:Ops']) => ns:Ops ns:Ops'
        # basically, if we aren't modifying defs, they're done.

      # common merges
      df:A df:B => df:(union A B)
        # need to deal with ambiguity  
      ln:A ln:B => ln:(A `fby` B)
      mv:A mv:B => mv:(A `fby` B)
        # see *Followed By (fby) Composition of Move, Link, Translate Maps*
      rm:A rm:B => rm:(union A B)
        # see *Composition and Simplification of Removes*

      # links and moves commute. Convenient for simplifications.
      ln:A mv:B => mv:B ln:A
      ln:A rm:B => rm:B ln:A

      # removes can be pushed ahead of moves.
      mv:A rm:B => rm:B' mv:A'
        # see *Pushing Removes ahead of Moves*

      # apply rewrites! not detailed here.
      df:Defs ln:Links   => ln:Links   df:(update links in Defs)
      df:Defs mv:Moves   => mv:Moves   df:(move Defs in map)
      df:Defs rm:Removes => rm:Removes df:(remove indicated Defs)

These rewrite rules can serve as pseudo-code. 

One minor issue is that, in case of `df:A df:B rm:M` where `df:(union A B)` has an ambiguity error that is subsequently removed by `rm:M`, it is non-deterministic (compiler dependent) whether we'll notice the ambiguity error. I favor reducing priority of applying 'rm' below evaluation of 'ns' and merge of 'df' to ensure ambiguity must be resolved locally.

### Identifying Definitions

Applying 'ln' is the most expensive operation. If all we need to do is determine which definitions a namespace provides, we could compute a namespace to a point where all that remains is 'ns:mx', 'df', and 'ln'. Or we could make the `Def` type lazy, and apply linking lazily to each definition.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. The syntax resists accidental reference to private symbols. However, syntactic protection is weak and easily bypassed in glas systems, where user-defined syntax is supported. Better to provide a bypass that is easily discovered by search or linter.

Where robust privacy is required, we should instead rely on the namespace to control access to names. The namespace supports [ocap security](https://en.wikipedia.org/wiki/Object-capability_model) for hierarchical components. For example, if we rename `'' => 'foo.'` in a hierarchical component then it cannot access any names outside of `'foo.'` unless they are provided by the host. Providing methods to subcomponents can be expressed via another prefix rename (e.g. `'foo.sys.' => 'sys.'`) or abstracted via mixins (perhaps translating `'dst.' => 'foo.'`).

Nominative data types, implicit parameters, and algebraic effects may also be tied to the namespace, providing an effective basis to control access.

## Annotations and Associated Definitions

For each user-defined method, there might be several 'slots' defined in the namespace - e.g. representing declared types, the function code, and perhaps even a macro for invoking that code (to unify methods and macros, similar to fexprs). This is left entirely to the language design.

## Tentative Extensions

### Mapping over Definitions

Currently, our only operation that touches definitions is link (ln). But it might be useful to introduce another operation to support integration between abstract assembly languages. An initial proposal:

        ap:(NSOp, DefOp)    # apply

This would apply function DefOp to every definition in NSOp. DefOp could be expressed as a name of a function that we'll apply to each definition. Or perhaps itself as abstract assembly that will wrap the definition (i.e. `DefOp ++ [Def]`).

I hesitate to introduce this feature because I currently lack a clear use case. I'm uncertain whether the proposal will prove inadequate or irrelevant. Perhaps we'll instead prefer to design our abstract assembly with built-in hooks for sandboxing and adaptation.

### Conditional Definitions

We could introduce some operators that are the equivalent of ifdef/ifndef. This would be useful for defining default behavior. However, I hesitate because this complicates reasoning about the namespace and partial evaluation of namespaces. There are also other decent approaches to 'default' definitions, such as leveraging associated definitions.

## Lists

It is feasible to model lists in the namespace. For example, we could model lists as namespace components that define 'head' and 'tail'. Then access to to the third element would be `.tail.tail.head`. This essentially encodes a unary representation of the numeric index into the name. We could easily reduce this to one byte per element:

        h     first element
        th    second element
        tth   third element
        ...

One motive for this is that we can 'append' two lists by adding a sufficient 'ttt..t' prefix to one of the two lists then merging into the same namespace. Further, a runtime can easily introduce compression of a repeating 'ttt..t' prefix to something like `(t)^{42}` under the hood. Alternatively, we could explicitly extend the map type to support lists, leveraging that reserved `0b01` key suffix.

However, to effectively use lists we'll also need namespace operators to append lists, to map mixins to every element of a list, fold over lists, and so on. It won't be convenient to access or override elements of the list based on offsets because offsets would often be unstable, so I'd want to focus on collective operations. 

Developing such namespace operators seems feasible, but it isn't clear to me that the potential use cases would justify the added complexity. Maybe it would be worthwhile for a configuration language? But I think in most cases we could 'chain' definitions instead of composing the namespace directly. 


