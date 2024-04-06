# Glas Applications

## Applications as Transactional Objects

A glas application is expressed as an abstract class or mixin. The application declares abstract methods to be provided by the runtime and implements concrete methods for system integration. 

Application methods are always called in context of an atomic, isolated transaction. Transactions simplify reasoning about progress, failure, interruption, live coding, and open systems. Consistency can be weakly enforced by defining *transaction invariant assertions* that are checked before commit. In general, transactions may be distributed, involving method calls across multiple applications, e.g. via RPC.

Instead of a 'main' procedure, an application typically defines 'start', 'step', and 'stop' methods. After start, 'step' is called repeatedly in separate transactions. With a few optimizations, this can express reactive and concurrent systems (see *Transaction Loops*). An application may provide interfaces to handle HTTP or RPC requests between steps. 

I propose to centralize runtime provided interfaces under hierarchical namespace 'sys'. This resists name conflicts and simplifies routing, sharing, and sandboxing access to effects in case of hierarchical composition. However, not all methods under 'sys' need to be runtime provided, e.g. defaults are permitted, type annotations might be checked by the runtime, and there may be special cases where the system wraps a user-provided method.

## Transaction Loops

In the transaction loop application model, systems are modeled by an open set of atomic, isolated, repeating transactions operating in an environment. This has many nice systemic properties, contingent on a sufficiently powerful optimizer:

* *Parallelism.* Multiple transactions can be opportunistically evaluated in parallel. A subset of transactions (those with no read-write conflicts) can be committed in parallel. In context of repeating transactions, the system can heuristically schedule transactions to avoid conflicts. Programmers can also mitigate conflict, e.g. by introducing intermediate queues. *Note:* Depending on language, there may be opportunities for parallelism within transactions, too.

* *Concurrency.* Intriguingly, repetition and replication are equivalent for isolated transactions. A transaction that makes a fair non-deterministic binary choice can be modeled as a pair of transactions that each deterministically select a different choice. If there is no conflict, they can both commit. Thus, leveraging a simple 'fork' effect, a single transaction loop can represent the entire open, dynamic set of transactions, each performing a different subtask.

* *Reactivity.* The system can recognize obviously unproductive transactions, such as failed transactions or those that repeatedly write the same values to variables. Instead of warming the CPU with unproductive recomputation, the system can arrange to wait for relevant changes before rescheduling.

* *Incremental Computing.* We don't need to recompute every transaction from the start. When a transaction starts with a stable prefix, the system can heuristically cache the partially evaluated transaction. This cached computation can serve as a checkpoint for further evaluation. This combines nicely with concurrency when a non-deterministic choice is stable.

* *Consistency.* It isn't difficult to express transactional invariants as assertions that must pass before a transaction is committed. Any transaction that would break these invariants can instead be aborted.

* *Live Coding.* Transaction loops provide a natural opportunity to update code between transactions. Further, they provide an opportunity to test proposed code changes within a transaction, without committing to anything. However, language and IDE support are necessary to provide a smooth transition, especially in case of schema update.

* *Persistent.* Application state can be transparently backed by a persistent filesystem or network resources, allowing for the application to maintain stable behavior over multiple power cycles. Of course, this isn't perfectly transparent; the application must ultimately be designed to handle long 'sleeps'. 

* *Distributed.* Intriguingly, running multiple instances of the same transaction loop is idempotent. If we run the same transaction loop on multiple remote nodes, they'll continue to provide degraded service when the network is partitioned, then recover resiliently when connectivity is restored. To support this, application state might be backed by a distributed database with a few partitioning tolerance features such as mirroring or buffering. Distributed transactions would be used minimally, only as needed.

* *Interactive.* An application can potentially participate in multiple transaction loops. The application's main loop is represented by a 'step' method, but other loops may be implicit via RPC or GUI bindings.  The different loops will interact asynchronously through application state.

* *Real-time.* A transaction can observe current time and abort if the transaction runs too early. In context of a repeating transaction, this effectively models 'wait' for a timestamp. A runtime could easily optimize this wait. Intriguingly, it is feasible to evaluate such transactions ahead of time and hold them ready to commit when the time comes. Full *hard* real-time would require some guarantees about scheduling and how much work is performed, but transaction loops support *soft* real-time very easily.

