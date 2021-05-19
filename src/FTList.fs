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
        let rec chunkify<'V when 'V :> ISized> (lv : 'V list) : B<'V> list =
            match lv with
            | (b1::b2::rem) -> 
                match rem with 
                | [] -> (mkB2 b1 b2)::[] // 2 elems
                | (b3::[]) -> (mkB3 b1 b2 b3)::[] // 3 elems
                | (b3::b4::[]) -> (mkB2 b1 b2)::(mkB2 b3 b4)::[] // 4 elems
                | (b3::lv') -> (mkB3 b1 b2 b3)::(chunkify lv') // 5+ elems
            | _ -> invalidArg (nameof lv) "not enough data to chunkify" // 0 or 1 elems
            

    [<NoComparison; NoEquality>]
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

        let inline mkMany p f s =
            let sz = D.size p + isize f + D.size s
            Many (size=sz, prefix=p, finger=f, suffix=s)

        let ofD (d:D<'V>) : T<'V> =
            match d with
            | D1 d1 -> Single d1
            | D2 (d1,d2) -> mkMany (D1(d1)) Empty (D1(d2))
            | D3 (d1,d2,d3) -> mkMany (D1(d1)) Empty (D2(d2,d3))
            | D4 (d1,d2,d3,d4) -> mkMany (D2(d1,d2)) Empty (D2(d3,d4))

        let rec viewL<'V when 'V :> ISized> (t : T<'V>) : struct('V * T<'V>) option =
            match t with
            | Empty -> None
            | Single v -> Some (v, Empty)
            | Many (prefix=p; finger=f; suffix=s) ->
                match p with
                | D4 (d1, d2, d3, d4) -> Some struct(d1, mkMany (D3(d2,d3,d4)) f s)
                | D3 (d1, d2, d3) -> Some struct(d1, mkMany (D2(d2,d3)) f s)
                | D2 (d1, d2) -> Some struct(d1, mkMany (D1(d2)) f s)
                | D1 (d1) ->
                    match viewL f with
                    | Some struct(b, f') -> Some struct(d1, mkMany (B.toD b) f' s)
                    | None -> Some struct(d1, ofD s)
        
        let rec viewR<'V when 'V :> ISized> (t : T<'V>) : struct(T<'V> * 'V) option =
            match t with
            | Empty -> None
            | Single v -> Some struct(Empty, v)
            | Many (prefix=p; finger=f; suffix=s) ->
                match s with
                | D4 (d1, d2, d3, d4) -> Some struct(mkMany p f (D3(d1,d2,d3)), d4)
                | D3 (d1, d2, d3) -> Some struct(mkMany p f (D2(d1,d2)), d3)
                | D2 (d1, d2) -> Some struct(mkMany p f (D1(d1)), d2)
                | D1 (d1) ->
                    match viewR f with
                    | Some struct(f', b) -> Some struct(mkMany p f' (B.toD b), d1)
                    | None -> Some struct(ofD p, d1)

        let rec cons<'V when 'V :> ISized> (v : 'V) (t : T<'V>) : T<'V> =
            match t with
            | Empty -> Single v
            | Single v0 -> mkMany (D1(v)) Empty (D1(v0))
            | Many (prefix=p; finger=f; suffix=s) ->
                match p with
                | D4 (d1,d2,d3,d4) -> mkMany (D2(v,d1)) (cons (B.mkB3 d2 d3 d4) f) s
                | D3 (d1,d2,d3) -> mkMany (D4(v,d1,d2,d3)) f s
                | D2 (d1,d2) -> mkMany (D3(v,d1,d2)) f s
                | D1 (d1) -> mkMany (D2(v,d1)) f s

        let rec snoc<'V when 'V :> ISized> (t : T<'V>) (v : 'V) : T<'V> =
            match t with
            | Empty -> Single v
            | Single v0 -> mkMany (D1(v0)) Empty (D1(v))
            | Many (prefix=p; finger=f; suffix=s) ->
                match s with
                | D4 (d1,d2,d3,d4) -> mkMany p (snoc f (B.mkB3 d1 d2 d3)) (D2 (d4,v))
                | D3 (d1,d2,d3) -> mkMany p f (D4(d1,d2,d3,v))
                | D2 (d1,d2) -> mkMany p f (D3(d1,d2,v))
                | D1 (d1) -> mkMany p f (D2(d1,v))

        let inline ofList l =
            List.fold snoc Empty l

        /// Append two trees.
        let rec append<'V when 'V :> ISized> (l : T<'V>) (r : T<'V>) : T<'V> =
            match l with
            | Empty -> r
            | Single v -> cons v r
            | Many (prefix=pl; finger=fl; suffix=sl) ->
                match r with
                | Empty -> l
                | Single v -> snoc l v
                | Many (prefix=pr; finger=fr; suffix=sr) ->
                    let bc = B.chunkify (D.consDL sl (D.consDL pr List.empty))
                    let fl' = List.fold snoc fl bc
                    mkMany pl (append fl' fr) sr

        let rec private _splitListAcc<'V when 'V :> ISized> n acc l : struct ('V list * 'V * 'V list) =
            match l with
            | (x::xs) -> 
                let xsize = isize x
                if (xsize > n) then struct(List.rev l, x, xs) else
                _splitListAcc (n - xsize) (x::acc) xs
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
                            | Some struct(b, f') -> mkMany (B.toD b) f' s
                            | None -> ofD s
                        else mkMany (D.ofList r) f s
                    struct(ofList l, x, rt)
                else if n < pfz then // split f
                    let n' = n - pz
                    let struct(lf, b, rf) = _splitAt n' f
                    let struct(lb, x, rb) = _splitList (n' - isize lf) (B.toList b)
                    let lt = 
                        if List.isEmpty lb then
                            match viewR lf with
                            | Some struct(lf', b') -> mkMany p lf' (B.toD b')
                            | None -> ofD p
                        else mkMany p lf (D.ofList lb)
                    let rt =
                        if List.isEmpty rb then
                            match viewL rf with
                            | Some struct(b', rf') -> mkMany (B.toD b') rf' s
                            | None -> ofD s
                        else mkMany (D.ofList rb) rf s
                    struct(lt, x, rt)
                else // split s
                    let n' = n - pfz
                    let struct(l, x, r) = _splitList n' (D.toList s)
                    let lt = 
                        if List.isEmpty l then
                            match viewR f with
                            | Some struct(f', b) -> mkMany p f' (B.toD b)
                            | None -> ofD p
                        else mkMany p f (D.ofList l)
                    struct(lt, x, ofList r)
            | Empty -> failwith "inner split on empty tree; should be impossible"

        /// Split tree based on index. 
        let splitAt<'V when 'V :> ISized> (n : uint64) (t : T<'V>) : struct(T<'V> * T<'V>) =
            // ensure n < isize t for internal _splitAt
            if (n >= isize t) then struct(t, Empty) else
            let struct(l, x, r) = _splitAt n t
            assert((isize l = n) && (isize t = (1UL + n + isize r)))
            struct(l, cons x r)

        let rec fold fn st t =
            match viewL t with
            | Some struct(x, t') -> fold fn (fn st x) t'
            | None -> st

        let rec foldBack fn t st =
            match viewR t with
            | Some struct(t', x) -> foldBack fn t' (fn x st)
            | None -> st
    

open FT

/// An FTList is a list whose underlying finger-tree representation is
/// abstracted away, though it's still accessible.
///
/// ASIDE: 
/// F# has some very awkward handling of equality/comparison constraints,
/// especially in context of generics. For example, we cannot elide the
/// :equality constraint even with the EqualityConditionalOn attribute. 
[<Struct; CustomEquality; CustomComparison>]
type FTList<[<EqualityConditionalOn; ComparisonConditionalOn>] 'a when 'a :equality and 'a :comparison> = 
    val T: T<Atom<'a>> 
    new(t) = { T = t }
    override x.GetHashCode() =
        // based on fnv-1a hash function.
        let hmix (h : int) (a : Atom<'a>) : int = 16777619 * (h ^^^ hash (a.V))
        T.fold hmix (int 2166136261ul) (x.T)
    override x.Equals(yobj) =
        match yobj with
        | :? FTList<'a> as y -> (x :> System.IEquatable<_>).Equals(y)
        | _ -> false
    interface System.IEquatable<FTList<'a>> with
        member x.Equals(y) =
            let rec eqloop (l : T<Atom<'a>>) (r : T<Atom<'a>>) : bool =
                match T.viewL l, T.viewL r with
                | Some struct(l0,l'), Some struct(r0,r') ->
                    if (l0.V) <> (r0.V) then false else eqloop l' r'
                | None, None -> true
                | _ -> false
            eqloop (x.T) (y.T)
    interface System.IComparable with
        member x.CompareTo(yobj) =
            match yobj with
            | :? FTList<'a> as y -> (x :> System.IComparable<_>).CompareTo(y)
            | _ -> invalidArg (nameof yobj) "comparison of different types"
    interface System.IComparable<FTList<'a>> with
        member x.CompareTo(y) =
            let rec cmpLoop (l : T<Atom<'a>>) (r : T<Atom<'a>>) =
                match T.viewL l, T.viewL r with
                | Some struct(l0, l'), Some struct(r0,r') ->
                    let cmp = compare (l0.V) (r0.V)
                    if (0 <> cmp) then cmp else cmpLoop l' r'
                | Some _, None -> 1
                | None, Some _ -> -1
                | None, None -> 0
            cmpLoop (x.T) (y.T)

module FTList =

    let inline private mkList t = 
        FTList(t)

    let inline private toT (l : FTList<'a> ) : T<Atom<'a>> =
        l.T

    let private snocAtom t e = 
        T.snoc t (Atom(e))
    let ofList l = 
        mkList <| List.fold snocAtom Empty l
    let ofSeq s = 
        mkList <| Seq.fold snocAtom Empty s
    let ofArray a = 
        mkList <| Array.fold snocAtom Empty a

    let toList (ftl : FTList<'a>) : 'a list =
        let ca (e : Atom<'a>) l = e.V :: l
        T.foldBack ca (toT ftl) (List.empty)

    let toSeq (ftl : FTList<'a>) : 'a seq =
        let unf (t : T<Atom<'a>>) =
            match T.viewL t with
            | Some struct(x, t') -> Some (x.V, t')
            | None -> None
        Seq.unfold unf (ftl.T) 

    /// We can directly allocate an array of the correct size.
    // let toArray (ftl : FTList<'a>) : 'a array =




    /// Empty FTList. This is a function
    let inline empty() = 
        mkList Empty

    /// Length of FTLists, reported as a uint64.
    let inline length l = 
        isize (toT l)
    


