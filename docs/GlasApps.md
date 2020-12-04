# Glas Applications

In my vision of software systems, applications are easy for users to comprehend, control, compose, extend, modify, and share - especially at runtime. Further, applications should be robust and resilient, degrade gracefully, recover swiftly from disruption.

Conventional models of applications usually lack these properties. They embed too much state within the program, which hinders replacing the program. Conventional use of references also makes it difficult to track relationships. 

This document will explore options, especially *Transaction Machines*.

## Conventional Procedural Apps

Glas programs can model procedural applications by writing `io:request` and reading `io:response` with values representing effects and results. The program would have internal state and parallelism. External concurrency is feasible with asynchronous results. 

A request-response API can be tuned for context. For example, web-applications would make requests related to the Document Object Model and XML Http Request. Usefully, we can easily interpret a request-response stream within a larger application, simplifying adapters and testing.

A challenge with request-response bindings is ensuring performance. Optimal performance requires static loop fusion and partial evaluation, i.e. such that the effects can be directly integrated with code. We could also benefit from supporting first-class code within our effects, e.g. installing new operations.

## Reference Model

For Glas, I propose references have form `(ref:AbstractValue, ...)`. 

The reference header is a row-polymorphic dict containing a 'ref' field, enhancing extensibility relative to purely abstract values. The reference body is an abstract value to support precise garbage collection and prevent construction of references. Abstraction can be enforced statically or dynamically.

An initial environment of references can be provided to a Glas program via `io:env` parameter. The program can control effects by controlling which references are provided to subprograms. Depending on syntax, the environment parameter can be propagated implicitly. It is feasible to control a subprogram's effects by controlling which references are provided.

*Aside:* An alternative to references is second-class cursors, e.g. like turtle graphics. However, references are more conventional and immediately useful.

## Transaction Machines

A transaction machine is a software architecture consisting of repeatedly applying transactions to an external environment. Transactions are scheduled non-deterministically. 

Process control is implicit: When a deterministic transaction is unproductive, the system can implicitly wait for a change in inputs before repeating the computation. Programmers can explicitly abort transactions to wait for ad-hoc conditions.

Non-determinism and hierarchical partitioning are expressed via a fork operation. Fork selects a value non-deterministically from a set. In context of isolated transactions, repetition and replication are logically equivalent. Thus, stable forks can be optimized via replication, dividing one transaction into many.

Incremental computing is the basis for performance. The compiler and system can coordinate to reuse stable subcomputations across repetitions of a transaction. This improves latency and efficiency, especially in context of stable forks.

External effects can be continuous or asynchronous: a transaction must commit a task before the external system can process it, so any result is deferred until a future transaction.

### Transaction Model

Procedural programming is suitable for representing deterministic transactions. With Glas programs, we can represent procedures via request-response channel, with requests implicitly applying to a transaction. A viable request API:

* **commit:Result | abort:Cause** - Accept or reject state. Response is unit. No further requests are read from current transaction. The commit result or abort cause can serve as a return value for the transaction.
* **try** - Begin hierarchical transaction. Response is unit. Hierarchical transactions must be terminated with matching commit or abort request. This is also useful to model safe exception handling.
* **fork:\[Set, Of, Values\]** - Response is non-deterministic value from set. System should replicate stable forks to run as concurrent threads. For unstable forks, we might try different values until one commits.
* **note:Annotation** - Response is unit. Annotations should not have any semantic effect, but can support performance or debugging, e.g. scheduling hints, rollback checkpoint hints, call stacks, debug logs, breakpoints. Annotations use open variant type with de-facto standardization.
* **apply:(object:Reference, method:Value)** - External effects. Response is a dictionary or variant (for extensibility) whose type depends on object and method.

If a request would cause a type error, the transaction implicitly aborts for debugging. Thus, it's necessary to know in advance which methods a given object reference will support. Static type analysis can help with this.

## Environment Model

Desiderata for environment model: 

