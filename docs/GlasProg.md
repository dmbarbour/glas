# Program Model for Glas

The [namespace](GlasNamespaces.md) supports modules and user-defined front-end syntax. Programs are compiled to an AST structure built upon '%\*' primitives. This document describes a viable set of primitives for my vision of glas systems and some motivations for them.

## Proposed Program Primitives

Program Control:

* `n:Name` - we'll generally try to interpret a name in terms of inlining its definition, though recursion requires special attention for performance reasons.
* `(%do P1 P2 P3 ...)` - execute P1 then P2 then P3 etc. in sequence. We also use `(%do)` as our primary no-op, and '%do' as the constructor for committed action in a decision tree structure.
* `%fail` - voluntary failure, interpretation is contextual but will gend to abort the current transaction and allow some observable handling of this, e.g. backtracking conditions, or aborting a coroutine step until observed conditions change. For contrast, involuntary failures such as type errors are instead modeled as divergence like an infinite loop.
* `(%cond Sel)` - supports if/then/else, pattern matching, etc.. The Sel type consists primarily of '%br' nodes, but may terminate with '%do' or '%fail'. It's a divergence error if no operation is matched.
  * `(%br C BrL SelR)` - run C within a hierarchical transaction. If C terminates normally, run SelL. If C fails voluntarily, abort then run SelR. Note that committing C is left to SelL. 
  * `(%do P1 ...)` - in context of Sel, represents committed action. Commit the entire chain of prior branch conditions, then run '%do' in the same transactional context as '%cond' or '%loop'.
  * `%fail` - in context of Sel, represents the lack of a case on the current branch. Causes computation to backtrack, aborting the most recent successful branch condition then trying the SelR path. In normal form, appears only in SelR position because we can optimize `(%br C %fail SelR) => SelR`.
  * Beyond these, Sel may also support language declarations, annotations, and macros.
* `(%loop Sel)` - supports anonymous loops, uses same Sel type as '%cond'. In the simplest case, `"while Cond do Body"` becomes `(%loop (%br Cond (%do Body) %fail))`. But we can integrate loop conditions with an action selector. Programs may also express loops via recursive definitions!
* `(%c P1 P2 P3 ...)` - execute P1, P2, P3, etc. concurrently as coroutines with independent stacks and voluntary yield of shared registers and handlers. This operation exits only when all coroutines exit, i.e. fork-join behavior. The scheduler shall guarantee associativity.
* `%yield` - pauses a computation, providing an opportunity for concurrent computations to operate. Resumption is implicit. Each yield-to-yield step should be logically atomic, thus '%fail' implicitly rewinds to a prior '%yield' then awaits changes to observed state.
* `(%atomic P)` - an atomic operation may yield, but will only resume internally. Fails only if all internal resumptions are failing.
* `(%choice P1 P2 P3 ...)` - represents non-deterministic runtime choice of P1 or P2 or P3. 

* `(%sched Schedule P)` - (tentative; low priority) local guidance of a continuation scheduler, would only apply to continuations expressed within P. 

Data Stack Manipulation:
* `d:Data` - push copy of data to top of data stack
* `(%dip P)` - run P while hiding top of data stack
* `%swap` - flip top two stack elements. i.e. "ab-ba"
* `%copy` - copy top stack element, i.e. "a-aa".
* `%drop` - drop top stack element, i.e. "a-".  

Data Manipulation:
* `%take` - "rl-vr" given a radix trie and bitstring label, extract the value and the radix tree minus the label. This is equivalent to '%fail' if no such label exists.
* `%put` - "vrl-r" given a value, radix trie, and label, add the value to the radix tree at the label. This will diverge if it overwrites existing tree structure.

Environment Interaction:
* `(%call HandlerName TL)` - invoke a handler defined in the environment, with a translation applied to the handler's view of the caller's context (may be identity). 
* `(%rw RegisterName)` - exchange data between a register and top stack element.

Environment Control:
* `(%scope TL P)` - applies TL to RegisterNames and HandlerNames when running P. 
introducing locals and handlers
* TODO - *introducing local registers and handlers*

Tooling and Evolution:

* `(%lang Version AST)` - Language declaration. Version is a dict for ad hoc extensibility. Idempotent, thus usually equivalent to AST, but in special cases we might apply adapters or switch interpreters to integrate languages.
* `(%an Annotation AST)` - Equivalent to AST, but Annotations provide ad hoc guidance to compilers, interpeters, optimizers, debuggers, typecheckers, theorem provers, and similar tools. By convention, Annotations have form `(%an.ctor Args)`, and are not directly interpreted as programs. 

Metaprogramming:
* `(%macro Template)` - Template is an AST that locally contains special '%macro.\*' elements for rewriting. 'Locally' means no rewriting across definition boundaries. Applied as a user-defined constructor.
* `(%macro.arg K)` - is substituted by the Kth AST argument to the constructor. This includes K=0 referring to the constructor, the macro definition, thus supporting anonymous recursion.
* `%macro.argc` - is substituted by the count of AST arguments after arg 0.
* `(%macro.eval Localization ASTBuilder)` - after macro substitions, ASTBuilder should represent a pure, 0--1 arity program that returns an AST value (see namespace types). This AST is localized, replaces the eval constructor, then receives another round of macro substitutions.

### Annotations

        (%an Annotation Operation)

Critical annotations, necessary in early versions of runtime:

* `(%an.accel Accelerator)` - non-semantic performance primitives. Indicates that a compiler or interpreter should substitute Op for a built-in Accelerator. By convention, an Accelerator has form `(%accel.OpName Args)` and is invalid outside of '%an.accel'. See *Accelerators* later.
* `(%an.memo MemoHints)` - we don't immediately need full-featured memoization, but at least enough for incremental compilation, e.g. persistent memoization of pure computations. 
* `(%an.log Chan MsgSel)` - printf debugging! Rather sophisticated. See *Logging*.
* `(%an.reject Chan MsgSel)` - negative assertions structured as logging an error message. If there is no error message, the assertion passes, otherwise we diverge.
* `(%an.profile Chan Options)` - record performance metadata such as entries and exits, time spent, yields, fails, and rework. 
  * TODO: this will benefit from dynamic virtual channels for fine-grained profiling
* `(%an.chan.scope TL)` - a simple prefix rewrite on Chan names for Operation. 
* `(%an.static)` - indicates that the top stack element should be statically computed.

Nice to haves:
* stowage. Work with larger-than-memory values via content-addressed storage.
* type safety. I'd like to get bidirectional type checking working in many cases relatively early on.
* tail-call declarations. This could feasibly be presented as part of a function type.
* lazy computation. Thunk, force, spark. Requires some analysis of registers written.
* debug trace. Probably should wait until we have a clear idea of what a trace should look like. 
* debug views. Specialized projectional editors through debuggers.

### Accelerators

        (%an (%an.accel (%accel.OpName Args)) Op)

TODO: proposed initial list of accelerators. 

## Design Motivations

Some discussions that led to the aforementioned selection of primitives.

### Operation Environment

I propose to express programs as operating on a stable, labeled environment. This environment includes stateful registers and callable 'handlers' for abstraction of state or effects. A program may introduce local registers and handlers in scope of a subprogram, and may translate or restrict a subprogram's access to the program's environment.

To keep it simple and consistent, I propose that registers and handlers are named with simple strings similar to names in the namespace (ASCII or UTF-8, generally excluding NULL and C0). This allows us to apply the namespace TL type to translate and restrict the environment exposed to a subprogram. It also ensures names are easy to render in a projectional editor or debug view.

Toplevel application methods ('app.\*') receive access to a finite set of handlers and registers from the runtime. See [glas apps](GlasApps.md) for details. The program may include annotations describing the assumed or expected environment, allowing for validation and optimization.

### In-Place Update

Each register contains glas data - an immutable binary tree, albeit subject to accelerated representations, abstract data types, scoping, linearity, and content-addressed storage. However, we can express operations on registers in terms of mutations, such as incrementing a number register or appending a list register. 

Updates on a specific index (or slice of indices), e.g. of a dict or array, are amenable to an optimization where, instead of allocating a new copy with the change applied, we copy the existing representation if it isn't unique then update in place. This is essentially an optimization of the allocator and garbage-collector, but it can offer a significant performance boon in many cases.

