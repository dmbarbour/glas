# Glas Design

As a backronym, 'General LAnguage System'. Glas was named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. 

Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. However, there is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool knows how to compile modules, bootstrap language-g0, extract binary data, and run simple applications. The 'language' of command line arguments is extensible by defining 'glas-cli-(opname)' modules and use of application macros. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax

Modules are represented by files and folders. Every module compiles to a glas value. This value usually represents a dictionary of useful definitions, but there is no restriction on type.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will load the global module 'language-ext' then evaluate the compiler program defined by that module. File extensions may compose, e.g. to process file "foo.x.y" we'll first appy language-y followed by language-x.

A folder is compiled to the value of the contained 'public' module, which must be a file. Folders also serve as the boundary for local modules: a local module must be another file or subfolder within the same folder as the file being compiled. This ensures folders are relocatable within a context of global modules.

Global modules are represented by folders and are discovered based on command line configuration. Initially this configuration supports a conventional search path, but it should eventually include network repositories.

The compiler function has a limited effects API to simplify caching and reproducibility. The glas system will also report an error in case of dependency cycles. See *Language Modules*

## Values

Glas represents data as finite, immutable binary trees, i.e. such that each node in a tree has optional left and right children respectively labeled '0' and '1'. The most naive representation is:

        type Tree = ((1+Tree) * (1+Tree))

A binary tree can easily represent a pair `(a, b)` or either type `(Left a | Right b)`. However, glas systems favor labeled data because labels are more meaningful and extensible. Labels are encoded into a *path* through a tree, favoring null-terminated UTF-8. For example, label 'data' would be encoded into the path `01100100 01100001 01110100 01100001 00000000` where '0' and '1' respectively represent following the left or right branch. A 'record' may have many such paths with shared prefixes, forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). A 'variant' (aka 'tagged union') should have exactly one label. 

To efficiently represent labeled data, non-branching paths must be compactly encoded far fewer allocations. One viable under the hood representation is closer to:

        type Tree = (Stem * Node)
        type Stem = compact Bool list
        type Node = Leaf | Branch of Tree * Tree 

Short, simple data, such as integers and symbols, will be directly encoded into bitstrings. Bytes are encoded using a bitstring of exactly 8 bits, msb to lsb. Integers are usually encoded as variable length bitstring, msb to lsb, with negatives in one's complement:

        Integer  Bitstring
         4       100
         3        11
         2        10
         1         1
         0         . (empty)   
        -1         0
        -2        01
        -3        00
        -4       011

The tree consisting of a single node with no children is widely used to represent zero, unit values, or an empty list or record. 

Sequential structure is often encoded as a list. A list is represented as a binary tree where the left nodes are elements and the right nodes form the spine of the tree, terminating with unit.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

