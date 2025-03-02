# Glas Applications

## The Transaction Loop

A *transaction loop* is a very simple idea - a repeating transaction, atomic and isolated. A direct implementation is nothing special; at best, it makes a convenient event dispatch loop, friendly for live coding and orthogonal persistence. However, consider a few optimizations we can apply: 

* *incremental computing* - We don't always need to repeat the entire transaction from the start. Instead, roll-back and recompute based on changed inputs. If repeating the transaction is obviously unproductive, e.g. it aborts, then we don't need to repeat at all until conditions change. If productive, we still get a smaller, tighter repeating transaction that can be optimized with partial evaluation of the stable inputs.
* *duplication on non-deterministic choice* - If the computation is stable up to the choice, then duplication is subject to incremental computing. This provides a basis for concurrency, a set of threads reactive to observed conditions. If the computation is unstable, then at most one will commit while the other aborts (due to read-write conflict), representing a race condition or search. Both possibilities are useful.
* *distributed runtimes* - A distributed runtime can mirror a repeating transaction across multiple nodes. For performance, we can heuristically abort transactions best started on another node, e.g. due to locality of resources. Under incremental computing and duplication on non-deterministic choice, this results in different transactions running on different nodes. In case of network partitioning, transactions in each partition may run independently insofar as we can guarantee serializability (e.g. by tracking 'ownership' of variables). An application can be architected such that *most* transactions fully evaluate on a single node and communicate asynchronously through runtime-supported queues, bags, or CRDTs. 

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
* 'settings' - influences integration of the application, e.g. adapters and some runtime options. 

The transactions will interact with the runtime - and through it the underlying systems - with an algebraic effects API. Unfortunately, most external systems - filesystem, network, FFI, etc. - are not transactional. We resolve this by buffering operations to run between transactions. But there are a few exceptions: application state, remote procedure calls, and a convenient escape hatch for safe, cacheable operations like HTTP GET.

## Application State

Applications receive access to a database with support for atomic transactions, structured data, and content-addressed storage. I propose a key-value database in this role. The root database may be persistent and shared with other applications. A subset of keys are ephemeral, bound to runtime memory and permitting runtime-scoped data (including keys). We can further support transaction-scoped data, serving a role similar to thread-local storage. In the persistent database, we're limited to plain old data.

A viable API:

* `sys.db.*` - a key-value database with abstract keys. 
  * `root : Key` - initial access to a database. Runtime-scoped, but usually persistent.
  * `key.*` - referencing the database
    * `dir(Key, Data) : Key` - access subdirectory labeled by plain old data. 
    * `arc(Key, Key) : Key` - access subdirectory labeled by another key. This serves as a basis for associative structure. Unlike dirs, arcs cannot be listed (modulo reflection) so GC can treat the key as a weak reference.  
    * `eph(Key) : Key` - arc via implicit ephemeral key with runtime lifespan.
    * `txn(Key) : Key` - arc via implicit ephemeral key with *transaction* lifespan. Associated data is logically cleared after the transaction terminates.
    * `gen() : Key` - runtime allocates a new ephemeral key. Subject to garbage collection.
  * `dir.*` - support for browsing the database
    * `list(Key) : List of Data` - list edge labels for active directories in the database. An active directory has at least one defined value. (There is no equivalent for *arcs*, modulo reflection APIs.)
    * `pick(Key) : Data` - pick active edge non-deterministically. Like 'peek' on a bag.
    * `clear(Key)` - transitively delete everything reachable from this directory (val, dirs, arcs).
  * `val.*` - every key may have an associated value
    * `get(Key) : Data` - copy data at key. diverges if key has no associated value.
    * `set(Key, Data) : Data` - write data into key. The data must not have a shorter lifespan than the key. 
    * `del(Key)` - reset key to an undefined state.  
    * `has(Key) : Bool` - observe if key has value without observing the value
    * `take(Key) : Data` - get then del data at key. Diverges if key has no value.
    * `put(Key, Data)` - set only if key is undefined. Diverges if key has a value.
    * `move(Key, Key)` - blindly 'take' from one key and 'put' to another. Blindness is relevant 
    * `copy(Key, List of Key)` - blindly 'get' from one key and 'set' to one or more keys. 
  * `queue.*` - specialized view of list state. A runtime can evaluate multiple blind writer transactions and a single reader in parallel without risk of serializability conflicts.
    * `read(Key, N) : List of Data` - reader takes items from head of list. Diverges if not a list or insufficient data.
    * `push(Key, List of Data)` - reader pushes items to head of list for future reads (i.e. use as stack or deque)
    * `write(Key, List of Data)` - addends items to tail of list. Diverges if not a list.
    * `wire(Key, N, Key)` - (tentative) blindly read N items from one queue and write them to another 
  * `bag.*` - essentially, an unordered queue. Reads and writes operate on non-deterministic locations in the list. Many readers and writers can be evaluated in parallel with some heuristic coordination. Useful for distributed systems.
    * `write(Bag, Data)` - add item to bag, non-deterministic position in list.
    * `read(Bag) : Data` - read and remove data, non-deterministic choice. Diverges if bag is empty. *Note:* An optimizer might look at the *continuation* to heuristically select reads that more likely lead to a commit.
    * `peek(Bag) : Data` - read data without removing it. Can be useful to manage concurrency.