I hope to design the program model to readly leverage in-place updates, tracking opportunities to do so both statically and dynamically. For example, we could keep a tag bit (perhaps in the pointer) to track whether a pointer is 'unique' or 'shared' as an efficient alternative to a full reference count. Or we could efficiently maintain a small reference count, and use full GC only for widely shared representations.

*Note:* An inherent limitation is that uniqueness doesn't play nicely with transactions. The easiest implementation is often to maintain a copy of prior values for easy reversion. This could be mitigated by recording an update log into transaction registers.

### Static, Structured Behavior

The intermediate language will express behavior in a tree-structured manner to simplify reasoning and optimizations. Coroutines, conditionals, loops, locals, etc.. Notably, glas will avoid mobile code, such as 'jumping' to a dynamic address or function pointer, or calling a first-class function.

That said, we can effectively represent jumps to static labels as tail-calls, and support higher-order programming in terms of algebraic effects handlers or intermediate language macros. We might view a program as operating on a static collection of 'stack objects' in scope. We'll aim for an expressive intermediate language within a few constraints.

Another relevant constraint is that program behavior must not rely on static analysis, such as type-driven overloading and dispatch. The best we can do is use annotations to insist certain registers are computed at compile-time, and dispatch on those.

### Expressions and Statements

At the moment, I lean towards a statement-based intermediate language. We can express 'return values' in terms of a program that writes a 'return' register. Parameters in terms of translating a subprogram's access to an operation environment of registers and handlers.

It is possible to support a mixed language of expressions and statements. However, doing so complicates things a little, e.g. requiring several primitives to regulate interactions between the two, requiring an operational 'evaluation order' semantics for expressions. It seems simpler to model expressions as a calling convention.

An intriguing alternative is to support FP-inspired lenses and prisms and editable views as 'virtual registers' that scatter-gather data with limited calculations. This might offer a viable basis for integrating expressions into a statement-oriented language. However, it's not a priority.

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

### Long-Running and Multi-Party Transactions? Defer.

A long-running transaction is executed across multiple coroutine steps. A multi-party transaction is executed across multiple coroutine threads.

It is feasible for a runtime to support abstract, linear, first-class 'transaction' that can be maintained and manipulated across multiple steps. We could support operations like `(%tn.new Reg)`, `(%tn.in Reg Op)`, and `(%tn.commit Reg)` to execute a transaction across multiple steps, perhaps adding `(%tn.split Reg Reg)` for multi-party transactions (with multiple commits). Alternatively, we could favor an effectful reflection API over primitives.

However, I'm not convinced of the cost-benefit tradeoffs. Complexity and performance overhead is non-trivial. I expect atomic sections and queues will prove adequate in practice. Later, if we discover application programmers reinventing transactions for convincing reasons, we can reconsider providing runtime support.

### Conditional Behavior and Hierarchical Transactions

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
* `(%macro.eval Localization ASTBuilder)` - after macro substitions, ASTBuilder should represent a pure, 0--1 arity program that returns a concrete AST representation (from namespace types) on the data stack. This AST is localized, replaces the eval constructor, and is then subjected to another round of macro substitutions.

