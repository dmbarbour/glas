# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary according to a program defined in a module named `language-ext`. Although there is a special exception for bootstrapping, most syntax will be user-defined. The value of a folder is the value of its contained `public` file.

File extensions compose. For example, to compute the value for `foo.xyz.json` we first apply `language-json` to the file binary to compute an intermediate value, then apply `language-xyz` to that intermediate value. Thus, source input for language modules can be arbitrary Glas values. Relatedly, if a file has no extension, its value is the file binary. Files and folders whose names start with `.` are hidden from the Glas module system.

In addition to local modules within a folder, a `GLAS_PATH` environment variable will support search for installed modules in the filesystem. It is feasible to further extend search to include network resources. 

*Note:* Glas does not specify a package manager. I favor package managers suitable for community management and reproducible builds, such as Nix or Guix. Later, we might design a manager optimized for Glas.

## Data Model

Glas data is modeled as immutable binary trees. Each node may have up to two edges, uniquely labeled 0 and 1, to subtrees. A naive representation is:

        type T = ((1+T) * (1+T))

This supports algebraic products (both edges), sums (choice of one edge), and the unit value (no edges). However, Glas encourages use of labeled data structures instead of basic products and sums. Labels greatly improve extensibility and documentation of data.

Glas can encode labels or symbols using 'bitstrings', which are long sequences of nodes with only one edge. For example, symbol 'path' is represented by the bitstring `01110000 01100001 01110100 01101000 00000000`, which simply encodes 'path' in UTF-8 with a null-terminator. For labeled products, aka records, the labels form a [radix tree](https://en.wikipedia.org/wiki/Radix_tree), with the associated value following the null terminator. Labeled sums, aka variants, are essentially singleton records. To support this heavy use of labels, the representation of trees in Glas systems will usually compact bitstring sequences.

Numbers are also encoded as bitstrings in msb to lsb order. Fixed-width numbers will literally be encoded as a bitstring of fixed width, e.g. `00010111` is an 8-bit representation of the number 23. There is no limit on the size of a number, nor for alignment to multiples of 8 bits, but performance of the implementation may degrade for large numbers.

Pairs are mostly used for lists. Lists are simply modeled as `type List a = (a * List a) | 1`, and are widely used for modeling sequential structure in Glas systems. For example, binaries are modeled as lists of bytes. To improve performance working with lists, Glas systems are expected to replace the simplistic linked-list representation with a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation. Finger tree lists support constant-time access to both ends and logarithmic inner manipulations.

To control memory resources, Glas systems may offload large subtrees to content-addressed storage. I call this pattern *Stowage* (see below), and also has benefits for incremental computation and communication. 

*Note:* There is no built-in support for rational numbers, signed numbers, floating point numbers, complex numbers, matrix and vector math, and so on. However, it is feasible to extend Glas systems to support performance of more types via *Acceleration*.

## Binary Extraction

A subset of Glas modules may define binary outputs. Common representations include a list of bytes or a program that writes a stream of binary fragments (see *Streaming Binaries* below). The Glas command-line tool will provide modes to extract common representations of binary data to file or stdout.  

An extracted binary should represent an externally useful software artifact - e.g. pdf, music file, an executable, a meme image, or a JavaScript program. To represent multi-file software artifacts, the binary can represent a tarball or zipfile which can be extracted by another tool.

Compilation in Glas is based on binary extraction: a 'compile' is essentially a module that specifies the executable. It is feasible to target binaries for specific machines by having the compiler depend on a `target` module, which might specify the local machine or be overridden in `GLAS_PATH` for cross-compilation.

An intriguing consequence is that compiled binaries remain accessible within the module system for further composition or automated testing.

## Glas Programs

Glas systems can support multiple program models, but a standard model is required for bootstrap of language modules. This program model is essentially an intermediate program representation for an abstract machine, focused on basic operation, composition, annotation, and simple interpretation. Many program features - such as named variables, keyword parameters, etc. - are left to the syntax layer. Performance is a significant long-term concern: it should be feasible to compile and accelerate language modules for expensive build-time computations.

This section describes the standard program model. The only required static check for Glas programs is verifying that stack arity is static, i.e. branches are balanced and loops are stack-invariant. 

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
* **eq** - compare top two values on stack. If equal, do nothing. Otherwise fail. This uses structural equality of values, not equality under an interpretation.
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
* **del** ((label?_|R) label -- R) - given label and record, create new record with label removed modulo prefix sharing with other paths in record. 

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

Bitstring operators fail if assumed conditions are not met, i.e. not a bitstring or not of equal length.

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

The 'prog' header also serves as the variant for programs within a namespace. Namespaces are often modeled as a record whose elements include programs, types, data, macros, and other constructs that programmers might define for reuse.

## Glas Initial Syntax (g0)

Glas requires an initial syntax for bootstrap. To serve this role, I define [the g0 syntax](GlasZero.md). However, g0 is a rather simplistic language, with a Forth-like look and feel but lacking Forth's metaprogramming features. The intention is to simplify bootstrap implementation. A more sophisticated language modules (with support for local variables, recursive function groups, metaprogramming, etc.) should be developed for normal programming in Glas. 

## Application Models

### Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a record of form `(compile:Program, ...)`. Aside from 'compile', the record for a language module may define other ad-hoc properties - documentation, linter, decompiler, language server app, REPL mode, etc..

The compile program implements a function from source (e.g. file binary) to a compiled value on the stack. The compile program can also access other module values and generate some log outputs. Effects API:

* **load:ModuleID** - Modules are currently identified by UTF-8 strings such as `"foo"`. File extension is elided. We search for the named module locally then on `GLAS_PATH`. 
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports, debugging, code change proposals, etc.. 

Load failure may occur due to missing modules, ambiguity, dependency cyles, failure by the compile program, etc. The compile program decides what to do after a load fails, e.g. fail the compile or continue with fallback. Cause of failure is not visible to the compile program but may implicitly be reported to the programmer.

### Streaming Binaries

It is frequently convenient to represent large or computed binaries as programs that generate a stream of binary fragments. A viable representation is a zero-arity program (no input or output on data stack) with a 'write' effect for outputting binary fragments. Effects API:

* **write:Binary** - addend a binary (a list of bytes) to the generated output stream. Response is unit, or fails if argument is not valid binary.
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports or debugging.

It is possible for a binary stream to fail after writing a few megabytes. Failure should be interpreted as a stream error, but exactly how this error is handled will depend on context.

*Aside:* [The g0 syntax](GlasZero.md) does not have direct access to static evaluation, thus will rely on streaming binaries for extraction. 

### Automated Testing

For lightweight automatic testing, Glas systems will support a convention where modules with name `test-*` should produce a record containing `(test:Program, ...)`. The test program should be zero-arity (no input or output on data stack) and use a 'fork' effect for fuzzing of input. Effects API:

* **fork** - response is unit or failure, non-deterministically.
* **log:Message** - Response is unit. Arbitrary output message to simplify debugging.

The only input to the test program is the sequence of 'fork' outcomes. Fork inputs can be used to select independent sub-tests or randomize test parameters. Backtracking and incremental computing of tests with a similar 'fork' prefix is feasible, allowing implicit sharing of test setup overheads while maintaining logical independence. A good test system may heuristically explore the input space to improve coverage or statically analyze the test program to search for inputs leading to potential failure conditions. 

The primary output from a test is success or failure. If the test terminates without failing, it is considered a success even if the log includes error or warning messages. The log is a secondary output, intended for humans or the integrated development environment.

*Aside:* Including test modules within a package system could provide a good measure of the health of the system.

### User Applications

Due to backtracking conditional behavior, Glas programs are a good fit for the *transaction machine* model of applications. Of course, it is feasible to design alternative program models as a basis for user applications. But transaction machines are an excellent fit for my long-term vision of live coding and reactive systems. I'm developing this idea in the [Glas Apps](GlasApps.md) document. 

A Glas command-line utility should provide a lightweight interpreter or JIT for console apps that does not rely on full binary extraction.

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

Glas has implicit, pervasive staging: a module's source is processed and compiled into a value, and this value is processed and compiled further by a client module. It is possible to leverage this for metaprogramming using problem-specific intermediate representations. Additionally, language modules can potentially support user-defined, modular macros and other techniques.

However, access to this feature depends on syntax. For example, [the g0 syntax](GlasZero.md) does not support metaprogramming. But users could implement a Glas program or macro interpreter for static evaluation by user-defined language modules. By leveraging *acceleration* we can feasibly achieve excellent performance for staging.

### Acceleration

Acceleration is an optimization pattern where we replace an inefficient reference implementation of a function with an optimized implementation. Accelerated code could leverage hardware resources that are difficult to use within normal Glas programs.

For example, we can design a program model for GPGPU, taking notes from OpenCL, CUDA, or Haskell's [Accelerate](https://hackage.haskell.org/package/accelerate). This language should provide a safe (local, deterministic) subset of features available on most GPGPUs. A reference implementation can be implemented using the Glas program model. A hardware-optimized implementation can be developed and validated against the reference.

Or we design a program model based on Kahn Process Networks. The accelerated implementation could support distributed, parallel, stream-processing computations. The main cost is limiting use of the effects handler.

Accelerators are a high-risk investment because there are portability, security, and consistency concerns for the accelerated implementation. Ability to fuzz-test against a reference is useful for detecting flaws - in this sense, accelerators are better than primitives because they have an executable specification. Carefully selected accelerators can be worth the risk, extending Glas to new problem domains.

To simplify recognition and to resist invisible performance degradation, acceleration must be explicitly annotated via `prog:(do:Program, accel:(...))`. This allows the compiler or interpreter to report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target). 

