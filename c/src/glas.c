// Aiming for an 'all in one file' for glas runtime.
#define _DEFAULT_SOURCE

#include <stdint.h>
#include <stdalign.h>
#include <stdatomic.h>
#include <stddef.h>
#include <stdlib.h>
#include <stdio.h>
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
static_assert((0 == (0x2F & GLAS_PAGE_CARD_COUNT)),
    "glas runtime assumes cards align to a 64-bit bitmaps");

/**
 * glas will do some pointer packing, but it isn't encoded yet.
 * So, just use a placeholder for now.
 */    
#define GLAS_VAL_UNIT NULL

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
    GLAS_TYPE_EXTREF,
    // experimental
    GLAS_TYPE_SMALL_GLOB, // interpret binary as glas object
    GLAS_TYPE_BIG_GLOB,
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
typedef struct glas_thread glas_thread; // green threads
typedef struct glas_tls glas_tls; // per OS thread
typedef struct glas_siglist glas_siglist;
typedef struct glas_gc_scan glas_gc_scan;

typedef struct glas_cell_hdr {
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    _Atomic(uint8_t) gcbits;  // for write barriers and concurrent marking 
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
            glas_cell* T;
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
            // note: the actual allocation relative to fptr is:
            //    Scan Bits <- ptr -> Cell Pointers
        } big_arr;

        struct {
            void* ptr;
            glas_refct pin;     // finalizer UNLESS pin.refct_upd is NULL.
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
            _Atomic(glas_cell*) content;
            _Atomic(glas_cell*) assoc_lhs; // associated volumes (r,_)
            glas_cell* tombstone; // weakref + stable ID; reg is finalizer
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
        } extref;

        struct {
            // (TBD)
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

    // heuristics for GC
    size_t occupancy;                   // number of cells
    glas_cell* alloc_start;             // GC snapshot of bump-pointer alloc start 
    uint8_t gen;                        // 0 - nursery, higher is older

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
 * storage) embed this structure.
 * 
 * Root fields must be edited by a BUSY thread, or in a context where at
 * least one thread is BUSY, to guard against concurrent edits by GC.
 */
struct glas_thread {
    glas_thread* next;  // global thread list

    /**
     * Always initialize to IDLE.
     */
    _Atomic(glas_thread_state) state;

    /**
     * A temporary semaphore for waiting on GC.
     * 
     * This is NULL at most times, but is set when waiting on GC,
     * e.g. sharing wakeup from thread-local storage.
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
     * Finalizer called after a DONE thread is garbage-collected.
     * May be NULL if no cleanup is needed.
     */
    void (*finalizer)(void* self);

    /** 
     * Roots specification.
     * 
     * Array of uint16_t offsets to `glas_cell*` fields, relative to 
     * `self`, such that each root is at `(((glas_cell**)self)[offset]`.
     * Terminated by UINT16_MAX.
     * 
     * This array must not be changed after a thread is initialized. And
     * ideally, this points to a statically allocated array. For dynamic
     * roots, either spill into glas heap or allocate extra glas_thread* 
     * objects to serve as rooted structs in the C heap.
     * 
     * Note: At least one glas_thread must be BUSY to edit root fields.
     * But it doesn't need to be this thread. 
     */
    uint16_t const* roots;

    /**
     * Each thread has a scan bitmap, computed based on root offsets.
     * 
     * Motive is to support snapshot at the beginning (SATB) semantics
     * for concurrent marking. Bits track for each field whether it has
     * already been scanned this mark cycle.
     */
    _Atomic(uint64_t)* scan_bitmap;
    // tentative: could track first field offset, but likely irrelevant in practice
};

struct glas {
    glas_thread gcbase;
    struct {
        glas_cell* overflow;
        glas_cell* data[32];
        size_t count;
    } stack;
    struct {
        glas_cell* overflow;
        glas_cell* data[32];
        size_t count;
    } stash;
    glas_cell* ns;
};


/**
 * Thread-local storage per OS thread.
 * 
 * We can view 'glas*' threads as green threads
 * We can also use thread-local storage to support write barriers, e.g. 
 * keeping a GC scan list per OS thread. This would reduce contention.
 * 
 * When GC is performed, the GC may decided to perform nursery collections
 * on the thread-local nursery pages.
 */
struct glas_tls {
    glas_tls *next;
    _Atomic(bool) detached; // associated thread has closed

    // bump-pointer allocator, points into a nursery
    glas_cell* nursery;
    glas_cell* nursery_end;

