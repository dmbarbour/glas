# Glas Applications

Glas programs have algebraic effects, which simplifies exploration of different effects APIs. In conjunction with access to programs as values, it is feasible to implement robust adapters layers between APIs.

The most conventional application model is the procedural loop, i.e. the application code is some variation on `void main() { init; loop { do stuff }; cleanup }`. However, concurrency, reactivity, and distribution are awkward and error-prone in this model. The state is inaccessible, hidden within the loop, which hinders nice features such as live programming or orthogonal persistence.

An intriguing alternative is to encode applications directly as *Transaction Machines*. A transaction machine application is expressed as a transaction that is evaluated repeatedly until reaching a halting state. Application state is separated from transaction logic, and is much more accessible. Updates to the transaction logic can be deployed between transactions at runtime. We can leverage nice properties of transactions as a simple foundation for concurrency and reactivity. 

Glas programs are a good fit for transaction machines because backtracking conditional behavior is already essentially transactional. This already imposes on the effects APIs. The [Glas Command Line Interface](GlasCLI.md) takes transaction machines as the standard application model in Glas systems. 

This document explores my general vision for the development of applications in Glas systems.

## Transaction Machines

Transaction machines model software systems as a dynamic set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has very many nice emergent properties that cover a wide variety of systems-level concerns. The cost is that a high-performance implementation is non-trivial, requiring optimizations from incremental computing, replication on fork, and cached routing.

### Process Control

Deterministic, unproductive transactions will be unproductive when repeated unless there is a relevant change in the environment. Thus, the system can optimize a busy-wait loop by directly waiting for relevant changes. Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. 

### Reactive Dataflow

A successful transaction that reads and writes variables (no external effects) is unproductive if the written values are equal to the original content. If there is no cyclic data dependency, then repeating the transaction will always produce the same output. If there is a cyclic data dependency, it is possible to explicitly detect change to check for a stable fixpoint.

A system could augment reactive dataflow by scheduling transactions in a sequence based on the observed topological dependency graph. This would minimize 'glitches' where two variables are inconsistent due to the timing of an observation.

*Aside:* Transaction machines can also use conventional techniques for acting on change, such as writing update events or dirty bits.  

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail implicitly loops to process channels or fast-changing variables.

Stable prefix and attention from the programmer is adequate for transaction machine performance. However, it is feasible to take incremental computing further with reordering optimizations such as lazy reads, or implicitly forking a transaction based on dataflow analysis.

### Task-Based Concurrency and Parallelism

Relevant observations: A non-deterministic transactions is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When a non-deterministic choice is stable, replication reduces recomputation and latency. 

It is feasible for a transaction machine to start as a single transaction then introduce a non-deterministic 'fork' effect to represent the set of transactions. In contrast to explicit threading, use of fork results in a reactive set of tasks, no need to explicitly 'kill' tasks. Additionally, this does not conflict with effects handlers.

Concurrent transactions can evaluate in parallel insofar as they operate on different parts of memory, or do not modify shared dependencies. When a read-write conflict occurs between parallel transactions, one must be aborted. Progress can be guaranteed, and a good scheduler can additionally guarantee fairness for transactions that meet reasonable time-quota limits. 

Programmers can improve parallelism by software architecture and design patterns that avoid read-write conflict. For example, using channels or buffers to communicate between tasks.

### Distributed Computation 

It is possible to distribute state and computation across multiple physical machines. Distribution of state may include partitioning and mirroring, depending on how that state is used. However, distributed transactions are expensive and should be avoided. With careful application design, we can arrange for most transactions to be machine-local and implement many remaining transactions with efficient point-to-point message passing.

Relevantly, when a transaction blindly updates state on a single remote machine, the transaction can be very efficiently implemented using an update message with acknowledgement. Blind updates could set a variable, increment a number, or extend a list. Basic channels can be modeled using a read buffer, a write buffer, and a repeating transaction that removes data from the write buffer and blindly adds it to the read buffer. Assuming optimization, performance will not be worse than imperative implementations.

Transaction machines are naturally resilient to network disruption. Tasks local to each network partition may continue evaluating independently, but transactions that would communicate across network partitions must implicitly wait until communication is re-established. However, loss of control is a concern, e.g. the application cannot properly halt while the network is partitioned. Control could be mitigated by explicitly modeling heartbeats or network pushback within the application.

### Transaction Fusion

