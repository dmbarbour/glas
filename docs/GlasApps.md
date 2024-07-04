# Glas Applications

## Overview

A glas application is similar to an abstract OOP class. A [namespace](GlasNamespaces.md) may be expressed in terms of multiple inheritance. This namespace should implement interfaces recognized by the runtime, such as 'step' to model background processing. Access to effects, such as filesystem or network APIs, is represented by abstract methods to be provided by the runtime.

Application methods are transactional. Instead of a long-running 'main' loop, a transactional 'step' method is called repeatedly by the runtime. This allows interesting *transaction loop* features and optimizations between isolated transactions and non-deterministic choice. Other methods might handle HTTP requests, service remote procedure calls, or support a graphical user interface.

The transactional context does unfortunately complicate 'direct style' interaction with non-transactional systems. For example, programmers should not send a request via TCP and expect a response in the same transaction because the request is usually buffered until the transaction commits. This can be mitigated by language design: state machines can model multi-step processes.

Application state is logically mapped to a key-value database. Construction of keys is non-trivial, providing a basis for lifespan and access control. For example, keys constructed in terms of plain old data are persistent by default, but keys containing an abstract reference to the OS process or an open file can be safely represented in-memory. The front-end language should simplify efficiently mapping of application state to the database.

## Transaction Loops

In a transaction loop, we repeatedly evaluate the same atomic, isolated transactions. Many interesting optimizations and opportunities apply to this scenario based on the assumptions of atomicity, isolation, and predictable repetition.

* *Incremental.* Instead of recomputing everything from the start of the transaction, we can cache the transaction prefix and recompute from where inputs actually changed. Programmers can leverage this by designing most transactions to have a 'stable' prefix. This can generalize to caching computations that tend to vary independently.

* *Reactive.* In some cases, repeating a transaction is obviously unproductive. For example, if the transaction aborts, or if it repeatedly writes the same values to the same variables. In these cases, the system can simply wait for relevant changes in the environment before retrying. With some careful API design (and perhaps a few annotations), programmers can ensure wait conditions are obvious to the runtime without ever explicitly waiting.

* *Concurrent.* For isolated transactions, there is no observable distinction between repetition and replication. This becomes useful in context of incremental computing and fair non-deterministic choice. For example, a `fork(N)` effect might logically return a natural number less than N, but might be implemented by creating N copies of the transaction and evaluating them in parallel. Assuming those transactions do not have a read-write conflict, they may even commit in parallel. This effectively models multi-threaded systems without explicit threads.

* *Live Coding.* Transaction loops simplify the problem. The behavior of a repeating transaction can be easily modified between transactions. We can test changes and obtain feedback before committing to ensure a predictable transition. However, application state, schema update, and IDE integration also need attention. 

* *Distribution.* Distributed transactions are expensive in general, but there are ways to mitigate costs. For example, a queue can have multiple concurrent writers and a single reader without a read-write conflict between transactions. In case of network disruption, writes to a queue can be buffered locally then delivered when the systems reconnect. Read-mostly state can be locally cached. Programmers can design concurrent operations such that only a few are blocked by lack of network connectivity. 

* *Mirroring.* In a fully connected network, a distributed transaction can be processed anywhere, and location affects only performance. But when the network is fails, location suddenly matters. It is feasible to replicate a repeating transaction across nodes to improve network partitioning tolerance. Upon network disruption, each partition can maintain partial access to the distributed database and RPC registries. As the network is recovered, the transaction loop would regain full functionality.

* *Congestion Control.* If a repeating transaction writes to a queue that already contains many items, the runtime might reduce priority for evaluating that transaction again. Conversely, we could increase priority of transactions that we expect will write to a near-empty queue. A few heuristics like this can mitigate scenarios where work builds up or runs dry. This combines nicely with more explicit controls.

* *Real-time.* A repeating transaction can wait on a clock by becoming unproductive when the time is not right. This combines nicely with *reactivity* for scheduling future actions. Intriguingly, the system can precompute a pending transaction slightly ahead of time and hold it ready to commit at the earliest moment. This can form a simple and robust basis for real-time systems.

* *Auto-tune.* Even without transactions, a system control loop can read calibration parameters and heuristically adjust them based on feedback. However, transactions make this pattern much safer, allowing aggressive experimentation without committing to anything. This creates new opportunities for adaptive software.