* *Search.* Operating within transactions makes it easier to explore multiple possibilities without committing to anything, e.g. adjusting tuning or calibration variables. This could feasibly be leveraged in combination with RPC to support distributed constraint systems.

There are also some known weaknesses for this model. The biggest one, IMO, is that many difficult optimizations are needed for transaction loops to perform competitively with conventional application models. Another is that conventional multi-step procedural operations will be awkwardly expressed as state machines to run over multiple transactions.

## Ephemeral Types

Abstract types are 'ephemeral' if restricted to the current transaction. To support my vision for glas systems, I propose that essentially all reference-like types should be ephemeral including database keys, first-class objects or functions, and nominative types declared in the application namespace.

Ephemeral types can simplify reasoning about live coding, authorization and revocation, discovery in open systems, interaction between systems with different life cycles or intermittent connectivity. The cost of ephemeral types can be mitigated via partial evaluation and incremental computing, e.g. in context of a *transaction loop*.

When serialized, ephemeral types could be concretely represented as indices into transaction-specific tables. They can be protected against forgery in open systems via cryptographically random allocation or HMAC.

## Transactional Remote Procedure Calls

A distributed transaction may involve remote procedure calls (RPC) to multiple applications. This works well with the *transaction loop* application model, i.e. an RPC call within a loop can still exhibit features such as incemental computing, reactivity, and concurrency via choice. 

An application will publish and discover RPC 'objects' through a configurable registry. A basic registry might be represented via remote service (URL and access tokens), distributed hash table, or shared database. But in practice, we'll want many fine-grained registries to support access control and integration between communities. A composite registry can filter and rewrite RPC objects as they are published to or discovered in component registries. This effectively represents a routing table. RPC objects may include ad-hoc 'tags' to guide routing.

To support interaction, the RPC system should support RPC objects as parameters or results. These objects will implicitly have *ephemeral type*, valid only within the current transaction.

*Note:* Unexpected reentrancy is a common source of bugs. Reentrancy is difficult to avoid, especially in context of open system RPC. But we could annotate assumptions to support static or dynamic checks, reject transactions that violate assumptions.

### Optimizations

Instead of always performing a remote call, we might distribute some code. This code can handle some calls locally and preprocess arguments to reduce serialization overheads. Most issues with code distribution can be mitigated by aborting transactions and maintaining the option for a remote call. 

A stable, read-only `unit -> Data` operation can be cached and mirrored on remote nodes to support low-latency access. Instead of a full distributed transaction, we can track coarse per-node version metadata for a lightweight consistency check. If this optimization is applied predictably, RPC can double as a publish-subscribe protocol.

The system can evaluate some procedural operations in parallel, but RPC resists static analysis. Transactions simplify this: if an ordering accident occurs, we can abort and retry. This requires tracking metadata, such as logical time, to efficiently detect ordering accidents and resist repeating the accident on retry. 

RPC can transparently integrate the glas stowage system (content-addressed references for large values) and [content delivery networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network). This can reduce network overheads in many cases, e.g. in context of persistent data structures, large videos, or distributing entire libraries of code.

Code distribution, caching, parallelism, and stowage should be guided by annotations. Use of CDNs should be configurable. 

*Note:* The RPC system and distributed transaction model must also be designed to also support *transaction loop* optimizations such as incremental computing, reactivity, and replication on 'fork'.

## Application State

Standard glas applications will provide access to a key-value database. Because state is a common requirement, glas languages should provide syntax to conveniently bind state to the external database. For example, we might declare variables within hierarchical application components. Depending on convention and configuration, different volumes of the database might be shared, persistent, or even distributed and mirrored, similar to memory-mapped IO. 

Specialized data types, such as counters, queues, and bags, can potentially enhance performance and partitioning tolerance. For example, we can still write to a queue even if we're in a separate partition from the reader, whereas in general writing to a remote variable should be blocked.

Database keys are abstract and ephemeral. The runtime provides initial key and hierarchical constructors. Persistent references such as URLs must be translated to ephemeral keys before use. This provides a robust opportunity to handle disruption, redirection, authorization, and revocation in context of live coding and open systems.

