# Glas Applications

## Applications as Objects

In glas systems, applications will be expressed as a [hierarchical namespace](ExtensibleNamespaces.md) that defines methods and declares state. This hierarchical, stateful namespace can effectively express a static composition of objects. Methods are evaluated within context of atomic, isolated transactions. 

The application will provide *interfaces* that are recognized by the runtime. For example, the application may provide methods to support a graphical user interface or HTTP CGI service integration. Whether the runtime immediately renders the GUI might be configurable via environment variables or glas profile. Background processing is supported by providing a 'step' method, which is repeatedly called by the runtime, forming a *transaction loop* with many nice properties. 

Conversely, the application namespace may declare and describe abstract methods representing an *effects API*. The runtime should recognize and provide these methods based on name and description. Descriptions may include type annotations and version numbers; the runtime should raise an error if these requirements cannot be met. To simplify reasoning about security and sandboxing, the runtime will only provide shallow interfaces, e.g. under `io.method`, while the application explicitly binds and propagates effectful resources into subcomponents.

## Transaction Loops

In the transaction loop application model, systems are modeled by an open set of atomic, isolated, repeating transactions operating in an environment. This has many nice systemic properties, contingent on a sufficiently powerful optimizer:

* *Parallelism.* Multiple transactions can be opportunistically evaluated in parallel. A subset of transactions (those with no read-write conflicts) can be committed in parallel. In context of repeating transactions, the system can heuristically schedule transactions to avoid conflicts. Programmers can also mitigate conflict, e.g. by introducing intermediate queues. *Note:* Parallelism is possible within transactions, too.

* *Concurrency.* Each repeating transaction in a system set can represent a different subtask. These subtasks may interact asynchronously, e.g. one transaction writes a queue that another later reads. Intriguingly, repetition and replication are equivalent for isolated transactions. A transaction making a fair non-deterministic choice can be modeled as a set of transactions that each deterministically select one of the options. Leveraging this, a single transaction can represent an entire system.

* *Reactivity.* The system can recognize obviously unproductive transactions, such as failed transactions or those that repeatedly write the same values to variables. Instead of warming the CPU with unproductive recomputation, the system can arrange to wait for relevant changes before rescheduling.

* *Incremental Computing.* We don't need to recompute every transaction from the start. When a transaction starts with a stable prefix, the system can heuristically cache the partially evaluated transaction. This cached computation can serve as a checkpoint for further evaluation. This combines nicely with concurrency when a non-deterministic choice is stable.

* *Consistency.* Although transactions aren't inherently consistent, it isn't difficult to express transactional invariants that must pass before the transaction is committed. Any transaction that would break these invariants can be aborted.

* *Live Coding.* Application state is held outside the loop. We can atomically update the transaction loop code and relevant state between transactions. Transactions make it feasible to check safety of proposed updates before committing. 

* *Distribution.* The system can support distributed transactions and mitigate their cost. High-level code can be safely copied to remote nodes for computing. When a node is observed only within the stable prefix of a transaction, we can potentially use a cache consistency analysis instead of including that node in the expensive distributed commit protocol. Between these, incremental transactions can evaluate on remote nodes without communicating with their origin (modulo an infrequent heartbeat), effectively forming a distributed overlay network.

* *Partitioning Tolerance.* A transaction loop application with distributed transactions can be installed on multiple nodes. When the system is fully connected, this has no additional effect because where the distributed transaction starts doesn't affect its behavior. When the system is partitioned, each application instance can continue to provide degraded service based on which resources are locally available on the partition. To fully leverage this requires deliberate design of the application such that concurrent subtasks only involve subsets of partitions. It also benefits from careful attention to resource models, e.g. favoring queues between partitions or CRDTs for data shared between partitions.

* *Real-time Systems.* Transactions may abort if the time isn't right. If this is expressed without directly observing time, the runtime can potentially wait for the clock before evaluating, and can also evaluate slightly ahead of time then hold the transaction ready to commit. This allows for relatively precise time control, though it's at best *soft* real-time unless relevant external conditions are also controlled.

* *Auto-tune and Search.* An application can declare tuning parameters and output a hueristic fitness metric. The system can adjust these tuning parameters to improve fitness. The transactional context makes it easier to experiment without committing to changes.

* *Loop fusion.* Multiple transaction steps can be merged into a larger transaction. Doing so can be useful for performance, e.g. allowing some partial evaluation optimizations. It can also be useful for testing or debugging, i.e. we can simulate outcome for a few system steps without committing to anything.

