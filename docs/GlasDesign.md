# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary using a program defined in module `language-ext`. Most syntax is user-defined in the module system excepting [g0](GlasZero.md) for bootstrapping. File extensions compose. For example, to compute the value for `foo.xyz.json` we first apply `language-json` to compute an intermediate value, then apply `language-xyz` to compute the value of the `foo` module. If a file has no extension, its value is the file binary. Files and folders whose names start with `.` are hidden from the Glas module system.

To compute the value for a folder `foo/`, we use the value of its contained `public` file. Folders are implicit boundaries for dependencies: a file can only reference other modules (files or subfolders) within the same folder, or reference global modules.

Global modules are found using the GLAS_PATH environment variable, whose value should be a list of folders separated by semicolons. If there is no local module with a given name, we'll search for the first matching module on GLAS_PATH. It's best that all modules on GLAS_PATH are subfolders. Later, we might extend module search to a configurable distribution on the network.

*Note:* Glas does not specify a package manager. We can start with Nix or Guix, then later develop something more specialized. 

## Data Model

Glas data is modeled as immutable binary trees. Each node may have up to two edges, uniquely labeled 0 and 1, to subtrees. A naive representation is:

        type T0 = ((1+T0) * (1+T0))
        // A tree is a node with two optional, distinct edges to subtrees.

This trivially supports algebraic products (pairs with both edges), sums (choice of either edge), and the unit value (no edges). However, Glas encourages use of labeled data structures instead of basic products and sums. Labels greatly improve extensibility and documentation of data.

Glas systems encode labels with 'bitstrings', which are long sequences of nodes with zero or one edge. The label is encoded into the bitstring using UTF-8 with a null terminator. For example, symbol 'path' is represented by the bitstring `01110000 01100001 01110100 01101000 00000000`. A 'symbol' is just the label by itself, but in general we can follow the null terminator with an arbitrary value. Because long bitstrings are very common in Glas systems, we compact bitstrings.

        type BitString = (space-optimized) Bool list
        type T1 = BitString * (1 + (T1*T1))
        // A tree is a bitstring that ends either in unit or a fork.
        // T1 is equivalent to T0 but more efficient for bitstrings.

Labeled products, aka records, are then represented by a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). Common label prefixes will overlap, and the associated value immediately follows the label's null terminator. Labeled sums, or variants, are essentially singleton records. 

Bytes are encoded as fixed-width bitstrings, msb to lsb, e.g. `00010111` is an 8-bit byte. Natural numbers are encoded as bitstrings of variable width, eliding the '0' prefix.

Glas uses lists to encode general sequential structures. Logically, a list is `type List a = (a * List a) | ()`, i.e. a list is constructed of `(Head * Tail)` pairs and terminated with unit `()`. This encoding is not an algebraic sum type; it requires distinguishing unit values. For performance, Glas systems may implicitly use a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation for lists, supporting efficient indexing, split, append, and access to both endpoints.

        type T2 = BitString * (1 + (T2 * FingerTree<T2> * NonPairT2))
        type NonPairT2 = 1 + (T2 + T2) // unit, left tree, or right tree.
        // A tree is a bitstring that ends either in unit or a list-like structure 
        // with at least two items. An actual list ends in unit, but the list-like
        // structure might end in a node with a single edge.
        //
        // T2 is equivalent to T1 but more efficient for ad-hoc list manipulations. 

Glas systems represent strings and binaries as lists of bytes, favoring the UTF-8 encoding for strings. It is feasible to further optimize via compact encoding of binary fragments (cf. [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29)). In general, Glas systems have much freedom to optimize representations so long as the details are abstracted. For example, records with a few statically known labels can be optimized to C-like structs at runtime. 

To work with larger-than-memory data structures, Glas systems may offload subtrees to content-addressed storage, then lazily load data into memory as needed or anticipated. I call this pattern *Stowage*. In addition to serving as a virtual memory and large scale structure sharing compression layer for immutable data, stowage has benefits for incremental computation and communication.

