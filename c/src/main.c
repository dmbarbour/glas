
#include "impl.h"
#include <stdio.h>

API int main(int argc, char const** argv) 
{
    for(int ix=0; ix < argc; ++ix) {
        fprintf(stdout, "arg[%d]=%s\n", ix, argv[ix]);
    }

}
