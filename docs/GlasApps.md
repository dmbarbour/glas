# Glas Applications

Glas programs have algebraic effects, which greatly simplifies exploration of different application models. In conjunction with access to programs as values, it is feasible to implement robust adapters layers between models.

The most conventional application model is the procedural loop, i.e. the application code is some variation of `void main() { init; loop { do stuff }; cleanup }`. State and behavior are both private to the application loop, not accessible for extension or inspection. Concurrency is very awkward and error-prone in this model. The only *generic* operation on the running application is to kill it. Glas systems will use this conventional model for command line verbs (i.e. `glas-cli-verb.run` programs) because it's easy to implement due to conventions.

An intriguing alternative is the *transaction machine*, where an application is a dynamic set of transactions that run repeatedly. The loop is implicit, and state is maintained outside the loop, which significantly improves extensibility and enables orthogonal persistence. Concurrent coordination is very natural in this program model. It is feasible to update code atomically at runtime. Transaction machines are a good foundation for my visions of robust live coding as a basis for user-interfaces.

This document explores my vision for the development of applications in Glas systems.

## Transaction Machines

Transaction machines model software systems as a dynamic set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has very many nice emergent properties that cover a wide variety of systems-level concerns. The cost is that a high-performance implementation is non-trivial, requiring optimizations from incremental computing, replication on fork, and cached routing.

### Process Control

Deterministic, unproductive transactions will be unproductive when repeated unless there is a relevant change in the environment. Thus, the system can optimize a busy-wait loop by directly waiting for relevant changes. Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. Explicit waits, such as waiting for an external input or on a clock, are also feasible. In context of transaction machines, explicit waits are implicitly be canceled and recomputed in case of concurrent changes to values read earlier within the same transaction. Thus, waits and interrupts are implicit and natural for transaction machines.

However, a consequence is that transactions should not 'sleep'. They can wait on a specific future timestamp, but reading the current time then waiting is a problem because the current time is continuously changing.

### Reactive Dataflow

A successful transaction that reads and writes variables (no external effects) is unproductive if the written values are equal to the original content. If there is no cyclic data dependency, then repeating the transaction will always produce the same output. If there is a cyclic data dependency, it is possible to explicitly detect change to check for a stable fixpoint.

A system could augment reactive dataflow by scheduling transactions in a sequence based on the observed topological dependency graph. This would minimize 'glitches' where two variables are inconsistent due to the timing of an observation.

*Aside:* Transaction machines can also use conventional techniques for acting on change, such as writing update events or dirty bits.  

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail implicitly loops to process channels or fast-changing variables.

Stable prefix and attention from the programmer is adequate for transaction machine performance. However, it is feasible to take incremental computing further with reordering optimizations such as lazy reads, or implicitly forking a transaction based on dataflow analysis.

### Task-Based Concurrency and Parallelism

Relevant observations: A non-deterministic transactions is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When a non-deterministic choice is stable, replication reduces recomputation and latency. 

It is feasible for a transaction machine to start as a single transaction then introduce a non-deterministic 'fork' effect to represent the set of transactions. This has the advantage of making the set much more dynamic and reactive to observed needs and configurations.

Transactions evaluate in parallel only insofar as conflict is avoided. When conflict occurs between two transactions, one must be aborted by the scheduler. Progress is still guaranteed, and a scheduler can also guarantee fairness for transactions that respect a compute quota. A scheduler can heuristically avoid most conflict based on tracking conflict history. Programmers can avoid conflict based on design patterns, e.g. using channels to separate tasks from high-contention state variables.

### Real-Time Systems 

It is feasible for a transaction to compare estimated time of commit with a computed boundary. If the transaction aborts because it runs too early, the system can implicitly wait for the comparison result to change before retrying. Use of timing constraints effectively enables transactions to both observe estimated time and control their own scheduling. 

Usefully, a system can precompute transactions slightly ahead of time so they are ready to commit at the earliest timing boundary, in contrast to starting computation at that time. It is also feasible to predict several transactions based on prior predictions. It is feasible to implement real-time systems with precise control of time-sensitive outputs.

Transaction machines can flexibly mix opportunistic and scheduled behavior by having only a subset of concurrent transactions observe time. In case of conflict, a system can prioritize the near-term predictions.

