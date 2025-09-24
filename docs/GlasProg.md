# Program Model for Glas

The [namespace](GlasNamespaces.md) supports modules and user-defined front-end syntax. Programs are compiled to an AST structure built upon '%\*' primitives. This document describes a viable set of primitives for my vision of glas systems and some motivations for them.

## Proposed Program Primitives

Control Flow:

* `n:Name` - we can interpret a name in terms of substituting its definition. Performance may involve call stacks and tail-call optimization.
* `(%seq P1 P2)` - execute P1 then P2 in order. 
* `%pass` - the no-op. Does nothing.
* `%fail` - voluntary failure, interpretation is contextual but generally aborts the current transaction and allows observable handling of this, e.g. backtracking conditions, or aborting a coroutine step then implicitly yielding. (In contrast, most errors are treated as infinite loops.)
* `(%cond Sel)` - supports if/then/else, pattern matching, etc.. The Selector has a distinct AST structure from full programs, and everything up to %sel is evaluated atomically within a hierarchical transaction. Error if Sel fails to select any action.
  * `(%sel P)` - selected action. The condition should be equivalent to running prior chain of branch conditions prior chain of passing branch conditions, followed by running P.
  * `(%br C L R)` - run branch condition C as a program. If C fails, undo then process selector R. Otherwise, process selector L.  
  * `%bt` - backtrack to the most recent successful branch condition, cause it to fail, then take the right branch. Or if this is already the rightmost branch, behavior is contextual: error for %cond, exit for %loop, etc.. 
    * As a special rule, we can optimize `(%br C %bt R) => R` even if C is divergent. Thus, in normal form, %bt appears only in R position.
  * Sel also supports macro and annotation AST nodes.
