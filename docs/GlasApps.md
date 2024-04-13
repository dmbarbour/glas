# Glas Applications

## Applications as Transactional Objects

A glas application is expressed as an abstract class or mixin. The application declares abstract methods to be provided by the runtime and implements concrete methods for system integration. 

Application methods are always called in context of an atomic, isolated transaction. Transactions simplify reasoning about progress, failure, interruption, live coding, and open systems. Consistency can be weakly enforced by defining *transaction invariant assertions* that are checked before commit. In general, transactions may be distributed, involving method calls across multiple applications, e.g. via RPC.

Instead of a 'main' procedure, an application typically defines 'start', 'step', and 'stop' methods. After start, 'step' is called repeatedly in separate transactions. With a few optimizations, this can express reactive and concurrent systems (see *Transaction Loops*). An application may provide interfaces to handle HTTP or RPC requests between steps. 

I propose to centralize runtime provided interfaces under hierarchical namespace 'sys'. This resists name conflicts and simplifies routing, sharing, and sandboxing access to effects in case of hierarchical composition. However, not all methods under 'sys' need to be runtime provided, e.g. defaults are permitted, type annotations might be checked by the runtime, and there may be special cases where the system wraps a user-provided method.

## Transaction Loops

In the transaction loop application model, systems are modeled by an open set of atomic, isolated, repeating transactions operating in an environment. This has many nice systemic properties, contingent on applications are expressed and a sufficiently powerful optimizer:

* *Parallelism.* Multiple transactions can be opportunistically evaluated in parallel. A subset of transactions (those with no read-write conflicts) can be committed in parallel. In context of repeating transactions, the system can heuristically schedule transactions to avoid conflicts. Programmers can also mitigate conflict, e.g. by introducing intermediate queues. *Note:* Depending on language, there may be opportunities for parallelism within transactions, too.

* *Concurrency.* Repetition and replication are equivalent for isolated transactions. A transaction that makes a fair non-deterministic binary choice can be modeled as a pair of transactions that each deterministically select a different choice. These transactions can evaluate in parallel. 

* *Incremental Computing.* A repeated transaction will often repeat many computations. Instead of recomputing everything, we can cache a stable prefix and only recompute from the point where inputs change. Combined with *concurrency*, a stable non-deterministic choice effectively becomes a separate thead.

* *Reactivity.* The system can recognize obviously unproductive transactions, such as repeating an aborted transaction, a read-only transaction, or a transaction that will write the same values to the same variables. Instead of warming the CPU, the system can set triggers and wait for a relevant change. Reactive applications can be designed to leverage this optimization.

* *Live Coding.* Transaction loops provide a natural opportunity to update code between transactions. Further, they provide an opportunity to test proposed code changes within a transaction, without committing to anything. However, language and IDE support are necessary to provide a smooth transition, especially in case of schema update.

* *Persistent.* Application state can be transparently backed by a persistent filesystem or network resources, allowing for the application to maintain stable behavior over multiple power cycles. Of course, this isn't perfectly transparent; the application must ultimately be designed to handle long 'sleeps'. 

* *Consistent.* Transactions simplify the problem of enforcing consistency. Assumptions about application or system state can be checked before we commit to a transaction that might break them. Checks may be evaluated using a hierarchical transaction that we later abort. 

* *Distributed.* Repetition and replication are equivalent for isolated transactions. We can semi-transparently mirror a transaction loop application across multiple nodes, leveraging a distributed database and pubsub RPC. In case of network disruption, each mirror can provide degraded service using only resources within its own partition, then recover resiliently when connectivity is restored. 

* *Congestion Control.* A system can heuristically adjust frequency of transactions that read and write buffers (such as queues or bags) based on how full or empty are the buffers. This would help balance asynchronous producer-consumer interactions. I wouldn't recommend depending entirely on heuristics, but this can easily supplement more explicit controls.

