# Glas Applications

## Overview

A *Transaction Loop* application implements an interface of transactional methods such as a repeating 'step' for background processing and 'http' to receive HTTP requests. It is feasible to extend transactions across multiple applications through transactional remote procedure calls and a distributed transaction protocol. 

This is subject to many useful optimizations: fork on non-deterministic choice, incremental computing of a stable prefix, wait for changes before repeating an unproductive transaction, and more. Relying on these optimizations, transaction loops support concurrency, reactivity, live coding, and distributed programming. This is a great fit for my vision of glas systems, but does require careful design to simplify optimization.

Unfortunately, most external systems - filesystem, network, FFI, etc. - won't support transactions. This forces asynchronous interaction, i.e. write a request, commit, read response in a future transaction. There is a workaround for safe, cacheable  operations such as HTTP GET. But where multiple transactions are required, we might favor a direct-style procedural language that compiles into a stateful transaction loop. 

This document aims to develop and describe the interface a transaction loop application should expose to a runtime, the API the runtime should expose to the application, and some exploration of a direct style language.

## Transaction Loop

A transaction loop system involves repeatedly running atomic, isolated transactions. Many optimizations and opportunities apply to transaction loops.

* *Incremental Computing.* Instead of recomputing from the start of a transaction, we can cache and run from where relevant changes are first observed. Programmers can leverage this by designing transactions to have a stable prefix. 

* *Reactive.* If repeating a transaction would obviously be unproductive, e.g. it would abort or outputs are idempotent, we can instead wait for a relevant change. This involves implicitly setting triggers on observed input sources. 

* *Parallel.* The system can optimistically evaluate multiple transactions at the same time, then abort a few where read-write conflicts occur. Upon repetition, the system can heuristically schedule to reduce repeating conflicts. 

* *Concurrent.* In case of non-deterministic choice in a repeating transaction, we can fork and evaluate both choices in parallel. If there is no conflict, both may commit. Stable repeating choice is akin to multi-threading. 

* *Distributed.* Transactions may be distributed via remote procedure calls or a mirrored runtime. Remote transaction loops can interact asynchronously through shared queues, bags, or CRDTs.

* *Congestion Control.* Instead of random choice, a scheduler may heuristically favor transactions that drain full buffers or fill empty buffers. A few heuristics can help control buffer sizes and improve the ratio of productive transactions.

* *Real-Time.* Insofar as repeating transactions have controllable size, a hard real-time scheduler is feasible. When a transaction waits on the clock, the system can evaluate slightly ahead of time then hold ready to commit. 

* *Live Coding.* We can model a repeating transaction as reading and interpreting its own code, automatically integrating changes in coded behavior. This does leave challenges like schema update, but it simplifies the problem.

* *Orthogonal Persistence.* Application state is separate from the transactions. A system can easily abort the current transactions and serialize the state. However, some features such as open files require special attention.

The main challenge is implementing the optimizer. Without optimization, transaction loops will usually perform poorly compared to conventional application architectures. At most, we can efficiently implement a single-threaded event dispatch loop to support early tooling and bootstrap.

## Settings

To support configuration, applications should define an ad-hoc `settings(Query)` method. The runtime does not query settings directly. Instead, the runtime provides access to settings when querying the configuration for application-specific runtime options. This indirection is convenient for portability and security. 

Settings will usually be evaluated at compile-time, and lack access to stateful effects. However, they might have access to stateless effects such as querying for configuration features. Again, to serve

  configuration. And the configured properties.

 typically be evaluated before any effectful operation.

The glas executable may support multiple run modes and select a runtime based on settings. This document assumes a transaction loop mode is selected.

## Life Cycle

For a transaction loop application, the first effectful operation is `start()`. This is logically retried indefinitely until it commits successfully or the application is killed externally. After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions, modeling the main loop. 

Insofar as the 'step' method is non-deterministic, the runtime may fork and evaluate both options in parallel, and even commit both if there is no read-write conflict. If a fork aborts (or is otherwise obviously unproductive), the runtime may heuristically set some triggers to wait for a relevant change before retrying. This, together with some incremental computing optimizations, provides a simple basis for concurrency and reactivity that is also friendly to live coding.