### Cached Routing

A subset of transactions may focus on data plumbing - i.e. moving or copying data without observing it. If these transactions are stable, it is feasible for the system to cache the route and move data directly to its destination, skipping the intermediate transactions. 

Designing around cached routing can improve latency without sacrificing visibility, revocability, modularity, or reactivity to changes in configuration or code. In contrast, stateful bindings of communication can improve latency but lose most of these other properties.

Cached routing can partially be supported by dedicated copy/forward APIs, where a transaction blindly moves the currently available data from a source to a destination. However, it can be difficult to use such APIs across abstraction layers. In general, we could rely on abstract interpretation or lazy evaluation to track which data is observed within a transaction.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing factors: 

* code can be updated atomically between transactions
* application state is accessible for transition
* feasible to simulate several cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Embeddings

### Procedural Embedding of Transaction Machines

It is feasible to compile a transactional loop, such as a Glas program of form `loop:(until:Halt, do:cond:try:Step)`, to run as a transaction machine within a procedural context. The data stack can be compiled into a set of transaction variables. Static analysis and runtime instrumentation supporting fine-grained read-write conflict detection. A non-deterministic fork within Step would support task-based concurrency. 

The embedding loses implicit live coding, but we still benefit from implicit process control, incremental computing, reactive dataflow, etc.. I intend to use this procedural embedding in context of [Glas command line interface](GlasCLI.md).

### Transaction Machine Embedding of Procedural

A transaction machine cannot directly evaluate procedural code within a transaction. However, it is feasible to create a concurrent task that repeatedly evaluates the procedural code by a step then yields, saving the procedure's continuation for a future transaction. 

This embedding is essentially the same as multi-threading. We could kill the procedural thread or spawn new ones. However, the transaction machine substrate does offer a few benefits: lightweight support for transactional memory, an implicit 'select' behavior via fork-read within a transaction, and a more robust foundation to integrate with other paradigms.

## Abstract Design Thoughts

### Robust References

References are essentially required for concurrent interaction. For example, an app will interact with *more than one* file or tcp connection at a time.

Applications should allocate their own references. For example, instead of `open file` returning a system-allocated handle, effect APIs should favor the format `open file as foo` where `foo` is an arbitrary value allocated by the application as a handle for future interactions with the file.

This design has a lot of benefits: References can be descriptive and locally meaningful for debugging. Static allocation of references is easily supported. Dynamic allocation can be partitioned and decentralized, reducing contention. We avoid an implicit source of observable non-determinism. There is no security risk related to leaky abstraction or forgery of references.

The application host will generally maintain a lookup table to associate references with external resources. Garbage collection is feasible if the application can fetch lists of live references.

### Procedural APIs

I do not recommend an object-oriented API, i.e. where file streams and TCP streams share an interface. The main reason for this is that it becomes difficult to track which values represent references. Additionally, it is inconvenient to ensure consistency between interfaces. An application should implement its own resource abstraction layers as needed, no need to handle this at the host-app boundary.

### Regarding Timeouts

It is logically inconsistent to have explicit timeouts for individual effects within a transaction. Instead, timeouts are expressed by racing concurrent transactions, e.g. where one fork waits on data and another waits on the clock before we have committed to either fork. Alternatively, a repeating transaction that queries whether data is available then later fails would implicitly wait for change in availability of data.

However, implicit waits require some advanced optimizers to implement, and may be unavailable in some contexts. At least for early implementations, to resist busy-wait loops, we might implicily introduce a timeout when, for example, a TCP socket has been queried twice in a short time-frame by failed transactions. This timeout might be applied to the next transaction failure instead of directly to the effect.

### Hierarchical Composition and State

An application should be composable as a subprogram of a larger application. Effects handlers already support hierarchical composition of effects. However, the state of a subprogram should also be accessible to its parent program to support debugging, invariants, extensions, checkpoints, and other generic host features. 

My preferred choice is to represent input and output on the data stack. This is consistent with procedural embedding of transaction machines, and benefits from abstract interpretation and other static analysis of dataflow to compile a record into many fine-grained transaction variables. The consequence is that state composes in several ways, e.g. we can easily model partial sharing of state, wrappers, sequential composition.

