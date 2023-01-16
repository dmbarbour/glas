# Glas Design

As a backronym, 'General LAnguage System'. Glas was named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. 

Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. However, there is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool knows how to compile modules, bootstrap language-g0, extract binary data, and run simple applications. The 'language' of command line arguments is extensible by defining 'glas-cli-(opname)' modules and use of application macros. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax

Modules are global or local. Global modules are represented by folders, and are found based on configuration of the glas command line. Local modules are represented by files or subfolders in the same folder as a file currently being compiled or (if no such file) the current directory. Every valid module compiles to an arbitrary glas value.

A folder compiles the contained 'public' file. This file may have any extension, such as "public.g0" or "public.json". Glas does not provide any mechanism to reference specific files across folder boundaries, thus the compiled value of a folder depends only on its content and global modules rather than its context.

A file is compiled based on its file extensions. For example, to process a file named "foo.ext", the glas command line will load a global module named language-ext then extract a value representing a compiler function. This function is interpreted, receiving the file binary as input and producing the compiled value as output. 

File extensions may compose, e.g. "foo.x.y" would imply a two-pass compile, first by language-y then by language-x. The value passed to language-x may be structured data. Use cases for composition include text preprocessors and building higher level compilers based on structured data.

The compiler function has a very limited effects API. Other than producing a value, it may load other modules and log some messages, e.g. describing warnings or errors to support debugging. The constraint on effects ensures a deterministic and cacheable outcome. Dependencies between modules must form a directed acyclic graph, otherwise the glas system will report dependency cycles as errors.

## Values

Glas represents data using immutable binary trees, i.e. such that each node in a tree has optional left and right children respectively labeled '0' and '1'. The most naive representation is:

        type T = ((1+T) * (1+T))

A binary tree can directly represent unit `()`, pair `(a,b)`, and sum types `L a | R b`. However, glas systems generally favor labeled data because it is extensible and meaningful. Text labels are encoded into a null-terminated UTF-8 path through the tree. For example, label 'data' is encoded by path `01100100 01100001 01110100 01100001 00000000`, where '0' represents a left branch and '1' a right branch. Multiple labels can be encoded into a binary tree with shared prefixes, forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree) as the basis for record and dictionary data.

To efficiently represent non-branching path fragments, the under the hood representation is closer to:

        type Bits = compact Bool list
        type T = (Bits * (1 + (T * T)))

Simple data such as integers and symbols can be encoded into bitstrings - trees with a single, non-branching path terminating in a leaf node. For example, a byte is an 8-bit bitstring, msb to lsb, such that `00010110` is byte 22. A variable-width natural number 22 is almost the same but loses the zeroes prefix `10110`. Negative integers can be conveniently represented by negating all the bits, so -22 is `01001`.

Glas systems encode most sequential structures as lists. Logically, a list is tree representing a right-associative sequence of pairs, terminating in unit, i.e. `type List = (Elem * List) | ()`. 

        /\
       1 /\     the list [1,2,3]
        2 /\
         3  ()

However, direct representation of lists is awkward and inefficient for many use-cases such as random access or queues. Thus, glas systems will often represent lists under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). Binaries (lists of bytes) are even more heavily optimized, being the most popular type for interfacing with external systems (filesystems, networks, etc.).

Glas systems can be further extended with specialized representations for tables, matrices, and other common types. This is related to *Acceleration*, which extends performance primitives without affecting formal semantics.

Glas values may scale to represent entire databases. Subtrees of larger-than-memory values can be semi-transparently stored to disk then content-addressed by secure hash, optionally guided by program annotations. This pattern is called *Stowage* and it also contributes to performance of persistence, communication, and memoization.

## Programs

Programs are values with a stable interpretation. 

The interpretation described here is used for language modules and to represent basic applications for the glas command line. It is designed for simplicity and compositionality at significant costs to flexibility and convenience of direct use by humans. Performance can be augmented by acceleration and other specialized optimizations.

Programmers usually express these programs indirectly, e.g. write ".g0" files that compile into dictionaries of reusable programs. Metaprogramming features can mitigate flexibility.

### Stack Operators

Glas programs manipulate an implicit data stack. All operations apply to data near top of the stack.

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

The stack in glas is really an intermediate data plumbing model. User syntax could hide stack shuffling behind local variables. The glas compiler can replace the stack with static memory and register allocations, leveraging static arity of valid glas programs. The main reason for the stack is that it simplifies operators, which don't need to specify input sources or output targets.

