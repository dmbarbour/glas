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
#include <sched.h>
#include <time.h>
#include <assert.h>
#include <errno.h>
#include <sys/mman.h>
#include <unistd.h>

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
  #define debug(fmt, ...) do { fprintf(stderr, "%s:%u:%s: " fmt "\n", __FILE__, __LINE__, __PRETTY_FUNCTION__, ##__VA_ARGS__); } while(0)
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
#define GLAS_CELL_BATCH_ALLOC 400

/**
 * GC design:
 * - non-moving GC, concurrent mark + lazy sweep on alloc
 * - because all cells are 32 bytes, there is no need to compact
 * - concurrent mark requires a write barrier
 * - snapshot at the beginning; new allocations marked but not traced
 * - double mark buffers, flip buffers after mark completes
 * - non-generational, at least initially. can add later.
 * 
 * As part of lazy sweep, each OS thread allocates from its own page.
 * Pages are marked for reallocation after they undergo GC unless they
 * are currently 'owned' by an OS thread.
 */
#define GLAS_GC_CELL_BUFFSZ 120
#define GLAS_GC_STAT_SIZE 16
#define GLAS_GC_POLL_USEC (10 * 1000)
#define GLAS_GC_THREADS_MAX 8
#define GLAS_GC_THREAD_IDLE_CYCLES 3
#define GLAS_THREAD_CHECKPOINT_MAX 9
#define GLAS_STACK_MAX 32

typedef struct glas_heap glas_heap; // mmap location    
typedef struct glas_page glas_page; // aligned region
typedef struct glas_cell glas_cell; // datum in page
typedef struct glas_cell_hdr glas_cell_hdr;
typedef struct glas_roots glas_roots; // GC roots
typedef struct glas_os_thread glas_os_thread; // per OS thread
typedef struct glas_gc_mb glas_gc_mb;
typedef struct glas_gc_fl glas_gc_fl;
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
#define GLAS_VAL_UNIT ((glas_cell*)(GLAS_STEM63_EMPTY | 0b01))

#define GLAS_PTR_MAX_INT ((((int64_t)1)<<61) - 1)
#define GLAS_PTR_MIN_INT (-GLAS_PTR_MAX_INT)

#define GLAS_DATA_TAG_BITS UINT64_C(0b01)
#define GLAS_DATA_TAG_SHRUB UINT64_C(0b10)
#define GLAS_DATA_TAG_PAKRAT UINT64_C(0b011)
#define GLAS_DATA_TAG_BINARY UINT64_C(0b00111)

#define GLAS_DATA_IS_PTR(P) (0 == (((uint64_t)P) & 0x1F))
#define GLAS_DATA_IS_BITS(P) (GLAS_DATA_TAG_BITS == (((uint64_t)P) & 0b11))
#define GLAS_DATA_IS_SHRUB(P) (GLAS_DATA_TAG_SHRUB == (((uint64_t)P) & 0b11))
#define GLAS_DATA_IS_PACKRAT(P) (GLAS_DATA_TAG_PAKRAT == (((uint64_t)P) & 0b111))
#define GLAS_DATA_IS_BINARY(P) (GLAS_DATA_TAG_BINARY == (((uint64_t)P) & 0b11111))
#define GLAS_DATA_IS_ABSTRACT_CONST(P) (0xFF == (((uint64_t)P) & 0xFF))
#define GLAS_DATA_BINARY_LEN(P) (0b111 & (((uint64_t)P) >> 5))

/**
 * Abstract values.
 * 
 * Uniformly treated as abstract and runtime-ephemeral. 
 * 
 * - GLAS_VOID - treat as permanently sealed value.
 */
#define GLAS_ABSTRACT_CONST(N) ((glas_cell*)((((uint64_t)N)<<8)|0xFF))
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
#define GLAS_STEM31_HIBIT (UINT32_C(1)<<31)
#define GLAS_STEM63_HIBIT (UINT64_C(1)<<63)
#define GLAS_STEM31_EMPTY GLAS_STEM31_HIBIT
#define GLAS_STEM63_EMPTY GLAS_STEM63_HIBIT
#define GLAS_STEM31_VALID(S) (0 != (S))
#define GLAS_STEM63_VALID(S) (0 != (S))


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
#define GLAS_DATA_SHRUB_BITS(P) (((uint64_t)P) & ~UINT64_C(0b11))
#define GLAS_SHRUB_STEP_MASK (UINT64_C(0b11)<<62)
#define GLAS_SHRUB_LBITS (UINT64_C(0b10)<<62)
#define GLAS_SHRUB_RBITS (UINT64_C(0b11)<<62)
#define GLAS_SHRUB_PBITS (UINT64_C(0b01)<<62)
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
 * Packed Rational (PACKRAT)
 * 
 * Rationals of up to 30 bit numerator and denominator can be stored in
 * the 64-bit packed pointer.
 * 
 *    numerator denominator tag
 *     31 bits    30 bits   011
 * 
 * Denominator stores numbers of 1..30 bits with an implicit '1' prefix.
 * The numerator stores 0..30 bits, positive or negative. There are many
 * redundant encodings, e.g. 1/3 vs. 2/6. The PACKRAT encoding does not
 * require normalization, only packs the encoding for dicts looking like
 * rational numbers.
 */
#define GLAS_PACKRAT_NUM_MASK ~((UINT64_C(1)<<33)-1)
#define GLAS_PACKRAT_DEN_MASK (((UINT64_C(1)<<33)-1) & ~UINT64_C(0b111))
#define GLAS_PACKRAT_NUM_STEM(P)   (((uint64_t)P) & GLAS_PACKRAT_NUM_MASK)
#define GLAS_PACKRAT_DEN_STEM(P) (((((uint64_t)P) & GLAS_PACKRAT_DEN_MASK)<<31)|GLAS_STEM63_HIBIT)
#define GLAS_PACKRAT_NUM_CELL(P) ((glas_cell*)(GLAS_PACKRAT_NUM_STEM(P) | GLAS_DATA_TAG_BITS))
#define GLAS_PACKRAT_DEN_CELL(P) ((glas_cell*)(GLAS_PACKRAT_DEN_STEM(P) | GLAS_DATA_TAG_BITS))
#define GLAS_PACKRAT_NUM_MAX INT32_C(0x3fffffff)
#define GLAS_PACKRAT_NUM_MIN (-GLAS_PACKRAT_NUM_MAX)
#define GLAS_PACKRAT_DEN_MAX GLAS_PACKRAT_NUM_MAX
#define GLAS_PACKRAT_DEN_MIN INT32_C(1)

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

typedef enum glas_type_id {
    GLAS_TYPE_STEM,
    GLAS_TYPE_BRANCH,
    GLAS_TYPE_SMALL_BIN,
    GLAS_TYPE_SMALL_ARR,
    GLAS_TYPE_BIG_BIN,
    GLAS_TYPE_BIG_ARR,
    GLAS_TYPE_TAKE_CONCAT, 
    GLAS_TYPE_FOREIGN_PTR,
    GLAS_TYPE_REGISTER,
    GLAS_TYPE_TOMBSTONE,
    GLAS_TYPE_SEAL,
    // under development
    GLAS_TYPE_THUNK,
    GLAS_TYPE_EXTREF,
    // experimental
    //GLAS_TYPE_SMALL_GLOB,   // interpret small_bin as glas object
    //GLAS_TYPE_BIG_GLOB,     // interpret big_bin as glas object
    //GLAS_TYPE_STEM_OF_BIN, 
    // end of list
    GLAS_TYPEID_COUNT
} glas_type_id;
static_assert(32 > GLAS_TYPEID_COUNT, 
    "glas reserves a two type bits for logical wrappers");

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
 * gcbits: xxxxxsss
 *    s: once-per-slot write barrier, also update on scan
 *    x: unused, tentatively could track cell age
 */
struct glas_cell_hdr {
    uint8_t type_id;   // logical structure of this node
    uint8_t type_arg;  // e.g. number of bytes in small_bin
    uint8_t type_aggr; // monoidal, e.g. linear, ephemeral (2+ bits), abstract
    _Atomic(uint8_t) gcbits;  // reserved for use by GC
};
#define GLAS_GCBITS_SCAN    0b00000111
#define GLAS_AGGR_LINEAR_FLAG   0b0001
#define GLAS_AGGR_ABSTRACT_FLAG 0b0010
#define GLAS_AGGR_EPH_MASK      0b1100

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
            //
            // Always abstract + runtime ephemeral. Optionally linear.
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
            glas_cell* key;     // usually a weakref to register
            glas_cell* data;    // sealed data
            glas_cell* meta;    // metadata for debug, e.g. timestamp, provenance
            // data may be collected when key becomes unreachable
            // optionally linear.
        } seal;

        struct {
            _Atomic(glas_cell*) version;    // tentative!
            _Atomic(glas_cell*) assoc_lhs;  // associated volumes (reg,_)
            _Atomic(glas_cell*) ts;         // weakref + stable ID; reg is finalizer
            // Sketch:
            // - basic volume of registers as a lazy dict (load via thunks)
            // - associated volume as radix tree mapping rhs register stable
            //   IDs to rhs-sealed basic volumes, i.e. mutate assoc_lhs.
            // - GC can eliminate unreachable branches of radix tree 
            // - version may be more sophisticated than raw content, e.g. to
            //   support snapshot views of data.

            // Alt design:
            // - externalize state via identity
            // 
        } reg;
        
        struct {
            // tombstone, provides a weak ref and a stable ID
            _Atomic(glas_cell*) wk;     // GLAS_VOID if collected
            uint64_t            id;     // for hashmaps, debugging, etc.
            // id is global atomic incref; I assume 64 bits is adequate.
        } ts;

        struct {
            // data held externally, e.g. content-addressed storage
            glas_cell* ref;     // runtime-recognized reference
            glas_cell* ts;    // stable ID and weakref for caching
        } extref;

        struct {
            // computation language (e.g. prog or ns) captured in closure.
            // non-deterministic computations may be captured: 
            //   - defer choice until observer thread forces AND commits
            //   - sparks can precompute multiple choices
            //   - requires some careful attention to thunk state
            // the computation captures function and inputs, perhaps a
            // frozen view of relevant registers in the general case. 
            _Atomic(glas_cell*) closure;    // what to evaluate
            _Atomic(glas_cell*) result;     // final result (or GLAS_VOID)
            _Atomic(glas_cell*) claim;      // evaluating thread and waitlist

            // Note: At the moment, thunks aren't erased by GC and may be
            // explicit in the glas types. I must consider what I want here.
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
    _Atomic(uint64_t) marks[2][GLAS_PAGE_CELL_COUNT/64]; // bit per mark
    _Atomic(uint64_t) *marking, *marked; // swapped per mark cycle
    uint8_t utilization[GLAS_GC_STAT_SIZE]; // inverse of free space last sweep
    uint8_t defer_reuse;    // delay sweep+realloc if utilization high

    // track how long pages are held.
    uint64_t cycle_acquired;
    uint64_t cycle_released; 

    glas_page *gc_next;     // GC private linked list to reduce locking (rebuilt each cycle)
    glas_page *next;        // allocator linked lists
    glas_heap *heap;        // owning heap object
    uint64_t magic_word;    // only used in debug assertions
} __attribute__((aligned(GLAS_HEAP_CARD_SIZE)));

static_assert((0 == (sizeof(glas_page) & (GLAS_HEAP_CARD_SIZE - 1))),
    "glas page header not aligned to card");
static_assert((GLAS_HEAP_PAGE_SIZE >> 6) >= sizeof(glas_page), 
    "glas page header is too large");
static_assert((0 == (GLAS_PAGE_CARD_COUNT & 0x1ff)),
    "glas page cards not nicely aligned");

struct glas_heap {
    // each 'heap' tracks 63-64 'pages', depending on alignment
    glas_heap* next;
    void* mem_start;
    _Atomic(uint64_t) page_bitmap;
};

/**
 * Thread-local storage per OS thread.
 */
struct glas_os_thread {
    glas_os_thread *next;

    /**
     * ID for this thread.
     */
    pthread_t self;

    /**
     * Semaphore for waiting on GC stop.
     */
    sem_t wakeup;

    /**
     * A state for blocking on GC.
     * 
     * Busy threads may enter busy state recursively, like a recursive
     * mutex, blocking GC from proceeding.
     */
    glas_os_thread_state state;
    size_t busy_depth; 

    /**
     * As part of GC concurrent mark + lazy sweep, a thread owns a page
     * and scans the mark bitmap during allocation. This can result in a
     * high allocation cost in some cases, i.e. if a page is mostly full
     * and has a high survival rate for data. To mitigate, we can track
     * how many allocations we got out of a page, and pull pages out of
     * circulation for a while if the number is low.
     */
    struct glas_os_thread_alloc {
        glas_page* page;        // owned page.
        size_t mark_word;       // offset into mark bitmap
        uint64_t free_bits;     // free bits from recent alloc
        size_t free_count;      // t
    } alloc;
    glas_gc_fl* fl; // recently allocated finalizers
};

/**
 * Rooted glas data.
 * 
 * This represents a collection of roots as an array of uint16_t offsets
 * to `glas_cell*` fields in another structure. The fields may be edited
 * by any OS thread in the mutator state.
 */
struct glas_roots {
    glas_roots* next;                   // for global roots list
    _Atomic(size_t) refct;              // usually 1 or 0, but simplifies sharing
    _Atomic(uint64_t) trace_cycle;      // for concurrent mark

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
    size_t max_offset; 
    size_t root_count; 

    /**
     * A slot bitmap, computed based on root offsets.
     * 
     * This supports snapshot at the beginning (SATB) semantics during
     * concurrent marking, tracking for each slot whether it has been
     * scanned or not during the current GC.
     * 
     * Overhead for densely packed roots is about 1 bit per field. Will
     * be more expensive if fields are spread out.
     */
    _Atomic(uint64_t)* slot_bitmap; 
};

/**
 * A 'stem cell' is a miniature workspace for manipulating stem bits on 
 * the data stack. The motive is to reduce number of allocations when 
 * working with stemHd or GLAS_TYPE_STEM.
 */
typedef struct glas_sc {
    uint64_t stem;      // 0..63 stem bits
    glas_cell* cell;
} glas_sc;

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
    glas_sc data[GLAS_STACK_MAX];
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

    bool has_abort_handlers; 
    //glas* next; // for linked list contexts
    // TBD:
    // - error signaling
    // - 
};

/**
 * buffers used by GC for a work-stealing concurrent mark phase.
 */
struct glas_gc_mb {
    /**
     * The main to-be-scanned buffer. 
     */
    glas_cell* buffer[GLAS_GC_CELL_BUFFSZ]; // items 0..fill-1
    size_t fill;

    /**
     * To support lazy marking of big arrays, we'll also track up to
     * one array per scan buffer. This ensures we never need allocate
     * more than one buffer per big array.
     */
    struct {
        glas_cell** data;
        size_t len;
    } arr;

    glas_gc_mb* next; // support lists of scans
};

/**
 * list for tracking finalizers
 */
struct glas_gc_fl {
    glas_cell* buffer[GLAS_GC_CELL_BUFFSZ];
    size_t fill;
    glas_gc_fl* next;
};

typedef struct glas_gc_wp {
    pthread_t* workers; 
    size_t count;
    _Atomic(size_t) done;
    sem_t wakeup; // shared semaphore
} glas_gc_wp;

typedef struct glas_gc_dq {
    pthread_t thread;
    pthread_mutex_t mutex;
    sem_t wakeup;
    size_t head;
    size_t tail;
    glas_refct* items;
    size_t capacity; 
    // note: actual capacity is one less; 
    // tail location is always empty.
} glas_gc_dq;