* `sys.refl.db.*` - methods for reflection on the database.

When a key is used in a specialized mode, the system may heuristically optimize for this use case. This is especially relevant in a distributed runtime, where reader and writer endpoints of a queue can be split between two nodes, or a bag could be partitioned across many nodes. This optimization can be undone, but it generally requires a distributed transaction, thus a 'get' on a queue might diverge during network disruption. To improve disruption tolerance, extending the API with support for some [CRDTs](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) could be a very effective.

An application adapter could easily hide 'root' and instead present an API with an application home, inbox, outbox, commons, perhaps a relational database, etc.. 

Regarding security, a configuration may describe some security policies at the granularity of specific directories. This can feasibly be enforced per call-site, based on which portions of an application are trusted. Based on application settings, a trusted application adapter can provide controlled access to an untrusted application, e.g. limiting an application to a 'home' directory, inbox, outbox, and a dedicated commons area. See *Securing Applications*.

## HTTP Interface

The runtime should recognize the 'http' interface and support requests over the same channels we use for remote procedure calls and debugging. Based on application settings, the runtime might intercept `"/sys"` for debugging and reflection. But every other subdirectory will be handled by 'http'.

        http : Request -> [sys] Response

The Request and Response types are binaries. However, these will often be *accelerated* binaries, i.e. with a structured representation under-the-hood that can be efficiently queried and manipulated through built-in functions. The application receives a complete request from the runtime, and must return a complete respon, no chunks. There is no support for WebSockets or SSE.

Each 'http' request is handled in a separate transaction. If this transaction aborts voluntarily, it is logically retried until it successfully produces a response or times out, providing a simple basis for long polling. A 303 See Other response is suitable in cases where multiple transactions are required to compute a response. Runtimes may eventually support multiple requests within one transaction via dedicated HTTP headers, but that will wait for the future.

Ideally, authorization and authentication are separated from the application. We could instead model them as application-specific runtime configuration, perhaps integrating with SSO.

*Aside:* It is feasible to configure a runtime to automatically launch the browser and attach to the application.

## Remote Procedure Calls

An RPC API can be declared in application settings. The runtime will inspect this APIs and publish to configured registries. Part of this API may be dynamic, requiring a runtime to repeatedly query the application after it has started. Instead of one monolithic RPC API, it is useful to describe a set of fine-grained APIs, each with their own interfaces, security constraints, roles, topics, and other ad hoc metadata. This allows a runtime to route each interface to a different subset of registries.

To receive RPC calls, the application defines a dispatch method:

        rpc : (MethodRef, List of Arg) -> [cb, sys] Result

The MethodRef is taken from declared APIs, and should be designed for efficient dynamic dispatch. For security reasons, the runtime must prevent forgery of MethodRef and support revocation in context of dynamic API changes. This is easily supported by building a lookup table, mapping local MethodRefs to external, cryptographically-generated tokens.

In addition to the runtime system API, 'rpc' may interact with the caller through a provided 'cb' handler. Because our compiler doesn't work across applications, we'll rely on dynamic dispatch, perhaps a `cb("method-name", List of Arg)` convention. In addition to arguments, callbacks may recursively receive a handler for making callbacks, ad infinitum. In practice, callback depth is constrained by protocols, performance, and quotas.

