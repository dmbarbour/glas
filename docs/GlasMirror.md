# Mirroring in Glas

Mirroring is replicating an application on a network while maintaining consistency between replicas. 

Mirroring is useful for performance and partitioning tolerance. Regarding performance, mirrors can be closer to the resources they manipulate, reducing latency and bandwidth costs. Mirrors also provide access to remote processors and specialized processors, supporting scalability and acceleration. Regarding partitioning tolerance, mirrors can provide degraded service when the network is disrupted, then automatically recover as network is restored.

The [transaction loop application model](GlasApps.md) supports near perfect mirroring with miniml attention from developers. The relative ease of mirroring can contribute greatly to scalability and resilience of glas systems. This document details my vision for mirroring in glas systems.

## Configuration

To configure mirroring, we might define `mirror.*` in the [glas configuration file](GlasConfigLang.md). Among other ad-hoc properties, this might describe a list or named collection of remote virtual machine services in enough detail that the runtime can use them: protocols, addresses, access tokens, architecture descriptions, and so on. The details can be handled later.

Not every application needs full use of mirroring. Thus, we must also provide application settings or runtime reflection APIs to control mirroring per application. It might be useful to support multiple mirroring configurations, allowing the application to select one by name.

It is convenient, though not strictly necessary, to configure a *Distributed Database* for persistent data between mirrors. Without this, there is a greater risk of losing information when a mirror fails. 

## Distributed Runtime

In general, all mirrors of an application, together with origin, are understood as one distributed 'runtime'. This implies mirrors share the same key-value database and RPC registries, and abstract data with 'runtime' lifespan can be shared freely. Also, communication between origin and mirrors is communication within a runtime thus may be implementation specific.

In context of network partitioning, mirrors can access a database in limited ways: reading cached variables, read-write to 'owned' variables, buffering writes to queues (with enough metadata for causal ordering), and full read-write access to local elements of a bag. Similarly, mirrors can each access the RPC registries accessible on the same partition. 

When an RPC registry is visible from multiple locations, it will receive multiple 'copies' of a published RPC object, albeit with reference to different mirrors. In general, runtimes should be smart enough to combine these objects and select a mirror based on latency, load, and other heuristics.

## Mirror Effects

The same effects API is provided to origin and every mirror. 

This can be awkward given that many effects are implicitly bound to resources on origin, such as console IO and filesystem access. However, this could be mitigated by annotations and heuristic optimization rules. No need to start an expensive distributed transaction here when the first effect is obviously over there.

For both performance and partitioning tolerance, it is best if we can 'localize' common effects into the mirrors. In many cases, this might involve a minor cost to consistency.

For example, instead of `sys.time.now` referring to the origin clock, we could let each mirror maintain a local clock and maintain a best effort at synchronization (perhaps using NTP or PTP). This isn't perfect, but it could be very difficult to notice.

Another useful 'effect' a mirror might provide is general network access. An [XMLHttpRequest](https://en.wikipedia.org/wiki/XMLHttpRequest) API could run on any node and access many useful network resources. More awkwardly, we could create a TCP listener bound to a mirror's network interface, treating those mirrors as part of one distributed runtime.

*Note:* Any specialized effects available to some mirrors should be expressed as RPC resources, or accessed through the general network APIs.

## Concurrent Use of Mirrors

A single glas configuration might be used by multiple applications concurrently. So, what should happen with the mirroring? One option is to share remote virtual machines between multiple applications, analogous to how multiple processes are created on origin. Another is to insist that mirroring is application specific, report an error if a configured mirror is already in use.

I slightly favor the shared mirrors, but I think this might depend on the 'type' of mirrors we configured. In general, we already want to configure mirrors as application specific because not every application needs this expensive feature.

## Optimizing Mirrors

