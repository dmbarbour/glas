# Glas Design

Glas was named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. As a backronym, 'General LAnguage System'. 

Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, live coding. Compared to conventional languages, there is a lot more focus on the compile-time. 

## Command Line

The glas system starts with a command line tool 'glas'. This tool knows how to compile modules, bootstrap language-g0, extract binary data, and run simple applications. The 'language' of command line arguments is extensible by defining 'glas-cli-(opname)' modules and use of application macros. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax

Modules are global or local, and are represented by files and folders. Every module either compiles to a glas value, or fails to compile. In addition to producing a compiled value, the compiler may generate a sequence of log messages to support development.

### Compiling Files

A file is compiled based on its file extensions. For example, to process the file "foo.ext", the glas command line will first load a global module named language-ext, then extract a value representing a compiler function. This function receives the file binary and outputs the compiled value for the module (or fails), and has access to limited effects such as loading other modules and logging warnings for the programmer (see *Language Modules* later).

File extensions may compose, e.g. "foo.json.m4" could be expanded by language-m4 that implements a macro preprocessor, then parsed into structured data by language-json. In the general case, a compiler might further process structured data. Conversely, a file with no extensions compiles to its raw binary.

### Compiling Folders

A folder compiles based on the contained 'public' file, which may have any file extension. Glas does not allow referencing across folder boundaries. This greatly simplifies local reasoning and refactoring. Global modules are always represented by folders, in part to provide a convenient space for README and LICENSE files.

### Module Search

When loading a module, whether it is 'global' or 'local' is explicit, essentially forming two distinct namespaces with no implicit fallback. File extensions are not included in the module name. Global modules are found based on configuration of the command line. By default, this involves sequentially searching GLAS_PATH (a list of folders) for a subfolder with a matching name. Local modules are files or subfolders found in the same folder as the current file, or within the current directory if no file is being compiled.  

Dependencies between modules must be acyclic, forming a directed acyclic graph. Cyclic dependencies will be detected and reported as an error.

## Values

Glas values are immutable binary trees, i.e. where each node in the tree has an optional left and right children, respectively labeled '0' and '1'. The most naive representation is:

        type T = ((1+T) * (1+T))

A binary tree can directly represent unit `()`, pair `(a,b)`, and sum types `L a | R b`. However, glas systems generally favor labeled data because it is extensible and meaningful. Text labels are encoded into a null-terminated UTF-8 path through the tree. For example, label 'data' is encoded by path `01100100 01100001 01110100 01100001 00000000`, where '0' represents a left branch and '1' a right branch. Multiple labels can be encoded, sharing prefixes, essentially forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree) as the basis for record and dictionary data.

To efficiently represent non-branching path fragments, glas values use a representation under the hood closer to:

        type Bits = compact Bool list
        type T = (Bits * (1 + (T * T)))

Glas systems will encode small, simple values directly into bitstrings. A bitstring is a tree with a single, non-branching path. For example, a byte is an 8-bit bitstring, msb to lsb, such that `00010110` is byte 22. A variable-width natural number 22 is almost the same but loses the zeroes prefix `10110`. Negative integers can be represented by inverting all the bits, so -22 is `01001`. 

Glas systems encode most sequential structures as lists. Logically, a list is tree representing a right-associative sequence of pairs, terminating in unit, i.e. `type List = (Elem * List) | ()`. 

        /\
       1 /\     the list [1,2,3]
        2 /\
         3  ()

However, direct representation of lists is awkward and inefficient for many use-cases such as random access or queues. Thus, glas systems will often represent lists under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_(data_structure)). Binaries (lists of bytes) are even more heavily optimized, being the most popular type for interfacing with external systems (filesystems, networks, etc.).

Glas systems can be further extended with specialized representations for tables, matrices, and other common types. This is related to *Acceleration*, which extends performance primitives without affecting formal semantics.

Glas values may scale to represent entire databases. Subtrees of larger-than-memory values can be semi-transparently stored to disk then content-addressed by secure hash, optionally guided by program annotations. This pattern is called *Stowage* and it also contributes to performance of persistence, communication, and memoization.

## Programs

Programs are simply values with a standard interpretation. The interpretation described here is the one used by the command line for use in language modules and runnable applications. It is designed for simplicity, compositionality, and convenient integration with the application model at cost to flexibility. Performance depends on embedded annotations and some specialized optimizations.

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
 * *nop* - do nothing, identity function - can be represented explicitly by empty seq.  
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
 * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat loop. Otherwise, exit loop. Variants:
 * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat loop. Otherwise, exit loop.
 * 'do' field is optional, defaults to nop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail, allows backtracking
