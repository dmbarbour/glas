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

* *Live Coding.* The behavior of a repeating transaction can be safely modified between transactions. Application state, schema update, and IDE integration also need attention, which might be expressed by inserting a handoff transaction. 

* *Concurrent.* For isolated transactions, there is no observable distinction between repetition and replication. This becomes useful in context of incremental computing and fair non-deterministic choice. For example, a `fork(N)` effect might logically return a natural number less than N, but can be implemented by creating N copies of the transaction and evaluating them in parallel. Assuming those transactions do not have a read-write conflict, they can commit in parallel. This effectively models multi-threaded systems without reifying threads, which greatly simplifies interaction with live coding.

* *Distribution.* The properties of transaction loops also apply to repeating distributed transactions. However, distributed transactions are expensive! To minimize need for distributed transactions, we might locally cache read-mostly data and introduce specialized state types such as queues, bags, or CRDTs. Multiple transactions can write to the same queue without risk of read-write conflict, and a distributed queue could buffer writes locally.

* *Mirroring.* It is feasible to configure a distributed runtime where multiple remote nodes logically repeat the same transaction. While the network is connected, where the transaction runs influences only performance. There is no need to run a distributed transaction if another node will run the same transaction locally. Concurrent 'threads' have implicit affinity to nodes based locality. Some resources, such as state, may be cached or migrated to improve locality or load balancing. If the network is disrupted, some distributed transactions will fail, but each node can continue to provide degraded services locally and the system will recover resiliently by default.

* *Congestion Control.* If a repeating transaction writes to a queue that already contains many items, the runtime might reduce priority for evaluating that transaction again. Conversely, we could increase priority of transactions that we expect will write to a near-empty queue. A few heuristics like this can mitigate scenarios where work builds up or runs dry. This combines nicely with more explicit controls.

* *Real-time.* A repeating transaction can wait on a clock by becoming unproductive when the time is not right. This combines nicely with *reactivity* for scheduling future actions. Intriguingly, the system can precompute a pending transaction slightly ahead of time and hold it ready to commit at the earliest moment. This can form a simple and robust basis for real-time systems.

* *Auto-tune.* Even without transactions, a system control loop can read calibration parameters and heuristically adjust them based on feedback. However, transactions make this pattern much safer, allowing aggressive experimentation without committing to anything. This creates new opportunities for adaptive software.

* *Loop Fusion.* The system is always free to merge smaller transactions into a larger transaction. Potential motivations include debugging of multi-step interactions, validation of live coding or auto-tuning across multiple steps, and loop fusion optimizations.

Unfortunately, we need a mature optimizer and runtime system for these opportunities become acceptably efficient. This is an unavoidable hurdle for transaction loops. Meanwhile, the direct implementation is usable only for single-threaded event dispatch and state machines.

## Application Life Cycle

For a transaction loop application, the first effectful operation is `start()`. This will be retried indefinitely until it commits successfully or the application is killed externally. If undefined, 'start' implicitly succeeds.

After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions. The runtime may also call methods to handle RPC and HTTP requests, GUI connections, and so on based on the interfaces implemented by the application.

The application may voluntarily halt via `sys.halt()`, marking the final transaction. To support graceful shutdown, a `stop()` method will be called in case of OS events such as SIGTERM on Linux or WM_CLOSE in Windows, but this won't necessarily halt the application. 

An applications may voluntarily restart via `sys.restart()`. This should be consistent with halting the application then starting again in a new OS process. That is, the runtime is fully restarted: the runtime database is cleared, open files or network sockets are closed, etc.. Only persistent state bound to the external database is preserved.

### Live Coding Extensions

Upon noticing an update to a running application, the updated application is compiled then we evaluate `switch()` - the updated implementation thereof - repeatedly until it succeeds. If undefined, switch implicitly succeeds. In contrast to start, switch must assume there are open files and network connections, that the runtime database is already in use, etc..

Upon a successful switch, we'll begin using the new code's version of step, RPC, HTTP, and GUI interfaces, and so on. Until then, the runtime will continue to use the prior definition. If an application is edited many times, a runtime may directly switch to the latest version.

*Note:* Support for live coding is expensive and is subject to configuration. In general it could be disabled or configured to an external trigger (such as Linux SIGHUP or a named Windows event object).

## Application Settings and Configurations

In glas systems, configurations are generally centralized to [a ".gin" file](GlasInitLang.md) indicated by `GLAS_CONF`. This file can modularly compose configurations from multiple sources. 

To support application-specific settings, applications may define ad-hoc data under `settings.*`. These methods should be statically computable and might later be presented as implicit parameters when computing configuration settings such as quotas or ports. The configuration ultimately decides how settings influence runtime behavior, but in practice this is subject to de-facto standardization.

