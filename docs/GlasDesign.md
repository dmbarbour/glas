# Glas Language Design

Glas is now a backronym for 'General LAnguage System'. It was originally named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and the human mastery over glass as a material. 

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary using a program defined in module `language-ext`. To bootstrap, the [g0](GlasZero.md) language is predefined. File extensions compose. For example, to compute the value for `foo.xyz.json` we first compile using `language-json` then compile using `language-xyz`. If a file has no extension, its value is simply the binary. Files and folders whose names start with `.` are hidden from the Glas module system.

To compute the value for a folder `foo/`, we use the value from its contained `public` file. Folders are implicit boundaries for dependencies: a file can only reference global modules or those within the same folder as itself. 

Global modules are found using the GLAS_PATH environment variable, whose value should be a list of folders separated by semicolons. If there is no local module with a given name, we'll search for the first matching folder (not file) on GLAS_PATH. Later, we may extend module search to the network by some means, perhaps including a URL on GLAS_PATH.

*Note:* Glas does not specify a package manager. We can start with Nix or Guix, then later develop something more specialized. 

## Data Model

Glas data is immutable binary trees. A naive representation is:

        type T0 = ((1+T0) * (1+T0))

This trivially supports algebraic products (pairs with both edges), sums (choice of edge labeled '0' or '1'), and unit value (terminal tree node with no edges). The empty tree cannot be represented.

Glas encourages use of *labeled* data structures. Instead of simple pairs and sums, we encode labels into edges on a path through a tree. For example, the label 'data' is represented by path `01100100 01100001 01110100 0110001 00000000`, using a null terminator to clarify the end of label. Labeled data means we reach data after traversing the label. To efficiently encode labels, we'll use a different tree representation:

        type BitString = (compact) Bool list
        type T1 = (BitString * (1 + (T1*T1)))

