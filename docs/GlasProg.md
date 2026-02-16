# Program Model for Glas

This document describes the runtime program model for glas systems and several design patterns for how to use it more effectively.

## Primitive Constructors

Programs are modeled as an abstract data type. For example, `(%do P1 P2)` returns an abstract program that, when executed, runs two subprograms sequentially.

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
* `(%opt P1 P2)` - non-deterministic choice of P1 or P2. Associative.
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
* `%mkp` - "lr-(l,r)" make pair elements (r from top of stack) 
* `%mkl` - rewrite top stack element to be left branch of tree
* `%mkr` - rewrite top stack element to br right branch of tree
* `%unp` - undoes mkp, fails if not a pair.
* `%unl` - undoes mkl, fails if not a left branch
* `%unr` - undoes mkr, fails if not a right branch

The primitive data operations touch one bit at a time. In practice, developers will rely heavily on accelerated functions for performance, even for basic operations like inserting a label into a dictionary. 

### Registers

Registers are second-class. Access is provided through the namespace. Aside from storing data, registers also serve as a source of identity and ephemerality, e.g. for data abstraction and associative structure.

* `(%rw Register)` - register swap; exchange data between stack and register.
* `(%local RegOps)` - RegOps must be a namespace term of type `Env -> Program`. Every name in Env references a register, initialized to zero. But the Program may reference only a finite subset of these registers.
* `(%assoc R1 R2 RegOps)` - Every ordered pair of registers implicitly names another volume of registers. Mostly serves as a more static and optimizable alternative to abstract data types for securing APIs.

Performance relies heavily on acceleration of useful `Register -> Program` operations. Even a basic 'get' `(%data 0; %rw R; %copy; %rw R; %drop)` can usefully be accelerated. But we'll also accelerate queues, bags, CRDTs, etc. to simplify transaction conflict analysis and optimize organization of state in distributed runtimes.

### Metaprogramming

* `(%eval Env)` - pops a namespace AST from the data stack that, in context of provided Env, evaluates to a Program. Verifies, e.g. typechecks and static assertions in context. Runs, may involve JIT compilation.

If the AST is determined statically, the optimizer can erase '%eval' and substitute the program. We can insist on static AST via annotations, '%an.data.static' or '%an.eval.static'. For dynamic eval, we can attempt to leverage abstract interpretation and incremental computing to reduce rework.

## Note on Metaprogramming

Aside from '%eval', we can also support metaprogramming via '%macro', or within the namespace layer (e.g. via Church-encoded lists of namespace terms). But only '%eval' supports runtime metaprogramming.

* `(%macro P) : Any` - 0--1 arity program P must return closed-term namespace AST.

Use of '%eval' is more convenient when we don't *locally* know whether an AST is static or not. Or when the dataflow to construct an AST doesn't align nicely with the lexical namespace scope.

## Design Patterns

The primitive program model is very constraining. We can leverage the namespace layer to greatly enhance expressiveness and improve the user experience.

### Pass-By-Ref and Algebraic Effects

Instead of working directly with `Program` definitions, we can compose `Env -> Program` definitions, providing part of the caller's environment. This enables the caller to provide access to local registers or callbacks, for example. Effectively, this can support higher-order programming and algebraic effects, albeit without the ability to 'return' a function.

### Calling Conventions

We can freely mix `Program`, `Env -> Program`, embedded `Data`, and other definitions, within a namespace. However, doing so complicates front-end syntax, i.e. the caller must locally know and explicitly adapt each definition. This extra knowledge becomes an source of unnecessary rigidity for some program updates, e.g. a 0--1 `Program` can be trivially updated to embedded `Data` and sometimes the converse, but with explicit adapters we're always forced to edit the caller.

To mitigate this, we can tag user definitions, e.g. with "prog", "call", and "data".

* "data" - raw, embedded data of any type; program adapter via '%data'
* "prog" - directly wraps an abstract Program; no embedding context
* "call" - `Object -> Def`, tagged parameter object to tagged definition

To make it more extensible and generic, "call" is tuned from `Env -> Program` to include tags on both input and output. For the input, we may initially support "env"-tagged environments or "obj"-tagged basic objects. Favoring the latter, we can conveniently support default parameters.

### Positional Parameters

Positional parameters shall be encoded into the "call" parameter object as 'args' - a "list"-tagged, Church-encoded list of tagged namespace terms. These terms aren't evaluated by the caller, thus default behavior is call-by-name. It isn't difficult to develop generic wrappers for call-by-value or call-by-need (assuming 0--1 programs).

I hope to avoid positional parameters in glas syntax. Keyword parameters seem nicer for many use cases - easier to extend, deprecate, share when refactoring a method, etc.. But a standard approach is convenient for integration across front-end compilers. 

## Reflection

When we construct a program `(%do P1 P2)`, there is no primitive mechanism to separate this back into its constituent elements. However, a runtime may provide ad hoc reflection APIs, e.g. passing 'sys.refl.\*' to an application. The view of a definition through this lens may be runtime-specific, but a viable mechanism is to return a namespace AST that can reconstruct a definition given program primitive constructors.

