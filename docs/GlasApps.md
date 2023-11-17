# Glas Applications

A basic glas application, in context of [Glas CLI](GlasCLI.md), is currently represented by a transactional step function.

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

A step that returns successfully is committed. A failed step is aborted then retried, implicitly waiting for changes to external conditions. The first step receives 'init' and the final step returns 'halt'. Intermediate steps receive and return 'step'. 

This is an example of a *transaction loop* - modeling an application as a repeating transaction. Transaction loops are simple yet offer a robust foundation for reactivity, concurrency, and process control. However, these benefits rely on optimizations that are difficult to implement on step functions. To resolve this, I intend to develop a specialized program representation.

## Transaction Loops

Transaction loops model software systems as an open set of repeating, atomic, isolated transactions in a shared environment. Scheduling of different transactions is non-deterministic. This is a simple idea, but has many nice systemic properties regarding extensibility, composability, concurrency, distribution, reactivity, and live coding. However, transaction loops depend on advanced optimizations such as replication to evaluate many non-deterministic choices in parallel, and incremental computing to stabilize replicas. Implementation of the optimizer is the biggest development barrier for this model.

### Waiting and Reactivity

If nothing changes, repeating a deterministic, unproductive transaction is guaranteed to again be unproductive. The system can recognize a simple subset of unproductive transactions and defer repetition until a relevant change occurs. Essentially, we can optimize a busy-wait into triggering updates on change.

The most obvious unproductive transaction is the failed transaction. Thus, aborting a transaction expresses waiting for changes. For example, if we abort a transaction after it fails to read from an empty channel, we'll implicitly wait on updates to the channel. Successful transactions are unproductive if we know repetition writes the same values to the same variables. Optimizing the success case would support spreadsheet-like evaluation of transaction loops.

Further, incremental computing can be supported. Instead of fully recomputing each transaction, it is feasible to implement repetition as rolling back to the earliest change in observed input and recomputing from there. We can design applications to take advantage of this optimization by first reading relatively stable variables, such as configuration data, then read unstable variables near end of transaction. This results in a tight 'step' loop that also reacts swiftly to changes in configuration data.

### Concurrency and Parallelism

Repeating a single transaction that makes a non-deterministic binary choice is equivalent to repeating a set of two transactions that are identical before this choice then deterministically diverge. We can optimize non-deterministic choice using replication. Usefully, replicas can be stable under incremental computation. Introducing non-deterministic choice enables a single repeating transaction to represent a dynamic set of repeating transactions, i.e. a set of threads.

Transactions in the set will interact via shared state. Useful interaction patterns such as channels and mailboxes can be modeled and typefully abstracted within shared state. Transactional updates and ability to wait on flexible conditions also mitigates many challenges of working directly with shared state.

Concurrent transactions can evaluate in parallel insofar as they avoid read-write conflict. When conflict does occur, one transaction will be aborted by the system while the other proceeds. The system can record a conflict history to heuristically schedule transactions to reduce risk of conflict. Fairness is feasible if we ensure individual transactions do not require overly long to evaluate. Additionally, applications can be architected to avoid conflict, using intermediate buffers and staging areas to reduce contention.

### Distribution 

Distributed evaluation of transaction loops is possible using distributed transactions. However, arbitrary distributed transactions are expensive and vulnerable to denial-of-service and disruption. We can mitigate this by identifying a subset of distributed transactions that can be implemented robustly and efficiently, then designing our distributed applications around them.

One good option is to build around a channels API with abstract intermediate communication. A 'writer' will write to a local buffer that is later moved to the remote buffer by separate transaction. This move transaction requires a lightweight, idempotent message-ack interaction with a single remote node.

There are other patterns that can also be optimized. But channels alone are adequate primitives for developing distributed applications. And I propose to start there.

### Transaction Fusion

