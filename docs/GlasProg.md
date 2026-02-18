# Program Model for Glas

This document describes the runtime program model for glas systems and several design patterns for how to use it more effectively.

## Primitive Constructors

Programs are modeled as an abstract data type. For example, `(%do P1 P2)` returns an abstract program that, when executed, runs two subprograms sequentially.

*Notation:* `(F X Y Z)` desugars to `c:(c:(c:(F,X),Y),Z)`, representing curried application in the [namespace AST](GlasNamespaces.md). Names become `n:"name"`. Capitalized symbols as variables.

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

Aside from '%eval', the glas system also supports metaprogramming via '%macro' or within the namespace layer, e.g. via Church-encoded lists of namespace terms. But only '%eval' supports runtime metaprogramming.

* `(%macro P) : Any` - 0--1 arity program P must return closed-term namespace AST.

Use of '%eval' is more convenient when we don't *locally* know whether an AST is static or not. Or when the dataflow to construct an AST doesn't align nicely with the lexical namespace scope.

## Design Patterns

The primitive program model is very constraining. We can leverage the namespace layer to greatly enhance expressiveness and improve the user experience.

### Parameter Objects

Passing arguments via the data stack is fragile, adequate only for low-level code. An alternative is to parameterize a subprogram through the namespace, e.g. `Env -> Program`. In this case, `Env` provides ad hoc access to named registers and callbacks. However, a namespace environment is still rigid. 

To further enhance flexibility, we can provide arguments as a mixin, e.g. the basic "obj"-tagged namespace object. This allows parameters to be expressed in terms of overriding default parameters, and it enables refactoring of parameters into a composition of mixins.

If users insist on positional parameters, the parameter object may define 'args' as a "list"-tagged Church-encoded list. If we want to provide the caller's environment, consider an "env"-tagged `Env` named 'env'. 

### Expressions

Many languages introduce a concept of expressions and evaluation thereof, returning a single value. Expressions are convenient for structured composition. In context of glas programs, we can easily model expressions as 0--1 programs. Arity can be enforced by type annotations. 

### Calling Conventions

The front-end compiler will provide adapters for integrating functions with various tags into a program. Consider an initial set of tags:

* "data" - embedded data
* "prog" - abstract program
* "call" - `Object -> Def`, tagged parameter object to tagged definition
  * e.g. "obj"-tagged parameter object to "prog"-tagged abstract program

In this case, we adapt "data" by wrapping with '%data', and we adapt "prog" by direct embedding. If non-trivial arguments are ignored, we can report an error or warning. In case of "call", we build our parameter object, apply, then integrate the resulting definition. 

Namespace terms are naturally call-by-name, but that can be awkward to work with. Fortunately, a front-end compiler can easily implement call-by-value or call-by-need via local registers. I suggest call-by-value as the front-end default, but with lightweight syntax to indicate call-by-name or call-by-need on the level of expressions or subexpressions.

## Reflection

When we construct a program `(%do P1 P2)`, there is no primitive mechanism to separate this back into its constituent elements. However, a runtime may provide ad hoc reflection APIs, e.g. passing 'sys.refl.\*' to an application. The view of a definition through this lens may be runtime-specific, but a viable mechanism is to return a namespace AST that can reconstruct a definition given program primitive constructors.

## Annotations

Annotations are structured comments supported in the namespace AST:

        a:(Annotation, Operation)

Annotations should not affect formal behavior of valid programs. That is, it should be 'safe' in terms of semantics to ignore annotations. However, annotations significantly influence optimization, verification, instrumentation, and other 'non-functional' features. In practice, performance is part of correctness. Annotations aren't optional. To resist *silent* degradation of performance (and other properties), glas systems shall report warnings for unrecognized annotations.

By convention, abstract annotation constructors are provided alongside program constructors, favoring names in '%an.\*'. This reduces need to 'interpret' arbitrary namespace terms as annotations. It also simplifies reporting for unrecognized or invalid annotations.

Annotations aren't limited to programs. However, within this document, I'm focused on programs and program-adjacent structures (e.g. `Register->Program`). 

### Acceleration

        a:((%an.accel %accel.list.concat), SlowListConcat)
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

We can safely delay some computations, and potentially discard them if their result is never needed. I propose to initially restrict this to 'pure', terminating 1--1 subprograms, returning an abstract thunk (capturing program and argument) in place of the true result. This thunk must be explicitly 'forced' to observe the data. It may also be 'sparked', asking a runtime worker thread to evaluate the thunk in the background. The result, once evaluated, is cached by the thunk.

Termination is not checked. But if computation of a thunk diverges in a later transaction, laziness annotations have influenced observable program behavior. A divergent thunk is considered an invalid program. However, there is a special case: a non-deterministic thunk can backtrack from divergence and seek another solution (just as the thunk creator would have done).

An intriguing opportunity is to defer choice for non-deterministic thunks, accepting a choice only when the observer commits. Then lazy thunks model entangled constraint systems that 'collapse' when observed (forced). To support this, we might introduce '%an.lazy.opt' to affect all lazy thunks created by a subprogram.

*Note:* Interaction with linear types needs careful attention. The simplest option is to raise an error if a thunk returns linear data when forced. Or we could introduce '%an.lazy.thunk.linear'. 

*Note:* In context of code updates, unevaluated thunks still refer to old code from moment of creation.

### Partial Evaluation

        a:(%an.data.static, %pass)      # insist top stack data element is static
        a:(%an.static, Program)         # Program must evaluate at compile-time
        a:(%an.eval.static, Program)    # all '%eval' in Program have static AST 

