namespace Glas

module Value =

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
    // list values with expected algorithmic efficiencies. But it is still missing some
    // desirable features, such as rope-like compact representation of binary data and
    // content-addressed storage (stowage) of large data.
    //
    // This representation can be taken as a proof-of-concept, and perhaps is adequate
    // for bootstrap. If performance is not sufficient, we could perhaps model large 
    // values using objects instead, applying compact representations where feasible.
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

    let vfst v =
        match tryPairSpine v with
        | Some struct(v0,_,_) -> v0
        | None -> invalidArg "v" "not a pair"

    let tryVFst v =
        match tryPairSpine v with
        | Some struct(v0,_,_) -> Some v0
        | None -> None
    
    let vsnd v =
        match tryPairSpine v with
        | Some struct(_, s, e) -> _ofSE s e 
        | None -> invalidArg "v" "not a pair"

    let tryVSnd v =
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
    
    let (|U8|_|) v =
        if isBits v then Bits.(|Byte|_|) v.Stem else None

    let inline nat n =
        Bits.ofNat64 n |> ofBits
    
    let (|Nat|_|) v =
        match v with
        | Bits b when (Bits.isEmpty b) || (Bits.head b) ->
            Bits.(|Nat64|_|) b
        | _ -> None

    let ofI = Bits.ofI >> ofBits
    let (|I|_|) v =
        match v with
        | Bits b when (Bits.isEmpty b) || (Bits.head b) ->
            Some (Bits.toI b)
        | _ -> None

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

    /// Attempt to match bitstring prefix with value. 
    let inline (|Stem|_|) (p : Bits) (v : Value) : Value option =
        tryMatchStem p v

    /// Prefixed with a specific string label.
    let inline (|Variant|_|) (s : string) : Value -> Value option =
        tryMatchStem (label s)


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

    let record_set k vOpt r =
        match vOpt with
        | Some v -> record_insert k v r
        | None -> record_delete k r

    let asRecord ks vs = 
        let addElem r k v = record_insert (label k) v r
        List.fold2 addElem unit ks vs

    let ofMap (m:Map<string,Value>) : Value =
        let addElem r k v = record_insert (label k) v r
        Map.fold addElem unit m

    let (|RecL|) lks r = 
        let vs = List.map (fun k -> record_lookup k r) lks
        let r' = List.fold (fun s k -> record_delete k s) r lks
        (vs,r')

    /// Record with optional keys.
    let inline (|Record|) ks =
        (|RecL|) (List.map label ks)

    /// Record containing all listed keys.
    let (|FullRec|_|) ks r =
        let (vs, r') = (|Record|) ks r
        if List.exists Option.isNone vs then None else
        Some (List.map Option.get vs, r')


    let private isFlagField opt = 
        match opt with
        | Some U | None -> true
        | _ -> false

    /// Extract flags as booleans. A flag is a label within a record whose only data
    /// is presence vs. absence.  
    let (|Flags|_|) ks r =
        let (vs, r') = (|Record|) ks r
        if List.exists (isFlagField >> not) vs then None else
        Some (List.map Option.isSome vs, r')


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

    let ofList (lv : Value list) : Value =
        lv |> FTList.ofList |> ofFTList

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


    // conversions to/from strings. Assumes UTF-8

    let ofString (s : string) : Value =
        ofBinary (System.Text.Encoding.UTF8.GetBytes(s))

    let tryString (v : Value) : string option =
        match v with
        | Binary b ->
            try 
                let enc = System.Text.UTF8Encoding(false,true)
                enc.GetString(b) |> Some
            with
            | _ -> None
        | _ -> None

    let inline (|String|_|) v = 
        tryString v
    
    let toString v =
        match v with
        | String s -> s
        | _ -> invalidArg (nameof v) "value is not a string"


    let rec private _isRecord rs xct b v =
        if ((b &&& 0x100) = 0x100) then
            if(xct > 0) then
                // expecting bit pattern '10xx xxxx'
                if((b &&& 0b11000000) = 0b10000000)
                    then _isRecord rs (xct - 1) 1 v
                    else false 
            else if(b = 0x100) then
                // null-terminated label.
                match rs with
                | struct(xct', b', v')::rs' -> _isRecord rs' xct' b' v' 
                | [] -> true // final label
            else if((b &&& 0b10000000) = 0b00000000) then
                _isRecord rs 0 1 v   // 1-byte utf-8
            else if((b &&& 0b11100000) = 0b11000000) then
                _isRecord rs 1 1 v   // 2-byte utf-8
            else if((b &&& 0b11110000) = 0b11100000) then
                _isRecord rs 2 1 v   // 3-byte utf-8
            else if((b &&& 0b11111000) = 0b11110000) then
                _isRecord rs 3 1 v   // 4-byte utf-8
            else false
        else 
            match v with
            | U -> false // label is not 8-bit aligned
            | L v' -> _isRecord rs xct (b <<< 1) v'
            | R v' -> _isRecord rs xct ((b <<< 1) ||| 1) v'
            | P (l,r) -> 
                let rs' = struct(xct, ((b <<< 1) ||| 1), r)::rs
                _isRecord rs' xct (b <<< 1) l

    /// Check that a value is a record with null-terminated UTF-8 labels.
    /// Rejects the empty symbol and C0 or DEL characters. I'm wondering
    /// if I should restrict the symbols to UTF-8 identifiers.
    ///
    /// Note: the UTF-8 check currently allows overlong encodings. This is
    /// not ideal, but the extra logic isn't too important for a bootstrap
    /// implementation.
    let isRecord (v:Value) : bool =
        if isUnit v then true else
        if Option.isSome (record_lookup (Bits.ofByte 0uy) v) then false else
        _isRecord [] 0 1 v

    let rec private _recordBytes m ct b v =
        if(8 = ct) then Map.add b v m else
        match v with
        | U -> m // incomplete byte. Just drop it.
        | L v' -> _recordBytes m (1+ct) (b <<< 1) v'
        | R v' -> _recordBytes m (1+ct) ((b <<< 1) ||| 1uy) v'
        | P (lv,rv) -> // max 8 stack depth, so use direct recursion
            let ct' = 1 + ct
            let lm = _recordBytes m ct' (b <<< 1) lv
            _recordBytes lm ct' ((b <<< 1) ||| 1uy) rv

    /// Obtain byte-aligned keys from a record. Drops any partial bytes. 
    /// This is a helper function to iterate symbolic keys in the record.
    let recordBytes (r:Value) : Map<uint8, Value> =
        _recordBytes (Map.empty) 0 0uy r

    let rec private _seqSym ss  =
        match ss with
        | [] -> None
        | struct(p, m)::ssRem ->
            match Map.tryFindKey (fun _ _ -> true) m with
            | None -> _seqSym ssRem // done with p
            | Some 0uy -> // null terminator for key string
                let ss' = struct(p, Map.remove 0uy m)::ssRem
                let pb = p |> List.toArray |> Array.rev
                let sym = System.Text.Encoding.UTF8.GetString(pb);
                Some ((sym, Map.find 0uy m), ss')
            | Some b -> 
                let ss' = struct(b::p, recordBytes (Map.find b m))::
                          struct(p, Map.remove b m)::ssRem
                _seqSym ss'

    /// Assuming value is a valid record, lazily return all key-value pairs.
    let recordSeq (r:Value) : (string * Value) seq =
        Seq.unfold _seqSym [struct(List.empty, recordBytes r)]

    /// Match a record as a map of strings.
    let tryRecord v =
        if not (isRecord v) then None else
        Some (Map.ofSeq (recordSeq v))

    /// Match record as a map.
    let (|RecordMap|_|) v =
        tryRecord v

    /// Match any variant, returning label and value.
    let (|AnyVariant|_|) v =
        match v with
        | RecordMap m when (1 = Map.count m) ->
            Map.toList m |> List.head |> Some
        | _ -> None

    let inline private _toHex n =
        if (n < 10) 
            then char (int '0' + n) 
            else char (int 'A' + (n - 10))

    // roll my own string escape for pretty-printing strings 
    let private _escape (s0 : string) : string =
        let sb = System.Text.StringBuilder()
        for c in s0 do
            match c with
            | '\\' -> ignore <| sb.Append("\\\\")
            | '"'  -> ignore <| sb.Append("\\\"")
            | '\x7F' -> ignore <| sb.Append("\\x7F")
            | _ when (int c >= 32) -> ignore <| sb.Append(c)
            | '\n' -> ignore <| sb.Append("\\n")
            | '\r' -> ignore <| sb.Append("\\r")
            | '\t' -> ignore <| sb.Append("\\t")
            | '\a' -> ignore <| sb.Append("\\a")
            | '\b' -> ignore <| sb.Append("\\b")
            | '\f' -> ignore <| sb.Append("\\f")
            | '\v' -> ignore <| sb.Append("\\v")
            | _ -> 
                ignore <| sb.Append("\\x")
                            .Append(_toHex ((0xF0 &&& int c) >>> 4))
                            .Append(_toHex ((0x0F &&& int c) >>> 0))
        sb.ToString()


    let rec private _ppV (bLR : bool) (v:Value) : string seq = 
        seq {
            match v with
            | U -> yield "()"
            | _ when isRecord v ->
                let kvSeq = recordSeq v
                if Seq.isEmpty (Seq.tail kvSeq) then
                    // variant or symbol (a singleton record)
                    if bLR then
                        yield "."
                    yield! _ppKV (Seq.head kvSeq)
                else
                    yield "("
                    yield! _ppKV (Seq.head kvSeq)
                    yield! kvSeq |> Seq.tail |> Seq.collect (fun kv -> seq { yield ", "; yield! _ppKV kv})
                    yield ")"
            | String s ->
                yield "\""
                yield _escape s
                yield "\""
            | FTList l ->
                yield "["
                match l with
                | FTList.ViewL (v0, l') ->
                    yield! _ppV false v0
                    yield! FTList.toSeq l' |> Seq.collect (fun v -> seq { yield ", "; yield! _ppV false v})
                | _ -> ()
                yield "]"
            | (Bits b) ->
                assert(not bLR)
                if Bits.isEmpty b then yield "0" else
                if (Bits.head b) then yield (Bits.toI b).ToString() else
                yield "0b"
                for bit in Bits.toSeq b do
                    if bit then yield "1" else yield "0"
            | P (a,b) ->
                yield "("
                yield! _ppV false a
                yield ", "
                yield! _ppV false b
                yield ")"
            | L v ->
                if not bLR then 
                    yield "~"
                yield "0"
                yield! _ppV true v
            | R v ->
                if not bLR then 
                    yield "~"
                yield "1"
                yield! _ppV true v
        }
    and private _ppKV (k:string,v:Value) : string seq = 
        seq {
            yield k
            if not (isUnit v) then
                yield ":"
                yield! _ppV false v
        }

    /// Pretty Printing of Values. (dubiously pretty) 
    ///
    /// Heuristic Priorities:
    ///   - unit
    ///   - records/variants/symbol
    ///   - strings and lists of values
    ///   - numbers/bitstrings
    ///   - pairs and sums.
    ///
    /// I should probably restrict size of what I write, but it's low
    /// priority for now. This is mostly for debugging and initial log
    /// outputs.
    /// 
    /// A precise pretty printer should use a type description as an
    /// additional value input. This one is more heuristic, but is ok
    /// for most problems. The main issue is that some numbers might 
    /// render as symbols by accident.
    let prettyPrint (v:Value) : string =
        v |> _ppV false |> String.concat "" 


    // TODO:

    // Dictionary Compression Pretty-Printing.
    // 
    // For larger values, it would be useful to present the value as a 
    // dictionary where shared structure (beyond a threshold size) is 
    // presented using a generated dictionary. 
    //
    // This is low priority for now, but might become relevant when I'm
    // debugging large values. It's also feasible to solve this within
    // Glas, e.g. as part of `std.print`. Might be better to defer.
    // 
    // A related option is to represent a large value as a program that
    // would reproduce the value. Obviously we could write `data:Value`,
    // but we could use compression mechanisms on the value.

type Value = Value.Value

// Developing alternative value model aligned to Glob representation
module GlobValue =

    // immutable arrays with efficient slicing
    [<Struct; NoEquality; NoComparison>] // todo: CustomEquality/Comparison. IEnumerable.
    type ImmArray<'T> =
        val private Data : 'T array
        val private Offset : int
        val Length : int

        // constructors are private; use static methods instead.
        private new(data : 'T array, offset, length) =
            { Data = data; Offset = offset; Length = length }
        private new(data : 'T array) =
            { Data = data; Offset = 0; Length = data.Length }

        static member Empty =
            ImmArray(Array.empty)
        static member OfArray(data : 'T array) =
            ImmArray(Array.copy data)
        static member OfArraySlice(data : 'T array, offset, len) =
            let ok = ((len >= 0) && (offset >= 0) && ((data.Length - offset) >= len))
            if not ok then failwith "invalid Array slice" else
            let arr = Array.zeroCreate len
            Array.blit data offset arr 0 len
            ImmArray(arr)
        static member OfSeq(dataSeq : 'T seq) =
            ImmArray(Array.ofSeq dataSeq)

        // private constructors are accessible directly via 'Unsafe' methods.
        // The intention here is to make the 'Unsafe' qualifier more visible.
        // Ideally, caller should ensure array is not shared. 
        static member UnsafeOfArray(data : 'T array) =
            ImmArray(data)
        static member UnsafeOfArraySlice(data : 'T array, offset, len) =
            assert((offset >= 0) && (len >= 0) && ((data.Length - offset) >= len))
            ImmArray(data, offset, len)

        member a.Item
            with get(ix) = 
                let ok = ((ix >= 0) && (ix < a.Length))
                if not ok then failwith "invalid index" else
                a.Data.[a.Offset + ix]
        member a.Take(len) =
            let ok = (a.Length >= len) && (len >= 0)
            if not ok then failwith "invalid slice (take)" else
            ImmArray(a.Data, a.Offset, len)
        member a.Drop(ct) =
            let ok = (a.Length >= ct) && (ct >= 0)
            if not ok then failwith "invalid slice (drop)" else
            ImmArray(a.Data, a.Offset + ct, a.Length - ct)
        member a.Slice(offset, len) =
            a.Drop(offset).Take(len)
        member a.GetSlice(optIxStart, optIxEnd) = // support for .[1..3] slices.
            let ixStart = Option.defaultValue 0 optIxStart
            let ixEnd = Option.defaultValue (a.Length - 1) optIxEnd
            a.Slice(ixStart, (ixEnd - ixStart + 1))

        member a.CopyTo(dst, dstOffset) =
            Array.blit a.Data a.Offset dst dstOffset a.Length
        member a.ToArray() =
            let arr = Array.zeroCreate a.Length
            a.CopyTo(arr, 0)
            arr
        member a.TrimUnused(tolerance) =
            if (tolerance >= (a.Data.Length - a.Length)) then a else
            ImmArray(a.ToArray())

        // todo: append one or many items  

        interface System.Collections.Generic.IEnumerable<'T> with
            member a.GetEnumerator() =
                // borrow existing implementation
                let s = System.ArraySegment(a.Data, a.Offset, a.Length)
                let e = s :> System.Collections.Generic.IEnumerable<'T>
                e.GetEnumerator()

        interface System.Collections.IEnumerable with
            member a.GetEnumerator() =
                let e = a :> System.Collections.Generic.IEnumerable<'T>
                e.GetEnumerator() :> System.Collections.IEnumerator


    [<NoEquality; NoComparison>]
    type Value =
        // basic tree structure.
        | Leaf
        | Stem of uint64 * Value        // encodes 1..63 bits, e.g. `abc1000..0` is 3 bits.
        | Branch of Value * Value

        // optimized lists 
        | Array of ImmArray<Value>      // optimized list of values (non-empty)
        | Binary of ImmArray<uint8>     // optimized list of bytes (non-empty)
        | Concat of Value * Value
        | Take of uint64 * Value        // non-zero list take (to cache concat size)

        // CURRENTLY ELIDING:
        //
        //  External Refs
        //  Accessor Nodes (follow, list drop)
        //  Annotations
        // 
        // These features aren't likely to be necessary for boostrap.

    module StemBits =
        // support for encoding compact bitstrings into stem nodes

        let hibit = (1UL <<< 63)
        let lobit = 1UL
        let inline lenbit len = (hibit >>> len)
        let inline match_mask mask bits = ((mask &&& bits) = mask) 
        let inline msb bits = match_mask hibit bits 
        let inline lsb bits = match_mask lobit bits
        let inline isValid bits = (bits <> 0UL)
        let empty = hibit
        let inline head bits = msb bits
        let inline tail bits = (bits <<< 1)
        let inline isEmpty bits = (bits = empty)
        let inline isFull bits = lsb bits
        let inline match_len len bits =
            let lb = lenbit len
            let mask = (lb ||| (lb - 1UL))
            (lb = (mask &&& bits)) 

        let len bits = 
            // 6-step computation of size via binary division.
            let mutable v = bits
            let mutable n = 0
            if (0UL <> (0xFFFFFFFFUL &&& v)) then n <- n + 32 else v <- v >>> 32
            if (0UL <> (    0xFFFFUL &&& v)) then n <- n + 16 else v <- v >>> 16
            if (0UL <> (      0xFFUL &&& v)) then n <- n +  8 else v <- v >>>  8
            if (0UL <> (       0xFUL &&& v)) then n <- n +  4 else v <- v >>>  4
            if (0UL <> (       0x3UL &&& v)) then n <- n +  2 else v <- v >>>  2
            if (0UL <> (       0x1UL &&& v)) then      n +  1 else n

        type Builder = (struct(uint64 * Value)) // non-full bits

        let toBuilder (v : Value) : Builder =
            match v with
            | Stem(bits,v') when not (isFull bits) -> struct(bits, v')
            | _ -> struct(empty, v)

        let ofBuilder (struct(bits, v) : Builder) : Value = 
            assert(isValid bits)
            if isEmpty bits then v else Stem(bits,v)

        let consBit (b : bool) (struct(bits, v) : Builder) : Builder =
            let bits' = (if b then hibit else 0UL) ||| (bits >>> 1)
            if isFull bits' 
                then struct(empty, Stem(bits', v)) 
                else struct(bits', v)
        
        let consByte (n : uint8) (sb0 : Builder) : Builder =
            let inline cb ix sb = consBit (0uy <> ((1uy <<< ix) &&& n)) sb
            sb0 |> cb 0 |> cb 1 |> cb 2 |> cb 3 
                |> cb 4 |> cb 5 |> cb 6 |> cb 7

    let inline wrapStem bits v =
        StemBits.ofBuilder (struct(bits,v))

    // add a single bit to a stem. 
    let consBit (b : bool) (v : Value) : Value =
        v |> StemBits.toBuilder |> StemBits.consBit b |> StemBits.ofBuilder

    // add a full byte to a stem. Used for symbols, etc..
    let consByte (b : uint8) (v : Value) : Value =
        v |> StemBits.toBuilder |> StemBits.consByte b |> StemBits.ofBuilder

    // add multiple bytes
    let consBytes (b : uint8 array) (v:Value) : Value =
        v |> StemBits.toBuilder |> Array.foldBack (StemBits.consByte) b |> StemBits.ofBuilder

    let unit = Leaf
    let inline pair a b = Branch(a,b)
    let inline left v = consBit false v
    let inline right v = consBit true v

    let variant (s : string) (v : Value) : Value =
        consBytes (System.Text.Encoding.UTF8.GetBytes(s)) v

    let inline symbol (s : string) : Value =
        variant s unit

    let inline ofByte (b:uint8) : Value = 
        consByte b unit

    let inline isByte (v:Value) : bool =
        match v with
        | Stem(bits, Leaf) when (StemBits.match_len 8 bits) -> true
        | _ -> false


    let ofNat (n0 : uint64) : Value =
        let rec loop sb n =
            if (0UL = n) then StemBits.ofBuilder sb else
            let sb' = StemBits.consBit (0UL <> (1UL &&& n)) sb
            loop sb' (n >>> 1)
        loop (StemBits.toBuilder unit) n0

    module VList =
        // This section will implement utilities to support finger-tree ropes.
        //
        // This is based on treating certain patterns of 'concat' and 'take' nodes
        // as finger trees. If not applied to a finger tree rope, manipulations
        // will still be efficient and gradually balance towards a finger-tree.
        //
        // However, shorter lists will favor arrays or binaries! 

        // Concat  (L1 ++ L2)
        // Take    (Size . List)
        // Digits(k) - up to four digits, logically concatenated
        //    1Dk     Dk
        //    2Dk     Dk ++ 1Dk
        //    3Dk     Dk ++ 2Dk
        //    4Dk     Dk ++ 3Dk
        // Digit(k) (or Dk)
        //    k=0 # primary data!
        //        Array
        //        Binary
        //    k>0 # 2-3 nodes
        //        Size . (2Dk | 3Dk)
        // FTRope(k)
        //    Empty   Unit
        //    Small   Digits(k)
        //    Full    Size . (Digits(k) ++ (FTRope(k+1) ++ Digits(k)))
        // List 
        //    FTRope(0)
        //    Pair(elem, List)

        let rec private appTakeLoop n v =
            if (0UL = n) then struct(Leaf, 0UL) else
            match v with
            | Concat(l, r) ->
                // I'm assuming reasonably well-balanced ropes.
                // So, no need to worry about stack depth here.
                let struct(l',nRem) = appTakeLoop n l
                let struct(r',n') = appTakeLoop nRem r
                let v' = 
                    match r' with
                    | Leaf -> l'
                    | _ -> Concat(l',r')
                struct(v',n')
            | Take(n',v') ->
                if (n' >= n)
                    then struct(Take(n, v'), 0UL)
                    else struct(v, (n - n'))
            | Array a ->
                let aLen = uint64 a.Length
                if (aLen >= n)
                    then struct(Array (a.Take(int n)), 0UL)
                    else struct(v, n - aLen)
            | Binary b ->
                let bLen = uint64 b.Length
                if (bLen >= n)
                    then struct(Binary (b.Take(int n)), 0UL)
                    else struct(v, n - bLen)
            | Branch (l, r) ->
                // Naive representation of lists! Stack depth is a concern!
                // Using a specialized subloop to control stack depth.
                appTakeLoopB [l] (n - 1UL) r
            | Leaf | Stem _ -> 
                failwith "target of 'take' is not a valid list"
        and private appTakeLoopB ls n v =
            match v with
            | Branch(l,r) when (n > 0UL) ->
                appTakeLoopB (l::ls) (n - 1UL) r
            | _ ->
                let struct(v',n') = appTakeLoop n v
                let fn r l = Branch(l,r)
                let bs = List.fold fn v' ls
                struct(bs, n')

        let tryApplyTake n v =
            let struct(v',n') = appTakeLoop n v
            if(0UL = n') then Some v' else None

        let applyTake n v =
            let struct(v',n') = appTakeLoop n v
            if (0UL = n') then v' else
            failwith "take more than list length" 

        // computing list length. Assumes valid 'Take' nodes.
        let rec private lenLoop1 acc cs v =
            match v with
            | Concat(l,r)-> lenLoop1 acc (r::cs) l
            | Take(n,_) -> lenLoop (acc + n) cs // assume valid take
            | Leaf -> lenLoop acc cs
            | Array(a) -> lenLoop (acc + (uint64 a.Length)) cs
            | Binary(b) -> lenLoop (acc + (uint64 b.Length)) cs
            | Branch(_,r) -> lenLoop1 (acc + 1UL) cs r
            | Stem _ -> None
        and private lenLoop acc cs =
            match cs with
            | (v::cs') -> lenLoop1 acc cs' v
            | [] -> Some acc

        let tryLen v =
            lenLoop1 0UL [] v 
        let len v =
            match tryLen v with
            | Some n -> n
            | None -> failwith "input is not a valid list"


    let ofImmArray (a : ImmArray<Value>) : Value =
        if (0 = a.Length) then unit else Array a

    let ofImmBinary (b : ImmArray<uint8>) : Value =
        if (0 = b.Length) then unit else Binary b

    let ofArray (a : Value array) : Value =
        ofImmArray (ImmArray.OfArray a)

    let ofList (l : Value list) : Value =
        ofImmArray (ImmArray.OfSeq l)

    let ofSeq (s : Value seq) : Value =
        ofImmArray (ImmArray.OfSeq s)

    let ofBinary (b : uint8 array) : Value =
        ofImmBinary (ImmArray.OfArray b)

    let ofBinarySeq (b : uint8 seq) : Value =
        ofImmBinary (ImmArray.OfSeq b)

    let ofString (s : string) : Value =
        // using UnsafeOfArray to avoid extra copy of bytes. Safe here.
        let b = System.Text.Encoding.UTF8.GetBytes(s)
        ofImmBinary (ImmArray.UnsafeOfArray b)

    // basic pattern matching support for tree structured data.
    //  L(v) - left (value)
    //  R(v) - right (value)
    //  P(a,b) - pair (a,b)
    //  U - unit
    let rec (|L|R|P|U|) v =
        match v with
        | Leaf -> U
        | Stem (bits, v') ->
            let vRem = wrapStem (StemBits.tail bits) v'
            if StemBits.head bits 
                then R(vRem)
                else L(vRem)
        | Branch(a,b) -> P(a,b)
        | Array a -> P(a.[0], ofImmArray (a.Drop(1)))
        | Binary b -> P(ofByte b.[0], ofImmBinary (b.Drop(1)))
        | Concat (l, r) ->
            match l with
            | Concat (ll, lr) -> 
                // flatten concat then retry 
                let v' = Concat(ll, Concat(lr, r))
                (|L|R|P|U|) v'
            | P(a,l') ->
                P(a, Concat(l',r)) // head of list
            | U -> // done with l
                (|L|R|P|U|) r 
            | _ -> failwith "invalid concat node"
        | Take(n,v) ->
            // apply take node then continue
            match VList.tryApplyTake n v with
            | Some v' -> (|L|R|P|U|) v'
            | None -> failwith "invalid take node"

    let inline isUnit v =
        match v with
        | U -> true
        | _ -> false
    
    let inline isPair v =
        match v with
        | P _ -> true
        | _ -> false
    
    let inline isLeft v =
        match v with
        | L _ -> true
        | _ -> false

    let inline isRight v =
        match v with
        | R _ -> true
        | _ -> false

    let rec isBits v =
        match v with
        | Stem(_,v') -> isBits v'
        | Leaf -> true
        | _ -> false

    // todo: 
    //  radix tree operations
    //  stem and record matching


// type Value = Value.Value
