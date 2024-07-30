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

* *Auto-tune.* Even without transactions, a system control loop can read calibration parameters and heuristically adjust them based on feedback. However, transactions make this pattern much safer, allowing aggressive experimentation without committing to anything. This creates new opportunities for adaptive software.

* *Loop Fusion.* The system is always free to merge smaller transactions into a larger transaction. Potential motivations include debugging of multi-step interactions, validation of live coding or auto-tuning across multiple steps, and loop fusion optimizations.

Unfortunately, we need a mature optimizer and runtime system for these opportunities become acceptably efficient. This is an unavoidable hurdle for transaction loops. Meanwhile, the direct implementation is usable only for single-threaded event dispatch and state machines.

## Application Life Cycle

For a transaction loop application, the first effectful operation is `start()`. This will be retried indefinitely until it commits successfully or the application is killed externally. If undefined, 'start' implicitly succeeds.

After a successful start, the runtime will begin evaluating `step()` repeatedly in separate transactions. The runtime may also call methods to handle RPC and HTTP requests, GUI connections, and so on based on the interfaces implemented by the application.

The application may voluntarily halt via `sys.halt()`, marking the final transaction. To support graceful shutdown, a `stop()` method will be called in case of OS events such as SIGTERM on Linux or WM_CLOSE in Windows, but this won't necessarily halt the application. 

An applications may voluntarily restart via `sys.restart()`. This should be consistent with halting the application then starting again in a new OS process. That is, the runtime is fully restarted: the runtime database is cleared, open files or network sockets are closed, etc.. Only persistent state bound to the external database is preserved.

### Live Coding Extensions

Upon noticing an update to a running application, the updated application is compiled then we evaluate `switch()` - the updated implementation thereof - repeatedly until it succeeds. If undefined, switch implicitly succeeds. In contrast to start, switch must assume there are open files and network connections, that the runtime database is already in use, etc..

Upon a successful switch, we'll begin using the new code's version of step, RPC, HTTP, and GUI interfaces, and so on. Until then, the runtime will continue to use the prior definition. If an application is edited many times, a runtime may directly switch to the latest version.

*Note:* Support for live coding is expensive and is subject to configuration. In general it could be disabled or configured to an external trigger (such as Linux SIGHUP or a named Windows event object).

## Application Settings and Configurations

In glas systems, configurations are generally centralized to [a ".gin" file](GlasInitLang.md) indicated by `GLAS_CONF`. In general, this configures the global module system, the runtime, and access to host resources. The latter includes override environment variables, which may be structured. 

Excepting the global module namespace, most configuration options may be application specific by depending on `settings.*` properties defined in the application namespace. Settings are subject to convention and de-facto standardization. 

Configurations will manage any relationship between applications and host resources. For example, filesystem access requires an extra parameter referring to a configured root, and sockets are bound to configured network interfaces, and even access to time may depend on a configured clock.

## Application Mirroring

I intend to model mirroring in terms of configuring a distributed runtime. Due to the unique properties of transaction loops, we can logically run the same transaction repeatedly on multiple nodes, then optimize based on locality. Configuration of mirroring would be application specific. See [Glas Mirrors](GlasMirror.md) for details. 

An essential property for mirroring is that all mirrors use the same effects API, with the same meaning regardless of where the transaction is run. This influences bindings to host resources such as network interfaces, filesystems, and clocks. However, it is feasible to support a distributed API in general based on implicit parameters representing a cursor for 'where' an effect is applied, or based on explicit parameters identifying resources

## System APIs

The `sys.*` namespace component is generally reserved for runtime-provided methods, and `%*` for the abstract assembly intermediate AST. 

## Stable Database Bindings

A subset of names with a specific prefix in the application namespace, perhaps `$*`, will be mapped to keys in a key-value database. This provides a simple basis for application state that is friendly for live coding, orthogonal persistence, and debug views. It also enables namespace-based control over access to the state effect. 

