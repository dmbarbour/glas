# Glas Bootstrap

This implementation of the `glas` command-line tool is written in F#, using dotnet core with minimal external dependencies. This is intended as a bootstrap implementation and experimentation platform for early development of Glas, until Glas is ready to fully self host.

## Build



## Usage




## Implementation Notes

### Lists

Implementing a finger-tree structure in F# is possible, but it's a little awkward to integrate the rope-based chunking.

Alternative: a list is represented by an immutable array of singleton values, binary fragments, and sublist fragments. There is no typeful enforcement of finger-tree structure. Instead, we have heuristics during construction and decomposition.

F# doesn't have immutable arrays, but I could introduce a lightweight wrapper.

There are other possible representations, but I should be aiming to align with whatever Glas Object does. For Glas Object, it is more convenient to avoid an assumption for typeful enforcement of structure, and to treat finger-tree organization as an optimized structure.

### Content-Addressed Storage

Rather than generic stowage model, I'll focus on the Glas Object (glob) data representation for stowage. Further, I won't attempt to support generic stowage providers, just use a global source. It is feasible to later extend stowage for HTTP access.

### Code Generation

The bootstrap will start as an interpreter. However, one of my motives for choosing a .NET language, F#, is access to the CodeDOM. It is feasible to extend the bootstrap to JIT compile Glas programs, improving performance of builds and interpreted apps.
