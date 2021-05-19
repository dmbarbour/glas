namespace Glas

// Glas values can directly be encoded as a node with two optional children:
//
//     type Value = { L: Value option; R: Value option }
//
// However, Glas often uses non-branching path segments to encode symbols or
// numbers. If an allocation is required per bit, this is much too inefficient.
// So, I favor a radix tree structure that compacts the non-branching stem:
//
//     type Value = { Stem: Bits; Node: (Value * Value) option }
//
// This is barely adequate, but inefficient if we use many ad-hoc list ops in
// our language modules. 
//
// Glas programs assume that lists are accelerated using a finger-tree encoding.
// To support this, we can represent structures of form `(A * (B * (C * D)))` as
// lists. We have a valid 'list' type when the final element (D) has unit value.
// 
//     type Value = { Neck: Bits; Spine: FTList<Value> }
// 
// At this point, we don't have a strongly normalizing representation. In normal
// form, the last element of the spine is not a pair, and the Spine is not a 
// singleton list (i.e. size is not 1), and we need to ignore the tree-structure
// within the finger tree. Fortunately, none of these constraints are difficult.
//
// This representation still elides use of Stowage, so scalability is limited by
// volatile memory. However, it is adequate for a bootstrap interpreter. 

[<Struct>]
type Branch<'V> = { L: 'V; R: 'V }

[<Struct>]
type Value = { Stem : Bits; Term: Branch<Value> option }

