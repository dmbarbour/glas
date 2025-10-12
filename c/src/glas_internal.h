#pragma once

#include <stdint.h>
#include <stdalign.h>
#include <stdatomic.h>

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

static_assert((sizeof(void*) == 8), 
    "glas runtime assumes 64-bit pointers");    
static_assert((sizeof(_Atomic(uint8_t)) == 1), 
    "glas runtime assumes atomic bytes");

   
typedef struct glas_cell glas_cell;

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
    GLAS_TYPE_REG,
    GLAS_TYPE_TOMBSTONE,
    // under development
    GLAS_TYPE_THUNK,
    GLAS_TYPE_BLACKHOLE, // thunk being computed
    GLAS_TYPE_CONTENT_ADDRESSED_DATA,
    GLAS_TYPE_SHRUB,
    // end of list
    GLAS_TYPEID_COUNT
} glas_type_id;
// Also use top few bits, e.g. for optional values: singleton list
// without an extra allocation

static_assert(32 > GLAS_TYPEID_COUNT, 
    "glas runtime wants to reserve a few bits for logical wrappers");

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

// For finalizers, just need a glas_refct in a cell. 
// Can couple to foreign pointers.
// 
//
// For array slices, I can maintain a reference to the origin array so
// I can potentially rejoin array slices on append. Only need to mark
// data within the slice, though, and the main array pin.
//
// But I might want to distinguish 'slices' for arrays and binaries.
// keep the reference to the original volume. This allows me to flatten
// when we append slices back together.

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
            // sealed data may be collected (set to NULL) after the key
            // becomes unreachable, thus also serves as an ephemeron.
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
            // glas_cell* pin;  // NULL if collected; extended finalizer
            uint64_t   id;      // for hashmaps, debugging, etc.
            // id is global atomic incref; I assume 64 bits is adequate.
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
    } data;
};

_Static_assert(16 >= sizeof(glas_refct), "unexpected refct size");
_Static_assert(32 == sizeof(glas_cell), "unexpected glas_cell size");
