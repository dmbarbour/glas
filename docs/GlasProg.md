# Program Model for Glas

The [namespace](GlasNamespaces.md) supports modules and user-defined front-end syntax. Programs are compiled to an AST structure built upon '%\*' primitives. This document describes a viable set of primitives for my vision of glas systems and some motivations for them.

## Proposed Program Primitives

These primitives are constructors for an abstract data time, i.e. constructing a program does not execute it. The %macro and %load primitives are special exceptions, lazily evaluating at the namespace layer to support metaprogramming and modularity.

*Notation:* `(F X Y Z)` desugars to `(((F,X),Y),Z)`, i.e. curried application. 

### Control Flow

* `(%do P1 P2)` - execute P1 then P2 in order. Associative.
* `%pass` - the no-op. Does nothing.
* `%fail` - voluntary failure. Used for bracktracking a branch condition, choice, or coroutine step. (In contrast, errors are treated as divergence, i.e. infinite loops observable only via reflection APIs.)
* `(%cond Sel)` - supports if/then/else and pattern matching. The selector, Sel, has a distinct AST structure to support sharing a common prefix. Sel constructors:
  * `(%br Cond Left Right)` - runs branch condition, Cond. If Cond fails, backtracks to run Right selector, otherwise processes Left selector. The full chain of branch conditions runs atomically.
  * `(%sel P)` - selected action. Commits prior chain of passing branch conditions, then runs P.
  * `%bt` - backtrack. Forces most recent passing Cond to fail. If no such Cond, i.e. if %bt is rightmost branch, behavior is context-dependent (e.g. error for %cond, exit for %loop). As a special rule, we optimize `(%br C %bt R) => R` regardless of whether C is divergent.
* `(%loop Sel)` - Repeatedly runs Sel until it fails to select an action, then exits loop. Same Sel structure as %cond. Essentially a hybrid of while-do and pattern matching.
* `(%co P1 P2)` - execute P1 and P2 as coroutines with a non-deterministic schedule. Associative. Each coroutine operates on its own stack but shares access to registers and methods.
  * Preemption: scheduler may freely abort a coroutine to select another. 
  * Parallelism: run many, abort a subset to eliminate conflicts, aka [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control).
  * Fork-join behavior: %co yields repeatedly until coroutines terminate. We can optimize the case where %co is the final operation of a coroutine, thus no join required.
* `%yield` - within a coroutine, commit operations since prior yield. Each yield-to-yield step is an atomic, isolated transaction that may abort via fail.
* `(%atomic P)` - runs P within a hierarchical transaction, thus yielding within P does not yield from atomic and must be resumed within P. 
  * *Note:* a chain of %br branch conditions, up to %sel, is implicitly atomic.
* `(%ch P1 P2)` - non-deterministic choice of P1 or P2. Associative. Can be implemented by forking the transaction and evaluating all choices, but only one can commit. 
  * Special case: in context of transaction loops, e.g. `while (Cond) { atomic Action; yield }`, repeated choice can optimize into a reactive form of concurrency. 
* `%error` - explicit divergence. Logically equivalent to an infinite no-yield loop, but much easier to optimize. Please compose with `%an.error.log` to attach a message!

*Note:* For %do, %co, and %ch, it is *very tempting* to support a variable number of arguments, but directly doing so complicates semantics. A viable approach to variable arguments involves a front-end language Church-encoding lists of ASTs into an argument.

### Data Stack

* `d:Data` - push data to top of data stack
* `(%dip P)` - run P while hiding top element of data stack
* `%swap` - exchange top two stack elements. i.e. "ab-ba"
* `%copy` - copy top stack element, i.e. "a-aa".
* `%drop` - drop top stack element, i.e. "a-".
* `%mkp` - "ba-(a,b)" pair elements, right element starts on top
* `%mkl` - rewrite top stack element to be left branch of tree
* `%mkr` - rewrite top stack element to br right branch of tree
* `%unp` - undoes mkp, fails if not a pair.
* `%unl` - undoes mkl, fails if not a left branch
* `%unr` - undoes mkr, fails if not a right branch

