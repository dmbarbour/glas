# Ideas for Glas Languages

## KPN Language

I believe Glas systems would benefit heavily from good support for Kahn Process Networks, especially temporal KPNs (where processes, channels, and messages have abstract time). 

I would like the KPN language to produce KPN representations without any specific assumption about the target platform, i.e. so it could be compiled to multiple targets - including, but not limited to, Glas processes.

Instead of dynamic channels, might aim for labeled ports with external wiring. This would be a closer fit for the static-after-metaprogramming structure of glas systems.

## Soft Constraint Search

This is a big one. I'd like to support search for flexible models, especially including metaprogramming. The main thing here would be to express and compose search models, and also develop the optimizers and evaluators for the search.

## Logic Language


## Glas Lisp 

We could support a lisp or scheme variant in Glas. Or perhaps something based on the vau calculus. Each function would take a list/tuple of inputs, and returns a single output. Variables can be supported. 

A relevant issue will be how to express macros to robustly interact with variables. We could potentially support this by applying 'gensym' whenever a variable is declared or introduced, and immediately performing the substitution at parse-time (before any macros apply).

Lightweight support for recursion might be convenient.



