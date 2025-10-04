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
  #define LOCAL __attribute__((visibility("hidden")))
#endif

_Static_assert(sizeof(void*) == 8, 
    "glas runtime assumes 64-bit pointers");

typedef struct {
    void* addr;
    void (*free)(void*);
} FOREIGN_PTR;

_Static_assert(sizeof(FOREIGN_PTR) == (2 * sizeof(void*)), 
    "glas runtime expects function and data pointers to share size");


typedef struct {
    uint8_t flags;  // flags for mark, gen, region.
    uint8_t refct;  // small refct for hybrid GC
} GC_HDR;

typedef struct Cell {
    _Atomic(GC_HDR) gc_hdr;
    uint8_t type_arg;
    uint8_t cell_type;
    uint32_t stem;
    union {
        struct { 
            uint32_t stemL; 
            uint32_t stemR; 
            struct Cell* L; 
            struct Cell* R; 
        } branch;
        struct {
            uint32_t ext_stem[4];
            struct Cell* C;         
        } stem;
        uint8_t small_binary[24];       // binary of 8..24 items; byte length in type_arg
        struct Cell* small_tuple[3];    // list of 1..3 items
        struct {
            uint32_t offset;
            uint32_t len;
        } big_binary;
    } data;
} Cell;


