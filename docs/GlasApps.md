# Glas Applications

Glas programs are essentially transactional due to backtracking conditional behavior. I propose to embrace this, using a *transaction machine* application architecture, where the application loop is a repeating transaction. Transaction machines have many nice properties for process control, live coding, reactive behavior, and real-time systems. 

Transactions restrict direct use of synchronous effects APIs, i.e. an application cannot send a request then await a response within a single transaction. However, transactions can work with channels, mailboxes, tuple spaces, and various other models. APIs for filesystem, network, GUI, or specialized for web-apps can be designed around use of transactions. 

This document discusses transaction machines and development of applications with Glas.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has many nice emergent properties for a wide variety of systems-level concerns. However, a high-performance implementation is non-trivial, requiring incremental computing, replication on fork, and routing optimizations. This requires support from compiler or runtime system.

### Process Control

Deterministic, unproductive transactions will also be unproductive when repeated unless there is a relevant change in the environment. The system can optimize by waiting for relevant changes. 

Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. For example, we could abort to wait for data on a channel, or abort to wait on time to reach a threshold condition. This supports process control.

### Reactive Dataflow

A successful transaction that reads and writes variables is unproductive if the written values are equal to the original content. If there is no cyclic data dependency, then repeating the transaction will always produce the same output. If there is a cyclic data dependency, it is possible to explicitly detect change to check for a stable fixpoint.

A system could augment reactive dataflow by scheduling transactions in a sequence based on the observed topological dependency graph. This would minimize 'glitches' where two variables are inconsistent due to the timing of an observation. A compiler could take this deeper with potential fusion of transactions. 

*Aside:* Transaction machines can also use conventional techniques for acting on change, such as writing update events or dirty bits.  

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail implicitly loops to process channels or fast-changing variables.

*Aside:* With precise dataflow analysis, we can theoretically extract several independent rollback-read-write-commit cycles from a transaction. However, stable prefix is adequate for transaction machine performance.

### Task-Based Concurrency and Parallelism

Task-based concurrency for transaction machines can be supported by a fair non-deterministic fork operation, combined with incremental computing and a replication optimization. 

Relevant observations: A non-deterministic transaction is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When the choice is stable, replication reduces recomputation and latency. 

Stable forks enable a single transaction to model a concurrent transaction machine. Fork is is conveniently dynamic and reactive. For example, if we fork based on configuration data, any change to the configuration will rollback the fork and rebuild a new set.

Transactions evaluate in parallel only insofar as conflict is avoided. When conflict occurs between two transactions, one must be aborted by the scheduler. Progress is still guaranteed, and a scheduler can also guarantee fairness for transactions that respect a compute quota. A scheduler heuristically avoids conflict based on known conflict history. Programmers avoid conflict based on design patterns and fine-grained state manipulations.

*Note:* Unstable forks can still be used for non-deterministic operations. There may also be a quota limit on replication, above which forks are effectively unstable.

### Real-Time Systems 

The logical time of a transaction is the instant of commit. It is awkward for transactions to directly observe time before we commit. However, it is not difficult to constrain time of commit. If the transaction aborts because it's too early, the system knows how long to wait.

When constrained by high-precision timestamps, transactions can control their own scheduling to a fine degree. Usefully, the system can also precompute transactions slightly ahead of time so they can 'commit' almost exactly on time. This enables precise control of timed IO effects, such as streaming sound.

It is acceptable that only a subset of transactions are timed. The other transactions would be scheduled opportunistically. In case of transaction conflict, time-constrained transactions can be given heuristic priority over opportunistic transactions.

### Declarative Routing

Transactions can move or copy data without directly observing the data moved. While stable, further repetition will *continuously* move or copy data. The system can optimize by verifying stability then sending committed data directly to its destination.

This optimization is most useful where it bypasses interface boundaries between applications or components. An application can serve as a switchboard or introducer for other applications, yet preserves ability to modify routes based on a dynamic configuration. There is no compromise to latency, modularity, or reactivity.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing features: 

* code can be updated atomically between transactions
* application state is accessible for transition code
* preview execution for a few cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Common Effects

### Memory

I propose to model application memory as a Glas value, implicitly a field within a record. To support conflict analysis and avoidance, memory is accessed indirectly via 'eff' operator, and there are a few operators for abstract manipulation. Effects API:

* **mem:(on:Path, do:MemOp)** - Apply MemOp to memory element designated by Path. Path is a bitstring - conventionally encoding null-terminated UTF-8 text - and may be empty. MemOps:
 * **get** - Response is value on Path. Fail if Path does not exist.
 * **put:Value** - Set value on Path. Add Path if it does not exist.
 * **del** - Remove Path from memory, modulo shared prefix with other paths. 
 * **exist** -  Fails if path does not exist in memory. 
 * **read:N** - Split list in memory at Path. Response is first N elements. Remainder is kept in memory. 
 * **unread:List** - Prepend list argument to list value in memory at Path.
 * **write:List** - Append list argument to list value in memory at Path. 
 * **copy:(src:Src, dst:Dst)** - In context of Path, copy current state of Src to Dst. This includes deleting Dst if Src does not exist.
 * **wcopy:(src:Src, dst:Dst)** - In context of Path, append to list value at Dst a copy of the list value at Src. 

The get/put/del memops support basic record manipulations. Use of 'exist' is a more stable alternative to 'get'. The read/unread/write memops enable use of lists in memory as lightweight channels. The copy/wcopy memops support declarative routing.

