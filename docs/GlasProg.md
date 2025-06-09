# Program Model for Glas

A program is defined in context of a [namespace](GlasNamespaces.md). An [application](GlasApps.md) program is represented within a namespace by defining a few toplevel names with simple naming conventions. Other names are useful for modularization of programs, abstraction and reuse of program fragments across applications. A subset of names are primitives, defined by interpreters or compilers and standardized for consistency. The convention in glas is to favor the '%' prefix for primitive names, simplifying recognition and propagation of primitives through the namespace.

This document designs and defines an initial set of primitive names.

## Design Thoughts

### Extensible Intermediate Language

A user-defined front-end language is supported based on the namespace model. Definitions are represented in the intermediate language. Ideally, this intermediate language is similarly subject to extension and evolution as the front-end.

To support this, every definition in the namespace should have a toplevel AST wrapper of form `(%Lang AST)`, where Lang indicates how to interpret AST. Relevantly, the AST may be invalid in some languages or interpreted differently in others. Calls or references between definitions may be restricted based on language or program compatibility.

This immediately introduces an opportunity for specialized sublanguages to define types, tests, tables, or constraint systems separately from procedural programs. How such things are integrated could be ad hoc and flexible. But the primary motive is to provide an opportunity to retract and deprecate old features and introduce new ones, to simplify static analysis for compatibility issues separately from a type system. 

### Application State

