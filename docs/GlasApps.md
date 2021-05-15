# Glas Applications

The conventional application model is the procedural loop, where an application initializes then enters a main loop. However, this is a poor fit for my vision of live coding and reactive systems. There is no mechanism to access state hidden within the loop during a software update, nor to robustly inform the application when its observations or assumptions have been invalidated. 

An intriguing alternative is the *transaction machine* model of applications. Transaction machines have many nice properties for concurrency, process control, live coding, reactive behavior, and real-time systems. They are also a good fit for Glas programs, where the 'try' clauses are essentially hierarchical transactions.

The disadvantage of transaction machines is development overhead. Transactions do not support synchronous interaction, thus many APIs must be redesigned. This is a non-trivial effort. This document discusses transaction machines and suitable APIs to get started with this model.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has very many nice emergent properties that cover a wide variety of systems-level concerns. The cost is that a high-performance implementation is non-trivial, requiring optimizations from incremental computing, replication on fork, and cached routing.

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

It is feasible for a transaction to compare estimated time of commit with a computed boundary. If the transaction aborts because it runs too early, the system can implicitly wait for the comparison result to change before retrying. Use of timing constraints effectively enables transactions to both observe estimated time and control their own scheduling. 

Usefully, a system can precompute transactions slightly ahead of time so they are ready to commit at the earliest timing boundary, in contrast to starting computation at that time. It is also feasible to predict several transactions based on prior predictions. It is feasible to implement real-time systems with precise control of time-sensitive outputs.

Transaction machines can flexibly mix opportunistic and scheduled behavior by having only a subset of concurrent transactions observe time. In case of conflict, a system can prioritize the near-term predictions.

### Cached Routing

A subset of transactions may focus on data plumbing - i.e. moving or copying data without observing it. If these transactions are stable, it is feasible for the system to cache the route and move data directly to its destination, skipping the intermediate transactions. 

Designing around cached routing can improve latency without sacrificing visibility, revocability, modularity, or reactivity to changes in configuration or code. In contrast, stateful bindings of communication can improve latency but lose most of these other properties.

Cached routing can partially be supported by dedicated copy/forward APIs, where a transaction blindly moves the currently available data from a source to a destination. However, it can be difficult to use such APIs across abstraction layers. In general, we could rely on abstract interpretation or lazy evaluation to track which data is observed within a transaction.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing features: 

* code can be updated atomically between transactions
* application state is accessible for transition code
* preview execution for a few cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Application Model Design

One goal is to get started soon, so I'd prefer to avoid radical ideas that complicate the implementation on modern OS. However, within that constraint, I'd like to tune design of applications to better fit transaction machines and my vision for software systems.

### Applications as Objects

Almost any application will have some private state to model state machines. A transaction machine can be modeled as a scheduler repeatedly applying a transactional 'step' method to an application object. 

Then methods other than 'step' can support a user or system interacting with the application. For example, GUI integration might benefit from an 'icon' method that returns an application icon, or a 'notifications' method that returns a list of active alerts for the user. Those methods might only be called occasionally. The system might support a few methods to support hibernation mode, graceful termination, garbage collection, or API versioning.

I propose to model application objects as a `Method -[Effects]- Result` program, where effect and result types may depend on the method. For the `step` method, the result should also be `step` so in the simplest case we could ignore the method parameter and define an application as just the repeating step operation.

### Structured Channels

Communication with external systems should not assume they are local, transactional, or stable. Thus, external communications should be asynchronous, monotonic, and disruption tolerant. Channels are an excellent model under these constraints. Plain data channels are too inflexible, but we can support subtasks using subchannels. 

Glas can support second-class subchannels: the writer may write a choice of data or subchannel, and the reader can detect whether the next element is a subchannel or data and receive it appropriately. An operator may exist to logically wire channels together, which serves as a pseudo first-class channel transfer. 

Loopback channels can support consistent composition and modularity within applications. To ensure consistent behavior with external channels, a transaction cannot read its own writes to a loopback, i.e. there is implicitly an external transaction that forwards the data and subchannels.

*Aside:* Although it is feasible to extend point-to-point channels into a broadcast databus shared by multiple writers and readers, I believe it wiser to limit channels to point-to-point then model broadcast explicitly via connection to intermediate services.

### System Services

For consistency, I propose to model an application's access to the host system as a channel. System requests will mostly be represented by writing a subchannel per request then writing the request description as the first value in the subchannel. The subchannel can receive the future result or support ad-hoc interaction with a long-running background task.

Because the system channel is write-only, there is no risk of read-write conflict between transactions initiating new system requests. Usefully, the system channel preserves *order* of requests, which can be relevant when incrementally patching system state. It is feasible to support a system request to fork the system channel. Requests on different forks would be processed in a non-deterministic order.

### Robust References

References used by an application should be allocated by the application. For example, instead of `open file` *returning* a system-allocated reference such as a file descriptor, we should express `open file as foo` to specify that symbol 'foo' is the application-allocated reference for the newly opened file. 

This design has several benefits: References can be allocated statically by application code. Reference values can be descriptive of purpose and provenance, simplifying debugging or reflection. Allocation of runtime references can be manually partitioned to resist read-write conflict on a central allocator. There are no security risks related to attempted forgery of system references. 

