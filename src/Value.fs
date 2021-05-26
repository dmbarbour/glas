namespace Glas

// Glas values can directly be encoded as a node with two optional children:
//
//     type Value = { L: Value option; R: Value option }
//
// However, Glas often uses non-branching path segments to encode symbols or
// numbers. If an allocation is required per bit, this is much too inefficient.
// So, I favor a radix tree structure that compacts the non-branching stem:
//
//     type Value = { Stem: Bits; Term: (Value * Value) option }
//
// This is barely adequate, but inefficient if we use many ad-hoc list ops.
// Glas programs assume that lists are accelerated using a finger-tree encoding.
// To support this, we can represent structures of form `(A * (B * (C * D)))` as
// finger-tree lists. 
// 
//     type Value = { Stem: Bits; Spine: (Value * FTList<Value> * EndValue) option }
//
// This enables us to efficiently check whether a value is a list, and to manipulate
// list values with the expected algorithmic efficiencies. The EndValue type will
// ensure we have normal forms with no pair in the EndValue type. This does complicate
// some operations, but not too much.
// 
// This representation still excludes Stowage or rope-like chunks for binaries, so
// there is a lot that could be improved. But it seems adequate for bootstrap.


[<Struct>]
type Value = { Stem : Bits; Spine: Option<struct(Value * FTList<Value> * EndVal)> }
and [<Struct>] EndVal =
    val Value : Value
    new(v : Value) =
        if Bits.isEmpty v.Stem && Option.isSome v.Spine then 
            invalidArg (nameof v) "EndVal must not be a pair"
        { Value = v }

