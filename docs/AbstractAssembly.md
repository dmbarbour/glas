# Abstract Assembly

Proposed plain-old-data representation for abstract assembly as glas data:

        type AST = (Name, List of AST)      # constructor
                 | d:Data                   # embedded data
                 | n:Name                   # namespace ref 
                 | s:(AST, List of TL)      # scoped AST
                 | z:List of TL             # localization

        type Name = prefix-unique Binary, excluding NULL, as bitstring
        type Prefix = any prefix of Name (empty to full), byte aligned
        type TL = Map of Prefix to (Prefix | NULL) as radix tree dict
          # rewrites longest matching prefix, invalidates if NULL

This encoding uses unlabeled lists for the primary AST node constructor, and a tagged union for everything else. An essential feature is that constructors always start with names. This allows us to leverage the namespace to extend, restrict, and redirect constructors. The system will provide a set of primitive constructor names prefixed with '%', such as '%i.add' for arithmetic and '%seq' for procedural composition. This common prefix simplifies recognition and translation. 

The only computation expressed at the AST layer is scoping 's', which applies a sequence of translations to an AST node. Scoping isn't strictly necessary. It will be eliminated when we apply the translations and evaluate an AST to normal form. However, scoping is convenient when composing large AST fragments, and supports lazy evaluation. A localization essentially records a scope for use in later computations, mostly multi-staged programming. 

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.

Abstract assembly is designed for use in context of the [glas namespace model](GlasNamespaces.md), and I intend to gradually merge this document into that one.