### Registers

* `(%xch Register)` - exchange value of register and top item of data stack.
  * *Static analysis*: you can model this as also swapping *types* (or logical locations) between the register slot and the stack. That lets checkers propagate stack‑effect typing and enforce invariants.  
  * *Optimization hint*: unconditional, atomic patterns such as `(%rw x; ... ; %rw x)` can be heavily optimized because logical locations are restored.
  * *Concurrency semantics*: For fine-grained conflict analysis, compiler built-in accelerators can define common patterns such as get and set, queue or bag reads and writes, indexed operations on arrays or dicts, or even support a few CRDTs. However, these interactions are not modeled as primitives.
* `(%local RegOps)` - allocates a fresh register environment, passes it to `RegOp : Env -> Program`, runs Program, then clears the environment. The Env logically defines every Name to a unique register, but Program must use only a static, finite subset of these names.
* `(%assoc R1 R2 RegOps)` - this binds an implicit environment of registers named by an ordered pair of registers `(R1, R2)`. The primary use case is abstract data environments: an API can use per-client space between client-provided registers and hidden API registers.

### Metaprogramming

* `(%macro Builder)` - Builder represents a program of 0--1 arity, and is expected to return a closed-term AST representation on the data stack. This returned AST is validated, lazily evaluated in an empty environment, then substituted in place of the macro node. Because AST is closed term, external linking must be provided in context. 
* `(%eval Adapter)` - pop arbitrary Data from the stack, pass to Adapter - a namespace-layer function of type `d:Data -> Program`. Adapter typically includes %macro nodes for staged compilation of Data. The Program is subject to validation in context (e.g. verify type). Although dynamic eval is feasible, glas systems frequently forbid dynamic eval, requiring static Data argument (`%an.eval.static` by default).

Non-deterministic metaprogramming is not *necessarily* an error, but it complicates reasoning and caching, requires expensive backtracking and heuristic search. Glas systems shall reject non-determinism in metaprogramming until they're mature enough to properly tackle these challenges.

Both %macro and %eval serve at the boundary between namespace and program layers. There is also some metaprogramming possible purely in the namespace layer, e.g. we could build and process Church-encoded lists of ASTs.

### Modularity Extensions

* `(%load Src)` - Load external resources at compile time. The result is embedded data that may be processed further via %macro. Errors are possible, e.g. if Src is malformed or unreachable, in which case this operation logically diverges.
* `%src.*` - abstract Src constructors, e.g. to read local files, load from DVCS, search folders, possibly even look into a database.

See [namespaces](GlasNamespaces.md) for details.

## Calling Conventions

Definitions in the namespace should be tagged to indicate integration. A carefully designed set of tags can significantly simplify extension and metaprogramming. Proposed:

* "data" - `Data` - embedded data, can integrate as program
* "prog" - `Program` - abstract program, can integrate as program
* "call" - `Env -> Def` - receives caller context (algebraic effects, pass-by-ref registers, etc.), returns another tagged definition. 
  * We can develop further conventions around Env, e.g. supporting keyword or variable arguments.
* "list" - Church-encoded list of tagged ASTs, useful for aggregations or variable arguments. Not necessarily homogeneous.

In my vision, most definitions are tagged "call" and return "prog", except near the edges where we might have a lot of "prog" and "data" definitions. Use of "list" would be rare outside of aggregators and var-args, and requires specialized processing by an adapter. Eventually, we'll also have many non-callable tags, such as "type". We might also support multiple inheritance graphs in a generic way, and develop specialized tags for grammars, process networks, etc.. 

## Annotations

        a:(Annotation, Op)    # dedicated AST node

