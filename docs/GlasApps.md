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

*Aside:* An intriguing alternative is to express an application as a collection of aggregators, e.g. for background behavior, HTTP routes, and RPC methods. However, I imagine we'll leave this style of expression to application frameworks that ultimately define the aforementioned methods.

### Composition

It is feasible to define new applications in terms of composition, extension, inheritance, and override of existing applications. We could develop macros to support composition. 

I imagine we'll ultimately want front-end syntax oriented around composition of applications, with great defaults for partitioning registers, routing HTTP, composing TTY interfaces via pipelines or something like ncurses. 

## Standard Behavior

The runtime provides an initial namespace of registers and methods to the application:

* 'app.\*' - Fixpoint definition of application. Useful for expressing inheritance and override when composing applications. Similar to 'self' in OOP.
* 'g.\*' - ephemeral, 'global' registers bound to runtime instance; initially zero.
* 'sys.\*' - system APIs, e.g. network, filesystem, clock, FFI, reflection
* 'db.\*' - shared, persistent registers, bound to a configured database

The runtime will set things up, fork a few coroutines to call 'app.main', loop 'app.http' and 'app.rpc', and handle background operations. When 'app.main' returns, the runtime will set a register to halt background processes then perform any cleanup.

## State

Registers in 'g.\*' or 'db.\*' are the primary locations for application state. Each register contains a value, glas data of arbitrary size. Though, if compiling with verified type annotations, a compiler can theoretically optimize for conventional i64, u32, or even struct representations.

Database registers are bound to a configured database. This can support asynchronous interactions between applications. It is feasible to share register type annotations with the database to better detect risk of inconsistency upon application start or between apps. We can abort transactions that would commit invalid states, modulo use of reflection APIs to debug a database.

## Concurrency

The program model supports fork-join coroutines where each yield-to-yield step is a transaction, and scheduling is non-deterministic. This gives us a form of preemption at the cost of rework. As a variant of [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control), avoiding rework and starvation are relevant concerns.

Although the schedule is non-deterministic, it isn't random. The scheduler may analyze program code and its dynamic behavior, attempting to develop an optimal schedule with high utilization, low rework, and acceptable fairness. Programmers are also expected to debug performance issues, e.g. introducing intermediate steps or checkpoints, queues for high-contention resources, etc..

Other than yielding, a coroutine may voluntarily fail or diverge with an error (such as an assertion failure). In these cases, we logically abort the transaction and retry. A retry may explore alternative non-deterministic choices or observe updated state. But if it's obvious that retry is unproductive, the runtime could arrange to wait for relevant changes.

The fork-join structure limits expressiveness but simplifies interaction with local state and methods. It still supports many useful concurrency patterns. 

### Transaction Loops

An intriguing opportunity for concurrency and reactivity arises in context of non-deterministic, atomic loop structures. For example:

        while (Cond) do { atomic (choice ...); yield }

This loop represents sequential repetition of a yield-to-yield transaction. Isolated transactions are equivalent to sequential transactions. Thus, we can implement this loop by running many cycles simultaneously. Running the exact same operation is useless. However, with non-deterministic choice, we can run the choices concurrently with optimistic concurrency control.

Predictable repetition simplifies incremental computing. Instead of fully recomputing a transaction on every cycle, we can introduce checkpoints for partial rollback. With careful design, each choice should have a stable prefix but an unstable suffix where most work is performed (e.g. reading and writing queues).

In case of unproductive loops (failed, diverged, or idempotent), a runtime may wait for relevant state changes. This provides a simple basis for modeling reactive systems.

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

This API does not support discovery. It's left to the application to publish the MethodURLs.

*Aside:* MethodURL does not have a canonical representation. Each runtime may use its own encoding, compression, encryption or signature, etc.. Regardless of encoding, it's opaque in the normal mode of use. The only critical features are being unforgeable, and stable enough to not significantly harm runtime-level incremental computing.

### Relative Bind for Composition

The 'bind' method provided to 'rpc' initially links to 'sys.rpc.bind'. However, within a composite application, we can intercept 'bind' to wrap a MethodRef to better support routing and other features. Essentially, 'bind' is relative while 'sys.rpc.bind' is absolute. 

### Revocation

Users can implement revocable capabilities by including expiration times or lookup keys in MethodRef. Expiration is obvious. In case of lookup keys, the capability is disabled if the lookup fails, and we also can conveniently store a large or mutable context with a small, stable MethodURL.

We can also revoke MethodURLs by changing the cryptographic secret so they no longer authenticate. This doesn't need to be all-or-nothing. In practice, we might wish to rotate secrets so old ones remain available for several hours. A minimum viable API:

        sys.refl.rpc.secret.max(N)          # set how many secrets to rotate
        sys.refl.rpc.secret.update(Binary)  # use a secret (random if empty)

Note that this doesn't allow the app to query its own secrets. The provided secret may be mangled in memory, e.g. storing a secure hash. However, it is feasible to support persistent secrets and thereby support persistent MethodURLs.

