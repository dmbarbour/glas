# Glas Applications

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

## Transaction Loop Application Model

A transaction loop application might define several transactional methods:

* 'start' - Set initial state, perform initial checks. Retried until it commits once.
* 'step' - After a successful start, repeatedly run 'step' until it voluntarily halt or killed externally.
* 'http' - Handle HTTP requests between steps. Our initial basis for GUI and events.
* 'rpc' - Transactional inter-process communications. Multiple calls in one transaction. Callback via algebraic effects.
* 'gui' - Like an immediate-mode GUI. Reflective - renders without commit. See [Glas GUI](GlasGUI.md).
* 'switch' - First transaction in new code after live update. Old code runs until successful switch.

The exact interface may vary between runtimes. The configuration may generate an adapter based on application 'settings' and runtime version information.

The transactions will interact with the runtime - and through it the underlying systems - with an algebraic effects API. Unfortunately, most external systems - filesystem, network, FFI, etc. - are not transactional. We resolve this by buffering operations to run between transactions. But there are a few exceptions: application state, remote procedure calls, and a convenient escape hatch for safe, cacheable operations like HTTP GET.

## Application State

A runtime can provide a familiar and flexible heap-allocation API. Part of this heap may be persistent, with a global variables shared between applications. To simplify garbage collection, control scope, and support type annotations, heap references will be abstracted. A viable API (tentative):

