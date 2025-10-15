
#define _DEFAULT_SOURCE

#include <stdint.h>
#include <stdalign.h>
#include <stdatomic.h>
#include <stdlib.h>
#include <string.h>
#include <pthread.h>
#include <semaphore.h>
#include <time.h>
#include <assert.h>
#include <errno.h>

#include <sys/mman.h>

#include <glas.h>

#if defined _WIN32 || defined __CYGWIN__
  #ifdef __GNUC__
    #define API __attribute__((dllexport))
  #else
    #define API __declspec(dllexport)
  #endif
  #define LOCAL static
#else /* Linux, macOS */
  #define API __attribute__((visibility("default")))
  #define LOCAL static
#endif

#ifdef DEBUG
  #include <stdio.h>
  #define debug(fmt, ...) do { fprintf(stderr, "%s:%s(%u): " fmt "\n", __FILE__, __PRETTY_FUNCTION__, __LINE__,  ##__VA_ARGS__); } while(0)
#else
  #define debug(fmt, ...) 
#endif

#pragma GCC diagnostic ignored "-Wunused-function"

#define likely(x)    __builtin_expect(!!(x), 1)
#define unlikely(x)  __builtin_expect(!!(x), 0)

static_assert((sizeof(void*) == 8), 
    "glas runtime assumes 64-bit pointers");
static_assert((sizeof(void(*)(void*, bool)) == 8),
    "glas runtime assumes 64-bit function pointers");
static_assert((sizeof(_Atomic(void*)) == 8) && (ATOMIC_POINTER_LOCK_FREE == 2),
    "glas runtime assumes lock-free atomic pointers");
static_assert((sizeof(_Atomic(uint64_t)) == 8),
    "glas runtime assumes lock-free atomic 64-bit integers");
static_assert((sizeof(_Atomic(uint8_t)) == 1) && (ATOMIC_CHAR_LOCK_FREE == 2),
    "glas runtime assumes lock-free atomic bytes");

#define GLAS_HEAP_PAGE_SIZE_LG2 16
#define GLAS_HEAP_CARD_SIZE_LG2  9
#define GLAS_HEAP_PAGE_SIZE (1 << GLAS_HEAP_PAGE_SIZE_LG2)
#define GLAS_HEAP_CARD_SIZE (1 << GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_HEAP_MMAP_SIZE (GLAS_HEAP_PAGE_SIZE << 6)
#define GLAS_PAGE_CARD_COUNT (GLAS_HEAP_PAGE_SIZE >> GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_CELL_SIZE 32
#define GLAS_PAGE_CELL_COUNT (GLAS_HEAP_PAGE_SIZE / GLAS_CELL_SIZE)
#define GLAS_GC_MAX_GEN 3

static_assert((0 == (0x2F & GLAS_PAGE_CELL_COUNT)), 
    "glas runtime assumes cells align to 64-bit bitmaps");
static_assert((0 == (0x2F & GLAS_PAGE_CARD_COUNT)),
    "glas runtime assumes cards align to a 64-bit bitmaps");

typedef struct glas_heap glas_heap; // mmap location    
typedef struct glas_page glas_page; // aligned region
typedef struct glas_cell glas_cell;
typedef struct glas_thread glas_thread;

// note: a glas* may wrap or extend glas_thread with API-specific stuff.

typedef struct glas_gc_scan glas_gc_scan;
typedef struct glas_gc_thread glas_gc_thread; 

/** 
 * Checking my assumptions for these builtins.
 * Interaction between stdint and legacy APIs is so awkward.
 */
static inline size_t popcount64(uint64_t n) {
    static_assert(sizeof(unsigned long long) == sizeof(uint64_t));
    return (size_t) __builtin_popcountll(n);
}
static inline size_t popcount32(uint32_t n) {
    static_assert(sizeof(unsigned int) == sizeof(uint32_t));
    return (size_t) __builtin_popcount(n);
}
static inline size_t ctz64(uint64_t n) {
    static_assert(sizeof(unsigned long long) == sizeof(uint64_t));
    return (size_t) __builtin_ctzll(n);
}
static inline size_t ctz32(uint32_t n) {
    static_assert(sizeof(unsigned int) == sizeof(uint32_t));
    return (size_t) __builtin_ctz(n);
}

