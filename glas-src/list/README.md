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

A tuple is described by a list of potentially heterogeneous types. A value of the tuple type consists of a static length list of values of heterogeneous types.

To simplify type inference, we might avoid dynamic loops. For example, to access the Nth element we might first use a macro to produce a unary label of form `0b(1*)0`. Appending two tuples would depend on knowing the lengths of one tuple. Alternatively, it is feasible to operate on tuples using list operations wrapped with dependent type annotations.

### Vectors

        type Vector n a = List a of length n

A vector is essentially a list of statically known length. All operations on vectors should have a static effect on the length. Though, we can have operations to convert vectors to list types and the inverse after verifying length. In some ways, it might be easier to model vectors as a refinement on tuples.

