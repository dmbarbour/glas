namespace Glas

// It is weird to me that F# doesn't include an immutable bytestring.
// I'll just add this one.

module BS =
    open System.Runtime.InteropServices

    [< CustomEquality; CustomComparison; Struct >]
    type ByteString =
        val UnsafeBytes : byte[]
        val Offset : int
        val Length : int
        internal new (arr, off, len) = { UnsafeBytes = arr; Offset = off; Length = len }
        static member Empty = ByteString(Array.empty, 0, 0)

        member inline x.Item (ix : int) : byte =
            assert ((0 <= ix) && (ix < x.Length))
            x.UnsafeBytes.[x.Offset + ix]

        member x.GetSlice (iniOpt : int option, finOpt : int option) : ByteString =
            let ini = defaultArg iniOpt 0
            let fin = defaultArg finOpt (x.Length - 1)
            if ((ini < 0) || (fin >= x.Length)) then raise (System.IndexOutOfRangeException "ByteString slice")
            elif (fin < ini) then ByteString.Empty
            else ByteString(x.UnsafeBytes, ini + x.Offset, 1 + (fin - ini))

        static member inline FoldLeft f r0 (a:ByteString) =
            let mutable r = r0
            for ix = a.Offset to (a.Offset + a.Length - 1) do
                r <- f r a.UnsafeBytes.[ix]
            r

        /// basic FNV-1a hash (32 bits)
        static member Hash32 (a:ByteString) : uint32 =
            let fnvPrime = 16777619u
            let offsetBasis = 2166136261u
            let accum h b = ((h ^^^ (uint32 b)) * fnvPrime)
            ByteString.FoldLeft accum offsetBasis a

        override x.GetHashCode() = int <| ByteString.Hash32 x

        static member Eq (a:ByteString) (b:ByteString) : bool =
            if (a.Length <> b.Length) then false else
            let rec loop ix =
                if (a.Length = ix) then true else
                if (a.[ix] <> b.[ix]) then false else
                loop (1 + ix)
            loop 0

        override x.Equals (yobj : System.Object) = 
            match yobj with
                | :? ByteString as y -> ByteString.Eq x y
                | _ -> false
        interface System.IEquatable<ByteString> with
            member x.Equals y = ByteString.Eq x y

        static member Compare (a:ByteString) (b:ByteString) : int =
            let sharedLen = min a.Length b.Length
            let rec loop ix =
                    if (sharedLen = ix) then 0 else
                    let c = compare a.[ix] b.[ix]
                    if (0 <> c) then c else
                    loop (1 + ix)
            let cmpSharedLen = loop 0 
            if (0 <> cmpSharedLen) then cmpSharedLen else
            compare a.Length b.Length

        interface System.IComparable with
            member x.CompareTo (yobj : System.Object) =
                match yobj with
                    | :? ByteString as y -> ByteString.Compare x y
                    | _ -> invalidArg "yobj" "cannot compare values of different types"
        interface System.IComparable<ByteString> with
            member x.CompareTo y = ByteString.Compare x y

        member x.ToSeq() = 
            let a = x // copy byref for capture
            seq {
                for ix = (a.Offset) to (a.Offset + a.Length - 1) do
                    yield a.UnsafeBytes.[ix]
            }
        member inline private x.GetEnumerator() = 
            x.ToSeq().GetEnumerator()

        interface System.Collections.Generic.IEnumerable<byte> with
            member x.GetEnumerator() = x.GetEnumerator()

        interface System.Collections.IEnumerable with
            member x.GetEnumerator() = 
                x.GetEnumerator() :> System.Collections.IEnumerator

        // String conversion assumes an ASCII or UTF-8 encoding. 
        override x.ToString() : string =
            System.Text.Encoding.UTF8.GetString(x.UnsafeBytes, x.Offset, x.Length)

    let empty = ByteString.Empty
    let inline length (x : ByteString) = x.Length
    let inline isEmpty x = (0 = length x)

    let unsafeCreate (arr : byte []) (off : int) (len : int) : ByteString =
        assert((off >= 0) && (arr.Length >= (off + len)))
        ByteString(arr,off,len)
    
    let unsafeCreateA (arr : byte []) = 
        ByteString(arr, 0, arr.Length)

    // avoid allocation for single character bytestrings
    let private allBytes = [| System.Byte.MinValue .. System.Byte.MaxValue |]
    do assert (256 = allBytes.Length)

    let singleton (c : byte) = ByteString(allBytes, (int c), 1)

    let inline ofSeq (s : seq<byte>) : ByteString = unsafeCreateA (Array.ofSeq s)
    let inline ofList (s : byte list) : ByteString = unsafeCreateA (Array.ofList s)
    let inline toArray (s : ByteString) : byte [] =
        if isEmpty s then Array.empty else
        Array.sub (s.UnsafeBytes) (s.Offset) (s.Length)
    let inline toSeq (s : ByteString) : seq<byte> = s :> seq<byte>
    let inline toList (s : ByteString) : byte list = List.ofSeq (toSeq s)

    let inline blit (src : ByteString) (tgt : byte[]) (offset : int) =
        Array.blit (src.UnsafeBytes) (src.Offset) tgt offset (src.Length)


    /// concatenate into one large bytestring
    let concat (xs : seq<ByteString>) : ByteString =
        let mem = new System.IO.MemoryStream()
        for x in xs do
            mem.Write(x.UnsafeBytes, x.Offset, x.Length)
        unsafeCreateA (mem.ToArray())

    let cons (b : byte) (s : ByteString) : ByteString =
        let mem = Array.zeroCreate (1 + s.Length)
        do mem.[0] <- b
        do blit s mem 1
        unsafeCreateA mem

    let snoc (s : ByteString) (b : byte) : ByteString =
        let mem = Array.zeroCreate(s.Length + 1)
        do blit s mem 0
        do mem.[s.Length] <- b
        unsafeCreateA mem

    let append (a : ByteString) (b : ByteString) : ByteString =
        if isEmpty a then b else
        if isEmpty b then a else
        let mem = Array.zeroCreate (a.Length + b.Length)
        do blit a mem 0
        do blit b mem (a.Length)
        unsafeCreateA mem

    /// Appending three items comes up quite frequently due to separators.
    let append3 (a:ByteString) (b:ByteString) (c:ByteString) : ByteString =
        let mem = Array.zeroCreate (a.Length + b.Length + c.Length)
        do blit a mem 0
        do blit b mem (a.Length)
        do blit c mem (a.Length + b.Length)
        unsafeCreateA mem

    /// take and drop are slices that won't raise range errors.
    let inline take (n : int) (s : ByteString) : ByteString =
        if (n < 1) then empty else
        if (n >= s.Length) then s else
        unsafeCreate s.UnsafeBytes s.Offset n

    let inline drop (n : int) (s : ByteString) : ByteString =
        if (n < 1) then s else
        if (n >= s.Length) then empty else
        unsafeCreate s.UnsafeBytes (s.Offset + n) (s.Length - n) 

    /// takeLast and dropLast are like take and drop, but index from the end
    let inline takeLast (n : int) (s : ByteString) : ByteString = drop (s.Length - n) s
    let inline dropLast (n : int) (s : ByteString) : ByteString = take (s.Length - n) s

    /// unsafe accessors, use only when you know the bytestring is non-empty
    let inline unsafeHead (x : ByteString) : byte = 
        x.UnsafeBytes.[x.Offset]
    let inline unsafeTail (x : ByteString) : ByteString = 
        unsafeCreate (x.UnsafeBytes) (x.Offset + 1) (x.Length - 1)
    
    /// basic left-to-right fold function.
    let inline fold f r0 s = ByteString.FoldLeft f r0 s

    /// head is the first byte, tail is all remaining bytes
    let inline head (x : ByteString) : byte = 
        if isEmpty x then invalidArg "x" "not enough elements" else unsafeHead x
    let inline tail (x : ByteString) : ByteString =
        if isEmpty x then invalidArg "x" "not enough elements" else unsafeTail x

    let inline tryHead (x : ByteString) : byte option =
        if isEmpty x then None else Some (unsafeHead x)
    let inline tryTail (x : ByteString) : ByteString option =
        if isEmpty x then None else Some (unsafeTail x)

    let inline uncons (x : ByteString) : (byte * ByteString) =
        if isEmpty x then invalidArg "x" "not enough elements" else 
        (unsafeHead x, unsafeTail x)
    let inline tryUncons (x : ByteString) : (byte * ByteString) option =
        if isEmpty x then None else Some (unsafeHead x, unsafeTail x)

    /// Split bytestring with longest sequence matched by provided function.
    let inline span (f : byte -> bool) (x : ByteString) : struct(ByteString * ByteString) =
        let limit = (x.Offset + x.Length)
        let rec step ix =
            if ((ix = limit) || not (f (x.UnsafeBytes.[ix]))) 
                then ix 
                else step (1 + ix)
        let stop = step x.Offset
        let l = unsafeCreate (x.UnsafeBytes) (x.Offset) (stop - x.Offset)
        let r = unsafeCreate (x.UnsafeBytes) (stop) (x.Length - l.Length)
        struct(l,r)
    let inline takeWhile f x = 
        let struct(l,_) = span f x
        l
    let inline dropWhile f x = 
        let struct(_,r) = span f x 
        r

    /// Predicate Testing
    let inline forall pred x = isEmpty (dropWhile pred x)
    let inline exists pred x = not (forall (not << pred) x)
    

    /// As 'span', but working right to left
    let inline spanEnd (f : byte -> bool) (x : ByteString) : struct(ByteString * ByteString) =
        let rec step ix =
            let ix' = ix - 1 
            if ((ix = x.Offset) || not (f (x.UnsafeBytes.[ix'])))
                then ix
                else step ix' 
        let stop = step (x.Offset + x.Length)
        let l = unsafeCreate (x.UnsafeBytes) (x.Offset) (stop - x.Offset)
        let r = unsafeCreate (x.UnsafeBytes) (stop) (x.Length - l.Length)
        struct(l,r)
    let inline takeWhileEnd f x = 
        let struct(_,r) = spanEnd f x
        r
    let inline dropWhileEnd f x = 
        let struct(l,_) = spanEnd f x
        l
        
    /// Compute the maximal shared prefix between two strings.
    let sharedPrefix (a:ByteString) (b:ByteString) : ByteString =
        let ixMax = min (a.Length) (b.Length)
        let rec loop ix =
            let halt = (ix = ixMax) || (a.[ix] <> b.[ix])
            if halt then ix else loop (ix + 1)
        take (loop 0) a

    /// Compute the maximal shared suffix between two strings.
    let sharedSuffix (a:ByteString) (b:ByteString) : ByteString =
        let ixMax = min (a.Length) (b.Length)
        let rec loop ix =
            let halt = (ix = ixMax) || (a.[a.Length - ix] <> b.[b.Length - ix])
            if halt then ix else loop (ix + 1)
        takeLast (loop 0) a

    /// conversions for other string encodings
    let inline encodeString (s : string) (e : System.Text.Encoding) = 
        unsafeCreateA (e.GetBytes(s))
    let inline decodeString (x : ByteString) (e : System.Text.Encoding) = 
        e.GetString(x.UnsafeBytes, x.Offset, x.Length)

    /// fromString and toString assume UTF-8 encoding
    let inline fromString s = encodeString s System.Text.Encoding.UTF8
    let inline toString s = decodeString s System.Text.Encoding.UTF8

    /// Trim excess bytes from bytestring, if underlying binary is larger
    /// than the bytestring by a given threshold.
    let inline trimBytes' (threshold:int) (s:ByteString) : ByteString =
        if ((s.Length + threshold) >= s.UnsafeBytes.Length) then s else
        unsafeCreateA (toArray s) 

    /// Trim excess bytes from bytestring (no threshold)
    let inline trimBytes (s : ByteString) : ByteString = trimBytes' 0 s

    /// convenient access to secure hashes
    let sha256 (s : ByteString) : ByteString = 
        use alg = System.Security.Cryptography.SHA256.Create()
        let bytes = alg.ComputeHash(s.UnsafeBytes, s.Offset, s.Length)
        unsafeCreateA bytes

    let sha512 (s : ByteString) : ByteString = 
        use alg = System.Security.Cryptography.SHA512.Create()
        let bytes = alg.ComputeHash(s.UnsafeBytes, s.Offset, s.Length)
        unsafeCreateA bytes

    /// Convenient access to pinned bytestring data (for interop).
    let inline withPinnedBytes (s : ByteString) (action : nativeint -> 'x) : 'x =
        let pin = GCHandle.Alloc(s.UnsafeBytes, GCHandleType.Pinned)
        try let addr = (nativeint s.Offset) + pin.AddrOfPinnedObject()
            action addr
        finally pin.Free()

    /// Disposable access to Pinned bytestring data.
    ///     use ps = new PinnedByteString(s)
    ///     ... actions with ps.Addr ...
    [<Struct>]
    type PinnedByteString =
        val private Pin : GCHandle
        val BS : ByteString
        val Addr : nativeint
        new(s : ByteString) =
            let pin = GCHandle.Alloc(s.UnsafeBytes, GCHandleType.Pinned)
            let addr = (nativeint s.Offset) + pin.AddrOfPinnedObject()
            { Pin = pin; BS = s; Addr = addr }
        member inline x.Length with get() = x.BS.Length
        interface System.IDisposable with
            member x.Dispose() = x.Pin.Free()     


type ByteString = BS.ByteString

