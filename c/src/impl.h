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
  #define LOCAL
#else /* Linux, macOS */
  #define API __attribute__((visibility("default")))
#endif
#define LOCAL static


/**
 * We'll be embedding some data into pointers, so
 * let's check our assumptions.
 */
_Static_assert(sizeof(void*) == 8, 
    "glas runtime assumes 64-bit pointers");

typedef struct glas_cell {
    _Atomic(uint8_t) refct; // hybrid reference counts
    _Atomic(uint8_t) gcbits;
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
            struct glas_cell* pin;           // a separate gc_event to free memory.
        } big_binary;
        glas_release_cb gc_event;       // invoked by garbage collector.
        struct {
            size_t len;                     // 
            struct glas_cell* data;          // pointer to array of glas_cells
            struct glas_cell* pin;           // to support GC event
        } big_array;

        struct {
            // this is primarily for rope nodes. Represents taking
            // takeLeft items from left, then concatenation onto right.
            // We'll ensure by construction that 'takeLeft' is a valid.
            size_t takeLeft;
            struct glas_cell* left;
            struct glas_cell* right;
        } concat;
    } data;
} glas_cell;

_Static_assert(32 == sizeof(glas_cell), "unexpected glas_cell size");
