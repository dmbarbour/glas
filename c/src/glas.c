
#include <assert.h>
#include "glas_internal.h"

API glas* glas_thread_new() {
    return NULL;
}

API void glas_thread_exit(glas* g) {
    assert((NULL == g) && "expecting a valid glas context");
}