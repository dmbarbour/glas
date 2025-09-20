# Program Model for Glas

The [namespace](GlasNamespaces.md) supports modules and user-defined front-end syntax. Programs are compiled to an AST structure built upon '%\*' primitives. This document describes a viable set of primitives for my vision of glas systems and some motivations for them.

## Proposed Program Primitives

Control Flow:

* `n:Name` - we can interpret a name in terms of substituting its definition. Performance may involve call stacks and tail-call optimization.
* `(%do P1 P2 P3 ...)` - execute P1 then P2 then P3 etc. in sequence. We also use `(%do)` as our primary no-op, and '%do' as the constructor for committed action in a decision tree structure.
* `%fail` - voluntary failure, interpretation is contextual but will gend to abort the current transaction and allow some observable handling of this, e.g. backtracking conditions, or aborting a coroutine step until observed conditions change. For contrast, involuntary failures such as type errors are instead modeled as divergence like an infinite loop.
* `(%cond Sel)` - supports if/then/else, pattern matching, etc.. The Sel type has a distinct AST structure from full programs.
  * `(%br C BrL SelR)` - run C within a hierarchical transaction. If C terminates normally, run SelL. If C fails voluntarily, abort then run SelR. Note that committing C is left to SelL. 
  * `(%do P1 ...)` - in context of Sel, represents committed action. Commit the entire chain of prior branch conditions, then run '%do' in the same transactional context as '%cond' or '%loop'.
  * `%fail` - in context of Sel, represents the lack of a case on the current branch. Causes computation to backtrack, aborting the most recent successful branch condition then trying the SelR path. In normal form, appears only in SelR position because we can optimize `(%br C %fail SelR) => SelR`.
  * Beyond these, Sel may also support language declarations, annotations, and macros.
* `(%loop Sel)` - supports anonymous while-do loops, albeit mixing selection of action. Uses same Sel type as '%cond'. Implicitly exits loop if no '%do' step is selected. 
* `(%co P1 P2 P3 ...)` - execute P1, P2, P3, etc. concurrently as coroutines with independent data stacks and voluntary yield. This step only exits when all coroutines complete. Scheduling of coroutines must be associative and may be commutative (e.g. non-deterministic scheduling is both).
* `%yield` - pauses a coroutine, providing an opportunity for concurrent computations to operate. Resumption is implicit. Each yield-to-yield step should be logically atomic, thus '%fail' implicitly rewinds to a prior '%yield' then awaits changes to observed state.
* `(%atomic P)` - an atomic operation may yield, but will only resume internally. Fails only if all internal resumptions are failing.
* `(%choice P1 P2 P3 ...)` - represents non-deterministic runtime choice of P1 or P2 or P3. 
* `%error` - explicit divergence. Unlike '%fail', we do not backtrack on error; it's equivalent to an infinite loop. However, errors can be recoverable in context of non-deterministic choice or non-deterministic scheduling of coroutines.

* `(%sched Schedule P)` - (tentative) local guidance for a continuation scheduler. Not sure what this should look like, other than that we will likely want to support a few deterministic schedules such as "prioritize leftmost" and "round robin". 
* *tbd* - (tentative) local guidance for non-deterministic choice

Data Manipulation:
* `d:Data` - push copy of data to top of data stack
* `(%dip P)` - run P while hiding top of data stack
* `%swap` - exchange top two stack elements. i.e. "ab-ba"
* `%copy` - copy top stack element, i.e. "a-aa".
* `%drop` - drop top stack element, i.e. "a-".


Environment Access and Manipulation:
* `(%scope EnvTL P)` - apply EnvTL to RegisterNames and HandlerNames in P. This applies across definition boundaries. To support extension, composition, and metaprogramming, EnvTL has a dedicated AST structure.
  * `(%tl TL)` - the common case, same prefix-to-prefix radix tree TL as namespaces.  
    * Translation to NULL or WARN will block use of a register or handler, with WARN reducing compile-time errors to compile-time warnings and runtime errors (by default).
  * `(%tl.arc Prefix RegisterName RegisterName)` - binds Prefix to a namespace indexed by a directed edge between two registers. Useful for access control and *Environment Abstraction*.
  * `(%tl.seq EnvTL1 EnvTL2 ...)` - apply EnvTL1 then EnvTL2 etc. in sequence.
* `(%rw RegisterName)` - swap data between named register and top data stack element. 
* `(%call HandlerName EnvTL)` - invoke a handler, applying a translation to control the handler's view of the caller's environment. Error if handler is not defined.
* `(%call.avail HandlerName)` - if HandlerName is defined, acts as a no-op. Otherwise acts as '%fail'.
* `(%local Prefix P)` - (tentative) allocate a fresh namespace with registers initialized to zero. Translate Prefix to this namespace in context of P. Clear the registers upon exit from P. Clear may diverge if registers contain linear data.
  * we could feasibly integrate with handlers, e.g. `(%local Prefix Handlers P)`
* *tbd* - introduce handlers in context of a subprogram.

Tooling and Evolution:

