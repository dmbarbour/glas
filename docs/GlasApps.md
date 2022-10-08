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

Transaction machines don't solve live coding, but they do lower a few barriers. Application code can be updated atomically between transactions. Threads can be regenerated according to stable non-deterministic choice in the updated code. Divergence or 'tbd' programs simply never commit; they await programmer intervention and do not interfere with concurrent behavior.

Remaining challenges include stabilizing application state across minor changes, versioning major changes, provenance tracking across compilation stages, rendering live data nearby the relevant code.

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

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value. This method will always return the same value within a transaction. 
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail. 

These APIs interact differently with incremental computing. Use of 'time:now' is inherently unstable so will force the transaction to repeatedly backtrack and retry. Use of 'time:check' will only update once for a future TimeStamp, and is useful for precise waits and timeouts. A runtime can arrange for the transaction to evaluate again slightly ahead of the specified time, then commit at that time.

TimeStamp values will use NT time - a natural number of 100ns intervals since midnight, Jan 1, 1601 UTC. The 100ns interval is more than accurate enough for casual use. 

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

The proposed convention is that a log message is represented by a record of ad-hoc fields, whose roles and data types are de-facto standardized. For example, `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", msg:(event:(...),state:(...)) from:hal)`. This supports extension with new fields and structured content.

Initially, log messages will simply write to standard error. Messages may be colored based on 'lv'. However, this could be improved significantly. I hope to eventually support a graphical tree-view of 'fork' processes where each process has its own stable subset of log messages, and we can scrub or search the timeline.

### Random Data

Non-deterministic 'fork' is not random because a scheduler can heuristically search for choices that lead to success. Similarly, 'random' is not necessarily non-deterministic. These two ideas must be distinguished. A viable API:

* **random:N** - response is cryptographically random binary of N bytes.

The implementation of random must backtrack on failure, such that we aren't implicitly searching for a successful string of random bits. It is possible to use a separate 'random' source per stable thread (i.e. per fork path) to further stabilize the system. Performance should be good, e.g. users are free to directly use random for simulating dice.

### Shared Database

Glas applications provide access to a lightweight, hierarchical key-value database that is shared with concurrent and future applications. For many use-cases, this database is more convenient than working with the filesystem. Proposed API:

* **db:get:Key** - get value associated with Key. Fails if no associated value.
* **db:put:(k:Key, v:Value)** - set value associated with Key.
* **db:del:Key** - remove Key from database.

Keys are bitstrings, and the database is implicitly a radix tree, a dict value. It is possible to operate on volumes of the database via shared key prefix, e.g. to take snapshots or clear the database. Transactions on the database are atomic, isolated, and durable. Consistency is left to the applications. Conventions to avoid naming conflicts or carve out application-private spaces are left to the community. 

In context of the glas command line, this database is normally represented under GLAS_DATA or `~/.glas`. This database cannot reasonably be separated from the content-addressed storage layer, and GLAS_DATA may also contain a persistent memoization cache that mitigates need for persistent state.

### Filesystem

Filesystems are ubiquitous and awkward. The filesystem API is mostly supported for integration with external tools. If you just want some persistent state, use the *Shared Database* API instead.

Console IO will be modeled as filesystem access with `std:in`, `std:out`, and `std:err` as implicit open file references. (By default, `std:err` is also used by the logging system, so there may be some interference.)

Proposed API:

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:Interaction)** - Response is unit, or failure if the FileRef is already in use. Binds a new filesystem interaction to the given FileRef. Usually does not wait on OS (see 'status').
  * *read* - read file as stream. Status is set to 'done' when last byte is available, even if it hasn't been read yet.
  * *write* - open file and write from beginning. Will delete content in existing file.
  * *append* - open file and write start writing at end. Will create a new file if needed.
  * *delete* - remove a file. Use status to observe potential error.
  * *move:NewFileName* - rename a file. Use status to observe error.
 * **close:FileRef** - Release the file reference.
 * **read:(from:FileRef, count:Nat)** - Response is list of up to Count available bytes taken from input stream. Returns fewer than Count if input buffer is empty. 
 * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. Fails if not opened for write or append. Use 'busy' status for heuristic pushback.
 * **status:FileRef** - Returns a record that may contain one or more flags and values describing the status of an open file.
  * *init* - the 'open' request has not yet been seen by OS.
  * *ready* - further interaction is possible, e.g. read buffer has data available, or you're free to write.
  * *busy* - has an active background task.
  * *done* - successful termination of interaction.
  * *error:Message* - reports an error, with some extra description.
 * **ref:list** - return a list of open file references. 
 * **ref:move:(from:FileRef, to:FileRef)** - reorganize references. Fails if 'to' ref is in use. 

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:DirName, as:DirRef, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Fails if DirRef is already in use, otherwise returns unit. Potential Interactions:
  * *list* - read a list of entries from the directory. Reaches Done state after all items are read.
  * *move:NewDirName* - rename or move a directory. Use status to observe error.
  * *delete:(recursive?)* - remove an empty directory, or flag for recursive deletion.
 * **close:DirRef** - release the directory reference.
 * **read:DirRef** - read a file system entry, or fail if input buffer is empty. This is a record with ad-hoc fields including at least 'type' and 'name'. Some potential fields:
  * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
  * *name:Path* (always) - a full filename or directory name, usually a string
  * *mtime:TimeStamp* (optional) - modify time 
  * *ctime:TimeStamp* (optional) - creation time 
  * *size:Nat* (optional) - number of bytes
 * **status:DirRef** ~ same as file status
 * **ref:list** - return a list of open directory references.
 * **ref:move:(from:DirRef, to:DirRef)** - reorganize directory references. Fails if 'to' ref is in use.
 * **cwd** - return current working directory. Non-rooted file references are relative to this.
 * **sep** - return preferred directory separator substring for current OS, usually "/" or "\".

