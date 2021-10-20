# Representation of Numbers

I've proposed a convention that natural numbers in Glas are represented using a min-width base-2 encoding, msb to lsb, such as `10111` encodes 23, and the unit value is equivalent to 0. 

This could be readily extended with negative integers by negating either the first bit or all bits. In either case, we neatly avoid the issue of encoding a negative 0, which has no bits. 

Rational numbers can be supported explicitly by pairing two numbers. Additionally, it is not difficult to support floating-point or scientific numbers that pair significand and exponent. 

I'd like to extend numbers with units. However, I'm uncertain whether to express this directly in the number type or keep it as an associative type in the type system. 

