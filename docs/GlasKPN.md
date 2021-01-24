# Glas KPN

## Kahn Process Networks (KPNs)

[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) express programs as concurrent processes that communicate by reading and writing channels. To ensure a deterministic outcome, reading a channel will wait indefinitely. Writes are buffered. In practice, we'll use static analysis to verify bounded buffers.

Compared to lambdas or combinatory logic, KPNs are more expressive for partial evaluation, stream processing, interaction, and state abstraction. This reduces need for special features. 

Unfortunately, KPNs are relatively awkward to interpret and implement, so I have some doubts about making KPNs into 

A useful variation is static, hierarchical KPNs with external wiring between components. This design improves simplicity, stability, composability, comprehensibility, optimizability, and other nice properties. The cost to expressiveness can be mitigated by metaprogramming.

## Program Model

Programs are concretely represented by a dict of form `(do:[List, Of, Operators], ...)`. The dict header can be extended with annotations and metadata to support performance, presentation, or debugging. Operators are represented as variants with form `opname:(StaticArgs)`. Operators can declare process components and describe dataflow between components. 

A program represents a process. Every process has a set of labeled ports for input and output, like keyword parameters. For example, with a process component labeled `foo`, we might write `foo:arg` then read `foo:result`. Within a program, external process ports are represented by implicit component `io`. For example, writing `io:result` within a subprogram labeled `foo` corresponds to reading `foo:result` from the parent program.

Glas represents most computation as processes. For example, instead of an operator to add numbers, we'd declare a component process for adding numbers then write inputs and read results. Other than declaring components, most operators describe communication between components, including loops and conditional behavior. Operators within a program evaluate concurrently, except that operations modifying the same port will be sequenced according to the list.

Channels and processes are second class in this model, and subprograms are directly included within a program. Macros and metaprogramming are essential for expressiveness, and can be supported by language modules. Structure sharing and content-addressed storage at the data model layer supports efficient representation.

### Loops


### Conditionals


# OLDER

[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) consist of concurrent loops and processes that communicate by reading and writing channels. Reading a channel waits indefinitely to ensure a deterministic outcome. Channels buffer writes, which is suitable for high-latency communications. 

The benefit of KPNs is that they are scalable and expressive, representing flexible communication between subprograms. The disadvantage is that implementing KPNs is non-trivial relative to a combinatory logic or term rewrite model.

I am interested in KPNs for Glas systems, but I'm hesitant to support them at the language module foundation because they would certainly complicate bootstrapping. Instead, KPNs could be supported using accelerators.

## Static KPNs

Static linking trades expressiveness for simplicity, stability, locality, and performance. I propose that Glas KPN processes and channels should be second-class. A program reads and writes statically labeled ports, and may wire ports between statically declared subprograms.

Glas will generally rely on static analysis for type safety, dead code elimination, bounded buffers, and deadlock prevention. Metaprogramming can potentially support laziness, temporal semantics, and other features, using patterns above KPNs. Acceleration is necessary for dynamic programs.

## Program Model

Concretely, a Glas program is represented by dictionary with `code:[List, Of, Operations]`. Each operator is parameterized by statically labeled ports, representing input sources and output destinations. 

External IO ports are accessed via `io:portname`. Local variables, scoped to the program, use `var:varname`. Programs compose hierarchically and statically: for subprogram foo, the external port `foo:portname` maps to `io:portname` within foo. Input and output ports may share the same name (for variables, this is always the case) but are implicitly distinguished by usage context.

Operations on separate ports compute concurrently, but operations modifying the same port are sequenced according to the list. Variables are really two separate ports for writing and reading. A program will typically have concurrent loops that interact via variables and declared subprograms. 

Reading a port is non-modifying. There is a separate operator to advance a read port to its next value, affecting subsequent reads. It is possible possible to push data onto a read port to affect the next read. It is possible to detect end of input. Attempting to read a port after end of input will wait indefinitely.

Writing a port is buffered until read. Although there are no hard limits on buffer size, Glas systems will normally ensure bounded buffers via static analysis.

Wires are primitive loops that read from one port and write to other ports. Wires should be composed and compiled for direct communications across subprogram boundaries instead of routing data indirectly through a parent program.

## Namespace Model

Glas will support definition-level programming with a static, higher-order namespace model. Definitions are resolvable at compile time and acyclic. This ensures namespaces do not influence runtime semantics: we can transitively expand names to primitives, thus namespaces are essentially a compression model.

The envisioned use case is that Glas modules should compute namespace values. This supports a conventional programming style where programs are decomposed into reusable definitions with flexible scoping and export control, then composed as needed. 

The higher-order namespace features can support dependencies, defaults, generic programming, and separate compilation. Favoring namespace-layer dependency injection instead of directly loading modules can reduce duplication and improve flexibility.

Concretely, a namespace will be represented as `ns:[List, Of, Namespace, Operators]`. Namespace operators are much simpler than program operators - oriented around definitions, defaults, visibility. Glas does not support useful computation in the namespace. 

