# Glas Applications

## Overview

An application defines a subset of transactional procedures recognized by a runtime, such as 'step' to model background processing and 'http' to handle an HTTP request. The 'step' procedure is called repeatedly by the runtime, leveraging optimizations such as incremental computing and replication on non-deterministic choice as the basis for reactivity and concurrency. See *Transaction Loops* below.

These procedures interact with their environment via algebraic effects. Application state is bound to an external key-value database, providing a simple basis for live coding and orthogonal persistence. Communication within glas systems is via transactional remote procedure calls. Interaction with the filesystem, network, FFI, and other host resources is often asynchronous, committing to run non-transactional operations between transactional steps.

The runtime configuration abstracts access to host resources, e.g. an application's access to the filesystem may involve named roots like "AppData" that are routed to a specific folder by the configuration. A subset of configuration options are application-specific, supported by algebraic effects to query the application's `settings.*` methods. For example, the configuration may bind "AppData" to the filesystem based on `settings.name`. 

## Transaction Loops

In a transaction loop, we repeatedly evaluate the same atomic, isolated transactions. Many interesting optimizations and opportunities apply to this scenario based on the assumptions of atomicity, isolation, and predictable repetition.

* *Incremental.* Instead of recomputing everything from the start of the transaction, we can cache the transaction prefix and recompute from where inputs actually changed. Programmers can leverage this by designing most transactions to have a 'stable' prefix. This can generalize to caching computations that tend to vary independently.

* *Reactive.* In some cases, repeating a transaction is obviously unproductive. For example, if the transaction aborts, or if it repeatedly writes the same values to the same variables. In these cases, the system can simply wait for relevant changes in the environment before retrying. With some careful API design (and perhaps a few annotations), programmers can ensure wait conditions are obvious to the runtime without ever explicitly waiting.

* *Live Coding.* The behavior of a repeating transaction can be safely modified between transactions. Application state, schema update, and IDE integration also need attention, which might be expressed by inserting a handoff transaction. 

* *Concurrent.* For isolated transactions, there is no observable distinction between repetition and replication. This becomes useful in context of incremental computing and fair non-deterministic choice. For example, a `sys.fork(N)` effect might logically return a natural number less than N, but can be implemented by creating N copies of the transaction and evaluating them in parallel. Assuming those transactions do not have a read-write conflict, they can commit in parallel. This effectively models multi-threaded systems without reifying threads, which greatly simplifies interaction with live coding.

* *Distribution.* The properties of transaction loops also apply to repeating distributed transactions. However, distributed transactions are expensive! To minimize need for distributed transactions, we might locally cache read-mostly data and introduce specialized state types such as queues, bags, or CRDTs. Multiple transactions can write to the same queue without risk of read-write conflict, and a distributed queue could buffer writes locally.

* *Mirroring.* It is feasible to configure a distributed runtime where multiple remote nodes logically repeat the same transaction. While the network is connected, where the transaction runs influences only performance. There is no need to run a distributed transaction if another node will run the same transaction locally. Concurrent 'threads' have implicit affinity to nodes based locality. Some resources, such as state, may be cached or migrated to improve locality or load balancing. If the network is disrupted, some distributed transactions will fail, but each node can continue to provide degraded services locally and the system will recover resiliently by default.

* *Congestion Control.* If a repeating transaction writes to a queue that already contains many items, the runtime might reduce priority for evaluating that transaction again. Conversely, we could increase priority of transactions that we expect will write to a near-empty queue. A few heuristics like this can mitigate scenarios where work builds up or runs dry. This combines nicely with more explicit controls.

* *Real-time.* A repeating transaction can wait on a clock by becoming unproductive when the time is not right. This combines nicely with *reactivity* for scheduling future actions. Intriguingly, the system can precompute a pending transaction slightly ahead of time and hold it ready to commit at the earliest moment. This can form a simple and robust basis for real-time systems.

* *Adaptation.* Even without transactions, a system control loop can read calibration parameters and heuristically adjust them based on feedback. However, transactions make this pattern much safer, allowing experimentation without committing. This creates new opportunities for adaptive software.

