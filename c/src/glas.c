// Aiming for an 'all in one file' for glas runtime.
#define _DEFAULT_SOURCE

#include <stdint.h>
#include <stdalign.h>
#include <stdatomic.h>
#include <stddef.h>
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

// check my assumptions
static_assert((sizeof(void*) == 8), 
    "glas runtime assumes 64-bit pointers");
static_assert((sizeof(void(*)(void*, bool)) == 8),
    "glas runtime assumes 64-bit function pointers");
static_assert((sizeof(_Atomic(void*)) == 8),
    "glas runtime assumes 64-bit atomic pointers");
static_assert((sizeof(_Atomic(uint64_t)) == 8) && sizeof(_Atomic(uint8_t)),
    "glas runtime assumes atomic integers don't affect size");
static_assert((ATOMIC_POINTER_LOCK_FREE == 2) && 
              (ATOMIC_LLONG_LOCK_FREE == 2) &&
              (ATOMIC_LONG_LOCK_FREE == 2) && 
              (ATOMIC_INT_LOCK_FREE == 2) &&
              (ATOMIC_SHORT_LOCK_FREE == 2) &&
              (ATOMIC_CHAR_LOCK_FREE == 2), 
              "glas runtime assumes lock-free atomic integers");

#define GLAS_HEAP_PAGE_SIZE_LG2 21
#define GLAS_HEAP_CARD_SIZE_LG2  9
#define GLAS_HEAP_PAGE_SIZE (1 << GLAS_HEAP_PAGE_SIZE_LG2)
#define GLAS_HEAP_CARD_SIZE (1 << GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_HEAP_MMAP_SIZE (GLAS_HEAP_PAGE_SIZE << 6)
#define GLAS_PAGE_CARD_COUNT (GLAS_HEAP_PAGE_SIZE >> GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_CELL_SIZE 32
#define GLAS_PAGE_CELL_COUNT (GLAS_HEAP_PAGE_SIZE / GLAS_CELL_SIZE)
#define GLAS_GC_MAX_GEN 3
#define GLAS_GC_SCAN_SIZE 1000
#define GLAS_GC_POLL_WAIT_NSEC (1000 * 1000)
#define GLAS_GC_POLL_STOP_NSEC (200 * 1000)
static_assert((0 == (0x2F & GLAS_PAGE_CARD_COUNT)),
    "glas runtime assumes cards align to a 64-bit bitmaps");

/**
 * GC state transitions.
 * 
 * States:
 * - Idle (0b00) - no GC activity
 * - Stop (0b01) - stop-the-world
 * - Busy (0b11) - mutating heap structure
 * - Mark (0b10) - concurrent mark phase
 * 
 * Transitions:
 * - Idle->Stop - gc triggers
 * - Stop->Busy - wait on busy threads, set Busy
 * - Busy->Mark - clear Stop flag, awaken threads
 * - Mark->Busy - set Stop flag, wait on busy threads
 * - Busy->Idle - clear flags, awaken threads
 * 
 * Guarantees:
 * - threads and GC aren't mutating at the same time
 * - mark and idle states are stable in view of busy threads
 */
typedef enum {
    GLAS_GC_IDLE = 0b00, 
    GLAS_GC_STOP = 0b01,
    GLAS_GC_BUSY = 0b11,
    GLAS_GC_MARK = 0b10,
} glas_gc_flags;


/**
 * Thread state, transitions, and GC coordination.
 *
 * States:
 * - Done - thread finished, GC to reclaim when busy
 * - Idle - not executing, or waiting on non-GC events
 * - Busy - mutating heap, blocks GC busy phase
 * - Wait - suspended, waiting for GC to complete
 * 
 * Transitions:
 * - Idle->Busy|Wait - set Busy, check GC Stop.
 *   - If Stop, switch to Wait.
 *   - Otherwise, continue with Busy.
 * - Wait->Busy - repetitive retries when waiting
 * - Busy->Idle - thread sets this state at any time.
 * - Idle|Busy->Done - set Done, and never change state again
 * 
 * Note that the idle state includes waiting on client APIs, waiting on 
 * thunks, etc.. Anything that isn't waiting on GC wakeup. The thread
 * type is really just the GC's view of a thread.
 * 
 * New threads must atomically join the global linked list in idle state.
 */
