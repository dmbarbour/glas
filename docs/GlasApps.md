# Glas Application Model (Glam?)

This document discusses my vision for modeling applications in Glas systems. By 'application' I broadly include console applications, GUI applications, web apps, web servers, etc..

The foundation model for everything else is *Live Coding Applications*. I envision some larger systems above this.

## Live Coding Applications

In many contexts, it's convenient if changes to a program can immediately, safely be deployed into the real world. Further, it's useful if we can test programs with real world input before deployment.

This requires careful attention to how state and effects are modeled, and to the larger system as a whole.

A good model for live systems:

* Application is deterministic transaction, repeating.
* Incremental computing by stable partial evaluation. 
* Request-response supports abstract objects, state.
* Process control implicit based on detecting change.
* Hierarchical transactions for error handling, testing. 
* Scale by non-deterministic fork and idempotent clone.
* Programs may be aborted and updated at any time.

Transactions can run concurrently unless they conflict. Conflict leads to wasted work, with one transaction being aborted or rolled back. However, a scheduler can dynamically arrange for repeating transactions that are observed to frequently conflict to run at different times.

After a transaction commits or aborts, it isn't always restarted from the very beginning. A compiler could instrument the program for partial rollback and replay based on internal dataflows and a list of external changes. Programs can be designed to minimize rework by reading stable data and preparing before their main behavior.

Process control is based on detecting change. If a deterministic transaction doesn't modify anything, it will do the same when repeated until there is a change in the requested input. Thus, a scheduler could implicitly wait for a relevant change. Aborted transactions obviously won't modify anything when repeated, which makes it easy to wait on a queue or mailbox.

Abstracting external objects behind requests simplifies precise conflict and change detection. It also simplifies modeling of unordered structures to reduce risk of read-write conflict. For example, concurrent reads from a queue will conflict (both remove same head element), but concurrent reads from an unordered tuple space often won't conflict (remove different tuples).

Hierarchical transactions can be supported start with a `try` request and terminate with `commit` or `abort`. This feature is very convenient for backtracking on failure, and can hugely simplify program logic. Hierarchical transactions can also support unit and integration testing with live data prior to deployment.

The top-level transaction also terminates with a final commit or abort. No response, except to close the request-response channel. This ensures termination is deliberate, supports early exit, and simplifies composition of applications.

Applications in this model are idempotent in a useful sense: replicating a repeating transaction does not affect the observable outcomes of the system. If a transaction does not conflict with itself, it can be replicated for performance, scheduling multiple instances concurrently. This is feasible when reads mostly take from unordered structures, such as a tuple space.

Large transactions have high risk of conflict. To solve this, I introduce a `fork:[foo,bar,baz]` request that non-deterministically responds with one of the values in the list. Together with idempotent replication, this can take all paths.

Real-world effects are never performed by transactions directly. Instead, after a transaction commits, writes to specific objects will implicitly be handled by the runtime. For example, if we write binary data to an object that represents a TCP connection, we might try to send that value after we commit.

This application model is simple, observable, extensible, composable, updateable, and scalable. It can support a familiar, procedural syntax and excellent failure handling. Compilers can optimize by introducing hooks for checkpointing, rollback, efficient replication.

Live coding applications are the core concept in my vision for a Glas application model.

### Requests API

This is a general summary of the top-level requests visible to a live-coding application.

* **commit:Status | abort:Reason** - Finalize transaction and early exit. Commit accepts state, abort rejects it. Response is `ok` then closes request-response channel if top-level transaction.
* **try:Name** - Start hierarchical transaction. Response is `ok`. Transaction terminates with matching commit/abort. Name is for debugging, should be unit `()` or symbol.
* **fork:\[List,Of,Values\]** - Replicate current transaction. Response to each replica is different value from list. 
* **note:Message** - Emit a message to an implicit transaction log. Supports administration and debugging. Response is `ok`.
* **apply:(obj:Ref, msg:Value)** - Update object. Response is provided b the object, or `error` if invalid ref. 
* **query:(obj:Ref, msg:Value)** - As apply, but for read-only operations. Simplifies reasoning about behavior and caching.
* **check:(obj:Ref)** - Check whether a reference is valid. Response is `ok` or `invalid`. 

