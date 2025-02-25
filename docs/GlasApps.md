# Glas Applications

## The Transaction Loop

A *transaction loop* is a very simple idea - a repeating transaction, atomic and isolated. A direct implementation is nothing special; at best, it makes a convenient event dispatch loop, friendly for live coding and orthogonal persistence. However, consider a few optimizations we can apply: 

* *incremental computing* - We don't always need to repeat the entire transaction from the start. Instead, roll-back and recompute based on changed inputs. If repeating the transaction is obviously unproductive, e.g. it aborts, then we don't need to repeat at all until conditions change. If productive, we still get a smaller, tighter repeating transaction that can be optimized with partial evaluation of the stable inputs.
* *clone on non-deterministic choice* - If the computation is stable up to the choice, cloning is subject to incremental computing. This provides a stateless basis for concurrency, a set of threads reactive to observed conditions. If the computation is unstable, then at most one will commit while the other aborts (due to read-write conflict), representing a race condition or search. Both possibilities are useful.
* *distributed runtimes* - A runtime can mirror a repeating transaction across multiple nodes then heuristically abort transactions best started on another node due to locality of resources. Under incremental computing and cloning on non-deterministic choice, this results in different transactions running on different nodes. In case of network partitioning, transactions in each partition may run independently insofar as we can guarantee serializability (e.g. by tracking 'ownership' of variables). An application can be architected such that *most* transactions fully evaluate on a single node and communicate asynchronously through runtime-supported queues, bags, or CRDTs. 

If implemented, the transaction loop covers many more use-cases - reactive systems, concurrency, distributed systems with graceful degradation and resilience. Applications must still be designed to fully leverage these features, this is simplified by a comprehensible core: users can easily understand or debug each transaction in isolation. Also, even with these optimizations, the transaction loop remains friendly for live coding and orthogonal persistence. 

Further, there are several lesser optimizations we might apply:

* *congestion control* - A runtime can heuristically favor repeating transactions that fill empty queues or empty full queues.
* *conflict avoidance* - A runtime can arrange for transactions that frequently conflict to evaluate in different time slots.
* *soft real-time* - A repeating transaction can 'wait' for a point in time by observing a clock then diverging. A runtime can precompute the transaction slightly ahead of time and have it ready to commit.
* *loop fusion* - A runtime can identify repeating transactions that compose nicely and create larger transactions, allowing for additional optimizations. 

These optimizations don't open new opportunities, but they can simplify life for the programmer.

*Note:* A conventional process or procedure can be modeled in terms of a repeating transaction that always writes state about the next step. Of course, there is a performance hit compared to directly running concurrent procedures.

## Transaction Loop Application Model

A transaction loop application might define several transactional methods:

* 'start' - Set initial state, perform initial checks. Retried until it commits once.
* 'step' - After a successful start, repeatedly run 'step' until it voluntarily halt or killed externally.
* 'http' - Handle HTTP requests between steps. Our initial basis for GUI and events.
* 'rpc' - Transactional inter-process communications. Multiple calls in one transaction. Callback via algebraic effects.
* 'gui' - Like an immediate-mode GUI. Reflective - renders without commit. See [Glas GUI](GlasGUI.md).
* 'switch' - First transaction in new code after live update. Old code runs until successful switch.
* 'settings' - (pure) influences runtime configuration when computing application-specific runtime options.

The transactions will interact with the runtime - and through it the underlying systems - with an algebraic effects API. Unfortunately, most external systems - filesystem, network, FFI, etc. - are not transactional. We resolve this by buffering operations to run between transactions. But there are a few exceptions: application state, remote procedure calls, and a convenient escape hatch for safe, cacheable operations like HTTP GET.

## Application State

The runtime will support a few useful state models: cells, queues, bags, key-value stores, and so on. Applications can construct and interact with these objects. The runtime provides a root key-value store to get started. To simplify garbage collection, all state references are runtime scoped. 

