
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

#define GLAS_HEAP_PAGE_SIZE_LG2 21
#define GLAS_HEAP_CARD_SIZE_LG2  9
#define GLAS_HEAP_MAX_SIZE_DEFAULT (((size_t)256) << 20)
#define GLAS_HEAP_PAGE_SIZE (1 << GLAS_HEAP_PAGE_SIZE_LG2)
#define GLAS_HEAP_CARD_SIZE (1 << GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_PAGE_CARD_COUNT (GLAS_HEAP_PAGE_SIZE >> GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_CELL_SIZE 32
#define GLAS_PAGE_CELL_COUNT (GLAS_HEAP_PAGE_SIZE / GLAS_CELL_SIZE)

static_assert((0 == (0x2F & GLAS_PAGE_CELL_COUNT)), 
    "glas runtime assumes cells align to 64-bit bitmaps");
static_assert((0 == (0x2F & GLAS_PAGE_CARD_COUNT)),
    "glas runtime assumes cards align to a 64-bit bitmaps");

typedef struct glas_page glas_page;
typedef struct glas_cell glas_cell;
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

/**
 * Some forward declarations.
 */
LOCAL void glas_gc_safepoint(glas*); // allow stop-the-world
LOCAL glas_page* glas_rt_try_pop_page_from_free_list();
LOCAL void glas_rt_push_page_to_free_list(glas_page*);
LOCAL glas_page* glas_rt_try_alloc_page_from_os();
LOCAL void glas_rt_return_page_to_os(glas_page*);

/**
 * A global runtime mutex. Use sparingly!
 */
static pthread_mutex_t glas_rt_mutex = PTHREAD_MUTEX_INITIALIZER;
LOCAL void glas_rt_lock() { pthread_mutex_lock(&glas_rt_mutex); }
LOCAL void glas_rt_unlock() { pthread_mutex_unlock(&glas_rt_mutex); } 

LOCAL struct glas_rt {
    /**
     * Page allocations.
     */
    void* mmap_start;  
    size_t mmap_size;

    void* pages_start;      // may differ from mmap_start if unaligned
    void* pages_end;
    size_t page_count;      // number of pages
    _Atomic(uint64_t)* page_bitmap;  // array, '0' page unused, '1' page in use
    size_t page_bitmap_len; // u64s; last one may be partial
    _Atomic(size_t) page_bitmap_cursor; // last allocation (as a hint)

    // note: page_bitmap is 1 bit for every page in the address space.
    // This doesn't take much: 64GB has a 4kB bitmap (of 2MB pages).
    // But it won't scale well to terabytes without another index level.

    /**
     * Live pages.
     * 
     * Can access all, or by generation. 
     * 
     * Impl: doubly linked lists via page headers.
     */
    glas_page* live_pages;
    size_t live_page_count;

    glas_page* nursery_pages;
    size_t nursery_page_count;

    glas_page* survivor_pages;
    size_t survivor_page_count;

    glas_page* old_pages;
    size_t old_page_count;

    /**
     * Pages freed via compaction.
     * 
     * We might hold a few, heuristically. But these can also release
     * memory back to OS.
     */
    _Atomic(glas_page*) free_pages; 
    _Atomic(size_t) free_page_count;

    /**
     * GC Support
     * - gc_state: indicates global stop requests, gc in-progress
     * - gc_scan: tracks cells that are marked but not scanned
     * 
     * GC threads will mostly operate on their own queues, but may
     * overflow to a shared stack.
     */
    _Atomic(uint8_t) gc_state; // stop requested, gc in progress
    _Atomic(glas_gc_thread*) gc_threads;
    _Atomic(glas_gc_scan*) gc_scan_head;
    _Atomic(_Atomic(glas_gc_scan*)*) gc_scan_tail_field;

    /**
     * Memory and GC
     * 
     * Currently, two sources of GC roots: 
     * 
     * - the volume of global registers
     * - glas threads (including forks, workers, bgcalls, etc.)
     * 
     * To simplify GC, we'll control new threads via global rt mutex.
     * 
     * Cells and glas threads (~coroutines)
     * We'll support worker threads and bgcalls as glas threads.
     */
    _Atomic(glas_cell*) globals;
    _Atomic(glas*) threads;

    /**
     * ID generator for tombstones, etc.
     */
    _Atomic(uint64_t) idgen;

    /**
     * User configuration of runtime.
     */
    glas_vfs vfs;


    _Atomic(bool) initialized;
} glas_rt;

