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

### Shrubs

The essential idea for shrubs is to compactly and uniquely encode tree structures into bitstrings.

        stream ops
        (a, S) => (0b0.a, S)        mkL     10
        (a, S) => (0b1.a, S)        mkR     11
        (l, (r, S)) => ((l,r), S)   mkP     01
        S => ((), S)                intro   00

        or read in reverse
        mkP(L,R)   01
        mkL(T)     10 
        mkR(T)     11
        unit       00

To simplify, we can assume S starts with an infinite stack of unit values, and we insist it ends that way. Thus, we can truncate any stream prefix (or constructor suffix) of zeroes. There is no risk of underflow with mkP, but some streams do not encode a valid tree; they encode a stack of trees, instead.

The potential benefit from shrubs is to avoid allocation and GC overheads for fine-grained branching structures up to a modest size limit. Whether this offers a performance benefit remains to be seen. 

### Linearity and Ephemerality Header Bits

To efficiently enforce linear types at runtime, we should maintain a linearity bit per allocation. We may similarly benefit from tracking ephemerality for escape analysis, to ensure data 'sealed' by a short-lived register is never stored to a longer-lived register. In the latter case, it seems sufficient to cover the RPC vs. database vs. runtime-instance vs. transaction-local cases, and simply not dynamically detect issues like escape between %local frames within a transaction or runtime.

These bits can be constructed as a simple composition of the same header bits in component data.

### Binaries and Arrays

Might be worth having a couple options here, based on whether we want to allocate in-arena or outside of it. Will need to consider my options. Might heuristically keep smaller binaries or arrays within the arena. Support for slices would be good, too.

### Thunks

I'll probably want to model explicit thunks for at least some use cases, such as lazy eval of the namespace. I'll likely need two types of thunks: one for program model, another for namespace layer. Due to the distinct program types.

Ideally, GC may recognize and collapse completed thunks. GC could also recognize and complete 'selector' thunks, e.g. accessing a particular definition from an environment, or a particular field from a dict, when it is available.

## Garbage Collection

### Redesign of Memory Layout

Several mmap 'heaps', each of which contain many aligned 'pages'. Each page has a page header whose address is determined by masking any address within the page. 



### Page Allocations

mmap a few gigabytes to start, allocate in ~2MB pages. Each page gets a page header that includes a mark bitmap, a local card table for 'dirty' pages (e.g. old-to-young, perhaps a few more states), and some metadata (e.g. generation number, next and prev page, bump-pointer allocator, page-local free list).

In addition to the card table for 'dirty' pages, we can add one for finalizers, e.g. 1 bit per 512 bytes so we can locate finalizers that weren't marked during GC.

I'll need some associated structures, e.g. a list of finalizers. This list can be encoded as a flat array, or a linked-list of arrays, allocating pages or normal heap space for this special purpose.

*Aside:* Could use 1024-byte cards, or 2048. Trade card scanning costs for smaller card tables. Not sure it's worthwhile, but it doesn't seem bad. Maybe make this configurable via preprocessor, together with page size and heap mmap size.

### Intra-Page References

Two concepts in contention:

* Ideally, pages can track incoming references from older generations with enough precision to avoid global scans of 'dirty' blocks in older generations. Just scan the possibly relevant dirty blocks.
* Ideally, this requires a small amount of state per page.

Idea: Use a bloom filter on the card table addresses. Each page would have a little space allocated to track which 'dirty' cards possibly link back to this page specifically. This would reduce the set of dirty cards that need explicit scanning.

We can fix the bloom filter during the scanning process, updating to a fresh filter based on references during the scans. But it seems difficult to handle concurrent updates to the bloom filter, in this case. Alternatively, only fix during migration to a new page.

Could use 'atomic_fetch_or' to update bloom filters, similar for updating card tables per page.

We could track old-to-old refs, too. With bloom filters, this is greatly mitigated, and it would simplify compaction of old pages. OTOH, we can compact a heuristic selected set of old pages as part of a stop-the-world collection.