It is possible to apply [loop optimizations](https://en.wikipedia.org/wiki/Loop_optimization) to repeating transactions. Conceptually, we might view this as refining the non-deterministic transaction schedule. A random schedule isn't optimal because we must assume external updates to state between steps. By fusing transactions, we eliminate concurrent interference and enable optimization at the boundary. An optimizer can search for fusions that best improve performance. 

Fusion could be implemented by a just-in-time compiler based on empirical observations, or ahead-of-time based on static analysis. Intriguingly, just-in-time fusions can potentially optimize communication between multiple independent applications and services. In context of distributed transaction loops, each application or service essentially becomes an patch on a network overlay without violating security abstractions.

### Real-Time Systems 

Transaction loops can wait on the clock by (logically) aborting a transaction until a desired time is reached. Assuming the system knows the time the transaction is waiting for, the system can schedule the transaction precisely and efficiently, avoiding busy-waits from watching the clock. Usefully, the system can precompute a transaction slightly ahead of time, such that effects apply almost exactly on time.

Further, we could varify that critical transactions evaluate with worst-case timing under controllable stability assumptions. This enables "hard" real-time systems to be represented.

### Live Coding

Transaction loops don't solve live coding, but they do lower a few barriers. Application code can be updated atomically between transactions. Threads can be regenerated according to stable non-deterministic choice in the updated code. Divergence or 'tbd' programs simply never commit; they await programmer intervention and do not interfere with concurrent behavior.

Remaining challenges include stabilizing application state across minor changes, versioning major changes, provenance tracking across compilation stages, rendering live data nearby the relevant code.

## Effects API

### Concurrency

Repetition and replication are equivalent for isolated transactions. If a repeating transaction externalizes a choice, it could be replicated to evaluate each choice and find an successful outcome. If this choice is part of the stable prefix for incremental computing, then these replicas also become stable, each repeating from some later observation. This provides a simple basis for task-based concurrency within transaction loops as an optimization of choice.

* **fork:[List, Of, Values]** - Response is a value externally chosen from a non-empty list. Fails if the argument is empty or is not a list.

I propose modeling fork as a deterministic operation on a non-deterministic environment. This is subtly different from fork as a non-deterministic effect for backtracking and optimizations. For example, we can optimize `cond:(try:A, then:B, else:seq:[A, C])` to `seq:[A, B]` only if we assume `A` is a deterministic operation. Either way, we can effectively support concurrency.

### Time

Transactions are logically instantaneous. The concept of 'timeout' or 'sleep' is incompatible. However, we can constrain a transaction to commit before or after a specified time. A proposed effects API:

* **time:now** - Response is an estimated logical time of commit, as a TimeStamp value. 
* **time:check:TimeStamp** - If 'time:now' is equal or greater to TimeStamp, responds with unit. Otherwise fails. 

The 'time:check' API should be favored over 'time:now' for implementing waits. The imprecise, monotonic observation of time makes 'time:check' easier to optimize and stabilize for incremental computing. But 'time:now' can provide greater than precision. 

Observing time may cause a transaction to be delayed a little such that it commits at the estimated time, or may cause a transaction to be aborted if computation runs much longer than estimated.

TimeStamp values in this API use the Windows NT format: an integer representing 100ns intervals since midnight, Jan 1, 1601 UTC.

*Note:* This time API is simplistic and insufficient for contexts involving distributed transactions, relativistic speeds, or science-fiction time travel. Applications intended for such contexts may require a more sophisticated time API.

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

* **db:get:Key** - get value associated with Key. May fail.
* **db:put:(k:Key, v:Value)** - set value associated with Key. May fail. 
* **db:del:Key** - remove Key from database. May fail.

Keys are bitstrings, and the database is usually a record/dict value. Operating on the prefix of a key will operate on the record of all keys with a matching prefix. Keys should be short, but large values are supported via stowage. Updates are atomic, isolated, and durable. Consistency and security are left to applications. 

Operations may fail due to implicit validation, e.g. verify invariants upon put or commit. In some contexts, an effects handler might also rewrite keys, modeling a logical chroot or virtual memory.

### Filesystem

Filesystems are ubiquitous and universally awkward. The filesystem API here provides a bare minimum for streaming files. Writes are buffered until committed, and reads may be limited to what is in the input buffer when the transaction starts. This should mostly be used for integration; if you just need data persistence, the *Shared Database* is a much better option due to abstracting integration with stowage.

Proposed API:

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
  * **open:(name:FileName, for:Interaction)** - Open a file, returning a fresh FileRef. This operation returns immediately; the user can check 'status' in a future transaction to determine whether the file is opened successfully. However, you can begin writing immediately. Interactions:
    * *read* - read file as stream. Status is set to 'done' when last byte is available, even if it hasn't been read yet.
    * *write* - open file and write from beginning. Will delete content in existing file.
    * *append* - open file and write start writing at end. Will create a new file if needed.
    * *delete* - remove a file. Use status to observe potential error.
    * *move:NewFileName* - rename a file. Use status to observe error.
  * **close:FileRef** - Release the file reference.
  * **read:(from:FileRef, count:N, exact?)** - Tries to read N bytes, returning a list. May return fewer than N bytes if input buffer is low. Fails if 0 bytes would be read, or if 'exact' flag is included and fewer than N bytes would be read.
  * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. Fails if not opened for write or append. Use 'busy' status for heuristic pushback.
  * **status:FileRef** - Returns a record that may contain one or more flags and values describing the status of an open file.
    * *init* - the 'open' request has not yet been seen by OS.
    * *ready* - further interaction is possible, e.g. read buffer has data available, or you're free to write.
    * *busy* - has an active background task.
    * *done* - successful termination of interaction.
    * *error:Message* - reports an error, with some extra description.
  * **std:out** - returns FileRef for standard output
  * **std:in** - returns FileRef for standard input

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
  * **open:(name:DirName, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Returns a DirRef.
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
  * **cwd** - return current working directory. Non-rooted file references are relative to this.
  * **sep** - return preferred directory separator substring for current OS, usually "/" or "\".


It is feasible to extend directory operations with option to 'watch' a directory for updates.

### Environment Variables

A simple API for access to OS environment variables, such as GLAS_PATH.

* **env:get:String** - read-only access to environment variables. 
* **env:list** - returns a list of defined environment variables.

Glas applications won't update environment variables. However, it is possible to simulate the environment for a subprogram via effects handlers. 

### Network

Most network interactions with external services can be supported by TCP or UDP. Support for raw Ethernet might also be useful, but it's low priority for now.

* **tcp:TcpOp** - namespace for TCP operations
  * **l:ListenerOp** - namespace for TCP listener operations.
    * **create:(port?Port, addr?Addr)** - Listen for TCP connections. Returns a ListenerRef. The OS operation is deferred until after the current transaction commits; see 'status'.
      * *port* - indicates which local TCP port to bind. If omitted, OS chooses port.
      * *addr* - indicates which local network cards or ethernet interfaces to bind. Can be a string or bitstring. If omitted, attempts to bind all interfaces.
    * **accept:(from:ListenerRef)** - Receive an incoming connection, and return a TcpRef. This operation will fail if there is no pending connection. 
    * **status:ListenerRef** ~ same as file status
    * **info:ListenerRef** - For active listener, returns a list of local `(port:Port, addr:Addr)` pairs for that are being listened on. Fails in case of 'init' or 'error' status.
    * **close:ListenerRef** - Release listener reference and associated resources.
  * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr))** - Create a new connection to a remote TCP port. Returns a TcpRef. The connection may fail, but it will only be visible in a future transaction; use 'status' to verify successful connection. If 'src' is omitted, it can be dynamically determined.
  * **read:(from:TcpRef, count:N, exact?)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'status' to diagnose error vs. end of input. 
  * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes. Use 'busy' status for heuristic pushback.
  * **limit:(of:Ref, cap:Count)** - fails if number of bytes pending in the write buffer is greater than Count or if connection is closed, otherwise succeeds returning unit. Not necessarily accurate or precise. This method is useful for pushback, to limit a writer that is faster than a remote reader.
  * **status:TcpRef** ~ same as file status
  * **info:TcpRef** - Returns a `(dst:(port, addr), src:(port, addr))` pair after TCP connection is active. May fail in some cases (e.g. 'init' or 'error' status).
  * **close:TcpRef**