* *Loop Fusion.* The system is always free to merge smaller transactions into a larger transaction. Potential motivations include debugging of multi-step interactions, validation of live coding or auto-tuning across multiple steps, and loop fusion optimizations.

Unfortunately, we need a mature optimizer and runtime system for these opportunities become acceptably efficient. This is an unavoidable hurdle for transaction loops. Meanwhile, the direct implementation is usable only for single-threaded event dispatch and state machines.

## Application Life Cycle

An application is represented by a namespace. For a transaction loop application, the first effectful operation is `start()`. This will be retried indefinitely until it commits successfully or the application is killed externally. After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions. The runtime may also call methods to handle RPC and HTTP requests, GUI connections, and so on based on the interfaces implemented by the application. If undefined, start and step default to pass.

The application may voluntarily halt via committing `sys.halt()`, asking the runtime to stop. To signal an application, we might call `stop()` upon specific OS events such as Ctrl+C or SIGTERM on Linux or WM_CLOSE in Window. The application would be expected to voluntarily halt within a short period after a stop signal.

## Live Coding Extensions

Source code may be updated after an application has started. In my vision for glas systems, these changes are usually applied to the running system. However, not every application needs live coding. Based on configuration and application settings we might disable this feature for some applications or require manual `sys.refl.reload()` to trigger the update. We could also configure the runtime to reload on external signals such as SIGHUP in Linux or a named event in Windows.

To support a smooth transition after a live update, the runtime will evaluate `switch()` (if defined) as the first operation in the updated code. If switch fails, it may be retried until it succeeds, or the runtime may try some other later version of code. This allows the runtime to skip 'broken' intermediate versions and also to favor a stable intermediate version (in context of DVCS) over the bleeding edge.

## Application Specific Settings

Applications may define ad-hoc `settings.*` methods. When evaluating application-specific runtime configuration options, the runtime will provide an algebraic effect to query settings in the selected application. There is an important layer of indirection: the runtime never directly observes settings, instead letting a configuration 'interpret' these settings in a runtime-specific way. Conversely, the application is abstracted except for these settings, which simplifies reasoning about refactoring.

*Note:* One reason for this design is to be amenable to staged applications or anonymous scripts. We can configure based on `settings.name = "foo"` independent of how an application is named within the configuration.

## Application Mirroring

The essential idea is that we'll configure a distributed runtime for some applications. Leveraging the *transaction loop* model, we can run the same transaction loop on multiple nodes, and heuristically filter which concurrent 'threads' run where based on the first node-specific effect or load balancing. See [*Mirroring in Glas*](GlasMirror.md) for more. 

Configurations and effects APIs must be designed with mirroring in mind. Mirroring is application specific, and many runtime resources will be mirror specific. This might be expressed by implicitly parameterizing such configuration options with a mirror identifier, or by including mirror identifiers in the configuration options.

## Application State

Application state is generally bound to a key-value database. Developers are encouraged to favor the database over the filesystem to more conveniently integrate with transactions, content-addressed storage for large values, support for mirroring of data, and precise conflict analysis for specialized data types such as queues, bags, and CRDTs. Ideally, we can support performance, parallelism, and partitioning tolerance both between and within transactions.

A subset of keys may be local to the runtime or even to the transaction. This might align with simple naming conventions and provide a simple, robust basis for orthogonal persistence and limited parallelism or concurrency within transactions.

## Dynamic Code (TBD)

I need to revisit how code and references are bound dynamically.

There are use cases for integrating code at runtime without modifying source files. However, in context of live coding, first-class functions should be ephemeral or eschewed. This limits our options.

One viable option is `sys.refl.eval(Program, Method, Args) with Localization`. Users can memoize compilation of scripts to program values, and the runtime can further cache compilation of the program value based on the selected methods and actual branches. I suggest localization as an implicit parameter, but it could use ephemeral types instead. Reflection is useful to integrate runtime features such as type safety, logging, profiling, and assertions.