### Control Operators

Glas programs must have static stack arity, i.e. the 'try-then' arity must match the 'else' arity, or a loop 'while-then' must have the same input and output arity. This is designed to be easy to verify.

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * *nop* - identity function, can be represented by empty seq  
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
 * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat loop. Otherwise, exit loop. Variants:
 * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat loop. Otherwise, exit loop.
 * 'do' field is optional, defaults to nop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail; causes backtracking in context of cond/loop
* **halt:Message** - logically diverges, like an infinite loop but more explicit in its intention. Prevents backtracking. Message should indicate cause to a human programmer: undefined behavior, type-error, todo, etc.. 

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
        halt:_ : ∀S,S' . S → S'

A consequence of this 'loop' model is that we essentially need to represent loops as state machines.

In context of transaction machines, 'halt' will effectively abort the entire transaction.

User syntax can extend the effective set of control operators, e.g. compiling a mutually recursive function group into a central loop. Glas does not provide an 'eval' operator, but an 'eval' function may potentially be accelerated.

### Data Operators

Glas programs support a few operators to support ad-hoc manipulation of data (binary trees). These operators are most directly applicable to radix tree (aka record) manipulation but can be leveraged together with control operators to manipulate arbitrary data.

Operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not in record.
* **put** (V (label?_|R) label -- (label:V|R)) - given a label, record, and value on the data stack, create new record with the given label associated with the given value. Will replace existing label in record.
* **del** ((label?_|R) label -- R) - returns new record minus label and any vestigial prefix thereof (i.e. any prefix that isn't shared by another label in the record). Effectively will 'put' label with unit value then erase back towards branch.

Labels are bitstrings that often share prefixes but are distinguished by suffix. It is possible to operate on multiple fields within a record by operating on the shared prefix directly. *Note:* If the label input is not a bitstring, behavior is divergence instead of backtracking failure.

Performance of data manipulations will depend on *Acceleration* in many cases.

### Effects and Environments

Glas effects are a based on a simple exchange of data with the host environment. An effects API can be described in terms of which types of values are accepted as requests and the external behavior and returned response for each type.

Operators:

* **eff** - interact with the environment by exchange of data, 1--1 arity, `Request -- Response`. 
* **env:(do:P, with:Handler)** - simulate environment for subprogram P. Captures top value from data stack as environment State, runs P threading State through each 'eff' handler, then returns final State to the data stack. The Handler should be a glas program with 2--2 arity, `Request State -- Response State`. 

Dependent types or session types may be required to precisely describe an effects API. A top-level API must be documented for use by applications, perhaps specialized for context. 

### Annotations

Annotations in glas programs support tooling without affecting formal program behavior. The 'prog' header is also used as the required toplevel header for glas programs in many cases, even if no annotations are needed.

* **prog:(do:P, ...)** - runs program P. If 'do' is elided, defaults to nop. All fields other than 'do' are annotations and must not affect formal behavior of program.

Annotations can affect performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), debugging (tracing, profiling, assertions, breakpoints), decompilation (variable names, comments, etc.), and essentially anything other than program meaning or behavior. 

Some annotations in use:

* *accel:Model* - accelerate the program. The Model is usually a symbol indicating a built-in function that should replace the direct implementation. 
* *arity:(i:Nat, o:Nat)* - describe stack arity of a program. This can be checked by a compiler, or help stabilize partial evaluation.

Still need to develop good annotations for stowage and memoization.

## Applications

The glas command line knows how to run applications with access to filesystem and network effects. A basic application is represented by a program that implements a transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

This application model has many nice properties related to live coding, extenbsibility, debuggability, reactivity, concurrency, distribution, and orthogonal persistence. It is an excellent fit for my vision of glas systems. However, achieving these benefits requires optimizations that are difficult to guarantee for a step function. To mitigate this, the glas command line will also support a program model specialized for applications.

Alternatively, we can extract an executable binary from the glas module system. This would use glas modules as a build system.

## Language Modules

Language modules are global modules with a simple naming convention: `language-xyz` is used as the compiler function for files with extension `.xyz`. The language module shall compile to a value of form `(compile:prog:(do:Program, ...), ...)`. The 'prog' header provides a path for potential extension. Fields other than 'compile' can support other tooling, such as IDE or REPL integration.