    /**
     * A local scan stack for concurrent marking.
     */
    glas_gc_scan* scan;

    /**
     * Semaphore for GC waits per OS thread.
     */
    sem_t wakeup;
};

/**
 * A buffer for work-stealing concurrent mark and sweep.
 */
 struct glas_gc_scan {
    glas_cell* buffer[GLAS_GC_SCAN_SIZE]; // items 0..fill-1
    _Atomic(size_t) fill;
    _Atomic(size_t) claim;
    glas_gc_scan* next;
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

static struct glas_rt {
    pthread_mutex_t mutex; // use sparingly!
    _Atomic(uint64_t) idgen;

    struct {
        pthread_key_t key;
        _Atomic(glas_tls*) list;
    } tls;

    struct {
        _Atomic(glas_heap*) heaps;
        _Atomic(glas_page*) free_pages;
        _Atomic(size_t) free_page_count;
    } alloc;

    struct {
        _Atomic(glas_thread*) threads;  // GC's view of threads
        _Atomic(glas_cell*) globals;    // lazy volume of registers
        _Atomic(glas_page*) live_pages; // may treat old-gen pages as roots
    } root;

    // TBD: 
    // - on_commit operations queues, 
    // - worker threads for opqueues, GC, lazy sparks, bgcalls
    // idea: count threads, highest number thread quits if too many,
    // e.g. compared to a configuration; configured number vs. actual.

    struct {
        _Atomic(glas_gc_flags) state;    
        _Atomic(glas_gc_scan*) scan_head;

        // enable last busy thread to signal GC 
        _Atomic(size_t) busy_threads_count;
        sem_t wakeup;

        // To support snapshot at the beginning, 'new' cells must be
        // marked as if all slots were already scanned. This is tracked
        // here in gcbits, which is updated only during stop-the-world.
        // Also determines initial scan bits for new threads and arrays.
        uint8_t gcbits; // initial gcbits for new cells
    } gc;

    glas_conf* conf; // 'immutable' via copy on write, uses mutex
} glas_rt;
static pthread_once_t glas_rt_init_once = PTHREAD_ONCE_INIT;

LOCAL void glas_rt_init_slowpath();
LOCAL inline void glas_rt_init() {
    pthread_once(&glas_rt_init_once, &glas_rt_init_slowpath);
}
LOCAL inline void glas_rt_lock() {
    glas_rt_init(); // ensure we have a mutex!
    pthread_mutex_lock(&glas_rt.mutex); 
}
LOCAL inline void glas_rt_unlock() {
    pthread_mutex_unlock(&glas_rt.mutex); 
}
LOCAL inline uint64_t glas_rt_genid() {
    return atomic_fetch_add_explicit(&glas_rt.idgen, 1, memory_order_relaxed);
}
LOCAL glas_conf* glas_conf_new() {
    glas_conf* conf = malloc(sizeof(glas_conf));
    memset(conf, 0, sizeof(glas_conf));
    atomic_init(&(conf->refct), 1);
    return conf;
}
LOCAL glas_conf* glas_conf_clone(glas_conf const* src) {
    glas_conf* conf = glas_conf_new();
    // copy all fields except refct
    conf->cb = src->cb;
    return conf;
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
LOCAL void glas_tls_detach(void* addr) {
    glas_tls* tls = addr;
    atomic_store_explicit(&(tls->detached), true, memory_order_release);
    // future GC handles further cleanup
}
API void glas_rt_tls_reset() {
    void* tls = pthread_getspecific(glas_rt.tls.key);
    if(NULL != tls) {
        pthread_setspecific(glas_rt.tls.key, NULL);
        glas_tls_detach(tls);
    }
}
LOCAL glas_tls* glas_tls_new() {
    glas_tls* tls = malloc(sizeof(glas_tls));
    memset(tls, 0, sizeof(glas_tls));
    sem_init(&(tls->wakeup),0,0);
    return tls;
}
LOCAL void glas_tls_free(glas_tls* tls) {
    sem_destroy(&(tls->wakeup));
    free(tls);
}
LOCAL glas_tls* glas_rt_tls_get_new() {
    assert(NULL == pthread_getspecific(glas_rt.tls.key));
    glas_tls* tls = glas_tls_new();
    pthread_setspecific(glas_rt.tls.key, tls);
    tls->next = atomic_load_explicit(&glas_rt.tls.list, memory_order_relaxed);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.tls.list, &(tls->next), tls));
    return tls;
}
LOCAL inline glas_tls* glas_rt_tls_get() {
    glas_tls* tls = (glas_tls*) pthread_getspecific(glas_rt.tls.key);
    return likely(NULL != tls) ? tls : glas_rt_tls_get_new();
}
LOCAL void glas_rt_init_slowpath() {
    pthread_mutex_init(&glas_rt.mutex, NULL);
    pthread_key_create(&glas_rt.tls.key, &glas_tls_detach);
    sem_init(&(glas_rt.gc.wakeup), 0, 0);
    glas_rt.conf = glas_conf_new();
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
        bitmap = atomic_fetch_or_explicit(pb, bit, memory_order_relaxed);
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
    atomic_fetch_and_explicit(&(heap->page_bitmap),~bit, memory_order_relaxed);
}


