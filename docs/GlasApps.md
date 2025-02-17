# Glas Applications

## The Transaction Loop

A *transaction loop* is a very simple idea - a repeating transaction, atomic and isolated. A direct implementation is nothing special; at best, it makes a convenient event dispatch loop, friendly for live coding and orthogonal persistence. However, consider a few optimizations we can apply: 

* *incremental computing* - We don't always need to repeat the entire transaction from the start. Instead, roll-back and recompute based on changed inputs. If repeating the transaction is obviously unproductive, e.g. it aborts, then we don't need to repeat at all until conditions change. If productive, we still get a smaller, tighter repeating transaction that can be optimized with partial evaluation of the stable inputs.
* *clone on non-deterministic choice* - If the computation is stable up to the choice, cloning is subject to incremental computing. This provides a stateless basis for concurrency, a set of threads reactive to observed conditions. If the computation is unstable, then at most one will commit while the other aborts (due to read-write conflict), representing a race condition or search. Both possibilities are useful.
* *distributed runtimes* - A runtime can mirror a repeating transaction across multiple nodes then heuristically abort transactions best started on another node due to locality of resources. Under incremental computing and cloning on non-deterministic choice, this results in different transactions running on different nodes. In case of network partitioning, transactions in each partition may run independently insofar as we can guarantee serializability (e.g. by tracking 'ownership' of variables). An application can be architected such that *most* transactions fully evaluate on a single node and communicate asynchronously through runtime-supported queues, bags, or CRDTs. 

If implemented, the transaction loop covers many more use-cases - reactive systems, concurrency, distributed systems with graceful degradation and resilience. Applications must still be designed to fully leverage these features, this is simplified by a comprehensible core: users can easily understand or debug each transaction in isolation. Also, even with these optimizations, the transaction loop remains friendly for live coding and orthogonal persistence. 

Further, there are several lesser optimizations we might apply:

* *congestion control* - A runtime can heuristically favor repeating transactions that fill empty queues or empty full queues.
* *conflict avoidance* - A runtime can arrange for transactions that frequently conflict to evaluate in different time slots.
* *soft real-time* - A repeating transaction can 'wait' for a point in time by observing a clock then diverging. A runtime can precompute the transaction slightly ahead of time and have it ready to commit.
* *loop fusion* - A runtime can identify repeating transactions that compose nicely and create larger transactions, allowing for additional optimizations. 

These optimizations don't open new opportunities, but they can simplify life for the programmer.

*Note:* A conventional process or procedure can be modeled in terms of a repeating transaction that always writes state about the next step. Of course, there is a performance hit compared to directly running concurrent procedures.

## Transaction Loop Application Model

A transaction loop application might define several transactional methods:

* 'start' - Set initial state, perform initial checks. Retried until it commits once.
* 'step' - After a successful start, repeatedly run 'step' until it voluntarily halt or killed externally.
* 'http' - Handle HTTP requests between steps. Our initial basis for GUI and events.
* 'rpc' - Transactional inter-process communications. Multiple calls in one transaction. Callback via algebraic effects.
* 'gui' - Like an immediate-mode GUI. Reflective - renders without commit. See [Glas GUI](GlasGUI.md).
* 'switch' - Like 'start' except is first operation in new code after update. Old code runs until successful switch.
* 'settings' - (pure) influences runtime configuration when computing application-specific runtime options.

The transactions will interact with the runtime - and through it the underlying systems - with an algebraic effects API. Unfortunately, most external systems - filesystem, network, FFI, etc. - are not transactional. We resolve this by buffering operations to run between transactions. But there are a few exceptions: application state, remote procedure calls, and a convenient escape hatch for safe, cacheable operations like HTTP GET.

*Note:* I assume that 'http' and FFI will serve as our initial bases for GUI. The perspective of users participating in transactions through reflection on their own user-agents does not strike me as easy to implement. Need to consider the minimum viable product.

## Application State

### Scope

The runtime will distinguish two scopes for mutable state: shared and runtime. Shared state is usually stored in a database based on the user configuration. Guided by application settings, different applications will often bind separate volumes of shared state, only 'sharing' with future or concurrent instances of the application or associated tools. Runtime state has the lifespan of the runtime, typically an ephemeral OS process.

Shared state cannot hold runtime-scoped data such as open file handles, network sockets, or FFI futures. Thus, semi-transparent persistence is relatively easy: we can easily route shared state to the runtime, but not vice versa. Full orthogonal persistence is feasible only for a subset of applications that carefully abstract those runtime features.

### Model