### Implementation

        POST /sys/m/encoded-methodref/sig HTTP/1.1

The earliest implementations of RPC might simply use HTTP. The callback method could be supported by a URL back to the caller, or via special headers in the response to indicate a callback instead of a final response. It is feasible - with clever encoding - to eventually support transactions, to support lazy loading of content-addressed data and reference to content-delivery networks.

I eventually will want a protocol that is more friendly for callbacks, transactions, transaction-loop optimizations, multiplexing, content-addressed data and integration with content-delivery networks, etc.. But, with HTTP, I can get something working immediately.

In an HTTP-based implementation, callbacks will likely be represented by runtime-internal MethodURLs, and must be revoked by the runtime when the 'cb' falls out of scope. However, a dedicated protocol should have a built-in notion of lexically scoped callbacks, avoiding that overhead.

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

In this case, the caller provides an 'op' to evaluate in a separate coroutine. That coroutine will run just within scope of op, processing Argument and returning Result. The op does not need to be atomic: it may freely yield, e.g. to await an HTTP response. After completion, Result is then returned to the caller.

If a bgcall is interrupted, the runtime does not immediately cancel the operation. There is a heuristic grace period, an opportunity to reattach via by repeating the call with a matching op and Argument. The runtime may share Result even between independent bgcalls. However, at some point op may test 'canceled' and pass. After this decision, any Result will be discarded, but op may continue to perform cleanup. OTOH, if op never queries 'canceled', the opportunity to attach remains open.

Known Limitations:
* A bgcall will wait indefinitely for Result. If op diverges, so does bgcall.
* Result is not held by the runtime longer than it takes to complete the transfer. However, op may explicitly maintain a cache.
* Potential for thrashing when op conflicts with the calling transaction. No mitigation, but easy to diagnose and debug. Not always a problem if it resolves in a few cycles.

Other than safe queries, bgcall is useful to trigger safe background tasks, such as lazy processing of a task queue. This is arguably 'safe' because we had previously committed to perform that work 'later'.

*Note:* In context of a transaction loop, bgcalls can support the incremental computing and non-deterministic choice optimizations. Logically, we're repeatedly executing the bgcall.

## Foreign Function Interface (FFI)

I propose a pipelined FFI model. A transaction builds a stream of commands to be handled by a non-transactional FFI thread. The FFI thread interprets this stream, loading libraries, calling functions, reading memory, perhaps even JIT-compiling C code (via TinyCC). Results are observed in a future transaction through a queue.

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
  * `create(Hints) : [ffi] ()` - create an FFI thread bound to register 'ffi' but abstracted. Error if location is already in use. 
    * With runtime support and appropriate hints, a separate FFI process is also feasible.
    * In a distributed runtime, hints would determine which node owns the FFI thread.
  * `fork() : [src,dst]` - duplicate an FFI thread from src into dst. This sends a command to duplicate thread-local state. The results queue will be duplicated for commands sprior to fork.
  * `close() : [ffi] ()` - sends a command to terminate the FFI thread, and clears the local 'ffi' state. This does not immediately halt the FFI thread.
    * Note: We might introduce methods in 'sys.refl.ffi' to browse and kill FFI threads.
  * `status() : [ffi] FFIStatus` - recent status of FFI thread:
    * *future* - FFI thread doesn't fully exist yet, newly created.
    * *ready* - FFI thread is awaiting commands, all prior commands complete.
    * *busy* - ongoing activity, still processing prior commands.
    * *error:(text:Message, ...)* - FFI thread is halted in a bad state. Unrecoverable without reflection APIs.
  * `link.lib(SharedObject) : [ffi] ()` - load a ".dll" or ".so" file. When looking up a symbol, last linked is first searched.
  * `link.c.hdr(Name, Text) : [ffi] ()` - redirects `#include<Name>` to `Text` in context of C JIT.  
  * `link.c.src(Text) : [ffi] ()` - JIT-compile C source and link (e.g. via Tiny C Compiler).
  * `call(Symbol, TypeHint) : [ffi] ()` - call a previously linked symbol. Parameters and results are taken from the thread's data stack, and the return value is pushed backk. TypeHint for `int (*)(float, size_t, void*)` is `"fZp-i"`. In this case, float 'p' should be at top of stack to match C calling conventions. 
    * Void type is elided, e.g. TypeHint for `void (*)()` is simply `"-"`.
  * `cscript(Text, Symbol, TypeHint) : [ffi] ()` - one-off JIT and call symbol. 
  * `mem.write(Binary) : [ffi] ()` - (type `"p-"`) send command to write a binary to a pointer found on the FFI thread's data stack. 
  * `mem.read() : [ffi] ()` - (type `"pZ-"`) given a pointer and size on the data stack, return a binary via the result stream.
  * `push(List of Data, TypeHint) : [ffi] ()` - send command to push data to FFI thread's data stack. TypeHint determines conversions, e.g. `"fZp"` may receive glas representations of a rational, an integer, and an abstract pointer in that order, i.e. pointer is last element. 
  * `peek(N) : [ffi] ()` - query a list of N items from the data stack. The FFI data stack tracks types, so no need to provide them. Notes:
    * N=0 returns empty list, useful to await for prior operations to complete.
    * floating-point NaNs and infinities aren't supported, result in error status.
    * order is consistent with push, i.e. last item was top of FFI thread data stack.
  * `xchg(Text)` - ad hoc stack manipulation, described visually. E.g. Text `"abcd-cdabb"` will swap two pairs of data then copy the new top item. Limited to 'a-z' and each may appear at most once in LHS.
  * `stash(N) : [ffi] ()` - move top N items from data stack to top of auxilliary stack, called stash. Order is same as repeating `stash(1)` N times, i.e. inverting order onto stash.
    * *Note:* The 'stash' op is intended to serve a role similar to %dip, hiding the top of the data stack until some operations complete.  
  * `stash.pop(N) : [ffi] ()` - move top N items from top of stash to top of data stack. Order is same as repeating `stash.pop(1)` N times.
  * *registers* - TBD. Maybe just support a register per upper-case character? Not a priority.
  * `results.read(N) : [ffi] (List of Data)` - read and remove N results from the results queue. First result is head of list. Diverges if insufficient data.
  * `results.unread(List of Data) : [ffi] ()` - push a list back into results for future reads.
  * `results.peek(N) : [ffi] (List of Data)` - as read, copy, unread.
  * `ptr.*` - a safety on a footgun. Ptr is an abstract data, and may only be shared between FFI threads ultimately forked from the same 'create' unless 'addr' is used at one and 'cast' at the other.
    * `addr(Ptr) : [ffi] Int` - view pointer as an integer (via intptr_t). Error if Ptr and FFI thread have different origin 'create'. 
    * `cast(Int) : [ffi] Ptr` - treat any integer as a pointer
    * `null() : Ptr` - pointer with 0 addr is a special case, accepted by any FFI