Between 'step' transactions, the runtime may call 'rpc' or 'http' or 'gui' based on external events. It is possible to define applications without 'step' that only act based on external events. The runtime may optimistically evaluate these events in parallel with steps, but may be forced to abort and retry a conflicting step or event. Some applications may leave 'step' undefined, depending entirely on external events.

An application may voluntarily terminate by calling and committing a `sys.halt()` effect. Depending on configuration, SIGTERM, Ctrl+C, or WM_CLOSE events might also kill the application. Otherwise, the application runs indefinitely, i.e. until killed externally by debugger or operating system. Aside from halting, we may also support restarts, clearing all runtime state.

## Administrative Control and Orthogonal Persistence

Other than starting and killing an application, we might want a few other standard administrative controls. We could use these controls to pause and continue the 'step' operations separately from disabling and enabling RPC or HTTP requests. In context of orthogonal persistence, it is also convenient if we can hibernate and awaken the application without fully killing it. Relatedly, applications could also have something like a power save mode where it continues to run but with severe quotas. 

Not everything requires informing the application. But for orthogonal persistence, we could benefit from a `hibernate(status)` event, or perhaps some way to query the runtime for the status.

## Mirroring

It is feasible to configure a 'distributed' runtime, i.e. a runtime that overlays two or more networked machines. The *Transaction Loop* model is a great fit because it formally doesn't matter where a transaction is initiated so long as references have consistent meaning. In a distributed runtime, some references will be associated with resources bound to specific machines (e.g. open files or network sockets), and in some cases a distributed transaction is required. 

For both performance and partitioning tolerance, we must minimize need for distributed transactions. This requires careful design of the application, e.g. favoring queues (or bags, or CRDTs) to communicate between asynchronous tasks that may run on separate machines. However, assuming a suitable application, a runtime may heuristically abort any 'step' transaction where the first effect involves a remote resource, implicitly partitioning steps between machines for locality.

Conveniently, when the network is fully connected, a mirrored transaction loop is formally equivalent to running on a single machine. When disconnected, each mirror might continue with degraded service, and may notice the disruption (e.g. via reflection or timeouts) and behave accordingly. When reconnected, blocked distributed transactions will proceed opportunistically. In theory, this should result in more survivable systems than most manual approaches to mirroring. 

