
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


