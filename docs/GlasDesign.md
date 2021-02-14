# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary according to a program defined in a module named `language-ext`. Although there is a special exception for bootstrapping, most syntax will be user-defined. The value of a folder is the value of a contained `public` module, if exists, otherwise a record with an element per contained module..

File extensions compose. For `foo.xyz.json`, apply `language-json` to the file binary, followed by `language-xyz`. Source input to the latter can be a structured value. Conversely, when a file has no extension, its computed value is its binary content. Folders also may use language extensions, i.e. `foo.xyz/` will first compute the folder value then compile this value via `language-xyz`.

To find a module `foo`, a Glas system will first search for a local file or subfolder such as `foo.ext` or `foo/`, ignoring language extensions. If the module is not found locally, the Glas system will fall back to a non-local search based on a `GLAS_PATH` environment variable. Files and folders whose names start with `.` are naturally hidden from the Glas module system.

The module system is our primary namespace in Glas. Multiple references to a resource 

*Note:* Glas does not specify a package manager. I recommend use of Nix, Guix, or other package managers designed for reproducible builds.

## Data Model

Glas data is modeled as a binary tree. Edges from each node are uniquely labeled in binary (i.e. 0 or 1). Nodes are unlabeled except for the 'root' node that represents initial focus. All nodes are reachable from root. As an optimization, the tree may be represented as a directed acyclic graph, reusing subtrees internally. 

Data is encoded in the tree structure. 

For example, algebraic product and sum types, `(A * B)` and `(A + B)`, can be encoded respectively as a node containing both edges or exactly one edge. Unit `()` is simply a node with no further edges. A linked list can be encoded as `type List A = () + (A * List A)`. However, products and sums are not extensible or versionable. 

For most use cases, symbolic records and variants are a better option. For example, `type Color = rgb:(r:Byte, g:Byte, b:Byte) | hsl:(h:Byte, s:Byte, l:Byte)`. In this type, choice of `rgb` or `hsl` is a variant, while `(r, g, b)` and `(h, s, l)` are records. Well chosen text labels have the added benefit of being slightly self-documenting. 

