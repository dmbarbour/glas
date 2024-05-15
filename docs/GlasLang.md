# Initial Language for Glas

## Outcome

A ".g" file should compile to `g:(Struct of (Namespace of AbstractAssembly))`. In this context, a dict is a simple trie of `(symbol:Value, ...)` with no direct reference between symbols. A [namespace](GlasNamespaces.md) is a more sophisticated 

 containing [abstract assembly](AbstractAssembly.md) definitions. This compiles 
To simplify extensibility, dictionary definitions are initially limited to 'ns' and 'mx' headers, and the 'g' header can help integrate ".g" modules into other languages.



## Desiderata

This is a procedural language without a separate heap. Objects are second-class, in the sense they can be allocated on the stack but not directly returned from a subroutine. This is mitigated by a macro-like system leveraging [abstract assembly](AbstractAssembly.md). A basic procedure is compiled into two parts: a procedure body, plus a simple wrapper macro that evaluates arguments in the caller then calls the procedure body. Other macros may abstract over declaration of local vars, closures, and construction of stack objects.

The language should also support static eval. Users can express assumptions about which parameters and results should be determined at compile time. It should be feasible to support lightweight DSLs, e.g. a syntactic sugar around different monads or GADTs (though true syntax extensions are left to language modules). 

I would like to support *units* on numbers in some sensible way. Phantom types? Associated metadata? Ideally something that doesn't cost too much for serialization and makes sense for basic arithmetic. 

 And perhaps similar on other values. These units should be subject to compile-time analysis, and could provide a lightweight basis as a type system.

. Some lightweight DSLs might be supported in terms of compile-time p of data that represents subprograms. Of course, the glas system fully supports user-defined syntax (by defining language modules), 

, including true front-end extensions by defining lang-g. 


 The glas system also supports staging via 
although ".g" syntax is not directly extensible (indirectly, users could extend lang-g or de

Some lightweight DSLs may be feasible based on this (i.e. a macro could parse a const value representing a program)., though glas also allows users to define other file extensions with ad-hoc syntax.

Ideally, the ".g" language should also support lightweight user-defined DSLs. However, this is inherently limited to structures we can parse without 

I hope to also support lightweight DSLs based on macros. 

## Macros For All

##



