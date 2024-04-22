
# Extensible Namespaces

A namespace is essentially a dictionary with late binding of definitions, allowing for extension and recursion. The model described in this document supports multiple inheritance, mixins, hierarchical components, and robust access control to names. However, support for 'new' or first-class objects is not implied. 

I assume [abstract assembly](AbstractAssembly.md) or similar for definitions. It must be easy to precisely identify and rewrite names within definitions.

## Proposed AST

        type NSOp 
            = ns:(List of NSOp)             # namespace
            | mx:(List of NSOp)             # mixin (sequential compositon)
            | df:(List of (Name, Def))      # define
            | dc:(List of Name)             # declare
            | rq:(List of Name)             # require
            | rj:(List of Prefix)           # reject
            | rn:(List of (Prefix, Prefix)) # rename
            | mv:(List of (Prefix, Prefix)) # move
            | rm:(List of Prefix)           # remove (delete)
            | ap:(List of Op)               # apply (map, wrap, adapt)

        type Name   = Binary                # must be prefix unique
        type Prefix = Binary                # empty up to full name
        type Def    = abstract assembly     # (usually!)
        type Op     = abstract assembly     # curried `AST -> AST`

Behaviors:

* *namespace* (ns) - apply sequence of operations to a *fresh, initially empty* namespace, logically reducing to a final set of definitions (df). Apply as definitions.
* *mixin* (mx) - apply sequence of operations to the tacit namespace.  This represents sequential composition; 'mx' lists may be flattened into 'mx' or 'ns' lists. 
* *definitions* (df) - add new definitions to tacit namespace. Potential errors:
  * *ambiguity error* - name already defined AND definition structurally different
  * *prefix conflict* - detect violation of prefix uniqueness of names
* *rename* (rn) - modify names by prefix, also applies to definitions. 
* *move* (mv) - modify names by prefix, does not touch definitions. Mostly used for overrides.
* *remove* (rm) - erase all definitions under prefixes from tacit namespace. 
* *apply* (ap) - (tentative!) apply sequence of ops to all definitions in tacit namespace. 

Annotations (no observable behavior in valid system):

* *declare* (dc) - expect future definition of names, influences require and reject
* *require* (rq) - error if name isn't defined or declared in tacit namespace
* *reject* (rj) - error if prefix contains definition or declaration in tacit namespace

I currently don't have a use case for ad-hoc user-defined annotations on NSOp. Instead, most such annotations are represented in the final namespace (e.g. `foo#type`) or within definitions. 

## Performance Concerns

I expect namespaces to grow very large and contain many redundant definitions tucked away in private namespaces. Thus, careful attention is needed to performance. Unfortunately, lazy evaluation is hindered by conditional namespaces. We're essentially forced to construct the full namespace. What can we do to improve performance?

## Single Pass Rewrites

A set of prefix-based rewrites can be expressed as an associative map such as `{ f => xy, foo => x }` with a rule that the longest match 'wins'. In this case, we'd prioritize a match on 'foo' over a match on 'f'. To support renames versus moves, we'll construct *two* sets of rewrites: one for the namespace layer, one for the definition body. A rename will add a rule to both, while a move only adds a rule for the namespace layer. 

It is feasible to compose these rewrites. For example, if a component from which we inherit adds `{ bar => fo }`, we'll logically apply this *before* the prior `{ f => xy, foo => x }` rewrites. In this case, the composite rewrite would be `{ bar => xyo, baro => x, foo => xy, foo => x }`. Computing this composite isn't trivial, but it's possible just by looking for all matching prefixes and suffixes of 'fo'. In practice, most rewrites will be aligned to hierarchical namespaces or complete names, in which case the composite rewrites will not usually increase in size.

## Private Definitions

As a convention, I propose private symbols start with '~'. This resists accidental shadowing of public names. Later, when composing namespaces, we can rewrite prefix '~' from each composed namespace to avoid collisions between private names. 

The syntax can prevent accidental reference to private symbols. However, syntactic protection is weak and easily bypassed in glas systems, where user-defined syntax is supported. Better to provide a bypass that is easily discovered by search or linter.

Where robust privacy is required, we can rely on the namespace to control access to names. This involves defining hierarchical components and restricting which names are forwarded or wired to other components. The namespace is designed to make this pattern [ocap secure](https://en.wikipedia.org/wiki/Object-capability_model). 

Abstract types, implicit parameters, and algebraic effects can also be tied to the namespace to inherit namespace-layer security. 

## Overrides

To override a definition, we *move* the original definition then redefine it. If we're overriding from multiple sources, we might need to *move* the definition from each source before those sources are inherited.

## Multiple Inheritance

There is an ambiguity risk when the same name is inherited from two sources. The proposed namespace model doesn't do much to resolve ambiguity, but it includes a few annotations to help express and check user assumptions. The resulting namespace can support import and export control, mixins, and composition of non-overlapping.

Further, the rule that there is no ambiguity if two definitions are the same is convenient for *interfaces*, i.e. because we can repeatedly merge the same definitions of types and documentation. 

## Nominative Types, Implicit Parameters, Algebraic Effects

We can tie abstract, ephemeral types to the namespace. We're limited to *ephemeral* (per transaction) types if we assume live coding, otherwise we can treat the namespace as having the same lifespan as the OS process. I think it would be especially useful to tie implicit parameters and algebraic effects to the namespace, such that we can easily reason about access and interaction with RPC.

