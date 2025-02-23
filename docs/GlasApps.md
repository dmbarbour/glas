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
* 'switch' - First transaction in new code after live update. Old code runs until successful switch.
* 'settings' - (pure) influences runtime configuration when computing application-specific runtime options.

The transactions will interact with the runtime - and through it the underlying systems - with an algebraic effects API. Unfortunately, most external systems - filesystem, network, FFI, etc. - are not transactional. We resolve this by buffering operations to run between transactions. But there are a few exceptions: application state, remote procedure calls, and a convenient escape hatch for safe, cacheable operations like HTTP GET.

## Application State

The runtime will support a few useful state models: cells, queues, bags, key-value stores, and so on. Applications can construct and interact with these objects, but cannot share them: they are runtime scoped. The objects can be garbage collected if unreachable. The runtime provides a root key-value store to get started.

A viable API:

* `sys.db.root : KVS` - the application's state.
* `sys.db.cell.*` - minimalist state, holds a single value. In a distributed runtime, only one node can write, but others may read a cached cell.
  * `new(Data) : Cell` - construct a new cell with initial value
  * `get(Cell) : Data` - access current value
  * `set(Cell, Data)` - update value
  * `swap(Cell, Data) : Data` - combines get and set (necessary for linear types)
* `sys.db.queue.*` - a cell containing a list with controlled access. In a distributed runtime, reader and writer may be separate nodes. A writer can support multiple concurrent write transactions, buffering and interleaving data. 
  * `new() : Queue` - construct a new queue, initially empty
  * `read(Queue, N) : List of Data` - reader removes list of exactly N items from head of queue. Will diverge if fewer items available, forcing the transaction to retry later.
  * `unread(Queue, List of Data)` - reader adds list to head of queue for a future read, for convenience
  * `write(Queue, List of Data)` - add list to tail of queue, primary update operation
* `sys.db.bag.*` - like a queue, but reads are unordered. In a distributed runtime, each node may read and write its local slice of the bag, and the runtime is free to shuffle items between nodes.
  * `new() : Bag` - construct a new bag, initially empty
  * `read(Bag) : Data` - read and remove data, non-deterministic choice.
  * `write(Bag, Data)` - add data to bag.
* `sys.db.kvs.*` - a key-value store, like a cell containing a dict with controlled access. Keys are arbitrary data but should be small for performance. In a distributed system, each key may have a separate writer node, but every node may have a read-only cache.
  * `new(weak?) : KVS` - construct a new KVS, initially empty. Can configure for weak references to keys (recommended!).
  * `get(KVS, Key) : Data` - read data at key. Will diverge if Key is undefined
  * `set(KVS, Key, Data)` - write data at key
  * `swap(KVS, Key, Data) : Data` - swap data at key, combines get and set
  * `del(KVS, Key)` - modify Key to undefined state. If configured for weak references, GC may automatically delete keys that cannot be constructed.
  * `keys(KVS) : List of Key` - return a list of defined keys.
* `sys.db.key.new() : Key` - allocates a new, unique reference primarily for use as a key in KVS. This is useful for conflict prevention, and also for weak references and automatic deletion.