* `sys.ffi.pack() : [ffi] FFI` - package FFI thread into an abstract, linear object.
* `sys.ffi.unpack(FFI) : [ffi] ()` - rebind a previously packaged FFI thread.
* `sys.refl.ffi.*` - *TBD* perhaps debugging, browsing, CPU usage, force kill

This API is designed assuming use of [libffi](https://en.wikipedia.org/wiki/Libffi) and TinyCC. We'll also need a [version of TinyCC](https://github.com/frida/tinycc/tree/main) that supports callbacks for includes and linking.

Potential extensions:
* support for structs, e.g. `"{ysw}"`
  * or just use JIT for this.

## API Design Policy: Avoid Abstract References

References complicate conflict analysis, garbage collection, and schema changes. The latter is mostly relevant to live coding, but it isn't trivial. I would prefer to avoid them. Linear objects avoid or mitigate these issues, but it's awkward to always be migrating linear objects to whomever is using them. 

We can resolve this by shoving the linear object into a shared register. Or, alternatively, 'unpack' the linear object into an abstract volume of shared registers to support fine-grained conflict analysis and update static locations.

I propose that we build most APIs around the notion of unpacking linear objects into abstract volumes of references. We can 'pack' them up again for migration. But in many cases we'll just keep them unpacked all the time.

As a related point, it is not difficult to model a heap and abstract references to it, but I would prefer to avoid doing so.

## Regarding Filesystem, Network, Native GUI, Etc.

I'm hoping to build most APIs above FFI and bgcall, reducing the development burden on the runtime. We should stick with the 'unpacked linear object' concept instead of references in each case.

## Time

Query the system clock.

* `sys.time.now() : TimeStamp` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. the Windows NT epoch but with arbitrary precision.
* `sys.time.after(TimeStamp)` - fails unless `sys.time.now() >= TimeStamp`. Use this if waiting on the clock, as it provides the runtime a clear hint for how long to wait.

It is possible to wait on a clock and model sleeps, but not within a single transaction. Atomicity is semantic or logical instantaneity. Thus, 'yield' is always required. We can acquire time in one transaction, yield, and await that timestamp plus a sleep duration within another transaction. Timeouts can then be expressed as a non-deterministic choice between awaiting the clock and another operation.

Later, when we develop distributed runtimes, we'll want to extend this API to support multiple clocks. Otherwise, semantics get weird due to observing clock drift on "the same" clock.  Perhaps `"sys.clock.time.now() : [clock] TimeStamp"` plus clock creation and so on. With multiple clocks, we could reasonably argue that `sys.time` represents a non-deterministic choice of clocks, allowing best effort with drift.

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

A fundamental issue with console IO is that it isn't very composable. The default is to awkwardly mix streams and hope for the best. Or to avoid composing apps within a single process. But with translation, we could feasibly present a distinct 'sys.tty.\*' to each component application. This could support a few slightly-useful forms of composition, e.g. pipes or screens.

*Note:* I would like to mirror the terminal through the runtime HTTP interface, e.g. `"/sys/tty"` via [xterm.js](https://xtermjs.org/).

## Reflection

* sys.refl.src.\* - access to abstract '%src.\*' metadata from load time.