The compiler function currently must be a glas program with 1--1 arity. Input is usually the file binary (multiple file extensions is a special case). Output is the compiled module value, or failure if the input cannot be compiled. Compile-time effects are constrained to simplify caching, sharing, and reproducibility:

* **load:ModuleRef** - Response is compiled value for the indicated module. The request may fail, e.g. if the module cannot be found or compiled, with cause implicitly logged. Currently propose a few forms of ModuleRef: 
 * *global:String* - search for global module based on configuration of CLI
 * *local:String* - search for module in same folder as file being compiled
* **log:Message** - Message should be a record, e.g. `(text:"Uh oh, you messed up!", lv:warn)`, so that it can be flexibly extended with metadata. Response is unit. Behavior depends on development environment, e.g. might print the message to stderr with color based on level.

The glas command line executable shall include a built-in implementation of the ".g0" compiler, and an interpreter for glas programs. This is used to bootstrap the [language-g0](GlasZero.md) module, if possible. If bootstrap fails, a warning or error will be reported.

## Automated Testing

Static assertions when compiling modules are useful for automated testing. However, build-time is deterministic and under pressure to resolve swiftly. This leaves an open niche for long-running or non-deterministic tests, such as overnight fuzz-testing. Use of a non-deterministic 'fork' effect would be useful for testing:

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' bitstring. In context of testing, the choice doesn't need to be fair or random. It can be guided by heuristics, memory, and program analysis to search for failing test cases.

A test might be represented as a 0--0 arity program that is pass/fail. In addition to fork, a 'log' effect would be useful for generating messages to support debugging.

## Type Checking

Glas systems will at least check for stack arity ahead of time. A more precise static type analysis is optional, but can be supported via annotations. Memoization is required to mitigate rework. In some cases, types might be checked at runtime, as we do with label inputs to get/put/del - in that case, `halt:type-error` is an effective way to handle a runtime type error. 

## Performance

### Acceleration

        prog:(do:Behavior, accel:Model, ...)

Acceleration is an optimization pattern where a compiler or interpreter substitutes subprograms with built-in implementations. To guard against silent performance degradation in glas systems, this is explicitly specified by 'accel:Model' annotations. The Model is often a symbol such as 'list-append' indicating a built-in function. 

Accelerators are often associated with specialized runtime representations of data. For example, lists might be represented by finger-tree ropes to support accelerated list-append. However, the data retains its view as a tree. Essentially, accelerators introduce new 'performance primitives' without any impact on formal semantics.

Although it is feasible to accelerate many individual operations (such as list append or floating point arithmetic), one of the more fruitful and robust options is to accelerate evaluation of a virtual machine. For example, glas systems could broadly support compression and cryptography by accelerating a virtual CPU. Or support machine learning and ray tracing by accelerating a virtual GPGPU. The accelerated machine becomes a new compilation target for metaprogramming.

*Note:* In some cases, we might want optional accelerators where it would be nice but is not necessary. For this case, I propose 'accel:opt:Model' to explicitly indicate optional acceleration.

### Stowage

I use the word 'stowage' to describe the use of content-addressed storage to serialize larger-than-memory data to disk or the network, then loading it back into local memory as needed. Stowage serves as an alternative to virtual memory paging. Stowage indirectly supports compression, memoization, and content distribution.

The [Glas Object](GlasObject.md) representation is designed for use with stowage. However, my vision is that stowage should mostly be implicit within glas systems. Program annotations can guide stowage without observing it. An application effects API can provide access to a persistent database and data channels that abstract over serialization, integrating stowage. 

### Memoization

Purely functional subprograms in glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds, and integrate with stowage.

Glas systems assume memoization as a solution to many performance issues that would otherwise require explicit state. Without memoization, the potential scale of glas systems would be severely constrained.

### Content Distribution

Networked glas systems will support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve performance when repeatedly serving large values. This feature is tightly aligned with *Stowage*.

The CDN service is not fully trusted, so data distributed through the CDN should be encrypted. A lookup key and encryption key are derived from the content secure hash (e.g. take 256 bits each). The client will query the CDN using the lookup key, download the encrypted binary, decrypt then decompress locally, and finally verify against the original secure hash. 

The encrypted binary may include a non-encrypted header that indicates encryption algorithm, compression algorithm, decompressed size, and metadata.

A list of dependencies (lookup keys) will also be uploaded such that the CDN can request any missing dependencies or track dependencies for purpose of accounting, scoping sessions, or automatic garbage collection.

