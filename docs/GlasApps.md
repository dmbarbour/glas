# Glas Applications

## Applications as Objects

In glas systems, applications will be expressed as a [hierarchical, stateful, extensible, partial namespace](ExtensibleNamespaces.md). This namespace effectively represents a static object that recursively contain more static objects, with a static graph of relationships between objects based on names. The stability of this object graph is an excellent fit for my vision of glas systems, which involves projectional editing, zoomable user interfaces, and live coding. 

The application is integrated with the environment based on names it declares or provides. The runtime provides effectful methods under a hierarchical 'io' namespace and allocates space for defined state resources. Conversely, the application defines a 'step' method for background processing, and might define 'gui' and 'rpc' methods to provide a user interface or handle remote procedure calls. The namespace can precisely control which names are exposed to subprograms, thus names serve as lightweight, static [object capabilities](https://en.wikipedia.org/wiki/Object-capability_model).

In practice, how gui or rpc are integrated should be configurable based on glas profile and environment variables. They may also be disabled.

Application methods can be evaluated in context of a transaction. A failed transaction is aborted, any effects canceled or undone. This influences design of the provided effects API. In context of RPC and reentrancy, a transaction may involve multiple calls to multiple apps. But smaller transactions generally be favored for performance. 

By default, the runtime repeatedly calls the 'step' method. This implements a *transaction loop* application, which has useful systemic qualities but requires a relatively sophisticated optimizer.

## Transaction Loops

In the transaction loop application model, systems are modeled by an open set of atomic, isolated, repeating transactions operating in an environment. This has many nice systemic properties:

* *Parallelism.* Multiple transactions can be opportunistically evaluated in parallel. A subset of those transactions with no read-write conflicts can be committed in parallel. The system can remember conflict history and heuristically schedule transactions that frequently conflict into different time slots. Programmers can easily mitigate conflict based on design. 

* *Concurrency.* For isolated transactions, repetition and replication are equivalent. A transaction that makes a non-deterministic choice can be modeled as a set of deterministic transactions that each make the same choice deterministically. Leveraging this, an application program can represent an entire system, with fine-grained transactions to handle subtasks.

* *Incremental Computing.* We don't need to recompute every transaction from the start. If a transaction starts with a stable prefix (i.e. read slow-change vars before fast-change vars), the system can heuristically checkpoint (cache the partially evaluated transaction). Future repetition can start from the checkpoint until it is invalidated. This combines with concurrency when the non-deterministic choice is stable.

* *Reactivity.* The system can recognize some obviously unproductive transactions, such as failed transactions or those that repeatedly write the same values to variables. In these cases, the system can deschedule these transactions until a relevant change occurs. 

* *Consistency.* Although transactions aren't inherently consistent, it is possible for applications to express transactional invariants, and for the system to enforce these invariants by aborting transactions that would break them.

* *Live Coding.* We can model a transaction as reading relevant code as it runs, allowing atomic updates of code and state between transactions. Transactions also make it easier to check for safety of proposed updates in a live system before committing the changes.

* *Distribution.* Distributed transactions are expensive, but can be mitigated. A runtime can distribute code to run parts of a transaction remotely. If a node is only observed within the stable prefix of a transaction, we could replace talking to that node per transaction with a cache consistency analysis between the remaining nodes. If transactions support *internal* concurrency, that stable prefix potentially includes observations on multiple nodes. Between these, an application might run transactions on remote nodes without talking to the origin, effectively representing an overlay network.

* *Partitioning Tolerance.* A transaction loop application can be installed on multiple nodes, configured to use the same state resources. While the system is fully connected, there is no difference between running on one node or multiple nodes because they'll all run the same transaction. During a break down in communications, each local instance of the app can continue to provide degraded service based on a subset of transactions that only require resources available on the local partition. To fully leverage this requires attention to organization of resource and may benefit from specialized resources models such as CRDTs.

* *Real-time Systems.* An atomic transaction cannot usefully 'sleep', but it can ask whether current time is below a given timestamp, then contingently abort. The runtime may then precompute the transaction just before the observed condition changes and have it ready to commit. This would be *soft* real-time by default, but could become hard real-time if we control relevant external conditions. Of course, for fine-grained timing, we can also commit a buffer of future events.

*Auto-tune and Search.* A reactive application could read tuning variables and have an associated fitness function. The system could automate tuning of those variables to improve fitness. Transactions make this easier because the runtime can systematically experiment with tuning parameters without committing to changes. This can potentially result in a more adaptive and reactive system while avoiding unnecessary state. In many cases, the fitness heuristic could be represented via annotation.

I have ideas on how to support most of these, but the gap between idea and implementation is intimidating.

## Static State Resources

The application namespace can declare state resources, such as variables or queues, that the runtime implements. Access to these resources is implicit based on access to the namespace. If programs express concurrent computations, they will need to resolve access to state resources, e.g. treating the runtime environment as an additional implicit parameter with temporal semantics. 

## Applications as Objects

* namespaces can define methods 
* namespaces may declare variables
* namespaces are hierarchical (static objects)
* anonymous or private names are supported
* lightweight per-object naming conventions for:
  * annotations - types, versions, authorship, etc.
  * assertions - static tests, transactional invariants
  * debugging - diagnostics and debug views
  * admin or user interface - notifications, dashboards, main views
* the runtime extends the 'io' namespace to support effects
  * required definitions must be declared, may have defaults
  * runtime may be guided by annotations within io (e.g. type, version)
* the application defines the 'main' namespace (static object)


First, an application's access to *effects* will often be integrated with the application via extending the namespace. This will be isolated to a hierarchical `io/` namespace. The runtime namespace should already represent the expected interface via 

 (e.g. by defining some annotations and declared methods)

. The namespace type should support *interfaces* that are already declared, which the runtime will integrate..

The namespace should already have declared the required interfaces.

In general, the 


Some applications will be recognized as *staged*. 

An essential feature of the glas system is that some applications may be *staged*, meaning that they read some command line arguments then return another.

 Application. 



This may be extended over time. Also, I'm still developing the type for namespaces. But the general idea is that specifying a module is enough to specify an application based on a few simple conventions.


Some applications will be *staged* meaning that instead of directly representing the final application, they will read some command line arguments then return another application.





An application program is expressed as an effectful procedure. This procedure receives a parameter representing a request. The primary transaction loop behavior is to repeatedly run this procedure in separate transactions with a 'step' requests. Steps generally return unit, but in general the type of the result may depend on the request. Aside from 'step' the runtime might use other methods for OS signals or self-diagnostics.

Applications can issue requests to other applications. These requests represent synchronous interactions between applications. It is possible for a request or result to include channels for ongoing interactions. Temporal semantics can coordinate multiple ongoing interactions with an application. 

To receive requests, an application must be discovered by the other application. This may involve registering with a shared service. Registration may include some metadata about what the application can do, and a callback context so the application knows where each call is coming from. The registration and discovery APIs should be designed with attention to compositionality, reactivity, and security.

Applications have effectful access to state resources that persist between transactions. State resources obviously include variables, but more specialized forms such as queues can be supported when there is a good reason such as performance. State cannot hold any type that is ephemeral within a transaction, but it isn't a big problem to store references to state resources. Applications can interact asynchronously (over multiple transactions) through shared state. Many issues with shared state are mitigated in context of transactions, temporal semantics, and [robust references](https://en.wikipedia.org/wiki/Object-capability_model).

*Note:* In context of ".g" modules, we run the 'main' method of the 'app' grammar.

## Effects API

The runtime will extend the application namespace with a procedural effects API and provide an implicit parameter representing interaction with the runtime environment. In context of a ".g" module, I propose use of 'rte/' hierarchical namespace and 'env.rte' as the implicit parameter. The runtime environment parameter is left abstract, manipulated only via the provided effects API.  

### Orthogonal Persistence


### Concurrency

Repetition and replication are equivalent for isolated transactions. If a transaction loop involves a fair non-deterministic choice, we can implement this by replicating the transaction to try every choice. Multiple choices can commit concurrently if there is no read-write conflict, otherwise we have a race condition. When a choice is part of the stable incremental computing prefix for a repeating transaction, these replicas also become stable, effectively representing a multi-threaded application.

* **fork(N)** - Response is non-deterministic fair choice of natural number between 1 and N. Does not fail, but `fork(0)` would diverge.

Fair choice means that, given enough time, the runtime will eventually try any valid prefix of fork choices in the future (regardless of the past). This isn't a guarantee that races resolve fairly, only that fork choices aren't sticky. Race conditions should instead be resolved by application design, perhaps introducing some queues.

### Search

In this case, the only effect is to ask the runtime for some specialized environment variables.  The runtime can heuristically adjust the variables over time and attempt to stabilize them. Potential API:

* **tune:bool:Var** - Response is a boolean represented as a one-bit word, `0b0` or `0b1`. 
* **tune:ratio:Var** - Response is a rational number between 0 and 1 (inclusive), represented as an `(A,B)` pair of natural numbers.

The application might provide a heuristic function to the runtime via annotations. The alternative is to add more effects for output a fitness score. 

*Note:* Search could be especially useful in context of staged applications, i.e. staged metaprogramming.

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

*Note:* It is feasible to push logging into the debug layer, e.g. with a 'trace' annotation on a log function. Ideally we'd abstract logging effects.

### Random Data

Non-deterministic 'fork' is not random because a scheduler can heuristically search for choices that lead to success. Similarly, 'random' is not necessarily non-deterministic. These two ideas must be distinguished. A viable API:

* **random:N** - response is cryptographically random binary of N bytes.

The implementation of random must backtrack on failure, such that we aren't implicitly searching for a successful string of random bits. It is possible to use a separate 'random' source per stable thread (i.e. per fork path) to further stabilize the system. Performance should be good, e.g. users are free to directly use random for simulating dice.

### Shared Database

Transaction loop applications constrain the effects API. Transactions easily support buffered interactions, such as reading and writing channels or mailboxes. However, they hinder synchronous request-response interactions with external services. If a remote service supports distributed transactions, or if we can reasonably assume a request is read-only and cacheable (like HTTP GET), then we could issue a request within the transaction. Otherwise, the request will be scheduled for delivery after we commit, and the response is deferred to a future transaction.


The filesystem API doesn't conveniently integrate with glas structured data, stowage, and transactions. We could instead have the runtime provide a database to the application. A simplistic API might start with this:

* **db:get:Key** - get value associated with Key.
* **db:set:(k:Key, v:Value)** - set value associated with Key. 
* **db:del:Key** - remove value from database.

We could incrementally introduce more APIs for performance reasons:

* **db:check:Key** - test if key is defined without reading it.
* **db:read:(k:Key, n:Count)** - Key must refer to a List value. Remove and return a list of up to Count available items.
* **db:write:(k:Key, v:List)** - Key must refer to a List value. Addend this list. 

We could further extend this to databuses, pubsub systems, mailboxes, tuple spaces via shared structure more sophisticated than lists, and perhaps with abstraction and structure on Keys. Our database could also support abstract or accelerated sets. 

Assuming Keys are abstracted, we can provide a few initial keys through the effectful environment and also provide APIs to derive keys, e.g. to restrict permissions, or build and discover associated structure similar to a filesystem directory. Examples of deriving Keys via API:

* *db:assoc:(k:Key, rel:Label)* - derive and return a key corresponding to following a labeled edge from a given key. Label might be restricted to data, but we could develop a variation that supports labeling with other Keys.
* *db:restrict:(k:Key, allow:Ops)* - return a derived key with restricted authority.

Abstraction can be enforced through type systems, address translation tables, or cryptography (e.g. HMAC). Intriguingly, we can also control abstract keys via logical expiration, such that fresh keys must be continuously derived. This would ensure visibility, revocability, and development of reactive systems.

I propose to start with a simple key-value database API, a few initial abstract keys (e.g. app home, user home, global shared), and a filesystem-based derivation rule for new keys. This would be enough for most apps while leaving room for performance and security extensions. 

### Standardized Mailbox or Databus or Tuple Space

Support for a lightweight mailbox style event systems would greatly simplify integration of an application with OS signals, HTTP services, and inter-app communication within glas systems. This could potentially build on the db API, or it could be a separate effect.

One challenge is that we need each subprogram to filter for relevant events. This might involve abstraction of keys that apply simple filters, rather than applying a filter to every access.

### Standardized Configurations

Instead of configuration files, it would be convenient to support configuration as a database feature. Perhaps one shared between application and runtime. This would allow configurations to be edited through runtime layer HTTP services, for example.


### Environment Variables

A simple API for access to OS environment variables, such as GLAS_PATH, or extended environment variables from the runtime.

* **env:get:Key** - read-only access to environment variables. 
* **env:list** - returns a list of defined environment variables.

Glas applications won't update environment variables. However, it is possible to simulate the environment for a subprogram via effects handlers. 

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
