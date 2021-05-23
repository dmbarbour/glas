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
// ensure we have anormal form with no pair in the EndValue type.
// 
// This representation still excludes Stowage or rope-like chunks for binaries, so
// there is a lot that could be improved. But it is adequate for bootstrap.


[<Struct>]
type Value = { Stem : Bits; Spine: Option<struct(Value * FTList<Value> * EndVal)> }
and [<Struct>] EndVal =
    val Value : Value
    new(v : Value) =
        if Bits.isEmpty v.Stem && Option.isSome v.Spine then 
            invalidArg (nameof v) "EndVal must not be a pair"
        { Value = v }

module Value =

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

    let inline (|Pair|_|) v =
        if Bits.isEmpty v.Stem then v.Spine else None

    let pair a b =
        match b with 
        | Pair struct(b0,bs,bend) -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.cons b0 bs, bend) }
        | _ -> 
            { Stem = Bits.empty; Spine=Some struct(a, FTList.empty, EndVal(b)) }

    let fst v =
        match v with
        | Pair struct(v0,_,_) -> v0
        | _ -> invalidArg "v" "not a pair"

    let tryFst v =
        match v with
        | Pair struct(v0,_,_) -> Some v0
        | _ -> None
    
    let snd v =
        match v with
        | Pair struct(_, vs, vend) -> 
            match FTList.tryViewL vs with
            | Some (v1, vs') -> { Stem = Bits.empty; Spine = Some struct(v1, vs', vend) }
            | None -> vend.Value
        | _ -> invalidArg "v" "not a pair"

    let trySnd v =
        match v with
        | Pair struct(_, vs, vend) -> 
            let r = 
                match FTList.tryViewL vs with
                | Some (v1, vs') -> { Stem = Bits.empty; Spine = Some struct(v1, vs', vend) }
                | None -> vend.Value
            Some r
        | _ -> None

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

    // remove shared prefix without recording the prefix.
    let rec private dropSharedPrefix a b =
        let halt = Bits.isEmpty a || Bits.isEmpty b || (Bits.head a <> Bits.head b)
        if halt then struct(a, b) else
        dropSharedPrefix (Bits.tail a) (Bits.tail b) 

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

    // same as `Bits.append (Bits.rev a) b`.
    let private bitsAppendRev a b =
        Bits.fold (fun acc e -> Bits.cons e acc) b a

    /// Access a value within a record. Essentially a radix tree lookup.
    let rec record_lookup (p:Bits) (r:Value) : Value option =
        let struct(p',stem') = dropSharedPrefix p (r.Stem)
        if (Bits.isEmpty p') then Some { r with Stem = stem' } else
        if not (Bits.isEmpty stem') then None else
        match r.Spine with
        | Some struct(l, s, r) -> _rlu_spine p' l s r
        | None -> None
    and private _rlu_spine p l s r =
        let pRem = Bits.tail p
        if Bits.head p then record_lookup pRem l else
        match FTList.tryViewL s with
        | None -> record_lookup pRem (r.Value)
        | Some (l', s') -> 
            if not (Bits.isEmpty pRem) then _rlu_spine pRem l' s' r else 
            Some { Stem = Bits.empty; Spine = Some struct(l', s', r) } 

(*
    let rec record_delete (p:Bits) (r:Value) : Value =
        let struct(common, p', stem') = findSharedPrefix p (r.Stem)
        if Bits.isEmpty p' then unit else
        if not (Bits.isEmpty stem') then r else
        match r.Spine with
        | Some struct(l, s, e) ->
            let spine' = _rdel_spine 
        _rdel_spine p' l s e
        | None -> r
    and private _rdel_spine

*)

(*
    let rec _record_delete acc p r =
        let struct(sh, p', stem') = findSharedPrefix p (r.Stem)
        if Bits.isEmpty p' then unit else // value entirely on path p
        if not (Bits.isEmpty stem') then r else // value does not have p
        match r.Spine with
        | Some struct(l, s, e) -> _rdel_spine p' l s e 
        | None -> r // 

    /// Remove path from a record value. O(len(p)). 
    /// This also removes the vestigial path, unless shared by another value.
    let record_delete (p:Bits) (r:Value) : Value =
        _record_delete [] p r






    /// Insert a value into a record at a given path.
    let record_insert (p:Bits) (v:Value) (r:Value) : Value =
*)


//* **get** - given label and record, extract value from record. Fails if label is not in record.
//* **put** - given a label, value, and record, create new record that is almost the same except with value on label. 
//* **del** - given label and record, create new record with label removed modulo prefix sharing with other paths in record.



    /// Record values will share prefixes in the style of a radix tree.
    //let record_insert ()


    /// Glas logically encodes lists using pairs terminating in unit.
    ///
    ///    (A * (B * (C * (D * (E * ()))))).
    /// 
    /// This check can be performed in O(1) time based on the finger-tree
    /// representation of list structures.
    let isList (v : Value) =
        if not (Bits.isEmpty v.Stem) then false else
        match v.Spine with
        | None -> true
        | Some struct(_,_,vend) -> isUnit vend.Value

    let tryFTList v =
        if not (Bits.isEmpty v.Stem) then None else
        match v.Spine with
        | None -> Some (FTList.empty)
        | Some struct(v0,vs,vend) ->
            if not (isUnit vend.Value) then None else
            Some (FTList.cons v0 vs)

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



