# Glas Applications

Glas applications, at least for [Glas CLI](GlasCLI.md) verbs, will be modeled by a 1--1 arity program that represents a multi-step process over time, where each step is evaluated atomically within a transaction.

        type Process = (init:Params | step:State) → [Effects] (halt:Result | step:State) | Failure

An application process is started with 'init', voluntarily terminates by returning 'halt', or may return 'step' to continue in a future transaction, carrying state. Intermediate outputs or latent inputs will be modeled with effects, such as reading and writing channels. Failure aborts the current transaction then logically retries, implicitly waiting for external changes or searching among non-deterministic choices. 

This application model, which I call a *Transaction Machine*, has nice systemic properties regarding extensibility, composability, concurrency, distribution, reactivity, and live coding. However, transaction machines depend on advanced optimizations such as replication to evaluate many non-deterministic choices in parallel, and incremental computing to stabilize replicas. Implementation of the optimizer is the biggest development barrier for this model.

## Transaction Machines

Transaction machines model software systems as an open set of repeating, atomic, isolated transactions in a shared environment. Scheduling of transactions is non-deterministic. This is a simple idea, but has many nice properties, especially when optimized. 

### Waiting and Reactivity

If nothing changes, repeating a deterministic, unproductive transaction is guaranteed to again be unproductive. The system can recognize a simple subset of unproductive transactions and defer repetition until a relevant change occurs. Essentially, we can optimize a busy-wait into triggering updates on change.

The most obvious unproductive transaction is the failed transaction. Thus, aborting a transaction expresses waiting for changes. For example, if we abort a transaction after it fails to read from an empty channel, we'll implicitly wait on updates to the channel. Successful transactions are unproductive if we know repetition writes the same values to the same variables. Optimizing the success case would support spreadsheet-like evaluation of transaction machines.

Further, incremental computing can be supported. Instead of fully recomputing each transaction, it is feasible to implement repetition as rolling back to the earliest change in observed input and recomputing from there. We can design applications to take advantage of this optimization by first reading relatively stable variables, such as configuration data, then read unstable variables near end of transaction. This results in a tight 'step' loop that also reacts swiftly to changes in configuration data.

### Concurrency and Parallelism

Repeating a single transaction that makes a non-deterministic binary choice is equivalent to repeating two transactions that are identical before this choice then deterministically diverge. We can optimize non-deterministic choice using replication. Usefully, replicas can be stable under incremental computation. Introducing non-deterministic choice enables a single repeating transaction to represent a full dynamic set of repeating transactions.

Transactions in the set will interact via shared state. Useful interaction patterns such as channels and mailboxes can be modeled and typefully abstracted within shared state. Transactional updates and ability to wait on flexible conditions also mitigates many challenges of working directly with shared state.

Concurrent transactions can evaluate in parallel insofar as they avoid read-write conflict. When conflict does occur, one transaction will be aborted by the system while the other proceeds. The system can record a conflict history to heuristically schedule transactions to reduce risk of conflict. Fairness is feasible if we ensure individual transactions do not require overly long to evaluate. Additionally, applications can be architected to avoid conflict, using intermediate buffers and staging areas to reduce contention.

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

Repetition and replication are equivalent for isolated transactions. If a repeating transaction externalizes a choice, it could be replicated to evaluate each choice and find an successful outcome. If this choice is part of the stable prefix for incremental computing, then these replicas also become stable, each repeating from some later observation. This provides a simple basis for task-based concurrency within transaction machines as an optimization of choice.

* **fork:[List, Of, Values]** - Response is a value externally chosen from a non-empty list. Fails if the argument is empty or is not a list.

I propose modeling fork as a deterministic operation on a non-deterministic environment. This is subtly different from fork as a non-deterministic effect for backtracking and optimizations. For example, we can optimize `cond:(try:A, then:B, else:seq:[A, C])` to `seq:[A, B]` only if we assume `A` is a deterministic operation. Either way, we can effectively support concurrency.

### Distribution

Application state is represented in a massive `step:State` tree value. An optimizer can potentially use abstract interpretation to partition the tree into variables that can be distributed or replicated across physical machines. Where needed, the effects API could also include some location metadata, e.g. use of `at:(loc:MachineRef, do:LocalEffect)` where a local effect might involve the local filesystem, network, or clock.

