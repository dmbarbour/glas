# Glas Applications

Applications are represented as a set of methods within a [namespace](GlasNamespaces.md). The [glas executable](GlasCLI.md) can reference applications in the user's configuration namespace, typically at 'env.appname.app.\*', or by loading a script and extracting 'app.\*'. These naming conventions simplify composition, recognition, and extraction of applications.

Every application must define 'app.settings' to guide integration. The executable does not observe settings directly. Instead, for reasons of portability and security, the configuration generates an adapter based on application settings and runtime version info.

The glas executable may support multiple run modes. For example:

* *transaction-loop applications* - repeatedly evaluate 'step' in separate transactions, rely on incremental computing and non-deterministic choice as basis for concurrency.
* *threaded applications* - long-running procedure evaluated over the course of multiple transactions, may spawn many such 'threads'. Conventional 'main' procedure.
* *staged applications* - generate another application based on command-line arguments, then run that.

The adapter must heuristically select the best run mode based on application settings and runtime version info. This document focuses on the transaction loop, which is a very nice fit for my vision of glas systems. However, threaded applications and staging are discussed towards the end.

## The Transaction Loop

A *transaction loop* is a very simple idea - a repeating transaction, atomic and isolated. A direct implementation is nothing special; at best, it makes a convenient event dispatch loop, friendly for live coding and orthogonal persistence. However, consider a few optimizations we can apply: 

* *incremental computing* - We don't always need to repeat the entire transaction from the start. Instead, roll-back and recompute based on changed inputs. If repeating the transaction is obviously unproductive, e.g. it aborts, then we don't need to repeat at all until conditions change. If productive, we still get a smaller, tighter repeating transaction that can be optimized with partial evaluation of the stable inputs.
* *duplication on non-deterministic choice* - If the computation is stable up to the choice, then duplication is subject to incremental computing. This provides a basis for concurrency, a set of threads reactive to observed conditions. If the computation is unstable, then at most one will commit while the other aborts (due to read-write conflict), representing a race condition or search. Both possibilities are useful.
* *distributed runtimes* - A distributed runtime can mirror a repeating transaction across multiple nodes. For performance, we can heuristically abort transactions best started on another node, e.g. due to locality of resources. Under incremental computing and duplication on non-deterministic choice, this results in different transactions running on different nodes. In case of network partitioning, transactions in each partition may run independently insofar as we can guarantee serializability (e.g. by tracking 'ownership' of variables). An application can be architected such that *most* transactions fully evaluate on a single node and communicate asynchronously through runtime-supported queues, bags, or CRDTs. 

If implemented, the transaction loop covers many more use-cases - reactive systems, concurrency, distributed systems with graceful degradation and resilience. Applications must still be designed to fully leverage these features, this is simplified by a comprehensible core: users can easily understand or debug each transaction in isolation. Also, even with these optimizations, the transaction loop remains friendly for live coding and orthogonal persistence. 

Further, there are several lesser optimizations we might apply:

* *congestion control* - A runtime can heuristically favor repeating transactions that fill empty queues or empty full queues.
* *conflict avoidance* - A runtime can arrange for transactions that will likely conflict to evaluate in different time slots. This reduces the amount of rework.
* *soft real-time* - A repeating transaction can 'wait' for a point in time by observing a clock then diverging. A runtime can precompute the transaction slightly ahead of time and have it ready to commit.
* *loop fusion* - A runtime can identify repeating transactions that compose nicely and create larger transactions, allowing for additional optimizations. 

These optimizations don't open new opportunities, but they can simplify life for the programmer.

*Note:* A conventional process or procedure can be modeled in terms of a repeating transaction that always writes state about the next step. Of course, there is a performance hit compared to directly running concurrent procedures.

## Transaction-Loop Applications

A transaction-loop application might define several transactional methods:

* 'app.start' - Set initial state, perform initial checks. Retried until it commits once.
* 'app.step' - After start, repeatedly run 'step' until voluntary 'sys.halt' or killed.
* 'app.http' - Handle HTTP requests between steps. Our initial basis for GUI and events.
* 'app.rpc' - Transactional inter-process communications. Multiple calls in one transaction. Callback via algebraic effects.
* 'app.gui' - Like an immediate-mode GUI. Reflective - renders without commit. See [Glas GUI](GlasGUI.md).
* 'app.switch' - First transaction in new code after live update. Old code runs until successful switch.