An unrecognized or invalid request responds with `error`.

In addition to the `io:request` and `io:response` ports, the top-level program should receive an ad-hoc configuration of values and references through `io:env`.

Applications will inevitably have quotas, e.g. a limited number of concurrent forks, bounded CPU consumption. If a fork quota is reached, the system can implicitly switch to heuristically rotating through forks, preserving logic at cost of latency.

### Handling of Write-Write Conflicts

A read-write conflict is resolved by aborting one transaction. But a write-write conflict can be resolved in favor of either transaction. When two transactions blindly write a variable, the the one that runs later 'wins'.

However, with repeating transactions, it isn't clear which is running "later". Rapid flip-flopping of the variable is a waste of CPU and obviously wrong. Forking on the variable is intriguing, but hinders local reasoning about performance.

A viable solution: choose a winner based on priority. This would be useful for extending a system with overrides. Priority could be configured, with a stable default priority in case of ties.

We should ensure the conflict is visible to administrative interfaces, especially if resolving by default priority, so programmers can easily identify and debug the issue.

## Storage Object Model

The objects accessible to transactions are oriented around storage, such as variables or queues. These objects do not directly communicate with each other, even if they hold a reference. Queries or updates only affect their internal data.

Simple variables are sufficient but not ideal. To avoid transaction conflicts and for detection of relevant changes, it's better to have specialized operations.

For example, if we have a numeric variable, in addition to basic read-write, we could support threshold queries (greater than 100?) and increment/decrement operations. Multiple transactions could concurrently increment the number without reading it, and the threshold query probably won't change every time we increment the number.

The challenge is to develop a set of storage models that are simple to implement and leverage, easy to comprehend, and widely useful.

Variables, Queues, Mailboxes, Tuple Spaces, Tables, Databases...

### Object Capability Security

References are cryptographically secure and semantically opaque. For example, a reference could be a value including a description, revoker ID, and an HMAC. Or it might be an AES-encrypted opaque binary.

There are contexts where it might be useful to serialize, render, or compare references. Thus, reference values are not hidden from applications, but applications are encouraged to police their own use with abstract types.

Performance overhead of constructing and authenticating cryptographic references can be mitigated with caching. Also, the number of references used by a transaction should be relatively small. 

An application will start with a few provided references then refine access with queries. For example, given a read-write filesystem reference, we might query to obtain a read-only subfolder reference.

References may also be written, e.g. a DSL for a GUI form might require a variable reference for where to store the text field, perhaps another for where to read recommended completions.

### Residual Reference Revocation

Object references in Glas systems gradually expire. 

By default, references passed to the application transaction might be replaced by new ones every few minutes or hours. New references, but still referring to the same objects. The old references would continue working for a grace period of a few generations, but eventually become invalid.

This feature simplifies reasoning about security, improves visibility of relationships due to their continuous maintenance, and creates pressure to design live systems in the object model.

## External Effects

### Filesystem

### Network

### Shared Memory





## System Administration

Whether a transaction commits or aborts, the system should record its final status and log, and keep useful metrics - CPU, memory consumption, productivity (committed CPU time / total CPU time), conflict history, etc.. This information should be available through reflection objects. 

We could create an administrative dashboard and task manager based on this. The dashboard would also support disabling an app, or drilling in for more data.

The additional data might not be recorded by default. However, Glas applications have a nice property that they can safely be replaced by a version instrumented for debugging at any time, and we can immediately see results on the next cycle.









## Reflection

 (we might have two or three sets of active 


), but will




These specialized references can be passed to forked subprograms to ensure separation.


In Glas applications, new references will usually be derived from existing references by query. For example, given a reference to a megabyte of shared memory, we could create a new reference that just has access to bytes 4096-1638. 

The costs of cryptographic references aren't trivial, but they can be mitigated by caching, incremental computing, and optimized representation. Also, 

applications shouldn't be creating huge numbers of references, only enough to support inter-process communication.



An application with access to a filesystem folder might perform a few queries to obtain references to subfolders. But the resulting references should be relatively stable, thus won't be recomputed every cycle.





 transaction might use a few queries to obtain


