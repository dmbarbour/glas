# Initial Language for Glas (TODO!)

The glas system can support multiple programming languages, but there 

## Convenient Access to Implicit Environment

Perhaps something like this:

        foo.x       # local foo.x
        :foo.x      # %env.foo.x

## Overrides and Hyperstatic Env

When we import from a source, we might want to hide some definitions and override others. This could be expressed as part of the import alias list (e.g. 'overriding' clauses on load), or alternatively by syntactically discriminating '=' (let, hide prior) vs. ':=' (assign, update in place) or similar. Need to find a solution that makes both comfortable.

## Outcome

The [namespace model](GlasNamespaces.md) describes a process of iteratively writing definitions into an abstract, monotonic, partial namespace. The namespace is never expressed as a concrete value.


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


## Caching

## Types

## Logging

## Profiling

## Proof Carrying Code


## Process Networks



it isn't clear how to make a process networks integrate nicely with effects and transactions.

To support distributed computations at larger scales, it might be convenient to support some model of process networks within glas programs. However, 



I believe Glas systems would benefit heavily from good support for Kahn Process Networks, especially temporal KPNs (where processes, channels, and messages have abstract time). 

I would like the KPN language to produce KPN representations without any specific assumption about the target platform, i.e. so it could be compiled to multiple targets - including, but not limited to, Glas processes.

Instead of dynamic channels, might aim for labeled ports with external wiring. This would be a closer fit for the static-after-metaprogramming structure of glas systems.

## Soft Constraint Search

This is a big one. I'd like to support search for flexible models, especially including metaprogramming. The main thing here would be to express and compose search models, and also develop the optimizers and evaluators for the search.

## Logic Language


## Glas Lisp 

Lightweight support for recursion might be convenient.


