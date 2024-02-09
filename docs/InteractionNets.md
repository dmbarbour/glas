# Interaction Nets

Interaction nets are a graph rewrite model where each node has exactly one 'active' port, and rewrite rules exist for pairs of nodes connected on active ports. Multiple rewrites can occur in parallel, and evaluation is deterministic if each rewrite rule is deterministic. Link: [a good introduction to interaction nets](https://zicklag.github.io/blog/interaction-nets-combinators-calculus/).

We can optimally model a useful subset of lambda calculus with interaction calculus. But I'll probably need to develop node types that are very suitable for what I want to express, including the glas data model.