Beyond these, we might adapt some [conflict-free replicated datatypes (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) for partitioning tolerance. For CRDTs, each node can locally read and write its own replica, but (for serializable transactions) we must synchronize CRDTs between nodes whenever they interact.

*Note:* For a distributed runtime, this state API can tolerate temporary network disruptions, but permanent node destruction requires deliberate design. Still, we have options: favor bags or CRDTs, architect apps so the cells and queues state used remote nodes can be removed from root and garbage collected, or use reflection APIs to forcibly transfer ownership. 

## HTTP Interface

The runtime should recognize the 'http' interface and support requests over the same channels we use for remote procedure calls and debugging. By default, `"/sys/*"` will be intercepted for external debugger integration.

        http : Request -> [sys] Response

The Request and Response types are binaries. However, these will often be *accelerated* binaries, i.e. with a structured representation under-the-hood that can be efficiently queried and manipulated through built-in functions. The application receives a complete request from the runtime, and must return a complete respon, no chunks. There is no support for WebSockets or SSE.

Each 'http' request is handled in a separate transaction. If this transaction aborts voluntarily, it is logically retried until it successfully produces a response or times out, providing a simple basis for long polling. A 303 See Other response is suitable in cases where multiple transactions are required to compute a response. Runtimes may eventually support multiple requests within one transaction via dedicated HTTP headers, but that will wait for the future.

Ideally, authorization and authentication are separated from the application. We could instead model them as application-specific runtime configuration, perhaps integrating with SSO.

*Aside:* It is feasible to configure a runtime to automatically launch the browser and attach to the application.

## Remote Procedure Calls

If an application implements 'rpc' it may receive remote procedure calls (RPC).

        rpc : (MethodRef, UserArg) -> [rpc.cb, sys] Result

The UserArg and Result values are exchanged with the caller. Optionally, limited interaction may be supported via algebraic effects, an 'rpc.cb' callback. The MethodRef is instead a runtime parameter, relating to how RPC is registered and published. The runtime will map between local use of MethodRef and external use of GUIDs or URLs.

RPC must be configured. The simplest solution is to declare a static API via application settings. Alternatively, we could specify a MethodRef to fetch a dynamic API at runtime. 

I propose to organize RPC methods into 'objects' that are published to different registries based on trust and roles. A prospective caller will query for RPC objects matching an interface and metadata.

To enhance performance, I hope to support annotation-guided code distribution. The 'rpc' method can be partially evaluated based on MethodRef, then have some code extracted for evaluation at the caller. A caller can similarly forward part of the callback code and continuation. These optimizations would mitigate performance pressures, supporting simplified remote APIs.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. There are many interesting opportunities with this perspective. However, implementing a new GUI framework is a non-trivial task that should be done well or not at all. Thus, I'll defer support until I'm able to dedicate sufficient effort. 

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.
* `(%fork Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice, convenient for static analysis.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may compute forks in a very predictable pattern, some more frequently than others.

## Random Data

A stateful random number generator is awkward in context of concurrency, distribution, and incremental computing. However, we can easily provide access to a stable, cryptographically random field.

* `sys.random(Seed, N) : Binary` - (pure) return a list of N cryptographically random bytes, uniformly distributed. Unique function per runtime instance.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes share the secret. The Seed value may be structured.

## Background Eval

In some scenarios, we can reasonably assume operations are 'safe' such as HTTP GET, triggering a lazy computation, or writing some metadata only for reflection-like purposes. In these cases, we might want an escape hatch from the transaction system, i.e. such that we can trigger the computation, await the result, and pretend this result is already present. 

A proposed mechanism is background eval:

* `sys.refl.bgeval(StaticMethodName, UserArg) : Result` - Evaluate `StaticMethodName(UserArg)` in a separate transaction. The caller waits for this to commit then continues with the returned Result. 

Intriguingly, stable bgeval integrates with incremental computing, and non-deterministic bgeval can implicitly fork the caller for each Result. We can apply transaction-loop optimizations. We can also abort bgeval together with the caller, in case of read-write conflict or live coding.

*Caveats:* Computation may 'thrash' if bgeval repeatedly conflicts with the caller. But this is easy to detect and debug. The new transaction receives the original 'sys.\*' effects API, which may constitute a privilege escalation. But we should restrict untrusted code from reflection APIs in general.

## Time

A transaction can query a clock. A repeating transaction can wait on the clock, i.e. by aborting before the time is right. But the direct implementation is extremely inefficient, so we'll want to optimize this pattern.

* `sys.time.now()` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with flexible precision. Multiple queries to the same clock within a transaction will return the same value.
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. A runtime can easily optimize this to wait for the specified time. The runtime could precompute the transaction slightly ahead of time and hold it ready to commit, a viable basis for soft real-time systems.

In context of a distributed runtime and network partitioning, each node maintains its own local estimate of the runtime clock. When the nodes communicate, we conservatively include the maximum *observed* TimeStamp, i.e. the maximum timestamp that might have contributed to that message. For 'await', we observe the TimeStamp parameter. With this value, we can guarantee observation of the runtime clock is serializable and monotonic. (Fixing clock drift is a separate concern best left to NTP or PTP.)

*Note:* If attempting to record how long a computation takes, use profiling annotations!

## Arguments and Environment Variables

A runtime can easily provide access to OS environment variables and command-line arguments.

* `sys.env.list : List of Text` - return the defined environment variables
* `sys.env.get(Text) : Text` - return value for an OS environment variable
* `sys.env.args : List of Text` - return the command-line arguments

The application cannot mutate this environment, though it can override access to 'sys.env.\*' within scope of a subprogram.

*Note:* Applications integrate the configuration environment through the namespace layer, '%env.\*'.

## Console IO

With users launching glas applications from a command-line interface, it is convenient to support user interaction directly through the same interface. The basics are just reading and writing some text, but it is possible to disable line buffering and input echo then implement sophisticated applications via [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or extended protocols.

A viable API:

* `sys.tty.write(Binary)` - write to standard output, buffered until commit
* `sys.tty.read(N) : Binary` - read from standard input. Diverges if not enough data.
* `sys.tty.unread(Binary)` - add Binary to head of input buffer for future reads.
* `sys.tty.ctrl(Hint)` - ad hoc control, extensible but mostly for line buffering and echo

The control hint is runtime specific, perhaps something like `(icanon:on, ...)`. It can be adapted easily enough. I leave standard error for runtime use, warnings or such as log outputs.

## Foreign Function Interface

The glas system discourages use of FFI for performance roles where *acceleration* is a good fit. However, there are other  use cases such as integration with host features or resources the 'sys.\*' API doesn't cover, or access to vast libraries of pre-existing code. Even for performance, FFI can serve as a convenient stopgap.

I propose an API based around streaming commands to FFI threads. In general, these threads may run in attached processes to isolate concerns with memory safety and sharing. Each thread maintains a local 'namespace' of mutable variables and loaded functions. This namespace is not shared between threads, but threads within the same process do share the heap, thus may interact through pointers to allocated objects.

A viable effects API:

* `sys.ffi.open(Hint) : FFI` - Returns a reference to a new FFI thread. The Hint may guide sharing of processes, location in a distributed runtime, redirection of standard output and error streams, and other configurable options. The thread's initial namespace may include a few built-in functions and configured properties. Initial status is 'busy' - open is effectively the first command.
* `sys.ffi.load(FFI, SharedObject, Functions)` - Adds functions from a referenced ".so" or ".dll" file to the FFI thread's namespace. The Functions argument should describe aliases, types, and calling conventions to support integration.
* `sys.ffi.fork(FFI) : FFI` - Splits a stream of FFI operations and clones the FFI thread. Returns reference to the clone. The thread namespace is copied and will evolve independently based on future commands, but the process heap and global variables are shared between threads.
* `sys.ffi.eval(FFI, Script)` - Run a simple procedure in context of the thread's namespace. The Script can read and write variables, call FFI functions, and supports simple conditionals and loops.
* `sys.ffi.define(FFI, Name, Script)` - For performance, we might define functions for reuse within the FFI context.
* `sys.ffi.store(FFI, Name, Type, Data)` - inject data into a thread's namespace. The type indicates how the value is translated, e.g. rational to floating point. Deleting a name might be expressed as storing a void type.
* `sys.ffi.fetch(FFI, Name, Type) : Data` - extract data of known type from the thread's namespace. This will wait for the FFI thread to settle, i.e. it diverges while Status is 'busy'. We can fetch from a failed thread.
* `sys.ffi.status(FFI) : Status` - query whether the FFI thread is busy, halted in a failure state, or awaiting commands. Limited details of failure cause or busy status (e.g. how many steps behind) might be available.
* `sys.ffi.close(FFI) : unit` - release the FFI, allowing for cleanup. This won't kill the process. To kill the process, a built-in function or loaded function might supply an 'exit()'.

This API incurs moderate overhead per operation for transactions, serialization, and processing of scripts. This is negligible for long-running or infrequent operations, but swiftly adds up for short operations at high-frequencies. Performance can be mitigated by pushing more code to the FFI process. If necessary, users might construct a loop within the FFI process that is controlled asynchronously through the heap.

*Note:* The Hint, SharedObject, Script, etc. types may be runtime specific. We may be relying on configuration-provided adapters for portability!

## Content-Addressed Storage and Glas Object (Low Priority!)

The runtime uses content-addressed data when modeling very large values in context of remote procedure calls, persistent data, and virtual memory. Based on configuration, we might integrate with content delivery networks. Users can potentially extend these use cases with sufficient access, but we must be careful regarding garbage-collection.

Rough API sketch: 

* `sys.refl.glob.*` - an API for serialization or parsing of glas data into a binary representation. Operations will take a CAS as a context argument to keep hashes in scope. Lazy loading of a binary into a value might extend the set of hashes that CAS is waiting on. Serializing a value can store hases into the CAS. Values cannot be serialized if they're still waiting on lazy hashes, but we can potentially determine which hashes we're lazily waiting upon.

* `sys.refl.cas.*` - (tentative) an API that helps users maintain a content-addressed storage (CAS) context. This might prove unnecessary, perhaps we could maintain a serialization context as plain old data without any access to hashes held by the runtime. In any case, we can store and fetch binaries. Each stored binary might be paired with a list of hashes referenced by the binary. We can potentially report which hashes we're waiting on. The details need work, but should closely align to whatever a runtime is doing under the hood.

## Node Locals

A distributed transaction doesn't have a location, but it must start somewhere. With runtime reflection, we can take a peek.

* `sys.refl.txn.node() : NodeRef` - return a stable identifier for the node where the current transaction started.

This API is useful for keeping associative state per node in a key-value store. 

Observing the starting node has consequences! A runtime might discover that a transaction started on node A is better initiated on node B. Normally, we abort the transaction on node A and let B handle it. After observing that we started on node A, we instead *migrate* to node B. With optimizations, we can repeatedly evaluate on node B, *but only while connected to node A*. Thus, carelessly observing the node results in a system more vulnerable to disruption.

## Filesystem

Adapting the normal filesystem API is essentially specialized FFI. Users queue up a few operations to run between transactions then fetch results or status. In context of a distributed runtime, the notion of filenames might be extended to indicate node. This would be similar to how we indicate node for FFI.

We can extend the filesystem API to read or write a whole file from a single transaction. The 'read' can be supported via *Background Eval*. It isn't truly atomic, but we can safely pretend it is in many cases.

I'm interested in extending the filesystem API for DVCS integration and cooperative work. However, this is relatively low priority.

## Network

We can wrap the conventional sockets APIs with a specialized FFI. Use references. Schedule the actual operations between transactions.

Intriguingly, we might also support opening 'listeners' on multiple nodes as a single command. Similar to how a TCP listen command can bind to multiple interfaces on a single node.