Records are represented by encoding null-terminated UTF-8 text labels into a path through a tree. A 'path' traverses a sequence of nodes such as `01110000 01100001 01110100 01101000 00000000`. The associated value follows the terminator. Encoding multiple labels implicitly forms a [trie](https://en.wikipedia.org/wiki/Trie). For efficiency, Glas systems will compactly encode non-branching path segments, upgrading trie to [radix tree](https://en.wikipedia.org/wiki/Radix_tree). Variants are essentially single-element records. Symbols are unit variants.

A byte is encoded by a fixed-width, non-branching path of 8 bits, msb to lsb, terminating in unit. For example, `00111101` encodes 61. Larger words are encoded similarly, just with more bits. However, binary data is normally encoded as a list of bytes, not a huge path.

Glas systems will use lists for most sequential structures - e.g. stacks, queues, arrays, binaries, etc.. Logically, lists are linked lists - `0` is empty list, `10` is head of list, `11` is tail of list. However, Glas systems will normally substitute a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation under-the-hood. The finger-tree enables efficient access to the list and potential structure sharing of common sequences. [Rope-based extensions](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) can support compact encodings of binary data.

A compiler can use static analysis of types and dataflow to further specialize and optimize runtime representations. A record with known labels could be implemented by C-style struct. A linearly updated list can be implemented by mutable array.

### Stowage via Content-Addressed Storage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs have a 'stow' operator to guide use of stowage. However, modulo reflection effects, use of stowage is not observable within the program. Stowage can be deferred heuristically, e.g. waiting for memory pressure or potential GC of the stowed data.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries should include a cryptographic salt. This salt prevents global deduplication, but local deduplication can be supported by convergence secret.

## Extraction and Executables

A subset of Glas modules will compute binary data. The Glas command-line tool will provide a utility to extract the computed binary for external use, e.g. writing bytes to stdout for the shell to redirect. For very large, potentially infinite binaries, the command-line tool should also support extraction from a program that generates a binary data stream.

The binary data could represent an executable, but is not limited by this. It could instead represent a pdf document or MP3 music. Perhaps a C file that can be integrated into another project. For multi-file artifacts, the binary might represent a tarball or zipfile.

A relevant concern is targeting of executables. To support this, we can define a `system-info` module on `GLAS_PATH`. This module describes target architecture, OS, and other relevant details. A Glas installation could include a `system-info` targeting the current host, while cross-compilation only needs to tweak `GLAS_PATH`.

In this design, responsibility for generating a useful binary is shifted into the module system. A useful consequence is that computed binaries are accessible within the module system for potential composition into a zipfile or automated testing. The command-line tool can be relatively small and simple by omitting this logic.

## Automated Testing

Within a Glas folder, any module with name `test-*` should be implicitly processed for automatic testing purposes even if its value is not required by the `public` module. Failure and logged errors would be reported to developers. 

Testing of programs or binary executables often requires simulating their evaluation environment. This is feasible via accelerators that can simulate a virtual machine for running a binary.

## Glas Programs

Glas programs are represented by variants, records, and lists. The user-layer syntax has already been parsed and processed by a language module. For example, Glas programs do not use variables, but user-layer syntax might express functions using local variables. Essentially, Glas programs are an intermediate language.

Glas programs are based on a combinatory logic: each operator represents a function on the tacit environment, which consists of a few stacks and an abstract effects handler. Some operators compose other operators into larger programs. This design gives Glas a very procedural style, except that data cannot be pervasively mutated.

Glas programs should be analyzed to verify statically bounded stack size before evaluation. This property is simple to check in Glas. More sophisticated static analysis based on type annotations is also recommended but not required.

Conditional behavior in Glas programs is based on failure and backtracking. This is convenient for composable pattern matching, parsing, transactional models, and graceful error handling. However, backtracking is incompatible with synchronous remote requests, limiting direct use of some effects APIs.

*Note:* Mature Glas systems will support multiple program models via metaprogramming, acceleration, and extraction. However, the Glas program model provides the robust foundation, and is necessary to describe language modules.

### Stack Operators

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
 * move top item from data stack to dip stack
 * run P
 * move top item from dip stack to data stack
* **swap** - switch the top two items on stack
* **data:V** - copy V to top of stack

The data stack for a valid Glas program is statically bounded. A Glas compiler should replace the stack by a static set of variables. 

### Control Operators

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * *seq:\[\]* - empty sequence serves as identity operator, a nop
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, backtrack then run R.
* **loop:(try:P, then:Q)** - begin loop: run P; if P does not fail, run Q then repeat loop. If P fails, backtrack then exit loop.
* **eq** - compare top two values on stack. If they are equal, do nothing. Otherwise fail.
* **fail** - always fail

### Effects Handler

* **env:(do:P, eff:E)** - override effects handler in context of P.
 * move top item from data stack to eff stack
 * when `eff` is invoked within P (modulo further override):
  * move top item from eff stack to data stack
  * run E
  * move top item from data stack to eff stack
 * move top item from eff stack to data stack
* **eff** - invoke effects handler on current stack.

The top-level effects handler is provided by runtime or compiler. Use of 'env' enables a program to sandbox a subprogram and control access to effects. Effects handlers should currently have type `Request State -- Response State`, i.e. stack arity 2-2, at least until we have more advanced static types and static analysis.

### Annotation Operators

Annotations support static analysis, performance, automated testing, debugging, decompilation, and other external tooling. Annotations should not directly affect behavior of a program.

* **prog:(do:P, ...)** - runs P. Except for 'do', all fields are annotations about P. Potential annotations:
 * **name:Symbol** - an identifier to support debugging, profiling, logging, etc.. Will be hierarchical with other prog names. Preferably unique in context.
 * **in:\[List, Of, Symbols\]** - human-meaningful labels for stack input; rightmost is top of stack. This also indicates expected input arity.
 * **out:\[List, Of, Symbols\]** - human meaningful labels for stack output; rightmost is top of stack. This also indicates expected output arity. 
 * **bref:B** - assert that program P has the same behavior as program B. In this case, the intention is that P is an optimized or refactored B.
 * **type:Type** - assert P is compatible with a type description. This requires a type descriptor language.
 * **memo:(...)** - remember outputs for given inputs for incremental computing. Options could include table sizes and other strategy.
 * **accel:(...)** - tell compiler or interpreter to replace P by a known high-performance implementation
* **assert:Q** - try to run Q; if successful, undo effects then continue. Otherwise, halt the program and report assertion failure. Verified statically if feasible.
* **assume:Q** - infrequently checked assertion. Verified statically if feasible. Checked after things go wrong for error isolation. Might be checked on first encounter, too.
* **stow** - move top stack value to cheap, high-latency, content-addressed storage. Subject to runtime heuristics.

Assertions and assumptions are expressive for describing expectations and intended behavior, but are expensive at runtime and difficult to verify statically. Type annotations are preferred, at least for properties that are easy to express using types, but developing a good type model is non-trivial.

### Record Operators

* 


### Arithmetic Operators

add, mul, sub, div, mod

Arithmetic removes two numbers on the stack and adds one number. Use of 'sub' will fail if it would result in a negative number. Use of 'div' or 'mod' will fail with a 0 divisor.

### List Operators

pushl, popl, pushr, popr, split, join, len

A logical reverse operator is feasible, but I'm uncertain of utility or full consequences. An indexing operator is feasible, but I'd prefer to encourage cursor/zipper-based approaches to iteration - i.e. split first, then operate in the middle.

### Records

tag, find, erase, union, keys

One goal here is to simplify elimination of tag-select pairs.

Use of 'case' is almost the same as 'get' but also fails if the record has more than one key. This supports variant data types. Use of 'keys' is for iteration and is probably not efficient for large, dynamic records.



## Applications

The Glas command-line tool can also provide a mode to run applications that conform to a limited effects API, e.g. with support for console, filesystem, network, and lightweight GUI. This would not rely on extraction of an executable.

The main challenge here is API design. Synchronous APIs need to be avoided, and ideally we can ensure our GUI works nicely with live coding. These are relevant concerns even if we later extract executables. Discussion in[Glas Apps](GlasApps.md).

## Acceleration

Acceleration is an optimization pattern where we replace an inefficient reference implementation of a function with an optimized implementation. Accelerated code could leverage hardware resources that are difficult to use within normal Glas programs.

For example, we can design a program model for GPGPU, taking notes from OpenCL, CUDA, or Haskell's [Accelerate](https://hackage.haskell.org/package/accelerate). This language should provide a safe (local, deterministic) subset of features available on most GPGPUs. A reference implementation can be implemented using the Glas program model. A hardware-optimized implementation can be developed and validated against the reference.

Or we design a program model based on Kahn Process Networks. The accelerated implementation could support distributed, parallel, stream-processing computations. The main cost is limiting use of the effects handler.

Accelerators are a high-risk investment because there are portability, security, and consistency concerns for the accelerated implementation. Ability to fuzz-test against a reference is useful for detecting flaws - in this sense, accelerators are better than primitives because they have an executable specification. Carefully selected accelerators can be worth the risk, extending Glas to new problem domains.

To simplify recognition and to resist invisible performance degradation, acceleration must be explicitly annotated, e.g. via `prog:(do:Program, accel:ModelHints)`. This allows the compiler or interpreter to report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target). It also enables evaluation with and without acceleration for validation.

