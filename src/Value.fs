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
// This is barely adequate, but Glas programs assume that lists are accelerated 
// using a finger-tree encoding for efficient split, append, and indexing. To 
// support this, we can represent structures of form `(A * (B * (C * D)))` as
// finger-tree lists, restricting the final value to be a non-pair. 
// 
//     type Value = { Stem: Bits; Spine: (Value * FTList<Value> * NonPairVal) option }
//
// This enables us to efficiently check whether a value is a list, and to manipulate
// list values with expected algorithmic efficiencies.
//
// This representation still excludes support for Stowage or rope-like chunks for 
// binaries, much less support for acceleration of matrix math and so on. However,
// this should be adequate for bootstrap. I'd prefer to shove the more advanced 
// value representation features to post-bootstrap.

[<Struct; CustomComparison; CustomEquality>]
type Value = 
    { Stem : Bits
    ; Spine: Option<struct(Value * FTList<Value> * NonPairVal)> 
    }

    // Custom Equality and Comparison
    static member private OfSE s e =
        match FTList.tryViewL s with
        | Some (s0, s') -> { Stem = Bits.empty; Spine = Some struct(s0,s',e) }
        | None -> e.Value

    static member private Cmp rs x y = 
        let cmpStem = Bits.cmp (x.Stem) (y.Stem)
        if 0 <> cmpStem then cmpStem else
        if LanguagePrimitives.PhysicalEquality x.Spine y.Spine then 
            // this includes None None and fast-matches subtrees by ref.
            match rs with
            | (struct(x',y')::rs') -> Value.Cmp rs' x' y'
            | [] -> 0
        else 
            match x.Spine, y.Spine with
            | Some struct(x', sx, ex), Some struct(y', sy, ey) ->
                let rs' = struct(Value.OfSE sx ex, Value.OfSE sy ey) :: rs
                Value.Cmp rs' x' y'
            | l,r -> 
                assert (Option.isSome l <> Option.isSome r)
                compare (Option.isSome l) (Option.isSome r)

    static member private Eq rs x y =
        if (x.Stem <> y.Stem) then false else
        if LanguagePrimitives.PhysicalEquality x.Spine y.Spine then
            // this includes the None None case and fast-matches subtrees by ref 
            match rs with
            | (struct(x',y')::rs') -> Value.Eq rs' x' y'
            | [] -> true
        else
            match x.Spine, y.Spine with
            | Some struct(x',sx,ex), Some struct(y',sy,ey) ->
                let rs' = struct(Value.OfSE sx ex, Value.OfSE sy ey) :: rs
                Value.Eq rs' x' y'
            | l,r -> assert (Option.isSome l <> Option.isSome r); false

    override x.Equals yobj =
        match yobj with
        | :? Value as y -> Value.Eq (List.empty) x y
        | _ -> false

    interface System.IEquatable<Value> with
        member x.Equals y = 
            Value.Eq (List.empty) x y

    interface System.IComparable with
        member x.CompareTo yobj =
            match yobj with
            | :? Value as y -> Value.Cmp (List.empty) x y
            | _ -> invalidArg (nameof yobj) "Comparison between Value and Non-Value"

    interface System.IComparable<Value> with
        member x.CompareTo y =
            Value.Cmp (List.empty) x y        

    static member inline private HMix a b = 
        16777619 * (a ^^^ b) // FNV-1a

    static member private Hash rs h v = 
        match (v.Spine) with
        | None ->
            let h' = (hash v.Stem) |> Value.HMix h |> Value.HMix 0
            match rs with
            | (r::rs') -> Value.Hash rs' h' r
            | [] -> h'
        | Some struct(l, s, e) ->
            let h' = (hash v.Stem) |> Value.HMix h |> Value.HMix 1
            let rs' = (Value.OfSE s e)::rs
            Value.Hash rs' h' l

    override x.GetHashCode() =
        Value.Hash (List.empty) (int 2166136261ul) x 


and [<Struct>] NonPairVal = 
    // restriction via smart constructor
    val Value : Value
    new(v : Value) =
        if Option.isSome v.Spine && Bits.isEmpty v.Stem then 
            invalidArg (nameof v) "NonPairVal must not be a pair"
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

    /// A node with two children can represent a pair (A * B).
    /// To support list processing, there is some special handling of pairs.
    /// A list is essentially a right spine of pairs (A * (B * (C * (...)))).
    let inline isPair v =
        Option.isSome (v.Spine) && Bits.isEmpty (v.Stem)

    let inline private tryPairSpine v =
        if Bits.isEmpty v.Stem then v.Spine else None

    let pair a b =
        match tryPairSpine b with 
        | Some struct(l,s,e) -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.cons l s, e) }
        | None -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.empty, NonPairVal(b)) }

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
        if Bits.isEmpty v.Stem then false else (not (Bits.head v.Stem))


    /// The right sum adds a `1` prefix to an existing value.
    let inline right b = 
        { b with Stem = Bits.cons true (b.Stem) }

    let inline isRight v = 
        if Bits.isEmpty v.Stem then false else (Bits.head v.Stem)

    /// pattern matching support on a single node.
    let (|L|R|P|U|) v =
        if Bits.isEmpty v.Stem then 
            match v.Spine with
            | Some struct(l,s,e) -> P (l, _ofSE s e)
            | None -> U
        else
            let hd = Bits.head v.Stem
            let v' = { v with Stem = Bits.tail v.Stem }
            if hd then R v' else L v'

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

    let asRecord ks vs = 
        let addElem r k v = record_insert (label k) v r
        List.fold2 addElem unit ks vs

    /// Record with optional keys.
    let (|Record|) ks r =
        let lks = List.map label ks
        let vs = List.map (fun k -> record_lookup k r) lks
        let r' = List.fold (fun s k -> record_delete k s) r lks
        (vs,r')

    /// Record containing all listed keys.
    let (|FullRec|_|) ks r =
        let (vs, r') = (|Record|) ks r
        if List.exists Option.isNone vs then None else
        Some (List.map Option.get vs, r')

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
        | _ -> struct(sb, k)
    let rec private _key_parse st k =
        let struct(rstem, kim) = _key_stem (Bits.empty) k
        match kim with
        | Bits.Cons (false, Bits.Cons (false, k')) -> 
            _key_term st (ofBits (Bits.rev rstem)) k'
        | Bits.Cons (true, Bits.Cons (true, k')) ->
            let st' = struct(rstem, None) :: st
            _key_parse st' k'
        | _ -> None
    and private _key_term st v k =
        match st with
        | [] -> Some struct(v, k)
        | (struct(rstem, None) :: stRem) -> 
            let st' = struct(rstem, Some v) :: stRem
            _key_parse st' k
        | (struct(rstem, Some l) :: st') ->
            let p = _bitsAppendRev rstem (pair l v)
            _key_term st' p k

    /// Parse key to value, return remaining bits.
    let ofKey' b =
        _key_parse (List.empty) b

    /// Parse a key back into a value. This may fail, raising invalidArg
    let inline ofKey (b : Bits) : Value =
        match ofKey' b with
        | Some struct(v, rem) when Bits.isEmpty rem -> v
        | _ -> invalidArg (nameof b) "not a valid key"


    /// Glas logically encodes lists using pairs terminating in unit.
    ///
    ///    (A * (B * (C * (D * (E * ()))))).
    /// 
    /// This check can be performed in O(1) time based on the finger-tree
    /// representation of list-like structures. We only need to check if
    /// the NonPairVal is unit instead of some non-list terminal.
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
            { Stem = Bits.empty; Spine = Some struct(fv0, fv', NonPairVal(unit)) }

    /// Convert from a binary 
    let ofBinary (s : uint8 array) : Value =
        let fn e l = pair (u8 e) l
        Array.foldBack fn s unit

    let rec private _tryBinary l arr ix =
        match FTList.tryViewL l with
        | Some (U8 n, l') -> Array.set arr ix n; _tryBinary l' arr (ix + 1)
        | None when (Array.length arr = ix) -> Some arr
        | _ -> None

    let tryBinary (v : Value) : uint8 array option =
        match v with 
        | FTList l -> 
            let len = FTList.length l
            if len > uint64 System.Int32.MaxValue then None else
            let arr = Array.zeroCreate (int len) 
            _tryBinary l arr 0
        | _ -> None

    let inline (|Binary|_|) v = 
        tryBinary v

    let toBinary v =
        match v with
        | Binary arr -> arr
        | _ -> invalidArg (nameof v) "value is not a binary"


