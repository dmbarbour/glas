# Glas Language Design

## Module System

Large Glas programs are represented by multiple files and folders. Each file or folder deterministically computes a value.

To compute the value for a file `foo.ext`, search for a language module or package `language-ext`, which should include a program to process the file's binary. To compute a value for a folder `foo/`, use the value of the contained `public` module if one exists, otherwise a simple dictionary reflecting the folder's structure.

Language modules have access to limited compile-time effects, including to load values from external modules or packages. File extensions are elided. For example, loading `module:foo` could refer to a local file `foo.g` or `foo.xyz`, or a local subfolder `foo/`. Loading `package:foo` would instead search along the `GLAS_PATH` environment variable for a folder `foo/`.

Ambiguous references, directed dependency cycles, invalid language modules, or errors during processing would be raised as compile-time errors.

Glas does not provide its own package manager. Current intention is to leverage Nix or Guix. 

*Note:* Glas specifies a *Namespace Model* for definition-level programming. Programmers will likely find it familiar and convenient if most modules compute values that represent namespaces.

## Compilation Model

A subset of Glas modules compute externally useful binaries, perhaps representing music, images, documents, tarballs, or an executable. The Glas command-line tool provides methods to extract binary values from a module from a list or a program generating a stream of bytes.

To produce an executable binary, the type checking, optimization, and code generation will be modeled as Glas programs, ultimately driven by a language module. To produce binaries specific to a system, a system-info package can describe the default compilation target.

The application behavior can be represented by a value from another package. As a convention, appname-model and appname-exe packages should be separated to simplify extension or composition of applications, model testing, experimentation with compiler parameters, etc..

*Note:* The Glas command-line tool may privately compile and cache programs for performance, e.g. compile language modules into plugins specific to the command-line tool. This should be mostly hidden from normal users of the tool.

## Acceleration

Acceleration is a pattern to support high performance computing. 

For example, we can develop a subprogram that simulates a programmable processor. This abstract processor may have a static set of registers, binary memory, and support for bit banging and floating point operations. We annotate this subprogram for acceleration. 

A compiler (or interpreter) should recognize the annotated subprogram and substitute an actual processor, performing a little translation as needed. If acceleration fails for any reason (unrecognized, deprecated, no support on target, resource constraints, etc.), the compiler should alert programmers to resist invisible performance degradation.

Acceleration of abstract CPUs would open a variety of problem domains where performance is a deciding factor: compression, cryptography, signal processing, etc.. Abstract GPGPUs or FPGAs are also valuable targets.

*Note:* Accelerators are major investments involving design and development, maintenance costs, portability challenges, and security hazards. Fixed functions have poor return on investment compared to abstract programmable hardware.

## Data Model

Glas data is immutable. Basic data is composed of dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. The empty dictionary `()` can serve as a unit value. Symbols or enumerations are encoded as variants with unit type. 

Glas uses lists for all sequential structure: arrays, binaries, deques, stacks, tables, tuples, queues. To efficiently support this gamut of applications with immutable data, Glas systems will represent large lists as [finger trees](https://en.wikipedia.org/wiki/Finger_tree). Binaries also use [rope-style chunking](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) to minimize overhead.

Glas natural numbers do not have a hard upper limit, and do support bignum arithmetic. Glas does not have built-in support for negative integers or rationals or floating point or other numeric types, but they could be modeled. Note that Glas is not suitable for high-performance numeric computing without *Acceleration*.

## Content-Addressed Storage

Glas will support larger-than-memory data structures using content-addressed storage: a subtree may be serialized for external storage on a large, higher-latency medium such as disk or network. 

The binary representation will be referenced by secure hash. Use of secure hashes simplifies incremental and distributed computing:

* efficient memoization, matches on tree hashes
* persistent data structures, structure sharing
* incremental upload and download by hash cache
* provider-independent distribution, validation

A Glas runtime may heuristically store values to mitigate memory pressure, similar to virtual memory paging. However, programmers may have a more holistic view of which values should be stored or speculatively loaded. So, Glas programs will support a few operators to guide use of storage. 

[Glas Object](GlasObject.md) is designed to serve as a standard representation for large Glas values with content-addressed storage.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries will include a cryptographic salt (among other metadata). To support incremental computing, this salt must be computed based on a convergence secret. However, it prevents global deduplication.

## Computation Model

The Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) to support scalable computation and expressive composition under constraint of determinism.

KPNs consist of concurrent processes that communicate by reading and writing channels. Channels may be externally wired between processes, supporting open composition of cyclic dependencies. Use of channels are restricted to ensure a deterministic outcome. 

Glas does not directly model the writer's end of a channel. Instead, channel output is intrinsic to certain processes. For example, an unfold process might repeatedly apply a function to generate a channel.

Glas uses bounded-buffer channels, such that a fast writer will wait on a slow reader. Conceptually, buffered channels are a coupled `(ready, data)` pair of unbuffered channels with dataflow in opposite directions. Writer reads ready token then writes data. Reader writes ready token then reads data. Buffer size is the maximum number of ready tokens. Zero-buffer supports rendezvous pattern.