The front-end language should be aware of state and translate names appropriately by default. For example, when introducing hierarchical component 'foo' the language might add default translations `{ "" => "foo.", "$" => "$foo.", "$global." => "$global." }`, supporting local state under `$varname` and global state (shared between components) under `$global.varname`.  

Ultimately, the runtime is responsible for mapping names to database keys. This may be configurable and application specific, allowing different apps to automatically bind different regions of a database. Assumptions about state may be expressed as annotations, while specialized operations (e.g. for queues, bags, CRDTs) can improve performance in context of parallelism or mirroring.

### Lifespan and Scope

The database is persistent by default, but there may be regions with 'runtime' lifespan bound to an in-memory database, and ephemeral state with 'transaction' lifespan is also feasible. This might be indicated by naming convention (e.g. `$xy.tmp.zzy` or special char `$xy%zzy`). Runtime types, such as open file handles, may also be limited to the in-memory database. This constraint would prevent fully orthogonal persistence, but semi-transparent persistence is possible.

### Dynamically Indexed Collections? Low Priority.

It is feasible to express indexed collections of components in the namespace via something like `$foo[].bar` together with `$foo[@]` as an implicit parameter or ephemeral variable for indexing the collection. We will also want support for browsing indices in use. The details need work! 

## Remote Procedure Calls? Low Priority.

Most network protocols force asynchronous interactions, i.e. writes are buffered until commit, and responses are received in a future transaction. This is usable, but there are opportunity costs. 

Transaction-aware remote procedure calls (RPC) are a better fit, allowing synchronous interactions within a transaction. With some careful design of the transaction model, it is feasible to support transaction loop optimizations such as incremental computing, reactivity, even parallellism and search on non-deterministic choice.

I propose to model applications as publishing and subscribing to RPC 'objects' through a configured registry. Each object could have stable metadata that a composite registry filters and rewrites, effectively routing RPC objects to component registries. Compared to point-to-point connections, publish-subscribe improves reactivity and adaptivity of applications to their environment.

As I'd prefer to avoid relying on first-class objects or functions, an application might publish RPC objects by defining `rpc.(ObjectName).(Method*)`, and subscribe to RPC objects by declaring methods under `sys.rpc.(Interface).(Method*)`. In case there is more than one instance of a subscribed interface, we could implicitly 'fork' to select one for the remaining transaction, or we might extend RPC interfaces to support indexed collections.

