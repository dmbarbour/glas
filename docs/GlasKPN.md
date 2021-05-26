# Glas KPN

## Kahn Process Networks (KPNs)

[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) express programs as concurrent processes that communicate by reading and writing channels. To ensure a deterministic outcome, reading a channel will wait indefinitely. Writes are buffered. In practice, we'll want static analysis to verify bounded buffers.

Compared to lambdas or combinatory logic, KPNs are more expressive for partial evaluation, stream processing, interaction, and state abstraction. This reduces need for special features. 

Useful Variations:

### Static Hierarchical KPNs

Static, hierarchical KPNs with external wiring between components. Instead of first-class channels, each process has second-class IO ports and concurrent loops. The process can read and write IO ports, and also ports of embedded processes. Wiring is a loop that forever reads one port and writes to another. This design is simple, stable, and composable compared to designs based on first-class channels.

### Temporal KPNs

Temporal KPNs implicitly add logical timing metadata to every message, and the interpreter also tracks the logical time of processes. This allows a process to wait on multiple channels concurrently and observe logical races between them. Logical time is deterministic within the KPN, but external inputs could be timed based on real events.

Temporal KPNs are convenient because we can easily and robustly merge event sources, or model periodic clocks within the KPN.

## KPNs in Glas

KPNs are not simple to implement and are not great for pattern matching or live coding. So, I chose to exclude them from the base program model for Glas.

However, KPNs can easily be supported at the Glas application model layer: transaction machines can easily model waiting on input channels and writing to output channels. Channels are easily modeled using memory. We could easily design a program model then compile it for the application layer.

Additionally, it is feasible to support KPNs as an accelerated model within a program. The accelerated KPN could support distribution, parallelism, and concurrency. Effects would become a bottleneck in this case, binding 'eff' to a request-response channel, but they can be avoided or mitigated.

## Program Model (Old)

Programs are concretely represented by a dict of form `(do:[List, Of, Operators], ...)`. The dict header can be extended with annotations and metadata to support performance, presentation, or debugging. Operators are represented as variants with form `opname:(StaticArgs)`. Operators can declare process components and describe dataflow between components. 

A program represents a process. Every process has a set of labeled ports for input and output, like keyword parameters. For example, with a process component labeled `foo`, we might write `foo:arg` then read `foo:result`. Within a program, external process ports are represented by implicit component `io`. For example, writing `io:result` within a subprogram labeled `foo` corresponds to reading `foo:result` from the parent program.

Glas represents most computation as processes. For example, instead of an operator to add numbers, we'd declare a component process for adding numbers then write inputs and read results. Other than declaring components, most operators describe communication between components, including loops and conditional behavior. Operators within a program evaluate concurrently, except that operations modifying the same port will be sequenced according to the list.

Channels and processes are second class in this model, and subprograms are directly included within a program. Macros and metaprogramming are essential for expressiveness, and can be supported by language modules. Structure sharing and content-addressed storage at the data model layer supports efficient representation.