* `(%loop Sel)` - Repeatedly runs Sel until it fails to select an action. Upon failure, exits loop. Essentially, a while-do loop with integrated pattern matching. 
* `(%co P1 P2)` - execute P1 and P2 as coroutines. To transfer control, a coroutine must voluntarily %yield or %fail. Each coroutine has its own data stack but shares access to methods and registers. Fork-join behavior: %co will continuously yield until all of its coroutines complete. Scheduling is contextual but shall guarantee associativity. In case of non-deterministic scheduling, coroutines support preemption and parallelism with [optimistic concurrency control](https://en.wikipedia.org/wiki/Optimistic_concurrency_control). 
* `%yield` - commits operations of a coroutine since prior yield, providing an opportunity for other coroutines to observe and interact with shared state. Each yield-to-yield step is implicitly an atomic transaction. A step may %fail, implicitly rewinding to a prior %yield.
* `(%atomic P)` - an atomic operation may yield, but will only resume internally. Fails only if all internal resumptions are failing. 
* `(%choice P1 P2)` - run a choice of P1 or P2. If a choice fails, we'll backtrack and try another, thus a choice only fails if all options fail. Evaluation order is contextual, but shall guarantee associativity. If the context is non-deterministic evaluation order, choice will diverge only if all non-failing options diverge.
* `%error` - explicit divergence. Unlike '%fail', we do not backtrack on error; it's equivalent to an infinite loop.



Evaluation Order Control (tentative):

* `(%sched Order P)` - (tentative) It is feasible to specify a scheduling rule for coroutines introduced within a given scope. Viable schedules:
  * `%order.any` - Embrace non-deterministic choice! In context of transactions, we get a limited form of preemption because a scheduler may abort an uncommitted choice to try another. It is possible to evaluate many choices at once. Many coroutines can commit at once if as there are no conflicts, aka optimistic concurrency control.
  * `%order.rr` - Round robin. After a coroutine yields, schedule the next in sequence, then cycle upon reaching the end. Not valid for choice. 
  * `%order.lr` - Left to right. Always run the leftmost coroutine that can make progress. Or evaluate choices left to right.
* `(%choice.order Order P)` - (tentative)


Data Manipulation:
* `d:Data` - push copy of data to top of data stack
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

Environment Manipulation:
* `(%scope TLL P)` - translate RegisterNames and MethodNames in scope of P. TLL is same as in the AST representation (see namespaces).
* `(%rw RegisterName)` - swap data between named register and top data stack element. 
* `(%call MethodName TLL)` - invoke a method, applying a translation to control the method's view of the caller's environment.
* `(%call.avail MethodName)` - an ifdef of sorts for methods. Pass (no-op) if method is defined to anything other than `()`, otherwise fails. (Here `()` is explicitly undefined.)
* `(%local Prefix P)` - introduces a set of registers in scope of P. Will mask Prefix, i.e. equivalent to `(%scope {Prefix => NULL} (%local Prefix P))`. The set of registers is inferred from use, thus do not need to be declared explicitly, but must be finite (check recursion). All registers are initialized to zero.
* `(%def Prefix Methods P)` - introduces a set of methods in scope of P. Will mask Prefix. 

Method Namespaces:


Tooling and Evolution:

* `(%an Annotation P)` - Equivalent to P, modulo performance, analysis, instrumentation, and reflection. Annotations provide ad hoc guidance to compilers, interpeters, optimizers, debuggers, typecheckers, theorem provers, and similar tools. 
* `(%lang Version P)` - Language declaration for P. Declarations should be idempotent, so this does nothing if the language version doesn't change. However, in the general case it may result in support for different primitives and distinct interpretations of existing primitives in P. Version may be encoded as a dict.

Metaprogramming:
* `(%eval Localization)` - pops AST representation from data stack, links it via the given Localization, performs further expansions as needed, then runs the AST as a program. In most contexts, AST must be statically computed, i.e. implicit '%an.eval.static'. 

* `(%macro Template)` - Template is an AST that contain special '%macro.\*' elements for rewriting. To simplify implementation and user comprehension, the compiler may enforce locality by rejecting '%macro.\*' elements outside '%macro' within the same definition, i.e. no 'free' macro variables. Macros should be supported for all ASTs used in expressing programs. 
* `(%macro.arg K)` - is substituted by the Kth AST argument to the constructor. This includes K=0 referring to the constructor, the macro definition, thus supporting anonymous recursion.
* `%macro.argc` - is substituted by the count of AST arguments after arg 0.
* `(%macro.eval Localization ASTBuilder)` - first perform macro expansion for Localization and ASTBuilder. ASTBuilder must represent a 0--1 program returning an AST representation. We link via Localization, substitute the linked AST, then perform a second round of macro expansion.

I might be changing macros into an AST primitive feature.

### Annotations

        (%an Annotation Operation)

Annotations are not executable as programs, but they will support macros.

Acceleration:
* `(%an.accel Accelerator)` - non-semantic performance primitives. Indicates that a compiler or interpreter should substitute Op for a built-in Accelerator. By convention, an Accelerator has form `(%accel.OpName Args)` and is invalid outside of '%an.accel'. See *Accelerators*.

Composition:
* `(%an.compose Anno1 Anno2 ...)` - composition of annotations, applies right to left, e.g. can rewrite `(%an (%an.compose A1 A2 A3) Op)` to  `(%an A1 (%an A2 (%an A3 Op)))`. This is mostly useful for metaprogramming.

Instrumentation:
* `(%an.log Chan MsgSel)` - printf debugging! Rather sophisticated. See *Logging*.
* `(%an.error.log Chan MsgSel)` - log messages generated only when Operation halts due to an obvious divergence error, such as '%error' or an assertion failure.
* `(%an.assert Chan ErrorMsgSel)` - assertions structured as logging an error message, i.e. an assertion passes only if no error message is generated. We'll treat choice in the selector branches as a conjunction of conditions.
* `(%an.assert.static Chan ErrorMsgSel)` - the same as assert, except it's also an error if the conditions cannot be computed at compile-time.
* `(%an.profile Chan BucketSel)` - record performance metadata such as entries and exits, time spent, yields, fails, and rework. Profiles may be aggregated into buckets based on BucketSel. 
* `(%an.trace Chan MsgSel)` - record information to support *replay* of a computation. The MsgSel allows for conditional tracing and attaches a helpful message to each trace. See *Tracing*.
* `(%an.chan.scope TLL)` - apply a prefix-to-prefix translation to Chan names in Operation. 

Validation:
* `(%an.arity In Out)` - express expected data stack arity for Operation. In and Out must be non-negative integers. Serves as an extremely simplistic type description. 
* `%an.atomic.reject` - error if running Operation from within an atomic scope, including %atomic and %br conditions. Useful to detect errors early for code that diverges when run within a hierarchical transaction, e.g. waiting forever on a network response.
  * `%an.atomic.accept` - to support simulation of code containing %an.atomic.reject, e.g. with a simulated network, we can pretend that Operation is running outside a hierarchical transaction, albeit only up to external method calls.
* `(%an.data.wrap RegisterName)` - support for abstract data types, hides top stack element from observation until unwrapped with the same RegisterName, implies scope for data (e.g. don't store to longer-lived register). Can feasibly be enforced statically or dynamically, or safely ignored.
  * `(%an.data.unwrap RegisterName)` - removes the wrapper, allowing observation of the data.
  * `(%an.data.wrap.linear RegisterName)` - as wrap, but attempts to copy or drop the wrapped value (including implicitly, such as exiting a local registers scope or a coroutine terminating with data on the stack) will diverge if detected at runtime or become a compile-time error if recognized earlier. Dynamic enforcement is feasible with one metadata bit per value.
  * `(%an.data.unwrap.linear RegisterName)` - as unwrap for linear data
* `%an.data.static` - Indicates that top stack element should be statically computable. Exercise left to compiler!
* `%an.eval.static` - Indicates that all '%eval' steps in Operation must be linked at compile-time. This is the default for glas applications, but it doesn't hurt to make the assumption explicit more locally.
* `(%an.type TypeDesc)` - Describes a partial type of Operation. Or, with a no-op and identity type, we can partially describe the Environment. TypeDesc TBD.

Incremental computing:
* `(%an.memo Hints)` - memoize a computation. For simplicity and immediate utility, initial support for memoization may be restricted to pure data stack functions, perhaps extended to pure methods. Hints may indicate persistent vs. ephemeral memoization, cache-invalidation policy, and other options.
* `(%an.checkpoint Hints)` - when retrying a transaction, instead of recomputing from the start it can be useful to rollback partially and retry from there. In this context, a checkpoint suggests a rollback boundary. A compiler may heuristically eliminate unnecessary checkpoints, and Hints may guide heuristics. 

Future development:
* type declarations. I'd like to get bidirectional type checking working in many cases relatively early on.
* tail-call declarations. Perhaps not per call but rather an indicator that a subroutine can be optimized for static stack usage, optionally up to method calls. 
* stowage. Work with larger-than-memory values via content-addressed storage.
* lazy computation. Thunk, force, spark. Requires some analysis of registers written.
* debug trace. Probably should wait until we have a clear idea of what a trace should look like. 
* debug views. Specialized projectional editors through debuggers.

### Accelerators

        (%an (%an.accel (%accel.OpName Args)) Op)

Todo: list some useful accelerators.

## Design Motivations

Some discussions that led to the aforementioned selection of primitives.

### Fixed Arity

I originally had the associative ops like %seq, %co, %choice accept variable numbers of arguments. But this has caused me headaches for metaprogramming, requiring much more complicated metaprogramming to process or generate. I'm considering to push the basic substitution-like metaprogramming into the namespace structure with something like lambdas, and it's causing me headaches.

In any case, this should be an intermediate language. It shouldn't be a problem for a front-end compiler to generate a little more structure. The repeating names can be interned.

### Operation Environment

Programs operate on a stable environment of named methods and registers. Methods and registers never share a name, and are typically partitioned on separate prefixes (though the two can be mixed via translations). In practice, methods are defined explicitly, while registers are inferred from use.

The standard environment provided to an application:

* 'app.\*' - self-reference between application methods
* 'sys.\*' - runtime-provided APIs
* 'db.\*' - shared, persistent registers bound to configured database
* 'g.\*' - application private registers, initially zero

When applications define methods, those methods receive:

* '.\*' - self-reference between the methods
* '^\*' - reference to the host environment
* '$\*' - reference to caller's environment

These are simply the inital values, subject to translation. Applications are implemented as method definitions with a standard, runtime-provided translation to simplify recognition and documentation.

Registers are inferred from use. In context of recursion, the set of external registers in use must be finite, reaching a fixpoint. In general, registers may contain arbitrary glas data of any size, but type annotations may restrict things, enabling a compiler to use specialized data representations.

Methods are explicitly defined, and we can test for their availability before invoking them.

Register and method names never overlap in context. This can be enforced by separating prefixes upon introduction, and masking the prior environment on prefixes.

Toplevel application methods ('app.\*') receive access to a finite set of methods and registers from the runtime. See [glas apps](GlasApps.md) for details. The program may include annotations describing the assumed or expected environment, allowing for validation and optimization.

### In-Place Update? Defer.

It is possible to support in-place update of 'immutable' data if we hold the only reference to its representation. This can be understood as an opportunistic optimization of garbage-collection: allocate, transfer, and collect in one step. In glas programs, this would be feasible with accelerators, such as a list update operator could swap a list element without reallocatng the list. This is especially useful if the list is represented by an array.

However, pervasive use of transactions and backtracking complicates this optimization. It is convenient to capture a snapshot of registers so we can revert if necessary. Although this snapshot isn't a logical copy and thus doesn't conflict with linear types, it is a shared representation and thus does hinder in-place update.

A viable alternative is to maintain a 'log' of updates to apply later. For example, a runtime could feasibly represent the updated list as a special `(update log, original list ref)` pair within runtime. This might generalize to [log-structured merge-tree (LSM trees)](https://en.wikipedia.org/wiki/Log-structured_merge-tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)).

This doesn't quite support the ideal of in-place update. We must allocate that log, and perhaps some metadata to track elements to process further upon commit. But perhaps we can still perform in-place update upon commit, and benefit from editing nearer to the tree root. This seems a viable approach.

Meanwhile, we'll still support decent persistent data structures by default, e.g. finger-tree ropes still support O(log(N)) updates in the center, O(1) at the edges, and we can easily use a pair as a gap buffer.

### Tail Call Optimization

Tail calls allow for recursive definitions in finite stack space. This optimization isn't always worthwhile - not when we instead model the data stack via memory allocation - but it can be useful in enough cases.

This can be viewed as a form of static garbage collection, recycling memory allocations on the call stack. To support a non-moving TCO, we could feasibly unroll a loop a little then recycle all the relevant locations.

In glas systems, I want tail calls to be a robust, checked optimization. But it's awkward to express this on a definition directly on names. Perhaps instead we might express an annotation that the stack is finite? Or finite up to external handlers?



Annotations can indicate that calls are expected to be tail calls. Further, I hope to encourage tail calls as the default form of recursion, as it greatly simplifies compilation.

Even if all recursion is tail calls, we can model dynamic stacks in terms of registers containing list values. Performance in this case might be mitigated by preallocation of a list buffer for in-place update and use as a stack. This could be supported through annotations or accelerators.

### Algebraic Effects and Methods and Stack Objects

I propose that most effects APIs are expressed in terms of invoking 'methods' in the environment. This allows for overrides by the calling program independent of the namespace structure. There may be a few special exceptions, e.g. non-deterministic choice may be expressed as a primitive and restricted through annotations instead of by manipulating the environment.

Unlike first-class functions or closures, methods introduced by a subprogram cannot be "returned" to a caller. They can only be passed to further subprograms in context. However, it may be convenient to introduce a notion of 'objects' on the stack, modular collections of local state and methods, rather than focus on individual methods.

When a method is invoked, it must receive access to two environments - host and caller. With the 'stack object' concept, it is useful to conflate a third environment: local state. Local registers may then be modeled as a stack object with no methods. To resist naming conflicts, access to these environments from within a method may be distinguished by prefixes, e.g. "^" for host, "$" for caller, and "." for local state. (We may need to see how '.' interacts with hierarchical objects.)

Stack objects should be able to hierarchically compose more stack objects. It seems feasible to express stack objects as namespace constructors, but it may prove simpler to introduce dedicated primitives to declare objects.

*Aside:* It might be interesting to express an application as a method 'stack object' instead of a collection of definitions. This could be supported by a runtime via application settings.

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

I propose to initially support two layers:




* Program layer, obtain AST via partial evaluation. Support via `(%eval Localization)`.
* AST layer, obtain AST via 

At the program layer, we could feasibly support something like `(%eval Localization)` that receives a (usually static) AST from the data stack and links it.

The glas program model should support user-defined AST constructors of form `(UserDef AST1 AST2 ...)`. There are a few ways to approach this. I'm seeking a simple, flexible, and robust solution.

One viable approach is akin to 'template' metaprogramming. We could support something like `(%macro Template)` where the template contains a few special primitives like `(%macro.arg K)` to substitute an AST input. To avoid complications, we can forbid 'free' macro variables or general use of '%macro.\*' words outside a local '%macro' template. However, pure templates are not very flexible. We'll also want some means to compute a program based on static arguments.

Viable model:

* `(%macro Template)` - Template is an AST that locally contains `(%macro.arg K)` and other special elements for rewriting. The locality constraint forbids substitution across definition boundaries.
* `(%macro.arg K)` - is substituted by the Kth AST argument to the constructor. This includes K=0 referring to the constructor, the macro definition, supporting anonymous recursion. 
* `%macro.argc` - is substituted by the count of AST arguments.
* `(%macro.eval Localization ASTBuilder)` - after macro substitions, ASTBuilder should represent a pure, 0--1 arity program that returns a concrete AST representation (from namespace types) on the data stack. This AST is linked based on the Localization, replaces the eval constructor, and is then subjected to another round of macro substitutions.

This model permits [non-hygienic macros](https://en.wikipedia.org/wiki/Hygienic_macro), leaving problems of hygiene to front-end syntax. Aside from these intermediate-language macros, we could also support text-based macros in a front-end syntax.

However, this doesn't support metaprogramming based on static parameters. For that, we might need a constructor like `(%eval Localization)` that receives its AST from the data stack.

ASTBuilder may receive access to some compile-time effects via '%call'. So far, I don't have a much better idea on how to integrate loading files and DVCS resources. Might need to introduce a notion of local handlers to the macro layer?



### Extensible Intermediate Language

We can always extend the intermediate language by adding new primitives. But we could also support something like a scoped 'interpretation' of the existing primitives. It might be useful to support something like `(%lang Ver AST)` or `(%lang.ver AST)` declarations, at least for the toplevel application methods. This would provide more robust integration with the runtime in case of language drift, and would allow languages adjust more flexibly.

As a convention, front-end compilers could include language declarations for most definitions, and the runtime may require it for 'app.\*', raising an warning and assuming a version otherwise.

### Non-Deterministic Choice

I propose to express non-deterministic choice as a primitive, not relying entirely on runtime 'effects' to introduce non-determinism. This doesn't truly reduce control over where choice is introduced. Relevantly, we could introduce a few annotations to control use of choice:

* annotation to reject choice entirely (forbid non-deterministic effects and RPC, too)
* annotation to reject *introduction* of choice (choice allowed via methods in scope)

Beyond a few annotations, we might want to introduce some means to control non-deterministic choice. However, I'm unclear what this might be.

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

Essentially, primitives with a reference implementation.

        (%an (%an.accel (%accel.OpName Args)) Op)

Accelerators ask a compiler or interpreter to replace Op with an equivalent built-in implementation. The built-in should offer a significant performance advantage, e.g. the opportunity to leverage data representations, CPU bit-banging, SIMD, GPGPU, etc.. Arguments to an accelerator may support specialization or integration.

Ideally, the compiler or interpreter should verify equivalence between Op and Accelerator through analysis or testing. However, especially in early development and experimentation phases, it can be awkward to maintain Op and Accelerator together. During this period, we may accept `()` or an undefined name as a placeholder, emitting a TODO warning.

Accelerators support 'performance primitives' without introducing semantic primitives. If we build upon a minimalist set of semantic primitives, we'll be relying on accelerators for arithmetic, large lists, and many other use cases.

### Logging

        (%an (%an.log Chan MsgSel) Operation)

We express logging 'over' an Operation. This allows for continuous logging, animations when rendering the log, as Operation is evaluated. For example, we might evaluate log messages upon entry and exit to Operation, and also upon '%yield' and resume, and perhaps upon '%fail' when it aborts a coroutine step. Instead of a stream of messages, we might render logging as a 'tree' of animated messages. Of course, we can always use a no-op Operation for conventional logging.

Log messages are expressed as a conditional message selector, i.e. the `(%br ...)` AST nodes seen in '%cond' or '%loop'. Thus, logging may '%fail' to generate a message. The log message is computed atomically within a hierarchical transaction. The transaction is canceled after capturing the message. This allows for evaluating messages under hypothetical "what if" conditions, and evaluation of multiple messages in context of non-deterministic choice.

We evaluate MsgSel in a 'method' environment, e.g. implicitly adding a "^" prefix to access host registers and methods. This enables unambiguous use of a "$" prefix for parameters from the caller. In context, the 'caller' is the runtime, and parameters may include per-channel configuration of verbosity, output format(s), and other integration features. 

### Assertions

        (%an (%an.reject Chan MsgSel) Operation)

Conventionally assertions are 'positive' in nature, i.e. `"assert Condition ErrorMsg"` means something similar to `"if (not Condition) { print ErrorMsg; exit(); }"`; the Condition must hold. However, this structure forces us to either use a sequence of assertions for fine-grained error messages, or to recompute conditions as part of evaluating an ErrorMsg. To fully leverage the MsgSel structure, I propose a negative 'reject' condition, allowing for each disjunction to have its own ErrorMsg, e.g. `"reject { Cond1 -> ErrorMsg1 | Cond2 -> ErrorMsg2" }`. 

Assertions are structurally similar to logging. It is feasible to implement assertions in terms of arranging a program to halt after logging messages above a specific severity. However, use of '%an.log' sacrifices clarity of intention, the connotation that some conditions should hold as preconditions, postconditions, or invariants. Thus, I propose a dedicated '%an.reject' constructor for assertions.

As with logging, users may configure ad hoc, per-channel parameters for MsgSel. This would offer more flexible control over assertions than enabling or disabling the full channel, e.g. we could feasibly select between 'precise' and 'fast' assertions, subject to partial evaluation.

### Profiling

        (%an (%an.profile Chan) Operation)

For performance analysis, we can ask the runtime to maintain some extra metadata, e.g. for number of entries and exits and yields, time spent and relative amount of rework (due to '%fail' after '%yield'), and so on.

### Tracing

We can ask the runtime to record sufficient information to replay a computation. This is expensive, so we might configure tracing (per Chan) to perform random samples or something, switching between traced and untraced versions of the code.

What information do we need?

* input registers - initial, updates after yield, checkpoints from scrubbing
* updates and return values from calling external methods 
* non-deterministic choices and scheduling, optionally including backtracking
* for long-running computations, heuristic checkpoints for timeline scrubbing 
* for convenience, a complete representation of the subprogram being traced
* consider adding the contextual stack of log messages etc.

I think this won't be easy to implement, but it may be worthwhile.

Support for conditional tracing may also be useful. 

### Breakpoints

I'm not fond of debugging via breakpoints, but some people swear by them. Conventional breakpoints can be improved a little with a conditional check. In context of glas annotations, we could also 

Break


Anyhow, I think we can do a lot better than inserting rigid 'breakpoints' into programs. We could instead apply 'breakpoints' to log messages or similar.

### Projection? Defer.

My idea with projection is that we can extend '%an.log' to instead describe interactive debug views in terms of projectional editors over local registers and such. The main distinction from logging is interactivity. With logging the assumption is that messages are written into a log for future review, while for projections we're letting users directly peek and poke into a running system.

### Content-Addressed Storage

Annotations can transparently guide use of content-addressed storage for large data. The actual transition to content-addressed storage may be transparently handled by a garbage collector. Access to representation details may be available through reflection APIs but should not be primitive or pervasive.

### Data Stack

It is very convenient to introduce an intermediate data stack. 

With registers alone, we end up naming input and output registers for every little operation. To avoid this, I propose to introduce a data stack and static arity analysis. The data stack serves as an intermediate location for dataflow and a scratch space for computation. With this, the only primitive register operation we might require is linear swap of data between register and stack. Alternatively, we could support a universal 'empty' state for registers.

We can include a basic set of stack manipulators: dip, swap, copy, drop. We can use 'dip' and 'swap' together for arbitrary stack shuffling. We can interpret `d:Data` as pushing static data onto the data stack, no wrappers required. All very familiar features.

Beyond that, perhaps the only primitive data manipulations we need are dict take and put. These may be linear, too, e.g. treating it as a type error if put would overwrite existing content. Anything else can be implemented via accelerators.

*Aside:* Register and method names may overlap in theory, but doing so is confusing and hinders independent translation. In practice, such overlap is unlikely to happen by accident, and a compiler can raise an error when it does occur.

### Data Manipulation

In an older version of glas, I had dictionary 'take' and 'put' operations for full bitstring labels:

* `%take` - "rl-vr" given a radix trie and bitstring label, extract the value and the radix tree minus the label. This is equivalent to '%fail' if no such label exists.
* `%put` - "vrl-r" given a value, radix trie, and label, add the value to the radix tree at the label. This will diverge if it overwrites existing tree structure.

However, I felt this was a little awkward for primitives. Doing a little too much. Requires too much knowledge of representations. I propose to go a bit more primitive for glas, like operating on individual bits. What do we need? Perhaps the following is sufficient:

* Pair and unpair.
* Left and unleft.
* Right and unright.

I don't need anything special for unit/zero, since if a value isn't a pair, left, or right it must be unit. With this set, we'll absolutely be relying on accelerators for the vast majority of data manipulations. But that isn't a bad thing - accelerators are essentially primitives with a reference implementation.

### Data Abstraction

It is feasible to support abstract data types (ADTs) through annotations. ADTs are useful when expressing many APIs, controlling the provenance of data.

        (%an (%an.data.wrap RegisterName) (%do))
        (%an (%an.data.unwrap RegisterName) (%do))

These wrap or unwrap the top stack element, respectively. Tying this to a RegisterName supports access control and escape analysis. Access control is via '%scope', i.e. we can hide RegisterNames from a subprogram. Static escape analysis can verify that abstract data is never written into a register with a longer lifespan than RegisterName. Dynamic escape analysis might be limited to shared database vs. runtime scopes.

Ideally, a compiler can use static analysis to eliminate most wrap/unwrap pairs within a program. But we can support specialized representations for abstract data types and dynamic checking where static analysis proves inadequate.

By default, we can '%copy' and '%drop' abstract data. This can hinder enforcement of protocols. For example, if we want to guarantee an abstract file handle is closed exactly once, we'll need to ensure the file handle isn't copied or dropped. Fortunately, it is possible to forbid these operations, thus supporting 'linear' data, with a small extension:

        (%an (%an.data.wrap.linear RegisterName) (%do))
        (%an (%an.data.unwrap.linear RegisterName) (%do))

As with abstract data, it's preferable that safe use of linear data is determined through static analysis, avoiding runtime checks. However, dynamic enforcement is feasible if we maintain metadata (perhaps via tagged pointer bits) about whether data transitively contains linear elements.

### Environment Abstraction

An ADT essentially allows the client to move things without accessing them. An intriguing alternative is to let a client allocate a space without access it, i.e. hiding parts of a client-allocated environment from that client.

This isn't difficult. Essentially, may require multiple names to reference a register. Thus, by controlling access to one of those names, we prevent direct access to the register. System APIs can easily control an application's access to specific names.

I've decided to push this feature up to the namespace model, e.g. with specialized translation of "/" separators in names.

### Protocol Registers

Linear types are useful for enforcing protocols, but dynamic enforcement has higher overhead than I'd prefer, and I'd prefer to avoid dynamic representations of data abstraction where feasible.

. A viable alternative is environment abstraction plus annotations that some registers must be manually cleared before exit. Or perhaps on yield?.

### Method Namespaces

Some observations:

* Methods receive access to at least two 'environments': host and caller. To avoid naming conflicts, we could introduce standard or specified prefixes for these. Standard prefixes seems more convenient for most use cases.
* It is feasible to translate access to the 'caller' environment for a whole set of methods instead of for individual methods.
* When defining a set of (possibly) mutually recursive methods, it may be convenient to introduce another prefix to refer to this set.
* Ideally, we can conveniently compose, extend, inherit, metaprogram, and override sets of methods. This suggests something akin to an OOP-inspired structure, perhaps via the 'link' and 'move' translations of namespaces.
* Ideally, an application can be expressed as a set of methods that we call from a runtime process. This provides a high level of consistency. Applications should support orthogonal persistence and live coding. Thus, it may be best to clearly separate state *allocation* from the description of a set of methods. 
* That said, we can usefully conflate introduction of local registers and methods, e.g. `(%local Prefix Methods Prog)`. This gives us a clear prefix for the new methods, ensures methods have access to some private state aside from the host, and prevents any naming conflicts with existing methods (because Prefix is a fresh namespace). 
* Use of `(%call MethodName TL)` should should have roughly the same performance as use of `n:Name`, and similar optimizations - inlining, partial evaluation, tail-call optimization, etc.. 
* Macro methods are possible if we permit a call in constructor position, e.g. `((%call MethodName TL) AST1 AST2 ...)`. This does require that the caller is aware that the method defines a macro, but that's consistent with names. These higher-order macros should be useful.
* Eventually, we might want to integrate full procedurally generated namespace for local method definitions. But this can be deferred for now. 

None of this seems too difficult to integrate. Perhaps we introduce `%ns.move` and `%ns.link` and `%ns.def` and so on for building up the method namespace. `%ns.union` for composition. Unlike full procedurally generated namespaces, we don't need 'eval' or 'read', though we get something similar from macro calls. We can still support lazy evaluation of a namespace in context of macros. Then '%local' implicitly provides the initial translation and integration.

Ideally, I'd unify method namespaces and the configuration namespace, use the same representations for both. Perhaps procedural generation is not the best direction to *start* with.

### Type Descriptions

        (%an (%an.type TypeDesc) Op)

We can just invent some primitive type descriptions like '%type.int' or whatever, things a typechecker is expected to understand without saying, and build up from there. It isn't a big deal if we want to experiment with alternatives later.


## Misc Thoughts

The initial environment model will be kept simple - a static, hierarchical structure of names. 

It seems convenient to model extension of this environment in terms of introducing 'stack objects' under a given name, conflating declaration of local registers, methods, and hierarchical objects. We should be able to override methods or hierarchical components when declaring stack objects.

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

The header may also include metadata bits for incremental or generational GC, adding a GC signal method for automatic cleanup of FFI resources, a few metadata bits for linear, affine, and relevant types, runtime versus global scope, etc.. and perhaps a few bits for how to interpret the structure as a binary tree.

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
