# Glas Applications

Glas programs support context-specific effects APIs. For example, we can develop one API for web-apps oriented around document object model, local storage, and XMLHttpRequest. Another API for console apps can be oriented around filesystem, sockets, and binary streams. For portability, it is useful to develop applications against an idealized, purpose-specific effects API, then adapt it to a target system via 'env' operator.

However, effects APIs for Glas programs must be transactional. Every conditional and loop combinator is essentially a hierarchical transaction. When a 'try' clause fails, any effects from that clause should be reverted. Essentially, effects must reduce to manipulating variables, though several of those variables might be hidden behind an interface. 

A *transaction machine* architecture is an excellent fit for Glas applications. In contrast to the conventional procedural loop, transaction machines have very nice properties for live coding and extension, process control, incremental computing, and resilience.

Glas applications will generally be modeled as transaction machines with problem-specific or target-specific effects APIs.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically.

This model is conceptually simple, easy to implement naively, and has very nice emergent properties for a wide variety of systems-level concerns.

### Process Control

Deterministic, unproductive transactions will also be unproductive when repeated unless there is a relevant change in the environment. The system can optimize by waiting for relevant changes. 

Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. For example, we could abort to wait for data on a channel, or abort to wait on time to reach a threshold condition. This supports process control.

### Reactive Dataflow

A successful transaction that reads and writes variables is unproductive if the written values are equal to the original content. Thus, a system can compare written and prior values to decide whether to wait for external changes. In some cases, e.g. when there is no cyclic data dependency, we can also predict that repetition is unproductive without comparing values.

Effectively, transaction machines implicitly support a reactive dataflow system.

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail can process channels or fast-changing variables.

*Aside:* With precise dataflow analysis, we can theoretically extract several independent rollback-read-write-commit cycles from a transaction. However, stable prefix is adequate for transaction machine performance.

### Task-Based Concurrency

Task-based concurrency for transaction machines can be supported by fair non-deterministic fork operation combined with incremental computing. 

Relevant observations: A non-deterministic transaction is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When the choice is stable, replication reduces recomputation and latency. 

Stable forks enable a single transaction to model a concurrent transaction machine. Fork is is conveniently dynamic and reactive. For example, if we fork based on configuration data, any change to the configuration will rollback the fork and rebuild a new set.

*Notes:* Unstable forks can still be used for non-deterministic operations. There may also be a quota on replication, above which forks are treated as unstable.

### Real-Time Systems 

The logical time of a transaction is the instant of commit. It is awkward for transactions to directly observe time before we commit. However, it is not difficult to constrain time of commit. If the transaction aborts because it's too early, the system knows how long to wait.

When constrained by high-precision timestamps, transactions can control their own scheduling to a fine degree. Usefully, the system can also precompute transactions slightly ahead of time so they can 'commit' almost exactly on time. This enables precise control of timed IO effects, such as streaming sound.

It is acceptable that only a subset of transactions are timed. The other transactions would be scheduled opportunistically. In case of transaction conflict, time-constrained transactions can be given heuristic priority over opportunistic transactions.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing features: 

* code can be updated atomically between transactions
* application state is accessible for transition code
* preview execution for a few cycles without commit
* warmup cache and JIT during preview to reduce jitter

A complete solution for live coding requires additional support from the development environment. Transaction machines can only lower a few barriers.

### State Machines

Transaction machines naturally model state machines. Each transaction observes states and inputs, dispatches appropriate code, and sets a next state. Dispatch can be decentralized, with several transactions each handling only the states they recognize, otherwise aborting.

Unfortunately, transaction machines are often forced to model state machines. Relevantly, request-response IO cannot be expressed in a direct style. The request must be committed before a response is produced. Waiting on the response must be a separate transaction. With state machines, we can model sending the request as one state, awaiting response as another.

Transaction machines would benefit from a higher-level language that can generate good state machines from direct-style code. Ideally, these machines should also be versioned or stable to simplify live program update.

### Hierarchical Transactions

Transaction machines are easily extended with hierarchical transactions. This is very convenient for error handling, testing, and invariants. We can review whether a subprogram tries anything problematic before committing. 

### Parallelism

Transactions can evaluate in parallel insofar as there is no conflict with serializability. Conflict can be difficult to predict, but it is possible to evaluate optimistically, detect conflicts upon commit, and abort all but one conflicting transaction. With transaction machines, we can remember conflict history and heuristically schedule to avoid conflict.

The problem of designing programs to minimize conflict is left to programmers. There are useful patterns for this such as moving high-contention variables behind a channel. Of course, we're ultimately limited by parallelism available within the problem.

*Aside:* It is also feasible to support parallelism within a transaction. However, doing so is outside the domain of transaction machines.

## Common Effects

Glas models transactions as normal Glas programs. The try/then/else conditional behavior is an implicit hierarchical transaction. Failure is implicit abort. Interaction with the environment is supported by the eff operator, and is mostly based on variables.

