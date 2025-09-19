# Glas Applications

The [glas executable](GlasCLI.md) lets users run an application defined in the configuration namespace or a separate script file. By convention, an application is named 'env.appname.app' in the configuration, or simply 'app' for a script. A script can reference definitions in 'env.\*' and thus compose applications.

## Methods

Applications are expressed as a collection of methods. Useful methods include:

* 'settings' - guidance for runtime configuration. The runtime does not observe settings directly, instead providing access to settings when evaluating configuration options.
* 'main' - a procedure representing the main application process. Is evaluated as a sequence of transactions, each using '%yield' to commit a step and '%fail' to abort a step.
* 'http' - a flexible interface with many use cases (services, signaling, gui, etc.). The runtime opens a configurable port multiplexed with remote procedure calls. A configurable HTTP path (default "/sys/") is reserved for runtime use, e.g. built-in debug views and APIs. When composing applications, we can compose 'http' interfaces.
* 'rpc' - receive remote procedure calls in a distributed transaction. The primary basis for inter-process communication between glas applications.
* 'gui' - a conventional GUI can be implemented via FFI or HTTP, but I have a vision for reactive, immediate-mode GUIs based around transaction-loop optimizations and user participation in transactions. See [Glas GUI](GlasGUI.md).
* 'switch' - in context of live coding, runs as the first transaction when updating code. If this transaction fails, we'll retry but continue with the old code until it succeeds.

Application methods are represented in the [program model](GlasProg.md) as a namespace of handlers. This supports declarative composition, extension, inheritance, and overrides similar to OOP classes. 

## Standard Behavior

The runtime process binds application methods to local state and a system API. The runtime will implicitly apply a translation to the application handlers such as `{ "app." => ".", "sys." => "^sys.", "." => NULL, "^" => NULL }`, to support a naming convention:

* 'app.\*' - application-local registers and application methods
* 'sys.\*' - system API and shared, persistent registers 'sys.db.\*'
* '$\*' - call-site specific handlers or registers (rarely used)

The runtime queries the configuration for any application-specific options, passing along 'app.settings' and specializing the runtime based on responses. Then the runtime forks a few coroutines - one to call 'app.main', another to handle 'app.http' requests, etc.. Upon returning from 'app.main', we'll implicitly halt background tasks handling 'app.http' among others.

Shared, persistent registers in 'sys.db.\*' are bound to a configured database. I expect this will mostly be used for transactional persistence. Asynchronous, shared-memory interaction through the database is possible, but we might not immediately optimize for it, favoring transactional remote procedure calls for inter-process communication.

Aside from persistent registers, the system API provides access to network, filesystem, console, clocks, native GUI, secure random data, etc.. However, it is a huge hassle to implement all these features up front. Initially, I plan to develop a pipelined FFI that is then used to implement those other features.

## Concurrency

The program model supports fork-join coroutines. The normal concern with coroutines is the absence of preemption. A coroutine must yield voluntarily. However, this problem is mitigated by transactional steps and a non-deterministic scheduler. A coroutine that does not yield in a timely manner can be aborted to be retried later. The runtime can evaluate several steps simultaneously, analyze for read-write conflicts, commit a non-conflicting subset of steps and retry the rest.

Essentially, the glas program model favors [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control).

Concerns remain regarding starvation and rework. Rework can be mitigated based on static or empirical conflict analysis, scheduling operations that are likely to conflict in different timeslots. Resolving starvation will generally require attention from the developer, e.g. introducing intermediate steps or queues.

Usefully, this approach to concurrency can also recover from some error conditions. For example, if a step is aborted due to assertion failure or divide-by-zero, we could automatically retry and continue after a concurrent operation updates the relevant state. This would also apply to fixing state through a debugging API, or fixing code through live coding.

If a coroutine step includes non-deterministic choice, the runtime may try both choices. This can be useful for expressing timeouts, e.g. one choice waits on the clock, another waits on an empty queue, and the runtime commits whichever can proceed first. 

An intriguing opportunity arises with structures similar to:

        while (atomic stable Cond) do { atomic (choice Op1 Op2 Op3); yield }

In this case, we can split this loop for each non-deterministic choice, then evaluate the split loops concurrently. This works because isolated transactions are equivalent to sequential transactions, and because exiting the loop will be detected as a read-write conflict. See *Transaction-Loop Optimizations*.

## Transaction-Loop Optimizations

If we repeatedly run isolated transactions, there are a few useful optimizations we can perform:

* *incremental computing* - Instead of recomputing an entire transaction each time, we can cache the stable prefix of the transaction and roll back based on observable changes. If we determine that repetition is unproductive (e.g. failure or idempotence) we can wait for relevant changes.
* *concurrent choice* - On non-deterministic choice, we can duplicate the transaction and run both cases. If there is no read-write conflict, we can commit both (due to repetition). If a conflict occurs, we can still commit one and retry the other. Choice in the stable incremental computing prefix effectively results in stable 'threads' that handle different subtasks.
* *distributed programming* - A transaction loop can be conveniently mirrored across the nodes, observing loop termination conditions through mirrored state. We can avoid unnecessary distributed transactions with a simple heuristic: abort any transaction where the *first* resource written is remote.
 
There are also a few lesser benefits:

* *congestion control* - a runtime can heuristically recognize producer and consumer loops, when treating a register as a queue, and tune the scheduler to keep inputs and outputs in balance.
* *conflict avoidance* - a runtime can arrange transactions that will likely conflict to evaluate in different time slots, aiming to reduce the amount of rework.

