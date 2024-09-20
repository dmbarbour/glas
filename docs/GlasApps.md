# Glas Applications

## Overview

A glas application is similar to an abstract OOP class. A [namespace](GlasNamespaces.md) may be expressed in terms of multiple inheritance. This namespace should implement interfaces recognized by the runtime, such as 'step' to model background processing. Access to effects, such as filesystem or network APIs, is represented by abstract methods to be provided by the runtime.

Application methods are transactional. Instead of a long-running 'main' loop, a transactional 'step' method is called repeatedly by the runtime. This allows interesting *transaction loop* features and optimizations between isolated transactions and non-deterministic choice. Other methods might handle HTTP requests, service remote procedure calls, or support a graphical user interface.

To simplify live coding and orthogonal persistence, application state is mapped to an external key-value database. To simplify conventional direct-style programming, a front-end syntax might compile to multi-step procedures, capturing continuations into application state.

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

An application is represented by a namespace. For a transaction loop application, the first effectful operation is `start()`. This will be retried indefinitely until it commits successfully or the application is killed externally. If undefined, defaults to a no-op. 

After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions. The runtime may also call methods to handle RPC and HTTP requests, GUI connections, and so on based on the interfaces implemented by the application.

The application may voluntarily halt via `sys.halt()`, marking the final transaction. To support graceful shutdown, a `stop()` method will be called in case of OS events such as SIGTERM on Linux or WM_CLOSE in Windows, but the application must still explicitly halt itself. An applications may voluntarily restart via `sys.restart()`, which resets runtime state (including open files, network sockets, etc.) and the next operation becomes `start()`. 

### Live Coding Extensions

To support live coding, a runtime might introduce a `sys.refl.reload(Filters)` method. This asks the runtime to scan for updates in the configuration and transitive source dependencies, recompile, and apply changes. If called within the stable prefix of a transaction loop, this becomes a continuous request. Aside from reloading external files, perhaps we could introduce a `sys.refl.patch.*` API to rewrite the application namespace, or let code be sourced partially from application state. This provides a controlled form of self-modifying code.

To support a smooth transition, the runtime will evaluate a `switch()` method as the first operation in the updated code. The will be retried until it succeeds, allowing the application to run tests and control the handoff. While updates are ongoing, the runtime may heuristically skip intermediate versions. With access to edit history, a runtime can heuristically search backwards for the most recent version that switches successfully. Unlike start, switch must assume the application has open file handles, network sockets, etc..

### Application Specific Settings

Applications may define ad-hoc `settings.*` methods. Settings methods are purely functional and accessible when evaluating any 'application specific' configuration option, such as the HTTP/RPC port, mirroring, or FFI bindings. The runtime never directly observes application settings. Instead, the runtime observes a configuration, which may refer to application settings according to community conventions.

## Application Mirroring

The essential idea is that we'll configure a distributed runtime for some applications. Leveraging the *transaction loop* model, we can run the same transaction loop on multiple nodes, and heuristically filter which concurrent 'threads' run where based on the first node-specific effect or load balancing. See [*Mirroring in Glas*](GlasMirror.md) for more. 

Configurations and effects APIs must be designed with mirroring in mind.

Mirroring is application specific. Some configuration options should be mirror specific. Similar to application settings, mirror specific configuration options could be evaluated once for each mirror with implicit parameters for mirror settings. Some mirror settings might come from the configuration, others from observing mirror nodes after establishing the distributed runtime.

Network interfaces abstracted through the configuration and are obviously mirror specific. If we open a TCP listener on network interface "xyzzy", we might ask the configuration what this means to each mirror. For each mirror we might return a list of actual hardware interfaces. Perhaps "xyzzy" is a specific interface on a specific mirror, and we only listen at one location. Or perhaps multiple mirrors will have an "xyzzy" interface, requiring a distributed transaction to open the distributed TCP listener. 

Of course, each incoming TCP connection would still be bound to a specific network interface on a specific mirror. If we initiate a connection, having multiple hardware interfaces might be interpreted as a non-deterministic choice. Though, it might be convenient to let the configuration distinguish "xyzzy-in" vs "xyzzy-out". 

Anyhow, we should think carefully about how a distributed runtime interacts with every effects API, not just network interfaces. A little attention here could greatly simplify distributed programming.

## Database Bindings

To simplify live coding and orthogonal persistence, application state is mapped to an external key-value database. Ideally, it should be easy to stabilize this binding across code changes, and to constrain a program subcomponent's access to certain regions of the database or to state in general.

One viable solution is to reserve names with a specific prefix for database keys, perhaps `sys.db.*`. Each name could refer to a variable, and the front-end language could by default map hierarchical components to hierarchical state, e.g. `{ "" => "foo.", "sys.db." => "sys.db.foo." }` for component 'foo'. But this solution requires awkwardly interpreting names to work with multiple lifespans, fine-grained permissions, or indexed collections.