## Annotations

Annotations are structured comments supported in the namespace AST:

        a:(Annotation, Operation)

Annotations should not affect formal behavior of valid programs. That is, it should be 'safe' in terms of semantics to ignore annotations. However, annotations significantly influence optimization, verification, instrumentation, and other 'non-functional' features. In practice, performance is part of correctness. Annotations aren't optional. To resist *silent* degradation of performance (and other properties), glas systems shall report warnings for unrecognized annotations.

By convention, abstract annotation constructors are provided alongside program constructors, favoring names in '%an.\*'. This reduces need to 'interpret' arbitrary namespace terms as annotations. It also simplifies reporting for unrecognized or invalid annotations.

Annotations aren't limited to programs. However, within this document, I'm focused on programs and program-adjacent structures (e.g. `Register->Program`). 

### Acceleration

        a:(%an.accel.list.concat, SlowListConcat)
        # replaces SlowListConcat with runtime built-in

Annotation-guided acceleration is a simple, effective, extensible performance solution. The essential idea is to replace a slow implementation with a fast runtime built-in. In case of list concat, the built-in should leverage specialized representations, implementing large lists as finger-tree ropes.

The runtime may perform lightweight validation before substitution, e.g. typechecks and a few simple tests even if not proof-carrying code. Enough for confidence. Users are also encouraged to develop a suite of unit tests comparing SlowListConcat with the accelerated version. But, during early or experimental development of an accelerator, it is inconvenient to maintain both. In these cases, consider '%an.tbd' to convert validation errors into TBD warnings.

We will accelerate several `Register->Program` operations, e.g. for queue reads and writes, bags, and CRDTs. Aside from local performance benefits, accelerated state models can simplify conflict analysis for optimistic concurrency control, or provide useful hints to effectively mirror and partition state within a distributed runtime.

An especially useful pattern is to accelerate interpreters for abstract machines. For example, we could develop a memory-safe, portable subset of Vulkan or OpenCL. A runtime built-in would then 'accelerate' by compiling for CPU SIMD or GPGPU hardware. As with queues, it may be useful to bind a Register for abstract machine state. A careful choice of abstract machines enables glas systems to integrate high-performance computing without FFI.

Development of accelerators is runtime specific but subject to de facto standardization.

### Lazy Evaluation and Parallel Sparks

        a:(%an.lazy.thunk, Program)     # Data -- Thunk
        a:(%an.lazy.force, %pass)       # Thunk -- Data
        a:(%an.lazy.spark, %pass)       # Thunk -- Thunk

We can thunkify a pure (maybe non-deterministic), terminating 1--1 arity program. This immediately returns an abstract thunk on the data stack that captures input and program. When forced, we evaluate, cache, and return the result. We can ask the runtime to enqueue a thunk for evaluation in a worker thread, called 'sparks' here (allusion to Haskell). Sparks enable developers to separate expensive computations from transactions (reducing risks of read-write conflicts), and provide an independant mechanism for parallelism.

In context of non-deterministic choice, a divergent choice should backtrack and try other paths until one succeeds. But there is an intriguing opportunity: we can defer choice. Logically, instead of committing to a choice when the thunk is created, commit when the thunk is forced and the observer commits. By deferring choice, computation with thunks implicitly becomes a constraint system, with observers refining choices. Lazy choice becomes benign instead of fair. To make this mode explicit, we could introduce '%an.lazy.opt' to annotate subprograms that create thunks.

*Note:* Interaction with linear types needs attention. A thunk should be linear if the result may be linear. In theory this can be achieved via type inference, but I propose introducing '%an.lazy.thunk.linear' to explicitly construct linear thunks.

### Logging

        a:((%an.log Chan MsgSel), Program)
        type Chan = embedded String
        type MsgSel = Object -> Sel 

This annotation expresses logging *over* a Program. If Program is '%pass', we can simply print once. But if Program is a long-running loop that affects observed registers, we can feasibly 'animate' the log, maintaining the message. If the program has errors, we can attach messages to a stack trace. These are useful integrations.

The Chan is a string that would be valid as a name. The role of Chan is to disable some operations and precisely configure the rest. We can use `(%an.chan.scope TL)` to rewrite or disable channels within a subprogram.

MsgSel receives an "obj"-tagged parameter object. This includes configuration options and some reflection APIs, e.g. to support stack traces. The returned selector (cf. '%cond' and '%loop') should model a 0--1 program, returning at most one message on the data stack. By convention, this is an ad hoc dict of form `(text:"oh no, not again!", code:42, ...)`. In case of '%opt', the runtime may evaluate both choices to generate a set of messages. MsgSel isn't necessarily 'pure', but any effects are aborted after the message is generated.

*Note:* A stream of log outputs can be augmented with metadata to maintain an animated 'tree' of messages, where the tree includes branching on non-deterministic choice. Users could view the stream or the tree.

### CLEANUP NEEDED

