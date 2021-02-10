# Glas Language Design

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must be acyclic (i.e. a directed acyclic graph), and dependencies across folder boundaries are structurally restricted. Every module will deterministically compute a value. 

To compute the value for a file `foo.ext`, the Glas system will compile the file binary according to a program defined in a module named `language-ext`. Although there is a special exception for bootstrapping, most syntax will be user-defined. The value of a folder is the value of a contained `public` module, if exists, otherwise a dictionary with an element per contained module.

File extensions compose. For `foo.xyz.json`, apply `language-json` to the file binary, followed by `language-xyz`. Source input to the latter can be a structured value. Conversely, when a file has no extension, its computed value is its binary content. Folders also may use language extensions, i.e. `foo.xyz/` will first compute the folder value then compile this value via `language-xyz`.

To find a module `foo`, a Glas system will first search for a local file or subfolder such as `foo.ext` or `foo/`, ignoring language extensions. If the module is not found locally, the Glas system will fall back to a non-local search based on a `GLAS_PATH` environment variable. Files and folders whose names start with `.` are naturally hidden from the Glas module system.

*Note:* Glas does not specify a package manager. I recommend use of Nix, Guix, or other package managers designed for reproducible builds.

## Binary Extraction

Glas values can represent binaries as a list of small numbers (0..255). A subset of Glas modules will compute binaries, or values containing binaries. A Glas command line tool should provide a mechanism to extract computed binaries for external use. 

A subset of binaries may represent executables. Thus, the Glas module system can compile executable binaries, which we extract then execute. The logic to compute an executable binary would be embedded in the module system instead of the command line tool. Target-specific builds are possible: we only need a target-specific `system-info` module in `GLAS_PATH`.

We aren't limited to extracting binary executables. It is possible to extract a binary that represents a PDF document, PNG image, or a zipfile. If we aren't confident in our optimizers, we could extract a C++ file for external compilation.

In any case, binary extraction is the primary mechanism to get useful software artifacts out of the Glas system. Conveniently, these binaries remain accessible for further processing within the Glas system, such as automated testing.

## Graph-Structured Data Model

Glas data is logically modeled as a closed, non-empty, directed, connected graph with labeled edges. Nodes are unlabeled except for a 'start' node, which represents focus. Edges are labeled in binary, 1 or 0, and labels are unique per node. Cycles are permitted. Data is represented in the graph structure. 

Natural numbers can be encoded as a chain of nodes whose edges are labeled in a unary representation (e.g. `1111...0`). Textual labels can be encoded as zero-terminated chains of natural numbers. A dictionary is encoded by a node with a set of textual labels. Labels with a common prefix will share the prefix a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). Variant data can be encoded as a singleton dictionary.

Glas implementations will heavily optimize representations for natural numbers and textual labels so we can treat these features as performance primitives.

For sequences, programmers can directly model linked lists, e.g. edge 0 is head, edge 1 is tail. Glas implementations could feasibly optimize representation for binary lists, but anything beyond this may require *acceleration*. Programmers should model finger-trees to work with very large sequences.

*Aside:* Originally, I favored a conventional tree-structured data model. However, when integrating effects, the tree structure requires too much explicit reference management; the logical graph is too implicit.

## Content-Addressed Storage (CAS)