API bool glas_rt_is_initialized() {
    return atomic_load_explicit(&glas_rt.initialized, memory_order_relaxed);
}

LOCAL inline uint64_t glas_rt_genid() {
    return atomic_fetch_add_explicit(&glas_rt.idgen, 1, memory_order_relaxed);
}

LOCAL inline void* glas_rt_addr_to_page(void* addr) {
    return (void*)(((uintptr_t) addr) & ~(GLAS_HEAP_PAGE_SIZE - 1));
}

LOCAL inline bool glas_rt_addr_in_heap(void* addr) {
    return (glas_rt.pages_end > addr) && (addr >= glas_rt.pages_start);
}

LOCAL void glas_rt_init_locked() {
    if(likely(0 == glas_rt.mmap_size)) {
        glas_rt.mmap_size = GLAS_HEAP_MAX_SIZE_DEFAULT;
    }
    assert(0 == (glas_rt.mmap_size & (GLAS_HEAP_PAGE_SIZE - 1)));
    int map_flags = MAP_PRIVATE | MAP_ANONYMOUS | MAP_NORESERVE;
    // TODO: Make HUGETLB support configurable, perhaps via env vars.
    //    | MAP_HUGETLB | (21 << MAP_HUGE_SHIFT) // not on my machine
    glas_rt.mmap_start = mmap(NULL, glas_rt.mmap_size, PROT_NONE, map_flags, -1, 0);
    if(unlikely(MAP_FAILED == glas_rt.mmap_start)) {
        debug("failed to allocate heap address space (size %lu)", glas_rt.mmap_size);
        exit(EXIT_FAILURE);
    }

    glas_rt.pages_start = glas_rt_addr_to_page((void*) ((uintptr_t)glas_rt.mmap_start + (GLAS_HEAP_PAGE_SIZE - 1)));
    glas_rt.page_count = (glas_rt.mmap_size / GLAS_HEAP_PAGE_SIZE) - 
        ((glas_rt.pages_start == glas_rt.mmap_start) ? 0 : 1); // may lose one page to realignment
    glas_rt.pages_end = (void*)(((uintptr_t) glas_rt.pages_start) + (glas_rt.page_count * GLAS_HEAP_PAGE_SIZE));

    glas_rt.page_bitmap_len = ((glas_rt.page_count + 63) / 64);
    size_t const page_bitmap_bytes = sizeof(uint64_t) * glas_rt.page_bitmap_len;
    glas_rt.page_bitmap = (_Atomic(uint64_t)*) malloc(page_bitmap_bytes);
    if(unlikely(NULL == glas_rt.page_bitmap)) {
        debug("failed to allocate page bitmap (size %lu)", page_bitmap_bytes);
        exit(EXIT_FAILURE);
    }
    memset(glas_rt.page_bitmap, 0, page_bitmap_bytes);

    // last bitmap may be partial; mark pages after end of heap as 'in use'
    // so we don't allocate them later. 
    size_t const last_bitmap_pages = (glas_rt.page_count % 64);
    if(0 != last_bitmap_pages) {
        _Atomic(uint64_t)* const last_bitmap = glas_rt.page_bitmap + glas_rt.page_bitmap_len - 1;
        for(size_t ix = last_bitmap_pages; ix < 64; ++ix) {
            atomic_fetch_or_explicit(last_bitmap, (((uint64_t)1)<<ix), memory_order_relaxed);
        }
        uint64_t bitmap_final = atomic_load(last_bitmap);
        debug("final bitmap: %lx", bitmap_final);
    }
}