Naturally, there are limits on what can be achieved in a single transaction. FFI is expressed in terms of buffering commands to an FFI 'thread', then reading results in a future transaction.

## Application State

A runtime can provide a familiar and flexible heap-allocation API. Part of this heap may be persistent, with a global variables shared between applications through a configured database. Heap references are abstracted to simplify garbage collection, control scope, and support type annotations. A viable API (tentative):

* `sys.heap.*` - 
  * `scope.*` - Data from a longer-lived scope may be stored into a shorter-lived scope, but not vice versa. Dynamically enforced via tagged pointers.
    * `db : Scope` - persistent, shared state through a configured database, with flexible heap refs.
    * `rt : Scope` - runtime or process resources like open files, network connections, FFI threads.
    * `tn : Scope` - implicitly cleared before commit, can enforce protocols via linear types.
    * `cmp.eq(Scope, Scope) : Boolean` - returns whether two scopes are the same.
    * `cmp.ge(Scope, Scope) : Boolean` - true iff lifespan of left scope greater or equal to right scope
  * `var(Scope, Name) : Ref` - global variable of given scope and name, serves as a garbage collection root. Name should be a short, meaningful text. The default value for a unassigned variable is zero, but this may be influenced by declared schema or usage hints.
  * `ref.*` - new refs, weak refs, reference equality
    * `new(Scope, Data) : Ref` - new anonymous reference at given scope, garbage collected upon becoming unreachable. A new shared-scope Ref may be represented entirely within the runtime until it must be serialized to the shared database. Diverges with type error if scope is incompatible with data.
    * `scope(Ref) : Scope` - return scope of a given reference
    * `weak(Ref) : WeakRef` - returns a [weak reference](https://en.wikipedia.org/wiki/Weak_reference). A garbage collector may collect Refs only reachable through WeakRefs. To access the Ref, use 'fetch' or 'tryfetch'.
    * `weak.fetch(WeakRef) : Ref` - obtain the strong reference associated with a weak reference. If the associated Ref has already been garbage collected, this operation will diverge. See also: 'tryfetch' from the reflection API.
    * `weak.cache(Ref, CacheHint) : WeakRef` - a variant WeakRef to support manual caching. The garbage collector keeps Ref in memory until there is sufficient memory pressure, with heuristic guidance from CacheHint and history of use.
    * `cmp.eq(Ref, Ref) : Boolean` - reference equality, asks whether two Refs are the same.
  * `cell.*` - whole-value heap ops
    * `get(Ref) : Data` - read the heap value, returns a copy.
    * `set(Ref, Data)` - modify the value for future access, drops prior value.
    * `swap(Ref, Data) : Data` - get and set as one operation (for linear types).
  * `slot.*` - option values (singleton or empty list) can be treated like a mutex.
    * `take(Ref) : Data` - if Ref has data, swap with empty and return data. Otherwise diverge.
    * `put(Ref, Data)` - if Ref has no data, swap with singleton containing data. Otherwise diverge.
  * `queue.*` - a cell containing a list can be treated as a queue. A runtime can potentially optimize queue operations, supporting a single reader and multiple writers in parallel. This involves buffering writes locally then ordering write buffers based on final serialization of transactions.
    * `read(Ref, N) : List of Data` - remove and return exactly N items from head of list, or diverge
    * `unread(Ref, List of Data)` - reader adds items to head of list for a future read
    * `peek(Ref, N) : List of Data` - same as read then unread a copy.
    * `write(Ref, List of Data)` - add data to end of queue for a future reader.
  * `bag.*` - an unordered queue. Reads and writes operate on non-deterministic locations in the list. Allows multiple readers and writers in parallel, each operating on a partition. However, also at risk from combinatorial explosions of 
    * `write(Ref, Data)` - add item to bag, non-deterministic position in list.
    * `read(Ref) : Data` - read and remove data from a non-deterministic position in the list. Diverge if bag is empty. 
    * `peek(Ref) : Data` - read data non-deterministically without removing it.
  * `index.*` - (tentative) logically view a cell containing a dict as a dict of cells, and similar for arrays. This allows fine-grained read-write conflict analysis per index and whole-value operations. Indexed refs may be (perhaps temporarily) invalid, e.g. binding index 42 of a 40-element array. Data operations on invalid refs diverge. *Caveats:* Need to try this out, see if it's difficult to implement efficiently.
    * `dict(Ref, Name) : Ref` - Return Ref to access a field in referenced dict. Name is binary excluding NULL.  
    * `array(Ref, N) : Ref` - Return Ref to access an offset into referenced list or array.
    * `array.slice(Ref, Offset, Len) : Ref` - Ref to a slice of an array. Operations that would shrink or grow the array will instead diverge.
  * *CRDTs* - (tentative) runtime support for cells modeling [conflict-free replicated data types (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are useful for network partitioning tolerance in a distributed system. See *Distributed State*. However, a lot of research is needed to choose suitable CRDTs.
* `sys.refl.heap.*` - methods for reflection on the database.
  * `ref.weak.tryfetch(WeakRef) : opt Ref` - returns a singleton list containing Ref if available, otherwise empty list. This operation indirectly observes the garbage collector.
  * `ref.hash(Ref) : N` - returns a stable natural number per Ref suitable for use with hashmaps or hashtables. 
  * `ref.usage(Ref, Hint)` - runtime-specific hints regarding usage of a ref. This may include representation schema, initial values for vars, or migration suggestions for a distributed runtime.
  * `var.iter(Scope, Text) : Name | empty` - seeks Name of next defined var in lexicographic order from given Text. Returns empty name if there is no successor. A var may be garbage collected if the Ref contains zero and is unreachable except via 'var'. 
  * `queue.avail(Ref) : N` - returns number of items are locally available to a reader, i.e. without divergence or a distributed transaction.

The shared heap should support atomic transactions, structured data, and content-addressed storage. Transactions significantly mitigate many challenges with coordinating shared state. Structured data and content-addressed storage enable a shared heap to work with *very large* values, especially in context of [persistent data structures](https://en.wikipedia.org/wiki/Persistent_data_structure). Large binaries could be represented as finger-tree ropes.

Trust and access control for shared state require attention. See *Securing Applications*. A configurable security policy might express role restrictions in terms of regex on var names. Trusted application adapters or libraries can provide sandboxed access to shared state, presenting some volumes of shared state in terms of mailboxes, databuses, publish-subscribe, or a relational database.

*Note:* Ideally, we can support static allocation, memory layout, unboxed representations. This seems feasible insofar as we insist on static 'var' (scope and name), and static 'ref.usage' hints and type annotations on those vars.

### Distributed State

A distributed runtime can be configured to use a distributed database as a shared heap. Both shared and runtime heaps may distribute 'partial' cells, e.g. splitting a queue into a read buffer and write buffer. This allows reader and writer to continue processing during a temporary network disruption, and lets us separate the writer transaction from a distributed operation to move data between buffers.

What optimizations are viable?

* cell - owned by a writer, may be cached read-only on reader nodes
* slot - single owner for read and write, migrate as needed, no caching
* queue - reader and writer buffers may implicitly be owned by separate nodes, writer buffers data while the network is partitioned. Reader may eventually react to the disruption via timeouts.
* bag - partition list across multiple nodes. Each node can read and write to local partition while network. The runtime opportunistically shuffles data between partitions when the network is up. Useful for load balancing.
* index - distribute ownership per index based on how the ref is used.
* CRDTs - replicate to every node, interact with locally, and synchronize replicas when nodes interact.

In general we should consider *permanent* network disruptions, e.g. destruction of a node. In this context, ownership by a single node can be a problem. For critical Refs, such as shared or runtime vars, we might favor use consensus algorithms instead of ownership by a single Ref. But requiring consensus for every update is expensive. We might favor indirection, such that a non-critical intermediate Ref can be updated efficiently by a node, or detached and replaced (with consensus) after a timeout.

### Stack State

Instead of a dynamic heap, we could present application state as a set of local vars allocated on a call stack below 'step' or 'rpc', available to application methods through a set of algebraic effects handlers. A subset of state can bind to a shared database, perhaps based on prefix in the naming convention.

There are no first-class heap 'Refs' to local vars. However, we will need a robust alternative to 'sys.heap.index.\*' for stable, efficient dynamic structure and fine-grained conflict analysis. Perhaps we could approach this in terms of lenses or prisms in the program model, or perhaps some means of passing higher-order 'queries' through handlers. In any case, this is a problem for the program model to resolve in general.

My intuition is that avoiding heap Refs is a good thing. Local vars on a call stack are easier to render, comprehend, compose, and extend compared to a mess of heap refs, and we'll get static types and layout much more conveniently. Of course, the two models are easily combined, and it is very conventional to do so. Perhaps this will mostly replace 'sys.heap.var', serving as application root state.

## HTTP Interface

Based on application-specific configuration, a runtime will open TCP ports to receive HTTP and RPC requests. A configurable subset of HTTP requests, perhaps `"/sys/*"`, will be routed to the runtime-provided 'sys.refl.dbg.http' to support administration and debugging. Other HTTP requests are routed to the application via 'app.http', if defined. 

        app.http : Request -> [sys] Response

The Request and Response are binaries. They may be *accelerated* binaries with structure under-the-hood that can be efficiently queried, manipulated, and validated. Each request runs in a separate transaction. If a request fails to generate a valid response, it is logically retried until timeout, serving as a simple basis for long polling. Asynchronous operations can immediately return "303 See Other", allowing the caller to fetch the result in a future query.

If there is sufficient demand, we can extend this API to accept WebSockets, perhaps via 'app.http.ws'. We can also invent HTTP headers to handle multiple requests in one atomic transaction. To effectively use 'app.http' as an early basis for GUI, we could configure the runtime to open a browser window when the application is started.

*Note:* The application-specific configuration might also describe integration with SSO to support multiple users and roles for the built-in HTTP interface.

## Remote Procedure Calls

To receive RPC calls, an application should declare an RPC API in application settings, and define a dispatch method:

        app.rpc : (MethodRef, List of Arg) -> [cb, sys] Result

The MethodRef is application specific and should be suitable for efficient dynamic dispatch. Initial MethodRefs are declared in application settings, but settings may specify a protocol to discover more methods at runtime. For security reasons, MethodRef is not directly published to the client. Instead, the client receives a cryptographic token that the runtime maps to MethodRef. This supports non-homogenous publishing and efficient revocation of published interfaces.

An application's RPC API is organized as a set of objects, each with methods and metadata. Based on metadata, the runtime publishes each object to a subset of configured registries. For example, an object that requires high trust might be published only to a private registry. Concurrently, the prospective client subscribes for RPC objects meeting some arbitrary criteria, with methods, metadata, and provenance.

A viable API:

* `sys.rpc.sub(Criteria) : RpcSub` - subscription for RPC objects. RpcSub is runtime scoped. Criteria is runtime specific. May start in a ready status due to caching. Is not explicitly closed, but may be garbage collected.
* `sys.rpc.sub.ready(RpcSub) : Boolean` - returns whether an RpcObj is available for 'recv'.
* `sys.rpc.sub.recv(RpcSub) : RpcObj` -  return the next available RpcObj, or diverge if not ready. RpcObj is runtime scoped.
* `sys.rpc.sub.reset(RpcSub)` - resets every RpcObj previously received from this RpcSub.
* `sys.rpc.obj.valid(RpcObj) : Boolean` - an RpcObj may be marked invalid due to remote applications closing, network disruption, or manually via 'reset'. If marked invalid, calls diverge, but the object may be received anew from the origin RpcSub.
* `sys.rpc.obj.reset(RpcObj)` - marks RpcObj invalid. Idempotent.
* `sys.rpc.obj.call(RpcObj, MethodName, List of Arg) : [cb] Result` - initiate a remote call to a valid RpcObj. In addition to data arguments, a callback handler is integrated as an algebraic effect to support interaction with the caller. Args and Result are limited to global-scoped data (i.e. no Refs, FFI thread handles, RpcObj or RpcSub, etc.)
  * `cb : (MethodName, List of Arg) -> [cb] Result` - Callbacks recursively receive callback handlers. Depth of recursion is limited by protocols or quotas.
* `sys.rpc.obj.prop(RpcObj, Name) : opt Data` - return metadata associated with RpcObj; basically, any property we could filter in Criteria. Properties are immutable, but a runtime may stabilize RpcObj over changes to unobserved properties. 
* `sys.rpc.obj.prop.list(RpcObj) : List of Name` - list all available properties.

To enhance performance, I hope to support systematic code distribution, such that some calls or callbacks can be handled locally. Guided by annotations, an 'rpc' method may be partially evaluated based on MethodRef and have code extracted for evaluation at the caller. We can do the same with 'cb', partially evaluating based on MethodName. To support pipelining of multiple calls and remote processing of results, we could even send part of the continuation. However, this is all very theoretical at the moment.

The notion that network disruption invalidates an RpcObj is debatable in context of distributed runtimes. Even if one node loses access, another might retain access. In context of distributed runtimes, we might indicate how multi-homing is handled in the search criteria.

*Note:* A runtime may publish RPC methods to support integrated development environments and debuggers. This might be presented within an application as 'sys.refl.dbg.rpc'. We'll additionally want reflection on RPC registries and resources, perhaps 'sys.refl.rpc.\*'. 

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. There are many interesting opportunities with this perspective. However, implementing a new GUI framework is a non-trivial task that should be done well or not at all. Thus, I'll defer support until I'm able to dedicate sufficient effort. Use HTTP or FFI in the meanwhile.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.select(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.
* `(%select Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice, convenient for static analysis.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may be very predictable, and may heuristically choose for performance reasons.

*Note:* Without transaction-loop optimizations for incremental computing and duplication on non-deterministic choice, a basic single-threaded event-loop will perform better. We can still use sparks for parallelism.

## Random Data

Instead of a stateful random number generator, the runtime will provide a stable, cryptographically random field. 

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The result varies on Seed, N, and runtime instance. The Seed may be structured but is limited to plain old data.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes share the secret. 

## Background Transactions

In some use cases, we want an escape hatch from transactional isolation. This occurs frequently when wrapping FFI with 'safe' APIs. We might support HTTP GET within a single transaction, trigger lazy computations, or manually maintain a cache. To support these scenarios, I propose a reflection API to run a transaction prior to the calling transaction:

* `sys.refl.bgcall(StaticMethodName, Args) : Result` - asks the runtime to call the indicated method in a separate transaction, wait for commit, then continue the current transaction with the result. If the caller aborts, e.g. due to read-write conflict with a concurrent transaction, an incomplete bgcall may be aborted. If bgcall aborts, it is implicitly retried. In general, args and result must be non-linear and global scoped. 
  * *StaticMethodName* - for example 'n:"MethodName"' in the abstract assembly, subject to namespace translations. The indicated method will receive the same 'sys.\*' environment passed to application 'step', independent of the caller's environment.

The bgcall logically runs before the caller, time travel of a limited nature. There is risk of transaction conflict, a time travel 'paradox' where the bgcall modifies something the caller previously observed. In this case, we still commit the bgcall, but then we abort the caller. In context of transaction loops, we rollback and replay the caller from where change is observed. If careless this leads to data loss or thrashing, but is easily mitigated by manual caching.

Insofar as paradoxes are avoided, background transactions are compatible with transaction-loop optimizations: a bgcall with stable results can be part of an incremental computing prefix, and a non-deterministic bgcall can clone a stable caller per result. Intriguingly, only the result needs to be stable: the bgcall could be processing an event queue in the background and returning 'ok' every time, modeling a background 'step' function active only while a caller is waiting.

## Time

A transaction can query a clock. A repeating transaction can wait on that clock, i.e. by aborting the transaction until the time is right. However, repeatedly querying the clock and aborting is extremely inefficient, so we'll want to optimize this pattern.

* `sys.time.now()` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with flexible precision. Multiple queries to the same clock within a transaction will return the same value.
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. A runtime can easily optimize a stable computation to wait for the specified time. The runtime can also precompute the transaction slightly ahead of time and hold it ready to commit, a viable basis for soft real-time systems.

In context of a distributed runtime and network partitioning, each node maintains a local estimate of the runtime clock. When the nodes interact, we don't need to synchronize clocks, but we must track the maximum observed timestamp (received from 'now' or passed to 'await') that might have contributed to an interaction to ensure we observe a monotonic distributed clock. Fixing clock drift and aligning with true time is best left to NTP or PTP.

*Note:* If attempting to record how long a computation takes, use profiling annotations!

## Arguments and Environment Variables

A runtime can easily provide access to OS environment variables and command-line arguments.

* `sys.env.list : List of Text` - return the defined environment variables
* `sys.env.get(Text) : Text` - return value for an OS environment variable
* `sys.env.args : List of Text` - return the command-line arguments

These will simply be read-only within an application, but users could intercept 'sys.env.\*' handlers when calling a subprogram.

*Note:* Applications integrate the configuration environment at compile time through the namespace layer, '%env.\*'.

## Console IO

With users launching glas applications from a command-line interface, it is convenient to support user interaction directly through the same interface. The basics are just reading and writing some text, but it is possible to disable line buffering and input echo then implement sophisticated applications via [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or extended protocols.

A viable API:

* `sys.tty.write(Binary)` - write to standard output, buffered until commit. 
* `sys.tty.read(N) : Binary` - read from standard input. Diverges if not enough data.
* `sys.tty.unread(Binary)` - add Binary to head of input buffer for future reads.
* `sys.tty.ctrl(Hint)` - ad hoc control, extensible but mostly for line buffering and echo

The control hint is runtime specific, perhaps something like `(icanon:on, ...)`. I reserve standard error for runtime use - compile-time warnings, logging, etc..

*Note:* An application adapter could redirect or mirror `sys.tty.*`, perhaps to 'http' and [xterm.js](https://xtermjs.org/).

## Foreign Function Interface (FFI)

The only FFI of any relevance for system integration is calling functions on a ".so" or ".dll" using the C ABI. C doesn't have any native support for transactions, but we can freely buffer a sequence of C calls to run between transactions. To ensure sequencing, I propose to stream commands and queries to FFI threads. To mitigate risk, FFI threads may run in separate OS processes. To mitigate latency, a simple environment enables users to pipeline outputs from one C call as input to another.

A viable API:

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
  * `fork(FFI) : FFI` - Clone the FFI thread. This involves a copy of of the data stack, stash, registers, and runtime-local vars. The next command will run in a new thread. The heap is shared between forks.
  * `status(FFI) : FFIStatus` - recent status of FFI thread:
    * *uncommitted* - initial status for a 'new' or 'fork' FFI.
    * *busy* - ongoing activity in the background - setup, commands, or queries
    * *ready* - FFI thread is halted in a good state, can receive more requests.
    * *error:(text:Message, ...)* - FFI thread is halted in a bad state and cannot receive any more commands or queries. The error is a dict with at least a text message. The FFI cannot receive any more commands or queries.
  * `link.lib(FFI, SharedObject)` - SharedObject is runtime or adapter specific, but should indirectly translate to a ".dll" or ".so" file. When looking up a symbol, last linked is first searched.
  * `link.c.hdr(FFI, Name, Text)` - redirects `#include<Name>` to Text in future use of 'link.c.src' or 'cscript'. There is no implicit access to filesystem headers, so this is the most convenient way to declare APIs, support `#define` parameters or macros, etc.
  * `link.c.src(FFI, Text)` - JIT-compile C source in memory and link (via Tiny C Compiler). Consider including a `#line 1 "source-hint"` directive as the first line of text to improve debug output from the JIT comiler.
  * `call(FFI, Symbol, TypeHint)` - call a previously linked symbol. Parameters and results are taken from the data stack. TypeHint for `int (*)(float, size_t, void*)` is `"fZp-i"`. In this case, pointer 'p' should be at the top of the data stack. Void type is elided, e.g. `void (*)()` is simply `"-"`.
  * `cscript(FFI, Text, Symbol, TypeHint)` - JIT compile a C source text, call one function defined in this source, then unload. This is convenient for one-off operations or long-running operations, but it has *a lot* of overhead per op. 
  * `mem.write(FFI, Binary)` - (type `"p-"`) copy a runtime Binary into FFI thread memory, starting at a pointer provided on the data stack. A sample usage pattern: push length, call malloc, copy pointer, write. When writing text, consider addending a NULL byte to the binary.
  * `mem.read(FFI, Var)` - (type `"pZ-"`) given a pointer and size on the data stack, copy FFI process memory into a runtime Binary. This binary is returned through Var.
  * `push(FFI, List of Data, TypeHint)` - adds primitive data to the stack. TypeHint should have form `"fZp"`, one character per datum in the list, as the RHS of a call. In this example, pointer should be last item in list and is pushed last to top of data stack. Caveats: Conversion to floating point numbers may be lossy. Integer conversions must not be lossy, or we'll block the transaction. For pointers, must use abstract Ptr bound to same FFI process (see 'sys.ffi.ptr.\*').
  * `peek(FFI, Var, N)` - read N items from top of stack into runtime Var. If N is 0, this reduces to a status check. Some caveats: a floating-point NaN or infinity will result in error queries, and FFI pointers are abstracted (see 'sys.ffi.ptr.\*').
  * `copy(FFI, N)` - copy top N items on stack
  * `drop(FFI, N)` - remove top N items from stack
  * `xchg(FFI, Text)` - ad hoc stack manipulation, described visually. The Text `"abc-abcabc"` is equivalent to 'copy(3)'. In this example, 'c' is top of stack. Mechanically, we find '-', scan backwards popping values from stack into single-assignment local variables 'a' to 'z', then scan forward pushing variables back onto the stack.
  * `stash(FFI, N)` - move top N items from data stack to top of auxilliary stack, called stash. Preserves order: top of stack becomes top of stash.
  * `stash.pop(FFI, N)` - move top N items from top of stash to top of data stack.
  * `reg.store(FFI, Reg)` - pop data from stack into a local register of the FFI thread. Register names should be short texts.
  * `reg.load(FFI, Reg)` - copy data from local register of FFI thread onto data stack.   
  * `var.*` - receiving data from 'peek' or 'mem.read'. Var should be a short text.
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
* `sys.refl.ffi.*` - we could do some interesting things here, e.g. support remote debugging of an FFI process. But it's highly runtime specific.

This API is designed assuming use of [libffi](https://en.wikipedia.org/wiki/Libffi) and the [Tiny C Compiler (TCC)](https://bellard.org/tcc/). For the latter, a [recent version](https://github.com/frida/tinycc/tree/main) lets us redirect `#include` and resolve missing symbols via callbacks.

*Aside:* It is unclear what should happen with the FFI process stdin, stdout, and stderr file streams. These can feasibly be redirected to the runtime and made available through reflection. Perhaps we can present an xterm.js per FFI process via `"/sys/ffi/name"`. Alternatively, we could let the user manage this.

## Filesystem, Network, Native GUI, Etc.

Many APIs are essentially specialized wrappers for FFI. Instead of implementing them in the runtime, I intend to leave them to libaries or adapters. Instead of providing these APIs directly, we can adjust them for pipelining and cover utility code.

## Content-Addressed Storage and Glas Object (Low Priority!)

The runtime uses content-addressed data when modeling very large values in context of remote procedure calls, persistent data, and virtual memory. Based on configuration, we might integrate with content delivery networks. Users can potentially extend these use cases with sufficient access, but we must be careful regarding garbage-collection.

Rough API sketch: 

* `sys.refl.glob.*` - an API for serialization or parsing of glas data into a binary representation. Operations will take a CAS as a context argument to keep hashes in scope. Lazy loading of a binary into a value might extend the set of hashes that CAS is waiting on. Serializing a value can store hases into the CAS. Values cannot be serialized if they're still waiting on lazy hashes, but we can potentially determine which hashes we're lazily waiting upon.

* `sys.refl.cas.*` - (tentative) an API that helps users maintain a content-addressed storage (CAS) context. This might prove unnecessary, perhaps we could maintain a serialization context as plain old data without any access to hashes held by the runtime. In any case, we can store and fetch binaries. Each stored binary might be paired with a list of hashes referenced by the binary. We can potentially report which hashes we're waiting on. The details need work, but should closely align to whatever a runtime is doing under the hood.

## Node Locals

A distributed transaction doesn't have a location, but it must start somewhere. With runtime reflection, we can take a peek.

* `sys.refl.txn.node() : NodeRef` - return a reference to the node where the current transaction started. NodeRef is a runtime-scoped type.
* `sys.refl.node.name(NodeRef) : Name` - stable name for a node, name is a short text.

Observing node allows for an application to vary its behavior based on where a transaction runs, e.g. to select different vars for storage.

Observing the starting node has consequences! A runtime might discover that a transaction started on node A would be better initiated on node B. Normally, we cancel the transaction on node A and let B handle it. However, after observing that we started on node A, we instead *migrate* to node B. With optimizations, we can skip the migration step and run the transaction on node B directly, but *only while connected to node A*. Thus, carelessly observing the node results in a system more vulnerable to disruption. Observing *after* a node is determined for other reasons (such as FFI usage) avoids this issue.

## Reflection on Definitions

We'll eventually need to look at the namespace, e.g. to extract an executable binary or a web application. We can dedicate 'sys.refl.ns.\*' for this role. However, we might restrict this to compile-time computations (perhaps lazy) to simplify separate compilation of an executable.

## Composing Applications

We can compose applications by composing their public interfaces, and by wrapping the algebraic effects handlers to redirect resources like heap vars. My vision for glas systems calls for convenient composition of applications, both for [notebook apps](GlasNotebooks.md) and sandboxing purposes.

For efficient composition of applications, and to enable scripts to compose applications, we should include most applications in '%env.appname.app.\*' alongside shared libraries. This influences the user's configuration namespace.

A lot of composition can be automated into namespace macros or user-defined syntax. Ideally, users can very easily express composite applications where components can communicate according to simple conventions (e.g. databus, publish-subscribe, internal RPC).

## Alternative Run Modes

### Threaded Applications

In some use cases, developers may prefer a conventional 'app.main' procedure. The evaluator will heuristically partition the main procedure into a sequence of atomic steps. Minimum step size can be controlled by annotating 'atomic' sections, e.g. `(%an (%an.atomic) Operation)`, while maximum step size could be guided by 'yield' annotations (reporting an error if 'yield' appears within an 'atomic' operation). 

Every successful step will implicitly update stateful thread environment representing the call stack or continuation, local mutable vars, algebraic effects handlers, invariant assertions, instrumentation, and so on. Every aborted or divergent step is implicitly retried, much like a 'step' function for a transaction loop. This retry provides a basis to wait on a mutex, queue, or arbitrary conditions. Use of 'sys.select' together with 'atomic' can express flexible waits, e.g. wait on a queue OR a timeout, and bounded searches.

Support for invariant assertions or instrumentation extends easily to threads. For example, in case of 'assert(Chan, Cond, Message) { Operation }' we might verify Cond holds across every atomic step in Operation. If Cond fails, we can log the error and abort the step, continuing when conditions change. Of course, this behavior would be configurable per Chan.

For concurrency, we can support multi-threading. A viable API:

* `sys.thread.*`
  * `spawn(Expr) : Thread` - evaluates Expr in a separate thread, but shares the caller's environment. Expr cannot reference any transaction-scoped resources.
  * `kill(Thread)` - forces a running thread to terminate. Threads do not error out, i.e. even a thread that halts on a type error is still logically retrying that last step forever, until code is updated or the thread is killed.
  * `join(Thread) : opt Result` - Returns a thread's final result, or nothing if the thread was killed. Diverges if the thread is still running. Join on a newly spawned thread within an atomic section will always diverge.

Multi-threading requires representing local mutable vars shared between threads as runtime-scoped heap refs. Ideally, an optimizer will perform analyses to minimize sharing, keeping most data on the call stack. We can also introduce annotations to express and enforce ownership assumptions.

We can hybridize threaded and transaction-loop run modes. In this case, the runtime offers 'sys.thread.\*' to a transaction-loop application. Instead of 'app.main', users spawn a main thread from 'app.start'. Intriguingly, a threaded 'while' loop can potentially be optimized as another transaction loop when the condition is stable and body is atomic.

However, any use of 'sys.thread.\*' will hinder live coding. It is easy to update new function calls, but difficult to update the thread continuation. Users can mitigate this by maintaining stable APIs within a program, e.g. don't change function arguments or algebraic effects handlers, excepting optional arguments. At least in theory, we could also introduce 'sys.refl.thread.\*' APIs for discovering threads and rewriting continuations during 'app.switch'.

*Note:* We could also provide a lower-level interface for small-step eval of an abstract AST or Expr. I'm also exploring program models that have some built-in concurrency, which could significantly reduce need for arbitrary threads. 

### Staged Applications

In some cases, we might want to write an application that generates or selects another application based on command-line arguments, perhaps integrating some local files. Staged applications might define 'app.build' as a [namespace procedure](GlasNamespaces.md), generating another application based on command-line arguments. The procedure can be parameterized by a list of command-line arguments. It receives access to the same '%env.\*' environment of shared libraries, languages, and configured applications as scripts.

