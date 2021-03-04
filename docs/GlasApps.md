# Glas Applications

Glas programs are essentially transactional due to backtracking conditional behavior. I propose to embrace this, using a *transaction machine* application architecture, where the application loop is a repeating transaction. Transaction machines have many nice properties for process control, live coding, reactive behavior, and real-time systems. 

Transactions restrict direct use of synchronous effects APIs, i.e. an application cannot send a request then await a response within a single transaction. However, transactions can work with channels, mailboxes, tuple spaces, and various other models. APIs for filesystem, network, GUI, or specialized for web-apps can be designed around use of transactions. 

This document discusses transaction machines and development of applications with Glas.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has very many nice emergent properties that cover a wide variety of systems-level concerns. The cost is that a high-performance implementation is non-trivial, requiring incremental computing, replication on fork, and data routing optimizations.

### Process Control

Deterministic, unproductive transactions will also be unproductive when repeated unless there is a relevant change in the environment. The system can optimize by waiting for relevant changes. 

Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. For example, we could abort to wait for data on a channel, or abort to wait on time to reach a threshold condition. This supports process control.

### Reactive Dataflow

A successful transaction that reads and writes variables is unproductive if the written values are equal to the original content. If there is no cyclic data dependency, then repeating the transaction will always produce the same output. If there is a cyclic data dependency, it is possible to explicitly detect change to check for a stable fixpoint.

A system could augment reactive dataflow by scheduling transactions in a sequence based on the observed topological dependency graph. This would minimize 'glitches' where two variables are inconsistent due to the timing of an observation.

*Aside:* Transaction machines can also use conventional techniques for acting on change, such as writing update events or dirty bits.  

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail implicitly loops to process channels or fast-changing variables.

Stable prefix and attention from the programmer is adequate for transaction machine performance. However, it is feasible to take incremental computing further with reordering optimizations such as lazy reads, or implicitly forking a transaction based on dataflow analysis.

### Task-Based Concurrency and Parallelism

Task-based concurrency for transaction machines can be supported by a fair non-deterministic fork operation, combined with incremental computing and a replication optimization. 

Relevant observations: A non-deterministic transaction is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When the choice is stable, replication reduces recomputation and latency. 

Stable forks enable a single transaction to model a concurrent transaction machine. Forks are dynamic and reactive. For example, if we fork based on configuration data, any change to the configuration will rollback the fork and rebuild a new set.

Transactions evaluate in parallel only insofar as conflict is avoided. When conflict occurs between two transactions, one must be aborted by the scheduler. Progress is still guaranteed, and a scheduler can also guarantee fairness for transactions that respect a compute quota. A scheduler heuristically avoids conflict based on known conflict history. Programmers avoid conflict based on design patterns and fine-grained state manipulations.

### Real-Time Systems 

The logical time of a transaction is the instant of commit. It is awkward for transactions to directly observe time before we commit. However, it is not difficult to constrain time of commit. If the transaction aborts because it's too early, the system knows how long to wait.

When constrained by high-precision timestamps, transactions can control their own scheduling to a fine degree. Usefully, the system can also precompute transactions slightly ahead of time so they can 'commit' almost exactly on time. This enables precise control of timed IO effects, such as streaming sound.

It is acceptable that only a subset of transactions are timed. The other transactions would be scheduled opportunistically. In case of transaction conflict, time-constrained transactions can be given heuristic priority over opportunistic transactions.

### Declarative Routing

A subset of transactions may focus on wiring or routing data - i.e. moving or copying data without observing it. It is feasible for the system to identify stable routes, such that the writer transaction can directly send data to its destination, skipping one or several transactions. A motive is to eliminate latency and indirection of intermediate transactions. 

This optimization is most useful when it penetrates interface boundaries between applications or components. An application can serve as a switchboard or introducer for other applications, yet preserves ability to modify routes based on a time-varying configuration. There is no compromise to latency, modularity, or reactivity.