*Aside:* Data in Glas has a low probability of sharing representations by accident. The empty list, empty record, min-width zero, and unit do overlap (and not by accident). But probability of collision for non-empty lists, records, symbols, and numbers is very small. Tools could heuristically render Glas data and support human editing without much difficulty even without context of a known data type.

## Binary Extraction

The Glas command-line tool shall provide a simple option to print module system values as binaries to file or stdout. This is concretely expressed by command-line arguments `print Value with Printer`, where `Value` and `Printer` both have the form `modulename(.symbol)*` allowing access into labeled records and lists. The printer's value must represent a valid *Data Printer* function (see below). If a printer is unspecified, we implicitly use `std.print`. 

Printers can extract externally useful binaries from the module system. For example, we could 'print' a document, streaming music, or an executable binary. Multi-file outputs can be represented indirectly by printing a tar or zip file. Printing values is also useful for REPL-style development and debugging of the module system. 

In Glas systems, extraction of binary executables replaces the conventional command-line compiler tools. To adapt executables for a host system, it is feasible to depend on a `target` module that describes OS and machine architecture. This target could feasibly be adjusted for cross-compilation via tweaking GLAS_PATH.

A consequence of this design is that the compiler logic is fully accessible within the module system. All binaries that artifacts that can be constructed externally can be constructed internally within the module system.

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

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * **seq:\[\]** - empty sequence doubles as identity operator (nop)
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, backtrack P then run R.
* **loop:(while:P, do:Q)** - begin loop: run P; if P does not fail, run Q then repeat loop. If P fails, backtrack P then exit loop.
* **eq** - Structural equality of values. Takes top two items from data stack. If they are identical, continue (with those items removed from stack), otherwise fail.
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
        eq : ∀S,A,B . ((S * A) * B) → ((S * B) * B) | FAIL
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

A set of operations useful for records and variants. Pair and sum types are also supported by these operators as a trivial case using one-bit labels. However, Glas systems don't favor use of pairs and sums because they lack convenient extensibility. 

Labels are encoded into bitstrings, usually as null-terminated UTF-8 text. Within a record, label bitstrings must exhibit the prefix property: no valid label is a prefix of another valid label. Other than null terminators, this could be supported by fixed-width label encodings, e.g. forming an intmap.

Record operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not in record.
* **put** ((label?_|R) V label -- (label:V|R)) - given a label, value, and record, create new record that is almost the same except with value on label.
* **del** ((label?_|R) label -- R) - given label and record, create new record with label fully removed modulo prefix sharing with other paths in record. 

A record may have many labels, sharing prefixes. Non-branching path segments can be compactly encoded to reduce the number of pointers. It is feasible for a compiler to optimize records with statically known labels like a C struct. A variant is a singleton record. 

### List Operators

The basic linked list is a simple structure but has awful performance for any use-case except a data stack. To solve this, Glas systems should transparently represent lists using a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation, at least by default, allowing efficient access to both ends and to split ops. This allows immutable lists to be used as arrays or double-ended queues.

* **pushl** (L V -- V:L) - given value and list, add value to left (head) of list
* **popl** (V:L -- L V) - given a non-empty list, split into head and tail. 
* **pushr** (L V -- L:V) - given value and list, add value to right of list
* **popr** (L:V -- L V) - given non-empty list, split into last element and everything else
* **join** (L1 L2 -- L1+L2) - appends list at top of data stack to the list below it
* **split** (Lm+Ln |Lm| -- Lm Ln) - given number and list of at least that length, produce a pair of sub-lists such that join produces the original list, and the first slice has requested number of elements. Fails if this is not possible.
* **len** (L -- |L|) - given list, return a number that represents length of list. 

