# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary according to a program defined in a module named `language-ext`. Although there is a special exception for bootstrapping, most syntax will be user-defined. The value of a folder is the value of a contained `public` file or subfolder, if it exists, otherwise a record with an element per successfully compiled module contained within the folder.

File extensions compose. For example, to compute the value for `foo.xyz.json` we first apply `language-json` to the file binary to compute an intermediate value, then apply `language-xyz` to that intermediate value. Thus, source input for language modules can be arbitrary Glas values. Relatedly, if a file has no extension, its value is the file binary. Folders also support language extensions, e.g. `foo.xyz/` will first compute the folder value then further compile this value via `language-xyz`.

Modules are identified by symbols such as `foo`, eliding representation details (file or folder and extensions). Search will first seek a local resource, then fall back to searching for a folder based on environment variable `GLAS_PATH`. It is feasible to extend the search path to include network package repositories. Files and folders whose names start with `.` are hidden from the Glas module system.

*Note:* Glas does not specify a package manager. I recommend a package manager designed for reproducible builds and sharing between similar builds, such as Nix or Guix. Distant future, I might develop something optimized for Glas.

## Data Model

Glas data is modeled as an immutable binary tree. Edges from each node are uniquely labeled in binary (i.e. 0 or 1). Operations on data return a new value. As an optimization, the tree is usually represented by a directed acyclic graph, reusing common subtrees.

Data is encoded in the tree structure. For example, we can directly encode algebraic products `(A * B)` and sums `(A + B)` using a single node that contains two or one edges respectively. Unit `()` is can be encoded by a leaf node. However, basic algebraic products and sums are awkward to extend or version.

Glas systems favor labeled records and variants. Null-terminated UTF-8 text is encoded into a path through a tree, e.g. label 'path' uses bitstring `01110000 01100001 01110100 01101000 00000000`. The value follows the null terminator. The runtime can compactly encode non-branching path segments, thus a record becomes a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). A compiler could further optimize records, e.g. use a symbol table to share labels across records. Variants are single element records, and symbols can be viewed as variants with the unit value.

A byte is encoded as a bitstring, i.e. a non-branching path encoding one bit per edge such as `00010111`. Glas has a few bitwise and unsigned arithmetic operators to work with bytes and bitstrings in general. Bitstrings are assumed to be reasonably short, often fixed-width.

Binary data is logically encoded as a list of bytes. Glas uses lists for arbitrary size sequential structure. However, to avoid the awkward and awful performance scalability characteristics of linked lists, Glas systems often substitute a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation for lists. Finger tree lists support constant-time access to both ends and logarithmic inner manipulations.

*Note:* There is no built-in support for rational numbers, signed numbers, floating point numbers, complex numbers, matrix and vector math, and so on. However, it is feasible to extend Glas to support performance of more types via *Acceleration*.

## Binary Extraction

A subset of Glas modules will compute binary data. The Glas command-line tool shall provide modes to extract binary data to stdout or file. The binary data should represent an externally useful software artifact - e.g. pdf, music file, an executable, or a C program. For multi-file software artifacts, the binary could represent a tarball or zipfile.

For targeting of executables, I propose to define a central `system-info` module on `GLAS_PATH`. This module will describe the target architecture, OS, and other relevant details. The default target is the local system, but cross-compilation only requires a tweak to the path. 

Responsibility for generating a useful binary is shifted into the module system. A useful consequence is that computed binaries are accessible within the module system. This supports composition of binaries (e.g. into a tarball) and automated testing over binaries without leaving the Glas module system. 

## Glas Programs

Glas programs are represented using variants, records, and lists. We assume the user-layer syntax has already been parsed and compiled by a language module. For example, Glas programs do not use variables, but user-layer syntax could express functions using local variables that are compiled away by the language module. Essentially, Glas programs are an intermediate language.

Glas programs are based on a combinatory logic: each operator represents a function on the tacit environment, which consists of a few stacks and an abstract effects handler. Some operators compose other operators into larger programs. This design gives Glas a very procedural style, except that data cannot be pervasively mutated.

Glas programs should be analyzed to verify statically bounded stack size before evaluation. This property is simple to check in Glas. More sophisticated static analysis based on type annotations is also recommended but not required.

Conditional behavior in Glas programs is based on failure and backtracking. This is convenient for composable pattern matching, parsing, transactional models, and graceful error handling. However, backtracking is incompatible with synchronous remote requests, limiting direct use of some effects APIs.

