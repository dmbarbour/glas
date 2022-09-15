# Glas Applications

A Glas application, at least for [Glas CLI](GlasCLI.md) verbs, is represented by a step function that is repeatedly evaluated over time until it successfully halts, with each evaluation in a transaction. A failed evaluation does not halt the application, but retries with the original input over time, implicitly waiting for external conditions (or any non-deterministic choices) to change. 

        type Process = init:Params | step:State -> [Effects] (halt:Result | step:State) | FAILURE

This application model, which I call *Transaction Machine*, provides a robust foundation for reactivity, concurrency, and process control. 

## Transaction Machines

Transaction machines model software systems as an open set of repeating, atomic, isolated transactions in a shared environment. Scheduling of different transactions is non-deterministic. This is a simple idea, but has many nice systemic properties regarding extensibility, composability, concurrency, distribution, reactivity, and live coding. However, transaction machines depend on advanced optimizations such as replication to evaluate many non-deterministic choices in parallel, and incremental computing to stabilize replicas. Implementation of the optimizer is the biggest development barrier for this model.

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

Transaction machines don't solve live coding, but they do lower a few barriers. Application code can be updated atomically between transactions. Threads can be regenerated according to the new code. Unhandled cases can simply diverge (e.g. via 'tbd' operator) to await programmer intervention and won't hinder concurrent progress.

Remaining challenges include stabilizing application state across minor changes, versioning major state changes, or tracing compiled code back to sources to render live data inline. These issues must be solved in other layers.

## Procedural Programming on Transaction Machines

In context of a transaction machine, a procedure will evaluated over multiple process steps.

For example, with sequential composition the halt:Result of one process becomes init:Param to the next. If the first step yields before completion, the composite process must add information to the 'step:State' to remember that it's the first step that yielded. This effectively records the program counter within State. We can similarly define conditionals and loops in terms of composing processes.

A blocking call at the procedure layer becomes a process that sends a message, yields, then awaits a response at the start of the next transaction.

## Concrete API Design

### Performance-Risk Mitigation

Initially, application programs must use the 'prog' header, i.e. `prog:(do:GlasProgram, app, ...)`. However, optimizations on this representation are not easy.

Eventually, we might extend representation or application programs with specialized variants to simplify essential transaction machine optimizations - i.e. nodes explicitly for checkpointing, stable forks, and fine-grained partitioning of state. This opportunity mitigates risk in case annotations prove awkward or inadequate for the task.

Use of 'fork' for concurrency is not very efficient without the incremental computing and replication optimizations. We should avoid it where feasible. But we can still support fork concurrency to a limited degree by 'scheduling' forks instead of randomizing them:

* after evaluation of a fork succeeds, try that fork again soon.
* ordered cycle through failed forks; guarantee opportunity to run.
* when all forks seem to be failing, wait briefly before retry.

This would be inefficient because we don't have incremental computing, but predictable and effective in case we try running an app that uses fork-based concurrency.

### Robust References

Applications are in charge of allocating local references to objects, i.e. instead of `var foo = open filename` I favor an API style closer to `open filename as "foo"`. This allows for static allocation, hierarchical regions, or decentralization for dynamic allocations. References can carry convenient information for debugging. Importantly, it avoids concerns related to abstraction or forgery for references. 

This design essentially makes references second-class, in the sense that they cannot be directly communicated between scopes. Indirect communication of references is still feasible, e.g. we could include an API that allows establishing a subchannel over an existing channel, or allows connecting two channels.

### Time

Transactions are logically instantaneous. The concept of 'timeout' or 'sleep' is incompatible with transactions. However, we can constrain a transaction to commit before or after a given time. We can also estimate time of commit then abort if the estimate is too far off. Proposed effects API:

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail.

Time 'check' provides stable, monotonic, indirect observation of time. If a transaction aborts after a failed time check, the runtime can implicitly wait for specified time (or other relevant changes) to retry. Time check is useful for modeling timeouts and scheduling.

Reading 'now' will always destabilize a transaction, so it's best read after the transaction is unstable for other reasons, such as processing an incoming message from a channel.

Suggest timestamps are usually in NT time: a natural number of 100ns intervals since midnight Jan 1, 1601 UT.

### Concurrency

Repetition and replication are equivalent for isolated transactions. If a repeating transaction externalizes a choice, it could be replicated to evaluate each choice and find an successful outcome. If this choice is part of the stable prefix for incremental computing, then these replicas also become stable, each repeating from some later observation. This provides a simple basis for task-based concurrency within transaction machines as an optimization of choice.

* **fork:[List, Of, Values]** - Response is a value externally chosen from a non-empty list. Fails if the argument is empty or is not a list.

I propose modeling fork as a deterministic operation on a non-deterministic environment. This is subtly different from fork as a non-deterministic effect for backtracking and optimizations. For example, we can optimize `cond:(try:A, then:B, else:seq:[A, C])` to `seq:[A, B]` only if we assume `A` is a deterministic operation. Either way, we can effectively support concurrency.

