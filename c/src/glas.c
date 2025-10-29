/**
 *   An implementation of the glas runtime system.
 *   Copyright (C) 2025 David Barbour
 *
 *   This program is free software: you can redistribute it and/or modify
 *   it under the terms of the GNU General Public License as published by
 *   the Free Software Foundation, either version 3 of the License, or
 *   (at your option) any later version.
 *
 *   This program is distributed in the hope that it will be useful,
 *   but WITHOUT ANY WARRANTY; without even the implied warranty of
 *   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *   GNU General Public License for more details.
 *
 *   You should have received a copy of the GNU General Public License
 *   along with this program. If not, see <https://www.gnu.org/licenses/>.
 */

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
#define GLAS_HEAP_CARD_SIZE_LG2  7
#define GLAS_HEAP_PAGE_SIZE (1 << GLAS_HEAP_PAGE_SIZE_LG2)
#define GLAS_HEAP_CARD_SIZE (1 << GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_HEAP_MMAP_SIZE (GLAS_HEAP_PAGE_SIZE << 6)
#define GLAS_PAGE_CARD_COUNT (GLAS_HEAP_PAGE_SIZE >> GLAS_HEAP_CARD_SIZE_LG2)
#define GLAS_CELL_SIZE 32
#define GLAS_PAGE_CELL_COUNT (GLAS_HEAP_PAGE_SIZE / GLAS_CELL_SIZE)

#define GLAS_GC_SCAN_BUFFER_SIZE 100
#define GLAS_GC_POLL_USEC (10 * 1000)
#define GLAS_THREAD_CHECKPOINT_MAX 9
#define GLAS_STACK_MAX 32

typedef struct glas_heap glas_heap; // mmap location    
typedef struct glas_page glas_page; // aligned region
typedef struct glas_cell glas_cell; // datum in page
typedef struct glas_cell_hdr glas_cell_hdr;
typedef struct glas_roots glas_roots; // GC roots
typedef struct glas_os_thread glas_os_thread; // per OS thread
typedef struct glas_gc_scan glas_gc_scan;
typedef struct glas_wb_scan glas_wb_scan;
typedef struct glas_conf glas_conf;
typedef struct glas_stack glas_stack;


/**
 * Macros to help build GC roots specifications.
 */
#define REP2(Op,Index,...) Op(Index,##__VA_ARGS__) Op((Index+1),##__VA_ARGS__)
#define REP4(Op,Index,...) REP2(Op,Index,##__VA_ARGS__) REP2(Op,(Index+2),##__VA_ARGS__)
#define REP8(Op,Index,...) REP4(Op,Index,##__VA_ARGS__) REP4(Op,(Index+4),##__VA_ARGS__)
#define REP16(Op,Index,...) REP8(Op,Index,##__VA_ARGS__) REP8(Op,(Index+8),##__VA_ARGS__)
#define REP32(Op,Index,...) REP16(Op,Index,##__VA_ARGS__) REP16(Op,(Index+16),##__VA_ARGS__)
#define REP64(Op,Index,...) REP32(Op,Index,##__VA_ARGS__) REP32(Op,(Index+32),##__VA_ARGS__)
#define GLAS_ROOT_FIELD(struct_name, field_name)\
    (uint16_t)(offsetof(struct_name, field_name)/sizeof(glas_cell*)),
#define GLAS_ROOT_ARRAY(index, struct_name, field_name)\
    GLAS_ROOT_FIELD(struct_name, field_name[index])
#define GLAS_ROOTS_END UINT16_MAX

#define atomic_pushlist(phead, pnext, newhd)\
    do {\
        *(pnext) = atomic_load_explicit((phead), memory_order_relaxed);\
        do{}while(!atomic_compare_exchange_weak_explicit((phead), (pnext), (newhd),\
                                memory_order_release, memory_order_relaxed));\
    } while(0)

/**
 * Glas Pointer Packing
 * 
 * Very small values are encoded directly into `glas_cell*` pointers to
 * reduce allocations and improve locality. This is discriminated on the
 * lowest few alignment bits for a pointer.
 *      
 *      Last Byte       Interpretation
 *      xxxpp000        Pointer (pp reserved - must be 00 for now)
 *      xxxxxx01        Bitstring of 0..61 bits, also integers
 *      xxxxxx10        Shrub (tiny tree), 2..31 edges, at least one pair
 *      xxxxx011        Small rationals. 0..30 bit num, 1..30 bit denom (implicit '1' prefix in denom)
 *      nnn00111        Binaries of 1..7 bytes (length as 001..111)
 *      11111111        special abstract constants, e.g. thunk claimed
 * 
 * There are many ways to represent some values. To provide a canonical
 * form, shrubs (the most flexible) have lowest priority for packing. If
 * a pointer can be packed any other way, we'll favor the other way. And
 * bitstrings have highest priority, very convenient for arithmetic.
 * 
 * Aside: Still have some space for expansions
 */
static_assert(sizeof(void*)==sizeof(uint64_t));
#define GLAS_VAL_UNIT ((glas_cell*)(((uint64_t)1)<<63 | 0b01))

#define GLAS_PTR_MAX_INT ((((int64_t)1)<<61) - 1)
#define GLAS_PTR_MIN_INT (-GLAS_PTR_MAX_INT)

#define GLAS_DATA_IS_PTR(P) (0 == (((uint64_t)P) & 0x1F))
#define GLAS_DATA_IS_BITS(P) (0b01 == (((uint64_t)P) & 0b11))
#define GLAS_DATA_IS_SHRUB(P) (0b10 == (((uint64_t)P) & 0b11))
#define GLAS_DATA_IS_RATIONAL(P) (0b011 == (((uint64_t)P) & 0b111))
#define GLAS_DATA_IS_BINARY(P) (0b00111 == (((uint64_t)P) & 0b11111))
#define GLAS_DATA_IS_ABSTRACT_CONST(P) (0xFF == (((uint64_t)P) & 0xFF))

#define GLAS_DATA_BINARY_LEN(P) (0b111 & (((uint64_t)P) >> 5))

#define GLAS_ABSTRACT_CONST(N) ((glas_cell*)((((uint64_t)N)<<8)|0xFF))
/**
 * Abstract values.
 * 
 * Uniformly treated as abstract and runtime-ephemeral. 
 * 
 * - GLAS_VOID - treat as permanently sealed value.
 */
#define GLAS_VOID GLAS_ABSTRACT_CONST(0)

/**
 * Stem bits (32 bits in cells, 62 bits in ptr or stemcell)
 * 
 *  1000..0     empty
 *  a100..0     1 bit
 *  ab10..0     2 bits
 *  abcd..1     (Width-1) bits
 *  0000..0     unused
 */
#define GLAS_CELL_STEM_EMPTY (((uint32_t)1)<<31)

/**
 * Shrub encoding.
 * 
 * Shrubs are simple in concept. But heuristics to use them are awkward.
 * The motive is to avoid allocations, trading immediate CPU for space,
 * writes, and GC overheads later.
 * 
 * 2 bits per edge. 
 * - left edge: 10(Shrub)
 * - right edge: 11(Shrub)
 * - pair: 01(Shrub)00(Shrub)
 * 
 * We zero-fill a suffix, or truncate a zeroes suffix. To construct the
 * shrub efficiently, we'll want to first scan the data to determine the
 * number of shrub bits needed, short circuiting above a threshold.
 */
#define GLAS_DATA_SHRUB_BITS(P) (((uint64_t)P) & ~((uint64_t)0b11))
#define GLAS_SHRUB_STEP_MASK (((uint64_t)0b11)<<62)
#define GLAS_SHRUB_LBITS (((uint64_t)0b10)<<62)
#define GLAS_SHRUB_RBITS (((uint64_t)0b11)<<62)
#define GLAS_SHRUB_PBITS (((uint64_t)0b01)<<62)
#define GLAS_SHRUB_IS_EDGE(N) (0 != (N & GLAS_SHRUB_LBITS))
#define GLAS_SHRUB_IS_INL(N) (GLAS_SHRUB_LBITS == (N & GLAS_SHRUB_STEP_MASK))
#define GLAS_SHRUB_IS_INR(N) (GLAS_SHRUB_RBITS == (N & GLAS_SHRUB_STEP_MASK))
#define GLAS_SHRUB_IS_PAIR(N) (GLAS_SHRUB_PBITS == (N & GLAS_SHRUB_STEP_MASK))
#define GLAS_SHRUB_IS_PSEP(N) (0 == (N & GLAS_SHRUB_STEP_MASK))
#define GLAS_SHRUB_IS_UNIT(N) (0 == N)
#define GLAS_SHRUB_MKL(N) (GLAS_SHRUB_LBITS | (N >> 2))
#define GLAS_SHRUB_MKR(N) (GLAS_SHRUB_RBITS | (N >> 2))
#define GLAS_SHRUB_MKP_HD(N) (GLAS_SHRUB_PBITS | (N >> 2))
#define GLAS_SHRUB_MKP_SEP(N) (N >> 2)

/**
 * Thread state for GC coordination.
 *
 * States:
 * - Done - thread finished, GC to reclaim later
 * - Idle - not blocking GC or waiting on GC
 * - Busy - mutating heap, blocks GC busy phase
 * - Wait - suspended, waiting for GC to complete
 * 
 * Transitions:
 * - Idle->Busy|Wait - thread enter busy, depends on GC state
 * - Wait->Busy - GC trigger
 * - Busy->Idle - thread exit busy
 * - Idle|Busy->Done - set Done, never change state again
 * 
 * New threads join in idle state.
 */
typedef enum glas_os_thread_state {
    GLAS_OS_THREAD_IDLE=0,
    GLAS_OS_THREAD_BUSY,
    GLAS_OS_THREAD_WAIT,
    GLAS_OS_THREAD_DONE,
} glas_os_thread_state;

/**
 * Rough design:
 * - mutators always allocate into a nursery
 * - GC fills survivor and old gen via evacuations
 * - nursery is always compacted
 * - survivors heuristic, may age in place
 * - old gen is non-moving, alloc via mark bitmap
 */
typedef enum glas_page_gen {
    GLAS_PAGE_NURSERY=0,
    GLAS_PAGE_SURVIVOR,     
    GLAS_PAGE_OLD,          
    // ---
    GLAS_PAGE_GEN_COUNT
} glas_page_gen;

typedef enum glas_gc_level {
    GLAS_GC_SKIP = 0,
    GLAS_GC_NURSERY,
    GLAS_GC_SURVIVOR,
    GLAS_GC_OLD,
} glas_gc_level;

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
    //GLAS_TYPE_THUNK,
    //GLAS_TYPE_EXTREF,
    // experimental
    //GLAS_TYPE_SMALL_GLOB,   // interpret small_bin as glas object
    //GLAS_TYPE_BIG_GLOB,     // interpret big_bin as glas object
    //GLAS_TYPE_STEM_OF_BIN, 
    // end of list
    GLAS_TYPEID_COUNT
} glas_type_id;
static_assert(32 > GLAS_TYPEID_COUNT, 
    "glas reserves a two type bits for logical wrappers");

typedef enum glas_card_id {
    GLAS_CARD_OLD_TO_YOUNG = 0, // track refs from older to younger gens
    GLAS_CARD_FINALIZER,        // track locations of finalizers in page
    // end of list
    GLAS_CARD_IDYPECOUNT
} glas_card_id;