Acceleration:
* `(%an.accel Accelerator)` - performance primitives. Indicates that a compiler or interpreter should substitute Op for a built-in Accelerator. By convention, Accelerators have form `(%accel.OpName Args ...)` (or `%accel.OpName` if no arguments). Accelerators are not Programs, and are only useful in context of `%an.accel`. 

Instrumentation:
* `(%an.log Chan MsgSel)` - printf debugging! Logging will *overlay* an Operation, automatically maintaining the message. The MsgSel type is sophisticated; see *Logging*.
* `(%an.error.log Chan MsgSel)` - log a message only when Operation halts due to an obvious divergence error (such as '%error', assertion failure, or a runtime type error).
* `(%an.assert Chan ErrorMsgGen)` - assertions are structured as logging an error message. If no error message is generated, the assertion passes. May reduce to warning.
* `(%an.assert.static Chan ErrorMsgGen)` - assertion that must be computed at compile-time, otherwise it's a compile-time error. May reduce to compile-time warning with or without a runtime error.
* `(%an.profile Chan BucketSel)` - record performance metadata such as entries and exits, time spent, yields, fails, and rework. Profiles may be aggregated into buckets based on BucketSel. 
* `(%an.trace Chan BucketSel)` - record information to support slow-motion replay of Operation. BucketSel helps control and organize traces. See *Tracing*.
* `(%an.view Chan Viewer)` - support interactive debug views of a running application. See *Debug Views*
* `(%an.chan.scope TL)` - apply a prefix-to-prefix translation to Chan names in Operation.

Validation:
* `(%an.arity In Out)` - express expected data stack arity for Op. In and Out must be non-negative integers. Serves as an extremely simplistic type description. 
* `%an.atomic.reject` - error if running Operation from within an atomic scope, including %atomic and %br conditions. Useful to detect errors early for code that diverges when run within a hierarchical transaction, e.g. waiting forever on a network response.
  * `%an.atomic.accept` - to support simulation of code containing %an.atomic.reject, e.g. with a simulated network, we can pretend that Operation is running outside a hierarchical transaction, albeit only up to external method calls.
* `(%an.data.seal Key)` - operational support for abstract data types. For robust data sealing, Key should name a Register, Src (like '%src'), or other unforgeable identity. Sealed data cannot be observed until unsealed with a matching Key, usually symmetric. If the Key becomes unreachable (e.g. Register out of scope), the sealed data may be garbage collected, and this may be detectable via reflection APIs. Actual implementation is flexible, e.g. compile-time static analysis at one extreme, encryption at another, but simple wrappers is common.
  * `(%an.data.unseal Key)` - removes matching seal, or diverges
  * `(%an.data.seal.linear Key)` - a variant of seal that also marks sealed data as linear, i.e. no copy or drop until unsealed. Note: This does not fully guard against implicit drops, e.g. storing data into a register that falls out of scope. But a best and warnings are expected.
    * `(%an.data.unseal.linear Key)` - counterpart to a linear seal. If data is sealed linear, it must be unsealed linear.
* `%an.data.static` - Indicates that top stack element should be statically computable. This may propagate requirements for static inputs back through a call graph. In context of conditionals, choice, coroutines, etc. the compiler can feasibly attempt to verify that all possible paths (up to a quota) share this result.
* `%an.eval.static` - Indicates that all '%eval' steps in Operation must receive their AST argument at compile-time. This is the default for glas systems, but it can make intentions clearer to reiterate the constraint locally.
* `(%an.type TypeDesc)` - Describes a partial type of Operation. Not limited to programs, so namespace-layer and higher-kinded types are also relevant. Can also support type inference in the context surrounding Operation. TypeDesc will have its own abstract data constructors in '%type.\*'.
* `%an.det` - Annotates an `Env -> Program` structure. This expresses the intention that Program should be deterministic *up to Env*. A compiler should prove this or raise an error. 
  * The simplest proof is that Program doesn't use '%co' or '%choice' or interact with mutable state (even indirectly) except through Env. 
  * Ideally, we can also recognize simple confluence patterns, e.g. Kahn Process Networks, where coroutines communicate through queues with clear ownership (no races between two readers or between two writers). 
  * Eventually, proof-of-confluence annotations may be viable. Not sure how feasible this is.

