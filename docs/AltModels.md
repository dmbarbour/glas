# Alternative Program and Data Models

This document explores some alternative program and data models for glas systems.

## Program Model Variations

What I want is a good 'low level' program model for glas that is suitable for incremental and distributed computing with the transaction machine model. This might eventually replace the 'prog' model of programs, or extend it, as a built-in to the 'glas' executable. This model may favor performance over compositionality, simplicity, and locality. 

It can support accelerators while still providing a fallback implementation. Registers may have several primitive data types associated with them.




* every program has a static set of registers for input, output, and memory. 
* Programs are still non-recursive, to avoid issues of runtime register alloc.
* the environment for effects is fine-grained and supports reuse of subprograms.
* effects and reusable subprograms may share some state registers between them.
* it is clear what needs to be saved for incremental computing



## Alternative Data 

Currently, glas data is a binary tree. The ability to represent *sets* and *graphs* would be convenient for some use-cases. However, it is unclear to me how to achieve this while still ensuring data is indexable, shares structure, etc.. Additionally, it is unclear whether '0' and '1' edges are sufficient for graphs.

For now, trees are convenient enough, and were chosen for their ability to directly represent structure of languages.

## Potential Program Models

The main weakness of the glas program model is lack of support for parallelism, concurrency, and distribution. It is feasible to build an alternative program model based on *Lafont interaction networks (LINs)* or *Kahn process networks (KPNs)*, i.e. such that computation is inherently local, monotonic, and confluent.

I'd like a more robust model for purpose of concurrent effects, ideally without hindering an effective approach to live coding.

### Kahn Process Networks

A program model based on KPNs is much more comprehensible to me. The idea is that each program represents a *process* with incremental inputs and outputs, via *channels*. Simple KPNs may use static channels that can be externally wired for composition.

We could extend KPNs to include support for dynamic creation and communication of subchannels, e.g. by including an effect to 'accept' a subchannel or 'attach' a channel over a subchannel. This would need to be paired with some form of dynamic subprocess model. 

However, KPNs feel a little too coarse grained due to operating in terms of processes and channels instead of atoms that can create them. Additionally, it seems an awkward fit for live coding due to embedding a lot of state over time.

### Lafont Interaction Networks

A program model based on LINs would be much more fine-grained than KPNs. An LIN is composed of atomic agents from a fixed set, but wired together in flexible ways. Behavior of an agent isn't locally determined but depends instead on the agent to which it is paired, based on rewrite rules.

A modular LIN consists of a set of atomic or hierarchical agents and edges between them. Concretely, this might be represented by labeling the agents then representing a list of wires.

        agents: (foo:free, bar:free, baz:alpha 
                ,qux:net:( agents:(x:free, y:free, ...), wires:(...))
                )
        wires: [(foo:main, baz:x), (bar:main, qux:x), (baz:y, qux:y), ...]

In this example, I use 'free' as a special agent type to represent free wire, and 'net' for hierarchical structure. Free wires are accessible to external wirings of a 'net' agent. All other agents are atomic symbols, with behavior determined by simple rewrite rules where the main edges of primitive agents are wired. Every agent has at least a 'main' edge.

I hesitate to push forward with LINs because it will take a lot of work to make them usable. Also, they don't play very nicely with live coding if the network structure is dynamic, embedding state of the computation. That said, if LINs are developed to a certain extent, they might become a viable primitive program model for glas systems.