typedef struct glas_alloc_l {
    _Atomic(glas_page*) page_list;
    _Atomic(size_t) page_count;
} glas_alloc_l;

static struct glas_rt {
    pthread_mutex_t mutex; // use sparingly!
    _Atomic(uint64_t) idgen;

    struct glas_rt_tls {
        pthread_key_t key;
        _Atomic(glas_os_thread*) list;
    } tls;

    struct glas_rt_alloc {
        /**
         * Linked list of heaps, sized for 64 pages each. The main use
         * is to ensure page alignment has low address-space overhead.
         */
        _Atomic(glas_heap*) heaps;

        /**
         * Pages divided into two lists:
         * - avail: estimated low utilization, not necessarily empty
         * - await: pages full or usage deferred to a later GC cycle
         */
        glas_alloc_l avail, await;
        pthread_mutex_t mutex;
    } alloc;

    struct glas_rt_root {
        _Atomic(glas_roots*) list;      // ad hoc glas_cell* slots
        _Atomic(glas_cell*) globals;    // lazily constructed, shared
        _Atomic(glas_cell*) conf;       // the configuration register
    } root;

    // TBD: 
    // - on_commit operations queues, 
    // - worker threads for opqueues, GC, lazy sparks, bgcalls
    // idea: count threads, highest number thread quits if too many,
    // e.g. compared to a configuration; configured number vs. actual.

    struct glas_rt_gc {
        _Atomic(uint64_t) cycle; // how many GC cycles, also a sync var

        /**
         * Support for concurrent marking.
         * 
         * Workers will be initialized by the main GC thread.
         */
        glas_gc_wp pool;
        glas_roots* roots_snapshot;
        glas_page* pages; // via gc_next

        /**
         * During concurrent mark phase, put overflow scan buffers here. 
         * Will eventually be useful with concurrent GC threads.
         */
        _Atomic(glas_gc_mb*) mb;
        pthread_mutex_t gc_mb_pop_mutex; // guard against ABA issues
        _Atomic(glas_cell*) wb; // from write-barriers

        _Atomic(glas_gc_fl*) fl;    // registered finalizers
        glas_gc_dq dq;              // decref queue for foreign pointers

        /**
         * GC waits after entering 'stop' until busy_threads_count is
         * reduced to zero. Last thread to exit signals wakeup. Threads
         * entering busy instead wait on their own semaphores.
         */
        _Atomic(size_t) busy_threads_count;
        pthread_t gc_main_thread;
        sem_t wakeup;

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
         * Stats at start of prior GC to guide heuristics
         */
        uint64_t prior_page_ct; 
        uint64_t prior_root_ct; 

        /**
         * API guidance of GC
         */
        _Atomic(bool) signal_gc;
        _Atomic(bool) force_fullgc;
    } gc;

    struct glas_rt_stat {
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
        _Atomic(uint64_t) page_release;
        _Atomic(uint64_t) heap_alloc;   // mmaps
        _Atomic(uint64_t) heap_free;
        _Atomic(uint64_t) gc_wb_resume;   // how many write-barriers activated
        _Atomic(uint64_t) gc_wb_stop;   // marked write-barriers when stopped
    } stat;

} glas_rt;
static pthread_once_t glas_rt_init_once = PTHREAD_ONCE_INIT;

LOCAL void glas_rt_init_slowpath();
LOCAL inline void glas_rt_init() { pthread_once(&glas_rt_init_once, &glas_rt_init_slowpath); }
LOCAL inline void glas_rt_lock() { pthread_mutex_lock(&glas_rt.mutex); }
LOCAL inline void glas_rt_unlock() { pthread_mutex_unlock(&glas_rt.mutex); }
LOCAL inline void glas_rt_alloc_lock() { pthread_mutex_lock(&glas_rt.alloc.mutex); }
LOCAL inline void glas_rt_alloc_unlock() { pthread_mutex_unlock(&glas_rt.alloc.mutex); }
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
    assert(likely((NULL == t->fl) && (NULL == t->alloc.page)));
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
    pthread_mutex_init(&glas_rt.gc.gc_mb_pop_mutex, NULL);
    pthread_key_create(&glas_rt.tls.key, &glas_os_thread_detach);
    sem_init(&(glas_rt.gc.wakeup), 0, 0);
    atomic_init(&glas_rt.root.conf, GLAS_VAL_UNIT);
    atomic_init(&glas_rt.root.globals, GLAS_VOID);
    atomic_init(&glas_rt.gc.wb, GLAS_VOID);
    atomic_init(&glas_rt.gc.cycle, 1);
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
static inline size_t ctz64(uint64_t n) { 
    assert(likely(0 != n)); // undefined behavior if n is 0
    return (size_t) __builtin_ctzll(n); 
}
static inline size_t ctz32(uint32_t n) { 
    assert(likely(0 != n)); // undefined behavior if n is 0
    return (size_t) __builtin_ctz(n); 
}
static inline size_t clz64(uint64_t n) {
    assert(likely(0 != n)); // undefined behavior if n is 0
    return (size_t) __builtin_clzll(n); 
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
    // We can lose the last page per mmap due to alignment. This is
    // only a 1.6% loss of address space (not RAM) thus not an issue.
    bool const is_aligned = (glas_heap_pages_start(heap) == (heap->mem_start));
    return is_aligned ? 0 : (UINT64_C(1)<<63);
}
LOCAL inline bool glas_heap_is_empty(glas_heap* heap) {
    return (glas_heap_initial_bitmap(heap) == 
            atomic_load_explicit(&(heap->page_bitmap), memory_order_relaxed));
}
LOCAL inline bool glas_heap_is_full(glas_heap* heap) {
    return (0 == ~(atomic_load_explicit(&(heap->page_bitmap), memory_order_relaxed)));
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
    uint64_t bitmap = atomic_load_explicit(&(heap->page_bitmap), memory_order_relaxed);
    while(0 != ~bitmap) {
        size_t const ix = ctz64(~bitmap);
        uint64_t const bit = (UINT64_C(1)<<ix);
        bitmap = atomic_fetch_or_explicit(&(heap->page_bitmap), bit, memory_order_relaxed);
        if(0 == (bitmap & bit)) {
            // race to claim was successful
            void* const page = (void*)(((uintptr_t)glas_heap_pages_start(heap)) + (ix * GLAS_HEAP_PAGE_SIZE));
            if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_READ | PROT_WRITE))) {
                debug("could not mark page for read+write, error %d: %s", errno, strerror(errno));
                abort();
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
    uint64_t const bit = UINT64_C(1)<<ix;
    uint64_t const prior = atomic_fetch_and_explicit(&(heap->page_bitmap), ~bit, memory_order_relaxed);
    assert(0 != (prior & bit));
    if(unlikely(0 != mprotect(page, GLAS_HEAP_PAGE_SIZE, PROT_NONE))) {
        debug("error protecting page %p from read-write, %d: %s", page, errno, strerror(errno));
        // not a halting error
    }
    if(unlikely(0 != madvise(page, GLAS_HEAP_PAGE_SIZE, MADV_DONTNEED))) {
        debug("error expunging page %p from memory, %d: %s", page, errno, strerror(errno));
        // not a halting error, but may waste some memory
    }
}
LOCAL inline uint64_t glas_page_magic_word_by_addr(void* addr) {
    static uint64_t const prime = UINT64_C(12233355555333221);
    return prime * (uint64_t)(((uintptr_t)addr)>>(GLAS_HEAP_PAGE_SIZE_LG2));
}
LOCAL void glas_page_init(glas_heap* heap, glas_page* page) {
    assert(likely((glas_mem_page_ceil(page) == page) && 
                   glas_heap_includes_addr(heap, page)));
    memset(page, 0, sizeof(glas_page));
    page->marking = page->marks[0];
    page->marked = page->marks[1];
    page->heap = heap;
    page->magic_word = glas_page_magic_word_by_addr(page);
}
LOCAL inline glas_page* glas_page_from_internal_addr(void* addr) {
    glas_page* const page = (glas_page*) glas_mem_page_floor(addr);
    assert(likely(glas_page_magic_word_by_addr(page) == page->magic_word));
    return page;
}
LOCAL glas_page* glas_allocl_try_pop(glas_alloc_l* l) {
    glas_page* page = atomic_load_explicit(&(l->page_list), memory_order_acquire);
    do {} while((NULL != page) && atomic_compare_exchange_weak_explicit(&(l->page_list), 
        &page, page->next, memory_order_acquire, memory_order_acquire));
    if(NULL != page) {
        atomic_fetch_sub_explicit(&(l->page_count), 1, memory_order_relaxed);
    }
    return page;
}
LOCAL void glas_allocl_push(glas_alloc_l* l, glas_page* page) {
    assert(likely((NULL != page) && (NULL == page->next)));
    atomic_pushlist(&(l->page_list), &(page->next), page);
    atomic_fetch_add_explicit(&(l->page_count), 1, memory_order_relaxed);
}
LOCAL bool glas_rt_grow_full_heap() {
    bool ok = false;
    // goal here is to have a non-full heap. Locking this to avoid
    // race where a thread must immediately return a heap to the OS.
    glas_rt_alloc_lock();
    glas_heap* const curr_heap = atomic_load_explicit(&glas_rt.alloc.heaps, memory_order_acquire);
    if((NULL == curr_heap) || glas_heap_is_full(curr_heap)) {
        glas_heap* const new_heap = glas_heap_try_create();
        if(NULL != new_heap) {
            atomic_pushlist(&glas_rt.alloc.heaps, &(new_heap->next), new_heap);
            ok = true;
        }
    } else {
        // heap is not full, perhaps another thread allocated while awaiting lock 
        ok = true; 
    }
    glas_rt_alloc_unlock();
    return ok;
}
LOCAL glas_page* glas_rt_page_alloc() {
    atomic_fetch_add_explicit(&glas_rt.stat.page_alloc, 1, memory_order_relaxed);
    do {
        glas_page* page = glas_allocl_try_pop(&glas_rt.alloc.avail);
        if(NULL != page) { 
            return page; 
        }
        glas_heap* heap = atomic_load_explicit(&glas_rt.alloc.heaps, memory_order_relaxed);
        page = (NULL == heap) ? NULL : glas_heap_try_alloc_page(heap);
        if(NULL != page) {
            glas_page_init(heap, page);
            return page;
        }
    } while(glas_rt_grow_full_heap());
    debug("runtime is out of memory!");
    abort();
}
LOCAL inline bool glas_os_thread_is_busy() {
    glas_os_thread* const t = pthread_getspecific(glas_rt.tls.key);
    return ((NULL != t) && (GLAS_OS_THREAD_BUSY == t->state));
}

LOCAL inline size_t glas_page_utilization_run_of(glas_page* page, uint8_t thresh) {
    size_t ix = 0;
    while((ix < GLAS_GC_STAT_SIZE) && (page->utilization[ix] >= thresh)) { ++ix; }
    return ix;
}

LOCAL void glas_page_swept(glas_page* page, size_t amt_freed) {
    assert(likely((NULL != page) && (amt_freed < GLAS_PAGE_CELL_COUNT)));
    // track relative utilization for the last few cycles.
    for(size_t ix = (GLAS_GC_STAT_SIZE-1); ix > 0; --ix) {
        page->utilization[ix] = page->utilization[ix-1];
    }
    static_assert(0 == (GLAS_PAGE_CELL_COUNT % 256));
    page->utilization[0] = 0xFF & ((GLAS_PAGE_CELL_COUNT - amt_freed) / 
        (GLAS_PAGE_CELL_COUNT/256));

    // Heuristically defer recycling based on runs of ineffective sweeps.
    size_t const r66 = glas_page_utilization_run_of(page, UINT8_C(170));
    size_t const r80 = glas_page_utilization_run_of(page, UINT8_C(205));
    page->defer_reuse = (uint8_t)(r66/2 + r80);
}
LOCAL void glas_os_thread_release_page(glas_os_thread* t) {
    atomic_fetch_add_explicit(&glas_rt.stat.page_release, 1, memory_order_relaxed);
    if(NULL != t->alloc.page) { 
        t->alloc.page->cycle_released = atomic_load_explicit(&glas_rt.gc.cycle, memory_order_relaxed);
        glas_page_swept(t->alloc.page, t->alloc.free_count);
    }
    t->alloc.page = NULL;
    t->alloc.mark_word = 0;
    t->alloc.free_bits = 0;
    t->alloc.free_count = 0;
}
LOCAL void glas_os_thread_alloc_reserve(glas_os_thread* t) {
    // return with t->free_bits or bust!
    // This will sweep pages as part of finding allocations.
    assert(likely((GLAS_OS_THREAD_BUSY == t->state) && (0 == t->alloc.free_bits)));
    static_assert(0 == (GLAS_PAGE_CELL_COUNT % 64));
    static size_t const MARK_WORD_MAX = (GLAS_PAGE_CELL_COUNT/64) - 1; // one bit per cell
    do {
        if(unlikely((NULL == t->alloc.page) || (MARK_WORD_MAX == t->alloc.mark_word))) {
            glas_os_thread_release_page(t);
            t->alloc.page = glas_rt_page_alloc();
            glas_allocl_push(&glas_rt.alloc.await, t->alloc.page);
            t->alloc.page->cycle_acquired = atomic_load_explicit(&glas_rt.gc.cycle, memory_order_relaxed);
            assert(likely(t->alloc.page->cycle_acquired > t->alloc.page->cycle_released));
            // begin allocation immediately after `glas_page` header
            static_assert(0 == (sizeof(glas_page)%sizeof(glas_cell)));
            static size_t const HDR_MARK_BITS = sizeof(glas_page)/sizeof(glas_cell);
            static size_t const HDR_END = HDR_MARK_BITS/64;
            static uint64_t const HDR_REM = (UINT64_C(1)<<(HDR_MARK_BITS%64))-1;
            uint64_t const survivors = atomic_load_explicit((t->alloc.page->marked + HDR_END), memory_order_relaxed);
            t->alloc.mark_word = HDR_END;
            t->alloc.free_bits = ~(HDR_REM | survivors);
        } else {
            ++(t->alloc.mark_word);
            uint64_t const survivors = atomic_load_explicit((t->alloc.page->marked + t->alloc.mark_word), memory_order_relaxed);
            t->alloc.free_bits = ~survivors;
        }
    } while(0 == t->alloc.free_bits);
    t->alloc.free_count += popcount64(t->alloc.free_bits);
    if(glas_rt.gc.marking) {
        // Mark all new allocations during concurrent mark phase. This ensures
        // they aren't reallocated immediately when we swap marked and marking.
        atomic_fetch_or_explicit((t->alloc.page->marking + t->alloc.mark_word), 
            t->alloc.free_bits, memory_order_relaxed);
    }
}
LOCAL inline bool glas_gc_b0scan() {
    // return true iff a 0 bit means 'scanned'
    return (0 == (1 & glas_rt.gc.gcbits));
}
LOCAL glas_cell* glas_cell_alloc() {
    glas_os_thread* const t = glas_os_thread_get();
    if(unlikely(0 == t->alloc.free_bits)) {
        glas_os_thread_alloc_reserve(t);
    }
    size_t ix = ctz64(t->alloc.free_bits);
    t->alloc.free_bits &= (t->alloc.free_bits - 1);
    glas_cell* const cell = ((glas_cell*)(t->alloc.page)) + ((t->alloc.mark_word) * 64) + ix;
    atomic_init(&(cell->hdr.gcbits), glas_rt.gc.gcbits); 
    return cell;
}
LOCAL inline glas_cell* glas_cell_clone(glas_cell* cell) {
    assert(likely(GLAS_DATA_IS_PTR(cell)));
    glas_cell* result = glas_cell_alloc();
    (*result) = (*cell); // copy everything
    atomic_init(&(result->hdr.gcbits), glas_rt.gc.gcbits); // repair gcbits
    return result;
}

