# Glas Runtime

I hope to bootstrap the runtime swiftly! Unfortunately, I doubt I'll accomplish this. Thus, the pre-bootstrap implementation must perform adequately, and should be usable long term. A viable solution is to leverage JIT compilation. With this in mind, JVM and .NET are tempting targets. Alternatively, a low-level runtime like C is viable, write my own JIT and GC. At present, I lean towards the latter.

A basic GC is not difficult to write. I can implement something simple to get started then return to it later. As for JIT, even a simple one will certainly be a pain to write. But I can still start with an interpreter. So, I propose to start with a C implementation and work from there.

## Data Representation

At least for the initial bootstrap implementation, I propose to use fixed-size allocations to simplify GC. We can use 32-byte cells. It might be more optimal to support flexible sizes, eventually, e.g. 16 byte pairs and (stem, data) or (header, data) elements could be in a separate page from 32-byte cells. But that's a problem for after bootstrap.

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
        xxxpp000            pointer
        xxxxxx01            bitstring (0..61 bits)
        xxxxxx10            shrubs of 0,2,4..62 bits (see below)
        xxxxx011            small rationals
        nnn00111            binary (nnn: encodes 1 to 7 bytes)
        11111111            abstract runtime constants, e.g. claimed thunks


### Linearity and Ephemerality Header Bits

To efficiently enforce linear types at runtime, we should maintain a linearity bit per allocation. We may similarly benefit from tracking ephemerality for escape analysis, to ensure data 'sealed' by a short-lived register is never stored to a longer-lived register. In the latter case, it seems sufficient to cover the RPC vs. database vs. runtime-instance vs. transaction-local cases, and simply not dynamically detect issues like escape between %local frames within a transaction or runtime.

These bits can be constructed as a simple composition of the same header bits in component data.

### Binaries and Arrays

Might be worth having a couple options here, based on whether we want to allocate in-arena or outside of it. Will need to consider my options. Might heuristically keep smaller binaries or arrays within the arena. Support for slices would be good, too.

### Shrubs

We can encode small trees as a bitstring:

        00  - leaf
        01  - branch (left tree) (right tree)
        10  - left stem (tree)
        11  - right stem (tree)

We can pad with '00'.

The advantage of shrubs is the ability to encode a complex branching structure without pointers. The disadvantage is the lack of indexing, i.e. 'unpair' takes time linear in the size of the left tree. Despite this limitation, I think it would be very useful for in-pointer encodings. For larger structures, we could use a glob encoding for similar benefits.

### Globs

A viable feature is to support 'globs' where we directly represent a glas data structure as a binary. This doesn't even need a separate header, instead tag small or large binary representations. An advantage is that a glob may be far more compact for complex tree structures. A disadvantage is that we cannot easily 'slice' a glob, we can only index into one, i.e. a complete glob has a 

### Thunks

I'll need lazy evaluation for at least the namespace layer. I eventually also want in the program layer, e.g. as an annotation to run 'pure' functions.

I'll probably want to model explicit thunks for at least some use cases, such as lazy eval of the namespace. I'll likely need two types of thunks: one for program model, another for namespace layer. Due to the distinct program types.

Ideally, GC may recognize and collapse completed thunks. GC could also recognize and complete 'selector' thunks, e.g. accessing a particular definition from an environment, or a particular field from a dict, when it is available.

Waiting on thunks needs attention. In context of transactions, especially, a wait may be interrupted because we decide to backtrack. This requires some robust way to represent waits that can be canceled. Tying back directly to the host is probably a bad idea. An intermediate heap object that we can GC might work, at the cost of adding an allocation for every thunk wait.

## Note on callbacks

Use `pthread_cleanup` to properly handle when a callback closes an OS thread, especially for callbacks into user code.

## TBD

Try to get raylib and GUI FFIs working ASAP.

## Transactional Registers

It isn't difficult to ensure updates across multiple registers are consistent via mutex for writes. But ensuring isolated reads, and thus ensuring a read-only transaction never aborts, is a bit more difficult. What are our options?

One idea is horizontal versions. 
- A version has associated, sealed data for each register updated 
- A register has a strong reference to the most recent version
- A version may have a weak-ref to previous version per register

That is, so long as a register survives, its most recent version of data also survives. So long as a version survives, we can also access data from that version across all surviving registers. The cost is a fair bit of indirection. 