### Distribution

Application state is represented in a massive `step:State` tree value. An optimizer can potentially use abstract interpretation to partition the tree into variables that can be distributed or replicated across physical machines. 

If necessary, the effects API could also include some location metadata, e.g. use of `at:(loc:MachineRef, do:LocalEffect)` where a local effect might involve the local filesystem, network, or clock. This might not be necessary if we separate distribution issues from regular 'prog' nodes.

Distributed transactions support the general case, but are very expensive. High performance distribution depends on careful application design, with a goal that most transactions are evaluated on a single machine, and most distributed transactions are two-party blind-writes such as appending a list. It is possible to optimize common two-party blind-write transactions into simple message passing. It also is possible to abstract common two-party blind-write transactions into the effects API (see *Channels*, later).

In case of network partitioning, it is safe for each partition to continue evaluating in isolation, delaying only the distributed transactions that communicate across partitions. This design is resilient to short-lived network disruption. However, programs may need to explicitly detect and handle long-lived disruption. This is possible by using timeouts or pushback buffers.

### Logging

Logging is a convenient approach to debugging. We can easily support a logging effect. Alternatively, we could introduce logging as a program annotation, accessible via reflection. But it's convenient to introduce as an effect because it allows for flexible handling.

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

The proposed convention is that a log message is represented by a record of ad-hoc fields, whose roles and data types are de-facto standardized. For example, `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:hal)`. This simplifies extension with new fields and a gradual shift towards more structured, less textual messages.

In context of transaction machines with incremental computing and fork-based concurrency, the conventional notion of streaming log messages is not a good fit. A better presentation is a tree, with branches based on stable fork choices, and methods to animate the tree over time. Additionally, we'll generally want to render log outputs from failed transactions (perhaps in a faded color), e.g. using some reflection mechanism. 

### Random Data

For optimization and security purposes, it's necessary to distinguish non-deterministic choice from reading random data. Relevantly, 'fork' is not random (cf. *Transaction Fusion* selecting optimizable schedules), and 'random' does not implicitly search on failure (e.g. cryptographic PRNG per fork under hood). A viable API:

* **random:N** - response is cryptographically random binary of N bytes.

Most apps should use PRNGs or noise models instead of external random input. But access to secure random data is necessary for some use cases, such as cryptographic protocols.

## Inter-App Communication APIs

Some ideas for communication between applications or components of a large application. 

### Channels 

A channel communicates with a remote process using reliable, ordered message passing. If channels are fine-grained and can be communicated, we can easily represent object-oriented software design patterns. A viable effects API:

* **c:send:(data:Value, over:ChannelRef)** - send a value over a channel. Return value is unit.
* **c:recv:(from:ChannelRef)** - receive data from a channel. Return value is the data. Fails if no input available or if next input isn't data (try 'accept').
* **c:attach:(over:ChannelRef, chan:ChannelRef, mode:(copy|move|bind))** - connect a channel over a channel. Behavior varies depending on mode:
 * *copy* - a copy of 'chan' is sent (see 'copy')
 * *move* - 'chan' is detached from calling process. (attach copy then drop original)
 * *bind* - a new channel is established, with one endpoint bound to 'chan'. Fails if 'chan' in use.
* **c:accept:(from:ChannelRef, as:NewChannelRef)** - Receives a channel endpoint, binding to the 'as' channel. This will fail if the next input on the channel is not a channel (or not available), such that send/attach order is preserved at recv/accept.
* **c:pipe:(with:ChannelRef, and:ChannelRef, mode:(copy|move|bind))** - connect two channels such that future messages received on one channel are automatically forwarded to the other, and vice versa. This includes pending message and attached channels. Behavior varies depending on mode:
 * *copy* - a copy of the channels is connected; original refs can tap communications.
 * *move* - piped channels are detached from caller (see 'close'), managed by host system.
 * *bind* - new channel is created between two references. Fails if either ChannelRef is already in use.
* **c:copy:(of:ChannelRef, as:ChannelRef)** - duplicate a channel and its future content. Both original and copy will recv/accept the same inputs in the same order (transitively copying subchannels). Messages sent to either channel are routed to the same destination. Send order is preserved for each copy independently, e.g. if a transaction copies A as B then sends over ABABAB, the receive order can be AAABBB or BBABAA depending on implementation details.
* **c:drop:ChannelRef** - detach channel from calling process, enabling host to recycle associated resources. Indirectly observable via 'test'.
* **c:test:ChannelRef** - Succeeds, returning unit, if the channel has any pending inputs or is still remotely connected. Otherwise fails. If the remote endpoint of a channel is copied, all copies must be dropped before 'test' will fail.