/**
 * Cell header.
 * 
 * type_id: glas_type_id, except the top bit marks wrapping cell in singleton list.
 * type_arg: type_id specific
 * type_aggr: lowest four bits in use:  xxxxeeal
 *    ee: two ephemerality bits
 *      00 - plain old data (~ can share via RPC)
 *      01 - database lifespan (can persist)
 *      10 - runtime lifespan (e.g. all foreign pointers)
 *      11 - transaction lifespan 
 *    a: abstract
 *      sealed or encrypted data
 *      special constants
 *    l: linear
 *      forbid copy and drop
 * gcbits: yyyyfsss
 *    y: cycle counter per gen, updated when traced
 *    f: finalizer, clear upon evacuation or processing
 *    s: once-per-slot write barrier, also update on scan
 */
struct glas_cell_hdr {
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    _Atomic(uint8_t) gcbits;  // reserved for use by GC
};

struct glas_cell {
    glas_cell_hdr hdr;
    uint32_t stemHd; // 0..31 bits
    union {
        struct { 
            uint32_t stemL; // 0..31 bits before L
            uint32_t stemR; // 0..31 bits before R
            glas_cell* L; 
            glas_cell* R; 
        } branch;
        struct {
            uint32_t stem32[4]; // full 32-bit stems, count in type arg
            glas_cell* fby;
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
            // big arrays are allocated outside the managed heap, using
            // an fptr finalizer to clean up memory. Big arrays may be
            // sliced logically. Slices may be rejoined into a big array
            // if they align exctly, recognized via the shared fptr.
            //
            // The array is immutable, which avoids some troubles with
            // tracking old-to-young pointers outside the heap. Even so,
            // we must be careful to evacuate arrays and data together.
            glas_cell** data;
            size_t len;
            glas_cell* fptr;
        } big_arr;

        struct {
            // Foreign pointers are used in big arrays, big binaries, 
            // and abstract client data.
            void* ptr;
            glas_refct pin; 
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
            _Atomic(glas_cell*) version;   // tentative!
            _Atomic(glas_cell*) assoc_lhs; // associated volumes (r,_)
            glas_cell* tombstone; // weakref + stable ID; reg is finalizer
            // Sketch:
            // - tombstone and assoc_lhs are allocated only when needed
            // - basic volume of registers as a lazy dict (load via thunks)
            // - associated volume as radix tree mapping rhs register stable
            //   IDs to rhs-sealed basic volumes, i.e. mutate assoc_lhs.
            // - GC can eliminate unreachable branches of radix tree 
            // - version may be more sophisticated than raw content, e.g. to
            //   support snapshot views of data.
        } reg;
        
        struct {
            glas_cell* target;  // GLAS_VOID if collected
            uint64_t   id;      // for hashmaps, debugging, etc.
            // id is global atomic incref; I assume 64 bits is adequate.
        } tombstone;

        struct {
            // data held externally, e.g. content-addressed storage
            glas_cell* ref;         // runtime-recognized reference
            glas_cell* tombstone;   // stable ID and weakref for caching
        } extref;

        struct {
            // (TBD)
            //  
            // the computation 'language' is indicated in type_arg.
            // the computation captures function and inputs, perhaps a
            // frozen view of relevant registers in the general case. 
            _Atomic(glas_cell*) closure;    // what to evaluate
            _Atomic(glas_cell*) result;     // final result (or GLAS_VOID)
            _Atomic(glas_cell*) claim;      // evaluating thread and waitlist
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
    // one bit per marked object
    _Atomic(uint64_t) gc_mark[GLAS_PAGE_CELL_COUNT/64];
    // cards help find finalizers, old-to-young pointers, etc.. via rough locations
    _Atomic(uint64_t) gc_card[GLAS_CARD_IDYPECOUNT][GLAS_PAGE_CARD_COUNT/64]; 

    // heuristics for GC
    uint8_t gen;
    uint16_t alloc_iter;


    glas_page *next;                    // page in linked list
    glas_heap *heap;                    // owning heap object
    uint64_t magic_word;                // used in assertions
} __attribute__((aligned(GLAS_HEAP_CARD_SIZE)));

static_assert((0 == (sizeof(glas_page) & (GLAS_HEAP_CARD_SIZE - 1))),
    "glas page header not aligned to card");
static_assert((GLAS_HEAP_PAGE_SIZE >> 6) >= sizeof(glas_page), 
    "glas page header is too large");
static_assert((0 == (GLAS_PAGE_CARD_COUNT & 0x1ff)),
    "glas page cards not nicely aligned");
static_assert((UINT16_MAX > (GLAS_PAGE_CELL_COUNT/64)),
    "glas page card needs bigger alloc iterator");

struct glas_heap {
    // each 'heap' tracks 63-64 'pages', depending on alignment
    glas_heap* next;
    void* mem_start;
    uint64_t page_bitmap;
};

/**
 * Thread-local storage per OS thread.
 * 
 * The glas API threads are essentially green threads, i.e. many may be
 * hosted per OS thread, and they may migrate between OS threads.
 */
struct glas_os_thread {
    glas_os_thread *next;

    /**
     * ID for this thread. In case we want to send a kill or similar.
     */
    pthread_t self;

    /**
     * A state for blocking on GC.
     * 
     * Busy threads may enter busy state recursively, like a recursive
     * mutex, blocking GC from proceeding.
     */
    glas_os_thread_state state;
    size_t busy_depth; 

    /**
     * Semaphore for waiting on GC.
     */
    sem_t wakeup;

    /**
     * support for GC write-barriers, allocated from nursery heap.
     */
    glas_wb_scan* wb_next;
    glas_wb_scan* wb_end;

    /**
     * thread-local bump-pointer allocator. We'll allocate a batch of
     * addresses whenever we run low. This reduces contention on the
     * shared runtime allocator.
     */
    glas_cell* alloc_next;
    glas_cell* alloc_end;
};

/**
 * Rooted glas data.
 * 
 * This represents a collection of roots as an array of uint16_t offsets
 * to `glas_cell*` fields in another structure. The fields may be edited
 * by any OS thread in the mutator state.
 */
struct glas_roots {
    glas_roots* next;       // for global roots list
    _Atomic(size_t) refct;  // usually 1 or 0, but simplifies sharing

    /**
     * Self-reference to associated structure.
     * 
     * Points to containing structure (worker thread, api thread, etc.).
     * Used by callbacks to access thread-specific state.
     */
    void* self;

    /**
     * Finalizer is called on a future GC after refct hits zero.
     */
    void (*finalizer)(void* self);

    /** 
     * Roots specification.
     * 
     * Array of uint16_t offsets to `glas_cell*` fields, relative to 
     * `self`, such that each root is at `(((glas_cell**)self)[offset]`.
     * Terminated by GLAS_ROOTS_END (UINT16_MAX).
     * 
     * These root offsets shall not change after initialization. Dynamic
     * roots are possible by allocating more `glas_root` structures or
     * spilling into the heap.
     * 
     * GC will be more efficient if roots are sorted or clustered, but 
     * it isn't a hard requirement.
     */
    uint16_t const* roots; 
    uint16_t max_offset; 
    uint16_t root_count; 

    /**
     * A scan bitmap, computed based on root offsets.
     * 
     * This supports snapshot at the beginning (SATB) semantics during
     * concurrent marking, tracking for each field whether it has been
     * scanned or not during the current GC.
     * 
     * Overhead for densely packed roots is about 1 bit per field. Will
     * be more expensive if fields are spread out.
     */
    _Atomic(uint64_t)* scan_bitmap; 
};

/**
 * A 'stem cell' is a miniature workspace for manipulating stem bits on 
 * the data stack. The motive is to reduce number of allocations. The 
 * stem buffer can overflow into the cell, or we can allocate a new cell
 * extracting some stem bits upon underflow.
 * 
 * Although mostly used for stem bits, I'll reserve a few bits for other
 * allocation-saving heuristic flags:
 * 
 * - track 'unique' cell pointers that can be safely edited in-place
 * - mark singleton list wrappers, useful for optional args or returns
 * 
 * This leaves room for 0..61 stem bits via the `abc1000..0` encoding.
 */
typedef struct glas_stemcell {
    uint64_t stem;      // 0..61 stem bits, 2 flag bits
    glas_cell* cell;
} glas_stemcell;
static_assert(sizeof(glas_stemcell) == 16);
#define GLAS_STEMCELL_STEMBITS  (~((uint64_t)0b11))
#define GLAS_STEMCELL_UNIQUE_FLAG ((uint64_t)0b01)
#define GLAS_STEMCELL_OPTVAL_FLAG ((uint64_t)0b10)
#define GLAS_STEMCELL_STEM_EMPTY  (((uint64_t)1)<<63)

/**
 * Stack and stash structure.
 * 
 * A stack provides a workspace for efficient computation. We can push,
 * pop, and perform some simple data plumbing without heap allocations.
 * The stem cells extend this to working with bitstrings, variant data,
 * and in-place edits.
 * 
 * A typed stack would be more flexible, but requires more sophisticated
 * integration with the garbage collector. We'll just use this for now. 
 */
struct glas_stack {
    glas_cell* overflow;
    size_t count; // amount of data in use
    glas_stemcell data[GLAS_STACK_MAX];
};
#define GLAS_STACK_INDEX_DATA_CELL(index, host, name)\
    GLAS_ROOT_FIELD(host, name.data[index].cell)
#define GLAS_STACK_ROOTS(host, name)\
    GLAS_ROOT_FIELD(host, name.overflow)\
    REP32(GLAS_STACK_INDEX_DATA_CELL, 0, host, name)
static_assert((32 == GLAS_STACK_MAX), "fix stack roots for new stack size!");

typedef struct glas_thread_state {
    glas_stack stack;
    glas_stack stash;
    glas_cell* ns;
    glas_cell* debug_name;
    // also needed: 
    //   register reads and writes
    //   pending on-commit ops
    //   integration with fork and detach (via on-commit?)
    glas_roots gcbase;
} glas_thread_state;

static uint16_t const glas_thread_state_offsets[] = {
    GLAS_STACK_ROOTS(glas_thread_state, stack)
    GLAS_STACK_ROOTS(glas_thread_state, stash)
    GLAS_ROOT_FIELD(glas_thread_state, ns)
    GLAS_ROOT_FIELD(glas_thread_state, debug_name)
    GLAS_ROOTS_END
};

/**
 * The primary API thread.
 * 
 * Each thread consists primarily of stack, stash, and namespace.
 * But I'll need to add a bit more to support forks, callbacks, etc..
 */
struct glas {
    glas_thread_state* state;
    GLAS_ERROR_FLAGS err;

    // support for transactions
    glas_thread_state* step_start;
    struct {
        glas_thread_state* stack[GLAS_THREAD_CHECKPOINT_MAX];
        size_t count;
    } checkpoint;
    //glas* next; // for linked list contexts
    // TBD:
    // - error signaling
    // - 
};

/**
 * buffers used by GC for processing lists of cells.
 */
struct glas_gc_scan {
    glas_cell* buffer[GLAS_GC_SCAN_BUFFER_SIZE]; // items 0..fill-1
    size_t fill;
    glas_gc_scan* next; // for global linked list
};

struct glas_wb_scan {
    // allocated in nursery for concurrent mark write barrier
    glas_cell *cell;
    struct glas_wb_scan *next;
};

typedef struct glas_alloc_l {
    glas_page* list;
    size_t count;
} glas_alloc_l;

typedef struct glas_alloc {
    // separate heaps per generation
    glas_heap*   heap;
    glas_alloc_l free;
    glas_alloc_l live;
} glas_alloc;

static struct {
    pthread_mutex_t mutex; // use sparingly!
    _Atomic(uint64_t) idgen;

