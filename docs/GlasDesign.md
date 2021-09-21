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

Glas systems encode labels with 'bitstrings', which are long sequences of nodes with only one edge. The label is encoded into the bitstring using UTF-8 with a null terminator. For example, symbol 'path' is represented by the bitstring `01110000 01100001 01110100 01101000 00000000`. A 'symbol' is just the label by itself, but in general we can follow the null terminator with an arbitrary value. Because long bitstrings are very common in Glas systems, we compact bitstrings.

        type BitString = (space-optimized) Bool list
        type T1 = BitString * (1 + (T1*T1))
        // A tree is a bitstring that ends either in unit or a fork.
        // T1 is equivalent to T0 but more efficient for bitstrings.

Labeled products, aka records, are then represented by a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). Common label prefixes will overlap, and the associated value immediately follows the label's null terminator. Labeled sums, or variants, are essentially singleton records. 

Glas systems usually encode natural numbers as bitstrings in MSB to LSB order. For example, the bitstring `10111` represents the number 23. In many cases, we'll favor fixed-width numeric representations, e.g. `00010111` encodes 23 as an 8-bit byte. The standard Glas program model only has operators for natural numbers, but we could also use bitstrings to encode floats and other numeric types. Intmaps, sparse arrays, hashtables, etc. can usefully be encoded as radix trees indexed by fixed-width numbers.

Bitstrings are only used for small things. Glas uses lists to encode general sequential structures. Logically, a list is `type List a = (a * List a) | 1`, i.e. a list is constructed of `(Head * Tail)` pairs and terminated with unit `()`. (This encoding is not an algebraic sum type.) However, performance of the direct list representation is awkward for many use-cases. Glas systems tend to use a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation for lists, supporting efficient indexing, split, append, and access to both endpoints.

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

Glas defines a standard program model designed for staging, composition, and compilation of programs, while being easy to interpret. This model is used when defining language modules, streaming binaries, automated tests, and is suitable for transaction machine applications. However, it is possible to support many other program models within a mature Glas system via binary extraction or acceleration.

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

A linked list has simple structure, but terrible performance for any use-case except a stack. To solve this, Glas provides a few dedicated operators for lists. This enables the runtime or compiler to transparently substitute a more efficient representation such as a [finger tree](https://en.wikipedia.org/wiki/Finger_tree).

* **pushl** (L V -- V:L) - given value and list, add value to left (head) of list
* **popl** (V:L -- L V) - given a non-empty list, split into head and tail. 
* **pushr** (L V -- L:V) - given value and list, add value to right of list
* **popr** (L:V -- L V) - given non-empty list, split into last element and everything else
* **join** (L1 L2 -- L1+L2) - appends list at top of data stack to the list below it
* **split** (Lm+Ln |Lm| -- Lm Ln) - given number and list of at least that many elements, produce a pair of sub-lists such that join produces the original list, and the head portion has requested number of elements. Fails if this is not possible.
* **len** (L -- |L|) - given list, return a number that represents length of list. Uses smallest encoding of number (i.e. no zeroes prefix).

These operators assume a simplistic, Lisp-like representation of lists: `type List = () | (Value * List)`. That is, each list node has either no edges or two edges. In a non-empty list node, edge 0 refers to the head value and edge 1 to the remaining list.

### Bitstring Operators

A bitstring is a specialized list of bits represented by a non-branching path, e.g. `01100101` is a string of 8 bits. Empty bitstrings are permitted. Glas uses bitstrings to represent bytes and 'machine' words, natural numbers, and as a path parameter when constructing records.

List operators:

* **bjoin** - as list join, but for bitstrings
* **bsplit** - as list split, but for bitstrings
* **blen** - as list len, but for bitstrings

Bitwise operators:

* **bneg** - bitwise 'not'; flip all bits of one bitstring
* **bmax** - bitwise 'or' of two bitstrings of equal length
* **bmin** - bitwise 'and' of two bitstrings of equal length
* **beq** - bitwise equivalence of two bitstrings of equal length, i.e. 1 where bits match, 0 otherwise. (Negation of 'xor'.)

Bitstring operators fail if assumed conditions are not met, i.e. not a bitstring or not of equal length. Unlike lists, the assumption for bitstrings is that they are relatively small and Glas systems should optimize for compact or fixed-width encodings instead of efficient access to both ends of a large bitstring.

*Aside:* We probably don't need bitwise ops; they're essentially extensions of the arithmetic ops.

### Arithmetic Operators

Glas encodes natural numbers into bitstrings assuming msb-to-lsb order. For example, `00010111` encodes 23. Although the zeroes prefix `000` doesn't contribute to numeric value, Glas arithmetic operators preserve bitstring field widths. For example, subtracting 17 results in `00000110`.

* **add** (N1 N2 -- Sum Carry) - compute sum and carry for two numbers on stack. Sum has field width of N1, carry has field width of N2. Carry can be larger than 1 iff N2 is wider than N1 (e.g. adding 32-bit number to an 8-bit number).
* **mul** (N1 N2 -- Prod Overflow) - compute product of two numbers on stack. Product has field width of N1, while overflow uses field width of N2. 
* **sub** (N1 N2 -- Diff) - Computes a non-negative difference (N1 - N2), preserving field width of N1. Fails if result would be negative. This also serves as the comparison operator for natural numbers.
* **div** (Dividend Divisor -- Quotient Remainder) - If divisor is zero, this operator fails. Compute division of two numbers. Quotient has field width of dividend, and remainder has field width of divisor.

Glas doesn't have built-in support for negative numbers, floating point, etc.. Extending arithmetic will benefit from *Acceleration*.

### Annotations Operators

Annotations support performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), automated testing, debugging (tracing, profiling, assertions, breakpoints), decompilation, and other external tooling. However, annotations should not be directly observable within a program's evaluation. They might be indirectly observable via reflection effects (e.g. performance is reflected in timing, and assertion failures or traces might be visible via special log).