Laziness:
* `%an.lazy.thunk` - The simplest integration for lazy evaluation for laziness. Op must be pure, atomic, 1--1 arity, terminating - anything else is a type error, though perhaps only detected upon 'force'. Instead of computing immediately, we return a thunk representing the future result. 
  * Non-deterministic Op is accepted, i.e. commit to a non-deterministic choice without observing the result. An intriguing opportunity is to only choose the value for a non-deterministic thunk after an observing transaction commits. This is formally valid with non-determinism.
* `%an.lazy.force` - Op (usually %pass) must return a thunk at top of data stack. We force evaluation of the thunk before returning, placing the data result of evaluating that thunk on the stack. 
  * Force diverges if computation represented by a thunk fails or diverges. This is considered a type error.
* `%an.lazy.spark` - Op (usually %pass) must return a thunk at top of data stack. If the thunk has not already been computed or scheduled, we'll schedule that thunk for background computation by runtime worker threads. 

Content-addressed storage:
* `%an.cas.stow` - Op (usually %pass) must return data of persistent or global ephemerality at top of stack. We wrap that data then lazily move it to external storage based on size, usage, and memory pressure.
* `%an.cas.load` - Op (usually %pass) must return stowed data at top of stack. Loads and substitutes the actual data. Loading may be lazy, but only when the runtime is confident it can fully load the data (accounting for risks of network disruption and invalid representation). Diverges if the data cannot be loaded. Reflection APIs may offer a detailed view of errors.
* `%an.cas.need` - Op (usually %pass) must return stowed data at top of stack. Tells runtime that this data will be needed in the near future. This enables the runtime to heuristically download, validate, etc. the data ahead of time so it's more available when needed.

Incremental computing:
* `(%an.memo MemoHint)` - memoize a computation. Useful memoization hints may include persistent vs. ephemeral, cache-invalidation heuristics, or refinement of a 'stable name' for persistence. TBD. 
  * As a minimum viable product, we'll likely start by only supporting 'pure' functions, because that's a low-hanging, very tasty fruit.
* `(%an.checkpoint Hints)` - when retrying a transaction, instead of recomputing from the start it can be useful to rollback partially and retry from there. In this context, a checkpoint suggests a rollback boundary. A compiler may heuristically eliminate unnecessary checkpoints, and Hints may guide heuristics. 

Guiding non-deterministic choice: 
* `(%an.cost Chan CostFn)` - (tentative) emits a heuristic 'cost'. CostFn has type `Env -> Program`, with Env providing access to Chan configuration options, ultimately returning a non-negative rational number on the data stack. Like logging, the Program also has implicit access to the host environment for dynamic costs. The only role of costs is to guide non-deterministic choice, disfavoring "high cost" options - or choices that will obviously lead to high costs later on.
  * Beyond tweaks by CostFn based on Chan configuration, a user configuration could amplify or suppress costs per channel, enabling an encoding of purpose and preference into channel names.
  * *Aside:* In theory, we could support non-monotonic costs to represent gains, too. But all the efficient search algorithms assume monotonicity. 

Future development:
* type declarations. I'd like to get bidirectional type checking working in many cases relatively early on.
* tail-call declarations. Perhaps not per call but rather an indicator that a subroutine can be optimized for static stack usage, optionally up to method calls. 
* stowage. Work with larger-than-memory values via content-addressed storage.
* debug trace. Probably should wait until we have a clear idea of what a trace should look like. 
* debug views. Specialized projectional editors within debuggers.

### Logging

        a:((%an.log Chan MsgSel), Operation)
        
        type Chan is AST of form d:Name     
          # naming conventions apply

        type MsgSel : Env -> Sel                    # where
          (%cond (%br %pass Sel %fail)) : Program   

        type Msg is plain old glas data, often a Text