These operators assume a simplistic, Lisp-like representation of lists: `type List = () | (Value * List)`. That is, each list node has either no edges or two edges. In a non-empty list node, edge 0 refers to the head value and edge 1 to the remaining list.

### Arithmetic Operators

Glas encodes natural numbers (0, 1, 2, ...) in base-2 as bitstrings, msb to lsb order, using the fewest possible bits. For example, `10111` encodes 23, but `00010111` is not a valid number because it has an unnecessary zeroes prefix. If input to an arithmetic operator is not a valid number, the operator will fail. Consequently, programmers must explicitly convert between bytes and numbers.

* **add** (N1 N2 -- Sum) - compute sum of two numbers on stack.
* **mul** (N1 N2 -- Prod) - compute product of two numbers on stack.
* **sub** (N1 N2 -- Diff) - computes non-negative difference (N1 - N2). Fails if N2 > N1.
* **div** (Dividend Divisor -- Quotient Remainder) - computes number of times that divisor can be subtracted from dividend, and remaining value from the dividend. Fails if divisor is zero.

Glas is not optimized for number processing, but does provide convenient access to basic bignum arithmetic. It should not be too difficult for programmers to model rational or scientific numbers. High performance number processing will certainly rely on accelerators.

### Annotations Operators

Annotations support performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), automated testing, debugging (tracing, profiling, assertions, breakpoints), decompilation, and other external tooling. However, annotations should not be directly observable within a program's evaluation. They might be indirectly observable via reflection effects (e.g. performance is reflected in timing, and assertion failures or traces might be visible via special log).

* **prog:(do:P, ...)** - runs program P. All fields other than 'do' are annotations. 

The set of annotations is openly extensible and subject to de-facto standardization. If a Glas compiler or interpreter encounters any annotations it does not recognize, it can log a warning then ignore. 

The 'prog' header also serves as the primary variant for programs within a *Dictionary* value.

## Bootstrap Syntax

Glas requires an initial syntax for bootstrap. To serve this role, I define [the g0 language](GlasZero.md). The g0 language is essentially a Forth variant with staged metaprogramming, algebraic effects, and immutable tree-structured data. The g0 language should be bootstrapped early such that outputs extracted from the Glas system depend only on state of the module system, not on external tooling. Other language modules will ultimately be defined in terms of g0.

## Application Models

We can model various apps with Glas programs by controlling input, output, and effect types.

### Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a record of form `(compile:Program, ...)`. Aside from 'compile', the record for a language module may define other ad-hoc properties - documentation, linter, decompiler, language server app, REPL mode, etc..

The compile program implements a function from source (e.g. file binary) to a compiled value on the stack. The compile program can also access other module values and generate some log outputs. Effects API:

* **load:ModuleID** - Modules are currently identified by UTF-8 strings such as `"foo"`. File extension is elided. We search for the named module locally then on `GLAS_PATH`. 
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports, debugging, code change proposals, etc.. 

Load failures may occur due to missing modules, ambiguous files (e.g. if we have both `foo.g0` and subdirectory `foo/`), dependency cyles, failure of the compiler function, etc. A compiler can continue with load failures. Cause of failure should implicitly be logged for access by the developers.

A language may expose these effects to the programmer in context of compile-time metaprogramming. For example, these effects are explicitly supported by language-g0 macro calls.

### Data Printer 

A viable command-line interface for extraction of binary data from the Glas module system is `glas print (ValueRef) with (ValueRef)`. Here a ValueRef is a dotted path into the module system, such as `std.print`. The referenced printer should be an arity 1--0 function with an effect to write binary data fragments. Additionally, the printer may output log messages. In normal use, binary data will be written to stdout and log messages to stderr. Effects API:

* **write:Binary** - Write binary data (list of bytes) to stdout. Response is unit. Fails if argument is not binary.
* **log:Message** - Arbitrary log message to stderr. Useful for progress reports or debugging.

