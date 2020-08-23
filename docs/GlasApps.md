# Glas Applications

In my vision of software systems, applications are easy for those users to comprehend, control, compose, extend, modify, and share - especially at runtime. Further, applications should be robust and resilient, degrade gracefully and recover swiftly from disruption.

Kahn Process Networks (KPNs) are not an optimal foundation for applications. KPNs capture state, which hinders runtime update. KPNs do not gracefully handle unbounded disruption such as network partitioning. KPNs are best for modeling deterministic, interactive computations at very large scales. Glas program model is based on KPNs to support very large, reproducible builds.

The *Transaction Machine* (TXM), described below, is much closer to my ideal application model. The TXM separates state and behavior which simplifies observation and modification of both. TXMs support partitioning and non-determinism (via fork), and are naturally resilient to disruption. A TXM does require a deterministic, interactive computation to represent the transaction, and KPNs are a good fit for this role.

This document describes Transaction Machine, how it might be implemented in Glas systems, and how applications might be developed with it.

## Transaction Machine

A transaction machine is a deterministic, hierarchical, forkable, repeating transaction over an abstract environment. The transaction is implicitly scheduled and repeated by the system.

Process control is implicit. A deterministic transaction that changes nothing will be unproductive when repeated, unless input has changed. The system can implicitly wait for relevant changes. 

Hierarchical transactions simplify composition and testing of transaction machines. However, the primary use case is for conditional backtracking behavior and error handling.

Forking supports division of large transactions into smaller subtasks. A fork request replicates a transaction then responds to each replica with a different value. Together with incremental computing via partial rollback, every fork becomes a concurrent transaction machine.

Abstraction of the environment simplifies extension and makes it easier for the system to track which values are observed and modified and compute conflicts.

### Required Optimizations

For transaction machines to be viable, two optimizations are necessary: rollback and replication.

Rollback will reuse prior computation of a transaction up to the first change in input. Without rollback, transactions must be recomputed from the start. Efficient rollback requires compiler support.

Replication will concurrently compute forks. Logically, a fork actually returns a non-deterministic value, but replication and repetition are equivalent for transactions. Without replication, we could rotate through forks, but this increases latency.

*Note:* There are many potential optimizations beyond the required two, e.g. fast roll-forward for irrelevant change, fusion of cooperative transaction loops, compile-time forks, and constant propagation.

## Application Behavior

Glas programs can model transactions via request-response channel. Requests:

* **commit:Status | abort:Reason** - Accept or reject state. Response is `ok`. No further requests accepted by transaction.
* **try** - Begin hierarchical transaction. Response is `ok`. Transaction terminates with matching commit or abort.
* **fork:Keys** - Replicate current transaction. Response to each replica is different key from dictionary. The fork-path, a list of keys, provides a stable name for debugging.
* **note:Annotation** - Response is `ok`. Intended for optimization and scheduling hints, debug traces, breakpoints, assertions, etc. - requests a host may ignore without breaking behavior.
* **apply:(obj:Reference, method:Method)** - Query or update environment. References and methods are values meaningful to the environment. Response is provided by environment.

Runtime type errors will normally cause a transaction to abort without response. Ideally, static analysis of an application should validate use of the API and determine environment type.

In addition to the request-response channel for transactions, the programs receive a parameter via input port `env`. For a top-level OS application, this parameter will have form `(cmd:[List, Of, Command, Line, Args], var:(PATH:String, ...))`.  

## Application State

All Glas applications have state, modeled by a Glas value. An application definition has two main parts: a program that computes a transaction, and the initial or current state. Application state is private except where explicitly shared (see *External Effects*).

State is referenced as `st:Path`. The path is a list of symbols and numbers such as `[foo, 42, baz]`. A symbol indexes a dictionary, while a number indexes a list. The empty path `[]` references the root value. A program is free to compute paths. 

State methods are pure functions that query or updates the referenced value. The get and set methods are logically sufficient. Other methods are provided to improve precise detection of conflict and change.

### Generic State Methods

These methods apply for all application state references.

* **get** - Query. Response is whole value.
* **set:Value** - Update. Assigns value. Response is `ok`. 
* **eq:Value** - Query. Compare values for equality. Response is Boolean.
* **type** - Query. Response is `dict | list | number | invalid`. The `invalid` response is for invalid references.
* **valid** - Query. Response is Boolean.

### Dictionary Methods

Dictionaries are mostly used as namespaces, via pathing. Dictionaries support a few methods that would make them a passable basis for publish-subscribe or tuple space patterns.

* **d:keys:(in?Keys, ex?Keys)** - Query. Repond with Keys. Optional parameters:
 * *in* - restrict response by intersection (whitelist)
 * *ex* - restrict response by difference (blacklist)
* **d:insert:Dict** - Update. Insert or update keys in dictionary. Response is `ok`
* **d:remove:Keys** - Update. Remove selected keys from dictionary. Response is `ok`.

A keys value is a dictionary with unit values.

### List Methods

In context of transactional updates, lists are useful for modeling queues. Multiple writers and a single reader can commit concurrently under reasonable constraints.

