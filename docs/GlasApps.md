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

## Abstract Design

### Application Objects

A transaction machine can be usefully viewed as an object with private memory and transactional step method. The scheduler repeatedly calls 'step', resulting in the essential transaction machine behavior. However, additional methods might be called under suitable conditions to simplify extension of the application integration between the application and its host.

For example, we can introduce an 'icon' method that computes a small PNG icon based on application state. This potentially indicates whether the application is busy or has pending notifications. The icon might be infrequently requested by the system, and forbid some effects such as await, fork, and write. We could similarly have a 'status' method that provides a lightweight summary of what the application is doing or whether it requires administrative attention.

Applications that do not require passive background behavior could potentially define the 'step' method to fail, then rely on other methods to handle system requests or user interaction.

I propose to model applications as Glas programs of type `Method -[Eff]- Result` - a single parameter representing the method, a single result for the method return value, and suitable effects. This is is a lightweight dependent type: effects and result types may depend on the method variant.

### Robust References

There are a lot of benefits to having applications allocate their own references. For example, instead of `open file` returning a system-allocated reference, we express `open file as foo` to specify that symbol 'foo' will be our reference to the new file. 

This supports static allocation of references. The references can be descriptive and meaningful for debugging, reflection, and extension. Dynamic allocation of references is easily partitioned, reducing contention on a central allocator. There are no security risks related to potential forgery of system references.

### Specialized APIs

For applications and their platforms, specialized effects APIs are better. A console app platform should have an API specialized for file and network access, while a web-app platform would focus on document object model and XMLHttpRequest. 

The application should have an effects API that is specific to the application's data and user interaction model, almost independent of the host. This simplifies model testing, analysis, protection of invariants, porting to different hosts, etc.

General APIs and frameworks should be used mostly for intermediate adapter layers between application and host.

## Common Effects

Most effects are performed indirectly via channels. But we still need an env/eff API layer to manage these communications.

### Concurrency

Task-based concurrency is based on repeating transactions that perform fair, stable, non-deterministic choice. With support from runtime and compiler, this can be optimized into replication, with each replica taking a different choice. Effects API:

* **fork** - response is non-deterministic unit or failure. 

Fork reduces to fair random choice when used in an unstable or non-repeating context. Or if further replication is not supported, e.g. due to quotas.

### Timing

Transactions are logically instantaneous. The relevant time of a transaction is when it commits. It is troublesome to observe commit time directly, but we can constrain commit time to control scheduling. Effects API:

* **await:TimeStamp** - Response is unit if time-of-commit will be equal or greater than TimeStamp, otherwise fails.

The system does not know exact time-of-commit before committing. At best, it can make a heuristic estimate. It's preferable to estimate a just little high: the system can easily delay commit by a few milliseconds to make an 'await' valid. 

When await fails and the transaction aborts, the timestamp serves as a hint for when the transaction should be recomputed. It is feasible to precompute the future transaction and have them prepared to commit almost exactly on time. This can support real-time systems programming.

Timestamps will initially support `nt:Number` referring to Windows NT time - a number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. This could be extended with other variants.

### Memory

Applications need private memory to carry information across transactions. For convenience and simplicity, memory is modeled as a key-value database. Keys (MemRefs) and values both are arbitrary Glas data. The default value associated with a key is unit. Allocation of refs to each purpose is left to the application and is independent from other references.

* **mem:(on:MemRef, op:MemOp)** - MemRef is an arbitrary value. The MemOp represents an operation to observe or modify the associated value. 
 * **get** - Response is value currently associated with ref.
 * **put:Value** - Modify memory so value is subsequently associated with ref.
 * **swap:Value** - Combines get and put to avoid some read-write conflicts.

It is feasible to add more operations, e.g. specialized list ops so transactions operating at opposite ends of the list avoid conflict. However, this is not a high priority.

Memory is managed manually. Assigning unit to a memref can release underlying memory resources. We can potentially extend memory operations with dedicated ops for lists and records to improve precise conflict analysis. Garbage collection is also feasible, e.g. using several application methods for roots, tracing, and disposal such that they can run incrementally.

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

There is no equivalent to 'setenv', but it is feasible to use the env/eff operator to override these values in context of a subprogram. Similarly, use of env/eff operators can control a subprogram's exposure to command line arguments.

### Standard IO

Standard input and output can be modeled as initially open file references, following convention. However, instead of integers, I propose to reserve symbols `stdin`, `stdout`, and `stderr` as file references.

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
 * **read:(from:FileRef, count:N, exact?)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'state' to diagnose. Option:
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
  * *ok* - seems to be in a good state
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
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N, exact?)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'state' to diagnose. Option:
  * *exact* - flag. If set, fail if fewer than N bytes are available.
 * **write:(to:TcpRef, data:Binary)** 
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

An Addr could be a 32-bit number (IPv4), a 128-bit number (IPv6), or a string such as "www.google.com" or "192.168.1.42". Similarly, a Port can be a 16-bit number or a string such as "ftp" that is externally associated with a port (cf. `/etc/services` in Linux). 

At the moment, I'm not providing APIs for `getaddrinfo` and similar address lookup services. It is feasible to do so later, but it is very low priority.

*Note:* Half-closed TCP is a potentially useful feature, but has been effectively disabled by many routers to help resist Denial of Service attacks. I've decided to not support it in this API.

## Web Applications

Another promising target for Glas is web applications, compiling apps to JavaScript and using effects based on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Compilation to JavaScript should be written as a function within Glas.

### Document Object Model

### Local Storage

### HTTP Requests

### Web Sockets


## Miscellaneous

### IMGUI

The immediate mode graphical user interface (IMGUI) design pattern is a great fit for my vision of live coding and robust reactive systems. However, retained mode GUI is much more prevalent today. Transaction machines can provide APIs for either approach.

A viable approach to immediate mode GUI: the application provides an 'imgui' method that the system implicitly evaluates at 30Hz (or other framerate) while a user is observing. The method will draw boxes, buttons, text, and graphical primitives. Incremental computing applies: a stable GUI doesn't need to be fully recomputed. The 'fork' effect might be interpreted as partitioning the GUI into layers that may update independently.

The imgui method also has access to the user model - clipboard, cookies, display resolution, preferences, authority, attention, etc.. While drawing interactive elements such as buttons, there can be immediate feedback for whether the user is pressing that button. Multiple users can concurrently interact with an app, and a single user might have multiple concurrent sessions and views.

The specialized APIs design principle implies we should provide a retained mode API if that's what the underlying platform supports. An IMGUI API would be implemented as an adapter layer, above the platform but below the app. Support for IMGUI, thus, will depend on maturity of the Glas system.

### RPC

Remote Procedure Calls (RPC) should not be modeled as an application method because composition becomes too awkward. It's infeasible for an application to directly call another app. It's also difficult to know where to route a call in case of hierarchical composition. 

However, asynchronous RPC can be implemented above network connections or abstract channels. The transaction machine would simply fork a loop to read and handle incoming requests. Intriguingly, we could also support 'continuous' RPC if we build upon a publish-subscribe model. 

### Synchronous Syntax

Transaction machines work most conveniently with asynchronous interaction models, but it's often more convenient to express programs as synchronous interactions. To resolve this, we could design a syntax that compiles a program into many smaller transactions, perhaps using a state machine to identify the safe intermediate states.

Transaction machines still provide a lot of benefits for process control and safe concurrency even when used this way. However, it is important to ensure the concurrency semantics are clear in the syntax.