There are still some challenges. For example, a queue can support multiple independent writers, and we might eventually serialize transactions from one writer before another. But this may prove a little awkward if each writer is also time-stamping each message. We can only insist that each writer is using an independent clock, which may logically drifted, and perhaps aim to mitigate the extent of drift (e.g. by interleaving writes from separate transactions that the reader hasn't observed yet). State, filesystem, and network APIs must also be designed with mirroring in mind.

Anyhow, simplified mirroring of applications is one of my design goals for glas systems, and I believe transaction loops support this admirably. See [*Mirroring in Glas*](GlasMirror.md) for more. 

## State

An application will declare external registers or variables as needed to hold state between transactions. These should be subject to renaming and restrictions similar to namespace translations. Depending on the final translation, some variables might bind to a persistent database while others bind to runtime or even transaction-local memory.

For open files and network sockets and similar resources, we might instead maintain state in abstract linear data, i.e. values that cannot be directly observed, copied, or dropped. These values might be runtime scoped, e.g. forbidding storage in the persistent database or transfer over remote procedure calls. This is easily enforced with dynamic types via metadata bits in packed pointers. However, this does limit 'orthogonal persistence' to be semi-transparent, i.e. not all values may be written into persistent storage.

In addition to basic get/set variables, runtimes should support queues, bags, and perhaps a few CRDTs. Introducing a few specialized state models can simplify conflict analysis and maximize parallelism for concurrent or distributed transactions, at least for applications that leverage them effectively.

*Note:* Developers may also manually push some application state to filesystem or external database, but favoring the built-in database lets the runtime handle integration with transactions, acceleration, incremental computing, content-addressed storage for large values, etc.. 

## Remote Procedure Calls

Remote procedure calls (RPCs) are a good fit for transactional interactions between applications. In conjunction with algebraic effects, RPC can support flexible synchronous interactions with the caller in a single transaction. Asynchronous interactions can be expressed using two calls in separate transactions: initiate an operation, request the result, perhaps with an intermediate token to identify the result. (*Note:* Asynchronous RPC 'callbacks' are feasible but conflict with my vision for live coding and resilient open systems.)

In context of glas systems, I propose to publish RPC APIs through a configured registry. Based on metadata, different parts of the API might be published to a 'trusted' registry versus a 'public' one, or filtered on topic or other criteria. Conversely, applications may search and discover registered RPC APIs and filter on metadata. Registries may be logical composites that perform further filtering and adaptation. Applications may come and go at any time, subject to continuous discovery, but registered APIs should be relatively stable and cacheable.

When calling a remote procedure, it is feasible to include a partial continuation. This is especially useful when the continuation involves further calls to the remote application, allowing a simpler RPC API and reducing need for round-trips. Conversely, when registering RPC methods, partial code for that procedure would allow the caller to perform some trivial calls locally or filter extraneous arguments to reduce network traffic. At the extreme, registering RPC methods that return cacheable, read-only views reduces to publish-subscribe.

An application might define an 'rpc' method, or perhaps a few standard `rpc.*` methods (e.g. `rpc.api` vs `rpc.event`), to provide the initial API and receive events. Still need to work the details! In any case, this should be carefully designed to support partial evaluation and incremental computing, e.g. based on static parameters.

*Note:* The runtime should maintain the TCP/UDP listener for RPC requests, perhaps shared with 'http' and debugger access. The details are configurable and may be application specific.

## HTTP Interface

An application can implement an interface `http : Request -> Response` to receive HTTP requests. Request and Response are binaries that include full HTTP headers and body. However, they may be accelerated to mitigate redundant parsing and validation. Because 'http' is flexible and relatively easy to implement compared to the proposed RPC or GUI models, we'll rely on it heavily for early development, debugging, and integration of glas systems.

By default, each 'http' request is handled in a separate transaction. If this transaction aborts, it is logically retried until it succeeds or times out. To integrate with asynchronous server operations, an initial POST request might commit then return a 303 response, triggering a GET request that awaits the result or times out. Implicit retry is also useful for long polling. Eventually, custom HTTP headers might support multi-request transactions.

To simplify integrated development and debugging, I propose for the runtime to reserve a path such as `"/sys"` for reflection and event APIs. The exact path and authorization requirements may be configurable. 

*Note:* Websockets and server-sent events (SSE) do not align nicely with transactions. If necessary, users could manually implement HTTP over a TCP API. But, for most cases, I expect long polling is adequate and much more convenient.

## Live Coding Integration

Source code may be updated after an application has started. In my vision for glas systems, these changes are usually applied to the running system. However, not every application needs live coding, and we might want some application control over when an update is applied. 

It is feasible to disable live coding via application-specific configuration, or restrict it to some external events (e.g. via SIGHUP in Linux, a named event in Windows, or debugger events via HTTP). And the runtime could further defer the update until `switch()`, if defined in the updated code, successfully commits. This would allow skipping 'broken' intermediate versions of code, or delaying update when the application is in a fragile state.



## Dynamic Code

There are several viable approaches to dynamic code: Live coding. Dynamic eval. Hot patching of the application namespace. I think that 'eval' and live coding are perhaps the better options, and hot patching could be treated as a special case of live coding (e.g. applying a 'patch' variable to the code). 

See also [glas notebooks](GlasNotebooks.md).


## Implicit Parameters and Algebraic Effects

It is useful to tie algebraic effects to the application namespace, such that we can control access to effects through the namespace. This also reduces risk of accidental name collisions. Implicit parameters might be modeled as a special case of algebraic effects.

First-class functions and objects are relatively awkward in context of live coding and orthogonal persistence. This could be mitigated by escape analysis, e.g. runtime or ephemeral types might restrict which variables are used. But for glas systems I'd like to push most higher order programming to an ad-hoc combination of staging and algebraic effects, reducing need for analysis.


## Defunctionalized Procedures and Processes? Language and State Layer.

To support 'direct style' in programming of network connections, filesystem access, and FFI it is convenient if a front-end syntax knows to compile some sequences of operations into state machines that yield and commit to await a response in a future transaction. Intriguingly, we might also introduce `atomic {}` sections to force multiple steps to complete within a single transaction. In any case, each 'step' must result in an intermediate state that can be continued in a simple way, with minimal analysis and routing. It's best if we have fine-grained application state to support this, so we only need to detect changes, as opposed to constructing a very large value that we then need to analyze.

## Graphical User Interface? Defer.

My vision for [glas GUI](GlasGUI.md) is that users participate in transactions indirectly via user agent. This allows observing failed transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing, and also allows for users to 'search' for preferred outcomes in case of non-determinism. 

Analogous to `sys.refl.http` a runtime might also define `sys.refl.gui` to provide generic GUI access to logs, profiles, and debug tools.

Anyhow, before developing GUI we should implement the transaction loop optimizations and incremental computing it depends upon. 

## Background Eval

For heuristically 'safe' operations, such as reading a file or HTTP GET, or manually triggering background processing, it is convenient to have an escape hatch from the transaction system. Background eval can serve this role, relaxing isolation.

* `sys.refl.bgeval(MethodName, Args) with Localization` - evaluate `MethodName(Args)` in a background transaction, with implicit localization of MethodName. This background transaction logically runs prior to the current transaction, and inherits prior reads in the current transaction for purpose of read-write conflict analysis. Thus, in case of interruption, both the background operation and its caller will abort. Args are restricted to plain old data, and the return value cannot be ephemeral. 

One transaction scheduling triggering another in the future is a bad idea because it's a form of inaccessible state, but going backwards in time with a shared interrupt avoids this problem. There is significant risk of *thrashing* if the background operation is unstable, i.e. if it writes data previously read by the caller and doesn't swiftly reach a fixpoint, but this is easy for developers to detect and debug. 

## Foreign Function Interface

A foreign function interface (FFI) is convenient for integration with existing systems. Of greatest interest is a C language FFI. But FFI isn't trivial due to differences in data models, error handling, memory management, type safety, concurrency, and so on. For example, most C functions are not safe for use within a transaction, and some must be called from consistent threads.

A viable API:

* `sys.ffi(StaticArg, DynamicArgs)` - call a foreign function specified in the configuration by StaticArg. The StaticArg may in general represent a custom function or script involving multiple FFI calls.

This avoids cluttering application code with FFI semantics. The configuration is free to pass the StaticArg onwards to the runtime unmodified, but has an opportunity to rewrite it in an application specific way or adapt between runtimes with different feature sets. The runtime must understand from the rewritten StaticArg what to run *and* where. In most cases, we'll schedule the operation on a named FFI thread between transactions on a specific mirror.

It is feasible to construct or distribute required, mirror specific ".so" or ".dll" files through the configuration, instead of relying on pre-installed libraries on each mirror. In some cases, we could also compile StaticArg operations directly into the runtime.

*Note:* Where FFI serves a performance role, the StaticArg might indicate a non-deterministic choice of mirrors, providing opportunity for load balancing and partitioning tolerance. Naturally, this use case must carefully avoid mirror-specific pointers and resource references in arguments or results. Also, glas systems should ultimately favor *Acceleration* in performance roles, though FFI is a convenient stopgap.


## Non-Deterministic Choice for Concurrency and Search

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, it doesn't mean random choice. An optimizer could copy the thread and try multiple choices in parallel. A scheduler could systematically select forks in a predictable pattern. 

## Random Data

Random data needs attention for stability and performance in context of incremental computing, distribution, and orthogonal persistence. I've decided against a global CPRNG in this role because dealing with state and read-write conflicts becomes awkward. A viable alternative is to 'sample' a stable, cryptographically random field. 

* `sys.random(Index, N)` - on the first call for any pair of arguments, returns a cryptographically unpredictable number in range `[0,N)` with a uniform distribution. Subsequent calls with the same arguments will return the same number within a runtime.

This API can be implemented statelessly via HMAC, with configuration of initial entropy. Users may always roll their own stateful PRNGs or CPRNGs. 

## Time

Transactions are logically instantaneous, but they may observe the estimated time of commit. However, directly observing time is unstable. To simplify reactive and real-time systems, we can introduce an API to await a given timestamp. 

* `sys.time.now()` - Returns a TimeStamp for estimated commit time. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with arbitrary precision. Multiple queries to the same clock within a transaction should return the same value. 
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. Intriguingly, it is feasible to evaluate the transaction slightly ahead of time then hold it ready to commit. This is convenient for modeling scheduled operations in real-time systems.
* `sys.time.clock` - implicit parameter, reference to a configured clock source by name. If unspecified, will use the configured default clock source.

In context of mirroring, we might configure a non-deterministic choice of network clocks to simplify interaction with network partitioning.  

Note that `sys.time.*` should not be used for profiling. We'll introduce dedicated annotations for profiling, and access profiles indirectly through reflection APIs such as `sys.refl.http`. 

## Environment and Configuration Variables

The configuration controls the environment presented to the application. The configuration has access to OS environment variables and application settings. A viable API:

* `sys.env.get(Query)` - The configuration should define a function representing the environment presented to the application. This operation will evaluate that function with an ad-hoc Query. This should deterministically return some data or fail.

There is no finite list of query variables, but we can develop conventions for querying an 'index' that returns a list of useful queries. There is no support for setting environment variables statefully, but we could override `sys.env.get` in context of composing namespaces, and reference application state.

## Command Line Arguments

I propose to present arguments together with the environment. Unlike the OS environment variables, the configuration does not observe or interfere with arguments.

* `sys.env.args` - runtime arguments to the application, usually a list of strings

*Note:* These arguments don't include executable name. That property might be accessible indirectly via reflection APIs, but it's a little awkward in context of staged computing.

## Globs of Data? Defer.

RPC can support content-addressed data implicitly, but if we want to integrate content addressing manually with TCP or UDP messages we'll instead need suitable reflection APIs. To avoid troublesome interactions with a garbage collector, we'll also locally maintain a Context that associates content-addressed hashes to values. 

* `sys.refl.glob.write(&Context, Value)` - returns a binary representation of Value together with an updated reference Context that records necessary hashes. 
* `sys.refl.glob.read(Context, Binary)` - returns a Value, computed with access to a Context of external references. Fails if the binary contains any references not described in Context.

The exact representation of Context is runtime specific, but should be plain old data and open to extension and versioning.

## Console IO

We could support console IO from applications via `sys.tty.*` methods. Simple streaming binaary reads and writes are probably adequate. Viable API:

* `sys.tty.write(Binary)` - add Binary to write buffer, written upon commit
* `sys.tty.read(Count)` - read exactly Count bytes, diverge/wait if it's not available
* `sys.tty.unread(Binary)` - add Binary to head of read buffer to influence future reads.

We could optionally add some methods to control line buffering and input echo.

*Note:* I'd recommend the 'http' interface over console IO for any sophisticated user interaction. But console IO is still a good option for integration in some cases.

## Filesystem

The filesystem API should mostly be used for system integration instead of persistence. For persistence, the database API is much more convenient with full support for transactions, structured data, content-addressed storage, and mirroring. In contrast, filesystem operations are mostly asynchronous, running between transactions after commit, returning the response through an abstract runtime reference.

Instead of a single, global filesystem root, an application queries the configuration for abstract application named roots like "AppData". Filesystem paths are abstract to control construction of relative paths and enforce restrictions such as read-only access. In addition a user's filesystem, abstract paths may refer to mirrors, DVCS resources, or a simulated in-memory filesystem with some initial state. 

*Note:* In my vision of [applications as notebook interfaces](GlasNotebooks.md), the compiler will also capture an abstract reference to application source files to integrate projectional editors and live coding environment. Thus, the compilation environment is another source of abstract file paths.

## Network

It is feasible to support something similar to the sockets API, perhaps initially limited to TCP and UDP. However, network interfaces (and possibly port bindings) should be restricted and abstracted by the configuration. 




Network interfaces abstracted through the configuration and are obviously mirror specific. If we open a TCP listener on network interface "xyzzy", we might ask the configuration what this means to each mirror. For each mirror we might return a list of actual hardware interfaces. Perhaps "xyzzy" is a specific interface on a specific mirror, and we only listen at one location. Or perhaps multiple mirrors will have an "xyzzy" interface, requiring a distributed transaction to open the distributed TCP listener. 

Of course, each incoming TCP connection would still be bound to a specific network interface on a specific mirror. If we initiate a connection, having multiple hardware interfaces might be interpreted as a non-deterministic choice. Though, it might be convenient to let the configuration distinguish "xyzzy-in" vs "xyzzy-out". 


## Debugging

In general, debugger integration should be supported using annotations rather than effects. That is, it should be easy to insert or remove and enable or disable debugging features without influencing formal behavior modulo reflection. Runtime reflection can potentially observe performance or debug outputs, and should be modeled effectfully through APIs in `sys.refl.*`. 

### Logging

In context of hierarchical transactions and incremental computing, the conventional model of logging as a stream of messages is very awkward. It's more useful to understand logging in terms of reflection and debugger integration.

        # a useful syntax for logging
        log(chan, message) { operation }

This model for logging allows us to track a time-varying 'message' as it changes over the course of 'operation', and we can statically configure logging per 'chan'. The 'chan' description must be static, the 'message' expression is implicitly evaluated in a hierarchical transaction. It may fail. Modulo reflection, the log expression is equivalent to 'operation'. 

An implementation to efficiently capture every change to a message is non-trivial. But users can feasibly configure logging per chan to trigger on operation boundaries, periodic, random, or perhaps only when extracting the call stack to debug the operation.

For extensibility, I propose a convention that most log messages are expressed as dictionaries, e.g. `(text:"Message", type:warn, ...)`. The compiler and runtime system can potentially add some metadata based on where the log message defined. To support structured data and progressive disclosure, it is feasible to log an `(ns:Namespace)` of definitions including 'http' or 'gui' interfaces. For larger log messages, we must rely on structure sharing for performance.

Recent log messages may be accessible to an application through `sys.refl.http` and perhaps via structured reflection methods `sys.refl.log.*`. There may also be some opportunities for dynamic configuration of logging. 

### Profiling

Profiling might be understood as a specialized form of logging.

        # a useful syntax for profiling
        prof(chan, dynId) { operation }

Here we have a static chan, and a dynamic identifier to aggregate performance statistics while performing the operation. Gathered statistics could include entry counts, time spent, allocations, etc.. 

*Aside:* Log channels might also be configured to capture these statistics. Use of 'log' vs 'prof' is mostly about capturing user intention and simplifying the default configuration.

### Testing

Aside from automated testing described in [the design doc](GlasDesign.md), it can be useful to add assertions to programs as annotations. Similar to logging and profiling, tests could be associated with static channels to support configuration (e.g. test frequency, disable after so many tests, etc.), and it can be useful to support continuous testing over the course of an operation.

        # viable syntax
        test(chan, property, message) { operation }

In this case we might evaluate a property as pass/fail, and then evaluate the message expression only when the property fails. This might also be interpreted as a form of logging, except the default configuration would be to abort the current transaction on a failed test.

Other than inline testing, it might be feasible to express ad-hoc assumptions about final conditions just before commit. 

### Tracing? Tentative.

In some cases, it is useful to track dataflows through a system including across remote procedure calls and transactions. This can be partially supported by including provenance annotations within data representations. The [glas object](GlasObject.md) representation supports such annotations, for example. We will need something more to efficiently maintain provenance when processing data. I suspect this will need an ad-hoc solution for now.

### Mapping

For live coding, projectional editing, debugging, etc. we often want to map program behavior back to source texts. In context of staged metaprogramming, this might best be modeled as *Tracing* source texts all the way to compiled outputs. This implies provenance annotations are represented at the data layer, not the program layer. 

For performance, a late stage compiler might extract and preprocess annotations in order to more efficiently maintain provenance metadata (e.g. as if tracing an interpreter). But this should be understood as an optimization.

## Rejected Features

It is feasible to introduce some `sys.disable()` and `sys.enable()` operations, perhaps parameterized for specific events such as 'step' and 'rpc'. However, I'm not convinced this is a good idea. Runtime state is less visible to users than application state, and more likely to be forgotten. It's also imprecise. In comparison, users can easily add some application state and add some explicit conditions to specific 'step' threads or RPC calls.

It is feasible to introduce periodic operations that evaluate at some configurable frequency, perhaps a `status()` health check. However, I think it better to let users manage this more explicitly, using `sys.time` to control the frequency of some operations and using HTTP requests to get information about health. 

