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

* **cell** - the basic get, set, swap (for linear types). In a distributed runtime, only one node can 'own' a cell for writing, but mirrored caching is possible for read-mostly cells, and the cell can be migrated.

* **queue** - a list cell accessed with 'write', 'read', and 'putback'. Supports one reader and multiple concurrent writer transactions. However, in a distributed runtime, the write end is owned by only one node. Attempting to share is infeasible in context of observing time and network partitioning.

* **bag** - a cell containing a multiset (an unordered list), accessed via 'write', 'read', and 'peek'. Serializable with any number of concurrent readers and writers. In a distributed runtime, every node can operate on a local slice of the bag, and the runtime can freely shuffle elements between slices. Reads are non-deterministic but can be filtered (via read then abort), but read variants with runtime support for filtering can enhance performance and support heuristic routing of items to interested nodes.

* **CRDTs?** - [conflict-free replicated datatypes](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are designed for concurrent edits and partitioning tolerance, and can be adapted. But I don't know which ones I'd want as built-ins. For now, users might manually replicate CRDTs, maintaining a cell in each node with a bag of updates.

### Keys

State is accessed through algebraic effects. We'll present this a suitable API for a key-value database with abstract static keys. Construction of keys can follow a directory-like structure, and runtime-scoped keys can be transparently constructed from shared-scope keys (but not vice versa). This allows a program to control subprogram access to the database through controlling access to keys. It also supports encoding ad hoc metadata, such as caching hints into keys.

### API

TBD

## HTTP Interface

The runtime should recognize the 'http' interface and support requests over the same channels we use for remote procedure calls and debugging. By default, `"/sys/*"` will be intercepted for external debugger integration.

        http : Request -> [sys] Response

The Request and Response types are binaries. However, these will often be *accelerated* binaries, i.e. with a structured representation under-the-hood that can be efficiently queried and manipulated through built-in functions. The application receives a complete request from the runtime, and must return a complete respon, no chunks. There is no support for WebSockets or SSE.

Each 'http' request is handled in a separate transaction. If this transaction aborts voluntarily, it is logically retried until it successfully produces a response or times out, providing a simple basis for long polling. A 303 See Other response is suitable in cases where multiple transactions are required to compute a response. Runtimes may eventually support multiple requests within one transaction via dedicated HTTP headers, but that will wait for the future.

Ideally, authorization and authentication are separated from the application. We could instead model them as application-specific runtime configuration, perhaps integrating with SSO.

## Remote Procedure Calls

If an application implements 'rpc' it may receive remote procedure calls (RPC).

        rpc : (MethodRef, UserArg) -> [rpc.cb, sys] Result

The UserArg and Result values are exchanged with the caller. Optionally, limited interaction may be supported via algebraic effects, an 'rpc.cb' callback. The MethodRef is instead a runtime parameter, relating to how RPC is registered and published. The runtime will map between local use of MethodRef and external use of GUIDs or URLs.

RPC must be configured. The simplest solution is to declare a static API in application settings. Alternatively, the application settings might indicate a MethodRef for fetching a dynamic API. I propose to organize RPC methods into 'objects' that are published to different registries based on trust and roles. A prospective caller will query for RPC objects matching an interface and metadata. 

To enhance performance, I hope to support annotation-guided code distribution. The 'rpc' method can be partially evaluated based on MethodRef, then have some code extracted for evaluation at the caller. A caller can similarly forward part of the callback code and continuation. These optimizations would mitigate performance pressures, supporting simplified remote APIs.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) is that users participate in transactions indirectly through reflection on a user-agent. This allows for some interesting integration across multiple services via transactional remote procedure calls. However, I'd rather not develop a half-assed GUI framework; do it well or not at all. In the meanwhile, we'll rely on HTTP or FFI as basis for GUI.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may compute forks in a very predictable pattern, some more frequently than others.

## Random Data

A stateful random number generator is awkward in context of concurrency, distribution, and incremental computing. However, we can easily provide access to a stable, cryptographically random field.

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The seed argument is arbitrary and may be structured. To robustly partition the random field, users can include state keys in the seed.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source. In a distributed runtime, all nodes share the secret.

## Foreign Function Interface

The glas system discourages use of FFI for performance roles where *acceleration* is a good fit. However, there are other  use cases such as integration with host features or resources the 'sys.\*' API doesn't cover, or access to vast libraries of pre-existing code. Even for performance, FFI can serve as a convenient stopgap.

I propose an API based around streaming commands to FFI threads. In general, these threads may run in attached processes to isolate concerns with memory safety and sharing. Each thread maintains a local 'namespace' of mutable variables and loaded functions. This namespace is not shared between threads, but threads within the same process do share the heap, thus may interact through pointers to allocated objects.

A viable effects API:

