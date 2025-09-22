# Glas Applications

The [glas executable](GlasCLI.md) lets users run an application defined in the configuration namespace or a separate script file. By convention, an application is named 'env.appname.app' in the configuration, or simply 'app' in a script. A script may reference shared libraries or applications defined in the configuration's 'env.\*' (via '%env.\*'), thus can compose apps.

## Methods

Applications are expressed as a collection of methods in the [program model](GlasProg.md). Useful methods include:

* 'settings' - guidance for runtime configuration. The runtime does not observe settings directly, instead providing access to settings when evaluating configuration options.
* 'main' - a procedure representing the main application process. Is evaluated as a sequence of transactions, each using '%yield' to commit a step and '%fail' to abort a step.
* 'http' - a flexible interface with many use cases (services, signaling, gui, etc.). The runtime opens a configurable port multiplexed with remote procedure calls. A configurable HTTP path (default "/sys/") is reserved for runtime use, e.g. built-in debug views and APIs. When composing applications, we can compose 'http' interfaces.
* 'rpc' - (tentative) receives remote procedure calls, frequently within a distributed transaction.
* 'gui' - see [Glas GUI](GlasGUI.md).
* 'switch' - in context of live coding, runs as the first transaction when updating code. If this transaction fails, we'll retry but continue with the old code until it succeeds.

Applications should define 'settings' to support application-specific configuration. Applications should define 'main' as primary behavior. Most apps should define 'http' because it's convenient. But depending on user-defined front-end syntax, these definitions might be derived automatically rather than explicitly defined by the user.

### Composition

It is feasible to define new applications in terms of composition, extension, inheritance, and override of existing applications. We could develop macros to support composition. 

I imagine we'll ultimately want front-end syntax oriented around composition of applications, with great defaults for partitioning registers, routing HTTP, composing TTY interfaces via pipelines or something like ncurses. 

## Standard Behavior

The runtime provides an initial namespace of registers and methods to the application:

* 'app.\*' - application methods may call each other, e.g. mutual recursion
* 'g.\*' - application-private 'global' registers, initially zero
* 'sys.\*' - system APIs, e.g. network, filesystem, clock, FFI, reflection
* 'db.\*' - shared, persistent registers, bound to a configured database

The runtime will set things up, fork a few coroutines to call 'app.main', loop 'app.http' and 'app.rpc', and handle background operations. When 'app.main' returns, the runtime will set a register to halt background processes then perform any cleanup.

## State

Registers in 'g.\*' or 'db.\*' are the primary locations for application state. Each register contains a value. In general, values may have arbitrary size. Type annotations may describe expected register types, potentially including size constraints. Insofar as a compiler can verify static type analysis, it is feasible to represent state very efficiently.

Database registers are bound to a configured database and thus shared with other applications. Type annotations may also describe these shared registers. Doing so provides an opportunity to verify interaction with the database prior to application start. It is also feasible to record type assumptions into the database to detect conflicting assumptions.

### Checked Shared State

Annotations may describe type assumptions on the shared datbase. The runtime may validate these assumptions to detect type errors, and may also write type assumptions into the database (together with metadata like app name) to support checked, asynchronous interactions. If necessary, we could block database transactions on a subset of registers while the user debugs.

### Rejecting References

It is easy to introduce and implement a heap-like reference API. Consider:

* `sys.heap.alloc : [heap] HeapRef<heap>` - allocate reference to a heap. 
* `sys.heap.rw(HeapRef<heap>, NewVal) : [heap] OldVal` - swap data with heap. 

Here the 'heap' argument is used associatively, supporting allocation of multiple heaps. It also influences lifespan and valid scope for HeapRefs. 

Unfortunately, a heap API complicate conflict analysis, garbage collection, and support for linear types. In context of live coding, heap refs complicate schema changes. And glas lacks any equivalent to first-class functions, so we introduce an inconsistency where 'methods' and 'registers' can be coupled to support stack objects, but we cannot couple methods to heap refs for first-class OOP.