It is feasible to extend directory operations with option to 'watch' a directory for updates.

### Environment Variables

A simple API for access to OS environment variables, such as GLAS_PATH.

* **env:get:String** - read-only access to environment variables. 
* **env:list** - returns a list of defined environment variables.

Glas applications won't directly update environment variables. However, it is possible to simulate updates using the env/eff handler.

### Network

Most network interactions with external services can be supported by TCP or UDP. Support for raw Ethernet might also be useful, but it's low priority for now.

* **tcp:TcpOp** - namespace for TCP operations
 * **l:ListenerOp** - namespace for TCP listener operations.
  * **create:(port?Port, addr?Addr, as:ListenerRef)** - Create a new ListenerRef. Return unit. Whether listener is successfully created is observable via 'state' a short while after the request is committed.
   * *port* - indicates which local TCP port to bind. If omitted, OS chooses port.
   * *addr* - indicates which local network cards or ethernet interfaces to bind. Can be a string or bitstring. If omitted, attempts to bind all interfaces.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **status:ListenerRef** ~ same as file status
  * **info:ListenerRef** - For active listener, returns a list of local `(port:Port, addr:Addr)` pairs for that are being listened on. Fails in case of 'init' or 'error' status.
  * **close:ListenerRef** - Release listener reference and associated resources.
  * **ref:list** - returns list of open listener refs 
  * **ref:move:(from:ListenerRef, to:ListenerRef)** - reorganize references. Cannot move to an open ref.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'status' to diagnose error vs. end of input. 
 * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes. Use 'busy' status for heuristic pushback.
 * **limit:(of:Ref, cap:Count)** - fails if number of bytes pending in the write buffer is greater than Count or if connection is closed, otherwise succeeds returning unit. Not necessarily accurate or precise. This method is useful for pushback, to limit a writer that is faster than a remote reader.
 * **status:TcpRef** ~ same as file status
 * **info:TcpRef** - Returns a `(dst:(port, addr), src:(port, addr))` pair after TCP connection is active. May fail in some cases (e.g. 'init' or 'error' status).
 * **close:TcpRef**
 * **ref:list** - returns list of open TCP refs 
 * **ref:move:(from:TcpRef, to:TcpRef)** - reorganize TCP refs. Fails if 'to' ref is in use.

* **udp:UdpOp** - namespace for UDP operations. UDP messages use `(port, addr, data)` triples, with port and address refering to the remote endpoint.
 * **connect:(port?Port, addr?Addr, as:UdpRef)** - Bind a local UDP port, potentially across multiple ethernet interfaces. Fails if UdpRef is already in use, otherwise returns unit. Whether binding is successful is observable via 'state' after the request is committed. Options:
  * *port* - normally included to determine which port to bind, but may be left to dynamic allocation. 
  * *addr* - indicates which local ethernet interfaces to bind; if unspecified, attempts to binds all interfaces.
 * **read:(from:UdpRef)** - returns the next available UDP message value. 
 * **write(to:UdpRef, data:Message)** - output a UDP message
 
  using same `(port, addr, data)` record as messages read. Returns unit. Write may fail if the connection is in an error state, and attempting to write to an invalid port or address or oversized packets may result in an error state.
 * **status:UdpRef** ~ same as file status
 * **info:UdpRef** - Returns a list of `(port:Port, addr:Addr)` pairs for the local endpoint.
 * **close:UdpRef** - Return reference to unused state, releasing system resources.
 * **ref:list** - returns list of open UDP refs.
 * **ref:move:(from:UdpRef, to:UdpRef)** - reorganize UDP refs. Fails if 'to' ref is in use.

A port is a fixed-width 16-bit number. An addr is a fixed-width 32-bit or 128-bit bitstring (IPv4 or IPv6) or a text string such as "www.example.com" or "192.168.1.42" or "2001:db8::2:1". Later, I might add a dedicated API for DNS lookup, or perhaps for 'raw' Ethernet.

### Channels

A channel communicates using reliable, ordered, buffered message passing. Unlike TCP, channels will support structured data and fine-grained subchannels. This can support distributed object-oriented systems, for example. A viable API:

* **c:send:(data:Value, over:ChannelRef, many?)** - send a value over a channel. Return value is unit. Extensions:
 * *multi* - optional flag. Value must be a list. Equivalent to separately sending each value in that list in order.