The runtime will support the basic memory cell with 'get' and 'set' operations. However, in context of distributed computations and caching, we may wish to support a few other useful types. What are our options?

* **cell** - the basic get, set, swap (for linear types). Only one node can 'own' a cell for writing, but mirrored caching is possible for read-mostly cells.

* **queue** - a list cell accessed with 'write', 'read', and 'putback'. Serializable with one reader (read and putback) and multiple concurrent writers blindly adding to the other end. Transactions and network partitions can buffer writes locally, then merge them in any serializability-consistent order. In practice, we might want to include metadata clocks to merge writes from multiple network partitions.

* **bag** - a cell containing a multiset (represented as a list), accessed via 'write', 'read', and 'peek'. Serializable with any number of concurrent readers and writers. Each node can operate on a local slice of the bag, and freely shuffle elements between slices. Reads are non-deterministic but can be filtered (via read then abort), but read variants with runtime support for filtering can enhance performance and support heuristic routing of items to interested nodes.

* **CRDTs?** - [conflict-free replicated datatypes](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are designed for concurrent edits and partitioning tolerance, and can be adapted. But I don't know which ones I'd want as built-ins. For now, users might manually replicate CRDTs, maintaining a cell in each node with a bag of updates.

### Keys

State is accessed through algebraic effects. We'll present this a suitable API for a key-value database with abstract static keys. Construction of keys can follow a directory-like structure, and runtime-scoped keys can be transparently constructed from shared-scope keys (but not vice versa). This allows a program to control subprogram access to the database through controlling access to keys. It also supports encoding ad hoc metadata, such as caching hints into keys.

## HTTP Interface

If an application implements an `http : Request -> Response` interface, it can receive HTTP requests over the same channel as RPC requests and debugging. The Request type is abstracted, with a few methods to access URL, headers, body, and support routing.

Each 'http' request is handled in a separate transaction. If the transaction aborts, it is logically retried until it successfully produces a response or times out. In some cases, programmers might need a 303 response to ask the client to separately GET the asynchronous result.

Regardless of whether the application implements 'http', a debugger interface might be configured to intercept `"/sys"` requests, binding to 'sys.refl.http'. Authorization would also be configurable.

## Remote Procedure Calls

If an application implements `rpc : (MethodRef, UserArg) -> Result`, it may receive remote procedure calls (RPC). The UserArg and Result values are exchanged with the caller. The 'rpc' method may also interact with the caller through an algebraic effect, perhaps 'rpc.cb' for arbitrary callbacks. MethodRef is a stable routing parameter related to how RPC interfaces are configured and published. The runtime will map between local use of MethodRef and external use of GUIDs or URLs.

To configure RPC, the simplest solution is to declare a static API in application settings. The configuration can route this to the runtime, optionally applying a translation. A dynamic API is also feasible: settings specify a MethodRef to fetch more detailed APIs according to known protocols. This can be queried as a repeating transaction, expressing reactive APIs. Instead of publishing the same interface to everyone, an application may express multiple RPC 'objects' that each have some methods and some metadata for routing. This provides a basis for security: the configuration can route objects based on trust levels and roles declared in the metadata. Methods can potentially be renamed or wrapped by some registries.

A prospective caller will query the runtime for RPC objects with a specific interface, filtering on metadata. This query returns a stable set of abstract objects. The caller can peek at the full metadata, and may invoke methods on these objects. We can extend transaction loop optimizations and incremental computing to remote calls.

To enhance performance, we can support limited code distribution. The 'rpc' method can be written and annotated such that an optimizer will partially evaluate MethodRef and extract code fragments for further evaluation by the remote caller. From a caller, we might extract parts of the callback and continuation, suporting sequential calls without a round-trips. These optimizations can mitigate performance pressures, supporting simplified remote APIs.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) is that users participate in transactions indirectly through reflection on a user-agent. This allows observing aborted transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing, and also allows users to also interact with non-deterministic choice, e.g. display multiple outcomes then commit the preferred outcome and abort the rest.

As a minimum viable product, some sort of immediate-mode GUI that renders every 'gui' frame regardless of commit is viable. But I'd rather not develop a half-assed GUI model, so we might stick to HTTP and FFI for GUIs in the short term.

## Foreign Function Interface

Access to existing C or C++ APIs is convenient for integration. However, it is relatively awkward in context of transactions or memory safety. To mitigate this, I propose to run foreign functions in a background processes, i.e. with OS-enforced memory separation. The process maintains a tacit environment of variables and defined methods, and operations can use a lightweight scripting language to express loops or call multiple methods. If a process crashes, this can be indirectly observed. 

