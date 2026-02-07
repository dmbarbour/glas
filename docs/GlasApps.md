# Glas Applications

The [glas CLI](GlasCLI.md) lets users run an application defined in the configuration namespace (`env.AppName.app`) or a separate script file that compiles to module defining `app`. 

## Basic Application Model

The basic application is represented by an "app"-tagged `Env -> Env` namespace term. Although structurally similar to the module type, the environments are different. 

The input environment provides access to the runtime, e.g. 'sys.\*' effects APIs and instance-specific registers. The output environment defines runtime-recognized application methods, such as:

* 'step' - executed repeatedly as transaction loop
* 'settings' - guide application-specific integration or configuration
* 'http' - receive HTTP requests from runtime. Shares TCP port with RPC and debug APIs. 
* 'rpc' - receive remote procedure calls; compared to 'http' shall better support transactions, transaction loops, structured data, algebraic effects, and object capability security
* 'signal' - OS or administrative signals, mostly to gracefully halt
* 'switch' - first operation on new code in context of live coding

There is no 'main' procedure. Instead, we express main-loop behavior with a transactional 'step' function, and we separate handlers for runtime-supported events like 'http' or 'rpc'. This design simplifies robust live coding, concurrency, and distribution, but requires sophisticated transaction-loop optimizations to recover performance. See the [program model](GlasProg.md).

The application halts by calling 'sys.halt' then committing to it.

*Note:* We can develop alternative application models, replacing the "app" tag.

### Application Adapter

