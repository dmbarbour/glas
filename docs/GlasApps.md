# Glas Applications

Glas programs have algebraic effects, which greatly simplifies exploration of different application models. In conjunction with access to programs as values, it is feasible to implement robust adapters layers between models.

The most conventional application model is the procedural loop, i.e. the application code is some variation of `void main() { init; loop { do stuff }; cleanup }`. State and behavior are both private to the application loop, not accessible for extension or inspection. Concurrency is very awkward and error-prone in this model. The only *generic* operation on the running application is to kill it. Glas systems will use this conventional model for command line verbs (i.e. `glas-cli-verb.run` programs) because it's easy to implement due to conventions.

An intriguing alternative is the *transaction machine*, where an application is a dynamic set of transactions that run repeatedly. The loop is implicit, and state is maintained outside the loop, which significantly improves extensibility and enables orthogonal persistence. Concurrent coordination is very natural in this program model. It is feasible to update code atomically at runtime. Transaction machines are a good foundation for my visions of robust live coding as a basis for user-interfaces.

This document explores my vision for the development of applications in Glas systems.

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

It is feasible for a transaction machine to start as a single transaction then introduce a non-deterministic 'fork' effect to represent the set of transactions. This has the advantage of making the set much more dynamic and reactive to observed needs and configurations.

Transactions evaluate in parallel only insofar as conflict is avoided. When conflict occurs between two transactions, one must be aborted by the scheduler. Progress is still guaranteed, and a scheduler can also guarantee fairness for transactions that respect a compute quota. A scheduler can heuristically avoid most conflict based on tracking conflict history. Programmers can avoid conflict based on design patterns, e.g. using channels to separate tasks from high-contention state variables.

### Real-Time Systems 

It is feasible for a transaction to compare estimated time of commit with a computed boundary. If the transaction aborts because it runs too early, the system can implicitly wait for the comparison result to change before retrying. Use of timing constraints effectively enables transactions to both observe estimated time and control their own scheduling. 

Usefully, a system can precompute transactions slightly ahead of time so they are ready to commit at the earliest timing boundary, in contrast to starting computation at that time. It is also feasible to predict several transactions based on prior predictions. It is feasible to implement real-time systems with precise control of time-sensitive outputs.

Transaction machines can flexibly mix opportunistic and scheduled behavior by having only a subset of concurrent transactions observe time. In case of conflict, a system can prioritize the near-term predictions.

### Cached Routing

A subset of transactions may focus on data plumbing - i.e. moving or copying data without observing it. If these transactions are stable, it is feasible for the system to cache the route and move data directly to its destination, skipping the intermediate transactions. 

Designing around cached routing can improve latency without sacrificing visibility, revocability, modularity, or reactivity to changes in configuration or code. In contrast, stateful bindings of communication can improve latency but lose most of these other properties.

Cached routing can partially be supported by dedicated copy/forward APIs, where a transaction blindly moves the currently available data from a source to a destination. However, it can be difficult to use such APIs across abstraction layers. In general, we could rely on abstract interpretation or lazy evaluation to track which data is observed within a transaction.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing factors: 

* code can be updated atomically between transactions
* application state is accessible for transition
* feasible to simulate several cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Embeddings

### Procedural Embedding of Transaction Machines

It is feasible to compile a top-level transactional loop, such as a Glas program of form `loop:(until:Halt, do:cond:try:Step)`, to run as a transaction machine within a procedural context. The data stack can be compiled into a set of transaction variables. Static analysis and runtime instrumentation supporting fine-grained read-write conflict detection. 

The embedding loses implicit live coding, but we still benefit from convenient process control, incremental computing, task-based concurrency, reactive dataflow, etc.. Use of loops to express waits is more composable and extensible than explicit wait effects. I intend to use this procedural embedding in context of [Glas command line interface](GlasCLI.md).

### Transaction Machine Embedding of Procedural

