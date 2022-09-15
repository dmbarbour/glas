# Program Search

This document explores how program search might be supported in Glas.

## Design Goals

* Avoid named variables, i.e. favor a tacit program model. This simplifies composition, decomposition, and metaprogramming. This might be achieved via stack of anonymous constraint/unification variables or graph structure, for example.
* Modular partial evaluation and memoization. This likely requires monotonic heuristics that can be evaluated locally without full knowledge of context. 
* Handles ambiguity. A word in a program could refer to a range of meanings that only become obvious given context of other words. 
* Handles vagueness. Meanings can be missing some details that can be filled in later, preferably with reference to context.
* Flexible output model. Doesn't need to be a Glas program, but can be one. Output could also be a DSL or something else entirely. 
* Supports extension. There is a mechanism to effectively compose subprograms that can grow or refine the search space. This likely requires a structured search space.