In context of transaction machines, forked transactions can essentially time-share references within the application while preserving logical linearity of references.

### Specialized APIs

Systems and applications should have specialized effects APIs. For example, a console app at the system layer should have an API specialized for file and network access, while a web-app focuses on document object model and XMLHttpRequest. An application's effects model should be designed for the specific data and user interaction models - this simplifies model testing, analysis, protection of invariants, porting to different hosts, etc..

Frameworks and generalized APIs are useful as intermediate models to simplify implementation of many applications across many hosts. But attempting to start with, say, a general model of channels to unify file streams and network sockets will normally prove a mistake due to the subtle differences.

## Common Effects

Most effects are performed indirectly via channels. But we still need an env/eff API layer to manage these communications.

### Concurrency

Task-based concurrency is based on repeating transactions that perform fair, stable, non-deterministic choice. With support from runtime and compiler, this can be optimized into replication, with each replica taking a different choice. Effects API:

* **fork** - response is non-deterministic unit or failure. 

Fork becomes a random choice if used in an unstable context or beyond the limits of a replication quota.

### Timing

Transactions are logically instantaneous. The relevant time of a transaction is when it commits. It is troublesome to observe commit time directly, but we can constrain commit time to control scheduling. Effects API:

* **await:TimeStamp** - Response is unit if time-of-commit will be equal or greater than TimeStamp, otherwise fails.

The system does not know exact time-of-commit before committing. At best, it can make a heuristic estimate. It's preferable to estimate a just little high: the system can easily delay commit by a few milliseconds to make an 'await' valid. 

When await fails and the transaction aborts, the timestamp serves as a hint for when the transaction should be recomputed. It is feasible to precompute the future transaction and have them prepared to commit almost exactly on time. This can support real-time systems programming.

Timestamps will initially support `nt:Number` referring to Windows NT time - a number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. This could be extended with other variants.

### Memory

Applications need private memory to carry information across transactions. For convenience and simplicity, memory is modeled as a key-value database, where keys and values both are arbitrary Glas data. Allocation of keys, aka MemRefs, is left entirely to the application and is independent from other references.

* **mem:(on:MemRef, op:MemOp)** - MemRef is an arbitrary value. The MemOp represents an operation to observe or modify the associated value. 
 * **get** - Response is associated memory value; fails if there is no value.
 * **put:Value** - Insert or replace value associated with ref.
 * **del** - Remove associated value (if any) from MemRef. 
 * **touch** - respond with unit if 'get' would succeed, otherwise fails.
 * **swap:Value** - Atomic get and put. Supports precise conflict analysis: the value written doesn't depend on the value read.

The get/put/del memops support basic manipulations. The touch/swap memops help stabilize common update patterns. I might later add methods for partial observation and manipulation of lists or records, but doing so has relatively low priority.

*Aside:* It is feasible to support automatic garbage collection of memory by introducing an application method to trace all the living memory.

## Console Applications

Console applications minimally require access to:

* environment variables and command line arguments
* standard input and output
* filesystem
* network - UDP and TCP

Log output may bind stderr by default.

### Environment Variables and Command Line Arguments

Accessed by effect as implicit parameters:

* **cmd** - response is list of strings representing command-line arguments.
* **env** - response is list of 'key=value' strings for environment variables.

There is no equivalent to 'setenv', but it is feasible to use the env/eff operator to override these values in context of a subprogram.

### Standard IO

Standard input and output can be modeled as initially open file references, following convention. However, instead of integers, we could reserve symbols `stdin`, `stdout`, and `stderr` as meaningful file references.

### Filesystem

Glas applications support a relatively direct translation of the conventional filesystem API. The main differences are that reads cannot directly wait for data, and for *robust references* I require the application to allocate the file reference.

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:(read | write | append | create | delete))** - Create a new system object to interact with the specified file resource. Fails if FileRef is already in use, otherwise returns unit. Whether the file is successfully opened is observable via 'state' a short while after request is committed. The intended interaction must be specified:
  * *read* - read file as stream.
  * *write* - erase current content of file or start a new file.
  * *append* - extend current content of file.
  * *create* - same as write, except fails if the file already exists.
  * *delete* - removes a file. Use 'state' to observe done or error.
 * **close:FileRef** - Delete the system object associated with this reference. FileRef is no longer in-use, and operations (except open) will fail.
 * **read:(from:FileRef, count:N, exact?)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes can be returned; see 'state' to diagnose. Option:
  * *exact* - flag. If set, fail if fewer than N bytes are available.
 * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. This fails if the file is read-only or is in an error state, otherwise returns unit. It is permitted to write while in a 'wait' state.
 * **state:FileRef** - Return a representation of the state of the system object. 
  * *init* - state immediately after 'open' until request is committed and processed.
  * *ok* - seems to be in a good state. 
  * *done* - requested interaction is complete. This currently applies to read or delete modes. 
  * *error:Value* - any error state, with ad-hoc details. 