This model permits [non-hygienic macros](https://en.wikipedia.org/wiki/Hygienic_macro), leaving problems of hygiene to front-end syntax. Aside from these intermediate-language macros, we could also support text-based macros in a front-end syntax.

### Extensible Intermediate Language

We can always extend the intermediate language by adding new primitives. But we could also support something like a scoped 'interpretation' of the existing primitives. It might be useful to support something like `(%lang Ver AST)` or `(%lang.ver AST)` declarations, at least for the toplevel application methods. This would provide more robust integration with the runtime in case of language drift, and would allow languages adjust more flexibly.

As a convention, front-end compilers could include language declarations for most definitions, and the runtime may require it for 'app.\*', raising an warning and assuming a version otherwise.

### Non-Deterministic Choice

I propose to represent access to non-deterministic choice as a primitive (instead of handler). 

There are a few reasons for this. First, it does us very little good to intercept non-deterministic choice within a program. Only at the level of a runtime or interpreter might we heuristically guide non-deterministic choice to a useful outcome. Second, we may still control non-deterministic choice via annotations, i.e. to indicate a subprogram is observably deterministic, or at least does not introduce non-determinism (though it may invoke non-deterministic handlers). Third, use of non-deterministic choice in assertions, fuzz testing, etc. make it awkward to present as a handler.

Not quite sure what I want to call this. Could use '%sel' or '%ndc' or something else.

### JIT Compilation

Although the glas program model should support an interpreter, it will be designed with AOT and JIT compilation in mind as the primary mode for evaluation. Every serious glas runtime will compile most code before or during execution, and may cache compiled code for convenient reuse across repeated executions of an application. 

Compilation, and especially optimizations, be heavily guided and aided by annotations. For example, we may annotate type information on variables, allowing for static or dynamic validation of assumptions, dynamic testing of "final" states, specialized representations for unboxed numbers, and so on.

An intriguing possibility is to separate much compilation and optimization logic from the runtime executable, moving it into the user configuration, i.e. such that optimizations become user-defined but separate from applications. An application could suggest additional checks or optimizations to apply via application settings. The runtime may need to initially interpret the configured JIT compiler to compile itself, but this should be cacheable.

### Unit Types

We'll allow annotations to express types within a program. Static analysis to verify consistency of type assumptions is left to external tooling. However, annotations aren't suitable for unit types on numbers, where we might want to print type information as part of printing a number.

Instead of directly encoding unit types within number values, it may prove convenient to bind unit types to *associated* registers. With some discipline - or sufficient support from the front-end syntax - we can arrange for these associated registers to be computed statically ahead of the runtime arguments or results. We can also test that the unit type variable is static.

The unit type variable may be associated with a 'constant value' type when annotating type information for the implicit environment or an operation, and subject to static or dynamic verification. When rendering numbers, we may peek at the unit type variable for information, which is something we cannot do with annotation of types alone.

### Memoization

In context of procedural programming, memoization involves recording a trace. This trace describes the computation performed (perhaps via hash), the data observed, and outputs written. To execute a memoized computation, we search for a matching trace then write the outputs directly. If no matching trace is found, we run the computation while recording the trace, then add it to the cache.

We can improve memoization by making the traces more widely applicable, abstracting irrelevant details. For example, we might observe that a register contains 42, but a trace might match so long as a register value is greater than zero.

However, even the simplest of traces can be useful if users are careful about where they apply memoization. We can memoize a subset of procedures that represent "pure" expressions or functions to support incremental compilation, monoidal indexing of structure, and similar use cases.

### Lazy Computation? Defer.

A proposed adaptation of explicit laziness to procedural programs:

        (%an (%an.lazy.thunk Options) Op)
        (%an (%an.lazy.force) (%do))
        (%an (%an.lazy.spark) (%do))

In case of '%an.lazy.thunk' our Options may need to include assumptions on output arity and which registers are written. We could infer these things, but that would be suitable only for implicit thunks, which may be a valid option. The '%an.lazy.force' and '%an.lazy.spark' operations could simply operate as 1--1 data stack operations on thunks.

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

### Data Manipulation

With registers alone, we end up naming input and output registers for every little operation. To avoid this, I propose to introduce a data stack and static arity analysis. The data stack serves as an intermediate location for dataflow and a scratch space for computation. With this, the only primitive register operation we might require is linear swap of data between register and stack. Alternatively, we could support a universal 'empty' state for registers.

We can include a basic set of stack manipulators: dip, swap, copy, drop. We can use 'dip' and 'swap' together for arbitrary stack shuffling. We can interpret `d:Data` as pushing static data onto the data stack, no wrappers required. All very familiar features.

Beyond that, perhaps the only primitive data manipulations we need are dict take and put. These may be linear, too, e.g. treating it as a type error if put would overwrite existing content. Anything else can be implemented via accelerators.

Registers may simply initialize with zero values. In case of external registers, we might prefer to model this in terms of optional values.

### Locals and Handlers or Stack Objects



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
