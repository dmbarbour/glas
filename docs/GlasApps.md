# Glas Applications

## Overview

A glas application is similar to an abstract OOP class. A [namespace](GlasProgNamespaces.md) may be expressed in terms of multiple inheritance. This namespace should implement interfaces recognized by the runtime, such as 'step' to model background processing. Access to effects, such as filesystem or network APIs, is represented by abstract methods to be provided by the runtime.

Application methods are transactional. Instead of a long-running 'main' loop, a transactional 'step' method is called repeatedly by the runtime. This allows interesting *transaction loop* features and optimizations between isolated transactions and non-deterministic choice. Other methods might handle HTTP requests, service remote procedure calls, or support a graphical user interface.

The transactional context does unfortunately complicate 'direct style' interaction with non-transactional systems. For example, programmers should not send a request via TCP and expect a response in the same transaction because the request is usually buffered until the transaction commits. This can be mitigated by language design: state machines can model multi-step processes.

Application state is logically mapped to a key-value database. Construction of keys is non-trivial, providing a basis for lifespan and access control. For example, keys constructed in terms of plain old data are persistent by default, but keys containing an abstract reference to the OS process or an open file can be safely represented in-memory. The front-end language should simplify efficiently mapping of application state to the database.

## Transaction Loops

When an atomic, isolated transaction is predictably repeated, we can apply many useful optimizations and support some interesting features.

* *Incremental.* Instead of recomputing everything from the start of the transaction, we can cache a 'stable prefix' and recompute from where inputs change. We would need to track inputs regardless for read-write conflict analysis.

* *Reactive.* No need to repeat an obviously unproductive transaction. A compiler could instead attach some hooks to wait for relevant changes before retrying. Obviously unproductive transactions include those that would abort or would repeatedly write the same data into the same variables.

* *Concurrent.* To isolated transactions there is no observable distinction between repetition and replication. If a transaction includes a non-deterministic choice, we can replicate the transaction and evaluate both choices in parallel. Assuming there is no read-write conflict, we can commit both. If the choice is within the stable prefix for incremental computing, each replica effectively become a separate thread.

* *Live Coding.* A repeating transaction is easily modified between transactions. Further, it is feasible to evaluate a proposed update in the live system and detect obvious transition errors before committing to anything.

* *Distribution.* Assume we replicate a repeating transaction across multiple nodes, leveraging a distributed database for application state. While the network is connected, this replication doesn't affect formal behavior. While the network is disrupted, the application can continue to provide degraded service in each partition containing a replica. The system can recover resiliently when connectivity is restored. 

* *Congestion Control.* The system can heuristically tune relative frequency of repeating transactions to control buffer sizes. For example, transactions that would write to a buffer can speed up when the buffer is running low and slow down when the buffer is running high, and vice versa for readers. This is easily combined with more explicit control.

* *Real-time.* A repeating transaction can wait on a clock by simply aborting when it notices the time is not right. This combines nicely with *reactivity* for scheduling future actions. Intriguingly, the system can precompute the transaction slightly ahead of time, hold it ready to commit, and block any transactions that would result in a read-write conflict. This can form a robust basis for real-time systems.

* *Auto-tune.* A system control loop may read some tuning parameters. An external agent might heuristically adjust tuning parameters based on feedback. A transaction loop makes this pattern more robust and flexible because we can potentially test a proposed adjustment without committing to it. Changes that would obviously lead to worse outcomes can be aborted.

* *Loop Fusion.* The system is free to merge smaller transactions into a larger transaction. This can be performed for performance reasons where the fused transaction improves stability or allows some new optimizations. It could also be used for debugging, e.g. to obtain a view immediately after specific transactions and control commit.

These optimizations are non-trivial and won't immediately be available for glas systems. Short term, we might be limited to single-threaded event loops or state machines. 

## Abstract Types and Lifespans

Consider a few broad lifespans for data:

* *persistent* - plain old data or abstract *accelerated representations* (e.g. for sets, graphs, or unboxed matrices). Persistent data can be stored persistently and shared between apps.
* *runtime* - includes abstract reference to open file handles, network sockets, or the OS process. Runtime data is stored in memory between transactions within a single OS process.  
* *ephemeral* - bound to the current transaction or even to a frame on the call stack. Although ephemeral types cannot be stored between transactions, they may be 'stable' in context of incremental computing or memoization. 