LOCAL inline void* glas_mem_page_floor(void* addr) {
    return (void*)((uintptr_t)addr & ~(GLAS_HEAP_PAGE_SIZE - 1));
}
LOCAL inline void* glas_mem_page_ceil(void* addr) {
    return glas_mem_page_floor((void*)((uintptr_t)addr + (GLAS_HEAP_PAGE_SIZE - 1)));
}
LOCAL inline void* glas_mem_card_floor(void* addr) {
    return (void*)((uintptr_t)addr & ~(GLAS_HEAP_CARD_SIZE - 1));
}

/**
 * Memory allocations.
 * 
 * Overview:
 * - We maintain a linked list of small heaps, a few megabytes each
 *   - this reserves an address space, may only be partially used
 *   - a heap supports 63 or 64 pages, depending on mmap alignment
 * - Pages are aligned allocations; we can bitmask to a page header
 * - Each page header has bitmaps and heuristic info for local GC
 * - Page headers have doubly linked lists to other pages by GC gen
 * - Pages contain fixed-size 'cells' for basic glas data structures
 * - Large arrays and binaries are allocated in the normal C heap
 */
struct glas_heap {
    glas_heap* next;
    void* mem_start;
    _Atomic(uint64_t) page_bitmap;
};
LOCAL inline void* glas_heap_pages_start(glas_heap* heap) {
    return glas_mem_page_ceil(heap->mem_start);
}
LOCAL inline bool glas_heap_includes_addr(glas_heap* heap, void* addr) {
    return (addr >= heap->mem_start) &&
            ((void*)((uintptr_t)heap->mem_start + GLAS_HEAP_MMAP_SIZE) > addr);
}
LOCAL inline uint64_t glas_heap_initial_bitmap(glas_heap* heap) {
    bool const is_aligned = (glas_heap_pages_start(heap) == (heap->mem_start));
    return is_aligned ? 0 : (((uint64_t)1)<<63);
}
LOCAL inline bool glas_heap_is_empty(glas_heap* heap) {
    uint64_t const bitmap = atomic_load_explicit(&(heap->page_bitmap), memory_order_relaxed);
    return (bitmap == glas_heap_initial_bitmap(heap));
}
LOCAL inline bool glas_heap_is_full(glas_heap* heap) {
    uint64_t const bitmap = atomic_load_explicit(&(heap->page_bitmap), memory_order_relaxed);
    return (0 == ~bitmap);
}
LOCAL glas_heap* glas_heap_try_create() {
    glas_heap* heap = malloc(sizeof(glas_heap));
    if(unlikely(NULL == heap)) { return NULL; }
    heap->next = NULL;
    heap->mem_start = mmap(NULL, GLAS_HEAP_MMAP_SIZE, PROT_NONE, 
        MAP_ANONYMOUS | MAP_PRIVATE | MAP_NORESERVE,
        -1, 0);
    if(unlikely(MAP_FAILED == heap->mem_start)) {
        int const err = errno;
        debug("mmap failed to reserve memory for glas heap, error %d: %s", err, strerror(err));
        free(heap);
        return NULL;
    }
    atomic_init(&(heap->page_bitmap), glas_heap_initial_bitmap(heap));
}
LOCAL void glas_heap_destroy(glas_heap* heap) {
    assert(glas_heap_is_empty(heap));
    if(0 != munmap(heap->mem_start, GLAS_HEAP_MMAP_SIZE)) {
        int const err = errno;
        debug("munmap failed, error %d: %s", err, strerror(err));
        // address-space leak, but not a halting error
    }
    free(heap);
}
LOCAL void* glas_heap_try_alloc_page(glas_heap* heap) {
    _Atomic(uint64_t)* const pb = &(heap->page_bitmap);
    uint64_t bitmap = atomic_load_explicit(pb, memory_order_relaxed);
    while(0 != ~bitmap) {
        size_t const ix = ctz64(~bitmap);
        uint64_t const bit = ((uint64_t)1) << ix;
        bitmap = atomic_fetch_or_explicit(pb, bit, memory_order_acquire);
        if(likely(0 == (bitmap & bit))) { // i.e. if not marked previously
            // we won the race, return the page, but first mark it read-writable
            void* const page = (void*)(((uintptr_t)glas_heap_pages_start(heap)) + (ix * GLAS_HEAP_PAGE_SIZE));
            if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_READ | PROT_WRITE))) {
                int const err = errno;
                debug("could not mark page for read+write, error %d: %s", err, strerror(err));
                return NULL; // tried and failed
            }
            debug("allocated page %p from runtime heap", page);
            return page;
        } 
    }
    return NULL;
}
LOCAL void glas_heap_free_page(glas_heap* heap, void* page) {
    assert(glas_mem_page_ceil(page) == page);
    assert(glas_heap_includes_addr(heap, page));
    size_t const ix = (size_t)((uintptr_t)page - (uintptr_t)glas_heap_pages_start(heap)) 
                >> GLAS_HEAP_PAGE_SIZE_LG2;
    assert(ix < 64);
    uint64_t const bit = ((uint64_t)1)<<ix;
    // return page to OS
    if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_NONE))) {
        int const err = errno;
        debug("error protecting page %p from read-write, %d: %s", page, err, strerror(err));
        // not a halting error
    }
    if(unlikely(0 != madvise(page, GLAS_HEAP_PAGE_SIZE, MADV_DONTNEED))) {
        int const err = errno;
        debug("error expunging page %p from memory, %d: %s", page, err, strerror(err));
        // not a halting error, but may start with garbage when allocated again.
    }
    atomic_fetch_and_explicit(&(heap->page_bitmap),~bit, memory_order_release);
}