* apps can hierarchically host other apps
* modular boundaries between subprograms
* extensible, can add features or properties
* performance is efficient and predictable
* simple, easy to understand and render
* asynchronous effects are easy to express
* avoid first-class functions, dynamic code

Under these constraints, we could use channels for asynchronous communication between modular components. Variables are convenient for private state across transactions. Variables could be understood as a special case of channels, but it's simple to support them explicitly.

Other than channels and variables, I think we don't need much else.

## Timing Control

The time associated with an isolated transaction is instant of commit. Thus, it does not make sense for a transaction to 'sleep' for a duration like an imperative thread. However, it is feasible to control or constrain time of commit.

In context of transaction machines, this might be expressed by comparing time to a threshold, without directly observing the time. Then, in case the transaction aborts, the scheduler would know which time values to try next. Intriguingly, transactions can be computed ahead of time, and it is feasible to compute multiple transactions in advance insofar as we're willing to accept risk of rollback.

Time could be accessed via cursor to a clock. This allows for modeling multiple clocks, logically delaying subprograms, and controlling time dependency. It is also feasible to construct cursors to access the same time with different models, e.g. seconds since 1970 vs. 0.1 microsecond intervals since 1601.



## Graph-based Environment Model



An application will manipulate an environment through cursors. Desired features:

* **modular** - the environment has natural barriers for implementation-hiding; programs can leverage barriers between subprograms.
* **fractal** - a program can easily sandbox a subprogram at the environment layer, i.e. instead of intercepting the request-response stream.
* **sharing** - it is not difficult to partition work or share results between subprograms.
* **renderable** - the environment can easily be displayed for debugging or administration.

A relatively uniform cyclic graph structure seems useful, covering the latter three features. For modularity, we can design special edges that cannot be navigated by cursor, e.g. to model read-only or write-only access to variables and channels.

Potential Components:

* **values** - a node could carry a Glas value for arbitrary purposes. The reason to model values within the graph is generalization of code to work with things other than values.
* **channels** - we could model channels as providing a reader and writer endpoints, which could feasibly be duplicated. Bounded-buffer channels are possible, with pushback or lossy behavior. Variables could also be modeled as channels. 
* **request-future-response** - a special channel mode could provide an immediate 'future' response for each request. Indeed, all channels could use this pattern, 



## Application Private State

In general, applications should have part of the environment dedicated for their own personal use, no risk of interference from the system or other applications (modulo debuggers). In case of forking transactions, the program should be easily able to also fork the private space.

## Random Numbers or Entropy

Random numbers should be modeled as asynchronous, i.e. request random numbers now then receive them later. Otherwise, when the transaction aborts, it is unclear whether it should be recomputed with a new random number. However, programmers should also be encouraged to model their own PRNGs.

## Cache-Query Pattern? Defer.

There is a subset of effects where consequences are insignificant and results are cacheable. Use of HTTP GET is a very good example. In these cases, it could be convenient to model the query as synchronous: the transaction is implicitly delayed while the cache is updated.

However, HTTP GET is modeled above sockets. It is not locally obvious that use of a socket API has insignificant consequences. It's unclear how we should go about modeling cache-query such that another transaction could implement the sockets without vi. It is feasible to treat this as a specialized form of system reflection.

For now, I'll abandon this feature. It's worth reconsidering later.

## Graph-based Environment Model

Contemplating my options. Transaction machines can work with almost any asynchronous environment, but not all environment models are a good fit for my vision of software systems.

### Reference-based API? No

A reference is a value that identifies a separate object. Reference-based APIs are convenient and conventional, cf. file-descriptors. 

Unfortunately, references are an awkward fit for my vision of software systems. References hinder local reasoning, stability, observability. References entangle application and system state in fragile ways, which complicates persistence, distribution, sharing. Also, manual management of referenced resources is fragile and prone to error, while automatic management requires too much API knowledge and runtime support.

### Environment of Objects? Needs work.

Most problems with references are due to mixing references with values. Instead, a program could separate values and objects into distinct operational stacks, of sort. A request could implicitly receive and return a record of references via the environment, instead of mixing objects into the normal request or response value.

