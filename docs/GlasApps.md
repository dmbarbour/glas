# Glas Applications

Glas programs have algebraic effects, which simplifies exploration of different effects APIs. In conjunction with access to programs as values, it is feasible to implement robust adapters layers between APIs.

The most conventional application model is the procedural loop, i.e. the application code is some variation of `void main() { init; loop { do stuff }; cleanup }`. However, concurrency, reactivity, and distribution are awkward and error-prone in this model. The state is inaccessible, hidden within the loop, which hinders nice features such as live programming or orthogonal persistence.

An intriguing alternative is to encode applications directly as *Transaction Machines*. A transaction machine application is expressed as a transaction that is evaluated repeatedly. Application state is separated from transaction logic, and is much more accessible. Updates to the transaction logic can be deployed between transactions at runtime. We can leverage nice properties of transactions as a simple foundation for concurrency and reactivity. 

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

An effects API can explicitly model effects at different locations, e.g. filesystem per machine. Distributed transactions, which evaluate effects at multiple locations, are difficult to make robust and efficient. Ideally, programmers should arrange for tasks to either be location-independent (allowing migration for performance) or location-specific (stable evaluation on a specific machine).

Channels are convenient for communication between locations, avoiding synchronization. A basic channel can be modeled as a list variable where the reader takes from list head and the writer appends to list tail. Because reader and writer operate abstractly on different parts of the list, read-write conflicts are avoided. Messages buffered in the list abstract over network latency.

Distributed computation of transaction machines should be based around forking stable tasks to evaluate on remote machines, then communicate between tasks on different machines via channels. Effectively, a transaction machine will represent an overlay network on a distributed system.

### Transaction Fusion

Concurrent transactions can easily be fused into larger transactions. For example, instead of scheduling A and B independently, we can construct and schedule a combined transaction AB. This is most useful when AB enables optimizations that A and B independently do not support, or eliminates scheduling overheads for lightweight data-plumbing transactions. 

In context of live coding or open systems, it is very useful if fusion optimizations are performed at runtime, as a sort of just-in-time compilation. Together with distributed computation techniques, this enables transaction machines to effectively overlay and patch a system without compromising performance, modularity, or security.

*Note:* Relatedly, we can also fuse a transaction with itself. This is effectively a form of loop unrolling. Given transaction A, we produce AA then check if useful optimizations are exposed.

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

It is feasible to compile a top-level transactional loop, such as a Glas program of form `loop:(until:Halt, do:cond:try:Step)`, to run as a transaction machine within a procedural context. In the interest of predictable performance, a compiler should require an explicit hint to recognize and optimize the loop as a transaction machine, e.g. `prog:(do:loop:(...), accel:txloop, ...)`.

In context of this loop, the data stack might be compiled into a set of transaction variables. Abstract interpretation together with runtime instrumentation can feasibly support fine-grained read-write conflict detection. It is feasible to recognize *channels* as a patterned use of list variables, i.e. channels are just lists on the data stack that we optimize based on usage. 

The embedding loses implicit live coding, but we still benefit from convenient process control, incremental computing, task-based concurrency, reactive dataflow, potential for parallelism and distribution, etc.. 

### Transaction Machine Embedding of Procedural

It is feasible to create a concurrent task where each transaction steps through procedural code, interpreting it and waiting for data as needed. Evaluating a collection of procedural continuations essentially models multi-threaded systems within a transaction machine, albeit with benefits of transactional memory. Evaluation of individual steps could be accelerated for performance.

### Interleave of Embeddings

Conveniently, these embeddings interleave. Relevantly, when a transaction loop evaluates a procedure that contains another transaction loop, we can evaluate the loop without updating the continuation state until the loop halts. This allows for a 'stable' loop with respect to incremental computing and replication of forks. No need for special optimizations.

## Before Optimization

Transaction machines rely on sophisticated optimizations. Implementation of these optimizations is non-trivial, and they certainly will not be available during early development of Glas systems. A transition plan is required.

Without incremental computing optimizations, implicit process control reduces to a busy-wait loop. We can mitigate this by simply slowing down an unproductive loop. For example, we could limit a loop that fails to do anything to 40Hz, adjustable via annotation. This might apply only to the 'txloop' accelerator in a procedural embedding.

Without replication and incremental computing optimizations, use of 'fork' for task-based concurrency results in unpredictable latency and unnecessary rework. We can mitigate this by avoiding 'fork' until later. Early applications can be designed around a centralized event polling loop instead of concurrent tasks.

These interim solutions are adequate for many applications, especially at smaller scales. Importantly, they do not pollute the effects API and also work for procedural embeddings.

## Abstract Design

### Application State

Application state can (and should) be represented as normal values on the normal data stack within a program loop. No separate effects API for state. This design is consistent with a procedural embedding of transaction machines and makes it semantically clear what state is 'owned' by the application vs. owned by the runtime. The cost is that this design places a heavy burden on the optimizer, e.g. precise conflict detection requires abstract analysis to partition state into smaller fragments of a transactional memory. 