Objects are services that repeatedly 'accept' and handle calls. Each call is represented by a distinct subchannel. Typically, the caller will attach/create a subchannel per call, write parameters to that channel before committing, then await a response from the call channel in a future transaction. However, this is easily extended to support streams or interactive sessions. The channel that handles method calls serves as the object reference.

To program these objects would benefit from a dedicated language module that knows how to compile procedural method calls into multiple steps. It would also benefit from named/versioned 'yield' points to stabilize the resulting state machines in context of live coding. It is feasible for transaction fusion to collapse unnecessary waits, but we could also try some annotations to stabilize the optimizations.

There is some risk with channels of a fast producer outpacing a slow consumer of data, resulting in a space leak. This should usually be solved at the protocol layer for streaming data, e.g. require the consumer to provide some feedback such as acknowledgements or readiness tokens. Buffer limits and pushback aren't built into the channels because they interact awkwardly with transaction boundaries.

A weakness of this API is that the network has a lot of implicit state related to routes (implicitly built via attach/accept/pipe) and buffers. Unlike private application state, the network state is not accessible for update in case of changes to code or configuration. Old connections can be inconsistent with new security policies. This can be mitigated by designing APIs that constrain the maximum age of connections, e.g. favoring short-lived connections or periodic regeneration of long-lived connections (like subscriptions). 

### Reactive Dataflow Networks

An intriguing option is to communicate using only ephemeral connections, where logical lifespan approaches zero. Connections and delegated authority are visible, revocable, reactive to changes in code, configuration, or security policy. This is a convenient guarantee for live coding, debugging, extensibility, and open systems.

A viable API:

* **d:read:(from:Port, mode:(list|fork))** - read a set of values currently available on a dataflow port. Behavior depends on mode:
 * *list* - returned set represented as a list with arbitrary but stable order.
 * *fork* - same behavior as reading the list then immediately forking on the list; easier to stabilize compared to performing these operations separately.
* **d:write:(to:Port, data:Value)** - add data to a dataflow port. Writes within a transaction or between concurrent transactions are monotonic, idempotent, and commutative. Concurrent data is read as a set. Data implicitly expires from the set if not continuously written. Unstable data might be missed by a reader.
* **d:wire:(with:Port, and:Port)** - When two ports are wired, data that can be read from each port is temporarily written to the other. Applies transitively to hierarchical ports. Like writes, wires expire unless continuously maintained.

Ports are lists to abstract over hierarchical multiplexing. The ports used by a process should be documented. For example, a simple request-response protocol might involve writing `query:"GLAS_PATH"` to port `[env]` then reading responses from port `[env, val:"GLAS_PATH"]`. In this case, a process might describe the 'env' port as providing access to system environment variables. An efficient implementation requires abstracting over the expiration and regeneration of connections, and optimizing stable routes through wires. 

Many processes will use a standard pair of loopback ports 'lo' and 'li', applied hierarchically (such that stable writes to `[lo, foo]` are eventually read on `[li, foo]` and vice versa). This enables hierarchical process networks to delegate implementation and optimization of reactive dataflow to runtime or compiler.

A weakness of this model is that it can be difficult to predict or control which intermediate values are observed by external processes in context of unstable computations. This can be mitigated by stabilizing communication with application state, e.g. maintaining output until acknowledgement is received or timeout. 

### Synchronous Remote Procedure Calls? Reject.

Supporting synchronous remote procedure calls, i.e. within a transaction, is technically feasible but I'm not convinced it's a good idea. Doing so complicates the application model (to allow for reentrant calls), resists local reasoning and optimizations, and hinders integration with non-transactional systems. At least for now, I would suggest that distributed transaction be explicitly modeled between applications as needed.

## Misc Thoughts

### Console Applications

See [Glas command line interface](GlasCLI.md).

### Notebook Applications

I like the idea of building notebook-style applications, where each statement is also a little application serving its own little GUI. Live coding should be implicit. The notebook pages should be highly reactive to changes in code, avoiding overuse of history-dependent behavior. 

The GUI must be efficiently composable, such that a composite application can combine GUI views from component applications. Ideally, we can also support multiple views and concurrent users, e.g. an application serves multiple GUIs.

Component applications would be composed and connected. I like the idea of using *Reactive Dataflow Networks* for communication because it works nicely with live coding, so we might assume the notebook has some access to a loopback port and possibly to user model and GUI requests via reactive dataflow.

### User Interface APIs

Initial GUI for command line interface applications will likely just be serving HTTP connections. But for notebook applications, we might benefit from a higher level API such that we can do more structured composition before converting the GUI to lower level code. About the only idea I'm solid on is that processes should accept and 'serve' GUI connections, which can easily support running headless or multiple users and views. 

### Web Applications

A promising target for Glas is web applications, compiling applications to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Transaction machines are a decent fit for web apps. And we could also adapt notebook applications to the web target.
