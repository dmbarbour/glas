# Glas Applications

Glas applications, at least for [Glas CLI](GlasCLI.md) verbs, will be modeled by a 1--1 arity program that represents a multi-step process over time, where each step is evaluated atomically within a transaction.

        type Process = (init:Params | step:State) → [Effects] (halt:Result | step:State) | Failure

An application process is started with 'init', voluntarily terminates by returning 'halt', or may return 'step' to continue in a future transaction, carrying state. Intermediate outputs and latent inputs can be modeled effectfully. Failure aborts the transaction but does not halt the application, implicitly waiting for changes or (in context of non-deterministic choice effects) implicitly searching for a choice that does not result in failure.

This application model, which I call a *Transaction Machine*, has nice systemic properties regarding extensibility, composability, concurrency, distribution, reactivity, and live coding. Administrative control, debugging, and orthogonal persistence can be implemented as extensions that work with application state between steps. However, transaction machines depend on sophisticated optimizations. Implementation of the optimizer is the main development barrier for this model.

## Transaction Machines

Transaction machines model software systems as an open set of repeating, atomic, isolated transactions in a shared environment. Scheduling of transactions is non-deterministic. This is a simple idea, but has many nice properties, especially when optimized. 

### Waiting and Reactivity

If nothing changes, repeating a deterministic, unproductive transaction is guaranteed to again be unproductive. The system can recognize a simple subset of unproductive transactions and defer repetition until a relevant change occurs. Essentially, we can optimize a busy-wait into triggering updates on change.

The most obvious unproductive transaction is the failed transaction. Thus, aborting a transaction expresses waiting for changes. For example, if we abort a transaction after it fails to read from an empty channel, we'll implicitly wait on updates to the channel. Successful transactions are unproductive if we know repetition writes the same values to the same variables. Optimizing the success case would support spreadsheet-like evaluation of transaction machines.

Further, incremental computing can be supported. Instead of fully recomputing each transaction, it is feasible to implement repetition as rolling back to the earliest change in observed input and recomputing from there. We can design applications to take advantage of this optimization by first reading relatively stable variables, such as configuration data, then read unstable variables near end of transaction. This results in a tight 'step' loop that also reacts swiftly to changes in configuration data.

### Concurrency and Parallelism

Repeating a single transaction that makes a non-deterministic binary choice is equivalent to repeating two transactions that are identical until this choice then deterministically diverge. We can optimize non-deterministic choice using replication. Usefully, each replica can be stable under incremental computation. Introducing a non-deterministic choice effect enables a single repeating transaction to represent a dynamic set of repeating transactions.

Transactions in the set will interact via shared state. Useful interaction patterns such as channels and mailboxes can be modeled and typefully abstracted within shared state. Transactional updates and ability to wait on flexible conditions also mitigates many challenges of working directly with shared state.

Concurrent transactions can evaluate in parallel insofar as they avoid read-write conflict. When conflict does occur, one transaction will be aborted by the system while the other proceeds. The system can record a conflict history to heuristically schedule transactions to reduce risk of conflict. Additionally, applications can be architected to avoid conflict, using intermediate buffers and staging areas to reduce contention.

### Distribution 

Application state can be partitioned and mirrored across physically separated machines. A random distribution will be inefficient, requiring a distributed transaction for every step. With careful application design and annotation-guided distribution, we can arrange for most transactions to be machine-local, and further optimize most communication between machines.

For example, transaction machines can model a basic channel using a write buffer, a read buffer, and a data plumbing task that repeatedly moves data from (local) write buffer to (potentially remote) read buffer. Data previously in the read buffer is not observed and ideally shouldn't be serialized by this transaction. Ideally, the system would recognize this transaction and optimize it into a simple update message with acknowledgement.

Transaction machines are inherently resilient to network disruption. Operations within each network partition may continue unabated. Operations that communicate between network partitions fail temporarily, then implicitly continue when connectivity is re-established. This behavior is convenient for short-term disruption. Long-term disruption should be handled by weak synchronization patterns within the application, such as pushback buffers and heartbeats.

### Transaction Fusion