As a guiding principle, a glas configurations should be able to sandbox applications where there is no major detriment to performance. For example, it is feasible to rewrite file paths or choice of network ports, but not the actual contents of a file or network packet. Relatedly, many effects APIs might assume that resources are named and detailed in the configuration.

## Application Mirroring

I intend to model mirroring in terms of configuring a distributed runtime. See [Glas Mirrors](GlasMirror.md). I'm still thinking about exactly how to handle system clocks in context of mirroring. 

In general, system effects APIs may be mirror-aware, perhaps based on an implicit parameter `*sys.time.clock` or similar. Applications may be mirror aware based on reflection `sys.refl.*`. Perhaps it is sufficient to let users select and configure clocks by name.

## Data Lifespans and Abstraction

Abstract data isn't universally meaningful. There is a spatial-temporal 'scope' where meaning is accessible. Careful attention to this scope is needed, especially in context of live coding and orthogonal persistence.

Some observations:

* A 'database' lifespan would be convenient for channels, references, or data abstraction within a database. But it introduces significant risk of [path dependence](https://en.wikipedia.org/wiki/Path_dependence), where a past choice of abstractions constrains future update opportunities. I'd prefer to avoid this in glas systems, limiting the database to plain old data. We can still abstract based on controlling access to regions of the database.
* We might want a 'runtime' scope larger than a transaction for open file handles, network sockets, an 'in-memory' database, etc.. This shouldn't be a problem because we aren't live coding the runtime and there is no persisting of these types in any case. 
* There is also a use for 'ephemeral' data, scoped to the transaction or even to a subroutine. This might be useful for referencing objects on the data stack, for example. But it might be easier to avoid such references as first-class, instead focusing on implicit parameters and algebraic effects.
* In context of live coding, the application namespace should be treated as ephemeral because we may `switch()` between transactions.
* Ephemeral computations are still subject to partial evaluation and incremental computing. The only requirement here is stability. In contrast, allocation of runtime resources such as open files is generally not stable.

For glas systems, I want to avoid the 'database' lifespan and instead restrict databases to storing plain-old-data. I'm uncertain about 'ephemeral' types - I must consider how to robustly and efficiently implement APIs like database access without ephemeral 'keys' or other elements.

Due to runtime types without a corresponding 'database' lifespan, we cannot have fully orthogonal persistence, but semi-transparent persistence is feasible, i.e. software components that don't use runtime types may bind transparently to a persistent volume of the key-value database.

### Database API Without First-Class Keys?

I'd like to eliminate use of first-class 'ephemeral' references. I also want robust partitioning of the database. What can be done? Thoughts:

* implicit parameters or algebraic effects are awkward in this role. We want certain names in the namespace to bind to certain objects in the database, implicit parameters would lose this binding.
* we could feasibly model key construction as second-class, relying on macro-layer computations instead of return values, i.e. objects or functions are not first-class values but can be expressed and held on the stack. 

The latter option seems feasible, though we may need to treat database access as keywords instead of generic methods. That said, maybe this is a reasonable constraint.

## Remote Procedure Calls

Applications may be able to publish and subscribe to RPC 'objects' through a configurable registry. An application may send and receive multiple remote calls within a distributed transaction. The distributed transaction protocol should ideally support the *transaction loop* optimizations described earlier, such as incremental computing, reactivity, replication on non-deterministic choice, and loop fusion.

Publishing RPC objects might be expressed as defining a hierarchical component `rpc.foo.(Method*)`. Subscribing to an RPC interface might conversely be expressed as declaring `sys.rpc.bar.(AbstractMethod*)`.  In case of missing or ambiguous remote object, the transaction may simply diverge. But we could also support collections both directions, i.e. `rpc.foo[]` and `sys.rpc.bar[]`, with `sys.rpc.bar[].keys` listing available instances.

The configured registry is generally a composite with varying trust levels. In addition to ad-hoc authorization and authentication methods at the registry level, each RPC object or subscribed interface may include tags for routing published RPC objects and filtering received RPC objects. For example, by defining faux method `tag.access.trusted` we could restrict publishing to a registry 'trusted' in the runtime configuration. Other useful tags might indicate topics or service names.

*Note:* The runtime must open a network port to receive RPC requests. Configuring this is a bit awkward because the port cannot be shared concurrently.

### Optimizations

When publishing an RPC object, we could also publish some code for each method to support fully or partially local evaluation and reduce network traffic. Conversely, when calling a remote method, the caller could include some code representing the next few steps in the continuation, which would support pipelining of multiple remote calls.

When an RPC method refers to a cacheable computation, we can potentially mirror that cache to support low-latency access between nodes. This allows RPC registry to serve a role as a [publish-subscribe](https://en.wikipedia.org/wiki/Publish%E2%80%93subscribe_pattern) system, but where published objects aren't limited to plain old data.

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


## Defunctionalized Procedures and Processes

Glas languages should provide a convenient syntax for partitioning large tasks into smaller transactional steps. Although this has significant overhead, it can be acceptable if each transactional step is doing enough work. Further, if we bind this state into the database (instead of constructing a large value), then it should be compatible with incremental computing optimizations.

Anyhow, the main benefit of this is that it allows a more 'direct style' for programming. For example, synchronous request-response must be divided between transactions, but we could feasibly *express* this as a procedure with a yield point, i.e. request-yield-response. This is very convenient when working with network APIs, filesystem APIs, and FFI. 

A relevant concern is that these steps should be 'stable' in context of live coding. Most changes to code should let the procedure continue in a reasonable way.

## Implicit Parameters and Algebraic Effects

Implicit parameters can be modeled as a special case of algebraic effects. I intend to tie implicit parameters and algebraic effects to the namespace. This supports namespace-based access control, prevents name collisions, and supports static analysis and reasoning. 

In my vision for glas systems, algebraic effects and staged computing are favored over first-class functions or objects. This restricts the more dynamic design patterns around higher order programming, but support for maps and folds aren't a problem. This restriction has benefits for both reasoning and performance, e.g. no need to consider variable capture, and a compiler can allocate functions on the data stack.

*Note:* It should be possible to model `sys.*` methods as wrappers around algebraic effects, where the abstracted algebraic effect performs the actual interaction with the runtime. This might constrain some API designs.

## HTTP Interface

Implementing the `http : Request -> Response` method is highly recommended for all applications. It provides a more extensible basis for user interaction with a running application than console IO, and it's easier to implement than GUI. 

However, even if the application ignores this opportunity, the `"/sys"` path is reserved by the runtime and implicitly routed to `sys.refl.http`. Through this standard interface, a runtime can provide access to logging, profiling, debug views, administrative controls, and other generic features.

The Request and Response types are binaries, including HTTP headers. However, they will often be accelerated to avoid redundant parsing and validation as we route the request or process the response.  

Each HTTP request is handled in a separate transaction. If the transaction aborts, it is implicitly retried until success or timeout. This provides a basis for long polling and reactive web applications. Eventually, we might develop custom HTTP headers to support multiple requests in one transaction for use with XMLHttpRequest.

The HTTP interface will usually share the same network ports as RPC requests and mirroring protocols. Authorization and authentication can be configured almost independently of the application. 

## Graphical User Interface? Defer.

My vision for [glas GUI](GlasGUI.md) is that users indirectly participate in transactions through reflection on a user agent. This allows observing failed transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing. 

Analogous to `sys.refl.http` a runtime might also define `sys.refl.gui` to provide generic GUI access to logs and debug views.

But I think it would be better to develop those transaction loop optimizations before implementing the GUI framework. There's a lot of feature interaction to consider with incremental computing and non-deterministic choice.

## Non-Deterministic Choice for Concurrency and Search

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. If `sys.fork(N)` is part of the 'stable' prefix for incremental computing, it effectively selects a thread. We can optimize to evaluate multiple stable threads in parallel, and even commit them in parallel.  

Meanwhile, even where 'fork' is not part of the stable prefix, it can still be useful to model search. We would implicitly retry with different responses from 'fork', seeking one that leads to a committing transaction. 

However, fair choice isn't random. Never use `sys.fork()` to roll dice. It's perfectly legit for an implementation of fair choice to schedule threads or search in a predictable order.

## Random Data

Stateful APIs for random data are awkward in context of transaction loops and incremental computing. A good alternative is to sample a cryptographically random field. This could be implemented using HMAC or similar methods. To robustly partition the field of random numbers while respecting orthogonal persistence, we might align the field to the database.

## Time

Transactions may observe time and abort if time isn't right. In context of a transaction loop, this can be leveraged to model timeouts or waiting on the clock. To support incremental computing, we add a variant API:

* `sys.time.now()` - Returns a TimeStamp representing a best estimate of current time as a rational number of seconds since Jan 1, 1601 UTC. This corresponds to Windows NT time, but doesn't specify precision. Multiple queries to the clock within a transaction must return the same value. 
* `sys.time.after(TimeStamp)` - Observes `sys.time.now() >= TimeStamp`. This is more convenient for incremental computing and reactivity, allowing the runtime to schedule waiting on the clock.
* `sys.time.clock` - (potential) implicit clock variable, a reference to the configuration 

This API is useful for adding timestamps to received messages or waiting on the clock, but useless for profiling. *Profiling* will instead be supported via annotations.


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

A reflection API could provide a method to initiate a transaction, returning a linear reference that may survive multiple transactions. Further operations could ask the runtime to perform some operations within the transaction, perhaps returning some feedback immediately. Eventually, we could try to commit or abort the transaction. 

Of course, the transaction might also abort due to to external interference, e.g. a read-write conflict. So we might also need methods to check the status of the remote transaction.


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


## Debugging

In general, debugger integration should be supported using annotations rather than effects. That is, it should be easy to insert or remove and enable or disable debugging features without influencing formal behavior modulo reflection. Runtime reflection can potentially observe performance or debug outputs, and should be modeled effectfully through APIs in `sys.refl.*`. 

### Logging

In context of hierarchical transactions and incremental computing, the conventional model of logging as a stream of messages is very awkward. It's more useful to understand logging in terms of reflection and debugger integration.

        # a useful syntax for logging
        log(chan, message) { operation }

This model for logging allows us to track a time-varying 'message' as it changes over the course of 'operation', and we can statically configure logging per 'chan'. The 'chan' description must be static, the 'message' expression is implicitly evaluated in a hierarchical transaction. It may fail. Modulo reflection, the log expression is equivalent to 'operation'. 

An implementation to efficiently capture every change to a message is non-trivial. But users can feasibly configure logging per chan to trigger on operation boundaries, periodic, random, or perhaps only when extracting the call stack to debug the operation.

For extensibility, I propose a convention that most log messages are expressed as dictionaries, e.g. `(text:"Message", type:warn, ...)`. The compiler and runtime system can potentially add some metadata based on where the log message defined. To support structured data and progressive disclosure, it is feasible to log an `(ns:Namespace)` of definitions including 'http' or 'gui' interfaces. For larger log messages, we must rely on structure sharing for performance.

Recent log messages may be accessible to an application through `sys.refl.http` and perhaps via structured reflection methods `sys.refl.log.*`. There may also be some opportunities for dynamic configuration of logging. 

### Profiling

Profiling might be understood as a specialized form of logging.

        # a useful syntax for profiling
        prof(chan, dynId) { operation }

Here we have a static chan, and a dynamic identifier to aggregate performance statistics while performing the operation. Gathered statistics could include entry counts, time spent, allocations, etc.. 

*Aside:* Log channels might also be configured to capture these statistics. Use of 'log' vs 'prof' is mostly about capturing user intention and simplifying the default configuration.

### Testing

Aside from automated testing described in [the design doc](GlasDesign.md), we can add assertions to programs as annotations. Similar to logging and profiling, tests could be associated with static channels to support configuration (e.g. enable, disable, random testing), and it can be useful to support continuous testing over the course of an operation.

        # viable syntax
        test(chan, property, message) { operation }

In this case we might evaluate a property as pass/fail, and then evaluate the message expression only when the property fails. This might also be interpreted as a form of logging, except the default configuration would be to abort the current transaction on a failed test.

Other than inline testing, it might be feasible to express ad-hoc assumptions about final conditions just before commit. 

### Tracing? Tentative.

In some cases, it is useful to track dataflows through a system including across remote procedure calls and transactions. This can be partially supported by including provenance annotations within data representations. The [glas object](GlasObject.md) representation supports such annotations, for example. We will need something more to efficiently maintain provenance when processing data. I suspect this will need an ad-hoc solution for now.

### Mapping

For live coding, projectional editing, debugging, etc. we often want to map program behavior back to source texts. In context of staged metaprogramming, this might best be modeled as *Tracing* source texts all the way to compiled outputs. This implies provenance annotations are represented at the data layer, not the program layer. 

For performance, a late stage compiler might extract and preprocess annotations in order to more efficiently maintain provenance metadata (e.g. as if tracing an interpreter). But this should be understood as an optimization.

### Quotas

To simplify reasoning about performance, it's often useful to choke an application to use fewer resources than are available. This might be expressed as quotas, e.g. limiting how much CPU is used in a given task. It should be feasible to support quotas on both CPU use and memory use. Quotas can be managed in two layers: External quotas would be expressed in the configuration, perhaps adjusted by application settings. Internal quotas would be expressed as annotations within the program.

## Rejected Features

It is feasible to introduce some `sys.disable()` and `sys.enable()` operations, perhaps parameterized for specific events such as 'step' and 'rpc'. However, I'm not convinced this is a good idea. Runtime state is less visible to users than application state, and more likely to be forgotten. It's also imprecise. In comparison, users can easily add some application state and add some explicit conditions to specific 'step' threads or RPC calls.

It is feasible to introduce periodic operations that evaluate at some configurable frequency, perhaps a `status()` health check. However, I think it better to let users manage this more explicitly, using `sys.time` to control the frequency of some operations and using HTTP requests to get information about health. 

