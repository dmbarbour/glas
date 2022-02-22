# Glas Applications

Glas application behavior will be represented by a Glas program with 1--1 arity and a transactional effects API. 

        type App = (init:Params | step:State) → [Effects] (halt:Result | step:State) | Failure

Initial input to the application program is 'init'. The application halts if 'halt' value is returned. If the program returns 'step', the system will evaluate the program again with the step value as input, modeling application state. Evaluation of the program is logically atomic and instantaneous, committing effects after evaluation successfully returns 'step' or 'halt'. When evaluation fails, effects are aborted and the system will later retry with the same initial input.

This application model, the *Transaction Machine*, has nice systemic properties regarding extensibility, composability, debuggability, administration, orthogonal persistence, concurrency, distribution, reactivity, and live coding. However, these benefits depend on optimizations that take advantage of repetition and isolation of transactions. The optimizer is a non-trivial barrier for this application model.

## Transaction Machines

Transaction machines model software systems as a set of repeating, atomic, isolated transactions in a shared environment. Scheduling of transactions is non-deterministic. This is a simple idea, but has many nice properties, especially when optimized.

### Waiting and Reactivity

If nothing changes, repeating a deterministic, unproductive transaction is guaranteed to again be unproductive. The system can recognize a simple subset of unproductive transactions and defer repetition until a relevant change occurs. Essentially, we can optimize a busy-wait into triggering updates on change.

The most obvious unproductive transaction is the failed transaction. Thus, aborting a transaction expresses waiting for changes. For example, if we abort a transaction after it fails to read from an empty channel, we'll implicitly wait on updates to the channel. Successful transactions are unproductive if we know repetition writes the same values to the same variables. Optimizing the success case would support spreadsheet-like evaluation of transaction machines.

Further, incremental computing can be supported. Instead of fully recomputing each transaction, it is feasible to implement repetition as rolling back to the earliest change in observed input and recomputing from there. We can design applications to take advantage of this optimization by first reading relatively stable variables, such as configuration data, then read unstable variables near end of transaction. This results in a tight 'step' loop that also reacts swiftly to changes in configuration data.

### Concurrency and Parallelism

Repeating a single transaction that makes a non-deterministic binary choice is equivalent to repeating two transactions that are identical before this choice then deterministically diverge. We can optimize non-deterministic choice using replication. Usefully, each replica can be stable under incremental computation. Introducing a non-deterministic choice effect enables a single repeating transaction to represent a dynamic set of repeating transactions.

Transactions in the set will interact via shared state. Useful interaction patterns such as channels and mailboxes can be modeled and typefully abstracted within shared state. Transactional updates and ability to wait on flexible conditions also mitigates many challenges of working directly with shared state.

Concurrent transactions can evaluate in parallel insofar as they avoid read-write conflict. When conflict does occur, one transaction will be aborted by the system while the other proceeds. The system can record a conflict history to heuristically schedule transactions to reduce risk of conflict. Additionally, applications can be architected to avoid conflict, using intermediate buffers and staging areas to reduce contention.

### Distribution 

Application state can be partitioned and mirrored across physically separated machines. A random distribution will be inefficient, requiring a distributed transaction for every step. With careful application design and annotation-guided distribution, we can arrange for most transactions to be machine-local, and further optimize most communication between machines.

For example, transaction machines can model a basic channel using a write buffer, a read buffer, and a data plumbing task that repeatedly moves data from (local) write buffer to (potentially remote) read buffer. Data previously in the read buffer is not observed and ideally shouldn't be serialized by this transaction. Ideally, the system would recognize this transaction and optimize it into a simple update message with acknowledgement.

Transaction machines are inherently resilient to network disruption. Operations within each network partition may continue unabated. Operations that communicate between network partitions fail temporarily, then implicitly continue when connectivity is re-established. This behavior is convenient for short-term disruption. Long-term disruption should be handled by weak synchronization patterns within the application, such as pushback buffers and heartbeats.

### Transaction Fusion