* `sys.heap.*` - 
  * `scope.*` - We divide the heap into a few scopes. The larger the scope, the more restrictive of abstract data. Conversely, data from a larger scope can be stored into a smaller scope. Scope can be enforced dynamically via tagged pointers.
    * `shared : Scope` - persistent, shared state through a configured database, with flexible heap refs.
    * `runtime : Scope` - runtime-local resources, such as open files, network connections, FFI threads.
    * `transaction : Scope` - implicitly cleared before commit, can enforce protocols via linear types.
    * `cmp.eq(Scope, Scope) : Boolean` - returns whether two scopes are the same.
    * `cmp.ge(Scope, Scope) : Boolean` - is left scope is greater or equal to right scope?
  * `var(Scope, Name) : Ref` - global variable of given scope, default value zero. Name should be a short, meaningful text. Adapters or handlers may translate names, e.g. add a prefix, for fine-grained scope within applications.
  * `ref.*` - new refs, weak refs, reference equality
    * `new(Scope, Data) : Ref` - obtain a new reference, initially associated with the given data. This Ref and associated data may be garbage collected if it becomes unreachable.
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
  * *CRDTs* - (tentative) runtime support for cells modeling [conflict-free replicated data types (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) are useful for network partitioning tolerance in a distributed system. See *Distributed State*. However, a lot of research is needed to choose suitable CRDTs.
* `sys.refl.heap.*` - methods for reflection on the database.
  * `ref.weak.tryfetch(WeakRef) : opt Ref` - returns a singleton list containing Ref if available, otherwise empty list. This operation indirectly observes the garbage collector.
  * `ref.hash(Ref) : N` - returns a stable natural number per Ref, on average above 2^60, suitable for use with hashmaps or hashtables.
  * `var.iter(Scope, Binary) : Name` - iteration through var names in use. Returns the next name lexicographically after a given binary, or an empty string if we've reached the end. The garbage collector may remove vars from use if they contain zero and nobody is holding a Ref.
  * `queue.reader.buffer(Ref) : Ref` - access the read buffer as a separate Ref from the main list ref. If you want to read 'all available' data, this provides a means to do so.
  * `queue.writer.buffer(Ref) : Ref` - access the write buffer as a separate Ref from the main list ref. Content in the write buffer may implicitly transfer to the read buffer between transactions.

The heap should support atomic transactions, structured data, and content-addressed storage. Transactions significantly mitigate many problems with shared state, but schema versioning and trust are remaining issues. To mitigate this, application adapters might present volumes of shared state in terms of mailboxes, databuses, publish-subscribe, perhaps a relational database.

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

### Static Allocations

The heap API is more dynamic than I'd prefer. This can be mitigated to some degree via static 'var' name, scope, and type annotations. With enough detail, perhaps we can support unboxed representations and layout. For a shared heap, a runtime could also write type information to support schema consistency between apps.

However, my vision for glas suggests more robust static allocation and layout. This might be feasible if we instead integrate state in terms of declaring and binding external objects with compile time algebraic effects. These objects could be logically provided on the call stack instead of through a heap. TBD: This is an opportunity I cannot detail without further developing the program model.

## HTTP Interface

The runtime should recognize the 'http' interface and support requests over the same channels we use for remote procedure calls and debugging. Based on application settings, the runtime might intercept `"/sys/*"` for debugging and reflection as a web app. But every other subdirectory will be handled by 'http'.

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
  * `mem.write(FFI, Binary)` - (type `"p-"`) copy a runtime Binary into FFI thread memory, starting at a pointer provided on the data stack. A sample usage pattern: push length, call malloc, copy pointer, write. This operation is the be
  * `mem.read(FFI, Var)` - (type `"pZ-"`) given a pointer and size on the data stack, read FFI process memory into an immutable runtime Binary value. This binary is eventually returned through Var.
  * `push(FFI, List of Data, TypeHint)` - adds primitive data to the stack. TypeHint should have form `"fZp"`, one character per datum in the list, as the RHS of a call. In this example, pointer should be last item in list and is pushed last to top of data stack. Caveats: Conversion to floating point numbers may be lossy. Integer conversions must not be lossy, or we'll block the transaction. For pointers, must use abstract Ptr bound to same FFI process (see 'sys.ffi.ptr.\*').
  * `peek(FFI, Var, N)` - read N items from top of stack into runtime Var. If N is 0, this reduces to a status check. Some caveats: a floating-point NaN or infinity will result in error queries, and FFI pointers are abstracted (see 'sys.ffi.ptr.\*').
  * `copy(FFI, N)` - copy top N items on stack
  * `drop(FFI, N)` - remove top N items from stack
  * `xchg(FFI, Text)` - ad hoc stack manipulation, described visually. The Text `"abc-abcabc"` is equivalent to 'copy(3)'. In this example, 'c' is top of stack. Mechanically, we find '-', scan backwards popping values from stack into single-assignment temporary local variables 'a-z', then scan forwards from '-' to push variables back onto the stack.
  * `stash(FFI, N)` - move top N items from data stack to top of auxilliary stack, called stash. Preserves order: top of stack becomes top of stash.
  * `stash.pop(FFI, N)` - move top N items from top of auxilliary stack to top of data stack.
  * `reg.store(FFI, Reg)` - pop data from stack into a local register of the FFI thread. Register names should be short strings.
  * `reg.load(FFI, Reg)` - copy data from local register of FFI thread onto data stack.    
  * `var.*` - future return value from 'peek' or 'mem.read' 
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

*Aside:* It is unclear what should happen with the FFI process stdin, stdout, and stderr file streams. These can feasibly be redirected to the runtime and made available through reflection. Perhaps we can present an xterm.js per FFI process via `"/sys/ffi"`. Alternatively, we could let the user manage this.

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

## Securing Applications

Not every program should be trusted with FFI, shared state, and other sensitive resources. This is true within a curated community configuration, and even more true with external scripts of ambiguous provenance. So, what can be done?

A configuration can describe who the user trusts or how to find this information. With a few conventions, when loading files we could poke around within a folder or repository for signed manifests and certifications of public signatures. A compiler can generate record of sources contributing to each definition - locations, secure hashes, other definitions, etc. - even accounting for overrides and 'eval' of macros.

Instead of a binary decision for the entire application, perhaps we can isolate trust to specific definitions. Via annotations, some trusted definitions might express that they properly sandbox FFI, permitting calls from less-trusted definitions. And perhaps we can express a gradient of trust with our signatures, e.g. that a given definition can be trusted with 'filesystem access' instead of a role like 'full FFI access'. 

Ultimately, we can run untrusted applications in trusted sandboxes, and a trusted application adapter can construct sandboxes upon request based on application settings. If an application asks for more authority than it's trusted to receive, the adapter won't stop it: the adapter observes only application settings and runtime version info. Instead, the runtime will examine the final application, see that FFI or abstract resources are being used by untrusted functions without sufficient sandboxing, and reject the application. At that point, we can abandon the app, further sandbox the app, or extend trust.