    struct {
        pthread_key_t key;
        _Atomic(glas_os_thread*) list;
    } tls;

    struct {
        glas_alloc ofgen[GLAS_PAGE_GEN_COUNT];

        /**
         * Allocation from most recent nursery page.
         * 
         * OS threads will allocate in blocks, e.g. of ~16kB, until end
         * of page is reached. Then we allocate another nursery. Thus, a
         * 2MB page effectively serves a list of 128 smaller nurseries.
         */
        _Atomic(glas_cell*) freecells;

        /**
         * Locking down allocation of large objects (pages and heaps) to
         * eliminate risk allocation races, i.e. where the losing thread
         * must free the resulting memory. 
         */
        pthread_mutex_t mutex;
    } alloc;

    struct {
        _Atomic(glas_roots*) list;      // ad hoc glas_cell* slots
        _Atomic(glas_cell*) globals;    // lazily constructed, shared
        _Atomic(glas_cell*) conf;       // the configuration register
    } root;

    // TBD: 
    // - on_commit operations queues, 
    // - worker threads for opqueues, GC, lazy sparks, bgcalls
    // idea: count threads, highest number thread quits if too many,
    // e.g. compared to a configuration; configured number vs. actual.

    struct {
        /**
         * During concurrent mark phase, mutators must signal GC to scan
         * prior values for snapshot-at-the-beginning semantics. This is
         * represented by allocating a linked list in the nursery, then
         * linking it here.
         */
        _Atomic(glas_wb_scan*) wb_scan_head;

        /**
         * During concurrent mark phase, put overflow scan buffers here. 
         * Will eventually be useful with concurrent GC threads.
         */
        _Atomic(glas_gc_scan*) gc_scan_head;

        /**
         * To ensure popping from a list is safe
         */
        pthread_mutex_t scan_mutex;

        /**
         * GC waits after entering 'stop' until busy_threads_count is
         * reduced to zero. Last thread to exit signals wakeup. Threads
         * entering busy instead wait on their own semaphores.
         */
        _Atomic(size_t) busy_threads_count;
        sem_t wakeup;
        pthread_t gc_main_thread;

        /**
         * If true, stop-the-world. Blocks new busy threads.
         */
        _Atomic(bool) stopping;

        /** 
         * If true, write barriers are active, i.e. slows down state
         * updates to cooperate with GC.
         */
        bool marking;

        /** 
         * For snapshot-at-the-beginning semantics, all new cells are
         * allocated in the scanned state by setting initial gcbits.
         */
        uint8_t gcbits;

        /**
         * API guidance of GC
         */
        _Atomic(bool) signal_gc;
        _Atomic(bool) force_fullgc;
    } gc;

    struct {
        // miscellaneous statistics
        _Atomic(uint64_t) g_alloc;      // API threads
        _Atomic(uint64_t) g_free;
        _Atomic(uint64_t) g_ts_alloc;   // checkpoints, steps
        _Atomic(uint64_t) g_ts_free;
        _Atomic(uint64_t) roots_init;   // GC roots (per slot)
        _Atomic(uint64_t) roots_free;
        _Atomic(uint64_t) tls_alloc;    // per OS thread
        _Atomic(uint64_t) tls_free;
        _Atomic(uint64_t) page_alloc;   // allocator and GC
        _Atomic(uint64_t) page_accept;  
        _Atomic(uint64_t) page_free;
        _Atomic(uint64_t) heap_alloc;   // mmaps
        _Atomic(uint64_t) heap_free;
    } stat;

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
LOCAL inline void glas_rt_alloc_lock() {
    glas_rt_init();
    pthread_mutex_lock(&glas_rt.alloc.mutex);
}
LOCAL inline void glas_rt_alloc_unlock() {
    pthread_mutex_unlock(&glas_rt.alloc.mutex);
}
LOCAL inline void glas_gc_thread_scan_lock() {
    pthread_mutex_lock(&glas_rt.gc.scan_mutex);
}
LOCAL inline void glas_gc_thread_scan_unlock() {
    pthread_mutex_unlock(&glas_rt.gc.scan_mutex);
}
LOCAL inline uint64_t glas_rt_genid() {
    return atomic_fetch_add_explicit(&glas_rt.idgen, 1, memory_order_relaxed);
}
LOCAL void glas_os_thread_set_done(glas_os_thread*);
LOCAL void glas_os_thread_detach(void* addr) {
    glas_os_thread_set_done((glas_os_thread*)addr);
}
API void glas_rt_tls_reset() {
    void* addr = pthread_getspecific(glas_rt.tls.key);
    if(NULL != addr) {
        pthread_setspecific(glas_rt.tls.key, NULL);
        glas_os_thread_detach(addr);
    }
}
LOCAL glas_os_thread* glas_os_thread_create() {
    atomic_fetch_add_explicit(&glas_rt.stat.tls_alloc, 1, memory_order_relaxed);
    glas_os_thread* const t = calloc(1,sizeof(glas_os_thread));
    t->self = pthread_self();
    t->state = GLAS_OS_THREAD_IDLE;
    sem_init(&(t->wakeup),0,0);
    return t;
}
LOCAL void glas_os_thread_destroy(glas_os_thread* t) {
    atomic_fetch_add_explicit(&glas_rt.stat.tls_free, 1, memory_order_relaxed);
    sem_destroy(&(t->wakeup));
    free(t);
}
LOCAL glas_os_thread* glas_os_thread_get_slowpath() {
    assert(NULL == pthread_getspecific(glas_rt.tls.key));
    glas_os_thread* const t = glas_os_thread_create();
    pthread_setspecific(glas_rt.tls.key, t);
    atomic_pushlist(&glas_rt.tls.list, &(t->next), t);
    return t;
}
LOCAL inline glas_os_thread* glas_os_thread_get() {
    glas_os_thread* const t = (glas_os_thread*) pthread_getspecific(glas_rt.tls.key);
    return likely(NULL != t) ? t : glas_os_thread_get_slowpath();
}
LOCAL void glas_gc_thread_init();
LOCAL void glas_rt_init_slowpath() {
    pthread_mutex_init(&glas_rt.mutex, NULL);
    pthread_mutex_init(&glas_rt.alloc.mutex, NULL);
    pthread_mutex_init(&glas_rt.gc.scan_mutex, NULL);
    pthread_key_create(&glas_rt.tls.key, &glas_os_thread_detach);
    sem_init(&(glas_rt.gc.wakeup), 0, 0);
    atomic_init(&glas_rt.root.conf, GLAS_VAL_UNIT);
    atomic_init(&glas_rt.root.globals, GLAS_VOID);
    glas_gc_thread_init();
    // TBD: proper init of globals as lazy dict of reg.
    // TBD: Worker threads for on_commit.
}

API void glas_rt_gc_trigger(bool fullgc) {
    glas_rt_init();
    if(fullgc) { 
        atomic_store_explicit(&glas_rt.gc.force_fullgc, true, memory_order_relaxed); 
    }
    atomic_store_explicit(&glas_rt.gc.signal_gc, true, memory_order_release);
    sem_post(&glas_rt.gc.wakeup);
}

/** 
 * Checking my assumptions for these builtins.
 * Interaction between stdint and legacy APIs is so awkward.
 */
static_assert(sizeof(unsigned long long) == sizeof(uint64_t));
static_assert(sizeof(unsigned int) == sizeof(uint32_t));
static inline size_t popcount64(uint64_t n) { return (size_t) __builtin_popcountll(n); }
static inline size_t popcount32(uint32_t n) { return (size_t) __builtin_popcount(n); }
static inline size_t ctz64(uint64_t n) { return (size_t) __builtin_ctzll(n); }
static inline size_t ctz32(uint32_t n) { return (size_t) __builtin_ctz(n); }
static inline size_t clz64(uint64_t n) { return (size_t) __builtin_clzll(n); }
static inline size_t clz32(uint32_t n) { return (size_t) __builtin_clz(n); }

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
    // We can lose the last page per mmap due to alignment. This is
    // only a 1.6% loss of address space (not RAM) thus not an issue.
    bool const is_aligned = (glas_heap_pages_start(heap) == (heap->mem_start));
    return is_aligned ? 0 : (((uint64_t)1)<<63);
}
LOCAL inline bool glas_heap_is_empty(glas_heap* heap) {
    return (glas_heap_initial_bitmap(heap) == heap->page_bitmap);
}
LOCAL inline bool glas_heap_is_full(glas_heap* heap) {
    return (0 == ~(heap->page_bitmap));
}
LOCAL glas_heap* glas_heap_try_create() {
    atomic_fetch_add_explicit(&glas_rt.stat.heap_alloc, 1, memory_order_relaxed);
    glas_heap* heap = calloc(1,sizeof(glas_heap));
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
    heap->page_bitmap = glas_heap_initial_bitmap(heap);
    //debug("%luMB heap created at %p", (size_t)GLAS_HEAP_MMAP_SIZE>>20, heap->mem_start); 
    return heap;
}
LOCAL void glas_heap_destroy(glas_heap* heap) {
    atomic_fetch_add_explicit(&glas_rt.stat.heap_free, 1, memory_order_relaxed);
    assert(glas_heap_is_empty(heap));
    if(0 != munmap(heap->mem_start, GLAS_HEAP_MMAP_SIZE)) {
        debug("munmap failed, error %d: %s", errno, strerror(errno));
        // address-space leak, but not a halting error
    }
    free(heap);
}
LOCAL void* glas_heap_try_alloc_page(glas_heap* heap) {
    if(glas_heap_is_full(heap)) 
        return NULL;
    size_t const ix = ctz64(~(heap->page_bitmap));
    heap->page_bitmap |= ((uint64_t)1) << ix;
    void* const page = (void*)(((uintptr_t)glas_heap_pages_start(heap)) + (ix * GLAS_HEAP_PAGE_SIZE));
    if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_READ | PROT_WRITE))) {
        debug("could not mark page for read+write, error %d: %s", errno, strerror(errno));
        abort();
    }
    return page;
}
LOCAL void glas_heap_free_page(glas_heap* heap, void* page) {
    assert(glas_mem_page_ceil(page) == page);
    assert(glas_heap_includes_addr(heap, page));
    size_t const ix = (size_t)((uintptr_t)page - (uintptr_t)glas_heap_pages_start(heap)) 
                >> GLAS_HEAP_PAGE_SIZE_LG2;
    uint64_t const bit = ((uint64_t)1)<<ix;
    assert(0 != (bit & heap->page_bitmap));
    heap->page_bitmap &= ~bit;
    if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_NONE))) {
        debug("error protecting page %p from read-write, %d: %s", page, errno, strerror(errno));
        // not a halting error
    }
    if(unlikely(0 != madvise(page, GLAS_HEAP_PAGE_SIZE, MADV_DONTNEED))) {
        debug("error expunging page %p from memory, %d: %s", page, errno, strerror(errno));
        // not a halting error, but may waste some memory
    }
}

