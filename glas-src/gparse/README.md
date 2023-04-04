# Goals

This is intended to support parsing of texts into structured data. One approach is parser combinators, and another is compiling 'grammars' into parser functions. The latter approach has potential to be much more efficient insofar as we can recognize common structure between grammars (e.g. that two options share a prefix). 

Later, I'd like to develop a DSL for representing grammars. So, the grammar approach should align nicely with a grammar we can easily produce.