Distributed transactions support the general case, but are very expensive. High performance distribution depends on careful application design, with a goal that most transactions are evaluated on a single machine, and most distributed transactions are two-party blind-writes such as appending a list. It is possible to optimize common two-party blind-write transactions into simple message passing. It also is possible to abstract common two-party blind-write transactions into the effects API (see *Channels*, later).

In case of network partitioning, it is safe for each partition to continue evaluating in isolation, delaying only the distributed transactions that communicate across partitions. This design is resilient to short-lived network disruption. However, programs may need to explicitly detect and handle long-lived disruption. This is possible by using timeouts or pushback buffers.

### Logging

Logging is a convenient approach to debugging. We can easily support a logging effect. Alternatively, we could introduce logging as a program annotation, accessible via reflection. But it's convenient to introduce as an effect because it allows for flexible handling.

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

The proposed convention is that a log message is represented by a record of ad-hoc fields, whose roles and data types are de-facto standardized. For example, `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This simplifies extension with new fields and a gradual shift towards more structured, less textual messages.

In context of transaction machines with incremental computing and fork-based concurrency, the conventional notion of streaming log messages is not a good fit. A better presentation is a tree, with branches based on stable fork choices, and methods to animate the tree over time. Additionally, we'll generally want to render log outputs from failed transactions (perhaps in a faded color), e.g. using some reflection mechanism. 

### Random Data

For optimization and security purposes, it's necessary to distinguish non-deterministic choice from reading random data. Relevantly, 'fork' is not random (cf. *Transaction Fusion* selecting optimizable schedules), and 'random' does not implicitly search on failure (e.g. cryptographic PRNG per fork under hood). A viable API:

* **random:Count** - response is requested count of cryptographically secure, uniformly random bits, represented as a bitstring. E.g. `random:8` might return `0b00101010`. 

Most apps should use PRNGs or noise models instead of external random input. But access to secure random data is necessary for some use cases, such as cryptographic protocols.

## Misc Thoughts

### Console Applications

See [Glas command line interface](GlasCLI.md).

### Notebook Applications

I like the idea of building notebook-style applications, where each statement is also a little application serving its own little GUI. Live coding should be implicit. 

The GUI must be efficiently composable, such that a composite application can combine GUI views from component applications. Ideally, we can also support multiple views and concurrent users, e.g. an application serves multiple GUIs.

Component applications would be composed and connected. I like the idea of using *Reactive Dataflow Networks* for communication because it works nicely with live coding, so we might assume the notebook has some access to a loopback port and possibly to user model and GUI requests via reactive dataflow.

### User Interface APIs

Initial GUI for command line interface applications will likely just be serving HTTP connections. But for notebook applications, we might benefit from a higher level API such that we can do more structured composition before converting the GUI to lower level code. About the only idea I'm solid on is that processes should accept and 'serve' a GUI connections, without needing an extra step to listen for connections on a specific 'port', e.g. **ui:accept:(as:UIRef)** returning a requested view. 

### Web Applications

A promising target for Glas is web applications, compiling applications to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Transaction machines are a decent fit for web apps. And we could also adapt notebook applications to the web target.

### Channels and Object Oriented Programming

A channel communicates with a remote object using reliable, ordered message passing. Channels are themselves communicated to support dynamic networks and object-oriented software design patterns. A viable effects API:

* **c:send:(data:Value, over:ChannelRef)** - send a value over a channel. Return value is unit.
* **c:recv:(from:ChannelRef)** - receive data from a channel. Return value is the data. Fails if no input available or if next input isn't data (try 'accept').
* **c:attach:(over:ChannelRef, chan:ChannelRef, mode:(copy|move|create))** - send a channel endpoint over another channel.Behavior varies depending on mode:
 * *copy* - a copy of chan is sent (see 'copy')
 * *move* - chan is detached from calling process (see 'drop')
 * *create* - new pipe created, move one end, bind other to chan which should be an unused ref.
* **c:accept:(from:ChannelRef, as:ChannelRef)** - receive a channel endpoint, locally binding to the 'as' ChannelRef. Fails if no input available or if next input is data or if 'as' ref is already bound.
* **c:pipe:(with:ChannelRef, and:ChannelRef, mode:(copy|move|create))** - connect two channels such that messages received on one channel are automatically forwarded to the other, and vice versa. Behavior varies depending on mode:
 * *copy* - a copy of the channels is connected; original refs can tap communications.
 * *move* - piped channels are detached from caller (see 'close'), managed by host system.
 * *create* - new pipe is created created, binding two refs. Forms a loopback connection.
* **c:copy:(of:ChannelRef, as:ChannelRef)** - duplicate a channel and its future content. Both original and copy will recv/accept the same inputs in the same order (transitively copying subchannels). Messages sent or subchannels attached to the original copy are both routed to the same destination.
* **c:drop:ChannelRef** - detach channel from calling process, enabling host to recycle associated resources. Eventually observable via 'test'.
* **c:test:ChannelRef** - Succeeds, returning unit, if a channel has pending inputs or is still remotely connected. Otherwise fails. 

Method calls are reified as fresh connections in order to abstract over routing of responses. A remote caller must 'attach' a fresh connection per method call, send request information over this connection, then await a response. An object's host process 'accepts' then serves method calls, optionally processing the active requests concurrently. Objects may also be attached to the request or response. A syntax or intermediate language can compile asynchronous request-response to evaluate over multiple transactional steps. Beyond conventional request-response pattern, we can also model interactive sessions and streams.

A relevant concern is that the connection graph grows very complicated and stateful while not being very visible or revocable. This has negative implications for debuggability, security, and live coding. Old connections, and the authorities they represent, can easily survive changes in configuration or security policy. System behavior will diverge from current system code unless the system is restarted. This can be mitigated by API design that favors short-lived sessions or periodically drops and regenerates long-lived connections. 

### Reactive Dataflow Networks

An intruiging option is to communicate using only ephemeral connections, where logical lifespan approaches zero. A short lifespan ensures that network connectivity and associated authority is visible, revocable, reactive to changes in code, system configuration, or security policies. This is a convenient guarantee for live coding, debugging, extensibility, and open systems. However, continuous expiration and replacement of ephemeral connections is expensive.

The cost can be mitigated by abstracting over expiration and replacement, such that we can incrementally compute the dynamic network structure. In context of transaction machines, this optimization is feasible if the API supports blind forwarding of data. Stable but reconfigurable connections can be represented in the transaction's stable prefix, and connections implicitly break when the transaction fails.

It is convenient for consistency if communication of data is similar to connections, e.g. represented in a transaction's stable prefix. This results in a reactive dataflow networks across process and application boundaries. Streaming data and message passing can be modeled above dataflow when needed.

A viable API:

* **d:read:(from:Port, mode:(list|fork))** - read a set of values currently available on a dataflow channel. Behavior depends on mode:
 * *list* - returned set represented as a sorted list with arbitrary but stable order.
 * *fork* - same behavior as reading the list then immediately forking on the list; easier to stabilize than seperately reading the list then forking on it.
* **d:write:(to:Port, data:Value)** - add data to a dataflow channel. Writes within a transaction or between concurrent transactions are monotonic, idempotent, and commutative. Concurrent data is read as a set. Data implicitly expires from the set if not continuously written. Unstable data might be missed by a reader.
* **d:wire:(with:Port, and:Port)** - When two ports are wired, data that can be read from each port is temporarily written to the other. Applies transitively to hierarchical ports. Like writes, wires expire unless continuously maintained.

Ports represent points on the external surface of a process. For most use cases, I propose to model Ports as a non-empty list of values. The first element is the primary port, used for external wiring of processes, while remaining elements abstract over recursive mux/demux of the primary port. A simple request-response protocol might involve writing `query:"Foo"` to `[env]` then reading the response on port `[env, val:"Foo"]`.

To simplify hierarchical composition, processes may assume 'lo' and 'li' primary ports are externally wired as a loopback, such that stable data written to port `[lo, foo]` is eventually read on port `[li, foo]` and vice versa. Internal dataflows can be mapped through external loopback, which enables the runtime consistently implement expiration of data after a transaction fails.
