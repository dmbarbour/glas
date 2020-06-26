# Glas Language Design

## Module System

Glas modules are represented by files and folders. 

A request for `module:foo` refers to a local file or folder such as `foo.g` or `foo.xyz` or `foo/`, within the same folder as the file processed. Ambiguous references raise errors. Request for `package:foo` searches along `GLAS_PATH` environment variable for folder `foo/`.

Glas does not specify a package manager. The current intention is to leverage Nix or Guix, which are designed for reproducible builds, concurrent build environments, and caching. 

A Glas module deterministically computes a value.

To compute a value for file `foo.ext`, a compiler loads module or package `language-ext`. The *Language Module* should represent a program to process the binary and compute a value. This program has access to limited compile-time effects, including requests for other modules.

To compute a value for a folder `foo/`, a compiler uses the value of the contained `public` module if it exists, otherwise a dictionary whose value reflects the folder structure. Public modules provide more control over what a folder exposes, and allows non-dictionary values.

A request for a module returns its value. Computed module values can be heuristically cached to support incremental compilation. Module dependencies must form a directed acyclic graph.

*Note:* Glas also exhibits modularity within the *Program Model*. Large programs can be organized into reusable components and concurrent processes. However, to the module system, Glas programs are just another structured value.

## Compilation Model

A subset of Glas modules should compute externally useful binaries, perhaps representing music, images, documents, tarballs, or an executable. The Glas command-line tool will provide methods to extract binary values from a module.

To usefully produce an executable binary requires all the normal functions of a compiler: type checking, optimization, register allocation, code generation, etc.. In Glas, these will be normal user-defined functions, distributed by normal packages.

The Glas command-line tool may privately JIT-compile and cache programs for performance reasons. However, the client's binary should be independent of this compiler, computed deterministically from module definitions.

We could specify a default compilation target by defining a system-info package.

*Note:* The command-line tool should support binary extraction from either a list of bytes or a program that produces a stream of bytes.

## Acceleration

Hardware acceleration can support high performance computing. 

For example, we could develop a subprogram that simulates a programmable processor. This abstract processor has a static set of registers, binary memory, and support for bit banging and floating point operations.

If we annotate this subprogram for acceleration, a compiler (or interpreter) should recognize and substitute for actual processors. If acceleration fails for any reason (unrecognized, deprecated, no support on target, resource limits, etc.), the compiler should alert programmers to resist invisible performance degradation.

Acceleration of a processor would support compression, cryptography, signal processing, and other domains. Acceleration of a GPGPU could support machine learning and rendering. Without acceleration, Glas is effectively locked out of some problem domains for performance reasons.

*Note:* Accelerators are huge investments involving design and development, maintenance costs, portability challenges, and security hazards. For best return on investment, accelerate programmable hardware.

## Automatic Testing

A simple convention to support automatic testing: in a Glas folder, any module with name `test_*` should be processed even if its value is not required.

*Note:* Testing of compiled executable binaries is theoretically feasible using accelerated simulation of a machine. However, in the short term, we'll need to test executables by more conventional means.

## Data Model

