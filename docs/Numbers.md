
# Representation of Numbers

I'm frequently frustrated by how numbers are represented in computers. Programmers shouldn't need to contemplate whether -128 can be negated depends on word size, for example. The assymmetry in range '-128 .. 127' for a signed byte annoys me. Dealing with overflow and underflow errors is quite troublesome. Also, floating point cannot exactly represent common numbers such as 0.1 or 1/3. For encoding probabilities, the asymmetry for denormalized numbers near zero or near 1.0 is awkward.

Fortunately, Glas doesn't need floating point arithmetic, at least not at this layer. 

Desiderata for numbers in Glas:

* exact arithmetic
* lightweight arithmetic
* compact representation
* no overflow/underflow info lost
* similar to conventional encodings
* canonical representations
* includes natural numbers

Natural numbers are required to slice and index lists, return list length, etc.. However, I can feasibly extend the representations to integers or rational numbers. 

Currently, most of my attention is on encoding numbers as bitstrings, e.g. `10110` might encode 22, or perhaps -10.

## Variable Naturals

I observe that any positive integer can be encoded in normal form with an implicit `1` in the most-significant bit.

        type PNat =
            1       (1).   (empty bit string)
            2       (1)0.  
            3       (1)1.
            4       (1)00.
            5       (1)01.
            6       (1)10.
            7       (1)11.
            8       (1)000.
            15      (1)111.
            16      (1)0000.
            31      (1)1111.
            ...

We can trivially combine this with a simple algebraic prefix that distinguishes zero and positive numbers:

        type Nat =  (() + PNat)
            0       0.
            1       1.
            2       10.
            3       11.
            4       100.
            5       101.
            ...

Alternatively, I could encode zero as the empty path, and essentially require that all positive numbers start with `1`. 

This gives a unique encoding to every number. Additionally, if we encounter denormalized numbers such as `00010111` we can easily reduce to `10111`. 

## Variable Two's Complement Integers

For integers in two's complement, we can view the sign bit as repeating infinitely to the left. For variable-width integers, we can simply remove the repetition, leaving only one sign bit.

        non-negative integers:
        0       0.          
        1       01.         1
        2       010.        2
        3       011.        2+1
        4       0100.       4
        5       0101.       4+1
        ...

        negative integers:
        -1      1.          -1
        -2      10.         -2
        -3      101.        -4+1
        -4      100.        -4
        -5      1011.       -8+2+1
        ...

This has the conventional assymmetry, where 127 and -128 both use 8 bits. If we encounter denormalized numbers, we can normalize by rewriting `11 => 1` or `00 => 0` in the number's prefix.

## Structure Preserving Arithmetic

In the above cases, we 'normalize' numbers to eliminate a meaningless prefix. However, this throws away some information - field size - that might be important to the user in some cases. 

An alternative is to develop arithmetic operations that preserve field sizes in some predictable manner. For example, when adding or multiplying two numbers, the result (including carry or high bits) will normally fit into the same fields as the inputs. We could produce a high-bits and low-bits field. When dividing, the remainder would have the same field size as the divisor, while the quotient has the same field size as the dividend.

An advantage of preserving field sizes is that we can decide more flexibly on whether to interpret a field as two's complement integers vs. natural numbers. It's close to how machine-words are used, just with a more flexible choice of field sizes.

I like this approach for its flexibility. OTOH, dealing with signed vs. unsigned interpretations and specialized operators adds more complexity than I'd prefer to manage in Glas. I could resolve this by focusing on the unsigned interpretations only.