How does this help? We could test for consistency when first reading a register, i.e. check that there are no missed updates to vars previously read within the same transaction. This makes register reads relatively expensive, i.e. upon a transaction bringing a version into scope, it may invalidate prior reads to other registers, or we can opportunistically read an older version where there is no conflict (if it hasn't been garbage-collected yet).

This makes reads more expensive, but it isn't too bad.

We could optionally remove the data directly, instead a version just tracks which registers were updated, and perhaps the nature of those updates. This would prevent us from switching to older versions of data to allow a transaction to complete, but it would reduce indirection and simplify GC.





A "version" captures updates to multiple registers, and the most recent version for each register is strongly referenced, but the version itself only has weakrefs back to the registers.  

One idea is to 

Atomic reads also need some attention: ideally, we can operate on a snapshot view of all
registers, i.e. such that we never observe inconsistent states and abort read-only transactions. Perhaps we could capture version numbers within each register instead of data. 






## Garbage Collection

### Allocators and Thread Local Storage

I want nursery-style allocations, but I don't want a lot of fine-grained nurseries for `glas*` objects. A viable approach is to use thread-local storage for 'affinity' when allocating, i.e. where the `glas*` thread allocates depends on which OS thread it's using. 

By keeping a linked list of thread-local structures, when GC is performed, it can fetch back all the nurseries and force threads to grab new ones on their next allocation.

Aside from allocators, TLS may prove convenient for write barriers, e.g. keeping a `glas_gc_scan*` per OS thread to avoid contention on a global scan list. Maybe move the semaphore here, too.

### Finalizers

It is necessary to precisely recognize finalizers in the heap. However, I cannot afford a linked list, nor even an additional mark bit per object, so I'm thinking to use cards to track finalizers to a small region (e.g. 128 bytes, a quarter-bit per object), then rely on gcbits to mark unexecuted finalizers more precisely.



### Snapshot at the Beginning?

Some thoughts on adapting SATB to glas.

GHC's GC uses a 'snapshot at the beginning' (SATB) strategy for garbage collection. The idea, IIUC, is that a collection should only sweep garbage that was present (i.e. was already garbage) when the collection started. This ensures the collection is consistent with performing a full GC under stop-the-world. 

The trick is to extend to concurrent marking and mutation. New allocations obviously create new garbage. But we must touch none of that until a future GC. Mutations can transform older allocations into new garbage. But we must GC as if no mutation occurred.

This will be supported by a write barrier. When the write barrier sees a mutation on a slot, it must take action depending on whether it's the first mutation on that slot in the GC cycle. If so, copy prior value, atomically claim the scan, write new value, then if the claim was successful, add prior value to a scan buffer. This is our basic barrier for SATB. Otherwise, just update.

But now we have a new problem: How does the write barrier know whether a specific slot has been scanned? Some options:

* hashtable - unpredictable size, poor locality, easy to understand
* bitmaps - predictable size, good locality, easy to understand, but 1.6% memory overhead
* other ideas?

I lean towards bitmaps in this role. We'll need 1 bit per 8-byte slot (1.6% overhead, unless we can roll it into currently wasted space) for tracking scans. For a `glas_cell` we have a 32-bit header per 32-byte object, of which only 24 bits are currently assigned. I can dedicate 3 bits to support 3 slots per cell. For a `glas_thread` we could add an `_Atomic(uint64_t)* scan_bitmap` to every thread, allocating based on a thread's root offsets. If we allocate arrays, we could allocate extra space for scan bits walking backwards from the array data.

We flip every GC cycle whether we interpret '1' as scanned or '0' as scanned. At the start of marking, all 'live' slots must begin with the same mark. Thus, slots for new allocations must also be marked as scanned. This is doable. For new cells, we can add bits from runtime-global state to our initial header, flipping this interpretation only while stopped. For new threads, we might need to block the runtime from flipping scan bits while threads are initializing, but a simple counter and semaphore is adequate.

With this design, we'll need a separate 'write' function for registers, threads, and mutable arrays. These write functions can handle the write barrier, whether it's by GC flipping a `void (*write_reg)(glas_cell* reg, glas_cell* newval)` while GC is stopped, or branching on GC state. (I have no intuition for which option would perform better.)

Some notes:

* I still favor bump-pointer allocation, but I'm not married to it. This design doesn't rely on location to distinguish cells as 'new', thus I can also use fragmented allocation, e.g. using the prior marks. Mixed allocation modes are feasible.
* New registers, thunks, etc. are initialized as 'scanned', thus don't need special handling within a GC cycle.
* When writing to a slot that initially contains NULL - or perhaps small constant data embedded into pointer bitfields - pushing the prior value to a scan buffer becomes dropping the prior value.

### Compacting old gen?

I'd like to occasionally compact a subset of old pages, e.g. to free up some heap address space. But at the moment, I don't have a good solution for moving data from old pages without breaking old-to-old links. 

One viable option is to track old-to-old references in cards, or perhaps to build up a special card for ref-to-compacting when performing marks. Or perhaps, while marking, we could record the 


        // Problem: I want to compact a subset of old pages, but I cannot 
        // find old-to-old references. One option is intra-gen cards, but
        // that adds overhead to a lot of allocations (whereas old-to-young
        // only impacts mutations, )


### Arrays

We can model big arrays, mutable or otherwise, as a foreign pointer to some `glas_roots*`. But use of roots in this role has a 25% overhead for the root offsets and may touch unreachable slices. 

We can simplify by focusing on immutable arrays, which don't need old-to-young tracking. Later, if I need mutable arrays, I can develop a dedicated structure.

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