An alternative is to incrementally construct an abstract database key. A few abstract key constructors are provided through `sys.db` and implicitly wrapped for hierarchical components. This pushes more work to an optimizer, but we can embed more information into keys (such as lifespans, permissions, associations) and we can easily support indexed collections. 

In addition to basic read-write variables, the database can support queues, bags, CRDTs, and other built-in types. A careful choice of built-in types could improve performance and partitioning tolerance in context of concurrency and mirroring.

## Remote Procedure Calls? Defer.

Transactional remote procedure calls with publish-subscribe of RPC resources is an excellent fit for my vision of glas systems. With careful design of the distributed transaction, transaction loop features such as incremental computing and replication on non-deterministic choice can be supported across application boundaries. The publish-subscribe aspect simplifies live coding and orthogonal persistence.

Concretely, applications will define a few methods such as `rpc.event` to receive incoming requests and `rpc.api` to support registration and search. The system might define `sys.rpc.find` to search the registry and `sys.rpc.call` to perform a remote call. We might introduce `sys.rpc.cb` callbacks to support algebraic effects and higher order programming over RPC boundaries. A single transaction may involve many requests to each of many applications.

Security should mostly be handled at the configuration layer. One viable solution is to configure a composite registry, composed of fine-grained registries for different trust groups or trust levels (and also for different topics). An application can publish RPC objects that are routed based on metadata to different registries. Metadata can be rewritten as objects are routed, allowing for search based on trusted origin.

For performance, it is feasible to perform partial evaluation of given RPC events and distribute this code for local evaluation. Conversely, when performing a remote call, we could send a continuation to remotely handle the result, avoiding unnecessary network traffic.

*Note:* The runtime will maintain its own TCP/UDP listener for RPC requests, usually shared with the 'http' interface. This is configurable and may be application specific.

## Defunctionalized Procedures and Processes? Low Priority.

To support 'direct style' in programming of network connections, filesystem access, and FFI it is convenient if a front-end syntax knows to compile some sequences of operations into state machines that yield and commit to await a response in a future transaction. Intriguingly, we might also introduce `atomic {}` sections to force multiple steps to complete within a single transaction.

A relevant concern is stability of these procedures in context of live coding. To simplify manual stabilization, we might only yield at explicit, labeled 'yield' points. This also encourages coarse-grained steps, which mitigates performance overhead.

I feel this feature would be useful as a bridge between glas and host systems.

## Dynamic Code (TODO)

Where we need dynamic code, we'll want mechanisms that interact nicely with live coding and the other features. One option is to interpret a program namespace in context of a localization. If paired with memoization or accelerated representations, we could cache safety checks and JIT compiled machine code. 

Another useful option is to let the runtime maintain an in-memory 'patch' on the application namespace, effectively live coding with controlled self-modifying code.


## Implicit Parameters and Algebraic Effects

It is useful to tie algebraic effects to the application namespace, such that we can control access to effects through the namespace. This also reduces risk of accidental name collisions. Implicit parameters might be modeled as a special case of algebraic effects.

First-class functions and objects are relatively awkward in context of live coding and orthogonal persistence. This could be mitigated by escape analysis, e.g. runtime or ephemeral types might restrict which variables are used. But for glas systems I'd like to push most higher order programming to an ad-hoc combination of staging and algebraic effects, reducing need for analysis.

## HTTP Interface

An application can implement an interface `http : Request -> Response` to receive HTTP requests through a configurable port, which is also shared with RPC. By defining 'http', users can support ad-hoc views and user interactions with an application, much more flexible than console IO. Regardless, the `"/sys"` path is reserved by the runtime and implicitly routed to `sys.refl.http` to support access to logs, debugger integration, administrative controls, and so on. 

The Request and Response types are binaries that include the full HTTP headers and body. However, they will often be accelerated under the hood to minimize need for redundant parsing and validation. Some HTTP features and headers, such as HTTP pipelining, will be handled by the runtime and are not visible to the application. Authorization and authentication should be configurable.

By default, every HTTP request is handled in a separate transaction. If this transaction aborts due to system state or read-write conflict, it is implicitly retried until success or timeout, providing a basis for long polling. I propose custom response header 'Commit: false' to return the response - minus this header - without committing. This is potentially useful for safe HTTP GET requests or invalid HTTP POST requests. Default commit behavior may be configurable. Eventually, custom headers could support multi-request transactions. 

## Graphical User Interface? Defer.

My vision for [glas GUI](GlasGUI.md) is that users  participate in transactions indirectly via user agent. This allows observing failed transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing, and also allows for users to 'search' for preferred outcomes in case of non-determinism.