Transaction loops are very nice in theory, but the gap between idea and implementation is intimidating. Developing a 'sufficiently smart' optimizer, or a language that can simplify the relevant optimizations, will be the biggest challenge for transaction loop application model.

## Transactional Remote Procedure Calls

I propose remote procedure calls (RPC) within transactions as a primary basis for interaction between applications. Without RPC, applications can only interact asynchronously via standard mailbox per app or TCP sockets. With RPC, applications can model their own ad-hoc mailboxes by providing a suitable interface, and also can support many synchronous interactions. A transaction may involve multiple calls to multiple applications. 

Ideally, support for RPC should be consistent with *Applications as Objects*. An application might provide multiple variant RPC interfaces expressed as hierarchical component namespaces, perhaps indicated via naming convention (e.g. `rpc.instance.method`). State resources would implicitly publish associated accessor methods. Conversely, the application might declare RPC interfaces that should be provided by the runtime (perhaps as `io.rpc.api.method`). We shouldn't need first-class functions or objects for any of this.

There are known challenges for this integration. Let's consider them here.

In general, there will be multiple instances of RPC interfaces in the environment. Multiple apps may publish the same interface. A single app may masquerade as several apps. Of course, there could also be zero instances, and the set may vary over time due to the nature of open systems. In general, RPC methods require an extra parameter to indicate which instance is called. RPC interfaces may also need some standard methods to support system discovery.

In context of asynchronous interactions, we must reference remote resources over the course of several transactions. This requires a stable means of identifying the remote resources. An obvious solution is to use a stable identifier for remote instances. An intriguing alternative is to extend our RPC discovery APIs to support stable, asynchronous sessions similar to TCP connections. Advantages of sessions are that they can be cleanly 'broken' and can carry status information or ad-hoc metadata. 

There are security concerns that must be addressed. Security might be supported via a few standard methods on RPC interfaces indicating registries - i.e. where to publish, where to search, cryptographic access tokens, etc.. It is feasible to separate most of these details into runtime configuration (glas profile or environment vars) while instead indicating labeled groups within the apps.

## Ephemeral References

An *ephemeral reference* is valid only within the current transaction. For my vision of glas systems, I propose that our APIs should favor ephemeral references, especially for anything that directly references the application namespace, such as otherwise first-class functions or objects. 

Leveraging ephemeral references:

* Live coding is easier because we don't need to consider whether state is holding references to deprecated or deleted functions. This is especially relevant in context of RPC and open systems, where that state might be held by external apps.
* Users can broadly assume that ephemeral references are connected and authorized for duration of the transaction, whereas persistent references (such as URLs) require an explicit connection step, returning an ephemeral reference. This simplifies reasoning about connectivity and security, and provides a clear boundary for redirection or revocation.
* APIs can be designed to encourage or force applications to continuously discover available resources instead of one-off discovery. For example, return a list of ephemeral references instead of a list of URLs. Continuous discovery and integration of resources simplifies orthorgonal persistence, partitioning tolerance, and adaptivity of open systems.

The cost of repeatedly reconstructing the same ephemeral reference can be mitigated by incremental computing, i.e. keep this step in the stable prefix of a repeating transaction. The under-the-hood representation of ephemeral references should be compatible with incremental computing. One viable representation is indices into a transaction-specific table.

Anyhow, this is mostly an API design concern, albeit with a light touch of type system support. But it is related to many features I envision for glas systems.

## Concurrency within Transactions? Defer.

It is possible to express transactions in terms of actors model or another concurrency model. We mostly need a clear boundary where the transaction 'commits', e.g. after messages are processed and the system is settled, but this isn't a big problem. Support for concurrency allows for more flexible incremental computing (especially in context of distributed computation) and simplifies the runtime by moving most coordination to the program layer. 

But many concurrency models, including actors model, introduce pervasive arrival-order non-determinism, aka race conditions. Unfortunately, arrival order tends to be inconsistent across machines and configurations, resulting in awkward "works on my machine" bugs, heisenbugs, testing that misses entire ranges of valid behavior just because it's improbable on the test system. This is contrary my design goals for glas systems!

Deterministic (or confluent) concurrency models are a better fit for my vision. But most of these models seem awkward to integrate with RPC. For now, I propose to just use annotations to guide parallel evaluation of normal procedures, then we can later consider what would be necessary to parallelize a user-defined model of green threads.