/** 
 * In the GC mark bitmaps, pre-mark bits addressing page headers. 
 */
LOCAL void glas_page_mark_hdr_bits(glas_page* page) {
    static size_t const page_header_mark_bits = sizeof(glas_page) / GLAS_CELL_SIZE; 
    static size_t const full_mark_u64s = page_header_mark_bits / 64;
    static uint64_t const partial_mark = (((uint64_t)1)<<(page_header_mark_bits % 64))-1;
    size_t ix = 0;
    for(; ix < full_mark_u64s; ++ix) {
        atomic_store_explicit(page->gc_mark + ix, ~((uint64_t)0), memory_order_relaxed);
    }
    atomic_fetch_or_explicit(page->gc_mark + ix, partial_mark, memory_order_relaxed);
}
LOCAL inline uint64_t glas_page_magic_word_by_addr(void* addr) {
    static uint64_t const prime = (uint64_t)12233355555333221ULL;
    return prime * (uint64_t)(((uintptr_t)addr)>>(GLAS_HEAP_PAGE_SIZE_LG2));
}
LOCAL void glas_page_init(glas_page* page) {
    assert(glas_mem_page_ceil(page) == page);
    memset(page, 0, sizeof(glas_page));
    glas_page_mark_hdr_bits(page);
    page->magic_word = glas_page_magic_word_by_addr(page);
}
LOCAL inline glas_page* glas_page_from_internal_addr(void* addr) {
    glas_page* const page = (glas_page*) glas_mem_page_floor(addr);
    assert(likely(glas_page_magic_word_by_addr(page) == page->magic_word));
    return page;
}
LOCAL inline void glas_page_card_mark_old_to_young(glas_page* page, size_t card) {
    _Atomic(uint64_t)* const pbitmap = page->gc_card[GLAS_CARD_OLD_TO_YOUNG] + (card/64);
    uint64_t const bit = ((uint64_t)1)<<(card%64);
    atomic_fetch_or_explicit(pbitmap, bit, memory_order_relaxed);
}
LOCAL inline void glas_page_card_mark_finalizer(glas_page* page, size_t card) {
    _Atomic(uint64_t)* const pbitmap = page->gc_card[GLAS_CARD_FINALIZER] + (card/64);
    uint64_t const bit = ((uint64_t)1)<<(card%64);
    atomic_fetch_or_explicit(pbitmap, bit, memory_order_relaxed);
}
LOCAL inline glas_page* glas_allocl_try_pop(glas_alloc_l* l) {
    if(NULL == l->list) { return NULL; }
    glas_page* const page = l->list;
    l->list = page->next;
    page->next = NULL;
    (l->count)--;
    return page;
}
LOCAL inline glas_page* glas_allocl_takeall(glas_alloc_l* l) {
    glas_page* const page = l->list;
    l->list = NULL;
    l->count = 0;
    return page;
}
LOCAL inline void glas_allocl_push(glas_alloc_l* l, glas_page* page) {
    page->next = l->list;
    l->list = page;
    (l->count)++;
}
LOCAL glas_page* glas_rt_page_prep(glas_heap* heap, glas_page* page, glas_page_gen gen) {
    assert(glas_heap_includes_addr(heap, page));
    glas_page_init(page);
    page->heap = heap;
    page->gen = gen;
    glas_allocl_push(&(glas_rt.alloc.ofgen[gen].live), page);
    return page;
}
LOCAL glas_page* glas_rt_page_alloc(glas_page_gen gen) {
    // assume single-threaded (e.g. via page lock)
    assert(likely((GLAS_PAGE_GEN_COUNT > gen) && (gen >= 0)));
    atomic_fetch_add_explicit(&glas_rt.stat.page_alloc, 1, memory_order_relaxed);
    glas_alloc* const a = glas_rt.alloc.ofgen + gen;
    glas_page* page = glas_allocl_try_pop(&(a->free));
    if(NULL != page) { return glas_rt_page_prep(page->heap, page, gen); }
    page = (NULL != a->heap) ? glas_heap_try_alloc_page(a->heap) : NULL;
    if(NULL != page) { return glas_rt_page_prep(a->heap, page, gen); }
    glas_heap* heap = glas_heap_try_create();
    if(NULL != heap) {
        heap->next = a->heap;
        a->heap = heap;
        page = glas_heap_try_alloc_page(heap);
        assert(NULL != page);
        return glas_rt_page_prep(heap, page, gen);
    }
    debug("runtime is out of memory!");
    abort();
}
LOCAL inline bool glas_os_thread_is_busy() {
    glas_os_thread* const t = pthread_getspecific(glas_rt.tls.key);
    return ((NULL != t) && (GLAS_OS_THREAD_BUSY == t->state));
}
LOCAL void glas_os_thread_alloc_reserve(glas_os_thread* t) {
    assert(likely((GLAS_OS_THREAD_BUSY == t->state)));
    static size_t const batch = 512; // 512 cells * 32-bytes per cell is 16kB
    static_assert(0 == (GLAS_PAGE_CELL_COUNT % batch));
    t->alloc_next = atomic_load_explicit(&glas_rt.alloc.freecells, memory_order_relaxed);
    do {
        glas_cell* const page_ceil = glas_mem_page_ceil(t->alloc_next);
        if(likely(t->alloc_next < page_ceil)) {
            // space remains in shared nursery page; allocate some
            t->alloc_end = t->alloc_next + batch;
            if(unlikely(t->alloc_end > page_ceil)) { t->alloc_end = page_ceil; }
            if(likely(atomic_compare_exchange_weak_explicit(&glas_rt.alloc.freecells, 
                    &(t->alloc_next), t->alloc_end, memory_order_relaxed, memory_order_relaxed))) 
            {
                return; // successfully claimed some space
            } // else retry with updated t->alloc_next
        } else {
            // nursery is fully allocated
            glas_rt_alloc_lock(); 
            glas_cell* const freecells = atomic_load_explicit(&glas_rt.alloc.freecells, memory_order_relaxed);
            if(freecells == t->alloc_next) {
                // allocate and install new nursery, then retry via loop
                glas_page* const new_nursery = glas_rt_page_alloc(GLAS_PAGE_NURSERY);
                t->alloc_next = (glas_cell*)(((uintptr_t)new_nursery) + sizeof(glas_page));
                atomic_store_explicit(&glas_rt.alloc.freecells, t->alloc_next, memory_order_relaxed);
            } else {
                // nursery allocated by another thread, use it via loop
                t->alloc_next = freecells;
            }
            glas_rt_alloc_unlock();
        }
    } while(1);
}
LOCAL inline glas_cell* glas_cell_alloc() {
    glas_os_thread* const t = glas_os_thread_get();
    if(unlikely(t->alloc_end == t->alloc_next)) {
        glas_os_thread_alloc_reserve(t);
    }
    return (t->alloc_next)++;
}
LOCAL inline void glas_cell_mark_finalizer(glas_cell* cell) {
    assert(likely(GLAS_DATA_IS_PTR(cell)));
    glas_page_card_mark_finalizer(glas_page_from_internal_addr(cell), glas_mem_card_index(cell));
    atomic_fetch_or_explicit(&(cell->hdr.gcbits), 0b1000, memory_order_relaxed);
}
LOCAL void glas_cell_binary_refct_upd(void* fptr, bool incref) {
    _Atomic(size_t)* const pRefct = fptr;
    if(incref) {
        atomic_fetch_add_explicit(pRefct, 1, memory_order_relaxed);
    } else {
        size_t const prior = atomic_fetch_sub_explicit(pRefct, 1, memory_order_relaxed);
        if(1 == prior) {
            free(fptr);
        }
    }
}
LOCAL glas_cell* glas_cell_binary_alloc(uint8_t const* data, size_t len) {
    if(7 >= len) {
        // use a packed pointer
        if(0 == len) { return GLAS_VAL_UNIT; }
        uint64_t const result 
            = (((uint64_t)(data[0]))<<56)
            #define X(N) ((N < len) ? (((uint64_t)(data[N]))<<(8*(7-N))) : 0)
            | X(1) | X(2) | X(3) | X(4) | X(5) | X(6)
            #undef X
            | ((((uint64_t)len)&0b111)<<5 | 0b00111);
        return (glas_cell*) result;
    } else if(24 >= len) {
        assert(likely(glas_os_thread_is_busy()));
        glas_cell* const cell = glas_cell_alloc();
        cell->hdr.type_id = GLAS_TYPE_SMALL_BIN;
        cell->hdr.type_arg = len;
        cell->hdr.type_aggr = 0;
        cell->stemHd = GLAS_CELL_STEM_EMPTY;
        memcpy(cell->small_bin, data, len);
        return cell;
    } else {
        assert(likely(glas_os_thread_is_busy()));
        glas_cell* const slice = glas_cell_alloc();
        glas_cell* const fptr = glas_cell_alloc();
        void* const addr = malloc(sizeof(_Atomic(size_t)) + len); // include space for refct
        atomic_init((_Atomic(size_t)*) addr, 1);
        uint8_t* const data_copy = (uint8_t*)(((uintptr_t)addr) + sizeof(_Atomic(size_t)));
        memcpy(data_copy,data,len);
        slice->hdr.type_id = GLAS_TYPE_BIG_BIN;
        slice->hdr.type_arg = 0;
        slice->hdr.type_aggr = 0;
        slice->stemHd = GLAS_CELL_STEM_EMPTY;
        slice->big_bin.data = data_copy;
        slice->big_bin.len = len;
        slice->big_bin.fptr = fptr;
        fptr->hdr.type_id = GLAS_TYPE_FOREIGN_PTR;
        fptr->hdr.type_arg = 0;
        fptr->hdr.type_aggr = 0b1010;
        fptr->stemHd = GLAS_CELL_STEM_EMPTY;
        fptr->foreign_ptr.pin.refct_upd = glas_cell_binary_refct_upd;
        fptr->foreign_ptr.pin.refct_obj = addr;
        fptr->foreign_ptr.ptr = data_copy;
        glas_cell_mark_finalizer(fptr);
        return slice;
    }
}
LOCAL inline bool glas_gc_b0scan() {
    // return true iff a 0 bit means 'scanned'
    return (0 == (1 & glas_rt.gc.gcbits));
}
LOCAL inline uint8_t glas_type_aggr_comp(uint8_t lhs, uint8_t rhs) {
    // bits: xxxxeeal
    // xxxx - reserved (zero for now)
    // ee - max ephemerality
    // a, l - abstraction, linearity - bitwise or
    uint8_t const le = (lhs & 0b1100);
    uint8_t const re = (rhs & 0b1100);
    uint8_t const ee = (le > re) ? le : re;
    uint8_t const al = (lhs|rhs)&(0b0011);
    return (ee|al);
}
LOCAL inline uint8_t glas_cell_type_aggr(glas_cell* cell) {
    return GLAS_DATA_IS_PTR(cell) ? cell->hdr.type_aggr :
           unlikely(GLAS_DATA_IS_ABSTRACT_CONST(cell)) ? 0b1010 : 
           0;
}
LOCAL uint8_t glas_cell_array_type_aggr(glas_cell** data, size_t len) {
    uint8_t result = 0;
    for(size_t ix = 0; ix < len; ++ix) {
        result = glas_type_aggr_comp(result, glas_cell_type_aggr(data[ix]));
    }
    return result;
}
LOCAL void glas_cell_array_free(void* base_ptr, bool incref) {
    // we encode as refct, but we never incref a cell array
    assert(!incref); // check that assumption
    free(base_ptr);
}
LOCAL glas_cell* glas_cell_array_alloc(glas_cell** data, size_t len) {
    // array as a list
    if(len < 4) {
        if(0 == len) { return GLAS_VAL_UNIT; }
        assert(likely(glas_os_thread_is_busy()));
        glas_cell* const cell = glas_cell_alloc();
        cell->hdr.type_id = GLAS_TYPE_SMALL_ARR;
        cell->hdr.type_arg = len;
        cell->hdr.type_aggr = glas_cell_array_type_aggr(data, len);
        cell->stemHd = GLAS_CELL_STEM_EMPTY;
        cell->small_arr[0] = data[0];
        cell->small_arr[1] = (len > 1) ? data[1] : GLAS_VOID;
        cell->small_arr[2] = (len > 2) ? data[2] : GLAS_VOID;
        return cell;
    } else {
        static_assert(sizeof(_Atomic(uint64_t)) == 8);
        static_assert(sizeof(glas_cell*) == 8);
        size_t const bitmap_count = ((len+63) / 64);
        size_t const total_size = (len + bitmap_count) * 8;
        void* const base_addr = malloc(total_size);
        glas_cell** const data_copy = ((glas_cell**)base_addr) + bitmap_count;
        memset(base_addr, (glas_gc_b0scan() ? 0 : ~0), (bitmap_count * 8));
        for(size_t ix = 0; ix < len; ++ix) {
            data_copy[ix] = data[ix];
        }
        // Need two cells, one for the fptr, other for slice
        assert(likely(glas_os_thread_is_busy()));
        glas_cell* const slice = glas_cell_alloc();
        glas_cell* const fptr = glas_cell_alloc();
        slice->hdr.type_id = GLAS_TYPE_BIG_ARR;
        slice->hdr.type_arg = 0;
        slice->hdr.type_aggr = glas_cell_array_type_aggr(data, len);
        slice->stemHd = GLAS_CELL_STEM_EMPTY;
        slice->big_arr.data = data_copy;
        slice->big_arr.len = len;
        slice->big_arr.fptr = fptr;
        fptr->hdr.type_id = GLAS_TYPE_FOREIGN_PTR;
        fptr->hdr.type_arg = 0;
        fptr->hdr.type_aggr = 0b1010; 
        fptr->stemHd = GLAS_CELL_STEM_EMPTY;
        fptr->foreign_ptr.pin.refct_upd = glas_cell_array_free;
        fptr->foreign_ptr.pin.refct_obj = base_addr;
        fptr->foreign_ptr.ptr = data_copy;
        glas_cell_mark_finalizer(fptr);
        return slice;
    }
}