typedef enum glas_thread_state {
    GLAS_THREAD_DONE = 0,
    GLAS_THREAD_IDLE,
    GLAS_THREAD_BUSY,
    GLAS_THREAD_WAIT,
} glas_thread_state;

typedef enum glas_type_id {
    GLAS_TYPE_INVALID = 0, 
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
    GLAS_TYPE_CONTENT_ADDRESSED_DATA,
    // experimental
    GLAS_TYPE_GLOB, // interpret binary as glas object
    GLAS_TYPE_STEM_OF_BIN,
    // end of list
    GLAS_TYPE_ID_COUNT
} glas_type_id;
// Also use top few bits, e.g. for optional values: singleton lists
// without allocation.
static_assert(32 > GLAS_TYPE_ID_COUNT, 
    "glas runtime reserves a few bits for logical wrappers");

typedef enum glas_card_id {
    GLAS_CARD_OLD_TO_YOUNG = 0, // track refs from older to younger gens
    GLAS_CARD_FINALIZER,        // track locations of finalizers in page
    // end of list
    GLAS_CARD_IDYPECOUNT
} glas_card_id;

typedef struct glas_heap glas_heap; // mmap location    
typedef struct glas_page glas_page; // aligned region
typedef struct glas_cell glas_cell; // datum in page
typedef struct glas_tls glas_tls; // per OS thread
typedef struct glas_thread glas_thread; // green threads
typedef struct glas_siglist glas_siglist;
typedef struct glas_gc_scan glas_gc_scan;

typedef struct glas_cell_hdr {
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    _Atomic(uint8_t) gcbits;  // write barriers, perhaps a tiny refct. 
} glas_cell_hdr;

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
            uint32_t stem32[4]; // full 32-bit stems, count in type arg
            glas_cell* D;
        } stem;

        uint8_t    small_bin[24]; // size in type arg
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
            glas_refct pin;     // finalizer unless pin.refct_upd is NULL.
        } foreign_ptr;

        struct {
            // for inner rope nodes, combines slicing with concatenation.
            // maybe add zero-fill for left suffix up to left_len, too.
            uint64_t left_len;
            glas_cell* left;
            glas_cell* right;
        } take_concat;

        struct {
            // linearity is recorded into header
            glas_cell* key;     // weakref to register
            glas_cell* data;    // sealed data
            glas_cell* meta;    // metadata for debug, e.g. timestamp, provenance
            // data may be collected when key becomes unreachable
        } seal;

        struct {
            glas_cell* content;
            glas_cell* tombstone; // weakref + stable ID; reg is finalizer
            glas_cell* assoc_lhs; // associated volumes (r,_)
            // Sketch:
            // - tombstone and assoc_lhs are allocated only when needed
            // - basic volume of registers as a lazy dict (load via thunks)
            // - associated volume as radix tree mapping rhs register stable
            //   IDs to rhs-sealed basic volumes, i.e. mutate assoc_lhs.
            // - GC can eliminate unreachable branches of radix tree 
        } reg;
        
        struct {
            glas_cell* target;  // NULL if collected
            uint64_t   id;      // for hashmaps, debugging, etc.
            glas_cell* meta;    // metadata for debug (survives object)
            // id is global atomic incref; I assume 64 bits is adequate.
        } tombstone;

        struct {
            // data held externally, e.g. content-addressed storage
            glas_cell* ref;         // runtime-recognized reference
            glas_cell* tombstone;   // stable ID and weakref for caching
        } xref;

        struct {
            // (Tentative)
            // constraints:
            //  - can 'claim' or 'set' a thunk. No need to await transactions.
            //  - can await thunks
            //  - wait can be interrupted safely, e.g. cancellation
            //  - canceled waits can be garbage-collected (weakrefs?)
            //  
            // the computation 'language' is indicated in type_arg.
            // the computation captures function and inputs, perhaps a
            // frozen view of relevant registers in the general case. 
            _Atomic(glas_cell*) computation;
            _Atomic(glas_cell*) result;
            // ~ need some way to wait for a result, but interruptible.
        } thunk;

        // TBD: I may want specialized foreign pointers for glas_link_cb
        // and glas_prog_cb objects. 
        //
        // Also, it's unclear whether I need a separate type for mutable
        // thread roots. 

        struct {
            // (TENTATIVE)
            // represent data as indexing into a compact binary, ideally verified
            size_t index;
            uint8_t const* binary;
            glas_cell* fptr;
        } glob;

        struct {
            // (TENTATIVE)
            // for very large stems or bitstrings, it is possible to
            // directly represent the stem as a binary.
            glas_cell* binary;
            glas_cell* fby;
        } stem_of_bin;

    };
};
static_assert(GLAS_CELL_SIZE == sizeof(glas_cell), "invalid glas_cell size");