For performance, it is feasible for RPC objects to distribute some code for local evaluation on the caller, and to also maintain some cached computations. Conversely, the caller can potentially pass a continuation object that can be processed remotely to reduce network traffic. Further, it is feasible to integrate RPC with a [content delivery network](https://en.wikipedia.org/wiki/Content_delivery_network) to distribute large values.

*Note:* The runtime will maintain its own TCP/UDP listener for RPC requests. The same TCP listener would also support the 'http' interface. This binding should be configurable and application specific. 

## Defunctionalized Procedures and Processes? Low Priority.

To support 'direct style' in programming of network connections, filesystem access, and FFI it is convenient if a front-end syntax knows to compile some sequences of operations into state machines that yield and commit to await a response in a future transaction. Intriguingly, we might also introduce `atomic {}` sections to force multiple steps to complete within a single transaction.

A relevant concern is stability of these procedures in context of live coding. To simplify manual stabilization, we might only yield at explicit, labeled 'yield' points. This also encourages coarse-grained steps, which mitigates performance overhead.

I feel this feature would be useful as a bridge between glas and host systems.

## Implicit Parameters and Algebraic Effects

Implicit parameters can be modeled as a special case of algebraic effects. I intend to tie implicit parameters and algebraic effects to the namespace. This supports namespace-based access control, prevents name collisions, and supports static analysis and reasoning. 

In my vision for glas systems, algebraic effects and staged computing are favored over first-class functions or objects. This restricts the more dynamic design patterns around higher order programming, but support for maps and folds aren't a problem. This restriction has benefits for both reasoning and performance, e.g. no need to consider variable capture, and a compiler can allocate functions on the data stack.

*Note:* It should be possible to model `sys.*` methods as wrappers around algebraic effects, where the abstracted algebraic effect performs the actual interaction with the runtime. This might constrain some API designs.

## HTTP Interface

Implementing the HTTP interface `http : Request -> Response` allows users to interact with an application through a browser or other external tools in a flexible way. The Request and Response types are binaries, including HTTP headers and body, but may be accelerated to minimize redundant processing. 

Even if the application ignores this opportunity, the `"/sys"` path is reserved by the runtime and implicitly routed to `sys.refl.http` (with configurable authorization requirements). This is intended to simplify tooling, e.g. access to logs, debugger integration, and administrative controls. Defining 'http' interfaces on hierarchical application components and RPC objects might be useful for debug views or projectional editors.

Initially, every HTTP request is handled in a separate transaction. If this transaction aborts, it is implicitly retried until success or timeout, providing a simple basis for long polling. Eventually, we might develop custom HTTP headers to support multi-request transactions and incremental computing.

*Misc:* Authorization and authentication are configurable and may be application specific. The HTTP port usually overloads the RPC port. The runtime is responsible for HTTP pipelining and similar meta-level HTTP features. There are no plans to support WebSockets at this time.

## Graphical User Interface? Defer.

My vision for [glas GUI](GlasGUI.md) is that users indirectly participate in transactions through reflection on a user agent. This allows observing failed transactions and tweaking agent responses until the user is satisfied and ready to commit. This aligns nicely with live coding and projectional editing. 

Analogous to `sys.refl.http` a runtime might also define `sys.refl.gui` to provide generic GUI access to logs and debug views.

But I think it would be better to develop those transaction loop optimizations before implementing the GUI framework. There's a lot of feature interaction to consider with incremental computing and non-deterministic choice.

## Non-Deterministic Choice for Concurrency and Search

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - blindly but fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. If `sys.fork(N)` is part of the 'stable' prefix for incremental computing, it effectively selects a thread. We can optimize to evaluate multiple stable threads in parallel, and even commit them in parallel.  

Meanwhile, even where 'fork' is not part of the stable prefix, it can still be useful to model search. We would implicitly retry with different responses from 'fork', seeking one that leads to a committing transaction. 

However, fair choice isn't random. Scheduling may be predictable for performance reasons. Never use `sys.fork()` to roll dice. 

## Random Data

Random data needs attention for stability and performance in context of incremental computing, distribution, and orthogonal persistence. I've decided against a global CPRNG in this role because dealing with state and read-write conflicts becomes awkward. A viable alternative is to 'sample' a stable, cryptographically random field. 

* `sys.random(Index, N)` - on the first call for any pair of arguments, returns a cryptographically unpredictable number in range `[0,N)` with a uniform distribution. Subsequent calls with the same arguments will return the same number within a runtime.

To ensure robust partitioning, Index might be a database name. This API can be implemented statelessly via HMAC, with configuration of initial entropy. Users may always roll their own stateful PRNGs or CPRNGs.

## Time

Transactions may observe time and abort if time isn't right. In context of a transaction loop, this can be leveraged to model timeouts or waiting on the clock. To support incremental computing, we add a variant API:

* `sys.time.now()` - Returns a TimeStamp representing a best estimate of current time as a rational number of seconds since Jan 1, 1601 UTC. This corresponds to Windows NT time, but doesn't specify precision. Multiple queries to the clock within a transaction must return the same value. 
* `sys.time.after(TimeStamp)` - Observes `sys.time.now() >= TimeStamp`. This is more convenient for incremental computing and reactivity, allowing the runtime to schedule waiting on the clock.
* `sys.time.clock` - (potential) implicit clock variable, a reference to the configuration 

This API is useful for adding timestamps to received messages or waiting on the clock, but useless for profiling. *Profiling* will instead be supported via annotations.

## Environment Variables

The configuration controls an application's view of host resources, including OS environment variables. However, this isn't all bad: the configuration can easily support an environment of structured data instead of flat strings, and conveniently supports application-specific environments.

A minimum viable API:

* `sys.env.get(Query)` - asks the configuration to evaluate a function in the configured environment with a given Query parameter. This will either fail or return some data.

The configuration would have access to OS environment variables and application settings. The application cannot directly access OS environment variables, but the configuration may choose to forward them under some conventions. It isn't possible to set environment variables, but the application namespace can easily control the environment exposed to a hierarchical subprogram.

A weakness of this API is that we cannot directly browse environment variables. This might be mitigated by developing some conventions for browsing, e.g. querying an index.

## Command Line Arguments

I propose to present arguments together with the environment. Unlike the OS environment variables, the configuration does not observe or intercept these arguments.

* `sys.env.args` - a list of strings, the arguments to the application. 

One area where I break tradition is that the arguments don't include executable names. Including executable names would be awkward and break abstractions in context of staged applications. However, applications may have access to this information via reflection APIs.

## Data Representation? Defer.

We can manually serialize data to and from binaries. However, in some cases we might prefer to preserve underlying data representations used by the runtime. This potentially allows for greater performance, but it requires reflection methods to observe runtime representations or interact with content addressed storage. Viable sketch:

* Interaction with content-addressed storage. This might be organized into 'storage sessions', where a session can serve as a local GC root for content-addressed data.
* Convert glas data to and from [glob](GlasObject.md) binary. This requires some interaction with content-addressed storage, e.g. taking a 'storage session' parameter.

The details need some work, but I think this is sufficient for many use-cases. It might be convenient to introduce a few additional methods for peeking at representation details without full serialization.

## Background Eval

It is inconvenient to require multiple transactions for heuristically 'safe' operations, such as reading a file or HTTP GET. Also, if a transaction is waiting on background operations, it might be convenient to force resolution. In these cases, background eval can be a useful escape hatch from the transaction system. 

* `sys.refl.bgeval(MethodName, Args)` - evaluate `MethodName(Args)` in a background transaction logically prior to the calling transaction, commit, then continue in the caller with the returned value. If the caller is aborted due to read-write interference, so is any incomplete background transaction. The argument and return value types must be plain old data. MethodName should reference the local namespace.

Transferring method names between transactions usually interferes with live coding, but this is a special case because we're sending the name backwards in time (logically) and because we'd also abort the background operation upon 'switch'.

*Note:* Background eval is compatible with all transaction loop optimizations - incremental computing, reactivity, concurrency based on non-deterministic choice, etc..

## Long Running Transactions

A reflection API could provide a method to initiate a transaction, returning a linear reference that may survive multiple transactions. Further operations could ask the runtime to perform some operations within the transaction, perhaps returning some feedback immediately. Eventually, we could try to commit or abort the transaction. 

Of course, the transaction might also abort due to to external interference, e.g. a read-write conflict. So we might also need methods to check the status of the remote transaction.


## Module System Access? Partial. Defer. 

Users might want APIs for browsing and querying the module system, listing which global modules contributed to the current application, access to automated test results, loading modules, and so on.

We can immediately implement the `sys.load` API used by language modules. In this case, the implicit localization might be `{ "" => "distro." }`, i.e. assuming names come from the end user. A more complete API can deferred until we have a better understanding of use cases and what is easily implemented.

## Foreign Function Interface? Tentative.

A foreign function interface (FFI) is convenient for integration with existing systems. Of greatest interest is a C language FFI. But FFI can be non-trivial due to differences in data models, error handling, memory management, and so on. I haven't discovered any simple and effective means to reconcile a C FFI with hierarchical transactions or transaction loop optimizations (incremental computing, logical replication on fork, reactivity, etc.). 

The best solution I've found is to instead schedule non-transactional C operations to run between transactions. A viable API:

* `sys.ffi.cfunc(LibName, FunctionName, AdapterHint)` - returns abstract FnRef for a C function in a dynamically loaded library. The AdapterHint should minimally indicate argument and result types, but might further include calling conventions, parallelism options, cacheability or idempotence, and other features. Also, we won't necessarily support all C functions, but we should support common integer and binary array types.
* `sys.ffi.sched(FnRef, Args)` - schedules a call to a foreign function and returns an abstract OpRef. This will run shortly after the current transaction commits. For convenience, calls will usually run in a single thread in the same order they are scheduled unless configured otherwise via AdapterHint.
* `sys.ffi.result(OpRef)` - returns result of a completed operation, otherwise diverge. If called from the same transaction that scheduled the operation, divergence is guaranteed.

We can later introduce methods to observe detailed status of OpRef, or support limited scripts in place of FnRef. However, as a general rule, transaction loops should avoid scheduling long running scripts or operations because doing so interferes with live coding and orthogonal persistence.

*Note:* Where it's just a performance question, glas systems should favor *acceleration* over FFI if it's just for performance. Acceleration is a lot more friendly than FFI (safe, secure, portable, reproducible, scalable, etc.) but does not solve integration with the outside world.

## Configuration Variables? Tentative.

A runtime configuration might include ad-hoc variables. However, I'm not sure I want this to be a separate feature from environment variables. We could instead express configurations as applying a mixin to the environment, allowing ad-hoc extensions and overrides.


## Console IO

Access to standard input and output streams is provided through `sys.tty.*`.

* `sys.tty.read(Count)` - return list of Count bytes from standard input; diverge if data insufficient. (*Note:* the input buffer will be treated as empty during 'start'.)
* `sys.tty.write(Binary)` - write given binary to standard output, buffered until commit. Returns unit.

This API ensures that `sys.tty.*` does not itself introduce any non-determinism, e.g. due to race conditions on step. There is also no option to close TTY because doing so is awkward in context of live coding. Input echo and line buffering are subject to application-specific configuration, and perhaps also via `sys.refl.tty.*`. 

Console IO is suitable for some command line tools, but I believe building upon the 'http' interface would be superior for most applications. The 'standard error' stream is left to the runtime and may output some log messages or profiling data, contingent on configuration . 

## Filesystem

The filesystem structure is somewhat abstracted through the configuration. Relevantly, an application must specify a configured 'root' together with each file path. This allows for user home, application data, and actual OS filesystem roots. It also simplifies configuration of overlays or integration of logical filesystems. 



Proposed API:

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
  * **open:(name:FileName, for:Interaction)** - Open a file, returning a fresh FileRef. This operation returns immediately; the user can check 'status' in a future transaction to determine whether the file is opened successfully. However, you can begin writing immediately. Interactions:
    * *read* - read file as stream. Status is set to 'done' when last byte is available, even if it hasn't been read yet.
    * *write* - open file and write from beginning. Will delete content in existing file.
    * *append* - open file and write start writing at end. Will create a new file if needed.
    * *delete* - remove a file. Use status to observe potential error.
    * *move:NewFileName* - rename a file. Use status to observe error.
  * **close:FileRef** - Release the file reference.
  * **read:(from:FileRef, count:N, exact?)** - Tries to read N bytes, returning a list. May return fewer than N bytes if input buffer is low. Fails if 0 bytes would be read, or if 'exact' flag is included and fewer than N bytes would be read.
  * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. Fails if not opened for write or append. Use 'busy' status for heuristic pushback.
  * **status:FileRef** - Returns a record that may contain one or more flags and values describing the status of an open file.
    * *init* - the 'open' request has not yet been seen by OS.
    * *ready* - further interaction is possible, e.g. read buffer has data available, or you're free to write.
    * *busy* - has an active background task.
    * *done* - successful termination of interaction.
    * *error:Message* - reports an error, with some extra description.
  * **std:out** - returns FileRef for standard output
  * **std:in** - returns FileRef for standard input

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
  * **open:(name:DirName, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Returns a DirRef.
    * *list* - read a list of entries from the directory. Reaches Done state after all items are read.
    * *move:NewDirName* - rename or move a directory. Use status to observe error.
    * *delete:(recursive?)* - remove an empty directory, or flag for recursive deletion.
  * **close:DirRef** - release the directory reference.
  * **read:DirRef** - read a file system entry, or fail if input buffer is empty. This is a record with ad-hoc fields including at least 'type' and 'name'. Some potential fields:
    * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
    * *name:Path* (always) - a full filename or directory name, usually a string
    * *mtime:TimeStamp* (optional) - modify time 
    * *ctime:TimeStamp* (optional) - creation time 
    * *size:Nat* (optional) - number of bytes
  * **status:DirRef** ~ same as file status
  * **cwd** - return current working directory. Non-rooted file references are relative to this.
  * **sep** - return preferred directory separator substring for current OS, usually "/" or "\".

It is feasible to extend directory operations with option to 'watch' a directory for updates.


## Network APIs

I'm uncertain whether I should try to provide TCP/UDP APIs directly, provide something closer to a sockets-based API (with bind, connect, etc.), or perhaps just provide XMLHttpRequest. 


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

Aside from automated testing described in [the design doc](GlasDesign.md), we can add assertions to programs as annotations. Similar to logging and profiling, tests could be associated with static channels to support configuration (e.g. enable, disable, random testing), and it can be useful to support continuous testing over the course of an operation.

        # viable syntax
        test(chan, property, message) { operation }

In this case we might evaluate a property as pass/fail, and then evaluate the message expression only when the property fails. This might also be interpreted as a form of logging, except the default configuration would be to abort the current transaction on a failed test.

Other than inline testing, it might be feasible to express ad-hoc assumptions about final conditions just before commit. 

### Tracing? Tentative.

In some cases, it is useful to track dataflows through a system including across remote procedure calls and transactions. This can be partially supported by including provenance annotations within data representations. The [glas object](GlasObject.md) representation supports such annotations, for example. We will need something more to efficiently maintain provenance when processing data. I suspect this will need an ad-hoc solution for now.

### Mapping

For live coding, projectional editing, debugging, etc. we often want to map program behavior back to source texts. In context of staged metaprogramming, this might best be modeled as *Tracing* source texts all the way to compiled outputs. This implies provenance annotations are represented at the data layer, not the program layer. 

For performance, a late stage compiler might extract and preprocess annotations in order to more efficiently maintain provenance metadata (e.g. as if tracing an interpreter). But this should be understood as an optimization.

### Quotas

To simplify reasoning about performance, it's often useful to choke an application to use fewer resources than are available. This might be expressed as quotas, e.g. limiting how much CPU is used in a given task. It should be feasible to support quotas on both CPU use and memory use. Quotas can be managed in two layers: External quotas would be expressed in the configuration, perhaps adjusted by application settings. Internal quotas would be expressed as annotations within the program.

## Rejected Features

It is feasible to introduce some `sys.disable()` and `sys.enable()` operations, perhaps parameterized for specific events such as 'step' and 'rpc'. However, I'm not convinced this is a good idea. Runtime state is less visible to users than application state, and more likely to be forgotten. It's also imprecise. In comparison, users can easily add some application state and add some explicit conditions to specific 'step' threads or RPC calls.

It is feasible to introduce periodic operations that evaluate at some configurable frequency, perhaps a `status()` health check. However, I think it better to let users manage this more explicitly, using `sys.time` to control the frequency of some operations and using HTTP requests to get information about health. 