Glas can support large data structures using content-addressed storage: a closed graph can be serialized to disk or network and referenced by secure hash. We could encode multiple subgraphs within a binary by also moving the entry point to the reference. Logical replication is preserved via [copy-on-write](https://en.wikipedia.org/wiki/Copy-on-write). Secure hashes simplify incremental and distributed computing:

* persistent data structures, structure sharing
* incremental upload and download by hash cache
* efficient memoization, matches on tree hashes
* provider-independent distribution, validation

Glas systems can use content-addressed storage to mitigate memory pressure similar to virtual paging, and to support distributed computations with lazy and incremental communication. However, content-addressed storage is not observable within the program - it's essentially an optimization.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries should have a header space to include a cryptographic salt (and other metadata). This salt prevents global deduplication, but local deduplication is supported by stable convergence secret.

## Program Model

Glas defines a foundational program model for language modules and as a suitable starting point for applications. Design goals for this model include simplicity, determinism, extensibility, incremental computing, and low barriers for metaprogramming. 

Programs are represented by an abstract syntax tree formed of variants, dictionaries, and lists. There are variant operators for sequencing, conditionals, loops, natural number arithmetic, annotations, etc.. Programs are second-class (no eval), acyclic (no directed cycles, modulo 'data'), and statically linked (no function pointers). These restrictions simplify the model.

Programs manipulate a tacit environment consisting of a graph, a cursor stack, and effects handler. The cursor stack essentially contains references to nodes within the graph. Because the tacit environment is effectively linear, operations can be usefully viewed as mutating the environment and as pure functions on the environment.

The effects handler is a lightweight mechanism to interact with the environment. The env operator enables interception of this handler within scope of a subprogram. The runtime or compiler will provide the external effects handler. It is also possible to override an effects handler within scope of a subprogram.

Conditionals and loops are based on failure and backtracking. Effects are also backtracked. Backtracking is expressive for pattern matching and parsing and suitable for modeling transactions. However, it does interfere with expression of synchronous interactions. 

## Namespace? Defer.

The module system implicitly serves as a namespace layer, but results in too much duplicate code. Optimizers can deduplicate common subprograms via directed acyclic graph. It is feasible to model a namespace using the env/eff operators.

Thus, there is no strong pressure to implement namespaces. Meanwhile, there is some pressure to avoid doing so: explicit sharing via namespaces can easily interfere with metaprogramming due to contextual entanglement. 

Glas programs do support indirect namespace models via the effects model.

## Operators

### Data Flow

Glas uses a data stack as an intermediate representation of dataflow. Programs can be expressed using variables then compiled for the stack. Valid Glas programs have a statically predictable stack behavior (no recursion). A Glas compiler can eliminate the data stack and compute using a static allocation of variables.

        copy, drop, dip:P, swap, data:Val

        copy        forall x,xs . x:xs -> x:x:xs
        drop        forall x . x:xs -> xs
        dip:P       P of xs->xs'   |-  dip:P of forall x. x:xs -> x:xs'
        swap        forall x,y,xs . x:y:xs -> y:x:xs
        data:Val    forall xs . xs -> const(Val):xs

Elements on the stack may be arbitrary Glas values, thus dynamic memory is still used.

### Control Flow

\[list, of, operators\]
cond:(try:P, then:Q, else:R)
loop:(try:P, then:Q)
fail
eq
lt

Sequential composition is represented by a list. Consequently, a list containing a list can be fully flattened.

Conditionals and loops branch based on success or failure of a 'try' program. The 'try' program may perform effects and manipulate the stack, and this is preserved on success. It is often possible to statically eliminate try operations based on context of use. We can also flatten conditionals such as 'try(try(...))'. 

A lot of operators will fail conditionally. The 'fail' operator always fails. 

The 'eq' operator will compare values of arbitrary size for equality. It could potentially use value reference equality for performance under the hood.

The 'lt' operator imposes a total order on values, asserting the that the second stack element is less-than the top stack element. For example, `[data:4, data:5, lt]` succeeds while `[data:6, data:5, lt]` fails. The 'lt' operator primarily exists for deterministic iteration on dicts.

The total order is arbitrary but deterministic:

* numbers LT lists LT dicts
* number n LT n+1
* lists compare lexicographically
* dicts compare as a list of `[[key1, val1], ...]` pairs, sorted by key.

*Aside:* I originally was using dicts compare all keys before any vals, but that's both awkward to implement and doesn't work nicely with sorting dicts containing optional fields.

### Assertions? Defer.

Assertions don't need special operators. They can be expressed thusly:

        assert { P } = try { try { P } then { fail } else {} } then { fail } else {}

I'm still contemplating whether it's worth adding an operator for assertions. If I use a lot of them, it might be worthwhile. Let's defer this.

### Annotations

prog:(do:Program, ... Annotation Fields ...)
stow

Use of 'prog' represents a program or subprogram header with ad-hoc annotations. Use of 'stow' proposes moving the top stack element to content-addressed storage. Application of stowage is transparent and heuristic, and might be deferred lazily (e.g. performed by a GC pass to reduce memory pressure).

### Environment and Effects

env:(eff:Program, do:Program)
eff

The 'env' operator will take the top stack value as initial state, run the 'do' program, then place the final state back onto the stack. The 'eff' operator will invoke the current handler in context of the environment's state above the caller's stack.

For now, I'll restrict 'eff' arity to only observe the top element on the caller's stack (the request), and to always produce a single value (the response). This might change when static analysis is mature. 

### Arithmetic

add, mul, sub, div, mod

Arithmetic removes two numbers on the stack and adds one number. Use of 'sub' will fail if it would result in a negative number. Use of 'div' or 'mod' will fail with a 0 divisor.

### Lists

pushl, popl, pushr, popr, split, join, len

A logical reverse operator is feasible, but I'm uncertain of utility or full consequences. An indexing operator is feasible, but I'd prefer to encourage cursor/zipper-based approaches to iteration - i.e. split first, then operate in the middle.

### Dicts

tag, find, erase, union, keys

One goal here is to simplify elimination of tag-select pairs.

Use of 'case' is almost the same as 'get' but also fails if the dictionary has more than one key. This supports variant data types. Use of 'keys' is for iteration and is probably not efficient for large, dynamic dictionaries.


## Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a dict of form `(compile:Program, ...)`. This program should at least pass a simple static arity check. The language module can be extended with other fields for documentation and tutorials, interactive interpreter, language server component, etc..

The compile program is a value that represents a function from source to compiled value with limited log and load effects. A viable effects API:

* **log(Message)** - Response is unit. Messages may include warnings and issues, progress reports, code change proposals, etc.. 
* **load(ModuleID)** - Module ID is typically a symbol such as `foo`. Response is the copied from compiling the module, or failure if the module's value cannot be computed. 

Load failure may occur due to missing modules, ambiguous file names, dependency cyles, runtime type error, etc.. Cause of failure is not reported. I assume the program model can catch failure. 



## Glas Application Model

See [Glas Apps](GlasApps.md).

## Acceleration

Acceleration is an optimization where we replace an inefficient reference implementation of a  function with a hardware-optimized implementation. Unlike most optimizations, acceleration has the potential to expand the effective domain of a programming language.

For example, Glas programs are inefficient for bit-banging algorithms. However, a Glas subprogram could simulate an abstract CPU that is effective for bit-banging. If we accelerate this CPU, Glas would become effective for compression, cryptography, and other domains that require heavy bit-banging. If the CPU separates code and data (Harvard architecture) and the abstract machine code is statically computed, we could even compile those algorithms ahead of time.

Similarly, acceleration of a simulated GPGPU might be useful for purely functional video processing, machine learning, or physics simulations. Acceleration using FPGA could support flexible hardware simulations. Acceleration of Kahn Process Networks would enable builds to conveniently scale as distributed computations.

Accelerators are high-risk because it's easy for the optimized implementation to deviate from the reference, especially with multiple ports. This can be mitigated by fuzz testing against the reference, and by favoring acceleration of a few 'generic' models (abstract code for CPU or GPGPU) instead of acceleration of many fixed functions (such as specific hash functions and floating point arithmetic).

To simplify recognition and to resist invisible performance degradation, acceleration should be explicitly annotated within a program. A compiler or interpreter should report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target).

## Glas Object

See [Glas Object](GlasObject.md).

## Automated Testing

Within a Glas folder, any module with name `test-*` should be implicitly processed for automatic testing purposes even if its value is not required by the `public` module. An error result or annotation would be reported to the developers.

Testing of programs or binary executables often requires simulating their evaluation. This is feasible via accelerators that can simulate a virtual machine for running a binary.

## Thoughts

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas is intended to provide a viable foundation for this idea, but it's still a distant future.

This does influence definition of language modules to support working with error, such that we can create indexing catalog modules that don't automatically break when any module is broken.

### Provenance Tracking

I'm very interested in potential for automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. However, I still don't have a good idea about how to best approach this.

### Graph-Based Data

I could change Glas to support graph-structured data. 
