# Glas Applications

An application is a program that automates behavior of programmable systems on behalf of its users. Ideally, applications are easy for those users to comprehend, control, compose, extend, modify, and share. Especially at runtime.

This document proposes an application model for Glas systems that contributes to these goals. The core concept of this model is the *Transaction Machine*.

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

*Note:* There are many potential optimizations beyond the required two, e.g. fast roll-forward for irrelevant change, fusion of cooperative transaction loops, and constant propagation.

## Transaction API

Glas programs can model transactions via request-response channel. Requests:

* **commit:Status | abort:Reason** - Accept or reject state. Response is `ok`. No further requests accepted by transaction.
* **try:(scope?Scope)** - Begin hierarchical transaction. Optionally scoped. Response is `ok`. Transaction terminates with matching commit or abort.
* **fork:\[List,Of,Values\]** - Replicate current transaction. Response to each replica is different value from list. 
* **note:Note** - Runtime-layer annotations. Intended for performance hints, debug logs, etc.. Response is `ok`.
* **apply:(obj:Reference, method:Method)** - Query or update environment. Response is provided by environment.

Scopes, references, and methods are values meaningful to the environment model. Notes should be meaningful to the runtime system (but may be ignored). The top-level commit/abort status will likely be visible in a task manager.

Runtime type errors should cause a transaction to abort without response. Ideally, static analysis of an application should validate use of this API and determine environment type.

## Environment Model

The environment model for Glas applications is a Glas value, modeling a hierarchically structured memory. An application consists of an initial or current environment and a program that implements the transaction API.

By convention, the top-level environment value is a dictionary. The public interface is `io`. Everything else is private to the application. Occasionally, between application transactions, the external system should read and write `io` according to a standard API (see *External Effects*).

An object reference or scope is a path. The path is a list of symbols and numbers such as `[foo, 42, baz]`. A symbol indexes a dictionary, while a number indexes a list. The empty path `[]` references the root value. A program is free to compute paths.

A scoped hierarchical transaction logically prepends every `apply` with given path. Programmers could do this manually, but moving to the environment layer supports potential optimizations.

Methods are pure functions that query or modify the referenced value. Use of get/set is logically sufficient. Other methods improve precision of conflict and change detection.

### Generic Methods

These methods apply for all data types. 

* **get** - Query. Response is whole value.
* **set:Value** - Update. Assigns value. Response is `ok`. 
* **type** - Query. Response is `dict | list | number | invalid`. The `invalid` response is for invalid references.

### Dictionary Methods

Dictionaries are mostly used as namespaces, but that's covered by pathing. Dictionaries support a few methods that would make them a passable basis for publish-subscribe or tuple space patterns.

* **d:keys:(in?Keys, ex?Keys)** - Query. Repond with Keys. Optional parameters for precision:
 * *in* - restrict response by intersection (whitelist)
 * *ex* - restrict response by difference (blacklist)
* **d:insert:Dict** - Update. Insert or update keys in dictionary. Response is `ok`
* **d:remove:Keys** - Update. Remove selected keys from dictionary. Response is `ok`.

A keys value is a dictionary with unit values.

### List Methods

Lists are most useful for modeling queues. Multiple writers and a single reader can commit concurrently under some reasonable constraints. 

* **l:addend:(items:\[List, Of, Values\], head?)** - Update. Add items to tail of list. Response is `ok`. Optional flags:
 * *head* - write to head of list instead, e.g. putback
* **l:remove:(count?Count, exact?, tail?)** - Default Count is 1. Remove Count items from head of list, or maximum available. Response is items removed. Optional flags:
 * *exact* - Remove Count items if available, else zero.
 * *tail* - Remove from tail of list instead, preserves order.
* **l:length** - Query. Response is number of items in list.
* **l:lencmp:Threshold** - Query. Compares list length to threshold. Response is `lt | eq | gt`. (See also `n:compare`.)

*Note:* Lists can be used for dynamic allocations, but creating a fresh dictionary symbol is easier to remove when done. Glas systems should favor dictionaries over lists for most use cases.

### Number Methods