## Mitigating Reentrancy

Reentrant calls are a common source of bugs, e.g. when a subprogram manipulates state that the caller is also manipulating. However, reentrancy is difficult to fully avoid in context of remote procedure calls and first-class objects.

What we can do is detect patterns where reentrancy bugs are likely. We could perform a static analysis and warn programmers when reentrancy problems are likely, e.g. if a subprogram writes state between a read and write by the caller. Further, we could logically lock resources against reentrancy, perhaps expressed via annotations, then abort transactions that would violate the lock.

Reentrancy can also be mitigated by many features that would support parallel or concurrent computation. We might choose to resolve reentrancy by allowing for a high level of concurrency, then solving this more generally at the concurrency layer.

## Regarding Location-Specific Resources

Some resources are location specific, such as filesystem access or opening a TCP listen port. A few resources are even process instance specific, such as process ID, command line arguments, or the OS environment. When I contemplate mirroring of apps for partitioning tolerance, it isn't entirely clear to me how to express access to 'local' resources on many different partitions.

To keep it simple, I propose to initially develop an effects API suitable for local applications, then later develop an effects API suitable for mirrored apps. We can develop adapters for expressing mirrored apps in terms of the local effects API. Eventually, if some API becomes a de-facto standard, we might directly support it from the glas runtime, recognizing which API to use based on annotations.

When we do mirror apps, declared state resources will tend to be 'owned' by one partition or another. In special cases, we might also allow for ownership to be partitioned dynamically based on indices. We might use annotations to guide ownership.

## Securing RPC?

Applications can certainly secure their own RPC methods, e.g. via access tokens or HMAC. To avoid authentication per operation, this might be separated into a method that performs authentication then returns ephemeral references representing fine-grained authority. 

Alternatively, with runtime support we could annotate RPC methods with security policy, or potentially organize methods as `rpc.group.method` such that each group may have its own security policy. Ideally, a lot of the security details (registry locations, access tokens, etc.) can be separated into the runtime configuration. Keeping some security details separate from the application is useful to control communication, preventing backdoor access via tokens embedded in the app.

## Transactional User Models?

We can render debug views of the runtime system, including aborted transactions. This includes opportunity to render debug views of a user agent, which is asked questions or to perform certain operations on behalf of the user. If a question cannot be answered based on what the user agent knows, or if the operation cannot be handled, it can be treated as a divergent computation and rendered as an aborted transaction. This allows the user to update the agent before the transaction can continue (in context of a transaction loop).

This is a viable basis for user interfaces that enables users to akwardly participate within transactions. Some 'questions' may be graphical. There is an opportunity to script answers to cover a range of similar questions. There is a vague similarity to immediate-mode GUI.

But I'm not convinced this is the right option for modeling user interaction in most cases.

## Application State

I propose to declare state in the namespace. This simplifies support for 'private' state, and simplifies the problem of how state is accessed by RPC callbacks. However, it does complicate how access to state is controlled in context of concurrent transactions.

organization of 'private' state, but other options do exist: we could represent state


In addition to simple get/put state variables, we could model tuple spaces or bags, queues, counters, and other types. It is feasible to support user-defined models, but it is difficult to prove properties such as confluence or idempotence. So, I expect we'll rely on several built-in models, at least initially.

Some useful state types:

* Vars. 
* Maps. The language could support a lightweight key-value database type.
* MVars. A variable that optionally contains a value, perhaps represented by a var containing an option type. This can be useful to guard against reentrancy or to support concurrent operations.
* Queues. Writers can write to one end of a queue, while readers read from the other end. A useful advantage is that parallel or concurrent writes can be deferred then merged later. In context of concurrent operations, we m
* Bags or Sets. Unordered 

## Ideas for Runtime Interfaces

### Background Processing

For background processing, we'll evaluate a 'step' method repeatedly in separate transactions. The step function can use fair non-deterministic choice to represent dynamic subtasks. 

### Command Line Interface

We could add a method, or a few, to support command line interfaces such as tab completion, standardized help, etc.. 

Also, we could potentially model command line processing as something closer to a 'constructor' method call before we begin running the app (via 'step') instead of something we access effectfully in the environment. This might be more convenient for many use cases, such as the ability to essentially change the arguments at runtime. But I'm uncertain about it.

### Configuration