A prospective client will search for RPC interfaces matching a certain interface, role, provenance, and other metadata. Instead of expressing this search as a stateful event channel, I propose to model it as a continuous, stable query. This ensures our applications are reactive to changes in the open system. 

A viable API for the client:

        type SearchResult = ok:List of RpcObj | error:(text:Message, ...)
        type Criteria is runtime specific, for now
        type RpcObj is abstract and transaction scoped

* `sys.rpc.*` - 
  * `find(Criteria) : SearchResult` - search for available objects matching some ad hoc criteria.
  * `find.fork(Criteria) : SearchResult` - as 'find' but will non-deterministically pick one RpcObj on success. That is, on an okay result, the list contains one result. This is more stable than observing the whole list before forking.
  * `obj.*`
    * `call(RpcObj, MethodName, List of Arg) : [cb] Result` - initiate a remote operation on RpcObj. The caller must provide the simple callback 'cb' as an algebraic effects. If no callback is needed based on protocols, assert false to terminate the transaction. 
    * `meta(RpcObj, FieldName) : ok:Data | none` - observe associated metadata fields that were received with the RpcObj. Observing them one at a time is convenient for incremental computing.

This API supports continuous discovery and opportunistic interactions at the expense of long-term connections. But we can model long-term interactions by reusing 'session' GUIDs across multiple transactions, and by including GUIDs or URLs in the metadata.

To enhance performance, I hope to support a little code distribution, such that some calls or callbacks can be handled locally. Guided by annotations, an 'rpc' method may be partially evaluated based on MethodRef and have code extracted for evaluation at the caller. We can do the same with 'cb', partially evaluating based on MethodName. To support pipelining of multiple calls and remote processing of results, we could even send part of the continuation. However, this is all very theoretical at the moment.

