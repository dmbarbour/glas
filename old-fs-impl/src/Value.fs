namespace Glas

// Developing alternative value model aligned to Glob representation
//
// The main benefit of this implementation is support for compact arrays
// of values or bytes. This is important because binary inputs and outputs
// are very common. 
module Value =

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
            if not ok then failwith "invalid array slice" else
            let arr = Array.zeroCreate len
            Array.blit data offset arr 0 len
            ImmArray(arr)
        static member OfSeq(dataSeq : 'T seq) =
            ImmArray(Array.ofSeq dataSeq)

        // private constructors are accessible directly via 'Unsafe' methods.
        // The intention here is to make the 'Unsafe' qualifier more visible.
        // It is left to the caller to ensure array is not shared mutably. 
        static member UnsafeOfArray(data : 'T array) =
            ImmArray(data)
        static member UnsafeOfArraySlice(data : 'T array, offset, len) =
            let ok = (offset >= 0) && (len >= 0) && ((data.Length - offset) >= len)
            if ok then ImmArray(data, offset, len) else
            failwith "invalid array slice"

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
        member inline a.Slice(offset, len) =
            a.Drop(offset).Take(len)
        member inline a.GetSlice(optIxStart, optIxEnd) = // support for .[1..3] slices.
            let ixStart = Option.defaultValue 0 optIxStart
            let ixEnd = Option.defaultValue (a.Length - 1) optIxEnd
            a.Slice(ixStart, (ixEnd - ixStart + 1))

        member a.CopyTo(dst, dstOffset) =
            Array.blit a.Data a.Offset dst dstOffset a.Length
        member inline a.ToArray() =
            let arr = Array.zeroCreate a.Length
            a.CopyTo(arr, 0)
            arr
        member a.TrimUnused(tolerance) =
            if (tolerance >= (a.Data.Length - a.Length)) then a else
            ImmArray(a.ToArray())

        member a.Insert(ix : int, v : 'T) : ImmArray<'T> =
            let lhs = a.Take(ix)
            let rhs = a.Drop(ix)
            let arr = Array.zeroCreate (1 + a.Length)
            lhs.CopyTo(arr,0)
            arr[ix] <- v 
            rhs.CopyTo(arr,ix+1)
            ImmArray(arr)
        member inline a.Cons(v) = a.Insert(0,v)
        member inline a.Snoc(v) = a.Insert(a.Length,v)

        // we'll append up to 3 items in context of finger-trees.
        static member Append3(a : ImmArray<'T>, b : ImmArray<'T>, c : ImmArray<'T>) : ImmArray<'T> =
            let arr = Array.zeroCreate (a.Length + b.Length + c.Length)
            a.CopyTo(arr,0)
            b.CopyTo(arr,a.Length)
            c.CopyTo(arr,a.Length + b.Length)
            ImmArray(arr)
        static member inline Append(a : ImmArray<'T>, b : ImmArray<'T>) : ImmArray<'T> =
            ImmArray.Append3(a,b,ImmArray<'T>.Empty)

        interface System.Collections.Generic.IEnumerable<'T> with
            member a.GetEnumerator() =
                // borrow ArraySegment implementation
                let s = System.ArraySegment(a.Data, a.Offset, a.Length)
                let e = s :> System.Collections.Generic.IEnumerable<'T>
                e.GetEnumerator()

        interface System.Collections.IEnumerable with
            member a.GetEnumerator() =
                let e = a :> System.Collections.Generic.IEnumerable<'T>
                e.GetEnumerator() :> System.Collections.IEnumerator

    module StemBits =
        // support for encoding compact bitstrings into stem nodes
        //
        // length is encoded via lowest '1' bit, e.g. abc10..0 will
        // encode three data bits. Exception is 64-bit stem, which
        // doesn't include a length indicator.

        let hibit = (1UL <<< 63)
        let lobit = 1UL
        let inline lenbit len = (hibit >>> len)
        let inline match_mask mask bits = ((mask &&& bits) = mask) 
        let inline msb bits = match_mask hibit bits 
        let inline lsb bits = match_mask lobit bits
        let inline isValid bits = (bits <> 0UL)
        let empty = hibit
        let inline cons b bits = (if b then hibit else 0UL) ||| (bits >>> 1)
        let inline head bits = msb bits
        let inline tail bits = (bits <<< 1)
        let inline head64 bits = msb bits
        let inline tail64 bits = (bits <<< 1) ||| lobit
        let inline isEmpty bits = (bits = empty)
        let inline isFull bits = lsb bits
        let inline consByte (n : uint8) (bits) = 
            assert(0UL = (bits &&& 0xFFUL))
            ((uint64 n) <<< 56) ||| (bits >>> 8)
        let inline ofByte (b : uint8) = 
            consByte b hibit

        // true if exactly len bits.
        let inline match_len len bits =
            let lb = lenbit len
            let mask = (lb ||| (lb - 1UL))
            (lb = (mask &&& bits)) 

        // true if len bits or fewer
        let inline max_len len bits =
            let lb = lenbit len
            let mask = (lb - 1UL)
            (0UL = (mask &&& bits))

        // true if len bits or more 
        let inline min_len len bits =
            let lb = lenbit len
            let mask = (lb ||| (lb - 1UL))
            (0UL <> (mask &&& bits))

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

        let rec eraseZeroesPrefix bits =
            if msb bits then bits else eraseZeroesPrefix (tail bits)

        let rec eraseOnesPrefix bits =
            if msb bits then eraseOnesPrefix (tail bits) else bits


    // Values encoded similarly to Glas Object. 
    //
    // Features:
    // - Finger-tree rope structure.
    // - Compact, embedded binary data.
    // - Non-allocating short bitstrings.
    //
    // Elided Glas Object Features:
    // - External references
    // - Accessor nodes (useless without external refs)
    // - Annotations
    //
    // Known Weaknesses:
    // - O(N) check for 'bitstring' type.
    // - Representing invalid data is possible (via Take or Concat)
    // - list-like ropes terminating in non-Leaf is not supported
    //
    // 0..63 Stem bits are encoded into a uint64, with lowest '1' bit
    // indicating length, e.g. `abc10...0` encodes 3 bits, msb first.
    //
    // This design is intended to reduce allocation for variants,
    // radix trees, symbols, and numbers.
    [<CustomEquality; NoComparison>]
    type Term =
        // basic tree structure.
        | Leaf
        | Stem64 of uint64 * Term       // extended with 64 stem bits (msb to lsb)
        | Branch of Value * Value
        // optimized lists 
        | Array of ImmArray<Value>      // optimized list of values (non-empty)
        | Binary of ImmArray<uint8>     // optimized list of bytes (non-empty)
        | Concat of Term * Term         // concatenation of list values (non-empty)
        | Take of uint64 * Term         // non-zero list take (caches concat size)

        // some utility functions to support custom equality attribute
        // (I really hate that we cannot use normal functions in this)
        member t.ListLen() =
            t.ListLenAcc(0UL)

        member private t.ListLenAcc(acc) =
            match t with
            | Array(a) -> acc + uint64 a.Length
            | Binary(b) -> acc + uint64 b.Length
            | Concat(l,r) -> r.ListLenAcc(l.ListLenAcc(acc))
            | Take(sz,_) -> acc + sz
            | Leaf -> acc
            | Branch(l,r) when StemBits.isEmpty r.Stem -> 
                r.Term.ListLenAcc(1UL + acc)
            | _ -> failwith "not a list"

        member t.ApplyTake(sz) =
            if(0UL = sz) then Leaf else
            match t with
            | Array a when ((uint64 a.Length) >= sz) ->
                Array(a.Take(int sz))
            | Binary b when ((uint64 b.Length) >= sz) ->
                Binary(b.Take(int sz))
            | Concat(l,r) ->
                let szL = l.ListLen()
                if (sz < szL) then l.ApplyTake(sz) else
                if (sz = szL) then l else
                Concat(l, r.ApplyTake(sz - szL))
            | Take(n, t') when (n >= sz) ->
                Take(sz, t')
            | Branch(l, _) when (1UL = sz) ->
                Branch(l, Value.Unit)
            | Branch(l, r) when (StemBits.isEmpty r.Stem) ->
                let r' = Value.OfTerm (Take((sz - 1UL), r.Term))
                Branch(l, r')
            | _ -> 
                failwith "invalid list take"

        member t.AsPair() =
            match t with
            | Branch(l, r) -> 
                struct(l, r)
            | Array(a) ->
                let l = a[0]
                let r = if (1 = a.Length) then Value.Unit else Value.OfTerm (Array(a.Drop(1)))
                struct(l, r)
            | Binary(b) ->
                let l = Value.OfByte b[0]
                let r = if (1 = b.Length) then Value.Unit else Value.OfTerm (Binary(b.Drop(1)))
                struct(l, r)
            | Concat(lt, rt) ->
                match lt with
                | Leaf -> 
                    rt.AsPair()
                | Concat(llt, lrt) -> 
                    let t' = Concat(llt, Concat(lrt, rt))
                    t'.AsPair()
                | _ ->
                    let struct(lv0,lvRem) = lt.AsPair()
                    assert(StemBits.isEmpty lvRem.Stem)
                    let r = Value.OfTerm (Concat(lvRem.Term, rt))
                    struct(lv0,r)
            | Take(sz, t') -> 
                t'.ApplyTake(sz).AsPair()
            | _ -> failwith "not a pair"

        static member Eq(a,b) = Term.Eq1([],a,b)
        static member Eq0(cc) =
            match cc with
            | struct(a,b)::cc' -> Term.Eq1(cc',a,b)
            | [] -> true
        static member private Eq1(cc,a,b) =
            if (Microsoft.FSharp.Core.LanguagePrimitives.PhysicalEquality a b) then Term.Eq0(cc) else
            match a with
            | Leaf -> 
                match b with
                | Leaf -> Term.Eq0(cc)
                | _ -> false
            | Stem64(aBits, a') ->
                match b with
                | Stem64(bBits, b') when (aBits = bBits) ->
                    Term.Eq1(cc, a', b')
                | _ -> false
            | Concat(Leaf, a') | Concat(a', Leaf) -> 
                Term.Eq1(cc, a', b)
            | _ ->
                match b with
                | Leaf | Stem64 _ -> false
                | Concat(Leaf, b') | Concat(b', Leaf) -> 
                    Term.Eq1(cc, a, b')
                | _ ->
                    let struct(al,ar) = a.AsPair()
                    let struct(bl,br) = b.AsPair()
                    let eqStems = (al.Stem = bl.Stem) && (ar.Stem = br.Stem)
                    if not eqStems then false else
                    let cc' = struct(ar.Term,br.Term)::cc
                    Term.Eq1(cc', al.Term, bl.Term)

        override t.GetHashCode() =
            Term.Hash(0,[],t)

        static member private Hash(acc,cc,t) =
            match t with
            | Leaf ->
                let acc' = hash(struct(acc, 0x1eaf))
                match cc with
                | (t'::cc') -> Term.Hash(acc',cc',t')
                | [] -> acc'
            | Stem64(bits,t') -> 
                let acc' = hash(struct(acc, bits))
                Term.Hash(acc', cc, t')
            | _ ->
                let struct(tl, tr) = t.AsPair()
                let acc' = hash(struct(acc, tl.Stem, tr.Stem))
                let cc' = (tr.Term)::cc
                Term.Hash(acc', cc', tl.Term)

        interface System.IEquatable<Term> with
            member a.Equals(b) =
                Term.Eq(a,b)

        override a.Equals(b) =
            match b with
            | :? Term as tb -> (a :> System.IEquatable<_>).Equals(tb)
            | _ -> false

        // CURRENTLY ELIDING:
        //
        //  External Refs
        //  Accessor Nodes (follow path, list drop)
        //  Annotations
        // 
    and [<Struct>] Value = 
        { Stem: uint64; Term : Term } // partial stem
        static member OfStemTerm(s,t) = { Stem = s; Term = t }
        static member OfStem(s) = { Stem = s; Term = Leaf }
        static member OfTerm(t) = { Stem = StemBits.empty; Term = t }
        static member Unit = Value.OfStem (StemBits.empty)
        static member OfByte(n) = Value.OfStem (StemBits.ofByte n)

    module VTerm =
        let inline isLeaf t =
            match t with
            | Leaf -> true
            | _ -> false

        let inline isStem64 t =
            match t with
            | Stem64 _ -> true
            | _ -> false

        let rec isBits t =
            match t with
            | Leaf -> true
            | Stem64 (_,t') -> isBits t'
            | _ -> false

        let rec stemBitLen acc t =
            match t with
            | Stem64(_, t') -> stemBitLen (64 + acc) t'
            | _ -> acc

    let inline ofTerm t = 
        { Stem = StemBits.empty; Term = t }
    let inline ofStem bits =
        assert(StemBits.isValid bits)
        { Stem = bits; Term = Leaf}

    let unit = ofTerm Leaf
    let inline isUnit v =
        (StemBits.isEmpty v.Stem) && (VTerm.isLeaf v.Term)
    let inline (|Unit|NotUnit|) v = 
        if isUnit v then Unit else NotUnit

    // add a single bit to a stem. 
    let consStemBit (b : bool) (v : Value) : Value =
        let bits' = StemBits.cons b v.Stem
        if StemBits.isFull v.Stem
            then { Stem = StemBits.empty; Term = Stem64(bits',v.Term) }
            else { Stem = bits'; Term = v.Term }

    let consStemByte (n : uint8) (v0 : Value) : Value =
        let inline cb ix v = consStemBit (0uy <> ((1uy <<< ix) &&& n)) v
        v0 |> cb 0 |> cb 1 |> cb 2 |> cb 3
           |> cb 4 |> cb 5 |> cb 6 |> cb 7

    let rec private consStemLoop (n : uint64) (nLen : int) (v : Value) : Value = 
        if(nLen = 0) then v else
        let b = (n &&& (1UL <<< (64 - nLen))) <> 0UL
        let v' = consStemBit b v
        let nLen' = nLen - 1
        consStemLoop n nLen' v'

    // Prepend 0 to 63 stem bits on a value, encoding length in the 64-bit
    // integer based on the lowest '1' bit, e.g. `abc1000..0` is a 3 bit stem.
    let consStemBits (stem : uint64) (v0 : Value) : Value =
        assert(StemBits.isValid stem)
        if StemBits.isEmpty v0.Stem
            then { Stem = stem; Term = v0.Term }
            else consStemLoop stem (StemBits.len stem) v0

    // Prepend exactly 64 bits from a stem to a value.
    let consStem64 (stem64 : uint64) (v0 : Value) : Value =
        if StemBits.isEmpty v0.Stem 
            then ofTerm (Stem64 (stem64, v0.Term))
            else consStemLoop stem64 64 v0 

    let inline isStem v =
        (not (StemBits.isEmpty v.Stem)) || (VTerm.isStem64 v.Term)

    let stemHead v =
        if StemBits.isEmpty v.Stem 
          then
            match v.Term with
            | Stem64 (bits, _) -> StemBits.head64 bits
            | _ -> failwith "stemHead at end of stem"
          else StemBits.head v.Stem

    let stemTail v =
        if StemBits.isEmpty v.Stem
          then
            match v.Term with
            | Stem64 (bits, v') -> { Stem = StemBits.tail64 bits; Term = v' }
            | _ -> failwith "stemTail at end of stem"
          else { Stem = (StemBits.tail v.Stem); Term = v.Term }

    let inline stemBitLen v =
        VTerm.stemBitLen (StemBits.len v.Stem) v.Term

    let inline isBits v = 
        VTerm.isBits (v.Term)

    // optimized concatenation of labels
    let rec private withLabelT t v =
        match t with
        | Leaf -> v
        | Stem64(stem, t') -> consStem64 stem (withLabelT t' v)
        | _ -> failwith "invalid label"
    let withLabel p v =
        consStemBits (p.Stem) (withLabelT p.Term v)

    let variant (s : string) (v : Value) : Value =
        // encode a null-terminated utf-8 string into stem
        let utf8_bytes = System.Text.Encoding.UTF8.GetBytes(s)
        v |> consStemByte 0uy // null terminal
          |> Array.foldBack consStemByte utf8_bytes

    let inline symbol (s : string) : Value =
        variant s unit

    // label is synonym for symbol now 
    // (because Bits is integrated with Value)
    let inline label s = symbol s

    [<return: Struct>]
    let inline (|Bits|_|) v =
        if isBits v then ValueSome(v) else ValueNone

    let inline ofBits p = assert(isBits p); p 

    let inline pair a b = ofTerm (Branch(a,b))
    let inline left v = consStemBit false v
    let inline right v = consStemBit true v

    let isPair a =
        if StemBits.isEmpty a.Stem then
            match a.Term with
            | Branch _ | Array _ | Binary _ | Concat _ | Take _ -> true
            | Stem64 _ | Leaf -> false
        else false

    let inline ofByte (n:uint8) : Value = 
        // consStemByte b unit
        //   optimized impl
        ofStem (StemBits.ofByte n)

    let inline isByte (v:Value) : bool =
        (StemBits.match_len 8 v.Stem) && (VTerm.isLeaf v.Term)

    // assumes isByte
    let inline toByte (v:Value) : uint8 =
        (uint8 (v.Stem >>> 56))

    [<return: Struct>]
    let inline (|Byte|_|) v =
        if isByte v then ValueSome(toByte v) else ValueNone

    let ofNat (n : uint64) : Value =
        if StemBits.msb n then ofTerm(Stem64(n,Leaf)) else
        ofStem <| StemBits.eraseZeroesPrefix ((n <<< 1) ||| 1UL)

    let i64_min_bits = (1UL <<< 63) - 1UL

    let ofInt (n0 : int64) : Value =
        if (n0 >= 0L) then ofNat (uint64 n0) else
        if (System.Int64.MinValue = n0) then ofTerm (Stem64(i64_min_bits, Leaf)) else
        let n1c = uint64 (n0 - 1L) // use one's complement
        ofStem <| StemBits.eraseOnesPrefix ((n1c <<< 1) ||| 1UL) 

    [<return: Struct>]
    let (|Nat64|_|) v =
        match v.Term with
        | Leaf when (StemBits.msb v.Stem) -> // non-negative
            let sp = 63 - StemBits.len v.Stem
            let ss = StemBits.cons false v.Stem
            ValueSome(ss >>> sp)
        | Stem64(bits, Leaf) when (StemBits.isEmpty v.Stem) && (StemBits.head bits) ->
            // upper half of uint64 needs 64 bits
            ValueSome(bits)
        | _ -> ValueNone

    [<return: Struct>]
    let (|Nat32|_|) v =
        match v with
        | Nat64(n) when ((uint64 System.UInt32.MaxValue) >= n) ->
            ValueSome(uint32 n)
        | _ -> ValueNone

    [<return: Struct>]
    let (|Int64|_|) v =
        match v.Term with
        | Leaf when (StemBits.msb v.Stem) -> // non-negative
            let sp = 63 - StemBits.len v.Stem
            let ss = int64 (StemBits.cons false v.Stem)
            ValueSome (ss >>> sp)
        | Leaf -> // negative, 1..63 bits as one's complement
            let sp = 63 - StemBits.len v.Stem
            let ss = int64 (StemBits.cons true v.Stem)
            // the `+ 1L` is due to one's complement 
            ValueSome((ss >>> sp) + 1L)
        | Stem64(bits, Leaf) when (StemBits.isEmpty v.Stem) && (bits = i64_min_bits) -> 
            // only one int64 value needs 64 bits
            ValueSome(System.Int64.MinValue)
        | _ -> ValueNone

    [<return: Struct>]
    let (|Int32|_|) v =
        match v with
        | Int64(n) when ((int64 System.Int32.MaxValue >= n) && (n >= int64 System.Int32.MinValue)) ->
            ValueSome(int n)
        | _ -> ValueNone


    let ofImmArray (a : ImmArray<Value>) : Value =
        if (0 = a.Length) then unit else ofTerm (Array a)

    let ofImmBinary (b : ImmArray<uint8>) : Value =
        if (0 = b.Length) then unit else ofTerm (Binary b)

    let ofBinary (b : uint8 array) : Value =
        ofImmBinary (ImmArray.OfArray b)

    let ofBinarySeq (b : uint8 seq) : Value =
        ofImmBinary (ImmArray.OfSeq b)

    let ofString (s : string) : Value =
        // using UnsafeOfArray to avoid extra copy of bytes.
        let b = System.Text.Encoding.UTF8.GetBytes(s)
        ofImmBinary (ImmArray.UnsafeOfArray b)

    // the following might be reimplemented with ropes to
    // compact binary data. But is not essential to do so.
    let ofArray (a : Value array) : Value =
        ofImmArray (ImmArray.OfArray a)

    let ofList (l : Value list) : Value =
        ofImmArray (ImmArray.OfSeq l)

    let ofSeq (s : Value seq) : Value =
        ofImmArray (ImmArray.OfSeq s)

    let rec isList v =
        if StemBits.isEmpty v.Stem then 
            match v.Term with
            | Branch(_, r) -> isList r
            | Leaf | Array _ | Binary _ | Take _ | Concat _ -> true
            | Stem64 _ -> false
        else false

    module Rope =
        // Combines concat nodes and array/binary fragments to efficiently
        // represent big lists and support deque, slicing, and random access.

        // 2-3 Finger Tree Rope Structure, encoded into Glas Object nodes.
        //
        //  Concat  L1 ++ L2
        //  Take    Size . List
        //
        //  Digit(k)
        //    k=0 # primary data!
        //      Array
        //      Binary
        //    k>0 # collects 2 or 3 smaller digits, caches size info
        //      Larger array or binary (up to heuristic threshold)
        //      Size . Node(k-1)
        //  Node(k) - two or three Digit(k), concatenated.
        //  Digits(k) - one to four Digit(k), concatenated.
        //      LDigits - right assoc, e.g. (A ++ (B ++ C))
        //      RDigits - left assoc, e.g. ((A ++ B) ++ C)
        //  Rope(k) -
        //      Empty               Leaf
        //      Single              Array | Binary | Node(k-1)
        //      Many                Size . (LDigits(k) ++ (Rope(k+1) ++ RDigits(k)))
        //
        // Finger trees have O(1) access to both ends, O(lg(N)) slice and append,
        // and are memory-efficient if we assume reasonable array fragments. 

        // heuristic thresholds
        //  small - how much to build up to in Digit(0) (i.e. via cons/snoc)
        //  large - how much to build up to in Digit(>0) (i.e. via mkNode2/3)
        let len_small_arr = 6
        let len_small_bin = 16
        let len_large_arr = 512
        let len_large_bin = 4096

        let empty = Leaf

        // length computation (non-allocating; O(1) within ropes)
        let inline len (t : Term) = 
            t.ListLen() 

        let inline wrapSize t = 
            Take((len t), t)

        let isSized t =
            match t with
            | Take _ | Array _ | Binary _ | Leaf -> true
            | _ -> false

        let inline sized t = 
            if isSized t then t else wrapSize t

        let inline mkRope l s r = 
            wrapSize (Concat(l, Concat(s, r)))

        [<return: Struct>]
        let (|Rope|_|) t =
            match t with
            | Take(sz, Concat(l, sr)) when (sz = (len l + len sr)) ->
                match sr with
                | Concat(s, r) -> ValueSome(struct(l, s, r))
                | _ -> ValueSome(struct(l, Leaf, sr)) 
            | _ -> ValueNone

        let rec item ix t = 
            match t with
            | Array(a) when ((uint64 a.Length) > ix) -> a[int ix]
            | Binary(b) when ((uint64 b.Length) > ix) -> ofByte <| b[int ix]
            | Concat(l,r) ->
                let n = len l // O(1) for rope
                if n > ix then item ix l else item (ix - n) r
            | Take(n, v') when (n > ix) -> item ix v'
            | Branch(l,_) when (ix = 0UL) -> l
            | Branch(_,r) when (ix > 0UL) && (StemBits.isEmpty r.Stem) -> 
                item (ix - 1UL) (r.Term)
            | _ -> failwith "invalid list index"

        // invariant: 0 < sz < len t
        // preserves rope digits structure
        let digitsTake sz (t:Term) =
            assert(0UL < sz)
            t.ApplyTake(sz)

        // invariant: 0 < sz < len t
        // preserves rope digits structure
        let rec digitsDrop sz t =
            assert(0UL < sz)
            match t with
            | Array(a) -> 
                assert(sz < (uint64 a.Length))
                Array(a.Drop(int sz))
            | Binary(b) -> 
                assert(sz < (uint64 b.Length))
                Binary(b.Drop(int sz))
            | Concat(l,r) ->
                let szL = len l
                if sz < szL then Concat((digitsDrop sz l), r) else
                if sz = szL then r else
                digitsDrop (sz - szL) r
            | Take(n, t') -> 
                assert (sz < n)
                Take((n - sz), (digitsDrop sz t')) // eager drop
            | Branch(l, r) when (StemBits.isEmpty r.Stem) ->
                // shouldn't appear in practice
                if (1UL = sz) then (r.Term) else
                digitsDrop (sz - 1UL) (r.Term) // eager drop (tail recursive)
            | _ -> failwith "invalid digits drop"
            
        // unwrapSize and applySize are optimized assuming most nodes
        // are exactly sized (i.e. we're caching size info).
        let rec unwrapSize t =
            match t with
            | Take(sz, t') -> applySize sz t' 
            | _ -> t

        and applySize sz t =
            assert(0UL < sz)
            let szT = len t
            let t' = 
                if (sz = szT) then t else 
                assert (sz < szT)
                digitsTake sz t
            unwrapSize t'

        module CCL =
            // support for concat lists, e.g. (a ++ (b ++ c)) of terms. 
            let rec len t = 
                match t with
                | Concat(l,r) -> len l + len r
                | Leaf -> 0 
                | _ -> 1

            // convert a concat-list to an F# list. Not suitable for very large concat lists.
            let rec toListLoop (t : Term) (rs : Term list) =
                match t with
                | Concat(l,r) -> toListLoop l (toListLoop r rs)
                | Leaf -> rs
                | _ -> (t::rs)

            // obtain a list of concatenated items (i.e. lift ++ to ::)
            let inline toList t = toListLoop t []

            // access leftmost digit; fix associativity as needed
            let rec viewL t =
                match t with
                | Concat(l, r) ->
                    match l with
                    | Concat(ll, lr) -> viewL (Concat(ll, Concat(lr, r)))
                    | _ -> struct(l, r)
                | _ -> struct(t, Leaf)

            // access rightmost digit; fix associativity as needed
            let rec viewR t =
                match t with
                | Concat(l, r) ->
                    match r with
                    | Concat(rl, rr) -> viewR (Concat(Concat(l, rl), rr))
                    | _ -> struct(l, r)
                | _ -> struct(Leaf, t)

            // might need take/drop for digits.


        // mkNode2/3 will heuristically compact small arrays or binaries into
        // large ones. This increases locality, reduces average rope overheads 
        let mkNode2 d1 d2 =
            match d1, d2 with
            | Array a1, Array a2 when (len_large_arr >= (a1.Length + a2.Length)) ->
                Array <| ImmArray.Append(a1,a2) 
            | Binary b1, Binary b2 when (len_large_bin >= (b1.Length + b2.Length)) ->
                Binary <| ImmArray.Append(b1,b2)
            | _ -> Concat(d1,d2)

        let mkNode3 d1 d2 d3 = 
            match d1,d2,d3 with
            | Array a1, Array a2, Array a3 when (len_large_arr >= (a1.Length + a2.Length + a3.Length)) ->
                Array <| ImmArray.Append3(a1,a2,a3)
            | Binary b1, Binary b2, Binary b3 when (len_large_bin >= (b1.Length + b2.Length + b3.Length)) ->
                Binary <| ImmArray.Append3(b1,b2,b3)
            | _ -> Concat(d1, Concat(d2, d3))

        // chunkify a list of digits into larger nodes
        // should receive at least two input terms
        let rec chunkify (lv : Term list) : Term list =
            match lv with
            | (b1::b2::rem) -> 
                match rem with 
                | [b3] -> [mkNode3 b1 b2 b3] // 3 elems
                | _ -> (mkNode2 b1 b2) :: (chunkify rem)
            | _ -> lv 

        // add digit or node to left of rope
        let rec consD d t =
            if VTerm.isLeaf d then t else
            match t with
            | Leaf -> unwrapSize d // singleton
            | Rope(l, s, r) ->
                match l with
                | Concat(l1, lRem) when ((CCL.len lRem) >= 3) ->
                    let chunks = lRem |> CCL.toList |> chunkify
                    let l' = Concat((sized d), l1)
                    let s' = List.foldBack consD chunks s 
                    mkRope l' s' r
                | _ ->
                    let l' = Concat((sized d), l)
                    mkRope l' s r
            | _ -> mkRope (sized d) Leaf (sized t)

        // add digit or node to right of rope
        let rec snocD t d =
            if VTerm.isLeaf d then t else
            match t with
            | Leaf -> unwrapSize d // singleton
            | Rope(l, s, r) ->
                match r with
                | Concat(rRem, r1) when ((CCL.len rRem) >= 3) ->
                    let chunks = rRem |> CCL.toList |> chunkify
                    let s' = List.fold snocD s chunks
                    let r' = Concat(r1, (sized d))
                    mkRope l s' r'
                | _ ->
                    let r' = Concat(r, (sized d))
                    mkRope l s r'
            | _ -> mkRope (sized t) Leaf (sized d)

        // given Digits(k), return a Rope(k). 
        let ofDigits digits =
            match digits with
            | Concat(l, r) -> mkRope l Leaf r
            | _ -> unwrapSize digits // singleton or empty

        // return (digit/node, rope'). 
        let rec viewLD t =
            match t with
            | Rope(l, s, r) ->
                let struct(l0, l') = CCL.viewL l
                let t' =
                    if not (VTerm.isLeaf l') then mkRope l' s r else
                    if VTerm.isLeaf s then ofDigits r else
                    let struct(sl, s') = viewLD s 
                    mkRope (unwrapSize sl) s' r
                struct(l0, t')
            | _ -> struct(t, Leaf)

        // return (rope', digit/node). Returns leaf as right digit if empty.
        let rec viewRD t = 
            match t with
            | Rope(l, s, r) ->
                let struct(r', r0) = CCL.viewR r
                let t' =
                    if not (VTerm.isLeaf r') then mkRope l s r' else
                    if VTerm.isLeaf s then ofDigits l else
                    let struct(s', sr) = viewRD s
                    mkRope l s' (unwrapSize sr)
                struct(t', r0)
            | _ -> struct(Leaf, t)

        // an array/binary of a single element
        let singleton v =
            match v with
            | Byte n -> n |> Array.singleton |> ImmArray.UnsafeOfArray |> Binary
            | _ -> v |> Array.singleton |> ImmArray.UnsafeOfArray |> Array

        let cons v t = 
            let struct(d0, t') = viewLD t
            match d0 with
            | Binary b when ((b.Length < len_small_bin) && (isByte v)) ->
                let b' = b.Cons(toByte v)
                consD (Binary b') t'
            | Array a when (a.Length < len_small_arr) ->
                let a' = a.Cons(v)
                consD (Array a') t'
            | _ ->
                consD (singleton v) (consD d0 t')

        let snoc t v =
            let struct(t', d0) = viewRD t
            match d0 with
            | Binary b when ((b.Length < len_small_bin) && (isByte v)) ->
                snocD t' (Binary (b.Snoc(toByte v)))
            | Array a when (a.Length < len_small_arr) ->
                snocD t' (Array (a.Snoc(v)))
            | _ ->
                snocD (snocD t' d0) (singleton v)

        let rec append tl tr =
            match tl with
            | Rope(ll, ls, lr) ->
                match tr with
                | Rope(rl, rs, rr) ->
                    let chunks = Concat(lr,rl) |> CCL.toList |> chunkify
                    let ls' = List.fold snocD ls chunks 
                    mkRope ll (append ls' rs) rr
                | _ -> snocD tl tr
            | _ -> consD tl tr

        let rec _ofBasicList acc v =
            if StemBits.isEmpty v.Stem then
                match v.Term with
                | Branch(l, r) ->
                    _ofBasicList (snoc acc l) r
                | t ->
                    append acc t
            else failwith "invalid list"

        let inline ofBasicList v =
            _ofBasicList Leaf v 

        [<return: Struct>]
        let rec (|ViewL|_|) t =
            if VTerm.isLeaf t then ValueNone else
            let struct(d0, t') = viewLD t
            match unwrapSize d0 with
            | Array a ->
                assert(a.Length > 0)
                let tf = if (a.Length = 1) then t' else consD (Array (a.Drop(1))) t'
                ValueSome(struct(a[0], tf))
            | Binary b ->
                assert(b.Length > 0)
                let tf = if (b.Length = 1) then t' else consD (Binary (b.Drop(1))) t'
                ValueSome(struct(ofByte b[0], tf))
            | Concat(l, r) -> (|ViewL|_|) (consD l (consD r t'))
            | Leaf -> (|ViewL|_|) t'
            | Branch(l,r) when StemBits.isEmpty r.Stem ->
                ValueSome(struct(l, consD (r.Term) t'))
            | _ -> ValueNone
            
        [<return: Struct>]
        let rec (|ViewR|_|) t =
            if VTerm.isLeaf t then ValueNone else
            let struct(t', d0) = viewRD t
            match unwrapSize d0 with
            | Array a ->
                assert(a.Length > 0)
                let ix = (a.Length - 1)
                let tf = if (a.Length = 1) then t' else snocD t' (Array (a.Take(ix)))
                ValueSome(struct(tf, a[ix]))
            | Binary b ->
                assert(b.Length > 0)
                let ix = (b.Length - 1)
                let tf = if (b.Length = 1) then t' else snocD t' (Binary (b.Take(ix)))
                ValueSome(struct(tf, ofByte (b[ix])))
            | Concat(l, r) -> (|ViewR|_|) (snocD (snocD t' l) r)
            | Leaf -> (|ViewR|_|) t'
            | Branch _ ->
                // convert to a rope and retry
                (|ViewR|_|) (append t' (d0 |> ofTerm |> ofBasicList))
            | _ -> ValueNone

        let rec _take sz t =
            // invariant: 0 < sz < len t
            match t with
            | Rope(l,s,r) ->
                let szL = len l
                let szLS = szL + len s
                if (sz <= szL) then 
                    let l' = if (sz = szL) then l else digitsTake sz l
                    ofDigits l'
                elif (sz <= szLS) then
                    let sRem = if (sz = szLS) then s else _take (sz - szL) s
                    let struct(s', sr) = viewRD sRem
                    mkRope l s' (unwrapSize sr)
                else
                    let r' = digitsTake (sz - szLS) r
                    mkRope l s r'
            | _ -> digitsTake sz (unwrapSize t)

        // take first sz elements from rope, retaining rope balance
        let take sz t =
            if (0UL = sz) then Leaf else
            let szT = len t 
            if (sz < szT) then _take sz t else
            if (sz = szT) then t else
            failwith "invalid list take"

        let rec _drop sz t =
            // invariant: 0 < sz < len t
            match t with
            | Rope(l,s,r) ->
                let szL = len l
                let szLS = szL + len s
                if (sz < szL) then
                    let l' = digitsDrop sz l
                    mkRope l' s r
                elif (sz < szLS) then
                    let sRem = if (sz = szL) then s else _drop (sz - szL) s
                    let struct(sl, s') = viewLD sRem 
                    mkRope (unwrapSize sl) s' r
                else
                    let r' = if (sz = szLS) then r else digitsDrop (sz - szLS) r
                    ofDigits r'
            | _ -> digitsDrop sz (unwrapSize t)

        // drop first sz elements from rope, retaining rope balance
        let drop sz t =
            if (0UL = sz) then t else
            let szT = len t
            if (sz < szT) then _drop sz t else
            if (sz = szT) then Leaf else
            failwith "invalid list drop"

        // return true if term represents binary data.
        let rec isBinary t =
            match t with
            | Binary _ | Leaf -> 
                true
            | Array a ->
                Seq.forall isByte (a :> seq<Value>)
            | Concat(l,r) ->
                isBinary l && isBinary r
            | Take(sz, t') -> 
                isBinary (applySize sz t')
            | Branch(l, r) ->
                if (isByte l) && (StemBits.isEmpty r.Stem) 
                  then isBinary r.Term
                  else false
            | Stem64 _ -> 
                false

        let rec copyToBinary src dst ixDst =
            // assumes src isBinary, sufficient space in dst.
            match src with
            | Binary b -> 
                b.CopyTo(dst, ixDst)
            | Array a ->
                for ixA in 0 .. (a.Length - 1) do
                    dst[ixDst + ixA] <- toByte (a[ixA])
            | Leaf -> ()
            | Concat(l,r) ->
                copyToBinary l dst ixDst
                copyToBinary r dst (ixDst + int (len l))
            | Take(sz,src') ->
                copyToBinary (applySize sz src') dst ixDst
            | Branch(l,r) -> 
                dst[ixDst] <- toByte l
                copyToBinary (r.Term) dst (ixDst + 1)
            | Stem64 _ -> failwith "invalid binary src"

        let rec copyToArray src dst ixDst =
            // assumes src isList and sufficient space in dst
            match src with
            | Array a ->
                a.CopyTo(dst, ixDst)
            | Binary b ->
                for ixB in 0 .. (b.Length - 1) do 
                    dst[ixDst + ixB] <- ofByte (b[ixB])
            | Leaf -> ()
            | Concat(l, r) ->
                copyToArray l dst ixDst
                copyToArray r dst (ixDst + int (len l))
            | Take(sz, src') ->
                copyToArray (applySize sz src') dst ixDst
            | Branch(l, r) ->
                dst[ixDst] <- l
                copyToArray (r.Term) dst (ixDst + 1)
            | Stem64 _ -> failwith "invalid array src"

        let toSeq : Term -> seq<Value> =
            let fn t =
                match t with
                | ViewL(struct(v,t')) ->
                    Some(v,t')
                | _ -> None
            Seq.unfold fn

        let ofSeq (sv : seq<Value>) : Term =
            let fn acc v = snoc acc v
            Seq.fold fn Leaf sv

        let toSeqRev : Term -> seq<Value> =
            let fn t =
                match t with
                | ViewR(struct(t',v)) ->
                    Some(v, t')
                | _ -> None
            Seq.unfold fn

        let ofSeqRev sv =
            let fn acc v = cons v acc
            Seq.fold fn Leaf sv

        let inline fold (fn : 'A -> Value -> 'A) (acc : 'A) (t : Term) : 'A =
            Seq.fold fn acc (toSeq t)

        let inline foldBack (fn : Value -> 'A -> 'A) (t : Term) (acc : 'A) : 'A =
            let fn' acc v = fn v acc
            Seq.fold fn' acc (toSeqRev t) 

        let map (fn : Value -> Value) (t : Term) : Term =
            t |> toSeq |> Seq.map fn |> ofSeq

        let concat (ts : seq<Term>) : Term =
            Seq.fold append Leaf ts

        let rec forall (pred : Value -> bool) (t : Term) : bool =
            match t with
            | ViewL(struct(v,t')) -> (pred v) && (forall pred t')  
            | Leaf -> true
            | _ -> failwith "invalid list"

    [<return: Struct>]
    let (|ListT|_|) t =
        match t with
        | Leaf | Array _ | Binary _ | Take _ | Concat _ ->
            ValueSome(t)
        | Branch(_, r) ->
            if isList r 
                then ValueSome(Rope.ofBasicList (ofTerm t))
                else ValueNone
        | Stem64 _ -> ValueNone

    // convert to a rope
    [<return: Struct>]
    let inline (|List|_|) v =
        if StemBits.isEmpty v.Stem 
          then (|ListT|_|) v.Term
          else ValueNone

    // is convertable to a binary array
    let inline isBinary v =
        (StemBits.isEmpty v.Stem) && (Rope.isBinary v.Term) &&
        ((uint64 System.Int32.MaxValue) >= (Rope.len v.Term))

    [<return: Struct>]
    let (|BinaryArray|_|) v =
        if isBinary v then
            let arr = Array.zeroCreate (int (Rope.len v.Term))
            Rope.copyToBinary (v.Term) arr 0
            ValueSome(arr)
        else ValueNone

    let toBinary v =
        match v with
        | Binary arr -> arr
        | _ -> invalidArg (nameof v) "value is not a binary"

    [<return: Struct>]
    let (|ValueArray|_|) v =
        let ok = (isList v) && ((uint64 System.Int32.MaxValue) >= (Rope.len v.Term))
        if ok then
            let arr = Array.zeroCreate (int (Rope.len v.Term))
            Rope.copyToArray (v.Term) arr 0
            ValueSome(arr)
        else ValueNone

    [<return: Struct>]
    let (|String|_|) v = 
        match v with
        | BinaryArray b ->
            try 
                let enc = System.Text.UTF8Encoding(false,true)
                enc.GetString(b) |> ValueSome
            with
            | _ -> ValueNone
        | _ -> ValueNone
    
    let toString v =
        match v with
        | String s -> s
        | _ -> invalidArg (nameof v) "value is not a string"

    [<return: Struct>] 
    let (|PairT|_|) t =
        match t with
        | Branch(l,r) ->
            // direct branches, don't try to convert to list 
            ValueSome(struct(l,r))
        | Rope.ViewL(struct(l,rt)) ->
            // this covers array, binary, concat, take
            // does not support a right stem
            ValueSome(struct(l, ofTerm rt))
        | _ -> ValueNone

    [<return: Struct>]
    let inline (|Pair|_|) v =
        if StemBits.isEmpty v.Stem
          then (|PairT|_|) v.Term 
          else ValueNone

    let rec _eqV s a b =
        if (a.Stem <> b.Stem) then false else _eqT s (a.Term) (b.Term)
    and _eqT s a b =
        match a with
        | Leaf ->
            match b with
            | Leaf -> _eqS s
            | _ -> false
        | Stem64(aBits, a') ->
            match b with
            | Stem64(bBits, b') when (aBits = bBits) -> _eqT s a' b'
            | _ -> false
        | PairT(struct(al,ar)) ->
            match b with
            | PairT(struct(bl,br)) -> _eqV (struct(ar,br)::s) al bl
            | _ -> false
        | _ -> false
    and _eqS s =
        match s with
        | (struct(a,b)::s') -> _eqV s' a b
        | [] -> true

    // equality test on values
    let eq a b = 
        _eqV [] a b 


    // four most basic node types
    let (|L|R|P|U|) v = 
        match v with
        | _ when isUnit v -> U
        | Pair(struct(a,b)) -> P(a,b) 
        | _ ->
            assert(isStem v)
            let inR = stemHead v
            let v' = stemTail v
            if inR then R(v') else L(v')

    let cons v l =
        match l with 
        | List t -> ofTerm (Rope.cons v t)
        | _ -> failwith "cons: invalid list"

    let snoc l v =
        match l with
        | List t -> ofTerm (Rope.snoc t v)
        | _ -> failwith "snoc: invalid list"

    let append l r =
        match l, r with
        | List tl, List tr -> ofTerm (Rope.append tl tr)
        | _ -> failwith "append: invalid list(s)"

    let rec private dropSharedPrefix a b =
        // a bit of extra logic to match full bytes where easy to do so
        // can improve performance significantly due to common use of 
        // multi-byte labels. 
        let maskByte = 0xFFUL <<< 56
        let maskRem = ~~~maskByte
        let shareByte = ((maskByte &&& a.Stem) = (maskByte &&& b.Stem)) 
                     && (0UL <> (maskRem &&& a.Stem))
                     && (0UL <> (maskRem &&& b.Stem))
        if shareByte then
            let a' = { a with Stem = a.Stem <<< 8 }
            let b' = { b with Stem = b.Stem <<< 8 }
            dropSharedPrefix a' b'
        else
            let shareBit = (isStem a) && (isStem b) && ((stemHead a) = (stemHead b))
            if not shareBit then struct(a,b) else
            dropSharedPrefix (stemTail a) (stemTail b)

    let tryMatchStem p v = 
        let struct(p', v') = dropSharedPrefix p v
        if isUnit p' then ValueSome(v') else ValueNone

    [<return: Struct>]
    let inline (|Stem|_|) p v =
        tryMatchStem p v

    [<return: Struct>]
    let inline (|Variant|_|) s =
        tryMatchStem (label s)

    let rec accumSharedPrefixLoop acc a b =
        let shareHead = (isStem a) && (isStem b) && ((stemHead a) = (stemHead b))
        if not shareHead then struct(acc, a, b) else
        let acc' = consStemBit (stemHead a) acc // accumulates in reverse order
        accumSharedPrefixLoop acc' (stemTail a) (stemTail b)
    
    // returns a triple with (reversed shared prefix, remainder of a, remainder of b)
    let inline findSharedPrefix a b = 
        accumSharedPrefixLoop unit a b

    let rec private _bitsAppendRev p dst =
        if isStem p 
          then _bitsAppendRev (stemTail p) (consStemBit (stemHead p) dst)
          else assert(isUnit p); dst

    // Access a value within a record. Essentially a radix tree lookup.
    let rec record_lookup p r =
        let struct(p',r') = dropSharedPrefix p r
        if isUnit p' then ValueSome(r') else
        assert(isStem p')
        match r' with
        | Pair(struct(a,b)) ->
            record_lookup (stemTail p') (if stemHead p' then b else a)
        | _ -> ValueNone // partial field or another branch

    let rec record_delete p r =
        let struct(psh, p', r') = findSharedPrefix p r
        if isUnit p' then unit else // last field deleted.
        assert(isStem p')
        if isUnit r' then unit else // partial field deleted.
        match r' with
        | Pair(struct(a,b)) ->
            let rf =
                if stemHead p' then
                    let b' = record_delete (stemTail p') b
                    if isUnit b' then left a else pair a b'
                else
                    let a' = record_delete (stemTail p') a
                    if isUnit a' then right b else pair a' b
            _bitsAppendRev psh rf
        | _ -> // field not in record
            assert((isStem r') && ((stemHead r') <> (stemHead p')))
            r // nothing deleted

    let rec record_insert p v r =
        let struct(psh, p', r') = findSharedPrefix p r 
        let rf =
            if isUnit p' then v else    // replace value at r'
            assert(isStem p')
            if isUnit r' then (withLabel p' v) else // first field.
            match r' with
            | Pair(struct(a,b)) -> // edit existing branch
                if stemHead p' then
                    let b' = record_insert (stemTail p') v b
                    pair a b'
                else
                    let a' = record_insert (stemTail p') v a
                    pair a' b
            | _ -> // new branch in record
                assert((isStem r') && ((stemHead p') <> (stemHead r')))
                let rb = stemTail r'
                let vb = withLabel (stemTail p') v 
                if stemHead p' then pair rb vb else pair vb rb
        _bitsAppendRev psh rf

    let asRecord ks vs = 
        let addElem r k v = record_insert (label k) v r
        List.fold2 addElem unit ks vs

    let ofMap (m:Map<string,Value>) : Value =
        let addElem r k v = record_insert (label k) v r
        Map.fold addElem unit m

    let (|RecL|) lks r = 
        let vs = List.map (fun k -> record_lookup k r) lks
        let r' = List.fold (fun s k -> record_delete k s) r lks
        struct(vs,r')

    /// Record with optional keys.
    let inline (|Record|) ks =
        (|RecL|) (List.map label ks)

    /// Record containing all listed keys.
    [<return: Struct>]
    let (|FullRec|_|) ks r =
        let struct(vs, r') = (|Record|) ks r
        if List.exists ValueOption.isNone vs then ValueNone else
        ValueSome (List.map ValueOption.get vs, r')


    let private isFlagField opt = 
        match opt with
        | ValueSome Unit | ValueNone -> true
        | _ -> false

    /// Extract flags as booleans. A flag is a label within a record whose only data
    /// is presence vs. absence.  
    [<return: Struct>]
    let (|Flags|_|) ks r =
        let struct(vs, r') = (|Record|) ks r
        if List.exists (isFlagField >> not) vs then ValueNone else
        ValueSome (List.map ValueOption.isSome vs, r')

    module private VerifyUTF8 = 
        // support to check UTF8 structure incrementally 

        let inline isC1 c = (0x80uy &&& c) = (0x00uy)
        let inline isCC c = (0xC0uy &&& c) = (0x80uy)
        let inline isC2 c = (0xE0uy &&& c) = (0xC0uy)
        let inline isC3 c = (0xF0uy &&& c) = (0xE0uy)
        let inline isC4 c = (0xF8uy &&& c) = (0xF0uy)
        let inline isSurrogateHalf c = 
            (0xDFFF >= c) && (c >= 0xD800)

        let rec verifyLenCC lRev nCC =
            if(nCC > 3) then false else 
            match lRev with
            | [] -> (0 = nCC)
            | (c::lRev') ->
                if (isCC c) then verifyLenCC lRev' (1 + nCC)
                elif (isC2 c) then (nCC >= 1) 
                elif (isC3 c) then (nCC >= 2)
                elif (isC4 c) then (nCC >= 3) 
                else false

        // verify the most recent added character is complete
        // and within the valid range, and not surrogate half
        let verifyPrior lRev =
            match lRev with
            | [] -> true
            | (c1::_) when isC1 c1 -> true
            | (c1::c2::_) when (isC2 c2) && (isCC c1) ->
                let cp = (((int c2) &&& 0x1F) <<< 6) |||
                         (((int c1) &&& 0x3F) <<< 0)
                (cp > 0x7F) 
            | (c1::c2::c3::_) when (isC3 c3) && (isCC c2) && (isCC c1) ->
                let cp = (((int c3) &&& 0x0F) <<< 12) |||
                         (((int c2) &&& 0x3F) <<<  6) |||
                         (((int c1) &&& 0x3F) <<<  0)
                (cp > 0x7FF) && (not (isSurrogateHalf cp))
            | (c1::c2::c3::c4::_) when (isC4 c4) && (isCC c3) && (isCC c2) && (isCC c1) ->
                let cp = (((int c4) &&& 0x07) <<< 18) |||
                         (((int c3) &&& 0x3F) <<< 12) |||
                         (((int c2) &&& 0x3F) <<<  6) |||
                         (((int c1) &&& 0x3F) <<<  0)
                (0x10FFFF >= cp) && (cp > 0xFFFF)
            | _ -> false

        let verifyPartialUTF8 lRev c1 =
            if isCC c1 
                then verifyLenCC lRev 1
                else verifyPrior lRev

    let private _isRecordLabelChar b =
        ((byte('a') <= b) && (b <= byte('z'))) ||
        ((byte('0') <= b) && (b <= byte('9'))) ||
        (b = uint8('-'))

    let private _isRecordLabel = 
        List.forall _isRecordLabelChar 

    let rec private _isRecord rs lb pb v =
        // lb is reversed.
        if ((0x100 &&& pb) = 0x100) then
            // byte completed! I'll do a quick check on the UTF8 structure.
            let b = uint8 (0xFF &&& pb)
            if not (VerifyUTF8.verifyPartialUTF8 lb b) then false else
            if(0uy <> b) then _isRecord rs (b::lb) 1 v else
            // null terminator is end of label.
            if not (_isRecordLabel lb) then false else
            match rs with
            | struct(lb',pb',v')::rs' -> _isRecord rs' lb' pb' v'
            | [] -> true
        else
            // need more bits!
            match v with
            | U -> false // label is not 8-bit aligned
            | L v' -> _isRecord rs lb (pb <<< 1) v'
            | R v' -> _isRecord rs lb ((pb <<< 1) ||| 1) v'
            | P (l,r) -> 
                let rs' = struct(lb, ((pb <<< 1) ||| 1), r)::rs
                _isRecord rs' lb (pb <<< 1) l

    let empty_label = label ""

    /// Check that a value is a record with null-terminated UTF-8 labels.
    /// Rejects the empty symbol and C0 or DEL characters. I'm wondering
    /// if I should restrict the symbols to UTF-8 identifiers.
    ///
    /// Note: the UTF-8 check currently allows overlong encodings. This is
    /// not ideal, but the extra logic isn't too important for a bootstrap
    /// implementation.
    let isRecord (v:Value) : bool =
        if isUnit v then true else
        if ValueOption.isSome (record_lookup empty_label v) then false else
        _isRecord [] [] 1 v

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
        if not (isRecord v) then ValueNone else
        ValueSome (Map.ofSeq (recordSeq v))

    /// Match record as a map.
    [<return: Struct>]
    let (|RecordMap|_|) v =
        tryRecord v

    /// Match any variant, returning label and value.
    [<return: Struct>]
    let (|AnyVariant|_|) v =
        match v with
        | RecordMap m when (1 = Map.count m) ->
            Map.toList m |> List.head |> ValueSome
        | _ -> ValueNone

    let inline private _toHex n =
        if (n < 10uy) 
            then char (uint8 '0' + n) 
            else char (uint8 'A' + (n - 10uy))

    // roll my own string escape for pretty-printing strings 
    let private _escape (s0 : string) : string =
        let sb = System.Text.StringBuilder()
        for c in System.Text.Encoding.UTF8.GetBytes(s0) do
            let okByte = (0x7Fuy > c) && (c >= 0x20uy) 
                      && (c <> uint8 '"') && (c <> uint8 '%')
            if okByte then ignore (sb.Append(char c)) else
            ignore <| sb.Append('%')
                        .Append(_toHex ((0xF0uy &&& c) >>> 4))
                        .Append(_toHex ((0x0Fuy &&& c) >>> 0))
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
            | Int64 n when not bLR -> 
                yield (n.ToString())
            | String s ->
                let sLines = s.Split('\n')
                if (sLines.Length = 1) then
                    yield "\""
                    yield _escape s
                    yield "\""
                else 
                    yield "\"\"\""
                    for line in sLines do
                        yield "\n "
                        yield line
                    yield "\n\"\"\""
            | List l ->
                yield "["
                match l with
                | Rope.ViewL(struct(v0, l')) ->
                    yield! _ppV false v0
                    yield! Rope.toSeq l' |> Seq.collect (fun v -> seq { yield ", "; yield! _ppV false v})
                | _ -> ()
                yield "]"
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
            yield (_escape k)
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


type Value = Value.Value
type Rope = Value.Term

module PartialValue =
    // Values that contain references to an abstract or future context.
    // Encoded as glas values. 
    //
    // Initial encoding:
    //
    // type PValue =
    //   | PConst of Value                  prefix 00
    //   | PRef of Value                    prefix 01
    //   | PStem of Bits * PValue           prefix 10
    //   | PBranch of PValue * PValue       prefix 11
    //
    // Note: It is feasible to represent computations within PRefs. Thus,
    // further extension is via defining the 'type' of a reference.
    //
    // However, this does seem to be missing potentially useful features
    // such as logical concatenation of partial lists.

    type PValue = Value

    let private labelOfBits (bits : bool list) : Value =
        List.foldBack (Value.StemBits.cons) bits (Value.StemBits.empty)
            |> Value.ofStem

    let lPConst = labelOfBits [false; false]
    let lPRef = labelOfBits [false; true]
    let lPStem = labelOfBits [true; false]
    let lPBranch = labelOfBits [true; true]

    [<return: Struct>]
    let inline private (|VStem|_|) p = Value.(|Stem|_|) p

    let (|PConst|PRef|PStem|PBranch|) (pv : PValue) =
        match pv with
        | VStem lPConst v -> 
            PConst(v)
        | VStem lPRef r -> 
            PRef(r)
        | VStem lPStem (Value.Pair (p, v)) -> 
            PStem(p, pv)
        | VStem lPBranch (Value.Pair (l, r)) -> 
            PBranch(l, r)
        | _ -> failwith "type error: not a partial value"

    let PConst = Value.withLabel lPConst 

    let PRef = Value.withLabel lPRef

    let PStem (p : Value) (pv : PValue) : PValue =
        if not (Value.isBits p) then failwith "type error: invalid bits" else
        if Value.isUnit p then pv else
        match pv with
        | PConst(v) -> 
            PConst (Value.withLabel p v)
        | PStem(p2, pv') -> 
            let p' = Value.withLabel p p2 // append the labels
            Value.withLabel lPStem (Value.pair p' pv')
        | _ -> 
            Value.withLabel lPStem (Value.pair p pv)
    
    let PBranch (l:PValue) (r:PValue) : PValue =
        match l, r with
        | PConst(lv), PConst(rv) -> PConst(Value.pair lv rv)
        | _ -> Value.withLabel lPBranch (Value.pair l r)

    let rec private _getVarGen1 (st : PValue list) (pv : PValue) =
        match pv with
        | PConst _ -> _getVarGen0 st 
        | PRef vRef -> Some(vRef, st)
        | PStem (_, pv') -> _getVarGen1 st pv'
        | PBranch (pvL, pvR) -> _getVarGen1 (pvR::st) pvL
    and private _getVarGen0 (st : PValue list) : (Value * PValue list) option =
        match st with
        | (pv::st') -> _getVarGen1 st' pv
        | [] -> None

    let getVars (pv:PValue) : Value seq = 
        Seq.unfold _getVarGen0 [pv]

    let rec rewriteVars (rw : Value -> PValue) (pv:PValue) : PValue =
        match pv with
        | PConst _ -> 
            pv
        | PRef vRef -> 
            rw vRef
        | PStem (p, pvS) ->
            let pvS' = rewriteVars rw pvS  
            PStem p pvS'
        | PBranch (pvL, pvR) ->
            let pvL' = rewriteVars rw pvL
            let pvR' = rewriteVars rw pvR 
            PBranch pvL' pvR'

    // simplify a PValue via identity rewrite. This will:
    //  (1) merge sequential PStem elements
    //  (2) merge PBranch or PStem into constant where possible
    let simplify = rewriteVars PRef 

    // TODO:
    // get/put/del on partial records (known static paths)
    // let record_delete 


module DataType =
    // a representation of basic data types (as glas values)
    //
    // Possible types.
    //   - alt:(A * B)                      non-algebraic choice of types.
    //   - rec:(path:Path eq:Type in:Type)  allows for open records.
    //   - unit                             
    //   - var:Var
    //   - loop:(var:Var eq:Type)           where Type contains Var
    //   - void                             the type of failure!
    //
    // Loops are awkward to express this way, IMO. Instead, it might be better to
    // describe a loop as part of a unification of variables.

    let lAlt = Value.label "alt"
    let lRec = Value.label "rec"
    let lVar = Value.label "var"
    let lLoop = Value.label "loop"
    let lUnit = Value.label "unit"
    let lVoid = Value.label "void"

    let lEq = Value.label "eq"
    let lIn = Value.label "in"

