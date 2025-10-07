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


/**
 * We'll be embedding some data into pointers, so
 * let's check our assumptions.
 */
_Static_assert(sizeof(void*) == 8, 
    "glas runtime assumes 64-bit pointers");

typedef struct MemPin {
    // so we can release arrays or binaries
    _Atomic(size_t) refct;
    glas_pin pin;

    // so we split-append on flat binary can recover 
    // the original flat binary, we'll track the memory
    void* mem;
    size_t len;
} MemPin;

typedef struct glas_cell {
    // GC Bits?
    //  - refct (6 to 8 bits)
    //  - mark bits
    //  - 
    _Atomic(uint16_t) gchdr; // hybrid reference counts
    // what type bits do we want in common?
    //  - ephemerality and abstraction (3 bits?)
    //  - linearity (1 bit)
    //  - has destructor (glas_pin or MemPin) (1 bit)
    //  - 
    uint16_t type_arg; 
    uint32_t stem;
    union {
        struct { 
            uint32_t stemL; 
            uint32_t stemR; 
            struct glas_cell* L; 
            struct glas_cell* R; 
        } branch;
        struct {
            uint32_t ext_stem[4];
            struct glas_cell* Next;         
            // note: we can also represent a singleton pointer
            // as a stem glas_cell with an empty stem.
        } stem;
        uint8_t small_binary[24];       // binary of 8..24 items; byte length in type_arg
        struct glas_cell* tuple[3];          // list of 1..3 items
        struct {
            size_t len;
            uint8_t const* data;
            MemPin* pin;         // a separate gc_event to free memory.
        } big_binary;
        struct {
            size_t len;                 // 
            struct glas_cell** data;    // pointer to array of glas_cells
            MemPin* pin;         // to support GC event
        } big_array;

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
            glas_pin pin;
        } foreign_ptr;

        struct {
            // tentative - error values
            struct glas_cell* error_arg;
            int error_code; // change to enum
        } error_val;

        // tbd: gc features such as free cells, forwarding pointers
    } data;
} glas_cell;

_Static_assert(32 == sizeof(glas_cell), "unexpected glas_cell size");