### Object Sizes

Most objects will mostly be 32 bytes. I currently intend to allocate larger objects, e.g. binaries and arrays, separately from this mmap heap. Arrays would receive special attention by the collector when marking items.

### Thunks

Thunks need special attention. Initially, a thunk must contain some representation of a computation. Later, some thread will 'claim' the thunk. Other threads may await the thunk, which requires some careful integration:

- in context of transactions, the operation waiting on a thunk, or even the one performing it, may be aborted before the wait completes. Programmers can mitigate by designing tiny thunks.
- to keep it simple, we can restrict thunks within programs to pure data-stack manipulation, i.e. thunk with arity. 
- threads waiting on a thunk should also get busy! thunks beget thunks, and they can start doing some work without waiting for just one thread to finish everything. But we'll need some way to identify which thunks are needed ahead of request.

### Soft Refs ?

I'm also interested in something like content-addressed storage having a 'thunk' that gets executed when the result is needed and discarded when unnecessary. I don't want explicit soft refs in the language, but we could support an explicit cache layer of some form?


### Initial Algorithm

We can start with a concurrent mark+sweep collector, with tricolor markings.

This is good enough to get working on other things. 

But I hope to eventually integrate some ideas from G1GC. I especially want a nursery, compaction, and partial collections. (Even better would be a per-thread nursery.)

I only need a write barrier for registers and thunks. How to implement remembered sets? Each register may need some metadata, or I could use a card table.

S

Eventually, I may want a card table to track 'dirty' pages where old objects refer to young objects. 









### Small Reference Count?

I like the idea of reference counting for tree-structured data. But I cannot actually get in-place update effectively in context of transactions and checkpoints, except for newly allocated values (which I can do regardless).




Hybrid GC approach with a small reference count per value. One reason for this is to support in-place update when a value is not shared.

I have the idea of maintaining a 'small' reference count per value, perhaps only capable of counting from 1 to 63 or so. Most values would only have a few references, and this would allow immediate recovery of memory in most cases. Only widely shared data would be subject to GC, and even then we'd still be able to remove most components via reference counts.

If we use a nursery allocator, we might only perform reference counts on objects that make it out of the nursery, with GC itself building the initial reference count.
## Context

I could pass a first-class glas context around, or I could just use a global context. I suspect the global context would make for a more comfortable API in C, 


/**
 * GC design notes:
 * 
 * Keep it simple, at least at the start. I can build a mark+sweep
 * collector, no concurrent GC for now. Later, perhaps, upgrade to
 * 
 * 
 * When the object reaches 256, we set refct to 0 but push the object
 * into a sticky list (buffer-backed) of objects for the GC to track.
 * This adds about one pointer overhead for every sticky object, but
 * those should be relatively rare.
 * 
 * When we perform a full mark and sweep, we can clear objects from
 * the sticky list, and still propagate decrefs and such to their 
 * children normally, and apply finalizers when those are targeted.
 * The sticky list doubles as our finalizer table, in this sense.
 * 
 * The sticky list can be compacted upon sweep, moving data from 
 * end of list to the opened slots. Alternatively, we can track
 * a simple linked list of open slots.
 * 
 * Can use tricolor marking. No need to mark non-sticky objects in
 * the sense of actually updating the gcbits.
 * 
 * This is a non-compacting GC. Perhaps in the future, I can try to
 * support compacting GC. Adapting G1GC or ZGC could be nice.
 * 
 * A relevant consideration is how reference counts interact with loops.
 * Most glas data operations cannot introduce loops, but loops can be
 * introduced by:
 * 
 * - namespace fixpoint
 * - first-class registers
 * - thunks? uncertain, likely not
 * 
 * To mitigate this, some objects might be moved to the sticky region 
 * immediately, especially namespace environments and registers.
 * 
 * In any case, I should start simple and build from there.
 */
