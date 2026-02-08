# Program Model for Glas

## Proposed Primitives

Programs are modeled as an abstract data type, and most program primitives are constructors for this type. Special exceptions are '%load' and '%macro' which support modularity and metaprogramming.

*Notation:* `(F X Y Z)` desugars to `(((F,X),Y),Z)`, i.e. curried application. 

### Control Flow

* `(%do P1 P2)` - execute P1 then P2 in order. Associative.
* `%pass` - the no-op. Does nothing.
* `%fail` - voluntary failure. Used for bracktracking a branch condition or coroutine step. (In contrast, errors are treated as divergence, i.e. infinite loops observable only via reflection APIs.)
* `(%cond Sel)` - Sel is an abstract data type modeling a decision tree in terms of backtracking conditional operations. Error if no operation is selected. Sel constructors:
  * `(%br C L R)` - here C is a program that may fail, representing a condition. We run C. On failure, we undo writes from C then evaluate selector R, otherwise continue with selector L.
  * `(%sel Op)` - final selection, runs Op.
  * `%bt` - backtrack. logically causes prior condition to fail. As a special rule, we can optimize `(%br C %bt R) => R` even when C is divergent.
* `(%loop Sel)` - Repeatedly runs Sel until it fails to select an action, then exit loop. (See %cond for Sel constructors.)
* `(%opt P1 P2)` - fair, non-deterministic choice of P1 or P2. Associative.
* `%error` - explicit divergence, logically equivalent to an infinite loop. 
  * use `%an.error.log` to attach a message to errors.
  * as an optimization, %error can backtrack to prior %opt in context of transaction loops because the choice is repeated.

The %do and %opt constructors have exactly two arguments. It is tempting to support variable arity, but it isn't directly supported by namespace semantics. We could instead use a Church-encoded list to express variable arguments, but it won't save space.

### Data Stack

* `(%data d:Data)` - pushes Data onto stack
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

The glas system relies heavily on *acceleration* for performance.

Data logically consists of binary trees. Large trees spill into an implicit heap. A glas runtime may support specialized representations for use with accelerators or insert metadata to enforce dynamic types. 

### Registers

Registers are second-class. Access is provided through the namespace. Aside from storing data, registers also serve as a source of identity and ephemerality, e.g. for data abstraction and associative structure.

* `(%rw Register)` - register swap; exchange data between stack and register.
* `(%local RegOps)` - RegOps must be a namespace term of type `Env -> Program`. This receives an environment of registers, such that every name defines a register initialized to zero. The program is run in this context, then registers are logically reset (i.e. error if registers contain linear data).
* `(%assoc R1 R2 RegOps)` - This supports associative structure. Every directed edge between two registers is treated as naming an entire environment of registers. This is mostly useful for secure API design. 

The glas system can accelerate register operations. This is expressed in terms of providing registers as a static arguments to accelerated `Env -> Program` functions. If a register is used primarily through accelerated queue operations, the compiler can treat it as a queue for optimizing concurrency or distribution.

### Metaprogramming

* `(%macro Builder)` - Builder must be a deterministic 0--1 arity program that returns a closed-term namespace AST on the data stack. Returns the evaluated namespace term.
* `(%eval Compiler)` - Compiler must be be a namespace term of type `Data -> Program`. The data argument is popped from the stack then passed to Compiler as embedded data. The returned program is validated in context (e.g. static assertions, typechecks), optimized or JIT-compiled, then run.
  * In practice, we'll often restrict %eval to compile-time via `%an.data.static` or `%an.eval.static`. Static %eval is more flexible than %macro alone insofar as program dataflow may cross namespace scopes.
  * Although the glas program model does not support first-class functions at runtime, dynamic %eval with caching can serve a similar role.

A powerful design pattern is to accelerate metaprograms. For example, we can develop a memory-safe intermediate language and reference interpreter for a virtual CPU or GPGPU, then 'accelerate' by compiling for actual hardware. This pattern replaces the performance role of FFI.

*Aside:* We can also do metaprogramming at the namespace layer via Church-encoded lists and data. Tags and adapters are a limited example of this.

### Modularity Extensions

* `(%load Src)` - loads external resources (usually files) at compile time. Use in conjunction with %macro to actually process the data. Diverges if Src is malformed, unreachable, or there are permissions issues.
* `%src.*` - abstract Src constructors, e.g. to specify a relative file path or DVCS repository.

See [namespaces](GlasNamespaces.md) for details.

## Calling Conventions

Program definitions in the namespace are tagged to indicate calling conventions. The front-end compiler must insert an adapter at every call site. Initial calling conventions:

* "data" - `Data` - embedded data, wrap %data then integrate
* "prog" - `Program` - abstract program, directly integrate
* "call" - `Env -> Def` - implicitly parameterized by caller namespace with syntactically specified translations, then apply adapter again to returned Def.

We can gradually support more calling conventions, e.g. so we can directly 'call' grammars or logic programs. 

## Transaction Loops

The main loop of a [glas application](GlasApps.md) is repeating calls to a transactional 'step' method. This forms a transaction loop.

A transaction loop is any atomic, isolated ([serializable](https://en.wikipedia.org/wiki/Isolation_(database_systems)#Serializable)) transaction, run repeatedly. This structure supports many simplifications and optimizations:

- *Incremental*: Instead of fully repeating a transaction, we can partially-evaluate based on stable state then repeat only unstable computations. Partial evaluation ideally aligns with dataflow instead of control flow.
- *Reactive*: When we know repetition will be unproductive, instead of burning CPU we can install triggers to await relevant changes. With some clever API design, this includes waiting on clocks.
- *Concurrent*: For serializable transactions, repetition and replication are equivalent. In context of non-deterministic choice, we can run a parallel transaction loop for each series of choices with [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control).
- *Distributed*: Building on concurrency, we replicate a transaction loop across multiple nodes with a distributed runtime. To avoid unnecessary distributed transactions, we heuristically abort transactions better initiated on a remote node.
- *Live*: We can robustly update the transaction procedure between transactional steps. We can model the transaction as reading its own procedure.
- *Orthogonal Persistence*: Application state exists outside the loop. We can easily back application state with durable or distributed storage.

The system provides a mechanism to escape transactional isolation, 'sys.refl.bgcall'. This requests the runtime perform an operation *prior* to the current transactions. This is carefully designed to be compatible with transaction-loop optimizations, though it still requires ostensibly 'safe' operations like HTTP GET.

## State Machines and Control Flow

It isn't difficult to model a multi-step process with a transaction loop. It is sufficient to explicitly maintain state about which 'step' we're on. Further, we can generalize to multi-threading, maintaining a little state about each thread and making a non-deterministic choice about which thread is scheduled.

But there is a significant performance overhead to repeatedly examine the current state, navigate to the correct code, perform a slice of work, then navigate back out. Especially when the process is deeply hierarchical or the slice of work is short.

Ideally, we can optimize this pattern to achieve something close to performance of built-in control flow. My intuition is that this is feasible with a variation on incremental computing where we maintain a cache of control-flow states as we update machine states. Doesn't seem easy to implement, however.

## Distribution

We can develop a distributed runtime that runs on multiple nodes, multiple OS processes. This requires some careful attention to system API design, but otherwise it's just another runtime.

A transaction-loop application, and even runtime event handling like the 'http' and 'rpc' methods, can be mirrored on each node without affecting semantics. Of course, this may require expensive distributed transactions in many cases. But I'd rather solve a performance problem than a semantics issue.

One simple, effective performance heuristic is to abort any step that's better started on another mirror node. After all, the other node is also repeatedly running the step function and will do the work. At most, the runtime needs to communicate expectations. This rule eliminates most *unnecessarily* distributed transactions.

We can further optimize based on recognizing patterns of state usage. For example, a read-mostly register can be mirrored, while a write-heavy register might better 'migrate' to where it's currently being used. A queue's state can be divided between reader and writer nodes (reader diverges when waiting), while a bag (multiset) can be logically partitioned between any number of nodes. A conflict-free replicated datatype (CRDT) can be replicated on every node and synchronized opportunistically.

To support these optimizations, we'll rely on accelerated register operations. For example, a register accessed only via accelerated queue operations can be optimized as a queue. Of course, developers must also design for effective distribution. It could be even more expensive to repeatedly migrate queue endpoints than to access them remotely via distributed transactions.

*Aside:* I intend to design the system API such that we can transition to distributed runtimes with minimal changes.

## Annotations

Annotations are supported at the level of namespace terms, not Programs.

        a:(Annotation, Op)    # namespace AST node

This is important because we'll often annotate `Env -> Program` calls instead of Programs directly.

Acceleration:
* `(%an.accel Accelerator)` - performance primitives. Indicates that a compiler or interpreter should substitute Op for a built-in Accelerator. By convention, Accelerators have form `(%accel.OpName Args ...)` (or `%accel.OpName` if no arguments). Accelerators are not Programs, and are only useful in context of `%an.accel`. 

Instrumentation:
* `(%an.log Chan MsgSel)` - printf debugging! Logging will *overlay* an Operation, automatically maintaining the message. The MsgSel type is sophisticated; see *Logging*.
* `(%an.error.log Chan MsgSel)` - log a message only when Operation halts due to an obvious divergence error (such as '%error', assertion failure, or a runtime type error).
* `(%an.assert Chan ErrorMsgGen)` - assertions are structured as logging an error message. If no error message is generated, the assertion passes. May reduce to warning.
* `(%an.assert.static Chan ErrorMsgGen)` - assertion that must be computed at compile-time, otherwise it's a compile-time error. May reduce to compile-time warning with or without a runtime error.
* `(%an.profile Chan BucketSel)` - record performance metadata such as entries and normal exits, fails, errors, time spent, time wasted on rework, etc.. Profiles may be aggregated into buckets based on BucketSel. 
* `(%an.trace Chan BucketSel)` - record information to support slow-motion replay of Operation. BucketSel helps control and organize traces. See *Tracing*.
* `(%an.view Chan Viewer)` - support interactive debug views of a running application. See *Debug Views*
* `(%an.chan.scope TL)` - apply a prefix-to-prefix translation to Chan names in Operation.

Validation:
* `(%an.arity In Out)` - express expected data stack arity for Op. In and Out must be non-negative integers. Serves as an extremely simplistic type description. 
* `(%an.data.seal Key)` - operational support for abstract data types. For robust data sealing, Key should name a Register, Src (like '$src'), or other unforgeable identity. Sealed data cannot be observed until unsealed with a matching Key, usually symmetric. If the Key becomes unreachable (e.g. Register out of scope), the sealed data may be garbage collected, and this may be detectable via reflection APIs. Actual implementation is flexible, e.g. compile-time static analysis at one extreme, encryption at another, but simple wrappers is common.
  * `(%an.data.unseal Key)` - removes matching seal, or diverges
  * `(%an.data.seal.linear Key)` - a variant of seal that also marks sealed data as linear, i.e. no copy or drop until unsealed. Note: This does not fully guard against implicit drops, e.g. storing data into a register that falls out of scope. But a best and warnings are expected.
    * `(%an.data.unseal.linear Key)` - counterpart to a linear seal. If data is sealed linear, it must be unsealed linear.
* `%an.data.static` - Indicates that top stack element (upon return from Op) should be statically computable. Instead of analyzing for static dataflow, a glas compiler might take this as a hint for aggressive partial evaluation then directly verify.
* `%an.eval.static` - Indicates that all '%eval' steps in Operation must receive their AST argument at compile-time. This is the default for glas systems, but it can make intentions clearer to reiterate the constraint locally.
* `(%an.type TypeDesc)` - Describes a partial type of Operation. Not limited to programs, so namespace-layer and higher-kinded types are also relevant. Can also support type inference in the context surrounding Operation. TypeDesc will have its own abstract data constructors in '%type.\*'.
* `%an.det` - Annotates an `Env -> Program` structure. This expresses the intention that Program should be deterministic *up to Env*. A compiler should prove this or raise an error. 
  * The simplest proof is that Program doesn't use '%co' or '%choice' or interact with mutable state (even indirectly) except through Env. 
  * Ideally, we can also recognize simple confluence patterns, e.g. Kahn Process Networks, where coroutines communicate through queues with clear ownership (no races between two readers or between two writers). 
  * Eventually, proof-of-confluence annotations may be viable. Not sure how feasible this is.

Laziness:
* `%an.lazy.thunk` - Op must be pure, atomic, 1--1 arity. Instead of computing immediately, we return a thunk that captures Op and stack argument. 
  * Op may be non-deterministic. If so, we'll resolve the choice lazily, based on first observer to force thunk (which picks an outcome) then successfully commit.
  * Laziness shifts when and where errors are observed, enabling transactions to 'commit' with unobserved errors. Be careful!
* `%an.lazy.force` - Op (usually %pass) must return thunk at top of data stack. We force evaluation of the thunk before returning. May diverge in case of error.
* `%an.lazy.spark` - Op (usually %pass) must return a thunk at top of data stack. If not already evaluated or scheduled, schedule evaluation by background worker thread.

Content-addressed storage:
* `%an.cas.stow` - Op (usually %pass) must return data of persistent or global ephemerality at top of stack. We wrap that data then lazily move it to external storage based on size, usage, and memory pressure.
* `%an.cas.load` - Op (usually %pass) must return stowed data at top of stack. Loads and substitutes the actual data. Loading may be lazy, but only when the runtime is confident it can fully load the data (accounting for risks of network disruption and invalid representation). Diverges if the data cannot be loaded. Reflection APIs may offer a detailed view of errors.
* `%an.cas.need` - Op (usually %pass) must return stowed data at top of stack. Tells runtime that this data will be needed in the near future. This enables the runtime to heuristically download, validate, etc. the data ahead of time so it's more available when needed.

Incremental computing:
* `(%an.memo MemoHint)` - memoize a computation. Useful memoization hints may include persistent vs. ephemeral, cache-invalidation heuristics, or refinement of a 'stable name' for persistence. TBD. 
  * As a minimum viable product, we'll likely start by only supporting 'pure' functions, because that's a low-hanging, very tasty fruit.
  * See discussion of *Accelerating Coroutines* below.
* `(%an.checkpoint Hints)` - when retrying a transaction, instead of recomputing from the start it can be useful to rollback partially and retry from there. In this context, a checkpoint suggests a rollback boundary. A compiler may heuristically eliminate unnecessary checkpoints, and Hints may guide heuristics. 

Guiding non-deterministic choice: 
* `(%an.cost Chan CostFn)` - (tentative) emits a heuristic 'cost'. CostFn has type `Env -> Program`, with Env providing access to Chan configuration options, ultimately returning a non-negative rational number on the data stack. Like logging, the Program also has implicit access to the host environment for dynamic costs. The only role of costs is to guide non-deterministic choice, disfavoring "high cost" options - or choices that will obviously lead to high costs later on.
  * Beyond tweaks by CostFn based on Chan configuration, a user configuration could amplify or suppress costs per channel, enabling an encoding of purpose and preference into channel names.
  * *Aside:* In theory, we could support non-monotonic costs to represent gains, too. But all the efficient search algorithms assume monotonicity. 

Future development:
* type declarations. I'd like to get bidirectional type checking working in many cases relatively early on.
* tail-call declarations. Perhaps not per call but rather an indicator that a subroutine can be optimized for static stack usage. 
* stowage. Work with larger-than-memory values via content-addressed storage.
* debug trace. Probably should wait until we have a clear idea of what a trace should look like. 
* debug views. Specialized projectional editors within debuggers.

### Logging

        a:((%an.log Chan MsgSel), Operation)
        type MsgSel : Env -> Sel
        type Chan is an embeded string 

MsgSel can fail if there is no message, or return a primary message on the data stack. Extensible outputs are feasible through APIs in the Env argument. Instead of insisting that MsgSel is a 'pure' computation, we leverage transactions and simply undo writes performed during evaluation.  If non-deterministic, we can log multiple possible messages.

Note that logging overlays an Operation. If Operation is a no-op (`%pass`), this behaves like one-off logging. But, in the general case, we can 'animate' the log - recompute messages at several steps. MsgSel cannot directly view updates to the data stack, but we can provide reflection APIs through the Env argument.

Instead of a flat stream of text, log output might be modeled as a stream of updates to a logical tree structure, including branches for stable non-deterministic choice. Something like this should align better with transaction loops.

To support ad hoc configuration, there may be per-Chan settings with guidance from both user configuration and application settings. Additionally, for dynamic settings, we could support per-Chan registers managed via 'sys.refl.log.\*' or similar. MsgSel could be conditional on these settings and registers, allowing for flexible control.

### Tracing (TBD)

Instead of user-defined messages, why not record enough to fully replay an Operation? Cost. That's why. But it's still a useful tool in the box.

        a:((%an.trace Chan BucketSel), Operation)

BucketSel roughly returns an identifier for the 'bucket' where a trace is stored, if any, so we can control tracing dynamically. 

What information do we need?

* input registers and relevant stack state
* updates to registers and stack after calls to untraced methods
* recording of non-deterministic choices and scheduling for replay
* for long-running traces, heuristic checkpoints for timeline scrubbing 
* for convenience, complete representation of subprogram being traced
* association with log outputs and other features

I think this won't be easy to implement, but it may be worthwhile.

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

## Lazy Computation and Parallel Sparks

I propose explicit thunks of 1--1 arity, pure but optionally non-deterministic, atomic computations. Computation may fail or diverge, in which case forcing the thunk will diverge. 

Interaction with non-determinism is interesting. We can maintain a list of non-deterministic results for a lazy computation. When forcing the thunk, we'll pick one (non-deterministically) and save the chosen outcome. If the forcing transaction does not commit, the saved choice is also not committed, thus the thunk remains non-deterministic. Thus, non-deterministic choice is lazily resolved by observers.

We can ask a worker thread to compute thunks in the background. In case of non-determinism, worker threads can cache outcomes but cannot commit to anything. In any case, this provides a very convenient source of parallelism orthogonal to transaction-loop optimizations.

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

## Alternative Program Models

The proposed program model above is based on tacit programming with a data stack, in context of a namespace built using an extended lambda-calculus. This has some useful properties: the namespace provides a foundation for metaprogramming, but the 'program' is static at runtime.