* **udp:UdpOp** - namespace for UDP operations. UDP messages use `(port, addr, data)` triples, with port and address refering to the remote endpoint.
  * **connect:(port?Port, addr?Addr)** - Return a UdpRef. This doesn't wait on the OS, so view 'status' in future transactions to determine whether there are problems with the connection.
    * *port* - normally a small natural number specifying which port to bind, but may be left to dynamic allocation. 
    * *addr* - normally indicates which ethernet interface and IP address to bind; if unspecified, attempts to binds all interfaces.
  * **read:(from:UdpRef)** - returns the next available UDP message value. 
  * **write(to:UdpRef, data:Message)** - output a UDP message. Message uses same `(port, addr, data)` record as messages read. Returns unit, and buffers message to send upon commit.
  * **status:UdpRef** ~ same as file status
  * **info:UdpRef** - Returns a list of `(port:Port, addr:Addr)` pairs for the local endpoint.
  * **close:UdpRef** - Return reference to unused state, releasing system resources.

A port is a fixed-width 16-bit number. An addr is a fixed-width 32-bit or 128-bit bitstring (IPv4 or IPv6) or a text string such as "www.example.com" or "192.168.1.42" or "2001:db8::2:1". Later, I might add a dedicated API for DNS lookup, or perhaps for 'raw' Ethernet.