It is possible to apply [loop optimizations](https://en.wikipedia.org/wiki/Loop_optimization) to repeating transactions in the transaction machine, especially loop unrolling and loop fusion. For example, if we have transactions `{ A, B }` then we can optionally add fused transactions `{ AA, AB, BA, BB }` without affecting the formal behavior of our transaction machine. Further, if we know `AB` succeeds whenever `A` would succeed, we can eliminate independent evaluation of `A`.

Fusion can eliminate machine-local data plumbing (including a channel's network transactions), reduce context switching overheads, and provide opportunity for deep optimizations at the fusion boundary. Importantly, aggressive fusion allows programmers to use fine-grained tasks without worrying about how runtime performance is affected. This simplifies modular development.

### Real-Time Systems 

It is feasible for a transaction to compare estimated time of commit with a computed boundary. If the transaction aborts because it runs too early, the system can implicitly wait for the comparison result to change before retrying. Use of timing constraints effectively enables transactions to both observe estimated time and control their own scheduling. 

Usefully, a system can precompute transactions slightly ahead of time so they are ready to commit at the earliest timing boundary, in contrast to starting computation at that time. It is also feasible to predict several transactions based on prior predictions. It is feasible to implement real-time systems with precise control of time-sensitive outputs.

Transaction machines can flexibly mix opportunistic and scheduled behavior by having only a subset of concurrent transactions observe time. In case of conflict, a system can prioritize the near-term predictions.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing factors: 

* code can be updated atomically between transactions
* application state is accessible for transition
* feasible to simulate several cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Embeddings

### Procedural Embedding of Transaction Machines

It is feasible to compile a transactional loop within a procedural program to run as a transaction machine. This embedding loses several benefits related to extensibility, composability, and live coding of applications. However, it could retain the benefits related to process control, reactive behavior, concurrency, distribution, incremental computing, etc..

### Transaction Machine Embedding of Procedural

For some programs and subprograms, procedural code is a good fit. Procedural code can be interpreted as or compiled into a state machine that will evaluate over multiple transactional steps. In context of the transaction machine medium, the procedural language could be extended with transaction blocks, allowing for implicit waits or explicit fallbacks when the transaction would fail. 

### Interleave of Embeddings

Conveniently, these embeddings interleave. Relevantly, when a transaction loop evaluates a procedure that contains another transaction loop, we can evaluate the inner loop without updating continuation state until the loop halts. This allows for a 'stable' loop with respect to incremental computing and replication of forks. No need for special optimizations.

## Concrete Design

### Application Behavior

Application behavior will be represented by a Glas program with 1--1 arity and a transactional effects API. 

        type App = (init:Params | step:State) → [Effects] (halt:Result | step:State | Failure)

The program is evaluated in an implicit transaction. The first evaluation inputs 'init' with parameters. Termination is indicated by returning 'halt' with a final result. If evaluation returns 'step', effects are committed then evaluation continues in another transaction, taking the step output as next input. If evaluation fails, effects are aborted then evaluation retries with prior input, implicitly waiting for changes or selecting another non-deterministic choice.

Application state is explicit in the behavior type, and is accessible for composition or extension, including debug views. However, representing state as a single large value between steps is inefficient. An optimizer might use abstract interpretation to partition state into fine-grained variables under the hood.

### Mitigating Performance

Sophisticated optimizations are required to make waiting and fork-based concurrency acceptably efficient. Before those optimizations are available, programmers should either avoid concurrency or model it indirectly, e.g. using an event queue and centralized dispatch. Waits can be simplified to retrying after a few milliseconds, which is not optimal but is sufficient for many use cases.

### Robust References

Conventional APIs return allocated references to the client, e.g. `open file` returns a file handle. However, this design is unstable under concurrency and difficult to secure. A more robust API design for references is to have applications allocate their own references. For example, `open file as foo` where `foo` is a user-provided value. 

Further operations such as read and close would then use `foo` value to reference the open file. The host would maintain a translation table between application references and runtime objects. Compared to system allocation of references, it is much easier to secure which references a subprogram may use, e.g. based on prefix. References can be stable and meaningful, which should simplify debugging and reflection. Allocation of references can be decentralized, reducing contention on a central allocator.

### Asynchronous Effects

Effects must be aborted when evaluation fails. This has a huge impact on API design. For example, asynchronous writes are popular because we can simply not send anything if we abort. Synchronous operations are mostly limited to manipulating application or runtime state.

A useful exception is stable, cacheable reads. For example, loading a module from the module system, or reading a file's content, or even HTTP GET could potentially be performed within a transaction.

### Specialized Effects

Runtime effects APIs should be specialized. For example, although file streams and TCP streams are remarkably similar, `tcp:read:(...)` request should be separate and distinct from `file:read:(...)`. Attempting to generalize hinders domain specialized features. It is better for the runtime to provide a more specialized API then let the application implement its own abstraction layer as another effects handler if desired.

## Common Effects APIs

Effects should be designed for transaction machines yet suitable for procedural code to simplify embeddings.

### Time

Transactions are logically instantaneous. The concept of 'timeout' or 'sleep' is incompatible with transactions. However, we can constrain a transaction to commit before or after a given time. We can also estimate time of commit then abort if the estimate is too far off. Proposed effects API:

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail.

Time 'check' provides stable, monotonic, indirect observation of time. If a transaction aborts after a failed time check, the runtime can implicitly wait for specified time (or other relevant changes) to retry. Time check is useful for scheduling future activity, such as timeouts.

Reading 'now' will destabilize a transaction because time is always advancing. To avoid a busy-wait loop, reading time should be performed only by transactions that are already unstable for other reasons, such as reading from a channel. This is useful for adding timestamps to received events or data.

The default TimeStamp is Windows NT time - a natural number of 100ns intervals since midnight Jan 1, 1601 UT. 

### Concurrency

Repetition and replication are equivalent for isolated transactions. In context of a transaction machine, within the stable prefix of a transaction, fork can be implemented efficiently by replication, essentially spawning a thread for each stable choice. Outside this context, fork can be implemented as probabilistic choice.

* **fork** - non-deterministic behavior, respond with '0' or '1' bitstring.

Fork is abstractly a function of time. Relevantly, a hierarchical transaction may observe 'fork' backtracking within logically frozen time, but there is no history maintained between transactions or procedural steps.

### Distribution

Application state is represented in the `step:State` tree value. Instead of partitioning variables across machines, we'll partition stable branches of this tree. The read and write buffers for a channel can be represented by lists on two different machines. We can mirror stable regions of the tree for efficient read-only access. Distributed application programs should include annotations and types to support intelligent distribution and resist accidental performance degradation.

Effects APIs should also be extended to work with machine-specific resources. A viable, non-invasive option is to wrap machine-local effects with an explicit location, e.g. `at:(loc:MachineRef, do:LocalEffect)`. Transactions that reference multiple locations can be useful and would be implemented as distributed transactions, but should be avoided in normal use cases. 

### Logging

Almost any application model will benefit from a simple logging mechanism to support debugging, observability of computations, etc..

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc fields, whose meanings are de-facto standardized, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This enables the record to be extended with metadata or new features.

In context of transaction machines and fork-based concurrency, logs might be presented as a stable tree (aligned with forks) potentially with unstable leaf nodes. Logically, the entire tree updates, every frame, but log messages in the stable prefix of a fork will also be stable outputs. Further, messages logged by failed transactions might be observable indirectly via reflection, perhaps rendered in a distinct color.

### Random Data

Due to different intention leading to different optimizations, requests for random data from operating system or hardware should be distinct from 'fork' effects. 

* **random:Count** - response is requested count of cryptographically random bits as a bitstring. E.g. `random:8` might return `00101010`. Treated as an unstable read for incremental computing.

## Console Applications

See [Glas command line interface](GlasCLI.md).

## Web Applications

Another promising near-term target for Glas is web applications, compiling apps to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. 

## FFI-Based Applications

It is feasible to support effects based on FFI, e.g. via C, .NET, or JVM. In these cases we must defer FFI calls until commit then return a result via promise pipelining or similar. Disadvantages include reducted portability and security, but we could use layers of effects handlers to mitigate the FFI.

## Asynchronous External Effects? No.

An early design I had is essentially `send:(ch:Chan, msg:Request)` paired with `recv:Chan` effects as a common API for asynchronous request-response. Channels help control order of events: requests on separate channels may evaluate concurrently, while requests on a single channel must evaluate in order. The requests available would be consistent across channels. Each request produces a response on the same channel, and 'recv' will fail until the response is available. 

This design simplifies integration with FFI. For example, we could have requests for opening, reading, and writing files that almost directly adapt the existing APIs and fire up a background thread per active channel.

However, this results in awkward APIs within the application. It isn't feasible to synchronously report current status of prior requests, for example. Additionally, it requires more arbitrary data conversions, e.g. to deal with signed integers and exceptions. I think it's wiser to develop a specialized API for desired effects, focusing on TCP and UDP for general integration.