Methods could include access to a default configuration with documentation, default values, etc.. in addition to methods to apply a  configuration change. Conversely, an application might access its configuration via standard effects API, similar to an environment variable instead of ad-hoc filesystem access.

Standard support for configuration could simplify development of apps, and reduce dependency on locality such as access to the local filesystem.

### Graphical User Interface

An application may define user interface methods that are parameterized by ephemeral reference to an abstract user model. The separate user model allows for multiple concurrent users and multiple views per user. 

The user model would provide information such as perspective, preferences, proof of authority, focus and pending user input, and per-user state. Operations on the user model may support directly rendering a view ([immediate mode](https://en.wikipedia.org/wiki/Immediate_mode_GUI)) or manipulating a graph of objects (such as [DOM](https://en.wikipedia.org/wiki/Document_Object_Model)) that will separately be rendered ([retained mode](https://en.wikipedia.org/wiki/Retained_mode)). Navigation may also be supported, e.g. in the form of redirection or iframes.

Aside from the main views, methods on the user interface might support zoomed-out views (for a zoomable user interface), dynamic icons, notifications, etc.. Intriguingly, it is feasible to leverage transactional GUI views to render without commit, in context of 'safe' read-only views or previews for the result of committing to some operation.

*Aside:* An administrative or debug view might provide access not only to the main GUI but also browsing the underlying application namespace and rendering a GUI for each component, and rendering application state resources.

### Remote Procedure Calls

Need to consider both which methods to provide, and how to access RPC. In some cases, we might also want something like callback parameters, maybe via ephemeral references or signed URLs.

Public methods defined under hierarchical namespace 'rpc' are implicitly registered for external access. This might include a few standard interfaces (such as SOAP or CORBA), but I'm likely to favor some specialized interfaces to work better with transactions and incremental computing. 

### HTTP

HTTP can serve an ad-hoc role as both RPC and GUI. We could model incoming HTTP requests as a form of RPC with some conventions and type restrictions. I need to think about a suitable API for this - preferably an API that accounts for proxies, and providing multiple apps through one website or even one webpage. Web sockets might also need some attention.

A good reflection API might also be useful for converting subprograms to WASM.

### Administrative Methods

We could include some methods or state resources to:

* receive OS signals or events
* gracefully halt the app
* orthogonal persistence - hibernate and recovery methods
* 

restart
* 
* represent progress


### Publish Subscribe

A step function can maintain data dependencies in the background. But it might be more convenient for composition and optimization to make data dependencies and published data directly visible in the API. The runtime could ask for current subscriptions, automatically signal updates, etc..

### Shared State

Applications could directly share some state resources, e.g. we might share resources that are in the RPC section by automatically exposing all methods to manipulate that state.

## Application State

Application state is modeled in terms of declaring special objects in the application namespace. 



## Effects API

The runtime will extend the application namespace with a procedural effects API and provide an implicit parameter representing interaction with the runtime environment. In context of a ".g" module, I propose use of 'rte/' hierarchical namespace and 'env.rte' as the implicit parameter. The runtime environment parameter is left abstract, manipulated only via the provided effects API.  


### Concurrency

Repetition and replication are equivalent for isolated transactions. If a transaction loop involves a fair non-deterministic choice, we can implement this by replicating the transaction to try every choice. Multiple choices can commit concurrently if there is no read-write conflict, otherwise we have a race condition. When a choice is part of the stable incremental computing prefix for a repeating transaction, these replicas also become stable, effectively representing a multi-threaded application.

* **fork(N)** - Response is non-deterministic fair choice of natural number between 1 and N. Does not fail, but `fork(0)` would diverge.

Fair choice means that, given enough time, the runtime will eventually try any valid prefix of fork choices in the future (regardless of the past). This isn't a guarantee that races resolve fairly, only that fork choices aren't sticky. Race conditions should instead be resolved by application design, perhaps introducing some queues.

### Time

Transactions we can constrain a transaction to commit before or after a specified time. A proposed effects API:

* **time.now** - Response is an estimated logical time of commit, as a TimeStamp value. 
* **time.check(TimeStamp)** - Response is boolean representing whether `(time.now >= TimeStamp)`.

Checking time is more *stable* than requesting a timestamp, and thus allows for waits. But observing `time.now` is can be useful outside the stable prefix of a transaction. TimeStamp might use the Windows NT format by default, i.e. an integer representing 100ns intervals since midnight, Jan 1, 1601 UTC.

### Search

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