LOCAL glas_gc_fl* glas_gc_fl_new() {
    glas_gc_fl* fl = malloc(sizeof(glas_gc_fl));
    fl->fill = 0;
    fl->next = NULL;
    return fl;
}
LOCAL void glas_gc_fl_compact(glas_gc_fl* fl) {
    if(NULL == fl) { return; }
    while(NULL != fl->next) {
        if(GLAS_GC_CELL_BUFFSZ >= (fl->fill + fl->next->fill)) {
            glas_gc_fl* const tmp = fl->next;
            fl->next = tmp->next;
            memcpy(fl->buffer + fl->fill, tmp->buffer, (sizeof(glas_cell*) * tmp->fill));
            fl->fill += tmp->fill;
            free(tmp);
        } else {
            fl = fl->next;
        }
    }
}
LOCAL void glas_gc_register_finalizer(glas_cell* cell) {
    assert(likely(GLAS_DATA_IS_PTR(cell) && glas_os_thread_is_busy()));
    // record into thread-local list of new finalizers
    glas_os_thread* t = glas_os_thread_get();
    if(NULL == t->fl) {
        t->fl = glas_gc_fl_new();
    } else if(GLAS_GC_CELL_BUFFSZ == t->fl->fill) {
        assert(likely(NULL == t->fl->next));
        atomic_pushlist(&glas_rt.gc.fl, &(t->fl->next), t->fl);
        t->fl = glas_gc_fl_new();
    }
    t->fl->buffer[(t->fl->fill)++] = cell;
}
LOCAL inline glas_cell* glas_cell_fptr(void* ptr, glas_refct pin, bool linear) {
    glas_cell* cell = glas_cell_alloc();
    cell->hdr.type_id = GLAS_TYPE_FOREIGN_PTR;
    cell->hdr.type_aggr = 0b1010 | (linear ? 0b0001 : 0);
    cell->hdr.type_arg = 0;
    cell->stemHd = GLAS_STEM31_EMPTY;
    cell->foreign_ptr.ptr = ptr;
    cell->foreign_ptr.pin = pin;
    if(NULL != pin.refct_upd) {
        glas_gc_register_finalizer(cell);
    }
    return cell;
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
LOCAL glas_cell* glas_cell_binary_slice(uint8_t const* data, size_t len, glas_cell* fptr) {
    assert(likely(GLAS_TYPE_FOREIGN_PTR == fptr->hdr.type_id));
    glas_cell* const slice = glas_cell_alloc();
    slice->hdr.type_id = GLAS_TYPE_BIG_BIN;
    slice->hdr.type_arg = 0;
    slice->hdr.type_aggr = 0;
    slice->stemHd = GLAS_STEM31_EMPTY;
    slice->big_bin.data = data;
    slice->big_bin.len = len;
    slice->big_bin.fptr = fptr;
    return slice;
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
        glas_cell* const cell = glas_cell_alloc();
        cell->hdr.type_id = GLAS_TYPE_SMALL_BIN;
        cell->hdr.type_arg = len;
        cell->hdr.type_aggr = 0;
        cell->stemHd = GLAS_STEM31_EMPTY;
        memcpy(cell->small_bin, data, len);
        return cell;
    } else {
        void* const addr = malloc(sizeof(_Atomic(size_t)) + len); // include space for refct
        atomic_init((_Atomic(size_t)*) addr, 1);
        uint8_t* const data_copy = (uint8_t*)(((uintptr_t)addr) + sizeof(_Atomic(size_t)));
        memcpy(data_copy,data,len);
        glas_refct const pin = { .refct_obj = addr, .refct_upd = glas_cell_binary_refct_upd };
        return glas_cell_binary_slice(data_copy, len, glas_cell_fptr(data_copy, pin, false));
    }
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
    (void)incref;
    free(base_ptr);
}
LOCAL glas_cell* glas_cell_array_slice(glas_cell** data, size_t len, uint8_t type_aggr, glas_cell* fptr) {
    assert(likely(GLAS_TYPE_FOREIGN_PTR == fptr->hdr.type_id));
    glas_cell* const slice = glas_cell_alloc();
    slice->hdr.type_id = GLAS_TYPE_BIG_ARR;
    slice->hdr.type_arg = 0;
    slice->hdr.type_aggr = type_aggr; 
    slice->stemHd = GLAS_STEM31_EMPTY;
    slice->big_arr.data = data;
    slice->big_arr.len = len;
    slice->big_arr.fptr = fptr;
    return slice;
}
LOCAL glas_cell* glas_cell_array_alloc(glas_cell** data, size_t len) {
    // array as a list
    if(len < 4) {
        if(0 == len) { return GLAS_VAL_UNIT; }
        glas_cell* const cell = glas_cell_alloc();
        cell->hdr.type_id = GLAS_TYPE_SMALL_ARR;
        cell->hdr.type_arg = len;
        cell->hdr.type_aggr = glas_cell_array_type_aggr(data, len);
        cell->stemHd = GLAS_STEM31_EMPTY;
        cell->small_arr[0] = data[0];
        cell->small_arr[1] = (len > 1) ? data[1] : GLAS_VOID;
        cell->small_arr[2] = (len > 2) ? data[2] : GLAS_VOID;
        return cell;
    } else {
        size_t const total_size = len * sizeof(glas_cell**);
        glas_cell** const data_copy = malloc(total_size);
        memcpy(data_copy, data, total_size);
        glas_refct const pin = { .refct_obj = data_copy, .refct_upd = glas_cell_array_free };
        return glas_cell_array_slice(data_copy, len, 
                    glas_cell_array_type_aggr(data_copy, len),
                    glas_cell_fptr(data_copy, pin, false));
    }
}
LOCAL glas_cell* glas_sc_to_cell(glas_sc sc);
LOCAL void glas_sc_fill_cell_stem_bits(glas_sc* sc);
LOCAL inline bool glas_cell_is_short_stem(glas_cell* cell) {
    // short stem of 0..31 bits only
    return (GLAS_DATA_IS_PTR(cell) &&
            (GLAS_TYPE_STEM == cell->hdr.type_id) &&
            (0 == cell->hdr.type_arg));
}
LOCAL void glas_sc_branch_prep_extract_short_stem(glas_sc* sc) {
    size_t const stem_shift = 1 + ctz64(sc->stem);
    size_t const stem_len = 64 - stem_shift;
    if((stem_len < 31) && glas_cell_is_short_stem(sc->cell)) {
        // we can sometimes eliminate a short GLAS_TYPE_STEM from a branch.
        size_t const short_stem_space = ctz32(sc->cell->stemHd);
        if(short_stem_space >= stem_len) {
            uint32_t const short_stem = ((sc->cell->stemHd) >> stem_len) 
                | (uint32_t)((sc->stem >> stem_shift) << (32 - stem_len));
            sc->stem = ((uint64_t)short_stem) << 32; // merge stem bits
            sc->cell = sc->cell->stem.fby; // remove short GLAS_TYPE_STEM
        }
    }
}
LOCAL void glas_sc_branch_prep_collapse_long_stem(glas_sc* sc) {
    glas_sc_fill_cell_stem_bits(sc); // pack bits from sc->stem into sc->cell
    size_t const stem_shift = 1 + ctz64(sc->stem);
    size_t const stem_len = 64 - stem_shift;
    if(stem_len > 31) { // stem too large, move to heap
        sc->cell = glas_sc_to_cell(*sc);
        sc->stem = GLAS_STEM63_EMPTY;
    }
}
LOCAL inline void glas_sc_branch_prep(glas_sc* sc) {
    // extract short GLAS_TYPE_STEM into sc->stem (up to 31 bits), or
    // push a long stem into the heap (if longer than 31 bits).
    glas_sc_branch_prep_extract_short_stem(sc);  
    glas_sc_branch_prep_collapse_long_stem(sc);
}
LOCAL glas_cell* glas_cell_pair_alloc_sc(glas_sc lhs, glas_sc rhs) {
    // TBD: packed pointers for shrubs or small binaries
    glas_sc_branch_prep(&lhs);
    glas_sc_branch_prep(&rhs);

    glas_cell* cell = glas_cell_alloc();
    cell->hdr.type_id = GLAS_TYPE_BRANCH;
    cell->hdr.type_arg = 0;
    cell->hdr.type_aggr = glas_type_aggr_comp(
        glas_cell_type_aggr(lhs.cell), glas_cell_type_aggr(rhs.cell));
    cell->stemHd = GLAS_STEM31_EMPTY;
    cell->branch.stemL = (uint32_t) (lhs.stem >> 32);
    cell->branch.stemR = (uint32_t) (rhs.stem >> 32);
    cell->branch.L = lhs.cell;
    cell->branch.R = rhs.cell;
    return cell;
}
LOCAL inline glas_cell* glas_cell_pair_alloc(glas_cell* lhs, glas_cell* rhs) {
    glas_sc sc_lhs = { .stem = GLAS_STEM63_EMPTY, .cell = lhs };
    glas_sc sc_rhs = { .stem = GLAS_STEM63_EMPTY, .cell = rhs };
    return glas_cell_pair_alloc_sc(sc_lhs, sc_rhs);
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
    assert(likely(GLAS_OS_THREAD_BUSY == t->state));
    if(t->busy_depth > 1) {
        (t->busy_depth)--;
        return;
    }
    glas_os_thread_force_exit_busy(t);
}
LOCAL void glas_os_thread_gc_safepoint_slowpath() {
    glas_os_thread* const t = glas_os_thread_get();
    assert(likely(GLAS_OS_THREAD_BUSY == t->state));
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
        debug("OS thread canceled while busy");
        glas_os_thread_force_exit_busy(t);
    }
    assert(GLAS_OS_THREAD_IDLE == t->state);
    if(NULL != t->alloc.page) {
        t->alloc.page->cycle_released = atomic_load_explicit(&glas_rt.gc.cycle, memory_order_relaxed);
        t->alloc.page = NULL;
    }
    t->state = GLAS_OS_THREAD_DONE;
}
LOCAL inline bool glas_gc_has_stopped_the_world() {
    bool const stopping = atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed);
    size_t const busy_threads = atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_relaxed);
    return (stopping && (0 == busy_threads));
}
LOCAL void glas_gc_stop_the_world() {
    assert(likely(!atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed)));
    atomic_store_explicit(&glas_rt.gc.stopping, true, memory_order_seq_cst);
    while(0 != atomic_load_explicit(&glas_rt.gc.busy_threads_count, memory_order_acquire)) {
        sem_wait(&glas_rt.gc.wakeup);
    }
}
LOCAL void glas_gc_resume_the_world() {
    assert(likely(glas_gc_has_stopped_the_world()));
    atomic_store_explicit(&glas_rt.gc.stopping, false, memory_order_release);
    for(glas_os_thread* t = atomic_load_explicit(&glas_rt.tls.list, memory_order_acquire);
        (NULL != t); t = t->next) 
    {
        if(GLAS_OS_THREAD_WAIT == t->state) {
            sem_post(&(t->wakeup));
        }
    }
}
LOCAL glas_gc_mb* glas_gc_mb_new() {
    glas_gc_mb* mb = malloc(sizeof(glas_gc_mb));
    mb->fill = 0;
    mb->arr.len = 0;
    mb->next = NULL;
    return mb;
}
LOCAL inline bool glas_gc_mb_is_empty(glas_gc_mb* mb) {
    return ((0 == mb->fill) && (0 == mb->arr.len));
}
LOCAL void glas_gc_mb_free(glas_gc_mb* mblist) {
    while(NULL != mblist) {
        glas_gc_mb* const mb = mblist;
        mblist = mblist->next;
        assert(likely(glas_gc_mb_is_empty(mb)));
        free(mb);
    }
}
LOCAL void glas_gc_mb_grow(glas_gc_mb** mbhd) {
    // We'll rotate up to two buffers locally to avoid touching the 
    // global shared list at the overflow boundary 
    assert(likely(GLAS_GC_CELL_BUFFSZ == (*mbhd)->fill));
    glas_gc_mb* mb = (*mbhd)->next;
    (*mbhd)->next = NULL;
    if(NULL == mb) {
        mb = glas_gc_mb_new();
    } else {
        assert(likely(NULL == mb->next)); // keep at most two local buffers
        if(glas_gc_mb_is_empty(mb)) {
            // recycle empty buffer, a no-op here
        } else {
            // two full buffers, push one to global list for work sharing
            assert(likely(GLAS_GC_CELL_BUFFSZ == mb->fill));
            atomic_pushlist(&glas_rt.gc.mb, &(mb->next), mb);
            mb = glas_gc_mb_new();
        }
    }
    mb->next = (*mbhd);
    (*mbhd) = mb;
}
LOCAL inline void glas_gc_mb_push(glas_gc_mb** mb, glas_cell* data) {
    if(GLAS_GC_CELL_BUFFSZ == (*mb)->fill) {
        glas_gc_mb_grow(mb);
    }
    (*mb)->buffer[((*mb)->fill)++] = data;
}
LOCAL glas_os_thread* glas_gc_extract_done_threads() {
    assert(likely(glas_gc_has_stopped_the_world()));
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
    assert(likely(glas_gc_has_stopped_the_world()));
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
    atomic_init(&(r->trace_cycle), 0);
    r->root_count = 0;
    r->max_offset = 0;
    for( ; (GLAS_ROOTS_END != (*roots)); ++roots ) {
        ((glas_cell**)self)[(*roots)] = GLAS_VOID;
        if((*roots) > r->max_offset) {
            r->max_offset = (*roots);
        }
        (r->root_count)++;
        assert((UINT16_MAX >= r->root_count) && "seem to be missing a sentinel");
    }
    atomic_fetch_add_explicit(&glas_rt.stat.roots_init, r->root_count, memory_order_relaxed);
    assert(likely((0 < r->root_count) && "why roots with no roots?"));

    // bitmap covers from 'self' to 'max_offset' inclusive
    size_t const bitmap_len = 8 * (1 + ((r->max_offset)/64));
    r->slot_bitmap = malloc(bitmap_len);

    // 'scan' roots while busy to lock down scan bit
    // also add to roots list while GC is blocked 
    glas_os_thread_enter_busy();
    memset(r->slot_bitmap, (glas_gc_b0scan() ? 0 : ~0), bitmap_len); // mark roots scanned
    atomic_pushlist(&glas_rt.root.list, &(r->next), r);
    glas_os_thread_exit_busy();
}
LOCAL void glas_roots_finalize(glas_roots* r) {
    atomic_fetch_add_explicit(&glas_rt.stat.roots_free, r->root_count, memory_order_relaxed);
    free(r->slot_bitmap);
    r->slot_bitmap = NULL;
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
LOCAL bool glas_wb_claim_roots_slot(glas_roots* r, glas_cell** slot) {
    glas_cell** const base = (glas_cell**) r->self;
    size_t const slot_ix = (size_t) (slot - base);
    uint64_t const bit = UINT64_C(1) << (slot_ix % 64);
    _Atomic(uint64_t)* const pbitmap = r->slot_bitmap + (slot_ix / 64);
    if(glas_gc_b0scan()) {
        uint64_t const prior = atomic_fetch_and_explicit(pbitmap, ~bit, memory_order_release);
        return (0 != (prior & bit));
    } else {
        uint64_t const prior = atomic_fetch_or_explicit(pbitmap, bit, memory_order_release);
        return (0 == (prior & bit));
    }
}
LOCAL bool glas_wb_claim_cell_slot(glas_cell* reg, glas_cell** slot) {
    glas_cell** const base = reg->small_arr;
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
    size_t const coff = cell - ((glas_cell*)page);
    _Atomic(uint64_t)* const pbitmap = page->marking + (coff / 64);
    uint64_t const bit = UINT64_C(1) << (coff % 64);
    uint64_t const prior = atomic_fetch_or_explicit(pbitmap, bit, memory_order_relaxed);
    return (0 == (prior & bit));
}
LOCAL inline void glas_wb_snapshot_push(glas_cell* cell) {
    // allocating a cell for every write-barrier snapshot; should be rare!
    // keep some stats on how often this happens
    atomic_fetch_add_explicit(&glas_rt.stat.gc_wb_resume, 1, memory_order_relaxed);
    glas_cell* const wb = glas_cell_alloc(); // just using as memory
    wb->small_arr[1] = cell;
    atomic_pushlist(&glas_rt.gc.wb, wb->small_arr, wb);
}
LOCAL inline void glas_wb_snapshot_sched(glas_cell* cell) {
    if(GLAS_DATA_IS_PTR(cell) && glas_gc_try_cell_mark(cell)) {
        glas_wb_snapshot_push(cell);
    }
}
LOCAL inline void glas_roots_slot_write(glas_roots* roots, glas_cell** slot, glas_cell* new_val) {
    if(glas_rt.gc.marking) {
        glas_cell* const prior_val = (*slot);
        if(glas_wb_claim_roots_slot(roots, slot)) {
            glas_wb_snapshot_sched(prior_val);
        }
    }
    (*slot) = new_val;
}
LOCAL inline void glas_cell_slot_write(glas_cell* dst, glas_cell** slot, glas_cell* new_val) {
    if(glas_rt.gc.marking) {
        glas_cell* const prior_val = (*slot);
        if(glas_wb_claim_cell_slot(dst, slot)) {
            glas_wb_snapshot_sched(prior_val);
        }
    }
    (*slot) = new_val;
}
LOCAL void glas_gc_dq_push(glas_gc_dq*, glas_refct);
LOCAL void glas_cell_finalize(glas_cell* cell) {
    glas_type_id const ty = cell->hdr.type_id;
    if(GLAS_TYPE_FOREIGN_PTR == ty) {
        glas_gc_dq_push(&glas_rt.gc.dq, cell->foreign_ptr.pin);
        cell->foreign_ptr.pin.refct_upd = NULL;
    } else if(GLAS_TYPE_REGISTER == ty) {
        atomic_store_explicit(&(cell->reg.ts->ts.wk), GLAS_VOID, memory_order_relaxed);
    } else {
        debug("unrecognized finalizer type: %d", (int)ty);
    }
}
LOCAL inline bool glas_cell_is_linear(glas_cell* cell) {
    return (GLAS_DATA_IS_PTR(cell) &&
            (0 != (GLAS_AGGR_LINEAR_FLAG & cell->hdr.type_aggr)));
}


/***************************
 * GARBAGE COLLECTOR
 ***************************/
LOCAL inline void glas_gc_mark_cell(glas_gc_mb** mb, glas_cell* cell) {
    if(GLAS_DATA_IS_PTR(cell) && glas_gc_try_cell_mark(cell)) {
        glas_gc_mb_push(mb, cell);
    }
}
LOCAL bool glas_gc_trace_find_work(glas_gc_mb** mbhead) {
    assert(likely(glas_gc_mb_is_empty(*mbhead)));

    glas_gc_mb* mb = (*mbhead)->next;
    (*mbhead)->next = NULL;
    if(NULL != mb) {
        if(glas_gc_mb_is_empty(mb)) {
            // two empty buffers, free one
            glas_gc_mb_free(mb);
            mb = NULL;
        } else {
            // rotate to full buffer
            assert(likely(GLAS_GC_CELL_BUFFSZ == mb->fill));
            mb->next = (*mbhead);
            (*mbhead) = mb;
            return true;
        }
    }

    // load from shared worklists if possible.
    pthread_mutex_lock(&glas_rt.gc.gc_mb_pop_mutex); // lock to resist ABA problem
    mb = atomic_load_explicit(&glas_rt.gc.mb, memory_order_acquire);
    do {} while((NULL != mb) && !atomic_compare_exchange_weak_explicit(&glas_rt.gc.mb, 
            &mb, mb->next, memory_order_acquire, memory_order_acquire));
    pthread_mutex_unlock(&glas_rt.gc.gc_mb_pop_mutex);
    if(NULL != mb) {
        mb->next = (*mbhead);
        (*mbhead) = mb;
        return true;
    }

    // fill from write barrier (cf glas_wb_snapshot_push)
    static size_t const fill_goal = GLAS_GC_CELL_BUFFSZ/2;
    glas_cell* wb = atomic_load_explicit(&glas_rt.gc.wb, memory_order_acquire);
    while((GLAS_VOID != wb) && (fill_goal > (*mbhead)->fill)) {
        if(atomic_compare_exchange_weak_explicit(&glas_rt.gc.wb, &wb, 
            wb->small_arr[0], memory_order_acquire, memory_order_acquire))
        {
            (*mbhead)->buffer[((*mbhead)->fill)++] = wb->small_arr[1];
        }
    }
    return (0 < (*mbhead)->fill);
}
LOCAL void glas_gc_trace_array(glas_gc_mb** mb, glas_cell** data, size_t len) {
    // Use dedicated slot for lazy processing of data arrays. But there
    // is only one, so we spread smaller array into mark buffer. If that
    // overflows, we can lazily process remainder in next mark buffer.
    glas_cell** spread_data;
    size_t spread_len;
    if(len > (*mb)->arr.len) {
        // keep new array, spread old one
        spread_data = (*mb)->arr.data;
        spread_len = (*mb)->arr.len;
        (*mb)->arr.data = data;
        (*mb)->arr.len = len;
    } else {
        // keep old array, spread new one
        spread_data = data;
        spread_len = len;
    }
    while((spread_len > 0) && (GLAS_GC_CELL_BUFFSZ > (*mb)->fill)) {
        // spread data into fill
        glas_gc_mark_cell(mb, spread_data[--spread_len]);
    }
    if(spread_len > 0) {
        // lazily process remaining array in next mark buffer
        glas_gc_mb_grow(mb);
        assert(likely(0 == (*mb)->arr.len));
        (*mb)->arr.len = spread_len;
        (*mb)->arr.data = spread_data;
    }
}
LOCAL inline bool glas_cell_is_dead_tombstone(glas_cell* cell) {
    return (GLAS_DATA_IS_PTR(cell) && 
            (GLAS_TYPE_TOMBSTONE == cell->hdr.type_id) && 
            (GLAS_VOID == cell->ts.wk));
}
LOCAL void glas_gc_trace_cell(glas_gc_mb** mb, glas_cell* cell) {
    static_assert(sizeof(glas_cell*) == sizeof(_Atomic(glas_cell*)));
    static_assert(8 == sizeof(glas_cell*));
    glas_cell const cpy = (*cell); // claim snapshot
    uint8_t claim; // slots not already claimed by write barrier
    if(glas_gc_b0scan()) {
        uint8_t const prior = atomic_fetch_and_explicit(&(cell->hdr.gcbits), 
            ~(GLAS_GCBITS_SCAN), memory_order_release);
        claim = GLAS_GCBITS_SCAN & prior; // prior 1 bit is claimed
    } else {
        uint8_t const prior = atomic_fetch_or_explicit(&(cell->hdr.gcbits), 
            GLAS_GCBITS_SCAN, memory_order_release);
        claim = GLAS_GCBITS_SCAN & ~prior; // prior 0 bit is claimed
    }
    #define GLAS_CELL_SLOT_INDEX(Field)     ((offsetof(glas_cell, Field)/sizeof(glas_cell*))-1)
    #define GLAS_CELL_SLOT_CLAIMED(Field)   (0 != (claim & (1 << GLAS_CELL_SLOT_INDEX(Field))))
    #define GLAS_CELL_SLOT_MARK(Field)\
        if(GLAS_CELL_SLOT_CLAIMED(Field)) {\
            glas_gc_mark_cell(mb, cpy.Field);\
        }
    #define GLAS_CELL_SLOT_MARK_ATOMIC(Field)\
        if(GLAS_CELL_SLOT_CLAIMED(Field)) {\
            glas_gc_mark_cell(mb, atomic_load_explicit(&(cpy.Field), memory_order_relaxed));\
        }
    
    switch(cell->hdr.type_id) {
        case GLAS_TYPE_BRANCH:
            GLAS_CELL_SLOT_MARK(branch.L);
            GLAS_CELL_SLOT_MARK(branch.R);
            return;
        case GLAS_TYPE_STEM:
            GLAS_CELL_SLOT_MARK(stem.fby);
            return;
        case GLAS_TYPE_SMALL_ARR:
            GLAS_CELL_SLOT_MARK(small_arr[0]);
            GLAS_CELL_SLOT_MARK(small_arr[1]);
            GLAS_CELL_SLOT_MARK(small_arr[2]);
            return;
        case GLAS_TYPE_BIG_ARR:
            GLAS_CELL_SLOT_MARK(big_arr.fptr);
            glas_gc_trace_array(mb, cell->big_arr.data, cell->big_arr.len);
            return;
        case GLAS_TYPE_BIG_BIN:
            GLAS_CELL_SLOT_MARK(big_bin.fptr);
            return;
        case GLAS_TYPE_EXTREF:
            GLAS_CELL_SLOT_MARK(extref.ref);
            GLAS_CELL_SLOT_MARK(extref.ts);
            return;
        case GLAS_TYPE_THUNK:
            // Note: GC doesn't erase thunks
            GLAS_CELL_SLOT_MARK_ATOMIC(thunk.claim);
            GLAS_CELL_SLOT_MARK_ATOMIC(thunk.closure);
            GLAS_CELL_SLOT_MARK_ATOMIC(thunk.result);
            return;
        case GLAS_TYPE_SEAL:
            GLAS_CELL_SLOT_MARK(seal.key);
            GLAS_CELL_SLOT_MARK(seal.meta);
            if(glas_cell_is_dead_tombstone(cell->seal.key)) {
                // special case: seal as ephemeron
                cell->seal.data = GLAS_VOID;
            } else {
                GLAS_CELL_SLOT_MARK(seal.data);
            }
            return;
        case GLAS_TYPE_REGISTER:
            GLAS_CELL_SLOT_MARK_ATOMIC(reg.version);
            GLAS_CELL_SLOT_MARK_ATOMIC(reg.assoc_lhs);
            GLAS_CELL_SLOT_MARK_ATOMIC(reg.ts);
            return;
        case GLAS_TYPE_TAKE_CONCAT:
            GLAS_CELL_SLOT_MARK(take_concat.left);
            GLAS_CELL_SLOT_MARK(take_concat.right);
            return;
        case GLAS_TYPE_SMALL_BIN:
        case GLAS_TYPE_FOREIGN_PTR:
        case GLAS_TYPE_TOMBSTONE:
            // no-op
            return;
    }
    #undef GLAS_CELL_SLOT_MARK_ATOMIC
    #undef GLAS_CELL_SLOT_MARK
    #undef GLAS_CELL_SLOT_CLAIMED
    #undef GLAS_CELL_SLOT_INDEX
    debug("unhandled cell type: %d", (int) cell->hdr.type_id);
    abort();
}
LOCAL inline void glas_gc_trace_marked_cells(glas_gc_mb** mb) {
    do {
        if(0 < (*mb)->fill) {
            glas_gc_trace_cell(mb, (*mb)->buffer[--((*mb)->fill)]);
        } else if(0 < (*mb)->arr.len) {
            // lazy tracing for arrays            
            glas_gc_mark_cell(mb, (*mb)->arr.data[--((*mb)->arr.len)]);
        } else if(!glas_gc_trace_find_work(mb)) {
            break;
        }
    } while(1);
}
LOCAL void glas_gc_trace_roots(glas_gc_mb** mb, glas_roots* r) {
    glas_cell** const base = (glas_cell**) r->self;
    size_t root_ix = 0;
    while(root_ix < r->root_count) {
        size_t const start_offset = r->roots[root_ix];
        // batch clusters of roots that share a bitmap.
        size_t const bitmap_ix = start_offset / 64;
        glas_cell* snapshot[64]; // max 64 roots, usually fewer
        snapshot[0] = base[start_offset];
        uint64_t bitmask = (UINT64_C(1)<<(start_offset % 64));
        size_t count = 1;
        while((root_ix + count) < r->root_count) {
            uint16_t const offset = r->roots[root_ix + count];
            if(bitmap_ix != (offset/64)) {
                break;
            }
            snapshot[count] = base[offset]; 
            bitmask |= (UINT64_C(1) << (offset % 64));
            count++;
        }
        assert(likely(count > 0));
        // atomic claim for bitmap (racing with write barriers)
        uint64_t claimed;
        if(glas_gc_b0scan()) { // 0 bit means 'scanned' this phase.
            uint64_t const prior = atomic_fetch_and_explicit((r->slot_bitmap + bitmap_ix), ~bitmask, memory_order_release);
            claimed = bitmask & prior; // prior 1 bit is unscanned
        } else {
            uint64_t const prior = atomic_fetch_or_explicit((r->slot_bitmap + bitmap_ix), bitmask, memory_order_release);
            claimed = bitmask & ~prior; // prior 0 bit is unscanned
        }
        // mark our claim
        for(size_t j = 0; j < count; ++j) {
            uint16_t offset = r->roots[root_ix + j];
            if(0 != (claimed & (UINT64_C(1) << (offset % 64)))) {
                glas_gc_mark_cell(mb, snapshot[j]);
            }
        }
        root_ix += count;
        glas_gc_trace_marked_cells(mb);
    }
}
LOCAL void glas_gc_thread_stripe_trace(glas_gc_mb** mb) {
    // all threads scan same list, but race to claim roots and begin tracing.
    uint64_t const cycle = atomic_load_explicit(&glas_rt.gc.cycle, memory_order_acquire);
    for(glas_roots* r = glas_rt.gc.roots_snapshot; (NULL != r); r = r->next) 
    {
        uint64_t const prior_trace_cycle = 
            atomic_exchange_explicit(&(r->trace_cycle), cycle, memory_order_relaxed);
        assert(likely(cycle >= prior_trace_cycle));
        if(cycle != prior_trace_cycle) {
            glas_gc_trace_roots(mb, r);
        }
    }
}
LOCAL bool glas_gc_thread_try_finalize_cell(glas_cell* cell) {
    glas_page* const page = glas_page_from_internal_addr(cell);
    size_t const coff = cell - ((glas_cell*)page);
    uint64_t const bitmap = atomic_load_explicit(page->marked + (coff/64), memory_order_relaxed);
    uint64_t bit = UINT64_C(1) << (coff%64);
    if(0 != (bit & bitmap)) {
        return false; // marked
    }
    glas_cell_finalize(cell);
    return true;
}
LOCAL void glas_gc_thread_run_finalizers(glas_gc_fl* const fl_start) {
    glas_gc_fl* fl = fl_start;
    while(NULL != fl) {
        size_t ix = 0;
        while(ix < fl->fill) {
            bool const finalized = glas_gc_thread_try_finalize_cell(fl->buffer[ix]);
            if(finalized) {
                fl->buffer[ix] = fl->buffer[--(fl->fill)];
            } else {
                ++ix;
            }
        }
        fl = fl->next;
    }
    glas_gc_fl_compact(fl_start);
}

LOCAL void* glas_gc_worker_thread(void* arg) {
    (void)arg;
    glas_gc_mb* mb = glas_gc_mb_new();
    do {
        assert(likely(glas_gc_mb_is_empty(mb) && (NULL == mb->next)));
        atomic_fetch_add_explicit(&glas_rt.gc.pool.done, 1, memory_order_release);
        sem_wait(&glas_rt.gc.pool.wakeup);
        assert(likely(glas_rt.gc.marking));
        glas_gc_thread_stripe_trace(&mb);
        size_t idle = 0; 
        do {
            sched_yield();
            if(glas_gc_trace_find_work(&mb)) {
                glas_gc_trace_marked_cells(&mb);
            } else {
                ++idle;
            }
        } while(idle < GLAS_GC_THREAD_IDLE_CYCLES);
        assert(likely(glas_rt.gc.marking));
    } while(1);
    __builtin_unreachable();
}

static inline size_t num_cpus() {
    return sysconf(_SC_NPROCESSORS_ONLN);
}
LOCAL size_t glas_gc_decide_worker_count() {
    size_t const ncpus = num_cpus();
    size_t gc_thread_count = 1 + (ncpus/2); // includes main gc thread
    if(gc_thread_count > GLAS_GC_THREADS_MAX) {
        gc_thread_count = GLAS_GC_THREADS_MAX;
    }
    char const* const env_glas_gc_threads = getenv("GLAS_GC_THREADS");
    if(NULL != env_glas_gc_threads) {
        int const n = atoi(env_glas_gc_threads);
        if(n < 1) {
            debug("invalid value: GLAS_GC_THREADS=%s", env_glas_gc_threads);
        } else if(n > (int) ncpus) {
            debug("GLAS_GC_THREADS=%d > %lu CPUs; reducing", n, ncpus);
            gc_thread_count = ncpus;
        } else {
            gc_thread_count = n;
        }
    }
    assert(likely(gc_thread_count > 0));
    return (gc_thread_count - 1); // not counting main GC thread
}
LOCAL inline bool glas_gc_workers_are_done() {
    return (glas_rt.gc.pool.count == 
                atomic_load_explicit(&glas_rt.gc.pool.done, memory_order_acquire));
}
LOCAL inline void glas_gc_workers_signal() {
    assert(likely(glas_gc_workers_are_done()));
    atomic_store_explicit(&glas_rt.gc.pool.done, 0, memory_order_relaxed);
    for(size_t ix = 0; ix < glas_rt.gc.pool.count; ++ix) {
        sem_post(&glas_rt.gc.pool.wakeup);
    }
}
LOCAL void glas_gc_workers_init() {
    atomic_init(&glas_rt.gc.pool.done, 0);
    sem_init(&glas_rt.gc.pool.wakeup,0,0);
    glas_rt.gc.pool.count = glas_gc_decide_worker_count();
    if(0 == glas_rt.gc.pool.count) {
        return;
    }
    glas_rt.gc.pool.workers = calloc(glas_rt.gc.pool.count, sizeof(pthread_t));
    pthread_attr_t gc_worker_attr;
    pthread_attr_init(&gc_worker_attr);
    pthread_attr_setscope(&gc_worker_attr, PTHREAD_SCOPE_PROCESS);
    pthread_attr_setdetachstate(&gc_worker_attr, PTHREAD_CREATE_DETACHED);
    pthread_attr_setstacksize(&gc_worker_attr, (7 * 4096)); // small stack 
    pthread_attr_setguardsize(&gc_worker_attr, (1 * 4096));
    for(size_t ix = 0; ix < glas_rt.gc.pool.count; ++ix) {
        pthread_create((glas_rt.gc.pool.workers + ix), &gc_worker_attr,
            &glas_gc_worker_thread, NULL);
    }
    pthread_attr_destroy(&gc_worker_attr);

    // wait for GC workers to be ready
    while(!glas_gc_workers_are_done()) {
        sched_yield();
    }
}


LOCAL inline bool glas_gc_dq_is_full(glas_gc_dq const* dq) {
    return ((dq->tail + 1) % dq->capacity) == dq->head;
}
LOCAL inline bool glas_gc_dq_is_empty(glas_gc_dq const* dq) {
    return (dq->tail == dq->head);
}
LOCAL inline size_t glas_gc_dq_size(glas_gc_dq const* dq) {
    return (dq->tail >= dq->head) ? (dq->tail - dq->head) :
            (dq->capacity - (dq->head - dq->tail));
}
LOCAL void glas_gc_dq_grow(glas_gc_dq* dq, size_t new_cap) {
    assert(likely(new_cap > glas_gc_dq_size(dq)));
    glas_refct* const new_items = malloc(new_cap * sizeof(glas_refct));
    size_t new_tail = 0;
    while(dq->head != dq->tail) {
        new_items[new_tail++] = dq->items[dq->head];
        dq->head = (dq->head + 1) % dq->capacity;
    }
    free(dq->items);
    dq->items = new_items;
    dq->capacity = new_cap;
    dq->tail = new_tail;
    dq->head = 0;
}
LOCAL void glas_gc_dq_push(glas_gc_dq* dq, glas_refct refct) {
    pthread_mutex_lock(&dq->mutex);
    bool const send_wakeup = glas_gc_dq_is_empty(dq);
    if(glas_gc_dq_is_full(dq)) {
        glas_gc_dq_grow(dq, 2 * dq->capacity);
    }
    dq->items[dq->tail] = refct;
    dq->tail = (dq->tail + 1) % dq->capacity;
    pthread_mutex_unlock(&glas_rt.gc.dq.mutex);
    if(send_wakeup) {
        sem_post(&dq->wakeup);
    }
}
LOCAL inline bool glas_gc_dq_pop(glas_gc_dq* dq, glas_refct* refct) {
    pthread_mutex_lock(&dq->mutex);
    bool const item_found = !glas_gc_dq_is_empty(dq);
    if(item_found) {
        (*refct) = dq->items[dq->head];
        dq->head = (dq->head + 1) % dq->capacity;
    } else {
        refct->refct_upd = NULL;
    }
    pthread_mutex_unlock(&dq->mutex);
    return item_found;
}
LOCAL void* glas_gc_dq_worker(void* addr) {
    glas_gc_dq* const dq = addr;
    glas_refct refct;
    do {
        sem_wait(&dq->wakeup);
        do {} while(0 == sem_trywait(&dq->wakeup)); // drain extra wakeups
        while(glas_gc_dq_pop(dq, &refct)) {
            glas_decref(refct);
        }
    } while(1);
    __builtin_unreachable();
}
LOCAL void glas_gc_dq_init(glas_gc_dq* dq) {
    pthread_mutex_init(&dq->mutex, NULL);
    sem_init(&dq->wakeup, 0, 0);
    #if DEBUG
    dq->capacity = 1; // force growth
    dq->items = NULL;
    #else
    dq->capacity = 512; // unlikely to grow
    dq->items = malloc(sizeof(glas_refct) * dq->capacity);
    #endif
    dq->head = 0;
    dq->tail = 0;

    // note: I don't want a small stack in this case because I don't
    // know what insane things the API client will do upon decref.
    pthread_create(&dq->thread, NULL, &glas_gc_dq_worker, dq);
}

LOCAL bool glas_gc_heuristic_level() {
    if(atomic_exchange_explicit(&glas_rt.gc.force_fullgc, false, memory_order_relaxed)) {
        return true; 
    }
    size_t const curr_roots = atomic_load_explicit(&glas_rt.stat.roots_init, memory_order_relaxed);
    size_t const curr_pages = atomic_load_explicit(&glas_rt.stat.page_release, memory_order_relaxed);
    static size_t const roots_gc_thresh = 1024;
    static size_t const pages_gc_thresh = 32;
    if(curr_roots > (roots_gc_thresh + glas_rt.gc.prior_root_ct)) {
        return true; // need handle some external garbage
    }
    if(curr_pages < (pages_gc_thresh + glas_rt.gc.prior_page_ct)) {
        return false; // mutators are more or less idle
    }
    size_t const avail_count = atomic_load_explicit(&glas_rt.alloc.avail.page_count, memory_order_relaxed);
    size_t const await_count = atomic_load_explicit(&glas_rt.alloc.await.page_count, memory_order_relaxed);
    if(avail_count > (await_count/3)) {
        return false; // still have a lot of available pages
    }
    return true;
}
LOCAL inline void glas_page_swap_marked_marking(glas_page* page) {
    _Atomic(uint64_t)* const tmp = page->marking;
    page->marking = page->marked;
    page->marked = tmp;
}
LOCAL inline void glas_page_clear_marking(glas_page* page) {
    memset(page->marking, 0, GLAS_PAGE_CELL_COUNT/8);
}
LOCAL inline bool glas_gc_heuristic_decide_page_recycle(glas_page* page) {
    if(page->cycle_acquired > page->cycle_released) { return false; }
    if(page->defer_reuse > 0) {
        --(page->defer_reuse);
        return false;
    }
    return true;
}
LOCAL void glas_gc_pages_include(glas_page* page) {
    // rebuild a 'gc_next' list of pages
    while(NULL != page) {
        page->gc_next = glas_rt.gc.pages;
        glas_rt.gc.pages = page;
        page = page->next;
    }
}
LOCAL void* glas_gc_main_thread(void* arg) {
    (void)arg; // unused
    glas_gc_workers_init();
    glas_gc_dq_init(&glas_rt.gc.dq);
    glas_gc_mb* mb = glas_gc_mb_new();
    do {
        // check some assumptions
        assert(!glas_os_thread_is_busy() &&
            !atomic_load_explicit(&glas_rt.gc.stopping, memory_order_relaxed) &&
            !glas_rt.gc.marking && glas_gc_mb_is_empty(mb) &&
            (NULL == atomic_load_explicit(&glas_rt.gc.mb, memory_order_relaxed)) &&
            (GLAS_VOID == atomic_load_explicit(&glas_rt.gc.wb, memory_order_relaxed)) &&
            (NULL == glas_rt.gc.roots_snapshot) &&
            glas_gc_workers_are_done());

        // skip sleep if GC request is pending, otherwise wait for timer or wakeup
        if(!atomic_exchange_explicit(&glas_rt.gc.signal_gc, false, memory_order_relaxed)) {
            static struct timespec const gc_poll_period = { .tv_nsec = (GLAS_GC_POLL_USEC * 1000) };
            sem_timedwait(&glas_rt.gc.wakeup, &gc_poll_period);
        }
        do {} while(0 == sem_trywait(&glas_rt.gc.wakeup)); // drain extra wakeups

        if(!glas_gc_heuristic_level()) { 
            continue; 
        }

        // clear old marks? We'll handle that after swap, before returning pages to avail pool
        glas_gc_stop_the_world();
        // flip scan bits and activate write barrier
        glas_rt.gc.gcbits = glas_gc_b0scan() ? 0b111 : 0; 
        glas_rt.gc.marking = true;

        // gather some stats to help with heuristic decisions
        glas_rt.gc.prior_page_ct = atomic_load_explicit(&glas_rt.stat.page_release, memory_order_relaxed);
        glas_rt.gc.prior_root_ct = atomic_load_explicit(&glas_rt.stat.roots_init, memory_order_relaxed);

        // touch mutator threads, grab finalizers
        for(glas_os_thread* t = atomic_load_explicit(&glas_rt.tls.list, memory_order_acquire); 
            (NULL != t); t = t->next) 
        {
            assert(likely((GLAS_OS_THREAD_BUSY != t->state)));
            if(NULL != t->fl) { // grab recently registered finalizers
                assert(likely(NULL == t->fl->next));
                atomic_pushlist(&glas_rt.gc.fl, &(t->fl->next), t->fl);
                t->fl = NULL;
            }
        }
        
        // garbage outside the heaps
        glas_os_thread* tdone = glas_gc_extract_done_threads();
        glas_roots* rdetached = glas_gc_extract_detached_roots();

        // snapshot
        glas_rt.gc.roots_snapshot = atomic_load_explicit(&glas_rt.root.list, memory_order_relaxed);
        glas_cell* const conf = atomic_load_explicit(&glas_rt.root.conf, memory_order_relaxed);
        glas_cell* const globals = atomic_load_explicit(&glas_rt.root.globals, memory_order_relaxed);
        glas_gc_fl* const fl = atomic_load_explicit(&glas_rt.gc.fl, memory_order_acquire);

        // we'll only recycle pages in the 'await' list BEFORE concurrent marking;
        // pages added during concurrent mark are filled with new allocations

        // just grab them all, we'll process this list after marking completes
        glas_page* recycle_pages = atomic_exchange_explicit(&glas_rt.alloc.await.page_list, NULL, memory_order_relaxed);
        atomic_store_explicit(&glas_rt.alloc.await.page_count, 0, memory_order_relaxed);

        // update GC cycle
        atomic_fetch_add_explicit(&glas_rt.gc.cycle, 1, memory_order_release);
        glas_gc_resume_the_world();

        // initiate parallel marking
        glas_gc_workers_signal(); 

        // handle main thread's share of marking
        glas_gc_mark_cell(&mb, conf);
        glas_gc_mark_cell(&mb, globals);
        glas_gc_trace_marked_cells(&mb);
        glas_gc_thread_stripe_trace(&mb);

        // clear C heap garbage
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

        // if workers are still busy, help them. I also want a final
        // trace near stop to better win races with write barriers
        glas_gc_trace_marked_cells(&mb);
        while(!glas_gc_workers_are_done()) {
            sched_yield();
            glas_gc_trace_marked_cells(&mb);
        }

        glas_gc_stop_the_world();
        if(glas_gc_trace_find_work(&mb)) {
            // handle last-second marks from mutator write-barriers
            glas_gc_resume_the_world(&mb);
            atomic_fetch_add_explicit(&glas_rt.stat.gc_wb_resume, 1, memory_order_relaxed);
            glas_gc_trace_marked_cells(&mb);
            glas_gc_stop_the_world(&mb);
            if(glas_gc_trace_find_work(&mb)) {
                // it happened again, this time just finish tracing while stopped
                atomic_fetch_add_explicit(&glas_rt.stat.gc_wb_stop, 1, memory_order_relaxed);
                glas_gc_trace_marked_cells(&mb);
            }
        }
        // build complete list of pages
        glas_rt.gc.pages = NULL;
        glas_gc_pages_include(atomic_load_explicit(&glas_rt.alloc.avail.page_list, memory_order_acquire));
        glas_gc_pages_include(atomic_load_explicit(&glas_rt.alloc.await.page_list, memory_order_acquire));
        glas_gc_pages_include(recycle_pages);
        // swap the marked and marking buffers
        for(glas_page* page = glas_rt.gc.pages; (NULL != page); page = page->gc_next) {
            glas_page_swap_marked_marking(page);
        }
        // marking completed!
        glas_rt.gc.roots_snapshot = NULL;
        glas_rt.gc.marking = false;
        glas_gc_resume_the_world();
        
        // must run finalizers before recycling pages
        glas_gc_thread_run_finalizers(fl);

        // recycle pages
        while(NULL != recycle_pages) {
            glas_page* const page = recycle_pages;
            recycle_pages = page->next;
            page->next = NULL;
            bool const recycle = glas_gc_heuristic_decide_page_recycle(page);
            glas_alloc_l* const dst = recycle ? &glas_rt.alloc.avail : &glas_rt.alloc.await;
            glas_allocl_push(dst, page);
        }

        // prepare for next GC cycle by clearing the marking bitmaps. The 'marked'
        // bitmap remains for lazy allocation on sweep
        for(glas_page* page = glas_rt.gc.pages; (NULL != page); page = page->gc_next) {
            glas_page_clear_marking(page);
        }
    } while(1);
    __builtin_unreachable();
}
LOCAL void glas_gc_thread_init() {
    // Create main GC thread here. It may create workers internally.
    pthread_attr_t gc_thread_attr;
    pthread_attr_init(&gc_thread_attr);
    pthread_attr_setscope(&gc_thread_attr, PTHREAD_SCOPE_PROCESS);
    pthread_attr_setdetachstate(&gc_thread_attr, PTHREAD_CREATE_DETACHED);
    pthread_attr_setstacksize(&gc_thread_attr, (4096 * 7));
    pthread_attr_setguardsize(&gc_thread_attr, (4096 * 1));
    pthread_create(&glas_rt.gc.gc_main_thread, &gc_thread_attr, &glas_gc_main_thread, NULL);
    pthread_attr_destroy(&gc_thread_attr);
}







LOCAL uint64_t glas_shrub_skip_elem(uint64_t shrub) {
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
LOCAL uint64_t glas_cell_shrub_cons(glas_cell* cell, uint64_t shrub) {
    static_assert(sizeof(glas_cell*) == sizeof(uint64_t));
    shrub = GLAS_SHRUB_MKP_SEP(shrub);
    if(GLAS_DATA_IS_BITS(cell)) {
        uint64_t stem = ((uint64_t)cell) & ~UINT64_C(0b11);
        assert(likely(0 != stem));
        size_t const shift = ctz64(stem) + 1;
        size_t const len = 64 - shift;
        stem = stem >> shift;
        for(size_t ix = 0; ix < len; ++ix) {
            if(0 == (stem & 0b1)) {
                shrub = GLAS_SHRUB_MKL(shrub);
            } else {
                shrub = GLAS_SHRUB_MKR(shrub);
            }
            stem = stem >> 1;
            assert(likely((0b00 == (0b11 & shrub)) && "shrub overflow"));
        }
    } else if(GLAS_DATA_IS_SHRUB(cell)) {
        uint64_t lhs = GLAS_DATA_SHRUB_BITS(cell);
        size_t const shift = ctz64(lhs) & ~1; // mask for `10` edge
        lhs = lhs >> shift;
        while(0 != lhs) {
            shrub = (shrub >> 2) | ((0b11 & lhs)<<62);
            lhs = lhs >> 2;
            assert(likely((0b00 == (0b11 & shrub)) && "shrub overflow"));
        }
    } else {
        debug("unhandled cell type for shrub cons");
        abort();
    }
    shrub = GLAS_SHRUB_MKP_HD(shrub);
    return shrub;
}
LOCAL bool glas_cell_shrub_fits(glas_cell* cell, size_t nbits) {
    if(GLAS_DATA_IS_BITS(cell)) {
        uint64_t const bits = ((uint64_t)cell) & ~UINT64_C(0b11);
        assert(likely(0 != bits));
        size_t const len = 63 - ctz64(bits);
        return (nbits >= (2 * len));
    } else if(GLAS_DATA_IS_SHRUB(cell)) {
        uint64_t const bits = GLAS_DATA_SHRUB_BITS(cell);
        size_t const size = 64 - (ctz64(bits) & ~1);
        return (nbits >= size);
    } else {
        // I could probably squeeze some small ratios or binaries into 
        // a shrub. But it probably isn't worthwhile.
        return false;
    }
}




/*************************
 * Main API Ops?
 */


LOCAL void glas_thread_state_free(void* addr) {
    atomic_fetch_add_explicit(&glas_rt.stat.g_ts_free, 1, memory_order_relaxed);
    free(addr); 
}
LOCAL inline glas_thread_state* glas_thread_state_new() {
    atomic_fetch_add_explicit(&glas_rt.stat.g_ts_alloc, 1, memory_order_relaxed);
    glas_thread_state* const ts = malloc(sizeof(glas_thread_state));
    glas_roots_init(&(ts->gcbase), ts, glas_thread_state_free, glas_thread_state_offsets);
    ts->stack.count = 0;
    ts->stack.overflow = GLAS_VAL_UNIT;
    ts->stash.count = 0;
    ts->stash.overflow = GLAS_VAL_UNIT;
    ts->debug_name = GLAS_VAL_UNIT;
    ts->ns = GLAS_VAL_UNIT;
    return ts;
}
LOCAL inline void glas_stack_copy(glas_stack* dst, glas_stack* src) {
    for(size_t ix = 0; ix < src->count; ++ix) {
        dst->data[ix] = src->data[ix];
    }
    dst->count = src->count;
    dst->overflow = src->overflow;
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
    if(g->has_abort_handlers) {
        debug("TODO: run on_abort handlers");
    }
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
    glas_rt_init();
    atomic_fetch_add_explicit(&glas_rt.stat.g_alloc, 1, memory_order_relaxed);
    glas* const g = calloc(1,sizeof(glas));
    g->state = glas_thread_state_new();
    g->step_start = glas_thread_state_clone(g->state);
    g->has_abort_handlers = false; // stand-in for now
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
API GLAS_ERROR_FLAGS glas_errors_read(glas* g) {
    return g->err;
}

LOCAL glas_cell* glas_data_list_append(glas_cell* lhs, glas_cell* rhs) {
    if(GLAS_VAL_UNIT == lhs) { return rhs; }
    if(GLAS_VAL_UNIT == rhs) { return lhs; }
    debug("todo: list append");
    return GLAS_VOID;
}
LOCAL void glas_sc_fill_cell_stem_bits(glas_sc* sc) {
    if(GLAS_STEM63_EMPTY == sc->stem) { return; }

    // move stem bits from sc->stem to sc->cell, but without increasing
    // depth of sc->cell. Can allocate a clone while maintaining depth.
    assert(likely(0 != sc->stem));
    size_t const shift = ctz64(sc->stem) + 1;
    size_t len = 64 - shift;
    uint64_t stem_bits = sc->stem >> shift;
    glas_cell* cell = sc->cell;

    if(GLAS_DATA_IS_PTR(cell)) {
        // pack a few more bits into clone of cell
        bool const stem_full = (0 != (0b1 & cell->stemHd)) 
            && !((GLAS_TYPE_STEM == cell->hdr.type_id) && (4 > cell->hdr.type_arg));
        if(!stem_full) {
            cell = glas_cell_clone(cell);
            do {
                size_t const space = ctz32(cell->stemHd);
                if(space > 0) {
                    size_t const split = (space > len) ? len : space;
                    cell->stemHd = (cell->stemHd >> split) | (uint32_t)(stem_bits << (32 - split));
                    stem_bits = stem_bits >> split;
                    len = len - split;
                }
                if(0 == len) { break; }
                else if((GLAS_TYPE_STEM == cell->hdr.type_id) && (4 > cell->hdr.type_arg)) {
                    cell->stem.stem32[(cell->hdr.type_arg)++] = (cell->stemHd >> 1) | (uint32_t)(stem_bits << 31);
                    cell->stemHd = GLAS_STEM31_EMPTY;
                    stem_bits = stem_bits >> 1;
                    len--;
                } else { break; }
            } while(1);
        }
    } else if(GLAS_DATA_IS_BITS(cell)) {
        // try to pack a few more bits into pointer
        uint64_t const packed_bits = ((uint64_t)cell) & ~UINT64_C(0b11);
        assert(likely(0 != packed_bits));
        size_t const space = ctz64(packed_bits) - 2;
        if(space > 0) {
            size_t const split = (space > len) ? len : space;
            cell = (glas_cell*)((packed_bits >> split) | (stem_bits << (64 - split)) | UINT64_C(0b01));
            len -= split;
            stem_bits = stem_bits >> split;
        }
    } else if(GLAS_DATA_IS_SHRUB(cell)) {
        // try to add a few bits to shrub (2 bits to encode 1 bit)
        uint64_t shrub = GLAS_DATA_SHRUB_BITS(cell);
        while((len > 0) && (0 == (0b1111 & shrub))) {
            if(0 == (0b1 & stem_bits)) {
                shrub = GLAS_SHRUB_MKL(shrub);
            } else {
                shrub = GLAS_SHRUB_MKR(shrub);
            }
            len--;
        }
        cell = (glas_cell*)(shrub | 0b10);
    }
    sc->stem = ((stem_bits<<1)|1)<<(63-len);
    sc->cell = cell;
}
LOCAL void glas_stem_sc_push(uint64_t bits, glas_sc* sc) {
    assert(likely((0 != bits) && (GLAS_STEM63_EMPTY != bits) && (0 != sc->stem)));
    // bits and sc->stem each encode 0..63 bits
    // push bits into sc->stem if possible
    size_t const bshift = 1 + ctz64(bits);
    size_t blen = 64 - bshift;
    bits = bits >> bshift;
    size_t const space = ctz64(sc->stem);
    if(space > 0) {
        size_t const split = (blen > space) ? space : blen;
        sc->stem = (sc->stem >> split) | (bits << (64 - split));
        bits = bits >> split;
        blen -= split;
    }
    if(0 != blen) { 
        // sc->stem is full, move bits into sc->cell if possible.
        glas_sc_fill_cell_stem_bits(sc);
        size_t const space = ctz64(sc->stem);
        if(space > 0) {
            // opened some space in sc->stem, fill it.
            size_t const split = (blen > space) ? space : blen;
            sc->stem = (sc->stem >> split) | (bits << (64 - split));
            bits = bits >> split;
            blen -= split;
        }
        if(0 != blen) {
            assert(likely(0 != (1 & sc->stem))); // full sc->stem
            // at this point, sc->stem and sc->cell are full, but still
            // have some bits to store. To keep it simple, move 63 bits
            // into a new GLAS_TYPE_STEM cell.
            glas_cell* const cell = glas_cell_alloc();
            cell->hdr.type_id = GLAS_TYPE_STEM;
            cell->hdr.type_aggr = glas_cell_type_aggr(sc->cell);
            cell->hdr.type_arg = 1;
            cell->stem.fby = sc->cell;
            cell->stem.stem32[0] = (uint32_t)(sc->stem>>1); // lower 32 bits
            cell->stemHd = ((uint32_t)(sc->stem>>32))|1; // upper 31 bits

            sc->cell = cell;
            sc->stem = ((bits << 1)|1) << (63-blen);
        }
    }
}
LOCAL glas_cell* glas_sc_to_cell(glas_sc sc) {
    if(GLAS_STEM63_EMPTY == sc.stem) { return sc.cell; }
    glas_sc_fill_cell_stem_bits(&sc);
    if(GLAS_STEM63_EMPTY == sc.stem) { return sc.cell; }
    assert(likely(0 != sc.stem));
    size_t const shift = 1 + ctz64(sc.stem);
    size_t len = 64 - shift;
    uint64_t bits = sc.stem >> shift;

    glas_cell* const cell = glas_cell_alloc();
    cell->hdr.type_id = GLAS_TYPE_STEM;
    cell->hdr.type_aggr = glas_cell_type_aggr(sc.cell);
    cell->hdr.type_arg = 0; 
    cell->stem.fby = sc.cell;
    if(len >= 32) {
        cell->hdr.type_arg = 1;
        cell->stem.stem32[0] = (uint32_t)bits; 
        bits = bits >> 32;
        len -= 32;
    }
    cell->stemHd = (uint32_t)(((bits<<1)|1)<<(31-len));
    return cell;
}
LOCAL bool glas_stack_prep_slowpath(glas_roots* r, glas_stack* s, uint8_t read, uint8_t reserve) {
    size_t const min_count = (size_t) read;
    size_t const max_count = GLAS_STACK_MAX - (size_t) reserve;
    assert(likely(min_count < max_count));
    size_t const tgt_count = (min_count + max_count) / 2; // heuristic
    if(tgt_count > s->count) {
        // TODO: shift this code to use list functions instead of assuming GLAS_TYPE_BRANCH.
        size_t const tgt_pull = tgt_count - s->count; // from overflow
        size_t pull = 0;
        glas_cell* const head = s->overflow; 
        glas_cell* list_iter = head;
        while((GLAS_VAL_UNIT != list_iter) && (pull < tgt_pull)) {
            assert(likely(GLAS_DATA_IS_PTR(list_iter) && (GLAS_TYPE_BRANCH == list_iter->hdr.type_id)));
            list_iter = list_iter->branch.R;
            ++pull;
        }
        glas_roots_slot_write(r, &(s->overflow), list_iter);
        // potential underflow
        size_t const valid_count = s->count + pull;
        size_t const underflow_count = likely((valid_count >= min_count)) ? 0 : 
            (min_count - valid_count);
        size_t const shift = pull + underflow_count;
        assert(likely(shift > 0));
        // shift existing content
        for(size_t ix = 0; ix < s->count; ++ix) {
            glas_sc* const src = s->data + ix;
            glas_sc* const dst = src + shift;
            dst->stem = src->stem;
            glas_roots_slot_write(r, &(dst->cell), src->cell);
        }
        // fill from overflow list
        list_iter = head;
        for(size_t ix = 1; pull >= ix; ++ix) {
            assert(likely(GLAS_DATA_IS_PTR(list_iter) && (GLAS_TYPE_BRANCH == list_iter->hdr.type_id)));
            glas_sc* const tgt = s->data + shift - ix;
            tgt->stem = ((uint64_t)list_iter->branch.stemL) << 32;
            glas_roots_slot_write(r, &(tgt->cell), list_iter->branch.L);
            list_iter = list_iter->branch.R;
        }
        assert(likely(list_iter == s->overflow)); // double check
        // fill with GLAS_VOID in case of underflow
        for(size_t ix = 0; ix < underflow_count; ++ix) {
            glas_sc* const tgt = s->data + ix;
            tgt->stem = GLAS_STEM63_EMPTY;
            glas_roots_slot_write(r, &(tgt->cell), GLAS_VOID);
        }
        return (0 == underflow_count);
    } else {
        size_t const push = s->count - tgt_count; // to overflow
        glas_cell* head = s->overflow;
        for(size_t ix = 0; ix < push; ++ix) {
            glas_sc sc_head = { .stem = GLAS_STEM63_EMPTY, .cell = head };
            head = glas_cell_pair_alloc_sc(s->data[ix], sc_head);
        }
        glas_roots_slot_write(r, &(s->overflow), head);
        for(size_t ix = 0; ix < tgt_count; ++ix) {
            s->data[ix].stem = s->data[ix + push].stem;
            glas_roots_slot_write(r, &(s->data[ix].cell), s->data[ix + push].cell);
        }
        for(size_t ix = tgt_count; ix < s->count; ++ix) {
            glas_roots_slot_write(r, &(s->data[ix].cell), GLAS_VOID);
        }
        s->count = tgt_count;
        return true;
    }
}

/**
 * Prepare stack with inputs (from overflow) and reserve space.
 * - read: how many inputs we will observe below stack pointer
 * - reserve: how much free space we want above stack pointer
 * 
 * Note: if we underflow, we'll fill some stack slots with GLAS_VOID,
 * and add an error to our context. Thus, we never observe underflow.
 */
LOCAL inline void glas_thread_stack_prep(glas* g, uint8_t read, uint8_t reserve) {
    glas_thread_state* const st = g->state;
    glas_stack* const s = &(st->stack);
    if(unlikely((read > s->count) || ((s->count + reserve) > GLAS_STACK_MAX))) {
        glas_roots* const r = &(st->gcbase);
        bool const ok = glas_stack_prep_slowpath(r, s, read, reserve);
        if(!ok) { g->err |= GLAS_E_UNDERFLOW; }
    }
}
LOCAL inline void glas_thread_stash_prep(glas* g, uint8_t read, uint8_t reserve) {
    glas_thread_state* const st = g->state;
    glas_stack* const s = &(st->stash);
    if(unlikely((read > s->count) || ((s->count + reserve) > GLAS_STACK_MAX))) {
        glas_roots* const r = &(st->gcbase);
        bool const ok = glas_stack_prep_slowpath(r, s, read, reserve);
        if(!ok) { g->err |= GLAS_E_UNDERFLOW; }
    }
}

LOCAL void glas_thread_stack_sc_push(glas* g, glas_sc sc) {
    glas_thread_stack_prep(g, 0, 1);
    glas_thread_state* const ts = g->state;
    glas_sc* const dst = ts->stack.data + ts->stack.count;
    dst->stem = sc.stem;
    glas_roots_slot_write(&(ts->gcbase), &(dst->cell), sc.cell);
    ts->stack.count++;
}
LOCAL inline void glas_thread_stack_cell_push(glas* g, glas_cell* cell) {
    glas_sc const sc = { .stem = GLAS_STEM63_EMPTY, .cell = cell };
    glas_thread_stack_sc_push(g, sc);
}
LOCAL glas_sc glas_thread_stack_pop_sc(glas* g) {
    glas_thread_stack_prep(g, 1, 0);
    glas_thread_state* const ts = g->state;
    glas_stack* const s = &(ts->stack);
    glas_sc* const psc = &(s->data[--(s->count)]);
    glas_sc const sc = *psc;
    glas_roots_slot_write(&(g->state->gcbase), &(psc->cell), GLAS_VOID);
    return sc;
}
LOCAL glas_cell* glas_thread_stack_pop_cell(glas* g) {
    glas_sc sc = glas_thread_stack_pop_sc(g);
    return glas_sc_to_cell(sc);
}
LOCAL uint64_t glas_cell_stem_pop(glas_cell** cell) {
    // opportunistically returns some bits from cell.
    if(GLAS_DATA_IS_BITS(*cell)) {
        uint64_t const stem = ((uint64_t)(*cell)) & ~UINT64_C(0b11);
        (*cell) = GLAS_VAL_UNIT;
        return stem;
    } else if(GLAS_DATA_IS_PTR(*cell)) {
        if(GLAS_TYPE_STEM == (*cell)->hdr.type_id) {
            assert(likely(0 != (*cell)->stemHd));
            // we can extract up to 63 bits, which corresponds to
            // one stem32 and a full stemHd. 
            size_t const hd_shift = 1 + ctz32((*cell)->stemHd);

            // first extract without modifying cell. In the best case, we
            // can avoid an extra allocation by forwarding to stem.fby.
            uint64_t bits = ((uint64_t)((*cell)->stemHd)) >> hd_shift;
            size_t len = 32 - hd_shift;
            size_t s32ix = (*cell)->hdr.type_arg;
            if(s32ix > 0) {
                bits = (bits << 32) | ((uint64_t)(*cell)->stem.stem32[--s32ix]);
                len += 32;
            }
            if(0 == s32ix) {
                // all stem bits in this cell are drained, move to fby.
                (*cell) = (*cell)->stem.fby;
            } else {
                // cannot drain all bits this round, need clone
                (*cell) = glas_cell_clone(*cell);
                (*cell)->hdr.type_arg = s32ix;
                (*cell)->stemHd = GLAS_STEM31_EMPTY;
                static size_t const target_drain = 55; // heuristic
                if(target_drain > len) {
                    size_t const split = target_drain - len;
                    uint32_t const s32 = (*cell)->stem.stem32[--((*cell)->hdr.type_arg)];
                    (*cell)->stemHd = ((s32<<1)|1)<<(split-1);
                    bits = (bits << split) | (s32 >> (32 - split));
                    len += split;
                }
            }
            assert(likely(64 > len));
            return ((bits<<1)|1)<<(63-len);
        } else if(GLAS_STEM31_EMPTY != (*cell)->stemHd) {
            (*cell) = glas_cell_clone(*cell);
            uint64_t stem = ((uint64_t)(*cell)->stemHd) << 32;
            (*cell)->stemHd = GLAS_STEM31_EMPTY;
            return stem; 
        } else {
            return GLAS_STEM63_EMPTY; 
        }
    } else if(GLAS_DATA_IS_SHRUB(*cell)) {
        uint32_t bits = 0;
        size_t len = 0;
        uint64_t shrub = GLAS_DATA_SHRUB_BITS(*cell);
        while(GLAS_SHRUB_IS_EDGE(shrub)) {
            len++;
            bits = bits << 1;
            if(GLAS_SHRUB_IS_INR(shrub)) {
                bits |= 0b1;
            } 
            shrub = shrub << 2;
        }
        (*cell) = (glas_cell*)(shrub | 0b10);
        return (((((uint64_t)bits)<<1)|1)<<(63-len));
    } else if(GLAS_DATA_IS_PACKRAT(*cell)) {
        // packed rationals share four bits, i.e.:
        //  (n:Bits, d:Bits)
        //  'n' is ascii 0x6E or 0b0110 1 110
        //  'd' is ascii 0x64 or 0b0110 0 100
        // we can extract these bits
        uint64_t const num_stem = (GLAS_PACKRAT_NUM_STEM(*cell)>>3) | (UINT64_C(0b110)<<61);
        uint64_t const den_stem = (GLAS_PACKRAT_DEN_STEM(*cell)>>3) | (UINT64_C(0b100)<<61);
        glas_cell* const num = (glas_cell*)(num_stem | 0b01);
        glas_cell* const den = (glas_cell*)(den_stem | 0b01);
        (*cell) = glas_cell_pair_alloc(den, num); // may allocate or use shrub
        return UINT64_C(0b01101)<<59; // return the four shared bits
    } else {
        return GLAS_STEM63_EMPTY; // empty stem
    }
}
LOCAL size_t glas_sc_stem_len(glas_sc sc) {
    size_t sum = 63 - ctz64(sc.stem);
    glas_cell* cell = sc.cell;
    while(GLAS_DATA_IS_PTR(cell)) {
        sum += (31 - ctz32(cell->stemHd));
        if(GLAS_TYPE_STEM == cell->hdr.type_id) {
            sum += 32 * (size_t) cell->hdr.type_arg;
            cell = cell->stem.fby;
        } else {
            cell = GLAS_VOID;
        }
    }
    if(GLAS_DATA_IS_BITS(cell)) {
        uint64_t const bits = ((uint64_t)cell) & ~UINT64_C(0b11);
        sum += (63 - ctz64(bits));
    } else if(GLAS_DATA_IS_SHRUB(cell)) {
        uint64_t shrub = GLAS_DATA_SHRUB_BITS(cell);
        while(GLAS_SHRUB_IS_EDGE(shrub)) {
            shrub = shrub << 2;
            sum += 1;
        }
    } else if(GLAS_DATA_IS_PACKRAT(cell)) {
        sum += 4; // 0b0110 bits shared
    }
    return sum;
}


LOCAL inline bool glas_sc_bits_load(glas_sc* sc) {
    if(GLAS_STEM63_EMPTY != sc->stem) { return true; }
    sc->stem = glas_cell_stem_pop(&(sc->cell));
    return (GLAS_STEM63_EMPTY != sc->stem);
}

API void glas_binary_push(glas* g, uint8_t const* buf, size_t len) {
    assert(likely((NULL != buf)));
    glas_os_thread_enter_busy();
    glas_thread_stack_cell_push(g, 
        glas_cell_binary_alloc(buf, len));
    glas_os_thread_exit_busy();
}
API void glas_binary_push_zc(glas* g, uint8_t const* buf, size_t len, glas_refct pin) {
    assert(likely((NULL != buf)));
    glas_os_thread_enter_busy();
    glas_thread_stack_cell_push(g, 
        glas_cell_binary_slice(buf, len, 
            glas_cell_fptr((void*)buf, pin, false)));
    glas_os_thread_exit_busy();
}
API void glas_thread_set_debug_name(glas* g, char const* debug_name) {
    if(NULL == debug_name) { debug_name = ""; }
    size_t const len = strlen(debug_name);
    glas_os_thread_enter_busy();
    glas_binary_push(g, (uint8_t const*) debug_name, len);
    glas_roots_slot_write(&(g->state->gcbase), &(g->state->debug_name),
        glas_thread_stack_pop_cell(g)); 
    glas_os_thread_exit_busy();
}



API bool glas_binary_peek(glas*, size_t start_offset, size_t max_read, 
    uint8_t* buf, size_t* amt_read);
API bool glas_binary_peek_zc(glas*, size_t start_offset, size_t max_read,
    uint8_t const** ppBuf, size_t* amt_read, glas_refct*);

API void glas_ptr_push(glas* g, void* ptr, glas_refct pin, bool linear) {
    glas_os_thread_enter_busy();
    glas_thread_stack_cell_push(g,
        glas_cell_fptr(ptr, pin, linear));
    glas_os_thread_exit_busy();
}
API bool glas_ptr_peek(glas* g, void** ptr, glas_refct* pin) {
    glas_os_thread_enter_busy();
    glas_thread_stack_prep(g, 1, 0);
    glas_sc* const sc = g->state->stack.data + g->state->stack.count - 1;
    bool const ok = (GLAS_STEM63_EMPTY == sc->stem) &&
        (GLAS_TYPE_FOREIGN_PTR == sc->cell->hdr.type_id);
    if(ok) {
        if(NULL != ptr) { (*ptr) = sc->cell->foreign_ptr.ptr; }
        if(NULL != pin) { (*pin) = sc->cell->foreign_ptr.pin; }
    } else if(NULL != pin) {
        pin->refct_upd = NULL;
    }
    glas_os_thread_exit_busy();
    if(pin) { 
        glas_incref(*pin); 
    } 
    return ok;
}
API bool glas_ptr_pop(glas* g, void** ptr, glas_refct* pin) {
    glas_os_thread_enter_busy();
    bool ok = glas_ptr_peek(g, ptr, pin);
    if(ok) {
        (void) glas_thread_stack_pop_sc(g);
    }
    glas_os_thread_exit_busy();
    return ok;
}

LOCAL void glas_data_swap_ngc(glas* g) {
    glas_thread_stack_prep(g, 2, 0);
    glas_stack* const s = &(g->state->stack);
    glas_sc* const a = s->data + s->count - 2;
    glas_sc* const b = a + 1;
    glas_sc const a_copy = *a;
    glas_roots* const r = &(g->state->gcbase);
    glas_roots_slot_write(r, &(a->cell), b->cell);
    glas_roots_slot_write(r, &(b->cell), a_copy.cell);
    a->stem = b->stem;
    b->stem = a_copy.stem;
}
API void glas_data_swap(glas* g) {
    glas_os_thread_enter_busy();
    glas_data_swap_ngc(g);
    glas_os_thread_exit_busy();
}
LOCAL void glas_data_push_to_stash_ngc(glas* g) {
    // keep the initial implemention simple
    glas_roots* const r = &(g->state->gcbase);
    glas_stack* const stack = &(g->state->stack);
    glas_stack* const stash = &(g->state->stash);
    glas_thread_stack_prep(g, 1, 0);
    glas_thread_stash_prep(g, 0, 1);
    glas_sc* const src = &(stack->data[--(stack->count)]);
    glas_sc* const dst = &(stash->data[(stash->count)++]);
    dst->stem = src->stem;
    glas_roots_slot_write(r, &(dst->cell), src->cell);
    glas_roots_slot_write(r, &(src->cell), GLAS_VOID);
}
LOCAL void glas_data_pull_from_stash_ngc(glas* g) {
    glas_roots* const r = &(g->state->gcbase);
    glas_stack* const stack = &(g->state->stack);
    glas_stack* const stash = &(g->state->stash);
    glas_thread_stash_prep(g, 1, 0);
    glas_thread_stack_prep(g, 0, 1);
    glas_sc* const src = &(stash->data[--(stash->count)]);
    glas_sc* const dst = &(stack->data[(stack->count)++]);
    dst->stem = src->stem;
    glas_roots_slot_write(r, &(dst->cell), src->cell);
    glas_roots_slot_write(r, &(src->cell), GLAS_VOID);
}
API void glas_data_stash(glas* g, int8_t amt) {
    if(0 == amt) { return; }
    glas_os_thread_enter_busy();
    if(amt > 0) {
        do {
            glas_data_push_to_stash_ngc(g);
        } while(0 != --amt);
    } else {
        do {
            glas_data_pull_from_stash_ngc(g);
        } while(0 != ++amt);
    }
    glas_os_thread_exit_busy();
}
LOCAL size_t glas_data_copy_lin_ngc(glas* g, uint8_t amt) {
    if(0 == amt) { return 0; }
    size_t amt_linear = 0;
    glas_sc copies[amt]; // copy items locally (1 to 255)
    for(uint8_t ix = 0; ix < amt; ++ix) {
        glas_data_push_to_stash_ngc(g);
        copies[(amt-1)-ix] = g->state->stash.data[g->state->stash.count - 1];
    }
    for(uint8_t ix = 0; ix < amt; ++ix) {
        if(glas_cell_is_linear(copies[ix].cell)) { ++amt_linear; }
        glas_thread_stack_sc_push(g, copies[ix]);
    }
    for(uint8_t ix = 0; ix < amt; ++ix) {
        glas_data_pull_from_stash_ngc(g);
    }
    return amt_linear;
}
API void glas_data_copy(glas* g, uint8_t amt) {
    glas_os_thread_enter_busy();
    size_t const amt_linear = glas_data_copy_lin_ngc(g, amt);
    glas_os_thread_exit_busy();
    if(0 != amt_linear) {
        g->err |= GLAS_E_LINEARITY;
    }
}

LOCAL size_t glas_data_drop_lin_ngc(glas* g, uint8_t amt) {
    size_t amt_linear = 0;
    glas_roots* const r = &(g->state->gcbase);
    glas_stack* const s = &(g->state->stack);
    for(uint8_t ix = 0; ix < amt; ++ix) {
        glas_thread_stack_prep(g, 1, 0);
        glas_sc* const sc = &(s->data[--(s->count)]);
        if(glas_cell_is_linear(sc->cell)) {
            ++amt_linear;
        }
        glas_roots_slot_write(r, &(sc->cell), GLAS_VOID);
    }
    return amt_linear;
}
API void glas_data_drop(glas* g, uint8_t amt) {
    glas_os_thread_enter_busy();
    size_t const amt_linear = glas_data_drop_lin_ngc(g, amt);
    glas_os_thread_exit_busy();
    if(0 != amt_linear) {
        g->err |= GLAS_E_LINEARITY;
    }
}
LOCAL inline bool glas_is_moves_var(uint8_t c) {
    return (((uint8_t)'a' <= c) && (c <= (uint8_t)'z')) ||
           (((uint8_t)'A' <= c) && (c <= (uint8_t)'Z'));
}

LOCAL size_t glas_data_move_lin_ngc(glas* g, uint8_t const* const moves) {
    // determine how many vars I need
    static uint8_t const NOINDEX = 255;
    uint8_t var_indices[256];
    memset(var_indices, NOINDEX, 256);
    size_t next_index = 0;
    uint8_t const* scan = moves;
    while('-' != (*scan)) {
        assert(likely(glas_is_moves_var(*scan) && (NOINDEX == var_indices[*scan])));
        var_indices[*scan] = (uint8_t) next_index++;
        ++scan;
    }
    uint8_t const* const center = scan; // at '-'

    // prepare space for vars on stack
    size_t const var_count = next_index;
    assert(likely(var_count > 0));
    glas_sc data[var_count];
    uint8_t copies[var_count];
    glas_roots* const r = &(g->state->gcbase);
    glas_stack* const s = &(g->state->stack);

    // pop stack into vars
    while(moves != scan) {
        --scan;
        uint8_t const ix = var_indices[*scan];
        glas_thread_stack_prep(g, 1, 0);
        glas_sc* const sc = &(s->data[--(s->count)]);
        data[ix] = *sc;
        copies[ix] = 0;
        glas_roots_slot_write(r, &(sc->cell), GLAS_VOID);
    }

    // push vars into stack
    scan = center+1;
    while(0 != *scan) {
        assert(likely(glas_is_moves_var(*scan) && (NOINDEX != var_indices[*scan])));
        uint8_t const ix = var_indices[*scan];
        glas_thread_stack_sc_push(g, data[ix]);
        ++(copies[ix]);
        ++scan;
    }

    // check for linearity violations
    size_t linearity_violations = 0;
    for(size_t ix = 0; ix < var_count; ++ix) {
        if((1 != copies[ix]) && glas_cell_is_linear(data[ix].cell)) { 
            ++linearity_violations; 
        }
    }
    return linearity_violations;
}

API void glas_data_move(glas* g, char const* moves) {
    glas_os_thread_enter_busy();
    size_t const linearity_violations = 
        glas_data_move_lin_ngc(g, (uint8_t const*) moves);
    glas_os_thread_exit_busy();
    if(0 != linearity_violations) {
        g->err |= GLAS_E_LINEARITY;
    }
}

LOCAL void glas_mkp_ngc(glas* g) {
    glas_thread_stack_prep(g, 2, 0);
    glas_roots* const r = &(g->state->gcbase);
    glas_stack* const s = &(g->state->stack);
    glas_sc* const a = s->data + s->count - 2;
    glas_sc* const b = a + 1;
    glas_cell* const cell = glas_cell_pair_alloc_sc(*a, *b);
    glas_roots_slot_write(r, &(a->cell), cell);
    glas_roots_slot_write(r, &(b->cell), GLAS_VOID);
    a->stem = GLAS_STEM63_EMPTY;
    --(s->count);
}
API void glas_mkp(glas* g) {
    // A B -- (A,B)     ; B is top of stack
    glas_os_thread_enter_busy();
    glas_mkp_ngc(g);
    glas_os_thread_exit_busy();
}
void glas_mkl(glas*);   // X -- 0b0.X
void glas_mkr(glas*);   // X -- 0b1.X
bool glas_unp(glas*);   // (A,B) -- A B | FAIL
bool glas_unl(glas*);   // 0b0.X -- X   | FAIL
bool glas_unr(glas*);   // 0b1.X -- X   | FAIL




LOCAL glas_sc glas_data_u64(uint64_t const n) {
    glas_sc sc;
    if(0 == n) { 
        sc.stem = GLAS_STEM63_EMPTY;
        sc.cell = GLAS_VAL_UNIT;
    } else {
        size_t const shift = clz64(n); // number of '000...' in prefix
        if(shift >= 3) { // use packed pointer
            sc.stem = GLAS_STEM63_EMPTY;
            sc.cell = (glas_cell*) ((((n<<1)|1)<<(shift-1))|0b01);
        } else { // full packed pointer + overflow into sc.stem
            static uint64_t const maskLo = (UINT64_C(1)<<61)-1;
            sc.stem = ((n & ~maskLo)|(UINT64_C(1)<<60))<<shift;
            sc.cell = (glas_cell*)(((n & maskLo)<<3)|0b101);
        }
    }
    //debug("u64 data %lu written as stem %lx cell %p", n, sc.stem, sc.cell);
    return sc;
}
LOCAL glas_sc glas_data_i64(int64_t const n) {
    glas_sc sc;
    if(n >= 0) {
        sc = glas_data_u64((uint64_t) n);
    } else {
        uint64_t const n1c = (INT64_MIN == n) ? ((UINT64_C(1)<<63)-1) : ((uint64_t)(n-1));
        size_t const shift = clz64(~n1c); // number of '1111...' in prefix
        if(shift >= 3) { // use packed pointer
            sc.stem = GLAS_STEM63_EMPTY;
            sc.cell = (glas_cell*) ((((n1c<<1)|1)<<(shift-1))|0b01);
        } else { // full packed pointer + overflow into sc.stem
            static uint64_t const maskLo = (UINT64_C(1)<<61)-1;
            sc.stem = ((n1c & ~maskLo)|(UINT64_C(1)<<60))<<shift;
            sc.cell = (glas_cell*)(((n1c & maskLo)<<3)|0b101);
        }
    }
    //debug("i64 data %ld written as stem %lx cell %p", n, sc.stem, sc.cell);
    return sc;
}
API void glas_i64_push(glas* g, int64_t n) {
    glas_os_thread_enter_busy();
    glas_thread_stack_sc_push(g, glas_data_i64(n));
    glas_os_thread_exit_busy();
}
API void glas_i32_push(glas* g, int32_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i16_push(glas* g, int16_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_i8_push(glas* g, int8_t n) { glas_i64_push(g, (int64_t) n); }
API void glas_u64_push(glas* g, uint64_t n) {
    glas_os_thread_enter_busy();
    glas_thread_stack_sc_push(g, glas_data_u64(n));
    glas_os_thread_exit_busy();
}
API void glas_u32_push(glas* g, uint32_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u16_push(glas* g, uint16_t n) { glas_u64_push(g, (uint64_t) n); }
API void glas_u8_push(glas* g, uint8_t n) { glas_u64_push(g, (uint64_t) n); }

#if 0
LOCAL bool glas_list_len_peek_cell(glas_cell* cell, size_t* aggrlen) {
    do {
        if(GLAS_DATA_IS_PTR(cell)) {
            if(GLAS_STEM31_EMPTY != cell->stemHd) {
                return false;
            } 
            switch(cell->hdr.type_id) {
                case GLAS_TYPE_BRANCH:
                    (*aggrlen)++;
                    if(GLAS_STEM31_EMPTY != cell->branch.stemR) {
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
        } else if(GLAS_DATA_IS_PACKRAT(cell)) {
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
LOCAL inline uint64_t glas_sc_stembits_pop(glas_sc* sc) {
    uint64_t const stem = sc->stem;
    sc->stem = GLAS_STEM63_EMPTY;
    return stem;
}
LOCAL inline bool glas_sc_stembits_pop64(glas_sc* sc, uint64_t* bits, size_t* len) {
    // extract a bitstring of up to length 64 bits, fails if not a bitstring
    // or if overly large. Can extend an existing bitstring.
    do {
        uint64_t const stem = glas_sc_stembits_pop(sc);
        size_t const shift = 1 + ctz64(stem);
        size_t const stemlen = 64 - shift;
        if((*len) > shift) { 
            return false; // overflow
        }
        (*bits) = ((*bits) << stemlen) | (stem >> shift);
        (*len) += stemlen;
    } while(glas_sc_bits_load(sc));
    return (GLAS_VAL_UNIT == sc->cell); // fully read bitstring
}
LOCAL bool glas_u64_peek_sc(glas_sc* osc, uint64_t* n) {
    glas_sc_bits_load(osc);
    if(0 == (GLAS_STEM63_HIBIT & osc->stem)) {
        return false; // negative number
    }
    glas_sc sc = (*osc);
    uint64_t bits = 0;
    size_t len = 0;
    if(!glas_sc_stembits_pop64(&sc, &bits, &len)) {
        return false;
    }
    (*n) = bits;
    return true;
}
LOCAL bool glas_i64_peek_sc(glas_sc* osc, int64_t* n) {
    glas_sc_bits_load(osc);
    if(0 != (GLAS_STEM63_HIBIT & osc->stem)) {
        // handle positive numbers via u64 peek
        uint64_t u = 0;
        bool ok = glas_u64_peek_sc(osc, &u);
        if(ok && ((uint64_t)INT64_MAX >= u)) {
            (*n) = (int64_t) u;
        }
        return ok;
    }
    glas_sc sc = (*osc);
    uint64_t bits = 0;
    size_t len = 0;
    if(!glas_sc_stembits_pop64(&sc, &bits, &len)) {
        return false;
    }
    if(64 > len) {
        uint64_t const ones_prefix = (~UINT64_C(0) << len);
        (*n) = 1 + (int64_t)(ones_prefix|bits);
        return true;
    } 
    if((64 == len) && (((UINT64_C(1)<<63)-1) == bits)) {
        (*n) = INT64_MIN;
        return true;
    }
    return false;
}
API bool glas_i64_peek(glas* g, int64_t* n) {
    glas_os_thread_enter_busy();
    glas_thread_stack_prep(g, 1, 0);
    glas_stack* const s = &(g->state->stack);
    bool const ok = glas_i64_peek_sc((s->data + s->count - 1), n);
    glas_os_thread_exit_busy();
    return ok;
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
    glas_os_thread_enter_busy();
    glas_thread_stack_prep(g, 1, 0);
    glas_stack* const s = &(g->state->stack);
    bool const ok = glas_u64_peek_sc((s->data + s->count - 1), n);
    glas_os_thread_exit_busy();
    return ok;
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


/*******************************************
 * UNIT TESTS FOR GLAS RUNTIME INTERNALS
 ******************************************/

#include "minunit.h"

// stuff that might need cleanup
static struct test_state {
    glas* g;
} test;
LOCAL void test_setup() { 
    test.g = glas_thread_new();
}
LOCAL void test_teardown() {
    glas_thread_exit(test.g);
    glas_rt_tls_reset();
    memset(&test, 0, sizeof(struct test_state));
}
MU_TEST(test_bitmanip) {
    mu_assert_int_eq(21, (int) popcount64(UINT64_C(0x707070707077)));
    mu_assert_int_eq(14, (int) popcount64(UINT64_C(0x09606606609)));
    uint32_t const test_a = UINT64_C(0xa)<<17;
    mu_assert_int_eq(43, (int) clz64((uint64_t)test_a));
    mu_assert_int_eq(18, (int) ctz64((uint64_t)test_a));
    mu_assert_int_eq(18, (int) ctz32(test_a));
}
MU_TEST(test_uint) {
    uint64_t n;
    glas_u64_push(test.g,GLAS_PTR_MAX_INT);
    glas_u64_peek(test.g, &n);
    mu_assert(n == GLAS_PTR_MAX_INT, "max ptr int");
    glas_u64_push(test.g, GLAS_PTR_MAX_INT + 1);
    glas_u64_peek(test.g, &n);
    mu_assert(n == (GLAS_PTR_MAX_INT + 1), "max ptr int + 1");
    glas_u64_push(test.g, 0);
    uint8_t n8;
    glas_u8_peek(test.g, &n8);
    mu_assert_int_eq(0, (int)n8);

    glas_u64_push(test.g, UINT64_MAX);
    glas_u64_peek(test.g, &n);
    mu_assert(n == UINT64_MAX, "max int");

    glas_i8_push(test.g, -1);
    bool not_ok = glas_u64_peek(test.g, &n);
    mu_assert(n == UINT64_MAX, "no update on fail");
    mu_assert(false == not_ok, "expect failure to read negative number");
}
MU_TEST(test_int) {
    int64_t n;
    glas_i64_push(test.g, GLAS_PTR_MAX_INT);
    glas_i64_peek(test.g, &n);
    mu_assert(n == GLAS_PTR_MAX_INT, "ptr max int");
    glas_i64_push(test.g, GLAS_PTR_MAX_INT+1);
    glas_i64_peek(test.g, &n);
    mu_assert(n == (GLAS_PTR_MAX_INT+1), "ptr max int +1");
    glas_i64_push(test.g, -1);
    glas_i64_peek(test.g, &n);
    mu_assert(n == -1, "-1");
    glas_i64_push(test.g, GLAS_PTR_MIN_INT);
    glas_i64_peek(test.g, &n);
    mu_assert(n == (GLAS_PTR_MIN_INT), "ptr min int");
    glas_i64_push(test.g, GLAS_PTR_MIN_INT-1);
    glas_i64_peek(test.g, &n);
    mu_assert(n == (GLAS_PTR_MIN_INT-1), "ptr min int -1");
    glas_i64_push(test.g, INT64_MAX);
    glas_i64_peek(test.g, &n);
    mu_assert(n == INT64_MAX, "i64 max");
    glas_i64_push(test.g, INT64_MIN);
    glas_i64_peek(test.g, &n);
    mu_assert(n == INT64_MIN, "i64 min");
}
MU_TEST(test_big_bits) {
    static size_t const STEP_MAX = 1600;
    glas_sc sc = { .stem = GLAS_STEM63_EMPTY, .cell = GLAS_VAL_UNIT };
    size_t bitct = 0;
    glas_os_thread_enter_busy();
    for(size_t ix = 1; ix < STEP_MAX; ++ix) {
        size_t const len = 64 - clz64(ix);
        bitct += len;
        uint64_t const bits = (((uint64_t)ix<<1)|1)<<(63-len);
        glas_stem_sc_push(bits, &sc);
    }
    mu_assert_int_eq((int)bitct, (int) glas_sc_stem_len(sc));
    // test actual bit storage
    static uint64_t const hibit = GLAS_STEM63_HIBIT;
    size_t bitct_pop = 0;
    size_t unmatched = 0;
    for(size_t ix = (STEP_MAX - 1); ix > 0; --ix) {
        size_t const len = 64 - clz64(ix);
        uint64_t expect = (((uint64_t)ix<<1)|1)<<(63-len);
        while(expect != hibit) {
            bitct_pop++;
            if(sc.stem == hibit) {
                sc.stem = glas_cell_stem_pop(&sc.cell);
            }
            bool const match_bit = ((hibit & sc.stem) == (hibit & expect));
            unmatched += likely(match_bit) ? 0 : 1;
            sc.stem = sc.stem << 1;
            expect = expect << 1;
        }
    }
    glas_os_thread_exit_busy();
    mu_assert_int_eq((int) bitct, (int) bitct_pop);
    mu_assert((GLAS_STEM63_EMPTY == sc.stem) && (GLAS_VAL_UNIT == sc.cell), "all bits popped");
    mu_assert_int_eq(0, (int)unmatched);
}
void test_refct_upd(void* arg, bool incref) {
    _Atomic(size_t)* const count = arg;
    if(incref) {
        atomic_fetch_add_explicit(count, 1, memory_order_relaxed);
    } else {
        atomic_fetch_sub_explicit(count, 1, memory_order_relaxed);
    }
}
MU_TEST(test_fin) {
    static size_t const stripes = 4;
    static size_t const stripe_len = 40000;
    _Atomic(size_t) counts[stripes];
    for(size_t ix = 0; ix < stripes; ++ix) {
        atomic_init(counts+ix, 0);
    }
    for(size_t ii = 0; ii < stripe_len; ++ii) {

    }

    mu_assert(false, "todo: finish");

    // striped allocations

}

MU_TEST_SUITE(test_glas) {
    MU_SUITE_CONFIGURE(&test_setup, &test_teardown);
    MU_RUN_TEST(test_bitmanip);
    MU_RUN_TEST(test_uint);
    MU_RUN_TEST(test_int);
    MU_RUN_TEST(test_big_bits);
    MU_RUN_TEST(test_fin);
}
API bool glas_rt_run_builtin_tests() {
    MU_RUN_SUITE(test_glas);
    MU_REPORT();
    return (0 == MU_EXIT_CODE);
}