* **prog:(do:P, ...)** - runs program P. All fields other than 'do' are annotations. 

The set of annotations is openly extensible and subject to de-facto standardization. If a Glas compiler or interpreter encounters any annotations it does not recognize, it can log a warning then ignore. 

The 'prog' header also serves as the primary variant for programs within a *Dictionary* value.

## Glas Initial Syntax (g0)

Glas requires an initial syntax for bootstrap. To serve this role, I define [the g0 language](GlasZero.md), which is essentially a Forth with some decent metaprogramming features and algebraic effects.

N

 However, g0 is a rather simplistic language, with a Forth-like look and feel but lacking Forth's metaprogramming features. The intention is to simplify bootstrap implementation. A more sophisticated language modules (with support for local variables, recursive function groups, metaprogramming, etc.) should be developed for normal programming in Glas. 


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

The command-line tool will support serialization of module system values to binary. A data printer is an arity 1--0 function with an effect to write binary data fragments. Additionally, the printer may output log messages. In common use, binary data is written to stdout and log messages are written to stderr.

* **write:Binary** - addend binary data - a list of bytes - to the output stream. Response is unit, or fails if argument is not valid binary.
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

Printers are usually not interpreted atomically at the top level. It's possible to write several megabytes before a program fails. A failure will usually be reported by exit code. Of course, we could write within a `try` clause whenever we want atomicity. 

### Automated Testing

Language modules implicitly support ad-hoc deterministic testing, such as static assertions and unit tests. It is feasible to simulate non-determinism. However, for robust testing of general simulations, it is useful to expose non-deterministic choice to the test system. This program would be evaluated with no inputs. Effects API:

* **log:Message** - output arbitrary message to log for debugging purposes.
* **fork:Count** - read and return a random bitstring of Count bit length.

Exposing non-determinism with 'fork' supports incremental computing with backtracking or use of static analysis and heuristics to focus on cases that have a chance of failing. Glas system tooling should be able to compile modules whose names start with `test` to obtain a zero-arity `(test:Program, ...)` that is evaluated with access to 'fork'. 

### User Applications

With backtracking conditionals, Glas programs are a good fit transaction machines, i.e. where an application is represented by a transaction that the system will run repeatedly. Transaction machines have many benefits and are an excellent fit for my long-term vision of live coding and reactive systems. I'm developing this idea in the [Glas Apps](GlasApps.md) document. 

The Glas command-line tool should provide an option to run a console application without requiring full compilation and binary extraction. Whether this uses an interpreter or JIT compiler would be left to the tool.

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

### Partial Evaluation

