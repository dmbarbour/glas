# Basic Arithmetic

## Integer Representation

In Glas, integers are normally represented by variable-length bitstrings, msb to lsb, with negative integers using negated bits (aka one's complement). Every bitstring uniquely represents an integer.

         5      0b101
         4      0b100
         3      0b11
         2      0b10
         1      0b1
         0      0b
        -1      0b0
        -2      0b01
        -3      0b00
        -4      0b011
        -5      0b010

This representation has some nice properties such as symmetry and having no upper bound on numbers. Of course, performance will take a hit when operating on larger integers.

## Performance

At the moment, the implementation of these functions doesn't take much advantage of CPU built-in operations. Essentially, it's bitwise primary school algorithms.

essentially primary school algorithms. I might end up marking several of these functions for optional acceleration, but I'd prefer to avoid relying on acceleration.

## Arithmetic by Bitstring Manipulations

### Increment/Decrement

We can increment an integer by rewriting the low '1' bits to '0' bits, then handling the upper bits specially: 

    0b => 0b1       (0 to 1)
    0b0 => 0b       (-1 to 0)
    0bxx0 => 0bxx1  (e.g. 2 to 3, or -3 to -2)

This can be viewed as a simplified add-with-carry that happens to work with negative numbers. It's easiest to operate on low bits if we first reverse the bitstring. Decrement can be implemented by inverting all the 0 and 1 bits. 

*Note:* Increment and decrement are currently implemented in the 'bits' module and re-exported here to avoid a dependency cycle for implementation of functions such as bitstring split and length.

### Add

It's easiest to think about adding two positive numbers. This can be achieved using the normal add-carry algorithm on individual bits, lsb to msb. Adding a negative number can become subtraction, though I'll need to properly handle negative outcomes. And subtracting from a negative number can be implemented via addition again.

### Subtract

I need to figure out the fine rules for this. 

        0b111 - 0b110 => 0b1
        0b110 - 0b110 => 0b
        0b101 - 0b110 => 0b0
        0b100 - 0b110 => 0b01

The transition to negatives is a tad awkward to express.


whether I can use simple subtract with carry in this context, where subtracting a final '1' via carry becomes a 0b0 prefix.  


### Multiply

We can multiply two positive numbers by adding with shifting. This is perhaps the most straightforward reasonably efficient version. It does benefit from preparing the shifts and add-carry onto those. Negative numbers can be handled by negating the number and the result.

### Divmod

Uh oh. I can't even remember how to do elementary school long division.