Implicit partial evaluation is a fragile optimization, but that can largely be resolved by simply making intentions explicit. In case of '%an.static' we aren't forcing partial evaluation of Program, instead we're providing an optimization hint and asking the compiler to raise an error or warning when things depend on runtime inputs.

This is still relatively fragile insofar as we're depending on runtime-specific implementation details in the optimizer, e.g. whether it uses abstract interpretation for partially-constant data. Porting code to another runtime may result in a deluge of errors. But at least we'll have a clear indicator!

### Tail-Call Optimization

        a:(%an.tco, Program)

This asks the compiler to ensure Program can execute in bounded call stack and data stack space. In practice, this is generally applied to a recursive or mutually-recursive call. If the optimizing compiler cannot figure out how to make this happen, it simply reports an error.

### Logging

        a:((%an.log Chan MsgSel), Program)
        type Chan = embedded String
        type MsgSel = Object -> Sel 

This annotation expresses logging *over* a Program. If Program is '%pass', we can simply print once. But if Program is a long-running loop that affects observed registers, we can feasibly 'animate' the log, maintaining the message. If the program has errors, we can attach messages to a stack trace. These are useful integrations.

The Chan is a string that would be valid as a name. The role of Chan is to disable some operations and precisely configure the rest. We can use `(%an.chan.scope TL)` to rewrite or disable channels within a subprogram.

MsgSel receives an "obj"-tagged parameter object. This includes configuration options and some reflection APIs, e.g. to support stack traces. The returned selector (cf. '%cond' and '%loop') should model a 0--1 program, returning at most one message on the data stack. By convention, this is an ad hoc dict of form `(text:"oh no, not again!", code:42, ...)`. In case of '%opt', the runtime may evaluate both choices to generate a set of messages. MsgSel isn't necessarily 'pure', but any effects are aborted after the message is generated.

Compile-time logging can be expressed via logging from a '%macro'.

*Note:* A stream of log outputs can be augmented with metadata to maintain an animated 'tree' of messages, where the tree includes branching on non-deterministic choice. Users could view the stream or the tree.

### Assertions

        a:((%an.assert Chan MsgSel), Program)

Assertions are modeled as a variation of logging where producing a message also implies an assertion failure. That is, MsgSel is returning an error message. If there is no error message, there is no assertion failure. Use '%macro' for static assertions.

### Profiling

        a:((%an.profile Chan BucketSel), Program)

Profiling asks the runtime to record performance metadata such as:

* number of entries and exits (also errors, aborts)
* time spent, CPU spent, backtracking rework
* memory allocated, memo-cache sizes, etc.

The intention is to collect performance data without slowing things down. The BucketSel allows us to organize this data a bit, i.e. Chan and bucket determine what data is aggregated. If no bucket is selected, we might still record the data under 'no bucket'.

### Source Mapping

        a:((%an.src Src Range), Program)

A front-end compiler can inject source location hints into the namespace AST to support debugging. In this case, Src should be the abstract file source location, while Range is glas data that indicates where to look within a file, perhaps a dict of form `(line:Int, col:Int, len:Int)`. Range may be observed by users or tools, thus align to conventions.

### Data Abstraction

        # seal or unseal data at top of stack
        a:((%an.data.seal Key), %pass)
        a:((%an.data.unseal Key), %pass)

        (%key.reg Register) : Key   # use register as a Key
        (%key.linear Key) : Key     # sealed data is linear

This is an awkward approach to abstract data types in terms of sealing and unsealing data. Runtime enforcement may wrap data to seal it, unwrap on unseal. It can be enforced at compile-time in some cases, though it's difficult if using heap Refs (from 'sys.ref.\*') as first-class Keys. With '%key.linear', we can raise an error upon attempt to copy or drop the sealed data.

This approach is awkward because data abstraction and linearity are much better understood as properties of subprograms rather than instrinsic properties of the data. This can result in weird alignment issues. OTOH, this approach is easy to express locally, enforce dynamically, implement without whole-system analysis.

*Note:* Contemplating a namespace-layer variation. Could feasibly use Src as key.

### Types

        a:((%an.type TypeDesc), Term)
        (%type.arity 1 2) : TypeDesc

Type annotations in the AST are useful for guiding a type checker. Type descriptors may be partial, in the sense of leaving detail unspecified that may or may not be filled by inference or another annotation. In my vision for glas systems, type checking is something to develop gradually and opportunistically, alongside proof-carrying code.

At the very least, we can get started with describing program data-stack arity, roughly the count of items popped then pushed to the data stack.

### Partial Code

        a:(%an.tbd, Term)

If a program is expected to be full of holes (e.g. undefined names) and other errors, consider a '%an.tbd' annotation to let the compiler know that you know that work is ongoing. The compiler may heuristically be more friendly about reducing related errors to warnings and running with errors.

### CLEANUP NEEDED

Validation:
* `(%an.arity In Out)` - express expected data stack arity for Op. In and Out must be non-negative integers. Serves as an extremely simplistic type description. 
* `(%an.type TypeDesc)` - Describes the expected partial type of Operation. Ideally, this is verified by a typechecker. We'll develop '%type.\*' constructors to work with this.
* `%an.det` - Indicates a subprogram should be observably deterministic. This isn't the same as rejecting non-deterministic choice; rather, such choices should lead to the same outcome. But without further proof hints, this effectively reduces to rejecting %opt.
* `%an.reg.id` - When applied to a register, limits further use of that register to a source of identity. That is, we can still use the register as a Key for sealing data, or in '%assoc', but cannot perform the '%rw' via this register.

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

## Challenges TBD

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
