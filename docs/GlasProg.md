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

### Structured Behavior

The intermediate language will be higher level than assembly and still impose some structure on control flow. The closest thing to 'jump to label' may instead involve tail calls between handlers or definitions.

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

### Conditional Behavior and Failure

A typical expression of conditional behavior is `"if Cond then P1 else P2"` or similar. We'll also need conditions for loops, and perhaps for coroutines. 

A direct solution is to make conditions a primitive feature of the program model. However, doing so seems structurally inconsistent with the procedural model. I expect that I would find myself wanting ever more sophisticated expressions, e.g. to compare computed numbers.

An interesting alternative is backtracking failure. We can introduce an unconditional `%fail` primitive into the program together with primitive operations such as `(%eq Reg1 Reg2)` that 'fail' conditionally. We'll generally assume that failure occurs in context of a transaction that can be canceled or backtracked, simplifying cleanup and retry.

A conditional behavior might be expressed as `"try Cond then P1 else P"`, and equivalent to `(%seq (%atomic Cond) P1)` if Cond does not fail, P2 otherwise. In other contexts, failure may cause an operation to wait for relevant state changes or retry with different, uncommitted non-deterministic choices.

This seems a good fit for glas systems, aligning nicely with transaction loop applications and fine-grained transactions for coroutine steps. However, it is somewhat expensive. This can be mitigated by heavily optimizing read-only conditions, and migrating tests closer to the start of a transaction where feasible.

### Coroutines and Concurrency

Coroutines are procedures that yield and resume. This supports concurrent composition and modularization of tasks, a convenient alternative to call-return structure in some cases.

A highly desirable optimization is parallel evaluation, utilizing multiple processors to evaluate multiple coroutines. With tolerance for latency and disruption, this may extend to distributed evaluation with remote processors and partitioned application state. This optimization requires determining a valid 'schedule' of coroutines ahead of time, and either analysis to prevent conflicts or some ability to undo conflicts. 

To support the latter, we could wrap each step with an implicit transaction. A coroutine becomes a sequence of transactional steps. Upon 'yield', the coroutine commits what it has done thus far. If a read-write conflict is detected, or if a step 'fails', we can abort and retry as needed. Rework can be mitigated via analysis, heuristics, annotations, and incremental computing. This aligns very nicely with transaction loop applications.

For my vision of glas systems, I also favor anonymous, second-class, fork-join coroutines. For example, `(%c P1 P2 P3)` represents concurrent composition three coroutines, but `%c` not 'return' before all three coroutines complete. Ideally, composition of coroutines is at least associative, that is `(%c (%c P1 P2) P3) = (%c P1 (%c P2 P3)) = (%c P1 P2 P3)`. This constrains a scheduler, but is still flexible, e.g. we can support basic schedules like 'prioritize leftmost' and 'round robin' and 'non-deterministic choice'. It is feasible to also support numeric priorities, using structure as a fallback.

We can support `(%atomic P)` sections as hierarchical transactions with a hierarchical scheduler. If P yields, the runtime will attempt to resume P. If P contains coroutines, we can resume other coroutines within P. If no coroutine within P can continue, we'll halt with a type error.

To fully support parallelism and distribution, we'll want the runtime recognition of certain update patterns for precise conflict analysis. For example, a 'queue' could be modeled as a register containing a list, but a runtime that recognizes enqueue and dequeue operations could feasibly evaluate a single read transaction in parallel with multiple write transactions, and might even partition the register between reader and writer nodes. This recognition can be achieved via primitives or accelerators. Beyond queues, we could usefully support bags and CRDTs.

*Note:* A transactional coroutine step may 'fail' to implicitly await relevant changes. Without this, we might replace 'yield' with 'await Cond' to delay resumption until some arbitrary condition is met.

### Virtual Registers? Defer.

Idea: Primitive support for 'virtual registers' that logically index or aggregate other registers. Inspiration from lenses and prisms in FP. However, this concept complicates implementations.

Alternative: We can currently support virtual 'getters' and 'setters' as handlers. In contrast, these aren't be transparently usable in place of registers, and they don't provide as many opportunities for optimizations. 

For the moment, I've decided to eschew support for virtual registers. However, it seems to be an extension compatible with other primitive features, so we can return to the idea in the future. Perhaps registers can logically be a getter/setter pair?

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

We can always extend the intermediate language by adding new primitives. But we could also support something like a scoped 'interpretation' of the existing primitives. It might be useful to support something like `(%lang Ver AST)` declarations, at least for the toplevel application methods. This would provide more robust integration with the runtime in case of language drift, and would allow languages to drift very flexibly.

As a convention, front-end compilers could include language declarations for most definitions, and the runtime may require it for 'app.\*', raising an warning and assuming a version otherwise.

### Non-Deterministic Choice

I propose to represent access to non-deterministic choice as a primitive (instead of handler). 

There are a few reasons for this. First, it does us very little good to intercept non-deterministic choice within a program. Only at the level of a runtime or interpreter might we heuristically guide non-deterministic choice to a useful outcome. Second, we may still control non-deterministic choice via annotations, i.e. to indicate a subprogram is observably deterministic, or at least does not introduce non-determinism (though it may invoke non-deterministic handlers). Third, use of non-deterministic choice in assertions, fuzz testing, etc. make it awkward to present as a handler.

### Parameters and Results

Programs operate on registers and handlers. To model parameters and results, thus, is left to conventions such as writing a result of a calculation to 'result.\*' registers or 'result.!' for the main result. The client will translate this into context. Similarly, arguments could bind 'arg.\*' registers, and we could have some conventions for providing a static list of keywords or similar. 

In many cases, we might wish some means to express and enforce that a subset of registers is read-only within scope of a call. This should be feasible with annotations.

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

## Misc Thoughts

The initial environment model will be kept simple - a static, hierarchical structure of names. 

It seems convenient to model extension of this environment in terms of introducing 'stack objects' under a given name, conflating declaration of local registers, handlers, and hierarchical objects. We should be able to override methods or hierarchical components when declaring stack objects.

Our runtime may support use of linear or abstract data types, but we can design our APIs to mostly hide this, instead favoring 'abstract' volumes of registers held by the client and explicit 'move' operations.

Registers must have an initial state. Perhaps an explicit 'undefined' state, distinct from containing a zero value.

## Proposed Primitives


### Control Flow


## Basic Operations

Everything is an expression and has a value. For procedural operations, that value is often unit.

* `(%seq Op1 Op2 ...)` - 

## Design Thoughts

* For number types, I want unbounded integers, rationals, complex numbers, and vectors or matrices to be the default. But ideally the program model should make it easy to identify and isolate subprograms where we can use bounded number representations to optimize things. 
* Ideally, every program has a clear small-step rewrite semantics. This greatly simplifies debugging.

Embedded data is the only type that doesn't contain names, and is thus not rewritten based on scope. However, we should wrap most embedded data with a suitable node that can validate its type and represent intentions, e.g. favoring `(%i.const 42)` where an integer expression is expected. Some languages might restrict which data can be embedded.

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
