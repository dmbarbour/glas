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
* small rational numbers?
  * 0..30 bit numerator
  * 1..30 bit denominator with implicit '1' bit prefix
  * cannot encode divide-by-zero, but not always normalized

Assuming 8-byte alignment of pointers.

        lowbyte             interpretation
        xxxxx000            pointer
        xxxxxx01            bitstring (0..61 bits)
        xxxxxx10            shrubs of 0,2,4..62 bits (see below)
        xxxxx011            small rationals
        nnn00111            binary (nnn: encodes 1 to 7 bytes)


### Linearity and Ephemerality Header Bits

To efficiently enforce linear types at runtime, we should maintain a linearity bit per allocation. We may similarly benefit from tracking ephemerality for escape analysis, to ensure data 'sealed' by a short-lived register is never stored to a longer-lived register. In the latter case, it seems sufficient to cover the RPC vs. database vs. runtime-instance vs. transaction-local cases, and simply not dynamically detect issues like escape between %local frames within a transaction or runtime.

These bits can be constructed as a simple composition of the same header bits in component data.

### Binaries and Arrays

Might be worth having a couple options here, based on whether we want to allocate in-arena or outside of it. Will need to consider my options. Might heuristically keep smaller binaries or arrays within the arena. Support for slices would be good, too.

### Globs

A viable feature is to support 'globs' where we directly represent a glas data structure as a binary. This doesn't even need a separate header, instead tag small or large binary representations. An advantage is that a glob may be far more compact for complex tree structures. A disadvantage is that we cannot easily 'slice' a glob, we can only index into one, i.e. a complete glob has a 

### Thunks

I'll need lazy evaluation for at least the namespace layer. I eventually also want in the program layer, e.g. as an annotation to run 'pure' functions.

I'll probably want to model explicit thunks for at least some use cases, such as lazy eval of the namespace. I'll likely need two types of thunks: one for program model, another for namespace layer. Due to the distinct program types.

Ideally, GC may recognize and collapse completed thunks. GC could also recognize and complete 'selector' thunks, e.g. accessing a particular definition from an environment, or a particular field from a dict, when it is available.

## Garbage Collection

### Thread Roots

In context of concurrent marking, we ideally can capture thread roots efficiently. With a compacting GC, we might also move them around. 

One option is to simply encode thread roots as an array of sorts. The thread could flexibly use this array as structures and stacks, whatever it needs. The obvious weakness is that big arrays will take a while to scan, and threads are paused during scan.

To mitigate, we could present roots as mutable data within the heap. But this requires too much structure.

A viable alternative is to limit threads to, say, 32 roots. This ensures bounded initial scan work per thread. Threads themselves then decide how (and whether) to use these 'registers'. But this is constraining for no great reason: I'm the only one implementing various kinds of threads!

Let's assume a `glas_thread` is wrapped in a larger structure, which provides a purpose-specific array or structure of registers. The thread points to this structure and specifies its size, and we insist its size is constant - never changes. GC is also responsible for destroying these threads, i.e. no risk of client removing roots while processing. This seems feasible.

### Thunks

Thunks need special attention. Initially, a thunk must contain some representation of a computation. Later, some thread will 'claim' the thunk. Other threads may await the thunk, which requires some careful integration:

- in context of transactions, the operation waiting on a thunk, or even the one performing it, may be aborted before the wait completes. Programmers can mitigate by designing tiny thunks.
- to keep it simple, we can restrict thunks within programs to pure data-stack manipulation, i.e. thunk with arity. 
- threads waiting on a thunk should also get busy! thunks beget thunks, and they can start doing some work without waiting for just one thread to finish everything. But we'll need some way to identify which thunks are needed ahead of request.

