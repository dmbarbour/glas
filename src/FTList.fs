namespace Glas

/// A finger-tree encoding of a list.
///
/// This encoding of lists is convenient when we need scalable indexing,
/// slicing, concatenation, enqueue and dequeue operations, etc.. 
/// 
/// The simple cons|nil encoding of lists is awkward at large scales. But
/// it is conceptually simple and robust. Glas programs assume this simple
/// encoding is transparently optimized by the compiler or interpreter. 
/// The finger-tree is the default encoding, though specialized encodings 
/// could be used in some cases based on static analysis.
/// 
/// This implementation of finger-trees is not generic, only tracking tree
/// size rather than an ad-hoc monoid.

module FTList =

    module Tree = 
        /// to compute tree sizes across different element types.
        type Sized =
            abstract member Size : uint64

        let inline size (v : 'V when 'V :> Sized) = 
            (v :> Sized).Size

        /// We'll wrap the base element type with 'Atom' just to 
        /// add the Sized interface. 
        [<Struct>]
        type Atom<'V> = 
            | Atom of 'V
            interface Sized with
                member _.Size = 1UL

        /// Remaining elements after D1 in Digits.
        type Z3<'V> =
            | Zero
            | One of 'V
            | Two of 'V * 'V
            | Three of 'V * 'V * 'V

        /// Digits holds 1 to 4 elements of a type.
        /// First element does not require allocation.
        [<Struct>]
        type Digits<'V> = { D1 : 'V ; Rem : Z3<'V> }

        let inline mkD1 a = { D1 = a; Rem = Zero }
        let inline mkD2 a b = { D1 = a; Rem = One b }
        let inline mkD3 a b c = { D1 = a; Rem = Two (b, c) }
        let inline mkD4 a b c d = { D1 = a; Rem = Three (b, c, d) }

        let inline dFull (d : Digits<'V>) : bool =
            match d.Rem with
            | Three _ -> true
            | _ -> false

        let dCons (v : 'V) (d : Digits<'V>) : Digits<'V> =
            match d.Rem with
            | Zero -> mkD2 v (d.D1) 
            | One (d2) -> mkD3 v (d.D1) d2
            | Two (d2, d3) -> mkD4 v (d.D1) d2 d3
            | Three _ -> invalidArg "d" "cons digits at maximum size"

        let dSize (d : Digits<'V> when 'V :> Sized) : uint64 =
            match d.Rem with
            | Zero -> size d.D1
            | One (d2) -> size d.D1 + size d2
            | Two (d2, d3) -> size d.D1 + size d2 + size d3
            | Three (d2, d3, d4) -> size d.D1 + size d2 + size d3 + size d4

        type B23<'V> =
            | B2 of 'V * 'V
            | B3 of 'V * 'V * 'V
        
        [<Struct>]
        type B<'V> =
            { Size : uint64
            ; Elem : B23<'V>
            }
            interface Sized with
                member b.Size = b.Size
        
        [<Struct>]
        type Finger<'V when 'V :> Sized> =
            { Prefix : Digits<'V>
            ; CoreSize : uint64
            ; Core : Tree<B<'V>>
            ; Suffix : Digits<'V>
            }
            interface Sized with
                member f.Size =
                    dSize (f.Prefix) + f.CoreSize + dSize (f.Suffix)

        and Tree<'V when 'V :> Sized> =
            | Empty
            | Single of 'V
            | Many of Finger<'V>
            interface Sized with
                member t.Size =
                    match t with
                    | Empty -> 0UL
                    | Single v -> size v
                    | Many f -> size f
  