LOCAL void glas_os_thread_force_enter_busy(glas_os_thread* t) {
    t->state = GLAS_OS_THREAD_WAIT; // note: GC may immediately signal wakeup.
    do {
        // GC doesn't wait on individual threads, only the number of busy threads.
        atomic_fetch_add_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
        if(!atomic_load_explicit(&(glas_rt.gc.stopping), memory_order_seq_cst)) {
            // set state to busy
            t->state = GLAS_OS_THREAD_BUSY;
            t->busy_depth = 1;
            // drain the semaphore of missed signals (if any)
            do {} while(0 == sem_trywait(&(t->wakeup)));
            return; // successfully entered busy
        }
        // otherwise, wait for GC wakeup. We'll check for GC once more to
        // avoid a missed wakeup race condition.
        atomic_fetch_sub_explicit(&(glas_rt.gc.busy_threads_count), 1, memory_order_relaxed);
        if(likely(atomic_load_explicit(&(glas_rt.gc.stopping), memory_order_relaxed))) {
            sem_wait(&(t->wakeup));
        }
    } while(1);
}
LOCAL void glas_os_thread_enter_busy() {
    glas_os_thread* const t = glas_os_thread_get();
    if(GLAS_OS_THREAD_BUSY == t->state) {
        (t->busy_depth)++; // recursive BUSY state
        return;
    }
    assert(likely(GLAS_OS_THREAD_IDLE == t->state));
    glas_os_thread_force_enter_busy(t);
}
LOCAL void glas_gc_busy_thread_decrement() {
    size_t const prior_busy_count = atomic_fetch_sub_explicit(&glas_rt.gc.busy_threads_count, 1, memory_order_release);
    if((1 == prior_busy_count) && (atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed))) {
        // The last thread to go IDLE while GC is STOPPING must awaken GC.
        sem_post(&glas_rt.gc.wakeup);
    }
}
LOCAL void glas_os_thread_force_exit_busy(glas_os_thread* const t) {
    assert(likely(GLAS_OS_THREAD_BUSY == t->state));
    t->busy_depth = 0;
    t->state = GLAS_OS_THREAD_IDLE;
    glas_gc_busy_thread_decrement();
}
LOCAL void glas_os_thread_exit_busy() {
    glas_os_thread* const t = glas_os_thread_get();
    if(t->busy_depth > 1) {
        (t->busy_depth)--;
        return;
    }
    glas_os_thread_force_exit_busy(t);
}
LOCAL void glas_os_thread_gc_safepoint_slowpath() {
    glas_os_thread* const t = glas_os_thread_get();
    size_t const saved_busy_depth = t->busy_depth;
    glas_os_thread_force_exit_busy(t);
    glas_os_thread_force_enter_busy(t);
    t->busy_depth = saved_busy_depth;
}
/**
 * A gc safepoint should be set within unbounded computations, and must
 * be set where it's safe to move data for compacting collections. That
 * is, if non-root C fields are holding onto cell pointers, they must be
 * considered invalid after the safepoint.
 */
LOCAL inline void glas_os_thread_gc_safepoint() {
    if(atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed)) {
        glas_os_thread_gc_safepoint_slowpath();
    }
}

LOCAL void glas_os_thread_set_done(glas_os_thread* const t) {
    if(GLAS_OS_THREAD_BUSY == t->state) {
        debug("glas thread canceled while busy");
        glas_os_thread_force_exit_busy(t);
    }
    assert(GLAS_OS_THREAD_IDLE == t->state);
    t->state = GLAS_OS_THREAD_DONE;
    // what else to do here?
    // - release any thunks claimed by this thread?
}
LOCAL inline bool glas_gc_is_stopped() {
    bool const stopping = atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed);
    size_t const busy_threads = atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_relaxed);
    return (stopping && (0 == busy_threads));
}
LOCAL void glas_gc_stop_the_world() {
    assert(!glas_gc_is_stopped());
    atomic_store_explicit(&glas_rt.gc.stopping, true, memory_order_seq_cst);
    while(0 != atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_acquire)) {
        sem_wait(&glas_rt.gc.wakeup);
    }
}
LOCAL void glas_gc_resume_the_world() {
    assert(glas_gc_is_stopped());
    atomic_store_explicit(&glas_rt.gc.stopping, false, memory_order_release);
    for(glas_os_thread* t = atomic_load_explicit(&glas_rt.tls.list, memory_order_acquire);
        (NULL != t); t = t->next) 
    {
        if(GLAS_OS_THREAD_WAIT == t->state) {
            sem_post(&(t->wakeup));
        }
    }
}
LOCAL glas_os_thread* glas_gc_extract_done_threads() {
    assert(glas_gc_is_stopped());
    glas_os_thread* tdone = NULL;
    glas_os_thread* tkeep = atomic_exchange_explicit(&glas_rt.tls.list, NULL, memory_order_acquire);
    glas_os_thread** cursor = &tkeep;
    while(NULL != (*cursor)) {
        if(GLAS_OS_THREAD_DONE == (*cursor)->state) {
            glas_os_thread* const t = (*cursor);
            (*cursor) = (*cursor)->next;
            t->next = tdone;
            tdone = t;
        } else {
            cursor = &((*cursor)->next);
        }
    }
    atomic_pushlist(&glas_rt.tls.list, cursor, tkeep);
    return tdone;
}
LOCAL glas_roots* glas_gc_extract_detached_roots() {
    assert(glas_gc_is_stopped());
    glas_roots* rdetached = NULL;
    glas_roots* rkeep = atomic_exchange_explicit(&glas_rt.root.list, NULL, memory_order_acquire);
    glas_roots** cursor = &rkeep;
    while(NULL != (*cursor)) {
        if(0 == atomic_load_explicit(&((*cursor)->refct), memory_order_relaxed)) {
            glas_roots* const r = (*cursor);
            (*cursor) = (*cursor)->next;
            r->next = rdetached;
            rdetached = r;
        } else {
            cursor = &((*cursor)->next);
        }
    }
    atomic_pushlist(&glas_rt.root.list, cursor, rkeep);
    return rdetached;
}

// TBD
// - debug view of cells
// - initial 'glas*' thread type
// - thread local storage and allocators
// - GC and worker threads
// - moving and marking GC