A transaction machine cannot directly evaluate procedural code within a transaction. However, it is feasible to create a concurrent task that repeatedly interprets the procedural code for a few steps then yields, saving the procedure's continuation for a future transaction. 

This embedding is essentially the same as multi-threading. We could manipulate state to kill the procedural thread or spawn new ones. However, the transaction machine substrate does offer a few benefits, such as lightweight support for transactional memory, and a robust foundation to integrate with dataflow paradigms.

### Interleave of Embeddings

Conveniently, these embeddings interleave. Relevantly, if we evaluate a procedure containing a transaction loop, we do not need to update the continuation for that procedure until the loop halts. While the continuation variable is not updated, the loop will be stable for incremental computing and forks.

This supports a flexible integration of programming styles.

## Before Optimization

Transaction machines rely on sophisticated optimizations. Implementation of these optimizations is non-trivial, and they certainly will not be available during early development of Glas systems. A transition plan is required.

Without optimizations, implicit process control reduces to a busy-wait loop. We can mitigate this by simply slowing down the loop. I propose to do so by limiting the polling frequency for external resources. For example, we might limit polling of a TCP channel to 40Hz (a 25ms period). After we fail to read data 7ms ago, the next read would have an 18ms timeout to avoid observing a failed read twice within 25ms. We can poll multiple TCP channels within a 25ms cycle.

Without optimizations, forks are random. There is no replication, parallel evaluation, or incremental computing of forked tasks. Forks will randomly be evaluated even when there is nothing to do. This results in unpredictable latency, reduced scalability, and unnecessary rework. Before optimizations are available, concurrent applications should manually manage their own schedule and cache. A simple case is to run every subtask on every cycle, but allow some tasks to short-circuit based on dirty bits.

These interim solutions are adequate for many applications, especially at small scales. Importantly, they do not pollute the effects API with explicit timeouts, waits, or multi-threading. And they work equally well for procedural interpretation. 

## Abstract Design

### Robust References

References are essentially required for concurrent interaction. For example, an app will interact with *more than one* file or tcp connection at a time.

Applications should allocate their own references. For example, instead of `open file` returning a system-allocated handle, effect APIs should favor the format `open file as foo` where `foo` is an arbitrary value allocated by the application as a handle for future interactions with the file.

This design has a lot of benefits: Abstraction of reference types is optional. References can be descriptive and locally meaningful for debugging. Static allocation of references is easily supported. Dynamic allocation can be partitioned and decentralized, reducing contention. We can avoid centralized allocation as an implicit source of observable non-determinism. There is no security risk related to leaky abstraction or forgery of references. 

The application host will generally maintain a lookup table to associate references with external resources. Garbage collection is feasible if the application can fetch lists of live references.

### Specialized Effects

Effects APIs should be specialized at the host-app layer. For example, `tcp:read:(...)` request is distinct from `file:read:(...)`. The host-app layer should not conflate the responsibility of resource interface abstraction. Applications may introduce their own resource abstraction layer if one is desired.

The host-app boundary is an awkward location for interface abstraction because it only supports abstraction of host objects, whereas within the application we can abstract over both host interfaces and application objects. Additionally, extracting a common interface is a lot of design work, and the resulting one-size-fits-all interface is almost always a little awkward for every use case.

### Asynchronous Effects 

In context of Glas programs, effects are `Request -- Response` thus fit a procedural style. However, in context of transactions or backtracking conditional behavior, external effects will usually be asynchronous because it is much easier to defer messages until commit than it is to implement a distributed transaction. There is a possible exception for 'safe' operations with negligible side-effects (e.g. read and cache a remote value).

### Application State

I propose that application state is represented as a value on the data stack instead of using an effects API. This is consistent with a procedural embedding of transaction machines, makes it clear (structurally and semantically) what state is 'owned' by the transaction (e.g. in contrast to environment variables), and simplifies composition of apps compared to managing a heap. State as one big value is also very convenient for accessibility and reflection.