Glas is designed to simplify partial evaluation: Linking is static. Stack allocation is static. Static record paths are readily distinguishable from dynamic record fields. Effects handlers can be inlined and partially applied, including a top-level compiler-provided handler. 

However, partial evaluation is fragile unless supported by annotations. Robust partial evaluation benefits from type-system hints: we can require certain record fields or stack elements to be statically computable. Conveniently, it is easy to verify partial evaluation - we need only to try partial evaluation and see. 

### Staged Metaprogramming

Glas has implicit, pervasive staging via its module system. Additionally, language modules can provide staging within a module, assuming the language includes a program interpreter. The g0 language, used in bootstrap, has support for staged metaprogramming via macros and an export function.

### Acceleration

Acceleration is an optimization pattern where we replace an inefficient reference implementation of a function with an optimized implementation. Accelerated code could leverage hardware resources that are difficult to use within normal Glas programs.

For example, we can design a program model for GPGPU, taking notes from OpenCL, CUDA, or Haskell's [Accelerate](https://hackage.haskell.org/package/accelerate). This language should provide a safe (local, deterministic) subset of features available on most GPGPUs. A reference implementation can be implemented using the Glas program model. A hardware-optimized implementation can be developed and validated against the reference.

Or we design a program model based on Kahn Process Networks. The accelerated implementation could support distributed, parallel, stream-processing computations. The main cost is limiting use of the effects handler.

Accelerators are a high-risk investment because there are portability, security, and consistency concerns for the accelerated implementation. Ability to fuzz-test against a reference is useful for detecting flaws - in this sense, accelerators are better than primitives because they have an executable specification. Carefully selected accelerators can be worth the risk, extending Glas to new problem domains.

To simplify recognition and to resist invisible performance degradation, acceleration must be explicitly annotated via `prog:(do:Program, accel:(...))`. This allows the compiler or interpreter to report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target). 

*Aside:* It is feasible to accelerate subsets of programs that conform to a common structure, thus a model hint could be broader than identifying a specific accelerator.

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

It is feasible to design a language module that knows how to 'parse' a database file such as MySQL or LMDB. Developing a few such language modules could simplify tooling for working with large amounts of small to medium sized data.

### Multi-Language Modules

A language module can explicitly load other language modules to parse certain regions of code into useful values. This is awkward with text-based languages because we must demark sections (e.g. using indentation), but it should well together with database modules. 

### Lazy Evaluation

Laziness is difficult to model in context of effects handlers and backtracking failure. However, it is feasible to model it explicitly and observably, e.g. computing partial data and continuations. Doing so may be worthwhile in certain contexts.

### Glas Object

The idea is a standard language for stowage and sharing of Glas values at a large scale. Even better if streamable for use in a network context.

See [Glas Object](GlasObject.md).

### Gradual Types

By default, Glas does lightweight static arity checks, but there is no sophisticated type system built-in unless language modules introduce one post-bootstrap. Thus, the Glas type system is ultimately user-defined within the module system.

Glas should work very well with gradual types. Access to program definitions as data enables type checks to be applied post-hoc without invasive modification of existing modules to add annotations. Use of annotations can potentially support several types of types based on different purposes or perspectives. Memoization can provide adequate performance.


### Graph Based Data

I've considered using graph based data structures instead of tree based. Similar to the current model, each node in a graph has at most two outbound edges, labeled 0 or 1. Complex labels would be formed by an arc through the graph. The difference is that we can also form directed cycles within this graph. 

I decided against graphs because they are relatively awkward to manipulate with using conventional operators in a purely functional style. Also, most cyclic phenomena is actually fractal or temporal, so the ability for finite graph data to usefully model the phenomena is limited.

Of course, we can still model graph data indirectly using adjacency lists, matrices, DSL programs that construct graphs, or other non-graph structures. And it might be worthwhile to develop some accelerated models for working with graphs when Glas is a little more mature.

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas provides a viable foundation, but it's still a distant future. Part of the solution will certainly involve loading 'catalog' modules that index other modules to support search.

Because search in context of Glas modules will be deterministic, we cannot rely on non-determinism to learn or stabilize heuristic choices. A challenge will be effective memoization and caching to avoid unnecessary recomputation of choices across minor changes in source.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this across multiple layers of language modules and metaprogramming. One idea is to keep some source metadata with every bit that can be traced back to its few 'best' sources. This seems very expensive, but viable.