LOCAL void glas_roots_init(glas_roots* r, void* self, void (*finalizer)(void*), uint16_t const* roots) {
    assert(NULL != self);
    r->self = self;
    r->finalizer = finalizer;
    r->roots = roots;
    atomic_init(&(r->refct), 1);
    r->root_count = 0;
    r->max_offset = 0;
    for( ; (GLAS_ROOTS_END != (*roots)); ++roots ) {
        ((glas_cell**)self)[(*roots)] = GLAS_VOID;
        if((*roots) > r->max_offset) {
            r->max_offset = (*roots);
        }
        (r->root_count)++;
        assert((UINT16_MAX >= r->root_count) && "missing a sentinel");
    }
    atomic_fetch_add_explicit(&glas_rt.stat.roots_init, r->root_count, memory_order_relaxed);

    // bitmap covers from 'self' to 'max_offset' inclusive
    size_t const bitmap_len = 8 * (1 + ((r->max_offset)/64));
    r->scan_bitmap = malloc(bitmap_len);

    // 'scan' roots while busy to lock down scan bit
    // also add to roots list while GC is blocked 
    glas_os_thread_enter_busy();
    memset(r->scan_bitmap, (glas_gc_b0scan() ? 0 : ~0), bitmap_len); // mark roots scanned
    atomic_pushlist(&glas_rt.root.list, &(r->next), r);
    glas_os_thread_exit_busy();
}
LOCAL void glas_roots_finalize(glas_roots* r) {
    atomic_fetch_add_explicit(&glas_rt.stat.roots_free, r->root_count, memory_order_relaxed);
    free(r->scan_bitmap);
    r->scan_bitmap = NULL;
    r->next = NULL;
    if(NULL != (r->finalizer)) {
        (r->finalizer)(r->self);
    }
}
LOCAL inline void glas_roots_incref(glas_roots* r) {
    atomic_fetch_add_explicit(&(r->refct), 1, memory_order_relaxed);
}
LOCAL inline void glas_roots_decref(glas_roots* r) {
    atomic_fetch_sub_explicit(&(r->refct), 1, memory_order_relaxed);
    // if refct is 0, finalize later during GC stop 
}
LOCAL bool glas_gc_claim_roots_slot(glas_roots* roots, glas_cell** slot) {
    glas_cell** const base = (glas_cell**) roots->self;
    assert(likely((slot >= base) && ((base+(roots->max_offset)) >= slot)));
    size_t const slot_ix = (size_t) (slot - base);
    uint64_t const bit = ((uint64_t)1) << (slot_ix % 64);
    _Atomic(uint64_t)* const pbitmap = roots->scan_bitmap + (slot_ix / 64);
    if(glas_gc_b0scan()) {
        uint64_t const prior = atomic_fetch_and_explicit(pbitmap, ~bit, memory_order_release);
        return (0 != (prior & bit));
    } else {
        uint64_t const prior = atomic_fetch_or_explicit(pbitmap, bit, memory_order_release);
        return (0 == (prior & bit));
    }
}
LOCAL bool glas_gc_claim_cell_slot(glas_cell* reg, glas_cell** slot) {
    assert(likely(GLAS_DATA_IS_PTR(reg)));
    glas_cell** const base = reg->small_arr;
    assert(likely((slot >= base) && ((base + 3) > slot)));
    size_t const ix = (size_t)(slot - base);
    uint8_t const bit = ((uint8_t)1)<<ix;
    if(glas_gc_b0scan()) {
        uint8_t const prior = atomic_fetch_and_explicit(&(reg->hdr.gcbits), ~bit, memory_order_release);
        return (0 != (prior & bit));
    } else {
        uint8_t const prior = atomic_fetch_or_explicit(&(reg->hdr.gcbits), bit, memory_order_release);
        return (0 == (prior & bit));
    }
}
LOCAL bool glas_gc_try_cell_mark(glas_cell* cell) {
    glas_page* const page = glas_page_from_internal_addr(cell);
    size_t const ix = cell - ((glas_cell*)page);
    _Atomic(uint64_t)* const pbitmap = page->gc_mark + (ix / 64);
    uint64_t const bit = ((uint64_t)1) << (ix % 64);
    uint64_t const prior_bitmap = atomic_fetch_or_explicit(pbitmap, bit, memory_order_relaxed);
    return (0 == (bit & prior_bitmap));
}
LOCAL void glas_os_thread_wb_reserve(glas_os_thread* t) {
    if(t->alloc_end == t->alloc_next) {
        glas_os_thread_alloc_reserve(t);
    }
    // take space from nursery, will clear up on next GC cycle.
    static size_t const batch = 32;
    static_assert(0 == ((batch * sizeof(glas_wb_scan)) % sizeof(glas_cell)));
    t->wb_next = (glas_wb_scan*) t->alloc_next;
    t->wb_end = t->wb_next + batch;
    if(t->wb_end > (glas_wb_scan*) t->alloc_end) {
        t->wb_end = (glas_wb_scan*) t->alloc_end;
    }
    t->alloc_next = (glas_cell*) t->wb_end;
}
LOCAL void glas_wb_scan_push(glas_cell* cell) {
    glas_os_thread* const t = glas_os_thread_get();
    if(t->wb_end == t->wb_next) {
        glas_os_thread_wb_reserve(t);
    }
    glas_wb_scan* const scan = (t->wb_next)++;
    scan->cell = cell;
    atomic_pushlist(&glas_rt.gc.wb_scan_head, &(scan->next), scan);
}
LOCAL inline void glas_wb_sched_scan(glas_cell* cell) {
    // Filter for cell pointers that aren't already marked.
    // Alternatively, I could defer marking and push duplicate scans.
    if(GLAS_DATA_IS_PTR(cell) && glas_gc_try_cell_mark(cell)) {
        glas_wb_scan_push(cell);
    }
}
LOCAL void glas_roots_slot_write(glas_roots* roots, glas_cell** slot, glas_cell* new_val) {
    if(glas_rt.gc.marking) {
        glas_cell* const prior_val = (*slot);
        if(glas_gc_claim_roots_slot(roots, slot)) {
            glas_wb_sched_scan(prior_val);
        }
    }
    (*slot) = new_val;
}
LOCAL void glas_cell_slot_write(glas_cell* dst, glas_cell** slot, glas_cell* new_val) {
    if(glas_rt.gc.marking) {
        glas_cell* const prior_val = (*slot);
        if(glas_gc_claim_cell_slot(dst, slot)) {
            glas_wb_sched_scan(prior_val);
        }
    }
    if(GLAS_DATA_IS_PTR(new_val)) { // track old-to-young refs 
        glas_page* const dst_page = glas_page_from_internal_addr(dst);
        glas_page* const val_page = glas_page_from_internal_addr(new_val);
        if(dst_page->gen > val_page->gen) {
            glas_page_card_mark_old_to_young(dst_page, glas_mem_card_index(dst));
        }
    }
    (*slot) = new_val;
}

LOCAL glas_gc_level glas_gc_decide_level() {
    if(atomic_exchange_explicit(&glas_rt.gc.force_fullgc, false, memory_order_relaxed)) {
        // API request for full GC
        return GLAS_GC_OLD;
    }

    // full GC every time, for now
    return GLAS_GC_OLD; 
}

LOCAL glas_gc_scan* glas_gc_scan_new() {
    glas_gc_scan* scan = malloc(sizeof(glas_gc_scan));
    scan->fill = 0;
    scan->next = NULL;
    return scan;
}
LOCAL void glas_gc_scan_free(glas_gc_scan* scan) {
    while(NULL != scan) {
        glas_gc_scan* const tmp = scan;
        scan = scan->next;
        assert(likely(0 == tmp->fill));
        free(tmp);
    }
}
LOCAL void glas_gc_scan_grow(glas_gc_scan** scan_head) {
    // We'll rotate up to two buffers locally to avoid touching the 
    // global shared list at the overflow boundary 
    assert(likely(GLAS_GC_SCAN_BUFFER_SIZE == (*scan_head)->fill));
    glas_gc_scan* scan = (*scan_head)->next;
    (*scan_head)->next = NULL;
    if(NULL == scan) {
        scan = glas_gc_scan_new();
    } else {
        assert(likely(NULL == scan->next)); // at most two buffers
        if(0 == scan->fill) {
            // recycle empty buffer, a no-op here
        } else {
            // two full buffers, push one to global list for work sharing
            assert(likely(GLAS_GC_SCAN_BUFFER_SIZE == scan->fill));
            atomic_pushlist(&glas_rt.gc.gc_scan_head, &(scan->next), scan);
            scan = glas_gc_scan_new();
        }
    }
    scan->next = (*scan_head);
    (*scan_head) = scan;
}
LOCAL inline void glas_gc_scan_push(glas_gc_scan** scan, glas_cell* data) {
    if(GLAS_GC_SCAN_BUFFER_SIZE == (*scan)->fill) {
        glas_gc_scan_grow(scan);
    }
    (*scan)->buffer[((*scan)->fill)++] = data;
}

LOCAL void glas_gc_thread_scan_sched(glas_gc_scan** scan, glas_cell* cell);
LOCAL void glas_gc_thread_scan_cell(glas_gc_scan** scan, glas_cell* cell);
LOCAL void glas_gc_thread_scan_array(glas_gc_scan** scan, void* fptr, glas_cell** data, size_t len);
LOCAL void glas_gc_thread_scan_roots(glas_gc_scan** scan, glas_roots* r);