### Robust References

Conventional APIs return allocated references to the client, e.g. `open file` returns a file handle. However, this design is unstable under concurrency and difficult to secure. A more robust API design for references is to have applications allocate their own references. For example, `open file as foo` where `foo` is a user-provided value. 

Further operations such as read and close would then use `foo` value to reference the open file. The host would maintain a translation table between application references and runtime objects. The application never has direct access to a runtime's internal reference, and cannot forge runtime references.

There are benefits to this design when compared to system allocation of references. It becomes much easier to secure which references a subprogram may use, e.g. based on prefix. References can be stable and meaningful, which should simplify debugging and reflection. Allocation of references can be decentralized within a concurrent application, reducing contention.

This avoids any need for abstraction or opacity of reference values. This does shift the burden of garbage collection to the application, but that can be supported by including an effect to list all active references of a given type.

### Asynchronous Effects

Top-level effects will operate on state shared with the runtime. This state will include queues for messaging and tables for working with multiple references. The details are abstracted from the program via the effects handler, but this does constrain the effects API. Synchronous effects are limited to local manipulation of shared state. External effects must be asynchronous.

*Note:* An exception can be made for safe, cacheable reads. For example, it is feasible to support HTTP GET as a synchronous effect. However, I believe it wiser to support this indirectly via reflective effects APIs and manual caching.

### Specialized Effects

Runtime effects APIs should be specialized. For example, although file streams and TCP streams are remarkably similar, `tcp:read:(...)` request should be separate and distinct from `file:read:(...)`. Attempting to generalize hinders domain specialized features. It is better for the runtime to provide a more specialized API then let the application implement its own abstraction layer as another effects handler if desired.

### Extensible Interfaces

Application input should use a variant type, i.e. `method:Args`. For example, a command-line application might use `cmd:[List, Of, Strings]`. Result and effect types may depend on the method. This design simplfies extension of applications with new methods or use in new contexts.

## Common Effects

Effects should be designed for transaction machines yet suitable for procedural code to simplify embeddings.

### Time

Transactions are logically instantaneous. The concept of 'timeout' or 'sleep' is incompatible with transactions. However, we can constrain a transaction to commit before or after a given time. We can also estimate time of commit then abort if the estimate is too far off. Proposed effects API:

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail.

Use of 'check' represents stable, monotonic, indirect observation of time. If a transaction aborts after a failed time check, the runtime can implicitly wait for specified time (or other relevant changes) to retry. Time check is very useful for scheduling future activity, such as timeouts.

Reading 'now' will destabilize a transaction. To avoid a busy-wait loop, reading time should be performed only by transactions that are already unstable for other reasons, such as reading a channel. This is useful for adding timestamps to received events or data.

The default TimeStamp is Windows NT time - a natural number of 100ns intervals since midnight Jan 1, 1601 UT. 

### Concurrency

Repetition and replication are equivalent for isolated transactions. In context of a transaction machine, within the stable prefix of a transaction, fork can be implemented efficiently by replication, essentially spawning a thread for each stable choice. Outside this context, fork can be implemented as probabilistic choice.

* **fork** - non-deterministic behavior, respond with '0' or '1' bitstring.

Fork is abstractly a function of time. Relevantly, a hierarchical transaction may observe 'fork' backtracking within logically frozen time, but there is no history maintained between transactions or procedural steps.

### Distribution

Channels should be modeled as lists in application state, annotated and accelerated as needed. Network disruption can be detected using timeouts, much as it would be for network channels. Actual distribution is an optimization of a stable, forking transaction machine. Location-specific effects can be part of the normal effects API. Different locations may have access to different effects.

*Notes:* Stateful expression of location results in a mobile process instead of a stable distributed process. Thus, we should favor APIs of form 'open file at X' instead of 'move to X; open file'. 

### Logging

Almost any application model will benefit from a simple logging mechanism to support debugging, observability of computations, etc..

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc fields whose meanings are de-facto standardized, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This enables the record to be extended with metadata or new features.

In context of transaction machines and task-based concurrency, the concept and presentation of logging should ideally be adjusted to account for stable forks. Instead of a stream of log messages, an optimal view is something closer to live tree of log messages with access to history via timeline.

Although messages logged by failed transactions are not observable within the program, they can be observed indirectly through reflection APIs which operate on runtime state. A debug view presented to a developer should almost always be based on a reflection API. Aborted messages might be distinguished by rendering in a different color, yet should be accessible for debugging purposes.

### Random Data

Due to different optimizations, a request for random data from operating system or hardware should be distinct from 'fork' effects. 

* **random:Count** - response is requested count of secure random bits as a bitstring. E.g. `random:8` might return `00101010`.

Programmers could use pseudo-random number generators if they do not require secure random bits.

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

