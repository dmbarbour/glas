namespace Glas

/// A finger-tree based encoding of a list.
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
/// This implementation of finger-trees is not generic, only tracking size.
module FT = 
    /// to compute tree sizes across different element types.
    type ISized =
        abstract member Size : uint64

    let inline isize (v : 'V when 'V :> ISized) = 
        (v :> ISized).Size

    /// wrap tree base elements with 'Atom' to add ISized. 
    [<Struct>]
    type Atom<'V> = 
        val V: 'V
        new(v) = { V = v }
        interface ISized with
            member _.Size = 1UL
    
    type D<'V> =
        | D1 of 'V
        | D2 of 'V * 'V
        | D3 of 'V * 'V * 'V
        | D4 of 'V * 'V * 'V * 'V 

    module D =

        let size (d : D<'V> when 'V :> ISized) : uint64 =
            match d with
            | D1 (d1) -> isize d1
            | D2 (d1,d2) -> isize d1 + isize d2
            | D3 (d1,d2,d3) -> isize d1 + isize d2 + isize d3
            | D4 (d1,d2,d3,d4) -> isize d1 + isize d2 + isize d3 + isize d4

        let consDL (d : D<'V>) (acc : 'V list) : 'V list =
            match d with
            | D1 (d1) -> d1::acc
            | D2 (d1,d2) -> d1::d2::acc
            | D3 (d1,d2,d3) -> d1::d2::d3::acc
            | D4 (d1,d2,d3,d4) -> d1::d2::d3::d4::acc

        let toList d =
            consDL d (List.empty)

        let ofList l0 =
            match l0 with
            | [] -> invalidArg (nameof l0) "insufficient elements to construct D"
            | (d1::l1) ->
                match l1 with
                | [] -> D1(d1)
                | (d2::l2) ->
                    match l2 with
                    | [] -> D2(d1,d2)
                    | (d3::l3) ->
                        match l3 with
                        | [] -> D3(d1,d2,d3)
                        | (d4::l4) -> 
                            match l4 with
                            | [] -> D4(d1,d2,d3,d4)
                            | _ -> invalidArg (nameof l0) "too many elements for D"
        
    type B<'V> =
        | B2 of size:uint64 * b1:'V * b2:'V
        | B3 of size:uint64 * b1:'V * b2:'V * b3:'V
        interface ISized with
            member b.Size =
                match b with
                | B2 (size=sz) -> sz
                | B3 (size=sz) -> sz

    module B =
        let inline mkB2<'V when 'V :> ISized> (a : 'V) (b : 'V) : B<'V> =
            let sz = isize a + isize b
            B2 (size=sz, b1=a, b2=b)

        let inline mkB3<'V when 'V :> ISized> (a : 'V) (b : 'V) (c : 'V) : B<'V> =
            let sz = isize a + isize b + isize c
            B3 (size=sz, b1=a, b2=b, b3=c)

        let toD (b : B<'V>) : D<'V> =
            match b with
            | B2 (b1=a; b2=b) -> D2 (a,b)
            | B3 (b1=a; b2=b; b3=c) -> D3 (a,b,c)

        let toList b =
            match b with
            | B2 (b1=a; b2=b) -> [a; b]
            | B3 (b1=a; b2=b; b3=c) -> [a; b; c]

        // chunkify is needed when appending trees.
        // minimum list size to chunkify is 2 elems.
        // not tail-recursive, use only on small lists
        let rec chunkify<'V when 'V :> ISized> (lv : 'V list) : B<'V> list =
            match lv with
            | (b1::b2::rem) -> 
                match rem with 
                | [] -> (mkB2 b1 b2)::[] // 2 elems
                | (b3::[]) -> (mkB3 b1 b2 b3)::[] // 3 elems
                | (b3::b4::[]) -> (mkB2 b1 b2)::(mkB2 b3 b4)::[] // 4 elems
                | (b3::lv') -> (mkB3 b1 b2 b3)::(chunkify lv') // 5+ elems
            | _ -> invalidArg (nameof lv) "not enough data to chunkify" // 0 or 1 elems

    [< NoEquality; NoComparison >]
    type T<'V when 'V :> ISized> =
        | Empty
        | Single of 'V
        | Many of size:uint64 * prefix:D<'V> * finger:T<B<'V>> * suffix:D<'V>
        interface ISized with
            member t.Size =
                match t with
                | Empty -> 0UL
                | Single v -> isize v
                | Many (size=sz) -> sz

    module T =
        let inline isEmpty t =
            match t with
            | Empty -> true
            | _ -> false

        let inline mkT p f s =
            let sz = D.size p + isize f + D.size s
            Many (size=sz, prefix=p, finger=f, suffix=s)

        let ofD (d:D<'V>) : T<'V> =
            match d with
            | D1 d1 -> Single d1
            | D2 (d1,d2) -> mkT (D1(d1)) Empty (D1(d2))
            | D3 (d1,d2,d3) -> mkT (D1(d1)) Empty (D2(d2,d3))
            | D4 (d1,d2,d3,d4) -> mkT (D2(d1,d2)) Empty (D2(d3,d4))

        let rec viewL<'V when 'V :> ISized> (t : T<'V>) : struct('V * T<'V>) option =
            match t with
            | Empty -> None
            | Single v -> Some (v, Empty)
            | Many (prefix=p; finger=f; suffix=s) ->
                match p with
                | D4 (d1, d2, d3, d4) -> Some struct(d1, mkT (D3(d2,d3,d4)) f s)
                | D3 (d1, d2, d3) -> Some struct(d1, mkT (D2(d2,d3)) f s)
                | D2 (d1, d2) -> Some struct(d1, mkT (D1(d2)) f s)
                | D1 (d1) ->
                    let t' = 
                        match viewL f with
                        | Some struct(b, f') -> mkT (B.toD b) f' s
                        | None -> ofD s
                    Some struct(d1, t')
        
        let rec viewR<'V when 'V :> ISized> (t : T<'V>) : struct(T<'V> * 'V) option =
            match t with
            | Empty -> None
            | Single v -> Some struct(Empty, v)
            | Many (prefix=p; finger=f; suffix=s) ->
                match s with
                | D4 (d1, d2, d3, d4) -> Some struct(mkT p f (D3(d1,d2,d3)), d4)
                | D3 (d1, d2, d3) -> Some struct(mkT p f (D2(d1,d2)), d3)
                | D2 (d1, d2) -> Some struct(mkT p f (D1(d1)), d2)
                | D1 (d1) ->
                    let t' =
                        match viewR f with
                        | Some struct(f', b) -> mkT p f' (B.toD b)
                        | None -> ofD p
                    Some struct(t', d1)

        let rec cons<'V when 'V :> ISized> (v : 'V) (t : T<'V>) : T<'V> =
            match t with
            | Empty -> Single v
            | Single v0 -> mkT (D1(v)) Empty (D1(v0))
            | Many (prefix=p; finger=f; suffix=s) ->
                match p with
                | D4 (d1,d2,d3,d4) -> mkT (D2(v,d1)) (cons (B.mkB3 d2 d3 d4) f) s
                | D3 (d1,d2,d3) -> mkT (D4(v,d1,d2,d3)) f s
                | D2 (d1,d2) -> mkT (D3(v,d1,d2)) f s
                | D1 (d1) -> mkT (D2(v,d1)) f s

        let rec snoc<'V when 'V :> ISized> (t : T<'V>) (v : 'V) : T<'V> =
            match t with
            | Empty -> Single v
            | Single v0 -> mkT (D1(v0)) Empty (D1(v))
            | Many (prefix=p; finger=f; suffix=s) ->
                match s with
                | D4 (d1,d2,d3,d4) -> mkT p (snoc f (B.mkB3 d1 d2 d3)) (D2 (d4,v))
                | D3 (d1,d2,d3) -> mkT p f (D4(d1,d2,d3,v))
                | D2 (d1,d2) -> mkT p f (D3(d1,d2,v))
                | D1 (d1) -> mkT p f (D2(d1,v))

        let ofList l =
            List.fold snoc Empty l

        /// Append two trees.
        let rec append<'V when 'V :> ISized> (l : T<'V>) (r : T<'V>) : T<'V> =
            match l with
            | Empty -> r
            | Single v -> 
                match r with
                | Empty -> l 
                | _ -> cons v r
            | Many (prefix=pl; finger=fl; suffix=sl) ->
                match r with
                | Empty -> l
                | Single v -> snoc l v
                | Many (prefix=pr; finger=fr; suffix=sr) ->
                    let bc = B.chunkify (D.consDL sl (D.consDL pr List.empty))
                    let fl' = List.fold snoc fl bc
                    mkT pl (append fl' fr) sr

        let rec private _splitListAcc<'V when 'V :> ISized> n acc l : struct ('V list * 'V * 'V list) =
            match l with
            | (x::xs) -> 
                let xz = isize x
                if (xz > n) then struct(List.rev acc, x, xs) else
                _splitListAcc (n - xz) (x::acc) xs
            | [] -> failwith "failed to find split point in list" 
        let inline private _splitList n l = 
            _splitListAcc n (List.empty) l

        let rec private _splitAt<'V when 'V :> ISized> (n : uint64) (t : T<'V>) : struct (T<'V> * 'V * T<'V>) =
            assert(n < isize t) // invariant
            match t with
            | Single v -> struct(Empty, v, Empty)
            | Many (prefix=p; finger=f; suffix=s) ->
                let pz = D.size p
                let pfz = pz + isize f
                if n < pz then  // split p
                    let struct(l, x, r) = _splitList n (D.toList p)
                    let rt = 
                        if List.isEmpty r then
                            match viewL f with
                            | Some struct(b, f') -> mkT (B.toD b) f' s
                            | None -> ofD s
                        else mkT (D.ofList r) f s
                    struct(ofList l, x, rt)
                else if n < pfz then // split f
                    let n' = n - pz
                    let struct(lf, b, rf) = _splitAt n' f
                    let struct(lb, x, rb) = _splitList (n' - isize lf) (B.toList b)
                    let lt = 
                        if List.isEmpty lb then
                            match viewR lf with
                            | Some struct(lf', b') -> mkT p lf' (B.toD b')
                            | None -> ofD p
                        else mkT p lf (D.ofList lb)
                    let rt =
                        if List.isEmpty rb then
                            match viewL rf with
                            | Some struct(b', rf') -> mkT (B.toD b') rf' s
                            | None -> ofD s
                        else mkT (D.ofList rb) rf s
                    struct(lt, x, rt)
                else // split s
                    let n' = n - pfz
                    let struct(l, x, r) = _splitList n' (D.toList s)
                    let lt = 
                        if List.isEmpty l then
                            match viewR f with
                            | Some struct(f', b) -> mkT p f' (B.toD b)
                            | None -> ofD p
                        else mkT p f (D.ofList l)
                    struct(lt, x, ofList r)
            | Empty -> failwith "inner split on empty tree; should be impossible"

        /// Split tree based on index. 
        let splitAt<'V when 'V :> ISized> (n : uint64) (t : T<'V>) : struct(T<'V> * T<'V>) =
            // ensure n < isize t for internal _splitAt
            if (n >= isize t) then struct(t, Empty) else
            let struct(l, x, r) = _splitAt n t
            struct(l, cons x r)


        let rec private _elemAtListRem n l =
            match l with
            | (x::xs) -> 
                let xz = isize x
                if (xz > n) then struct(n, x) else
                _elemAtListRem (n - xz) xs
            | [] -> failwith "failed to find offset of elem in list" 
        let inline private _elemAtList n l =
            let struct(nRem, x) = _elemAtListRem n l
            struct(n - nRem, x)

        let rec private _elemAt<'V when 'V :> ISized> (n : uint64) (t : T<'V>) : struct(uint64 * 'V) =
            match t with
            | Single v -> struct(0UL,v)
            | Many (prefix=p; finger=f; suffix=s) ->
                let pz = D.size p
                let pfz = pz + isize f
                if n < pz then  
                    _elemAtList n (D.toList p)
                else if n < pfz then 
                    let n' = n - pz
                    let struct(flz, b) = _elemAt n' f // find correct branch
                    let struct(blz, x) = _elemAtList (n' - flz) (B.toList b)
                    struct(pz + flz + blz, x)
                else
                    let struct(szr, x) = _elemAtList (n - pfz) (D.toList s)
                    struct(pfz + szr, x)
            | Empty -> failwith "inner elemAt on empty tree; should be impossible"


        let elemAt (n : uint64) (t : T<Atom<'V>>) : Atom<'V> =
            if (n >= isize t) then invalidArg (nameof n) "index out of range" else
            let struct(nSkipped, v) = _elemAt n t
            assert(nSkipped = n)
            v

        let rec eqAtoms (l : T<Atom<'a>>) (r : T<Atom<'a>>) = 
            match viewL l, viewL r with
            | Some struct(l0,l'), Some struct(r0,r') ->
                if (l0.V) <> (r0.V) then false else eqAtoms l' r'
            | None, None -> true
            | _ -> false

        let rec cmpAtoms (l : T<Atom<'a>>) (r : T<Atom<'a>>) =
            match viewL l, viewL r with
            | Some struct(l0, l'), Some struct(r0,r') ->
                let cmp = compare (l0.V) (r0.V)
                if (0 <> cmp) then cmp else cmpAtoms l' r'
            | Some _, None -> 1
            | None, Some _ -> -1
            | None, None -> 0
    

open FT

/// An FTList is a list whose underlying representation is a finger-tree.
/// This mostly enables efficient access to both ends, append, and slices.
/// The cost is some complexity, so average ops are more expensive.
///
/// ASIDE: I tried adding equality and comparison, but I'm having too much
/// trouble working with F# generic constraints (could not be generalized
/// because it would escape scope; implicit conversions to IComparable; etc.)
[<Struct>]
type FTList<'a> = 
    val T: T<Atom<'a>> 
    new(t) = { T = t }


module FTList =

    let inline ofT (t : T<Atom<'a>>) : FTList<'a> = 
        FTList(t)

    let inline toT (l : FTList<'a> ) : T<Atom<'a>> =
        l.T

    let empty<'a> : FTList<'a> = 
        ofT Empty

    let inline isEmpty l =
        T.isEmpty (toT l)

    let inline singleton a = 
        ofT (Single (Atom(a)))

    /// Length of FTLists, reported as a uint64.
    let inline length l = 
        isize (toT l)

    let inline tryViewL l = 
        match T.viewL (toT l) with
        | Some struct(e,t') -> Some (e.V, ofT t')
        | None -> None

    let inline (|ViewL|_|) l = 
        tryViewL l

    let inline tryViewR l = 
        match T.viewR (toT l) with
        | Some struct(t',e) -> Some (ofT t', e.V)
        | None -> None

    let inline (|ViewR|_|) l = 
        tryViewR l

    let inline eq x y =
        T.eqAtoms (toT x) (toT y)

    let inline compare x y = 
        T.cmpAtoms (toT x) (toT y)

    let inline cons e l = 
        ofT (T.cons (Atom(e)) (toT l))

    let inline snoc l e =
        ofT (T.snoc (toT l) (Atom(e)))

    let inline append a b =
        ofT (T.append (toT a) (toT b))

    let ofList l = 
        List.fold snoc empty l

    let ofSeq s = 
        Seq.fold snoc empty s

    let ofArray a = 
        Array.fold snoc empty a

    let rec fold fn (st0 : 'ST) ftl =
        match T.viewL (toT ftl) with
        | Some struct(e, t') -> fold fn (fn st0 (e.V)) (ofT t')
        | None -> st0
    
    let rec foldBack fn ftl (st0 : 'ST) =
        match T.viewR (toT ftl) with
        | Some struct(t', e) -> foldBack fn (ofT t') (fn (e.V) st0)
        | None -> st0

    let toList (ftl : FTList<'a>) : 'a list =
        let cons e l = e::l
        foldBack cons ftl (List.empty)

    let toSeq (ftl : FTList<'a>) : 'a seq =
        let unf (t : T<Atom<'a>>) =
            match T.viewL t with
            | Some struct(x, t') -> Some (x.V, t')
            | None -> None
        Seq.unfold unf (ftl.T) 

    let toArray (ftl : FTList<'a>) : 'a array =
        // we can directly allocate the array to a known size.
        let sz = length ftl
        if (sz > uint64 System.Int32.MaxValue) then invalidArg (nameof ftl) "list too large" else
        let arr = Array.zeroCreate (int sz)
        let mutable ix = 0
        let mutable t = toT ftl
        while (ix < arr.Length) do
            match T.viewL t with
            | Some struct(e, t') ->
                Array.set arr ix (e.V)
                ix <- ix + 1
                t <- t'
            | None ->  
                // this shouldn't happen...
                failwith "tree size or view is invalid"
        arr

    let inline splitAt n ftl =
        let struct(l,r) = T.splitAt n (toT ftl)
        (ofT l, ofT r)

    let inline take n ftl =
        fst (splitAt n ftl)
    
    let inline skip n ftl =
        snd (splitAt n ftl)

    let inline map fn ftl =
        let fn' e l = cons (fn e) l 
        foldBack fn' ftl empty

    /// FTList doesn't do logical reverse, so this is O(N).
    /// An advantage of FTList is that you can often avoid reverse.
    let rev ftl =
        let fn l e = cons e l
        fold fn empty ftl

    let inline item n x =
        (T.elemAt n (toT x)).V
    


