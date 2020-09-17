# Glas Applications

In my vision of software systems, applications are easy for users to comprehend, control, compose, extend, modify, and share - especially at runtime. Further, applications should be robust and resilient, degrade gracefully, recover swiftly from disruption.

Glas programs are based on Kahn Process Networks (KPNs), which are excellent for modeling deterministic, concurrent computations at large scales. However, at runtime, composition, extension, modification, and disruption are essentially non-deterministic. Further, it is awkward to extend or extract data models that are captured deep within a hierarchical process.

The *Transaction Machine* (TXM) - a repeating deterministic transactions in a non-deterministic environment - is a closer fit for my vision of an application model. Behavior and data are cleanly separated, which simplifies extension and modification. Tolerance to disruption is implicit.

This document explores an application model based on transaction machines, implementation and implications.

## Transaction Machines

A transaction machine forever repeats a deterministic transaction over a non-deterministic environment. The environment of the transaction machine will generally be partitioned between input/output regions and a private scratch space. IO may involve shared variables or abstract queues.

Process control is implicit: A deterministic transaction that changes nothing will always be unproductive when repeated on the same input. A scheduler can recognize unproductive transactions then wait for relevant changes. A transaction machine can effectively terminate by reaching a stable state that is not waiting on further input.

Large transactions can be partitioned into small ones via 'fork' request. Logically, the environment responds with an option non-deterministically. However, repetition and replication of isolated transactions are logically equivalent. With replication and rollback optimizations, we effectively partition one machine into many.

### Required Optimizations

For transaction machines to be viable, two optimizations are necessary: rollback and replication.

Rollback enables reuse of a computation for a relatively stable input prefix. Without rollback, transactions must be recomputed from the start. With rollback, the transaction could have a stable prefix followed by a tight loop. Efficient rollback requires compiler support.

Replication supports concurrent computation of forks. For isolated transactions, repetition and replication are logically equivalent. However, replication reduces latency for reacting to changes.

## Transaction Model

Glas programs can model deterministic transactions via request-response channel. This can be expressed with a procedural syntax. A viable request API: 

* **commit:Status | abort:Reason** - Accept or reject state. Response is `ok`. No further requests are read from current transaction.
* **try** - Begin hierarchical transaction. Response is `ok`. Hierarchical transaction must be terminated with matching commit or abort request.
* **fork:Keys** - Replicate current transaction. Response to each replica is `ok:Key` with a different key from the set. The fork path, a list of keys, provides a stable task name for debugging.
* **note:Annotation** - Provide performance and scheduling hints, program visualization and debug support, etc.. Response is `ok`. Host may log or ignore unrecognized requests.
* **apply:(object:Reference, method:Method)** - Effectful request. Object references are provided by host, methods are computed by program. Response has form `ok:Result | error:Reason`.
 * *unrecognized object* - response is `error:invalid:object`.
 * *unrecognized method for object* -  response is `error:invalid:method`.
 * other errors are specific to object or method
* *unrecognized request* - response is `error:invalid:request`.

For consistent processing, every successful request responds with `ok`, and every failing request responds with `error`. Failure is atomic, in the sense that there is no observable effect (modulo debuggers/profilers/etc.). Feature extension is feasible, e.g. introducing new requests or new methods.

If a program halts (closes the request-response channel) without explicitly committing or aborting the top-level transaction, it implicitly commits. This allows programs to be written without explicit knowledge about whether they are transactional. However, hierarchical transactions must still be explicitly committed or aborted.

Hierarchical transactions are convenient for program expression. The program can focus on the happy path, then conditionally backtrack and try a different path upon error. I intend to leverage this for a `try/then/else` syntactic construct.

### Environment Parameter

In addition to a request-response channel, the root program receives an 'env' parameter, which provides a *dictionary* of initial references and values. This parameter will be stable for the current host-app session. 

Keys within the dictionary might include `(state, app, time, random, chan, ...)`. The type and meaning of each top-level symbol is subject to standardization. This design supports lightweight restriction or extension of the environment and program adaptation to available resources.

### Reference Model

Reference values are specific to the host-app session. Under normal circumstances, they should not be sent to other applications or persistent storage. The host can and should cryptographically secure reference values via randomization or HMAC. Application programs might use abstract or modal data types to resist misuse of references.

In context of transaction machines, allocation of references should be stable, such that any aborted transaction will reallocate the same references when replayed. This might be achieved by recycling references on rollback after abort.

*Aside:* Glas will not implicitly support garbage collection of observable host references. However, a program layer could model this feature explicitly, assuming it has sufficient typeful control over references.

## Live Coding

A repeating transaction can be atomically replaced or updated between transactions. There is no need to explicitly 'save' or 'load' state. However, this is only half a solution. 

Relevantly, the application may need transition state resources, e.g. `User1.0 -> User2.0`. It is convenient if this transition can be performed lazily. This would benefit from explicit syntactic support, such that versioned data types and version transition functions are normal.

## Design Constraints for Object Model

In context of transaction machines, external effects are asynchronous. The current transaction must commit before activity begins. Results only become visible to a future transaction. Ongoing interaction may be required. Internal effects, such updating private state, can be synchronous. But most requests must respond with objects representing new asynchronous activity.

Ideally, applications easily simulate the host environment. This simplifies composition, extension, and testing. Although it is feasible to simulate a host by intercepting a subprogram's request-response stream, it is much more convenient if the application can allocate new objects with a suitable interface.