* **dir:DirOp** - filesystem directory (aka folder) operations.
 * **open:(name:DirName, as:DirRef, for:(read | create | delete))** - Create a new system object to interact with a directory resource. Fails if DirRef is already in use, otherwise returns unit. Whether the directory is successfully opened is observable via 'state' a short while after the request is committed. Intended interactions must be specified:
  * *read* - supports iteration through elements in the directory.
  * *create* - creates a new directory. Use 'state' to observe done or error.
  * *delete* - remove an empty directory. Use 'state' to observe done or error.
 * **close:DirRef** - Delete the associated system object. DirRef is no longer in-use, and operations (except open) will fail. 
 * **read:DirRef** - Read an entry from the directory table. An entry is a record of form `(name:String, type:(dir | file | ...), ...)` allowing ad-hoc extension with attributes or new types. An implementation may ignore types except for 'dir' and 'file', and must ignore the "." and ".." references. Fails if no entry can be read, see 'state' for reason. 
 * **state:DirRef** - Return a representation of the state of the associated system object. 
  * *init* - state immediately after 'open' until processed.
  * *ok* - seems to be in a good state at the moment.
  * *done* - requested interaction is complete. This applies to 'read' after reading the final entry, or after a successful create or delete.
  * *error:Value* - any error state, with ad-hoc details.

The file and directory APIs could feasibly be extended with additional modes. However, the API as described is probably sufficient for developing many useful Glas applications.

### Network

We can cover the needs of most applications with support for TCP and UDP protocol layers. Instead of touching the mess that is sockets, I propose to specialize the API for each protocol required. Later, we might add raw IP sockets support. 

* **tcp:TcpOp** - namespace for TCP operations
 * **listener:ListenerOp** - namespace for TCP listener operations.
  * **create:(port?Port, addrs?[List, Of, Addr], as:ListenerRef)** - Create a new ListenerRef. Return unit. Whether listener is successfully created is observable via 'state' a short while after the request is committed.
   * *port* - indicates which local TCP port to bind; if excluded, leaves dynamic allocation to OS. 
   * *addrs* - indicates which local network cards or ethernet interfaces to bind; if excluded, attempts to bind all of them.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **state:ListenerRef**
   * *init* - create request hasn't been fully processed yet.
   * *ok* - 
   * *error:Value* - failed to create or detached by OS, with details. 
  * **info:ListenerRef** - After successful creation of listener, returns `(port:Port, addrs:[List , Of, Addr])`. Fails if listener is not successfully created.
  * **close:ListenerRef** - Release listener reference and associated resources.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically.
 * **read:(from:TcpRef, count:N, exact?)**
 * **write:(to:TcpRef, data:Binary, fin?)** 
 * **state:TcpRef**
  * *init*
  * *ok*
  * *error:Value*
  
 * **info:TcpRef** - For a successful TCP connection (whether via 'tcp:connect' or 'tcp:listener:accept'), returns `(dst:(port:Port, addr:Addr), src:(port:Port, addr:Addr))`. Fails if TCP connection is not successful.
 * **close:TcpRef**

* **udp:UdpOp** - namespace for UDP operations.
 * **connect:(port?Port, addrs?[List, Of, Addr], as:UdpRef)** - Bind a local UDP port, potentially across multiple ethernet interfaces. Fails if UdpRef is already in use, otherwise returns unit. Whether binding is successful is observable via 'state' after the request is committed. Options:
  * *port* - normally included to determine which port to bind, but may be left to dynamic allocation. 
  * *addr* - indicates which local ethernet interfaces to bind; if unspecified, binds all of them.
 * **read:(from:UdpRef)** - returns the next available Message value, consisting of `(port:Port, addr:Addr, data:Binary)`. This refers to the remote UDP port and address, and the binary data payload. Fails if there is no available message.
 * **write(to:UdpRef, data:Message)** - output a message using same form as messages read. Returns unit. Write may fail if the connection is in an error state, and attempting to write to an invalid port or address or oversized packets may result in an error state.
 * **state:UdpRef**
  * *init*
  * *ok*
  * *error:Value*
 * **info:UdpRef** - For a successfully connected UDP connection, returns a `(port:Port, addrs:[List, Of, Addr])` pair. Fails if still initializing, or if there was an error during initialization.
 * **close:UdpRef**

An Addr can be a 32-bit number or a string such as "www.google.com" or "192.168.1.42". Similarly, a Port can be a 16-bit number or a string such as "ftp" that might be externally configured (cf. `/etc/services` in Linux).

*Aside:* It might be useful to support a DNS lookup service directly, e.g. similar to getaddrinfo. However, this is low priority.

## Web Applications

Another promising target for Glas is web applications, i.e. compiling apps to JavaScript and the Document Object Model, using XMLHttpRequest, WebSockets, and Local Storage as needed. 

## Meta

### Synchronous Syntax

Transaction machines work most conveniently with asynchronous interaction models, but it's often more convenient to express programs as synchronous interactions. To resolve this, we could design a syntax that compiles a program into many smaller transactions, perhaps using a state machine to identify the safe intermediate states.

Transaction machines still provide a lot of benefits for process control and safe concurrency even when used this way. However, it is important to ensure the concurrency semantics are clear in the syntax.
