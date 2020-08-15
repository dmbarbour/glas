# Glas Applications

This document discusses my vision for modeling applications in Glas systems. By 'application' I broadly include console applications, GUI applications, web apps, web servers, etc..

The foundation model for everything else is *Live Coding Applications*. I envision some larger systems above this.

## Live Coding Applications

In many contexts, it's convenient if changes to a program can immediately, safely be deployed into the real world. Further, it's useful if we can test programs with real world input before deployment.

This requires careful attention to how state and effects are modeled, and to the larger system as a whole.

A good model for live systems:

* Application is deterministic transaction, repeating.
* Request-response supports abstract state, queries.
* Process control implicit based on detecting change.
* Hierarchical transactions for error handling, testing. 
* Scale by non-deterministic fork and idempotent clone.
* Programs may be aborted and updated at any time.

Transactions can run concurrently unless they conflict. Conflict leads to wasted work, with one transaction being aborted or rolled back. However, a scheduler can dynamically arrange for repeating transactions that are observed to frequently conflict to run at different times.

Process control is based on detecting change. If a deterministic transaction doesn't modify anything, it will do the same when repeated until there is a change in the requested input. Thus, a scheduler could implicitly wait for a relevant change. 

Aborted transactions very obviously fail to modify anything. This can be leveraged to support conventional process control: a read-or-abort request on a queue or mailbox can cause the transaction to wait for input (or any change in prior input).

Abstracting external state behind requests simplifies precise conflict and change detection. It also simplifies modeling of unordered structures that have low risk of read-write conflict. For example, two concurrent reads taking from an ordered queue do conflict, but concurrent reads taking different elements of an unordered tuple space do not conflict.

Hierarchical transactions can start with a `try` request and terminate with `commit` or `abort`. This feature is very convenient for backtracking on failure, which can simplify program logic. Hierarchical transactions can also support unit and integration testing with live data prior to deployment.

The top-level transaction also terminates with a final commit or abort. No response, except to close the request-response channel. This ensures termination is deliberate, supports early exit, and simplifies composition of applications.

Applications in this model are idempotent in a useful sense: replicating a repeating transaction does not affect the observable outcomes of the system. If a transaction does not conflict with itself, it can be replicated for performance, scheduling multiple instances concurrently. This is feasible when reads mostly take from unordered structures, such as a tuple space.

Large transactions have high risk of conflict. To solve this, I introduce a `fork:[foo,bar,baz]` request that non-deterministically responds with one of the values in the list. Together with idempotent replication and early exit via commit, this behaves similar to the Linux 'fork' operation. It can divide a large transaction into many smaller ones,

This application model is simple, observable, extensible, composable, updateable, and scalable. It can support a familiar, procedural syntax and excellent failure handling. Compilers can optimize by introducing hooks for checkpointing, rollback, efficient replication.

Live coding applications are the core concept of the Glas application model.

### General Requests

Requests that every live coding application should support.

* **try** - Begins a hierarchical transaction. Response is `ok`. Requires a matching commit/abort.
* **commit | abort** - Finalize transaction. No response; request-response channel is closed. 
* **fork:\[list,of,values\]** - Replicate transaction. Respond to each replica with different value from list. 
* **log:Message** - Emit a message to an implicit transaction log, unless disabled. Response is `ok`.
* **checkpoint** - Inform system that this is good place to roll back in case of conflict. No promises. Response is `ok`.
* **check** - For long-running transactions, ask system to check for concurrent conflicts. Response is `ok`. Or no response if the system decides to abort.

Access to logs should be visible through system reflection. The partial log of an active transaction should be visible.

Fork is similar to the Linux feature, but simpler because there is no direct interaction between transactions. If an application forks many times and reaches its quota, we might switch to rotation through non-deterministic choices. (In context of idempotent replication, fork is logically equivalent to responding with a value non-deterministically.) 

### Security Model

The principle of least authority limits the scope of error or malice. Live coding applications should be restricted to what they must manipulate. Subprograms should be further restricted for the same reason.

It is feasible to intercept the request-response channel to impose restrictions. However, this hurts both performance and extensibility of effects, and is easy to get wrong by accident. It's a bad design.

Instead, a transaction should receive some abstract tokens that represent its authorities. These will be provided as additional parameters to the Glas program, e.g. a program might receive `io:auth-fs-music`, `io:auth-fs-home`, and `io:auth-media-sound`. 

Abstraction of these tokens could be checked by static type analysis. Abstraction may also be cryptographically enforced via CPRNG or HMAC.

An authority token must be be sent together with any request that has potential to be security sensitive. 

Additionally, requests support derived authorities. For example, if we have a token for access to a filesystem folder, we should be able to create a token for a subfolder. This can be supported by effectful request or by function parameter in the higher-order namespace.

Configuration of an application's initial authority might be performed by an administrator. 

### Interprocess Communication

Repeating transactions cannot carry any state from one cycle to another. Thus, even communication with future self is performed by interprocess communication.

Variables, Queues, Mailboxes, Tuple Spaces, Tables, Databases

### Durability of State

### Filesystem Access

### Network Access

### Console

### GUI


### Installation

It is possible to intercept the request-response stream from a subprogram, to modify 


It is possible to secure a live-coding application by wrapping it and intercepting the requests and responses.




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