Glas values are simple structured data, composed of immutable dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], extid:true)`.

Variant data is typically encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. The empty dictionary `()` can serve as a unit value. Symbols are generally encoded as variants with unit value.

Glas systems will represent large lists using [finger trees](https://en.wikipedia.org/wiki/Finger_tree). This usefully supports efficient access and updates, especially at both ends. Binary lists are further optimized by [rope-style](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) chunking. 

Glas natural numbers do not have a hard upper limit, and bignum arithmetic is supported. However, high performance computing with Glas will certainly rely on *Acceleration*.

## Content Addressed Storage

Glas supports larger-than-memory values using content-addressed storage: a subtree can be serialized for external storage on a larger but higher-latency medium such as disk or network. 

The binary representation will be referenced by secure hash. Use of secure hashes simplifies incremental and distributed computing:

* provider-independent distribution, validation
* can encrypt data, use part of hash as AES key 
* persistent data structures, structure sharing
* incremental upload and download by hash cache
* efficient memoization, matches on tree hashes

A Glas runtime may heuristically store values to mitigate memory pressure, similar to virtual memory paging. However, programmers may have a more holistic view of which values should be stored or speculatively loaded. So, Glas programs will support a few operators to guide use of storage. 

The [Glas Object](GlasObject.md) representation is designed to support content-addressed storage.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries may include a cryptographic salt (among other metadata). To support incremental computing, this salt should be computed based on a convergence secret. However, it prevents global deduplication.

## Computation Model

The Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs): Concurrent processes communicate by reading and writing channels. Use of channels is restricted to ensure a deterministic outcome. Deadlock is a potential outcome.

Glas process networks are constructed procedurally. A procedure may read and write channels, allocate channels, close channels, and fork subroutines as processes. Plus the normal loops, arithmetic, etc.. Channels may be transferred over other channels. This supports large or dynamic process networks.

Glas channels have affine type: they may be moved or transferred, but not shared or copied (modulo effects). This may be enforced dynamically, but should be enforced via static analysis where feasible.

Glas channels have bounded-buffers. Bounded-buffers can be modeled from a `(ready, data)` pair of unbuffered channels: a writer reads `ready` then sends `data`, reader sends a token to `ready` then reads `data`. Static buffer size is the initial number of ready tokens. A zero-buffer channel supports a rendezvous pattern, where the writer always waits on the reader.

Glas channels may be closed, representing the end of a list or stream. If the reader endpoint is closed, further reads may drain the buffer but will not send new ready tokens. 

## Effect Models

Glas programs can be extended with effects via channels or static dependency injection.

Channels offer a natural route to effects. A runtime could provide a duplex request-response channel, then handle filesystem and network requests. But it's awkward and unscalable to route requests from multiple processes through one channel.

With static dependency injection, we model an effectful program as depending on an abstract collection of effectful procedures. These procedures cannot be directly implemented within Glas, but may be provided by the compiler then integrated directly into the generated machine code. This also supports concurrent requests.

For performance and convenience, we should favor static dependency injection by default. Channels should be used where the affine, sequential nature is a natural fit.

Static injection and channels share a useful property: they are explicitly routed through a program. A parent program can restrict or intercept effects used by a subprogram. This simplifies testing, monitoring, and abstraction of effects.

*Note:* Under to the Glas compilation model, programmers may experiment with alternative program models not based on process networks. However, process networks are a good basis for general purpose programming.

## Program Model

Glas programs are represented by structured values, which are interpreted as programs in specific contexts, such as language modules. 


given a standard interpretation. The design goals for a program mod


The Glas program model has built-in support for static injection of procedures into subprograms. There are motives for this unrelated to effects: improved code reuse, smaller programs, generic programming, recursive dependencies. However, this also simplifies effects: 


Glas programs are represented by structured values. 

 to simplify code reuse and mutually recursive dependencies. However, a related benefit is that we can easily model subprograms that depend on an `effects` component. 












 be 'injected' into the subprogram. 

on some external values or procedures that it cannot locally define, hence they are provided at another layer.




Glas also supports static injection of values  subroutines into a program.

Glas explicitly supports static dependency injection for many reasons: generic programming, compression, static analysis, performance. 



Glas supports static dependency injection primarily for generic programming and reusable code. A subprogram may have partial dependencies, which must be st bound by a parent program. 





*Note:* The Glas computation and program models are adequately effective or extensible for most use cases. However, the Glas compilation model can support exploration of alternatives.


A runtime could also support a request to `fork` the effects handler, responding with a new request-response channel pair. This would support concurrent requests. 

At that pExplicit routing of the channel would still provide some benefits with respect to controlling, monitoring, or sandboxing use of effects. However, 



However,1 if we can fork channels, then we're effectively routing a first-class function through the program.

At this point, the main benefit from using a 'channel' 

The channel concept is only offering 

However, at this point, we've effectively reinvented procedural effects. The only advantage is that explicit routing provides an opportunity to sandbox, beit with explicit routing.




is bullshit. We're just routing some procedures dynamically throuth

we just have an extra hassle of explicitly routing a channel through the program. 



But let's consider a special request: to `fork` the request-response channel, returnin


It's also feasible to request for an effects channel to self-replicate, to support multiple concurrent requests.

A disadvantage of using channels is that they can be awkward to explicitly thread through a program and optimize. Further, if we wish to self moment we start self-replicating channels, 












 The runtime could support req

he ru

Glas supports interactive via channels. A safe, simple mechanism to support effects 

 so the most obvious mechanism to introduce effects is to introduce a r

 runtime or the outside world - filesystem, network, console.


Glas programs are readily extended with *effects* either at the channels 


with *effects* at two different layers. 


The most obvious and safest mechanism



However, process networks are very suitable for general purpose programming. They support a smooth transition between purely functional and effectful programs. To model effects, it is sufficient to provide some channels to access the runtime or outside world. 

For example, we could provide a request-response pair of channels to a top-level procedure, then implement ad-hoc requests for filesystem, network, and console access, or perhaps operate at a lower level such as system calls or FFI. Non-determinism could be introduced via requests to copy channels, or to `select` the a channel with data from a list of channels. 

To support concurrent requests, it's useful if the runtime also supports a request to fork a new request-response pair. This would enable process networks to model conventional threads.

## No Eval

Glas does not provide any built-in operator to evaluate a program. Glas procedures are essentially second-class. Glas does mitigate this limitation:

* staged programming via language modules
* linear objects modeled via KPN channels

That is, Glas supports higher-order programming without eval under a constraint that integration is static or linear.

There are indirect paths to support evaluation: 

* compile for abstract machine; accelerate
* evaluation effect via channel to runtime

Evaluation via compilation to an accelerated abstract machine is an optimal solution. It can provide ad-hoc support for reflection, quotas, deadlock detection, etc. 

Evaluation as an effect is a brute force solution. This solution is used by language modules, and could be provided by any runtime that includes a JIT compiler.

## Language Modules

Language modules are effectful programs, but the effects are limited to ensure deterministic outcome based on inputs:

* load module or package values
* report progress, warnings, etc.
* dynamic evaluation

The language module may include several procedures beyond the primary one to compile a file to a value. Some might support external tooling, such as projectional editing or auto-formatting.

## Provenance Metadata

Glas modules hides the provenance of a value; the client of a module  only observes its computed value, not how it was derived. However, it is feasible to augment a Glas evaluator to trace the primary influences on values and produce corresponding files.

This could be augmented by annotations within Glas programs to explicitly support provenance tracking, e.g. to support user-defined equivalence regions, human meaningful names.

## Type Checking and Static Analysis

Glas programs may be analyzed by other Glas programs, via their structured value representation. Glas programs may include type annotations to support this analysis.

However, process networks are a challenge to type check due to use of channels. For example, consider a simple request-response channel: there is a response AFTER the request; the response type depends on the corresponding request's value; must read response before next request.

[Session types](https://groups.inf.ed.ac.uk/abcd/) can help in many cases by describing common patterns for use of channels. However, there will inevitably be limits of what any type description language can conveniently express.

It is feasible to take a path of global analysis, e.g. with inspiration from the [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/).