Because objects are separated, we can easily track how the object is used, shared, or dropped. There is no opportunity to accidentally send a reference over a network interface or store it into a database. Automatic garbage collection is quite feasible.

I think that ad-hoc objects and methods might be a little too arbitrary. There isn't a convenient way to compose, integrate, or substitute arbitrary objects. 

### Graph-based API?

Instead of an environment with arbitrary objects, a transaction could operate on a uniform abstract graph with directed, labeled edges and unlabeled vertices. Logically, we can represent values using graph structure, and also optimize representation of numbers and lists.

The program will start with a cursor into the graph. For transaction machines, this starting location is stable for each repetition of the transaction, but a program can navigate and establish multiple cursors. Navigating a graph can use non-deterministic models when we have multiple edges with identical labels, i.e. a cursor is generally a set of vertices.

We can connect vertices by edges, but it's also feasible to merge vertices. 

To avoid mixing cursors with values, cursors can only be manipulated indirectly through an intermedate environment. This would be awkward in different ways from working with references.

To model an effect, we can represent a request within the graph, and the request would include an edge for outputting the result, which could be unified with a designated target location. Then, we link this request into the system's graph. 

The main issue here is that we cannot represent any form of commitment to the request, e.g. if we modify the request after handing it to the system then it's unclear whether the system is still trying to handle the original version. Also, it's difficult to enforce system invariants or interface boundaries.

### Session-based API?

Perhaps a graph itself isn't the right model, but something closer to bundles of wires, replacing unification with bulk 'attach', could work nicely and better represent interactions at system boundaries. This would also be more consistent with KPNs, I think.




 Part of the graph would be designated for use by the host, and could be protected via types or by special edges that cannot be 

In this case, external effects would be achieved by designating a certain regions of the graph to the host system, to interpret requests and write results. Or more accurately, the application would have a private space, with access to a public space.

The benefit of graph structure is effective structure sharing, work sharing, and efficient communication.


We could model the environment as an abstract graph that the program can navigate and manipulate. For navigation, the program will also have cursors into the graph. To avoid use of references, these cursors are never represented as values, but can be manipulated via special stack.

A transaction would manipulate the graph then commit. External effects could be expressed by operating in certain locations of the graph.






 with directed, labeled edges and unlabeled vertices. The graph can store normal values, logically as  as a specialized 

The program can navigate this graph to perform ad-hoc manipulations. (No rewrite 'rules' per s)


I can model the environment as an abstract graph with directed, labeled edges and unlabeled vertices. The program can navigate the graph and perform various manipulations, including unification of vertices. Unification will propagate along edges with identical labels.

I



The program has a cursor into the graph, or perhaps a stack of cursors.

 program has cursors into the graph, but cannot directly manipulate them

The program can manipulate this graph in flexible ways, including unification of vertices. 





We could model transactions as rewriting an abstract graph. To distinguish from reference-based models, we can  with anonymous vertices and labeled edges. This would avoid references to vertices. Instead, we can locate vertices by navigating a graph from an implicit start.



 e.g. unifying v

 adding edges between points, 

The application would have an implicit starting locat

Tree-structured data has nice properties for caching, but it's a little awkward for modeling interactive systems, work sharing, and structure sharing. 

Tree-structured data can be useful, but 

*Aside:* Before I selected Kahn Process Networks as the basis for Glas programs, I was considering a monotonic graph unification model, which is similar to the above.

### Hierarchical Sessions?

Sessions reify long-running interactions between systems. Each participant in a session (usually just two) will have some associated private state.

 session will have some associated private state for each participant, some public variables.
have some state 

Sessions might feature




### Session-based API?

A session reifies an abstract interaction between application and environment. The session could include assigned variables, channels, active tasks, and other elements. Similar to reference-based models, we might identify elements within a session, but these references would be explicitly session-local and we can always observe the entire session.

 active tasks, and other features. Unlike reference-based 

A session consists of: private state variables, input and output variables, input and output channels. It can be organized into hierarchical sub-sessions.

However, sessions are 