* `(%an Annotation P)` - Equivalent to P, modulo performance, analysis, instrumentation, and reflection. Annotations provide ad hoc guidance to compilers, interpeters, optimizers, debuggers, typecheckers, theorem provers, and similar tools. 
* `(%lang Version P)` - Language declaration for P. Declarations should be idempotent, so this does nothing if the language version doesn't change. However, in the general case it may result in support for different primitives and distinct interpretations of existing primitives in P. Version may be encoded as a dict.

Metaprogramming:
* `(%macro Template)` - Template is an AST that locally contains special '%macro.\*' elements for rewriting. In this context, 'locally' restricts rewriting across definition boundaries, i.e. no 'free' macro variables. Macros are applied as a user-defined constructor in programs, selectors, annotations, and other AST types.
* `(%macro.arg K)` - is substituted by the Kth AST argument to the constructor. This includes K=0 referring to the constructor, the macro definition, thus supporting anonymous recursion.
* `%macro.argc` - is substituted by the count of AST arguments after arg 0.
* `(%macro.eval Localization ASTBuilder)` - after macro substitions, ASTBuilder should represent a 0--1 arity program that returns an AST value (see namespace types). This AST is linked based on Localization, replaces the eval constructor, then receives another round of macro substitutions.

### Annotations

        (%an Annotation Operation)

Annotations are not executable as programs, but they will support macros.

Acceleration:
* `(%an.accel Accelerator)` - non-semantic performance primitives. Indicates that a compiler or interpreter should substitute Op for a built-in Accelerator. By convention, an Accelerator has form `(%accel.OpName Args)` and is invalid outside of '%an.accel'. See *Accelerators*.

Instrumentation:
* `(%an.log Chan MsgSel)` - printf debugging! Rather sophisticated. See *Logging*.
* `(%an.reject Chan MsgSel)` - negative assertions, structured as conditionally logging an error message. If no error message, the assertion passes, otherwise we diverge. A non-deterministic choice of messages is possible, in which case all choices are evaluated.
* `(%an.profile Chan Hint)` - record performance metadata such as entries and exits, time spent, yields, fails, and rework. Hints may guide 
This may benefit from dynamic virtual channels (tbd). 
* `(an.trace Chan Hint)` - record information to support *replay* of a computation
* `(%an.chan.scope TL)` - a simple prefix-to-prefix rewrite on Chan names for Operation.

Validation:
* `(%an.arity In Out)` - express the data stack arity for a subprogram. Represents reading 'In' elements and writing 'Out' elements. Operation may read and write fewer so long as balance is maintained.
* `(%an.data.wrap RegisterName)` - Support for abstract data types. Wraps top item on data stack, such that it cannot be observed until unwrapped. Operation should be a no-op. The RegisterName provides identity and access control, and also determines valid scope or lifespan (the data should not be stored to a register that is longer-lived than the named register). 
  * `(%an.data.unwrap RegisterName)` - unwrap previously wrapped data. This is an error if the data was not previously wrapped with the same register. A compiler can eliminate wrap/unwrap pairs based on static analysis.
  * `(%an.data.wrap.linear RegisterName)` - as wrap, but also marks as linear. Linearity applies until unwrapped. Efficient dynamic enforcement requires metadata bits.
  * `(%an.data.unwrap.linear RegisterName)` - corresponding unwrap for linear data.
* `(%an.reg.reject (List of Prefix))` - forbid reference to registers whose prefixes are listed in scope of Operation. 
  * `(%an.reg.accept (List of Prefix))` - forbid reference to registers whose prefixes are not listed.

Incremental computing:
* `(%an.memo Hints)` - memoize a computation. For simplicity and immediate utility, initial support for memoization may be restricted to pure data stack functions, perhaps extended to pure handlers. Hints may indicate persistent vs. ephemeral memoization, cache-invalidation policy, and other options.
* `(%an.checkpoint Hints)` - when retrying a transaction, instead of recomputing from the start it can be useful to rollback partially and retry from there. In this context, a checkpoint suggests a rollback boundary. A compiler may heuristically eliminate unnecessary checkpoints, and Hints may guide heuristics. 

Future development:
* hiding parts of data or environment as lightweight types
* type declarations. I'd like to get bidirectional type checking working in many cases relatively early on.
* 'static' types in type declarations.
* linear types or valid 'final' states in type declarations.
* robust data abstraction, optionally including linear data.
* tail-call declarations. Perhaps not per call but rather an indicator that a subroutine can be optimized for static stack usage, optionally up to handler calls. 
* stowage. Work with larger-than-memory values via content-addressed storage.
* lazy computation. Thunk, force, spark. Requires some analysis of registers written.
* debug trace. Probably should wait until we have a clear idea of what a trace should look like. 
* debug views. Specialized projectional editors through debuggers.

### Accelerators

        (%an (%an.accel (%accel.OpName Args)) Op)

The data manipulation operations are minimalist, basically just support for 


## Design Motivations

Some discussions that led to the aforementioned selection of primitives.

### Operation Environment

I propose to express programs as operating on a stable, labeled environment. This environment includes stateful registers and callable 'handlers' for abstraction of state or effects. A program may introduce local registers and handlers in scope of a subprogram, and may translate or restrict a subprogram's access to the program's environment.

