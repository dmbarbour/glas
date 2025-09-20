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

## State

Registers in 'app.\*' or 'sys.db.\*' are the primary locations for application state. Each register contains a value. In general, values may have arbitrary size. Type annotations may describe expected register types, potentially including size constraints. Insofar as a compiler can verify static type analysis, it is feasible to represent state very efficiently.

Database registers are bound to a configured database and thus shared with other applications. Type annotations may also describe these shared registers. Doing so provides an opportunity to verify interaction with the database prior to application start. It is also feasible to record type assumptions into the database to detect conflicting assumptions.

### Heap API? Disfavor. Defer.

It isn't difficult to introduce and implement a heap API. Consider a minimalist API:

* `sys.heap.new : [heap] HeapRef` - allocate a new reference to an abstract heap. 
* `sys.heap.rw(HeapRef, NewVal) : [heap] OldVal` - swap data with heap. Error if HeapRef was allocated by another heap.

In this API, the 'heap' parameter is a register name via linking `{ "MyRegister" => "heap" }`. The heap is not stored in this register, rather is associated (via '%tl.arc'). The valid scope of HeapRef is determined by the scope of 'heap', i.e. it's an error to store a HeapRef to any longer-lived heap. We can support persistent heaps bound to 'sys.db.\*'. 

Although a heap API isn't difficult to implement, it complicates conflict analysis and garbage collection, and it introduces a form of inconsistency: we cannot conveniently couple 'methods' with heap references. For these reasons, I'm reluctant to include a heap API. But it is a viable feature.

## Concurrency

The program model supports fork-join coroutines. The normal concern with coroutines is the absence of preemption. A coroutine must yield voluntarily. However, this problem is mitigated by transactional steps and a non-deterministic scheduler. A coroutine that does not yield in a timely manner can be aborted to be retried later. The runtime can evaluate several steps simultaneously, analyze for read-write conflicts, commit a non-conflicting subset of steps and retry the rest.

Essentially, the glas program model favors [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control).

Concerns remain regarding starvation and rework. Rework can be mitigated based on static or empirical conflict analysis, scheduling operations that are likely to conflict in different timeslots. Resolving starvation will generally require attention from the developer, e.g. introducing intermediate steps or queues.

Usefully, this approach to concurrency can also recover from some error conditions. For example, if a step is aborted due to assertion failure or divide-by-zero, we could automatically retry and continue after a concurrent operation updates the relevant state. This would also apply to fixing state through a debugging API, or fixing code through live coding.

If a coroutine step includes non-deterministic choice, the runtime may try both choices. This can be useful for expressing timeouts, e.g. one choice waits on the clock, another waits on an empty queue, and the runtime commits whichever can proceed first. However, we can extract some concurrency from non-deterministic, atomic loop structures. See *Transaction Loops*. 

### Transaction Loops

An intriguing opportunity for concurrency and reactivity arises in context of non-deterministic, atomic loop structures. For example:

        while (atomic Cond) do { atomic (choice Op1 Op2 Op3); yield }

This loop represents sequential repetition of a yield-to-yield transaction. Isolated transactions are equivalent to sequential transactions. Thus, we can implement this loop by running many cycles simultaneously. Running the exact same operation would guarantee read-write conflicts. But, with non-deterministic choice, we can potentially run different choices concurrently without conflict.

Predictable repetition simplifies incremental computing. Instead of fully recomputing a transaction on every cycle, we can introduce checkpoints (via annotations) for partial rollback and replay. With some careful design, each choice has a stable prefix but an unstable suffix where most work is performed (e.g. reading and writing queues).

In some cases, we may determine that some branches are unproductive, e.g. leading to failure, divergence, or an idempotent update. In these cases, the runtime may wait indefinitely for observed state to change. This provides a simple basis for reactive systems.

Unfortunately, implementation of these optimizations is a daunting task. The opportunity here is not easy to grasp. My vision for glas systems benefits enormously from transaction-loop optimizations, but short term we will rely on the more conventional coroutines.

### Distribution

I envision a 'runtime' distributed across networked node, and an application running upon it. This requires compatible design of effects APIs, e.g. supporting multiple filesystems, network cards, and clocks.