LOCAL void glas_rt_init_slow() {
    glas_rt_lock();
    if(!atomic_load_explicit(&glas_rt.initialized, memory_order_acquire)) {
        glas_rt_init_locked();
        atomic_store_explicit(&glas_rt.initialized, true, memory_order_release);
    }
    glas_rt_unlock();
}

LOCAL inline void glas_rt_init() {
    // only init once, but avoid locking every time
    if(unlikely(!atomic_load_explicit(&glas_rt.initialized, memory_order_relaxed))) {
        glas_rt_init_slow();
    }
};

API void glas_rt_cfg_heap(size_t heap_size) {
    static size_t const min_heap_size = 8 * GLAS_HEAP_PAGE_SIZE;
    heap_size = (min_heap_size > heap_size) ? min_heap_size : heap_size;
    heap_size = (heap_size & ~(GLAS_HEAP_PAGE_SIZE - 1));
    glas_rt_lock();
    if(likely(!glas_rt_is_initialized())) {
        glas_rt.mmap_size = heap_size;
    } else {
        debug("Cannot configure heap size: heap already initialized!");
    }
    glas_rt_unlock();
}

API void glas_rt_loader_intercept(glas_vfs vfs) {
    glas_rt_lock();
    glas_rt.vfs = vfs;
    glas_rt_unlock();
}



typedef enum glas_card_t {
    GLAS_CARD_OLD_TO_YOUNG = 0, // track refs from older to younger gens
    GLAS_CARD_FINALIZER,        // track locations of finalizers in page
    // end of list
    GLAS_CARD_TYPECOUNT
} glas_card_t;

/**
 * actually just the page header
 */
struct glas_page {
    /** double buffering of mark bitmaps for concurrent mark and lazy sweep */
    _Atomic(uint64_t) gc_mark_bitmap[2][GLAS_PAGE_CELL_COUNT >> 6]; // 16kB

    /** extra metadata bits for every 'card' of 512 bytes, to filter scans */
    _Atomic(uint64_t) gc_cards[GLAS_CARD_TYPECOUNT][GLAS_PAGE_CARD_COUNT >> 6]; // 512B * typecount
    // (tentative) bloom filter for cards referring to this page.
    //   useful if collecting a few pages at a time.

    /** The GC uses 'marking' while mutators use 'marked' to identify free space. */
    _Atomic(uint64_t) *gc_marked, *gc_marking;  // double buffers, swapped 

    // tentative: index to first gc_marked cell with open spaces.
    // heuristics for GC
    _Atomic(size_t) occupancy;          // number of cells

    // Last thing is our 
    uint32_t gen;                       // 0 - nursery, higher is older
    glas_page *gen_next, *gen_prev;     // e.g. nursery, survivor, or old pages
    glas_page *next, *prev;             // live or free pages

} __attribute__((aligned(GLAS_HEAP_CARD_SIZE)));

// test that alignment attribute
static_assert((0 == (sizeof(glas_page) & (GLAS_HEAP_CARD_SIZE - 1))),
    "page header not aligned to card");

// let's keep header to less than 2% of page sizes
static_assert((GLAS_HEAP_PAGE_SIZE >> 6) >= sizeof(glas_page), 
    "page header is too large!");

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

LOCAL inline size_t glas_rt_card_offset(void* addr) {
    return (size_t)(((uintptr_t) addr) & (GLAS_HEAP_PAGE_SIZE - 1)) 
                        >> GLAS_HEAP_CARD_SIZE_LG2;
}

/**
 * Attempt to grab a page from the free list, if one exists.
 */
LOCAL glas_page* glas_rt_try_pop_page_from_free_list() {
    glas_page* result = atomic_load_explicit(&glas_rt.free_pages, memory_order_acquire);
    while(NULL != result) {
        glas_page* next = result->next;
        if(atomic_compare_exchange_weak(&glas_rt.free_pages, &result, next)) {
            atomic_fetch_sub_explicit(&glas_rt.free_page_count, 1, memory_order_release);
            return result;
        }
    }
    return NULL;
}