*Aside:* No support for unix sockets at the moment, but could be introduced if needed.

### Channels

A channel communicates using reliable, ordered, buffered message passing. A very useful feature for dynamic systems is the ability to establish fine-grained subchannels, and the ability to connect two channels to move future data directly. A viable API sketch:

* *c:create:(...)* - construct a connected pair of channels
* *c:send:(...)* - send data over channel
* *c:recv:(...)* - read data from channel
* *c:attach:(...)* - send a channel over a channel
* *c:accept:(...)* - read a channel from a channel
* *c:drop:ChannelRef* - detach process from a channel and free resources
* *c:test:ChannelRef* - test whether a channel is still available for interaction
* *c:tune:(...)* - inform OS channel is read-only or write-only for optimizations
* *c:copy:ChannelRef* - copy a channel reference and unread inputs; writes are merged
* *c:route:(...)* - connect output from one channel as input to the other and vice versa

For external communications with other glas system applications, we could support wrapping glas channels around an open TCP connection.

* *c:tcp:bind:TcpRef* - return a channel that communicates over TCP. It will begin processing the remaining data on the TCP input.
* *c:tcp:l:bind:ListenerRef* - return a channel that can only 'accept' new channels. Same as using 'tcp:l:accept' then 'c:tcp:bind' on TCP channels.

I intend to develop this idea further in [Glas Channels](GlasChannels.md).

### Runtime Extensions

A runtime can provide a few effects for manipulating itself. May be implementation-dependent and not very portable. A few ideas:

* *rt:version* - return the string that would be printed by `glas --version`.
* *rt:help* - return the string that would be printed by `glas --help`.
* *rt:time:now* - similar to 'time:now' except not frozen within a transaction. This logically involves reflection over the runtime. Useful for manual profiling.
* *rt:gc:tune:(...)* - tune GC parameters
* *rt:gc:force* - ask runtime to perform a GC immediately(-ish)
* *rt:stat:Var* - return some useful metadata about the runtime

An application runtime should usually *halt* if it does not recognize a requested effect. However, it is feasible to introduce runtime reflection on the available effects.

### OS Extensions

I could support OS operations under an 'os:' prefix, and perhaps OS-specialized actions under a header such as 'os:posix:...'. Not really sure what I need, or how much of the OS should be exposed. Might develop incrementally as needed.

## Automatic Code Distribution

