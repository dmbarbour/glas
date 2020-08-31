# Glas Applications

In my vision of software systems, applications are easy for those users to comprehend, control, compose, extend, modify, and share - especially at runtime. Further, applications should be robust and resilient, degrade gracefully, recover swiftly from disruption.

Glas programs are based on Kahn Process Networks (KPNs), which are excellent for modeling deterministic, interactive computations at very large scales. However, runtime composition, extension, modification, and disruption tends to be non-deterministic. Further, it can be difficult to observe or extend state captured within a process network, or for modifications to robustly transfer state to a new process network.

The *Transaction Machine*, described below, is a better fit for my vision of an application model. Behavior and state are cleanly separated, which simplifies extension and modification. Non-determinism and tolerance and resilience to disruption are built-in. Transaction machines are a good fit for open systems. And Glas programs are a convenient fit for expression of the behavior.

This document describes the transaction machine, an application model, integration, and a potential vision for the system.

## Transaction Machine

A transaction machine forever repeats a deterministic transaction over a non-deterministic environment. The environment of the transaction machine will generally be partitioned between input/output regions and a private scratch space. IO may involve shared variables or abstract queues.

Process control is implicit. A deterministic transaction that changes nothing will always be unproductive when repeated on the same input. Computation can implicitly wait for updates to input. If input is done, the computation is effectively terminated.

Large transactions can be partitioned into small ones by a 'fork' request with an array of options. Logically, the environment responds with an option non-deterministically. However, in context of rollback and replication optimizations, we effectively fork concurrent machines.

### Required Optimizations

For transaction machines to be viable, two optimizations are necessary: rollback and replication.

Rollback enables reuse of a computation for a relatively stable input prefix. Without rollback, transactions must be recomputed from the start. Efficient rollback requires compiler support.

Replication supports concurrent computation of forks. For isolated transactions, repetition and replication are logically equivalent. However, replication reduces latency for reacting to changes.

## Behavior

The application will be represented by a Glas program.

### Transactions

Transactions will be supported via request-response channel. A viable request API:

* **commit:Status | abort:Reason** - Accept or reject state. Response is unit. No further requests accepted by transaction.
* **try** - Begin hierarchical transaction. Response is unit. Transaction terminates with matching commit or abort. 
* **fork:Keys** - Replicate current transaction. Response to each replica is different key from dictionary. The fork path, a list of keys, provides a stable name for debugging.
* **note:Annotation** - Response is unit. Intended for optimization and scheduling hints, checkpoints, debugger support, etc.. The host may ignore these requests without breaking behavior.
* **apply:(object:Reference, method:Method, checked?)** - Query or update. References are provided by host. Methods are computed by the program, but must be meaningful to the host. Response type depends on arguments. Variation:
 * *checked* - Flag. If set, recover from runtime errors: response is wrapped with `ok:SuccessResponse | error`. 
 
Runtime errors normally abort entire transaction without response, but checked apply can catch a useful subset of runtime errors, e.g. unrecognized methods, and runtime type errors. Success or is atomic.

The 'try' request can support conditional backtracking and pseudo-exceptions within the program, and can usefully be composed with 'checked' apply. I intend to leverage this for a `try/then/else` syntactic construct.

### Environment

The application program receives a top-level environment as parameter via data port `env`. The environment is represented by a dictionary of references. Keys might include `(scratch, state, chan, app, cli, time, network, filesys, random, gui, ...)`. 

Behavior associated with each symbol should be standardized. An application can examine the environment value and adjust behavior based on available features. Use of 'checked' apply can also be leveraged for feature discovery, but is not primarily intended for that purpose. 

### References

To simplify debugging and some extensions, Glas applications will represent references as `(ref:HostValue, ...)`. The host ignores every field except for `ref`, but the other fields would be useful for debug names and ad-hoc metadata.

References are specific to the current host-app session. The host should provide lightweight  protections, e.g. randomized numbers, indirect lookup via session hashtable. Applications should also protect references, e.g. using abstract types.

*Note:* References must be allocated. To reduce avoid contention on the allocator, the host could create pre-allocated pools for each fork, or recycle allocations from aborted transactions.

### Asynchronous IO

In the general case, a transaction must commit before external interactions begin. Synchronous IO with external systems is impossible. Of course, there are exceptions for manipulating local state or a few fire-and-forget operations.

To support asynchronous IO, the host will respond to many requests with a reference to a new object representing the future interaction. Through this reference, the application can add outputs, check for inputs, or explicitly terminate the interaction.

## State

Environment `(scratch:StateRef, state:StateRef, ...)`.

A StateRef will carry one Glas value. Scratch is ephemeral, reset to unit on host-app session start. State is durable, initially set to a value supplied with the application (or unit) then preserved across resets. 

State is private by default. Relevantly, an application is free to update its data model, or to erase data it doesn't need without concern for the needs of other applications.

### Generic Methods

Useful methods for any StateRef:

* **get** - Query. Response is whole value.
* **set:Value** - Update. Assigns value. Response is unit
* **eq:Value** - Query. Compare values for equality. Response is Boolean.
* **path:Path** - Query. Response is path-specific StateRef.
* **type** - Query. Response is `dict | list | number`.

A path is a simple list of symbols and numbers as a path, e.g. `[foo, 42, baz]`. A symbol indexes a dictionary, while a number indexes a list. It is possible to create a path reference that is currently invalid due to state; use of an invalid path is treated as a runtime error (can be caught with 'checked' apply).

### Dictionary Methods

Dictionaries are mostly used as namespaces, via pathing. However, it is feasible to reflect on dictionary structure or to add and remove just parts of a dictionary. 

* **d:keys:(in?Keys, ex?Keys)** - Query. Repond with Keys. Optional parameters:
 * *in* - restrict response by intersection (whitelist)
 * *ex* - restrict response by difference (blacklist). 
* **d:insert:Dict** - Update. Insert or update keys in dictionary. Response is dictionary containing replaced elements. 
* **d:select:(in?Keys, ex?Keys)** - Query. Response is subset of dictionary for given keys. The *in* and *ex* parameters are same as for `d:keys`.
* **d:remove:(Selection)** - Update. Remove elements that would be selected by same parameters to `d:select`.
* **d:extract:(Selection)** - Query-Update. Same as select, but also remove selection.

Keys is represented by a dictionary with unit values. Specifying both *in* and *ex* is a runtime error. Use of paths through dictionaries can support more precise conflict detection compared to 'select'. Use of dictionary methods on a non-dictionary value is a runtime error.

### List Methods

Lists are useful for modeling shared queues. Multiple writers and a single reader can commit concurrently under reasonable constraints. But do consider *Channels*!

* **l:length** - Query. Response is number of items in list.
* **l:lencmp:Threshold** - Query. Same as `n:compare` on length.
* **l:insert:(items:\[List, Of, Values\], tail?, skip?Number)** - Update. Addends items to head of list. Response is unit. Optional flags:
 * *tail* - Add items to tail of list instead.
 * *skip* - Default zero. Skip over items before writing. A skip larger than list size is a runtime error.
* **l:select:(count?Number, atomic?, tail?, skip?Number)** - Query. Response is a sublist, usually the head element. Optional flags:
 * *count* - Default one. Return this many items if available, fewer if insufficient.
 * *atomic* - Copy exactly count items, or it's a runtime error (see 'checked' apply).
 * *tail* - Select Count items backwards from tail of list instead. List order is preserved.
 * *skip* - Default zero. Exclude skipped items before selection. A skip larger than list size results in empty list.
* **l:remove:(Selection)** - Update. Removes elements that would be selected by same parameters to `l:select`. Response is unit.
* **l:extract:(Selection)** - Query-Update. Same as select, but also removes selection.

Use of paths through lists can support more precise conflict detection compared to 'select'. Use of list methods on a non-list value is a runtime error.

### Number Methods

For most numbers, get/set is sufficient. However, *counters* can be high-contention, and we can also try to stabilize observation of numbers with thresholds.

* **n:increment:(count?Number)** - Update. Default count is 1. Increase referenced number by count. Response is unit.
* **n:decrement:(count?Number)** - Update. Default count is 1. Decrease referenced number by count. Response is unit. Cannot decrement below zero - if count is larger than number, this is a runtime error (but see 'checked' apply).
* **n:compare:Threshold** - Query. Compares number to threshold. Response is `lt | eq | gt` corresponding to number being less than, equal to, or greater than the threshold.

Use of number methods on a non-number value is a runtime error.

### Data Model Versioning

In context of orthogonal persistence or live coding, application state may need to be reorganized to handle changes in the program. To achieve this, the state may include version identifiers. The application can check the version and apply a version-transformation function if needed.

For very large state, it is often preferable to perform this update lazily. This is possible if version identifiers are distributed through the state, e.g. by modeling a versioned data model as a composition of other versioned data models.

## Channels

Channels are a general purpose solution to asynchronous dataflow patterns, able to flexibly model broadcasts, mailboxes, data buses, queues, variables, and futures. As follows:

* *broadcast* - one writer, copy reader
* *mailbox* - one reader, dup writer
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

### Writer Methods

* **write:\[List, Of, Values\]** - Update. Add values to end of channel. Response is unit. Runtime error if there is insufficient available capacity, unless the channel is also 'lossy'.

## External Effects

### Application Control

Environment `(app:Ref, ...)`.

* **reset** - Fire and forget. Response is unit. After commit, halt the current host-app session then start a new one. This will close channels, invalidate host references, reset scratch to unit, etc.. 
* **halt** - Same as reset, except does not start a new host-app session. The application can be activated again through the host.

### Command Line Interface

Environment `(cli:Ref, ...)`.

Glas applications can accept concurrent command-line connections. Each connection has its own stdin, stdout, stderr, command line arguments, OS environment variables, etc.. This design simplifies interaction with persistent state - application instances as objects, commands as methods. 