Instrumentation:
* `(%an.assert Chan ErrorMsgGen)` - assertions are structured as logging an error message. If no error message is generated, the assertion passes. May reduce to warning.
* `(%an.assert.static Chan ErrorMsgGen)` - assertion that must be computed at compile-time, otherwise it's a compile-time error. May reduce to compile-time warning with or without a runtime error.
* `(%an.profile Chan BucketSel)` - record performance metadata such as entries and normal exits, fails, errors, time spent, time wasted on rework, etc.. Profiles may be aggregated into buckets based on BucketSel. 
* `(%an.trace Chan BucketSel)` - record information to support slow-motion replay of Operation. BucketSel helps control and organize traces. See *Tracing*.

Validation:
* `(%an.arity In Out)` - express expected data stack arity for Op. In and Out must be non-negative integers. Serves as an extremely simplistic type description. 
* `(%an.data.seal Key)` - Operation must be %pass. Seals top item on data stack, modeling an abstract data type. Key typically names a Register. If Key becomes unreachable the sealed data may be garbage collected. A compiler may eliminate seal and unseal operations based on static analysis, effectively a form of type checking.
  * `(%an.data.unseal Key)` - removes seal with matching Key or diverges
  * `(%an.data.seal.linear Key)` - a variant of seal that also marks sealed data as linear, forbidding copy or drop of the data until unsealed. (This doesn't prevent implicit copies, e.g. for backtracking.)
    * `(%an.data.unseal.linear Key)` - counterpart to a linear seal.
* `%an.data.static` - Operation must be %pass. Indicates top stack element should be statically computable. Serves as a hint for partial evaluation; error if data depends on runtime input.
* `%an.static` - Indicates subprogram must be statically computed. Serves as a hint for partial evaluation; error if computation depends on runtime input.
* `%an.eval.static` - Indicates that all %eval steps within a program must receive their data argument at compile-time. 
* `(%an.type TypeDesc)` - Describes the expected partial type of Operation. Ideally, this is verified by a typechecker. We'll develop '%type.\*' constructors to work with this.
* `%an.det` - Indicates a subprogram should be observably deterministic. This isn't the same as rejecting non-deterministic choice; rather, such choices should lead to the same outcome. But without further proof hints, this effectively reduces to rejecting %opt.

Laziness:

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

### Tail Call Optimization

Instead of annotating individual calls to be tail-call optimized, I propose we should specify that subprogram shall be evaluated with bounded call and data stacks. We could also have a variant for `Env -> Prog` that says the Program has a bounded call stack modulo use of methods in `Env`.

Performing the optimization can be difficult in context of `Env` arguments and register passing. But we can potentially unroll a recursive loop a few times, optimize, then verify that resources are recycled within just a few cycles.

### Source Mapping

It is feasible to use annotations for source mapping. Might take some work to prevent things from getting too bloated. We can leverage the abstract Src file in this context, but the front-end compiler must specify things like byte ranges or line numbers.

## Challenges TBD

### In-Place Update? Seems Infeasible.

If we hold a unique reference to a binary representation, we can directly mutate that binary in place. Some functional languages leverage linear types as a means to enforce unique reference. Unfortunately, linearity doesn't imply unique reference in context of backtracking. 

For persistent data structures, perhaps we could better utilize log-structured merge trees, maintaining a small 'working set' of updates closer to the root and propagating updates in heuristic batches. This would at least reduce the number of allocations per write.

For accelerated state, e.g. a register containing an array, we can at least support short-term in-place updates and maintain wide write buffers similar to cache lines, reducing need for allocations to rebuild a rope structure after each update.

### Type Descriptions

        (%an.type TypeDesc)

I propose to develop ad hoc constructors for describing types under '%type.\*'. However, I'm not in a hurry to do so.

Some thoughts:
- Instead of hard-coding a few types like 'i64' or 'u8', consider an `(%type.int.range 0 255)` or similar.
- need a const type that describes an expected value, too.

### Overlay Context (TBD)

I would like to wrap abstract programs with static context, i.e. metadata and dataflow, such that every subprogram has its own localized view of context. Sample use cases include support for unit types or performing modulo arithmetic based on a final modulo operation. Bi-directional dataflow seems essential, as does some form of unification (for loops and branches), and extensibility. It must be feasible to mirror local registers.

My intuition is that a grammar-based model is a good fit. Grammars represent sets of sentences - where 'sentence' is rather flexible, could be structured data. Unification is encoded as an intersection of two grammars, and in practice we can unify at a point by having a gramar that is open except at that point. Usefully, we can easily obtain a local 'view' of context, i.e. a small fragment of the sentence, and we can integrate ambiguity as needed. Constraint systems are also viable, but it's less clear what a local view would look like.

A grammar or constraint-system overlay can feasibly provide a robust mechanism for program search, too. Effective program search has long been a stretch goal for glas systems.

The details need a lot of work. But, this should be an alternative to the "prog" tag, with front-end languages composing contexts and programs. We can readily work without overlays for now.