* **tbd:Message** - logically diverges (like infinite loop), no backtracking. Message is for humans. 

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
* **del** ((label?_|R) label -- R) - remove label from record, including any suffix of the label that is not shared by other labels in the same record. If label is not in record, is equivalent to adding label first then removing it.

Labels are bitstrings that often share prefixes but are distinguished by suffix. It is possible to operate on multiple fields within a record by operating on the shared prefix directly. *Note:* If the label input is not a bitstring, behavior is divergence instead of backtracking failure.

Although optimized for records, these operators are used to build all data manipulation in glas programs. Performance will often depend on *Acceleration*.

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

To support robust optimizations, some applications will be represented as process networks. These are described more thoroughly within the [glas applications](GlasApps.md) document.

This application model has many nice properties related to live coding, extenbsibility, debuggability, reactivity, concurrency, distribution, and orthogonal persistence. It is an excellent fit for my vision of glas systems.

Alternatively, we can use glas as a build system then extract an executable binary. Bootstrap of the glas command line executable is supported this way.

## Language Modules

Language modules are global modules with a simple naming convention: `language-xyz` is used as the compiler function for files with extension `.xyz`. The language module must compile to a value of form `(compile:prog:(do:Program, ...), ...)`.  

The compiler program must be a glas program with 1--1 arity. Program input is usually a file binary (modulo multiple file extensions). Output must be the compiled module value, or failure if the input cannot be compiled. Compile-time effects are extremely limited to simplify reasoning about caching, sharing, and reproducibility:

* **load:ModuleRef** - Response is compiled value for the indicated module. The request may fail, e.g. if the module cannot be found or compiled, with cause implicitly logged. Currently propose a few forms of ModuleRef: 
 * *global:String* - search for global module with matching name, usually via GLAS_PATH.
 * *local:String* - search files and subfolders local to file currently being compiled.
* **log:Message** - Message should be a record, e.g. `(text:"Uh oh, you messed up!", lv:warn)`, so that it can be flexibly extended with metadata. Response is unit. Behavior depends on development environment, e.g. might print the message to stderr with color based on level.

*Note:* The glas command line will have a built-in implementation of the ".g0" compiler. This is be used to bootstrap [language-g0](../glas-src/language-g0/README.md) module, if possible. If bootstrap fails, the command line will log a warning but continue with the built-in.

## Automated Testing

Static assertions when compiling modules are useful for automated testing. However, build-time is deterministic and under pressure to resolve swiftly. This leaves an open niche for long-running or non-deterministic tests, such as overnight fuzz-testing. Use of a non-deterministic 'fork' effect would be useful for testing:

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' bitstring. In context of testing, the choice doesn't need to be fair or random. It can be guided by heuristics, memory, and program analysis to search for failing test cases.

A test might be represented as a 0--0 arity program that is pass/fail. In addition to fork, a 'log' effect would be useful for generating messages to support debugging.

## Type Checking

Glas systems will at least check for stack arity ahead of time. A more precise static type analysis is optional, but can be supported via annotations. Memoization is required to mitigate rework. In some cases, types might be checked at runtime, as we do with label inputs to get/put/del - in that case, `tbd:type-error` is an effective way to handle a runtime type error. 

## Performance

### Stowage

Glas systems will support storage and communication of large data by serializing subtrees to the [Glas Object](GlasObject.md) (aka 'glob') encoding then referencing the glob binary by SHA3-512 secure hash. I call this pattern 'stowage'. 

Stowage can be guided by program annotations or performed heuristically by a garbage collector. Stowage essentially replaces use of virtual-memory paging. Consistent use of secure hashes simplifies the interaction of stowage with memoization and content distribution.

### Memoization

Purely functional subprograms in glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds, and integrate with stowage.

Glas systems assume memoization as a solution to many performance issues that would otherwise require explicit state. Without memoization, the potential scale of glas systems would be severely constrained.

### Content Distribution

Networked glas systems will support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve performance when repeatedly serving large values. This feature is tightly aligned with *Stowage*.

The CDN service is not fully trusted, so data distributed through the CDN should be encrypted. A lookup key and encryption key are derived from the content secure hash (e.g. take 256 bits each). The client will query the CDN using the lookup key, download the encrypted binary, decrypt then decompress locally, and finally verify against the original secure hash. 

The encrypted binary may include a non-encrypted header that indicates encryption algorithm, compression algorithm, decompressed size, and metadata.

A list of dependencies (lookup keys) will also be uploaded such that the CDN can request any missing dependencies or track dependencies for purpose of accounting, scoping sessions, or automatic garbage collection.

