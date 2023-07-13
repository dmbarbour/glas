# Posits

The [posit (aka type III unum)](https://en.wikipedia.org/wiki/Unum_%28number_format%29#Unum_III) is a new format for floating point arithmetic, which is much better behaved than IEEE floating point in certain respects, albeit with much less hardware support at the moment. 

## Posit Types

Posit representations are essentially typed by two numbers: their bit length (n) and their maximum exponent size (es). I'll represent this type-level metadata as `posit:(n:Nat, es:Nat)`. Some typical values for these:

        posit:(n:8,  es:0)
        posit:(n:16, es:1)
        posit:(n:32, es:2)
        posit:(n:64, es:3)

Operations provided by this module may be parameterized by the `posit:(...)` descriptor, perhaps as a static parameter. However, I haven't entirely decided how I'm going to approach this, e.g. it might be preferable to implement posits within an accelerated virtual machine instead.








