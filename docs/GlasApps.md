# Glas Applications

Glas can support procedural-loop applications, e.g. `int main() { ... mainloop ... }`. This can be achieved by typefully restricting use of effects within 'try' clauses, using a syntax that compiles to hide direct use of 'try' behind if/then/else or pattern matching. The main benefit of this design is that it's utterly conventional: we can directly adapt existing APIs.

An intriguing alternative is to embrace the transactional nature of those 'try' clauses, and restrict effects to those that easily fit a *transaction machine* application model. The benefit of this alternative is that there are many nice non-functional properties for concurrency, process control, live coding, reactive behavior, and real-time systems. The weakness is that transactions do not support synchronous interaction with external systems, requiring a huge redesign of the APIs.

This document discusses development of transaction machines based applications with Glas. However, it might be wiser to get started with Glas apps using the conventional style.

## Transaction Machines

Transaction machines model software systems as a set of repeating transactions on a shared environment. Individual transactions are deterministic, while the set is scheduled fairly but non-deterministically. 

This model is conceptually simple, easy to implement naively, and has very many nice emergent properties that cover a wide variety of systems-level concerns. The cost is that a high-performance implementation is non-trivial, requiring optimizations from incremental computing, replication on fork, and cached routing.

### Process Control

Deterministic, unproductive transactions will also be unproductive when repeated unless there is a relevant change in the environment. The system can optimize by waiting for relevant changes. 

Aborted transactions are obviously unproductive. Thus, aborting a transaction serves as an implicit request to wait for changes. For example, we could abort to wait for data on a channel, or abort to wait on time to reach a threshold condition. This supports process control.

### Reactive Dataflow

A successful transaction that reads and writes variables is unproductive if the written values are equal to the original content. If there is no cyclic data dependency, then repeating the transaction will always produce the same output. If there is a cyclic data dependency, it is possible to explicitly detect change to check for a stable fixpoint.

A system could augment reactive dataflow by scheduling transactions in a sequence based on the observed topological dependency graph. This would minimize 'glitches' where two variables are inconsistent due to the timing of an observation.

*Aside:* Transaction machines can also use conventional techniques for acting on change, such as writing update events or dirty bits.  

### Incremental Computing

Transaction machines are amenable to incremental computing, and will rely on incremental computing for performance. Instead of fully recomputing a transaction, we rollback and recompute based on changes. 

To leverage incremental computing, transactions should be designed with a stable prefix that transitions to an unstable rollback-read-write-commit cycle. The stable prefix reads slow-changing data, such as configuration. The unstable tail implicitly loops to process channels or fast-changing variables.

Stable prefix and attention from the programmer is adequate for transaction machine performance. However, it is feasible to take incremental computing further with reordering optimizations such as lazy reads, or implicitly forking a transaction based on dataflow analysis.

### Task-Based Concurrency and Parallelism

Task-based concurrency for transaction machines can be supported by a fair non-deterministic fork operation, combined with incremental computing and a replication optimization. 

Relevant observations: A non-deterministic transaction is equivalent to choosing from a set of deterministic transactions, one per choice. For isolated transactions, repetition and replication are logically equivalent. When the choice is stable, replication reduces recomputation and latency. 

Stable forks enable a single transaction to model a concurrent transaction machine. Forks are dynamic and reactive. For example, if we fork based on configuration data, any change to the configuration will rollback the fork and rebuild a new set.

Transactions evaluate in parallel only insofar as conflict is avoided. When conflict occurs between two transactions, one must be aborted by the scheduler. Progress is still guaranteed, and a scheduler can also guarantee fairness for transactions that respect a compute quota. A scheduler heuristically avoids conflict based on known conflict history. Programmers avoid conflict based on design patterns and fine-grained state manipulations.

### Real-Time Systems 

It is feasible for a transaction to compare estimated time of commit with a computed boundary. If the transaction aborts because it runs too early, the system can implicitly wait for the comparison result to change before retrying. Use of timing constraints effectively enables transactions to both observe estimated time and control their own scheduling. 