LOCAL void glas_gc_thread_scan_seek(glas_gc_scan** scan_head) {
    assert(likely(0 == (*scan_head)->fill));

    glas_gc_scan* scan = (*scan_head)->next;
    (*scan_head)->next = NULL;
    if(NULL != scan) {
        assert(likely(NULL == scan->next));
        if(0 == scan->fill) {
            // two empty buffers, free one
            glas_gc_scan_free(scan);
            scan = NULL;
        } else {
            // rotate to local full buffer
            assert(likely(GLAS_GC_SCAN_BUFFER_SIZE == scan->fill));
            scan->next = (*scan_head);
            (*scan_head) = scan;
            return;
        }
    }

    // load from shared worklists if possible.
    glas_gc_thread_scan_lock(); // lock for ABA safety
    scan = atomic_load_explicit(&glas_rt.gc.gc_scan_head, memory_order_acquire);
    do {} while((NULL != scan) && !atomic_compare_exchange_weak_explicit(&glas_rt.gc.gc_scan_head, 
            &scan, scan->next, memory_order_relaxed, memory_order_acquire));
    glas_gc_thread_scan_unlock();

    if(NULL != scan) {
        assert(likely(GLAS_GC_SCAN_BUFFER_SIZE == scan->fill));
        scan->next = (*scan_head);
        (*scan_head) = scan;
        return;
    }

    // last choice is write-barrier scans. These are not recycled until
    // next GC cycle, so no ABA risk. load up half buffer, if possible. 
    static size_t const fill_goal = GLAS_GC_SCAN_BUFFER_SIZE / 2; 
    glas_wb_scan* wb = atomic_load_explicit(&glas_rt.gc.wb_scan_head, memory_order_acquire);
    while((NULL != wb) && ((*scan_head)->fill < fill_goal)) {
        if(atomic_compare_exchange_weak_explicit(&glas_rt.gc.wb_scan_head, 
                &wb, wb->next, memory_order_relaxed, memory_order_acquire)) 
        {
            (*scan_head)->buffer[((*scan_head)->fill)++] = wb->cell;
        }
    }
}
LOCAL void glas_gc_thread_scan_drain(glas_gc_scan** scan) {
    // process all available items before returning.
    do {
        while(0 != (*scan)->fill) {
            glas_gc_thread_scan_cell(scan, (*scan)->buffer[--((*scan)->fill)]);
        }
        glas_gc_thread_scan_seek(scan);
    } while(0 != (*scan)->fill);
}
LOCAL inline void glas_gc_thread_scan_sched(glas_gc_scan** scan, glas_cell* cell) {
    if(GLAS_DATA_IS_PTR(cell) && glas_gc_try_cell_mark(cell)) {
        glas_gc_scan_push(scan, cell);
    }
}
LOCAL void glas_gc_thread_scan_roots(glas_gc_scan** scan, glas_roots* r) {
    glas_cell** const base = (glas_cell**) r->self;

    size_t root_ix = 0;
    while(root_ix < r->root_count) {
        // We'll claim many roots at once that share a bitmap.
        size_t const bitmap_ix = (r->roots[root_ix]) / 64;
        glas_cell* snapshot[64]; // up to 64 roots
        uint64_t bitmask = 0;
        size_t count = 0;
        while((root_ix + count) < r->root_count) {
            uint16_t const offset = r->roots[root_ix + count];
            if(bitmap_ix != (offset/64)) {
                break;
            }
            snapshot[count] = base[offset]; 
            bitmask |= (((uint64_t)1) << (offset % 64));
            count++;
        }
        // atomic claim for bitmap 
        uint64_t claimed;
        if(glas_gc_b0scan()) { // 0 bit means 'scanned' this phase.
            uint64_t const prior = atomic_fetch_and_explicit((r->scan_bitmap + bitmap_ix), ~bitmask, memory_order_release);
            claimed = bitmask & prior; 
        } else {
            uint64_t const prior = atomic_fetch_or_explicit((r->scan_bitmap + bitmap_ix), bitmask, memory_order_release);
            claimed = bitmask & ~prior;
        }
        // mark our claim
        for(size_t j = 0; j < count; ++j) {
            uint16_t offset = r->roots[root_ix + j];
            if(0 != (claimed & (((uint64_t)1) << (offset % 64)))) {
                glas_gc_thread_scan_sched(scan, snapshot[j]);
            }
        }
        root_ix += count;
        glas_gc_thread_scan_drain(scan);
        assert(likely((count > 0) && (0 == (*scan)->fill)));
    }
}
LOCAL void glas_gc_thread_scan_cell(glas_gc_scan** scan, glas_cell* cell) {
    (void)cell; (void)scan;
    debug("todo: GC claims a cell slot");
    
}
LOCAL void* glas_gc_main_thread(void* arg) {
    (void)arg; // unused
    do {
        // periodic loop, can trigger early via wakeup; skip sleep if request is pending
        if(!atomic_exchange_explicit(&glas_rt.gc.signal_gc, false, memory_order_relaxed)) {
            static struct timespec const gc_poll_period = { .tv_nsec = (GLAS_GC_POLL_USEC * 1000) };
            sem_timedwait(&glas_rt.gc.wakeup, &gc_poll_period);
        }
        do {} while(0 == sem_trywait(&glas_rt.gc.wakeup)); // drain extra wakeups
        glas_gc_level const gclevel = glas_gc_decide_level();
        if(GLAS_GC_SKIP == gclevel) { 
            // not enough to GC; try again later
            continue; 
        }
        // check some assumptions
        assert(!glas_os_thread_is_busy() &&
            !atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed) &&
            !glas_rt.gc.marking &&
            (NULL == atomic_load_explicit(&glas_rt.gc.gc_scan_head, memory_order_relaxed)) &&
            (NULL == atomic_load_explicit(&glas_rt.gc.wb_scan_head, memory_order_relaxed)));

        glas_gc_stop_the_world(); // Block mutations briefly.
        // garbage outside the heaps
        glas_os_thread* tdone = glas_gc_extract_done_threads();
        glas_roots* rdetached = glas_gc_extract_detached_roots();

        // grab pages that we might compact
        glas_rt_alloc_lock();
        glas_page* nurseries = glas_allocl_takeall(&glas_rt.alloc.ofgen[GLAS_PAGE_NURSERY].live);
        glas_page* survivors = (gclevel < GLAS_GC_SURVIVOR) ? NULL : 
            glas_allocl_takeall(&glas_rt.alloc.ofgen[GLAS_PAGE_SURVIVOR].live);
        glas_rt_alloc_unlock();

        // force threads to switch to fresh nurseries
        atomic_store_explicit(&glas_rt.alloc.freecells, 0, memory_order_relaxed);
        for(glas_os_thread* t = atomic_load_explicit(&glas_rt.tls.list, memory_order_acquire); 
            (NULL != t); t = t->next) 
        {
            t->wb_next = t->wb_end;
            t->alloc_next = t->alloc_end;
        }
        // flip scan bits and activate write barrier
        glas_rt.gc.gcbits = glas_gc_b0scan() ? 0b111 : 0; 
        glas_rt.gc.marking = true;

        // snapshot of roots
        glas_cell* conf = atomic_load_explicit(&glas_rt.root.conf, memory_order_relaxed);
        glas_cell* globals = atomic_load_explicit(&glas_rt.root.globals, memory_order_relaxed);
        glas_roots* r = atomic_load_explicit(&glas_rt.root.list, memory_order_acquire);
        glas_gc_resume_the_world(); // GC thread is no longer blocking mutators.

        // clear external garbage
        while(NULL != tdone) {
            glas_os_thread* const tmp = tdone;
            tdone = tdone->next;
            glas_os_thread_destroy(tmp);
        }
        while(NULL != rdetached) {
            glas_roots* const tmp = rdetached;
            rdetached = rdetached->next;
            glas_roots_finalize(tmp);
        }

        // mark everything, could try worker threads here
        glas_gc_scan* scan = glas_gc_scan_new();
        glas_gc_thread_scan_sched(&scan, conf);
        glas_gc_thread_scan_sched(&scan, globals);
        glas_gc_thread_scan_drain(&scan);
        while(NULL != r) {
            glas_gc_thread_scan_roots(&scan, r);
            r = r->next;
        }
        glas_gc_thread_scan_drain(&scan);

        glas_gc_stop_the_world();
        glas_gc_thread_scan_drain(&scan); // final drain while stopped (likely empty)


        glas_gc_resume_the_world();
        glas_gc_scan_free(scan);

 

    } while(1);
    return NULL;
}
LOCAL void glas_gc_thread_init() {
    // Create main GC thread here. It may create workers internally.
    pthread_attr_t gc_thread_attr;
    pthread_attr_init(&gc_thread_attr);
    pthread_attr_setscope(&gc_thread_attr, PTHREAD_SCOPE_PROCESS);
    pthread_attr_setdetachstate(&gc_thread_attr, PTHREAD_CREATE_DETACHED);
    pthread_create(&glas_rt.gc.gc_main_thread, &gc_thread_attr, &glas_gc_main_thread, NULL);
    pthread_attr_destroy(&gc_thread_attr);
}





LOCAL void glas_thread_state_free(void* addr) {
    atomic_fetch_add_explicit(&glas_rt.stat.g_ts_free, 1, memory_order_relaxed);
    free(addr); 
}
LOCAL inline glas_thread_state* glas_thread_state_new() {
    atomic_fetch_add_explicit(&glas_rt.stat.g_ts_alloc, 1, memory_order_relaxed);
    glas_thread_state* const ts = malloc(sizeof(glas_thread_state));
    glas_roots_init(&(ts->gcbase), ts, glas_thread_state_free, glas_thread_state_offsets);
    ts->debug_name = GLAS_VAL_UNIT;
    ts->ns = GLAS_VAL_UNIT;
    return ts;
}
LOCAL void glas_stack_copy(glas_stack* dst, glas_stack* src) {
    // in most cases, stack or stash will be near-empty, so only copy
    // what is needed. Also need to clear 'unique' flags in src because
    // now we have a copy. (Thus 'src' cannot be const.)
    dst->count = src->count;
    for(size_t ix = 0; ix < src->count; ++ix) {
        src->data[ix].stem &= ~(GLAS_STEMCELL_UNIQUE_FLAG);
        dst->data[ix] = src->data[ix];
    }
}
LOCAL glas_thread_state* glas_thread_state_clone(glas_thread_state* ts) {
    glas_os_thread_enter_busy();
    // allocate and build within GC cycle to avoid write barriers. 
    glas_thread_state* const clone = glas_thread_state_new();
    glas_stack_copy(&(clone->stack), &(ts->stack));
    glas_stack_copy(&(clone->stash), &(ts->stash));
    clone->ns = ts->ns;
    clone->debug_name = ts->debug_name;
    glas_os_thread_exit_busy();
    return clone;
}
LOCAL inline void glas_thread_state_incref(glas_thread_state* ts) {
    glas_roots_incref(&(ts->gcbase));
}
LOCAL inline void glas_thread_state_decref(glas_thread_state* ts) {
    glas_roots_decref(&(ts->gcbase));
}
LOCAL void glas_checkpoints_clear(glas* g) {
    for(size_t ix = 0; ix < g->checkpoint.count; ++ix) {
        glas_thread_state_decref(g->checkpoint.stack[ix]);
    }
    g->checkpoint.count = 0;
}
LOCAL void glas_checkpoints_reset(glas* const g) {
    assert(likely(NULL != g->step_start));
    glas_checkpoints_clear(g);
    g->checkpoint.count = 1;
    g->checkpoint.stack[0] = g->step_start;
    glas_thread_state_incref(g->step_start);
}
API void glas_step_abort(glas* g) {
    glas_thread_state_decref(g->state);
    g->state = glas_thread_state_clone(g->step_start);
    glas_thread_state_incref(g->step_start);
    glas_checkpoints_reset(g);
    g->err = 0;
    debug("TODO: run on_abort handlers");
}
API bool glas_step_commit(glas* g) {
    debug("TODO: commit register updates and on_commit writes");
    // TODO: atomically detect conflicts; commit writes and on_commits
    // Viable approaches:
    //  - global commit mutex (easiest)
    //  - global commit log, processed in order, with feedback
    // In practice, the commit thread must wait on other commits either
    // way, so I'm leaning towards a simple commit mutex.
    if(0 != g->err) { 
        return false; 
    }
    glas_thread_state_decref(g->step_start);
    g->step_start = glas_thread_state_clone(g->state);
    glas_checkpoints_reset(g);
    return true;
}
API glas* glas_thread_new() {
    atomic_fetch_add_explicit(&glas_rt.stat.g_alloc, 1, memory_order_relaxed);
    glas* const g = calloc(1,sizeof(glas));
    g->state = glas_thread_state_new();
    g->step_start = glas_thread_state_clone(g->state);
    glas_checkpoints_reset(g);
    return g;
}
API void glas_thread_exit(glas* g) {
    atomic_fetch_add_explicit(&glas_rt.stat.g_free, 1, memory_order_relaxed);
    glas_step_abort(g);
    glas_checkpoints_clear(g);
    glas_thread_state_decref(g->state);
    glas_thread_state_decref(g->step_start);
    free(g);
}

API void glas_errors_write(glas* g, GLAS_ERROR_FLAGS err) {
    g->err = err | g->err;
}

LOCAL glas_cell* glas_data_list_append(glas_cell* lhs, glas_cell* rhs) {
    if(GLAS_VAL_UNIT == lhs) { return rhs; }
    if(GLAS_VAL_UNIT == rhs) { return lhs; }
    debug("todo: list append");
    return GLAS_VOID;
}

LOCAL void glas_thread_stack_prep_slowpath(glas* g, uint8_t read, uint8_t reserve) {
    assert(likely(GLAS_STACK_MAX > (read + reserve)));
    (void)g; (void)read; (void)reserve;
    glas_os_thread_enter_busy();
    glas_os_thread_exit_busy();
    // this is where we handle overflow 
    debug("todo: thread stack prep!");
    abort();
}

/**
 * Prepare stack with inputs (from overflow) and reserve space.
 * - read: how many inputs we will observe below stack pointer
 * - reserve: how much free space we want above stack pointer
 */
LOCAL inline void glas_thread_stack_prep(glas* g, uint8_t read, uint8_t reserve) {
    size_t const ct = g->state->stack.count;
    if(unlikely((read > ct) || (reserve > (GLAS_STACK_MAX - ct)))) {
        glas_thread_stack_prep_slowpath(g, read, reserve);
    }
}
LOCAL void glas_thread_stack_data_push(glas* g, glas_stemcell sc) {
    glas_thread_stack_prep(g, 0, 1);
    glas_thread_state* const ts = g->state;
    glas_stemcell* const dst = ts->stack.data + ts->stack.count;
    dst->stem = sc.stem;
    glas_roots_slot_write(&(ts->gcbase), &(dst->cell), sc.cell);
    ts->stack.count++;
}

