module Prog (
    P(..)
) where

data P 
    = Pass
    | Fail
    | Do P P