/** push a page to the free list. */
LOCAL void glas_rt_push_page_to_free_list(glas_page* const page) {
    // page must not be connected to anything
    assert((NULL != page) && (NULL == page->next) && (NULL == page->prev) &&
           (NULL == page->gen_next) && (NULL == page->gen_prev) &&
           (0 == atomic_load(&(page->occupancy))));
    page->next = atomic_load_explicit(&glas_rt.free_pages, memory_order_acquire);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.free_pages, &(page->next), page));
    atomic_fetch_add_explicit(&glas_rt.free_page_count, 1, memory_order_release);
}

/** Basic initialization. Does not attach page to live pages. */
LOCAL glas_page* glas_page_init(void* addr) {
    assert(glas_rt_addr_in_heap(addr));
    if(0 != mprotect(addr, GLAS_HEAP_PAGE_SIZE, PROT_READ | PROT_WRITE)) {
        int err = errno;
        debug("error %d on mprotect: %s", err, strerror(err));
        exit(EXIT_FAILURE);
    }
    #ifdef GLAS_PAGE_INIT_MZERO
    memset(addr, 0, sizeof(glas_page)); // should be unnecessary with mmap
    #endif
    glas_page* const page = (glas_page*) addr;
    page->gc_marked = page->gc_mark_bitmap[0];
    page->gc_marking = page->gc_mark_bitmap[1];
    return page;
}

LOCAL glas_page* glas_rt_try_alloc_page_from_os() {
    // simple round-robin search, remembers where it left off
    size_t const start = atomic_load_explicit(&glas_rt.page_bitmap_cursor, memory_order_relaxed);
    assert(start < glas_rt.page_bitmap_len);
    for(size_t offset = 0; offset < glas_rt.page_bitmap_len; ++offset) {
        size_t const bitmap_ix = (start + offset) % glas_rt.page_bitmap_len;
        _Atomic(uint64_t)* const pbitmap = glas_rt.page_bitmap + bitmap_ix;
        uint64_t bitmap = atomic_load_explicit(pbitmap, memory_order_relaxed);
        while(0 != ~bitmap) {
            size_t const bit_ix = ctz64(~bitmap);
            uint64_t const bit = ((uint64_t)1) << bit_ix;
            bitmap = atomic_fetch_or(pbitmap, bit);
            if(likely(0 == (bitmap & bit))) {
                // won the race and now own the page
                // save cursor state so we can continue
                atomic_store_explicit(&glas_rt.page_bitmap_cursor, bitmap_ix, memory_order_relaxed);
                size_t const page_ix = (64 * bitmap_ix) + bit_ix;
                assert(page_ix < glas_rt.page_count);
                void* const addr = (void*)(((uintptr_t) glas_rt.pages_start) +
                        (GLAS_HEAP_PAGE_SIZE * page_ix));
                debug("allocated page %lu (addr %p) from runtime heap", page_ix, addr);
                return glas_page_init(addr);
            } // end if acquired page
        } // end while unoccupied bits remain
    } // end loop over all bitmaps
    return NULL; // every page is in use (or was when we looked)
}

LOCAL void glas_rt_return_page_to_os(glas_page* page) {
    debug("returning page %p to OS", page);
    // should ensure page is detached first
    assert((NULL != page) && (NULL == page->next) && (NULL == page->prev) &&
           (NULL == page->gen_next) && (NULL == page->gen_prev) &&
           (0 == atomic_load(&(page->occupancy))));
    assert(glas_rt_addr_in_heap((void*)page));

    if(0 != mprotect((void*) page, GLAS_HEAP_PAGE_SIZE, PROT_NONE)) {
        int err = errno;
        debug("error %d on mprotect: %s", err, strerror(err));
    }
    if(0 != madvise((void*) page, GLAS_HEAP_PAGE_SIZE, MADV_DONTNEED)) {
        int err = errno;
        debug("error %d on madvise: %s", err, strerror(err));
    }
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
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
    uint8_t reserved; 
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
    glas_rt_init();
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
    glas_page* page = glas_rt_try_alloc_page_from_os();
    glas_rt_return_page_to_os(page);

    glas_thread_exit(g);
    return popcount_test 
        && ctz_test
        ;
}