*Aside:* It is feasible to accelerate subsets of programs that conform to a common structure, thus a model hint could be broader than identifying a specific accelerator.

## Types

Glas requires a simple stack arity check, i.e. to ensure that loops have invariant stack size, and that conditionals have the same stack size on every branch. This check is easy to evaluate and simplifies a few optimizations. But we can do a lot more.

Desired Structural Properties: 

* partial: type annotations can be ambiguous or constraints.
* extensible: can easily grow the type model. 
* macros: simple type functions support common patterns.

Desired Expression Properties:

* abstract types: ensure a subprogram does not directly observe certain data
* substructural types: may also restrict copy/drop for abstract types
* row-polymorphic record types: static fields and absence thereof
* fixed-width numeric types: bytes, 32-bit words, etc..
* static data types: for robust partial evaluation.
* session types: express protocols and grammars for effects and data structures.
* dependent types: trustable encodings of metadata
* refinement types, e.g. prime numbers only
* units for numbers, and associated or phantom types in general

Types are a deep subject, but I don't need to have everything up-front assuming I favor extensible models.

## Glas Object

See [Glas Object](GlasObject.md).

## Thoughts

### Graph Based Data

I've considered using graph based data structures instead of tree based. Similar to the current model, each node in a graph has at most two outbound edges, labeled 0 or 1. Complex labels would be formed by an arc through the graph. The difference is that we can also form directed cycles within this graph. 

I decided against graphs because they are relatively awkward to manipulate with using conventional operators in a purely functional style. Also, most cyclic phenomena is actually fractal or temporal, so the ability for finite graph data to usefully model the phenomena is limited.

Of course, we can still model graph data indirectly using adjacency lists, matrices, DSL programs that construct graphs, or other non-graph structures. And it might be worthwhile to develop some accelerated models for working with graphs when Glas is a little more mature.

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas provides a viable foundation, but it's still a distant future. Part of the solution will certainly involve loading 'catalog' modules that index other modules to support search.

Because search in context of Glas modules will be deterministic, we cannot rely on non-determinism to learn or stabilize heuristic choices. A challenge will be effective memoization and caching to avoid unnecessary recomputation of choices across minor changes in source.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this across multiple layers of language modules and metaprogramming. One idea is to keep some source metadata with every bit that can be traced back to its few 'best' sources. This seems very expensive, but viable.