Perhaps there will be sufficient demand to implement a heap-like API later, but I'd like to see how far we can go without.

## Concurrency

The program model supports fork-join coroutines where each yield-to-yield step is a transaction, and scheduling is non-deterministic. This gives us a form of preemption at the cost of rework. As a variant of [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control), avoiding rework and starvation are relevant concerns.

Although the schedule is non-deterministic, it isn't random. The scheduler may analyze program code and its dynamic behavior, attempting to develop an optimal schedule with high utilization, low rework, and acceptable fairness. Programmers are also expected to debug performance issues, e.g. introducing intermediate steps or checkpoints, queues for high-contention resources, etc..

Other than yielding, a coroutine may voluntarily fail or diverge with an error (such as an assertion failure). In these cases, we logically abort the transaction and retry. A retry may explore alternative non-deterministic choices or observe updated state. But if it's obvious that retry is unproductive, the runtime could arrange to wait for relevant changes.

The fork-join structure limits expressiveness but simplifies interaction with local state and methods. It still supports many useful concurrency patterns. 

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

There are two elements we can reasonably update in a runtime after a source change. First, calls to names can be transferred to the new namespace. Second, existing method declarations that included names in the prior namespace can be recomputed in the new namespace. The latter includes updating the 'app' methods.

Coroutines are not friendly to live coding. We cannot robustly update a partially-executed procedure after its definition changes. We cannot easily introduce new coroutines for background tasks or eliminate defunct ones. But there are some mitigation strategies.

Developers can architect applications and design front-end syntax with live coding in mind. For example, we may favor tail recursion for long-running loops. And syntax for user-defined data types may encourage users to track version info and provide version-to-version update operations.

The runtime can support a clean transition. The 'app.switch' method runs first, providing an opportunity to perform critical state updates, run assertions and tests, observe application state to let the application control updates. The actual switch to new code is atomic, treated the same as mirrored state in case of distribution.

Eventually, transaction loops should offer a far more friendly foundation than coroutines.

## HTTP 

The 'app.http' method receives HTTP requests not intercepted by the runtime. 

Instead of operating on a raw binary, this receives an environment of methods from the runtime providing features to swiftly route on the URL and access headers, and also write a valid, structured response. For details, I intend to borrow inspiration from the huge range of existing web server frameworks.

The 'app.http' method is not implicitly atomic, but it's convenient if most requests are atomic. Atomic requests are both more RESTful and more widely accessible.

*Aside:* Based on application settings and user configuration, we could automatically open a browser window after the application starts to provide a GUI.

*Note:* I am contemplating an alternative API. Instead of a toplevel 'http' method that handles routing to component methods, it seems feasible to 

## Remote Procedure Calls (RPC)

A significant benefit of built-in RPC in glas systems is the opportunity to integrate with transactions, transaction-loop optimizations, and my vision for GUI. But, short term, we can integrate conventional RPC without transactions.

A viable API:

        app.rpc(MethodRef, Argument) : [cb?, bind] Result
           cb(Argument) : [cb?] Result
           bind(MethodRef) : MethodURL
        sys.rpc.bind(MethodRef) : MethodURL
        sys.rpc(MethodURL, Argument) : [cb?] Result

        types MethodRef, Argument, Result = plain old data
        type MethodURL = friendly URL text, full URL
           # friendly: no spaces or quotes, balanced parens, etc.