*Note:* This state model supports semi-transparent persistence and distribution. Applications designed for persistence or distribution can transparently run as short-lived or local. But any such design would requires careful attention to initialization, schema migration, and network partitioning tolerance.

### Indexed Collections   

Indexed collections can be supported at language layer with some dedicated syntax. 

Proposal:

        foo[index].method(args)

          # rewrites to something like

        let *foo[].index = foo[].select index in 
          foo[].inst.method(args)

Here `foo[]` is a namespace, `*foo[].index` identifies an implicit parameter, `foo[].select` provides an opportunity to verify or virtualize the given index, and `.inst` distinguishes instance-level methods from collection-level. Index type is not restricted to integers or plain old data, though we'll eventually need to construct database keys based on the index.

Implicit parameters are convenient for more than indexing. I assume implicit identifiers are abstract, ephemeral, and unforgeable similar to database keys. This simplifies reasoning about who can access an implicit and optimization of implicits in context of RPC.

### Cached Computations

Manual cache management is notoriously awkward and error-prone. It also introduces unnecessary risk of read-write conflicts in context of parallel evaluation of transactions. It should be avoided.

It is feasible to annotate stable, read-only computations (with plain old data parameters) for implicit caching. With careful design, computation can be structured such that recomputing cache after a change will reuse previous work. We might also ask the runtime to maintain the cache automatically, such that it's ready for use. 

Effective support for cached computations and incremental indexing would greatly simplify modeling a relational database or language-integrated query. It also has interesting interaction with RPC, allowing the RPC registry to double as a publish-subscribe service. 

### Ephemeral State

We can consider a few relevant life spans for state resources:

* shared, persistent state - available between process instances
* application process state - survives until OS process is killed
* ephemeral state - lasts for duration of the current transaction

A key-value database API can easily support these few lifespans. Ephemeral state can be logically modeled as persistent or process state that is implicitly reset between transactions. Ephemeral state gives us transaction-local variables, analogous to thread-local variables in other languages. 

Potential use cases for ephemeral state include modeling ad-hoc transaction invariants or integration with per-fork debug views. It is also feasible to model implicit parameters or results, but this is not recommended due to awkward interaction with reentrancy and exceptions.

### Shared State

Shared state is useful for asynchronous interactions between applications, e.g. we could write to a queue when the reader isn't even running. 

Many weaknesses of shared state are mitigated between transactions, specialized data types like queues, and structured data with content-addressed stowage so we can efficiently work with large values. Fine-grained access control might be supported via introducing access tokens into key construction. Regardless, this should be more robust and convenient than filesystem or shared memory.

In some cases, we could try shared state via an intermediate RPC service. 

## Application Provided Interfaces

### Basic Life Cycle

The runtime will first evaluate `start()` if defined. Then we repeatedly evaluate `step()` in a background loop while handling RPC or HTTP both within and between steps. A completed application process may halt voluntarily via the effects API, e.g. call `sys.app.halt()` then commit. 

The `stop()` method supports graceful shutdown. If defined, it would be called by the runtime upon receiving SIGTERM in Linux or WM_CLOSE in Windows. After stop, the application should only receive further `step()` calls until it voluntarily halts or is forcibly killed by an annoyed admin.

### Runtime Configuration

In context of [Glas CLI](GlasCLI.md), the glas profile centralizes most configuration information. Applications may define `config.class` to select a subconfiguration by name, i.e. returning a priority list of names. Where apps need more local control, we can introduce additional methods under `config`. I expect this interface will be very ad-hoc.

The runtime configuration can also list application interfaces the runtime is expected to recognize. This allows the runtime to raise an error if an application defines an interface that the runtime should recognize but does not.

### RPC Interfaces

I propose to directly represent RPC objects and interfaces in the application namespace, with implicit publish-subscribe of RPC through an externally configured registry. 

To publish a calculator service, we might define `rpc.mycalc`. To subscribe to calculator services, we might declare an indexed collection of abstract interfaces via `sys.rpc.calculator[]`, then call `sys.rpc.calculator[].keys` to return a list of valid indices. For symmetry, we might also support publishing collections and subscribing to singletons. In those cases, we would define `keys` on publish, and would raise error in case of ambiguity or absence of a singleton.