struct glas_page {
    /** a glas_page is allocated in the mmap region of a glas_heap. */
    /** double buffering of mark bitmaps for concurrent mark and lazy sweep */
    _Atomic(uint64_t) gc_mark_bitmap[2][GLAS_PAGE_CELL_COUNT >> 6]; 

    /** extra metadata bits for every 'card' of 512 bytes, to filter scans */
    _Atomic(uint64_t) gc_cards[GLAS_CARD_IDYPECOUNT][GLAS_PAGE_CARD_COUNT >> 6]; 

    // (tentative) bloom filter for cards referring to this page.
    //   useful only if collecting a few pages at a time.

    // NOTE: for cache alignment reasons, keep the big flat arrays above.

    /** GC uses 'marking' while mutators use 'marked' to identify free space. */
    _Atomic(uint64_t) *gc_marked, *gc_marking;  // double buffers, swapped 

    // tentative: index to first gc_marked cell with open spaces.
    // heuristics for GC
    _Atomic(size_t) occupancy;          // number of cells
    uint32_t gen;                       // 0 - nursery, higher is older
    glas_page *next;                    // page in linked list
    glas_heap *heap;                    // owning heap object
    uint64_t magic_word;                // used in assertions
} __attribute__((aligned(GLAS_HEAP_CARD_SIZE)));

static_assert((0 == (sizeof(glas_page) & (GLAS_HEAP_CARD_SIZE - 1))),
    "glas page header not aligned to card");
static_assert((GLAS_HEAP_PAGE_SIZE >> 6) >= sizeof(glas_page), 
    "glas page header is too large");

struct glas_heap {
    // each heap tracks 63-64 'pages', depending on alignment
    glas_heap* next;
    void* mem_start;
    _Atomic(uint64_t) page_bitmap;
};

/**
 * GC's view of a thread.
 * 
 * Represents either an active execution thread or a passive collection
 * of GC roots. Specialized implementations (workers, API threads, root
 * storage) embed this structure and implement the callback hooks.
 * 
 * LIFECYCLE
 * - Created and added to global thread list
 * - State transitions coordinated with GC
 * - Marked DONE when finished
 * - Removed from list and finalized at next GC
 * 
 * PASSIVE ROOTS
 * For passive root storage (no execution), set:
 * - state = GLAS_THREAD_IDLE until DONE
 * - finalizer = cleanup function
 * 
 * Passive roots can be edited by any BUSY thread when GC is not stopping
 * operations. During marking, write barriers are required.
 */
struct glas_thread {
    glas_thread* next;  // global thread list
    _Atomic(glas_thread_state) state;

    /**
     * A temporary semaphore for waiting on GC.
     * 
     * Should initialize to NULL. This is assigned temporarily for the
     * GLAS_THREAD_WAIT state, e.g. shared via OS thread-local storage.
     */
    sem_t* wakeup;