*Aside:* It is feasible to accelerate subsets of programs that conform to a common structure, thus a model hint could be broader than identifying a specific accelerator.

## Types


## Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a record of form `(compile:Program, ...)`. This record can be extended with language utilities (e.g. linter, decompiler, interactive REPL). The compile program should minimally pass a static arity check.

The compile program should implement a function from source (usually binary data) to compiled value, with a limited effects API:

* **load:ModuleID** - Module ID is typically encoded as a path symbol such as `foo`. Response is the copied from compiling the module, or failure if the module's value cannot be computed. 
* **log:Message** - Response is unit. Messages may include warnings and issues, progress reports, code change proposals, etc.. 

Load failure may occur due to missing modules, ambiguous file names, dependency cyles, failed compile, etc.. Cause of failure is not reported.

*Aside:* Support for an 'eval' effect might be useful for language modules, to support automated testing. However, I'd like to try implementing and accelerating a Glas interpreter within Glas first.

## Binary Stream Generators

For extraction of large (potentially non-terminating) binary streams, the command line tool should support a simple program type with no inputs, and whose only relevant output is to 'write' data. A viable effects API:

* **write:Binary** - Response is unit. Writes binary data (a list of bytes) to output.
* **log:Message** - Response is unit. Use to report progress, issues, etc..

We could potentially describe a generator within a command line argument to 'query' a Glas module system in flexible ways.

## Glas Object

See [Glas Object](GlasObject.md).

## Thoughts

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas provides a viable foundation, but it's still a distant future.

Part of the solution will certainly involve loading 'catalog' modules that index other modules to support search.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this.