The printer will immediately write outputs unless it's within a 'try' or 'while' clause, in which case writes are buffered in case of failure. Thus, it is possible to produce several megabytes of data before failing, or to buffer everything and print at the end, depending on how the printer is defined.

### User Applications

With backtracking conditionals, Glas programs are a good fit transaction machines, i.e. where an application is represented by a transaction that the system will run repeatedly. Transaction machines have many benefits and are an excellent fit for my long-term vision of live coding and reactive systems. I'm developing this idea in the [Glas Apps](GlasApps.md) document. 

The Glas command-line tool should provide an option to run a console application without requiring full compilation and binary extraction. Whether this uses an interpreter or JIT compiler would be left to the tool.

### Automated Testing

Glas languages should support static assertions and other lightweight tests and checks. However, build-time tests are under pressure to resolve swiftly, and will tend to tread the same ground repeatedly. For long-running tests such as fuzz-testing, we need a different solution.

I propose to write *background tests* into log messages of form `(test:Program, ...)`. The test program should be 0--Any arity, and the primary outcome is pass/fail of evaluation. Supported effects are log and fork:

* **log:Message** - Response is unit. Write an arbitrary message to support debugging of tests.
* **fork** - Response is a non-deterministic boolean - a '0' or '1' bitstring.

Use of 'fork' can simulate race conditions or random inputs to a test. However, fork outcomes are not necessarily fair or random. A test system may use heuristics and program analysis to search for forks that are more likely to lead to test failure.

A failed background test would not directly prevent the system from running, but can be recorded to indicate health of the system to developers, and perhaps accessed via reflection on the system.

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

### Distributed Computation

For computation at larger scales, it is feasible to *accelerate* evaluation of observably deterministic concurrency models such as [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) or [Lafont Interaction Nets](https://en.wikipedia.org/wiki/Interaction_nets). The accelerator would distribute the computation across multiple processors, perhaps across a mesh network. 

Accelerated subprograms don't need to be pure. However, in context of a distributed systems, channeling all effects through the 'eff' operator is awkward and easily becomes a synchronization bottleneck. Fortunately, for build-time computations (e.g. language modules, data printers) the effects are very limited and this is unlikely to become a problem. If we need concurrent external interaction after build-time, we can solve it in the application model.

Build-time computations have minimal external effects - e.g. language modules (log and load) or data printers (log and write), thus distributed computations

 (log and load or log and write) this is unlikely to become an issue in general.

where effects are log and load, or printing binary data, this is unlikely to become a problem.

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

### Bitstring Operators? Dropped.

Each node in a bitstring has one or zero outbound edges, each labeled 0 or 1, forming a string such as `01100101` to represent a byte. Initially, I defined list ops for bitstrings, blen, bjoin, bsplit. Additionally, a few bitwise ops, e.g. bmax is bitwise-or, bmin is bitwise-and, bneg negates each bit, and beq is a negated bitwise-exclusive-or. Seven ops total.

However, in practice I almost never want these operators. The conventional uses of bitwise ops are for representing, observing, and manipulating flags, or possibly bit-banging for cryptography. In Glas systems, flags are instead represented as a record of labels, one label per flag. The list ops aren't very useful since I still need to construct or process bitstrings using loops. Cryptography needs acceleration anyways.

### Simplifying Arithmetic

Originally the arithmetic operators will preserve sizes of bitstring inputs, i.e. producing sum and carry or product and overflow. If we multiply a 16-bit number by a 32-bit number, then product is 16 bits and overflow is 32 bits. If we add an 8-bit number to a 32-bit number, sum is 8 bits and carry is 32 bits. Similarly, quotient and remainder would be sized matching dividend and divisor respectively. 

In theory, this could simplify static analysis of memory requirements. In practice, it is awkward to work around size constraints on numbers by default, especially knowing that lists don't have any particular size limit. I've decided to more simply use variable-size natural numbers in all cases. We can potentially defer fixed-width number processing to an accelerator.
