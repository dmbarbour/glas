# Glas Application Model (Glam?)

An application is a program that can automate behavior on behalf of the user. However, conventional application models are problematic - difficult for users (and programmers) to comprehend, control, compose, modify, integrate, or extend.

I envision an alternative application model for Glas systems with much nicer properties. The core concept of this model is a *Transaction Machine*. 

This document discusses the model, its benefits and tradeoffs, and how it might be integrated with existing systems.

## Transaction Machine

An application is an deterministic transaction which is implicitly repeated by an external loop. Forking and hierarchical and forking transactions are supported. Access to external state is abstracted through request-response.

Process control is based on the fact that a deterministic transaction that changes nothing will also be unproductive when repeated, at least until there is a change in requested input. The system can recognize unproductive transactions then wait for change.

Hierarchical transactions support composition. In practice, this will mostly be used for backtracking conditional behavior, e.g. some variation of `try/then/else`. But it's also useful for testing, modeling what-if scenarios, etc.. 

Forking controls risk of conflict. Logically, a `fork:[foo, bar, baz]` request has a non-deterministic response with one value from list. In context of repeating transactions, we can replicate transaction, run all forks concurrently. Each fork can handle a different task.

Abstraction of external state via request-response makes it easier for the system to track what is observed, detect potential conflicts, and protect invariants of system objects.

This application model is observable, extensible, composable, scalable, and has many other nice properties. 

### Required Optimizations

For transaction machines to be viable, two optimizations are required: replication and rollback.

Without replication, we can only rotate through forks. This would introduce unacceptable latency overheads. This optimization is valid because repetition and replication of a transaction are logically indistinguishable.

Rollback enables an application to efficiently continue a task. A compiler could instrument the generated program for rollback, then the system should determine the first request whose value changes and restart computation from there. This can avoid redundant setup or configuration costs such as forking.

With both replication and rollback, we can effectively fork tasks to handle tight loops, e.g. for processing elements from a queue.

### Transaction API

In context of Glas programs, transactions can be expressed via request-response channel, with several requests:

* **commit:Status | abort:Reason** - Finalize transaction and early exit. Commit accepts state, abort rejects it. Response is `ok` then closes request-response channel (if top-level transaction).
* **try** - Start hierarchical transaction. Response is `ok`. Transaction terminates with matching commit or abort. 
* **fork:\[List,Of,Values\]** - Replicate current transaction. Response to each replica is different value from list. 
* **note:Annotation** - Effects-layer annotations. This might be used for debug logging or GUIs, prefetch of resources, etc.. Response is `ok`.
* **apply:(obj:Ref, method:Val, query?, ignore?)** - Update object in transactional environment. Response is normally provided by environment. Optional flags:
 * **query** - Obtain response, don't change anything. Use implicit hierarchical transaction if necessary. 
 * **ignore** - Do the effects, ignore the response. Respond with `ok` instead.

Unrecognized requests, invalid method, etc. will implicitly abort the transaction without responding.

### Object Model

References are local to the application and have a typeful prefix such as `var:varname` or `queue:queuename`. The typeful prefix determines available methods and default value. Names must be symbols. Hierarchical namespaces are supported with prefix `ns:nsname`, e.g. `ns:foo:var:x`.

By convention, namespace `ns:io` represents the application's intended public surface. For example, `var:x` should be considered private while `ns:io:var:x` is considered public. 

The external system should occasionally read and modify objects in `ns:io` between transactions. This requires designing an API for what IO variables mean, whether they're for use as input or output or both, etc..

For example, a simple GUI application could write a virtual document object model under `ns:io:ns:gui:var:document`. The document could describe a form with some text fields and buttons bound to other IO variables. The system knows to look for this variable to render the application.

We similarly need APIs for network, filesystem, console, clock, etc.. Potential APIs are discussed in later sections.

### Object API

Variables, queues, and namespaces are supported. This might be extended in the future if there's a strong argument.

Variables can do everything we need. However, they do not support fine-grained conflict or change detection. 

Queues support one of the most common coordination patterns between transactions. Usefully, concurrent transactions can write to a queue without conflict.

Namespaces support bulk manipulations, e.g. clearing all the variables in a given volume, or taking a snapshot/checkpoint.

* **Variables.** Prefix `var`. Default value is unit `()`. 
 * **get** - Response is current value. Will read default value if undefined.
 * **set:Value** - Set value of the variable. Setting the default value implicitly removes variable.

* **Queues.** Inherit Variables, except default is empty list `[]`. Always contains list value.
 * **read** - Response is `ok:Value | empty`. Removes value from head of queue.
 * **empty** - Query whether queue is empty. Response is boolean.
 * **write:Value** - Adds a value to end of queue. Response is `ok`.
 * **write-many:\[List,Of,Values\]** - For efficiency. Write a list of values to end of queue. Response is `ok`. 

* **Namespaces.** Prefix `ns`. Root namespace can be referenced as symbol `ns` without a name.
 * **clone:Source** - Copy source namespace to object of apply. 
 * **clear** - Clear specified volume.

### Partial Evaluation

We can run an application for several cycles, starting from an initial state, such as cleared root, before binding external IO. Ideally, the application reaches a steady state before IO.

A useful consequence is that we should be able to render and interactively manipulate the application UI before we attach to network or filesystem.

## Live Coding

Applications based on transaction machines can easily be updated between transactions. The current application can be aborted, then replaced, with the new code taking over the application objects.

A related consequence is that we can safely replace code with an instrumented version, e.g. for debugging or profiling, without breaking the system.

## Notebook Applications

Transaction machines are well suited for notebook applications. Partial evaluation can produce an interactive GUI even before binding to network or filesystem effects. 

Every logical line could fork its own subtask, which has full access to IO. Just before the line, we could update a variable such that we know which GUI frame to update. 

If our syntax is also designed for graphical projections, then our program input might have a GUI of its own.

*Aside:* It seems feasible to extend this concept to spreadsheets, or at least to a flexible 2D layout.

## Web Apps and Wikis

We could represent a web application as a transaction machine represented by a Glas program. We only need to restrict the IO effects to the UI, client-side storage, and limited network (XmlHttpRequest, WebSockets).

We could partially evaluate the machine to generate an initial static UI, then compile to JavaScript to maintain it.

I have this vision where every page of a Wiki is essentially a web app - usually a notebook application. A subset of pages might also represent server-side behavior.

I'm not certain this vision is practical, but it would at least make an interesting sandbox for exploring Glas. And it could potentially scale via access to cloud computing.

## External Effects

This section discusses integration with existing systems.

### Filesystem

### Network

### Clock

### GUI

### Console

### Sound