Glas does not support first-class functions. Thus, the ability to simulate arbitrary interfaces is limited. To resolve this, we can design the host environment around channels, variables, or similar elements with standardized interfaces that are easily allocated for internal use by the application.

## Application Effects Model

In context of transaction machines, external effects are asynchronous. Transaction commits, action begins, results later become available to a future transaction. In some cases, results may be partial, or require incremental consumption and further interaction.

In this context, effectful requests will often respond with a new object that represents the asynchronous activity. It's convenient if these objects share a consistent interface, perhaps a future or channel.

The exception to asynchronous effects is update of application private state, which can be performed immediately. However, to support lightweight extensions, we might wish to constrain use of certain state elements even within the application.




Asynchronous results could be modeled via future values or channels. Returning a fresh channel is more robust, leaving control of features such as bounded-buffer pushback to the environment. Importantly, the host should be able to cancel a future result in case of error or interruption.

The future value must also be stored to survive the transaction. This could be modeled as a stateful reference, private to the application, easily partitioned. Alter

Transactions can store data to mutable state. This makes it easy to implement, understand, and control memory resources. To reduce transaction conflict, we can isolate change based on 'paths' into structured state. However, the problem with mutable state is that we cannot easily extend state with new behaviors. A shared-state interaction can start and finish before another transaction intervenes.

To support extension, we could read and write to channels, instead. But where do we store the channels, if not to mutable state? Do we use a single-element channel to model a variable?



Perhaps we can extend mutable state with something like lightweight observer pattern.





The notion of a data bus or channel is an interesting option. We could 



 that we cannot easily have multiple writers to one location.


However, a problem with mutable state is that we cannot easily 

rence must then be stored somewhere accessible to future transactions.









Mutable state has several desirable properties: easy to implement and understand, and easy to control memory resources. However, it is awkward to express asynchronous effects with shared state 

Although mutable state is easy to implement and understand, it hinders extension. To build extensible architectures above mutable state requires careful, explicit design. Without type abstraction for subprograms, the result is also very fragile. Beyond extensibility, the environment should also support asynchronous effects, abstraction of external state, and effective control over memory and bandwidth resources.









We could build on an idea of data bus., which can represent broadcast or data bus, and perhaps also some publish-subscribe. However, it's a littl


One idea is to build on *monotonic* state, such as single-assignment futures or CRDTs. However, a major problem with monotonic state is that we cannot easily determine when old state can be safely garbage collected.







One idea is to build on *monotonic* state, such as single-assignment futures. 




One idea is to build on *monotonic* state. 









## Time

Environment `(time:Ref, ...)`.

* **acquire** - Response is future (a single-use channel) that will receive commit time.
* **after:Time** - Response is unit. Constraint. Runtime error before specified time.
* **before:Time** - Response is unit. Constraint. Runtime error after specified time.
* **model** - Query. Response is a value, usually `nt`, representing host model of time.

Transactions are logically instantaneous, applying at instant of commit. Acquiring transaction time is supported by a future. Specifying an 'after' constraint can serve a similar role as synchronous 'sleep'. A 'before' method can represent real-time scheduling constraints.

The standard model for Time values is `nt`, referring to Microsoft Windows NT time epoch: a number of 0.1 microsecond intervals since 0h 1-Jan, 1601 (UTC). This will be sufficient for most systems. This provides space for extensions.

## Soft Concurrent Constraints

An interesting coordination model is a *soft concurrent constraint* system. Agents use hard or soft constraints to express requirements and desiderata, intentions and interpretations. The system implicitly coordinates machines to search for relatively stable, satisfactory solutions. Agents with a reflective view of the search might serve as proof assistants. However, unless carefully restricted, this model would hinder reasoning about performance and progress.



A constraint on a transaction machine might be expressed as a transaction that *should be able* to commit, without actually committing to it. This would be similar to an assertion

A transaction can easily represent constraints for itself, but an interesting possibility is to represent constraints for other forked transactions. This could be modeled as asserting that a transaction should be able to commit.


## State

Environment `(scratch:StateRef, state:StateRef, ...)`.

A StateRef will carry one Glas value. Scratch is ephemeral, reset to unit on host-app session start. State should be durable, initially set to a value supplied with the application (or unit) then preserved across resets. 

State is private by default. Relevantly, an application is free to update its data model, or to erase data it doesn't need without concern for the needs of other applications.

### Generic Methods

Useful methods for any StateRef:

* **get** - Query. Response is whole value.
* **set:Value** - Update. Assigns value. Response is unit
* **eq:Value** - Query. Compare values for equality. Response is Boolean.
* **path:Path** - Query. Response is path-specific StateRef.
* **type** - Query. Response is `dict | list | number`.

A path is a simple list of symbols and numbers as a path, e.g. `[foo, 42, baz]`. A symbol indexes a dictionary, while a number indexes a list. It is possible to create a path reference that is currently invalid due to state; use of an invalid path is treated as a runtime error (can be caught with 'checked' apply).

*Thought:* It might also be useful to 'observe' a StateRef, obtaining a sequence of values from prior transactions, perhaps with timestamps.

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

## Time

Also, lightweight real-time behavior can be expressed by asserting a transaction commits before or after a timestamp.

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

### Application Control

Environment `(app:Ref, ...)`.

* **reset** - Fire and forget. Response is unit. After commit, halt the current host-app session then start a new one. This will close channels, invalidate host references, reset scratch to unit, etc.. 
* **halt** - Same as reset, except does not start a new host-app session. The application can be activated again through the host.

Applications will implicitly terminate if they reach a stable state that isn't waiting on external input. But explicit termination will often be more convenient.


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
