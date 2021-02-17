# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary according to a program defined in a module named `language-ext`. Although there is a special exception for bootstrapping, most syntax will be user-defined. The value of a folder is the value of a contained `public` module, if exists, otherwise a record with an element per contained module.

File extensions compose. For `foo.xyz.json`, apply `language-json` to the file binary, followed by `language-xyz`. Source input to the latter can be a structured value. Conversely, when a file has no extension, its computed value is its binary content. Folders also may use language extensions, i.e. `foo.xyz/` will first compute the folder value then compile this value via `language-xyz`.

Module search will seek a local module within a folder then, failing that, search for a global module on `GLAS_PATH`. File extension and representation details are hidden, e.g. module `foo` may bind to folder `foo.d/` but the client won't know whether it was a folder or a file. Files and folders whose names start with `.` are simply hidden from the Glas module system.

*Note:* Glas does not specify a package manager. I recommend a package manager designed for reproducible builds and sharing between similar builds, such as Nix or Guix. Distant future, I might develop something optimized for Glas.

## Data Model

Glas data is modeled as an immutable binary tree. Edges from each node are uniquely labeled in binary (i.e. 0 or 1). Operations on data return a new value. As an optimization, the tree is often represented as a directed acyclic graph, reusing subtrees internally. 

Data is encoded in the tree structure. For example, we can directly encode algebraic products `(A * B)` and sums `(A + B)` using a single node that contains two or one edges respectively. Unit `()` is can be encoded by a leaf node. However, algebraic products and sums are awkward to extend or version.