It is possible to apply [loop optimizations](https://en.wikipedia.org/wiki/Loop_optimization) to repeating transactions. Conceptually, we might view this as refining the non-deterministic transaction schedule. A random schedule isn't optimal because we must assume external updates to state between steps. By fusing transactions, we eliminate concurrent interference and enable optimization at the boundary. An optimizer can search for fusions that best improve performance. 

Fusion could be implemented by a just-in-time compiler based on empirical observations, or ahead-of-time based on static analysis. Intriguingly, just-in-time fusions can potentially optimize communication between multiple independent applications and services. In context of distributed transaction machines, each application or service essentially becomes an patch on a network overlay without violating security abstractions.

### Real-Time Systems 

Transaction machines can wait on the clock by (logically) aborting a transaction until a desired time is reached. Assuming the system knows the time the transaction is waiting for, the system can schedule the transaction precisely and efficiently, avoiding busy-waits from watching the clock. Usefully, the system can precompute a transaction slightly ahead of time, such that effects apply almost exactly on time.

Further, we could varify that critical transactions evaluate with worst-case timing under controllable stability assumptions. This enables "hard" real-time systems to be represented.

### Live Coding

The application program can be updated at runtime between transactions. This would naturally destabilize incremental computing and concurrent replicas, but those can be swiftly regenerated. Application state would immediately be handled by the updated transaction.

Transaction machines are only a partial solution for live coding. The application must also include transition logic for the state value, as needed, or must be careful to use a stable state type across application versions. Thus, support from the development environment is the other major part of the solution.

## Procedural Programming on Transaction Machines

Procedural code is a good fit for some applications, especially for short-running tasks where the implicit state doesn't become a problem. In context of a transaction machine, a procedure is evaluated over multiple transactional steps. Blocking calls are implicitly modeled by retry on step failure. A procedure's call-by-value parameters and return value can be represented with init and halt. The implicit continuation and local variables will be represented in the procedure's continuation.

Concurrency keywords and atomic blocks are useful extensions for procedural programs. To model concurrent interaction between procedure calls, we use shared variables or channels. Syntactically this can be represented similarly to pass-by-reference in many PLs. Reads and writes of the shared variables is probably best represented effectfully.

*Note:* Procedures can be separately compiled into transactional processes then composed. This is convenient for flexible integration. However, some optimizations will likely be easier to perform at the procedural layer. 

## Concrete Design

### Initial Performance Mitigation

Before optimizations are available, programmers should avoid use of concurrency. Failed transactions could cause evaluation to pause for a few milliseconds before retrying. There is no incremental computing, but programmers could use manual approaches such as maintaining an event queue or dirty bits in state. This is adequate for simple single-threaded apps, but it won't scale nicely to waiting on many input sources.

### Robust References

Conventional APIs return allocated references to the client, e.g. `open file` returns a file handle. However, this design is unstable under concurrency and difficult to secure. A more robust API design for references is to have applications allocate their own references. For example, `open file as foo` where `foo` is a user-provided value. 

Further operations such as read and close would then use `foo` value to reference the open file. The host would maintain a translation table between application references and runtime objects. Compared to system allocation of references, it is much easier to secure which references a subprogram may use, e.g. based on prefix. References can be stable and meaningful, which should simplify debugging and reflection. Allocation of references can be decentralized, reducing contention on a central allocator.

### Asynchronous Effects

Effects must be aborted when evaluation fails. Compared to distributed transactions, it's a lot easier to write messages into a local buffer then send on commit. Ease of implementation will impact API designs, favoring asynchronous effects.

A useful exception is stable, cacheable reads. For example, loading a module from the module system, or reading a file's content, or even HTTP GET could potentially be performed within a transaction. In some cases, we might also read the age or staleness of the cached value.

## Common Effects APIs

Effects should be designed for transaction machines yet suitable for procedural code to simplify embeddings.

### Time

Transactions are logically instantaneous. The concept of 'timeout' or 'sleep' is incompatible with transactions. However, we can constrain a transaction to commit before or after a given time. We can also estimate time of commit then abort if the estimate is too far off. Proposed effects API:

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail.

Time 'check' provides stable, monotonic, indirect observation of time. If a transaction aborts after a failed time check, the runtime can implicitly wait for specified time (or other relevant changes) to retry. Time check is useful for modeling timeouts and scheduling.

Reading 'now' will always destabilize a transaction, so it's best read after the transaction is unstable for other reasons, such as processing an incoming message from a channel.

By default, I suggest timestamps are in NT time: a natural number of 100ns intervals since midnight Jan 1, 1601 UT. 

### Concurrency

Repetition and replication are equivalent for isolated transactions. In context of a transaction machine, within the stable prefix of a transaction, fork can be implemented efficiently by replication, essentially spawning a thread for each stable choice. Outside this context, fork can be implemented as probabilistic choice.

* **fork** - non-deterministic behavior, respond with '0' or '1' bitstring.

Fork is abstractly a function of time. Relevantly, a hierarchical transaction may observe 'fork' backtracking within logically frozen time, but there is no history maintained between transactions or procedural steps.

### Distribution

Application state is represented in the `step:State` tree value. Instead of partitioning and replicating variables across machines, an optimizer can separate state into components then distribute those. Effects can also be localized to machines where appropriate, e.g. `at:(loc:MachineRef, do:LocalEffect)`, allowing for local clocks and filesystems.

Any transaction that interacts with state or effects on multiple machines is naturally a distributed transaction. Communication is based on distributed transactions, but optimizing certain patterns. For example, we can heavily optimize a transaction that moves data between channel buffers on different machines. Read-only access to replicated state can potentially be optimized to use point-to-point updates if we also model 'time' as a machine-local effect (to avoid observing inconsistency between state and time).

In case of network partitioning, each partition continues to evaluate in isolation, but distributed transactions are blocked. When networks partitions reconnect, the distributed transactions can immediately continue running. This results in very good default resilience for short-lived network disruptions, but programs should explicitly handle long-term disruptions, e.g. via timeouts or pushback (waiting when write buffers are full).

### Logging

Logging is a convenient approach to debugging. We can easily support a logging effect. Alternatively, we could introduce logging as a special debugging annotation, similar to breakpoints and profiling. 

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc fields, whose meanings are de-facto standardized, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This enables the record to be extended with rendering hints and other useful features.

In context of transaction machines and fork-based concurrency, logs might be presented as a stable tree (aligned with forks) potentially with unstable leaf nodes. Logically, the entire tree updates, every frame, but log messages in the stable prefix of a fork will also be stable outputs. Further, messages logged by failed transactions might be observable indirectly via reflection, perhaps rendered in a distinct color.

### Random Data

For optimization and security purposes, it's necessary to distinguish non-deterministic choice from reading random data. If a transaction fails after reading random data, the same data may be read when the transaction is repeated. There is no implicit search for a 'successful' random choice. This might be implemented by maintaining a buffer per stable fork path.

* **random:Count** - response is requested count of cryptographically secure, uniformly random bits, represented as a bitstring. E.g. `random:8` might return `0b00101010`.

Many applications would be better off using deterministic pseudo-random data or noise models (such as Perlin noise) instead of random data. But access to secure random data is convenient in some use cases, and necessary for cryptographic communications.

## Misc Thoughts

### Console Applications

See [Glas command line interface](GlasCLI.md).

### Notebook Applications

I like the idea of building notebook-style applications, where each statement is a little application with its own little GUI. These small applications are connected by some means. Live coding is implicit: the code for a component is easily edited through the composite GUI view, and edits have immediate consequences. 

An effective model for GUI: The application listens for GUI connections. Each GUI connection can request a view, receive rendering data or commands, and support potential interaction with a user. This supports multiple users or views of the application, and would also work outside of notebook apps. A disadvantage in this case is that the application behavior may change depending on whether a GUI is connected. But we can easily add debug views that we guarantee are separated.

The other concern is how component applications should interact. Wiring components together as a process network is one viable option. Other options include database, databus, or publish-subscribe. It should be feasible to support multiple composition modes, e.g. a process network composite uses wiring, but might contain a process defined via pubsub app composite. This would be similar to composing different DSLs.

### Web Applications

A promising target for Glas is web applications, compiling applications to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Transaction machines should be a good fit for web apps, in theory. And we could also adapt notebook applications to the web target.

### Process Networks

We can model process networks or flow-based programming with a relatively simple effects API for operations on external channels:

* **c:send:(over:ChannelRef, data:Value)** - write arbitrary data to a named channel. Might fail if write buffer is full at start of transaction (to support pushback).
* **c:recv:(from:ChannelRef)** - read data from a named channel. Might fail if read buffer is empty or if next input is not data (see 'accept'). 

Channel references are local to each process, and are wired externally by a composite process. Channel references could be symbolic values indicative of their role, like labeled pins on a circuit board. Process networks with a graphical syntax can easily support flow-based programming style. 

*Aside:* It is feasible to extend the API to support dynamic process networks. But I cannot recommend it. Much benefit of process networks is the ability to treat software components like hardware (circuit boards) with regards to locality, modularity, replacement, etc..


