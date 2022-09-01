# Glas Design

Glas was named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. As a backronym, it can be taken as 'General LAnguage System'. 

Design goals for Glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, live coding. Compared to conventional languages, there is a lot more focus on the compile-time. 

## Modules and Syntax

Glas modules are usually represented by files or folders, and compile to Glas values. Modules may depend on the compiled values of other modules, but dependencies must form a directed acyclic graph.

The syntax of a file is identified by file extension. For example, to compile the file "foo.ext" we search for a module language-ext to provide the compile function then evaluate it with file binary as input. Extensions compose, e.g. "foo.x.y" will apply language-x then language-y (in this case, input to language-y may be structured data). The [g0 language](GlasZero.md) is built-in to bootstrap.

A folder is compiled based on the contained 'public' file. Within the folder, files may reference modules defined by local files and subfolders or global modules. It is not possible to reference across folder boundaries. Folders do not use file extensions.

The GLAS_PATH environment describes a list of filesystem directories. This represents the search space for global modules. Each global module is represented by the subfolder found by sequentially searching that list.

## Command Line

The Glas system starts with a command line tool 'glas'. This tool knows how to compile modules and how to bootstrap g0. After compiling a module, the command line tool can extract binary data or directly interpret simple applications, depending on the command line arguments.

Extraction of binary data is via `glas --extract modulename.label`, outputting binary data to standard output. This feature is primarily used for bootstrap of the glas command line tool, avoiding need for effects.

To support user-extensible tooling, there is a simple syntactic sugar where `glas opname arg1 arg2 arg3` rewrites to `glas --run glas-cli-opname.main -- arg1 arg2 arg3`. Thus, adding new features to the glas command line mostly involves adding some glas-cli-* modules to GLAS_PATH. Users can define operations for pretty-printing, inferring types, linting, etc..

See [Glas CLI](GlasCLI.md) for details.

## Values

Glas values build upon immutable binary trees. The naive representation is:

        type T = ((1+T) * (1+T))