module Value =
    // intermediate construct to help insert/delete/pair ops
    let inline private _ofSE s e =
        match FTList.tryViewL s with
        | Some (s0, s') -> { Stem = Bits.empty; Spine = Some struct(s0,s',e) }
        | None -> e.Value

    // remove shared prefix without recording the prefix.
    let rec private dropSharedPrefix a b =
        let halt = Bits.isEmpty a || Bits.isEmpty b || (Bits.head a <> Bits.head b)
        if halt then struct(a, b) else
        dropSharedPrefix (Bits.tail a) (Bits.tail b) 

    /// The unit (1) value is represented by the single-element tree.
    let unit = 
        { Stem = Bits.empty; Spine = None }

    let inline isUnit v =
        Option.isNone (v.Spine) && Bits.isEmpty (v.Stem)

    /// Check for value equality. 
    let rec eq x y =
        if (x.Stem <> y.Stem) then false else
        match x.Spine, y.Spine with
        | Some struct(lx,sx,ex), Some struct(ly,sy,ey) ->
            (eq lx ly) && (eqList sx sy) && (eq ex.Value ey.Value)
        | None, None -> true
        | _ -> false
    and eqList x y =
        match FTList.tryViewL x, FTList.tryViewL y with
        | Some (x0, x'), Some(y0, y') -> (eq x0 y0) && (eqList x' y')
        | None, None -> true
        | _ -> false

    /// Compare values. Rather arbitrary.
    let rec cmp x y = 
        let cmpStem = Bits.cmp (x.Stem) (y.Stem)
        if (0 <> cmpStem) then cmpStem else
        match x.Spine, y.Spine with
        | Some struct(lx, sx, ex), Some struct(ly, sy, ey) ->
            let cmpL = cmp lx ly
            if (0 <> cmpL) then cmpL else
            let cmpS = cmpList sx sy 
            if (0 <> cmpS) then cmpS else
            cmp (ex.Value) (ey.Value)
        | l,r -> compare (Option.isSome l) (Option.isSome r)
    and cmpList x y =
        match FTList.tryViewL x, FTList.tryViewL y with
        | Some (x0,x'), Some (y0,y') ->
            let cmp0 = cmp x0 y0
            if (0 <> cmp0) then cmp0 else
            cmpList x' y'
        | l,r -> compare (Option.isSome l) (Option.isSome r)

    let inline private hmix h0 h =
        16777619 * (h0 ^^^ h) // FNV-1a

    /// ad-hoc hash function for use with hashtables
    let rec vhash (v : Value) : int =
        match (v.Spine) with
        | None -> hmix (hash v.Stem) 0
        | Some struct(l, s, e) ->
            let h0 = hmix (hash v.Stem) (2 + FTList.length s)
            let hl = hmix h0 (vhash l)
            let hs = FTList.fold (fun h x -> hmix h (vhash x)) hl s
            hmix hs (vhash e.Value)

    /// A node with two children can represent a pair (A * B).
    /// To support list processing, there is some special handling of pairs.
    /// A list is essentially a right spine of pairs (A * (B * (C * (...)))).
    let inline isPair v =
        Option.isSome (v.Spine) && Bits.isEmpty (v.Stem)

    let inline private tryPairSpine v =
        if Bits.isEmpty v.Stem then v.Spine else None

    let inline (|Pair|_|) v = 
        match tryPairSpine v with
        | Some struct(l, s, e) -> Some (l, _ofSE s e)
        | None -> None

    let pair a b =
        match tryPairSpine b with 
        | Some struct(l,s,e) -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.cons l s, e) }
        | None -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.empty, EndVal(b)) }

    let fst v =
        match tryPairSpine v with
        | Some struct(v0,_,_) -> v0
        | None -> invalidArg "v" "not a pair"

    let tryFst v =
        match tryPairSpine v with
        | Some struct(v0,_,_) -> Some v0
        | None -> None
    
    let snd v =
        match tryPairSpine v with
        | Some struct(_, s, e) -> _ofSE s e 
        | None -> invalidArg "v" "not a pair"

    let trySnd v =
        match tryPairSpine v with
        | Some struct(_, s, e) -> Some (_ofSE s e) 
        | None -> None

    /// We can represent basic sum types (A + B) as a node with a single child.
    /// Glas mostly uses labeled variants instead, but this is illustrative.
    let inline left a = 
        { a with Stem = Bits.cons false (a.Stem) }

    let inline isLeft v =
        if Bits.isEmpty v.Stem then false else (false = (Bits.head v.Stem))

    let inline (|Left|_|) v =
        if isLeft v then Some { v with Stem = Bits.tail v.Stem } else None

    /// The right sum adds a `1` prefix to an existing value.
    let inline right b = 
        { b with Stem = Bits.cons true (b.Stem) }

    let inline isRight v = 
        if Bits.isEmpty v.Stem then false else (true = (Bits.head v.Stem))

    let inline (|Right|_|) v = 
        if isRight v then Some { v with Stem = Bits.tail v.Stem } else None

    /// Any bitstring can be a value. Glas uses bitstrings for numbers and
    /// labels, but not for binaries. Binaries are encoded as a list of bytes.
    let inline ofBits b =
        { Stem = b; Spine = None }

    let inline isBits v = 
        Option.isNone v.Spine

    let (|Bits|_|) v =
        if isBits v then Some v.Stem else None

    /// We can encode a byte as a short bitstring.
    let inline u8 (n : uint8) : Value = 
        Bits.ofByte n |> ofBits
    
    let inline (|U8|_|) v =
        if isBits v then Bits.(|Byte|_|) v.Stem else None

    let inline u16 (n : uint16) : Value = 
        Bits.ofU16 n |> ofBits

    let inline (|U16|_|) v =
        if isBits v then Bits.(|U16|_|) v.Stem else None

    let inline u32 (n : uint32) : Value =
        Bits.ofU32 n |> ofBits

    let inline (|U32|_|) v =
        if isBits v then Bits.(|U32|_|) v.Stem else None

    let inline u64 (n : uint64) : Value =
        Bits.ofU64 n |> ofBits

    let inline (|U64|_|) v = 
        if isBits v then Bits.(|U64|_|) v.Stem else None

    let inline nat n =
        Bits.ofNat64 n |> ofBits
    
    let inline (|Nat|_|) v =
        if isBits v then Bits.(|Nat64|_|) v.Stem else None

    // factored from 'label' and 'variant'
    let private consLabel (s : string) (b : Bits) : Bits =
        let strbytes = System.Text.Encoding.UTF8.GetBytes(s)
        Array.foldBack Bits.consByte strbytes (Bits.consByte 0uy b)

    /// Labeled variants and records are better than sums and pairs because
    /// they are openly extensible and self-documenting. In Glas, we encode
    /// labels as null-terminated UTF-8 bitstrings.
    let label (s : string) : Bits =
        consLabel s Bits.empty

    /// A variant is modeled by prefixing a value with a label.
    let variant s v =
        { v with Stem = (consLabel s v.Stem) }

    /// A symbol is modeled as a variant with the unit value.
    let inline symbol s =
        variant s unit


    let tryMatchStem p v = 
        let struct(p', stem') = dropSharedPrefix p (v.Stem)
        if Bits.isEmpty p' then Some { v with Stem = stem' } else None

    /// Attempt to match bitstring prefix with value. This corr
    let inline (|Stem|_|) (p : Bits) (v : Value) : Value option =
        tryMatchStem p v

    /// Prefixed with a string label.
    let inline (|Variant|_|) (s : string) (v : Value) : Value option =
        tryMatchStem (label s) v

    let rec private accumSharedPrefixLoop acc a b =
        let halt = Bits.isEmpty a || Bits.isEmpty b || (Bits.head a <> Bits.head b)
        if halt then struct(acc, a, b) else
        accumSharedPrefixLoop (Bits.cons (Bits.head a) acc) (Bits.tail a) (Bits.tail b)
    
    // returns a triple with (reversed shared prefix, remainder of a, remainder of b)
    let inline private findSharedPrefix a b = 
        accumSharedPrefixLoop (Bits.empty) a b

    /// Access a value within a record. Essentially a radix tree lookup.
    let rec record_lookup (p:Bits) (r:Value) : Value option =
        let struct(p',stem') = dropSharedPrefix p (r.Stem)
        if (Bits.isEmpty p') then Some { r with Stem = stem' } else
        if not (Bits.isEmpty stem') then None else
        match r.Spine with
        | Some struct(l, s, e) -> _rlu_spine p' l s e
        | None -> None
    and private _rlu_spine p l s e =
        let p' = Bits.tail p
        if not (Bits.head p) then record_lookup p' l else
        match FTList.tryViewL s with
        | None -> record_lookup p' (e.Value)
        | Some (l', s') -> 
            if not (Bits.isEmpty p') then _rlu_spine p' l' s' e else 
            Some { Stem = Bits.empty; Spine = Some struct(l', s', e) } 


    // same as `Bits.append (Bits.rev a) b`.
    let inline private _bitsAppendRev a v =
        { v with Stem = Bits.fold (fun acc e -> Bits.cons e acc) (v.Stem) a }

    let rec record_delete (p:Bits) (r:Value) : Value =
        // handle shared prefix of p and r
        let struct(common, p', stem') = findSharedPrefix p (r.Stem)
        let p_deleted = _rdel_diff p' { r with Stem = stem' }
        if isUnit p_deleted then unit else
        _bitsAppendRev common p_deleted
    and _rdel_diff p r = 
        if Bits.isEmpty p then unit else
        if not (Bits.isEmpty (r.Stem)) then r else
        match r.Spine with
        | Some struct(l, s, e) ->
            let p' = Bits.tail p
            if not (Bits.head p) then // delete left (false)
                let l' = record_delete p' l
                if isUnit l' then right (_ofSE s e) else 
                { unit with Spine = Some struct(l', s, e) }
            else // delete right (true)
                // note: potentially optimize to avoid _ofSE
                let se' = record_delete p' (_ofSE s e)
                if isUnit se' then left l else pair l se'
        | None -> unit 


    let rec record_insert (p:Bits) (v:Value) (r:Value) : Value =
        // handle the shared prefix of p and r
        let struct(common, p', stem') = findSharedPrefix p (r.Stem)
        _bitsAppendRev common (_rins_diff p' v { r with Stem = stem' } )
    and _rins_diff p v r =
        if Bits.isEmpty p then v else
        if not (Bits.isEmpty (r.Stem)) then
            // new branch node in record 
            assert (Bits.head p <> Bits.head (r.Stem))
            let v' = { v with Stem = Bits.append (Bits.tail p) (v.Stem) }
            let r' = { r with Stem = (Bits.tail (r.Stem)) } 
            if Bits.head p then pair r' v' else pair v' r'
        else
            // insert follows branch
            match r.Spine with
            | Some struct(l, s, e) ->
                let p' = Bits.tail p
                if not (Bits.head p) then // insert left (false)
                    let l' = record_insert p' v l
                    { unit with Spine = Some struct(l', s, e) }
                else // insert right (true)
                    // note: potentially optimize to avoid _ofSE
                    pair l (record_insert p' v (_ofSE s e))
            | None -> { v with Stem = Bits.append p (v.Stem) } 



    // edge is `01` for left, `10` for right. accum in reverse order
    let inline private _key_edge acc e =
        if e 
            then (Bits.cons false (Bits.cons true acc)) // right acc10 
            else (Bits.cons true (Bits.cons false acc)) // left acc01 

    let rec private _key_val bi br v =
        let bi_stem = Bits.fold _key_edge bi (v.Stem)
        match v.Spine with
        | None -> 
            let bi' = (Bits.cons false (Bits.cons false bi_stem)) // leaf
            match br with
            | (v'::br') -> _key_val bi' br' v'
            | [] -> bi'
        | Some struct(v', s, e) ->
            let br' = (_ofSE s e)::br
            let bi' = (Bits.cons true (Bits.cons true bi_stem)) // branch
            _key_val bi' br' v'

    /// Translate value to bitstrings with unique prefix property, suitable
    /// for use as a record key. 
    ///
    /// This uses a naive encoding:
    /// 
    ///   00 - leaf
    ///   01 - left (followed by node reached)
    ///   10 - right (followed by node reached)
    ///   11 - branch (followed by left then right)
    ///
    /// This means we have two bits per node in the value. Also, there is no 
    /// implicit sharing of internal structure. The assumption is that keys 
    /// are relatively small values where this is not a significant concern.
    let toKey (v : Value) : Bits =
        Bits.rev (_key_val (Bits.empty) (List.empty) v)

    let rec private _key_stem sb k =
        match k with
        | Bits.Cons (false, Bits.Cons (true, k')) -> _key_stem (Bits.cons false sb) k'
        | Bits.Cons (true, Bits.Cons (false, k')) -> _key_stem (Bits.cons true sb) k'
        | _ -> struct(Bits.rev sb, k)

    // note: this isn't tail recursive and might bust the stack if we have
    // a very 'deep' key, including a long list.
    let rec private _key_parse k =
        let struct(stem, kim) = _key_stem (Bits.empty) k
        match kim with
        | Bits.Cons (false, Bits.Cons (false, k')) -> 
            Some struct(ofBits stem, k')
        | Bits.Cons (true, Bits.Cons (true, kl)) ->
            match _key_parse kl with
            | None -> None
            | Some struct(l, kr) ->
                match _key_parse kr with
                | None -> None
                | Some struct(r, k') ->
                    let v = { (pair l r) with Stem = stem }
                    Some struct(v, k')
        | _ -> None

    /// Parse a key back into a value. This may fail, raising invalidArg
    let ofKey (b : Bits) : Value =
        match _key_parse b with
        | Some struct(v, _) -> v
        | None -> invalidArg (nameof b) "not a valid key"

    /// Glas logically encodes lists using pairs terminating in unit.
    ///
    ///    (A * (B * (C * (D * (E * ()))))).
    /// 
    /// This check can be performed in O(1) time based on the finger-tree
    /// representation of list-like structures. We only need to check if
    /// the EndVal is unit instead of some non-list terminal.
    let isList (v : Value) =
        if not (Bits.isEmpty v.Stem) then false else
        match v.Spine with
        | None -> true
        | Some struct(_,_,e) -> isUnit e.Value

    let tryFTList v =
        if not (Bits.isEmpty v.Stem) then None else
        match v.Spine with
        | Some struct(l,s,e) when isUnit e.Value -> Some (FTList.cons l s)
        | None -> Some (FTList.empty)
        | _ -> None

    let inline (|FTList|_|) v =
        tryFTList v

    let inline toFTList (v : Value) : FTList<Value> =
        match tryFTList v with
        | Some f -> f
        | None -> invalidArg (nameof v) "not a list"

    /// We can directly convert from an FTList of values. O(1)
    let ofFTList (fv : FTList<Value>) : Value =
        match FTList.tryViewL fv with
        | None -> unit // empty list
        | Some (fv0, fv') ->
            { Stem = Bits.empty; Spine = Some struct(fv0, fv', EndVal(unit)) }

    /// Convert from a binary 
    let ofBinary (s : uint8 array) : Value =
        let fn e l = pair (u8 e) l
        Array.foldBack fn s unit

    let toBinary (v : Value) : uint8 array =
        let mutable f = toFTList v
        let sz = FTList.length f
        let arr = Array.zeroCreate sz
        let mutable ix = 0
        while (ix < sz) do
            match FTList.tryViewL f with
            | Some (U8 b, f') ->
                Array.set arr ix b
                ix <- ix + 1
                f <- f'
            | _ -> invalidArg (nameof v) "non-binary data"
        arr


    /// Convert from a string, using UTF-8. 
    /// Glas uses UTF-8 as its common text encoding.
    let ofString (s : string) : Value =
        ofBinary (System.Text.Encoding.UTF8.GetBytes(s))