My vision for glas systems is that applications represent live-coded, distributed [overlay networks](https://en.wikipedia.org/wiki/Overlay_network). In context of glas systems, the best place to support this is the binding of channels over TCP.

In addition to communicating data, those TCP connections could communicate code and private state for remote evaluation. Computation quotas per TCP connection can be configurable. Code distribution would enable a flexible tradeoff between communication costs (latency and bandwidth) and computation costs (processor and memory).

The remote code would have very limited access to effects: read and write channels that would otherwise communicate over a TCP connection, and update private state. Everything that can be done by remote code could instead be done locally. The impact on semantics and security would be minimal except insofar as performance is an important part of correctness.

## Procedures and Processes

The glas 'prog' model of programs is not optimal for transaction loop applications. It places a huge burden on the optimizer to support concurrency, parallelism, and incremental computing. I'd like to design a model better optimized for this role, including more fine-grained effects and tracking of shared vars (read-only, write-only, channel writes, read-write vars). Something based loosely on Kahn Process Networks or Lafont Interaction Networks might be a good start.

## Misc Thoughts

### Console Applications

See [Glas command line](GlasCLI.md).

### Lightweight GUI

Console apps will support GUI indirectly via file and network APIs:

* networked GUI, e.g. web-apps, X, RDP, dbus (configured for TCP) 
* textual UI (TUI) with graphics extensions, e.g. kitty or sixel

These mechanisms benefit from buffering of IO, which conveniently aligns with transaction loops. Support for native GUI is non-trivial and low priority, but may eventually be supported via extended effects API.

### Notebook Applications

I like the idea of building notebook-style applications with live coding. But I'm uncertain how to best integrate everything. 

### Web Applications

A promising target for glas is web applications - compiling applications to JavaScript with read-write effects based on the Document Object Model, Web Sockets (or XMLHttpRequest), and Local Storage. Transaction loops are a reasonable fit for web apps, assuming something like React for rendering updated trees between transactions.

### Reactive Dataflow Networks

An intriguing option is to communicate using only ephemeral connections. Connections and delegated authority are visible, revocable, reactive to changes in code, configuration, or security policy. This is a convenient guarantee for live coding, debugging, extensibility, and open systems.

A viable API:

* **d:read:(from:Port, mode:(list|fork))** - read a set of values currently available on a dataflow port. Behavior depends on mode:
  * *list* - returned set represented as a list with arbitrary but stable order.
  * *fork* - same behavior as reading the list then immediately forking on the list; easier to stabilize compared to performing these operations separately.
* **d:write:(to:Port, data:Value)** - add data to a dataflow port. Writes within a transaction or between concurrent transactions are monotonic, idempotent, and commutative. Concurrent data is read as a set. Data implicitly expires from the set if not continuously written. Unstable data might be missed by a reader.
* **d:wire:(with:Port, and:Port)** - When two ports are wired, data that can be read from each port is temporarily written to the other. Applies transitively to hierarchical ports. Like writes, wires expire unless continuously maintained.

Ports are lists to abstract over hierarchical multiplexing. The ports used by a process should be documented. For example, a simple request-response protocol might involve writing `query:"GLAS_HOME"` to port `[env]` then reading responses from port `[env, val:"GLAS_HOME"]`. In this case, a process might describe the 'env' port as providing access to system environment variables. An efficient implementation requires abstracting over the expiration and regeneration of connections, and optimizing stable routes through wires. 

Many processes will use a standard pair of loopback ports 'lo' and 'li', applied hierarchically (such that stable writes to `[lo, foo]` are eventually read on `[li, foo]` and vice versa). This enables hierarchical process networks to delegate implementation and optimization of reactive dataflow to runtime or compiler.

A weakness of this model is that it can be difficult to predict or control which intermediate values are observed by external processes in context of unstable computations. This can be mitigated by stabilizing communication with application state, e.g. maintaining output until acknowledgement is received or timeout. 

### Synchronous Remote Procedure Calls? Reject.

Supporting synchronous remote procedure calls, i.e. within a transaction, is technically feasible but I'm not convinced it's a good idea. Doing so complicates the application model (to allow for reentrant calls), resists local reasoning and optimizations, and hinders integration with non-transactional systems. At least for now, I would suggest that distributed transaction be explicitly modeled between applications as needed.

### FFI

Direct support for FFI is a bad fit with transactions and effects handlers. But it seems feasible to include DLLs and headers as modules for use in an accelerated VM. Also, we could provide an API for evaluating a script between transactions after commit, perhaps prohibiting long-running scripts with loops to ensure this fits with my vision for live coding and extension. 

### Robust References

References are semantically awkward. I'd prefer to avoid them, but I haven't found a great means to do so while still integrating conveniently with modern operating systems (file handles, TCP sockets, etc.). I've considered explicit, local allocation of references, e.g. 'open (file) as (ref)'. This avoids reference abstraction and clarifies scope, but I found it inconvenient and inefficient in practice. 

For now, I'll stick to the convention where the environment allocates and returns references. Where appropriate, we can arrange for unique, unforgeable references, e.g. including an HMAC as part of each reference would ensure references are robust even when round-tripped through networks or databases.

A related issue: precise, automatic garbage collection is hindered if references are normal, serializable values. The glas program model will not have any built-in support for GC. However, it is feasible to abstract references and support GC in a higher program layer (that might compile to a glas program, or be interpreted via accelerator). 