Records are encoded as [radix trees](https://en.wikipedia.org/wiki/Radix_tree) (aka tries). For example, the label 'data' may be encoded as a path `01100100 01100001 01110100 01100001 00000000` favoring null-terminated UTF-8 text. Here '0' and '1' refer respectively to left or right branches through the tree. Multiple labels can be encoded.

Tagged unions (aka variants) are encoded as records with a single label. The unit value is encoded by the tree with no children. A symbol is a variant with the unit value. Numbers can be encoded as into bitstrings, e.g. `10111` for natural number 23, or `00010111` for byte 23. Lists are encoded by a sequence of pairs, terminating in unit - e.g. `type List a = (a * List a) | ()`.

The runtime representation of values can be optimized to support common use-cases. For example, to efficiently represent radix trees and bitstrings, we might favor a representation closer to:

        type Bits = compact Bool list
        type T = (Bits * (1 + (T*T)))

This encoding happens to be isomorphic to the naive T earlier, but isomorphism isn't required. In the general case, we may use specialized representations for large lists, binary data, etc.. This is discussed further in context of *Acceleration*.

To support larger-than-memory data, and structure sharing in context of network serialization, Glas systems will also use content-addressed references (i.e. secure hashes) to refer to binaries that contain parts of a value. This pattern is called *Stowage*.

## Programs

Glas programs are represented by values with a standard interpretation, designed for simplicity and compositionality. Linking is static. Evaluation is eager and sequential. There are no variables to complicate metaprogramming. Annotations within the program can support performance, analysis, and debugging.

A consequence of this design is that all higher-order programming must be staged via macros or templates. Additionally, if we aren't careful to maintain structure sharing, program size can literally become a huge problem.

Glas programs use backtracking for conditional behavior. Effects are also backtracked. This design is convenient for pattern matching or parsers, and is a great fit for the transaction-machine based *Process* model. But it does constrain the effects API to whatever can be supported transactionally, such as reading and writing local buffers. 

### Stack Operators

Glas programs manipulate a data stack. 

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
 * move top item from data stack to top of dip stack
 * run P
 * move top item from dip stack to top of data stack
* **swap** - switch the top two items on stack
* **data:V** - add copy of V to top of stack

My type notation is a little ad-hoc, but might help clarify.

        copy        ∀S,V . (S * V) → ((S * V) * V)
        drop        ∀S,V . (S * V) → S
        dip:P       
            P ⊲ (S → S')
            -------------------------- 
            dip:P ⊲ ∀V. (S * V) → (S' * V)
        swap        ∀S,A,B . ((S * A) * B) → ((S * B) * A)
        data:V      
            V ⊲ A
            ------------------------- 
            data:V ⊲ ∀S . S → (S * A)

The stack in Glas is really an intermediate data plumbing model. User syntax could hide stack shuffling behind local variables. The Glas compiler can replace the stack with static memory and register allocations, leveraging static arity of valid Glas programs. The main reason for the stack is that it simplifies operators, which don't need to specify input sources or output targets.

### Control Operators

Glas programs must have static stack arity, i.e. the 'try-then' arity must match the 'else' arity, or a loop 'while-then' must have the same input and output arity. This is designed to be easy to verify.

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * *nop* - do nothing, identity function - can be represented explicitly by empty seq.  
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
 * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat loop. Otherwise, exit loop. Variants:
 * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat loop. Otherwise, exit loop.
 * 'do' field is optional, defaults to nop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail
* **tbd:Message** - always diverge (like infinite loop). Message is for humans. Connotation is that the program is incomplete. 
 * *tbd:unreachable* - says that a given operation should not be reachable. This might be useful in context of dependent types inference, or static reachability analysis.

        seq:[]              ∀S . S → S
        seq:(Op :: Ops)     (as SEQ)
            Op ⊲ S → S'
            seq:Ops ⊲ S' → S''
            -------------------
            SEQ ⊲ S → S''
        cond:(try:P, then:Q, else:R)    (as COND)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S''
            R ⊲ S → S''
            -------------------
            COND ⊲ S → S''
        loop:(while:P, do:Q)    (as LOOP-WHILE)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S
            -------------------
            LOOP-WHILE ⊲ S → S
        loop:(until:P, do:Q)    (as LOOP-UNTIL)
            P ⊲ S → S' | FAIL
            Q ⊲ S → S
            ------------------
            LOOP-UNTIL ⊲ S → S'
        eq : ∀S,A,B . ((S * A) * B) → S | FAIL
        fail : ∀S . S → FAIL
        tbd:_ : ∀S,S' . S → S'

User syntax can extend the effective set of control operators, e.g. compiling a mutually recursive function group into a central loop. Glas does not provide an 'eval' operator, but an 'eval' function may potentially be accelerated.

### Data Operators

Glas programs support a few operators to support ad-hoc analysis and synthesis of data. These operators are directly applicable to record manipulation, but can be leveraged for ad-hoc analysis and synthesis of data. 

Operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not in record.
* **put** (V (label?_|R) label -- (label:V|R)) - given a label, record, and value on the data stack, create new record with the given label associated with the given value. Will replace existing label in record.
* **del** ((label?_|R) label -- R) - remove label from record. Equivalent to adding label then removing it except for any prefix shared with other labels in the record.

Labels don't need to be textual, but they must be valid bitstrings. Use of a non-label input as the top stack argument to get, put, or del will diverge instead of failing. For static analysis, it's preferable if the labels are statically known values in most contexts.

Glas programs do not have any math operators. Those must be awkwardly constructed using get/put/del and loops.

### Effects and Environments

Glas effects are a based on a simple exchange of data with the host environment. An effects API can be described in terms of which types of values are accepted as requests and the external behavior and returned response for each type.

Operators:

* **eff** - interact with the environment by exchange of data, 1--1 arity, `Request -- Response`. 
* **env:(do:P, with:Handler)** - simulate environment for subprogram P. Captures top value from data stack as environment State, runs P threading State through each 'eff' handler, then returns final State to the data stack. The Handler should be a Glas program with 2--2 arity, `Request State -- Response State`. 

Dependent types or session types may be required to precisely describe an effects API. A top-level API must be documented for use by applications, perhaps specialized for context. 

### Annotations

Annotations in Glas programs support tooling without affecting formal program behavior.

* **prog:(do:P, ...)** - runs program P. If 'do' is elided, defaults to nop. All fields other than 'do' are annotations and must not affect behavior of program.

Annotations can affect performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), debugging (tracing, profiling, assertions, breakpoints), decompilation (variable names, comments, etc.), and essentially anything other than program meaning or behavior. 

Some annotations in use:

* *accel:Model* - accelerate the program. The Model is usually a symbol indicating a built-in function that should replace the direct implementation. 
* *arity:(i:Nat, o:Nat)* - describe stack arity of a program. This can be checked by a compiler, or help stabilize partial evaluation.

I haven't developed good annotations for stowage or memoization yet. I expect some parameters will be desirable to configure memoization or tune stowage heuristics.

## Processes

Command-line runnable programs will initially represent processes as a 1--1 arity program that evaluates a single transactional step: 

        type Process = (init:Args | step:State) -> [Effects] (step:State | halt:Results) | FAIL

The process step function is repeatedly evaluated by an external program loop until 'halt' is returned. Step failure does not halt the program, but implicitly retries, waiting for changes. With several optimizations and suitable effects, this process model can also express concurrent or distributed computations. See [Glas applications](GlasApps.md) for details.

## Language Modules

Language modules have a module name of form `language-(ext)`, binding to files with extension `.(ext)`. For example, module `language-xyzzy` would be used to process file `foo.xyzzy`. The language module must compile to a record value of form `(compile:Program, ...)`. Other than 'compile', language modules might define functions for linting, code completion, repl, decompiler, documentation, interactive tutorials, etc..

The compile program must be 1--1 arity. Input is usually a file binary (excepting files with multiple extensions), and output is the compiled value. Compile-time effects are limited to loading modules and logging messages:

* **load:ModuleRef** - Response is compiled value for the indicated module, or the request may fail (e.g. if the module is not found, or could not be compiled). Cause of failure is not reported to the caller, but may implicitly be logged. Currently, two types of ModuleRef are supported:
 * *global:String* - reference a module in the global search space, e.g. based on GLAS_PATH environment variable. Global modules are always represented by folders.
 * *local:String* - reference a module in the local search space, within the same folder as the file currently being compiled.
* **log:Message** - Response is unit. The Message should be a dict, e.g. `(text:"Uh oh, you messed up!", lv:warn)`, with fields subject to ad-hoc extension. Depending on runtime, if a log message is backtracked, it may still be visible via reflection APIs or a debug view.

To support bootstrap, a compiler for the Forth-like [language-g0](GlasZero.md) is built into the Glas command line interface. However, the language-g0 module should still be defined.

### Useful Languages

First, developing a few data-entry languages early on could be very convenient. For example, we could support `.txt` files that verify unicode input, remove the byte order mark, and convert to UTF-8 if needed. We could support JSON, MsgPack, Cap'n Proto, or SQLite files for structured data entry.

For programming languages, we could develop something more Lisp-like, and also develop a language that conveniently compiles into multi-step procedures, processes, and applications (see below).

## Automated Testing

Static assertions when compiling modules are useful for automated testing. However, build-time is deterministic and under pressure to resolve swiftly. This leaves an open niche for long-running or non-deterministic tests, such as overnight fuzz-testing. Use of a non-deterministic 'fork' effect would be useful for testing:

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' bitstring. In context of testing, the choice doesn't need to be fair or random. It can be guided by heuristics, memory, and program analysis to search for failing test cases.

A test might be represented as a 0--0 arity program that is pass/fail. In addition to fork, a 'log' effect would be useful for generating messages to support debugging.

## Type Checking

Glas systems will at least check for stack arity ahead of time. A more precise static type analysis is optional, but can be supported via annotations. Memoization is required to mitigate rework. In some cases, types might be checked at runtime, as we do with label inputs to get/put/del - in that case, `tbd:type-error` is an effective way to handle a runtime type error. 

## Performance

### Stowage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs can use annotations to guide use of stowage. It is also feasible to extend the module system with access to some content-addressed data. And a garbage collector could heuristically use stowage to recover volatile memory resources. Use of stowage is not directly observable within a Glas program modulo reflection effects.

The [Glas Object](GlasObject.md) (aka 'glob') encoding is intended to provide a primary representation for stowage. However, stowage doesn't strongly imply use of Glas Object.

### Memoization

Purely functional subprograms in Glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds and integrate more conveniently with stowage.

Glas systems assume memoization as a solution to many performance issues that would otherwise require explicit state. Without memoization, the potential scale of Glas systems would be severely constrained.

### Acceleration

Acceleration is an optimization pattern. The idea to annotate specific subprograms for accelerated evaluation, then a compiler or interpreter should recognize the annotation then silently substitute a specialized implementation. Accelerated functions are often coupled with specialized data representations. For example, a Glas runtime may represent lists using finger trees.

Essentially, accelerators extend performance primitives without affecting formal semantics. However, performance is an important part of correctness for many programs. To stabilize performance, a compiler must accelerate only where explicitly annotated, and must report where requested acceleration cannot be achieved.

The cost of acceleration is implementation complexity and risk to correctness, security, and portability. The complexity tradeoff is most worthy where acceleration enables use of Glas in new domains. Further, risk is mitigated by the reference implementation, which can be leveraged to support automatic testing. 

*Aside:* To reduce cycle time while experimenting, we may allow acceleration to behave as a compiler built-in, e.g. `prog:(do:tbd, accel:list-append)`. The proper reference implementation can wait for behavior to stabilize.

### Parallelism

Glas program model is not designed for parallelism.

A compiler can squeeze out some parallelism via dataflow analysis. For example, assuming F and G are both 1--1 arity and one of F or G is pure (no effects), we can evaluate `seq:[F, dip:G]` in parallel. However, this technique has limited applicability and scalability.

For robust parallelism at scale, it is feasible to *accelerate* evaluation of a distributed, confluent, monotonic virtual machine. [Kahn process network](https://en.wikipedia.org/wiki/Kahn_process_networks) and [Lafont Interaction Networks](https://en.wikipedia.org/wiki/Interaction_nets) can provide some inspiration.

## Thoughts

### Abstract and Linear Data

Abstraction is a property of a subprogram, not of data. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. Linear types extend these contextual restrictions to copy and drop operations. A static analysis of a program could track whether each parameter is abstract or linear.

Linear types can potentially support in-place updates, reducing garbage collection. 

But I think this is better to explore after the Glas system is more mature. Binary trees provide a simple foundation, while acceleration and, later, full data abstraction for applications provides an effective means to escape the limits of this foundation.

### Object Oriented Programming

Glas program model isn't designed to support OOP, and I don't intend to encourage OOP. However, as a thought experiment:

One option is to model objects as communicating Glas processes. In each step, a process can send and receive some messages. This design keeps code separate from data in the Glas program layer, and simplifies live coding. However, all method calls become asynchronous, similar to actors model. It is feasible to reify each method call as a new session or subchannel between objects.

An alternative is to model and accelerate virtual machines that host a collection of objects. The virtual machine state would use an optimized internal representation associated with JIT-compiled code. If carefully designed, we could also support migration of volumes of objects, i.e. composing and decomposing the collection based on regions.

Either of these options have some advantages. Modeling objects as processes would fit distributed and concurrent programming styles. Accelerating a virtual machine could be widely useful in many other cases, and would make scope obvious. 

### Static Routing of Effects? Reject.

Currently 'eff' always runs the same handler for an effect. It is feasible to modify 'eff' to support static routing, i.e. use `eff:Operator` and an environment of type `Operator -> Handler`.

This solution hinders dynamic 'eval'. We must statically identify the runtime effects potentially used by a subprogram. Another issue is that it's tempting to treat 'eff' as a macro namespace, with Operator containing Program values that are integrated into the Handler - it easily becomes awkward to manage namespaces correctly.

The current form of eff/env fits my goals of maintaining simplicity and comprehensibility, albeit at the cost of shifting more optimization work to depend on abstract interpretation.

#### Bracketed Effects? Reject.

Bracketing currently requires explicit commands to modify state around an operation, i.e. `seq:[StartEff, Operation, StopEff]`. This is error prone, though it can be mitigated by language modules that abstract over the pattern. It's tempting to build this pattern into the Glas program model. But with retrospect, I don't want to extend Glas program model only to support patterns. That's the responsibility of the language layer.

### Database Modules

It is feasible to design language modules that parse MySQL database files, or other binary database formats (LMDB, MessagePack, Glas Object, etc.). Doing so can simplify tooling that supports interactive or graphical programming styles. 

A relevant concern is that database files will tend to be much larger than text files, and will receive more edits by concentrating program representation into fewer files. This makes fine-grained memoization more important.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for a program. Type safety can be considered a hard constraint to guide program decisions. It is possible to resolve ambiguity given a context of types and other constraints on meaning. For unresolved meaning, the search can seek a high-quality solution according to some heuristics.

Unfortunately, search is expensive. It is necessary to reduce rework. 

Stateful solutions are feasible, e.g. we could develop tools that record search state into the module system. This might be useful for interactive program search, especially. However, I'd prefer to statelessly reduce rework via memoization. 

Memoization can mitigate rework in cases where we can make good local - i.e. compositional or modular - predictions of utility. For example, we can use memoization to eliminate programs that aren't internally type-safe. Or if we have [consistent heuristics](https://en.wikipedia.org/wiki/Consistent_heuristic) for utility, we could memoize partial searches.

I think that support for program search will mostly reduce to careful design of the search language to support memoization, especially in context of interactive revision and refinement of searches.

### Provenance Tracking

Manually tracing data to its sources is challenging and error-prone. Glas further hinders manual tracking because language module compiler functions cannot refer to file or module names. This ensures code can be moved or shared without changing its meaning. Only content-addressed provenance is possible, e.g. based on secure hash of binary, or notation within the code.

I hope to support provenance tracking in a more systematic manner, such that all data is traced to its sources without any explicit effort by the programmers. This might involve something similar to [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) heuristics, e.g. assuming that widely used/tested dependencies (such as language parser code) contributes 'less' as a source.

### Extended Tacit Environment? Reject.

Instead of a single data stack, I could extend Glas programs to work with an auxilliary stack for temporary data storage. The 'dip' behavior already does this to some degree, but doesn't make it easy to access data deep within a loop. A second data stack would complicate arity descriptions but could simplify some data plumbing. However, I think this isn't very flexible compared to trying to improve data plumbing purely at the syntax layer.

### Abstract Operators and Namespaces? Defer.

I'm tempted to extend Glas programs with abstract operators that can be defined in context. Something like:

* **op:OpRef** - call an abstract operator specified by OpRef. OpRef could be symbolic, but could also be structured to model a macro or DSL.
* **ns:(do:P, with:Defs)** - evaluate P, but replace each 'op:OpRef' by its definition (if defined). 

Unfortunately, composition of 'ns' subprograms is awkward: either we replicate common definitions or require a lot of extra computation to tease them out. Unless 'ns' supports higher order staged programming, we'll still be using macro abstractions at the language layer. It's tempting to unify 'ns' with env/eff, but more difficult to consistently model system state.

Without this feature, we still benefit from structure sharing and content-addressed storage for common subprograms. Memoization can reduce rework when processing common subprograms. A compression pass can later produce an intermediate representation with a namespace for common subprograms. Language modules can provide their own namespace logic. 

Compositionality is a primary design goal for Glas. I will eschew namespaces at the Glas program layer until I have a solution that doesn't interfere with compositionality.