* `sys.ffi.open(Hint) : FFI` - Returns a linear reference to a new FFI thread with an initially empty namespace. The Hint may guide sharing of processes, location in a distributed runtime, and other configuration options. 
* `sys.ffi.load(FFI, SharedObject, Functions) : FFI` - Adds functions from a referenced ".so" or ".dll" file to the FFI thread's namespace. The Functions argument should describe aliases, types, and calling conventions to support integration.
* `sys.ffi.fork(FFI) : (FFI, FFI)` - Splits a stream of FFI operations and clones the FFI thread. The thread namespace is copied and will evolve independently based on future commands, but the process heap and global variables are shared between threads. 
* `sys.ffi.eval(FFI, Script) : FFI` - Run a simple procedure in context of the thread's namespace. The Script can read and write variables, call FFI functions, and supports simple conditionals and loops.
* `sys.ffi.store(FFI, Name, Type, Value) : FFI` - inject data into a thread's namespace. The type indicates how the value is translated, e.g. rational to floating point. Deleting a name might be expressed as storing a void type. 
* `sys.ffi.fetch(FFI, Name, Type) : (FFI, Value)` - extract data of known type from the thread's namespace. This will wait for the FFI thread to settle, i.e. it diverges while Status is 'busy'. We can fetch from a failed thread.
* `sys.ffi.status(FFI) : (FFI, Status)` - query whether the FFI thread is busy, halted in a failure state, or awaiting commands. Some details of the failure state or busy status might also be available.
* `sys.ffi.close(FFI) : unit` - release FFI. If we close all FFI associated with a process, we can kill that process.

This API incurs moderate overhead per operation for transactions and serialization. This is negligible for long-running or infrequent operations, but swiftly adds up for short operations at high-frequencies. Performance can be mitigated by constructing a long-running loop within the FFI process and interacting with it through the heap.

*Note:* For portability and security, the Hint and SharedObject types should be translated by the user configuration.

## Background Eval

In some scenarios, we can reasonably assume operations are 'safe' such as HTTP GET, reading a file, or triggering a lazy computation. In these cases, we might want an escape hatch from the transaction system, i.e. such that we can trigger the computation, await the result, and pretend this result is already present. 

A proposed mechanism is background eval:

* `sys.refl.bgeval(StaticMethodName, UserArg) : Result` - Evaluate `StaticMethodName(UserArg)` in a separate transaction. The caller waits for this to commit then continues with the returned Result. 

Intriguingly, stable bgeval integrates with incremental computing, and non-deterministic bgeval can clone the caller for each Result. If the caller is aborted for any reason, such as live code update, the background transaction may also be aborted unless it has already committed.

*Caveats:* Computation may 'thrash' if the background computation repeatedly conflicts with the caller. The new transaction receives the original 'sys.\*' effects API, which may constitute a privilege escalation.

## Time

A transaction can query a clock. A repeating transaction can wait on the clock, i.e. by aborting before the time is right. But the direct implementation is extremely inefficient, so we'll want to optimize this pattern.

* `sys.time.now()` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with flexible precision. Multiple queries to the same clock within a transaction should return the same value. 
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. A runtime can easily optimize this to wait for the specified time. Further, the runtime can evaluate the transaction slightly ahead of time and hold it ready to commit.

In context of a distributed runtime, each node can maintain its own local estimate of the runtime's clock, but we must synchronize as nodes interact based on the last 'observed' time for each transaction.

*Note:* If attempting to record how long a computation takes, use profiling annotations instead.

## Arguments and Environment Variables

A runtime can easily provide access to OS environment variables and command-line arguments.

* `sys.env.list : List of Text` - return the defined environment variables
* `sys.env.get(Text) : Text` - return value for an OS environment variable
* `sys.env.args : List of Text` - return the command-line arguments

In context of a distributed runtime, this environment is captured when the application was started. The application cannot mutate this environment, though it could intercept 'sys.env.\*' in scope of a subprogram.

## Console IO

A minimum viable API:

* `sys.tty.write(Binary)` - write Binary to stdout upon commit.
* `sys.tty.read(Count) : Binary` - read Count bytes, diverge/wait if not available.
* `sys.tty.unread(Binary)` - add Binary to head of read buffer.
* `sys.tty.ctl(Hint)` - configure tty, e.g. disable input echo and line buffering

It is feasible to disable line buffering and input echo, to support sophisticated console applications via ANSI escape codes and a graphical terminal emulator like kitty or ghostty. However, I hope to push most interaction to HTTP, GUI, and other modes, and I don't want to spend much time on this API. 

*Note:* The Hint might be be translated by the user configuration. 

## Globs and Content-Addressed Data

The runtime will handle most serialization of glas data - remote procedure calls, persistent state, persistent memo cache, etc.. And this can take advantage of content-addressed data. It is feasible to provide the user some access to this subsystem through a reflection API. Unfortunately, it's a little tricky to integrate this robustly with garbage collection.



RPC can support content-addressed data implicitly, but if we want to integrate content addressing manually with TCP or UDP messages we'll instead need suitable reflection APIs. To avoid troublesome interactions with a garbage collector, we'll also locally maintain a Context that associates content-addressed hashes to values. 

* `sys.refl.glob.write(&Context, Value)` - returns a binary representation of Value together with an updated reference Context that records necessary hashes. 
* `sys.refl.glob.read(Context, Binary)` - returns a Value, computed with access to a Context of external references. Fails if the binary contains any references not described in Context.

The exact representation of Context is runtime specific, but should be plain old data and open to extension and versioning.

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

