# Program Search

This document explores how program search might be supported in Glas.

I'm interested in program search as a long-term goal for glas systems. But my vision for this also has some nuance, such as the importance of modularity, incremental computation, and iterative refinement.

## Some Ideas

Something similar to prototype object-orientation is suitable, in the sense that we 'instantiate' objects that have only one final value, but we may have multiple independent instances within a larger program, yet those instances might not be fully independent due to sharing some decisions or context.

Hard constraints need clear, preferably modular dataflow dependencies, such that we can check them ASAP and filter results. This is the case even if the constraints are scattered throughout the codebase.

Soft constraints need to be monotonic, such that searches are more incremental in nature. This could be based on scoring 'costs', and perhaps by providing some vector of weights for different kinds of costs.

It should be feasible to extract search paths and apply machine learning to improve search performance.

## Design Goals

* Avoid named variables, at least for the lower level representation, i.e. favor a tacit program model. This simplifies composition, decomposition, and metaprogramming. This might be achieved via stack of anonymous constraint/unification variables or graph structure, for example.
* Modular partial evaluation and memoization. This likely requires monotonic heuristics that can be evaluated locally without full knowledge of context. 
* Handles ambiguity. A word in a program could refer to a range of meanings that only become obvious given context of other words. 
* Handles vagueness. Meanings can be missing some details that can be filled in later, preferably with reference to context.
* Flexible output model. Doesn't need to be a Glas program, but can be one. Output could also be a DSL or something else entirely. 
* Supports extension. There is a mechanism to effectively compose subprograms that can grow or refine the search space. This likely requires a structured search space.