Declarative routing can be supported by dedicated move and copy APIs, but it is awkward to use such APIs across abstraction layers and resource models. Thus, in practice, this optimization depends on abstract interpretation, lazy reads, and other techniques to distinguish blind dataflow.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing features: 

* code can be updated atomically between transactions
* application state is accessible for transition code
* preview execution for a few cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## High-level API Design

### Hierarchical Composition

Applications should compose hierarchically. A host will use Glas env/eff operators to partition resources and integrate communications between component applications. The host should be able to sandbox activity on component boundaries, preferably with low runtime overheads. 

Ideally, resources are also partitioned hierarchically, such that we can easily perform snapshots, reflection, resets, etc.. that align with component boundaries. Partitioning should be efficient and robust, not relying on fine-grained manipulation of calls.

One viable API is to support structured entry/exit effects to operate on different partitions. The Glas system would enforce safe use either via static analysis of 'eff' (e.g. session types) or use 'env' to count balanced, non-negative entry/exit operations.

### Declarative Delegation

In context of live coding or continuous deployment, an application cannot commit to any permanent distribution of authority. This relates to a few principles of secure interaction design: visibility, revocability, and trusted path.

APIs should avoid directly introducing objects. Instead, APIs should be designed around declarative routing optimizations, such that distribution of authority is reactive to change in code or configuration. Essentially, applications should behave as switchboard, not matchmaker. 

### Application Objects

Application transactions are more extensible and composable if they take a parameter for method and arguments then produce a result. This parameter might be label 'step' when called from the scheduler, but may generally represent queries, signals, events, interrupts, or remote procedure calls.

The system might support a mechanism for applications to register and bind services, perhaps requiring acyclic dependencies.

Result and effects types may depend on the parameter. For example, a call to draw a user interface might have specialized effects for drawing boxed and buttons, and limited access to side-effects other than presenting user controls and a view.

### Local References

References are values that identify external resources. However, the ability to share references between applications has awkward consequences for resource management, security, and hierarchical composition. These issues can be mitigated by favoring local references that cannot be shared.

In context of hierarchical composition, a convenient way to ensure locality of references is for each application to allocate its own references where feasible. For example, when opening a file, the file descriptor becomes a parameter to 'open' instead of a return value. This design supports static allocation and meaningful references.

*Note:* We should avoid manually manipulating or rewriting individual references with env/eff. That quickly becomes a tarpit. Instead, we might use entry/exit effects to implicitly isolate all references within a scope.

### Conflict Analysis

To support parallel computation of transactions, the API must enable precise, fine-grained read-write conflict detection between forks within an application, and also between applications. This can be supported by partitioning state into small variables, and also by specializing updates such as blindly writing a channel.

### Observable Failure

A weakness of the try/then/else hierarchical transaction is that a program cannot present the cause of failure to a user. This hinders potential for dry-runs and comprehension of the system. Thus, we might also support hierarchical transactions at the effects layer, e.g. with a 'try' followed by a paired 'commit' or 'abort' almost independent of program structure.

## Common Effects API

### Concurrency

For transaction machines, task-based concurrency is based on repeating transactions that perform a fair, non-deterministic choice.

* **fork** - response is unit or failure. This response is non-deterministic but fair. 

In context of repeating transactions and incremental computing, stable forks can be optimized by replication, taking both paths. Replication requires special support from runtime or compiler, and effective integration with incremental computing. When a fork is unstable, it can be implemented by fair random choice instead of replication. 

Each replicated fork may enter its own transaction loop, communicating indirectly via shared variables or reading and writing channels. Essentially, a replicated fork is the application thread of a transaction machine.

### Timing

Transactions are logically instantaneous. The only relevant time of a transaction is when it commits. However, it is troublesome to observe time directly. The system must know how long it should 'wait' before recomputing an aborted transaction.

* **await:TimeStamp** - Response is unit if time-of-commit will be equal or greater than TimeStamp. Otherwise fail.