In the worst case, we can run every application step in a distributed transaction. However, this is terribly slow and fragile to network faults. To effectively leverage a distributed runtime, we must architect applications such that most steps run on one node, and most remaining steps on two, with very few transactions touching three or more.

Behavior can be distributed. Coroutines can migrate based on which physical resources a current step is accessing. A non-deterministic transaction loop can mirror choices where locality is irrelevant, and partition choices where locality is relevant.

State can be distributed. Read-mostly registers can be mirrored, with updates propagated in a wavefront. Other registers may migrate to their users. Of notable interest are queues, bags, and CRDTs:

* *queues* - modeled by a register containing a list. Reader takes from one end. Writer pushes to the other. (For convenience, a reader may also 'unread' data.) A runtime can split the register between reader and writer nodes, and migrate writes as part of batched node sync.
* *bags* - modeled by a register containing a list. Reader removes a non-deterministic element. Writer inserts an element non-deterministically. A runtime can split the register across all nodes, each may read and write. Data migrates heuristically between nodes.
* *CRDTs* (Conflict-free Replicated Data Types) - a family of types, so pick a few useful ones. A runtime can split the register such that each node maintains a local replica. Replicas are synchronized as part of node sync (we still want isolated transactions, not weaker eventual consistency).

The runtime may recognize queues, bags, and CRDTs based on annotations, especially acceleration. 

*Note:* It is possible to change data usage patterns at runtime. Doing so generally requires a distributed transaction to rebuild the 'complete' value. But specific cases such as queue to bag may be trivial.

### Live Coding

Live coding can be understood as updating program behavior concurrently with its execution. Robust support for live coding is an essential aspects of my vision for glas systems.

There are two elements we can reasonably update in a runtime after a source change. First, calls to names can be transferred to the new namespace. Second, existing handler declarations that included names in the prior namespace can be recomputed in the new namespace. The latter includes updating the 'app' methods.

Coroutines are not friendly to live coding. We cannot robustly update a partially-executed procedure after its definition changes. We cannot easily introduce new coroutines for background tasks or eliminate defunct ones. But there are some mitigation strategies.

Developers can architect applications and design front-end syntax with live coding in mind. For example, we may favor tail recursion for long-running loops. And syntax for user-defined data types may encourage users to track version info and provide version-to-version update operations.

The runtime can support a clean transition. The 'app.switch' method runs first, providing an opportunity to perform critical state updates, run assertions and tests, observe application state to let the application control updates. The actual switch to new code is atomic, treated the same as mirrored state in case of distribution.

Eventually, transaction loops should offer a far more friendly foundation than coroutines.

## Event Handlers

For glas applications, we'll multiplex a configurable port for HTTP and remote procedure calls. The runtime will intercept a configurable subset of HTTP requests to support administration and debugging (e.g. "/sys/"). 

### HTTP 

The 'app.http' handler receives HTTP requests not intercepted by the runtime. 

Instead of operating on a raw binary, this receives an environment of handlers from the runtime providing features to swiftly route on the URL and access headers, and also write a valid, structured response. For details, I intend to borrow inspiration from the huge range of existing web server frameworks.

The 'app.http' method is not implicitly atomic, but it's convenient if most requests are atomic. Atomic requests are both more RESTful and more widely accessible.

*Aside:* Based on application settings and user configuration, we could automatically open a browser window after the application starts to provide a GUI.

### Remote Procedure Calls (RPC)

A lot of design work still needed here!

*Note:* It may be better to bind full RPC 'objects' instead of individual methods.

*Note:* It may be better to model RPC 'objects' as collections of app methods rather than reimplement routing.

 to bind the whole RPC 'objects' instead of individual methods.


        app.rpc(MethodRef, Argument) : [cb] Result
            cb(Argument) : [cb] Result 

To receive RPC requests, an application must do two things:

* define the 'app.rpc' handler
* publish an RPC interface, including MethodRefs

MethodRefs are not published. Instead, they are translated to random GUIDs when published, then translated back when method calls are received.




Publishing the API may be expressed effectfully. Details later. 


 the runtime will maintain some translation tables such that the MethodRefs serve as unforgeable capabilities.



indicate a 'procedure name' via abstract