*Note:* In general, RPC registries may intercept calls and rewrite metadata. This can be useful if trusted, serving as an adapter layer. We can configure trusted registries and include trust criteria for publish and search. But it is feasible to force end-to-end encryption for introductions through untrusted registries.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. There are many interesting opportunities with this perspective. However, implementing a new GUI framework is a non-trivial task that should be done well or not at all. Thus, I'll defer support until I'm able to dedicate sufficient effort. Use HTTP or FFI in the meanwhile.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.
* `(%fork Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice, convenient for static analysis.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may compute forks in a very predictable pattern, some more frequently than others.

*Note:* Without transaction-loop optimizations for incremental computing and duplication on non-deterministic choice, a basic single-threaded event-loop will perform better. We can still use sparks for parallelism.

## Random Data

Instead of a stateful random number generator, the runtime will provide a stable, cryptographically random field. Of course, users can grab a little data to seed a PRNG.

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. Unique function per runtime instance. The Seed may be structured but is limited to plain old data.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes share the secret. To partition a random field within an application, an application can intercept `sys.random` to wrap Seed within a subprogram.

## Background Eval

In some scenarios, we can reasonably assume operations are 'safe' such as HTTP GET, triggering a lazy computation, or writing some metadata only for reflection-like purposes. In these cases, we might want an escape hatch from the transaction system, i.e. such that we can trigger the computation, await the result, and pretend this result is already present. 

A proposed mechanism is background eval:

* `sys.refl.bgeval(StaticMethodName, UserArg) : Result` - Evaluate `StaticMethodName(UserArg)` in a separate transaction. The caller waits for this to commit then continues with the returned Result. 

Intriguingly, stable bgeval integrates with incremental computing, and non-deterministic bgeval can implicitly fork the caller for each Result. We can apply transaction-loop optimizations. We can also abort bgeval together with the caller, in case of read-write conflict or live coding.

*Caveats:* Computation may 'thrash' if bgeval repeatedly conflicts with the caller. But this is easy to detect and debug. The new transaction receives the original 'sys.\*' effects API, which may constitute a privilege escalation. But we should restrict untrusted code from reflection APIs in general.

## Time

A transaction can query a clock. A repeating transaction can wait on that clock, i.e. by aborting the transaction until the time is right. However, repeatedly querying the clock and aborting is extremely inefficient, so we'll want to optimize this pattern.

* `sys.time.now()` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with flexible precision. Multiple queries to the same clock within a transaction will return the same value.
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. A runtime can easily optimize this to wait for the specified time. The runtime can also precompute the transaction slightly ahead of time and hold it ready to commit, a viable basis for soft real-time systems.

In context of a distributed runtime and network partitioning, each node maintains a local estimate of the runtime clock. When the nodes interact, we don't need to synchronize clocks, but we must track the maximum observed timestamp (received from 'now' or passed to 'await') that might have contributed to an interaction to ensure we observe a monotonic distributed clock. Fixing clock drift and aligning with true time is best left to NTP or PTP.

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

* `sys.tty.write(Binary)` - write to standard output, buffered until commit. 
* `sys.tty.read(N) : Binary` - read from standard input. Diverges if not enough data.
* `sys.tty.unread(Binary)` - add Binary to head of input buffer for future reads.
* `sys.tty.ctrl(Hint)` - ad hoc control, extensible but mostly for line buffering and echo

The control hint is runtime specific, perhaps something like `(icanon:on, ...)`. I expect to use standard error for runtime use - compile-time warnings, logging, etc..

*Note:* An application or adapter could redirect or mirror `sys.tty.*`, perhaps to 'http' and [xterm.js](https://xtermjs.org/).

## Foreign Function Interface (FFI)

The only FFI of any relevance for system integration is calling functions on a ".so" or ".dll" using the C ABI. C doesn't have any native support for transactions, but we can freely buffer a sequence of C calls to run between transactions. To ensure sequencing, I propose to stream commands and queries to FFI threads. To mitigate risk, FFI threads may run in separate OS processes. To mitigate latency, a simple environment enables users to pipeline outputs from one C call as input to another.

A viable API:

        struct buffer { atomic_int refct; size_t size; uint8_t* data; }

        TypeHint:
            p   - pointer (void*)
            y,Y - int8, uint8
            s,S - int16, uint16
            w,W - int32, uint32
            q,Q - int64, uint64
            i,I - int, unsigned int
              Z - size_t
            f   - float
            d   - double

* `sys.ffi.*`
  * `new(Hint) : FFI` - Return a runtime-scoped reference to a new FFI thread. Hint guides integration, such as creating or attaching to a process, or at which node in a distributed runtime. The FFI thread will start with with a fresh environment.
  * `fork(FFI) : FFI` - Clone the FFI thread. The clone gets copy of query results and the remote thread-local environment (stacks and registers), and runs in a new thread. The heap is shared between forks.
  * `status(FFI) : FFIStatus` - recent status of FFI thread:
    * *uncommitted* - initial status for a 'new' or 'fork' FFI.
    * *busy* - ongoing activity in the background - setup, commands, or queries
    * *ready* - FFI thread is halted in a good state, can receive more requests.
    * *error:(text:Message, ...)* - FFI thread is halted in a bad state and cannot receive any more commands or queries. The error is a dict with at least a text message. The FFI cannot receive any more commands or queries.
  * `link.lib(FFI, SharedObject)` - SharedObject is runtime or adapter specific, but should indirectly translate to a ".dll" or ".so" file. When looking up a symbol, last linked is first searched.
  * `link.c.hdr(FFI, APIName, Text)` - redirects `#include<APIName>` to Text in future 'c.src'.
  * `link.c.src(FFI, Text)` - JIT-compile C source into memory and link (via Tiny C Compiler).
  * `call(FFI, Symbol, TypeHint)` - call a previously linked symbol. Parameters and results are taken from the data stack. TypeHint for `int (*)(float, size_t, void*)` is `"fZp-i"`. In this case, pointer 'p' should be at the top of the data stack. Void type is elided, e.g. `void (*)()` is simply `"-"`.
  * `mem.write(FFI, Binary)` - (`"p-"`) copy a runtime Binary into FFI thread memory, starting at a pointer provided on the data stack. A sample usage pattern: push length, call malloc, copy pointer, write. 
  * `mem.read(FFI, Var)` - (`"pZ-"`) given a pointer and size on the data stack, read FFI process memory into an immutable runtime Binary value. This binary is eventually returned through Var.
  * `push(FFI, List of Data, TypeHint)` - adds primitive data to the stack. TypeHint should have form `"-fZp"`. In this example, pointer should be last item in list and is pushed last to top of data stack. The TypeHint influences data conversion, and may raise an error - blocking the transaction - for lossy integer conversions. See 'sys.ffi.ptr.\*' for pointers.
  * `peek(FFI, Var, N)` - copy N items from top of stack into future Var. If N is 0, this reduces to a status check. Some caveats: a floating-point NaN or infinity will result in error queries, and FFI pointers are abstracted (see 'sys.ffi.ptr.\*').
  * `copy(FFI, N)` - copy top N items on stack
  * `drop(FFI, N)` - remove top N items from stack
  * `xchg(FFI, Text)` - ad hoc stack manipulation, described visually. The Text `"abc-abcabc"` is equivalent to 'copy(3)'. In this example, 'c' is top of stack. Mechanically, we find '-', scan backwards popping values from stack into single-assignment temporary local variables 'a-z', then scan forwards from '-' to push variables back onto the stack.
  * `stash(FFI, N)` - move top N items from data stack to top of auxilliary stack, called stash. Preserves order: top of stack becomes top of stash.
  * `stash.pop(FFI, N)` - move top N items from top of auxilliary stack to top of data stack.
  * `reg.store(FFI, Reg)` - pop data from stack into a local register of the FFI thread. Register names should be short strings.
  * `reg.load(FFI, Reg)` - copy data from local register of FFI thread onto data stack.    
  * `var.*` - a future result for 'peek', 'mem.read', and other feedback from FFI thread. Vars must be dropped before reuse.
    * `read(FFI, Var) : Data` - Receive result from a prior query. Will diverge if not *ready*.
    * `drop(FFI, Var)` - Remove result and enable reuse of Var.
    * `list(FFI) : List of Var` - Browse local environment of query results.
    * `status(FFI, Var) : VarStatus`
      * *undefined* - variable was dropped or never defined
      * *uncommitted* - commit transaction to send the query
      * *pending* - query enqueued, result in the future
      * *ready* - data is ready, can read in current transaction
      * *error:(text:Message, ...)* - problem that does not halt FFI thread.
      * *canceled* - FFI thread halted before query returned. See FFI status.
  * `ptr.*` - safety on a footgun; abstract Ptr is runtime scoped, explicitly cast, bound to a process
    * `addr(FFI, Ptr) : Int` - view pointer as integer (per intptr_t). Error if FFI thread does not belong to same OS process as Ptr.
    * `cast(FFI, Int) : Ptr` - treat any integer as a pointer
    * `null() : Ptr` - pointer with 0 addr, accepted by any FFI