We could model transaction machines as operating on a dynamic session between the application and environment. This makes explicit that we aren't manipulating an environment directly, but rather managing a public interface between application and environment.





A session models a bi-directional interaction between two or more participants. The session could have some associated private state and a public interface. Sessions can be hierarchical, with subsessions to model specialized interactions.

A session models an interaction with two or more participants. Each participant could associate some private state with the session,


 or initiate subsessions with other participants.






## Variables

A variable is an object that stores a value. This value can be accessed and modified via 'get' and 'set' methods. A transaction will usually read and write several variables. An application's private state can be represented with a root variable, `(state:Ref, ...)`, initially unit.

* **get** - Response is `ok:Value`. Reads the value of a variable.  
* **set:Value** - Response is `ok`. Writes the value of a variable. This value is in the response to future 'get' requests.

To avoid read-write conflicts, we'll partition state into variables such that the root is relatively stable, and concurrent transactions operate on subsets of variables that are shared in controlled ways. An allocator could be provided as `(var:Ref, ...)`, with one method:

* **new:Value** - Response is `ok:Ref`, with a reference to a new variable object. The variable will initially contain the provided value.

For performance reasons, we might want specialized variables to work with binaries, key-value trees, and other compact or indexed representations. However, in context of extension or update, specializing variables based on predicted data type is awkward. 

## Channels



## Futures

A future can be modeled effectively as the read-end of a write-once channel. 

## Time

## Tables

It is feasible to model relational database queries as normal requests within a transaction. However, I'm currently inclined to model this explicitly (within Glas) rather than as an implicit feature of the host system.

By leveraging stowage and memoizing indices, it should be feasible to achieve a high degree of performance. 

## Time

Environment `(time:Ref, ...)`.

* **now** - Response is future whose value will receive commit time. 
* **cmp:Time** - Compare to a specified time. Response is `ok:(lt | ge)`, unless the time model is not recognized, in which case an error response is provided.
* **model** - Query. Response is a value, usually `nt`, representing host model of time.

Transactions are logically instantaneous, applying at instant of commit. Acquiring transaction time is supported by a future. Specifying an 'after' constraint can serve a similar role as synchronous 'sleep'. A 'before' method can represent real-time scheduling constraints.

The standard model for Time values is `nt`, referring to Microsoft Windows NT time epoch: a number of 0.1 microsecond intervals since 0h 1-Jan, 1601 (UTC). This will be sufficient for most systems. This provides space for extensions.

### Data Model Versioning

In context of orthogonal persistence or live coding, application state may need to be reorganized to handle changes in the program. To achieve this, the state may include version identifiers. The application can check the version and apply a version-transformation function if needed.

For very large state, it is often preferable to perform this update lazily. This is possible if version identifiers are distributed through the state, e.g. by modeling a versioned data model as a composition of other versioned data models.


## Channels

Channels are a general purpose solution to asynchronous dataflow patterns, able to flexibly model broadcasts, mailboxes, data buses, queues, variables, and futures. As follows:

* *broadcast* - single writer, copy reader
* *mailbox* - single reader, dup writer
* *data bus* - copy reader, dup writer
* *queue* - dup reader/writer, large capacity
* *variable* - dup reader/writer, capacity one
* *future* - copy reader, capacity one, single use

Channels in Glas are divided into reader and writer endpoints. The reader endpoint can be copied to replicate data. Reader and writer endpoints are closed independently. Readers can detect when all writers have closed, and vice versa.

Channels have a buffer limit to ensure fast writers will eventually wait on slow readers and control memory consumption. This limit could be very large For copied readers, the slowest reader sets the pace.

### Application Channels

Environment `(chan:ChannelFactory, ...)`.

* **create:(cap?Number, lossy?, binary?)** - Response is a fresh `(reader:Reader, writer:Writer)` pair of references. Optional parameters:
 * *cap* - Default one. Limit on number of values in channel. Zero capacity channels are possible, usable to signal 'closed' status.
 * *lossy* - Flag. If set, instead of blocking the writer, a slow reader will lose the oldest unread data. Loss can be detected by reader. Effective use requires specialized protocols.
 * *binary* - Flag. If set, restricts channel to binary data (natural numbers, 0 to 255). Useful for performance and simulating external IO. 