Viable effects API:

* `sys.ffi.open() : FFI` - returns a linear reference to a new or existing FFI process by name.
* `sys.ffi.close(FFI) : unit` - release FFI, allows some GC of the associated process.
* `sys.ffi.link(FFI, SharedObject, Functions) : FFI` - load definitions into the FFI process. 
* `sys.ffi.eval(FFI, Script) : FFI` - ad-hoc script, can define things, call things, spawn threads, etc..
* `sys.ffi.fork(FFI) : (FFI, FFI)` - obtain a second cursor for multi-threading or checkpoints. 
* `sys.ffi.scope(FFI, TL) : FFI` - translate future names accessed or defined in FFI.
* `sys.ffi.store(FFI, Name, Range, Binary) : FFI` - inject binary data into FFI after prior operations.
* `sys.ffi.fetch(FFI, Name, Range) : (FFI, Binary)` - extract binary data from FFI

This API benefits from acceleration of the Script type, precompiling to a runtime-specific bytecode. Linking the same SharedObject many times should be very efficient. In context of distributed computing, SharedObject might influence which node hosts the process.

## Ordered Transactions

For these use cases, 'bgeval' offers a convenient escape hatch from the transactional effects API.

* `sys.refl.bgeval(MethodName, Arg)` - evaluate `MethodName(Arg)` in a prior transaction. The return value is logically cached then passed to the caller. If the caller aborts, the prior transaction may be aborted. 

 background transaction. The value is passed to a caller, as if computed then cached. If the caller aborts, bgeval may be aborted by the runtime.

This background transaction logically runs *before* to the current transaction, and returns the result through a cell. Going backwards in time like this avoids the problems of entangling code and state. There is some risk of thrashing, where bgeval repeatedly forces the caller to abort, but it's easily avoided or debugged.

## Non-Deterministic Choice for Concurrency and Search

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* choice. A scheduler could run forks in a predictable pattern. Based on heuristics, some forks may run far more frequently than others.

## Random Data

The runtime can provide a cryptographic noise function: 

* `sys.noise(Index, N)` - Within scope of a runtime, returns a constant integer in the range 0..(N-1). This result should be cryptographically unpredictable without first observing it, and should have a uniform distribution over indices.

This function may be expensive, e.g. involving an HMAC on `(Index, N)`. Users may take it as a seed for more efficient pseudo-random number generators or noise functions. To robustly partition this random field, the Index may involve state keys.

## Time

Transactions are logically instantaneous, but they may observe the estimated time of commit. However, directly observing time is unstable. To simplify reactive and real-time systems, we can introduce an API to await a given timestamp. 

* `sys.time.now()` - Returns a TimeStamp for estimated commit time. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with arbitrary precision. Multiple queries to the same clock within a transaction should return the same value. 
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. Intriguingly, it is feasible to evaluate the transaction slightly ahead of time then hold it ready to commit. This is convenient for modeling scheduled operations in real-time systems.
* `sys.time.clock` - implicit parameter, reference to a configured clock source by name. If unspecified, will use the configured default clock source.

In context of mirroring, we might configure a non-deterministic choice of network clocks to simplify interaction with network partitioning.  

Note that `sys.time.*` should not be used for profiling. We'll introduce dedicated annotations for profiling, and access profiles indirectly through reflection APIs such as `sys.refl.http`. 

## Environment and Configuration Variables

The configuration controls the environment presented to the application. The configuration has access to OS environment variables and application settings. A viable API:

* `sys.env.get(Query)` - The configuration should define a function representing the environment presented to the application. This operation will evaluate that function with an ad hoc Query. This should deterministically return some data or fail.

There is no finite list of query variables, but we can develop conventions for querying an 'index' that returns a list of useful queries. There is no support for setting environment variables statefully, but we could override `sys.env.get` in context of composing namespaces, and reference application state.

## Command Line Arguments

I propose to present arguments together with the environment. Unlike the OS environment variables, the configuration does not observe or interfere with arguments.

* `sys.env.args` - runtime arguments to the application, usually a list of strings

*Note:* These arguments don't include executable name. That property might be accessible indirectly via reflection APIs, but it's a little awkward in context of staged computing.

## Globs of Data? Defer.

RPC can support content-addressed data implicitly, but if we want to integrate content addressing manually with TCP or UDP messages we'll instead need suitable reflection APIs. To avoid troublesome interactions with a garbage collector, we'll also locally maintain a Context that associates content-addressed hashes to values. 