Logging overlays Operation. When Operation is a no-op (`%pass`), this reduces to conventional one-off logging. However, more generally, we may recompute messages when Operation yields or halts on error, heuristically at checkpoints or stable failure. Periodic or random sampling is also viable, perhaps heuristically tuning frequency based on performance. This behavior may be configurable per Chan, a precision versus performance decision.

Due to this overlay structure, it is useful to render logs as a time-varying tree structure. Instead of a simple stream of text, a log should be serialized as a stream of events on a tree, e.g. add and remove nodes. Non-deterministic choice adds another dimension to this tree, at least insofar as options are evaluated in parallel.

Messages are computed within hierarchical transactions that are aborted after extracting the message. MsgSel may destructively inspect registers and data stack in scope, or invoke atomic operations as a 'what if'. However, data stack is a special case: it is in scope only when Operation is `%pass`, in which case MsgSel has the same access as the surrounding Program.

The runtime provides the Env argument. Exact contents depend on runtime version and configuration-provided adapters, but should de facto stabilize. The role of Env is to provide reflection APIs, e.g. to read %src, inspect the call stack or data stack, or query configured Chan settings. Configured Chan settings are fully arbitrary, e.g. preferred language, accepted format, level of detail, and degree of sarcasm. Many queries can be completed at compile-time, allowing for partial evaluation and specialization of logging code.

If MsgSel uses non-deterministic choice, the runtime may generate all possible messages (subject to configuration). With runtime support, users may configure a non-deterministic choice of Chan settings, such that we log multiple versions of messages.

### Tracing (TBD)

        a:((%an.trace Chan BucketSel), Operation)

We can ask the runtime to record sufficient information to replay a computation. This is expensive, so we might configure tracing (per Chan) to perform random samples or something, switching between traced and untraced versions of the code.

What information do we need?

* input registers - initial, updates after yield
* input register updates and return values from calling untraced methods 
* stream of non-deterministic choices and scheduling for replay
  * distinguish backtracked choices to allow skipping them
* for long-running traces, heuristic checkpoints for timeline scrubbing 
* for convenience, complete representation of subprogram being traced
  * content-addressed for structure sharing
* consider adding contextual stack of log messages etc.

I think this won't be easy to implement, but it may be worthwhile.

BucketSel is just a means to conditionally disable tracing. Similar to MsgSel except only evaluated once, and the returned bucket(s) are simply dynamic indices for lookup.

### Debug Views

An intriguing opportunity: *interactive views* of running code. 

        (%an.view Chan Viewer)

Viewer may have type `Env -> Program` where the Env includes both channel configuration options and a view context of callbacks and registers. View callbacks support ad-hoc queries (level of detail, user preferences, content-negotiation) and a stream of writes (graphics and texts, GUI update commands, etc.). View registers are opaque to the user but held across requests, supporting persistence of navigation, progressive disclosure, or even retained-mode GUI (by tracking what has already been written). A client may fork, checkpoint, or freeze the view by controlling context.

Like logging, the viewer program runs in a hierarchical transaction. By default, updates to the application are undone after the program returns, while updates to the view context are retained. However, we can introduce a 'commit' callback in Env to change behavior on a per-call basis. This essentially enables editing of local registers in a running application through integrated debug views. Such edits may be rejected, e.g. because the user doesn't agree, or due to read-write conflict with concurrent operations. The [Glas GUI](GlasGUI.md) design document describes some relevant patterns.

The Chan can also serve a role of naming a view for discovery and integration. The compiler can warn if there is more than one view per Chan within an application. An application may serve as its own client through a reflection API (perhaps sys.refl.view.\*), thus serving debug views through non-debugger interfaces.

### Accelerators

        (%an.accel (%accel.OpName Args))