/** 
 * In the GC mark bitmaps, pre-mark bits addressing page headers. 
 * This will simplify allocation based on prior mark bits. 
 */
LOCAL void glas_page_mark_hdr_bits(_Atomic(uintptr_t)* mark_bitmap) {
    static size_t const page_header_mark_bits = sizeof(glas_page) / GLAS_CELL_SIZE; 
    static size_t const full_mark_u64s = page_header_mark_bits / 64;
    static uint64_t const partial_mark = (((uint64_t)1)<<(page_header_mark_bits % 64))-1;
    size_t ix = 0;
    for(; ix < full_mark_u64s; ++ix) {
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
    glas_page_mark_hdr_bits(page->gc_marked);
    glas_page_mark_hdr_bits(page->gc_marking);
    page->alloc_start = (glas_cell*)(((uintptr_t)addr)+sizeof(glas_page));
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
    glas_page* page = atomic_load_explicit(&glas_rt.alloc.free_pages, memory_order_acquire);
    while(NULL != page) {
        glas_page* next = page->next;
        if(atomic_compare_exchange_weak(&glas_rt.alloc.free_pages, &page, next)) {
            atomic_fetch_sub_explicit(&glas_rt.alloc.free_page_count, 1, memory_order_release);
            // reinitialize to ensure clean slate
            return glas_page_init(page->heap, page);
        }
    }
    return NULL;
}
LOCAL glas_page* glas_rt_try_alloc_page_from_heap() {
    glas_heap* const heap = atomic_load_explicit(&glas_rt.alloc.heaps, memory_order_relaxed);
    if(NULL == heap) { return NULL; }
    void* const addr = glas_heap_try_alloc_page(heap);
    if(NULL == addr) { return NULL; }
    return glas_page_init(heap, addr);
}
LOCAL bool glas_rt_try_add_heap() {
    // using a lock here to avoid case where multiple racing threads try
    // to create a heap. If that happens, I'd want to free all but one.
    bool result = false;
    glas_rt_lock();
    glas_heap* const curr_heap = atomic_load_explicit(&glas_rt.alloc.heaps, memory_order_relaxed);
    if((NULL == curr_heap) || glas_heap_is_full(curr_heap)) {
        // new heap is still needed!
        glas_heap* const new_heap = glas_heap_try_create();
        if(NULL != new_heap) {
            result = true;
            new_heap->next = curr_heap;
            atomic_store_explicit(&glas_rt.alloc.heaps, new_heap, memory_order_relaxed);
        } // else leave result as false
    } 
    glas_rt_unlock();
    return result;
}
LOCAL glas_page* glas_rt_try_alloc_page_detached() {
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
LOCAL glas_page* glas_rt_page_alloc() {
    glas_page* page = glas_rt_try_alloc_page_detached();
    if(unlikely(NULL == page)) {
        debug("runtime is out of memory!");
        abort();
    }
    page->next = atomic_load_explicit(&glas_rt.root.live_pages, memory_order_relaxed);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.root.live_pages, &(page->next), page));
    return page;
}
LOCAL void glas_rt_page_free(glas_page* const page) {
    // We'll put pages in the free list. We can try to free some heaps
    // during GC. In general, it would require moving a few sticky pages.
    // after a compacting GC.
    assert(0 == atomic_load(&(page->occupancy)));
    page->next = atomic_load_explicit(&glas_rt.alloc.free_pages, memory_order_acquire);
    do {} while(!atomic_compare_exchange_weak(&glas_rt.alloc.free_pages, &(page->next), page));
    atomic_fetch_add_explicit(&glas_rt.alloc.free_page_count, 1, memory_order_release);
}



