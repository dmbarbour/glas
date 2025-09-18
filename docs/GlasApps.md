# Glas Applications

The [glas executable](GlasCLI.md) lets users select and run an application from the configuration namespace or by compiling a file as a script. By convention, a glas application is defined in 'env.AppName.app' in the configuration [namespace](GlasNamespaces.md), or 'app' in a script. 

Instead of a single 'main' program, an application is defined by a collection of related methods such as 'start' and 'step' for transaction-loop applications, 'http' to receive events, perhaps 'gui' to render a view. Most critically, 'settings' supports application-specific configuration of the runtime.

This set of methods is expressed as a namespace of handlers (see [program model](GlasProg.md)). This allows the application to be reduced to a single definition, and simplifies composition, extension, inheritance, and overrides similar to OOP.

## Application Settings and Adapters  

Every application defines at least 'settings' to guide runtime integration. In general, the runtime does not directly observe application settings. Instead, the runtime passes access to 'settings' to the configuration, and the configuration queries the application as needed. This indirection ensures the configuration has robust control and provides greater portability between application conventions and runtime versions.

Although 'settings' may observe command-line arguments or environment variables, it is preferable that most queries can be processed at compile-time, allowing for greater specialization of the runtime.

## Application Models and Run Modes

A runtime may support more than one "run mode" for applications. A run mode would be configured based on application settings. A few useful run modes:

* *threaded applications* - Very conventional. The runtime runs a non-atomic 'main' program. Whenever main yields, the runtime may process outputs and update input buffers, then eventually resume. Parallelism is opportunistic. Not friendly for live coding because we cannot robustly update behavior except at name or '%call' boundaries.

* *transaction-loop applications* - The primary operation is to repeatedly run 'step' in isolated, atomic transactions. A naive implementation will likely perform a lot of rework, but we can optimize via incremental computing, duplicate the step upon non-deterministic choice, and abort a step when read-write conflicts are detected. Very friendly to live coding.

* *staged applications* - An application could define 'build' to construct the next-stage application. (However, explicit staging may be unnecessary if we have good implicit staging via late-bound dependencies.)

This document focuses on transaction-loop applications. Though, I imagine that threaded applications will prove more popular in early development due to difficulty of initially implementing transaction-loop optimizations.

Aside from the primary behavior, a runtime may support ad hoc event-processing methods:

* 'start' - First transaction. Initial checks and setup. Retried until it commits.
* 'switch' - First transaction after live update. Old code runs until switch commits.
* 'http' - Receive HTTP requests from configured port, multiplexed with RPC, debugger, etc.. 
* 'rpc' - Receive remote procedure calls. A single transaction might receive multiple calls.
* 'gui' - See [Glas GUI](GlasGUI.md).
* 'signal' - handle OS-level signals or interrupts.

An important feature of 'http' and such is that we can far more conveniently compose 'http' handlers than we can compose applications that independently open TCP ports to receive HTTP requests. I assume that handling HTTP requests will be the primary mode of external interaction with applications in early development, until the glas system matures.

This document focuses on transaction loop applications, as the best fit for my vision of glas systems. However, ultimately the application model supported in glas is very flexible and extensible.

## The Transaction Loop

A transaction loop is a very simple idea: a repeating transaction, atomic and isolated. Those properties - repetition, atomicity, and isolation - allow for several useful optimizations:

* *incremental computing* - We don't always need to repeat the entire transaction from the start. Instead, roll-back and recompute based on changed inputs. If repeating the transaction is obviously unproductive, e.g. it aborts, then we don't need to repeat at all until conditions change. If productive, we still get a smaller, tighter repeating transaction that can be optimized with partial evaluation of the stable inputs.
* *duplication on non-deterministic choice* - If the computation is stable up to the choice, then duplication is subject to incremental computing. This provides a basis for concurrency, a set of threads reactive to observed conditions. If the computation is unstable, then at most one will commit while the other aborts (due to read-write conflict), representing a race condition or search. Both possibilities are useful.
* *distributed runtimes* - A distributed runtime can mirror a repeating transaction across multiple nodes. For performance, we can heuristically abort transactions best started on another node, e.g. due to locality of resources. Under incremental computing and duplication on non-deterministic choice, this results in different transactions running on different nodes. In case of network partitioning, transactions in each partition may run independently insofar as we can guarantee serializability (e.g. by tracking 'ownership' of variables). An application can be architected such that *most* transactions fully evaluate on a single node and communicate asynchronously through runtime-supported queues, bags, or CRDTs. 

Assuming these optimizations, the transaction loop supports reactive systems, concurrency, and distributed systems with graceful degradation and resilience. Users can easily understand and debug transactions in isolation. Also, even with these optimizations, the transaction loop remains friendly for live coding and orthogonal persistence. 

Further, there are several lesser optimizations we might simplify a program:

* *congestion control* - A runtime can heuristically favor repeating transactions that fill empty queues or empty full queues.
* *conflict avoidance* - A runtime can arrange for transactions that will likely conflict to evaluate in different time slots. This reduces the amount of rework.
* *soft real-time* - A repeating transaction can 'wait' for a point in time by observing a clock then diverging. A runtime can precompute the transaction slightly ahead of time and have it ready to commit.
* *loop fusion* - A runtime can identify repeating transactions that compose nicely and create larger transactions, allowing for additional optimizations. 