    /**
     * Self-reference to derived structure.
     * 
     * Points to containing structure (worker thread, api thread, etc.).
     * Used by callbacks to access thread-specific state.
     */
    void* self;

    /**
     * Finalizer called when thread is removed from global list.
     * 
     * Called during GC after MUTATING phase, before clearing STOP.
     * At this point:
     * - Thread is in DONE state
     * - No other threads are BUSY
     * - Safe to free memory and clean up resources
     * 
     * May be NULL if no cleanup needed.
     */
    void (*finalizer)(void* self);

    /** 
     * Root set specification.
     * 
     * Array of uint16_t offsets to `glas_cell*` fields, relative to 
     * `self`. Terminated by UINT16_MAX sentinel.
     * 
     * This limits each glas_thread to 8192 roots. For larger root sets, 
     * options include:
     * - Spill excess roots to glas heap arrays
     * - Create additional passive glas_thread for overflow
     * 
     * MUTATION RULES
     * 1. At least one thread is in BUSY state to ensure GC not MUTATING
     * 2. During GC's MARKING phase, write barriers must be used
     * 3. Any BUSY thread may modify any thread's roots (coordination 
     *    via application-level locking or atomics, not GC)
     * 
     * The roots array itself must be stable (never reallocated) for the
     * thread's lifetime.
     */
    uint16_t const* roots;
};

/**
 * TBD: Allocation of cells.
 * 
 * To allocate cells, we'll need a page. But a page isn't at the right
 * level for a glas_thread to work with - too big, too awkward. Instead,
 * we might introduce some thread-local storage per OS thread to support
 * allocations. 
 * 
 * We can also use thread-local storage to support write barriers, e.g. 
 * keeping a GC scan list per OS thread. This would reduce contention.
 * 
 * When GC is performed, the GC may decided to perform nursery collections
 * on the thread-local nursery pages.
 */


/**
 * A scan stack or queue for concurrent GC marking
 * 
 * During concurrent mark and sweep, marked cells are added to a scan
 * list. This forms an implicit basis for tri-color marking:
 * 
 * - WHITE is unmarked, unscanned, not awaiting scan
 * - GREY is marked and awaiting scan
 * - BLACK is marked and scan completed
 * 
 * Mutation during the mark phase may require a write barrier that adds
 * some items to a thread-local scan stack. When filled, we can push the
 * entire stack for background processing and allocate another.
 */
struct glas_gc_scan {
    glas_gc_scan* next; // sequential composition
    size_t fill; // items 0..(fill-1) are assigned
    glas_cell* stack[GLAS_GC_SCAN_SIZE];
};

/**
 * An 'immutable' runtime configuration.
 * 
 * Via copy-on-write, and in-place updates when refct is 1. This design
 * ensures we don't have any "torn" reads when API is updating options,
 * without writers waiting on readers. 
 */
typedef struct glas_conf {
    _Atomic(size_t) refct;
    glas_file_cb cb;
} glas_conf;

/**
 * A global runtime lock - use very sparingly!
 */
static pthread_mutex_t glas_rt_mutex = PTHREAD_MUTEX_INITIALIZER;
LOCAL inline void glas_rt_lock() { pthread_mutex_lock(&glas_rt_mutex); }
LOCAL inline void glas_rt_unlock() { pthread_mutex_unlock(&glas_rt_mutex); }

