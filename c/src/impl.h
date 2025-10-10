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

typedef struct glas_cell glas_cell;

struct glas_cell {
    // Refct 1..255
    //   0 for sticky; special handling for GC 
    //   possible max-entry tables
    _Atomic(uint8_t) refct;
    // 32-bit header per object
    // ad hoc GC Bits:
    //  sticky table
    //  marking bits (2 for tricolor marking)
    //  generation or region tracking
    _Atomic(uint8_t) gcbits;
    
    // Type and Tags:
    //  linearity (1 bit) - no need for affine vs relevant
    //  ephemerality - 2 bits - 
    //    options: plain-old-data, database, runtime, transaction
    //    aggregator tracks most-ephemeral for self and children
    //  finalizer? unclear it's needed; use doubly linked list.
    //  Node type (4-5 bits)
    uint8_t ttag;
    // Extra stem bits.
    //   00000000 - look for stem bits further in
    uint8_t extra_stem;
    union {
        struct { 
            uint32_t stemH; // bits before split
            uint32_t stemL; // bits before L
            uint32_t stemR; // bits before R
            glas_cell* L; 
            glas_cell* R; 
        } branch;
        struct {
            // fills from rhs (stem[4]) to lhs (stem[0]).
            // leftmost unused stems are zero.
            // leftmost non-zero stem is partial (0..31 bits)
            // rightmost stems are filled (32 bits).
            uint32_t stem[5];
            glas_cell* D;

            // aside: could squeeze in a few more bits with a stem[7]
            // variant, but 
        } stem;
        struct {
            // bits are encoded per stem, but interpretation differs
            //
            //    00 - leaf
            //    01 - branch (fby left then right shrubs)
            //    10 - left (fby shrub)
            //    11 - right (fby shrub)
            //
            // We encode a small tree into a bitstring. Limited to leaf
            // nodes in the allocated tree structure because pointers.
            uint32_t bits[7];
        } shrub;
        struct {

        } 

        uint8_t small_bin[28];       // binary of 8..24 items; byte length in type_arg
        struct glas_cell* tuple[3];  // list of 1..3 items
        struct {
            size_t len;
            uint8_t const* data;
            glas_bin* pin;         // a separate gc_event to free memory.
        } big_bin;
        struct {
            size_t len;          // 
            glas_cell** data;    // pointer to array of glas_cells
            glas_arr* pin;       // to support GC event
        } big_arr;

        struct {
            // this is primarily for rope nodes. Represents taking
            // takeLeft items from left, then concatenation onto right.
            // We'll ensure by construction that 'takeLeft' is a valid.
            size_t takeLeft;
            struct glas_cell* left;
            struct glas_cell* right;
        } concat;

        struct {
            // abstract, runtime-ephemeral, maybe-linear
            void* ref;
            glas_refct pin;
        } foreign_ptr;

        struct {
            // tentative - error values
            struct glas_cell* error_arg;
            int error_code; // change to enum
        } error_val;

        // tbd: gc features such as free cells, forwarding pointers
    } data;
};

_Static_assert(16 >= sizeof(glas_refct), "unexpected refct size");
_Static_assert(32 == sizeof(glas_cell), "unexpected glas_cell size");