To receive RPC calls, an application must provide an RPC interface. This interface can be expressed through 'app.settings'. In some cases, we might want a dynamic interface that varies based on application state. Settings can feasibly indicate a dynamic interface routed through a single method, similar to HTTP requests. An application can feasibly publish multiple RPC interfaces for multiple roles, registries, and trust levels.

In context of glas systems, RPC methods will typically be called within distributed transactions. Thus, there is an opportunity to abort the transaction, or to check for consistency of application state after a series of RPC calls but before commit. Further, we can feasibly apply various transaction loop application optimizations - incremental computing, waiting reactively in case of 'unproductive' transactions, concurrency based on stable non-deterministic choice. However, robust support for distributed transactions and those optimizations will require new RPC protocols.

Also, RPC calls may support limited algebraic effects for callbacks, and local concurrency via 'await Cond'. This allows for relatively flexible interactions, but further modifies the RPC protocol.

To perform RPC calls, an application must discover and reference RPC resources in the environment. However, in my vision for glas systems, I'm aiming to avoid first-class references. We can feasibly model a subscription to RPC interfaces as a dynamic table of discovered interfaces with some abstract state, selecting one when performing each call.

The details need a lot of work. But I believe transactional RPC will be a very effective and convenient basis for inter-process communication.

### Graphical User Interface (GUI)

See [GUI](GlasGUI.md). We could automatically load a GUI view if 'app.gui' is defined.

## Effects APIs

Most runtime-provided effects APIs are essentially some combination of FFI to access external resources and bgcalls to integrate 'safe' effects into a transaction. (An obvious exception is reflection APIs, including bgcalls.) Anyhow, I hope to push most effects API development from the runtime to the application, so we'll focus on FFI and bgcalls.

### Background Calls - The Transaction Escape Hatch

For safe operations with cacheable results, such as HTTP GET, it is very convenient to pretend that we acquired that data ahead of the current transaction. To support this pretense, we can leverage a reflection API. Proposed API:

        sys.refl.bgcall(Argument) : [op] Result
          # constraint: Argument and Result are non-linear
          op(Argument) : [$assert-demand] Result

Here 'op' should be a 1--1 handler, e.g. linking `{ "SelectedHandlerName" => "op" }` in context of the bgcall. The runtime will insert a coroutine just within scope of this handler, push Argument onto the call stack, then invoke 'op'. Eventually, 'op' returns Result on the data stack, which is returned to the caller. Result is not implicitly cached by the runtime, leaving that to 'op'.

The caller may abort due to read-write conflict, then rollback and retry, possibly resulting in a bgcall with the same handler and Argument. To avoid unnecessary rework, near-concurrent bgcalls with the same handler and Argument should attach to the same Result. To avoid unnecessary work, an '$assert-demand' handler is provided to 'op' that diverges if nobody needs Result.

Aside from safe queries, bgcall are useful to trigger background tasks, such as lazy processing of an event queue. This is arguably 'safe' because we had previously committed to perform that work 'later'. As a rule, all bgcall operations should be 'safe' in some sense acceptable to the developer even when the caller aborts.

*Note:* If 'op' is defined in context of '%atomic', it cannot await a response to any non-atomic external requests. In practice, 'op' is usually an app method, bypassing this concern.

*Note:* In context of transaction-loop optimizations, bgcalls can support incremental computing and non-deterministic choice. Logically, we're repeatedly executing the bgcall.

### Foreign Function Interface (FFI)

I propose a pipelined FFI where streaming commands may load libraries, manipulate a data stack and a few 'registers', call functions, query data or memory, and define functions (via [jit](https://github.com/frida/tinycc/tree/main)). The calling transaction will buffer commands, yield, then read responses.













## Random Data

Instead of a stateful random number generator, the runtime will provide a stable, cryptographically random field. 

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The result varies on Seed, N, and runtime instance. The Seed could feasibly be abstract state or plain old data.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes should produce the same result for a given query.

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

This API is designed assuming use of [libffi](https://en.wikipedia.org/wiki/Libffi) and the [Tiny C Compiler (TCC)](https://bellard.org/tcc/). For the latter, a  lets us redirect `#include` and resolve missing symbols via callbacks.

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