LOCAL struct glas_rt {
    _Atomic(uint64_t) idgen;
    _Atomic(glas_heap*) heaps;

    struct {
        // GC may heuristically leave pages here for fast alloc
        _Atomic(glas_page*) pages;
        _Atomic(size_t) count;
    } free;

    struct {
        _Atomic(glas_thread*) threads;  // GC's view of threads
        _Atomic(glas_cell*) globals;    // lazy volume of registers
    } root;

    // TBD: 
    // - on_commit operations queues, 
    // - worker threads for opqueues, GC, lazy sparks, bgcalls
    // idea: count threads, highest number thread quits if too many,
    // e.g. compared to a configuration; configured number vs. actual.

    struct {
        _Atomic(glas_gc_flags) state;    
        _Atomic(glas_gc_scan*) scan_head;

        // enable busy threads to signal GC. The wakeup here must
        // be allocated only once. 
        _Atomic(size_t) busy_threads_count;
        sem_t wakeup;
        _Atomic(bool) wakeup_init;
    } gc;

    glas_conf* conf; // 'immutable' via copy on write, uses mutex
    _Atomic(uint32_t) initialized;
} glas_rt;

LOCAL inline uint64_t glas_rt_genid() {
    return atomic_fetch_add_explicit(&glas_rt.idgen, 1, memory_order_relaxed);
}
LOCAL glas_conf* glas_conf_new() {
    glas_conf* result = malloc(sizeof(glas_conf));
    memset(result, 0, sizeof(glas_conf));
    atomic_init(&(result->refct), 1);
    return result;
}
LOCAL glas_conf* glas_conf_clone(glas_conf const* src) {
    glas_conf* result = glas_conf_new();
    // copy all fields except refct
    result->cb = src->cb;
    return result;
}
LOCAL inline void glas_conf_incref(glas_conf* conf) {
    atomic_fetch_add_explicit(&(conf->refct), 1, memory_order_relaxed);
}
LOCAL inline void glas_conf_decref(glas_conf* conf) {
    size_t const prior_refct = atomic_fetch_sub_explicit(&(conf->refct), 1, memory_order_relaxed);
    if(1 == prior_refct) {
        free(conf);
    }
}
LOCAL void glas_rt_withlock_conf_prepare_for_write() {
    // copy on write, but skip copy if refct is 1 (likely)
    if(unlikely(NULL == glas_rt.conf)) {
        glas_rt.conf = glas_conf_new();
    }
    size_t const prior_refct = atomic_fetch_add_explicit(&(glas_rt.conf->refct), 1, memory_order_relaxed);
    if(likely(1 == prior_refct)) {
        atomic_store_explicit(&(glas_rt.conf->refct), 1, memory_order_relaxed);
        // prepared! no other refs to config, and lock prevents new ones
    } else {
        // copy-on-write
        glas_conf* const tmp = glas_rt.conf;
        glas_rt.conf = glas_conf_clone(tmp);
        glas_conf_decref(tmp);
    }
}
LOCAL glas_conf* glas_rt_conf_copy_for_read() {
    glas_rt_lock();
    if(unlikely(NULL == glas_rt.conf)) {
        glas_rt.conf = glas_conf_new();
    }
    glas_conf* result = glas_rt.conf;
    glas_conf_incref(result);
    glas_rt_unlock();
    return result;
}
API void glas_rt_set_loader(glas_file_cb const* pvfs) {
    glas_rt_lock();
    glas_rt_withlock_conf_prepare_for_write();
    glas_rt.conf->cb = *pvfs;
    glas_rt_unlock();
    // note: all configuration updates should look like this
}

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
static inline size_t clz32(uint32_t n) {
    static_assert(sizeof(unsigned int) == sizeof(uint32_t));
    return (size_t) __builtin_clz(n);
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
LOCAL inline size_t glas_mem_card_index(void* addr) {
    return (size_t)(((uintptr_t)addr & (GLAS_HEAP_PAGE_SIZE - 1)) >> GLAS_HEAP_CARD_SIZE_LG2);
}
LOCAL inline void* glas_heap_pages_start(glas_heap* heap) {
    return glas_mem_page_ceil(heap->mem_start);
}
LOCAL inline bool glas_heap_includes_addr(glas_heap* heap, void* addr) {
    return (addr >= heap->mem_start) &&
            ((void*)((uintptr_t)heap->mem_start + GLAS_HEAP_MMAP_SIZE) > addr);
}
LOCAL inline uint64_t glas_heap_initial_bitmap(glas_heap* heap) {
    // we lose at most one page per mmap due to alignment, always the last page
    // this is ~1.6% loss of address space, but is not allocated RAM from OS
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
        debug("mmap failed to reserve memory for glas heap, error %d: %s", errno, strerror(errno));
        free(heap);
        return NULL;
    }
    atomic_init(&(heap->page_bitmap), glas_heap_initial_bitmap(heap));
    //debug("%luMB heap created at %p", (size_t)GLAS_HEAP_MMAP_SIZE>>20, heap->mem_start); 
    return heap;
}
LOCAL void glas_heap_destroy(glas_heap* heap) {
    assert(glas_heap_is_empty(heap));
    if(0 != munmap(heap->mem_start, GLAS_HEAP_MMAP_SIZE)) {
        debug("munmap failed, error %d: %s", errno, strerror(errno));
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
            // won the potential race; return the page, but first mark it read-writable
            void* const page = (void*)(((uintptr_t)glas_heap_pages_start(heap)) + (ix * GLAS_HEAP_PAGE_SIZE));
            if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_READ | PROT_WRITE))) {
                debug("could not mark page for read+write, error %d: %s", errno, strerror(errno));
                return NULL; // tried and failed
            }
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
        debug("error protecting page %p from read-write, %d: %s", page, errno, strerror(errno));
        // not a halting error
    }
    if(unlikely(0 != madvise(page, GLAS_HEAP_PAGE_SIZE, MADV_DONTNEED))) {
        debug("error expunging page %p from memory, %d: %s", page, errno, strerror(errno));
        // not a halting error, but may start with garbage when allocated again.
    }
    atomic_fetch_and_explicit(&(heap->page_bitmap),~bit, memory_order_release);
}


