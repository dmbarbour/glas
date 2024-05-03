
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for extension and recursion. The model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. However, support for 'new' or first-class objects is not implied. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md) or a concrete variant, but it can be adapted to any type where names are easily rewritten.

## Proposed AST

        type NSOp 
              = mx:(List of NSOp)                   # mixin 
              | ns:NSOp                             # namespace
              | df:(Map of Name to Def)             # definitions
              | rw:(Map of Prefix to Prefix)        # rewrite defs
              | mv:(Map of Prefix to Prefix)        # move defs
              | rm:(Map of Prefix to unit)          # remove defs
              | at:(Prefix, NSOp)                   # targeted op

        type Name = Binary                          # assumed prefix unique
        type Prefix = Binary                        # empty up to full Name
        type Map = specialized trie                 # essential! see below!
        type Opt T = List of T, max length one      # keeping it simple

* mixin (mx) - apply a sequence of operations to the tacit namespace
* namespace (ns) - evaluate NSOp (usually mx) in context of empty namespace, evaluating to definitions (df). This could be evaluated eagerly at AST construction time, so this operation is mostly about deferring costs and lazy evaluation.
* definitions (df) - add definitions to tacit namespace. It's an error if a name has two different definitions, but it's okay to assign the same definition many times.
* rewrite (rw) - modify names within definitions in tacit namespace, based on longest matching prefix  
* move (mv) - move definitions in tacit namespace, based on longest matching prefix
* remove (rm) - remove definitions in tacit namespace, essentially a move to `/dev/null`.
* targeted op (at) - modify an NSOp (usually mx) to operate on a different prefix. 


## Common Usage Patterns

* A basic 'rename' involves both 'mv' and 'rw' operations with the same Map.
* To 'override' `foo`, we rename prefix `foo^ => foo^^` to open space, move `foo => foo^` so we can access the prior (aka 'super') definition, then define `foo` with optional reference to `foo^`. Alternatively, we could remove then redefine `foo` if we don't need the old version.
* To 'shadow' `foo`, we rename prefix `foo^ => foo^^` to open space, *rename* `foo => foo^` so existing references to `foo` are preserved, then define `foo` with optional reference to `foo^`. 
* To model 'private' definitions, we prefix private definitions with '~', then we systematically rename '~' in context of inheritance. The syntax doesn't need to provide direct access to '~'.
* To treat mixins as functions, we can introduce some conventions for naming a mixin's parameters and results as hierarchical components that can be provided and extracted with sufficient syntactic sugar. 
* To model hierarchical composition, add a prefix to everything (via 'at') then provide any missing dependencies via rename or delegation. This is object capability secure, i.e. the hierarchical component cannot access anything that is not provided to it.

## Specialized Map Type

I propose to encode the map as a trie (a tree where paths encode a bitstring), expanding the binary Name or Prefix to a prefix-unique trie key in a simple way:

        toTrieKey (Byte, Bytes) = 0b1:(toOctet Byte):(toTrieKey Bytes)
        toTrieKey ()            = 0b0

The value is not modified, and simply follows the trie key. We can easily find the longest matching prefix for any given name. 

## Composition of Move and Rewrite

The maps in 'mv' and 'rw' represent atomic sets of rewrites. This atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step to avoid name collisions. This does imply we cannot casually separate operations into smaller steps.

However, it is feasible to compose sequential rewrites and moves. For example, `{ bar => fo }` followed by `{ f => xy, foo => z }` can compose to `{ bar => xyo, baro => z, f => xy, foo => z }`. In this case, we must handle the case where 'fo' has a longer matching suffix 'foo', resulting in us adding a special rule for 'baro'. In practice, increasing the number of rules should be rare due to alignment of rewrites with hierarchical application structure.

The main motive for this composition is performance: we can reduce the number of times we walk definitions. This is most relevant when definitions are very large, or when there are very many definitions. However, it isn't strictly necessary. 

## Prefix Unique Names

This AST assumes names are prefix unique, meaning no name is a prefix of another name. Prefix uniqueness is trivial to achieve in practice, e.g. the front-end compiler can add a suffix (such as the NULL character) to defined names

This assumption simplifies the namespace AST because the prefix-oriented operations can also be applied to specific names. We can detect prefix uniqueness violations when merging definitions (df), or when applying any prefix operation (rw, rm, mv, tl with prefix longer than a name).

## Unambiguous Definitions and Multiple Inheritance

To detect accidental name collisions in context of moves, renames, and multiple inheritance, we'll treat it as an ambiguity error when a name is assigned two or more different definitions. To override, the prior definition must first be explicitly moved or removed. To shadow a word, the prior definition must be renamed. 

However, it is unambiguous if the same definition is assigned to a name many times. And we can leverage redundant expression as a lightweight verification of interfaces, i.e. that multiple components sharing an interface have the same expectations based on matching documentation or type annotations.

*Note:* I think it's best if the programming language explicitly represents user expectations, i.e. whether we are introducing, overriding, or shadowing a definition.

## Rewrite Semantics