### Compression Pass

When compiling glas programs, a useful optimization pass is to identify common subprograms and translate those to reusable function calls. This pass may be guided by annotations and is not necessarily aligned with user defined functions. Usefully, compression may occur after partial evaluation and other specialization passes. 

### Application Layer

I'm tentatively developing a 'proc' program model (in [Glas Applications](GlasApps.md)) for expressing transaction machines, with some design considerations to simplify several optimizations related to incremental computing, parallelism, concurrency, distributed evaluation, and transaction fusion. 

Once developed, this program model might be evaluated via 'glas --run', compiled to executable binary then extracted, or deterministically simulated within a glas program for compile-time computations.

In any case, glas system is not stuck forever with the 'prog' program model and its foibles. Performance is one reason we might develop alternative program models.

## Thoughts

### Useful Languages

The g0 language is used for bootstrap. It is a Forth-inspired language with expressive metaprogramming features. But it's intended as a stable starting point, not a final language, nor as something to directly improve. The glas system will need language modules for other use cases.

Data languages will often be more convenient than embedding data in a programming language. In part because this simplifies working with external tools. We could support ".txt" files (e.g. convert UTF-16 to UTF-8, remove byte-order mark, check spelling, etc.). We can also support structured data files - JSON, XML, CSV, MsgPack, SQLite, Cap'n Proto, or even [Glas Object](GlasObject.md).

Programming languages can support mutually recursive definitions, multi-step procedures, process networks, variables, type annotations, type-guided overrides and program search, and many more features. The g0 language is quite awkward for some use-cases.

Text preprocessor languages can support language-agnostic text-layer macros. We can leverage composition of file extensions, e.g. such that ".json.m4" will process a file first via text preprocessor 'language-m4' then via 'language-json'. Reusable text macros could be exported from related ".m4h" modules.

It is feasible to develop graphical programming in glas, using structured files (perhaps even database files) to represent modules within a program in a manner easily viewed and manipulated by tools other than text editors. This is a general direction I'd like to pursue relatively early in glas systems, though I don't have many precise ideas yet.

### Abstract and Linear Data

Abstraction is a property of a subprogram, not of data. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. Linear types extend these contextual restrictions to copy and drop operations. A static analysis of a program could track whether each parameter is abstract or linear.

Linear types can potentially support in-place updates, reducing garbage collection. 

But I think this is better to explore after the glas system is more mature. Binary trees provide a simple foundation, while acceleration and, later, full data abstraction for applications provides an effective means to escape the limits of this foundation.

### Databases as Modules

It is feasible to design language modules that parse MySQL database files, or other binary database formats (LMDB, MessagePack, Glas Object, etc.). Doing so might simplify use of tooling that outputs such files from a visual or graphical programming environment. 

A relevant concern is that database files will tend to be much larger than text files, and will receive more edits. This could be mitigated by partitioning a database into multiple files. But mostly I think we'll need to rely more upon explicit memoization instead of implicit caching per module.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be considered a hard constraint to guide program decisions. Programs expressed this way can resolve ambiguity, fill the gaps, and produce large working systems from relatively few words.

Search is expensive, so it is necessary to reduce rework. Stateful solutions are viable, i.e. we could use special editors or tools to move part of program search to edit-time. But I'd prefer stateless solutions, such as memoization.

Memoization can mitigate rework insofar as we have [consistent heuristics](https://en.wikipedia.org/wiki/Consistent_heuristic) for utility, i.e. such that we can locally filter for good modular components without looking at global fitness. Of course, those heuristics are also contextual. Perhaps a monotonic heuristic can be aligned with expression of component search.

### Provenance Tracking

Manually tracing data to its sources is challenging and error-prone. Glas further hinders manual tracking because language module compiler functions cannot refer to file or module names. This ensures code can be moved or shared without changing its meaning. Only content-addressed provenance is possible, e.g. based on secure hash of binary, or notation within the code.

I hope to support provenance tracking in a more systematic manner, such that all data is traced to its sources without any explicit effort by the programmers. This might involve something similar to [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) heuristics, e.g. assuming that widely used/tested dependencies (such as language parser code) contributes 'less' as a source.

### Computation Model 

I'm very tempted to replace the initial program model for glas systems to something more friendly for parallel/concurrent computation, preferably without relying on accelerators. I'm contemplating a few [alternative models](AltModels.md) but I'm not very satisfied with any particular idea.
