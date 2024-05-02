
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for extension and recursion. The model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. However, support for 'new' or first-class objects is not implied. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md) or a concrete variant, but it can be adapted to any type where names are easily rewritten.

## Proposed AST

        type NSOp 
              = mx:(List of NSOp)                   # mixin (sequence)
              | ns:NSOp                             # namespace
              | df:(Map of Name to Def)             # definitions
              | rn:(Map of Prefix to Prefix)        # rename
              | mv:(Map of Prefix to Prefix)        # move
              | rm:(Map of Prefix to unit)          # remove
              | tl:(NSOp, Map of Prefix to Prefix)  # translate

        type Name = Binary                          # assumed prefix unique
        type Prefix = Binary                        # empty up to full Name
        type Map = specialized trie                 # see below!

## Overview

A mixin (mx) applies a sequence of operations (NSOp) to a tacit namespace. Basic operations include introducing definitions (df) and renaming (rn), moving (mv), and removing (mv) definitions. The translate (tl) and namespace (ns) operations are somewhat more sophisticated.

To 'override' a definition can be expressed as a short sequence of basic operations: rename `foo^ => foo^^` to open a space, move `foo => foo^`, then define `foo` with reference to `foo^`. Alternatively, we could remove `foo` if we don't reference the prior definition. The programming language syntax should concisely capture common patterns including override, shadowing, private definitions, and hierarchical composition.

The translate operator (tl) is an adverb, modifying an NSOp to apply to a different context. This is the basis for abstraction and reuse of mixins. For example, we could define a reusable mixin in terms of 'src.' and 'dst.' prefixes, then translate those to the actual target. The common case is to translate the empty prefix to apply a mixin to a hierarchical component. 

The namespace operator (ns) represents a set of definitions (df) programmatically as an operation (NSOp, usually mx) on an initially empty namespace. To apply 'ns', evaluate to 'df' then apply that. The namespace operator allows a little laziness. 

## Composing Renames for Performance

The `rn:(Map of Prefix to Prefix)` operation represents an atomic set of renames. Atomicity is mostly relevant for cyclic renames, i.e. we could rename `{ foo => bar, bar => baz, baz => foo }` in a single step. To perform a rename, for each name, we'll find the longest matching prefix in the map and apply the rewrite. For example, if our rename map is `{ f => xy, foo => z }` then we'd rewrite names `four => xyor` and `foobar => zbar`. 

It is feasible to compose these rewrites. For example, `{ bar => fo }` followed by `{ f => xy, foo => z }` can compose to `{ bar => xyo, baro => z, f => xy, foo => z }`. 

Essentially, we must handle every prefix that `fo` might participate in, which is why we introduce two rules: `bar => xyo` and `baro => z`. In the general case, this will multiply the number of rules, which is expensive. But, in practice, renames on partial names will almost always be aligned with the hierarchical namespace, e.g. `foo.` and `bar.`, and such renames would rarely multiply.

The benefit of composing renames, then, is that we can reduce the number of times we walk definitions. Whether this is a big deal depends on whether the definitions are big. We could also compose moves, but the benefit is negligible.

## Specialized Map Type

I propose to encode the map as a trie (a tree where paths encode a bitstring), expanding the binary Name or Prefix to a prefix-unique trie key in a simple way:

        toTrieKey (Byte, Bytes) = 0b1:(toOctet Byte):(toTrieKey Bytes)
        toTrieKey ()            = 0b0  

In case of rename (rn), move (mv), or translate (tl) we leverage this structure to efficiently determine the longest matching prefix that is substituted by a rename. For definitions (df) the map is more convenient than a 'dict' for iteration and detection of prefix uniqueness errors. For removes (rm) the extra structure of the map isn't directly useful, but consistency is convenient.

## Prefix Unique Names

This AST assumes names are prefix unique, meaning no name is a prefix of another name. Prefix uniqueness is trivial to achieve in practice: the front-end compiler can simply reserve byte to terminate names, typically the NULL byte, and disallow use of this byte within names. 

This assumption simplifies the namespace AST because the prefix-oriented operations can also be applied to specific names. We can detect prefix uniqueness violations when merging definitions (df), or when applying any prefix operation (rn, rm, mv, tl with prefix longer than a name).

## Unambiguous Definitions and Multiple Inheritance

To help detect accidental name collisions in context of multiple inheritance, we'll treat it as an ambiguity error when a name is assigned two or more different definitions. To override, the prior definition must first be explicitly moved or removed. To shadow a definition, the prior definition must first be renamed. 

However, it is unambiguous if the same definition is assigned to a name many times. And redundant expression can be leveraged as a lightweight verification that an interface has the same 'meaning' to all participants. In this case the 'meaning' would be encoded as documentation and type annotations.

To simplify local reasoning, the programming language should syntactically distinguish defining, overriding, and shadowing names based on keywords. Make programmer assumptions explicit.

## Rewrite Semantics

We can define NSOp based on a rewrite semantics. All rewrites are bidirectional, but are written in the direction of simplifying things.

    mx:[Op] => Op                                     # singleton mx
    mx:(A ++ (mx:Ops, B)) => mx:(A ++ (Ops ++ B))     # flatten mx
    ns:df:Defs => df:Defs                             # namespace eval
    ns:mx:(rn:Any, Ops) => ns:mx:Ops                  # rename on empty ns
    ns:mx:(rm:Any, Ops) => ns:mx:Ops                  # remove on empty ns
    ns:mx:(mv:Any, Ops) => ns:mx:Ops                  # move on empty ns

    ns:mx:[] => mx:[]                                 # empty ns
    df:[] => mx:[]                                    # empty df
    rm:[] => mx:[]                                    # no-op rm
    rn:[] => mx:[]                                    # no-op rn
    mv:[] => mx:[]                                    # no-op mv
    tl:(Op, []) => Op                                 # no-op tl
    tl:(mx:[], Any)                                   # translate empty

    # translations
    tl:(mx:(Op, Ops), RN) =>                          # distribute tl
      mx:[tl:(Op, RN), tl:(mx:Ops, RN)]
    tl:(df:Defs, RN) => ns:mx:[df:Defs, rn:RN]        # translate df
    tl:(ns:Op, RN) => ns:mx:[Op, rn:RN]               # translate ns
    tl:(rn:RN, Any) => rn:RN
    tl:(mv:MV, Any) => mv:MV
    tl:(rm:RM, Any) => rm:RM

    # in context of mx
      # shorthand 'A B C' = 'mx:(LHS++[A,B,C]++RHS)'

      # basic joins
      df:A df:B => df:(union(A,B))                    # join defs
      rm:A rm:B => rm:(union(A,B))                    # join removes
      rn:A rn:B => rn:(A++B)                          # join renames
      mv:A mv:B => mv:(A++B)                          # join moves

      # manipulate definitions
      df:Defs rn:Renames =>  rn:Renames df:(apply Renames to Defs)
      df:Defs rm:Removes =>  rn:Removes df:(apply Removes to Defs)
      df:Defs mv:Moves   =>  rn:Moves   df:(apply Moves to Defs)
      


    (++)                              # list concatenation
    union                             # set union 


            = ns:NSOp                       # namespace             
            | mx:(List of NSOp)             # mixin
            | df:(Set of (Name, Def))       # define
            | rn:(List of (Prefix, Prefix)) # rename
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