typedef enum glas_card_t {
    GLAS_CARD_OLD_TO_YOUNG = 0, // track refs from older to younger gens
    GLAS_CARD_FINALIZER,        // track locations of finalizers in page
    // end of list
    GLAS_CARD_TYPECOUNT
} glas_card_t;

struct glas_page {
    /** a glas_page is allocated in the mmap region of a glas_heap. */
    /** double buffering of mark bitmaps for concurrent mark and lazy sweep */
    _Atomic(uint64_t) gc_mark_bitmap[2][GLAS_PAGE_CELL_COUNT >> 6]; 

    /** extra metadata bits for every 'card' of 512 bytes, to filter scans */
    _Atomic(uint64_t) gc_cards[GLAS_CARD_TYPECOUNT][GLAS_PAGE_CARD_COUNT >> 6]; 

    // (tentative) bloom filter for cards referring to this page.
    //   useful if collecting a few pages at a time.

    // NOTE: for cache line reasons, I want to keep the big flat stuff all
    // aligned nicely with the page start.

    /** GC uses 'marking' while mutators use 'marked' to identify free space. */
    _Atomic(uint64_t) *gc_marked, *gc_marking;  // double buffers, swapped 

    // tentative: index to first gc_marked cell with open spaces.
    // heuristics for GC
    _Atomic(size_t) occupancy;          // number of cells

    uint32_t cycle;                     // how many GCs has page seen
    uint32_t gen;                       // 0 - nursery, higher is older
    glas_page *next, *prev;             // double linked list
    glas_heap *heap;                    // owning heap object
    uint64_t magic_word;                // used in assertions
} __attribute__((aligned(GLAS_HEAP_CARD_SIZE)));

static_assert((0 == (sizeof(glas_page) & (GLAS_HEAP_CARD_SIZE - 1))),
    "page header not aligned to card");
static_assert((GLAS_HEAP_PAGE_SIZE >> 6) >= sizeof(glas_page), 
    "page header has too much overhead!");

/** 
 * In the GC mark bitmaps, pre-mark bits addressing page headers. 
 * This will simplify allocation based on prior mark bits. 
 */