This API is designed in context of [libffi](https://en.wikipedia.org/wiki/Libffi) and the [Tiny C Compiler (TCC)](https://bellard.org/tcc/). For the latter, a [recent version](https://github.com/frida/tinycc/tree/main) lets us redirect `#include` and resolve missing symbols via callbacks.

*Aside:* It is unclear what should happen with the FFI process stdin, stdout, and stderr file streams. These can feasibly be redirected to the runtime and made available through reflection. Perhaps we can present an xterm.js per FFI process via `"/sys/ffi"`.

## Filesystem, Network, Native GUI, Etc.

These APIs are specialized wrappers for FFI. Instead of implementing them in the runtime, I will leave them to application or adapter. Where feasible, we should extend these APIs a little to be more friendly for pipelining, and perhaps to cover useful utility patterns such as full file reads or HTTP requests.

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

## Securing Applications

Not every program should be trusted with FFI, shared state, and other sensitive resources. This is true within a curated community configuration, and even more true with external scripts of ambiguous provenance. So, what can be done?

A configuration can describe who the user trusts or how to find this information. With a few conventions, when loading files we could poke around within a folder or repository for signed manifests and certifications of public signatures. A compiler can generate record of sources contributing to each definition - locations, secure hashes, other definitions, etc. - even accounting for overrides and 'eval' of macros.

Instead of a binary decision for the entire application, perhaps we can isolate trust to specific definitions. Via annotations, some trusted definitions might express that they properly sandbox FFI, permitting calls from less-trusted definitions. And perhaps we can express a gradient of trust with our signatures, e.g. that a given definition can be trusted with 'filesystem access' instead of a role like 'full FFI access'. 

Ultimately, we can run untrusted applications in trusted sandboxes, and a trusted application adapter can construct sandboxes upon request based on application settings. If an application asks for more authority than it's trusted to receive, the adapter won't stop it: the adapter observes only application settings and runtime version info. Instead, the runtime will examine the final application, see that FFI or abstract resources are being used by untrusted functions without sufficient sandboxing, and reject the application. At that point, we can abandon the app, further sandbox the app, or extend trust.