To keep it simple and consistent, I propose that registers and handlers are named with simple strings similar to names in the namespace (ASCII or UTF-8, generally excluding NULL and C0). This allows us to apply the namespace TL type to translate and restrict the environment exposed to a subprogram. It also ensures names are easy to render in a projectional editor or debug view.

Toplevel application methods ('app.\*') receive access to a finite set of handlers and registers from the runtime. See [glas apps](GlasApps.md) for details. The program may include annotations describing the assumed or expected environment, allowing for validation and optimization.

### In-Place Update? Defer.

It is possible to support in-place update of 'immutable' data if we hold the only reference to its representation. This can be understood as an opportunistic optimization of garbage-collection: allocate, transfer, and collect in one step. In glas programs, this would be feasible with accelerators, such as a list update operator could swap a list element without reallocatng the list. This is especially useful if the list is represented by an array.

However, pervasive use of transactions and backtracking complicates this optimization. It is convenient to capture a snapshot of registers so we can revert if necessary. Although this snapshot isn't a logical copy and thus doesn't conflict with linear types, it is a shared representation and thus does hinder in-place update.

A viable alternative is to maintain a 'log' of updates to apply later. For example, a runtime could feasibly represent the updated list as a special `(update log, original list ref)` pair within runtime. This might generalize to [log-structured merge-tree (LSM trees)](https://en.wikipedia.org/wiki/Log-structured_merge-tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

This doesn't quite support the ideal of in-place update. We must allocate that log, and perhaps some metadata to track elements to process further upon commit. But perhaps we can still perform in-place update upon commit, and benefit from editing nearer to the tree root. This seems a viable approach.

Meanwhile, we'll still support decent persistent data structures by default, e.g. finger-tree ropes still support O(log(N)) updates in the center, O(1) at the edges, and we can easily use a pair as a gap buffer.

### Tail Call Optimization

Tail calls can support recursive definitions without increasing the call stack. This can be viewed as a form of static garbage collection, recycling memory allocations on the call stack. It is feasible to unroll a recursive loop to simplify this recycling. 

In glas systems, I want tail calls to be a robust, checked optimization. Annotations can indicate that calls are expected to be tail calls. Further, I hope to encourage tail calls as the default form of recursion, as it greatly simplifies compilation.

Even if all recursion is tail calls, we can model dynamic stacks in terms of registers containing list values. Performance in this case might be mitigated by preallocation of a list buffer for in-place update and use as a stack. This could be supported through annotations or accelerators.

### Algebraic Effects and Handlers and Stack Objects

I propose that most effects APIs are expressed in terms of invoking 'handlers' in the environment. This allows for overrides by the calling program independent of the namespace structure. There may be a few special exceptions, e.g. non-deterministic choice may be expressed as a primitive and restricted through annotations instead of by manipulating the environment.

Unlike first-class functions or closures, handlers introduced by a subprogram cannot be "returned" to a caller. They can only be passed to further subprograms in context. However, it may be convenient to introduce a notion of 'objects' on the stack, modular collections of local state and methods, rather than focus on individual handlers.

When a handler is invoked, it must receive access to two environments - host and caller. With the 'stack object' concept, it is useful to conflate a third environment: local state. Local registers may then be modeled as a stack object with no methods. To resist naming conflicts, access to these environments from within a handler may be distinguished by prefixes, e.g. "^" for host, "$" for caller, and "." for local state. (We may need to see how '.' interacts with hierarchical objects.)

Stack objects should be able to hierarchically compose more stack objects. It seems feasible to express stack objects as namespace constructors, but it may prove simpler to introduce dedicated primitives to declare objects.

*Aside:* It might be interesting to express an application as a handler 'stack object' instead of a collection of definitions. This could be supported by a runtime via application settings.

*Note:* It might be convenient to express a set of algebraic effects in terms of a localization of the current namespace.

### Coroutines and Concurrency

A procedure can potentially express a sequence, a 'thread', of fine-grained transactions. This is very convenient for expressing synchronous interactions, closer to conventional procedures or scripts. Every step is atomic, but other threads may observably interact with shared state between steps. In addition to retry due to conflict, a partially computed step may 'fail', retrying after a relevant state change or in context of an alternative, uncommitted non-deterministic choice.

This is similar to coroutines. Coroutines voluntarily 'yield', allowing other coroutines to interact with shared state until resumed. Due to lack of preemptive scheduling, operations between yield and resumption are atomic transactions from the perspective of a coroutine. Each coroutine effectively represents a thread, conflating yield and commit. 

For glas systems, I favor anonymous, second-class, fork-join coroutines. For example, `(%c P1 P2 P3)` represents fork-join composition of three anonymous threads, returning only after all three operations return. (We could give tasks a name for debugging purposes via annotation, but it isn't semantic.) Ideally, composition of coroutines is associative, such that `(%c (%c P1 P2) P3) = (%c P1 (%c P2 P3)) = (%c P1 P2 P3)`. This constrains a scheduler, but is still flexible, e.g. valid schedules include 'prioritize leftmost' and 'round robin' and 'non-deterministic choice' and we can feasibly extend to numeric priorities. We could introduce some primitives to guide scheduling.

It is convenient if a coroutine can wait for some arbitrary conditions before continuing. A very simple solution is to express 'yield' in terms of 'await Reg', waiting for a register to be non-zero. This supports semaphores, queues, or mutexes. We can feasibly extend this to waiting on composite conditions. Alternatively, in context of backtracking failure, we backtrack to the most recent 'yield' after a step 'fails', allowing assumptions to be mixed freely with operations in each step.

Ideally, we can utilize multiple processors to evaluate coroutines. This is feasible with static analysis to avoid read-write conflicts, or with dynamic analysis and backtracking after a conflict occurs (aka [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control)). The latter seems an excellent fit for glas systems, aligning with transaction-loop applications and hierarchical transactions. Conflicts and rework can feasibly be mitigated through analysis, annotations, and heuristics, scheduling steps sequentially where conflicts are likely.

We can evaluate a subprogram that uses coroutines and 'yield' internally with a localized scheduler. This might be expressed as `(%atomic P)`. When evaluating an atomic section, we resume locally and it's an error if no progress is possible. We can assume transaction-loop methods such as 'app.step' are implicitly evaluated in an atomic section.

### Conditional Behavior

I have two main options for conditional behavior:

* Branch on whether an atomic operation 'fails' or not.
* Branch on whether a register or expression is truthy.

In the former case, the latter is a trivial optimization. In the latter case, I imagine I'll still want primitives to support hierarchical transactions and backtracking, in which case we can support the former after introducing a little intermediate state, e.g. set a register within a hierarchical transaction to decide which branch to take. I'd prefer to avoid intermediate state as a requirement for conditionals, so I slightly favor the first option.

As a simplistic structure, we could support something like `"try Cond then P1 else P2"`, equivalent to `"atomic Cond; P1"` if Cond passes, P2 otherwise. Then we introduce a few primitives that conditionally fail.

However, the atomic nature of Cond can hinder some composition (and decomposition) of programs. Relevantly, we'll often have some shared prefix:

        try X; Y then A else
        try X; Z then B else
        C

A direct implementation of this will evaluate X twice. An implementation can feasibly recognize and optimise, caching the redundant 'X' prefix, but it's a little awkward in context of non-deterministic choice. Ideally, we can reduce this to a single evaluation structurally. In a front-end syntax, we could feasibly support something like the following, with 'and' as another keyword:

        try
            X and
                Y -> A
                Z -> B
            _ -> C

We can support a similar structure in the abstract syntax. Proposed encoding: `(%cond (%br X (%br Y (%do A) (%br Z (%do B) (%fail))) (%do C)))`. We can treat this as a little problem-specific language, i.e. use of '%br' is only valid in special contexts such as '%cond' or '%loop' and must terminate with '%do' or '%fail', albeit subject to annotations and macro expansions. In normal form, `(%fail)` appears only in the right branch, but in context of macro expansion we can optimize `(%br _ (%fail) R) => R` and `(%br (%fail) _ R) => R`.

To mitigate the overhead of hierarchical transactions, a compiler can precisely analyze which registers must be backed up for the transaction. In case of 'pure' computations, we can potentially eliminate need for a hierarchical transaction entirely.

### Loops

We don't absolutely need loop primitives, but it would be awkward and inefficient to encode all loops as recursive definitions. 

I propose a simple `%loop` primitive corresponding roughly to while-do loops, but in context of our unusual approach to conditional behavior. The  `"while Cond do Body"` might compile to `(%loop (%br Cond (%do Body) (%bt)))`. We aren't limited to just one condition and '%do' body, and a righmost '%bt' is essential if we intend to eventually exit the loop.

*Aside:* I hope to eventually support termination proofs on glas programs. But this will likely occur through a proof system without structural support from the intermediate language.

### Robust Partial Evaluation

Instead of a structured approach, I propose annotations specify that registers or expressions should be statically determined in context. We can easily raise an error at compile time if the annotated expectations are not met. Partial evaluation thus becomes a verified optimization, ensuring it remains robust across code changes, without truly becoming part of the semantics.

Separately, we can develop front-end syntax or libraries to more robustly support partial evaluation as we compose code, reducing the risk of errors. 

### Staged Metaprogramming

The glas program model should support flexible, user-defined AST constructors of form `(UserDef AST1 AST2 ...)`. There are a few ways to approach this. I'm seeking a simple, flexible, and robust solution.

One viable approach is akin to 'template' metaprogramming. We could support something like `(%macro Template)` where the template contains a few special primitives like `(%macro.arg K)` to substitute an AST input. To avoid complications, we can forbid 'free' macro variables or general use of '%macro.\*' words outside a local '%macro' template. However, pure templates are not very flexible. We'll also want some means to compute a program based on static arguments.

Viable model:

* `(%macro Template)` - Template is an AST that locally contains `(%macro.arg K)` and other special elements for rewriting. The locality constraint forbids substitution across definition boundaries.
* `(%macro.arg K)` - is substituted by the Kth AST argument to the constructor. This includes K=0 referring to the constructor, the macro definition, supporting anonymous recursion. 
* `%macro.argc` - is substituted by the count of AST arguments.
* `(%macro.eval Localization ASTBuilder)` - after macro substitions, ASTBuilder should represent a pure, 0--1 arity program that returns a concrete AST representation (from namespace types) on the data stack. This AST is linked based on the Localization, replaces the eval constructor, and is then subjected to another round of macro substitutions.

This model permits [non-hygienic macros](https://en.wikipedia.org/wiki/Hygienic_macro), leaving problems of hygiene to front-end syntax. Aside from these intermediate-language macros, we could also support text-based macros in a front-end syntax.

### Extensible Intermediate Language

We can always extend the intermediate language by adding new primitives. But we could also support something like a scoped 'interpretation' of the existing primitives. It might be useful to support something like `(%lang Ver AST)` or `(%lang.ver AST)` declarations, at least for the toplevel application methods. This would provide more robust integration with the runtime in case of language drift, and would allow languages adjust more flexibly.

As a convention, front-end compilers could include language declarations for most definitions, and the runtime may require it for 'app.\*', raising an warning and assuming a version otherwise.

### Non-Deterministic Choice

I propose to represent access to non-deterministic choice as a primitive (instead of handler). 

There are a few reasons for this. First, it does us very little good to intercept non-deterministic choice within a program. Only at the level of a runtime or interpreter might we heuristically guide non-deterministic choice to a useful outcome. Second, we may still control non-deterministic choice via annotations, i.e. to indicate a subprogram is observably deterministic, or at least does not introduce non-determinism (though it may invoke non-deterministic handlers). Third, use of non-deterministic choice in assertions, fuzz testing, etc. make it awkward to present as a handler.

Not quite sure what I want to call this. Perhaps '%choice'.

### Unit Types

We'll allow annotations to express types within a program. Static analysis to verify consistency of type assumptions is left to external tooling. However, annotations aren't suitable for unit types on numbers, where we might want to print type information as part of printing a number.

Instead of directly encoding unit types within number values, it may prove convenient to bind unit types to *associated* registers. With some discipline - or sufficient support from the front-end syntax - we can arrange for these associated registers to be computed statically ahead of the runtime arguments or results. We can also test that the unit type variable is static.

The unit type variable may be associated with a 'constant value' type when annotating type information for the implicit environment or an operation, and subject to static or dynamic verification. When rendering numbers, we may peek at the unit type variable for information, which is something we cannot do with annotation of types alone.

### Memoization

In context of procedural programming, memoization involves recording a trace. This trace describes the computation performed (perhaps via hash), the data observed, and outputs written. To execute a memoized computation, we search for a matching trace then write the outputs directly. If no matching trace is found, we run the computation while recording the trace, then add it to the cache.

We can improve memoization by making the traces more widely applicable, abstracting irrelevant details. For example, we might observe that a register contains 42, but a trace might match so long as a register value is greater than zero.

However, even the simplest of traces can be useful if users are careful about where they apply memoization. We can memoize a subset of procedures that represent "pure" expressions or functions to support incremental compilation, monoidal indexing of structure, and similar use cases.

### Lazy Computation? Defer.

        (%an (%an.lazy.thunk Options) Op)
        (%an %an.lazy.force (%do))
        (%an %an.lazy.spark (%do))

In the general case, we could 'thunkify' an operation by capturing each input and immediately replacing each output (whether stack or register) with a thunk. However, this requires implicit thunks. If we want explicit thunks, where 'force' is required to extract a value, we may need to explicitly list outputs and restrict the type of Op.

There are also many restrictions on Op: Op must not '%fail' or '%yield' (outside of '%atomic'). It may diverge, in which case 'force' will also diverge. Checking these conditions implies some static analysis.

I think it might be best to defer laziness until static analysis is more mature. But if there is demand, e.g. for parallel evaluation of sparks, we could get started with laziness of pure 1--1 computations. 

### Accelerators

        (%an (%an.accel (%accel.OpName Args)) Op)

Accelerators ask a compiler or interpreter to replace Op with an equivalent built-in implementation. The built-in should offer a significant performance advantage, e.g. the opportunity to leverage data representations, CPU bit-banging, SIMD, GPGPU, etc.. Arguments to an accelerator may support specialization or integration.

Ideally, the compiler or interpreter should verify equivalence between Op and Accelerator through analysis or testing. However, especially in early development and experimentation phases, it can be awkward to maintain Op and Accelerator together. During this period, we may accept `()` or an undefined name as a placeholder, emitting a TODO warning.

Accelerators support 'performance primitives' without introducing semantic primitives. If we build upon a minimalist set of semantic primitives, we'll be relying on accelerators for arithmetic, large lists, and many other use cases.

### Logging

        (%an (%an.log Chan MsgSel) Operation)

We express logging 'over' an Operation. This allows for continuous logging, animations when rendering the log, as Operation is evaluated. For example, we might evaluate log messages upon entry and exit to Operation, and also upon '%yield' and resume, and perhaps upon '%fail' when it aborts a coroutine step. Instead of a stream of messages, we might render logging as a 'tree' of animated messages. Of course, we can always use a no-op Operation for conventional logging.

Log messages are expressed as a conditional message selector, i.e. the `(%br ...)` AST nodes seen in '%cond' or '%loop'. Thus, logging may '%fail' to generate a message. The log message is computed atomically within a hierarchical transaction. The transaction is canceled after capturing the message. This allows for evaluating messages under hypothetical "what if" conditions, and evaluation of multiple messages in context of non-deterministic choice.

We evaluate MsgSel in a 'handler' environment, e.g. implicitly adding a "^" prefix to access host registers and handlers. This enables unambiguous use of a "$" prefix for parameters from the caller. In context, the 'caller' is the runtime, and parameters may include per-channel configuration of verbosity, output format(s), and other integration features. 

### Assertions

        (%an (%an.reject Chan MsgSel) Operation)

Conventionally assertions are 'positive' in nature, i.e. `"assert Condition ErrorMsg"` means something similar to `"if (not Condition) { print ErrorMsg; exit(); }"`; the Condition must hold. However, this structure forces us to either use a sequence of assertions for fine-grained error messages, or to recompute conditions as part of evaluating an ErrorMsg. To fully leverage the MsgSel structure, I propose a negative 'reject' condition, allowing for each disjunction to have its own ErrorMsg, e.g. `"reject { Cond1 -> ErrorMsg1 | Cond2 -> ErrorMsg2" }`. 

Assertions are structurally similar to logging. It is feasible to implement assertions in terms of arranging a program to halt after logging messages above a specific severity. However, use of '%an.log' sacrifices clarity of intention, the connotation that some conditions should hold as preconditions, postconditions, or invariants. Thus, I propose a dedicated '%an.reject' constructor for assertions.

As with logging, users may configure ad hoc, per-channel parameters for MsgSel. This would offer more flexible control over assertions than enabling or disabling the full channel, e.g. we could feasibly select between 'precise' and 'fast' assertions, subject to partial evaluation.

### Profiling

        (%an (%an.profile Chan) Operation)

For performance analysis, we can ask the runtime to maintain some extra metadata, e.g. for number of entries and exits and yields, time spent and relative amount of rework (due to '%fail' after '%yield'), and so on.

### Tracing? Defer.

My idea with tracing is that we can record enough of a computation to replay it in slow-motion. This isn't especially difficult, e.g. take a snapshot of relevant inputs upon entry and resumption from '%yield' (optionally including failed coroutine steps), similar to a memoization trace. However, it's also very low priority until we have an IDE that can easily render a replay.

### Projection? Defer.

My idea with projection is that we can extend '%an.log' to instead describe interactive debug views in terms of projectional editors over local registers and such. The main distinction from logging is interactivity. With logging the assumption is that messages are written into a log for future review, while for projections we're letting users directly peek and poke into a running system.

### Content-Addressed Storage

Annotations can transparently guide use of content-addressed storage for large data. The actual transition to content-addressed storage may be transparently handled by a garbage collector. Access to representation details may be available through reflection APIs but should not be primitive or pervasive.

### Data Stack

It is very convenient to introduce an intermediate data stack. 

With registers alone, we end up naming input and output registers for every little operation. To avoid this, I propose to introduce a data stack and static arity analysis. The data stack serves as an intermediate location for dataflow and a scratch space for computation. With this, the only primitive register operation we might require is linear swap of data between register and stack. Alternatively, we could support a universal 'empty' state for registers.

We can include a basic set of stack manipulators: dip, swap, copy, drop. We can use 'dip' and 'swap' together for arbitrary stack shuffling. We can interpret `d:Data` as pushing static data onto the data stack, no wrappers required. All very familiar features.

Beyond that, perhaps the only primitive data manipulations we need are dict take and put. These may be linear, too, e.g. treating it as a type error if put would overwrite existing content. Anything else can be implemented via accelerators.

*Aside:* Register and handler names may overlap in theory, but doing so is confusing and hinders independent translation. In practice, such overlap is unlikely to happen by accident, and a compiler can raise an error when it does occur.

### Data Manipulation

In an older version of glas, I had dictionary 'take' and 'put' operations on dynamic bitstring labels:

* `%take` - "rl-vr" given a radix trie and bitstring label, extract the value and the radix tree minus the label. This is equivalent to '%fail' if no such label exists.
* `%put` - "vrl-r" given a value, radix trie, and label, add the value to the radix tree at the label. This will diverge if it overwrites existing tree structure.

However, I'm not too satisfied here. I'd prefer to go even more primitive, with .

, like my older ao, where we manipulate data only in terms of individual bits and pairs. With these, implementing take and put might involve an unbounded stack or some form of intermediate zipper representation.






### Data Abstraction

It is feasible to support abstract data types (ADTs) through annotations. ADTs are useful when expressing many APIs, controlling the provenance of data.

        (%an (%an.data.wrap RegisterName) (%do))
        (%an (%an.data.unwrap RegisterName) (%do))

These wrap or unwrap the top stack element, respectively. Tying this to a RegisterName supports access control and escape analysis. Access control is via '%scope', i.e. we can hide RegisterNames from a subprogram. Static escape analysis can verify that abstract data is never written into a register with a longer lifespan than RegisterName. Dynamic escape analysis might be limited to shared database vs. runtime scopes.

Ideally, a compiler can use static analysis to eliminate most wrap/unwrap pairs within a program. But we can support specialized representations for abstract data types and dynamic checking where static analysis proves inadequate.

By default, we can '%copy' and '%drop' abstract data. This can hinder enforcement of protocols. For example, if we want to guarantee an abstract file handle is closed exactly once, we'll need to ensure the file handle isn't copied or dropped. Fortunately, it is possible to forbid these operations, thus supporting 'linear' data, with a small extension:

        (%an (%an.data.wrap.linear RegisterName) (%do))
        (%an (%an.data.unwrap.linear RegisterName) (%do))

As with abstract data, it's preferrable that safe use of linear data is determined through static analysis, avoiding runtime checks. However, dynamic enforcement is feasible if we maintain metadata (perhaps via tagged pointer bits) about whether data transitively contains linear elements.

### Environment Abstraction

An intriguing alternative to ADTs involves 'second class' bindings. Instead of calling a file API and receiving an abstract, linear file handle, we could call a file API to provide a volume of local registers to allocate the open file. The API could explicitly include methods to migrate the open file between volumes. 

The trick is then to block the caller from peeking and poking at the file representation. This is awkward to express with annotations, at least without a full type system. However, a relatively simple alternative is to extend the structure of the environment namespace to support association. 

How might we represent this? Let's consider a more concrete example. 

A caller to a file API provides a volume of registers such as "$file.\*" to the file API. The file API wants to block the caller from directly accessing these registers, so it effectively wants to use something like "$file.(FileAPIKey).\*", but where FileAPIKey is also a register name and thus unforgeable. We can bind this to another prefix such as "f." within the subprogram, such that the file API is managing "f.fd" and "f.status" and other useful data.

A viable encoding is something like:

        (%scope (%tl.arc Prefix RegisterName RegisterName) P)

In this case, we're translating Prefix to a volume of the environment indexed by a directed edge between two other registers. Constructing a reference to this volume therefore requires proving access to two other locations. (It doesn't hurt to treat a prefix like "$file." as a register name for this purpose.)

This may require some careful attention to how translations compose. But we could translate a prefix without adding the translation suffix, and treat FileAPIKey as untranslateable in composition. An actual implementation might involve a simple name mangling scheme.

Environment abstraction doesn't have a direct analog to linear data types for protocol control. However, we could keep a linear unit value in "$file.(FileAPIKey).stay-open" to force an error if registers are cleared prematurely. Or we could feasibly annotate register types with 'final' states, such that we raise an error if a register is not in an acceptable final state upon leaving scope.

### Handler Namespaces

Some observations:

* Handlers receive access to at least two 'environments': host and caller. To avoid naming conflicts, we could introduce standard or specified prefixes for these. Standard prefixes seems more convenient for most use cases.
* It is feasible to translate access to the 'caller' environment for a whole set of handlers instead of for individual handlers.
* When defining a set of (possibly) mutually recursive handlers, it may be convenient to introduce another prefix to refer to this set.
* Ideally, we can conveniently compose, extend, inherit, metaprogram, and override sets of handlers. This suggests something akin to an OOP-inspired structure, perhaps via the 'link' and 'move' translations of namespaces.
* Ideally, an application can be expressed as a set of handlers that we call from a runtime process. This provides a high level of consistency. Applications should support orthogonal persistence and live coding. Thus, it may be best to clearly separate state *allocation* from the description of a set of handlers. 
* That said, we can usefully conflate introduction of local registers and handlers, e.g. `(%local Prefix Handlers Prog)`. This gives us a clear prefix for the new handlers, ensures handlers have access to some private state aside from the host, and prevents any naming conflicts with existing handlers (because Prefix is a fresh namespace). 
* Use of `(%call HandlerName TL)` should should have roughly the same performance as use of `n:Name`, and similar optimizations - inlining, partial evaluation, tail-call optimization, etc.. 
* Macro handlers are possible if we permit a call in constructor position, e.g. `((%call HandlerName TL) AST1 AST2 ...)`. This does require that the caller is aware that the handler defines a macro, but that's consistent with names. These higher-order macros should be useful.
* Eventually, we might want to integrate full procedurally generated namespace for local handler definitions. But this can be deferred for now. 

None of this seems too difficult to integrate. Perhaps we introduce `%ns.move` and `%ns.link` and `%ns.def` and so on for building up the handler namespace. `%ns.union` for composition. Unlike full procedurally generated namespaces, we don't need 'eval' or 'read', though we get something similar from macro calls. We can still support lazy evaluation of a namespace in context of macros. Then '%local' implicitly provides the initial translation and integration.


### Type Descriptions

        (%an (%an.type TypeDesc) Op)

Types are an incomplete, abstract description of an operation. In context of glas systems, I want gradual typing as the norm, so we must allow type descriptions to have holes or "don't care" fields in them that might be filled later through inference and bidirectional type checking.


## Misc Thoughts

The initial environment model will be kept simple - a static, hierarchical structure of names. 

It seems convenient to model extension of this environment in terms of introducing 'stack objects' under a given name, conflating declaration of local registers, handlers, and hierarchical objects. We should be able to override methods or hierarchical components when declaring stack objects.

Our runtime may support use of linear or abstract data types, but we can design our APIs to mostly hide this, instead favoring 'abstract' volumes of registers held by the client and explicit 'move' operations.

Registers must have an initial state. We could initialize registers to 0 by default (the empty binary tree). But it may be convenient to support an explicit 'undefined' state for registers, distinct from containing a value.

It may be useful to wrap constant data with some indicator of type to support analysis, acceleration, and sandboxing. However, it doesn't seem essential to do so.




# Runtime Thoughts

## Desiderata

* generational, real-time GC with minimal overhead
* acceleration of:
  * lists as finger-tree ropes, with binary and array fragments
  * exact rational numbers and binary (or decimal) bignums
  * at least one low-level machine, e.g. GPGPU
* effective opportunity for JIT compilation
* effective opportunity for parallel evaluation

## Runtime Data Representation

Although glas data is logically mutable, the program constructs and manipulates data in terms of mutating *variables* in-place. This can be supported concretely by allocating every variable a local scratch space of sorts, or by heap-allocating data but tracking uniqueness, or perhaps by combining the two.

In context of garbage collection, it is convenient that data has a relatively uniform structure so we can easily follow pointers and know to not inspect integers and other data. Consider adjacent allocation of `(Header, Binary, Pointers)` triples, where the Header encodes size of binary and count of pointers and hints at how to interpret the binary. To keep it simple, Binary size includes the header and is also pointer-aligned. There may be specialized headers to further support weakrefs, caching, and other GC features, but the idea is that GC doesn't need to know much about the data.

The header may also include metadata bits for incremental or generational GC, adding a GC signal handler for automatic cleanup of FFI resources, a few metadata bits for linear, affine, and relevant types, runtime versus global scope, etc.. and perhaps a few bits for how to interpret the structure as a binary tree.

This encoding is inefficient for fine-grained allocations. We can mitigate this by favoring large allocations. However, large allocations tend to be overly specialized. To resolve this, I propose a generic approach to larger allocations: the binary is interpreted as [glas object](GlasObject.md), and associated pointers are accessed as external references. For efficient indexing of pointers, the header may indicate a runtime-specialized variant of glas object, e.g. binding 0xC0..0xFF to the first sixty-four pointers. (A few other headers could efficiently support arrays and binaries.)

The use of glas object internally within the runtime allows for both compact representations and a clean path towards content-addressed storage as the basis for virtual memory. However, I don't believe this approach has really been pursued before, so I'm somewhat uneasy about performance. Performance will depend on relative overhead of allocation versus added overhead to construct or observe the binary encoding.

*Note:* It is feasible to push some header bits into tagged pointers. This might support more efficient tracking of uniqueness, linearity, or scope. However, it doesn't seem essential.

### Header Bits

Some concrete ideas on encoding of the header bits. 

Regarding size information. A simple encoding could specify 6 bits for binary size (sizeof(ptr) * 1..64) and 6 bits for pointer count (0..63), for a total of 12 bits. These could overlap to encode a maximum allocation of roughly 512 bytes, or they could add, reaching 1024 bytes but only in rare cases.

Alternatively, we could use an exponential encoding, limiting allocations to `2^K` for small values of K, then specifying how many of those values are pointers. This can essentially use the same encoding as stems, relating allocation size to bitstring length.

        Stem Encoding
        abc1000...0     encodes three bits into N bits
        abcdefg...1     encodes (N-1) bits into N bits
        0000000...0     either unused or an extension

        Allocation Size for 4-bit encodings:
        abc1            encodes 0..7 pointers, sizeof 8 pointers 
        ab10            encodes 0..3 pointers, sizeof 4 pointers
        a100            encodes 0..1 pointers, sizeof 2 pointers
        1000 and 0000   unused (or special interpretation)

The latter would fully support 512 byte allocations with just 7 header bits, instead of 12. With 12 header bits, our max allocation is now 16kB. We lose the ability to allocate nodes that aren't a power of two in size, but limiting allocations to powers of two does simplify fragmentation as we recycle memory.

If we're willing for GC to read the binary, we could skip the bits to indicate number of pointers.

Aside from size information, I estimate:

* metadata bits for incremental or generational GC - perhaps 4 bits
* metadata bits for uniqueness (in-place update) - 1 bit
* metadata bits for data scope - 2 bits
* metadata bits for substructural types - 1 bit (linear flag) or 2 bits (affine and relevant flags)
* extensible GC header? 1 bit, uses binary data.
* an interpretation hint, e.g. glob vs binary vs array? 3 bits?

A simple 32-bit header seems adequate for all of this, a simple size encoding, and extension with future ideas.