LOCAL void glas_gc_mark_page_hdr_bits(_Atomic(uintptr_t)* mark_bitmap) {
    static size_t const page_header_mark_bits = sizeof(glas_page) / GLAS_CELL_SIZE; 
    static size_t const full_mark_u64s = page_header_mark_bits >> 6;
    static uint64_t const partial_mark = (((uint64_t)1)<<(page_header_mark_bits % 64))-1;
    size_t ix = 0;
    for(; ix < full_mark_u64s; ++ix) {
        /** This loop applies at page sizes 256kB+. */
        atomic_store_explicit(mark_bitmap + ix, ~((uint64_t)0), memory_order_relaxed);
    }
    atomic_fetch_or_explicit(mark_bitmap + ix, partial_mark, memory_order_relaxed);
}
LOCAL inline uint64_t glas_page_magic_word_by_addr(void* addr) {
    static uint64_t const prime = (uint64_t)12233355555333221ULL;
    return prime * (uint64_t)(((uintptr_t)addr)>>(GLAS_HEAP_PAGE_SIZE_LG2));
}
LOCAL glas_page* glas_page_init(glas_heap* heap, void* addr) {
    assert(glas_mem_page_ceil(addr) == addr);
    assert(glas_heap_includes_addr(heap, addr));
    memset(addr, 0, sizeof(glas_page)); 
    glas_page* const page = (glas_page*) addr;
    page->gc_marked = page->gc_mark_bitmap[0];
    page->gc_marking = page->gc_mark_bitmap[1];
    glas_gc_mark_page_hdr_bits(page->gc_marked);
    glas_gc_mark_page_hdr_bits(page->gc_marking);
    page->magic_word = glas_page_magic_word_by_addr(addr);
    page->heap = heap;
    return page;
}
LOCAL inline glas_page* glas_page_try_alloc(glas_heap* heap) {
    void* const addr = glas_heap_try_alloc_page(heap);
    return (NULL == addr) ? NULL : glas_page_init(heap, addr);
}
LOCAL inline void glas_page_release(glas_page* page) {
    assert(0 == atomic_load_explicit(&(page->occupancy), memory_order_relaxed));
    glas_heap_free_page(page->heap, page);
}
LOCAL inline glas_page* glas_page_from_internal_addr(void* addr) {
    glas_page* const page = (glas_page*) glas_mem_page_floor(addr);
    assert(likely(glas_page_magic_word_by_addr(page) == page->magic_word));
    return page;
}



/** An 8kB scan! Don't do it often! */
LOCAL size_t glas_page_marked_count(glas_page* page) {
    size_t const buflen = GLAS_PAGE_CELL_COUNT >> 6;
    size_t result = 0;
    for(size_t ix = 0; ix < buflen; ++ix) {
        uint64_t const bitmap = atomic_load_explicit(page->gc_marked + buflen, memory_order_relaxed);
        result += popcount64(bitmap);
    }
    return result;
}


typedef enum glas_thread_status {
    GLAS_THREAD_IDLE = 0,   // awaiting work
    GLAS_THREAD_BUSY,       // block GC!
    GLAS_THREAD_WAIT,       // waiting on GC wakeup
    GLAS_THREAD_DONE,       // gc may remove and free
} glas_thread_status;

typedef struct glas_thread_stack glas_thread_stack;

struct glas_thread {
    glas_thread* next;  // singly linked list of threads
    glas_refct refct;   // destroyed by GC when terminated
    _Atomic(glas_thread_status) status;
    sem_t wakeup;       // e.g. to wait for GC signal, or wait on a thunk 
    glas_thread_stack* stack;

    // we may need to bind a lexical namespace or env
    // but not certain if that should be part of the stack
};

typedef enum {
    GLAS_GC_IDLE = 0,       // no activity at the moment
    GLAS_GC_STOP_REQUESTED, // wait for the BUSY threads to finish, block new ones
    GLAS_GC_MUTATING,       // swapping buffers, modifying heap, thread, or page lists, compacting, etc.
    GLAS_GC_MARKING,        // concurrent mark, but affects some write barriers
} glas_gc_state;

/**
 * global runtime mutex. Use sparingly!
 */
static pthread_mutex_t glas_rt_mutex = PTHREAD_MUTEX_INITIALIZER;
LOCAL void glas_rt_lock() { pthread_mutex_lock(&glas_rt_mutex); }
LOCAL void glas_rt_unlock() { pthread_mutex_unlock(&glas_rt_mutex); } 