### Concurrency

* **fork:Value** - response is unit or failure. This response is consistent for a given fork value within a transaction, but non-deterministic fair choice across transactions.

The system can optimize stable forks into concurrent replication of the transaction to support task-based concurrency. Unstable fork represents a non-deterministic random choice. The set of observations on fork values within a transaction can serve as an implicit transaction identifier.

### Timing

* **await:TimeStamp** - Response is unit if time-of-commit is equal or greater than the awaited timestamp. Otherwise fails.

The system estimates time-of-commit. It's best to estimate just a little high: if necessary, the system can delay commit to make the estimate true. Ideally, the system will use await times to guide real-time scheduling.

*Note:* For the default timestamp, I propose use of Windows NT time - a number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. This is more precision than we need. 

### State

* **var:(on:Var, op:Method)** - Response value or failure depends on method and state of the specified variable. 

An application has a memory consisting of set of mutable variables, which are identified by arbitrary values. Each variable contains a value or is undefined. By default, variables are undefined. 

In addition to a few general-purpose methods, there are several specialized methods to avoid conflicts for simple models of buffers and counters. For example, multiple black-box writers may blindly increment a number or append a list without conflict.

#### Methods

General purpose methods for variables:

* **get** - Response is value contained in defined variable. Fails if undefined.
* **set:Value** - Update defined variable to contain Value. Response is unit. Fails if undefined.
* **new:Value** - Update undefined variable to contain Value. Response is unit. Fails if defined.
* **del** - Delete the variable's definition. Response is unit, or fails if already undefined. 

Specialized methods for buffers:

* **read:Count** - Removes first Count items from head of list variable. Response is the sublist it removes. Fails for non-list variables or if the variable contains fewer than Count items.
* **unread:\[List, Of, Vals\]** - Prepends list to head of list variable. Response is unit, or fails for non-list variables.
* **write:\[List, Of, Vals\]** - Addends list to tail of list variable. Response is unit, or fails for non-list variables.

Specialized methods for counters:

* **inc:Count** - Increase number variable by given value. Response is unit, or fails for non-number variables.
* **dec:Count** - Decrease number variable by given value. Response is unit, or fails for non-number variables or if would reduce value below zero.

### Channels

Channels are a useful pattern for eventful communication. Channels are primarily modeled by a list variable with read and write methods. However, to support closing of channels and bounded-buffer flow control, I propose a `(data, ready)` pair of variable identifiers.

* *data* - a buffer variable. Written by writer, read or unread by reader. Deleted by reader when finished consuming. 
* *ready* - a counter variable. Decremented by writer, incremented by reader per element read.Deleted by writer when finished producing. Initial value in 'ready' is the bounded-buffer capacity of a channel. 

For bi-directional dataflow, channels will often be coupled as a `(send, recv)` pair, requiring a total of four variables. For convenience and to resist accidents, we might also label individual channels with their intended directionality, i.e. either `send:(data, ready)` or `recv:(data, ready)`.

Correct use of channels can be enforced by a type system or structured syntax.

### IO Variables

Applications can reserve a subset of variables for interaction with the environment. Doing so improves consistency by enabling external and internal interactions to use the same code. I propose to reserve variables identified by dicts containing `io` such as `io:42`. 

Allocation of IO variables should be fully left to the environment. These variables may be returned from other effects calls, such as `query:stdio`. The environment should normally avoid allocating and manipulating non-IO variables, though there are rare exceptions (such as debug views).

The environment can dynamically enforce invariants for how IO variables are used. For example, for a binary output channel, the environment can abort a transaction that attempts to read the channel or write non-binary data. To avoid unusual failure semantics, this abort could be deferred until commit.

### Environment

* **query:Query** - Response value or failure depends on environment.

This represents an ad-hoc query to the environment. Use of query should be idempotent, commutative, and mostly stable. Query provides a useful layer of indirection to return IO variables for further interaction.

### Asynchronous Tasks

For any long-running effects, a good option is to create a background task then immediately return an interface for the asynchronous interaction. This interface can be modeled as a `(send, recv)` channel pair.

### Termination

Termination, if needed, should be explicit to ensure it is intentional. If a transaction machine halts implicitly, e.g. due to deadlock, that's probably a bug. Explicit termination could be represented by defining an IO exit variable before committing.

## Console App Effects API

Console applications are my initial target. I don't need all of Unix, just enough to support command line tooling and web servers.

Requirements:

* access to stdio resources, isatty
* environment variables
* filesystem access - browsing folders and files
* network access - TCP and UDP is sufficient
* termination/exit



## Web App Effects API

A web-app should compile for running in a browser. 

Requirements:

* DOM or virtual DOM + React style UI
* precompute a static document
* XMLHttpRequest, Websockets
* access to local storage

A web-app should compile to an HTML document using JavaScript or WASM.