A viable API:

* `sys.db.root : KVS` - stable reference to application state.
* `sys.db.kvs.*` - a key-value store. Keys are arbitrary data.
  * `new() : KVS` - construct a new KVS, initially empty. 
  * `get(KVS, Key) : Data` - read data at key. Diverge if Key is undefined
  * `set(KVS, Key, Data)` - write data at key.
  * `del(KVS, Key)` - set Key to undefined state. 
  * `swap(KVS, Key, Data) : Data` - combines get and set.
  * `has(KVS, Key) : Bool` - test whether a key is defined. 
* `sys.db.key.new() : Key` - returns an abstract key containing a fresh reference. Convenient for constructing unique KVS keys, and useless for any other purpose. Serves a similar role as 'gensym' in some languages. 
* `sys.db.cell.*` - minimalist state, essentially a KVS with exactly one key.
  * `new(Data) : Cell` - construct a new cell with initial value.
  * `get(Cell) : Data` - access current value. 
  * `set(Cell, Data)` - update value
  * `swap(Cell, Data) : Data` - combines get and set (support for linear types)
* `sys.db.queue.*` - a cell containing a list with controlled access. Supports multiple concurrent write transactions and a single reader, i.e. each transaction buffers writes locally, then a serialization is determined upon commit.
  * `new() : Queue` - construct a new queue, initially empty
  * `read(Queue, N) : List of Data` - reader slices list of exactly N items from head of queue. Will diverge if fewer items available.
  * `unread(Queue, List of Data)` - reader prepends list to head of queue for a future read. Could use queue as a deque or stack.
  * `write(Queue, List of Data)` - writer appends list to tail of queue, primary update operation
* `sys.db.bag.*` - an unordered queue, where reads select items non-deterministically. Ideally, we can optimize reads by recognizing the subsequent code that filters the result; this may require some guidance by annotations.
  * `new() : Bag` - construct a new bag, initially empty
  * `read(Bag) : Data` - read and remove data, non-deterministic choice. Diverges if bag is empty.
  * `peek(Bag) : Data` - read from bag without removing data. This allows for stable incremental computing.
  * `write(Bag, Data)` - add data to bag.
* `sys.refl.db.*` - additional methods for reflection on the runtime database. These APIs can observe runtime details, such as garbage collection, migration, or version tracking. We could add methods to guide migration or force ownership transfer and such.
  * `ref.type(Cell|KVS|etc...) : cell | kvs | etc.` - dynamically discriminate state references.
  * `kvs.keys(KVS) : List of Key` - list defined keys. Indirectly observes garbage collection.
  * `kvs.writer(KVS, Key) : ...` - describe write authority, e.g. node reference or consensus algorithm
  * `cell.writer(Cell) : ...`
  * ... plenty of options here, may be runtime specific

