# Ideas for Glas Languages

## Procedural Glas

A variation on g0 extended with procedure and process support, concurrency, and perhaps a channel API. That is, building applications from atomic step functions and stable forks. Should have explicit yield points for live coding support.

## KPN Language

I believe Glas systems would benefit heavily from good support for Kahn Process Networks, especially temporal KPNs (where processes, channels, and messages have abstract time). 

I would like the KPN language to produce KPN representations without any specific assumption about the target platform, i.e. so it could be compiled to multiple targets - including, but not limited to, Glas processes.

## Soft Constraint Search

This is a big one. I'd like to support search for flexible models, especially including metaprogramming. The main thing here would be to express and compose search models, and also develop the optimizers and evaluators for the search.

## Logic Language


## Glas Lisp 

We could support a lisp or scheme variant in Glas. Or perhaps something based on the vau calculus. Each function would take a list/tuple of inputs, and returns a single output. Variables can be supported. 

A relevant issue will be how to express macros to robustly interact with variables. We could potentially support this by applying 'gensym' whenever a variable is declared or introduced, and immediately performing the substitution at parse-time (before any macros apply).

Lightweight support for recursion might be convenient.