The system does not know time-of-commit before committing, but can make a heuristic estimate based on timing of past executions. It's best to estimate a little high: the system can delay for a few milliseconds to make a passing 'await' true.

The system will use 'await' to guide real-time scheduling by delaying transactions that abort after await fails. Even if nothing else changes, the transaction can be recomputed when 'await' is soon to succeed, then committed on time. A nice benefit of this design is that the transaction machine remains fully reactive to changes in variables observed before 'await'.

Glas applications will initially support `nt:Nat` timestamps, referring to Windows NT time - a natural number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. 

### Memory

Application memory is an implicit record of Glas values. Conflict analysis can track which fields in the record are read or written. Initial state for a cell is unassigned, meaning 'get' will fail. A few specialized ops enable use of lists as local channels. Effects API:

* **mem:(on:Label, op:MemOp)** - Apply MemOp to memory element designated by Label. Label is an arbitrary bitstring. MemOps:
 * **get** - Response is value assigned to Label, or fail if unassigned.
 * **put:Value** - Assign value to Label in memory.
 * **del** - Remove assigned value from Label in memory. 
 * **exist** -  Fails if Label is unassigned, otherwise respond with unit.
 * **read:N** - splits a list value in Label, returning first N elements and keeping the rest in memory. Fails if the list is smaller than N elements.
 * **unread:List** - Prepend list argument to list value in memory at Label.
 * **write:List** - Append list argument to list value in memory at Label. 

The get/put/del memops support basic record manipulations. Use of 'exist' is a stable alternative to 'get' for probing whether a cell is defined. The read/unread/write memops enable use of lists in memory as lightweight channels.

More flexible analysis of memory, and access to memory as a record value, might be provided by reflection effects. 

### Communication

The application will int

Shared memory is not a good communication model - it is awkward to model life cycles, enforce invariants, hide implementation, support a subset of synchronous interactions, etc.. Instead, an application should communicate with the system via attached interface objects with specialized APIs.

This might be expressed as 'sys' or 'io' calls. 








### System

Sessions reify long-running interactions, and are useful for modeling asynchronous requests, network access, and other effects that require multiple transactions to complete. Interactions with the system that cannot be expressed as a single operation will usually be expressed as a session. 

For Glas, session handles are allocated by the application then bound to a new system object. Like record fields, session handles are bitstring paths that must obey the prefix rule: no valid path is a prefix of another valid path.

To simplify hierarchical composition, session operators centralize all references. 


Requests that cannot complete immediately require allocating a task to continue processing in the background between transactions.

obj.verb()
obj2 = obj1.verb()
obj3 = obj1.verb(obj2) - utility is unclear

It is feasible for system objects to provide extra memory and support memops. But it might not be private memory, in this case.





### Random Numbers

Random numbers should either be based on deterministic PRNG or asynchronous request for entropy.

Transactions can request random numbers. However, it is troublesome if we observe the random numbers before commit. 

random numbers are non-deterministic because that becomes an implicit 'fork'.


### Effects API Discovery

### Resource Discovery

### External Channels

### Data Buses

### Publish-Subscribe

### Dataflow Interfaces

### Termination


 system that it provides cert



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


## Design: Applications as Service Consumers and Providers

A system can serve as an introducer between components. One option is hard-wiring components, but a more interesting option is that applications discover each other. Each application can declare the services they provide and consume, preferably with enough precision to form a topological dependency graph.

The question, then, is how to identify services. We might express a service as a session-type. Perhaps service dependencies can also be expressed as session types.

In any case, this is low priority.


## Immediate Mode GUI

Transaction machines can work well with retained-mode or immediate-mode GUIs. In the latter case, we would want to 'draw' a stable GUI, specifying resources for feedback. 

 But for live coding, something closer to immediate-mode seems a better fit to ensure continuous transition. Attempting to work with GUI in terms of the GUI model's state is a awkward.

 otherwise we can easily have some vestigial retained-mode GUI resources when we update the program. 


