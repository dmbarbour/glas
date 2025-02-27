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

Applications receive access to a database with support for atomic transactions, structured data, and content-addressed storage. Portions of this database may be persistent and shared with other applications. I propose to structure this as a key-value database with abstract, runtime-scoped keys. A subset of keys are *ephemeral*, thus referring to runtime memory and permitting runtime-scoped data (including keys). In the persistent database, we're limitied to plain old data, but we can still model *arcs* between things.

A viable API:

* `sys.db.*` - a key-value database with abstract keys. 
  * `root : Key` - initial access to a database. Runtime-scoped, but usually persistent.
  * `key.*` - referencing the database
    * `dir(Key, Data) : Key` - access subdirectory labeled by plain old data. 
    * `arc(Key, Key) : Key` - access subdirectory labeled by another key. This serves as a basis for associative structure. Unlike dirs, arcs cannot be listed (modulo reflection) so GC can treat the key as a weak reference.  
    * `eph(Key) : Key` - arc via implicit ephemeral key with runtime lifespan.
    * `gen() : Key` - runtime allocates a new ephemeral key. Subject to garbage collection.
  * `dir.*` - support for browsing the database
    * `list(Key) : List of Data` - list edge labels for active directories in the database. An active directory has at least one defined value. (There is no equivalent for *arcs*, modulo reflection APIs.)
    * `clear(Key)` - transitively delete everything reachable from this directory (val, dirs, arcs).
    * `clone(Key, Key)` - transitively copy from one directory to another. Target must be empty.
  * `val.*` - every key may have an associated value
    * `get(Key) : Data` - copy data at key. diverges if key has no associated value.
    * `set(Key, Data) : Data` - write data into key. 
    * `del(Key)` - reset key to an undefined state.  
    * `has(Key) : Bool` - observe if key has value without observing the value
    * `take(Key) : Data` - remove and return data at key. Diverges if key has no value.
    * `put(Key, Data)` - place data into undefined key. Diverges if key has a value.
    * `move(Key, Key)` - blindly 'take' from one key and 'put' to another.
    * `copy(Key, List of Key)` - blindly 'get' from one key and 'set' to one or more keys. 
  * `queue.*` - specialized view of list state. A runtime can evaluate multiple blind writer transactions and a single reader in parallel without risk of serializability conflicts.
    * `read(Key, N) : List of Data` - reader takes items from head of list. Diverges if not a list or insufficient data.
    * `push(Key, List of Data)` - reader pushes items to head of list for future reads (i.e. use as stack or deque)
    * `write(Key, List of Data)` - addends items to tail of list. Diverges if not a list.
    * `wire(Key, N, Key)` - (tentative) blindly read N items from one queue and write them to another 
  * `bag.*` - essentially, an unordered queue. Reads and writes operate on non-deterministic locations in the list. Useful for distributed systems. 
    * `read(Bag) : Data` - read and remove data, non-deterministic choice. Diverges if bag is empty.
    * `write(Bag, Data)` - add item to bag, non-deterministic position in list.
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

If an application implements 'rpc' it may receive remote procedure calls (RPC).

        rpc : (MethodRef, UserArg) -> [callback, sys] Result

The UserArg and Result values are exchanged with the caller. Optionally, limited interaction may be supported as a callback via algebraic effects. The MethodRef is instead a runtime parameter, relating to how RPC is registered and published. The runtime will map between local use of MethodRef and external use of GUIDs or URLs.

RPC must be configured. The simplest solution is to declare a static API via application settings. Alternatively, settings could specify a MethodRef to fetch a dynamic API. Either way, I propose to organize RPC methods into 'objects' that are published to different registries based on trust and roles. 

A prospective caller will query for RPC objects matching an interface and metadata.

*Note:* One point I'm still uncertain of is whether we should support 'stateful' subscription for RPC objects, or a more reactive query. I suspect the stateful approach would be more efficient but would result in applications that don't adapt easily to open systems and disruption. Can we mitigate the performance hit? Incremental computing may help.

