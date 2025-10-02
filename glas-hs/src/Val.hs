module Val (
    Val, zero,

    -- Basic Construction


    -- TODO: 
    -- ofInt,
    --mkL, mkR, mkP,
    
    is_bits, has_bits, bits_len, bits_invert,
    bit_cons, bits_head, bits_tail,


) where

import Data.Word
import Data.Bits
import qualified Data.Vector.Strict as VS
import qualified Data.ByteString as BS

-- Stem encodes 0..63 bits
--   1000..0        0 bits
--   a100..0        1 bit
--   ab10..0        2 bits
--   abcd..1        63 bits
--   0000..0        unused
type Stem = Word64

-- | the basic glas data type (no external refs)
--   Note: this data is strict; no laziness in glas data.
data Val = V !Stem !Node
data Node
    = Leaf
    | Stem64 !Word64 !Node
    | Branch !Val !Val
    -- optimized lists
    | Arr !(VS.Vector Val)
    | Bin !BS.ByteString
    | Concat !Node !Node
    | Take !Int !Node

empty_stem :: Stem
empty_stem = (1 `shiftL` 63)

stem_is_full :: Stem -> Bool
stem_is_full s = ((s .&. 1) == 1)

stem_is_empty :: Stem -> Bool
stem_is_empty s = (s == empty_stem)

stem_lenbit :: Int -> Stem
stem_lenbit n = 1 `shiftL` (63 - n)

-- invert all bits in stem
invert_stem :: Stem -> Stem
invert_stem s =
    let mask = (stem_lenbit (stem_len s)) - 1 in
    ((complement s) .&. (complement mask)) .|. (s .&. mask)


stem_len2 :: Stem -> Int -> Int
stem_len2 s n = 
    if (0 == (0x1 .&. s)) then 
        n 
    else 
        (n + 1)

stem_len4 :: Stem -> Int -> Int
stem_len4 s n =
    if (0 == (0x3 .&. s)) then
        stem_len2 (s `shiftR` 2) n
    else
        stem_len2 s (n + 2)

stem_len8 :: Stem -> Int -> Int
stem_len8 s n =
    if (0 == (0xF .&. s)) then
        stem_len4 (s `shiftR` 4) n
    else
        stem_len4 s (n + 4)

stem_len16 :: Stem -> Int -> Int
stem_len16 s n =
    if (0 == (0xFF .&. s)) then
        stem_len8 (s `shiftR` 8) n
    else
        stem_len8 s (n + 8)

stem_len32 :: Stem -> Int -> Int
stem_len32 s n =
    if (0 == (0xFFFF .&. s)) then
        stem_len16 (s `shiftR` 16) n
    else
        stem_len16 s (n + 16)

stem_len :: Stem -> Int
stem_len s =
    if (0 == (0xFFFFFFFF .&. s)) then
        stem_len32 (s `shiftR` 32) 0
    else
        stem_len32 s 32

-- | the zero value, also puns as empty list, empty dict
zero :: Val
zero = V empty_stem Leaf

-- | test whether Val is simply a bitstring
is_bits :: Val -> Bool
is_bits (V _ n) = is_bits_node n

is_bits_node :: Node -> Bool
is_bits_node n = case n of
    Leaf -> True
    Stem64 _ n' -> is_bits_node n'
    _ -> False

-- | test whether Val has at least one stem bit (bits_len > 0)
has_bits :: Val -> Bool
has_bits (V s n) = if stem_is_empty s then has_bits_node n else True 

has_bits_node :: Node -> Bool
has_bits_node n = case n of
    Stem64 _ _ -> True
    _ -> False

-- | return number of continuous bits before a leaf or branch
bits_len :: Val -> Int
bits_len (V s n) = bits_len_node (stem_len s) n

bits_len_node :: Int -> Node -> Int
bits_len_node ct n = case n of
    Stem64 _ n' -> bits_len_node (64 + ct) n'
    _ -> ct

-- | invert all bits indicated in bits_len
bits_invert :: Val -> Val
bits_invert (V s n) = V (invert_stem s) (bits_invert_node [] n)

bits_invert_node :: [Word64] -> Node -> Node
bits_invert_node l n = case n of
    Stem64 s n' -> bits_invert_node (s:l) n'
    _ -> foldl' (\t s -> Stem64 (complement s) t) n l

bit_cons :: Bool -> Val -> Val
bit_cons b (V s n) =
    let s' = (s `shiftR` 1) .|. (if b then (1 `shiftL` 63) else 0) in
    if stem_is_full s then 
        V empty_stem (Stem64 s' n)
    else
        V s' n

stem_head :: Stem -> Bool
stem_head s = ((1 `shiftL` 63) .&. s) /= 0

bits_head :: Val -> Bool
bits_head (V s n) = 
    if stem_is_empty s then
        case n of
            Stem64 s' _ -> stem_head s' 
            _ -> error "bit_head: no bits"
    else 
        stem_head s

bits_tail :: Val -> Val
bits_tail (V s n) =
    if stem_is_empty s then
        case n of
            Stem64 s' n' ->
                V ((s' `shiftL` 1) .|. 1) n'
            _ -> error "bit_tail: no bits"
    else
        V (s `shiftL` 1) n