A variant is a single labeled value. A record combines multiple labels, overlapping the common label prefixes to form a [radix tree](https://en.wikipedia.org/wiki/Radix_tree) due to compaction of bitstrings. A symbol is simply the label itself, terminated by unit (the single-node tree). 

Bytes are encoded as fixed-width bitstrings, msb to lsb, e.g. `00010111` is an 8-bit byte. A binary is a list of bytes, and a string is (usually) a utf-8 binary. Natural numbers are typically encoded as variable-width bitstrings, msb to lsb, excluding the zeroes prefix, e.g. number 23 is `10111`. 

Glas uses lists to encode sequential structures. Logically, a list has type `type List a = (a * List a) | ()`, i.e. a list is constructed of `(Head * Tail)` pairs, finally terminated by unit `()`. However, Glas systems will often represent lists under-the-hood as [finger trees](https://en.wikipedia.org/wiki/Finger_tree), using *Acceleration* to support efficient list join, indexing, and access to both ends.

To work with very large trees, Glas systems will offload subtrees into content-addressed storage. I call this pattern *stowage*. Of course, Glas applications may also use effects to interact with external storage. Stowage has a benefit of working nicely with pure computations, serves as a virtual memory and compression layer, and has several benefits for incremental computation and communication. Stowage is guided by program annotations.

## Binary Extraction

Binary extraction replaces the conventional command-line compiler. 

In concrete terms, a command line for binary extraction might be `glas print Value -p Printer`, where Value and Printer are references into the module system such as `std.print`, and Printer must represent a Glas program that uses limited effects. The Printer receives the Value as an input then is evaluated to produce a binary stream on stdout. The caller can redirect output to a file.

The binary in question could represent a jpeg image, PDF document, zipfile, or executable. With suitable printers, it might also represent a summary such as program arity or type, though for these roles it might be nicer to develop a new command.

In any case, the logic for producing a useful binary will be represented in the module system. The command-line tool may maintain a private cache for performance, but such byproducts should not be exposed to the client. Ideally, extracted binaries are deterministic based on module system state with no extraneous input from tooling or environment.

See also *Command Line* and *Data Printer* under *Application Models*.

## Glas Programs

Glas defines a standard program model designed for staging, composition, and compilation of programs, while being easy to interpret. This model is used when defining language modules, binary extraction, automated tests, and is suitable for transaction machine applications. However, it is possible to support many other program models within a mature Glas system via binary extraction or acceleration.

Glas programs are stack-based. Valid Glas programs have static stack arity, i.e. branches are balanced and loops are stack-invariant. To detect errors ahead-of-time where feasible, other ad-hoc type safety analyses are recommended but not required. In general, Glas programs are responsible for verifying other Glas programs. 

### Stack Operators

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
 * move top item from data stack to top of dip stack
 * run P
 * move top item from dip stack to top of data stack
* **swap** - switch the top two items on stack
* **data:V** - copy V to top of stack

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

*Note:* For modeling locally recursive functions, a language module compiler may model its own continuation stack as a recursive variant or list. This would be independent of the Glas program stack.

### Control Operators

* **seq:\[List, Of, Operators\]** - sequential composition of operators. Empty sequence serves as nop.
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat. Otherwise, terminate loop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail

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
        loop:(while:P, do:Q)    (as LOOP)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S
            -------------------
            LOOP ⊲ S → S
        eq : ∀S,A,B . ((S * A) * B) → S | FAIL
        fail : ∀S . S → FAIL

User syntax can extend the effective set of control operators, e.g. compiling a mutually recursive function group into a central loop. 

Glas does not directly support higher-order programming, i.e. there is no built-in 'eval' operator. However, it is feasible to write an 'eval' function. Glas systems can potentially accelerate eval, support JIT via memoization, but the short-term expectation is to leverage a slow eval via language modules that support staged metaprogramming. 

### Effects Handler

* **env:(do:P, with:E)** - override effects handler in context of P.
 * move top item from data stack to eff stack
 * where `eff` is invoked within P (modulo further override):
  * move top item from eff stack to top of data stack
  * run E
  * move top item from data stack to top of eff stack
 * move top item from eff stack to data stack
* **eff** - invoke current effects handler on current stack.

The top-level effects handler is provided by runtime or compiler. Use of 'env' enables a program to sandbox a subprogram to monitor or control access to effects. At least for bootstrap and early development, effects handlers should have type `Request State -- Response State`. Later, with sufficiently advanced static analysis, it is feasible for 'eff' to be dependently typed with variable arity.

    env:(do:P, with:E)           (as ENV)
        E ⊲ (Sf * State) → (Sf' * State)
        eff ⊲ Sf → Sf' ⊢ P ⊲ S → S'
        --------------------------------
        ENV ⊲ (S * State) → (S' * State)
    eff         type determined by env

A design feature of env/eff is that the handler code can be inlined and partially evaluated by a compiler. Effective support for partial evaluation will mitigate branching overheads, and top-level effects can be baked into an executable or eliminated if unused.

*Note:* I decided against named effects because it's difficult to intercept, restrict, and delegate. However, partial evaluation can serve a similar role as named effects.

### Record and Variant Operators

A set of operations useful for records and variants. These are essentially the only data operations in Glas programs; everything else is constructed from them and conditional/loop ops. Pair and sum types can also be supported via one-bit labels. 

If programs use pair and sum types, they'll also use these operators just with one-bit labels. Fixed-width labels can be useful for an intmap. But in Glas, most labels encode null-terminated text.

Operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not in record.
* **put** (V (label?_|R) label -- (label:V|R)) - given a label, record, and value on the data stack, create new record with the given label associated with the given value. Will replace existing label in record.
* **del** ((label?_|R) label -- R) - remove label from record. Equivalent to adding label then removing it except for any prefix shared with other labels in the record.

Variants are essentially singleton records. The distinction between records and variants will mostly be handled during by static analysis instead of runtime ops.

### Annotations Operators

Annotations support performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), automated testing, debugging (tracing, profiling, assertions, breakpoints), decompilation, and other external tooling. However, annotations should not be directly observable within a program's evaluation. They might be indirectly observable via reflection effects (e.g. performance is reflected in timing, and assertion failures or traces might be visible via special log).

* **prog:(do:P, ...)** - runs program P. All fields other than 'do' are annotations. 

The set of annotations is openly extensible and subject to de-facto standardization. If a Glas compiler or interpreter encounters any annotations it does not recognize, it can log a warning then ignore. Some annotations in use:

* *accel:Model* - accelerate the program. The model is often a symbol indicating that the program implements a specific accelerated function that the compiler should recognize. However, more general models are feasible.
* *arity:(i:Nat, o:Nat)* - effective arity, usually based on a program *before* it was optimized. This may be checked, ignored, or assumed to be correct depending on context.
* *eff:(arity?Arity)* - Presence of 'eff' indicates the subprogram is impure. May be augmented by 'arity' if not the typical 1--1.

The 'prog' header also serves as the primary variant for programs within a *Dictionary* value.

### List, Arithmetic, Bitwise Operators, Etc..

I've dropped most Glas program operators on data representations (modulo records). Instead, the idea is to implement these functions within Glas then annotate for *Acceleration*.

## Bootstrap Syntax

Glas requires an initial syntax for bootstrap. To serve this role, I define [the g0 language](GlasZero.md). The g0 language is essentially a Forth variant with staged metaprogramming, algebraic effects, and immutable tree-structured data. The g0 language should be bootstrapped early such that outputs extracted from the Glas system depend only on state of the module system, not on external tooling. Other language modules will ultimately be defined in terms of g0.

## Application Models

### Language Modules

Language modules have a module name of form `language-(ext)`, binding to files with extension `.(ext)`. The language module shall comile to a record value of form `(compile:Program, ...)`. Aside from 'compile', other properties may provide description, documentation, linters, decompiler, code completion support, [language server](https://en.wikipedia.org/wiki/Language_Server_Protocol) support, REPL support, etc..

The compile program must have arity 1--1 and implements a function from source (usually a file binary) to a compiled value on the stack. The compile program can also access other module values and generate some log outputs. Effects API:

* **load:ModuleID** - Modules are currently identified by UTF-8 strings such as `"foo"`. File extension is elided. We search for the named module locally then on `GLAS_PATH`. 
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports, debugging, code change proposals, etc.. 

Load failures may occur due to missing modules, ambiguous files (e.g. if we have both `foo.g0` and subdirectory `foo/`), dependency cyles, failure of the compiler function, etc. A compiler can continue with load failures. Cause of failure should implicitly be logged for access by the developers.

A language may expose these effects to the programmer in context of compile-time metaprogramming. For example, these effects are explicitly supported by language-g0 macro calls.

### Command Line

The Glas command line interface is oriented around a glas executable with user-defined verbs.

        glas (verb) Parameters ...

The glas command line interface is extensible by defining modules with name `glas-cli-(verb)`. This module shall compile to a record value of form `(run:Program, ...)`. Aside from 'run' this record may include properties to support help messages, tab completion, effects API version, etc..

The glas executable must know enough to bootstrap the g0 language, compile language modules and dependencies, then eventually compile the verb. Logically, this process is performed every time the executable runs. However, for performance, the executable should privately cache computations to avoid unnecessary rework. The executable can provide fallback implementations for critical verbs such as 'help' and 'print'.

Essentially, the glas executable provides a runtime environment for its verbs.

The program should be arity 1--0, receiving Parameters as a list of strings on the data stack then mostly interacting with the world via the *Console Applications* effects API described in [Glas Apps](GlasApps.md), albeit extended with module loading to leverage the executable's bootstrap effort and private cache.

### Data Printer 

Relating to *Binary Extraction*. Printers must have arity 1--0 and implement a function from a printed value to a written binary. The output is written as an stdout stream to simplify production of very large binaries. Log messages are a secondary output, useful for progress or debugging, and are normally written to stderr. Effects API:

* **write:Binary** - Write binary data (a list of bytes) to stdout. Response is unit. Fails if argument is not a binary.
* **log:Message** - Arbitrary log message. Texts will usually be written to stderr. 

Writes at the top-level are immediate unless under a 'try' or 'while' clause. Thus, it is possible to produce several megabytes of data before failing, or to buffer everything and print at the end, depending on how the printer is defined.

### Transaction Machines

Transaction machines have many benefits as an application architecture. They are an excellent foundation for my visions of live coding reactive systems. I describe this concept further in the [Glas Apps](GlasApps.md) document. 

Glas programs use backtracking conditionals: every 'try' or 'while' clause is a transaction. A long-running top-level 0--0 arity loop of form `loop:(while:A, do:cond:try:B)` can run as a transaction machine. Annotations can indicate which transaction machine optimizations are expected. It is feasible to extend to higher arity by compiling the data stack into a collection of fine-grained transaction variables.

Conveniently, a distinct evaluation mode is not required. We only need support for optimizations and a suitable effects API. A Glas command line verb could support transaction machines with live coding by loading Glas modules within the loop and implicitly watching for changes to loaded modules.

### Automated Testing

Static assertions within modules are very useful for automated testing. However, build-time tests are deterministic and under pressure to resolve swiftly. There leaves open a niche for long-running or non-deterministic tests, such as fuzz-testing.

To support this, we can express tests as arity 0--Any Glas programs with access to 'fork' effect for non-deterministic choice input. 

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' single-edge bitstring.
* **log:Message** - Response is unit. Write an arbitrary message to support debugging of tests.

Non-deterministic choice doesn't mean random choice. A good test system should support incremental computing with backtracking on fork choices, and apply heuristics, memory, and program analysis to focus attention on forks that are more likely to discover a failed test.

The primary output from a test is pass/fail of evaluation. Log messages are a secondary output mostly to support debugging of failed tests.

A glas command line verb could support automated testing based on this simplified effects API to encourage simulation of effects and guarantee reproducibility of failures (assuming we record the fork path).

## Performance

### Stowage via Content-Addressed Storage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs can use annotations to guide use of stowage. Stowage may also be implicit during garbage collection. Use of stowage is not observable within the Glas program except indirectly via reflection on performance and use of space.

*Note:* Deduplication is [security sensitive](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html). If we must secure the content, we will want to add a cryptographic salt before encryption.

### Staged Metaprogramming and Partial Evaluation

Glas has implicit, pervasive staging via its module system. Additionally, language modules can provide staging within a module, assuming the language includes a program interpreter. The g0 language, used in bootstrap, has support for staged metaprogramming via macros and an export function.

Staging supports manual partial evaluation. More implicitly, the Glas program model is also designed to support partial evaluation: Linking is static. Stack allocation is static. It is feasible to distinguish static record fields. Effects handlers can be inlined and partially applied, including the top-level compiler-provided handler.

Further, annotations can indicate where partial evaluation is assumed so we can properly warn programmers when it fails.

### Acceleration

Acceleration is an optimization pattern. The general idea to annotate subprograms for accelerated evaluation, then a compiler or interpreter should recognize the annotation and silently substitute an optimized implementation called an accelerator. If the acceleration is not supported, the compiler raises warnings to prevent silent performance degradation.

Abstract CPUs are a useful pattern for acceleration. Instead of accelerating twenty floating-point math functions independently, accelerate evaluation of a bytecode for an abstract CPU that features floating-point registers. Use the bytecode to implement those twenty functions, and perhaps many more. The accelerator may optionally require that bytecode is a static parameter support ahead-of-time compilation or non-local evaluation on a GPGPU.

The compiler or interpreter may provide specialized data representations to mitigate data conversion and validation overheads when moving data between accelerated operations. Type annotations can guard against accidental data conversions. Thus accelerators essentially extend their host with new performance primitives - functions and types both - without complicating formal semantics. 

The cost of acceleration is implementation complexity, greater entanglement with the compiler, and related risks to correctness, security, and portability. Nonetheless, this tradeoff is worthwhile when it enables Glas to be used in problem domains where its performance is otherwise unacceptable. Glas will rely on acceleration to effectively support compression, cryptography, image rendering, machine learning, physics simulations, and many other problem domains. 

*Aside:* It is acceptable to use unchecked accelerators, e.g. `prog:(do:fail, accel:list-append)`, for short-term development. Not recommended for long-term use. The reference implementation is valuable for verification of the compiler, user-defined transpilation, etc..

### Distributed Computation

For computation at larger scales, it is feasible to *accelerate* evaluation of observably deterministic concurrency models such as [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) or [Lafont Interaction Nets](https://en.wikipedia.org/wiki/Interaction_nets). The accelerator would distribute the computation across multiple processors, perhaps across a mesh network. Some processes within the network would have access to other accelerators. 

Accelerated subprograms don't need to be pure. However, in context of a distributed systems, channeling all effects through the 'eff' operator is awkward and easily becomes a synchronization bottleneck. Fortunately, for build-time computations (e.g. language modules, data printers) the effects are very limited and this is unlikely to become a problem. If we want concurrent external interaction at runtime, we will solve it in the application model.

## Thoughts

### Language Compatibility

Modulo export function, default output from a g0 module is a record of form `(swap:prog:(do:swap, ...), try-then-else:macro:Program, pi:data:Value, ...)`, representing a dictionary of definitions. Each entry in the record is a word:deftype:Def triple, with symbolic words and deftype. The enables g0 to call macros and programs the same way, without syntactic distinction at the call site.

It would be convenient if most languages adopt a convention of compiling compatible dictionaries. As needed, languages could extend the set of deftype or reserve certain symbols for multi-method lookup tables or other aggregators.

## Logging Conventions

Almost every application model will support a **log:Message** effect to support debugging. In general, we'll often want the ability to filter which messages we're seeing based on task, level, role, etc.. These properties are mostly independent of content. Instead of a variant type, log messages will normally use a record of ad-hoc fields, with de-facto standardization. For example:

        (lv:warn, role:debug, text:"English message")

This allows gradual and ad-hoc structured extension to log messages, e.g. with provenance metadata, without breaking existing routes or filters. It is feasible to extend log messages with images or even applets to better explain issues.

*Note:* Efficient logging with large values require support for structure sharing within the log storage, e.g. using the stowage model.

### Database Modules

It is feasible to design a language module that knows how to parse a structured binary or database file such as MySQL or LMDB or MessagePack. Such languages have several potential benefits. Databases are more scalable than text files. Support for multiple sublanguages is easier with databases instead of indenting or distinguishing texts. Databases are easier to tool with multiple views and visual programming.

A concern with database modules is that we'll often need to parse and 'compile' the whole database into a value for Glas modules. This will become more expensive as the database grows larger or is updated more frequently. This can be mitigated by memoization and by modular use of databases.

### Glas Object

The idea is a standard language for stowage and sharing of Glas values at a large scale. Even better if streamable for use in a network context.

See [Glas Object](GlasObject.md).

### Gradual Types

By default, Glas does lightweight static arity checkss, but there is no sophisticated type system built-in. Language modules can introduce their own type system, type annotations, and safety analyses. Thus, the type system is effectively user-defined.

Glas should work very well with gradual types. Access to program definitions as data enables type checks to be performed post-hoc, e.g. via assertions in a later module, without invasive modification of existing modules to add annotations. It is also feasible to overlay several type systems, with independent analyses. Memoization and stowage can mitigate redundant checks.

### Graph Based Data

I rejected general graphs as the basic data structure for Glas because trees are more simple and adequate for most use-cases. However, I think it's still a good idea to develop some language modules around the observation and manipulation of abstract graphs. We could implement and eventually accelerate these languages.

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program. For example, each program component would declare constraint variables, describe how to compute a result (or fail) based on the values of those variables, and describe potential ways to assign those variables. Separately, we search for solutions. Glas provides a viable foundation, though the g0 syntax is not suitable for this role.

A challenge will be achieving effective performance from search in context of deterministic computations. Memoization may help if applied carefully.

### Provenance Tracking

I'm very interested in automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. I still don't have a good idea about how to approach this without huge overheads.