/** 
 * In the GC mark bitmaps, pre-mark bits addressing page headers. 
 * This will simplify allocation based on prior mark bits. 
 */
LOCAL void glas_gc_mark_page_hdr_bits(_Atomic(uintptr_t)* mark_bitmap) {
    static size_t const page_header_mark_bits = sizeof(glas_page) / GLAS_CELL_SIZE; 
    static size_t const full_mark_u64s = page_header_mark_bits / 64;
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
LOCAL inline glas_page* glas_page_from_internal_addr(void* addr) {
    glas_page* const page = (glas_page*) glas_mem_page_floor(addr);
    assert(likely(glas_page_magic_word_by_addr(page) == page->magic_word));
    return page;
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
    if((NULL != curr_heap) && !glas_heap_is_full(curr_heap)) {
        return true;
    }
    glas_heap* const new_heap = glas_heap_try_create();
    if(NULL == new_heap) {
        return false; 
    }
    new_heap->next = curr_heap;
    while(!atomic_compare_exchange_weak(&glas_rt.heaps, &(new_heap->next), new_heap)) {
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
    // strategy: free list, head of old heaps, new heap.
    //  note: we don't scan old heaps list because they should
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
    return NULL;
}
LOCAL void glas_rt_free_page(glas_page* const page) {
    // always go to free list; GC can maybe clean up later
    assert(0 == atomic_load(&(page->occupancy)));
    page->next = atomic_load_explicit(&glas_rt.free.pages, memory_order_acquire);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.free.pages, &(page->next), page));
    atomic_fetch_add_explicit(&glas_rt.free.count, 1, memory_order_release);
}