LOCAL struct glas_rt {
    _Atomic(uint64_t) idgen;
    _Atomic(glas_heap*) heaps;

    struct {
        // We can keep a few pages around for fast alloc.
        _Atomic(glas_page*) pages;
        _Atomic(size_t) count;
    } free;

    struct {
        glas_page* pages;
        size_t count;
    } gen[GLAS_GC_MAX_GEN];

    struct {
        _Atomic(glas_thread*) threads;
        _Atomic(glas_cell*) globals;    // a mutable dict of registers
    } root;

    // TBD: 
    // - on_commit operations queues, 
    // - worker threads for opqueues, GC, lazy sparks, bgcalls

    struct {
        _Atomic(uint8_t) state;             // stop requested, gc in progress
        // note: may need scan stacks and a semaphore or similar.
        _Atomic(glas_gc_scan*) gc_scan_head;
    } gc;
    glas_file_cb vfs;
} glas_rt;

LOCAL inline uint64_t glas_rt_genid() {
    return atomic_fetch_add_explicit(&glas_rt.idgen, 1, memory_order_relaxed);
}

API void glas_file_intercept(glas_file_cb const* pvfs) {
    glas_rt_lock();
    glas_rt.vfs = *pvfs;
    glas_rt_unlock();
}

LOCAL inline bool glas_gc_stop_requested() {
    return (GLAS_GC_STOP_REQUESTED == atomic_load_explicit(&glas_rt.gc.state, memory_order_relaxed));
}
LOCAL inline bool glas_gc_mutating() {
    return (GLAS_GC_MUTATING == atomic_load_explicit(&glas_rt.gc.state, memory_order_relaxed));
}
LOCAL inline bool glas_gc_marking() {
    return (GLAS_GC_MARKING == atomic_load_explicit(&glas_rt.gc.state, memory_order_relaxed));
}

LOCAL glas_page* glas_rt_try_alloc_page_from_freelist() {
    glas_page* page = atomic_load_explicit(&glas_rt.free.pages, memory_order_acquire);
    while(NULL != page) {
        glas_page* next = page->next;
        if(atomic_compare_exchange_weak(&glas_rt.free.pages, &page, next)) {
            atomic_fetch_sub_explicit(&glas_rt.free.count, 1, memory_order_release);
            // reinitialize to ensure clean slate
            return glas_page_init(page->heap, page);
        }
    }
    return NULL;
}
LOCAL glas_page* glas_rt_try_alloc_page_from_heap() {
    glas_heap* const heap = atomic_load_explicit(&glas_rt.heaps, memory_order_relaxed);
    if(NULL == heap) { return NULL; }
    void* const addr = glas_heap_try_alloc_page(heap);
    if(NULL == addr) { return NULL; }
    return glas_page_init(heap, addr);
}
LOCAL bool glas_rt_try_add_heap() {
    // goal for this function is to have a heap at head of heaps list
    // that is not fully allocated. Doesn't actually matter who adds it.
    glas_heap* const curr_heap = atomic_load_explicit(&glas_rt.heaps, memory_order_relaxed);
    if(!glas_heap_is_full(curr_heap)) {
        return true;
    }
    glas_heap* const new_heap = glas_heap_try_create();
    if(NULL == new_heap) {
        return false; 
    }
    new_heap->next = curr_heap;
    while(!atomic_compare_exchange_strong(&glas_rt.heaps, &(new_heap->next), new_heap)) {
        if(!glas_heap_is_full(new_heap->next)) {
            // someone concurrently added a new non-full heap
            glas_heap_destroy(new_heap);
            debug("heap created then destroyed due to race condition");
            return true;
        }
        // otherwise keep looping
    }
    return true;
}

LOCAL glas_page* glas_rt_try_alloc_page() {
    assert(!glas_gc_mutating());
    // strategy: free list, head of old heaps, new heap.
    //  note: we don't scan the old heaps list because it should
    //  be fully allocated. Let GC decided what to do with that.
    do {
        // Priority: allocate from free list.
        glas_page* page = glas_rt_try_alloc_page_from_freelist();
        if(NULL != page) {
            return page;
        }
        // Secondary: allocate from head of existing heaps
        page = glas_rt_try_alloc_page_from_heap();
        if(NULL != page) {
            return page;
        }
        // Tertiary: allocate a new heap and try again. 
    } while(glas_rt_try_add_heap());
    debug("glas runtime is out of memory");
    return NULL;
}

