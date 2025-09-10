# Program Model for Glas

The [namespace](GlasNamespaces.md) supports modules and user-defined syntax. The front-end compiler will translate user facing syntax to a shared AST structure with primitive definitions ('%\*' by convention) forming an intermediate language. This document describes a suitable intermediate language for glas systems.

## Design Thoughts

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

As a simplistic structure, we could support something like `"try Cond then P1 else P2"`, equivalent to `"atomic Cond; P1"` if Cond passes, P2 otherwise. Then we introduce primitives such as `(%eq Reg1 Reg2)` that cause the current transaction to 'fail' if conditions aren't met. In the simplest case, Cond consists only of such read-only operations, thus can be evaluated without the overhead of hierarchical transactions.

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

We can support a similar structure in the abstract syntax. As a simplistic example, `(%try Tree X Y A Z B C)` can encode a decision tree structure and a tree traversal. But this encoding is awkward to extend or metaprogram. I imagine a solution closer to `(%cond (%br X (%br Y (%do A) (%br Z (%do B) (%bt))) (%do C)))`, essentially introducing a DSL for pattern matching into the AST. Here '%br' means branch, '%bt' means backtrack, and '%do' corresponds to the '->' arrow of committed action. But we can also support user-defined macros within the condition structure, and we can locally optimize `(%br Any (%bt) R) => R`, `(%br (%fail) Any R) => R`, and `(%cond (%do B)) => B`.

*Note:* I'm still concerned over the overhead of pervasive transactions and backtracking, but this seems to be a good design direction for glas.

### Loops

We don't absolutely need loop primitives, but it would be awkward and inefficient to encode all loops as recursive definitions. 

I propose a simple `%loop` primitive corresponding roughly to while-do loops, but in context of our unusual approach to conditional behavior. The  `"while Cond do Body"` might compile to `(%loop (%br Cond (%do Body) (%bt)))`. We aren't limited to just one '%do' body, and a righmost '%bt' is essential if we intend to eventually exit the loop.

*Aside:* I hope to eventually support termination proofs on glas programs. Unfortunately, neither recursion nor while-do loops do anything to simplify reasoning about termination. We'll be relying very heavily on annotations to guide such proofs.

### Robust Partial Evaluation

Instead of a structured approach, I propose annotations specify that registers or expressions should be statically determined in context. We can easily raise an error at compile time if the annotated expectations are not met. Partial evaluation thus becomes a verified optimization, ensuring it remains robust across code changes, without truly becoming part of the semantics.

Separately, we can develop front-end syntax or libraries to more robustly support partial evaluation as we compose code, reducing the risk of errors. 

### Intermediate Language Macros

The glas program model should support user-defined AST constructors, i.e. `(UserDef AST1 AST2 ...)`, serving a role similar to macros of the intermediate language. One viable solution is direct adaptation of macro substitution to AST nodes instead of text.

Potential model:

* `(%macro Template)` - describes a macro. In this case, Template is an AST that contains `(%macro.arg K)` and other elements for rewriting. When we apply a macro as a user-defined constructor, we'll substitute AST arguments into the template, then replace the constructor by the resulting AST, aka macro expansion.
* `(%macro.arg K)` - is substituted by the Kth AST argument, with static K starting at 1. Error if K is out of range. We can designate argument 0 to refer recursively to UserDef for convenient anonymous recursion. We'll report a compile-time error if K is out of range.
* `(%macro.args.count)` - is substituted by the count of AST arguments to the macro, intended to support a variable number of arguments
* `(%macro.args.range X Y)` - is substituted by a *series* of AST arguments `(%macro.arg X) (%macro.arg X+1) .. (%macro.arg Y)`. This expansion is valid only in context of another AST constructor. The series is empty if X is greater than Y. 
* `(%link Localization AST)` - deferred integration of a concrete AST representation with the namespace, such as `n:Name` binding to a defined symbol.
  * We can feasibly extend linking to hierarchical namespaces when describing stack objects.