Transaction loops present two significant challenges. First, the most important optimizations are difficult to implement. Without them, we're effectively limited to an event dispatch loop and heuristic selection of non-deterministic forks based on epoll. Fortunately, the simple event dispatch loop is still a nice application model and sufficient for many applications. Second, request-response interactions must generally commit the request and yield before a response becomes available, which can be awkward to express. This can be mitigated by front-end syntax, perhaps something like 'async' and 'await' keywords.

*Note:* Regarding [ACID (Atomicity, Consistency, Isolation, Durability)](https://en.wikipedia.org/wiki/ACID) properties, only atomicity and isolation are required for a transaction loop. However, consistency is indirectly supported via type systems and assertions. Durability may be orthogonal, binding some application state to configured persistent storage.

## Basic Integration

Applications are expressed as a set of handlers. However, we'll present the runtime primarily as the 'caller' to the application handlers, instead of the host. This allows for more flexibility per-call in what the runtime presents to the application.

* host prefix - an empty namespace, link everything to WARN
* locals prefix - binds to application-local state and app handlers.
* caller prefix - access runtime effects APIs, shared persistent 'db.\*' registers, and (for some methods) transaction-local ephemeral 'tn.\*' registers. The latter is mostly useful in context of remote procedure calls.

In this document, I'll generally translate the caller prefix as 'sys.\*' and the locals prefix as 'app.\*'. Thus, 'sys.db.x' would be a shared persistent register while 'app.x' is an application instance register. This may require an explicit translation in practice.

In context of shared persistent data, we might record some inferred metadata about register types into the database. This could support a more specialized representation and best-effort type safety for shared memory.

*Note:* Database registers default to zero.

## Distributed State

In context of a distributed runtime, application state and transactions may also be distributed. For performance, we can mirror read-mostly registers, and we can specialize accelerated operations such as treating a register as a FIFO. In the extreme case, we could accelerate CRDT registers that each mirror can update locally, merging updates when network connectivity resumes.

## HTTP Interface

Based on application-specific configuration, a runtime will open TCP ports to receive HTTP and RPC requests. A configurable subset of HTTP requests, perhaps `"/sys/*"`, will be routed to the runtime-provided 'sys.refl.dbg.http' to support administration and debugging. Other HTTP requests are routed to the application via 'app.http', if defined. 

        app.http : Request -> [sys] Response

The Request and Response are binaries. They may be *accelerated* binaries with structure under-the-hood that can be efficiently queried, manipulated, and validated. Each request runs in a separate transaction. If a request fails to generate a valid response, it is logically retried until timeout, serving as a simple basis for long polling. Asynchronous operations can immediately return "303 See Other", allowing the caller to fetch the result in a future query.

If there is sufficient demand, we can extend this API to accept WebSockets, perhaps via 'app.http.ws'. We can also invent HTTP headers to handle multiple requests in one atomic transaction. To effectively use 'app.http' as an early basis for GUI, we could configure the runtime to open a browser window when the application is started.

*Note:* The application-specific configuration might also describe integration with SSO to support multiple users and roles for the built-in HTTP interface.

## Remote Procedure Calls (RPC)

To receive RPC calls, an application must provide an RPC interface. This interface can be expressed through 'app.settings'. In some cases, we might want a dynamic interface that varies based on application state. Settings can feasibly indicate a dynamic interface routed through a single method, similar to HTTP requests. An application can feasibly publish multiple RPC interfaces for multiple roles, registries, and trust levels.

In context of glas systems, RPC methods will typically be called within distributed transactions. Thus, there is an opportunity to abort the transaction, or to check for consistency of application state after a series of RPC calls but before commit. Further, we can feasibly apply various transaction loop application optimizations - incremental computing, waiting reactively in case of 'unproductive' transactions, concurrency based on stable non-deterministic choice. However, robust support for distributed transactions and those optimizations will require new RPC protocols.

Also, RPC calls may support limited algebraic effects for callbacks, and local concurrency via 'await Cond'. This allows for relatively flexible interactions, but further modifies the RPC protocol.

To perform RPC calls, an application must discover and reference RPC resources in the environment. However, in my vision for glas systems, I'm aiming to avoid first-class references. We can feasibly model a subscription to RPC interfaces as a dynamic table of discovered interfaces with some abstract state, selecting one when performing each call.

The details need a lot of work. But I believe transactional RPC will be a very effective and convenient basis for inter-process communication.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. This is essentially an RPC feature, but we might present 'app.gui' or similar to simplify composition of GUI independent of other RPC. In the short term, we will rely on 'app.http' or FFI APIs as a simple basis for GUI.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `(%choice Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may be very predictable, and may heuristically choose for performance reasons.

*Note:* Without transaction-loop optimizations for incremental computing and duplication on non-deterministic choice, a basic single-threaded event-loop will perform better. We can still use sparks for parallelism.

## Random Data

Instead of a stateful random number generator, the runtime will provide a stable, cryptographically random field. 

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The result varies on Seed, N, and runtime instance. The Seed could feasibly be abstract state or plain old data.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes should produce the same result for a given query.

## Background Transactions

In some use cases, we want an escape hatch from transactional isolation. This occurs frequently when wrapping FFI with 'safe' APIs. We might logically support HTTP GET within a single transaction.

Conceptually, the idea is that we 'trigger' a runtime via reflection on an incomplete computation to perform some background computation and update a cache. This seems difficult to generalize, but we could explicitly reference a few toplevel handlers in this role.

There is risk of transaction conflict between the background computation and the caller. We'll commit the background computation and retry the caller. If careless, conflict may lead to thrashing, but this is easily mitigated by manual caching.

Insofar as conflicts are avoided, background transactions are compatible with transaction-loop optimizations such as incremental computing or support for non-deterministic results logically duplicating the caller.

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

*Note:* An adapter could redirect or mirror `sys.tty.*`, perhaps to 'http' and [xterm.js](https://xtermjs.org/).

## Foreign Function Interface (FFI)

The FFI of relevance for most system integration is calling functions on a ".so" or ".dll" using the C ABI. C doesn't have any native support for transactions, but we can freely buffer a sequence of C calls to run between transactions. To ensure sequencing, I propose to stream commands and queries to FFI threads. To mitigate risk, FFI threads may run in separate OS processes. To mitigate latency, a simple environment enables users to pipeline outputs from one C call as input to another.

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

TODO: consider update of API to use environment abstraction instead of abstract data.

* `sys.ffi.*` -
  * `new(Hint) : FFI` - Return a linear runtime-scoped reference to a new FFI thread. Hint guides integration, such as creating or attaching to a process, or at which node in a distributed runtime. The FFI thread will start with with a fresh environment.
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

*Note:* The first-class 'FFI' reference in this API will likely be replaced by biunding abstract state to simplify parallel use of FFI.

*Note:* We'll need to consider how to handle the standard input, output, and error channels for the FFI process. We could let the runtime capture these by default, perhaps modeling a virtual terminal through reflection APIs or the runtime's HTTP interface, or a dedicated 'sys.refl.tty.\*' API.

### Filesystem, Network, Native GUI, Etc.

Many APIs are essentially specialized wrappers for FFI. Instead of implementing them in the runtime, I intend to leave them to libaries or adapters. Instead of providing these APIs directly, we can adjust them for pipelining and cover utility code.

## Content-Addressed Storage and Glas Object (Low Priority!)

The runtime may use content-addressed data when modeling very large values. This is especially convenient in context of remote procedure calls, persistent data, and virtual memory, allowing reuse of stable fragments of the data without downloading or saving them every time. Based on configuration, we could integrate with content delivery networks.

However, in some cases the user might want more control or observability on this content-addressed structure. This is essentially a reflection API. We'll broadly need to break this API into a few parts:

* serialization of structured data that includes the hashes, e.g. using the [glas object](GlasObject.md) format
* access to content-addressed storage by hash, e.g. to publish or lookup data and support garbage collection

The main challenge here involves the interaction between published data and garbage collection. For every stored value, perhaps we also store an associated list of secure hashes that it references for GC purposes. And we might need to maintain a collection of GC roots in addition to whatever the runtime is maintaining.

## Node Locals

A distributed transaction doesn't have a location, but it must start somewhere. With runtime reflection, we can take a peek.

* `sys.refl.tn.node() : NodeRef` - return a reference to the node where the current transaction started. NodeRef may be a runtime-scoped abstract type. 
* `sys.refl.node.list : List of NodeRef` - represents set of nodes involved in the distributed runtime. Will be a singleton list if not a distributed runtime.
* `sys.refl.node.name(NodeRef) : Name` - stable name for a node, name is a short text.

Observing node allows for an application to vary its behavior based on where a transaction runs, e.g. to select different vars for storage.

Observing the starting node has consequences! A runtime might discover that a transaction started on node A would be better initiated on node B. Normally, we cancel the transaction on node A and let B handle it. However, after observing that we started on node A, we instead *migrate* to node B. With optimizations, we can skip the migration step and run the transaction on node B directly, but *only while connected to node A*. Thus, carelessly observing the node results in a system more vulnerable to disruption. Observing *after* a node is determined for other reasons (such as FFI usage) avoids this issue.

## Reflection on Definitions

We'll eventually need to look at the namespace, e.g. to extract an executable binary or a web application. We can dedicate 'sys.refl.ns.\*' for this role. However, we might restrict this to compile-time computations (perhaps lazy) to simplify separate compilation of an executable.

## Composing Applications

We can compose applications by composing their public interfaces. My vision for glas systems calls for convenient composition of applications, both for [notebook apps](GlasNotebooks.md) and sandboxing purposes.

For efficient composition of applications, and to enable scripts to compose applications, we should define most applications as 'env.AppName.app' in the configuration namespace.

A lot of composition can be automated into macros or user-defined syntax. Ideally, users can very easily express composite applications where components can communicate according to simple conventions (e.g. databus, publish-subscribe, internal RPC).

