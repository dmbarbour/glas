# Glas Applications

## Overview

A glas application is similar to an abstract OOP class. A [namespace](GlasNamespaces.md) may be expressed in terms of multiple inheritance. This namespace should implement interfaces recognized by the runtime, such as 'step' to model background processing. Access to effects, such as filesystem or network APIs, is represented by abstract methods to be provided by the runtime.

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

The configured registry is generally a composite with varying trust levels. In addition to ad-hoc authorization and authentication methods at the registry level, each RPC object or subscribed interface may include tags for routing published RPC objects and filtering received RPC objects. For example, by defining faux method `tag.access.trusted` we could restrict publishing to a registry 'trusted' in the runtime configuration. Other useful tags might indicate topics or service names.

*Note:* The runtime must open a network port to receive RPC requests. Configuring this is a bit awkward because the port cannot be shared concurrently.

### Optimizations

When publishing an RPC object, we could also publish some code for each method to support fully or partially local evaluation and reduce network traffic. Conversely, when calling a remote method, the caller could include some code representing the next few steps in the continuation, which would support pipelining of multiple remote calls.

When an RPC method refers to a cacheable computation, we can potentially mirror that cache to support low-latency access between nodes. This allows RPC registry to fully serve the role as a publish-subscribe system.

Large values might be delivered via proxy [CDN](https://en.wikipedia.org/wiki/Content_delivery_network) instead of communicated directly, leveraging content-addressed references. This can reduce network burdens in context of persistent data structures, large videos, or libraries of code.

## Application State

The runtime implements a key-value database API with both shared persistent and private in-memory data. The persistent database is configured externally and may include distributed databases. The database should implicitly support transactions, accelerated representations, and content-addressed storage for large values.

Database keys are aligned to a hierarchical directory structure, and integrate some logic for persistence. A basic key might be constructed as `sys.db.dir(oldDir, "foo")`, while an in-memory key might use `sys.db.rtdir(fooDir)`. We might understand the latter as mapping to `"oldDir/foo/~"` where `~` maps to an in-memory overlay specific to the runtime instance, except that `~` is abstracted to control accidents.

Supported data types should at least include variables, queues, and bags. These are simple to implement and have convenient behavior in context of concurrency, distribution, and network partitioning:

* variables are read-write in one partition, cached read-only on others
* queues are read-write in one partition, buffered write-only on others
* bags are read-write on all partitions, rearranging items if connected

We might eventually pursue extensions for counters or [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) and other specialized types.

The API to access these can be specific to each type, e.g. `sys.db.var.get` and `sys.db.queue.write`. This allows suitable optimized methods for each use case. Essentially, every database key might be understood as mapping to one var, one queue, one bag, and any number of subdirectories. 

In theory, we could add some access control to the database keys, e.g. restrict the key to read-only access to a specific variable. But I don't have a strong motive for this. Namespaces provide adequate access control within the application, and database keys will likely be ephemeral or even be treated as functional arguments (cannot be returned, but can be constructed locally via macros).

Anyhow, the programming language should help automate the mapping of declared state to stable database keys and specific access methods. Key construction can be heavily optimized, e.g. a static in-memory key might reduce to a cached pointer under-the-hood.

### Indexed Collections

An application can dynamically map data to the key-value database. The challenge is to make this convenient and efficient. I propose syntax `foo[index].method(args)`, modeling homogeneous collections in the hierarchical application namespace. This should correspond to a singleton object except that 'index' is used in construction of database keys.

Under-the-hood, 'index' might be assigned to an *implicit parameter* (see below) that is then used in dynamic construction of keys. The syntax `foo[index].method(args)` might desugar to something like `with *foo[].cursor = foo[].select index do foo[].inst.method(args)`, where `foo[].cursor` is defined as an implicit parameter. Here `foo[].select` allows for ad-hoc processing of the given index (such as bounds checks) to occur only once.

To maximize performance, we might introduce a dedicated constructor for database keys that indirect through another variable, i.e. `sys.db.refdir(&foo[].cursor)`. This allows the reference node to be treated as a constant for purpose of partial evaluation.

### Cached Computations

Manual caching using application state is error prone, likely to interfere with conflict analysis, and incompatible with ephemeral types. Instead, caching should be guided by annotations and implemented by the compiler.

### Shared State

Applications that share a database may interact asynchronously through shared, persistent state. Many common issues with shared state are mitigated between transactions and integration with incremental computing. However, glas systems do not encourage sharing a database between multiple users. Often, a database is used only by a single application.

In practice, instead of directly using shared state, two applications may interact asynchronously through a shared, stateful service. This has most benefits of shared state and further allows the service to abstract over representation details. However, it does require effective authentication models for RPC registries. 

### Nominative Types

It is feasible to integrate names into abstract types. However, in context of live coding, the application namespace is ephemeral, thus should be restricted to ephemeral types. In general, we'll also want abstract types with a runtime or persistent lifespan. Database keys can serve as an alternative source of 'names' in this role. 

## Implicit Parameters and Algebraic Effects

Implicit parameters can be modeled as a special case of algebraic effects or vice versa (with function passing). I propose to tie implicits to the namespace. This resists accidental name capture or conflict, allows for private or capability secure implicits, and simplifies interaction between implicits and remote procedure calls.

In the initial glas language, function passing will likely be one way, i.e. a procedure can pass a method to a subprocedure but not vice versa. This is convenient for closures over stack variables and avoiding heap allocations. Algebraic effects can be understood as one-way function passing.

## Automatic Testing and Consistency

Methods defined under `test.*` are implicitly understood as test methods. If testing is not disabled, the runtime might automatically evaluate tests just before committing to each transaction, i.e. after start, each step, http events, and so on. Tests would be evaluated in a hierarchical transaction so most side-effects can be aborted. 

If tests fail, the transaction can be aborted. This allows tests to protect ad-hoc invariants of the application and let programmers control consistency.

Of course, tests inevitably incur performance overheads. A runtime can potentially apply incremental computing and reactivity features of transaction loops to minimize rework. Alternatively, configurations can reduce testing, trading confidence for performance. Even infrequent tests could help track system health.

## Primary Life Cycle

The runtime first calls `start()`, repeating only if it fails to commit, then repeatedly calls `step()` in separate transactions. This allows for some one-time initialization and the main application loop. To support graceful shutdown, the runtime may also call `stop()` to signal that the application should halt after an OS event such as SIGTERM or WM_CLOSE. 

An application may voluntarily halt by calling and committing `sys.halt()`. This asks the runtime to make the current transaction the final one.

## HTTP Interface

Applications may define a `http : Request -> Response` method. The toplevel 'http' method may implicitly be bound to the same network port used to receive RPC requests. The runtime may validate requests before the application sees them, and validate responses before passing them to the caller. An 'http' interface on subcomponents can used in routing, composition, or to provide a debug interface. 

To simplify integration, the Request and Response types are binaries per the HTTP specification, albeit without support for pipelining or chunking. However, for performance, users will usually process requests and incrementally construct responses through `sys.http.*` methods, leveraging *accelerated representations* under the hood. HTTP pipelining may be transparently handled by the runtime.

Initially, each HTTP request is evaluated in a separate transaction. If the HTTP request aborts, it is implicitly retried until it commits or a configurable timeout is reached. This supports long polling and leverages the incremental computing and reactivity of transaction loops. Eventually, we might develop custom HTTP headers to compose multiple requests into a larger transaction.

As a convention, applications might route `/sys` to a runtime provided `sys.refl.http`. This could provide access to logging, testing, profiling, debugging, and similar features via browser.

*Note:* The runtime can provide RPC and HTTP on the same network port, distinguishing based on request headers. 

## Graphical User Interface? Defer.

The big idea for [glas GUI](GlasGUI.md) is that users participate in transactions through reflection on a user agent. That is, users can see data and queries presented to the user agent, and adjust how the agent responds to queries on their behalf. This combines nicely with live coding, but in conventional cases the response to a query can be modeled as a variable bound to a toggle, text-box, or slider.

Analogous to `sys.refl.http` a runtime could define `sys.refl.gui` to provide a generic debug interface. The application 'gui' method could route some requests to this location based on navigation vars.

Anyhow, this will be difficult to implement efficiently before the glas system matures, and is adequately substituted by HTTP interface in the short term. So, I don't plan to develop GUI until later.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. The idea is that if the choice is part of the stable prefix for a transaction, we can replicate the transaction to take each choice and evaluate in parallel. Further, where choice isn't stable, we can effectively 'search' for a choice that results in successful commit.

Proposed API:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice isn't random. Rather, given sufficient opportunities, we'll eventually try everything. Naturally, fairness is weakened insofar as a committed choice constrains future opportunities. More generally, 'fair' choice will also be subject to external reflection and influence to support conflict avoidance in scheduling, replay for automated testing or debugging, or user attention in a GUI. The sequence of choices might be modeled as an implicit parameter to a transaction. 

*Note:* Reading from a 'bag' would implicitly involve `sys.fork()`. 

## Random Data

Conventional APIs for random data are awkward in context of *transaction loops*, overlapping with PRNG state or non-deterministic choice. But there is at least one simple API concept that works very well: sample a cryptographically random field. To simplify persistence, mirroring, and partitioning of the random field, it is convenient to align this field with the database. Proposed API:

* `sys.db.rand(Key, N)` - returns a natural number less than N (diverges as type error if N is not a positive natural number), cryptographically randomized with a uniform distribution across different `(Key, N)` requests. Repeated requests will return the same value.

This API is very easily implemented using [HMAC](https://en.wikipedia.org/wiki/HMAC). However, HMAC is relatively slow and inefficient. Where an application needs many random values, might be better to model a conventional PRNG and seed it from `sys.db.rand`.

*Note:* Users may also access random data from the OS or network. 

## Time

Transactions may observe time and abort if time isn't right. In context of a transaction loop, this can be leveraged to model timeouts or waiting on the clock. To support incremental computing, we add a variant API:

* `sys.time.now()` - Returns a TimeStamp representing a best estimate of current time as a number of 100 nanosecond intervals since Jan 1, 1601 UTC (aka Windows NT time format, albeit not limited to 64 bits). Multiple queries to the clock should return the same value within a transaction. A runtime may adjust for estimated time of commit.
* `sys.time.check(TimeStamp)` - Observes `sys.time.now() >= TimeStamp`. Use of 'check' should be preferred in context of incremental computing or reactivity because it makes it very easy for the runtime to set precise trigger events on the clock, and can also avoid read-write conflicts. It is feasible to compute the transaction slightly ahead of time and have it ready to commit at the indicated time.

In context of RPC, each process may have its own local estimate of current time, but ideally we'd use something like NTP or PTP to gradually synchronize clocks.

This API does not cover one common conventional use case for time APIs: profiling. Profiling is instead handled as a form of runtime reflection in context of transactions.

## Debugging

In general, debugger integration should be supported using annotations rather than effects. That is, it should be easy to insert or remove and enable or disable debugging features without influencing formal behavior modulo reflection. Runtime reflection can potentially observe performance or debug outputs, and should be modeled effectfully through APIs in `sys.refl.*`. 

### Logging

In context of transaction loops, I find it useful to model logging as a form of debugger integration instead of an output stream. This enables observation of aborted transactions and presentation of logs as a time-varying structure, aligned with incremental computing and branching on `sys.fork`. 

Conventional logging can be improved considerably by introducing a `log(channel, message) { operation }` syntax (or `%log Channel Message Operation` AST). This allows the system to maintain a message as it changes over the course of an operation, or integrate log messages with a call stack. Further, the system can augment log messages with general metadata about the operation, such as time spent or memory allocations. 

The channel provides a handle for fine-grained configuration of logging behavior. This should include ability to disable logging statically or conditionally. To simplify configuration, channels should be compile time expressions (optionally including static parameters). Disabling logs should be transparent, thus message expressions must not modify state; alternatively, we might evaluate each message in an implicit transaction then abort. We can feasibly configure logging of method calls, i.e. each method name an implicit channel, parameters a message, body as operation.

Log messages should initially be accessible via `sys.refl.http`. Eventually, we might introduce an API under `sys.refl.log.*` for structured access and dynamic configuration. Also, it should be possible to configure a runtime to write logs to file or standard error.

### Profiling

Profiling can be modeled as a configuration option for logging, focused on performance metadata around 'operation' instead of the message. The message remains useful for indexing: we might aggregate stats on `(channel, message)` pairs, where channel is static and message is dynamic.

To simplify configuration and support reasonable defaults, we might use a distinct naming conventions for profiling channels. This might involve a simple variant header on the channel name. We could also use a separate syntax for profiling, to more clearly express intent.

### Tracing? Tentative.

In some cases, it is useful to track dataflows through a system including across remote procedure calls and transactions. This can be partially supported by including provenance annotations within data representations. The [glas object](GlasObject.md) representation supports such annotations, for example. We will need something more to efficiently maintain provenance when processing data. I suspect this will need an ad-hoc solution for now.

### Mapping

For live coding, projectional editing, debugging, etc. we often want to map program behavior back to source texts. In context of staged metaprogramming, this might best be modeled as *Tracing* source texts all the way to compiled outputs. This implies provenance annotations are represented at the data layer, not the program layer. 

For performance, a late stage compiler might extract and preprocess annotations in order to more efficiently maintain provenance metadata (e.g. as if tracing an interpreter). But this should be understood as an optimization.

## Data Representation? Defer.

We can manually serialize data to and from binaries. However, in some cases we might prefer to preserve underlying data representations used by the runtime. This potentially allows for greater performance, but it requires reflection methods to observe runtime representations or interact with content addressed storage. Viable sketch:

* Interaction with content-addressed storage. This might be organized into 'storage sessions', where a session can serve as a local GC root for content-addressed data.
* Convert glas data to and from [glob](GlasObject.md) binary. This requires some interaction with content-addressed storage, e.g. taking a 'storage session' parameter.

The details need some work, but I think this is sufficient for many use-cases. It might be convenient to introduce a few additional methods for peeking at representation details without full serialization.

## Background Eval

It is inconvenient to require multiple transactions for heuristically 'safe' operations, such as reading a file or HTTP GET. In these cases, an escape hatch from the transaction system is convenient. Background eval is a viable escape hatch for this role. Proposed API:

* `sys.refl.bgeval(MethodRef, Args)` - evaluate `MethodRef(Args)` in a background transaction logically prior to the calling transaction, commit, then continue with a returned value. While waiting, if the calling transaction is aborted due to change in external conditions, the background transaction should also be aborted. To keep it simple, argument and return values are limited to plain old data. The method reference is an abstract name, subject to namespace-based access control. 

Background eval can be used within a transaction loop and is fully compatible with transaction loop optimizations including incremental computing, reactivity, and replication on non-deterministic choice. In the latter case, we can implicitly fork the caller to handle each return value. 

We can safely leverage background eval for cacheable read-only queries or to lazily process background tasks that would have otherwise been handled in a concurrent loop (e.g. via 'step'). There is some risk of thrashing if the background transaction has a read-write conflict with the calling transaction, but thrashing is easy to recognize and debug.

## Lazy and Future Eval? Manual!

Applications can model asynchronous operations by writing requests into state that will eventually be processed by background `step` or `sys.refl.bgeval`. Modeling this explicitly is convenient for understanding how asynchronous operations interact with orthogonal persistence, live coding, and interruption. In contrast, background eval avoids most of these concerns.

## Foreign Function Interface? Tentative.

A foreign function interface (FFI) is convenient for integration with existing systems. Of greatest interest is a C language FFI. But FFI can be non-trivial due to differences in data models, error handling, memory management, and so on. I haven't discovered any simple and effective means to reconcile a C FFI with hierarchical transactions or transaction loop optimizations (incremental computing, logical replication on fork, reactivity, etc.). 

The best solution I've found is to instead schedule non-transactional C operations to run between transactions. A viable API:

* `sys.ffi.cfunc(LibName, FunctionName, AdapterHint)` - returns abstract FnRef for a C function in a dynamically loaded library. The AdapterHint should minimally indicate argument and result types, but might further include calling conventions, parallelism options, cacheability or idempotence, and other features. Also, we won't necessarily support all C functions, but we should support common integer and binary array types.
* `sys.ffi.sched(FnRef, Args)` - schedules a call to a foreign function and returns an abstract OpRef. This will run shortly after the current transaction commits. For convenience, calls will usually run in a single thread in the same order they are scheduled unless configured otherwise via AdapterHint.
* `sys.ffi.result(OpRef)` - returns result of a completed operation, otherwise diverge. If called from the same transaction that scheduled the operation, divergence is guaranteed.

We can later introduce methods to observe detailed status of OpRef, or support limited scripts in place of FnRef. However, as a general rule, transaction loops should avoid scheduling long running scripts or operations because doing so interferes with live coding and orthogonal persistence.

*Note:* Where it's just a performance question, glas systems should favor *acceleration* over FFI if it's just for performance. Acceleration is a lot more friendly than FFI (safe, secure, portable, reproducible, scalable, etc.) but does not solve integration with the outside world.

## Configuration Variables? Tentative.

A runtime configuration might include ad-hoc variables. However, I'm not sure I want this to be a separate feature from environment variables. We could instead express configurations as applying a mixin to the environment, allowing ad-hoc extensions and overrides.

## Environment Variables

A simple API to access OS environment variables. Viable API:

* `sys.env.get(Name)` - return value associated with Name, or fails if Name is undefined. Names and returned values are simple texts (restricted binaries).
* `sys.env.list()` - return list of defined Names.
* `sys.env.args()` - return list of strings provided as arguments to the executable (usually via CLI).

Glas applications won't directly update environment variables. However, it is feasible to intercept a subcomponent's access to `sys.env.*`. 

## Console IO

Access to standard input and output streams is provided through `sys.tty.*`.

* `sys.tty.read(Count)` - return list of Count bytes from standard input; diverge if data insufficient. (*Note:* the input buffer will be treated as empty during 'start'.)
* `sys.tty.write(Binary)` - write given binary to standard output, buffered until commit. Returns unit.

To keep it simple, input echo and line buffering are disabled by default, and the application does not directly observe the input buffer (which could result in race conditions). Also, the application does not distinguish between a 'closed' input stream and the user simply not providing further input. Applications may leverage [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) for pseudo-graphics or device control. 

The runtime can potentially provide associated methods via `sys.refl.tty.*` for low level access and configuring terminal options. However, this is low priority.

The standard error stream is reserved by the glas runtime. Before an application starts, the runtime may report status through standard error including initialization errors and warnings. After the application halts, some final summary output may also be reported. The runtime may grant applications limited access to standard error via reflection API, but in practice most use cases should be based on separate log files or HTTP access (routing `/sys` to `sys.refl.http`).

## Filesystem

Interaction with the filesystem is awkward due to the non-transactional nature of filesystems and their different security model. However, this can be mitigated, and we can shift most 'operations' on files 

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


## Network APIs

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

## Live Coding

* reload config
* reload source
* SIGHUP?
* reflection on source code