In KVS, references within keys are treated as [weak refs](https://en.wikipedia.org/wiki/Weak_reference). If a key cannot be constructed after GC of a reference, the associated data is unreachable and the key is implicitly deleted. If users intend to browse keys, they can either maintain a directory or use reflection APIs. These weak references are useful for maintaining and accessing state about associations between things.

### Distributed State

The state models mentioned above can be adapted for a distributed runtime:

* *KVS* - In a distributed runtime, write authority for individual keys can be spread across nodes, but nodes can maintain a local read-only cache of keys.  
* *Cell* - Same as for a kvs key.
* *Queue* - The reader and writer may be on separate nodes, with the writer buffering writes. Only a single writer node is supported unless to simplify interaction with 'sys.time' and timestamps.
* *Bag* - In a distributed runtime, each node may read and write its local 'slice' of the bag. When the nodes are communicating, the runtime is free to shuffle items between nodes, and also to cache peek-only views data. Ideally, data is routed heuristically based on the filtering of results performed at each node.

Write authority for keys in the root database should not be owned by any single node. Instead, write authority is controlled by a consensus algorithm. This supports robust recovery in case of permanent node loss. To mitigate performance, the data could be a Cell that is owned by a single node. Exceptions: no need for consensus during 'start', and a key containing new references can be committed to any KVS without risk of conflict.

When we get serious about distributed runtimes, we should also investigate [conflict-free replicated datatypes (CRDTs)](https://en.wikipedia.org/wiki/Conflict-free_replicated_data_type) for partitioning tolerance. Every node can locally read and write its own replica. When two nodes interact, they must synchronize their replicas (up to the moment of interaction) to preserve serializability of transactions. A few CRDTs to support lists, trees, and graphs, and trees could be very convenient.

## HTTP Interface

The runtime should recognize the 'http' interface and support requests over the same channels we use for remote procedure calls and debugging. By default, `"/sys/*"` will be intercepted for external debugger integration.

        http : Request -> [sys] Response

The Request and Response types are binaries. However, these will often be *accelerated* binaries, i.e. with a structured representation under-the-hood that can be efficiently queried and manipulated through built-in functions. The application receives a complete request from the runtime, and must return a complete respon, no chunks. There is no support for WebSockets or SSE.

Each 'http' request is handled in a separate transaction. If this transaction aborts voluntarily, it is logically retried until it successfully produces a response or times out, providing a simple basis for long polling. A 303 See Other response is suitable in cases where multiple transactions are required to compute a response. Runtimes may eventually support multiple requests within one transaction via dedicated HTTP headers, but that will wait for the future.

Ideally, authorization and authentication are separated from the application. We could instead model them as application-specific runtime configuration, perhaps integrating with SSO.

*Aside:* It is feasible to configure a runtime to automatically launch the browser and attach to the application.

## Remote Procedure Calls

If an application implements 'rpc' it may receive remote procedure calls (RPC).

        rpc : (MethodRef, UserArg) -> [rpc.cb, sys] Result

The UserArg and Result values are exchanged with the caller. Optionally, limited interaction may be supported via algebraic effects, an 'rpc.cb' callback. The MethodRef is instead a runtime parameter, relating to how RPC is registered and published. The runtime will map between local use of MethodRef and external use of GUIDs or URLs.

RPC must be configured. The simplest solution is to declare a static API via application settings. Alternatively, settings could specify a MethodRef to fetch a dynamic API. Either way, I propose to organize RPC methods into 'objects' that are published to different registries based on trust and roles. 

A prospective caller will query for RPC objects matching an interface and metadata.




        TBD - caller API




To enhance performance, I hope to support annotation-guided code distribution. The 'rpc' method can be partially evaluated based on MethodRef, then have some code extracted for evaluation at the caller. A caller can similarly forward part of the callback code and continuation. These optimizations would mitigate performance pressures, supporting simplified remote APIs.


## Shared State

It is useful to understand shared state in terms of remote procedure calls to another application that maintains state on behalf of its clients. The runtime could provide a built-in implementation, but we'll still treat it as remote. The API should support transactions, structured data, and content-addressed storage. Compared to application state, shared state requires some design work around garbage collection, security, accounting, and open extension.






## Graphical User Interface? Defer.

My vision for [GUI](GlasGUI.md) involves users participating in transactions indirectly via reflection on a user agent. There are many interesting opportunities with this perspective. However, implementing a new GUI framework is a non-trivial task that should be done well or not at all. Thus, I'll defer support until I'm able to dedicate sufficient effort. Use HTTP or FFI in the meanwhile.

## Non-Deterministic Choice

In context of a transaction loop, fair non-deterministic choice serves as a foundation for task-based concurrency. Proposed API:

* `sys.fork(N)` - fairly chooses and returns an integer in the range 0..(N-1). Diverges if N is not a positive integer.
* `(%fork Op1 Op2 ...)` - (tentative) AST primitive for non-deterministic choice, convenient for static analysis.

Fair choice means that, given sufficient opportunities, we'll eventually try all of them. However, this doesn't imply *random* or *uniform* choice! A scheduler may compute forks in a very predictable pattern, some more frequently than others.

*Note:* Without optimizations for incremental computing and cloning on non-deterministic choice, fork is not very useable. Better off modeling a single-threaded event loop. 

## Random Data

A stateful random number generator is awkward in context of concurrency, distribution, and incremental computing. However, we can easily provide access to a stable, cryptographically random field.

* `sys.random(Seed, N) : Binary` - (pure) return a list of N cryptographically random bytes, uniformly distributed. Unique function per runtime instance.

An implementation might involve a secure hash of `[Seed, N, Secret]`, where Secret is obtained from `"/dev/random"` or a configurable source when the application starts. In a distributed runtime, all nodes share the secret. To partition a random field within an application, an application can intercept `sys.random` to wrap Seed within a subprogram. The Seed may be structured, but it should be plain old data.

## Background Eval

In some scenarios, we can reasonably assume operations are 'safe' such as HTTP GET, triggering a lazy computation, or writing some metadata only for reflection-like purposes. In these cases, we might want an escape hatch from the transaction system, i.e. such that we can trigger the computation, await the result, and pretend this result is already present. 

A proposed mechanism is background eval:

* `sys.refl.bgeval(StaticMethodName, UserArg) : Result` - Evaluate `StaticMethodName(UserArg)` in a separate transaction. The caller waits for this to commit then continues with the returned Result. 

Intriguingly, stable bgeval integrates with incremental computing, and non-deterministic bgeval can implicitly fork the caller for each Result. We can apply transaction-loop optimizations. We can also abort bgeval together with the caller, in case of read-write conflict or live coding.

*Caveats:* Computation may 'thrash' if bgeval repeatedly conflicts with the caller. But this is easy to detect and debug. The new transaction receives the original 'sys.\*' effects API, which may constitute a privilege escalation. But we should restrict untrusted code from reflection APIs in general.

## Time

A transaction can query a clock. A repeating transaction can wait on that clock, i.e. by aborting the transaction until the time is right. However, repeatedly querying the clock and aborting is extremely inefficient, so we'll want to optimize this pattern.

* `sys.time.now()` - Returns a TimeStamp for estimated time of commit. By default, this timestamp is a rational number of seconds since Jan 1, 1601 UTC, i.e. Windows NT epoch with flexible precision. Multiple queries to the same clock within a transaction will return the same value.
* `sys.time.await(TimeStamp)` - Diverge unless `sys.time.now() >= TimeStamp`. A runtime can easily optimize this to wait for the specified time. The runtime can also precompute the transaction slightly ahead of time and hold it ready to commit, a viable basis for soft real-time systems.

In context of a distributed runtime and network partitioning, each node maintains a local estimate of the runtime clock. When the nodes interact, we don't need to synchronize clocks, but we must track the maximum observed timestamp (received from 'now' or passed to 'await') that might have contributed to an interaction to ensure we observe a monotonic distributed clock. Fixing clock drift and aligning with true time is best left to NTP or PTP.

*Note:* If attempting to record how long a computation takes, use profiling annotations!

## Arguments and Environment Variables

A runtime can easily provide access to OS environment variables and command-line arguments.

* `sys.env.list : List of Text` - return the defined environment variables
* `sys.env.get(Text) : Text` - return value for an OS environment variable
* `sys.env.args : List of Text` - return the command-line arguments

The application cannot mutate this environment, though it can override access to 'sys.env.\*' within scope of a subprogram.

*Note:* Applications integrate the configuration environment through the namespace layer, '%env.\*'.

## Console IO

With users launching glas applications from a command-line interface, it is convenient to support user interaction directly through the same interface. The basics are just reading and writing some text, but it is possible to disable line buffering and input echo then implement sophisticated applications via [ANSI escape codes](https://en.wikipedia.org/wiki/ANSI_escape_code) or extended protocols.

A viable API:

* `sys.tty.write(Binary)` - write to standard output, buffered until commit. 
* `sys.tty.read(N) : Binary` - read from standard input. Diverges if not enough data.
* `sys.tty.unread(Binary)` - add Binary to head of input buffer for future reads.
* `sys.tty.ctrl(Hint)` - ad hoc control, extensible but mostly for line buffering and echo

The control hint is runtime specific, perhaps something like `(icanon:on, ...)`. I expect to use standard error for runtime use - compile-time warnings, logging, etc..

*Note:* An application or adapter could redirect or mirror `sys.tty.*`, perhaps to 'http' and [xterm.js](https://xtermjs.org/).

## Foreign Function Interface (FFI)

The only FFI of relevance for system integration is the C FFI. In context of transactions, our best option is to commit to call C functions between transactions. I propose an API that involves streaming commands to FFI threads. Operations can be pipelined: a transaction can commit a sequence of commands to perform, wiring inputs to outputs without further intervention. To simplify memory safety and crash recovery, FFI threads may run in an attached process.

A viable API:

* `sys.ffi.*`
  * `new(Hint) : FFI` - Return a runtime-scoped reference to a new FFI thread. Hint guides integration, such as whether to start a new process or attach to a named process, or where to run in a distributed runtime. 
  * `run(FFI, Cmd)` - Enqueue a command in the FFI thread, drops any result (including status feedback).
  * `eval(FFI, Cmd, Var)` - Enqueue a command in the FFI thread, record future return value into local Var.
  * `fork(FFI) : FFI` - Returns a clone of the FFI thread. This copies the local vars, including pending results, then enqueues a command to copy the thread-local environment and construct a new OS thread.
  * `status(FFI) : FFIStatus` - current status of FFI:
    * *ready* - the FFI thread is halted in a good state, can receive more requests.
    * *busy* - FFI thread is working in the background, or perhaps stuck on a mutex.
    * *error:(text:Message, ...)* - the FFI thread halted in a bad state, some hint for cause.
  * `var.read(FFI, Var) : Data` - Receive result from a prior command. Will diverge if not available.
  * `var.drop(FFI, Var)` - Remove a current or pending result from this FFI. Memory management.
  * `var.list(FFI) : List of Var` - Browse your local environment!
  * `var.status(FFI, Var) : VarStatus` - A status per variable:
    * *undefined* - variable was either dropped or never defined
    * *uncommitted* - the command to set this variable has not been committed 
    * *pending* - operation is queued, running, perhaps waiting on a mutex, etc.. 
    * *available* - data is ready, can ask for it immediately
    * *failed* - command failed or could not be started due to prior failure. See FFI status!
  * `cmd.load.lib(SharedObject, Symbols) : Cmd` - load ".so" or ".dll" into environment.
    * *Symbols* - a list of names to export. Eventually, I might also want aliases.
  * `cmd.load.csrc(Text, Symbols) : Cmd` - compile C functions in memory for use in future commands.
  * `cmd.load.chdr(Text, IncludeName) : Cmd` - for use in `#include <IncludeName>` directives.
  * `cmd.invoke(Symbol) : Cmd` - run previously loaded command; assumes `void (*)()` interface.
  * `cmd.cscript(Text, Symbol) : Cmd` - compile C code, invoke Symbol, then free the generated code. Users should be careful around the lifespan of function pointers to this code, e.g. for threads or callbacks.
  * `cmd.env.*` - commands on the FFI's thread-local envronment. Commands are buffered and pipelined, so it isn't a problem to send a large number of gets and sets followed by running more commands.
     * `push(Data, Type) : Cmd` - add to environment's data stack
     * `pop() : Cmd` - remove and return from environment's data stack
     * `set(Name) : Cmd` - pop from data stack into named register
     * `get(Name) : Cmd` - copy from named register onto data stack
     * `peek(N)` - returns copy of top N items from data stack if N>0, or all items if N is 0.
     * `read(List of Name) : Cmd` - returns copy of listed registers, or all registers if list is empty.

Only `void(*)()` functions can be invoked. However, these operations will receive access to a thread-local environment through a simple API with methods such as `env_push_int(42)`. This environment can be used to receive arguments, return results, or pipeline simple data (including pointers) from one command to another. This environment is copied when the FFI is forked, but the heap and library globals are shared.

Instead of awkward FFI adapter libraries, we'll leverage [Tiny C Compiler (TCC)](https://bellard.org/tcc/) to let users define adapters and utility code inline. Reusable utilities or commands can be written using 'cmd.load.csrc', while single-use commands can be expressed via 'cmd.cscript'. The original TCC is a bit awkward for this, but [a recent version](https://github.com/frida/tinycc/tree/main) simplifies things with callbacks to redirect `#include` directives and resolve symbols. With 'cmd.load.chdr' we can conveniently declare our APIs for use in csrc or cscript. 

I imagine a C script might have a form similar to:

        #include <myapi>
        int utility_fn(...) { ... }
        void do() {
            int x = env_pop_int();
            ...  
            env_push_binary_bycopy(&buffer, bufflen);
        }

This API incurs moderate overhead for transactions, serialization, and JIT compilation. This should be negligible for long-running or infrequent operations, but it does impact fast operations at high-frequencies. In those cases, users might need to push a little bit more application logic into the C source, running an indefinite loop or starting some background threads.

A remaining question: what should we do with stdin, stdout, and stderr of the FFI process?

*Note:* The Hint and SharedObject types may be runtime specific. We may be relying on configuration-provided adapters for portability!

## Content-Addressed Storage and Glas Object (Low Priority!)

The runtime uses content-addressed data when modeling very large values in context of remote procedure calls, persistent data, and virtual memory. Based on configuration, we might integrate with content delivery networks. Users can potentially extend these use cases with sufficient access, but we must be careful regarding garbage-collection.

Rough API sketch: 

* `sys.refl.glob.*` - an API for serialization or parsing of glas data into a binary representation. Operations will take a CAS as a context argument to keep hashes in scope. Lazy loading of a binary into a value might extend the set of hashes that CAS is waiting on. Serializing a value can store hases into the CAS. Values cannot be serialized if they're still waiting on lazy hashes, but we can potentially determine which hashes we're lazily waiting upon.

* `sys.refl.cas.*` - (tentative) an API that helps users maintain a content-addressed storage (CAS) context. This might prove unnecessary, perhaps we could maintain a serialization context as plain old data without any access to hashes held by the runtime. In any case, we can store and fetch binaries. Each stored binary might be paired with a list of hashes referenced by the binary. We can potentially report which hashes we're waiting on. The details need work, but should closely align to whatever a runtime is doing under the hood.

## Node Locals

A distributed transaction doesn't have a location, but it must start somewhere. With runtime reflection, we can take a peek.

* `sys.refl.txn.node() : NodeRef` - return a stable identifier for the node where the current transaction started.

This API is useful for keeping associative state per node in a key-value store. 

Observing the starting node has consequences! A runtime might discover that a transaction started on node A is better initiated on node B. Normally, we abort the transaction on node A and let B handle it. After observing that we started on node A, we instead *migrate* to node B. With optimizations, we can repeatedly evaluate on node B, *but only while connected to node A*. Thus, carelessly observing the node results in a system more vulnerable to disruption.

## Filesystem and Network

I propose to implement filesystem and network APIs in terms of FFI. We don't get much out of these APIs other than a safety wrapper around FFI, and that can be left to the application or adapter layers.

There are a few tweaks I would make: better support for pipelining of commands or error handling without going back to the transaction on every little step, reading and writing whole files as a single command, etc..