LOCAL void glas_rt_gc_wakeup_init() {
    glas_rt_lock();
    bool const ready = atomic_load_explicit(&(glas_rt.gc.wakeup_init), memory_order_acquire);
    if(!ready) {
        if(0 != sem_init(&(glas_rt.gc.wakeup), 0, 0)) {
            debug("error initializing GC wakeup mutex (%d): %s", errno, strerror(errno));
            abort();
        }
    }
    atomic_store_explicit(&(glas_rt.gc.wakeup_init), true, memory_order_release);
    glas_rt_unlock();
}
LOCAL inline sem_t* glas_rt_gc_wakeup_get() {
    bool const ready = atomic_load_explicit(&(glas_rt.gc.wakeup_init), memory_order_relaxed);
    if(unlikely(!ready)) {
        glas_rt_gc_wakeup_init();
    }
    return &(glas_rt.gc.wakeup);
}
LOCAL inline glas_thread_state glas_thread_get_state(glas_thread* t) {
    return atomic_load_explicit(&(t->state), memory_order_relaxed);
}
LOCAL void glas_thread_become_busy(glas_thread* t) {
    assert(GLAS_THREAD_DONE != glas_thread_get_state(t)); // no exiting DONE!
    assert(GLAS_THREAD_BUSY != glas_thread_get_state(t)); // no recursive BUSY! 
    bool wakeup_init = false;
    sem_t wakeup; 
    do {
        // Attempt to enter the busy state, blocking GC mutation
        atomic_store_explicit(&(t->state), GLAS_THREAD_BUSY, memory_order_relaxed);
        glas_gc_flags gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_seq_cst);
        if(0 == (GLAS_GC_STOP & gc)) {
            atomic_fetch_add_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
            if(wakeup_init) { 
                sem_destroy(&wakeup); 
            }
            t->wakeup = NULL;
            return; // successfully became busy
        }

        // Before waiting on GC, we'll need a semaphore. For the moment, 
        // allocating on call stack. But could share via OS thread-local 
        // storage. TODO: profile.
        if(!wakeup_init) {
            wakeup_init = true;
            sem_init(&wakeup, 0, 0);
            t->wakeup = &wakeup;
        }

        // Attempt to enter the GC waiting state. 
        atomic_store_explicit(&(t->state), GLAS_THREAD_WAIT, memory_order_release);
        gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_seq_cst); 
        if(likely(0 != (GLAS_GC_STOP & gc))) {
            static_assert(GLAS_GC_POLL_WAIT_NSEC < (1000 * 1000 * 1000));
            static struct timespec const duration = { .tv_nsec = GLAS_GC_POLL_WAIT_NSEC };
            sem_timedwait(t->wakeup, &duration);
            // not a problem if wakeup early, simply cycle again.
        }
    } while(1);
}
LOCAL void glas_thread_become_idle(glas_thread* t) {
    assert(GLAS_THREAD_DONE != glas_thread_get_state(t));
    glas_thread_state const st = atomic_load_explicit(&(t->state), memory_order_relaxed);
    atomic_store_explicit(&(t->state), GLAS_THREAD_IDLE, memory_order_relaxed);
    if(GLAS_THREAD_BUSY == st) {
        // see if we can trigger GC
        size_t const prior_ct = atomic_fetch_sub_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
        if(1 == prior_ct) {
            glas_gc_flags const gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_relaxed);
            if(0 != (GLAS_GC_STOP & gc)) {
                sem_post(glas_rt_gc_wakeup_get());
            }
        }
    }
}
LOCAL void glas_thread_become_done(glas_thread* t) {
    glas_thread_become_idle(t);
    atomic_store_explicit(&(t->state), GLAS_THREAD_DONE, memory_order_relaxed);
}
LOCAL void glas_gc_wait_for_thread(glas_thread* t) {
    glas_thread_state ts = atomic_load_explicit(&(t->state), memory_order_relaxed);
    while(GLAS_THREAD_BUSY == ts) {
        static_assert(GLAS_GC_POLL_STOP_NSEC < (1000 * 1000 * 1000));
        static struct timespec const duration = { .tv_nsec = GLAS_GC_POLL_STOP_NSEC };
        sem_timedwait(glas_rt_gc_wakeup_get(), &duration);
        ts = atomic_load_explicit(&(t->state), memory_order_seq_cst);
    }
}
LOCAL inline bool glas_gc_is_stopped() {
    glas_gc_flags const gc = atomic_load_explicit(&glas_rt.gc.state, memory_order_relaxed);
    size_t const ct = atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_relaxed);
    return ((0 != (GLAS_GC_STOP & gc)) && (0 == ct));
}
LOCAL void glas_gc_stop_the_world() {
    // set the STOP flag
    atomic_fetch_or_explicit(&glas_rt.gc.state, GLAS_GC_STOP, memory_order_seq_cst);

    // Wait for busy threads to exit. We'll use simple polling here.
    // Single pass, because new threads are IDLE.
    for(glas_thread* t = atomic_load_explicit(&glas_rt.root.threads, memory_order_acquire);
        (NULL != t); t = t->next) 
    {
        glas_gc_wait_for_thread(t);
    }
    // Trust but verify:
    assert(glas_gc_is_stopped());
}
LOCAL void glas_gc_resume_the_world() {
    assert(glas_gc_is_stopped());
    atomic_fetch_and_explicit(&glas_rt.gc.state, ~GLAS_GC_STOP, memory_order_seq_cst);
    for(glas_thread* t = atomic_load_explicit(&glas_rt.root.threads, memory_order_acquire);
        (NULL != t); t = t->next) 
    {
        glas_thread_state const ts = atomic_load_explicit(&(t->state), memory_order_relaxed);
        if(GLAS_THREAD_WAIT == ts) {
            assert(NULL != t->wakeup);
            sem_post(t->wakeup);
        }
    }
}
LOCAL void glas_gc_remove_done_threads() {
    assert(glas_gc_is_stopped());
    // strategy: grab the entire list, modify-in-place, put it back.
    glas_thread* hd = atomic_exchange_explicit(&(glas_rt.root.threads), NULL, memory_order_acquire);
    glas_thread** cursor = &hd;
    while(NULL != *cursor) {
        glas_thread_state const ts = atomic_load_explicit(&((*cursor)->state), memory_order_relaxed);
        if(GLAS_THREAD_DONE == ts) {
            // remove item at cursor
            glas_thread* const tdone = (*cursor);
            (*cursor) = tdone->next;
            tdone->next = NULL;
            if(NULL != tdone->finalizer) {
                tdone->finalizer(tdone->self);
            }
        } else {
            // advance cursor to next item
            cursor = &((*cursor)->next);
        }
    }
    // if threads were added concurrently, move them to tail via cursor
    do {} while(!atomic_compare_exchange_weak(&(glas_rt.root.threads), cursor, hd));
}

