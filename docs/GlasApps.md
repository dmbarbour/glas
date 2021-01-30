# Glas Applications

Glas programs support context-specific effects APIs. For example, we can develop one API for web-apps oriented around document object model, local storage, and XMLHttpRequest. Another API for console apps can be oriented around filesystem, sockets, and binary streams. For portability, it is useful to develop applications against an idealized, purpose-specific effects API, then adapt it to a target system via 'env' operator.

However, effects APIs for Glas programs must be transactional. Every conditional and loop combinator is essentially a hierarchical transaction. When a 'try' clause fails, any effects from that clause should be reverted. Essentially, effects must reduce to manipulating variables, though several of those variables might be hidden behind an interface. 

A *transaction machine* architecture is an excellent fit for Glas applications. In contrast to the conventional procedural loop, transaction machines have very nice properties for live coding and extension, process control, incremental computing, and resilience.

Glas applications will generally be modeled as transaction machines with problem-specific or target-specific effects APIs.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled non-deterministically but fairly. 

This model is conceptually simple, easy to naively implement, and many useful properties emerge from the semantics.

### Process Control and Reactive Dataflow

Deterministic, unproductive transactions will also be unproductive when repeated unless there is a relevant change in the environment. The system can optimize by waiting for relevant changes. 

Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. If transactions read and write channels, we could abort when reading an empty channel to wait for data. This supports process control.

Transactions that repeatedly write the same values to variables are also unproductive. When a transaction reads several variables then writes variables in an acyclic manner, it can implicitly wait for changes in the values read. This supports reactive dataflow.

Transaction machines can flexibly and robustly mix dataflow, stream processing, and event processing models.

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail can process channels or fast-changing variables.

*Aside:* With precise dataflow analysis, we can theoretically extract several independent rollback-read-write-commit cycles from a transaction. However, stable prefix is adequate for transaction machine performance.

### Task-Based Concurrency

Task-based concurrency for transaction machines can be supported by a non-deterministic fork operations and incremental computing.

Relevant observations: A non-deterministic transaction is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When the choice is stable, replication reduces recomputation and latency. 

Effectively, stable forks enables a single transaction to model a concurrent transaction machine. Fork is is conveniently dynamic and reactive. For example, if we fork based on configuration data, any change to the configuration will rollback the fork and rebuild a new set.

*Note:* Although we cannot optimize forks after unstable operations, they're still useful for expressing non-deterministic systems.

### Real-Time Systems Programming

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

Transaction machines naturally model state machines. Each transaction observes states and inputs, dispatches appropriate code, then sets the next state. Dispatch can be decentralized, with several transactions each handling only the states they recognize, otherwise aborting.

Unfortunately, transaction machines are often forced to model state machines. Relevantly, request-response IO cannot be expressed in a direct style. The request must be committed before a response is produced. Waiting on the response must be a separate transaction. With state machines, we can model sending the request as one state, awaiting response as another.

Transaction machines would benefit from a higher-level language that can generate good state machines from direct-style code. Ideally, these machines should also be versioned or stable to simplify live program update.

### Parallelism

Transaction machines mitigate the issue of transaction conflict: a scheduler can heuristically arrange transactions history of conflict to run at separate times. Channels can reduce pressure on high-contention variables. With dataflow analysis, a compiler might leverage parallelism available within a transaction.

Transaction machines have high potential for parallelism. However, the parallelism we can achieve depends ultimately on the problem. 

## Common Effects API

Although the effects API can be tuned for different purposes, there are several common patterns that we can usefully leverage. A relatively generic application environment can be specialized by specifying a few environment variables, public variables, and data ports. 

### Variables

Most applications need state, and variables are a convenient way both model state and to partition state for concurrent update. 

Effects API:

* **get:(var:Var)** - response is current value of variable; fail if undefined
* **set:(var:Var, val?Value, new?)** - sets value for variable, response is unit
 * *val* - optional. if excluded, set to undefined state.
 * *new* - flag. if included, fail if currently defined. 

Variables are identified by arbitrary values local to the application, and most are allocated and managed by the application. Variables have identity behavior: get whatever was last set. 

### Channels

Channels are a convenient model for processing of streaming data or events and coordination between abstract concurrent components. Channels are transaction-friendly: Multiple writers and a single reader can evaluate in parallel and commit without conflict. 

Effects API:

* **read:(chan:Chan)** - Removes head value from channel buffer. Response is head value. Fails if channel is empty.
* **unread:(chan:Chan, data:Val)** - Response is unit. Adds data to head of channel buffer.
* **write:(chan:Chan, data:Val)** - Response is unit. Adds data to tail of channel buffer.

Channels are identified by arbitrary values local to an application, and most are allocated and managed by the application. Channels have identity behavior: read previous writes in FIFO order.

Channels will often be coupled as a `(send, recv)` pair, supporting bi-directional communications. The 'send' channel is write-only, 'recv' is read-only (enforced by types or user discipline), and there should be some cooperative protocol for communication. Even when communication is mostly unidirectional, the other channel can support flow control via sending or receiving 'ready' tokens.

### IO Channels and Variables

A subset of resource identifiers - `io:(...)` and dicts with the `io` key - are reserved for interaction between application and environment. Relevant resources include channels and variables, but should include any abstract resource models developed later. 

IO resources are abstractly allocated and managed by the environment. That is, programs should never use constants such as `io:1` or `io:stdout`. Instead, IO resources are returned in response to effects.

IO resources often have usage restrictions, e.g. a standard output channel is write-only and accepts only binary data. Ideally, these restrictions are enforced typefully. However, the environment could dynamically enforce some properties - for example, attempting to read from standard output might fail.

### Fork

Effect API:

* **fork:Value** - response is unit or failure. This response is consistent within the transaction but non-deterministic fair choice from one transaction to another. That is, the fork decision is implicitly cached per value.

Fork values help stabilize incremental computing and debugging. They may also be descriptive of the decision made to help document application behavior.

### Time

Effect API:

* **await:TimeStamp** - response is unit or failure. Response is unit if estimated time-of-commit is greater than awaited timestamp, otherwise succeeds.

If the estimate is just a little high, we can delay actual commit based on highest successful await timestamp. Other cases are more complicated, and the best solution is often to make a new estimate, rollback, and recompute. A good runtime should aim for estimates to be just a little high. 

*Note:* I propose use of Windows NT time for the TimeStamp. That is, number of 100ns intervals since midnight Jan 1, 1601 UT. This is significantly more precision than we're ever likely to use.

### Asynchronous Tasks

For effects that do not complete immediately within a transaction, the environment can allocate a task to run in the background then immediately return a `(send, recv)` channel pair for interaction with this task. 

Valid interactions aren't limited to request-response and might be described by a session type. As a convention, it should normally be possible to terminate interaction with a 'fin' token, releasing the IO resource.

## Console App Effects API

Console applications are my initial target. I don't need full potential of Unix apps, just enough to cover command line tooling and web servers.

Requirements:

* access to stdio resources, isatty
* environment variables
* file access - whole-file read and write, streaming read and write
* network access - TCP and UDP sockets is sufficient
* termination/exit


## Web App Effects API


## Other Effects APIs

It might be useful to develop application APIs specific to android, web-server components, etc.. 