LOCAL inline glas_thread_state glas_thread_get_state(glas_thread* t) {
    return atomic_load_explicit(&(t->state), memory_order_relaxed);
}
LOCAL void glas_thread_become_busy(glas_thread* t) {
    assert(GLAS_THREAD_DONE != glas_thread_get_state(t)); // no exiting DONE!
    assert(GLAS_THREAD_BUSY != glas_thread_get_state(t)); // no recursive BUSY! 
    do {
        // Attempt to enter the busy state, blocking GC mutation
        atomic_store_explicit(&(t->state), GLAS_THREAD_BUSY, memory_order_relaxed);
        glas_gc_flags gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_seq_cst);
        if(0 == (GLAS_GC_STOP & gc)) {
            atomic_fetch_add_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
            t->wakeup = NULL;
            return; // successfully became busy
        }

        // To wait, we need a semaphore. Sharing one per OS thread.
        t->wakeup = &(glas_rt_tls_get()->wakeup);

        // Attempt to enter the GC waiting state. (release order for wakeup)
        atomic_store_explicit(&(t->state), GLAS_THREAD_WAIT, memory_order_release);
        gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_seq_cst); 
        if(likely(0 != (GLAS_GC_STOP & gc))) {
            sem_wait(t->wakeup);
        }
    } while(1);
}
LOCAL void glas_thread_become_idle(glas_thread* t) {
    assert(GLAS_THREAD_DONE != glas_thread_get_state(t));
    glas_thread_state const prior_state = atomic_load_explicit(&(t->state), memory_order_relaxed);
    atomic_store_explicit(&(t->state), GLAS_THREAD_IDLE, memory_order_release);
    if(GLAS_THREAD_BUSY == prior_state) {
        // see if we can trigger GC
        size_t const prior_ct = atomic_fetch_sub_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
        if(1 == prior_ct) {
            glas_gc_flags const gc = atomic_load_explicit(&(glas_rt.gc.state), memory_order_relaxed);
            if(0 != (GLAS_GC_STOP & gc)) {
                sem_post(&(glas_rt.gc.wakeup));
            }
        }
    }
}
LOCAL void glas_thread_become_done(glas_thread* t) {
    glas_thread_become_idle(t);
    atomic_store_explicit(&(t->state), GLAS_THREAD_DONE, memory_order_relaxed);
}
LOCAL inline bool glas_gc_is_stopped() {
    glas_gc_flags const gc = atomic_load_explicit(&glas_rt.gc.state, memory_order_relaxed);
    size_t const ct = atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_relaxed);
    return ((0 != (GLAS_GC_STOP & gc)) && (0 == ct));
}
LOCAL void glas_gc_stop_the_world() {
    glas_rt_init(); // ensure wakeup is available
    // set STOP flag
    atomic_fetch_or_explicit(&glas_rt.gc.state, GLAS_GC_STOP, memory_order_seq_cst);
    // Wait for busy threads to exit.
    while(0 != atomic_load_explicit(&(glas_rt.gc.busy_threads_count), memory_order_acquire)) {
        sem_wait(&(glas_rt.gc.wakeup)); // last busy thread will signal.
    } 
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
LOCAL glas_thread* glas_gc_take_done_threads() {
    assert(glas_gc_is_stopped());
    glas_thread* threads_done = NULL;
    glas_thread* hd = atomic_exchange_explicit(&(glas_rt.root.threads), NULL, memory_order_acquire);
    glas_thread** cursor = &hd;
    while(NULL != (*cursor)) {
        glas_thread_state const ts = atomic_load_explicit(&((*cursor)->state), memory_order_acquire);
        if(GLAS_THREAD_DONE == ts) {
            glas_thread* const tmp = (*cursor);
            (*cursor) = tmp->next;
            tmp->next = threads_done;
            threads_done = tmp;
        } else {
            cursor = &((*cursor)->next);
        }
    }
    do {} while(!atomic_compare_exchange_weak(&(glas_rt.root.threads), cursor, hd));
    return threads_done;
}
LOCAL glas_tls* glas_gc_take_detached_tls() {
    assert(glas_gc_is_stopped());
    glas_tls* tls_detached = NULL;
    glas_tls* hd = atomic_exchange_explicit(&(glas_rt.tls.list), NULL, memory_order_acquire);
    glas_tls** cursor = &hd;
    while(NULL != (*cursor)) {
        bool const detach = atomic_load_explicit(&((*cursor)->detached), memory_order_acquire);
        if(detach) {
            glas_tls* const tmp = (*cursor);
            (*cursor) = tmp->next;
            tmp->next = tls_detached;
            tls_detached = tmp;
        } else {
            cursor = &((*cursor)->next);
        }
    }
    do {} while(!atomic_compare_exchange_weak(&(glas_rt.tls.list), cursor, hd));
    return tls_detached;
}


// TBD
// - initial 'glas*' thread type
// - thread local storage and allocators
// - GC and worker threads
// - moving and marking GC

LOCAL inline glas_gc_scan* glas_gc_scan_new() {
    glas_gc_scan* scan = malloc(sizeof(glas_gc_scan));
    atomic_init(&(scan->fill), 0);
    atomic_init(&(scan->claim), 0);
    return scan;
}
LOCAL inline void glas_gc_scan_free(glas_gc_scan* scan) {
    free(scan);
}
LOCAL inline bool glas_gc_scan_is_full(glas_gc_scan* scan) {
    return (GLAS_GC_SCAN_SIZE == atomic_load_explicit(&(scan->fill), memory_order_relaxed));
}
LOCAL inline bool glas_gc_scan_is_empty(glas_gc_scan* scan) {
    return (0 == atomic_load_explicit(&(scan->fill), memory_order_relaxed));
}
LOCAL inline void glas_gc_scan_push(glas_gc_scan* scan, glas_cell* data) {
    assert(likely(!glas_gc_scan_is_full(scan)));
    // fill is single-threaded, but we need 'store release' semantics for 
    // work stealing
    size_t fill = atomic_load_explicit(&(scan->fill), memory_order_relaxed);
    scan->buffer[fill] = data;
    atomic_store_explicit(&(scan->fill), (1 + fill), memory_order_release);
}

LOCAL inline bool glas_gc_bit_0_means_scanned() {
    return (0 == (1 & glas_rt.gc.gcbits));
}
LOCAL void glas_thread_init(glas_thread* t, void* self, void (*finalizer)(void*), uint16_t const* roots) {
    // NOTE: cannot be safely initialized while holding a busy thread
    // because this becomes a busy thread briefly to initialize. 
    assert(0 == (((uintptr_t)self) % 8));
    memset(t, 0, sizeof(glas_thread));
    t->self = self;
    t->finalizer = finalizer;
    t->roots = roots;
    atomic_init(&(t->state), GLAS_THREAD_IDLE);
    bool const no_roots = (NULL == roots) || (UINT16_MAX == *roots);
    uint16_t max_root_offset = 0;
    size_t scan_bitmap_len = 0;
    if(!no_roots) {
        assert(NULL != self);
        while(UINT16_MAX != *roots) {
            max_root_offset = ((*roots) > max_root_offset) ? (*roots) : max_root_offset;
            // don't want garbage roots visible upon attaching to runtime
            ((glas_cell**)self)[(*roots)] = GLAS_VAL_UNIT; 
        }
        scan_bitmap_len = 1 + (max_root_offset/64);
        // e.g. need 1 for max=0, still 1 for max=63, 2 for max=64
        t->scan_bitmap = malloc(sizeof(_Atomic(uint64_t)) * scan_bitmap_len);
    }
    // attach thread to runtime
    t->next = atomic_load_explicit(&(glas_rt.root.threads), memory_order_relaxed);
    do {} while(!atomic_compare_exchange_weak(&(glas_rt.root.threads), &(t->next), t));

    if(NULL != t->scan_bitmap) {
        // trivially 'scan' our unit-value roots to the correct mark phase.
        glas_thread_become_busy(t);
        uint64_t const bits = glas_gc_bit_0_means_scanned() ? 0 : ~((uint64_t) 0);
        for(size_t ix = 0; ix < scan_bitmap_len; ++ix) {
            atomic_init(t->scan_bitmap + ix, bits);
        }
        glas_thread_become_idle(t);
    }
}
LOCAL void glas_thread_finalize(glas_thread* t) {
    free(t->scan_bitmap);
    t->scan_bitmap = NULL;
    (t->finalizer)(t->self);
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

    glas_page* page = glas_rt_page_alloc();
    glas_rt_page_free(page);

    glas* g = glas_thread_new();

    // TBD: memory tests, structured data tests, computation tests, GC tests


    glas_thread_exit(g);
    return popcount_test 
        && ctz_test
        ;
}