* *Loop Fusion.* The system is always free to merge smaller transactions into a larger transaction. Potential motivations include debugging of multi-step interactions, validation of live coding or auto-tuning across multiple steps, and loop fusion optimizations.

Unfortunately, we need a mature optimizer and runtime system for these opportunities become acceptably efficient. This is an unavoidable hurdle for transaction loops. Meanwhile, the direct implementation is usable only for single-threaded event dispatch and state machines.

## Application Life Cycle

For a transaction loop application, the first effectful operation is `start()`. This will be retried indefinitely until it commits successfully. After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions. The runtime will also bind the application to external interfaces to receive RPC and HTTP requests, GUI connections, and so on.

In context of live coding or continuous deployment, we'll call the updated application's `switch()` method to help smoothly transition between versions of code at runtime. We'll continue running a prior version of `step()` and other methods until we switch successfully, at which point we'll transition atomically to the new code. Of course, if the prior version never successfully started, we may instead try the updated `start()`.

A transaction loop application may voluntarily halt by calling and committing `sys.halt()`. To support graceful shutdown, the glas runtime will bind OS events such as SIGTERM or WM_CLOSE to call a `stop()` method. However, if an application does not voluntarily halt, we'll simply leave it to more aggressive mechanisms such as SIGKILL, Task Manager, or cycling power.

*Note:* An application may define `settings.run-mode` to indicate alternative life cycles, such as staged applications, or a basic procedural application. However, transaction loops should be the default for glas systems.

## Data Lifespans, Live Coding, and Higher Order Programming

Consider a few broad lifespans for data:

* *persistent* - plain old data and possibly abstract *accelerated representations* (e.g. for sets, unlabeled graphs, and unboxed matrices). Persistent data can be stored persistently and shared between apps.
* *runtime* - includes abstract reference to open file handles, network sockets, or the OS process. Runtime data is stored in memory between transactions within a single OS process.  
* *ephemeral* - bound to the current transaction or perhaps to a specific frame on the call stack. Cannot be stored between transactions, yet ephemeral types may be *stable* for purpose of partial evaluation or caching.

In context of potential live coding, it is best to model method names as ephemeral types because we might switch the entire namespace between transactions. 