* *Interaction.* Applications can interact within a distributed transaction, perhaps based on remote procedure calls (RPC). In addition to the 'step' method for background processing, RPC calls may also be repeated, extending the benefits of transaction loops across application boundaries.

* *Real-time.* A transaction can abort if it runs too early. A repeating transaction can abort repeatedly, waiting on the clock. This is easily optimized for incremental computing and reactivity. Further, the system can easily precompute the transaction for a future time, and hold it ready to commit. Assuming adequate control of scheduling and resource use, this can support real-time systems.

* *Auto-tune.* An application may read some parameters representing external tuning or calibration. A system could automatically adjust these parameters to improve a fitness heuristic. With transactions, it is feasible to observe how fitness is influenced by proposed parameters without committing to anything. In context of a transaction loop, tuning may easily continue as the application runs.

* *Loop Fusion.* In some cases it is possible to optimize a repeating sequence of transactions further than the individual transactions. With JIT, such optimizations potentially cross application boundaries. Can be guided by fused transaction invariants, e.g. 'fusion queues' must be empty only upon final commit. 

To fully leverage transaction loops requires many non-trivial optimizations. However, even without those optimizations, it is feasible to compile procedures or multi-process programs into state machines for evaluation across multiple transactional steps. This can be efficient with just a little caching and acceleration, and provides a robust semantics for how procedures interact in a concurrent system.

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

The glas runtime will provide a hierarchical key-value database for application state. A programming language for glas systems should make it easy to bind state to variables and hierarchical software components. Compared to conventional programming languages, there is more emphasis on stable, static allocation of state because it simplifies orthogonal persistence, schema updates, and live coding.

A runtime configuration will usually describe at least one persistent database that is shared between glas applications by a single user, or perhaps shared between a few mutually trusted users. A configured database could be distributed, with built-in support for mirroring. Additionally, the runtime provides an in-memory database whose lifespan is limited to the OS process. An application can choose between binding state to an in-memory or configured database.

Specialized data types, such as queues, and bags, can potentially enhance performance and partitioning tolerance. For example, we can still write to a queue even if we're in a separate partition from the reader, whereas in general writing to a remote variable should be blocked.

Database keys are abstract and ephemeral. Initial keys refer to the configured or in-memory databases. Hierarchical structure is based on providing a key constructor such as `path(dbKey, "foo")`. Persistent references such as URLs must be translated to ephemeral keys before use.

### Indexed Collections   

Indexed collections can be supported at language layer with some dedicated syntax. 

Proposal:

        foo[index].method(args)

          # rewrites to something like

        let *foo[].index = foo[].select index in 
          foo[].inst.method(args)

Here `foo[]` is a namespace, `*foo[].index` is an implicit parameter, `foo[].select` can verify or virtualize the provided index, and `.inst` distinguishes instance-level methods from collection-level. The index type is not restricted to integers or plain old data, though we'll eventually need to construct database keys based on the index. 

Implicit parameters are convenient for more than indexing. I assume implicit identifiers are abstract, ephemeral, and unforgeable similar to database keys. This simplifies reasoning about who can access an implicit and optimization of implicits in context of RPC. I might adjust syntax around implicit parameters to support abstraction. 

*Note:* Unfortunately, this design is incompatible with my earlier ideas about expressing transaction invariants as assertions in the namespace. I'll need to reconsider one side or the other.

### Cached Computations

Manual cache management is notoriously awkward and error-prone. It's too easy to miss a cache invalidation. In context of transaction loops, cache state can very easily result in read-write conflicts. 

Instead, we could annotate some read-only computations for implicit caching. This moves cache invalidation to the compiler and allows conflict detection to be cache aware. It also integrates nicely with RPC and code distribution, enabling RPC to double as publish-subscribe via 'publishing' cached computations.

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

### Consistency Support

        built-in-test   : unit -> unit | fail

Applications can provide a simple built-in-test for self-diagnostic and consistency checks. These checks may implicitly be run before committing to any transaction that might cause a diagnostic to fail. Alternatively, depending on configuration, the check might run infrequently or on demand. 

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

