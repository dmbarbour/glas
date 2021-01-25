namespace Glas

module FingerTree =
    // Glas programs assume lists are suitable as general sequences, i.e.
    // with efficient operations at both ends, efficient split and append.
    // Even for bootstrap, this is useful.
    //
    // Other relevant features: 
    //   content-addressed storage for large subtrees 
    //   compact, rope-style chunking of binary fragments
    //
    // Including rope-style chunking within generically typed finger trees
    // is relatively awkward. However, it might be feasible to express as a
    // specialized stowage compaction option.

    open Monoid
    open Measured
    

    type Branch<'a> =
        | Branch2 of 'a * 'a
        | Branch3 of 'a * 'a * 'a

    [<Struct>]
    type Node<'a, 'm> =
        val M : 'm
        val B : 
    
    type Affix<'a> =
        | One of 'a
        | Two of 'a * 'a
        | Three of 'a * 'a * 'a
        | Four of 'a * 'a * 'a * 'a

    type Tree<'a, 'm> =
        | Empty
        | Single of 'a
        | Many of Many<'a>

    and Many<'a> =
        { Prefix: Affix<'a>
        ; Msize: bigint
        ; Middle: Tree<Node<'a>>
        ; Suffix: Affix<'a>
        } 
    
    let affixAppend aff x =
        match aff with
        | One a -> Two (a,x)
        | Two (a,b) -> Three (a,b,x)
        | Three (a,b,c) -> Four (a,b,c,x)
        | Four _ -> failwith "Cannot append four-element suffix"
    
    let affixPrepend x aff =
        match aff with
        | One a -> Two (x,a)
        | Two (a,b) -> Three (x, a, b)
        | Three (a,b,c) -> Four (x,a,b,c)
        | Four _ -> failwith "Cannot prepend four-element prefix"