To simplify both implementation and user comprehension, the compiler shall detect and reject use of macro primitives outside a local `(%macro Template)` in the same definition. This rule rejects free macro variables and aliasing of macro primitives. Similar analysis may apply to a linked AST, treating it as an anonymous inline definition.

Recursive macro expansion may require partial evaluation and dead-code elimination ultimately produce a finite AST. Otherwise, the compiler might expand macros until some quota is reached, warn the developer, then replace some infinite macro expansions with code to generate runtime errors. Annotations can guide a compiler in partial evaluation or recognition of dead code.

This model permits [non-hygienic macros](https://en.wikipedia.org/wiki/Hygienic_macro). Although non-hygienic macros can be convenient, they are a source of subtle bugs such as accidental name shadowing. It is left to front-end macro syntax to enforce hygiene or at least resist accidents, translating register and handler names over AST parameters as needed.

*Note:* Between user-defined syntax, intermediate language macros, and robust partial evaluation, glas offers ample opportunity for metaprogramming. For example, we can also support text macros in a front-end syntax, and it is feasible to translate text parameters into a local subprogram.

### Extensible Intermediate Language

We can always extend the intermediate language by adding new primitives. But we could also support something like a scoped 'interpretation' of the existing primitives. It might be useful to support something like `(%lang Ver AST)` or `(%lang.ver AST)` declarations, at least for the toplevel application methods. This would provide more robust integration with the runtime in case of language drift, and would allow languages adjust more flexibly.

As a convention, front-end compilers could include language declarations for most definitions, and the runtime may require it for 'app.\*', raising an warning and assuming a version otherwise.

### Non-Deterministic Choice

I propose to represent access to non-deterministic choice as a primitive (instead of handler). 

There are a few reasons for this. First, it does us very little good to intercept non-deterministic choice within a program. Only at the level of a runtime or interpreter might we heuristically guide non-deterministic choice to a useful outcome. Second, we may still control non-deterministic choice via annotations, i.e. to indicate a subprogram is observably deterministic, or at least does not introduce non-determinism (though it may invoke non-deterministic handlers). Third, use of non-deterministic choice in assertions, fuzz testing, etc. make it awkward to present as a handler.

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

### Content-Addressed Storage

Annotations can transparently guide use of content-addressed storage for large data. The actual transition to content-addressed storage may be transparently handled by a garbage collector. Access to representation details may be available through reflection APIs but should not be primitive or pervasive.

### Arithmetic

Although it's feasible to support arithmetic through accelerators, I propose to support simple arithmetic operations as primitives, albeit with arbitrary-sized integers, precise rationals, complex numbers, vectors, and matrices. 

I'm currently omitting built-in support for IEEE floating-point due to its awkward, non-deterministic nature across processors and compilers. This may hinder performance in some computations, but can be resolved through other accelerators.

## Misc Thoughts

The initial environment model will be kept simple - a static, hierarchical structure of names. 

It seems convenient to model extension of this environment in terms of introducing 'stack objects' under a given name, conflating declaration of local registers, handlers, and hierarchical objects. We should be able to override methods or hierarchical components when declaring stack objects.

Our runtime may support use of linear or abstract data types, but we can design our APIs to mostly hide this, instead favoring 'abstract' volumes of registers held by the client and explicit 'move' operations.

Registers must have an initial state. We could initialize registers to 0 by default (the empty binary tree). But it may be convenient to support an explicit 'undefined' state for registers, distinct from containing a value.

It may be useful to wrap constant data with some indicator of type to support analysis, acceleration, and sandboxing. However, it doesn't seem essential to do so.

## Proposed Primitives

* `(%seq P1 P2 P3 ...)` - execute P1 then P2 then P3 etc. in sequence. 
* `(%c P1 P2 P3 ...)` - execute P1 and P2 and P3 concurrently, implicitly switching as the different operations yield or fail.



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