For most numbers, get/set is sufficient. However, *counters* can be high-contention, and we can also try to stabilize observation of numbers with thresholds.

* **n:increment:(count?Count)** - Update. Default Count is 1. Increase number by Count. Response is `ok`.
* **n:decrement:(count?Count)** - Default Count is 1. Reduce number by Count. If number is reduced by zero, response is difference `(Count - number)`, otherwise response is zero.
* **n:compare:Threshold** - Compares number to threshold. Response is `lt | eq | gt` corresponding to number being less-than, equal, or greater-than the threshold.

## Live Coding

We can safely update an application's behavior between transactions. 

Ideally, the new application can pick up where the old one left without huge rewrites, i.e. the state model doesn't change. If necessary, it is feasible for application state to keep some version information, and for the applications to include update behavior between versions. Alternatively, a compiler could provide the update behavior.

## Notebook Applications

A notebook application mixes code and UI, essentially a REPL that prints usable GUIs.

Glas is very suitable for this. We could arrange for each logical line to fork a subtask and maintain its own GUI frame. The tasks may freely interact. The logical lines themselves could be graphical projections of a program. 

## Web Apps and Wikis

Transaction machines should be a very good basis for web applications. We can tweak the external effects to focus on UI, client-side storage, and limited network access (XmlHttpRequest, WebSockets). With partial evaluation, we can compute the initial 'static' page.

I have this vision where every page of a Wiki is essentially a web app - usually a notebook application. A subset of pages might also represent server-side behavior. These applications could support exploration of data, little games, and other things. Updates to the web-app could be deployed immediately.

I'm not certain this vision is practical. But it would at least make an interesting sandbox for exploring Glas.

## External Effects

I'd like to design these adapters for some balance of simplicity, resilience (graceful degradation and recovery after disruption), and performance. Disruption itself may need to be modeled.

### Filesystem

Desired features: read and write files, browse the system, watch for changes. 

It seems these operations can mostly be idempotent, which simplifies resilience. Idempotent write would involve writing to an offset (not 'end').

Files used as channels might need separate attention.

### Network

Desired features: listen on TCP/UDP, connect to remote hosts, read/write connections. Listening should be resilient, and connections should gracefully die.

### Clock

Rather than continuously update a clock variable, we could arrange for some methods to trigger on the clock, update the clock only when a trigger occurs. The system should be able to see the trigger times in order to make good scheduling decisions.

### GUI

Ideally, any parameter a GUI can manipulate is also part of the application's public API, i.e. in IO. A lightweight GUI could define a virtual DOM and bind to IO elements to model the buttons, labels, text fields, frames, etc..

Also ideally, most of the 'views' for a GUI are relatively stable and can be rendered ahead of time. We could model this as GUI frames or cards of some form.

### Console

I don't need much here. It could be useful to support full terminal manipulations. But I'm pretty sure that can be done with a normal stream of bytes.

### Sound

I wonder how much sound should bound to UI? But we could also do sound via network or other layer.

### Shared Memory?

Shared memory IPC is a relatively bad fit for transaction machines, because the memory is not transactional. So let's just avoid this for now.

## Orthogonal Persistence

Because application state is very accessible, we could easily support external persistence. This can be combined with content-addressed storage to support very large application states.

## Dynamic Scheduling

A system can track conflicts between repeating transactions. If two transactions frequently conflict, they can be heuristically scheduled to run at different times. Even when transactions do conflict, fairness can be guaranteed based on tracking conflict history.

Hard real-time systems can feasibly be supported by introducing a schedule of high-priority transactions that will 'win' any conflict resolution if it commits within its quota.

## Static GUI

We can evaluate an application from its initial state without attaching IO. This can be useful to improve startup speeds, perhaps to compile some static views.

Intriguingly, is also feasible to attach just the GUI without any other effect runtime behavior. This could simplify refinement and development of the initial state.

## Deployment Models?

The state for a large application could feasibly be distributed across many machines. But it might be easier to model deployment of several smaller transaction machines, and bind certain states between them. Perhaps the IO between components can be based on bounded-buffer queues or CRDTs. 

This seems an area worth exploring. But it might well become another 'layer' of models.