*Convention:* For pure representation transforms, such as asking a runtime to represent a list as an array under the hood, I express this as "acceleration" of a no-op. Representation transforms are naturally slower than a no-op, but exist only to support other accelerators.

List Ops:
* len
* append
* split
* index get/set/swap

Dict Ops:
* insert 
* remove
* count
* keys

Bitstring Ops:
* len
* invert
* reverse
* split
* append

Arithmetic
* Sum
* Product
* Negation
* Reciprocal

Register Ops:

* Cell
  * Get
  * Set
* Queue
  * Read
  * Peek
  * Unread
  * Write
* Bag
  * Put
  * Grab
  * Peek
* KVDB, implicit 'register' per key but dynamic

## TBD


### In-Place Update? Defer.

It is possible to support in-place update of 'immutable' data if we hold the only reference to its representation. This can be understood as an opportunistic optimization of garbage-collection: allocate, transfer, and collect in one step. In glas programs, this would be feasible with accelerators, such as a list update operator could swap a list element without reallocatng the list. This is especially useful if the list is represented by an array.

However, pervasive use of transactions and backtracking complicates this optimization. It is convenient to capture a snapshot of registers so we can revert if necessary. Although this snapshot isn't a logical copy and thus doesn't conflict with linear types, it is a shared representation and thus does hinder in-place update.

