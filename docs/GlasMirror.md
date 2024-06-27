# Mirroring in Glas

Mirroring is replicating an application on a network while maintaining consistency between replicas. 

Mirroring is useful for performance and partitioning tolerance. Regarding performance, mirrors can be closer to the resources they manipulate, reducing latency and bandwidth costs. Mirrors also provide access to remote processors and specialized processors, supporting scalability and acceleration. Regarding partitioning tolerance, mirrors can provide degraded service when the network is disrupted, then automatically recover as network is restored.

The [transaction loop application model](GlasApps.md) supports near perfect mirroring with miniml attention from developers. The relative ease of mirroring can contribute greatly to scalability and resilience of glas systems. This document details my vision for mirroring in glas systems.

## Configuration

Not every application needs to be mirrored, thus configuration of mirroring must be

To configure mirroring, we might define `mirror.*` in the [glas configuration file](GlasConfigLang.md). Among other ad-hoc properties, this might describe a list or named collection of remote virtual machine services in enough detail that the runtime can use them: protocols, addresses, access tokens, architecture descriptions, and so on. The details can be handled later.

Not every application needs full use of mirroring. Thus, we must also provide application settings or runtime reflection APIs to control mirroring per application. It might be useful to support multiple mirroring configurations, allowing the application to select one by name.

It is convenient, though not strictly necessary, to configure a *Distributed Database* for persistent data between mirrors. Without this, there is a greater risk of losing information when a mirror fails. 

## Distributed Runtime

In general, all mirrors of an application, together with origin, are understood as one distributed 'runtime'. This implies mirrors share the same key-value database and RPC registries, and abstract data with 'runtime' lifespan can be shared freely. Also, communication between origin and mirrors is communication within a runtime thus may be implementation specific.

In context of network partitioning, mirrors can access a database in limited ways: reading cached variables, read-write to 'owned' variables, buffering writes to queues (with enough metadata for causal ordering), and full read-write access to local elements of a bag. Similarly, mirrors can each access the RPC registries accessible on the same partition. 

When an RPC registry is visible from multiple locations, it will receive multiple 'copies' of a published RPC object, albeit with reference to different mirrors. In general, runtimes should be smart enough to combine these objects and select a mirror based on latency, load, and other heuristics.

## Mirror Local Effects

Every mirror has the same effects API as origin. Moreover, this API must have the same *meaning* on every mirror that it has on origin. For example, the filesystem API is implicitly bound to the local filesystem on origin. Thus, if a mirror attempts to read file "./foo.txt", this will involve a distributed transaction talking to origin.

Of course, it isn't *impossible* to access the mirror's local filesystem. However, to do so, we'll need to make this also work for origin, preferably without breaking the API. One viable and general approach is to introduce an implicit parameter representing location or perspective, and let this default to origin. 

Access to the mirror's local filesystem isn't very useful, but there are at least two areas where localizing effects is useful for performance and partitioning tolerance: the clock and the network.

For the clock, we might introduce an implicit parameter `sys.time.clock` that can select the clock used in `sys.time.now`. We might default to use 'any' clock, implicitly favoring the local clock. Assuming clocks are synchronized via NTP or PTP, the choice of clock might make very little difference.

Regarding the network, RPC access is implicitly localized. But for TCP and UDP, we might also want to use a specific mirror's local network interface, or we might be willing to use 'any' internet capable interface. If we provide a sockets-based network API, 'bind' to a local interface is already an explicit step, so no API change is needed. Otherwise, we might need another implicit parameter to select a network interface.

## Performance 

Every mirror is implicitly repeating the same transactional step function, but I assume this 'forks' into many threads. As a heuristic rule, if the first location-specific effect in a thread refers to another node, we could let the other node handle that transaction. This trims down the number of threads each mirror is handling and avoids unnecessary distributed transactions.

Threads where it doesn't matter where they run can potentially be moved for load balancing. Variables in the distributed database can also be migrated, moving them closer to the threads that use them. There is probably a lot of math we could do to optimize distributions.

In case of network partitioning, a subset of threads that would require a distributed transaction between partitions will simply be blocked until the network recovers. Another subset, which was running on a now remote partition for load balancing, might start running on the local partition to provide degraded service locally. Those that can run on a mirror without a distributed transaction can simply continue to run, albeit subject to congestion control if they write to a queue that is now blocked by the network partition.

## Long Term Partition Failure

If we have permanent failure, we will lose data 'owned' by the remote partition. Perhaps worse, if we have a long term non-permanent failure, we might try to continue with a recent back-up or cached versions of the database, then the system eventually reconnects and we cannot easily combine data that has evolved independently on the different partitions. 

If this is an expected problem, the right place to solve it is the database layer. We can extend the database with more types that don't assume 'ownership' by a single partition, where the data can be merged. I've already proposed one: the bag type. But [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) and [variants](https://dl.acm.org/doi/10.1145/3360580) are also a reasonable direction.

## Concurrent Use of Mirrors

A single glas configuration might be used by multiple applications concurrently. So, what should happen with the mirroring? One option is to share remote virtual machines between multiple applications, analogous to how multiple processes are created on origin. Another is to insist that mirroring is application specific, report an error if a configured mirror is already in use.

I slightly favor the shared mirrors, but I think this might depend on the 'type' of mirrors we configured. In general, we already want to configure mirrors as application specific because not every application needs this expensive feature.