*Note:* Mature Glas systems will support multiple program models via metaprogramming, acceleration, and extraction. However, the Glas program model provides the robust foundation, and is necessary to describe language modules.

### Stack Operators

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
 * move top item from data stack to top of dip stack
 * run P
 * move top item from dip stack to top of data stack
* **swap** - switch the top two items on stack
* **data:V** - copy V to top of stack

A Glas compiler should completely eliminate stack operators. For a valid Glas program the stack is statically bound, so the compiler can generally allocate all the variables we'll need before running the program. Stack shuffling is just an intermediate representation of dataflow. However, it could be directly implemented by an interpreter.

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

### Control Operators

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * **seq:\[\]** - empty sequence doubles as identity operator (nop)
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, backtrack then run R.
* **loop:(try:P, then:Q)** - begin loop: run P; if P does not fail, run Q then repeat loop. If P fails, backtrack then exit loop.
* **eq** - compare top two values on stack. If they are equal, do nothing. Otherwise fail.
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
        loop:(try:P, then:Q)    (as LOOP)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S
            -------------------
            LOOP ⊲ S → S
        eq : ∀S,A,B . ((S * A) * B) → ((S * A) * B) | FAIL
        fail : ∀S . S → FAIL

There are a lot of potential optimizations, e.g. we can attempt to combine conditionals that share a lot of work, we can flatten sequences within sequences, and we can reduce statically known failure in many cases.

Glas does not have first-class functions, i.e. there is no 'eval' operator. However, it is feasible to develop an evaluator for Glas as a Glas subprogram.

### Effects Handler

* **env:(do:P, eff:E)** - override effects handler in context of P.
 * move top item from data stack to eff stack
 * where `eff` is invoked within P (modulo further override):
  * move top item from eff stack to top of data stack
  * run E
  * move top item from data stack to top of eff stack
 * move top item from eff stack to data stack
* **eff** - invoke current effects handler on current stack.