It is possible to apply [loop optimizations](https://en.wikipedia.org/wiki/Loop_optimization) to repeating transactions in the transaction machine, especially loop unrolling and loop fusion. For example, if we have transactions `{ A, B }` then we can optionally add fused transactions `{ AA, AB, BA, BB }` without affecting formal behavior. Further, if we know `AB` succeeds whenever `A` would succeed, we can eliminate independent evaluation of `A`.

Fusion can eliminate data plumbing tasks, reduce context switching overheads, and provide opportunity for deep optimizations at the fusion boundary. Importantly, aggressive fusion allows programmers to use fine-grained tasks without worrying about how runtime performance is affected. This simplifies modular development.

### Real-Time Systems 

Transaction machines can wait on the clock by (logically) aborting a transaction until a desired time is reached. Assuming the system knows the time the transaction is waiting for, the system can schedule the transaction precisely and efficiently, avoiding busy-waits from watching the clock. Usefully, the system can precompute a transaction slightly ahead of time, such that effects apply almost exactly on time.

Further, we could varify that critical transactions evaluate with worst-case timing under controllable stability assumptions. This enables "hard" real-time systems to be represented.

### Live Coding

The application program can be updated at runtime between transactions. This would naturally destabilize incremental computing and concurrent replicas, but those can be swiftly regenerated. Application state would immediately be handled by the updated transaction.

Transaction machines are only a partial solution for live coding. The application must also include transition logic for the state value, as needed, or must be careful to use a stable state type across application versions. Thus, support from the development environment is the other major part of the solution.

### Composition and Extension

A large transaction machine application can be composed of smaller applications. 

Sequential composition will 'halt' one application before 'init' the next, and will track location for intermediate steps. Concurrent composition can either run both component applications to completion or race them, halting when the first component returns. Although limited to hierarchical structure (a concurrent child task does not survive its parent returning 'halt'), this is adequate for many applications.

Concurrent composition will need a model for communication between component applications. For example, we could use effects to access a shared database, tuple space, databus, or publish-subscribe layer. Or we could associate each application with a set of labeled 'ports' for message passing that can be externally wired together. 

Application private state, represented in the `step:State` value, can potentially be observed and manipulated on every step. This is where I draw the line between 'composition' and 'extension'. Extensions can support ad-hoc views, controls, behaviors, and other features based on access to state application private state.

### Flexible Integration with Procedural Programming

Procedural code is a good fit for some applications. In context of a transaction machine, a procedural program must be evaluated over multiple transactional steps. When a procedural step fails, it is logically retried until it succeeds, representing waits and blocking calls. A procedure's continuation will be represented in application state at the transaction machine layer, but syntax for procedural code should make the continuation implicit. 

Procedures can be separately compiled into transaction machines, then joined procedurally using sequential composition. Relatedly, the procedural language could expose lightweight concurrency and atomic blocks, e.g. `atomic { ... }`. All conventional concurrency primitives, such as mutexes and condition waits, can be implemented by atomic blocks that fail if conditions aren't right. 

For performance, we'll want to fuse small procedural steps into larger transactions. Doing so enables further optimizations because we eliminate external interference between fused steps. This is a form of *Transaction Fusion*, but it might be simpler to implement at the procedural layer, i.e. typefully tracking which procedures are blocking and heuristically joining non-blocking sequential operations into larger 'atomic' blocks.

*Note:* An implicit continuation state is troublesome in context of live coding and non-invasive extensions. Ideally, the continuation should align with stable syntactic structure, such as explicit labels in source code.

## Concrete Design

### Initial Performance Mitigation

Before optimizations are available, programmers should avoid use of concurrency. Failed transactions could cause evaluation to pause for a few milliseconds before retrying. There is no incremental computing, but programmers could use manual approaches such as maintaining an event queue or dirty bits in state. This is adequate for simple single-threaded apps, but it won't scale nicely to waiting on many input sources.

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

Time 'check' provides stable, monotonic, indirect observation of time. If a transaction aborts after a failed time check, the runtime can implicitly wait for specified time (or other relevant changes) to retry. Time check is useful for modeling timeouts and scheduling.

Reading 'now' will destabilize a transaction because time is always advancing in the background. But this is not a problem if the time is read after another destabilizing input. For example, we could use 'now' after reading from an input channel to associate a received timestamp with each incoming message.

By default, timestamps are in NT time: a natural number of 100ns intervals since midnight Jan 1, 1601 UT. 

### Concurrency

Repetition and replication are equivalent for isolated transactions. In context of a transaction machine, within the stable prefix of a transaction, fork can be implemented efficiently by replication, essentially spawning a thread for each stable choice. Outside this context, fork can be implemented as probabilistic choice.

* **fork** - non-deterministic behavior, respond with '0' or '1' bitstring.

Fork is abstractly a function of time. Relevantly, a hierarchical transaction may observe 'fork' backtracking within logically frozen time, but there is no history maintained between transactions or procedural steps.

### Distribution

Application state is represented in the `step:State` tree value. Instead of partitioning variables across machines, we'll partition stable branches of this tree. The read and write buffers for a channel can be represented by lists on two different machines. We can mirror stable regions of the tree for efficient read-only access. Distributed application programs should include annotations and types to support intelligent distribution and resist accidental performance degradation.

Effects APIs should also be extended to work with machine-specific resources. A viable, non-invasive option is to wrap machine-local effects with an explicit location, e.g. `at:(loc:MachineRef, do:LocalEffect)`. Transactions that reference multiple locations can be useful and would be implemented as distributed transactions, but should be avoided in normal use cases. 

### Logging

Logging is a convenient approach to debugging. We can easily support a logging effect. Alternatively, we could introduce logging as a special debugging annotation, similar to breakpoints and profiling. 

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc fields, whose meanings are de-facto standardized, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This enables the record to be extended with metadata or new features.

In context of transaction machines and fork-based concurrency, logs might be presented as a stable tree (aligned with forks) potentially with unstable leaf nodes. Logically, the entire tree updates, every frame, but log messages in the stable prefix of a fork will also be stable outputs. Further, messages logged by failed transactions might be observable indirectly via reflection, perhaps rendered in a distinct color.

### Random Data

Due to different intention leading to different optimizations, requests for random data from operating system or hardware should be distinct from 'fork' effects. 

* **random:Count** - response is requested count of cryptographically random bits as a bitstring. E.g. `random:8` might return `00101010`. Treated as an unstable read for incremental computing.

## Misc

### Console Applications

See [Glas command line interface](GlasCLI.md).

### Web Applications

Another promising near-term target for Glas is web applications, compiling apps to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Transaction machines are a good fit for web apps, I think.