It is feasible to model channels manually, within state. However, primitive channels are useful for modeling effects, and for composition it's convenient to use the same models within the application as we use externally.

Alternative loss models could be interesting, e.g. based on exponential decay patterns. However, I'd rather not embed any advanced logic into these models.

*Aside:* In addition to creating channels, it might be useful to provide some composition methods, similar to 'epoll' in Linux. However, the current intention is to use lightweight forks in a more direct style. I'll consider host-supported event polling based on level of success with forks.

### Channel Methods

These methods are shared by reader and writer.

* **dup** - Response is channel. The dup and original are interchangeable except that they must be closed independently. 
* **length** - Query. Response is current number of items in channel. This value is between 0 and cap, inclusive.
* **lencmp:Threshold** - Query. Response is `lt | eq | gt`. Same as `n:compare` on length.
* **cap** - Query. Response is configured capacity of channel. 
* **close** - Destructor. Response is unit. Further use of reference is error. 
* **active** - Query. Response is Boolean, `false` iff all writers for channel are closed.
* **relevant** - Query. Response is Boolean, `false` iff all readers for channel are closed.

### Reader Methods

* **read:(count?Number, atomic?)** - Response is a list of values, removed from channel. Options:
 * *count* - Default one. Read the maximum of count and length items.
 * *atomic* - Reading fewer than count items is treated as runtime error.
* **lossy** - Query. Response is Boolean, `true` iff values were lost since last read.
* **copy** - Response is a new reader that gets its own copy of all present and future data written to the channel. Buffering is controlled by the slowest reader.

A reader channel essentially has a 'lossy' flag, removed by read and set by writes that would overflow capacity of a lossy channel.

*Note:* It is feasible for a copy of a reader to extend its capacity or mark lossy, but I'm uncertain whether this is a good feature or not.

### Writer Methods

* **write:\[List, Of, Values\]** - Update. Add values to end of channel. Response is unit. Runtime error if there is insufficient available capacity, unless the channel is also 'lossy'.

## Graphical User Interface

The conventional GUI model is a terrible fit for my vision of software systems. The bindings between the GUI and application state are indirect, difficult to comprehend or modify. Composition is not reliably supported at all. There is no way to extend the GUI without invasive modification of code.

For Glas, I'm contemplating an alternative model based on editable projections and interaction grammars. Discussion in [Glas GUI](GlasGUI.md).

## External Effects

A transaction cannot perform synchronous IO with external systems. The transaction must commit before external interactions begin. Thus, most feedback must be provided through futures or channels. Exceptions for fire-and-forget.

### Secure Random Numbers

Environment `(random:Ref, ...)`.

* **acquire:Count** - Response is a future (a single-use channel), which will receive a cryptographically random binary of size Count after commit.

This design supports effectful implementations for obtaining initial entropy. The expected use case is that applications acquire random numbers to seed a CPRNG on a per-task basis. This would avoid contention on CPRNG state.

### Globs and Blobs

Some mechanism to tap into secure-hash resources, e.g. to support lazy serialization of large values across a network.

### Filesystem

Environment `(filesys:Ref, ...)`.

We might need abstract binary references to support seek, etc.. I'd also like to watch the filesystem.

### Network Interface

Environment `(network:Ref, ...)`.

Support for TCP and UDP network connections is perhaps the most immediately useful feature for Glas applications. Binary channels can usefully model TCP connections.

* **tcp:(port?Port, addr?Addr)** - Response is a future `(port, recv)` pair. The 'recv' element is a channel of incoming `(port, addr, in, out)` connections. 

I'm uncertain whether DNS should be separated.

* **dnsaddr:(name:Name, ...)** - Begin asynchronous DNS lookup. Response is a future list of `(addr:Addr)` elements. 
* **dnsname:(addr:Addr, ...)** - Begin asynchronous reverse DNS lookup. Response is a future `(name:Name)`

