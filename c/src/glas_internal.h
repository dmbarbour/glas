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

_Static_assert(sizeof(void*) == 8, 
    "glas runtime assumes 64-bit pointers");    

/**
 * GC design notes:
 * 
 * I have the idea of using hybrid reference counts, just a small
 * 1..255 count to support in-place updates and push most GC to the
 * mutator. 
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

typedef struct glas_cell glas_cell;
typedef struct glas_pin glas_pin;

typedef struct {
    _Atomic(uint8_t) refct; // 1..255, 0 sticky; leave to GC
    // ad hoc GC Bits:
    //  in sticky table? unclear if needed.
    //  finalizer? unclear if needed
    //  marking bits (2 for tricolor marking)
    //  generations and regions - for the future
    _Atomic(uint8_t) gcbits;

    // Type and Tags:
    //  linearity (1 bit) - no need for affine vs relevant
    //  ephemerality - 2 bits - 
    //    options: plain-old-data, database, runtime, transaction
    //    aggregator tracks most-ephemeral for self and children
    //  finalizer? unclear it's needed; use doubly linked list.
    //  Node type (4-5 bits)
    uint8_t type;
    uint8_t arg; // type specific, e.g. binary length
} glas_cell_hdr;



// For finalizers, just need a glas_refct in a cell, pinned by another. 
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
            uint32_t stemL; // bits before L
            uint32_t stemR; // bits before R
            glas_cell* L; 
            glas_cell* R; 
        } branch;
        struct {
            // filled right to left, each bits is either 0 or 32
            // bits (use common cell 'stemH' for partial). Track number
            // filled in 'arg'. 
            uint32_t bits[4]; // 
            glas_cell* D;
        } stem;
        struct {
            // (Tentative)
            // bits are encoded per stem, but interpretation differs
            //
            //    00 - leaf, may truncate in suffix
            //    01 - branch (fby left then right shrubs)
            //    10 - left (fby shrub)
            //    11 - right (fby shrub)
            //
            // We encode a small tree into a bitstring. Limited to leaf
            // nodes in the allocated tree structure because pointers.
            uint32_t bits[6];
        } shrub;
        uint8_t    small_bin[24];
        glas_cell* small_arr[3]; // a list of 1..3 items

        struct {
            uint8_t const* data;
            size_t len;
            glas_cell* pin; // indirect if slice
        } big_bin;

        struct {
            glas_cell** data;
            size_t len;
            glas_cell* pin; // indirect if slice
        } big_arr;

        struct {
            void* fp;
            glas_cell* pin;
        } foreign_ptr;

        glas_refct pin; // for binaries, arrays, foreign pointers

        struct {
            // this is primarily for rope nodes. Represents takeLeft items
            // from left, then concatenation onto right. We'll ensure by 
            // construction that 'takeLeft' is a valid.
            //
            // Can also be used for slicing, e.g. with 'right' as unit.
            size_t takeLeft;
            struct glas_cell* left;
            struct glas_cell* right;
        } concat;

        struct {
            // linearity is recorded into header
            glas_cell* key;
            glas_cell* data;
        } seal;

        struct {
            glas_cell* data;
            glas_cell* assoc_l; // Associated volumes (r, other)
            glas_cell* assoc_r; // Associated volumes (other, r)
            // may represent data + assoc list?
            // may need separate for env
        } reg;

        struct {
            // not quite sure what I need here.
        } thunk;

        // TBD: Namespace objects
        //   env, closures, etc..
        //   could feasibly abstract regular data.


        struct {
            // tentative - error values
            glas_cell* error_arg;
        } error_val;

        // tbd: gc features such as free cells, forwarding pointers
    } data;
};

_Static_assert(16 >= sizeof(glas_refct), "unexpected refct size");
_Static_assert(32 == sizeof(glas_cell), "unexpected glas_cell size");
