namespace Glas

/// A compact, immutable list of bits (bools). Optimized for short lists.
[<Struct>]
type Bits = 
    { Head     : uint64         // 0 to 63 bits encoded in head.  
    ; Tail     : uint64 list    // each element is 64 bit chunk. 
    }

module Bits =

    let private hibit = 
        (1UL <<< 63)
    let private lobit = 
        1UL
    let inline private match_mask mask n = 
        ((mask &&& n) = mask) 
    let inline private msb n = 
        match_mask hibit n 
    let inline private lsb n = 
        match_mask lobit n 

    let inline private invalidArgMatchLen l r =
        invalidArg (nameof l) "lists of different length"

    /// Verify a Bits is structured correctly.
    /// This requires only that Head is non-zero.
    let valid (b : Bits) : bool =
        (0UL <> b.Head)

    /// Returns whether Bits is empty list.
    let inline isEmpty (b : Bits) : bool =
        (1UL = b.Head) && (List.isEmpty b.Tail)

    /// The empty Bits list.
    let empty : Bits = 
        { Head = 1UL; Tail = List.empty }

    /// Head element of a non-empty list.
    let head (b : Bits) : bool =
        if (1UL <> b.Head) then lsb b.Head else 
        match b.Tail with
        | (x :: _) -> lsb x
        | [] -> invalidArg (nameof b) "head of empty list"

    let tryHead (b : Bits) : bool option =
        if isEmpty b then None else Some (head b)

    /// Remainder of a non-empty list after head.
    let tail (b : Bits) : Bits =
        if (1UL <> b.Head) then { b with Head = (b.Head >>> 1) } else
        match b.Tail with
        | (x :: xs) -> { Head = (hibit ||| (x >>> 1)); Tail = xs }
        | [] -> invalidArg (nameof b) "tail of empty list"

    let tryTail (b : Bits) : Bits option =
        if isEmpty b then None else Some (tail b)

    /// Add element to head of list.
    let cons (e : bool) (b : Bits) : Bits =
        let eUL = if e then 1UL else 0UL
        let hd' = (b.Head <<< 1) ||| eUL
        if msb b.Head 
          then { Head = 1UL; Tail = (hd' :: b.Tail) }
          else { b with Head = hd' }   

    let inline (|Cons|_|) b =
        if isEmpty b then None else
        Some (head b, tail b)

    /// Add N elements of the same value to head of list.
    let rec consN (count : int) (e : bool) (b : Bits) : Bits =
        if (count < 1) then b else
        consN (count - 1) e (cons e b)

    /// Construct by replication of bool.
    let inline replicate count e =
        consN count e empty

    /// Just one bit.
    let inline singleton (e : bool) : Bits = cons e empty

    let inline private lenHd hd = 
        // 6-step computation of size via binary division.
        let mutable v = hd
        let mutable n = 0
        if (0UL <> (0xFFFFFFFF00000000UL &&& v)) then n <- n + 32; v <- v >>> 32;
        if (0UL <> (        0xFFFF0000UL &&& v)) then n <- n + 16; v <- v >>> 16;
        if (0UL <> (            0xFF00UL &&& v)) then n <- n +  8; v <- v >>>  8;
        if (0UL <> (              0xF0UL &&& v)) then n <- n +  4; v <- v >>>  4;
        if (0UL <> (               0xCUL &&& v)) then n <- n +  2; v <- v >>>  2;
        if (0UL <> (               0x2UL &&& v)) then n+1 else n

    /// Return number of bits.
    let length (b : Bits) : int =
        (lenHd b.Head) + (64 * (List.length b.Tail))

    let inline private headBits hd = 
        (1UL <<< (lenHd hd)) - 1UL

    let inline private bmap1UL fn b =
        let m = headBits b.Head
        { Head = ((~~~m) &&& b.Head) ||| (m &&& (fn b.Head))
        ; Tail = List.map fn b.Tail
        }

    /// Bitwise Negation (flip all the bits)
    let bneg (b : Bits) : Bits =
        bmap1UL (~~~) b

    let inline private bmap2UL (fn) (bL : Bits) (bR : Bits) : Bits =
        let m = headBits bL.Head
        if (m <> headBits bR.Head) then invalidArgMatchLen bL bR else
        { Head = ((~~~m) &&& bL.Head) ||| (m &&& (fn bL.Head bR.Head))
        ; Tail = List.map2 fn bL.Tail bR.Tail
        }
    
    /// Bitwise not-equal (XOR)
    let bneq (bL : Bits) (bR : Bits) : Bits =
        bmap2UL (^^^) bL bR

    // F# doesn't have bitwise equality operator, but we can negate XOR
    let private beqUL a b = 
        ~~~(a ^^^ b)

    /// Bitwise Equality (negation of XOR)
    let beq (bL : Bits) (bR : Bits) : Bits =
        bmap2UL beqUL bL bR

    /// Bitwise Minimum (AND)
    let bmin (bL : Bits) (bR : Bits) : Bits =
        bmap2UL (&&&) bL bR

    /// Bitwise Maximum (OR)
    let bmax (bL : Bits) (bR : Bits) : Bits =
        bmap2UL (|||) bL bR

    /// Fold over bits within list, starting from head.
    let rec fold fn (st : 'ST) (b : Bits) : 'ST =
        if isEmpty b then st else
        fold fn (fn st (head b)) (tail b)

    /// Reverse a list of bits.
    let rev (b0 : Bits) : Bits =
        fold (fun b e -> cons e b) empty b0

    /// Fold over a pair of lists of equal length. 
    let rec fold2 fn (st : 'ST) (bL : Bits) (bR : Bits) : 'ST = 
        if (isEmpty bL) && (isEmpty bR) then st else
        if (isEmpty bL) || (isEmpty bR) then invalidArgMatchLen bL bR else
        fold2 fn (fn st (head bL) (head bR)) (tail bL) (tail bR)

    module private FoldBack64 =

        let rec foldBack fn ix n (st : 'ST) : 'ST =
            if (0 = ix) then st else
            let ix' = ix - 1
            let item = (0UL <> (n &&& (1UL <<< ix')))
            foldBack fn ix' n (fn item st)

        let rec foldBack2 fn ix nL nR (st : 'ST) : 'ST =
            if (0 = ix) then st else
            let ix' = ix - 1
            let bit = (1UL <<< ix')
            let itL = (0UL <> (nL &&& bit))
            let itR = (0UL <> (nR &&& bit))
            foldBack2 fn ix' nL nR (fn itL itR st) 

    /// Reverse fold
    let foldBack fn (b : Bits) (st0 : 'ST) : 'ST =
        let fb64 n st = FoldBack64.foldBack fn 64 n st
        let stTail = List.foldBack fb64 (b.Tail) st0
        FoldBack64.foldBack fn (lenHd b.Head) (b.Head) stTail

    /// Reverse fold on two lists.
    let foldBack2 fn (bL : Bits) (bR : Bits) (st0 : 'ST) : 'ST =
        let fill = lenHd bL.Head
        if (fill <> (lenHd bR.Head)) then invalidArgMatchLen bL bR else
        let fb64 nL nR st = FoldBack64.foldBack2 fn 64 nL nR st
        let stTail = List.foldBack2 fb64 (bL.Tail) (bR.Tail) st0
        FoldBack64.foldBack2 fn fill (bL.Head) (bR.Head) stTail

    /// Compose two bit lists end-to-end.
    /// Most efficient for input lengths aligned to 64-bits.
    let append (bL : Bits) (bR : Bits) : Bits =
        if isEmpty bR then bL else
        if isEmpty bL then bR else
        if (1UL = bR.Head) // optimizable
          then { Head = bL.Head; Tail = List.append (bL.Tail) (bR.Tail) }
          else foldBack cons bL bR

    /// Select an item based on indexing into a list.
    let rec item (nth : int) (b : Bits) =
        // naive implementation for now. We could feasibly optimize
        // indexing into the tail of b.
        if isEmpty b then invalidArg (nameof nth) "index out of range" else
        if (0 = nth) then head b else
        item (nth - 1) (tail b)

    let rec tryItem (nth : int) (b : Bits) =
        // naive implementation for now.
        if isEmpty b then None else
        if (0 = nth) then Some (head b) else
        tryItem (nth - 1) (tail b)

    let rec private sharedPrefixLenAccum bL bR n =
        let halt = isEmpty bL || isEmpty bR || (head bL <> head bR)
        if halt then n else sharedPrefixLenAccum (tail bL) (tail bR) (n + 1) 

    /// Find the length of the matching prefix for two bit lists.
    let sharedPrefixLen (bL : Bits) (bR : Bits) : int =
        sharedPrefixLenAccum bL bR 0

    /// Drop count items from the head. 
    let rec skip (count : int) (b : Bits) : Bits =
        // naive implementation for now. I could potentially optimize 
        // skipping several 64-bit chunks in the tail.
        if (0 >= count) then b else
        if isEmpty b then invalidArg (nameof count) "skip more than list length" else
        skip (count - 1) (tail b)

    let rec private takeAccum (count) (b) (acc) =
        // this 'take' is one bit at a time.
        if (count < 1) then rev acc else
        if isEmpty b then invalidArg (nameof count) "taking more than list length" else
        takeAccum (count - 1) (tail b) (cons (head b) acc)

    /// Keep count items from the head.
    let take count b =
        takeAccum count b empty

    let splitAt index b =
        (take index b, skip index b)

    let private seqgen b =
        if isEmpty b then None else Some (head b, tail b)

    let toSeq (b : Bits) : bool seq =
        Seq.unfold seqgen b 

    let toArray =
        toSeq >> Seq.toArray

    let toList =
        toSeq >> Seq.toList

    let ofArray (a : bool[]) : Bits =
        Array.foldBack cons a empty

    let ofList (l : bool list) : Bits =
        List.fold (fun b e -> cons e b) empty l |> rev

    let ofSeq (s : bool seq) : Bits =
        Seq.fold (fun b e -> cons e b) empty s |> rev

    /// Prefix a full byte, such that MSB is head.
    let consByte (byte : uint8) (b : Bits) : Bits =
        let inline cb n acc = cons (0uy <> ((1uy <<< n) &&& byte)) acc
        b |> cb 0 |> cb 1 |> cb 2 |> cb 3
          |> cb 4 |> cb 5 |> cb 6 |> cb 7

    /// Conversion of byte to bits; ordered msb to lsb 
    let inline ofByte (n : uint8) : Bits =
        consByte n empty

    let inline private matchHdLen hdlen n =
        let expect_hibit = (1UL <<< hdlen)
        let mask_hibits = ~~~(expect_hibit - 1UL)
        ((mask_hibits &&& n) = expect_hibit)

    let rec private matchListLen len xs =
        match xs with
        | [] -> (0 = len)
        | (x::xs') -> if (len > 0) then matchListLen (len - 1) xs' else false

    // matchlen is O(min(len, length b)), and requires constant time to
    // match the expected head length.
    let inline private matchLen len b =
        matchHdLen (len % 64) (b.Head) && matchListLen (len / 64) (b.Tail)

    let (|Byte|_|) (b : Bits) : uint8 option =
        if not (matchLen 8 b) then None else
        let addBit n e = (n <<< 1) ||| (if e then 1uy else 0uy)
        fold addBit 0uy b |> Some

    let ofU16 (n : uint16) : Bits =
        let inline cb ix acc = consByte (uint8 (n >>> (8 * ix))) acc
        empty |> cb 0 |> cb 1
    
    let (|U16|_|) (b : Bits) : uint16 option =
        if not (matchLen 16 b) then None else
        let addBit n e = (n <<< 1) ||| (if e then 1us else 0us)
        fold addBit 0us b |> Some

    let ofU32 (n : uint32) : Bits =
        let inline cb ix acc = consByte (uint8 (n >>> (8 * ix))) acc
        empty |> cb 0 |> cb 1 |> cb 2 |> cb 3

    let (|U32|_|) (b : Bits) : uint32 option =
        if not (matchLen 32 b) then None else
        let addBit n e = (n <<< 1) ||| (if e then 1ul else 0ul)
        fold addBit 0ul b |> Some

    let ofU64 (n : uint64) : Bits =
        let inline cb ix acc = consByte (uint8 (n >>> (8 * ix))) acc
        empty |> cb 0 |> cb 1 |> cb 2 |> cb 3 |> cb 4 |> cb 5 |> cb 6 |> cb 7

    let (|U64|_|) (b : Bits) : uint64 option =
        if not (matchLen 64 b) then None else
        let addBit n e = (n <<< 1) ||| (if e then 1UL else 0UL)
        fold addBit 0UL b |> Some

    let rec private _trim_zero_prefix b =
        if (isEmpty b) || (head b) then b else
        _trim_zero_prefix (tail b)

    /// min-width representation of number
    let ofNat64 n =
        _trim_zero_prefix (ofU64 n) 

    /// convert up to 64 bits to a number
    let (|Nat64|_|) b =
        let len_ok = (List.isEmpty b.Tail) || ((1UL = b.Head) && (matchListLen 1 b.Tail))
        if not len_ok then None else
        let addBit n e = (n <<< 1) ||| (if e then 1UL else 0UL)
        fold addBit 0UL b |> Some

    /// lexicographic comparison
    let rec cmp x y =
        if (isEmpty x) && (isEmpty y) then 0 else
        if (isEmpty x) then -1 else
        if (isEmpty y) then 1 else
        let cmp0 = compare (head x) (head y)
        if 0 <> cmp0 then cmp0 else 
        cmp (tail x) (tail y)

    // todo: consider support for dynamic-sized number representations,
    // e.g. excluding the leading zeroes.
    