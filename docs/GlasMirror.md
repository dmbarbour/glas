# Mirroring in Glas

Mirroring is replicating an application on a network while maintaining consistency between replicas. 

Mirroring is useful for performance and partitioning tolerance. Regarding performance, mirrors can be closer to the resources they manipulate, reducing latency and bandwidth costs. Mirrors also provide access to remote processors and specialized processors, supporting scalability and acceleration. Regarding partitioning tolerance, mirrors can provide degraded service when the network is disrupted, then automatically recover as network is restored.

The [transaction loop application model](GlasApps.md) supports near perfect mirroring with miniml attention from developers. The relative ease of mirroring can contribute greatly to scalability and resilience of glas systems. This document details my vision for mirroring in glas systems.

## Configuration

The details for mirrors should be expressed within the configuration file, and the configuration may be application specific (i.e. has access to `settings.*`). 

In general, the configuration would need to include a list of mirrors. And for each mirror we would need enough information to activate a remote node, authorize and authenticate, cross-compile, push code, and so on. 

Details TBD.

## Distributed Runtime

I find it useful to understand mirroring as implmenting a distributed runtime for the application. That is, we have one distributed application that happens to shares a database, has access to multiple network interfaces, and where the runtime itself is partitioning tolerant. Communication between mirrors may be specialized to the runtime version or even to the application (with compiler support).

Some 'effects' may be supported on multiple mirrors. Notably, access to `sys.time.*` could use the mirror-local clock, and the network API could support binding to mirror-local network interfaces. Other effects, such as filesystem access, would implicitly bind to origin.

Every mirror would publish to the same RPC registries, but glas runtimes should be mirroring-aware, able to merge the same RPC object from multiple mirrors into one. Which RPC instance is favored can be based on heuristics such as latency and load balancing.

## Performance 

Every mirror logically runs the same 'step' transactions, but we might heuristically abort any transaction whose first location-specific effect would require a distributed transaction with another mirror. The premise is that it's better to initiate that transaction on the other mirror, where it *might* run locally, avoiding the expensive distributed transaction.

For the database, 'ownership' of some variables may also be migrated. This would influence which steps require distributed transactions.

I expect a whole mess of math would be required to optimize the distribution of variables to maximize performance. Rather than relying entirely on math, we might express a programmer's assumptions about proximity and partitioning behavior via annotations within the program.

## Long Term Partition Failure and Recovery

The notion of 'ownership' of state is acceptable for short-term partitioning, but becomes awkward for long-term partition failures. To support long-term partitioning, we should instead favor state types that are ownerless, i.e. where each partition can continue performing reads and writes locally on its own instance, and where mirrored instances interact in a simple way while connected.

The 'bag' type is one case of this: every partition can have its own local instance of the bag, and while partitions are connected we're free to heuristically migrate data between instances. [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are another viable class of types.

In any case, this is mostly a problem to solve via database APIs.

## Live Coding and Mirrors

The 'switch' transaction only commits once for a given code change, so it's probably more convenient if a specific mirror - usually origin - 'owns' that responsibility.  All code updates would propagate from that mirror. Similarly, the origin would be responsible for 'start'.

## HTTP and Mirrors

Usefully, every mirror could provide the HTTP service locally, and we could integrate with conventional load balancers and such.

## Multi-Application Mirrors

Similar to how we might run multiple glas applications concurrently on 'origin', it would be convenient if we can easily support multiple applications concurrently on the mirrors. This might be supported by modeling mirroring as a remote service, perhaps based on virtual machines, that can run multiple processes.