### Acceleration

Acceleration is an optimization pattern. The idea to annotate specific subprograms for accelerated evaluation, then a compiler or interpreter should recognize the annotation then silently substitute a specialized implementation. Accelerated functions are often coupled with specialized data representations. For example, a glas runtime may represent lists using finger trees.

Essentially, accelerators extend performance primitives without affecting formal semantics. However, performance is an important part of correctness for many programs. To stabilize performance, a compiler must accelerate only where explicitly annotated, and must report where requested acceleration cannot be achieved.

The cost of acceleration is implementation complexity and risk to correctness, security, and portability. The complexity tradeoff is most worthy where acceleration enables use of glas in new domains. Further, risk is mitigated by the reference implementation, which can be leveraged to support automatic testing. 

*Aside:* To reduce cycle time while experimenting, we may allow acceleration to behave as a compiler built-in, e.g. `prog:(do:tbd, accel:list-append)`. The proper reference implementation can wait for behavior to stabilize.

### Parallelism

Glas program model is not designed for parallelism.

A compiler can squeeze out some parallelism via dataflow analysis. For example, assuming F and G are both 1--1 arity and one of F or G is pure (no effects), we can evaluate `seq:[F, dip:G]` in parallel. However, this technique has limited applicability and scalability.

For robust parallelism at scale, it is feasible to *accelerate* evaluation of a distributed, confluent, monotonic virtual machine. [Kahn process network](https://en.wikipedia.org/wiki/Kahn_process_networks) and [Lafont Interaction Networks](https://en.wikipedia.org/wiki/Interaction_nets) can provide some inspiration.

## Thoughts

### Useful Languages

The glas system will need many language modules. 

The g0 language is used for bootstrap. It is a Forth-inspired language with expressive metaprogramming features. But it's intended as a stable starting point, not a final language or something to significantly improve over time.

There is room for more programming languages. The g0 language falls short on many features: recursive definitions, visibility of pattern matching and data plumbing, type annotations and implicit type checking, process/procedure layer composition (involving multiple transactional steps), type-guided overrides and program search, etc..

Data languages will often be more convenient than embedding data within a programming language. In part because this simplifies working with external tools. We could support ".txt" files (e.g. convert UTF-16 to UTF-8, remove byte-order mark, check spelling, etc.). We can also support structured data files - JSON, XML, CSV, MsgPack, SQLite, Cap'n Proto, or even [Glas Object](GlasObject.md).

A generic text preprocessor language that can import, define, and invoke character-level macros can be widely useful. Users could apply the preprocessor anywhere it might be useful via composing file extensions, e.g. ".g0.m" vs. ".json.m", while keeping it separate from the underlying language.

It is feasible to explore graphical and structured editors for certain modules. This would generally require that all files are of types recognized by the editor, but also have consistent interpretations by glas language modules.

### Abstract and Linear Data

Abstraction is a property of a subprogram, not of data. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. Linear types extend these contextual restrictions to copy and drop operations. A static analysis of a program could track whether each parameter is abstract or linear.

Linear types can potentially support in-place updates, reducing garbage collection. 

But I think this is better to explore after the glas system is more mature. Binary trees provide a simple foundation, while acceleration and, later, full data abstraction for applications provides an effective means to escape the limits of this foundation.

### Object Oriented Programming

Glas program model isn't designed to support OOP, and I don't intend to encourage OOP. However, as a thought experiment:

One option is to model objects as communicating glas processes. In each step, a process can send and receive some messages. This design keeps code separate from data in the glas program layer, and simplifies live coding. However, all method calls become asynchronous, similar to actors model. It is feasible to reify each method call as a new session or subchannel between objects.

An alternative is to model and accelerate virtual machines that host a collection of objects. The virtual machine state would use an optimized internal representation associated with JIT-compiled code. If carefully designed, we could also support migration of volumes of objects, i.e. composing and decomposing the collection based on regions.

Either of these options have some advantages. Modeling objects as processes would fit distributed and concurrent programming styles. Accelerating a virtual machine could be widely useful in many other cases, and would make scope obvious. 

### Reactive Systems Programming 

Robust reactive systems benefit from precise temporal semantics, abstracting over variable latency and arrival-order non-determinism, and modeling precise synchronization of outputs.

[Kahn process networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) are a viable foundation. We can extend KPNs with temporal semantics - every process and message has an implicit logical time, and a process can observe which channel has the next message in temporal order. This can be combined with dynamic channels to model open systems. 

KPNs could then be compiled into a lower level model that exposes arrival-order non-determinism, such as glas applications.

### Static Routing of Effects? Reject.

Currently 'eff' always runs the same handler for an effect. It is feasible to modify 'eff' to support static routing, i.e. use `eff:Operator` and an environment of type `Operator -> Handler`.

This solution hinders dynamic 'eval'. We must statically identify the runtime effects potentially used by a subprogram. Another issue is that it's tempting to treat 'eff' as a macro namespace, with Operator containing Program values that are integrated into the Handler - it easily becomes awkward to manage namespaces correctly.

The current form of eff/env fits my goals of maintaining simplicity and comprehensibility, albeit at the cost of shifting more optimization work to depend on abstract interpretation.

#### Bracketed Effects? Reject.

Bracketing currently requires explicit commands to modify state around an operation, i.e. `seq:[StartEff, Operation, StopEff]`. This is error prone, though it can be mitigated by language modules that abstract over the pattern. It's tempting to build this pattern into the glas program model. But with retrospect, I don't want to extend glas program model only to support patterns. That's the responsibility of the language layer.

### Database Modules

It is feasible to design language modules that parse MySQL database files, or other binary database formats (LMDB, MessagePack, Glas Object, etc.). Doing so can simplify tooling that supports interactive or graphical programming styles. 

A relevant concern is that database files will tend to be much larger than text files, and will receive more edits by concentrating program representation into fewer files. This makes fine-grained memoization more important.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be considered a hard constraint to guide program decisions. Programs expressed this way can resolve ambiguity, fill the gaps, and produce large working systems from relatively few words.

Search is expensive, so it is necessary to reduce rework. Stateful solutions are viable, i.e. we could use special editors or tools to move part of program search to edit-time. But I'd prefer stateless solutions, such as memoization.

Memoization can mitigate rework insofar as we have [consistent heuristics](https://en.wikipedia.org/wiki/Consistent_heuristic) for utility, i.e. such that we can locally filter for good modular components without looking at global fitness. Of course, those heuristics are also contextual. Perhaps a monotonic heuristic can be aligned with expression of component search.

### Provenance Tracking

Manually tracing data to its sources is challenging and error-prone. Glas further hinders manual tracking because language module compiler functions cannot refer to file or module names. This ensures code can be moved or shared without changing its meaning. Only content-addressed provenance is possible, e.g. based on secure hash of binary, or notation within the code.

I hope to support provenance tracking in a more systematic manner, such that all data is traced to its sources without any explicit effort by the programmers. This might involve something similar to [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) heuristics, e.g. assuming that widely used/tested dependencies (such as language parser code) contributes 'less' as a source.

### Extended Tacit Environment? Reject.

Instead of a single data stack, I could extend glas programs to work with an auxilliary stack for temporary data storage. The 'dip' behavior already does this to some degree, but doesn't make it easy to access data deep within a loop. A second data stack would complicate arity descriptions but could simplify some data plumbing. However, I think this isn't very flexible compared to trying to improve data plumbing purely at the syntax layer.

### Abstract Operators and Namespaces? Defer.

I'm tempted to extend glas programs with abstract operators that can be defined in context. Something like:

* **op:OpRef** - call an abstract operator specified by OpRef. OpRef could be symbolic, but could also be structured to model a macro or DSL.
* **ns:(do:P, with:Defs)** - evaluate P, but replace each 'op:OpRef' by its definition (if defined). 

Unfortunately, composition of 'ns' subprograms is awkward: either we replicate common definitions or require a lot of extra computation to tease them out. Unless 'ns' supports higher order staged programming, we'll still be using macro abstractions at the language layer. It's tempting to unify 'ns' with env/eff, but more difficult to consistently model system state.

Without this feature, we still benefit from structure sharing and content-addressed storage for common subprograms. Memoization can reduce rework when processing common subprograms. A compression pass can later produce an intermediate representation with a namespace for common subprograms. Language modules can provide their own namespace logic. 

Compositionality is a primary design goal for glas. I will eschew namespaces at the glas program layer until I have a solution that doesn't interfere with compositionality.

### Decompilation? Defer.

An interesting possibility is to support 'decompile' functions on language modules, then to support access to the decompiler via something like 'load:global:"foo.xyzzy"' as fully compiling module 'foo' then subsequently decompiling with language-xyzzy, and perhaps automatically verifying the round-trip (that applying language-xyzzy 'compile' results in the original decompiled value).

This would make access to language-layer representations a lot more accessible without leaking information about the original syntax. It would also encourage compilers to maintain suitable metadata for a precise decompilation.