Usefully, a system can precompute transactions slightly ahead of time so they are ready to commit at the earliest timing boundary, in contrast to starting computation at that time. It is also feasible to predict several transactions based on prior predictions. It is feasible to implement real-time systems with precise control of time-sensitive outputs.

Transaction machines can flexibly mix opportunistic and scheduled behavior by having only a subset of concurrent transactions observe time. In case of conflict, a system can prioritize the near-term predictions.

### Cached Routing

A subset of transactions may focus on data plumbing - i.e. moving or copying data without observing it. If these transactions are stable, it is feasible for the system to cache the route and move data directly to its destination, skipping the intermediate transactions. 

Designing around cached routing can improve latency without sacrificing visibility, revocability, modularity, or reactivity to changes in configuration or code. In contrast, stateful bindings of communication can improve latency but lose most of these other properties.

Cached routing can partially be supported by dedicated copy/forward APIs, where a transaction blindly moves the currently available data from a source to a destination. However, it can be difficult to use such APIs across abstraction layers. In general, we could rely on abstract interpretation or lazy evaluation to track which data is observed within a transaction.

### Live Program Update

Transaction machines greatly simplify live coding or continuous deployment. There are several contributing features: 

* code can be updated atomically between transactions
* application state is accessible for transition code
* preview execution for a few cycles without commit

A complete solution for live coding requires additional support from the development environment. Transaction machines only lower a few barriers.

## Application Model Design

One goal is to get started soon, so I'd prefer to avoid radical ideas that complicate the implementation on modern OS. However, within that constraint, I'd like to tune design of applications to better fit transaction machines and my vision for software systems.

### Applications as Objects

Almost any application will have some private state to model state machines. A transaction machine can be modeled as a scheduler repeatedly applying a transactional 'step' method to an application object. 

Then methods other than 'step' can support a user or system interacting with the application. For example, GUI integration might benefit from an 'icon' method that returns an application icon, or a 'notifications' method that returns a list of active alerts for the user. Those methods might only be called occasionally. The system might support a few methods to support hibernation mode, graceful termination, garbage collection, or API versioning.

I propose to model application objects as a `Method -[Effects]- Result` program, where effect and result types may depend on the method. For the `step` method, the result should also be `step` so in the simplest case we could ignore the method parameter and define an application as just the repeating step operation.

### Structured Channels

Communication with external systems should not assume they are local, transactional, or stable. Thus, external communications should be asynchronous, monotonic, and disruption tolerant. Channels are an excellent model under these constraints. Plain data channels are too inflexible, but we can support subtasks using subchannels. 

Glas can support second-class subchannels: the writer may write a choice of data or subchannel, and the reader can detect whether the next element is a subchannel or data and receive it appropriately. An operator may exist to logically wire channels together, which serves as a pseudo first-class channel transfer. 

Loopback channels can support consistent composition and modularity within applications. To ensure consistent behavior with external channels, a transaction cannot read its own writes to a loopback, i.e. there is implicitly an external transaction that forwards the data and subchannels.

*Aside:* Although it is feasible to extend point-to-point channels into a broadcast databus shared by multiple writers and readers, I believe it wiser to limit channels to point-to-point then model broadcast explicitly via connection to intermediate services.

### System Services

For consistency, I propose to model an application's access to the host system as a channel. System requests will mostly be represented by writing a subchannel per request then writing the request description as the first value in the subchannel. The subchannel can receive the future result or support ad-hoc interaction with a long-running background task.

Because the system channel is write-only, there is no risk of read-write conflict between transactions initiating new system requests. Usefully, the system channel preserves *order* of requests, which can be relevant when incrementally patching system state. It is feasible to support a system request to fork the system channel. Requests on different forks would be processed in a non-deterministic order.

### Robust References

References used by an application should be allocated within the application. For example, instead of `open file` *returning* a system-allocated reference such as a file descriptor, we should express `open file as foo` to specify that symbol 'foo' is the application-allocated reference for the newly opened file. In context of structured channels, this will mostly apply to channel references.