* **accept:(limit?Number, req?Keys, opt?Keys)** - Response is list of available commands, usually one or zero. Options:
 * *limit* - Default one. If defined, is upper limit for number of commands. 
 * *req* - Required interfaces. Default is `(env, cmd, stdin, stdout, stderr)`. 
 * *opt* - Optional interfaces. Default is empty set, unit `()`.

Each command is represented by a dictionary of values and references, such as `(env:(PATH:"/usr/local/bin:...", ...), cmd:["./appname", "help"], stdin:Ref, stdout:Ref, stderr:Ref)`. 

The stdin, stdout, and stderr elements are references to endpoints of binary channels, with stdin as a reader endpoint and stdout/stderr as writer endpoints. These bind a command shell in the conventional manner. 

References that are neither required nor optional will implicitly be closed, or never created. For example, `accept:(req:(cmd, stdout))` would implicitly close stdin and stderr, and may skip conversion of env to a Glas dict. This simplifies safe use of potential extensions to the command model.

*Note:* We can still compile an application to accept only one command per halt/reset.

### Secure Random Numbers

Environment `(random:Ref, ...)`.

* **acquire:Count** - Response is a future (a single-use channel), which will receive a cryptographically random binary of size Count after commit.

This design supports effectful implementations for obtaining initial entropy. The expected use case is that applications acquire random numbers to seed a CPRNG on a per-task basis. This would avoid contention on CPRNG state.

### Time

Environment `(time:Ref, ...)`.

* **acquire** - Response is future (a single-use channel) that will receive commit time.
* **after:Time** - Response is unit. Constraint. Runtime error before specified time.
* **before:Time** - Response is unit. Constraint. Runtime error after specified time.
* **model** - Query. Response is the model for Time values used by the host. 

Transactions are logically instantaneous, but can't usually be pinned down to a specific instant before commit. Thus, acquiring the transaction's time is supported by a future. Transactions may voluntarily fail if they would commit before or after a specified time. Use of 'after' can support timeout behavior, while use of 'before' can express real-time behavior.

The standard model for Time values will be `nt`, referring to Microsoft Windows NT time epoch: the number of 0.1 microsecond intervals since midnight, January 1, 1601, UTC. This should be sufficient for most systems until we develop time travel or space travel.

### Globs and Blobs



### Filesystem

### Network Interface

Environment `(network:Ref, ...)`.

Support for TCP/IP enables Glas applications to behave as a webserver and interact with many external devices and systems. 

## GUI? Defer.

I'm still uncertain what concept I want for GUI. 

Ideally, a GUI should directly reflect the external surface of the application, or at least one such surface, such that users can easily 'compose' GUI elements, and also peek under the hood to observe how they are maintained.

Direct manipulation of application state and channels is possible.



Direct manipulation and rendering of the application object is a viable option. But 

 But in that case, should we bind to state, to channels, or to an explicit 'surface' model with its own variables and channels?






I envision an application as providing a 'surface' for interactions. The command line interface, filesystem, and network are part of this surface, intercepted by the host. Other parts might be rendered for a user. Of course, we could also supply multiple GUIs for multiple users or multiple views.





An application object could provide a GUI 'surface'. Network and command-line


For now, focus on webservers and web-applications. Web applications might need a GUI based on a document object model.

### System Discovery

Applications could feasibly publish some mailboxes for use by other applications. However, this probably won't be very relevant until we start looking into deployment models.



###  .... topics

* System Discovery - other apps?
* Network
* Filesystem
* Graphical User Interface
* Signal Handling
* Quota Control

### Signal Handling


### Filesystem

Desired features: read and write files, browse the system, watch for changes. 

It seems these operations can mostly be idempotent, which simplifies resilience. Idempotent write would involve writing to an offset (not 'end').

Files used as channels might need separate attention.

### Network

Desired features: listen on TCP/UDP, connect to remote hosts, read/write connections. Listening should be resilient, and connections should gracefully die.

### GUI

### Console

### Sound

## Data Bus and Publish-Subscribe? Defer.

I have an intuition that data bus and publish-subscribe are useful developing resilient and extensible applications, as a composition and communications model.

Data bus is a simple communications architecture: A bus may have a dynamic number of attached readers and writers. A message written to the bus is observed by all currently attached readers. With software, we can support fine-grained buses that model broadcast channels (one writer, many readers) or mailboxes (many writers, one reader). 

Publish-subscribe is a useful communications pattern: Publishers maintain up-to-date views of state via shared channels. Subscribers can filter for data and observe changes. Usefully, subscribers can publish their interests, allowing publishers to maintain relevant data.

One idea is to extend data bus readers and writers with some publish-subscribe features, i.e. to logically simulate publish-subscribe over data bus.

Data bus and publish-subscribe channels have no memory. This simplifies design of resilient systems, reducing need for explicit cleanup. The models are also both accessible, extensible with new observers and behaviors.

It seems worth further developing these models for resilient Glas systems.  However, it's also low priority in the short term.


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