### Extensible Interfaces

Applications should be extensible with new interfaces. For example, we might extend an application with intefaces to obtain an icon or present an administrative GUI.

In context of Glas programs, we might represent method selectors as part of program input on the data stack, e.g. `method:Args`. Result type and available effects may depend on the selected method. This technique has the advantage of being cheap, with negligible overhead if unused.

### Specialized Host APIs

An application directly interacts with its host via effects, and indirectly with other applications. It is feasible to develop an API for communications based on channels, publish-subscribe, etc.. However, a general API for communications is often difficult to integrate with existing systems and applications. It is wiser to focus on an effects model that is specialized to its host, easily implemented, then build effects adapters within the Glas program layer.

## Common Effects

Effects should be designed for transaction machines yet suitable for procedural code to simplify embeddings.

### Concurrency

For isolated transactions, a non-deterministic choice is equivalent to taking both paths then committing one. For repeating transactions, this becomes equivalent to replicating the transaction and repeating for every choice. Replication, together with incremental computing, enables non-deterministic choice to support task-based concurrency. Effects API:

* **fork** - response is a non-deterministic boolean (bitstring '0' or '1'). 

Fork effects are subject to backtracking in cases where this is visible, i.e. if a 'try' clause forks then fails, a subsequent fork will read the same response. Ideally, fork is implemented by replication. However, if the fork is unstable or if replication is unavailable for other reasons, we can implement fork by fair random choice.

### Logging

Almost any application model will benefit from a simple logging mechanism to support debugging, observability of computations, etc..

* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

By convention, a log message should be a record of ad-hoc values, such as `(lv:warn, text:"I'm sorry, Dave. I'm afraid I can't do that.", from:"HAL")`. This enables the record to be extended with metadata or new features.

In context of transaction machines, a forking set of transactions would essentially maintain a forking tree of log messages, the prefix of which might be stable. There is a lot of flexibility regarding how this might be rendered to the user.

### Time

Transactions are logically instantaneous, but it is not a problem to delay a transaction to commit later. Effects API:

* **time:now** - Response is the estimated time of commit, as a TimeStamp. 
* **time:await:TimeStamp** - Wait until the specified time or later, then continue. Response is unit.

Reading time will destabilize the transaction. This isn't a significant problem if time is read after the transaction would be destabilized anyways, e.g. after reading from an input channel. Estimated times might be a little high or low within heuristic tolerances, but there is increased risk of read-write conflicts when several transactions are reading the unstable time variable.

Await does not observe time. It is important to note that observing time T then awaiting time T+1 within a single transaction is inconsistent and results in a transaction that deadlocks, similar to attempting synchronous request-response. This should be avoided statically, or reported as a programmer error if it appears. It is feasible to precompute a transaction after await so it is ready-to-commit almost exactly on time; this might simplify real-time programming.

The proposed default TimeStamp type is Windows NT time - a natural number of 100ns intervals since midnight Jan 1, 1601 UT. If an alternative is used, just document it clearly.

### Environment Variables

The initial use-case for environment variables is to provide access to OS-provided environment variables, such as GLAS_PATH. This can be expressed as `env:get:"GLAS_PATH"`, or `env:get:list` to obtain a list of defined variable names. However, this API is easy to generalize. An environment can provide access to arbitrary variables. It is possible for variables to be computed upon get, or validated upon set (i.e. runtime type checks). Some may be read-only, others write-only. 

* **env:get:Variable** - response is a value for the variable, or failure if the variable is unrecognized or undefined. 
* **env:set:(var:Variable, val?Value)** - update a variable. The value field may be excluded to represent an undefined or default state. This operation may fail, e.g. if environment doesn't recognize variable or fails to validate a value. 

The environment controls environment variables, thier types, and opportunity for extension. Programs should not rely on environment variables for private state; use a structure managed on the data stack for this role.

## Console Applications

See [Glas command line interface](GlasCLI.md).

## Web Applications

Another promising near-term target for Glas is web applications, compiling apps to JavaScript and using effects oriented around on Document Object Model, XMLHttpRequest, WebSockets, and Local Storage. 
