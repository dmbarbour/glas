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

## State Model

Transactions operate primarily on state. State might be based on variables, channels, sessions, or graphs. Ideally, state resources are simple, extensible, expressive, composable, securable, manageable, and transaction-friendly. 

If state is hierarchical, we can restrict a subprogram to operate on a subtree. Communication between subprograms is explicitly managed by the parent program. We'll use references to communicate and maintain a logical connectivity graph. Reference counting is awkward and difficult to manage.

If state is graph-structured, we can restrict a subprogram to a subgraph reachable by directed edges. Communication between subprograms involves manipulating a shared subgraph. The connectivity graph is explicit in the model



## Environment Design

We can modify a system transactionally, but what should that system look like? The choice of environment model also has a significant impact on extensibility, liveness, and programming experience.




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

### References vs. Cursors

References access a resource through an intermediate identifier such as using `mem:42` to access a variable or memory cell. References are conventional and expressive, but also difficult to distinguish within a program, which is troublesome for memory management, rendering implicit relationships, etc..

Cursors navigate an implicitly struc





The advantage of doing so is that this is relatively conventional. A big disadvantage is that it becomes difficult to reason about memory management


### IO Variables

For consistent composition across internal and external systems, we can reserve a subset of variables for allocation by and interaction with the environment. For this role, I reserve variables identified by dicts containing `io` such as `io:42`.

The environment could enforce invariants for how IO variables are used. For example, it could enforce that an output channel is write-only and binary-data-only. This might be enforced at the top-level transaction to avoid semantic observation of type errors.

Without IO variables, it is feasible to use callback-style, i.e. an asynchronous effect receives a channel parameter representing where to put a result. However, it becomes difficult to 

 results or to control a long-running task. However, this entangles layers and hinders maintenance of invariants.


 and it doesn't compose nicely.

Second, we could provide interface resources to the external task when it is established, such as channels modeled using variables.

First, we could 'attach' the task to application control variables.

One option is that the application specifies some variables for use as channels or futures. This includes channels both *to* the task and *from* the task. 


This interface can be modeled as a `(send, recv)` channel pair.





## Common Effects APIs

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

In addition to a few general-purpose methods, there are several specialized methods to avoid conflicts for simple models of queues and counters. For example, multiple black-box writers may blindly increment a number or addend a list without conflict.

General purpose methods for variables:

* **get** - Response is value contained in defined variable. Fails if undefined.
* **set:Value** - Update defined variable to contain Value. Response is unit. Fails if undefined.
* **new:Value** - Update undefined variable to contain Value. Response is unit. Fails if defined.
* **del** - Delete the variable's definition. Response is unit. Fails if undefined. 

Specialized methods for queues:

* **deq:Count** - Removes first Count items from head of list variable. Response is the sublist it removes. Fails for non-list variables or if the list contains fewer than Count items.
* **enq:\[List, Of, Vals\]** - Addends to tail of list variable. Response is unit, or fails for non-list variables.

Specialized methods for counters:

* **inc:Count** - Increase number variable by given value. Response is unit, or fails for non-number variables.
* **dec:Count** - Decrease number variable by given value. Response is unit, or fails for non-number variables or if would reduce value below zero.

### Channels

For channels, we'll often want bounded buffer pushback and obvious termination. This can be modeled by a `(data, ready)` pair of variables, where 'data' is a queue, and 'ready' is a counter.

A reader will dequeue from data and increment ready. A writer will enqueue on data and decrement ready. The initial number in 'ready' is essentially the maximum buffer size. For termination, the reader deletes 'data' to indicate it's done consuming, or the writer deletes 'ready' to indicate it's done producing.

For bi-directional communication, channels can be paired as `(send, recv)`, for a total of four variables. For consistency, and to resist accident, single-direction channels should indicate their intended direction with a `send` or `recv` header, i.e. an output channel is fully represented by `send:(data:Var1, ready:Var2)`. Code to write to a channel then always selects a `send` header.

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

