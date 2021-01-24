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

## Data Model

Glas data is composed of immutable dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`. A specific data element is called a value. Glas values are acyclic and finite. Large values will often share structure via logical copies.

Dictionaries are a set of `Key:Val` pairs with unique keys. Keys are often short binary strings. An empty dictionary `()` serves as a convenient unit value. Variant data is typically encoded by singleton dictionaries. For example, `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing key `rgb` or `hsl`. 

Glas uses lists for sequential structure: arrays, binaries, deques, stacks, tables, or queues. Although lists could be used for tuples, dicts are favored for extensibility. To efficiently support a variety of applications with immutable data, Glas systems will generally represent large lists as [finger trees](https://en.wikipedia.org/wiki/Finger_tree). Binaries are further optimized via [rope-style chunking](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). 

Glas natural numbers do not have a hard upper limit, but will usually have a degradation point from a high-performance 'smallnum' to a low-performance 'bignum'. Glas is not designed for high-performance numeric computing unless using *Acceleration*. 

## Content-Addressed Storage (CAS)

Glas will support large data structures using content-addressed storage: a subtree may be serialized to disk or network then referenced by secure hash. Use of secure hashes simplifies incremental and distributed computing:

* persistent data structures, structure sharing
* incremental upload and download by hash cache
* efficient memoization, matches on tree hashes
* provider-independent distribution, validation

Glas systems can use content-addressed storage to mitigate memory pressure similar to virtual paging, and to support distributed computations with lazy and incremental communication. However, content-addressed storage is not observable within the program - it's essentially an optimization.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries should have a header space to include a cryptographic salt (and other metadata). This salt prevents global deduplication, but local deduplication is supported by stable convergence secret.

## Language Modules

Language modules have a module name of form `language-*`. The value of a language module should be a dict of form `(compile:Program, ...)`. This program should at least pass a simple static arity check. The language module can be extended with other fields for documentation and tutorials, interactive interpreter, language server component, etc..

The compile program is a value that represents a function from source to compiled value with limited log and load effects. A viable effects handler API:

* **log:Message** - Response is unit. Messages may include warnings and issues, progress reports, code change proposals, etc.. 
* **load:ModuleID** - Module ID is typically a symbol such as `foo`. Response value from compiling the identified module, or `error` if a value cannot be computed.

Load errors may occur due to missing modules, ambiguous file names, dependency cyles, runtime type error, etc.. The cause of error is not reported to the module client, but may be reported by the development environment. There are potential use cases for explicitly returning `error` when compiling a module.

## Program Model

Mature Glas systems can support many program models. However, to get started, we must define the standard program model for language modules. Design goals: trivial to interpret, good performance when compiled, deterministic and cacheable, low barriers for metaprogramming, reusable for runtime applications.

Proposed model: 

Programs are concretely represented by a structured value modeling an abstract syntax tree. There are nodes to express sequencing, conditionals, loops, arithmetic, etc.. Programs are second-class: there is no operator to interpret a runtime value as a program. Programs manipulate a tacit environment consisting of a data stack and effects handler. 

The data stack contains Glas values. There is no separate heap, but large values such as lists or trees may be placed on the stack and manipulated. Programs are designed to support a lightweight static arity (stack effect) analysis in the normal case. A more refined static type analysis can be provided by the .

The effects handler is an abstract object that may be overridden within a subprogram. The runtime or compiler will provide the external effects handler. However, it is also possible to override an effects handler within scope of a subprogram.

Conditionals and loops are based on failure and backtracking. Effects are also backtracked, though external effects might have special handling when canceled. Backtracking is expressive for pattern matching and parsing and suitable for modeling transactions. However, it does prohibit synchronous effects APIs.

## Namespace? Defer.

It is feasible to introduce a namespace layer in the program model, i.e. operators to define and call reusable subprograms by name. A potential benefit is program concision, avoiding replication of code. However, the cost is complication of the model, especially in context of metaprogramming. 

Without a program model namespace, we still have a namespace via the module system and we still have structure sharing at the data layer. A compiler can deduplicate common code. It is also feasible, albeit awkward, to simulate a namespace via effects handler and partial evaluation.

## Operators

### Data Flow

copy, drop, dip:P, swap, data

Use of copy, drop, and swap operate on the top stack values. Use of 'dip' hides the top stack value from a subprogram. Use of 'data' adds a constant value to the stack.

### Control Flow

\[list, of, operators\]
cond:(try:P, then:Q, else:R)
loop:(try:P, then:Q)
fail
eq
lt

The 'lt' operator asserts that the second stack element is 'less than' the top stack element. is fairly arbitrary: numbers before dicts before lists, dicts compare keys befor vals, lists compare lexicographically.

### Annotations

prog:(do:Program, ... Annotation Fields ...)
stow

Use of 'prog' represents a program or subprogram header that permits flexible annotations. It is possible to inject annotations within a sequence via adding annotations to an identity program.

Use of 'stow' indicates content-addressed storage for the top stack element. Application of stowage is transparent and heuristic, and might be deferred lazily (e.g. performed by a GC pass to reduce memory pressure).

### Effects

with:(eff:Program, do:Program)
eff

The 'with' operator will hide the top stack value as initial state from the 'do' program. The 'do' program may invoke the 'eff' handler via 'eff' operator. The 'eff' program should have a `Request State -- Response State` stack effect. Final state is returned to stack upon exit from the 'do' program.

*Note:* It is feasible to extend 'with' to serve as an ad-hoc namespace for defining multiple operators. However, at least for now, only 'eff' may be defined, and any namespace would be modeled within the effects handler.

### Arithmetic

add, mul, sub, div, mod

### Lists

pushl, popl, pushr, popr, split, join, len

A logical reverse operator is feasible, but I'm uncertain of utility or full consequences. An indexing operator is feasible, but I'd prefer to encourage cursor/zipper-based approaches to iteration - i.e. split first, then operate in the middle.

### Dicts

get, set, del, keys

This design makes dicts highly dynamic by default. Static structs and records would be based on computing the keys statically via partial evaluation or abstract interpretation. 

## Glas Application Model

This grew into a separate document. See [Glas Application Model](GlasApps.md).

## Acceleration

Acceleration is an optimization where we replace an inefficient reference implementation of a  function with a hardware-optimized implementation. Unlike most optimizations, acceleration has the potential to expand the effective domain of a programming language.

For example, Glas programs are inefficient for bit-banging algorithms. However, a Glas subprogram could simulate an abstract CPU that is effective for bit-banging. If we accelerate this CPU, Glas would become effective for compression, cryptography, and other domains that require heavy bit-banging. If the CPU separates code and data (Harvard architecture) and the abstract machine code is statically computed, we could even compile those algorithms ahead of time.

Similarly, acceleration of a simulated GPGPU might be useful for purely functional video processing, machine learning, or physics simulations. Acceleration using FPGA could support flexible hardware simulations. Acceleration of Kahn Process Networks would enable builds to conveniently scale as distributed computations.

Accelerators are high-risk because it's easy for the optimized implementation to deviate from the reference, especially with multiple ports. This can be mitigated by fuzz testing against the reference, and by favoring acceleration of a few 'generic' models (abstract code for CPU or GPGPU) instead of acceleration of many fixed functions (such as specific hash functions and floating point arithmetic).

To simplify recognition and to resist invisible performance degradation, acceleration should be explicitly annotated within a program. A compiler or interpreter should report when acceleration fails for any reason (i.e. unrecognized, deprecated, or no implementation for current target).

## Glas Object

[Glas Object](GlasObject.md) will specify a standard binary serialization for Glas values, designed for scalability, simple parsing, content-addressed storage. Suitable for bootstrapping language modules. File extension is `.glob`.

## Automated Testing

Within a Glas folder, any module with name `test-*` could be implicitly processed for automatic testing purposes even if its value is not required by a `public` module. An error result or annotation would be reported to the developers.

Testing of programs or binary executables often requires simulating their evaluation. This is feasible via accelerators that can simulate a virtual machine for running a binary.

## Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program - respectively representing requirements and desiderata and recommendations - then the system searches for valid solutions. Glas is intended to provide a viable foundation for this idea, but it's still a distant future.

## Provenance Tracking

I'm very interested in automatic provenance tracking, i.e. such that we can robustly trace a module's value to its contributing sources. However, I still don't have a good idea about how to make this easy.