This design has several benefits: References can be allocated statically by application code. Reference values can be descriptive of purpose and provenance, simplifying debugging or reflection. Allocation of runtime references can be manually partitioned to resist read-write conflict on a central allocator. Because all references are internal, there are no security risks related to attempted forgery of system references.

To simplify conflict analysis, references will be linear, i.e. no aliasing. Forking of channels to support concurrent interaction should be an explicit request. However, linearity is not too onerous when combined with forking of transaction machines

In context of transaction machines, concurrent transactions would still time-share the channels.

### Specialized APIs

System services can be specialized for the host. For example, web-apps can support services based around the document object model, local storage, XMLHttpRequest, and web-sockets. A console app might support an API based around TCP/UDP network sockets and file streams. These interactions can be modeled using a specialized protocol over the system channel.

Ideally, application APIs should be specialized by the developer, expressed in terms of a problem-specific data models, update events, effects, and editable projections. A specialized application API can simplify model testing, analysis, reuse, portability, and protection of invariants. 

Application APIs will ultimately be constrained by implementation on an asynchronous host API, requiring an asynchronous adapter layer.

## Common Effects

Most effects are performed indirectly via channels. But we still need an env/eff API layer to manage these communications.

### Concurrency

Task-based concurrency is based on repeating transactions that perform fair, stable, non-deterministic choice. With support from runtime and compiler, this can be optimized into replication, with each replica taking a different choice. Effects API:

* **fork** - response is non-deterministic unit or failure. 

Fork becomes a random choice if used in an unstable context or beyond the limits of a replication quota.

### Timing

Transactions are logically instantaneous. The relevant time of a transaction is when it commits. It is troublesome to observe commit time directly, but we can constrain commit time to control scheduling. Effects API:

* **await:TimeStamp** - Response is unit if time-of-commit will be equal or greater than TimeStamp, otherwise fails.

The system does not know exact time-of-commit before committing. At best, it can make a heuristic estimate. It's preferable to estimate a just little high: the system can easily delay commit by a few milliseconds to make an 'await' valid. 

When await fails and the transaction aborts, the timestamp serves as a hint for when the transaction should be recomputed. It is feasible to precompute the future transaction and have them prepared to commit almost exactly on time. This can support real-time systems programming.

Timestamps will initially support `nt:Number` referring to Windows NT time - a number of 0.1 microsecond intervals since midnight Jan 1, 1601 UT. This could be extended with other variants.

### Memory

All applications need private memory to carry information between transactions. Memory will be modeled as a Glas value, generally organized as a record such that each field models a variable for lightweight conflict analysis. Memory is accessible as a large value for purposes such as checkpointing.

* **mem:(on:Path, op:MemOp)** - Path is a bitstring, and the MemOp represents an operation to observe or modify memory. The intention is that conflict analysis can mostly be based on path analysis. Independent paths must respect the pref
 * **get** - Response is memory value at Path; fails if there is no value.
 * **put:Value** - Insert or replace value at Path.
 * **del** - Remove assigned value (if any) from Path in memory. 
 * **touch** - respond with unit if 'get' would succeed, otherwise fails.
 * **swap:Value** - Atomic get and put. Compared to separate get then put, swap avoids an implicit read-write dependency in case of black box conflict analysis.

The get/put/del memops support basic record manipulations. The touch/swap memops help stabilize common update patterns. We can add further methods for reading and writing lists, but there is little need in context of communication channels. 

### Communication

Channels work well with or without transactions due to their monotonic nature. Channels in Glas are second-class, but logical tunneling of subchannels can provide expressiveness very near to first-class channels. 

A viable API:

* **chan:ChanOp** - A namespace for channel operations.
 * **lo:(from:ChanRef, to:ChanRef)** - Create a new loopback channel pair. Writes to either channel can be read from the other after a non-deterministic delay. This delay means a transaction cannot read its own writes. Returns unit.
 * **co:(from:ChanRef, to:ChanRef)** - Connect two existing channels, such that elements in one channel are propagated to the other and vice versa. Channels cannot be manually read or written after connection. Returns unit.
 * **wd:(to:ChanRef, data:Elem, batch?)** - Write a data element to specified channel. Returns unit. Options:
  * *batch* - flag. If set, *data* must be a list of multiple elements to write.
 * **wc:(to:ChanRef, as:ChanRef)** - Constructs a new tunnel through the 'to' channel, and binds the writer's endpoint to the freshly allocated 'as' channel reference. Returns unit.
 * **wq:(to:ChanRef)** - Quit writing the channel. Further elements written to the channel are dropped before they reach the reader. Returns unit.
 * **rd:(from:ChanRef, count?N, exact?)** - Read a data element from specified channel. Returns element. Fails if no element available, including if next element is not data. Options:
  * *count* - optional number. If specified, returns list of up to N available data elements.
  * *exact* - flag. If set, fail if fewer than *count* elements available.
 * **rc:(from:ChanRef, as:ChanRef)** - Bind an incoming subchannel to the freshly allocated 'as' reference. Fails if the next element is not a subchannel. Returns unit.
 * **rq:(from:ChanRef)** - Returns unit if all elements have been read and no more elements will be received, whether due to involuntary disruption or voluntary 'wq'. Otherwise fails.

Channel references are arbitrary Glas values, allocated by the application. If a channel is already in use, binding it to a new channel will implicitly perform 'wq' on the prior channel.

Writers cannot directly detect whether readers are present or active. Channel protocols should be designed with explicit feedback from readers - acknowledgements, ready tokens, etc. - to ensure writers aren't wasting their efforts. If a reader applies 'wq', it should eventually stop the writer. Network disruption is modeled as an implicit 'wq' in both directions.

### Evolution

We can follow the example of language modules and tests and introduce a **log** effect for development and debugging purposes.

* **log:Message** - Response is unit. The message is intended for consumption by application developers or the development environment. Unlike most effects, even aborted log messages can be visible, perhaps distinguished by display color. Messages should have a variant header such as `warn:Warning` vs. `info:Information` to simplify routing.

Integration between logging and the runtime environment is ad-hoc, e.g. `log:bp:Number` could provide a basis for breakpoints. In context of transaction machines, a stable repeating transaction will frequently repeat log messages, so it can be useful to the log as a 'frame' of active messages instead of a stream of past messages.

## Console Applications

Console applications minimally require access to:

* environment variables and command line arguments
* standard input and output
* filesystem
* network - UDP and TCP

Log output may bind stderr by default.

### Environment Variables and Command Line Arguments

These values can be returned synchronously and modeled as implicit parameters:

* **query:cmd** - response is list of strings representing command-line arguments.
* **query:env** - response is list of 'key=value' strings for environment variables.

There is no equivalent to 'setenv', but it is feasible to use the env/eff operator to intercept 'query' and override the environment within context of a subprogram.

### Standard Input and Output

Applications can start with access to standard input and output as a pre-allocated channel, with channel reference unit. Reads from this channel correspond to standard input, and writes to standard output.

### Filesystem

I'm uncertain how to best model a filesystem API. Requirements include reading, writing, and addending files, and browsing folders. It would also be useful to continuously watch for filesystem changes, and perhaps to support Glas Object as a write mode alternative to binary read/write.

One idea is to combine browsing directories with watching for changes, e.g. by modeling the file browser as a channel that receives a stream of updates to maintain a view. 

* **file:FileOp** - A namespace for file operations.
 * **

Response is unit. The operation is deferred until transaction commit, and the future result is provided via specified channel. The file path may specify a directory in some cases.

### TCP

Most console applications can simply use TCP for all their networking needs.

### UDP

## Web Applications

### DOM

### HTTP Requests

For a web-application, we might 

### Web Sockets

### Local Storage


## Meta

### Synchronous Syntax

Transaction machines work most conveniently with asynchronous interaction models, such as message passing, channels, blackboard systems, and tuple spaces. 

However, it is feasible to support a program layer that compiles synchronous code to run on the transaction machine platform. This would involve representing control flow across transactions as a state machine, perhaps aligning continuations with labeled boundaries to improve stability.

As a compilation target, the transaction machine would still provide a robust foundation for concurrency, process control, incremental computing, and live coding. 

This seems a very promising direction to pursue in the long term.