This design does place a heavier burden on an optimizer to support precise conflict detection, e.g. abstract analysis to partition state into small transaction variables, recognition that certain functions such as list append do not fully observe the values they manipulate.

### Extensible Interfaces

Applications should be extensible with new interfaces. For example, we might extend an application with intefaces to obtain an icon or present an administrative GUI.

In context of Glas programs, we might represent method selectors as part of program input on the data stack, e.g. `method:Args`. Result type and available effects may depend on the selected method. This technique has the advantage of being cheap, with negligible overhead if unused.

*Aside:* Together with placing application state on the data stack, applications might be viewed as objects.

## Common Effects

Effects should be designed for transaction machines yet suitable for procedural code to simplify embeddings.

### Concurrency

Repetition and replication are equivalent for isolated transactions. In context of a transaction machine, within the stable prefix of a transaction, fork can be implemented efficiently by replication, essentially spawning a thread for each stable choice. Outside this context, fork can be implemented as probabilistic choice.

* **fork** - non-deterministic behavior, respond with '0' or '1' bitstring.

Fork is abstractly a function of time, e.g. `Time -> Index -> Bool`, advancing index on each fork. Deep within a hierarchical transaction, because time is logically frozen, 'fork' should backtrack and re-read the same values like other effects. Between top-level transactions, including between steps in a procedural embedding, time advances and a fresh sequence of fork values will be read.

### Logging

Almost any application model will benefit from a simple logging mechanism to support debugging, observability of computations, etc..

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc fields whose meanings are de-facto standardized, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This enables the record to be extended with metadata or new features.

In context of transaction machines and task-based concurrency, the concept and presentation of logging should ideally be adjusted to account for stable forks. Instead of a stream of log messages, an optimal view is something closer to live tree of log messages with access to history via timeline.

Although messages logged by failed transactions are not observable within the program, they can be observed indirectly through reflection APIs which operate on runtime state. A debug view presented to a developer should almost always be based on a reflection API. Aborted messages might be distinguished by rendering in a different color, yet should be accessible for debugging purposes.

### Time

Transactions are logically instantaneous, so we cannot model explicit timeouts or sleeps. However, we could wait on a clock by aborting a transaction until a given time is observed. Proposed effects API:

* **time:now** - Response is the estimated time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail.

Reading 'now' will destabilize a transaction and is unsuitable for waits or incremental computing. The use-case for 'now' is adding timestamps to observed events, such as messages received on a channel. In these cases, the transaction should already be in an unstable state so no harm is done.

Use of 'check' provides a stable option to indirectly observe time. If a transaction fails after a time check fails, the provided timestamp informs the system of how long it should wait. It is feasible to precompute the future transaction, have it ready to commit.

The default TimeStamp type is Windows NT time - a natural number of 100ns intervals since midnight Jan 1, 1601 UT. 

### Environment Variables

The initial use-case for environment variables is to provide access to OS-provided environment variables, such as GLAS_PATH. This can be expressed as `env:get:"GLAS_PATH"`, or `env:get:list` to obtain a list of defined variable names. However, this API is easy to generalize. An environment can provide access to arbitrary variables. It is possible for variables to be computed upon get, or validated upon set (i.e. runtime type checks). Some may be read-only, others write-only. 

* **env:get:Variable** - response is a value for the variable, or failure if the variable is unrecognized or undefined. 
* **env:set:(var:Variable, val?Value)** - update a variable. The value field may be excluded to represent an undefined or default state. This operation may fail, e.g. if environment doesn't recognize variable or fails to validate a value. 

The environment controls environment variables, their types, and opportunity for extension. Programs should not rely on environment variables for private state - that role is handled by the data stack.

## Console Applications

See [Glas command line interface](GlasCLI.md).

## Web Applications

Another promising near-term target for Glas is web applications, compiling apps to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. 