module Value =

    /// The unit (1) value is represented by the single-element tree.
    let unit = 
        { Stem = Bits.empty; Term = None }

    let isUnit v =
        Option.isNone (v.Term) && Bits.isEmpty (v.Stem)

    /// We can represent basic pair types (A * B) as a node with two children.
    /// This is mostly used for representing lists. Glas systems favor records
    /// instead of pairs in most cases.
    let inline pair a b =
        { Stem = Bits.empty; Term = Some { L=a; R=b } }

    let (|Pair|_|) (v : Value) =
        if (Bits.isEmpty v.Stem) then v.Term else None

    let fst v =
        match v with
        | Pair p -> p.L
        | _ -> invalidArg "v" "not a pair"

    let tryFst v =
        match v with
        | Pair p -> Some p.L
        | _ -> None
    
    let snd v =
        match v with
        | Pair p -> p.R
        | _ -> invalidArg "v" "not a pair"

    let trySnd v =
        match v with
        | Pair p -> Some p.R
        | _ -> None

    /// We can represent basic sum types (A + B) as a node with a single child.
    /// Glas mostly uses labeled variants instead, but this is illustrative.
    let inline left a = 
        { a with Stem = Bits.cons false (a.Stem) }

    let inline isLeft v =
        if Bits.isEmpty v.Stem then false else (false = (Bits.head v.Stem))

    let (|Left|_|) v =
        if isLeft v then Some { v with Stem = Bits.tail v.Stem } else None

    /// The right sum adds a `1` prefix to an existing value.
    let inline right b = 
        { b with Stem = Bits.cons true (b.Stem) }

    let inline isRight v = 
        if Bits.isEmpty v.Stem then false else (true = (Bits.head v.Stem))

    let (|Right|_|) v = 
        if isRight v then Some { v with Stem = Bits.tail v.Stem } else None

    /// Any bitstring can be a value. Glas uses bitstrings for numbers and
    /// labels, but not for binaries. Binaries are encoded as a list of bytes.
    let inline ofBits b =
        { Stem = b; Term = None }

    let inline isbits v = Option.isNone v.Term

    let (|Bits|_|) v =
        if isbits v then Some v.Stem else None

    /// We can encode a byte as a short bitstring.
    let inline u8 (n : uint8) : Value = 
        Bits.ofByte n |> ofBits
    
    let (|U8|_|) v =
        if isbits v then Bits.(|Byte|_|) v.Stem else None

    let inline u16 (n : uint16) : Value = 
        Bits.ofU16 n |> ofBits

    let (|U16|_|) v =
        if isbits v then Bits.(|U16|_|) v.Stem else None

    let inline u32 (n : uint32) : Value =
        Bits.ofU32 n |> ofBits

    let (|U32|_|) v =
        if isbits v then Bits.(|U32|_|) v.Stem else None

    let inline u64 (n : uint64) : Value =
        Bits.ofU64 n |> ofBits

    let (|U64|_|) v = 
        if isbits v then Bits.(|U64|_|) v.Stem else None

    let private consLabel (s : string) (b : Bits) : Bits =
        let strbytes = System.Text.Encoding.UTF8.GetBytes(s)
        Array.foldBack Bits.consByte strbytes (Bits.consByte 0uy b)

    /// Labeled variants and records are better than sums and pairs because
    /// they are openly extensible and self-documenting. In Glas, we encode
    /// labels as null-terminated UTF-8 bitstrings.
    let label (s : string) : Bits =
        consLabel s Bits.empty

    /// A variant is modeled by adding a label to an existing value.
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

    /// Attempt to match bitstring prefix with value.
    let (|Prefixed|_|) (p : Bits) (v : Value) : Value option =
        let struct(p', stem') = dropSharedPrefix p (v.Stem)
        if Bits.isEmpty p' then Some { v with Stem = stem' } else None

    /// Prefixed with a string label.
    let (|Labeled|_|) (s : string) (v : Value) : Value option =
        (|Prefixed|_|) (label s) v

    let rec private accumSharedPrefixLoop acc a b =
        let halt = Bits.isEmpty a || Bits.isEmpty b || (Bits.head a <> Bits.head b)
        if halt then struct(acc, a, b) else
        accumSharedPrefixLoop (Bits.cons (Bits.head a) acc) (Bits.tail a) (Bits.tail b)
    
    // returns a triple with (reversed shared prefix, remainder of a, remainder of b)
    let inline private findSharedPrefix a b = 
        accumSharedPrefixLoop (Bits.empty) a b

    let private bitsAppendRev a b =
        Bits.fold (fun acc e -> Bits.cons e acc) b a

    // construction of records
    // pattern matching on records? maybe match a list of symbols?

    /// Record values will share prefixes in the style of a radix tree.
    //let record_insert ()


    /// Glas encodes lists using pairs, e.g. (A * (B * (C * ()))).
    ///
    /// The intention is that list representations will be optimized by
    /// Glas systems, using finger trees for large lists or struct tuples
    /// for short lists. This module uses a direct representation, which
    /// is barely adequate for bootstrap. 
    let rec isList (v : Value) =
        if not (Bits.isEmpty v.Stem) then false else
        match v.Term with
        | Some b -> isList b.R
        | None -> true

    // A consequence of this is that we cannot cheaply check whether a  
    // value is a valid list. However, we can view all structures as
    // almost-valid lists, modulo the terminal, and use this to help 
    // handle conversions.


    /// Convert an F# list to a Glas list.
    let ofList (vs : Value list) : Value =
        List.foldBack pair vs unit

    /// Convert an F# list to a Glas list value with mapped conversions.
    let ofListM fn xs =
        List.foldBack (fun x v -> pair (fn x) v) xs unit

    /// Convert an F# array to a Glas list.
    let ofArray (vs : Value array) : Value =
        Array.foldBack pair vs unit

    /// Convert an F# array to a Glas list with mapped conversions.
    let ofArrayM fn xs =
        Array.foldBack (fun x v -> pair (fn x) v) xs unit
    
    /// Convert an F# seq to a Glas list.
    let ofSeq (vs : Value seq) : Value =
        Seq.foldBack pair vs unit

    /// Convert an F# seq to a Glas list with conversions. 
    let ofSeqM fn xs =
        ofSeq (Seq.map fn xs)

    let ofBinary (s : uint8 array) : Value =
        ofArrayM u8 s 

    /// Strings are normally represented 
    let ofString (s : string) : Value =
        ofBinary (System.Text.Encoding.UTF8.GetBytes(s))




    /// Convert a Glas list to an F# seq
    let listToSeq (v : Value) : Value = 
        unit 


    let rec private tryRevListLoop acc v =
        if not (Bits.isEmpty v.Stem) then None else
        match v.Term with
        | Some b -> tryRevListLoop (pair b.L acc) (b.R)
        | None -> Some acc

    let tryRevList (v : Value) : Value option =
        tryRevListLoop unit v

    let revList (v : Value) : Value =
        match tryRevList v with
        | Some rv -> rv
        | None -> invalidArg "v" "value is not a valid list"

    let inline tryListHead v = fst v




(*
* **pushl** - given value and list, add value to left (head) of list
* **popl** - given a non-empty list, split into head and tail.
* **pushr** - given value and list, add value to right of list
* **popr** - given non-empty list, split into last element and everything else
* **join** - appends list at top of data stack to the list below it
* **split** - given number N and list of at least N elements, produce a pair of sub-lists such that join produces the original list, and the head portion has exactly N elements.
* **len** - given list, return a number that represents length of list. Uses smallest encoding of number (i.e. no zeroes prefix).
*)