* **l:insert:(items:\[List, Of, Values\], head?, skip?Number)** - Update. Adds items to tail of list. Response is `ok`. Optional flags:
 * *head* - Add items to head of list instead.
 * *skip* - Default zero. Skip over items before writing. If skip is larger than list length, response is `error` and nothing is modified. (Zero skip cannot cause error.)
* **l:remove:(count?Number, atomic?, tail?, skip?Number)** - Default count is 1. Remove count items from head of list, or maximum available. Response is the slice of items removed . Optional flags:
 * *atomic* - Remove count items if available, else zero, nothing between.
 * *tail* - Take slice from tail of list instead. List order is still preserved.
 * *skip* - Default zero. Skip offset items before reading. Attempting to read beyond end of list responds with empty list.
* **l:length** - Query. Response is number of items in list.
* **l:lencmp:Threshold** - Query. Same as `n:compare` on length.

To use lists as arrays of objects, construct paths that contain numeric indices.

### Number Methods

For most numbers, get/set is sufficient. However, *counters* can be high-contention, and we can also try to stabilize observation of numbers with thresholds.

* **n:increment:(count?Count)** - Update. Default count is 1. Increase referenced number by Count. Response is `ok`.
* **n:decrement:(count?Count)** - Update with failure. Default count is 1. If count is larger than number, response is `error` and the number is unmodified. Otherwise, reduces number by count and response is `ok`.
* **n:compare:Threshold** - Query. Compares number to threshold. Response is `lt | eq | gt` corresponding to number being less than, equal to, or greater than the threshold.

### Live Coding, State Versioning, and Abstraction

Application behavior can be updated between transactions. However, application state may also need to be updated to handle changes in environment or data models. 

Support for state update can be integrated: Application state could contain version identifiers, and application behavior can check the version and rewrite representations as needed. For large applications, lazy state updates are also feasible, via dividing application state into abstract objects that can be updated independently.

Programming with abstract, versioned objects can be supported via the higher-order programming mechanisms in Glas, and further by careful design of syntax.

### Dynamic Allocation and Memory Management

Dictionaries can model regions for dynamic allocation. The region can include counters to compute numeric symbols. Each symbol can be inserted into the region with an initial value. Paths through this symbol can be computed.

The advantage of using a dictionary is that there is no need to maintain a free-list or defragment memory. It's sufficient to remove the reference when done. We never need to reuse a reference, thus use of the `type` method could tell us when we have dangling references.

Applications must manage their own state. Manual management is a likely starting point, explicit deletion of objects no longer needed. However, it is feasible for Glas applications to model concurrent garbage collection via forked transaction, and even incremental garbage collection based on snapshots.

*Note:* Managing state is a separate layer from managing 'memory'. The memory for values is managed by the Glas compiler or runtime.

### Orthogonal Persistence

Because state is separated from behavior, orthogonal persistence doesn't require special compiler support. The host system can continuously save application state to durable storage. 