Analogous to `sys.refl.http` a runtime might also define `sys.refl.gui` to provide generic GUI access to logs and debug views.

But I think it would be better to develop those transaction loop optimizations before implementing the GUI framework. There's a lot of feature interaction to consider with incremental computing and non-deterministic choice.

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

## Background Eval

For heuristically 'safe' operations, such as reading a file or HTTP GET, or manually triggering background processing, it is convenient to have an escape hatch from the transaction system. Background eval can serve this role.

* `sys.refl.bgeval(MethodName, Args)` - evaluate `MethodName(Args)` in a background transaction logically prior to the calling transaction, commit, then continue in the caller with the returned value. If the caller is aborted due to read-write interference, so is any incomplete background transaction. Args must be plain old data, and the return value cannot be ephemeral. MethodName should reference the local namespace.

MethodName is usually ephemeral due to potential for live coding, it's safe for bgeval because it's transferring backwards to a point in time where MethodName is known to have the same meaning. There is risk of thrashing if the background transaction conflicts with the caller, but this is easily detected and debugged. Background eval is compatible with transaction loop optimizations.

## Sagas, or Long Running Transactions? Defer.

It is feasible to request a runtime to create long-running transactions, called sagas, that we manually extend over multiple steps then eventually commit or abort. Between steps, a saga may be aborted implicitly due to read-write conflicts with other transactions or live code switch. 

The main use case for sagas is to implement transactional operations above custom network protocols. The main challenge is integrating with transaction loop optimizations. Ideally, this API should be adequate to reimplement RPC or custom HTTP headers for multi-request transactions. It might be best to develop this API later, after we have RPC as a working example.

## Module System Access? Defer.

I would like a good API for browsing and querying the module system, preferably with support for localization (perhaps relative to another module). This might be expressed as browsing the configured graph of modules, loading modules abstractly, starting with public modules.

## Foreign Function Interface

A foreign function interface (FFI) is convenient for integration with existing systems. Of greatest interest is a C language FFI. But FFI isn't trivial due to differences in data models, error handling, memory management, type safety, and so on. For example, it is infeasible to directly integrate C library functions with transactions, but a transaction could schedule a C function to run in a background thread.

A viable API:

* `sys.ffi(StaticArg, DynamicArgs)` - call a foreign function specified in the configuration. 

In this case, the configuration reads StaticArgs and returns a specification recognized by the runtime. This might represent a library file location, a specific method name, and integration hints such as queuing the request and returning a future. This may generalize to a script or inline assembly. 

This API provides a high degree of flexibility for how much specification is represented within the application versus the configuration, and for development of ad-hoc conventions. But essentially we can view FFI as configured runtime extension.

*Note:* FFI should be used primarily for system integration, but it can also serve a role in system performance until suitable accelerators are developed.

## Console IO

Access to standard input and output streams is provided through `sys.tty.*`.

* `sys.tty.read(Count)` - return list of Count bytes from standard input; diverge if data insufficient.
* `sys.tty.read(Binary)` - remove Binary from input buffer or fail; diverge if more input is required.
* `sys.tty.write(Binary)` - write given binary to standard output, buffered until commit. Returns unit.

Divergence on insufficient data ensures this API does not observe race conditions. For some console applications, we might disable line buffering or input echo. Those features should be configurable. 

*Note:* I'd recommend the 'http' interface over console IO in most cases where users need a simple interface. However, console IO is the best choice in some cases for integration reasons.

## Filesystem

Instead of an implicit global filesystem, our configuration will specify multiple named roots. File access is always relative to a configured root. This provides a simple basis to sandbox applications. It's also convenient in context of mirroring that we don't start with an assumption of a global filesystem.

The set of named roots is application and mirror specific. For example, to operate on "AppData", we'll first evaluate the configuration for each mirror whether it has paths for "AppData". This can be cached. In the simplest cases, at most one mirror reports a path, likely the origin mirror. Otherwise, behavior may depend on the operation and mirroring flags. We could read from one location non-deterministically, or write to multiple locations concurrently, or simply abort due to ambiguity.

Also, although we can support conventional streaming APIs on files, support for patch-based APIs would better support multiple users in the absence of a transactional filesystem. It's something to consider. Of course, a transactional filesystem would also be very nice, but would require significant redesigns.

*Note:* An open file handle should be an abstract, linear, and scoped to the runtime. There may be a period between 'open' and commit where we buffer some writes even before we receive feedback on potential permissions issues. But by buffering writes, we could save ourselves a transaction in most cases.

## Network

It is feasible to support something similar to the sockets API, perhaps initially limited to TCP and UDP. However, network interfaces (and possibly port bindings) should be restricted and abstracted by the configuration. 

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

