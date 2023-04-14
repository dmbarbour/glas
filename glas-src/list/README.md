# Lists

Lists in glas systems are logically constructed from tree branch nodes (pairs) extending to the right, terminating in a leaf node (unit), with elements as the left branch of each pair.

        type List a = (a * List a) | ()

                /\      list of four items
               a /\        [a,b,c,d]
                b /\
                 c /\ 
                  d  ()

Direct representation of lists only provides efficient access to the first few elements. This is inadequate for many use cases of sequential data. To mitigate this, glas systems will represent lists under-the-hood using arrays, binaries, and [finger-tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). These representations are accessible via accelerated list functions.

## Related Types

The list representation is also used for tuples and vectors. 

### Tuples

A tuple is a fixed-sized list of heterogeneous types.

        type Tuple [a,b,c] = (a * (b * (c * ())))

However, glas systems usually favor records over tuples. Records are much more extensible and self-documenting, and are still reasonably efficient.

### Vectors

A vector is a fixed-sized list of homogeneous type.

        type Vector n a = List a of length n

Vectors are very useful in certain maths. 