The top-level effects handler is provided by runtime or compiler. Use of 'env' enables a program to sandbox a subprogram and control access to effects. Effects handlers should have type `Request State -- Response State`, i.e. stack arity 2-2, until we have sufficiently advanced static types and static analysis.

    env:(do:P, eff:E)           (as ENV)
        E ⊲ (Sf * State) → (Sf' * State)
        eff ⊲ Sf → Sf' ⊢ P ⊲ S → S'
        --------------------------------
        ENV ⊲ (S * State) → (S' * State)
    eff         type determined by env

### Record and Variant Operators

A set of operations useful for records and variants. 

Labels are encoded into bitstrings, usually as null-terminated UTF-8 text. Within a record, label bitstrings must exhibit the prefix property: no valid label is a prefix of another valid label. Other than null terminators, this could be supported by fixed-width label encodings, e.g. forming an intmap.

Record operators:

* **get** - given label and record, extract value from record. Fails if label is not in record.
* **put** - given a label, value, and record, create new record that is almost the same except with value on label. 
* **del** - given label and record, create new record with label removed modulo prefix sharing with other paths in record.

A record may have many labels, sharing prefixes. Non-branching path segments can be compactly encoded to reduce the number of pointers. It is feasible for a compiler to optimize records with statically known labels like a C struct. A variant is a singleton record. 

### List Operators

A linked list has simple structure, but terrible performance for any use-case except a stack. To solve this, Glas provides a few dedicated operators for lists. This enables the runtime or compiler to transparently substitute a more efficient representation such as a [finger tree](https://en.wikipedia.org/wiki/Finger_tree).

* **pushl** - given value and list, add value to left (head) of list
* **popl** - given a non-empty list, split into head and tail.
* **pushr** - given value and list, add value to right of list
* **popr** - given non-empty list, split into last element and everything else
* **join** - appends list at top of data stack to the list below it
* **split** - given number N and list of at least N elements, produce a pair of sub-lists such that join produces the original list, and the head portion has exactly N elements.
* **len** - given list, return a number that represents length of list. Uses smallest encoding of number (i.e. no zeroes prefix).

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

Glas encodes natural numbers into bitstrings assuming msb-to-lsb order. For example, `00010111` encodes 23. Although the zeroes prefix `000` doesn't contribute to numeric value, Glas arithmetic operators will preserve bitstring field widths. For example, subtracting 17 results in `00000110`.

* **add** (N1 N2 -- Sum Carry) - compute sum and carry for two numbers on stack. Sum has field width of N1, carry has field width of N2. Carry can be larger than 1 iff N2 is wider than N1 (e.g. adding 32-bit number to an 8-bit number).
* **mul** (N1 N2 -- Prod Overflow) - compute product of two numbers on stack. Product has field width of N1, while overflow uses field width of N2. 
* **sub** (N1 N2 -- Diff) - Computes a non-negative difference (N1 - N2), preserving field width of N1. Fails if result would be negative. This serves as the comparison operator for natural numbers.
* **div** (Dividend Divisor -- Quotient Remainder) - If divisor is zero, this operator fails. Compute division of two numbers. Quotient has field width of dividend, and remainder has field width of divisor.

Glas doesn't have built-in support for negative numbers, floating point, etc.. Extending arithmetic may benefit from *Acceleration*.

### Annotation Operators

Annotations support static analysis, performance, automated testing, safety and sanity, debugging, decompilation, and other external tooling. However, annotations cannot be directly observable within a program, modulo reflective effects API. For example, assertion failures must halt the program because normal failure is observable within a program via try/then/else.

* **prog:(do:P, ...)** - runs P. Fields other than 'do' should be annotations about P. Potential annotations:
 * **name:Symbol** - an identifier for the region to support debugging (logging, profiling, etc.) based on external configuration. Implicitly hierarchical with containing prog name.
 * **stack:(in:\[Arg,Symbols\], out:\[Result,Symbols\])** - human-meaningful labels for stack effect. This can be leveraged for decompilation, debugging, arity checking, syntax bindings of keyword arguments and results, in-out parameters, etc..
 * **type:Type** - describe type of subprogram P.
 * **bref:B** - assert that program P has the same behavior as program B. In this case, the intention is usually that P is an optimized or refactored B.
 * **docs:Docs** - a record for arbitrary documentation, intended for a human or document generator. Might include text, icons, example usage, etc.. 
 * **memo:Hints** - remember outputs for given inputs for incremental computing. Options could include table sizes and other strategy.
 * **accel:Hints** - tell compiler or interpreter to replace P by an enhanced-performance implementation

* **note:(...)** - inline annotations for use with seq. Pseudo-operators on the tacit environment.
 * **stack:\[List, Of, Symbols\]** - human-meaningful labels for top few stack elements; rightmost is top of stack.
 * **type:Type** - describe assumptions about stack and state that should hold at this point in the program. 
 * **probe:Symbol** - explicit debug point for use with 'seq'. By default write a debug log with debug symbol, stack, state, and timing info. May be externally configurable with filters or assertions or for use as a breakpoint or checkpoint, depending on development environment.
 * **assert:Q** - assert that Q should succeed if run. Verify statically if feasible, otherwise check at runtime. Use backtracking to undo effects. Runtime assertion failures are uncatchable and directly halt the program.
 * **assume:Q** - as assert, but with greater emphasis on static verification. Unchecked or infrequently checked at runtime.
 * **stow** - hint to move top stack value to cheap, high-latency, content-addressed storage. Subject to runtime heuristics.
 * **test:T** - add automatic tests to your code; compiler may interpret per *Automated Testing*.

The set of annotations is openly extensible and subject to de-facto standardization. When a Glas compiler or interpreter encounters annotation labels it does not recognize, it should log a warning then ignore. 

*Aside:* Assertions are expressive and convenient for quick and dirty checks, but are difficult to statically analyze in a systematic manner. In contrast, types are restrictive but designed to support systematic static analysis. Glas systems should usually favor types.

## Applications

### Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a record of form `(compile:Program, ...)`. Aside from 'compile', the record for a language module may define other ad-hoc properties - documentation, linter, decompiler, language server app, REPL mode, etc..

The compile program implements a function from source (e.g. file binary) to a compiled value on the stack. The compile program can also access other module values and generate some log outputs. Effects API:

* **load:ModuleName** - Module names are typically encoded as a symbol such as `foo`, but mightl later be extended. Response is the result from compiling the module, or failure if the module's value cannot be computed.
* **log:Message** - Response is unit. Messages may include warnings and issues, progress reports, code change proposals, etc.. 

Load failure may occur due to missing modules, ambiguous file names, dependency cyles, failed compile, etc. The cause of failure is visible to the client module, but is reported in the development environment.

### Automated Testing

For lightweight automatic testing, Glas systems will follow a convention where modules with name `test-*` should produce a test program. The test program is then evaluated with access to 'fork' effects for fuzzing of input. Effects API:

* **fork** - response is unit or failure, non-deterministically.
* **log:Message** - arbitrary output message to simplify debugging.

Test programs should be stack neutral. Input to the test program is based on a sequence of 'fork' results. Primary output is success or failure of the program, though reflection on logged messages and location of failure would be visible for debugging. 

The test program can be evaluated many times with different forks, and a test's fork history can be recorded as a binary for replay. Usefully, each fork is essentially independent so we can continue a test with prior errors, yet incremental computing is also feasible where we cache the computations up to a fork.

Forks can select subtests, randomize parameters, and simulate non-deterministic or variable environments. When there are only a few forks, the system will test exhaustively. Otherwise, a good test environment might use heuristics and analysis to search for useful forks, aiming for branch coverage and error discovery.

### User Applications

Glas programs are a good fit for the *transaction machine* model of applications. And transaction machines are a good fit for my vision of live coding and reactive systems. I'm developing this idea in the [Glas Apps](GlasApps.md) document. 

The Glas command-line utility should provide an interpreter (or JIT-compiler) for at least a console application model, with access to network, filesystem, and stdio. 

## Performance

### Stowage via Content-Addressed Storage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs have a 'stow' operator to guide use of stowage. However, modulo reflection effects, use of stowage is not observable within the program. Stowage can be deferred heuristically, e.g. waiting for memory pressure or potential GC of the stowed data.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries should include a cryptographic salt. This salt prevents global deduplication, but local deduplication can be supported by convergence secret.

### Partial Evaluation

Glas is designed to simplify partial evaluation: Linking is static. Stack allocation is static. Static record paths are readily distinguishable from dynamic record fields. Effects handlers can be inlined and partially applied, including a top-level compiler-provided handler. 

However, partial evaluation is fragile unless supported by annotations. Robust partial evaluation benefits from type-system hints: we can require certain record fields or stack elements to be statically computable. Conveniently, it is easy to verify partial evaluation - we need only to try partial evaluation and see. 

### Staged Metaprogramming

Glas has implicit, pervasive staging: a module's source is processed and compiled into a value, and this value is processed and compiled further by a client module. It is possible to leverage this for metaprogramming using problem-specific intermediate representations. Additionally, language modules can potentially support user-defined, modular macros and other techniques.

Glas doesn't have an eval primitive. To fully support staged metaprogramming requires implementing the eval program, such that language modules can directly interpret Glas programs. Glas is a relatively simple language, but this may require compiling the interpreter to an accelerated model for performance.

### Acceleration

Acceleration is an optimization pattern where we replace an inefficient reference implementation of a function with an optimized implementation. Accelerated code could leverage hardware resources that are difficult to use within normal Glas programs.

For example, we can design a program model for GPGPU, taking notes from OpenCL, CUDA, or Haskell's [Accelerate](https://hackage.haskell.org/package/accelerate). This language should provide a safe (local, deterministic) subset of features available on most GPGPUs. A reference implementation can be implemented using the Glas program model. A hardware-optimized implementation can be developed and validated against the reference.

Or we design a program model based on Kahn Process Networks. The accelerated implementation could support distributed, parallel, stream-processing computations. The main cost is limiting use of the effects handler.

Accelerators are a high-risk investment because there are portability, security, and consistency concerns for the accelerated implementation. Ability to fuzz-test against a reference is useful for detecting flaws - in this sense, accelerators are better than primitives because they have an executable specification. Carefully selected accelerators can be worth the risk, extending Glas to new problem domains.

To simplify recognition and to resist invisible performance degradation, acceleration must be explicitly annotated via `prog:(do:Program, accel:(...))`. This allows the compiler or interpreter to report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target). 

*Aside:* It is feasible to accelerate subsets of programs that conform to a common structure, thus a model hint could be broader than identifying a specific accelerator.

## Types

Glas requires a simple stack arity check, i.e. to ensure that loops have invariant stack size, and that conditionals have the same stack size on every branch. This check is easy to evaluate and simplifies a few optimizations, but we could do a lot more.

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

### Cyclic Data

I've considered using cyclic graph data structures instead of trees. Similar to the current model, each node in the graph has at most two outbound edges, labeled 0 or 1. Complex labels would be formed by an arc through the graph. 

I decided against graphs because they are relatively difficult to locally reason about and control compared to trees, interact awkwardly with stowage, and are unnecessary for the initial goals of Glas. However, Glas systems could eventually compile or accelerate program models based around cyclic graph structures.

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas provides a viable foundation, but it's still a distant future.

Part of the solution will certainly involve loading 'catalog' modules that index other modules to support search.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this.


