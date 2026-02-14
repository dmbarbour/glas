# Program Model for Glas

## Proposed Primitives

Programs are modeled as an abstract data type, and most program primitives are constructors for this type. Special exceptions are '%load' and '%macro' which support modularity and metaprogramming.

*Notation:* `(F X Y Z)` desugars to `(((F,X),Y),Z)`, representing curried application in the [namespace AST](GlasNamespaces.md). 

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

*Note:* The %do and %opt constructors have exactly two arguments. It is feasible to support variable-arity with namespace semantics, but it would involve some variation of Church-encoded lists.

### Data Stack

* `(%data d:Data)` - pushes Data onto stack
* `(%dip P)` - runs P while temporarily hiding top stack item
* `%swap` - exchange top two stack elements. i.e. "ab-ba"
* `%copy` - copy top stack element, i.e. "a-aa".
* `%drop` - drop top stack element, i.e. "a-".
* `%mkp` - "lr-(l,r)" make pair elements (r at top of stack) 
* `%mkl` - rewrite top stack element to be left branch of tree
* `%mkr` - rewrite top stack element to br right branch of tree
* `%unp` - undoes mkp, fails if not a pair.
* `%unl` - undoes mkl, fails if not a left branch
* `%unr` - undoes mkr, fails if not a right branch

In practice, developers will rely on accelerated functions instead of manipulating trees one bit or branch at a time.

### Registers

Registers are second-class. Access is provided through the namespace. Aside from storing data, registers also serve as a source of identity and ephemerality, e.g. for data abstraction and associative structure.

* `(%rw Register)` - register swap; exchange data between stack and register.
* `(%local RegOps)` - RegOps must be a namespace term of type `Env -> Program`. Every name in Env references a register, initialized to zero. But the Program may reference only a finite subset of these registers.
* `(%assoc R1 R2 RegOps)` - Every ordered pair of registers implicitly names another volume of registers. Mostly serves as a more static and optimizable alternative to abstract data types for securing APIs.

Performance relies heavily on acceleration of useful `Register -> Program` operations. Even a basic 'get' `(%data 0; %rw R; %copy; %rw R; %drop)` can usefully be accelerated. 

might be accelerated to avoid temporary data and touching the register twice.

Even a basic register 'get' operation may be accelerated, but we can usefully accelerate queues, bags, CRDTs, etc. to optimize concurrency and distribution. For example, if we use a register only through queue ops, write transactions can use an intermediate buffer and any number of writers may commit concurrently with a single reader. Write order needs only be consistent with serializable transactions.

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

See [glas design](GlasDesign.md) for details.

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

We can develop a distributed runtime that runs on multiple nodes, multiple OS processes. A repeating 'step' transaction and event handlers like 'http' and 'rpc' can be mirrored on each node without affecting semantics. This requires expensive distributed transactions if we aren't careful. But it is possible to design with distribution in mind, limiting most transactions to just one or two nodes.

A simple performance heuristic is to abort any step that is better started on another mirror node. Assume the other will get to it. If necessary, communicate the expectation. 

We can further optimize based on patterns of state usage. For example, a read-mostly register can be mirrored, while a write-heavy register might 'migrate' to where it's currently being used. A queue's state can be divided between reader and writer nodes, while a bag (multiset) can be logically partitioned between any number of nodes. A conflict-free replicated datatype (CRDT) can be replicated on every node and synchronized opportunistically, when nodes interact.

State optimizations rely on accelerated register operations. A register accessed only via accelerated queue operations can be optimized as a queue.

*Aside:* Distribution also require attention to system API design, e.g. a distributed runtime doesn't have just one filesystem.

## Extension

The namespace supports several layers of extensibility for programs: module and application objects, tagged definitions, and introducing new program constructors. Object-layer extension is via OO-style inheritance and override. And new program constructors are rather ad hoc. 

Tagged definitions support both flexible calling conventions and alternative program models. 

* "data" - `Data` - embedded data, wrap %data then integrate
* "prog" - `Program` - abstract program, directly integrate
* "call" - `Env -> Def` - implicitly parameterized by caller namespace with syntactically specified translations, then apply adapter again to returned Def.