Glas systems favor labeled records and variants. Records are represented by encoding null-terminated UTF-8 text labels into paths through a tree, with prefix sharing (forming a [trie](https://en.wikipedia.org/wiki/Trie)). A 'path' traverses a sequence of nodes such as `01110000 01100001 01110100 01101000 00000000`. The associated value follows the terminator. For efficiency, Glas systems will compactly encode non-branching path segments, implicitly upgrading trie to [radix tree](https://en.wikipedia.org/wiki/Radix_tree). Variants are simply single-element records.

A byte is encoded as a string of 8 bits, i.e. a non-branching path with one bit per edge such as `00010111`. Glas has a few specialized bitwise and arithmetic operators to work with bytes and bitstrings in general. However, binary data is encoded as a list of bytes. 

For general sequential structure, Glas uses lists. However, the linked list representation has poor performance for many use-cases. Glas systems resolve this by transparently using a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation under-the-hood. Finger trees support constant-time access to both ends, logarithmic manipulation of the interior, and potential structure sharing for segments within the list.

## Binary Extraction and Executables

A subset of Glas modules will compute binary data. The Glas command-line tool will provide a utility to extract the computed binary for external use, e.g. writing bytes to stdout for the shell to redirect. For very large, potentially infinite binaries, the command-line tool can also have a mode for extraction from a program that generates a binary data stream.

The binary data could represent an executable, but is not limited by this. It could instead represent a pdf document or MP3 music. Perhaps a C file that can be integrated into another project. For multi-file artifacts, the binary might represent a tarball or zipfile.

A relevant concern is targeting of executables. To support this, we can define a `system-info` module on `GLAS_PATH`. This module describes target architecture, OS, and other relevant details. A Glas installation could include a `system-info` targeting the current host, while cross-compilation only needs to tweak `GLAS_PATH`.

In this design, responsibility for generating a useful binary is shifted into the module system. A useful consequence is that computed binaries are accessible within the module system for potential composition into a zipfile or automated testing. The command-line tool can be relatively small and simple by omitting this logic.

## Automated Testing

For lightweight automatic testing, Glas systems will follow a convention where modules in a folder with name `test-*` are processed even when their value is not required by a `public` module. Failed tests are reported to developers. At a larger scale, it is feasible to track overall health of a module distribution based on failed and passing tests.

Testing can be very flexible in Glas because the program representation is accessible and even the final binaries are still within the module system. Tests can abstractly analyze and interpret code, verify structure of binaries, simulate execution, etc..

## Glas Programs

Glas programs are represented by variants, records, and lists. The user-layer syntax has already been parsed and processed by a language module. For example, Glas programs do not use variables, but user-layer syntax could express functions using local variables that are compiled away by the language module. Essentially, Glas programs are an intermediate language.

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
 * **seq:\[\]** - empty sequence serves as the identity operator
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

A set of path-oriented operations useful for records and variants. Paths are encoded as bitstrings, i.e. a non-branching sequence of nodes that encode bits into edges.

* **get** - given path and record, extract value on path from record. Fails if path is not within record.
* **put** - given a path, value, and record, create new record including value on path. 
* **del** - given path and record, create a new record value at path removed, and path removed modulo prefix sharing with other paths in record.

It is feasible for a compiler to heavily optimize records and variants with statically known paths. These optimizations are greatly simplified by use of dedicated operators, in contrast to manipulating one bit at a time.

*Note:* These operators don't assume any path encoding. It could be UTF-8 null-terminated text, or fixed-width words (forming an intmap), or something else. However, the *prefix property* is required: no valid path is a prefix of another valid path in the record.

### List Operators

A linked list has simple structure, but terrible performance for any use-case except a stack. To solve this, Glas provides a few dedicated operators for lists. This enables the runtime or compiler to transparently substitute a more efficient representation, usually a [finger tree](https://en.wikipedia.org/wiki/Finger_tree).

* **pushl** - given value and list, add value to left (head) of list
* **popl** - given a non-empty list, split into head and tail.
* **pushr** - given value and list, add value to right of list
* **popr** - given non-empty list, split into last element and everything else
* **join** - appends list at top of data stack to the list below it
* **split** -  given number N and list, produce a pair of sub-lists such that join produces the original list, and the first portion of the list has N elements.
* **len** - given list, return a number that represents length of list. Uses smallest encoding of number (no zero prefix).

These operators assume a simplistic representation of lists: `type List = () | (Value * List)`. That is, every list node has no edges or two edges. In case of two edges, edge 0 is head and edge 1 is tail of list. List operators fail if applied to invalid lists, i.e. when what should be a list node has exactly one edge.

*Note:* Lists can be constructed and manipulated via non-list operators, but probably won't benefit from the optimized representation unless a later list operator converts them back.

### Bitstring Operators

A bitstring is a specialized list of bits represented by a non-branching path, e.g. `01100101` is a string of 8 bits. Empty bitstrings are permitted. Glas uses bitstrings to represent bytes and 'machine' words, natural numbers, and as a path parameter when constructing records.

List operators:

* **bjoin** - as list join, but for bitstrings
* **bsplit** - as list split, but for bitstrings
* **blen** - as list len, but for bitstrings

Bitwise operators:

* **bneg** - bitwise 'not', flip all bits of bitstring
* **bmax** - bitwise 'or' of two bitstrings of equal length
* **bmin** - bitwise 'and' of two bitstrings of equal length
* **beq** - bitwise equivalence of two bitstrings of equal length (i.e. 1 where bits match, 0 otherwise)

Bitwise operators fail if assumed conditions are not met: not a bitstring, or not equal length.

### Arithmetic Operators

Glas encodes natural numbers into bitstrings assuming msb-to-lsb order. For example, `00010111` encodes 23. Although the zeroes-prefix doesn't contribute to numeric value, Glas arithmetic operators preserve input structure in results, e.g. subtracting `1` results in `00010110`.

* **add** (N1 N2 -- Sum Carry) - compute sum of two numbers on stack. Sum has field width of lower number on stack, while carry uses field width of top stack element.
* **mul** (N1 N2 -- Prod Overflow) - compute product of two numbers on stack. Product has field width of lower number on stack, while overflow uses field width of top stack element.
* **sub** (N1 N2 -- Diff) - compute difference of two numbers on stack. Result has field width of lower number on stack. In case of underflow, this operator fails.
* **div** (Dividend Divisor -- Quotient Remainder) - compute result of dividing two numbers, and remaining elements. The quotient has field width of dividend, and remainder has field width of divisor. Fails for zero divisor.

*Note:* Use of 'sub' doubles as a conditional expression, i.e. that one number is greater than or equal to another.

### Annotation Operators

Annotations support static analysis, performance, automated testing, sanity checks, debugging, decompilation, and other external tooling. Annotations should not directly affect behavior of a program.

* **prog:(do:P, ...)** - runs P. Except for 'do', all fields are annotations about P. Potential example annotations:
 * **name:Symbol** - an identifier to support debugging, profiling, logging, etc.. Will be hierarchical with other prog names. Preferably unique in context.
 * **in:\[List, Of, Symbols\]** - human-meaningful labels for stack input; rightmost is top of stack. This also indicates expected input arity.
 * **out:\[List, Of, Symbols\]** - human meaningful labels for stack output; rightmost is top of stack. This also indicates expected output arity. 
 * **bref:B** - assert that program P has the same behavior as program B. In this case, the intention is that P is an optimized or refactored B.
 * **type:Type** - assert P is compatible with a type description. This requires a type descriptor language.
 * **icon:Image** - an icon to represent a program, e.g. in a visual programming environment or collapsed view for a debugger
 * **docs:(...)** - a record for arbitrary documentation, intended for a human or document generator.
 * **memo:(...)** - remember outputs for given inputs for incremental computing. Options could include table sizes and other strategy.
 * **accel:(...)** - tell compiler or interpreter to replace P by a known high-performance implementation
* **assert:Q** - assert that Q should be able to run at given point. Verify by static analysis if feasible. Assertions are frequently checked at runtime, using the backtracking to undo any effects. Failure of an assertion is not observable within the program, but may halt the program at the runtime layer.
* **assume:Q** - infrequently checked assertion, verified statically if feasible. Might be checked on first encounter then with exponential random backoff, or other heuristic strategy.
* **stow** - move top stack value to cheap, high-latency, content-addressed storage. Subject to runtime heuristics.

Assertions and assumptions are very expressive for describing expectations and intended behavior, but are also expensive at runtime and relatively difficult to verify statically. In contrast, type descriptions can be restricted to ensure the properties they express support robust static analysis. This shifts the burden to development of the type model and encoding properties in types.

## Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a record of form `(compile:Program, ...)`. This record can be extended with language utilities (e.g. linter, decompiler, interactive REPL). The compile program should minimally pass a static arity check.

The compile program should implement a function from source (usually binary data) to compiled value, with a limited effects API:

* **load:ModuleID** - Module ID is typically encoded as a symbol such as `foo`. Response is the copied from compiling the module, or failure if the module's value cannot be computed. 
* **log:Message** - Response is unit. Messages may include warnings and issues, progress reports, code change proposals, etc.. 

Load failure may occur due to missing modules, ambiguous file names, dependency cyles, failed compile, etc.. Cause of failure is not reported.

*Aside:* Support for an 'eval' effect might be useful for language modules, to support automated testing. However, I'd like to try implementing and accelerating a Glas interpreter within Glas first.

## Binary Stream Generators

For extraction of very large binary data, we might wish to recompute it as-needed instead of paying storage costs. The command line tool can support this by providing an option to interpret a program that generates a binary stream. 

Such a program doesn't need many effects - just emitting binary data, and perhaps some notes on the side for debugging and progress reports. A viable effects API:

* **write:Binary** - Response is unit. Writes binary data (a list of bytes) to output.
* **log:Message** - Response is unit. Use to report progress, issues, etc..

Relevantly, this program has no inputs, so it will produce the same binary every time unless there are relevant changes in code.

## Applications

The Glas command-line tool can provide an option to interpret applications that conform to a limited effects API, e.g. with support for console, filesystem, network, and lightweight GUI. This is useful for getting something running early on. 

The main challenge here is API design. For example, synchronous APIs must be avoided due to backtracking. For my vision, I'd like to ensure our application works nicely with live coding by default. Discussion in [Glas Apps](GlasApps.md).

## Stowage via Content-Addressed Storage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs have a 'stow' operator to guide use of stowage. However, modulo reflection effects, use of stowage is not observable within the program. Stowage can be deferred heuristically, e.g. waiting for memory pressure or potential GC of the stowed data.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries should include a cryptographic salt. This salt prevents global deduplication, but local deduplication can be supported by convergence secret.

## Acceleration

Acceleration is an optimization pattern where we replace an inefficient reference implementation of a function with an optimized implementation. Accelerated code could leverage hardware resources that are difficult to use within normal Glas programs.

For example, we can design a program model for GPGPU, taking notes from OpenCL, CUDA, or Haskell's [Accelerate](https://hackage.haskell.org/package/accelerate). This language should provide a safe (local, deterministic) subset of features available on most GPGPUs. A reference implementation can be implemented using the Glas program model. A hardware-optimized implementation can be developed and validated against the reference.

Or we design a program model based on Kahn Process Networks. The accelerated implementation could support distributed, parallel, stream-processing computations. The main cost is limiting use of the effects handler.

Accelerators are a high-risk investment because there are portability, security, and consistency concerns for the accelerated implementation. Ability to fuzz-test against a reference is useful for detecting flaws - in this sense, accelerators are better than primitives because they have an executable specification. Carefully selected accelerators can be worth the risk, extending Glas to new problem domains.

To simplify recognition and to resist invisible performance degradation, acceleration must be explicitly annotated, e.g. via `prog:(do:Program, accel:ModelHints)`. This allows the compiler or interpreter to report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target). It also enables evaluation with and without acceleration for validation.

*Aside:* It is feasible to accelerate subsets of programs that conform to a common structure, thus a model hint could be broader than identifying a specific accelerator.



## Types




## Glas Object

See [Glas Object](GlasObject.md).

## Thoughts

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas provides a viable foundation, but it's still a distant future.

Part of the solution will certainly involve loading 'catalog' modules that index other modules to support search.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this.

### Natural Numbers in Paths

An idea that comes up often is the idea of encoding arbitrary natural numbers into record labels, while ensuring compatibility with lexicographic order. The encoding of natural numbers doesn't have a clear suffix, so we'd need to use prefix-encoded sizes. 

One option is to encode the number of bits in unary, followed by the bits. For example, to encode the number `6` we'd write `110110`. 



 while preserving lexicographic order. 

 So far, I have two approaches that could well. 

First, I could encode the number of bits in the numb










