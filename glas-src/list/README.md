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

The list type `type List a = (a * List a) | ()` describes lists as having elements of homogeneous type, for example a list of integers or a list of programs. However, the list structure can be used for many other sequential types. In these cases, many list functions still apply, but would have different implications for type inference.

### Tuples

        type Tuple [a,b,c] = (a * (b * (c * ())))

A tuple is described by a list of potentially heterogeneous types, and a tuple value looks like a fixed-length list of hetereogeneous values. Building tuples on lists simplifies composition, processing, and rendering. However, tuple types are awkward to extend. In glas systems, records should usually be favored over tuples, even short ones, especially at API boundaries.

### Vectors

        type Vector n a = List a of length n

A vector is a homogeneous list of statically known length. Operations on vectors should have a static effect on the length. Though, we can have operations to convert vectors to list types and the inverse after verifying length. 