However, this implies we cannot pass first-class functions or objects between transactions. To mitigate this, glas front-end languages should support stable and efficient [defunctionalization](https://en.wikipedia.org/wiki/Defunctionalization) of multi-step procedures or processes, enabling evaluation of long-running tasks over multiple transactions. Further, it should be easy for programs to cache compilation of stable data into ephemeral functions.

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

Database keys are aligned to a hierarchical directory structure, and integrate some logic for persistence. A basic key might be constructed as `sys.db.dir(oldDir, "foo")`, while an in-memory key might use `sys.db.rtdir(fooDir)`. We might understand the latter as mapping to `"oldDir/foo/~"` where `~` maps to an in-memory overlay specific to the runtime instance, albeit with `~` abstracted.

Supported data types should at least include variables, queues, and bags. These are simple to implement and have convenient behavior in context of concurrency, distribution, and network partitioning:

* variables are read-write in one partition, cached read-only on others
* queues are read-write in one partition, buffered write-only on others
* bags are read-write on all partitions, rearranging items if connected

We might eventually pursue extensions for counters or [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) and other specialized types.

The API to access these can be specific to each type, e.g. `sys.db.var.get` and `sys.db.queue.write`. This allows suitable optimized methods for each use case. We could also allow the same key to be reused for a var, a queue, a bag, and a directory structure, implicitly selected based on the API used.

For my vision of glas systems, I think it's best if database keys are *ephemeral*. That is, the databases will contain data but not contain references to other parts of the database. This restriction results in simpler schema, more explicit relationships and indexing, greater consistency between internal and external APIs, and pushes most abstraction into the namespace layer.

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

## Mirroring for Performance and Partitioning Tolerance

See [Glas Mirrors](GlasMirror.md).

## Defunctionalized Procedures and Processes

Glas languages should provide a convenient syntax for partitioning large tasks into smaller transactional steps. Although this has significant overhead, it can be acceptable if each transactional step is doing enough work. Further, if we bind this state into the database (instead of constructing a large value), then it should be compatible with incremental computing optimizations.

Anyhow, the main benefit of this is that it allows a more 'direct style' for programming. For example, synchronous request-response must be divided between transactions, but we could feasibly *express* this as a procedure with a yield point, i.e. request-yield-response. This is very convenient when working with network APIs, filesystem APIs, and FFI. 

A relevant concern is that these steps should be 'stable' in context of live coding. Most changes to code should let the procedure continue in a reasonable way.

## Implicit Parameters and Algebraic Effects

Implicit parameters can be modeled as a special case of algebraic effects or vice versa (with function passing). I propose to tie implicits to the namespace. This resists accidental name capture or conflict, allows for private or capability secure implicits, and simplifies interaction between implicits and remote procedure calls.

In the initial glas language, function passing will likely be one way, i.e. a procedure can pass a method to a subprocedure but not vice versa. This is convenient for closures over stack variables and avoiding heap allocations. Algebraic effects can be understood as one-way function passing.


## HTTP Interface

Implementing the `http : Request -> Response` method is highly recommended for all applications. It provides a more extensible basis for user interaction with a running application than console IO, and it's easier to implement than GUI. 

However, even if an application ignores this opportunity, the `"/sys"` path is reserved by the runtime and routed to `sys.refl.http`. Through this standard interface, a runtime can provide access to logging, profiling, debug views, administrative controls, and other generic features.

The Request and Response types are binaries, including HTTP headers. However, they will often be accelerated to avoid redundant parsing and validation as we route the request or process the response.  

Each HTTP request is handled in a separate transaction. If the transaction aborts, it is implicitly retried until success or timeout. This provides a basis for long polling and reactive web applications. Eventually, we might develop custom HTTP headers to support multiple requests in one transaction for use with XMLHttpRequest.

The HTTP interface will usually share the same network ports as RPC requests and mirroring protocols. Authorization and authentication can be configured almost independently of the application. 

## Graphical User Interface? Defer.

My vision for [glas GUI](GlasGUI.md) is that users indirectly participate in transactions through reflection on a user agent. This allows observing failed transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing. 

Analogous to `sys.refl.http` a runtime might also define `sys.refl.gui` to provide generic GUI access to logs and debug views.

But I think it would be better to develop those transaction loop optimizations before implementing the GUI framework. There's a lot of feature interaction to consider with incremental computing and non-deterministic choice.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. The idea is that if the choice is part of the stable prefix for a transaction, we can replicate the transaction to take each choice and evaluate in parallel. Further, where choice isn't stable, we can effectively 'search' for a choice that results in successful commit.

Proposed APIs:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice isn't random. Rather, given sufficient opportunities, we'll eventually try everything. Naturally, fairness is weakened insofar as a committed choice constrains future opportunities. More generally, 'fair' choice will also be subject to external reflection and influence to support conflict avoidance in scheduling, replay for automated testing or debugging, or user attention in a GUI. The sequence of choices might be modeled as an implicit parameter to a transaction. 

*Note:* Reading from a 'bag' would implicitly involve `sys.fork()`. 

## Random Data

Stateful APIs for random data are awkward in context of transaction loops and incremental computing. However, we can sample a cryptographically random field. Indexing this field can conveniently be aligned to database keys to implicitly support lifespans and access control. Viable API:

* `sys.db.rand(Key, N)` - returns a natural number less than N. Diverges with a type error if N is not a positive integer. Always returns the same value for the same request.

If users need multiple random numbers, they can construct a database key per request. However, the implementation may be slow, perhaps involving [HMAC](https://en.wikipedia.org/wiki/HMAC). The alternative is to seed a more conventional PRNG from the cryptographically random field.

## Time

Transactions may observe time and abort if time isn't right. In context of a transaction loop, this can be leveraged to model timeouts or waiting on the clock. To support incremental computing, we add a variant API:

* `sys.time.now()` - Returns a TimeStamp representing a best estimate of current time as a rational number of seconds since Jan 1, 1601 UTC. This corresponds to Windows NT time, but doesn't specify precision. Multiple queries to the clock within a transaction must return the same value. 
* `sys.time.after(TimeStamp)` - Observes `sys.time.now() >= TimeStamp`. This is more convenient for incremental computing and reactivity, allowing the runtime to schedule waiting on the clock.

This API is useful for adding timestamps to received messages or waiting on the clock, but useless for profiling. *Profiling* will instead be supported via annotations.

## Debugging

In general, debugger integration should be supported using annotations rather than effects. That is, it should be easy to insert or remove and enable or disable debugging features without influencing formal behavior modulo reflection. Runtime reflection can potentially observe performance or debug outputs, and should be modeled effectfully through APIs in `sys.refl.*`. 

### Logging

In context of transaction loops, I find it useful to model logging as a form of debugger integration instead of an output stream. This enables observation of aborted transactions and presentation of logs as a time-varying structure, aligned with incremental computing and branching on `sys.fork`. 

Conventional logging can be improved considerably by introducing a `log(channel, message) { operation }` syntax (or `%log Channel Message Operation` AST). This allows the system to maintain a message as it changes over the course of an operation, or integrate log messages with a call stack. Further, the system can augment log messages with general metadata about the operation, such as time spent or memory allocations. 

The channel provides a handle for fine-grained configuration of logging behavior. This should include ability to disable logging statically or conditionally. To simplify configuration, channels should be compile time expressions (optionally including static parameters). Disabling logs should be transparent, thus message expressions must not modify state; alternatively, we might evaluate each message in an implicit transaction then abort. We can feasibly configure logging of method calls, i.e. each method name an implicit channel, parameters a message, body as operation.

Log messages should initially be accessible via `sys.refl.http`. Eventually, we might introduce an API under `sys.refl.log.*` for structured access and dynamic configuration. Also, it should be possible to configure a runtime to write logs to file or standard error.

### Profiling

Profiling can be modeled as a configuration option for logging, focused on performance metadata around 'operation' instead of the message. The message remains useful for indexing: we might aggregate stats on `(channel, message)` pairs, where channel is static and message is dynamic (such as identifying a specific object).

To simplify configuration and support reasonable defaults, we might use a distinct naming conventions for profiling channels. This might involve a simple variant header on the channel name. We could also use a separate syntax for profiling, to more clearly express intent.

### Testing

We can add assertions to our programs as annotations. Similar to logging and profiling, we might specify a channel so we can selectively enable and disable specific tests, or configure random testing. Something like: `(%assert Channel TestExpr MessageExpr)`. 

In context of transactions, I might also want the ability to express that some property should hold upon final commit. It might be feasible to express this as `(%require Channel MethodName DataExpr)` or similar, evaluating `MethodName(Data)` as an assertion just prior to commit. Multiple requests for the same test can be combined. However, it's difficult to efficiently include a message because we no longer have access to the original call stack at the time of test. We might instead insist that MethodName should be descriptive.

If assertions fail, the transaction can be aborted. But, in some debugging contexts, it might be useful to continue evaluating the transaction even after failure rather than aborting immediately.

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

* `sys.refl.bgeval(MethodName, Args)` - evaluate `MethodName(Args)` in a background transaction logically prior to the calling transaction, commit, then continue with a returned value. If the calling transaction is aborted, so is an incomplete background transaction. The argument and return value types must be plain old data. The method name is an abstract reference to the local namespace.

Normally, to simplify live coding, method names are ephemeral and cannot be transferred between transactions. However, this is a special case because we're transferring the method name *backwards* in time. There is no risk of a code switch logically occuring between the name being stored and applied.

Background eval is compatible with transaction loop optimizations. For example, the operation can be stable for incremental computing. If a background transaction may return a non-deterministic choice of values, this choice naturally propagates into the caller.

Aside from cacheable read-only operations, we can safely apply background eval for on-demand triggering of background tasks. For example, we can use background eval to process a queue of pending tasks. The on-demand nature allows for a bit more laziness, and may be more predictable than waiting on 'step'.

## Long Running Transactions

If we want to model 


## Module System Access? Partial. Defer. 

Users might want APIs for browsing and querying the module system, listing which global modules contributed to the current application, access to automated test results, loading modules, and so on.

We can immediately implement the `sys.load` API used by language modules. In this case, the implicit localization might be `{ "" => "distro." }`, i.e. assuming names come from the end user. A more complete API can deferred until we have a better understanding of use cases and what is easily implemented.

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

I'm uncertain whether I should try to provide TCP/UDP APIs directly, provide something closer to a sockets-based API (with bind, connect, etc.), or perhaps just provide XMLHttpRequest. 