We could introduce alternatives to "prog" for defining and composing grammars, logics, hardware description, constraint systems, process networks, interaction nets, etc.. If we decide there's a reasonable default interpretation for 'calling' a grammar, we could extend our call-site adapter. But even without that, we can explicitly integrate grammars into programs.

## Annotations

Annotations are supported at the namespace layer.

        a:(Annotation, Operation)    # namespace AST node

In context, Operation will usually be an abstract `Program` or an `Env -> Program` namespace term. It's preferable to keep annotations inside any "prog" or "call" tags.

Acceleration:
* `(%an.accel Accelerator)` - performance primitives. Indicates that a compiler or interpreter should substitute Op for an equivalent built-in. We use `%accel.OpName` to name the accelerator. In rare cases, a generic accelerator might be parameterized `(%accel.OpName Args ...)`.

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
* `(%an.data.seal Key)` - Operation must be %pass. Seals top item on data stack, modeling an abstract data type. Key typically names a Register. If Key becomes unreachable the sealed data may be garbage collected. A compiler may eliminate seal and unseal operations based on static analysis, effectively a form of type checking.
  * `(%an.data.unseal Key)` - removes seal with matching Key or diverges
  * `(%an.data.seal.linear Key)` - a variant of seal that also marks sealed data as linear, forbidding copy or drop of the data until unsealed. (This doesn't prevent implicit copies, e.g. for backtracking.)
    * `(%an.data.unseal.linear Key)` - counterpart to a linear seal.
  * `(%key.reg RegisterName)` - construct Key based on register.
* `%an.data.static` - Operation must be %pass. Indicates top stack element should be statically computable. Serves as a hint for partial evaluation; error if data depends on runtime input.
* `%an.static` - Indicates subprogram must be statically computed. Serves as a hint for partial evaluation; error if computation depends on runtime input.
* `%an.eval.static` - Indicates that all %eval steps within a program must receive their data argument at compile-time. 
* `(%an.type TypeDesc)` - Describes the expected partial type of Operation. Ideally, this is verified by a typechecker. We'll develop '%type.\*' constructors to work with this.
* `%an.det` - Indicates a subprogram should be observably deterministic. This isn't the same as rejecting non-deterministic choice; rather, such choices should lead to the same outcome. But without further proof hints, this effectively reduces to rejecting %opt.

Laziness:
* `%an.lazy.thunk` - Op must be pure, atomic, 1--1 arity, terminating. Instead of computing immediately, we return a thunk that captures Op and stack argument. The thunk must be explicitly forced.
  * If Op is non-deterministic, we'll resolve the choice lazily. It becomes committed choice only if an observer commits after forcing the thunk.
  * The 'terminating' constraint is not enforced by this annotation. But the rule exists because only terminating thunks have equivalent behavior across transactions. (Annotations should not affect formal behavior of valid programs.)
* `%an.lazy.force` - Op must be %pass. Forces evaluation of thunk at top of data stack. May diverge if thunk is invalid.
* `%an.lazy.spark` - Op must be %pass. Operates on thunk at top of data stack: if not already evaluated or scheduled, schedules evaluation by background worker thread.

Content-addressed storage:
* `%an.cas.stow` - Op must be %pass. Lazily offloads data to remote storage. Actual move is heuristic, e.g. based on memory pressure and size of data.
* `%an.cas.load` - Op must be %pass. Expects previously stowed data at top of data stack. Replaces it by referenced data. Diverges if the data cannot be loaded. (You may need 'sys.refl.cas.\*' APIs for a full diagnosis.)
* `%an.cas.need` - Op must be %pass. Expects previously stowed data at top of data stack. Asks runtime to prepare for load in the background, caching the data. Data may be removed from cache again based on memory pressure.

Incremental computing:
* `(%an.memo MemoHints)` - memoize a computation. As a minimum viable product, we'll likely start by only supporting 'pure' functions. MemoHints TBD (likely '%memo.\*' constructors).
* `%an.checkpoint` - when retrying a transaction, instead of recomputing from the start it can be useful to rollback partially and retry from there. In this context, a checkpoint suggests a rollback boundary.
* *TBD* - support for memoization of control flow.

Tail-Call Optimization:
* `%an.tco` - indicates a subprogram should evaluate with bounded data stack and call stack. Reports an error if compiler and optimizer cannot figure out how to make it happen. 

Future development:
* type declarations. I'd like to get bidirectional type checking working in many cases relatively early on.
* unit types? 
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

        (%an.accel %accel.OpName)           # built-in function 

Accelerators replace a reference Operation with a built-in. Ideally, this substitution is verified, e.g. we can check types for compatibility and run a few sample tests. If it is inconvenient to immediately provide Operation, as may be the case during early development of accelerators, users may use '%error'. The runtime will recognize the user isn't even trying and report a warning instead.

In a few cases, such as `%accel.list.flatten`, the accelerated Operation can be %pass because we're tuning representation instead of performing an observable computation. Flatten is reasonably part of the list accelerator family.

We can accelerate Operations that expect static arguments on the data stack, or accelerate `Env -> Program` for higher-order computations. In rare cases, we might use `(%accel.OpName Args)` to construct a templated accelerator.


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

Graphs? We could accelerate graph ops. May need graph canonization.

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
* CRDTs
* Indexed Data (arrays, dicts)

## Lazy Computation and Parallel Sparks

I propose explicit thunks of 1--1 arity, pure but optionally non-deterministic, atomic computations. Computation may fail or diverge, in which case forcing the thunk will diverge. 

Interaction with non-determinism is interesting. We can maintain a list of non-deterministic results for a lazy computation. When forcing the thunk, we'll pick one (non-deterministically) and save the chosen outcome. If the forcing transaction does not commit, the saved choice is also not committed, thus the thunk remains non-deterministic. Thus, non-deterministic choice is lazily resolved by observers.

We can ask a worker thread to compute thunks in the background. In case of non-determinism, worker threads can cache outcomes but cannot commit to anything. In any case, this provides a very convenient source of parallelism orthogonal to transaction-loop optimizations.

### Tail Call Optimization

Instead of annotating individual calls to be tail-call optimized, I propose we should specify that subprogram shall be evaluated with bounded call and data stacks. We could also have a variant for `Env -> Prog` that says the Program has a bounded call stack modulo use of methods in `Env`.

Performing the optimization can be difficult in context of `Env` arguments and register passing. But we can potentially unroll a recursive loop a few times, optimize, then verify that resources are recycled within just a few cycles.

## TBD

### In-Place Update? Seems Infeasible.

In theory, we can support in-place update for 'writing' indexed elements of a large structure. In practice, support for transactions, backtracking conditions, logging, etc. means we'll often have implicit copies of data even when we don't have semantic copies. Much simpler to stick to persistent data structures.

Best we can easily do: 

- accelerated bulk operations can skip intermediate data representations
- accumulate 'writes' on accelerated data then apply as a bulk operation

### Unit Types

I'd like to associate unit metadata (e.g. kilograms or meters per second) with numbers within a program. But I don't want the overhead of doing this at runtime. I also don't want pure annotations, because I'd like the ability to print unit information.

Use of the namespace might be feasible, but not via the basic "call"-tagged `Env -> Program`. I think we'd need something closer to a 'static' state monad, maintaining some ad hoc context across calls.

Use of the data stack and partial evaluation is technically possible, but I doubt it would be pleasant. Perhaps we could bind unit data to associated registers, then verify static computation of those registers?

Either way, I expect this will require ample support from the front-end compiler.

### Type Descriptions

        (%an.type TypeDesc)

I propose to develop ad hoc constructors for describing types under '%type.\*'. However, I'm not in a hurry to do so.

Some thoughts:
- Instead of hard-coding a few types like 'i64' or 'u8', consider an `(%type.int.range 0 255)` or similar.
- need a const type that describes an expected value, too.

### Checks Modulo Environment

For a few annotations like tail-call optimizations (%an.tco) and determinism (%an.det) it seems useful to have a variant where `Env -> Program` is bounded-stack or deterministic modulo use of `Env`. 