A viable alternative is to maintain a 'log' of updates to apply later. For example, a runtime could feasibly represent the updated list as a special `(update log, original list ref)` pair within runtime. This might generalize to [log-structured merge-tree (LSM trees)](https://en.wikipedia.org/wiki/Log-structured_merge-tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

This doesn't quite support the ideal of in-place update. We must allocate that log, and perhaps some metadata to track elements to process further upon commit. But perhaps we can still perform in-place update upon commit, and benefit from editing nearer to the tree root. This seems a viable approach.

Meanwhile, we'll still support decent persistent data structures by default, e.g. finger-tree ropes still support O(log(N)) updates in the center, O(1) at the edges, and we can easily use a pair as a gap buffer.

### Tail Call Optimization

I'd suggest unrolling a recursive loop a few frames then determining whether we can 'recycle' the stack locations. Ideally, TCO can be enforced via annotations, e.g. by specifying that a subprogram has a finite stack, or that an `Env -> Prog` is finite-up-to Env.

### Unit Types

Attaching unit-types to number representations is inefficient. Recording them into type annotations makes units difficult to use, e.g. for printing values. An interesting possibility, however, is to track units for *registers*, aiming for static computation of unit registers. We could use '%assoc' to associate unit registers with a number register.

### Memoization

In context of procedural programming, memoization involves recording a trace. This trace describes the computation performed (perhaps via hash), the data observed, and outputs written. To execute a memoized computation, we search for a matching trace then write the outputs directly. If no matching trace is found, we run the computation while recording the trace, then add it to the cache.

We can improve memoization by making the traces more widely applicable, abstracting irrelevant details. For example, we might observe that a register contains 42, but a trace might match so long as a register value is greater than zero.

However, even the simplest of traces can be useful if users are careful about where they apply memoization. We can memoize a subset of procedures that represent "pure" expressions or functions to support incremental compilation, monoidal indexing of structure, and similar use cases.

### Lazy Computation

To get started with a simple implementation, I propose explicit thunks of 1--1 arity, pure (but optionally non-deterministic), atomic computations. Computation may fail or diverge, in which case forcing the thunk will diverge. 

In case of non-deterministic lazy computations, the outcome remains non-deterministic until a thread commits AFTER force. This allows for expression of lazy choice, lazy entanglement, and searching outcomes.

Eventually, we might extend laziness to multiple stack inputs or read-only snapshots of registers. But doing so is difficult with explicit thunks. And I don't feel comfortable with a move to implicit thunks without proofs of timely (e.g. polynomial) termination, static analysis of linear type dataflow, and similar features.

*Note:* Because lazy annotations influence observation of divergence, I'm tempted to move from annotations to primitives.

### Futures, Promises, Channels

It isn't difficult to extend laziness with explicit 'holes', i.e. such that a program can allocate a `(future, promise)` pair. We'll need some integration with linear and non-linear data, i.e. allowing for linear and non-linear futures. This extends very naturally to channels, e.g. via including another future in the result, or by assigning a sequence of values to a promise.

It isn't difficult to present holes as a program primitive. But holes are fundamentally impure: they introduce identity. I think it's probably better to model them as part of a runtime-provided effects API.

### Accelerators

Essentially, primitives with a reference implementation.

        (%an (%an.accel (%accel.OpName Args)) Op)

Accelerators ask a compiler or interpreter to replace Op with an equivalent built-in implementation. The built-in should offer a significant performance advantage, e.g. the opportunity to leverage data representations, CPU bit-banging, SIMD, GPGPU, etc.. Arguments to an accelerator may support specialization or integration.

Ideally, the compiler or interpreter should verify equivalence between Op and Accelerator through analysis or testing. However, especially in early development and experimentation phases, it can be awkward to maintain Op and Accelerator together. During this period, we may accept `()` or an undefined name as a placeholder, emitting a TODO warning.

Accelerators support 'performance primitives' without introducing semantic primitives. If we build upon a minimalist set of semantic primitives, we'll be relying on accelerators for arithmetic, large lists, and many other use cases.

### Breakpoints

        (%an.bp Chan BucketSel)

We could feasibly annotate conditional breakpoints into a program. Ideally, we'll integrate the notion of overlay breakpoints, e.g. breaking when conditions are met.

Not sure exactly what I want here, however.

### Content-Addressed Storage

Annotations can transparently guide use of content-addressed storage for large data. The actual transition to content-addressed storage may be transparently handled by a garbage collector. Access to representation details may be available through reflection APIs but should not be primitive or pervasive.

### Environment Abstraction

Instead of only abstracting data, it can be useful to abstract volumes of the environment. This allows us to develop APIs where the client provides a location, but cannot access the associated data.

Modeling this in names is too awkward. However, it is feasible to introduce a corollary to '%local' for binding associated names. In this case, our goal is to draw an 'arc' between two registers and treat it as a new prefix of registers.

### Type Descriptions

        (%an (%an.type TypeDesc) Op)

We can just invent some primitive type descriptions like '%type.int' or whatever, things a typechecker is expected to understand without saying, and build up from there. It isn't a big deal if we want to experiment with alternatives later.

Some thoughts:
- Instead of hard-coding a few types like 'i64' or 'u8', consider an `(%type.int.range 0 255)` or similar. This would allow for more flexible packed representations and precise tracking of precision across arithmetic steps, e.g. adding u8 and u8 has range 0 to 510 (not quite a u9), and we can require explicit modulus or conditionals to store result back into a u8
- we could feasibly use 'int range' types as parameters to list length types, to support vectors of exactly one size (range 32 to 32) or more sizes.
- obviously need a const type that supports only a single value, too.

### Reflection, Transpilation, Alternative Models

The program model provides a foundation for glas systems, but I'm interested in exploring alternative foundations and support for compilation between them. As I see it, there are a few opportunities here:

- Reflection APIs: a runtime or compile-time could provide some APIs to inspect definitions.
  - However, this solution seems semantically troublesome because we'd either be observing recursive definitions *after* substitution of names for definitions is applied, or be observing some runtime-specific internal representation.
- Quotation APIs: a front-end language can support quoting of definitions or expressions into an embedded data representation of an AST. Further, it could support quoted imports, i.e. load binary source code or compile to the AST representation of `Env->Env` without evaluating it.
  - This solution is logistically troublesome. We'll need some way to efficiently cache definitions for macro evaluation when building a larger namespace.

Of these options, I think a foundation of quotation APIs offers the more robust solution. We can tackle logistical challenges by essentially integrating a compiler as a shared library and some clever use of caching and acceleration. 