// TBD
// - initial 'glas*' thread type
// - thread local storage and allocators
// - GC and worker threads
// - moving and marking GC



LOCAL inline glas_gc_scan* glas_gc_scan_new() {
    glas_gc_scan* result = malloc(sizeof(glas_gc_scan));
    result->fill = 0;
    return result;
}
LOCAL inline void glas_gc_scan_free(glas_gc_scan* scan) {
    free(scan);
}
LOCAL inline bool glas_gc_scan_stack_is_full(glas_gc_scan* scan) {
    return (GLAS_GC_SCAN_SIZE == scan->fill);
}
LOCAL inline bool glas_gc_scan_stack_is_empty(glas_gc_scan* scan) {
    return (0 == scan->fill);
}
LOCAL inline void glas_gc_scan_stack_push(glas_gc_scan* scan, glas_cell* data) {
    assert(likely(!glas_gc_scan_stack_is_full(scan)));
    scan->stack[(scan->fill)++] = data;
}









API void glas_i64_push(glas* g, int64_t n) {
    (void)g; (void) n;
    debug("push signed int %ld", n);
}
API void glas_i32_push(glas* g, int32_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i16_push(glas* g, int16_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i8_push(glas* g, int8_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_u64_push(glas* g, uint64_t n) {
    (void)g; (void) n;
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

    glas_page* page = glas_rt_try_alloc_page();
    glas_rt_free_page(page);

    glas* g = glas_thread_new();

    // TBD: memory tests, structured data tests, computation tests, GC tests


    glas_thread_exit(g);
    return popcount_test 
        && ctz_test
        ;
}