This API is designed to simplify hierarchical composition of applications: to confine a component application to a subtree, it is sufficient to use the Glas program 'env' operator to add a prefix to the 'on' path field. 

Application memory is private to the application. Other effects will use different effects APIs, enabling the system to hide the implementation.

*Aside:* High-precision conflict analysis is essentially the same problem as incremental computing. It's a deep subject. But the memory API should be adequate for most use-cases.

### Concurrency

* **fork:Value** - response is unit or failure. This response is consistent for a given fork value within a transaction, but non-deterministic fair choice across transactions.

The system should optimize stable forks into concurrent replication of the transaction to support task-based concurrency. Unstable fork represents a non-deterministic random choice. The set of fork values observed serves as an implicit, stable identifier of the fork.

### Timing

* **await:TimeStamp** - Response is unit if time-of-commit is equal or greater than TimeStamp. Otherwise fail.

The system does not know time-of-commit, but can estimate. It's best to estimate just a little high: if necessary, the system can delay commit to make the estimate true. The system will use await times to guide real-time scheduling.

Most Glas applications will support `nt:N` timestamps, referring to Windows NT time. Here, N is a natural number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. The `nt:` prefix is intended to support later extensions.

### Random Numbers

### Effects API Discovery

### Resource Discovery

### External Channels

### Data Buses

### Publish-Subscribe

### Dataflow Interfaces

### Termination


# ... OLD STUFF ...

### Asynchronous Interaction

In many cases, it is impossible to accept a request and obtain a result within the same transaction. The request is processed by the system only after the transaction commits. We'll often desire to observe and influence the ongoing operation.

Some options:

* Environment allocates reference to an abstract task object.
* Application allocates generic resources for task interface.
* Anonymous allocation model for generic resources.

Abstract task objects is a general solution and does the best job of enforcing invariants and supporting purpose-specific methods. Unfortunately, it is an awkward fit for Glas: I cannot easily introduce new abstract references via env/eff, nor robustly extend interactions.


An application will allocate generic resources internally for its own use. Additionally, we could support 'effects' that bind to application-provided resources. In this case, part of the effect state would be invisible; the application will only 



By providing generic resources for a task interface, the application can avoid explicit allocation

Allocation of generic application resources, we assume the application has access to a pool of resources such as variables or channels. When creating a task, the application provides resources to serve as the application-environment interface. This has a benefit of unifying internal and external interactions. The environment can hide some implementation by associated state.

Reserving a volume of resources, e.g. application resources with identifiers of form `io:(...)`, gives the environment a little more control over interactions and could simplify enforcement of invariants. However, the extra indirection from IO resources to others can hinder performance and requires explicit garbage collection of resources. Additionally, it's no use if we're doing graph-based environments.

Of these options, I somewhat favor allocation of generic resources by the application. 


## Common Effects APIs

Glas models transactions as normal Glas programs. The try/then/else conditional behavior is an implicit hierarchical transaction. Failure is implicit abort. Interaction with the environment is supported by the eff operator, and is mostly based on variables.

### Futures and Promises

A future can be modeled as a single-use recv channel, and a promise is the corressponding single-use send channel. Benefits of modeling futures as a special case of channels include consistency and the ability to 'close' channels to indicate irrelevant futures or broken promises. 

Futures are convenient for asynchronous IO. An effect can immediately allocate and return a future. The corresponding promise is implicitly handled by the external system. The main weakness is that futures need to be linear unless programmers explicitly introduce reference counts.

### Environment

* **query:Query** - Response value or failure depends on environment.

For env/eff overrides, it is convenient to centralize ad-hoc queries under a single heading instead of having a dozen top-level effects. As a rule, queries should be idempotent, commutative, and mostly stable.

### Termination

* **fin:ExitVal**

Termination, if needed, should be explicit to ensure it is intentional. If a transaction machine halts implicitly, e.g. due to deadlock, that's probably a bug. Explicit termination could be represented by defining an IO exit variable before committing.

## Filesystem API

A filesystem can be treated as a special state resource, similar to variables, allowing synchronous access. Alternativel Streaming reads and writes for files is also feasible.


## Network API



### TCP

### UDP

## Console App Effects API

Console applications are my initial target. I don't need all features, just enough to support command line tooling and web servers.

Requirements:

* access to stdio resources
 * stderr might be reserved for debug logs
* args and environment variables
* filesystem access - browsing, reading, writing
* network access - TCP and UDP is sufficient
* termination/exit

Network and filesystem access require some careful attention to the API. I assume we'll make heavy use of channels.

Desiderata:

* explicit content-addressed storage access
* storing / reading values via Glas Objects 

Notes: I looked into checking for console vs. pipe use, e.g. isatty, but it seems to not be a robust cross-platform feature. Also, it's more implicit than I'd prefer as an input. Instead, programs should use args to guide display mode.

## Web App Effects API

A web-app should compile for running in a browser. 

Requirements:

* DOM or virtual DOM + React style UI
* precompute a static document
* XMLHttpRequest, Websockets
* access to local storage

A web-app should compile to an HTML document using JavaScript or WASM.

## Immediate Mode GUI

Transaction machines can work well with retained-mode or immediate-mode GUIs. In the latter case, we would want to 'draw' a stable GUI, specifying resources for feedback. 

 But for live coding, something closer to immediate-mode seems a better fit to ensure continuous transition. Attempting to work with GUI in terms of the GUI model's state is a awkward.

 otherwise we can easily have some vestigial retained-mode GUI resources when we update the program. 


