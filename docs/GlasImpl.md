# Glas Runtime

I hope to bootstrap the runtime swiftly! Unfortunately, I doubt I'll accomplish this. Thus, the pre-bootstrap implementation must perform adequately, and should be usable long term. A viable solution is to leverage JIT compilation. With this in mind, JVM and .NET are tempting targets. Alternatively, a low-level runtime like C is viable, write my own JIT and GC. At present, I lean towards the latter.

A basic GC is not difficult to write. I can implement something simple to get started then return to it later. As for JIT, even a simple one will certainly be a pain to write. But I can still start with an interpreter. So, I propose to start with a C implementation and work from there.

## Data Representation

It is feasible to allocate 'cons cells' the size of two pointers, like Scheme or Lisp, supporting branches and such. This would require uncomfortably squeezing a lot of data and GC logic into tagged pointers. Alternatively, we can support a more conventional tagged union, perhaps aligned to three (or four) pointers in size, providing plenty room for metadata per value. I'm inclined to the latter. 

### Small Values

We can squeeze many small values into an 8-byte pointer. 

* small bitstrings and leaf nodes
* small binaries (up to 7 bytes)
* small trees or shrubs?
* small rationals 
  * 0..30 bits num
  * 1..30 bits denom (implicit '1' bit prefix)

Assuming 8-byte alignment of pointers.

        lowbyte             interpretation
        xxxxx000            pointer
        xxxxxx01            bitstring (0..61 bits)
        xxxxxx10            small tree (see below)?
        xxx00011            binary (xxx: encodes 1 to 7 bytes)
        xxxxx111            small rational numbers

### Shrubs

The essential idea for shrubs is to compactly encode tree structures into bitstrings. This would reduce the space and GC overheads for structured values in the leaf nodes, albeit at the cost of increased CPU costs to handle them. This is possibly worthwhile, but some performance testing is needed.

I could try to encode 'small trees' in general using an encoding such as:

        00 - leaf
        01 - pair (fby left fby right)
        10 - left stem 
        11 - right stem

This would let me encode all trees up to about 30 bits instead of ratios. If I represent rationals as a `(N,D)` pair with non-zero D, I'd still get a lot of ratios within a pointer. Or I could use `111` for ratios.

There is still plenty of unused space, but the above seems to cover all the low-hanging fruit.

We could use 


### Small Reference Count

Hybrid GC approach with a small reference count per value. One reason for this is to support in-place update when a value is not shared.

I have the idea of maintaining a 'small' reference count per value, perhaps only capable of counting from 1 to 63 or so. Most values would only have a few references, and this would allow immediate recovery of memory in most cases. Only widely shared data would be subject to GC, and even then we'd still be able to remove most components via reference counts.

If we use a nursery allocator, we might only perform reference counts on objects that make it out of the nursery, with GC itself building the initial reference count.

### Linearity and Ephemerality Header Bits

To efficiently enforce linear types at runtime, we should maintain a linearity bit per allocation. We may similarly benefit from tracking ephemerality for escape analysis, to ensure data 'sealed' by a short-lived register is never stored to a longer-lived register. In the latter case, it seems sufficient to cover the RPC vs. database vs. runtime-instance vs. transaction-local cases, and simply not dynamically detect issues like escape between %local frames within a transaction or runtime.

These bits can be constructed as a simple composition of the same header bits in component data.

### Arenas

I propose to allocate large blocks of memory, whether by malloc or mmap or whatever, then use my own allocators within them. One motive here is to mitigate per-allocation overheads, but it may also simplify GC in many cases, e.g. bump-pointer nurseries that won't conflict with FFI allocations.

### Binaries and Arrays

Might be worth having a couple options here, based on whether we want to allocate in-arena or outside of it. Will need to consider my options. Might heuristically keep smaller binaries or arrays within the arena. Support for slices would be good, too.

### Thunks

I'll probably want to model explicit thunks for at least some use cases, such as lazy eval of the namespace. GC may recognize and collapse the completed thunk. Each thunk may need a linked list of clients waiting on it, the value or computation, and some status. 

## Context

I could pass a first-class glas context around, or I could just use a global context. I suspect the global context would make for a more comfortable API in C, 