We can define NSOp based on a rewrite semantics. All rewrites are bidirectional, but are written in a direction that leads to a complete evaluation of a namespace.

    mx:(A ++ (mx:Ops, B)) => mx:(A ++ (Ops ++ B))     # flatten mx
    mx:[Op] => Op                                     # singleton mx

    ns:mx:(rw:_, Ops) => ns:mx:Ops                    # rewrite on empty
    ns:mx:(rm:_, Ops) => ns:mx:Ops                    # remove on empty
    ns:mx:(mv:_, Ops) => ns:mx:Ops                    # move on empty
    ns:df:Defs => df:Defs                             # eval ns

    df:[] => mx:[]                                    # empty df
    ns:mx:[] => mx:[]                                 # empty ns
    rm:[] => mx:[]                                    # no-op rm
    rw:[] => mx:[]                                    # no-op rw
    mv:[] => mx:[]                                    # no-op mv
    tl:(Op, []) => Op                                 # no-op tl

    # translations
    tl:(mx:(Op, Ops), RN) =>                          # distribute tl
      mx:[tl:(Op, RN), tl:(mx:Ops, RN)]
    tl:(mx:[], Any) => mx:[]                          # completed tl

    tl:(df:Defs, RN) => ns:mx:[df:Defs, rw:RN]        # translate df
    tl:(ns:Op, RN) => ns:mx:[Op, rw:RN]               # translate ns
    tl:(rw:RN, RN') => rw:(meeRN
    tl:(mv:MV, Any) => mv:MV
    tl:(rm:RM, Any) => rm:RM

    # in context of mx
      # shorthand 'A B C' = 'mx:(LHS++[A,B,C]++RHS)'

      # namespace partial eval
      ns:(Ops ++ [df:Defs]) => ns:Ops df:Defs
      ns:(Ops ++ [ns:Ops']) => ns:Ops ns:Ops'

      # commutativity of definitions
      ns:Ops ns:Ops'   => ns:Ops'  ns:Ops
      ns:Defs
      df:Defs df:Defs' => df:Defs' df:Defs



      # basic joins
      df:A df:B => df:(union(A,B))                    # join defs
      rm:A rm:B => rm:(union(A,B))                    # join removes
      rw:A rw:B => rw:(A++B)                          # join renames
      mv:A mv:B => mv:(A++B)                          # join moves

      # manipulate definitions
      df:Defs rw:Renames =>  rw:Renames df:(apply Renames to Defs)
      df:Defs rm:Removes =>  rw:Removes df:(apply Removes to Defs)
      df:Defs mv:Moves   =>  rw:Moves   df:(apply Moves to Defs)
      


    (++)                              # list concatenation
    union                             # set union 


            = ns:NSOp                       # namespace             
            | mx:(List of NSOp)             # mixin
            | df:(Set of (Name, Def))       # define
            | rw:(List of (Prefix, Prefix)) # rename
            | mv:(List of (Prefix, Prefix)) # move
            | rm:(Set of Prefix)            # remove (delete)
            | tl:(NSOp, List of (Prefix, Prefix)) # translate

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. The syntax resists accidental reference to private symbols. However, syntactic protection is weak and easily bypassed in glas systems, where user-defined syntax is supported. Better to provide a bypass that is easily discovered by search or linter.

Where robust privacy is required, we should instead rely on the namespace to control access to names. The namespace supports [ocap security](https://en.wikipedia.org/wiki/Object-capability_model) for hierarchical components. For example, if we rename `'' => 'foo.'` in a hierarchical component then it cannot access any names outside of `'foo.'` unless they are provided by the host. Providing methods to subcomponents can be expressed via another prefix rename (e.g. `'foo.sys.' => 'sys.'`) or abstracted via mixins (perhaps translating `'dst.' => 'foo.'`).

Nominative data types, implicit parameters, and algebraic effects may also be tied to the namespace, providing an effective basis to control access.


## Tentative Extensions

### Mapping over Definitions

It is feasible to introduce bulk operations for modifying definitions. An initial proposal:

        ap:(NSOp, DefOp)    # apply

This operator would apply a function on definitions (DefOp) to every definition introduced by NSOp. In context of abstract assembly, this might concretely be implemented as mapping `\Def -> (DefOp ++ [Def])`, essentially treating DefOp as a curried AST node.

I hesitate to introduce this feature because I lack a clear use case. It is potentially useful for sandboxing in context of overriding abstract assembly. But it could be subtly inadequate and awkward, or it may prove preferable to integrate sandboxing hooks when designing the abstract assembly.

### Conditional Definitions

We could introduce some operators that are the equivalent of ifdef/ifndef. This might be useful for expressing default definitions. However, I'm not inclined to support this because it complicates reasoning about what's in the namespace, and would also complicate optimizations such as composing renames.

### General Translation

I originally wanted `tl:(NSOp, Map of Prefix to Prefix)` but this doesn't generalize nicely. For example, with translation `{ '' => 'scratch.', 'src.' => 'foo.', 'dst.' => 'bar.' }` consider move `sr => xy`. When translated, this would rewrite `scratch.srx => scratch.xyx` and `foo.abc => scratch.xyc.abc` (maybe?). It's troublesome and confusing.

It might be feasible if I can restrict the translation model a great deal. Perhaps we could ensure reversibility, and perform a rewrite in each direction. But this could be implemented directly by a front-end compiler.

For now, we'll need to see how far we can get without translation. We might need to limit how we use mixins, but this might not be a problem in practice.
