
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for extension and recursion. The model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. However, support for 'new' or first-class objects is not implied. 

This document assumes definitions are expressed using [abstract assembly](AbstractAssembly.md) or a concrete variant, but it can be adapted to any type where names are easily rewritten.

## Proposed AST

        type NSOp 
            = ns:NSOp                       # namespace             
            | mx:(List of NSOp)             # mixin
            | df:(Set of (Name, Def))       # define
            | rn:(List of (Prefix, Prefix)) # rename
            | mv:(List of (Prefix, Prefix)) # move
            | rm:(Set of Prefix)            # remove (delete)
            | tl:(NSOp, List of (Prefix, Prefix)) # translate

        type Name   = Binary                # must be prefix unique
        type Prefix = Binary                # empty up to full name
        type Set    = List                  # commutative & idempotent 
        type Def    = abstract assembly     # define 

This expresses a namespace as a sequence of operations to modify an initially empty namespace. Overview of Behavior:

* *namespace* (ns) - apply NSOps sequentially to an initially empty namespace, evaluating to definitions (df). Apply those definitions. 
* *mixin* (mx) - apply a sequence of NSOp. This allows us to compose primitive operations into a cohesive behavior. 
* *definitions* (df) - union definitions into tacit namespace. It's an error if a name has two definitions, but it's okay to assign the same definition twice.
* *rename* (rn) - a prefix to prefix rewrite on names in the tacit namespace
* *move* (mv) - move definitions without modifying them, useful for overrides.
* *remove* (rm) - undefine words in the tacit namespace, useful for overrides.
* *translate* (tl) - modifies where a mixin is applied. Translate can serve a similar role as parameters in mixins, e.g. rewrite 'dst. => foo.' before applying mixin.

### Rewrite Semantics

We can define NSOp based on a rewrite semantics. All rewrites are bidirectional, but are written in the direction of simplifying things.

    mx:[Op] => Op                                     # singleton mx
    mx:(A ++ (mx:Ops, B)) => mx:(A ++ (Ops ++ B))     # flatten mx
    ns:df:Defs => df:Defs                             # namespace eval
    ns:mx:(rn:Any, Ops) => ns:mx:Ops                  # rename on empty ns
    ns:mx:(rm:Any, Ops) => ns:mx:Ops                  # remove on empty ns
    ns:mx:(mv:Any, Ops) => ns:mx:Ops                  # move on empty ns
    ns:mx:(tl:(Op, RN), Ops) =>                       # translate final
      ns:mx:(Op, (rn:RN, Ops))  

    ns:mx:[] => df:[]                                 # empty ns
    df:[] => mx:[]                                    # empty df
    rm:[] => mx:[]                                    # no-op rm
    rn:[] => mx:[]                                    # no-op rn
    mv:[] => mx:[]                                    # no-op mv
    tl:(mx:[], Any)                                   # translate empty
    tl:(Op, []) => Op                                 # no-op tl

    # in context of mx
      # shorthand 'A B C' = 'mx:(LHS++[A,B,C]++RHS)'

      # basic joins
      df:A df:B => df:(union(A,B))                    # join defs
      rm:A rm:B => rm:(union(A,B))                    # join removes
      rn:A rn:B => rn:(A++B)                          # join renames
      mv:A mv:B => mv:(A++B)                          # join moves

      # manipulate definitions



    (++)                              # list concatenation
    union                             # set union 


            = ns:NSOp                       # namespace             
            | mx:(List of NSOp)             # mixin
            | df:(Set of (Name, Def))       # define
            | rn:(List of (Prefix, Prefix)) # rename
            | mv:(List of (Prefix, Prefix)) # move
            | rm:(Set of Prefix)            # remove (delete)
            | tl:(NSOp, List of (Prefix, Prefix)) # translate



## Performance Concerns

I expect namespaces to grow very large and contain many redundant definitions tucked away in private namespaces. Thus, careful attention is needed to performance. Unfortunately, lazy evaluation is hindered by conditional namespaces. We're essentially forced to construct the full namespace. What can we do to improve performance?

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. 

The syntax can prevent accidental reference to private symbols. However, syntactic protection is weak and easily bypassed in glas systems, where user-defined syntax is supported. Better to provide a bypass that is easily discovered by search or linter.

Where robust privacy is required, we can rely on the namespace to control access to names. This involves defining hierarchical components and restricting which names are forwarded into subcomponents components. Forwarding into subcomponents can be expressed via whole prefix rename (e.g. `'foo.sys.' => 'sys.'`) or via mixin that renames or delegates individual definitions (e.g. `mix xyzzy with 'dst.'=>'foo.'`). The 

The namespace is designed to make this pattern [ocap secure](https://en.wikipedia.org/wiki/Object-capability_model). 

Abstract types, implicit parameters, and algebraic effects can also be tied to the namespace to inherit namespace-layer security. 

## Overrides

To override a definition, we *move* the original definition then redefine it. If we're overriding from multiple sources, we might need to *move* the definition from each source before those sources are inherited.

## Multiple Inheritance

There is an ambiguity risk when the same name is inherited from two sources. The proposed namespace model doesn't do much to resolve ambiguity, but it includes a few annotations to help express and check user assumptions. The resulting namespace can support import and export control, mixins, and composition of non-overlapping.

Further, the rule that there is no ambiguity if two definitions are the same is convenient for *interfaces*, i.e. because we can repeatedly merge the same definitions of types and documentation. 

## Nominative Types, Implicit Parameters, Algebraic Effects

We can tie abstract, ephemeral types to the namespace. We're limited to *ephemeral* (per transaction) types if we assume live coding, otherwise we can treat the namespace as having the same lifespan as the OS process. I think it would be especially useful to tie implicit parameters and algebraic effects to the namespace, such that we can easily reason about access and interaction with RPC.


## Future Extensions

### Operations to Modify Definitions? Tentative.

It is feasible to introduce namespace operators to modify definitions in bulk. For example, we could introduce 'apply' - `ap:(List of Function)` - that applies a sequence of functions to every definition in the tacit namespace. A potential use-case is sandboxing of abstract assembly. 

However, it is unclear that this wouldn't be better handled at another layer, such as designing the front-end compiler and primitive AST constructors to include hooks for effective sandboxing. I've decided to defer this until I have a clear use-case where the alternatives are clearly lacking.