* `sys.rpc.*` - discover and invoke RPC resources
  * 



To enhance performance, I hope to support annotation-guided code distribution. The 'rpc' method can be partially evaluated based on MethodRef, then have some code extracted for evaluation at the caller. A caller can similarly forward part of the callback code and continuation. These optimizations would mitigate performance pressures, supporting simplified remote APIs.


## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. There are many interesting opportunities with this perspective. However, implementing a new GUI framework is a non-trivial task that should be done well or not at all. Thus, I'll defer support until I'm able to dedicate sufficient effort. Use HTTP or FFI in the meanwhile.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.
* `(%fork Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice, convenient for static analysis.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may compute forks in a very predictable pattern, some more frequently than others.

*Note:* Without optimizations for incremental computing and cloning on non-deterministic choice, fork is not very useable. Better off modeling a single-threaded event loop. 

## Random Data

A stateful random number generator is awkward in context of concurrency, distribution, and incremental computing. However, we can easily provide access to a stable, cryptographically random field.

* `sys.random(Seed, N) : Binary` - (pure) return a list of N cryptographically random bytes, uniformly distributed. Unique function per runtime instance.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes share the secret. To partition a random field within an application, an application can intercept `sys.random` to wrap Seed within a subprogram. The Seed may be structured, but it should be plain old data.

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

The only FFI of any relevance for system integration is calling functions on a ".so" or ".dll" using the C ABI. C doesn't have any native support for transactions, but we can freely buffer a sequence of C calls to run between transactions. To ensure sequencing, I propose to stream commands and queries to FFI threads. These FFI threads may run in separate OS processes to control risk. To mitigate latency, a simple environment can pipeline outputs from one C call as inputs to the next without intervention. 

A viable API:

* `sys.ffi.*`
  * `new(Hint) : FFI` - Return a runtime-scoped reference to a new FFI thread. Hint guides integration, such as creating or attaching to a process, or which node in a distributed runtime. The FFI thread will start with with a fresh environment and must link definitions before calling them.
  * `run(FFI, Cmd)` - Enqueue a command in the FFI thread.
  * `eval(FFI, Qry, Var)` - Enqueue a query in the FFI thread. The future result is stored to Var. If that Var is already in use, it is implicitly dropped and replaced.
  * `fork(FFI) : FFI` - Clone the FFI thread. The clone gets copy of query results and the remote thread-local environment (stack and registers), and runs in a new thread. Buffers and heap are shared.
  * `status(FFI) : FFIStatus` - recent status of FFI thread:
    * *uncommitted* - initial status for a 'new' or 'fork' FFI.
    * *busy* - ongoing activity in the background - setup, commands, or queries
    * *ready* - FFI thread is halted in a good state, can receive more requests.
    * *error:(text:Message, ...)* - FFI thread is halted in a bad state and cannot receive any more commands or queries. The error is a dict with at least a text message. The FFI cannot receive any more commands or queries.
  * `var.*` - access the local store for query results. The Var may be any plain old data.
    * `read(FFI, Var) : Data` - Receive result from a prior query. Will diverge if not *ready*.
    * `drop(FFI, Var)` - Remove current or pending result from FFI.
    * `list(FFI) : List of Var` - Browse your local environment.
    * `status(FFI, Var) : VarStatus` - A status per variable:
      * *undefined* - variable was dropped or never defined
      * *uncommitted* - query will run between transactions
      * *pending* - query enqueued, result in the future
      * *ready* - data is ready, can read it immediately
      * *error:(text:Message, ...)* - query problem. Does not stop the FFI thread.
      * *canceled* - FFI thread halted before running query. See FFI status.
  * `qry.*` - query the FFI environment. Queries are read-only, but there is some risk of breaking things when trying to read pointers beyond their length.
    * `stack(N) : Qry` - read top N items from stack, or full stack if N is zero.
    * `stack.at(N) : Qry` - read zero-indexed element from top of stack
    * `reg(Reg) : Qry` - read a register by name
  * `ptr.*` - When a pointer value is queried, the abstract, runtime-scoped Ptr type guards against accidents, such as sending a pointer to the wrong FFI process. 
    * `null() : Ptr` -  special case, can use NULL with any FFI thread.
    * `addr(FFI, Ptr) : Int` - address according to intptr_t. Asserts same FFI process.
    * `cast(FFI, Int) : Ptr` - cast address to pointer. Unsafe, but clearly intentional.
  * `cmd.*` - manipulate the FFI environment. A failed command will generally result in an 'error' state for the FFI thread.
    * `link.*`- when looking for a symbol, linked last is searched first.
      * `lib(SharedObject) : Cmd` - load a ".so" or ".dll" into environment (e.g. via dlopen).
        * *SharedObject* - runtime specific; runtime can check config, translate to file path.
      * `c.src(Text) : Cmd` - JIT compile a C source text that defines reusable utility functions.
      * `c.hdr(Text, APIName) : Cmd` - TCC will redirect `#include <APIName>` to this text.
    * `call(Symbol, TypeHint) : Cmd` - Call a previously loaded symbol using the data stack.
      * *TypeHint* - initially, `"fip-i"`, a compact string like `"fip-i"`. Interpreted as `int (*)(float, int, void*)`, and taking the top of stack as the first argument. Assumes '`__cdecl`' as the default. This is extensible, e.g. we can support `stdcall:"fip-i"` if needed.
    * `withc(Text, Cmd) : Cmd` - JIT the C source, run Cmd in scope scope of the C symbols, then free the memory. Users should be cautious regarding lifespan, e.g. for callbacks and threads.
    * `ctrl.*` - simple control code.
      * `dip(N, Cmd) : Cmd` - temporarily hide top N items from data stack while running Cmd.
      * `seq(List of Cmd) : Cmd` - run commands in a sequence. No-op is expressed as empty seq.
      * `cond(Cmd, Cmd) : Cmd` - pop top item from data stack. If non-zero, run lhs, otherwise rhs.
      * `loop(Reg, Cmd) : Cmd` - repeat Cmd while a specified Reg is non-zero. Cmd *must* store to Reg.
    * `data.*` - 
      * `copy(N) : Cmd` - copy top N items on data stack
      * `drop(N) : Cmd` - drop top N items on data stack
      * `move(Text) : Cmd` - ad hoc stack manipulations, e.g. `"abc-abcabc"` will copy three items, while `"xy-yx"` is a swap. Stores then loads temporary registers 'a' to 'z'. In this notation, 'a' is top of stack.
      * `store(List of Reg) : Cmd` - move from stack to a named register. In this case 'register' is effectively a local variable (but I'm already using 'Var' for the runtime side). An FFI thread can use a few hundred if necessary.
        * *Reg* - register names should be short texts. The runtime may translate them to indices within an array.
      * `load(List of Reg) : Cmd` - copy items from registers to data stack. Error if undefined.
      * `push(List of Data, TypeHint)` - add data to stack. Pushed in reverse order, such that top of stack is head of list. TypeHint is a simple text like `"ibfp"` for 'int, buffer, float, pointer' to guide interpretation. 
      * `bfr.*` - A binary or text argument can be pushed as a buffer, and a query will copy buffer back to the user as a binary. Reference counted in the FFI environment. 
        * *impl* - `struct buffer { atomic_int refct; void* data; size_t size; void* (*realloc)(void*, size_t); }`. 
        * *C strings* - to keep it simple, we'll overallocate then addend a NULL byte.
        * *call* - a buffer may be used as a pointer argument to 'call'. We decref *after* the call. 
        * `alloc() : Cmd` - (`i-b`) allocate a new buffer of given size. 
        * `realloc() : Cmd` - (`bi-b`) resize buffer. Be careful about concurrent use.
        * `dup() : Cmd` - (`b-b`) allocates a fresh buffer with the same content 
        * `len() : Cmd` - (`b-i`) obtain length from buffer. This is length of data, does not include extra NULL bytes.
        * `ptr() : Cmd` - (`b-p`) obtain pointer from buffer. 
* *TypeHint* - A compact representation of types for efficient interaction with FFI. 
  * 'b' - buffer. This is used to push or query binaries and texts. We'll implicitly cast a buffer to a pointer for a call, deferring decref until after the call returns.
  * 'p' - pointer. '`void*`'. The runtime abstracts pointers to guard against some accident, but it's even better to shove pointers into registers and never send them to the runtime. The referent type is not tracked, so it's something users must be careful with.
  * Integers - the API will use lower-case for signed types, upper-case for unsigned. Sign is relevant when we push or query numbers, as the glas system uses a variable-width integer encoding and negatives are in one's complement. If we attempt to push a number outside the range, we'll reject that transaction and raise an error.
    * y,Y - int8, uint8
    * s,S - int16, uint16
    * w,W - int32, uint32
    * q,Q - int64, uint64
    * i,I - int, unsigned int
    *   Z - size_t
  * 'f' float, 'd' double. On the runtime side, we use exact rational numbers. Conversion to floating point is lossy. Conversion to rationals is exact except for not-a-number or infinities. Those cases result in a query error, including the original float as a binary. To avoid automatic conversions, users could instead encode floats into a buffer.
  * Void type - implicit. A call to a function of type `void (*)(int, size_t)` would use TypeHint `"iZ-"`.

For calls, we can use [libffi](https://en.wikipedia.org/wiki/Libffi). The TypeHint options are influenced by what libffi supports. For the C JIT, we can use the [Tiny C Compiler (TCC)](https://bellard.org/tcc/). *Note:* I'd use a [recent version](https://github.com/frida/tinycc/tree/main) that lets callbacks redirect `#include` and resolve missing symbols.

*Aside:* We should probably redirect stdin, stdout, and stderr per FFI process. But I'm not sure what to do with them in general. Perhaps make them available via runtime reflection APIs. In the runtime's web interface, we can present an xterm.js per attached FFI process.

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

## Filesystem and Network

I propose to implement filesystem and network APIs in terms of FFI. We don't get much out of these APIs other than a safety wrapper around FFI, and that can be left to applications or adapters to avoid cluttering the runtime.

The conventional APIs should be adapted for better pipelining. For example, even if 'open' has an error, it's best if we can safely continue applying several operations before checking the result. This might benefit from a null object pattern. Useful utility functions might involve reading or writing whole files in one step, or performing HTTP requests in one step. 

## Securing Applications

Not every program should be trusted with FFI, shared state, and other sensitive resources. This is true within a curated community configuration, and it is even more true with external scripts of ambiguous provenance. So, what can be done?

A configuration can describe who the user trusts or how to find this information. With a few conventions, when loading files we could poke around within a folder or repository for signed manifests and certifications of public signatures. A compiler can generate record of sources contributing to each definition - locations, secure hashes, other definitions, etc. - even accounting for overrides and 'eval' of macros.

Instead of a binary decision for the entire application, perhaps we can isolate trust to specific definitions. Via annotations, some trusted definitions might express that they properly sandbox FFI, permitting calls from less-trusted definitions. And perhaps we can support a gradient of trust with our signatures, e.g. trust with specific roles, such as access to the filesystem instead of full FFI. 

Ultimately, we can run untrusted applications in trusted sandboxes, and a trusted application adapter can construct sandboxes upon request based on application settings. If the application asks for more authority than it's trusted to receive, the adapter won't stop it: the adapter only observes application settings and runtime version info, it's ignorant of security concerns. Instead, it's the runtime that would look at the final web of definitions and say "no, this untrusted definition is using FFI". At that point, we can abandon the app, sandbox the app, or extend trust.