The user configuration may define an application adapter, i.e. an `App -> App` namespace function. This provides a final opportunity to rewrite applications for portability or other purposes. In the general case, the adapter may even 'compile' applications expressed in intermediate representations, e.g. expressing applications via [Kahn process networks](https://en.wikipedia.org/wiki/Kahn_process_networks).

### Extension

As a final opportunity for inheritance and override, the input environment includes 'app.\*' as a fixpoint of the application definitions. 

Effectively leveraging this requires support from the front-end compiler. This could be an app-specific syntax, or a generic OOP-inspired syntax coupled with a keyword to retag a class as "app" or reverse for compatible apps.

*Note:* In theory, we can extend an application indirectly by extending the module that defines the application. But this is limited by the affine file dependency constraint (never load a file twice).

Module-level inheritance and override is restricted by the constraint against affine file dependencies (never load a file twice). 


*Note:* We can also extend and override a module that defines an application. However, this is hindered by affine file dependencies (don't load a file twice). Applications are closer to OOP, instantiated many times loading files.


This feature risks role confusion: we could instead extend a module that defines an application, leveraging the '$self.\*' fixpoint. But module-level inheritance and override is constrained by affine file dependencies, i.e. don't load a file twice. We extend and override apps more flexibly.

 we don't want to load files more than once, so appl

This is intended for fine-grained overrides without rebuilding entire modules. 

This provides a final opportunity to 

The 'app.\*' fixpoint argument to an application provides a final opportunity for inheritance and override of mutually recursive definitions. 

*Note:* There isn't much benefit over inheritance and override of the *module* that defines the application. And it may prove troublesome to syntactically distinguish application methods versus module functions. But application-level 

To leverage this requires careful attention from the front-end compiler to distinguish application methods from the module layer. Though, it's also useful to define applications in terms of extending

But the advantage is the opportunity to instantiate applications many times with different views of system effects and stateful registers, while sharing work and structure at the module layer.



The motive for this is to support inheritance, override, extension, and composition of applications. To support inheritance and override, the application type supports yet another latent fixpoint step, much like modules. Indeed, we'll essentially define the application type as a module with a few conventions on entry points and staging.



### Staging

Basic applications awkwardly, indirectly support staging insofar as they receive access to 'sys.\*' APIs before fully defining application methods. These APIs are then accessible via '%meta'

To avoid awkward interactions with laziness, parallelism, or caching, it is recommended to limit these operations to read-only or 'safe' queries. 

This mechanism for staging is likely to hinder separate compilation. The exception is if 'sys.\*' is used only to access runtime information known at compile time, such as runtime version info. In theory, the compiler can mix AOT and JIT compilation based on when definitions become available.

## Runtime-Provided Effects

The runtime provides an initial namespace of registers and methods to a basic application:

* 'sys.\*' - system APIs, e.g. network, filesystem, clock, FFI, reflection
* 'db.\*' - shared, persistent registers, bound to a configured database
* 'mem.\*' - ephemeral registers bound to runtime-local memory

The 'sys.\*' APIs are described below. The 'db.\*' and 'mem.\*' registers have relevant type distinctions: runtime-scoped data, such as FFI threads, cannot be stored to 'db.\*'. Depending on configuration, 'db.\*' may still be ephemeral. Because there are no long-running application methods, all relevant application state is held within 'db.\*' or 'mem.\*'.

We also receive 'app.\*' as an open fixpoint of the application.

provide some toplevel state visible to 'http' and other methods. Although these model global state, it is possible to scope access and partition application state between subcomponents (assuming support from the front-end compiler).

## State

The program model has built-in support for registers, and the application receives a few volumes of registers as 'db.\*' and 'g.\*'. But it isn't difficult to support first-class references to mutable state (like Haskell's IORef). A viable API:

* `sys.state.ref.*` - (tentative) first-class state
  * `new(Data) : Ref` - new reference initially containing Data; runtime-ephemeral
  * `db.new(Data) : Ref` - as 'new' but backed by the database; database-ephemeral
  * `with(Ref) : [op]` - pop Ref from stack, run 'op' with access as register 'ref'

Relative to second-class registers, references complicate static dataflow and conflict analysis, linear type safety, and garbage collection. They also introduce a source of hysteresis or path dependence for data schema change, e.g. in context of live coding. For these reasons, my vision for glas systems favors registers over references, and I do not provide references as a program primitive.

## Concurrency

Concurrency is built into the program model (non-deterministic coroutines, optimistic concurrency control), thus no separate effects API is required. But we can discuss some interesting patterns.


## Futures and Promises (Tentative)

A useful pattern for asynchronous and concurrent interaction is construction of `(Future<T>, Promise<T>)` pairs. The promise is linear and represents a single-assignment reference. The promised data is readable through the future. Ideally, holding the future is equivalent to holding the promised data modulo reflection APIs, thus futures are linear unless we guarantee promised data is non-linear.

Compared to full references, futures and promises have simpler interaction static dataflow analysis, linear types, and garbage collection. Of course, futures and promises are also less flexible, but users can model channels, e.g. `type Chan<T> = Future<(T, Chan<T>)|()>`, or more sophisticated structures to support most asynchronous interactions.

A viable API:
* `sys.promise.*` - 
  * `new : (Promise<T>, Future<T>)` - returns an associated promise and future pair, runtime-ephemeral. The promise is linear, but future is non-linear. Writing linear data to the promise is a type error, diverging at runtime if detected at runtime.
  * `new.linear : (Promise<T>, Future<T>)` - As `new` but with a linear future and the promise accepts linear data when written.
  * `read(Future<T>) : T` - await a future. This diverges unless the associated promise is assigned. 
  * `write(Promise<T>, T)` - assign a promise. This data becomes available within the current transaction, but only becomes visible outside the current transaction after commit.
* `sys.refl.promise.*` -
  * `called(Promise) : Promise | FAIL` - returns argument only if the promise has been 'called', i.e. if it seems there is current demand for the promised data. Monotonic: will implicitly 'call' the associated future in case we're observing temporary demand.
  * `call(Future) : Future` - This marks a Future as in-demand for purpose of a `called` check. Idempotent and monotonic: once committed, 'called' will always pass, and 'forgotten' will always fail.
  * `forgotten(Promise) : () | FAIL` - allows dropping a promise if the future will not be observed. This relies on a garbage collector to decide when the future has fallen from scope.
  * `fulfilled(Future) : Future | FAIL` - returns argument if the future is immediately available, i.e. such that 'read' returns immediately and does not diverge. This allows roughly observing the timing for when a promise is fulfilled. 

In theory, we can also support database-ephemeral futures and promises. I do not recommend this because it interacts very awkwardly with network disruption or node failure. Thus, futures and promises are currently restricted to the runtime (albeit, permitting a distributed runtime).

*Note:* It is feasible to implement futures and promises in terms of mutable references, but we lose out on a few runtime optimizations based on the single assignment constraint.

## HTTP 

The 'http' method receives HTTP requests on a runtime-configured port. This also simplifies composition of applications in contrast to each application listening directly on separate TCP sockets. TBD: How the request is input, how the response is output, how to model multi-step requests or SSE.

I imagine initial GUIs for glas systems will be based on HTTP. 

*Aside:* Based on application settings and user configuration, we could automatically open a browser window after the application starts to provide a GUI.

*Note:* I am contemplating an alternative API. Instead of a toplevel 'http' method that handles routing to component methods, it seems feasible to 

## Remote Procedure Calls (RPC)

A significant benefit of built-in RPC in glas systems is the opportunity to integrate with transactions, transaction-loop optimizations, and my vision for GUI. But, short term, we can integrate conventional RPC without transactions.

A viable API:

        rpc(MethodRef, Argument) : [cb?, bind] Result
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

I have an interesting [vision for GUI](GlasGUI.md) in glas systems, but it's contingent on those transaction-loop optimizations, and it will be experimental even then. Until then, use FFI for native GUI or 'http' for browser-based GUI.

## Background Calls - Transaction Escape Hatch

For safe operations with cacheable results, such as HTTP GET, it is often convenient to pretend that we already acquired the data before the current transaction. This pretense can be supported via reflection APIs that logically insert an operation before the current transaction.

Proposed API:

        sys.refl.bgcall(Argument) : [op] Result
          op(Argument) : [canceled] Result
          canceled() # pass/fail
          # constraint: Argument and Result are non-linear

        sys.refl.bgcall.async(Argument) : [op] Future<Result>
          # asynchronous variant of bgcall, immediately returns

In this case, the caller provides an 'op' to evaluate in a separate coroutine. That coroutine will run just within scope of op, processing Argument and returning Result. The op does not need to be atomic: it may freely yield, e.g. to await an HTTP response. After completion, Result is then returned to the caller.

In context of interruption, the runtime does not forcibly cancel the operation. Instead, the background operation tests for 'canceled' at its own discretion. This is a simple pass/fail, passing if there is no demand on Result. Cancellation is weakly monotonic: if observed *and* the observing transaction commits, all future 'canceled' tests will pass, and the final Result is treated as garbage and dropped.

However, while cancellation is not observed - or if the observer does not commit (enabling developers to model timeouts) - a runtime may opportunistically bind multiple requests to the same Result based on matching 'op' and Argument. This supports re-attach after rollback, but it also enables stable 'bgcall' ops to serve as a publish/subscribe query of sorts.

Some notes:
- It is possible the background operation itself has a read-write conflict with the caller. There is risk of thrashing. Fortunately, this is relatively easy to detect and debug.
- The runtime Result cache is ephemeral, short-lived. Anything more stable must be maintained by the background operation, either manually or via memo annotations.
- In context of stable, non-deterministic bgcalls, a runtime may freely evaluate every non-deterministic path and return non-deterministic Results. This integrates nicely with transaction loops.
- Aside from 'safe' read-only queries, bgcall is useful for demand-driven triggering of background tasks. Operations need only be 'safe' in the limited sense that side-effects are acceptable after caller aborts.

## Foreign Function Interface (FFI)

I propose a pipelined FFI model. A transaction builds a stream of commands to be handled by a non-transactional FFI thread. The FFI thread interprets this stream, loading libraries, calling functions, reading memory, perhaps JIT-compiling C code so we can directly express composite operations. Results are observed in a future transaction through a queue.

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
    * *error:(text:Message, code:Integer, ...)* - FFI thread is halted in a bad state. Unrecoverable without reflection APIs.
  * `link.lib(SharedObject) : [ffi] ()` - load a ".dll" or ".so" file. When looking up a symbol, last linked is first searched.
  * `link.hdr(Name, Text) : [ffi] ()` - redirects `#include<Name>` to `Text` in context of C JIT.  
  * `link.src(Text) : [ffi] ()` - JIT-compile C source and link (e.g. via Tiny C Compiler).
  * `call(Symbol, TypeHint) : [ffi] ()` - call a previously linked symbol. Parameters and results are taken from the thread's data stack, and the return value is pushed backk. TypeHint for `int (*)(float, size_t, void*)` is `"fZp-i"`. In this case, float 'p' should be at top of stack to match C calling conventions. 
    * Void type is elided, e.g. TypeHint for `void (*)()` is simply `"-"`.
  * `script(Text, Symbol, TypeHint) : [ffi] ()` - one-off JIT and call symbol. 
  * `mem.write(Binary) : [ffi] ()` - (type `"p-"`) send command to write a binary to a pointer found on the FFI thread's data stack. 
  * `mem.read() : [ffi] ()` - (type `"pZ-"`) given a pointer and size on the data stack, return a binary via the result stream.
  * `push(List of Data, TypeHint) : [ffi] ()` - send command to push data to FFI thread's data stack. TypeHint determines conversions, e.g. `"fZp"` may receive glas representations of a rational, an integer, and an abstract pointer in that order, i.e. pointer is last element. 
  * `peek(N) : [ffi] ()` - query a list of N items from the data stack. The FFI data stack tracks types, so no need to provide them. Notes:
    * N=0 returns empty list, useful to await for prior operations to complete.
    * floating-point NaNs and infinities aren't supported, result in error status.
    * order is consistent with push, i.e. last item was top of FFI thread data stack.
  * `move(Text)` - ad hoc stack manipulation, described visually. E.g. Text `"abcd-cdabb"` will swap two pairs of data then copy the new top item. Limited to 'a-z' and each may appear at most once in LHS.
  * `stash(N) : [ffi] ()` - move top N items from data stack to top of auxilliary stack, called stash. Order is same as repeating `stash(1)` N times, i.e. inverting order onto stash. If N is negative, moves data from stash to stack instead.
    * *Note:* The 'stash' op is intended to serve a role similar to %dip, hiding the top of the data stack until some operations complete.  
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

This API is designed assuming use of [libffi](https://en.wikipedia.org/wiki/Libffi) and TinyCC. We'll need the [version of TinyCC](https://github.com/frida/tinycc/tree/main) that supports callbacks for includes and linking.

This kind of API can be adapted to other targets, e.g. JVM, .NET, or JavaScript.

Potential extensions:
* support for structs, e.g. `"{ysw}"`
  * or just use JIT for this.

*Note:* Full orthogonal persistence of FFI seems infeasible, but FFI as a pipe to a separate thread or process at least can clearly indicate disruption. Ideally, FFI-based APIs should be designed to recover resiliently after a disconnect, so we can support orthogonal persistence later.

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
  * `sys.env.arg(Index) : Text` - access individual args (may simplify caching staged apps)

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

With FFI and bgcall handling external integration, reflection remains one area where the runtime cannot effectively delegate.

- sys.refl.src.\* - access to abstract '$src' metadata from compile time. 
  - Minimally, support examination of the the abstract data type, Src 
  - The `(%src.meta MetaData Src)` can bind metadata for context. 

- sys.refl.log.\* - access to log output streams
  - browse log Chans and their activity
  - access to log histories
  - potentially adjust runtime-local configuration options per Chan 

- sys.refl.prof.\* - access profiling stats
- sys.refl.trace.\* - access recorded traces

- sys.refl.view.\* - debug thyself, application
  - browse view Chans and their activity
  - create, clone, pack, unpack, and destroy linear view register contexts
  - query a view with a register context and callbacks

- sys.refl.tty.\* - maybe provide xterm view of console via HTTP?
  - access buffered memory of inputs and outputs
  - adjust buffer sizes
  - 'inject' inputs as if from user input 

- sys.refl.ffi.\* - debugging of FFI issues, mostly
  - browse active FFI threads 
  - view:
    - step counter(s)
    - data stack and stash
    - current command (if any) and start time 
    - pending buffered commands 
    - unprocessed results buffers
  - estimate CPU utilization (?) 
  - force kill thread (notably unsafe)

- sys.refl.bgcall.\* - debug existing bgcalls
  - browse active bgcalls (op and Argument)
  - view activity, progress, thrashing
  - force cancel or kill, possibly

- sys.refl.http - access runtime's built-in HTTP interface
- sys.refl.http.\* 
  - browse prior and active requests

- sys.refl.rpc.\* 
  - control authentication of MethodURL (e.g. rotating expirations)
  - metadata about past and current requests for debugging
  - may forcibly kill some requests

- sys.refl.g.\*
  - browse the application 'global' registers
  - may forcibly edit them

- sys.refl.db.\*
  - browse persistent registers in use by app
  - can also anything app *could* have bound
  - may forcibly update registers

- sys.refl.gc.\* - garbage collection stats; trigger GC manually
- sys.refl.sched.\* - conflicts, rework, backtracking, productivity

