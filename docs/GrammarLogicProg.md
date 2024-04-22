# Procedural Programming with Grammars and Logic

Grammar and logic programs have similar semantics. 

The 'direction' of evaluation is flexible, i.e. the same program can flexibly *accept* or *generate* values. Interactive computation is based on [logic unification](https://en.wikipedia.org/wiki/Unification_(computer_science)#Application:_unification_in_logic_programming) of shared variables, where each component program is responsible for accepting or generating different parts of the value.

With logic programming we have `Proposition :- Derivation`. With grammar programming, the equivalent is possible via guarded patterns, i.e. `Pattern when Guard`. Similarly, logical negation might use `Pattern unless Guard`. Variables can be shared between proposition and derivation, or between pattern and guard.

A function can be modeled as a grammar or logic program that accepts and generates a set of `(args, result)` pairs, and ensures structurally or typefully that results are deterministic given args. In context of grammar and logic programming, we can evaluate functions non-deterministically backwards from results to args, or with partial args where any unspecified variable represents non-deterministic choice. 

A simplistic procedural interaction might be modeled as an `(args, io, result)` triple, where IO represents a request-response list of form `[(Request1, Response1), (Request2, Response2), ...]`. In this case, the procedure provides requests and results while the caller provides args and responses. To model processes, we could support ad-hoc channels within args and results, leveraging [substructural types](https://en.wikipedia.org/wiki/Substructural_type_system) to ensure each channel only has one writer. We can introduce temporal semantics to support logical time sharing of channels between writers.

The proposed language has a procedural programming style by default via implicit 'env' argument, but first-class channels and temporal semantics so we can model concurrent processes within a computation. Backtracking computation is pervasive, but not a bad fit for [transaction loop applications](GlasApps.md) because we can support hierarchical transactions. I assume programs are expressed in an [extensible namespace](GlasProgNamespaces.md) as the basis for mutual recursion and flexible tuning of grammars.

## Brainstorming

### Ordered Choice 

We can easily model ordered choice in terms of unordered choice and pattern guards.

        P or-else Q             =>      P or (Q unless P)
        if C then P else Q      =>      (P when C) or (Q unless C)

I propose to build the grammar-logic entirely in terms of ordered choice, without direct use of unordered choice. This supports deterministic functions as the standard program type. Non-deterministic choice can still be supported indirectly via effects or partial inputs.

### Factoring Conditionals

We can support factoring of conditions as:

        match Expr with
        | C1 and
            | C2 -> X
            | C3 -> Y
        | Z

This effectively means:

        if C1 and C2 then X else
        if C1 and C3 then Y else
        Z

However, it allows us to compute C1 only once without committing to it.

### Composition of Patterns

Ordered choice imposes an order - least to most specific - when defining patterns incrementally via override.

        foo = Specialization -> Outcome
            | prior foo

It might be feasible to support multi-method like overrides if we can identify which patterns are more 'specific'. This might require reifying the pattern match construct. Unfortunately, I don't have a good idea for this composition at present. 

### Non-Deterministic Choice

Non-deterministic choice can be expressed and controlled as an effect. Fair non-deterministic choice is useful for modeling concurrency in transaction loops. Biased choice is useful for modeling soft constraints and meta-stable reactive systems. To support biased choice, we might introduce scoring heuristics based on quality and stability. 

An intriguing possibility in context of grammar-logic programming is to model non-deterministic choice by returning a logic unification variable. This would make the choice implicitly adapt to the program. However, it would be difficult to implement fair choice from an infinite set.

### Channel Based Interactions

Channels can be modeled as partial lists. The writer holds a cursor at the 'tail' of the list. The reader holds a cursor to the 'head'. To write a channel, we unify the tail variable with a pair including the next tail `T <- (Data, T')` then update the cursor. To read a channel, we do the opposite. A written channel may be 'closed' by unifying with the empty list, but we could generalize to moving the channel to another writer. A reader can detect a closed channel.

We'll generally treat channels as linear types. But if we typefully constrain channels, or introduce some temporal semantics, they can be shared without interfering with deterministic computation. An inherent risk with channels is potential deadlock with multiple processes waiting on each other in a cycle.

#### Temporal Semantics? Tentative.

Temporal semantics can support deterministic merge of asynchronous events by converting to events synchronized on logical time. 

Channels can carry time-step messages. Sequential time-steps can be implicitly compressed by the runtime, i.e. time-step^N. When a process 'waits', it removes pending time-steps from input channels (allowing for a negative balance) and writes time-steps to output channels. Reading a channel will fail with 'try again later' if the next message is time-step.

This adds non-trivial complexity to our language, which must know which channels are 'owned' by a process when we 'wait'. This generally includes channels that are available on input channels within the current time-step. It isn't clear that the flexibility benefits are worth costs to locality.

#### Reflective Race Conditions? Tentative. As Effect.

A runtime can merge events non-deterministically based on arrival order. Arrival order would depend on the underlying implementation, thus this should be considered a form of reflection on the runtime. It can be supported as an effect.

Arrival-order non-determinism is among the worst flavors of non-determinism for reasoning because it's highly machine dependent. There is high risk of "works on my machine" bugs, i.e. bugs that cannot be reproduced on the test machine. Predictability can be mitigated insofar as we control where race conditions are observed, but temporal semantics might be a superior option.

#### Channel-Based Objects and Functions

An object can be modeled as a process that accepts a subchannel for each 'method call'. This subchannel would provide inputs for the request and route the response. If we know the process is stateless, we can optimize to evaluate requests in parallel.

Functions can be modeled similarly as a *stateless* object. If the runtime knows the object is stateless, it can optimize method calls to evaluate in parallel, interactions may be forgotten because they don't affect any external state, and the channel may be freely copied because there is no need to track order of writes. Effectively, the channel serves as a first-class function (albeit with restrictions on how it is passed around or observed).

Ideally, grammar methods and object methods via channels would have a consistent syntax and underlying semantics.

#### Pushback Operations

A reader process can push data or messages backwards into an input channel, to be read locally. This is a very convenient operation in some use cases, reducing the complexity for conditioning inputs.

### Pubsub Set Based Interactions? Defer. Tentative.

Channels operate sequentially on *partial lists*. An intriguing alternative is to operate collectively on *partial sets*. This makes writes commutative and idempotent, features we can easily leverage. 

Just as channels may transfer channels, values written into a partial set could contain a partial set for receiving the response. We might also track substructural types based on whether sets may include pass-by-refs or channels, i.e. to control copying. Readers could thus read a set of requests and write a set of responses back to the caller, forming an interaction. If the reader process leverages idempotence and commutativity, it is feasible to coalesce concurrent requests and responses, only forking computations where they observe different data. This would result in a highly declarative and reactive system, similar to publish-subscribe systems.

Pubsub has some challenges. If we read and write within the same time-step, it isn't clear how we'd know the reader is done reading and hence done writing. This could be mitigated by temporal semantics - read the past, write the future - but it's also unclear how to stabilize temporal semantics and avoid high-frequency update cycles.  

*Note:* The glas data type does not directly support sets. Indirectly, we could encode values into bitstrings and model a set as a radix tree, or we could model a set as an ordered list. To support pubsub, I assume the language would abstract and accelerate sets. 

### Procedural Applications

If we can guarantee that certain contexts never backtrack, we can support more flexible APIs such as synchronous request-response. The disadvantage is that we'll very often be dealing explicitly with error values, undo, etc.. But it'd be nice to have the option. We can consider supporting this as a special run-mode if we develop a type or proof system that supports the guarantee.

### Flexible Evaluation Order

Method calls in grammar-logic are opportunistic in terms of computation order, constrained only by dataflow. But we could support some performance and analysis hints representing programmer intentions, e.g. annotate subprograms as eager, parallel, or lazy.

Lazy evaluation is a good fit for grammar-logic and functional programming, at least for pure functions. Effectful functions would effectively be eager until they're finished writing requests. However, I'd prefer to avoid lazy evaluation semantics: no lazy fixpoints ["tying the knot"](https://wiki.haskell.org/Tying_the_Knot), no divergence semantics. In general, computation should be opportunistic, i.e. anything that can be computed is computed, and any specific tactic is an optimization.

### Method Calls

A conventional procedure call has at least three elements - argument, environment, and result. This might be concretely represented by a `((env:Env, arg:Arg), Result)` pair, consistent with modeling functions as pairs. Effectful channels could be provided through env. The Env, Arg, and Result may each contain an ad-hoc mix of data, channels, pass-by-refs, etc.. 

To better support a grammar-logic language, I propose to tune method calls a bit:

A grammar-logic method is typically applied in context of pattern matching. For example, we might parse an integer from a text, returning both the computed integer and the remaining input. I think we need to support this 'remaining input' in a general way that generalizes to input types other than lists, such as a pass-by-ref variable.Intriguingly, we could also use this to return a conditioned input or represent pushback. 

Auxilliary inputs could be used for lightweight abstraction or refinement of patterns. For example, we might express a ranged integer parse as `integer(min:-1, max:10)` method call as a pattern, with 'input' and 'env' both as tacit arguments. To support procedural programming, we might enable method calls to focus on these paramaters. One option is to treat pass-by-ref as an implicit argument in certain contexts. In general, we might simply treat 'input' as a standard parameter to method calls alongside 'env'. In any case, if a method doesn't use it, the pass-by-ref input would be returned immediately. 

We can model 'input' and 'env' as otherwise normal parameters supported with some syntactic sugar. 

#### Pass-by-Ref

In context of a grammar-logic language, a pass-by-ref mutable var might be modeled as an abstract pair `(Curr, Dest)` where our runtime will implicitly unify Curr and Dest after the final write to the var in scope. The program can update Curr as a functional state variable. Curr may have any type, including data, channels, and other pass-by-refs.

A pass-by-ref cannot be copied but it can be borrowed, i.e. we could further pass-by-ref the current value, replacing Curr with another free variable. The restriction on copying limits some patterns, but this is mitigated by the ability to return pass-by-refs from functions, or pass them through channels, to freely abstract patterns of writing into structures.

I would prefer to avoid depending on partial evaluation of Dest, at least implicitly. But an optimizing compiler could potentially determine the structure and some data in Dest before the final value is computed.

*Note:* Because method calls are concurrent, there is no specific issue with including refs in the return value. It is potentially very useful for refactoring method call arguments. But complicated use of pass-by-refs does increase risk of accidental deadlock. I'm interested in a lightweight static dataflow analysis to resist most issues.

#### Shared State?

It seems feasible to extend pass-by-refs with temporal semantics, such that when we 'wait' the ownership is temporarily forwarded, then eventually returned. This might be convenient in some cases. Of course, the another way to model shared state is to model channels to a shared object.

#### Environment, Effects, and Concurrency

The 'env' parameter is implicitly passed via linear copy to called methods, threading any bundled data, channels, or pass-by-refs in a procedural style. Users cannot directly manipulate env, but the language will include keyword syntax for scoped manipulation of env, e.g. `with (newEnv) do { ... }` might shadow env within scope of the subprogram, and a simple variation might run to end of the current scope.

Procedural style implicit effects can be supported through channels in the env. For example, to access the filesystem, we might use a channel-based object where the runtime handles requests. But env will often be abstracted to simplify extension. Access to an abstract env might be provided through a procedural interface.

Dataflow of env will implicitly constrain concurrent computation in most cases. Users might work around this limitation via `with () { ... }` to reduce the environment to a trivial unit value, or perhaps `with (forkEnv()) { ... }` to explicitly construct an environment for concurrent computations. But these concurrent computations must interact through channels instead of shared memory.

#### Local Variables

It is possible to model local vars as something like arguments or pass-by-refs at the scope of individual operations. Importantly, a loop should only capture pass-by-refs that it potentially modifies. Pass-by-refs should be returned ASAP if aren't furhther modified within a scope.

### Pattern Matching

Patterns are syntactic sugar over procedural fragments. We will support procedure fragments within the pattern to guarantee patterns have full flexibility of procedures. We can use method calls within patterns outside of a procedure fragment, in which case the 'input' parameter is implicitly threaded. Constants and lists have some special support, mostly operating on 'input' and returning themselves. 

In some cases an exact match is needed, in which case we'll insist that the 'remaining' input is unit. Our syntax can support both `parse &var with ...` and `match Expr with ...`, with the latter requiring an exact match but allowing any expression for initial input. A normal method call might be defined in context of an implicit `parse &input with ...`. And we can explicitly provide an 'input' argument in contexts where one is not implicitly provided.

Thoughts:

* Constant Patterns
  * Integer - match an integer (variable width bitstring) exactly, return matched integer. This includes all integer encodings such as `0xAB` and `0b101` and `42`.
  * Symbol - match a symbol exactly, return matched symbol.
  * Text - match start of text, return matched text, remaining text returned via input. Binaries as special encoding for text, maybe `x"0123456789ABCDEF"`
* List Patterns
  * `[A, B, C]` - match `(A, (B, (C, Rem)))` returning Rem as remaining input. A, B, C must be exact matches.
* Record or Variant Patterns
  * label:Pattern - match a specific label, removing it from input and returning remaining record. 
* Meta Patterns
  * P opt - optional match
  * P rep - match P multiple times
  * P until Q - match P repeatedly until Q is matched once.
  * P or Q - match P or Q

An important concern is how to return values from meta-patterns. A repeating pattern P has multiple variables the pattern P, but now we need a variable for the repetition. This could be supported by treating the variables as fields in a record of the metapattern. We could have special support for converting the array of structs to a structure of arrays if the meta-pattern variable isn't named. 

### Module Layer

        g:(app:gram:(...), MoreDefs)

A grammar-logic module will compile into a simple dictionary structure. The 'g' header will become useful when integrating multiple glas languages. A grammar-logic module that represents an application will define an 'app' grammar containing a 'main' method. 

I propose compiled definitions be represented by independent values. For example, in case of `grammar foo extends bar` we integrate the compiled value of bar within the compiled value of foo rather than 'foo' referring to 'bar' by name. This simplifies module-layer imports, exports, and local reasoning. But it limits overrides to lower and higher layers: methods in a grammar, or modules in a distribution.

        import Module                   # toplevel import
        from Module import ImportList   # explicit import
        import Module as LocalName      # qualified import
        Definitions
        export ImportList

To avoid ambiguity, I propose we limit modules to a single toplevel import followed by any number of explicit and qualified imports, followed by module definitions. Further, we forbid shadowing of anything that is previously defined or assumed. We might extend the only toplevel import with an optional ImportList to support aliasing and assertions. For example, `import my-module with bar, foo as prior-foo` would import everything from my-module but would assert 'bar' is defined and rename 'foo' to 'prior-foo'.

Definitions within a grammar-logic module would include grammars of various forms (including mixins or interfaces) and other program fragments that we might find reusable (such as macros or embedded DSLs, renames). However, anything requiring compile-time eval should be deferred until after bootstrap.

### Incremental Compilation

The module layer compiles grammars to an AST or intermediate language. A lot of processing is still needed: partial evaluation, verification of assertions, analysis of types, translation to machine code and accelerated representations, compression of similar definitions, and so on. Ideally, all of these steps are 'incremental' in context of persistent memoization tables, such that a change to a source file rarely requires rebuilding an entire application. But how do we design for incremental compilation?

Identifying 'cliques' - subsets of mutually recursive definitions - is likely an important part of incremental compilation. Additionally, we might need to rely on secure-hash content addressing instead of allocation of anonymous namespaces. 

### Procedural and Multi-Process Programming

I propose to develop a procedural sublanguage that compiles into a state machine for use within a transaction loop. This allows for more comfortable use of filesystem and network APIs, in particular for synchronous request-response interactions outside of a transaction.

With a few tweaks and conventions, this might extend to multi-process programs, using 'fork' to choose between subprocesses performing different subtasks or threads.

### Avoid Primitives?

It is feasible to model all language primitives as binding various names from the namespace. This requires support for higher-order program expression. It might be feasible to express this as a combinatory logic, without naming parameters or explicitly describing the environment.