MethodRef is application-provided data that supports routing, context, and a foundation for [capability-based security](https://en.wikipedia.org/wiki/Capability-based_security). The client calls an unforgeable URL, protected from tampering by cryptographic means such as HMAC signature. The optional 'cb' method supports flexible interactions with the caller before returning.

MethodURLs may be persistent or ephemeral, contingent on configuration and application settings. In my vision of glas systems, for greater compatibility with live coding, they're mostly ephemeral, discovered through publish-subscribe and automatically expired after several hours via rotating HMAC secrets. But this API doesn't directly address discovery. 

### Composing Apps

The 'bind' argument to 'app.rpc' supports composition. Initially, this links to 'sys.rpc.bind'. However, in the composite application, we will intercept 'bind' to wrap the MethodRef to support routing and other features. 

Essentially, 'bind' is relative while 'sys.rpc.bind' is absolute. Of course, we won't receive the compression benefits of relative paths.

### Implementation

        POST /sys/m/encoded-methodref/sig HTTP/1.1

Early implementations of RPC may simply use HTTP. The callback method could be supported by a URL back to the caller, or via special headers in the response to indicate a callback instead of a final response. It is feasible - with clever encoding - to eventually support transactions, to support lazy loading of content-addressed data and reference to content-delivery networks.

I eventually will want a protocol that is more friendly for callbacks, transactions, transaction-loop optimizations, multiplexing, content-addressed data and integration with content-delivery networks, etc.. But, with HTTP, I can get something working immediately.

*Note:* Until transactions are supported, 'sys.rpc' should be marked with the '%an.atomic.reject' annotation.

### Code Distribution

A round trip per call or callback adds a lot of latency to RPC. This latency encourages development of batch methods that do more work per trip. This easily leads to frustrating APIs with too many options that are never exactly what is needed. The natural endpoint of this evolution is to support all the options, by sending a script that is interpreted remotely. 

Better to skip the frustration. Implement every RPC API with at least one adequate scripting interface from early on.

An intriguing possibility is to compile RPC methods into scripts that partially run locally on the caller. Similarly, compile callbacks that partially run remotely. We can develop a MethodURL schema that supports scripting. And a pipeline could partially be encoded into a callback.

## Graphical User Interface (GUI)

I have an interesting [vision for GUI](GlasGUI.md) in glas systems, but it's contingent on those transaction-loop optimizations, and it will be experimental even then. Until then, use FFI for native GUI or 'app.http' for browser-based GUI.

## Background Calls - Transaction Escape Hatch

For safe operations with cacheable results, such as HTTP GET, it is often convenient to pretend that we already acquired the data before the current transaction. This pretense can be supported via reflection APIs that logically insert an operation before the current transaction.

Proposed API:

        sys.refl.bgcall(Argument) : [op] Result
          op(Argument) : [canceled] Result
          canceled() # monotonic pass/fail
          # constraint: Argument and Result are non-linear

In this case, the caller provides an 'op' to evaluate in a separate coroutine. That coroutine will run just within scope of op, processing Argument and returning Result. Result is then returned to the caller. Result is not implicitly cached for longer than it takes to complete this transfer, but op may maintain a cache.

There is a risk that the caller abandons the bgcall, e.g. due to read-write conflict. This doesn't halt the generated coroutine. There is an opportunity to reattach: concurrent bgcalls with the same op and Argument may share Result. Though, the runtime isn't required to do so. 

The op may test whether it has been 'canceled' and voluntarily terminate. The canceled condition is monotonic: once observed, any Result is ignored, and another bgcall with same op and Argument will evaluate in a new coroutine.

Other than safe queries, bgcall is useful to trigger background tasks, such as lazy processing of a task queue. This is arguably 'safe' because we had previously committed to perform that work 'later'. I'm sure there are many other reasonable concepts of safety.

*Note:* In context of a transaction loop, bgcalls can support the incremental computing and non-deterministic choice optimizations. Logically, we're repeatedly executing the bgcall.

## Foreign Function Interface (FFI)

I propose a pipelined FFI model. A transaction can buffer a stream of commands to an FFI thread or process. Those commands may include loading libraries, calling functions, stack shuffling, reading memory, even JIT-compiling C code via [TinyCC](https://github.com/frida/tinycc/tree/main). Upon commit, commands are delivered, results may begin to return. A future transaction can observe results and issue more commands.

This integration avoids many challenges of FFI, such as interaction with garbage collectors.

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

* `sys.ffi.*` -
  * `create() : [ffi] ()` - here 'ffi' is a register name, but state is bound associatively. Error if already in use! The runtime will allocate a new FFI thread.
  * `fork() : [src,dst]` - here 'src' must bind to a defined ffi, and 'dst' to an undefined one. We'll copy local resources immediately, i.e. query results, and send a command for the FFI thread to clone itself (copying stack, registers, etc.). Allocation of an OS thread may be lazy.
  * `status() : [ffi] FFIStatus` - recent status of FFI thread:
    * *uncommitted* - status for a 'new' or 'fork' FFI.
    * *busy* - ongoing activity in the background - setup, commands, or queries
    * *ready* - FFI thread is halted in a good state, can receive more requests.
    * *error:(text:Message, ...)* - FFI thread is halted in a bad state and cannot receive any more commands or queries. The error is a dict with at least a text message. The FFI cannot receive any more commands or queries.
  * `link.lib(SharedObject) : [ffi] ()` - SharedObject is runtime or adapter specific, but should indirectly translate to a ".dll" or ".so" file. When looking up a symbol, last linked is first searched.
  * `link.c.hdr(Name, Text) : [ffi] ()` - redirects `#include<Name>` to Text in future uses of 'link.c.src' or 'cscript'. These are the only headers available!
  * `link.c.src(Text) : [ffi] ()` - JIT-compile C source in memory and link (via Tiny C Compiler). Consider including a `#line 1 "source-hint"` directive as the first line of text to improve error output.
  * `call(Symbol, TypeHint) : [ffi] ()` - call a previously linked symbol. Parameters and results are taken from the data stack. TypeHint for `int (*)(float, size_t, void*)` is `"fZp-i"`. In this case, pointer 'p' should be at the top of the data stack. Void type is elided, e.g. `void (*)()` is simply `"-"`.
  * `cscript(Text, Symbol, TypeHint) : [ffi] ()` - invoke a symbol from a one-off C source. 
  * `mem.write(Binary) : [ffi] ()` - (type `"p-"`) given a pointer on the data stack, copy a binary to that location. Size is implied from the binary.
  * `mem.read(Var) : [ffi] ()` - (type `"pZ-"`) given a pointer and size on the data stack, return a binary, accessed through Var in the future.
  * `push(List of Data, TypeHint) : [ffi] ()` - write data to stack. TypeHint determines conversions, e.g. `"fZp"` can receive a rational, an integer, and an abstract pointer.
  * `peek(Var, N) : [ffi] ()` - read N items from data stack into Var. If N is 0, this reduces to a status check. Caveat: floating-point NaN or infinity will result in error queries, and pointers are abstracted
  * `copy(N) : [ffi] ()` - copy top N items on stack
  * `drop(N) : [ffi] ()` - remove top N items from stack
  * `xchg(Text)` - ad hoc stack manipulation, described visually. The Text `"abc-abcabc"` is equivalent to 'copy(3)'. In this example, 'c' is top of stack. Mechanically, we find '-', scan backwards popping values from stack into single-assignment local variables, then scan forward pushing variables back onto the stack.
  * `stash(N) : [ffi] ()` - move top N items from data stack to top of auxilliary stack, called stash. Preserves order: top of stack becomes top of stash.
  * `stash.pop(N) : [ffi] ()` - move top N items from top of stash to top of data stack.
  * `reg.store(Reg) : [ffi] ()` - pop data from stack into a local register of the FFI thread. Register names should be short texts.
  * `reg.load(Reg) : [ffi] ()` - copy data from local register of FFI thread onto data stack.   
  * `var.*` - receiving data from 'peek' or 'mem.read'. Var should be a short text.
    * `read(Var) : [ffi] Data` - Receive result from a prior query. Will diverge if not *ready*.
    * `drop(Var) : [ffi] ()` - Remove result and enable reuse of Var.
    * `list() : [ffi] List of Var` - Browse local environment of query results.
    * `status(Var) : [ffi] VarStatus`
      * *undefined* - variable was dropped or never defined
      * *uncommitted* - commit transaction to send the query
      * *pending* - query enqueued, result in the future
      * *ready* - data is ready, can read in current transaction
      * *error:(text:Message, ...)* - problem that does not halt FFI thread.
      * *canceled* - FFI thread halted before query returned. See FFI status.
  * `ptr.*` - safety on a footgun; abstract Ptr is scoped, may be shared between forks of an FFI, but not with independently created FFIs. Also cannot be stored to a database register.
    * `addr(Ptr) : [ffi] Int` - view pointer as integer (per intptr_t). Error if FFI thread does not belong to same OS process as Ptr.
    * `cast(Int) : [ffi] Ptr` - treat any integer as a pointer
    * `null() : Ptr` - pointer with 0 addr, accepted by any FFI
* `sys.refl.ffi.*` - we could do some interesting things here, e.g. support debugging of an FFI process. But it's highly runtime specific.

This API is designed assuming use of [libffi](https://en.wikipedia.org/wiki/Libffi) and TinyCC. We'll need the version of TinyCC that supports callbacks for includes.

## Time

Query the main system clock.

* `sys.time.now() : TimeStamp` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. the Windows NT epoch but with arbitrary precision. Multiple queries within a transaction will return the same value.
* `sys.time.after(TimeStamp)` - fails unless `sys.time.now() >= TimeStamp`. Use this if waiting on the clock, as it provides the runtime a clear indicator for how long to wait.

When we develop a distributed runtime, we'll probably extend this API to support multiple abstract clocks. But this API seems sufficient to get started. We might understand the default system clock as best-effort and non-deterministic in context of network partitioning and drift.

We can use 'sys.time.after' to wait on a clock. It is possible to express a sleep in terms of fetching time in one transaction then waiting on (time + sleep duration) after yield. 

*Note:* Favor profiling annotations, not timestamps, for performance metrics within a transaction.

## Random Data

Instead of a stateful random number generator, the runtime could provide a stable, cryptographically random field. The more stateful approaches are rather awkward in context of backtracking or transaction loops.

* `sys.random(Seed, N) : Binary` - return a list of N cryptographically random bytes, uniformly distributed. The result varies on Seed, N, and runtime instance. The Seed could feasibly be abstract state or plain old data.

## Arguments and Environment Variables

A runtime can easily provide access to OS environment variables and command-line arguments.

* `sys.env.list : List of Text` - return the defined environment variables
* `sys.env.get(Text) : Text` - return value for an OS environment variable
* `sys.env.args : List of Text` - return the command-line arguments

These will simply be read-only within an application, but users could intercept 'sys.env.\*' methods when calling a subprogram.

*Note:* Applications integrate the configuration environment at compile time through the namespace layer, '%env.\*'.

## Console IO

With users launching glas applications from a command-line interface, it is convenient to support user interaction directly through the same interface. The basics are just reading and writing some text, but it is possible to disable line buffering and input echo then implement sophisticated applications via [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or extended protocols.

A viable API:

* `sys.tty.write(Binary)` - write to standard output, buffered until commit. 
* `sys.tty.read(N) : Binary` - read from standard input. Diverges if not enough data.
* `sys.tty.unread(Binary)` - add Binary to head of input buffer for future reads.
* `sys.tty.ctrl(Hint)` - ad hoc control, extensible but mostly for line buffering and echo

The control hint is runtime specific, perhaps something like `(icanon:on, ...)`. I reserve standard error for runtime use - compile-time warnings, logging, etc..

The runtime may keep a large buffer of standard output available for use with the "/sys/" HTTP interface, perhaps via [xterm.js](https://xtermjs.org/). Use of 'unread' could also inject some user inputs through this view.

Composite applications can feasibly pipeline tty, but it would require careful translations of 'sys.tty' in the app definition.