Unfortunately, it's a rather daunting task to implement these optimizations. There's an opportunity here, but not one that is easy to grasp. In the short term, this is a non-starter.

## Distribution

Assume we distribute a runtime and application state across networked nodes then run an application on this distributed runtime. In the general case, we can use a distributed transaction for each step. However, distributed transactions are expensive and fragile to network failure. Ideally, we should architect the application and structure state such that most steps evaluate locally on a single node, and most distributed transactions require only two nodes.

Between annotations and accelerators, a runtime can recognize common state patterns. It is possible to mirror read-mostly state, or to logically partition a queue between reader and writer nodes. Support for CRDTs or bags (multisets) would support more flexible mirroring and partitioning, allowing each node to perform both reads and writes locally.

A coroutine can migrate between nodes, approaching relevant resources for the current step. A subset of registers may heuristically migrate with the coroutine. 

In context of transaction-loop optimizations, we can also mirror some operations across nodes. HTTP and RPC services can be understood as transaction loops. We could open a TCP port on each node to service those requests and support synchronization between nodes.

However, effective support for distribution does require attention to some effects APIs. For example, we cannot model a singular 'clock' for a distributed system. Features such as filesystems, networks, and FFI may need to somehow specify a node.

## Live Coding

Live coding and projectional editing is part of my vision for glas systems. It is possible to configure a runtime to check for updates either continuously or upon a trigger. When new code is discovered, the runtime can use an incremental compiler to rebuild. If rebuild is successful, and there are no obvious problems with switching, we can attempt to evaluate 'switch' in the new code and transition to it atomically.

Unfortunately, the 'main' procedure is not very friendly to live coding. We cannot robustly update a partially-executed procedure after yield. What we can do is switch to new defitinitions for future calls, and recompute declarative structures that were cached. The latter would include rebuilding application handlers after an update to 'app'.

To fully benefit, developers may need to architect applications with live coding in mind. For example, use of tail-recursive loops provides an opportunity to switch to new code within the loop. Or to update data schema, we may need to record version information into types or auxilliary registers.

## HTTP Interface

The runtime will intercept a subset of HTTP requests, e.g. to "/sys/". The rest will be passed to the 'app.http' handler. To avoid awkward workarounds, we can also provide access to the runtime HTTP API via 'sys.refl.http' or similar.

The HTTP handler is not necessarily atomic. However, it's convenient if most requests are atomic.

A relevant concern is how the HTTP request and response is presented. Accelerated binaries are a feasible option, or we could provide a set of handlers to process a request and build a response. I'm quite uncertain which is the better option.

*Aside:* Based on application settings and user configuration, we could automatically open a browser window when an application starts.




## Remote Procedure Calls (RPC)

To receive RPC calls, an application must provide an RPC interface. This interface can be expressed through 'app.settings'. In some cases, we might want a dynamic interface that varies based on application state. Settings can feasibly indicate a dynamic interface routed through a single method, similar to HTTP requests. An application can feasibly publish multiple RPC interfaces for multiple roles, registries, and trust levels.

In context of glas systems, RPC methods will typically be called within distributed transactions. Thus, there is an opportunity to abort the transaction, or to check for consistency of application state after a series of RPC calls but before commit. Further, we can feasibly apply various transaction loop application optimizations - incremental computing, waiting reactively in case of 'unproductive' transactions, concurrency based on stable non-deterministic choice. However, robust support for distributed transactions and those optimizations will require new RPC protocols.

Also, RPC calls may support limited algebraic effects for callbacks, and local concurrency via 'await Cond'. This allows for relatively flexible interactions, but further modifies the RPC protocol.

To perform RPC calls, an application must discover and reference RPC resources in the environment. However, in my vision for glas systems, I'm aiming to avoid first-class references. We can feasibly model a subscription to RPC interfaces as a dynamic table of discovered interfaces with some abstract state, selecting one when performing each call.

The details need a lot of work. But I believe transactional RPC will be a very effective and convenient basis for inter-process communication.

## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. This is essentially an RPC feature, but we might present 'app.gui' or similar to simplify composition of GUI independent of other RPC. In the short term, we will rely on 'app.http' or FFI APIs as a simple basis for GUI.

## Random Data

Instead of a stateful random number generator, the runtime will provide a stable, cryptographically random field. 

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The result varies on Seed, N, and runtime instance. The Seed could feasibly be abstract state or plain old data.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes should produce the same result for a given query.

## Background Transactions

For safe operations with cacheable results, such as HTTP GET, it is convenient to pretend the result is already cached and simply continue with the current transaction. To support this pretense, we can leverage a reflection API. Something like:

        sys.refl.bgcall(AppMethodName, Argument) : Result

In this case, we ask the runtime to invoke the named 1--1 arity handler in a separate transaction then return the result to the current transaction. We are relying on reflection to link and call the correct method. The result is not implicitly cached for future requests, but must not be linear.

There is risk of transaction conflict between the background computation and the caller. If so, we'll rewind the caller and retry. Manual caching can potentially resist thrashing.

*Note:* It is possible to return a non-deterministic choice of results. Ideally, results are also stable for incremental computing purposes.

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
  * `new(Hint) : FFI` - Return a runtime-scoped reference to a new FFI thread. Hint guides integration, such as creating or attaching to a process, or at which node in a distributed runtime. The FFI thread will start with with a fresh environment.
  * `fork(FFI) : FFI` - Clone the FFI thread. This involves a copy of of the data stack, stash, registers, and runtime-local vars. The heap is shared between forks.
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