LOCAL void glas_rt_free_page(glas_page* const page) {
    // always go to free list; GC can maybe clean up later
    assert(0 == atomic_load(&(page->occupancy)));
    page->next = atomic_load_explicit(&glas_rt.free.pages, memory_order_acquire);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.free.pages, &(page->next), page));
    atomic_fetch_add_explicit(&glas_rt.free.count, 1, memory_order_release);
}







typedef enum glas_type_id {
    GLAS_TYPE_FREE_CELL = 0, 
    GLAS_TYPE_FOREIGN_PTR,
    GLAS_TYPE_FORWARD_PTR,
    GLAS_TYPE_STEM,
    GLAS_TYPE_BRANCH,
    GLAS_TYPE_SMALL_BIN,
    GLAS_TYPE_SMALL_ARR,
    GLAS_TYPE_BIG_BIN,
    GLAS_TYPE_BIG_ARR,
    GLAS_TYPE_TAKE_CONCAT, 
    GLAS_TYPE_SEAL,
    GLAS_TYPE_REGISTER,
    GLAS_TYPE_TOMBSTONE,
    // under development
    GLAS_TYPE_THUNK,
    GLAS_TYPE_BLACKHOLE, // thunk being computed
    GLAS_TYPE_CONTENT_ADDRESSED_DATA,
    // experimental
    GLAS_TYPE_SHRUB,
    GLAS_TYPE_STEM_OF_BIN,
    // end of list
    GLAS_TYPE_ID_COUNT
} glas_type_id;
// Also use top few bits, e.g. for optional values: singleton list
// without an extra allocation

static_assert(32 > GLAS_TYPE_ID_COUNT, 
    "glas runtime reserves a few bits for logical wrappers");

typedef struct {
    _Atomic(uint8_t) gcbits;  // reserved for GC use 
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
} glas_cell_hdr;

// For GC, a conservative scan from old to young is probably good
// enough, but we could track objects per page and per card more 
// precisely

// note: should have a function from cell_hdr to a static bitmap 
// of pointer fields to support GC.
// note: consider tracking objects per card as a bitmap. This would
// serve as an alternative free list. With 32 byte allocations and 512
// byte cards, a uint16 per card could give a bitmap of all objects.



// Thought: one shape bit could support 'singleton list' versions of 
// each shape. This would support optional values without an extra
// allocation, similar to how pervasive stem bits help. With two bits,
// this could be multiple levels deep. 


struct glas_cell {
    glas_cell_hdr hdr;
    uint32_t stemH; // 0..31 bits
    union {
        struct { 
            uint32_t stemL; // 0..31 bits before L
            uint32_t stemR; // 0..31 bits before R
            glas_cell* L; 
            glas_cell* R; 
        } branch;
        struct {
            // track number filled in shape_arg.
            // only stemH is partial, these are each 32 bits
            uint32_t bits[4]; // 
            glas_cell* D;
        } stem;


        struct {
            // (TENTATIVE)
            // for very large stems or bitstrings, it is possible to
            // represent the stem as a binary.
            uint32_t stemH2, stemT; 
            glas_cell* binary;
            glas_cell* fby;
        } stem_of_bin;

        struct {
            // (TENTATIVE)
            // encode a small tree within a bitstring.
            // (Note: stemH is still a normal stem.)
            //
            //    00 - leaf
            //    01 - branch (fby left then right shrubs)
            //    10 - left (fby shrub)
            //    11 - right (fby shrub)
            //
            // can track fill in arg, but also use zeroes prefix,
            // first 'partial' fill is non-zero.
            uint32_t bits[6];
        } shrub;

        uint8_t    small_bin[24];
        glas_cell* small_arr[3]; // a list of 1..3 items

        struct {
            uint8_t const* data;
            size_t len;
            glas_cell* fptr;
            // note: append aligned slices back together if fptr matches
        } big_bin;