Glas channels may be 'closed' from either end, modeling end of list for data or ready tokens. This enables Glas to easily represent sequential composition of channels, or to effectively model termination behaviors.

## Program Model

The Glas program model is based on [arrowized](https://en.wikipedia.org/wiki/Arrow_%28computer_science%29) [Kahn process networks](https://en.wikipedia.org/wiki/Kahn_process_networks) via [concatenative](https://en.wikipedia.org/wiki/Concatenative_programming_language) [combinatory logic](https://en.wikipedia.org/wiki/Combinatory_logic).

Concretely, a Glas program is represented by a list of operators. Each operator is represented by variant data. The list models sequential composition. Each operator represents an abstract function, often a combinator whose argument is a static subprogram.

Glas programs operate on data structures extended with bounded-buffer channels and transparent futures. Functions on partial data are implemented by long-lived processes. These processes compute output incrementally and monotonically based on readiness of buffers and availability of inputs.

The fixpoint loop combinator is essential for process networks, modeling cyclic dataflows, piping channels from the right-hand side of a subprogram back to its left-hand side. In Glas, this is supported via transparent futures, and implementation relies on concurrent, opportunistic computation of operators. 

Most Glas primitive operators assume input and output is structured as a heterogeneous list, modeling a Forth-like data stack. This simplifies the operators because data source and destination are implicit.

Operator definitions are in a later section.

## Namespace Model

Definition-level programming is concise, accessible, extensible, composable in more dimensions, and human-friendly. At least in contrasted to lists of primitive operators.

Glas supports definition-level programming with operators to extend a namespace in scope of a subprogram, and to apply defined symbols. Names within a Glas program are statically resolvable, up to an initial namespace provided by runtime or compiler.

To support higher-order and generic programming, Glas supports virtual symbols with overrides. Overrides can model parameters, while the initial definitions model defaults.

Glas forbids recursive definitions. Loops must be expressed using primitive loop combinators. It should be possible to erase namespace operators by transitively inlining definitions. Namespaces contribute to expression, not meaning.

Namespaces are concretely represented as dictionary values with standard fields:

* **program** - A program, the main behavior of a namespace, used by the namespace operators.
* **define** - A dictionary of `symbol:Namespace` pairs, each defining a symbol. For the common case of defining subprograms, `symbol:program:[code]` is sufficient.
* **extend** - A symbol, or unit. This identifies the namespace we search for definitions that are not locally defined. Default is same as *import* field. Unit is empty namespace.
* **parent** - A symbol. A namespace is implicitly parameterized by its parent namespace, but we might want a different name in some cases. Default `parent`.
* **virtual** - A set of symbols, represented as a dictionary. Definition of virtual symbols may be deferred or overridden upon extension. Non-virtual symbols would be shadowed instead.

This should be sufficient for operation, though we might introduce some ad-hoc fields to represent exports, override intentions, types, etc. to a linter or type checker.

The Glas namespace model has much in common with OOP classes or prototypes. However, the second-class, static nature of namespaces will align usage patterns more closely with ML functors. 

## Effects Model

Glas programs can be extended with effects. This doesn't require any changes: effects can be modeled as abstract, user-defined operators in the top-level namespace, provided by compiler or runtime. 

This design is similar to conventional procedural effects. However, there are two noteworthy differences:

The namespace model is expressive enough that a program can restrict or attenuate a subprogram's access to effects. This can simplify testing in restricted environments.

Effects in Glas are aggressively concurrent, running opportunistically based on available data. This can be mitigated via primitive synchronization operators or careful design of the effects model.

*Note:* The Glas compilation model can support alternative program models for applications and effects. However, the Glas program model should be adequate for most use cases.

## Notable Exclusions

To keep it simple, the Glas program model *does not* have primitive operators for first-class functions (`program -> data`) or evaluation (`data -> program`). 

Absence of first-class functions is mitigated by other features:

* Channels serve many roles of continuations: await input, emit output, cyclic interactions.
* Namespace overrides support higher-order programming under constraint of static linking.

Consequently, Glas can abstract loops over lists or implement stream generators, but existential types or conventional OOP objects are not supported.

Evaluation is provided as an effect to language modules. This requires the runtime to include an interpreter or JIT compiler. 

Evaluation can feasibly be implemented by interpreter or compiler to an abstract machine that is interpreted. This would make evaluation available without an effect, but would require acceleration for performance.

## Automatic Testing

Simple conventions to support automatic testing: 

In a Glas folder, any module with name `test-*` should be processed even if its value is not required. Language modules could support ad-hoc tests internally. 

In a Glas namespace, any symbol `test-*` could be processed as a sanity check, perhaps waiting for final overrides.

Automatic testing within files is left to language definitions and *Language Modules*.

Deterministic testing of computed executable binaries is theoretically feasible via accelerated simulation of a machine, fuzzing the race conditions. However, in the short term, we canto test executables by more conventional means.

## Meta: Type Annotations

## Operators

### Stack Manipulation

        data
        void
        copy
        drop
        swap
        sip
        dip
        wrapl
        unwrapl

### Arithmetic

### Dictionary Values

### List Processing



### Channel Processing

### Fixpoint Loop

### Conditional Behavior


        fail

### Component System

### Synchronization

Ways we can synch:

* wait for a value before starting subprogram
* wait for channel to finish before producing value
* wait for multiple values before producing value
* 


        synch:[X->Y]   - wait for an unrelated input before continuing

### Content-Addressed Storage

### Memoization

### Distribution



# Old Junk

## Meta: Type Annotations

Most Glas operators assume the environment models a Forth-like 

 the environment is structured as a right-associative tuple `X * Y * S = (X * (Y * S))` modeled as a heterogeneous list such as `(cons X (cons Y S))` or `(X Y . S)` (in Lisps).


, modeled by heterogeneous list, as the data stack. Head of list is top of stack. Exceptions are operators `box` and `unbox`. Consequently, Glas benefits from a few ad-hoc stack type annotations.

* `X -> Y` is a normal function type. 
* `X * Y * S` is type for tuples, right associative `X * Y * Z = X * (Y * Z)`. The  
* `X * S` is type for list/tuple constructors, right associative.
* `X * Y * S` is shorthand for list prefixed with elements `[X,Y]`, followed by abstract list `S`. 
* `X Y -- Y X` is shorthand for `forall S. X*Y*S -> Y*X*S`.





Glas operators often represent some combinators with static parameters. These are represented together with the operator. For example: `sip:[X -> Y]` represents that operator `sip` takes one static parameter, a program (list of operators) w type `X -> Y`.

* `[X -> Y]` represents a program (a list of operators) interpreted as function of type `X -> Y`.





## Operators

Most operators assume the environment is structured as a heterogeneous list, modeling a Forth-like data stack. However, the box/unbox operators will support non-stack types.

Within Glas, new channels are mostly generated by unfold operations, which might otherwise generate unbounded lists. 


* Loops are modeled as a fixpoint behavior: abstract channels and values produced by a subprogram are routed as inputs to the same subprogram, e.g. `loop:[list of ops]`.
* Conditions are modeled as processes that mux or demux based on a selector input. Conditions will activate or disable regions of a network, perhaps even statically (via closed channels), but do not affect its construction.

Glas channels may be copied like normal values and broadcast to synchronous readers. Bounded-buffer pushback is based on the slowest active reader. The implicit ready channel will close only if there are no more readers.



A missing channel input to a process will default to a closed channel, unless some default is explicitly provided.

Higher order programming deserves special attention. A Glas process may capture a subprogram as a first-class function, and route it as a parameter to other processes. 


cannot directly loop or branch based on input values. Loops are fixpoint constructs, routing an output from a subprogram to its input. Conditional behavior could be modeled by a primitive process that selects between inputs based on a third input, but cannot select between subprograms.

### Graph Rewriting vs Abstract Evaluation

A predictable graph-rewrite semantics might simplify presentation of evaluation. The simplest model of rewriting is erasure: when a process is finished making decisions, it might 'become' a simple rename operation.

Glas does not have a strong use-case for a graph rewrite semantics. However, it is useful and not difficult to ensure a rewrite semantics can be expressed: it is sufficient that primitive processes can be  expressed by local rewriting. 

A graph rewrite semantics will essentially allow Glas programs to represent 'interaction networks'. 

## Language Modules

Language modules are effectful programs, but the effects are limited to ensure deterministic outcome based on inputs:

* load module or package values
* report progress, warnings, etc.
* proposal of corrections to input
* evaluation of values as programs
* reflection? procedures as values

Language modules should at least have one function to 'compile' an input file to produce a plain Glas value. There may be additional functions to support projectional editing, auto-formatting, decompilation, etc..

## Provenance Metadata

Glas modules hides the provenance of a value; the client of a module  only observes its computed value, not how it was derived. However, it is feasible to augment a Glas evaluator to trace the primary influences on values and produce corresponding files.

This could be augmented by annotations within Glas programs to explicitly support provenance tracking, e.g. to support user-defined equivalence regions, human meaningful names.

## Type Checking and Static Analysis

Glas programs may be analyzed by other Glas programs, via their structured value representation. Glas programs may include type annotations to support this analysis.

However, process networks are a challenge to type check due to use of channels. For example, consider a simple request-response channel: there is a response AFTER the request; the response type depends on the corresponding request's value; must read response before next request.

[Session types](https://groups.inf.ed.ac.uk/abcd/) can help in many cases by describing common patterns for use of channels. However, there will inevitably be limits of what any type description language can conveniently express.

It is feasible to take a path of global analysis, e.g. with inspiration from the [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/). Short term, we could output constraints for an external solver.