* **c:recv:(from:ChannelRef, many?Count, exact?)** - receive data from a channel. Return value is the data. Fails if no input available or if next input isn't data (try 'accept'). Extensions:
 * *many:Count* - optional. If specified, will return up to Count data items (at least one, otherwise read fails) as a list. If Count is zero, returns all available data items (still at least one).
 * *exact* - optional flag. Used with 'many:Count', adjusts behavior to return exactly Count items as a list, otherwise fail. Always fails if 'many' is unspecified or Count is zero.
* **c:attach:(over:ChannelRef, chan:ChannelRef, mode:(copy|move|bind))** - connect a channel over a channel. Behavior varies depending on mode:
 * *copy* - a copy of 'chan' is sent (see 'copy')
 * *move* - 'chan' is detached from calling process. (attach copy then drop original)
 * *bind* - a new channel is established, with one endpoint bound to 'chan'. Fails if 'chan' in use.
* **c:accept:(from:ChannelRef, as:NewChannelRef)** - Receives a channel endpoint, binding to the 'as' channel. This will fail if the next input on the channel is not a channel (or not available), such that send/attach order is preserved at recv/accept.
* **c:pipe:(with:ChannelRef, and:ChannelRef, mode:(copy|move|bind))** - connect two channels such that future messages received on one channel are automatically forwarded to the other, and vice versa. This includes pending message and attached channels. Behavior varies depending on mode:
 * *copy* - a copy of the channels is connected; original refs can tap communications.
 * *move* - piped channels are detached from caller (see 'close'), managed by host system.
 * *bind* - new channel is created between two references. Fails if either ChannelRef is already in use.
* **c:copy:(of:ChannelRef, as:ChannelRef)** - duplicate a channel, its pending inputs, and future inputs including subchannels. Writes to the copy and original will be merged in some non-deterministic order.
* **c:drop:ChannelRef** - detach channel from calling process, enabling host to recycle associated resources. Indirectly observable via 'test'.
* **c:test:ChannelRef** - Fails if the channel is known by system to be defunct, supporting no possibility of further interaction. Succeeds otherwise, returning unit. Interaction includes reading messages or writing messages and having them read.
* **c:tune:(chan:ChannelRef, with:Flags)** - Inform the system about your specific use-case for this channel, such that it can perform some extra optimizations. May restrict operations. Monotonic (no take-backs!). Multiple flags may be composed into a record. Flags:
 * *no-write* - disables future 'send' and 'attach' operations for this channel. Future attempted writes will fail. 
 * *no-read* - disables future 'recv' and 'accept' operations for this channel. Clears input buffer and arranges to silently drop future inputs. 

Channels over TCP is a viable foundation for networked Glas systems. See [Glas Channels](GlasChannels.md) for more discussion on this.

* **c:tcp:bind:(wrap:TcpRef, as:ChannelRef)** - removes TcpRef from scope, binds ChannelRef. This implements the channel (and subchannels) over TCP, using [Glas Object](GlasObject.md) to represent values. The TCP connection will also handle protocol-layer interactions to support features such as querying for globs, providing access to a content-distribution network, or routing pipes efficiently.
* **c:tcp:l:bind:(wrap:ListenerRef, as:ChannelRef)** - removes ListenerRef from Scope, binds ChannelRef. This ChannelRef can only 'accept' new subchannels, one for each received TCP connection.

General reference manipulation:

* **c:ref:list** - return a list of open channel references
* **c:ref:move:(from:ChannelRef, to:ChannelRef)** - rename a ChannelRef. Fails if target reference is in use.


## Misc Thoughts

### Console Applications

See [Glas command line interface](GlasCLI.md).

### GUI Apps

No direct support for GUI. Indirectly, we could potentially use network access to X11 and sound APIs, or write a web server that serves a GUI.

### Notebook Applications

I like the idea of building notebook-style applications, where each statement is also a little application serving its own little GUI. Live coding should be implicit. The notebook pages should be highly reactive to changes in code, avoiding overuse of history-dependent behavior. 

The GUI must be efficiently composable, such that a composite application can combine GUI views from component applications. Ideally, we can also support multiple views and concurrent users, e.g. an application serves multiple GUIs.

Component applications would be composed and connected. I like the idea of using *Reactive Dataflow Networks* for communication because it works nicely with live coding, so we might assume the notebook has some access to a loopback port and possibly to user model and GUI requests via reactive dataflow.

### User Interface APIs

Initial GUI for command line interface applications will likely just be serving HTTP connections. But for notebook applications, we might benefit from a higher level API such that we can do more structured composition before converting the GUI to lower level code. About the only idea I'm solid on is that processes should accept and 'serve' GUI connections, which can easily support running headless or multiple users and views. 

### Web Applications

A promising target for Glas is web applications, compiling applications to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. Transaction machines are a decent fit for web apps. And we could also adapt notebook applications to the web target.

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

### FFI

Direct support for FFI is a bad idea. But it might be useful to eventually include DLLs and headers as modules, and somehow use them when compiling an application. 