API void glas_thread_set_debug_name(glas* g, char const* debug_name) {
    if(NULL == debug_name) { debug_name = ""; }
    size_t const len = strlen(debug_name);
    glas_os_thread_enter_busy();
    glas_cell* const cell = glas_cell_binary_alloc((uint8_t*)debug_name, len);
    glas_thread_state* const ts = g->state;
    glas_roots_slot_write(&(ts->gcbase), &(ts->debug_name), cell);
    glas_os_thread_exit_busy();
}


LOCAL glas_stemcell glas_data_u64(uint64_t const n) {
    glas_stemcell sc;
    if(0 == n) { 
        sc.stem = GLAS_STEMCELL_STEM_EMPTY;
        sc.cell = GLAS_VAL_UNIT;
    } else {
        size_t const shift = clz64(n); // number of '000...' in prefix
        if(shift >= 3) { // use packed pointer
            sc.stem = GLAS_STEMCELL_STEM_EMPTY;
            sc.cell = (glas_cell*) ((((n<<1)|1)<<(shift-1))|0b01);
        } else { // full packed pointer + overflow into sc.stem
            static uint64_t const maskLo = (((uint64_t)1)<<61)-1;
            sc.stem = ((n & ~maskLo)|(((uint64_t)1)<<60))<<shift;
            sc.cell = (glas_cell*)(((n & maskLo)<<3)|0b101);
        }
    }
    //debug("u64 data %lu written as stem %lx cell %p", n, sc.stem, sc.cell);
    return sc;
}
LOCAL glas_stemcell glas_data_i64(int64_t const n) {
    glas_stemcell sc;
    if(n >= 0) {
        sc = glas_data_u64((uint64_t) n);
    } else {
        uint64_t const n1c = (INT64_MIN == n) ? ((((uint64_t)1)<<63)-1) : ((uint64_t)(n-1));
        size_t const shift = clz64(~n1c); // number of '1111...' in prefix
        if(shift >= 3) { // use packed pointer
            sc.stem = GLAS_STEMCELL_STEM_EMPTY;
            sc.cell = (glas_cell*) ((((n1c<<1)|1)<<(shift-1))|0b01);
        } else { // full packed pointer + overflow into sc.stem
            static uint64_t const maskLo = (((uint64_t)1)<<61)-1;
            sc.stem = ((n1c & ~maskLo)|(((uint64_t)1)<<60))<<shift;
            sc.cell = (glas_cell*)(((n1c & maskLo)<<3)|0b101);
        }
    }
    //debug("i64 data %ld written as stem %lx cell %p", n, sc.stem, sc.cell);
    return sc;
}
API void glas_i64_push(glas* g, int64_t n) {
    glas_os_thread_enter_busy();
    glas_thread_stack_data_push(g, glas_data_i64(n));
    glas_os_thread_exit_busy();
}
API void glas_i32_push(glas* g, int32_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i16_push(glas* g, int16_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i8_push(glas* g, int8_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_u64_push(glas* g, uint64_t n) {
    glas_os_thread_enter_busy();
    glas_thread_stack_data_push(g, glas_data_u64(n));
    glas_os_thread_exit_busy();
}
API void glas_u32_push(glas* g, uint32_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u16_push(glas* g, uint16_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u8_push(glas* g, uint8_t n) { glas_u64_push(g, (uint64_t) n); }

LOCAL inline uint64_t glas_shrub_skip_elem(uint64_t shrub) {
    size_t pairs_rem = 0;
    do {
        if(GLAS_SHRUB_IS_PSEP(shrub)) {
            if(0 == pairs_rem) { 
                return (shrub << 2); 
            }
            pairs_rem--;
        } else if(GLAS_SHRUB_IS_PAIR(shrub)) {
            pairs_rem++;
        }
        shrub = shrub << 2;
    } while(1);
}

#if 0
LOCAL bool glas_list_len_peek_cell(glas_cell* cell, size_t* aggrlen) {
    do {
        if(GLAS_DATA_IS_PTR(cell)) {
            if(GLAS_CELL_STEM_EMPTY != cell->stemHd) {
                return false;
            } 
            switch(cell->hdr.type_id) {
                case GLAS_TYPE_BRANCH:
                    (*aggrlen)++;
                    if(GLAS_CELL_STEM_EMPTY != cell->branch.stemR) {
                        return false;
                    }
                    cell = cell->branch.R;
                    break;
                case GLAS_TYPE_TAKE_CONCAT:
                    (*aggrlen) += cell->take_concat.left_len;
                    cell = cell->take_concat.right;
                    break;
                case GLAS_TYPE_BIG_ARR:
                    (*aggrlen) += cell->big_arr.len;
                    return true;
                case GLAS_TYPE_BIG_BIN:
                    (*aggrlen) += cell->big_bin.len;
                    return true;
                case GLAS_TYPE_SMALL_ARR:
                case GLAS_TYPE_SMALL_BIN:
                    (*aggrlen) += cell->hdr.type_arg;
                    return true;
                default:
                    // TBD: force thunks or extrefs
                    return false;
            }
        } else if(GLAS_VAL_UNIT == cell) {
            return true;
        } else if(GLAS_DATA_IS_BINARY(cell)) {
            // list of 1..7 bytes
            (*aggrlen) += GLAS_DATA_BINARY_LEN(cell);
            return true;
        } else if(GLAS_DATA_IS_SHRUB(cell)) {
            // a shrub can encode a very small list
            uint64_t shrub = GLAS_DATA_SHRUB_BITS(cell);
            while(GLAS_SHRUB_IS_PAIR(shrub)) {
                (*aggrlen)++;
                shrub = glas_shrub_skip_elem(shrub << 2);
            }
            return GLAS_SHRUB_IS_UNIT(shrub);
        } else {
            // not a list
            return false;
        }
    } while(1);
}
LOCAL bool glas_list_len_peek_sc(glas_stemcell* sc, size_t* aggrlen) {
    bool ok;
    if(0 != (GLAS_STEMCELL_OPTVAL_FLAG & sc->stem)) {
        // singleton list
        (*aggrlen) += 1;
        ok = true;
    } else if(GLAS_STEMCELL_STEM_EMPTY != (GLAS_STEMCELL_STEMBITS & (sc->stem))) {
        // not a list
        ok = false; 
    } else if(GLAS_DATA_IS_PTR(sc->cell)) {
        glas_os_thread_enter_busy();
        ok = glas_list_len_peek_cell(sc->cell, aggrlen);
        glas_os_thread_exit_busy();
    } else {
        ok = glas_list_len_peek_cell(sc->cell, aggrlen);
    }
    return ok;
}

LOCAL bool glas_bits_len_peek_cell(glas_cell* cell, size_t* aggrlen) {
    do {
        if(GLAS_DATA_IS_BITS(cell)) {
            (*aggrlen) += 63 - ctz64(((uint64_t)cell) & ~((uint64_t)0b11));
            return true;
        } else if(GLAS_DATA_IS_PTR(cell)) {
            (*aggrlen) += 31 - ctz32(cell->stemHd);
            if(GLAS_TYPE_STEM == cell->hdr.type_id) {
                (*aggrlen) += 32 * cell->hdr.type_arg;
                cell = cell->stem.fby;
            } else {
                // TBD: force thunks or extrefs
                // might add stem-of-bin eventually
                return false;
            }
        } else if(GLAS_DATA_IS_SHRUB(cell)) {
            // squeeze out a few more edge bits
            uint64_t shrub = GLAS_DATA_SHRUB_BITS(cell);
            while(GLAS_SHRUB_IS_EDGE(shrub)) {
                shrub = (shrub << 2);
                (*aggrlen)++;
            }
            assert(GLAS_SHRUB_IS_PAIR(shrub) && "shrub must contain pair");
            return false; 
        } else if(GLAS_DATA_IS_RATIONAL(cell)) {
            // rationals share 4 bits between 'n:' and 'd:' dict keys.
            //  'n' 0x6E 0b01101110
            //  'd' 0x64 0b01100100
            (*aggrlen) += 4;
            return false;
        } else {
            return false;
        }
    } while(1);
}
LOCAL bool glas_bits_len_peek_sc(glas_stemcell* sc, size_t* aggrlen) {
    if(0 != (GLAS_STEMCELL_OPTVAL_FLAG & sc->stem)) {
        return false;
    }
    (*aggrlen) += ctz64(GLAS_STEMCELL_STEMBITS & sc->stem) - 3;
    bool ok;
    if(GLAS_DATA_IS_PTR(sc->cell)) {
        glas_os_thread_enter_busy();
        ok = glas_bits_len_peek_cell(sc->cell, aggrlen);
        glas_os_thread_exit_busy();
    } else {
        ok = glas_bits_len_peek_cell(sc->cell, aggrlen);
    }
    return ok;
}
#endif

API bool glas_i64_peek(glas* g, int64_t* n) {
    // TBD: too many possible representations (e.g. thunks, extrefs). 
    // might be better to keep it simple, extract bits opportunistically
    (void)g; (void)n;
    return false;
}
API bool glas_i32_peek(glas* g, int32_t* n) {
    int64_t x = (*n);
    bool const ok = glas_i64_peek(g, &x) && (INT32_MAX >= x) && (x >= INT32_MIN);
    (*n) = (int32_t)x;
    return ok;
}
API bool glas_i16_peek(glas* g, int16_t* n) {
    int64_t x = (*n);
    bool const ok = glas_i64_peek(g, &x) && (INT16_MAX >= x) && (x >= INT16_MIN);
    (*n) = (int16_t)x;
    return ok;
}
API bool glas_i8_peek(glas* g, int8_t* n) {
    int64_t x = (*n);
    bool const ok = glas_i64_peek(g, &x) && (INT8_MAX >= x) && (x >= INT8_MIN);
    (*n) = (int8_t)x;
    return ok;
}
API bool glas_u64_peek(glas* g, uint64_t* n) {
    (void)g; (void) n;
    debug("u64 peek");
    return false;
}
API bool glas_u32_peek(glas* g, uint32_t* n) {
    uint64_t x = (*n);
    bool const ok = glas_u64_peek(g, &x) && (UINT32_MAX >= x);
    (*n) = (uint32_t)x;
    return ok;
}
API bool glas_u16_peek(glas* g, uint16_t* n) {
    uint64_t x = (*n);
    bool const ok = glas_u64_peek(g, &x) && (UINT16_MAX >= x);
    (*n) = (uint16_t)x;
    return ok;
}
API bool glas_u8_peek(glas* g, uint8_t* n) {
    uint64_t x = (*n);
    bool const ok = glas_u64_peek(g, &x) && (UINT8_MAX >= x);
    (*n) = (uint8_t)x;
    return ok;
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

    debug("max ptr int: %ld (%lx)", GLAS_PTR_MAX_INT, GLAS_PTR_MAX_INT);
    debug("min ptr int: %ld (%lx)", GLAS_PTR_MIN_INT, GLAS_PTR_MIN_INT);

    glas_page* page = glas_rt_page_alloc();
    //glas_rt_page_free(page);
    glas* g = glas_thread_new();
    glas_u64_push(g,GLAS_PTR_MAX_INT);
    glas_i64_push(g,GLAS_PTR_MIN_INT);
    glas_i64_push(g,INT64_MAX);
    glas_i64_push(g,INT64_MIN);
    glas_i64_push(g,INT64_MIN + 1);
    glas_u64_push(g,UINT64_MAX);


    glas_thread_exit(g);
    return popcount_test 
        && ctz_test
        ;
}