In conjunction with [content-addressed storage](https://en.wikipedia.org/wiki/Content-addressable_storage) and [log-structured merge-trees](https://en.wikipedia.org/wiki/Log-structured_merge-tree), it is quite feasible to incrementally store updates and support very large applications. The [Glas Object](GlasObject.md) representation is designed for this purpose.

Programmers should develop "in-memory" indexed, relational databases above application state. Together with orthogonal persistence, many applications can readily be designed on such databases. Integration and performance could be much smoother compared to networked databases.

## External Effects

Applications are hosted. The host will provide objects and methods to support interaction with user, network, and other applications. Top-level references such as `gui` or `net` are standardized. 

A transaction must commit before external interaction begins. Asynchronous IO is required. 

In the common case, the host will allocate a new object representing the activity, then respond with a reference to this object. Methods can query available input, specify further output, further branch, or terminate the activity. To keep it simple, the reference is specific to the current host-app session. This concept is essentially [file descriptors](https://en.wikipedia.org/wiki/File_descriptor).

From perspective of a persistent application, the host is ephemeral and exchangeable. Between transactions, the host may be restarted or application migrated. Thus, the application must verify a host reference is still valid before using it. The next host must efficiently and reliably invalidate references to prior host. This can be achieved by including a session identifier in allocated references. 

This section explores a potential foundation for Glas applications. Of course, the details may be adjusted simplify implementation.

### Reference Validation

An application may ask the host whether a reference is still valid. This would be specific to the application, i.e. if a reference is 

* **valid** - Query. Response is Boolean.

Programmers should wrap host references with metadata for internal use.

### Application Object

Reference `app`.

* **reset** - Fire-and-forget. Response is `ok`. After commit, performs soft-reset of the application: invalidates allocated host references and resets temporary storage.

A reset is implicit when a host loads an application.

### Temporary Storage

Reference `tmp:Path`.

Behaves same as application state, with a few exceptions:

* prefix `tmp:` instead of `st:`
* lost on reset or migration
* initial value is unit `()`.

Temporary storage is convenient for storing allocated host references, and makes clear which data should be persisted. 

### Data Bus

A message written on the bus is broadcast to all attached readers. Readers may snoop the bus, which is convenient for diagnostics, or filter messages for what is relevant to them. The bus does not store data. 

Reference `bus:busname`.

* **write:Message** - Fire and forget. Adds message to data bus upon commit. Response is `ok`.
* **attach** - Response is a new reader attached to the data bus. Messages will implicitly be queued. 

Attached reader:

* **read** - Response is list of available messages. Removes from subscriber's queue.

No filtering for now, but it's a feasible feature to add later. 

Data buses are a good basis for most inter-app communication, and have nice observability properties. Scalability is a concern, but can be mitigated by partitioning a system into smaller buses. 

*Note:* I'm currently rejecting shared state (too fragile) and publish-subscribe (too complicated). 

### Secure Random Numbers

Random numbers should be stable and cryptographically secure. By stability, I mean that a transaction that produces random numbers then *aborts* should produce the same random numbers on next attempt. This can be achieved by having the host maintain some hidden state associated with the application and every fork. 

Reference `random`

* **bytes:Count** - Response is a list with Count random bytes. Updates implicit CPRNG.

If programmers want a regular PRNG, they can model it themselves. The primary motive for effectful random source is integrating external entropy.

###  .... topics



SERVICES

* Service Discovery
* Help and Documentation
* Network
* Filesystem
* Tuple Spaces
* Publish-Subscribe
* Mailboxes? (how to secure? PKI? challenge?) 
* Quota Control
* Graphical User Interface

### Signal Handling


### System Services

A system can 


### Filesystem

Desired features: read and write files, browse the system, watch for changes. 

It seems these operations can mostly be idempotent, which simplifies resilience. Idempotent write would involve writing to an offset (not 'end').

Files used as channels might need separate attention.

### Network

Desired features: listen on TCP/UDP, connect to remote hosts, read/write connections. Listening should be resilient, and connections should gracefully die.

### Clock

Rather than continuously update a clock variable, we could arrange for some methods to trigger on the clock, update the clock only when a trigger occurs. The system should be able to see the trigger times in order to make good scheduling decisions.

### GUI

### Console

### Sound


## Scopes? A Feature Deferred. 

Use of try/commit/abort is implicitly a scope. It is feasible to augment scopes with a rule to restrict or redirect behavior of subprograms. For example, we could introduce a scope rule that adds a common prefix to every state reference.

        try:(scope:(..., st:[add-prefix:Path]))

This rule would apply until the corresponding commit/abort action. The same rule can be implemented manually by intercepting a subprogram's request-response channel, but there are potential performance benefits for making this visible to the environment.

However, whether this feature is actually useful will depend on the design patterns in our applications, whether references stored in state tend to be relative or absolute, etc.. I do not believe that I can anticipate the useful patterns without more experience. For now, I will avoid scope parameter and require manual implementation of scopes.





## Notebook Applications

A notebook application mixes code and UI, essentially a REPL that prints usable GUIs.

Glas is very suitable for this. We could arrange for each logical line to fork a subtask and maintain its own GUI frame. The tasks may freely interact. The logical lines themselves could be graphical projections of a program. 

## Web Apps and Wikis

Transaction machines should be a very good basis for web applications. We can tweak the external effects to focus on UI, client-side storage, and limited network access (XmlHttpRequest, WebSockets). With partial evaluation, we can compute the initial 'static' page.

I have this vision where every page of a Wiki is essentially a web app - usually a notebook application. A subset of pages might also represent server-side behavior. These applications could support exploration of data, little games, and other things. Updates to the web-app could be deployed immediately.

I'm not certain this vision is practical. But it would at least make an interesting sandbox for exploring Glas.


## Interprocess Communications

Take a step back: instead of one application, assume we have tens of applications. 





## Dynamic Scheduling

A system can track conflicts between repeating transactions. If two transactions frequently conflict, they can be heuristically scheduled to run at different times. Even when transactions do conflict, fairness can be guaranteed based on tracking conflict history.

Hard real-time systems can feasibly be supported by introducing a schedule of high-priority transactions that will 'win' any conflict resolution if it commits within its quota.

## Static GUI

We can evaluate an application from its initial state without attaching IO. This can be useful to improve startup speeds, perhaps to compile some static views.

Intriguingly, is also feasible to attach just the GUI without any other effect runtime behavior. This could simplify refinement and development of the initial state.

## Deployment Models?

The state for a large application could feasibly be distributed across many machines. But it might be easier to model deployment of several smaller transaction machines, and bind certain states between them. Perhaps the IO between components can be based on bounded-buffer queues or CRDTs. 

This seems an area worth exploring. But it might well become another 'layer' of models.



## Security

### Sandboxing Subprograms

A subprogram can easily be scoped to a region of the environment via scoped `try` or explicit rewriting of paths. The latter option, explicit rewriting, has worse performance but greater flexibility: the program can redirect paths to logically 'mount' different parts of the environment. 

### Protecting Invariants

A transaction can easily check invariants before running, and check again before committing. This benefits from hierarchical transactions.