* `sys.refl.glob.write(&Context, Value)` - returns a binary representation of Value together with an updated reference Context that records necessary hashes. 
* `sys.refl.glob.read(Context, Binary)` - returns a Value, computed with access to a Context of external references. Fails if the binary contains any references not described in Context.

The exact representation of Context is runtime specific, but should be plain old data and open to extension and versioning.

## Console IO

We could support console IO from applications via `sys.tty.*` methods. Simple streaming binaary reads and writes are probably adequate. Viable API:

* `sys.tty.write(Binary)` - add Binary to write buffer, written upon commit
* `sys.tty.read(Count)` - read exactly Count bytes, diverge/wait if it's not available
* `sys.tty.unread(Binary)` - add Binary to head of read buffer to influence future reads.

We could optionally add some methods to control line buffering and input echo.

*Note:* I'd recommend the 'http' interface over console IO for any sophisticated user interaction. But console IO is still a good option for integration in some cases.

## Filesystem

The filesystem API should mostly be used for system integration instead of persistence. For persistence, the database API is much more convenient with full support for transactions, structured data, content-addressed storage, and mirroring. In contrast, filesystem operations are mostly asynchronous, running between transactions after commit, returning the response through an abstract runtime reference.

Instead of a single, global filesystem root, an application queries the configuration for abstract application named roots like "AppData". Filesystem paths are abstract to control construction of relative paths and enforce restrictions such as read-only access. In addition a user's filesystem, abstract paths may refer to mirrors, DVCS resources, or a simulated in-memory filesystem with some initial state. 

*Note:* In my vision of [applications as notebook interfaces](GlasNotebooks.md), the compiler will also capture an abstract reference to application source files to integrate projectional editors and live coding environment. Thus, the compilation environment is another source of abstract file paths.

## Network

It is feasible to support something similar to the sockets API, perhaps initially limited to TCP and UDP. However, network interfaces (and possibly port bindings) should be restricted and abstracted by the configuration. 




Network interfaces abstracted through the configuration and are obviously mirror specific. If we open a TCP listener on network interface "xyzzy", we might ask the configuration what this means to each mirror. For each mirror we might return a list of actual hardware interfaces. Perhaps "xyzzy" is a specific interface on a specific mirror, and we only listen at one location. Or perhaps multiple mirrors will have an "xyzzy" interface, requiring a distributed transaction to open the distributed TCP listener. 

Of course, each incoming TCP connection would still be bound to a specific network interface on a specific mirror. If we initiate a connection, having multiple hardware interfaces might be interpreted as a non-deterministic choice. Though, it might be convenient to let the configuration distinguish "xyzzy-in" vs "xyzzy-out". 


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

Aside from automated testing described in [the design doc](GlasDesign.md), it can be useful to add assertions to programs as annotations. Similar to logging and profiling, tests could be associated with static channels to support configuration (e.g. test frequency, disable after so many tests, etc.), and it can be useful to support continuous testing over the course of an operation.

        # viable syntax
        test(chan, property, message) { operation }

In this case we might evaluate a property as pass/fail, and then evaluate the message expression only when the property fails. This might also be interpreted as a form of logging, except the default configuration would be to abort the current transaction on a failed test.

Other than inline testing, it might be feasible to express ad hoc assumptions about final conditions just before commit. 

### Tracing? Tentative.

In some cases, it is useful to track dataflows through a system including across remote procedure calls and transactions. This can be partially supported by including provenance annotations within data representations. The [glas object](GlasObject.md) representation supports such annotations, for example. We will need something more to efficiently maintain provenance when processing data. I suspect this will need an ad hoc solution for now.

### Mapping

For live coding, projectional editing, debugging, etc. we often want to map program behavior back to source texts. In context of staged metaprogramming, this might best be modeled as *Tracing* source texts all the way to compiled outputs. This implies provenance annotations are represented at the data layer, not the program layer. 

For performance, a late stage compiler might extract and preprocess annotations in order to more efficiently maintain provenance metadata (e.g. as if tracing an interpreter). But this should be understood as an optimization.

## Rejected Features

It is feasible to introduce some `sys.disable()` and `sys.enable()` operations, perhaps parameterized for specific events such as 'step' and 'rpc'. However, I'm not convinced this is a good idea. Runtime state is less visible to users than application state, and more likely to be forgotten. It's also imprecise. In comparison, users can easily add some application state and add some explicit conditions to specific 'step' threads or RPC calls.

It is feasible to introduce periodic operations that evaluate at some configurable frequency, perhaps a `status()` health check. However, I think it better to let users manage this more explicitly, using `sys.time` to control the frequency of some operations and using HTTP requests to get information about health. 

