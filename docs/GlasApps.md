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

Glas programs can model transactions via request-response channel. A viable API:

* **commit:Status | abort:Reason** - Accept or reject state. Response is `ok`. No further requests accepted by transaction.
* **try** - Begin hierarchical transaction. Response is `ok`. Transaction terminates with matching commit or abort. 
* **fork:\[List,Of,Values\]** - Replicate current transaction. Response to each replica is different value from list. 
* **apply:(obj:Ref, method:Val)** - Query or update the environment. Response is provided by environment.

Ideally, the transaction API, references, and methods will be statically analyzed for errors. Runtime type errors should cause a transaction to abort without response.

## Environment Model

The environment model for Glas applications is a Glas value. The initial value should be paired with the transaction machine's behavior.

By convention, the top-level environment value is a dictionary. The public interface is `io`. Everything else is private to the application. Occasionally, between application transactions, the external system should read and write `io` according to a standard API (see *External Effects*).

An object reference is a path into a structured value. A path is a list of symbols and numbers, such as `[foo, 42, baz]`. Each symbol indexes a dictionary. Each number indexes a list. The empty path `[]` references the root value. 

Methods represent pure functions to query or update the referenced value. The get/set methods are logically sufficient. Other methods are designed to improve precision for change and conflict detection. 

*Aside:* It is feasible to infer a valid initial environment value, but this is best done at edit-time. Polishing initial state is useful application development.

### Generic Methods

* **get** - Query. Response is copy of referenced value.
* **set:Value** - Update. Assign referened value. Response is `ok`.
* **type** - Query. Response is `dict | list | number | error`. The `error` response is returned for invalid references.

Currently, 'type' is the only method that supports invalid references. Everything else would abort without response.

### Dictionary Methods

* **d:keys:(in?Keys)** - Query. Response is Keys (a dictionary with unit values). This set is optionally restricted by intersection with the 'in' parameter. 
* **d:select:Keys** - Query. Response is values from dictionary restricted by intersection with given keys.
* **d:insert:Dict** - Update. Add new keys or override prior values for existing keys, but don't modify keys not in parameter. Response is `ok`.
* **d:remove:Keys** - Update. Remove keys and their values from the dictionary. Response is `ok`.

### List Methods

Lists should support methods for use as queues, and methods to detect change for a sequential range of values. It could also be useful support size comparisons.

### Number Methods

Numbers could usefully support increment, decrement, value comparisons (eq, lt, ge).

## Notebook Applications

A notebook application mixes code and UI, essentially a REPL that prints usable GUIs.

Glas is very suitable for this. We could arrange for each logical line to fork a subtask and maintain its own GUI frame. The tasks may freely interact. The logical lines themselves could be graphical projections of a program. 

## Web Apps and Wikis

Transaction machines should be a very good basis for web applications. We can tweak the external effects to focus on UI, client-side storage, and limited network access (XmlHttpRequest, WebSockets). With partial evaluation, we can compute the initial 'static' page.

I have this vision where every page of a Wiki is essentially a web app - usually a notebook application. A subset of pages might also represent server-side behavior. These applications could support exploration of data, little games, and other things. Updates to the web-app could be deployed immediately.

I'm not certain this vision is practical. But it would at least make an interesting sandbox for exploring Glas.

## External Effects

To support a more resilient system, the transaction machine should never receive volatile host references, such as file descriptors. Ideally, we can gracefully continue computation after a disruption in effects. These constraints influence my designs.

### Filesystem

### Network

### Clock

### GUI


For example, a simple GUI application could write a virtual document object model under `io:gui`. The GUI model might support binding buttons, labels, text fields, etc.. The system and application must share a standard model for rendering of GUIs.


### Console

### Sound

## Dynamic Scheduling

A system can track conflicts between repeating transactions. If two transactions frequently conflict, they can be heuristically scheduled to run at different times. Even when transactions do conflict, fairness can be guaranteed based on tracking conflict history.

Hard real-time systems can feasibly be supported by introducing a schedule of high-priority transactions that will 'win' any conflict resolution if it commits within its quota.

## Static GUI

We can evaluate an application from its initial state without attaching IO. This can be useful to improve startup speeds, perhaps to compile some static views.

Intriguingly, is also feasible to attach just the GUI without any other effect runtime behavior. This could simplify refinement and development of the initial state. 

## Deployment Models

Consider a heterogeneous system with unique, distributed resources: sensors and actuators, partitioned networks and storage.




 However, this doesn't do us much good unless the effects model also represents multiple machines, and  are 

 can feasibly be computed in a distributed application can feasibly be distribu
