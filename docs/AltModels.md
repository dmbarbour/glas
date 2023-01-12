# Alternative Program and Data Models

This document explores some alternative program and data models for glas systems.

Currently, glas models data as binary trees, and programs using a variation of combinatory logic. However, the current approach hinders convenient expression of parallel and concurrent behavior. And the current approach to data hinders expression of graph structures.

## Alternative Data 

Currently, glas data is a binary tree. The ability to represent *sets* would be convenient for many use-cases, such as representing concurrent requests and responses or relational databases. However, it's important to retain *indexability* of data, similar to the binary tree.

Relatedly, it might be convenient to represent data as *graphs*, allowing for cyclic structure. However, I'm not convinced this is a good idea.

### Set of Bitstrings? Meh.

Representing an index on a *set of bitstrings* requires a relatively lightweight extension to binary trees as data. We simply need one extra bit per tree node to indicate whether the bitstring terminating in that tree node is part of the set.

        type BitsSet = 
            { Stem : Bits
            ; Incl : Bool
            ; Term : (1 + (BitSet * BitSet)
            }

The 'Incl' flag could be integrated into the Stem for performance. In normal form, we can erase any bitstring not included in the BitSet. A record could still be represented by a set of bitstrings.

A related question: is this a useful feature? 

I could directly represent a set of bitstrings using a binary tree. I'd need only to encode some extra size information per bitstring, e.g. in a header or in style of a varnat. So, in the end, there is not much benefit compared to use of binary trees as data.

### Set of Binary Trees? How?

I currently don't know how to efficiently represent a set of trees in a conveniently indexed manner. Fundamentally, representing a set of pairs as a pair of sets would represent a cross product, where we want to represent a specific subset of the cross products.

It is feasible to 'interleave' representations of the left and right children of a pair into a bitstring. However, updating the tree would be non-trivial in this case.


### Sets as Program Model Feature

Ignoring the issue of sets as data, we could support a program model where each item on the data stack consists of a set of values. However, this doesn't need to be at the glas program model layer.

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