Another approach is hot patching, perhaps expressed as `sys.refl.patch(List of Mixin)` to statefully set a patch list, using the Mixin (MX) type from [namespaces](GlasNamespaces.md). The actual transition is deferred until successful live code 'switch' after committing the patch. Hot patching can be understood as a form of live coding where this list of patches is in-memory source code, applied to the original application namespace.

## Implicit Parameters and Algebraic Effects

It is useful to tie algebraic effects to the application namespace, such that we can control access to effects through the namespace. This also reduces risk of accidental name collisions. Implicit parameters might be modeled as a special case of algebraic effects.

First-class functions and objects are relatively awkward in context of live coding and orthogonal persistence. This could be mitigated by escape analysis, e.g. runtime or ephemeral types might restrict which variables are used. But for glas systems I'd like to push most higher order programming to an ad-hoc combination of staging and algebraic effects, reducing need for analysis.

## Remote Procedure Calls? Defer. 

TODO: needs revisit with much more attention to application composition

Transactional remote procedure calls with publish-subscribe of RPC resources is an excellent fit for my vision of glas systems. With careful design of the distributed transaction, transaction loop features such as incremental computing and replication on non-deterministic choice can be supported across application boundaries. The publish-subscribe aspect simplifies live coding and orthogonal persistence.

Concretely, applications will define a few methods such as `rpc.event` to receive incoming requests and `rpc.api` to support registration and search. The system might define `sys.rpc.find` to search the registry and `sys.rpc.call` to perform a remote call. We might introduce `sys.rpc.cb` callbacks to support algebraic effects and higher order programming over RPC boundaries. A single transaction may involve many requests to each of many applications.

I need to work on these details in context of application composition. It should be feasible for application components to construct an 'RPC' graph between eachother based on application overrides or implicit parameters. Probably requires support for defining runtime-scoped registries.

Security should mostly be handled based on a compositional registry model, filtering, routing, and rewriting metadata based on trust, topic, and roles for each component registry. I'll need to consider this model carefully.

For performance, it is feasible to perform partial evaluation of given RPC events and distribute this code for local evaluation. Conversely, when performing a remote call, we could send a continuation to remotely handle the result, avoiding unnecessary network traffic.

*Note:* The runtime will maintain its own TCP/UDP listener for RPC requests, usually shared with the 'http' interface. This is configurable and may be application specific.

## Defunctionalized Procedures and Processes? Low Priority.

To support 'direct style' in programming of network connections, filesystem access, and FFI it is convenient if a front-end syntax knows to compile some sequences of operations into state machines that yield and commit to await a response in a future transaction. Intriguingly, we might also introduce `atomic {}` sections to force multiple steps to complete within a single transaction.

A relevant concern is stability of these procedures in context of live coding. To simplify manual stabilization, we might only yield at explicit, labeled 'yield' points. This also encourages coarse-grained steps, which mitigates performance overhead.

I feel this feature would be useful as a bridge between glas and host systems.

## HTTP Interface

An application can implement an interface `http : Request -> Response` to receive HTTP requests through a configurable port, which is also shared with RPC. By defining 'http', users can support ad-hoc views and user interactions with an application, much more flexible than console IO. Regardless, the `"/sys"` path is reserved by the runtime and implicitly routed to `sys.refl.http` to support access to logs, debugger integration, administrative controls, and so on. 

The Request and Response types are binaries that include the full HTTP headers and body. However, they will often be accelerated under the hood to minimize need for redundant parsing and validation. Some HTTP features and headers, such as HTTP pipelining, will be handled by the runtime and are not visible to the application. Authorization and authentication should be configurable.

By default, every HTTP request is handled in a separate transaction. If this transaction aborts due to system state or read-write conflict, it is implicitly retried until success or timeout, providing a basis for long polling. I propose custom response header 'Commit: false' to return the response - minus this header - without committing. This is potentially useful for safe HTTP GET requests or invalid HTTP POST requests. Default commit behavior may be configurable. Eventually, custom headers could support multi-request transactions. 

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