        struct {
            glas_cell** data;
            size_t len;
            glas_cell* fptr;
            // note: append aligned slices back together if fptr matches
        } big_arr;

        struct {
            void* ptr;
            glas_refct pin;
        } foreign_ptr;

        struct {
            // this is for inner rope nodes. Concatenate two lists,
            // and track length of left list for indexing purposes.
            uint64_t left_len;
            glas_cell* left;
            glas_cell* right;
        } concat;

        struct {
            // linearity is recorded into header
            glas_cell* key;     // weakref to register
            glas_cell* data;    // sealed data
            // data may be collected when key becomes unreachable
            glas_cell* meta;    // metadata for debug (survives data)
        } seal;

        struct {
            glas_cell* content;
            glas_cell* assoc_lhs; // associated volumes (r,_)
            glas_cell* tombstone; // weakref + stable ID; reg is finalizer
            // Note: encode a 'volume' as mutable dict of registers, held
            // by another register as needed.
            //
            // Encode associated volumes via dict (radix tree) mapping a
            // stable ID of rhs registers to an rhs-sealed volume. GC can 
            // heuristically cleanup this dict.
        } reg;

        struct {
            glas_cell* target;  // NULL if collected
            uint64_t   id;      // for hashmaps, debugging, etc.
            // id is global atomic incref; I assume 64 bits is adequate.
            glas_cell* meta;    // metadata for debug (survives object)
        } tombstone;

        struct {
            // distinguish program and namespace layer thunks in arg?
            glas_cell* operation;
            glas_cell* environment;
            // may need to convert to a 'blackhole' for eval to let 
            // others await the result
        } thunk;
        glas_cell* forward_ptr; // e.g. result of thunk

        // tbd: gc features such as free cells, forwarding pointers
    };
};
static_assert(GLAS_CELL_SIZE == sizeof(glas_cell), "invalid glas_cell size");

API void glas_i64_push(glas* g, int64_t n) {
    (void)g;
    debug("push signed int %ld", n);
}
API void glas_i32_push(glas* g, int32_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i16_push(glas* g, int16_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i8_push(glas* g, int8_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_u64_push(glas* g, uint64_t n) {
    (void)g;
    debug("push unsigned int %lu", n);
}
API void glas_u32_push(glas* g, uint32_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u16_push(glas* g, uint16_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u8_push(glas* g, uint8_t n) { glas_u64_push(g, (uint64_t) n); }

API bool glas_i64_peek(glas* g, int64_t*);
API bool glas_i32_peek(glas* g, int32_t*);
API bool glas_i16_peek(glas* g, int16_t*);
API bool glas_i8_peek(glas* g, int8_t*);
API bool glas_u64_peek(glas* g, uint64_t*);
API bool glas_u32_peek(glas* g, uint32_t*);
API bool glas_u16_peek(glas* g, uint16_t*);
API bool glas_u8_peek(glas* g, uint8_t*);


API glas* glas_thread_new() {
    debug("creating a new glas thread");
    return NULL;
}

API void glas_thread_exit(glas* g) {
    (void)g;
    debug("exiting glas thread");
    assert((NULL == g) && "expecting a valid glas context");
}

LOCAL bool glas_rt_bit_popcount() {
    return (64 == popcount64(~0))
        && (32 == popcount32(~0))
        && ( 3 == popcount64(7))
        && ( 3 == popcount32(7 << 29))
        && ( 3 == popcount64(7ULL << 61));
}

LOCAL bool glas_rt_bit_ctz() {
    return (64 == ctz64(0))
        && (32 == ctz32(0))
        && ( 3 == ctz32(8))
        && (21 == ctz32(1<<21))
        && (63 == ctz64(8ULL << 60));
}

API bool glas_rt_run_builtin_tests() {
    bool const popcount_test = glas_rt_bit_popcount();
    debug("popcount test: %s", popcount_test ? "pass" : "fail");
    bool const ctz_test = glas_rt_bit_ctz();
    debug("ctz test: %s", ctz_test ? "pass" : "fail");
    glas* g = glas_thread_new();

    // TBD: memory tests, structured data tests, computation tests, GC tests


    glas_thread_exit(g);
    return popcount_test 
        && ctz_test
        ;
}

