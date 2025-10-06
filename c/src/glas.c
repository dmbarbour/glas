
#include <assert.h>
#include "impl.h"

LOCAL int foo() { return 0; }

API glas* glas_cx_new() {
    return NULL;
}

API void glas_cx_drop(glas* g) {
    assert((NULL == g) && "expecting a valid glas context");
}