A search for DNS address

### Command Line Interface

Environment `(cli:Ref, ...)`.

NOTE: I'm uncertain whether CLI should be treated as a special case of GUI, e.g. with a special constraint name such as `tty`. This might offer a much nicer approach to CLI, in general.

Glas applications can accept concurrent command line connections. Each connection should have its own stdin, stdout, stderr, command line arguments, OS environment variables, etc.. This design simplifies interaction with persistent state - application instances as objects, commands as methods. A compiler can produce conventional apps that accept CLI only once.

* **accept:(limit?Number, req?Keys, opt?Keys)** - Response is list of available commands, usually one or zero. Options:
 * *limit* - Default one. If defined, is upper limit for number of commands. 
 * *req* - Required interfaces. Default is `(env, cmd, stdin, stdout, stderr)`. 
 * *opt* - Optional interfaces. Default is empty set, unit `()`.

Each command is represented by a dictionary of values and references, such as `(env:(PATH:"/usr/local/bin:...", ...), cmd:["./appname", "help"], stdin:Ref, stdout:Ref, stderr:Ref)`. 

The stdin, stdout, and stderr elements are references to endpoints of binary channels, with stdin as a reader endpoint and stdout/stderr as writer endpoints. These bind a command shell in the conventional manner.

Interfaces that are neither required nor optional are either implicitly closed or never created. For example, `accept:(req:(cmd, stdout))` would implicitly close stdin and stderr, and may skip conversion of env to a Glas dict. This helps control reference leaks for extensions.

#### Terminal Interface

Rich command line interface, similar to ncurses applications, requires several features: raw IO, control over echo, interception of Ctrl+C and Ctrl+Z, detection of interactive input mode (cf. isatty()), detection of terminal lines and columns (height and width), window title, etc..

To access and control these properties, I might introduce a 'terminal' interface extension to the command line interface.



### System Discovery

Applications could feasibly publish some mailboxes for use by other applications. However, this probably won't be very relevant until we start looking into deployment models.



###  .... topics

* System Discovery - other apps?
* Network
* Filesystem
* Graphical User Interface
* Signal Handling
* Quota Control

## Live Coding

It is feasible to update application behavior while it is running, or between restarts, depending on how well it has been designed for the data model update.





## Notebook Applications

A notebook application mixes code and UI, essentially a REPL that prints usable GUIs.

Glas is very suitable for this. We could arrange for each logical line to fork a subtask and maintain its own GUI frame. The tasks may freely interact. The logical lines themselves could be graphical projections of a program. 

## Web Apps and Wikis

Transaction machines should be a very good basis for web applications. We can tweak the external effects to focus on UI, client-side storage, and limited network access (XmlHttpRequest, WebSockets). With partial evaluation, we can compute the initial 'static' page.

I have this vision where every page of a Wiki is essentially a web app - usually a notebook application. A subset of pages might also represent server-side behavior. These applications could support exploration of data, little games, and other things. Updates to the web-app could be deployed immediately.

I'm not certain this vision is practical. But it would at least make an interesting sandbox for exploring Glas.

## Dynamic Scheduling

A system can track conflicts between repeating transactions. If two transactions frequently conflict, they can be heuristically scheduled to run at different times. Even when transactions do conflict, fairness can be guaranteed based on tracking conflict history.

Hard real-time systems can feasibly be supported by introducing a schedule of high-priority transactions that will 'win' any conflict resolution if it commits within its quota.

## Static GUI

We can evaluate an application from its initial state without attaching IO. This can be useful to improve startup speeds, perhaps to compile some static views.

Intriguingly, is also feasible to attach just the GUI without any other effect runtime behavior. This could simplify refinement and development of the initial state.

## Deployment Models?

The state for a large application could feasibly be distributed across many machines. But it might be easier to model deployment of several smaller transaction machines, and bind certain states between them. Perhaps the IO between components can be based on bounded-buffer queues or CRDTs. 

This seems an area worth exploring. But it might well become another 'layer' of models.