I propose to model state as an ad hoc collection of implicit parameters in a structured environment. Essentially, these parameters are allocated on demand and pass-by-reference, but are passed implicitly to avoid messy calling conventions. Instead, a program can translate names, control the names visible to a subprogram, and introduce local spaces (masking any names that weren't translated). 

To support dynamic state, the environment will support limited dynamic structures akin to tables, maps, or arrays. I haven't decided the nature of these tables yet. There will be mechanisms to browse these tables and to alias records or entries. However, there are no first-class references. Thus, each step must access state, mitigated by incremental computing.

Avoiding references simplifies schema update, static analysis of opportunities for parallelism and concurrency, and avoids implicit garbage collection. It is also consistent with glas programs avoiding first-class functions, objects, and function pointers.

### Scoped State

I propose to divide application state into a few scopes: persistent, ephemeral, transaction-local, and method-local. 

* Persistent state is ultimately stored in a shared database and may serve as shared memory between applications, or a basis for maintaining data between past and future instances of a single application. 
* Ephemeral state is local to the process or runtime, implicitly cleared when the application is halted or reset. Open files or network sockets would generally be modeled as abstract ephemeral state, but developers may introduce their own.
* Transaction-local state is ephemeral state that is implicitly cleared when a transaction commits. This is useful in context of multi-step transactions, such as remote procedure calls.
* Method-local state is essentially local vars, introduced structurally within a program then reset when that structure exits.

Only method-local state is introduced within a program. The other scopes might instead be distinguished based on naming conventions, i.e. by introducing special characters into names.

### Accept States

We can introduce annotations or naming conventions to indicate that specific variables must be in a specific state before a transaction terminates, otherwise the transaction is not accepted. Naming conventions could involve a simple suffix on the variable name. 

Use of accept states is convenient for ensuring consistency, especially in context of multi-step transactions like remote procedure calls or use of coroutines. It can also serve a similar role as linear types.

### Data Stack

It is feasible to model a data stack in terms of method-local variables. However, it may be more convenient to assume a local data stack of sorts for primitive operations, i.e. for concision and simplicity of expression (instead of parameterizing every operation with var names). In that case, we'll likely want program operators or annotations that control access to the stack. 

It should be feasible to verify static arity of the data stack, and also support dynamic data stack for recursive methods. However, we might introduce some annotations restricting subprograms to a static data stack. That restriction might even apply to glas systems by default.

### Abstraction of State

Abstraction of vars is convenient for working with open files, network sockets, and user-defined abstract objects that are held in client-provided state. We could use annotations to logically 'seal' a volume of vars, such that the same seal must be used to access that state. Abstraction of state serves a similar role as abstract data types but is much easier to eliminate at compile-time and avoids potential schema update concerns.

### Typed State

We can use annotations to express assumptions on the type of state. These assumptions can be checked for consistency within a program, and also tracked for persistent state shared between apps. We might indicate some types such as 32-bit integers even though glas supports unbounded integers, influencing representation. A transaction that attempts to assign data outside the accepted type may be aborted.

### Shadow Guard

It should be feasible to annotate an assumption that certain var names or handler names aren't in use, such that we can extend without accidental shadowing. We could use a similar mechanism as the toplevel namespace, reporting a warning or error names are shadowed by non-equivalent definitions.

### Parallelism and Concurrency

The simple call-return structure in most procedural or functional programming is not always the most convenient. In some cases, we might want to express a program as a set of subprograms cooperating to achieve an answer. 

I propose to express this in terms of coroutine-inspired structures. A program may 'yield' and await some condition or signal from other coroutines. This signal or condition may be tied to vars or state, could feasibly be coupled to a notion of "channels" or similar. Logically, there is a single thread of control and scheduling is deterministic. However, with careful design - partitioning of state and signals, waiting on and writing to different channels, etc. - it should be feasible to evaluate multiple coroutines in parallel. 

I'm still very unclear on the details here, but it should be feasible to express many procedures as Kahn Process Networks. Conveniently, this approach - together with avoidance of first-class references for state or channels - allows me to start with conventional call-return structure and extend later.

*Note:* compatible with other forms of parallelism: lazy eval and sparks, evaluating multiple 'step' transactions in parallel, dataflow parallelism, potential SIMD, and possible use of accelerated models.

## Partial and Static Eval

I propose to support partial eval primarily via annotations, e.g. we could indicate our assumption that a certain var is computed statically, and raise a fuss at compile time if this assumption is invalid. In context of non-deterministic choice, static eval could also have multiple static outcomes.

I've contemplated more structural approaches to static eval, e.g. staged arguments, but most such approaches seem awkward and complicated, especially in context of conurrency. I hope to to avoid code being written several times for different "temporal alignment". So, separate our assumptions.

Static eval can interact flexibly with static analysis, e.g. in case of `if (Cond) then Expr1 else Expr2` we could permit Expr1 and Expr2 to have different data stack arity and return types in context of a static condition. Of course, this generalizes to dependent types.

## Non-Deterministic Choice

Non-deterministic choice should be explicit within a program. It could be modeled as an algebraic effect or a primitive, but I currently lean towards algebraic effects because they allow for more flexible processing of non-deterministic code.

## Metaprogramming

The namespace layer has metaprogramming. In addition to namespace macros, it should be possible to capture definitions introduced by a namespace procedure. However, this isn't trivial in context of non-deterministic choice, and it cannot capture primitive definitions or those introduced by shared libraries, and it isn't specific to a call site or static parameters.

I'll want additional metaprogramming at the program model to interpret static data into local subprograms. We'll need a recursive or fractal namespace within the program, perhaps integrated with algebraic effects and local variables. Further, for the general case, we'll need an effects API to query the runtime or compiler for global definitions. This might require a mechanism to 'quote' a name or AST into data for lookup, and some mechanism to 'eval' with global names independent of localization. These might be modeled as reflection APIs, secured similarly to FFI.

These features can simplify metaprogramming in cases not explicitly designed for it, but users may also explicitly model intermediate languages, staging, accelerated interpreters, etc. to support metaprogramming.

### Effects Handlers

Algebraic effects handlers can share a namespace with application state and local vars. 

It seems feasible to integrate the full namespace model, including iterative definitions and lazy loading via non-deterministic choice. This might be convenient for metaprogramming. But it's also fractal in nature, which might prove awkward. Anyhow, even if we lack the full namespace model, we should at least have 

An effects handler will need access to two scopes: the caller's scope (parameters and local algebraic effects of caller) and the host scope (where the effect is defined). This might be supported via implicit translation. 

### Unit Types

I would like good support for unit types early on when developing glas systems. But I don't have a good approach for this yet. Annotations seem the simplest option to start, perhaps bound to the vars.

### Stowage

### Memoization

such that code can be organized as a composition of generators and consumers, without any first-class structures. Most interaction models won't play nicely with hierarchical transactions. However, this *might* be achieved via capturing some local variables into 'objects' for algebraic effects. Perhaps there is something simple we can use, based on explicit yield and continue.

## Basic Operations

Everything is an expression and has a value. For procedural operations, that value is often unit.

* `(%seq Op1 Op2 ...)` - 

## Design Thoughts

* For number types, I want unbounded integers, rationals, complex numbers, and vectors or matrices to be the default. But ideally the program model should make it easy to identify and isolate subprograms where we can use bounded number representations to optimize things. 
* Ideally, every program has a clear small-step rewrite semantics. This greatly simplifies debugging.

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.