For an interface to match an object, the object must define every method declared in the interface unless the interface defines a default for that method. Further, the RPC system may recognize type annotations and automatically verify compatibility.

Published objects and subscribed interfaces may define and declare trivial 'tag' methods to support routing. A configured composite registry can route `tag.access.trusted` and `tag.access.public` to different sub-registries. We could filter on ad-hoc topics, e.g. `tag.topic.cat-pics`. Tags might include domain names or GUIDs to simplify singleton subscriptions, e.g. `tag.service.com.example.foo`. This set of tags is rewritten as the RPC object is routed.

### HTTP Interface

Applications can define an HTTP interface to support simple requests such as GET and POST. Each request is handled in a separate transaction. Multi-transaction operations can be awkwardly expressed using redirects. To support long polling, a pending HTTP request is implicitly retried until commit or timeout.

Proposed interface:

        # abstract Request and Response
        http : Request -> Response

        # accessors and constructors provided by runtime
        sys.http.request.parse : Binary -> Request | fail
        sys.http.request.text : Request -> Binary
        sys.http.request.method : Request -> Symbol # (:GET, :POST, etc.)
        sys.http.request.path : Request -> List of Text
        ...
        sys.http.response.parse : Binary -> Response | fail
        sys.http.response.text : Response -> Binary
        sys.http.response.basic : (code:StatusCode, type:ContentType, body:Text) -> Response
        etc.

Abstraction of Request and Response is intended to reduce repetitive parsing, localize validation, simplify routing and pipelining, and enable automatic construction of some response headers such as 'Content-Length' or 'Vary'. The methods listed above are for illustrative purposes and might not make the final cut.

HTTP is easy to implement and immediately useful. It can be leveraged as the initial basis for GUI, and it offers more flexible user interaction than console IO. HTTP interfaces in hierarchical application components are also convenient for composition, debugging, and live coding.

*Note:* It is feasible to share a TCP port for both RPC and HTTP. It might be useful for the runtime to intercept an HTTP path (perhaps `/sys`) to simplify integration between transactions, RPC, and web-apps.

### Graphical User Interface? Defer.

To fully develop a [Glas GUI](GlasGUI.md), we will need a mature glas system that implements several transaction loop optimizations and RPC optimizations. Meanwhile, applications can provide GUI via the conventional HTTP stack (HTML, DOM, JS, CSS). 

GUI interfaces in hierarchical application components can be convenient for composition, debug views, and live coding. 

## Effects API

The application may declare some abstract methods to be provided by the runtime. To simplify hierarchical composition of applications, effects might be centralized under the 'io' namespace. If the runtime does not recognize a declared method, and that method is used within the app, the runtime should raise an error.

### State

Access a key-value database and some initial state elements.


### Concurrency

Repetition and replication are equivalent for isolated transactions. If a transaction loop involves a fair non-deterministic choice, we can implement this by replicating the transaction to try every choice. Multiple choices can commit concurrently if there is no read-write conflict, otherwise we have a race condition. When a choice is part of the stable incremental computing prefix for a repeating transaction, these replicas also become stable, effectively representing a multi-threaded application.

* **fork(N)** - Response is non-deterministic fair choice of natural number between 1 and N. Does not fail. `fork(0)` would diverge.

Fair choice means that, given enough time, the runtime will eventually try any valid prefix of fork choices in the future (regardless of the past). This isn't a guarantee that races resolve fairly, only that fork choices aren't sticky. Race conditions should instead be resolved by application design, perhaps introducing some queues.

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

### Channels API

Channels are a great fit for transactions in a distributed system. Channels support multiple writers and a single reader in parallel. They are partitioning tolerant, allowing writes to buffer within each partition. But there is a problem: if we have a dedicated channels API, it's unclear who 'owns' a channel, who is responsible for security and cleanup, etc..

Instead, we should model channels within a database and make them accessible via RPC. For asynchronous interaction, we can model a channel-server app that can operate independently of the applications that read and write channels. This results in a more ad-hoc API but ensures responsibilities are clear.

Variations on the idea - dedicated APIs for mailboxes, tuple spaces, etc. - are rejected for similar reasons.