The cost of authenticating cryptographic references can be mitigated by caching, e.g. hashtable from reference value pointers to objects. The cost of creating cryptographic references can be mitigated by an incremental discovery pattern in the transactions: a 



References in Glas are subject to revocation and expiration. They might survive a few hours or days. However, they eventually stop working


 Thus, sharing references should be considered a temporary thing.



The cost of cryptographic protections can be mitigated by memoization of validated references and other incremental computing patterns.

 a little more expensive but 

However, due to cryptograp Glas systems won't depend on this.

 However, there are use-cases where references might be serialized

The 




  (perhaps generated by secure hash
    
    random GUID)


 or sparse identifiers, whichever mechanisms result in good performance.

Glas programs might also 

We could also enforce abstract types.

 enforce abstract types,

type-safet

can be achieved by a number of means: 




They might be generated by CPRNG, or 

They are not necessarily opaque, e.g. a Glas system could choose to use HMAC ins

The principle of least authority limits the scope of error or malice. Glas applications will introduce security at the level of object references: an object 

It's very convenient when working with trust boundaries.


Live coding applications will support this by using sparse references

 limits scope of error or malice.


 Live coding applications should be restricted to what they must manipulate. Subprograms should be further restricted for the same reason.

It is feasible to intercept the request-response channel to impose restrictions. However, this hurts both performance and extensibility of effects, and is easy to get wrong by accident. It's a bad design.

Instead, a transaction should receive some abstract tokens that represent its authorities. These will be provided as additional parameters to the Glas program, e.g. a program might receive `io:auth-fs-music`, `io:auth-fs-home`, and `io:auth-media-sound`. 

Abstraction of these tokens could be checked by static type analysis. Abstraction may also be cryptographically enforced via CPRNG or HMAC.

An authority token must be be sent together with any request that has potential to be security sensitive. 

Additionally, requests support derived authorities. For example, if we have a token for access to a filesystem folder, we should be able to create a token for a subfolder. This can be supported by effectful request or by function parameter in the higher-order namespace.

Configuration of an application's initial authority might be performed by an administrator. 

### Filesystem Access

### Network Access

### Console

### GUI

### Installation

It is possible to intercept the request-response stream from a subprogram, to modify 


It is possible to secure a live-coding application by wrapping it and intercepting the requests and responses.


## Resource Discovery

Glas systems should use a metaphor of resource discovery. We conceptualize a space of objects as existing prior to our application. We're just binding them to a purpose. This is a good fit for both live systems and users: we can use queries to bind resources that are 'already there', and the paths are stable which is easy to understand, render, and debug.

The alternative is creation or allocation of resources. However, this becomes very awkward with residual reference revocation.

### Ephemerality and Durability

There may be ephemeral objects whose data is implicitly lost after all references to them disappear. Thus, keeping these resources 'alive' requires continuously updating the references, like a baton relay. When the system is reset, ephemeral resources and references should also be cleared. 

However, there will also be durable objects and variables whose content is generally preserved unless explicitly deleted.


## System Administration



### Live Notebook Applications

The request-response channel for effects will tend to sequence programs. Thus, we can run a REPL sequence as a repeating transaction, integrating live data. Though, forking transactions might need some extra design.

If we implicitly bind a named component for a GUI associated with each logical line of code, (perhaps by setting an external variable) we could support interactive models that are flexibly mixed with the program code that generates them.

We could also design a syntax for projectional editing, such that the code can also be flexibly rendered as math equations, tables, forms, color pickers, and other widgets. GUIs to manipulate GUIs.

Pursuing this approach will likely result in an experience similar to Mathematica or Jupyter notebooks.

### Web Apps and Wikis

A web app as a live coding application might transactionally manipulate a document object model, client-side storage, and publish/subscribe access to server resources. A compiler could partially evaluate to extract an initial static document, then convert whatever's left to JavaScript.

I envision of modeling a Wiki where every page defines a web application or potential component, where editing the wiki is based on apps written in the wiki, and where we can also install server-side behaviors to extend it into something new.

Most pages might be live notebook applications.

I'm not certain this vision is practical, but it would at least make a good sandbox for playing with Glas. And it could potentially scale via access to cloud computing.