However, direct representation of lists is inefficient for many use-cases. Under the hood, lists may be represented using arrays, binaries, and [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This representation would be accessible via specialized append, slice, and other list operations (see *Acceleration*).

To work with very large values, glas systems favor content-addressed storage to offload volumes of data to disk. I call this pattern *Stowage*, and it will be heavily guided by program annotations. Stowage simplifies efficient data persistence, memoization, and network communication in context of large values and structure-sharing update patterns.

## Programs

Programs are values with a known interpretation. In this case, the 'prog' model interprets a structured glas value. Separately, [language-g0](GlasZero.md) compiles text into these programs. User-defined syntax is possible in glas systems, bootstrapping from language-g0.

This program model prioritizes simplicity, locality, and compositionality. Performance and scalability take a hit. To recover performance, glas systems will rely on acceleration of specialized models or extension of the glas command line to recognize and interpret models other than 'prog'. 

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

Glas programs must have static stack arity, i.e. the 'try-then' arity must match the 'else' arity, and a loop 'while-then' must have the same input and output arity. Arity is relatively easy to compute, albeit slightly complicated by 'fail' and 'halt'.

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
  * *nop* - identity function can be represented by empty seq  
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
  * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat loop. Otherwise, exit loop. Variants:
  * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat loop. Otherwise, exit loop.
  * 'do' field is optional, defaults to nop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail; causes backtracking in context of cond/loop
* **halt:Message** - logically diverges, like an infinite loop but more explicit in its intention. Prevents backtracking. Message should indicate cause to a human programmer: undefined behavior, type-error, invalid accelerator, todo, etc.. 

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

User syntax can extend the effective set of control operators. For example, it is feasible to compile a local set of mutually recursive definitions into a state machine loop. Also, there is no built-in 'eval' but it is feasible to implement one.

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

Some proposed annotations:

* *accel:Model* - accelerate a program, i.e. replace it with a built-in. The Model here is usually a symbol identifying a known built-in, such as list-append. However, there is room for extension.
* *stow:Options* - stow data heuristically, usually top item on data stack after running program. 
* *memo:Options* - memoize the program. Might only memoize cases where no effects escape.
* *prof:(chan:Value, ... Options)* - code profiling support. The profiling channel may aggregate from many subprograms.
* *arity:(i:Nat, o:Nat)* - describe stack arity of a program. This can be checked by a compiler, or help stabilize partial evaluation.
* *name:Value* - identify a subprogram to support debugging and other features. 
* *sub* - hint to reuse this subprogram. A compiler is free to identify and reuse common subprograms heuristically, but an explicit hint can be useful.

A lot more needs to be developed here, such as an effective type system and a better understanding of which options are useful for memoization and stowage.

## Applications

The glas command line can run some applications with access to filesystem and network. I'm developing an application model around the idea of a repeating transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State) | Fail

A successful step is committed, then the application halts or continues based on return value. A failed step is aborted then retried. There are several useful optimizations that can be applied to this model. For example, we don't need to fully recompute failed transactions if we track dataflow dependencies. For non-deterministic choice, it is possible to evaluate both choices in parallel and even commit both if they don't have any read-write conflicts. 

I discuss these ideas further under [glas applications](GlasApps.md) and [glas command line interface](GlasCLI.md).

*Note:* Although the ability to 'glas --run' an application is convenient, an alternative is to construct an executable binary value within the glas module system then use 'glas --extract' to access it. This trades convenience for flexibility.

## Language Modules

Language modules are global modules with a simple naming convention: `language-xyz` provides the compiler function for files with extension `".xyz"`. Initial language modules should compile to a dictionary of form `(compile:prog:(do:Program, ...), ...)` with a 1--1 arity program. 

Input to the compiler function is (usually) a file binary. Final output is the compiled module value. Compilation may fail, hopefully after logging some error messages. Compile-time effects are constrained to simplify caching, sharing, and reproducibility. Effects API:

* **load:ModuleRef** - Response is compiled value for the indicated module. The request may fail, e.g. if the module cannot be found or compiled, with cause implicitly logged. Currently propose a few forms of ModuleRef: 
  * *global:String* - search for global module based on configuration of CLI
  * *local:String* - search for module in same folder as file being compiled
* **log:Message** - Message should be a record, e.g. `(text:"Uh oh, you messed up!", lv:warn)`. This simplifies extension with contextual metadata or switching from texts to more structured content. Response is unit. Log messages should target the human user or the development environment.

The glas command line will include a built-in compiler for [language-g0](GlasZero.md), a Forth-like language with staged metaprogramming. This built-in compiler is used to bootstrap the actual language-g0 module if possible, emitting a warning on bootstrap failure. Other glas languages can build upon language-g0.

Definitions other than 'compile' in the language module may be defined to support tooling, such as IDE or REPL integration, a standard linter or formatter, etc.. And glas may eventually accept compiler functions of types other than 'prog', e.g. to enhance caching or parallelism.

## Automated Testing

Static assertions when compiling modules are useful for automated testing. However, build-time is deterministic and under pressure to resolve swiftly. This leaves an open niche for long-running or non-deterministic tests, such as overnight fuzz-testing that can heuristically search for failures. 

Use of a non-deterministic 'fork' effect would be useful for testing:

* **fork:List** - Response is a non-deterministic value from the list. In context of testing, the choice doesn't need to be fair or random. It can be guided by heuristics and analysis, e.g. filter out options that provably result in a successful test.

A test might be represented as a 0--0 arity program that is pass/fail. In addition to fork, a 'log' effect would be useful for generating messages to support debugging.

## Type Checking

Glas systems will at least check for stack arity ahead of time. A more precise static type analysis is optional, but can be supported via annotations. Memoization is useful to mitigate rework. In some cases, types might be checked at runtime, as we do with label inputs to get/put/del. Use of `halt:type-error` is an effective way to indicate a runtime type error without treating it as conditional.

## Performance

### Acceleration

        prog:(do:Behavior, accel:Model, ...)

Acceleration is an optimization pattern where a compiler or interpreter substitutes subprograms with built-in implementations. The Model is very often a symbol such as 'list-append' indicating a specific built-in function. 

Acceleration is always explicit, via the 'accel' annotation. Keeping this explicit simplifies verification (e.g. a module can compare the accelerated and non-accelerated implementations) and resists silent performance degradation (a compiler or interpreter can report when acceleration fails, and replace with 'halt:accel:DebugMsg'). Optional acceleration can be expressed explicitly via 'accel:opt:Model'.

Acceleration often involves specialized runtime representations. For example, accelerated operations on lists depend on lists being represented as finger-tree ropes. It is feasible for accelerators to essentially extend glas with conventional data types.

### Accelerated VMs

This is a useful pattern for acceleration.

Instead of independently accelerating a large number of fixed-width and floating point arithmetic operations, we could model a virtual CPU or GPGPU that includes all these operations then accelerate evaluation of the model as a whole. The accelerator could then compile the program argument to run on an actual CPU or GPGPU. When the program is a static argument, this could easily be compiled ahead-of-time.

An accelerated VM can make a number of problem domains much more accessible, such as compression, cryptography, ray tracing, physics simulations, and machine learning. The pattern also scales to accelerating a virtual 'network' of processors to achieve higher levels of parallelism than would be easily achieved with accelerating individual operations.

### Stowage

I use the word 'stowage' to describe systematic use of content-addressed storage to hibernate volumes of larger-than-memory data to disk or network. Stowage is the immutable variation on virtual memory paging. There are benefits for persistence, memoization, and communication of very large values.

In context of glas systems, stowage will be semi-transparent: invisible to pure functions and *most* effects, yet guided by annotations and accessible to top-level effects as needed (data channels over TCP, persistent key-value database, runtime reflection). [Glas Object](GlasObject.md) is intended to be the main binary representation for stowed data.

### Memoization

Purely functional subprograms in glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds. 

In combination with stowage, it is possible to incrementally process large data by memoizing recursive computations. This includes indexing of data, insofar as indexing can be expressed in terms of merging the indices of each component. Lists require special attention for stable chunking but it is possible to align memoization with the underlying finger-tree if we annotate a reducing function as associative (this annotation would be subject to proof or random testing).

Indexing large data via memoization and stowage is an underlying assumption for my vision of glas systems. Without this, we would rely entirely on stateful indices, which are locally efficient but error prone and difficult to share, compose, extend, or update at runtime.

### Content Distribution

Networked glas systems can potentially support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve performance when repeatedly communicating large stowed data values (see *Stowage*). A CDN service is not fully trusted, but it is feasible to derive a decryption and lookup key from the original secure hash of content. 

Usefully, we might support garbage collection of the CDN. With encrypted data, it cannot directly read the data to find dependencies. But we could upload some metadata such as a list of lookup keys that should be present together with the data.

### Compression Pass

When compiling glas programs, a useful optimization pass is to identify common subprograms and translate those to reusable function calls. This pass may be guided by annotations and is not necessarily aligned with user defined functions. Usefully, compression may occur after partial evaluation and other specialization passes. 

### Application Layer 

The glas system is not forever stuck with the 'prog' program model and its foibles. It is feasible to extend the glas command line to support other program models for `glas --run`. As a concrete example, I intend to develop a 'proc' representation for transaction machine applications to better optimize incremental computing and concurrency (in [Glas Apps](GlasApps.md)).

However, this is a solution to pursue mostly where accelerators are awkward (e.g. due to relationship with effects). 

## Thoughts

### Computation Models

I'm tempted to build on a foundation that is more suitable for lazy, concurrent, and distributed computations compared to glas programs. Some ideas are to build on Kahn Process Networks, Lafont Interaction Nets, or the Verse calculus. The main issue is well-defined support for concurrent or lazy effects while preserving the mostly-static structure of glas programs.

The KPN idea involves a PL designed around a hierarchical structure of concurrent processes that read and write labeled 'ports'. Effects are modeled in terms of operations on ports. A process may wire ports of its subcomponents. Temporal semantics could be introduced by default, to support reactive systems, clocks, and events.

So far, every time I explore this, I end up deciding that simpler is better for the initial program model. We aren't stuck with the basic program model; it's just a starting point, and even KPNs can be accelerated. Meanwhile, it ensures computation is easily represented without complications from variables or 'holes' in data.

### Useful Languages

The g0 language is used for bootstrap. It is a Forth-inspired language with expressive metaprogramming features. But it's intended as a simple, stable starting point, not the final language for users.

Data languages will often be more convenient than embedding data in a programming language. In part because this simplifies working with external tools. We could support ".txt" files (e.g. convert UTF-16 to UTF-8, remove byte-order mark, check spelling, etc.). We can also support structured data files - JSON, XML, CSV, MsgPack, SQLite, Cap'n Proto, or even [Glas Object](GlasObject.md).

Programming languages can support mutually recursive definitions, multi-step procedures, process networks, variables, type annotations, type-guided overrides and program search, and many more features. The g0 language is quite awkward for some use-cases.

Text preprocessor languages can support language-agnostic text-layer macros. We can leverage composition of file extensions, e.g. such that ".json.m4" will process a file first via text preprocessor 'language-m4' then via 'language-json'.

It is feasible to develop graphical programming in glas, using structured files to represent modules within a program in a manner easily viewed and manipulated by tools other than text editors. This is a general direction I'd like to pursue relatively early in glas systems, though I don't have many precise ideas yet.

### Abstract and Linear Data

Abstraction is a property of a subprogram, not of data. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. 

Linear types extend these contextual restrictions to copy and drop operations. Linear types can potentially support in-place updates, reducing garbage collection. However, this optimization remains difficult to achieve in context of backtracking conditionals or debugger views. The more general motive for linear types is to typefully enforce protocols, such as closing a channel when done.

### Databases as Modules

It is feasible to design language modules that parse MySQL database files, or other binary database formats (LMDB, MessagePack, Glas Object, etc.). Doing so might simplify use of tooling that outputs such files from a visual or graphical programming environment. 

A relevant concern is that database files will tend to be much larger than text files, and will receive more edits. This could be mitigated by partitioning a database into multiple files. But mostly I think we'll need to rely more upon explicit memoization instead of implicit caching per module.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

The 'prog' model of programs is unsuitable because we'll also want concurrent construction, refinement, and analysis of the solution by multiple subprograms. Concurrent access would support overlays and refinements of decisions.

### Provenance Tracking

The glas module system hinders manual provenance tracking, e.g. we cannot access module name from the language module 'compile' function. This was intentional in that I do not want location-dependent semantics and factoring. However, it does have some costs with regards to debugging.

This can be mitigated by adding some provenance to log messages when compiling code. But this is very coarse grained. A better solution is to track provenance at a fine granularity within values, then distribute blame (or responsibility) heuristically, e.g. inverse to the number of direct observers of the source data (based on [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) heuristics).

Fine-grained tracking of provenance will require somehow annotating or mapping values to their dependencies. I do have some concept of annoted values in [Glas Object](GlasObject.md) that might be something we can leverage for this purpose. But the details will require a lot of design and implementation work to get right.