In glas systems, many abstract types are ephemeral including first-class functions or objects, nominative types, and database keys. This is intended to simplify live coding, remote procedure calls, and access control and revocation. Of course, programmers can always model scripts as persistent data.

When serialized (e.g. in context of remote procedure calls) abstract ephemeral and runtime types may be represented concretely as external reference indices into transaction-specific tables, and protected against forgery or reuse by including a transaction-local HMAC or access token.

*Aside:* Note that lifespan is independent of computation time. For example, an optimizer can propagate ephemeral constants at compile-time, caching the result.

## Transactional Remote Procedure Calls

Applications may be able to publish and subscribe to RPC 'objects' through a configurable registry. An application may send and receive multiple remote calls within a distributed transaction. The distributed transaction protocol should ideally support the *transaction loop* optimizations described earlier, such as incremental computing, reactivity, replication on non-deterministic choice, and loop fusion.

Publishing RPC objects might be expressed as defining a hierarchical component `rpc.foo.(Method*)`. Subscribing to an RPC interface might conversely be expressed as declaring `sys.rpc.bar.(AbstractMethod*)`.  In case of missing or ambiguous remote object, the transaction may simply diverge. But we could also support collections both directions, i.e. `rpc.foo[]` and `sys.rpc.bar[]`, with `sys.rpc.bar[].keys` listing available instances.

The configured registry is generally a composite with varying trust levels. Published RPC objects or subscribed interfaces will include tags for routing and filtering. For example, we might define `.tag.access.trusted` on a published RPC object to ensure it's only published to trusted registries, or add it to a subscribed RPC object to only search trusted registries. Tags can also identify specific services or topics. Tags might be represented by abstract methods.

### Optimizations

When publishing an RPC object, we could also publish some code for each method to support fully or partially local evaluation and reduce network traffic. Conversely, when calling a remote method, the caller could include some code representing the next few steps in the continuation, which would support pipelining of multiple remote calls.

When an RPC method refers to a cacheable computation, we can potentially mirror that cache to support low-latency access between nodes. This allows RPC registry to fully serve the role as a publish-subscribe system.

Large values might be delivered via proxy [CDN](https://en.wikipedia.org/wiki/Content_delivery_network) instead of communicated directly, leveraging content-addressed references. This can reduce network burdens in context of persistent data structures, large videos, or libraries of code.

## Application State

The runtime implements a key-value database API with both shared persistent and private in-memory data. The persistent database is configured externally and may include distributed databases. The database should implicitly support transactions, accelerated representations, and content-addressed storage for large values.

Database keys integrate logic for persistence and access control. A basic key might be constructed as `sys.db.key(oldKey, sys.db.dir("foo"))`, while an in-memory key might use `sys.db.key(fooKey, sys.db.rtdir("bar"))`. We can understand `sys.db.rtdir` as constructing an index scoped to the runtime OS process lifespan (see *abstract types and lifespans*). Other constructors could accept an open file reference, modeling a temporary region.

Regarding access control, it is feasible to model 'secure' directory structures involving HMAC bearer tokens or other cryptographic access tokens. Also, some keys may restrict read access or write access to data. But I don't intend to focus on this opportunity until much later.

Supported data types should include variables, queues, and bags. These are simple to implement and have convenient behavior in context of concurrency, distribution, and network partitioning:

* variables are read-write in one partition, cached read-only on others
* queues are read-write in one partition, buffered write-only on others
* bags are read-write on all partitions, rearranging items if connected

I am contemplating extensions for [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) and other specialized types.

Anyhow, the programming language should automate most mapping from declared data variables to database keys and data access methods. Key construction should be heavily optimized, e.g. a static in-memory key might reduce to a cached pointer under-the-hood.

### Indexed Collections

An application can dynamically map data to the key-value database. The challenge is to make this convenient and efficient. I propose syntax `foo[index].method(args)`, modeling homogeneous collections in the hierarchical application namespace. This should correspond to a singleton object except that 'index' is used in construction of database keys.

Under-the-hood, 'index' might be assigned to an *implicit parameter* (see below) that is then used in dynamic construction of keys. The syntax `foo[index].method(args)` might desugar to something like `with *foo[].cursor = foo[].select index do foo[].inst.method(args)`, where `foo[].cursor` is defined as an implicit parameter. Here `foo[].select` allows for ad-hoc processing of the given index (such as bounds checks) to occur only once.

To maximize performance, we might introduce a dedicated constructor for database keys that indirect through another variable, i.e. `sys.db.refdir(&foo[].cursor)`. This allows the reference node to be treated as a constant for purpose of partial evaluation.

### Cached Computations

Manual caching using application state is error prone, likely to interfere with conflict analysis, and incompatible with ephemeral types. Instead, caching should be guided by annotations and implemented by the compiler.

### Shared State

Applications may use shared, persistent state in the configured database for asynchronous interaction between applications. Many potential problems with shared state are mitigated by transactions, incremental computing, reactivity, and lifespan types. With just a little access control, queues or bags would model effective mailboxes.

We can feasibly extend the database to support a few more common yet simple patterns, such as databuses for many-to-many communications. However, insofar as we need to protect ad-hoc invariants, we should hide state behind an RPC API.

### Nominative Types

It is feasible to integrate names into abstract types. However, in context of live coding, the application namespace is ephemeral, and should be restricted to ephemeral types. In general, we'll also want abstract types with the runtime lifespan.

Database keys can serve as an alternative source of names. We can introduce an abstract dictionary type indexed by database keys. Intriguingly, this implicitly supports [weak references](https://en.wikipedia.org/wiki/Weak_reference), e.g. if a dbKey included an open file reference in the directory structure. Ephemeral dictionaries can also be supported, using names from the application namespace in the directory structure.

## Implicit Parameters and Algebraic Effects



## Automatic Testing and Consistency

Methods defined under `test.*` are implicitly understood as test methods. Taking advantage of the transactional context, a runtime will evaluate test methods to the point they would return, then abort to control side-effects. A test that would not return is a failed test.

Tests represent transactional invariants, propositions that should hold true. By default, a runtime should evaluate tests just before committing any transaction. If any test fails, the transaction is aborted to protect consistency. 

However, continuous testing can be very expensive. Costs can mitigated by incremental computing and reactivity, or controlled via configuration, annotation, quotas, and runtime reflection. Some tests might be configured to run infrequently rather than per transaction.

## Application Life Cycle

The runtime will call `start()` as the first effectful operation, retrying until it commits successfully. This supports initialization. Then it will call `step()` repeatedly in separate transactions, modeling the background loop. If undefined, start or step are treated as no-ops.

Between steps the application may handle HTTP requests, GUI interactions, OS signals, and RPC. This involves implementing runtime-recognized interfaces and is contingent on configuration.

The application may voluntarily halt by calling (and committing) `sys.halt()`. This asks the runtime to make the current transaction the final one. Otherwise, the application runs until killed by (or together with) the OS.

## Runtime Configuration

I'm still developing the [configuration model](GlasConfigLang.md). But we want the application to have an opportunity to contribute to some configuration decisions. The question is how this participation should be expressed.

Ideas:

* Application defines ad-hoc configuration variables `config.*`, perhaps limited to text. These are imported into the user configuration, e.g. by overriding the `app` component. The configuration language supports simple comparisons and compositions of texts.
* Application defines `config.class` as a list of strings, and we either select the first matching subconfiguration or mixin all those in the list that are defined. This is relatively coarse grained but doesn't require conditional expressions in the configuration language.

At the moment, I lean towards the mixin idea.


## HTTP Interface

Applications could define a `http : Request -> Response` method. When defined at the toplevel, the runtime might implicitly support HTTP requests on the same port configured for RPC. When implemented on application components, this method can serve as a flexible interface for interactive debug views. I essentially propose `http` instead of `toString()`. 

The 'Request' and 'Response' are abstract, perhaps via `sys.http.*` methods, allowing the runtime to parse and cache inputs, correctly compute HTTP headers (such as 'Content-Length' and 'Vary'), and guarantee a valid response in context of HTTP pipelining. If the transaction aborts, it will be retried several times until a configurable timeout; this can be leveraged for long polling. 

By convention, users might route path `/sys` to `sys.refl.http`, allowing the runtime to provide a built-in web-based debugger, or hooks for an external debugger.

## Graphical User Interface? Defer.

To fully develop a [Glas GUI](GlasGUI.md), we will need a mature glas system that implements several transaction loop optimizations and RPC optimizations. Short term, applications can provide GUI via HTTP.

## Effects API

The application may declare some abstract methods to be provided by the runtime. To simplify hierarchical composition of applications, effects might be centralized under the 'io' namespace. If the runtime does not recognize a declared method, and that method is used within the app, the runtime should raise an error.

### State

I propose a hierarchical key-value database with support for three data types: var, queue, and bag.

* *var* - Holds a single value. Default value is zero, also representing empty list or dict, represented by the single node binary tree. In case of network partitioning, one partition 'owns' the var and may read and write normally, while others may read the value most recently cached in their partition (with some checks for cache consistency). 
* *queue* - FIFO ordered reads and writes, allows for multiple writers and a single reader in parallel. In case of network partitioning, only one partition can 'read' the queue, but writes in other partitions can be buffered. Supports heuristic congestion control: the scheduler can reduce priority of transactions that write to a full queue.
* *bag* - aka multi-set, unordered reads and writes, allows for multiple writers and readers in parallel. In case of network partitioning, each partition may continue to read its own writes, and things can migrate after reconnect. Supports heuristic congestion control like queues. A compiler can potentially optimize pattern matching after read into search, allowing a bag to serve as a tuple space.

In some contexts, access to bags may have a 


Assuming optimization of pattern match after read, this can serve as a tuple space.







Efficient key construction needs some attention. In general, we'll have some static path fragments mixed with occasional dynamic elements to handle collections. If we have something like `x.y.z[index].a.b.c` then it would be ideal if both the `x.y.z[]` and `.a.b.c` can be partially evaluated. 

        key(key(key(var, "a"), "b"), "c") # doesn't partially evaluate easily
         # but we might be able to inline constructors and rewrite?


A key-value database with abstract, ephemeral keys constructed based on other keys and plain old data. Distinctions between persistent storage, process storage, and transaction-local storage.

We can consider extending the database with a few more types. Counters are potentially a good option. 


*Note:* After reading data, we'll often immediately test whether it matches some pattern before continuing. It is feasible to provide APIs that integrate matching, but that severely complicates the API. What I hope to do instead is optimize database access based on continuation passing style and peeking at the subsequent code.

### Fair Non-Deterministic Choice

Proposed API:

        sys.fork(N) # returns fair choice of natural number between 1 and N.

This is intended for use in context of a stable transaction loop prefix, where fair choice optimizes to concurrent transactions. If the transaction is unstable at point of request, this can be replaced by a fair search algorithm that seeks a result leading to successful commit. 

Note that 'fair' in this context means that, given unlimited opportunities, the system guarantees that you'll eventually attempt any particular sequence of fork choices. There is no implication of random or uniform choice.

*Aside:* Reading from a bag also supports fair choice.

### Time

Transactions we can constrain a transaction to commit before or after a specified time. A proposed effects API:

* **time.now** - Response is an estimated logical time of commit, as a TimeStamp value. 
* **time.check(TimeStamp)** - Response is boolean representing whether `(time.now >= TimeStamp)`.

Checking time is more *stable* than requesting a timestamp, and thus allows for waits. But observing `time.now` is can be useful outside the stable prefix of a transaction. TimeStamp might use the Windows NT format by default, i.e. an integer representing 100ns intervals since midnight, Jan 1, 1601 UTC.

### Search?

In this case, the only effect is to ask the runtime for some specialized environment variables.  The runtime can heuristically adjust the variables over time and attempt to stabilize them. Potential API:

* **tune:bool:Var** - Response is a boolean represented as a one-bit word, `0b0` or `0b1`. 
* **tune:ratio:Var** - Response is a rational number between 0 and 1 (inclusive), represented as an `(A,B)` pair of natural numbers.

The application might provide a heuristic function to the runtime via annotations. The alternative is to add more effects for output a fitness score. 

*Note:* Search could be especially useful in context of staged applications, i.e. staged metaprogramming.

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

### Application Integation

* `sys.app.args` - Access CLI arguments.
* `sys.app.halt` - 



### Configuration Variables

Instead of configuration files, it would be convenient to support configuration as a database feature. Perhaps one shared between application and runtime. This would allow configurations to be edited through runtime layer HTTP services, for example.


### Environment Variables

A simple API for access to OS environment variables, such as GLAS_PATH, or extended environment variables from the runtime.

* **env:get:Key** - read-only access to environment variables. 
* **env:list** - returns a list of defined environment variables.

Glas applications won't update environment variables. However, it is possible to simulate the environment for a subprogram via effects handlers. 

### Filesystem

Filesystems are ubiquitous, universally awkwardly, and usually do not support transactions. Safe filesystem operations will need to be partitioned across multiple transactions to represent points of concurrent interference. However, we can provide some 'unsafe' filesystem APIs that assume non-interference and are considerably more convenient. 

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

### Database Integration?

It might be worthwhile to explicitly support some external transactional databases. It would allow us to mitigate a lot of issues associated with filesystem operations. However, this is not a high priority, and might be achievable via *background eval*.

### Network APIs

Network APIs have some implicit buffering that aligns well with transactional operations. However, the common request-response pattern must be awkwardly separated into two transactions. 

* `sys.tcp.` - namespace for TCP operations.
  * `listener.` - namespace for 



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


### Reflection

Reflection can weaken atomicity or isolation of transactions, violate component privacy, and provide a vector for covert communication. It is easily abused. However, it can be useful to support debugging, profiling, runtime extensions, and other features. 

* reload config
* reload source
* 

#### Background Eval

A runtime can provide `sys.refl.bgeval(MethodName, Args)` to pause the calling transaction, evaluate `MethodName(Args)` in a separate transaction (logically prior to the calling transaction), then continue the calling transaction with the returned value. If the background operation fails, it is implicitly retried until it succeeds or the caller is aborted.

Use cases: 

* Cacheable queries, e.g. evaluate HTTP GET request within a transaction. Background evaluation allows you to pretend you performed the query shortly before the transaction and cached the result.
* Demand driven scheduling, i.e. perform background tasks lazily as needed. This should be for operations that might have otherwise run in a concurrent 'step' task.
* Debugging, i.e. you could forcibly output some results before the calling transaction aborts.

Background evaluation is very easily abused. It should be handled with care, similar to 'unsafePerformIO' in Haskell. 

Of course, this can go wrong in many ways. The requested operation must be safe so we can ignore any effects. The result must be cacheable to mitigate stability issues. Read-write conflicts between the background operation and the current transaction could abort both or result in system thrashing. But these issues can be mitigated through API design and user discipline. 

*Note:* The type for MethodName might be an abstract built-in, accessed by keyword.

#### Data Representation

The underlying representation for data is usually transparent. However, there are some cases where we'd want to make it more visible, such as manually tunneling glas data over TCP. Potential operations:

* Interaction with content-addressed storage. This might be organized into 'storage sessions', where a session can serve as a cache and GC root for content-addressed binaries.
* Convert glas data to and from [glob](GlasObject.md) binary. This requires some interaction with content-addressed storage, e.g. taking a 'storage session' parameter.

This would probably be sufficient for most use-cases, but we could add some mechanisms to peek at representation details or access representation-layer annotations without serializing the data.

#### Miscellaneous

* Access current time (non-transactional). Useful for profiling.
* Access ad-hoc statistics
  * CPU, memory, GC stats, transaction counts 
  * conflict counts, wasted CPU due to aborts
    * worst offenders for transaction conflicts 
  * database and stowage sizes
  * RPC counters and recent history 
* Peruse stable transactions, log outputs, runtime type errors
* Internal access to runtime system HTTP, `sys.refl.http` 
* Reload application from sources. Useful for live coding.
* Access unique app version identifier, secure hash of sources.
* Browse the application namespace. Call arbitrary methods.
* Browse application database. Modify state directly.
* Potential source mapping, map methods to relevant sources.

## Rejected Ideas

### Ephemeral State

We can model ephemeral state as a region of the database that is implicitly reset at the start of each transaction. Ephemeral state avoids read-write conflicts by effectively being write-only. Ephemeral state can be useful as an awkward implementation of implicit parameters, for access from debug views, and assertion of transaction invariants. 

This idea proved problematic for composition. In some cases, a composite application must manually perform resets of component state. It can be difficult to track exactly which components are reset. And we certainly lose most of the debugging and assertion benefits.

I instead favor implicit pass-by-ref parameters where possible, assuming language support. Where we do need ephemeral state, we can model resets explicitly, which avoids some composition issues.

