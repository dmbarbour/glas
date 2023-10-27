# Kahn Process Networks for Glas

[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) express deterministic computation in terms of concurrent processes that communicate entirely by reading and writing channels. KPNs are able to leverage distributed processors by partitioning the process network. Their main weakness is that, insofar as they are deterministic, they do not directly handle network disruption or partitioning. 

Also, KPNs do not support backtracking - it is feasible to model distributed transactions, but it would require explicit support from the processes.

I'm interested in developing KPNs as a program model for glas systems. This could be provided as an accelerated model, but also as a basic model for language modules and applications. KPNs can make it easier for glas systems to model 'large scale' compile tasks, such as rendering video. However, even without KPNs, it is still feasible to use annotations to guide parallel evaluation if we avoid effects.

## Static vs. Dynamic KPNs

It is not difficult to model 'dynamic' process networks, where the set of processes and network links may vary over time. Dynamic KPNs have a simpler API and better flexibility and scalability. OTOH, static KPNs are easier to debug, analyze, optimize. 

I lean towards dynamic KPNs, at least initially, for their flexibility, avoidance of names, and convenient integration of concurrent effects. 

### Modeling Static KPNs

With static process networks, all subchannels and subprocesses are labeled statically. We can construct these declaratively: every process has a labeled set of subprocesses, a set of labeled input ports, a set of labeled output ports, a list of messages pending on each port, an anonymous set of internal 'wires', and a procedural main task. 

For consistency, the external boundary of a process might be referenced as a special 'io' subprocess. For example, to reference port 'x' on subprocess 'foo' we use 'foo:x'. To reference port 'x' on the outer process, we could use 'io:x'. Input and output ports with the same name can be distinguished contextually (i.e. if you're reading from it, it must be an input port).

Wires bypass the main task, implicitly forwarding data from one channel to another. It is possible to wire input channels directly to output channels, or to a subprocess. Wires between subprocesses are more efficient than using the procedural main task to read from one subchannel then write to another. 

The main task would be representing using a procedural sublanguage. This could be represented using the 'prog' model or something more specialized. There is no support for concurrency at this layer - any concurrent operations must be represented by subprocesses. 

The main issue with static KPNs is probably binding them to effects in an application. In practice, we might want multiple concurrent bindings to the filesystem, for example. It is feasible to represent this binding as a separate declarative configuration.

### Modeling Dynamic KPNs

A port represents one endpoint of a duplex channel. It is possible to send and receive data and ports through a channel. Spawning a subprocess immediately returns a port connected to that subprocess. The subprocess starts with one port, representing the other end of that channel, and nothing on the data stack. The initial connection can be used directly, or leveraged to establish fine-grained channels for concurrent subtasks. In many cases, initialization can be partially evaluated to build the initial process network.

I propose to separate ports and data onto two separate stacks. This simplifies many problems for the runtime and semantics, especially tracking of ports and copying data, though will require using subprocesses to model nodes in a 'routing' structure (distinct from data structures).

Dynamic KPNs are vastly more flexible than static KPNs, but they're also more complicated to optimize and distribute, requiring runtime dataflow analysis and distributed garbage collection. Static KPNs probably align better with my simplicity goals for glas systems.

## Temporal KPNs

Temporal KPNs extend the KPN with logical time, enabling a process to wait on multiple input channels and process the 'first' message. Temporal KPNs greatly simplify modeling of real-time systems, clocks, and routing... and they better align with intuitions about how a process can work with multiple asynchronous event channel. 

Normally, a KPN must wait indefinitely upon 'read' for data. This is how determinism is guaranteed. A temporal KPN effectively adds time step messages, such that a read can return early with a 'no data yet, wait for later' result. This enables simulation of polling multiple channels. A process may separately 'wait', which implicitly sends a time step to all output channels.

In practice, it's convenient if those 'time step' messages are very fine-grained. Perhaps corresponding to real-time nanoseconds. In any case, we don't want to run the process loop at the same frequency as the time step. Instead, we want an effect to 'wait' on data from multiple input channels or a timeout, whichever comes first.

So, in terms of API, temporal KPNs only need a couple extensions:

* the ability for reads to fail with 'try again later'
* the ability to wait on multiple input channels and a timeout

Wait messages implicitly propagate to all the output channels, but can also be implicitly combined in-buffer, e.g. `[time-step:1, time-step:2, ...]` combines to `[time-step:3, ...]`. Conversely, waiting removes time-step messages from input channels. Every input channel could add waits to a local time register, then cancel time-step messages while reducing the time register.

When binding to external effects, we'll need to inject appropriate time-steps. For example, to bind a 40kHz input stream, we might insert a `time-step:25000` message immediately after each data value, assuming time-steps correspond to real-time nanoseconds. (Though, in practice, we might prefer to model a buffered stream with lower frequency, larger blocks of data.)

In any case, temporal KPNs add a lot of value to KPNs without adding too much complexity or overhead. They make a lot of problem domains more accessible. They better match intuitions about time. I think they're a worthy default feature.

## Acceleration of Processes

It is possible to extend the process model with some specialized processes such as map or filter that are easily optimized by a compiler. Alternatively, we could try to model these as a form of 'acceleration' of processes without extending semantics. I currently favor acceleration.

This requires at least one annotation header for processes.

## Proposed Program Model 

### Basic Data Manipulation

* *copy* - duplicate top item on data stack, adds one item to data stack
* *drop* - remove top item from data stack
* *dip:P* - temporarily hide top item on data stack while evaluating P
* *swap* - switch top items on data stack
* *data:V* - add copy of value V to top of data stack
* *seq:\[List, Of, P\]* - 

* *put*
* *del*
* *get:(then:P1, else:P2)* - (R|K?V) K, if K is present evaluates P1 with V on stack, otherwise evaluates P2 with R on stack. (Either way K is dropped.)
* *eq:(then:P1, else:P2)* - V1 V2, if V1=V2 then eval P1, otherwise eval P2. Does not remove data from stack.

* *halt:ErrorType* - divergence. Same as entering an infinite do-nothing loop, except more efficient. No 'fail' for KPNs.

* *proc* - annotations as needed.

...

### Extensions for Dynamic KPNs

Channels represent a communication 'endpoint'  are duplex by default, representing the ability to interact with a remote process. The remote endpoint is

* copyc
* dropc
* swapc
* dipc

* send
* sendc
* recv:(data:P1, chan:P2, time:P3)
* poll:[P1, P2, ..., PK] - wait for data to become available on at least one of the top K channels on the stack. Behavior is specified per channel. If two channels have data at the same time, always selects topmost. Use with 'timeout' to limit wait time.

* pulse - create a timer that will trigger with some constant data every given number of time steps (at least 1). Use with 'poll' for timeouts.
* spawn
* wire - creates a pair of connected channels

* bind - connect top two channels from stack, such that reads from each are written to the other. Both channels are removed from the stack.

 Reads from C become reads from C2. Reads from C1 are implicitly written to C2.

top two channels on stack channels permanently. Returns a single channel, such that writes, i.e. so reads from one become inputs to the other and vice versa. Returns a single channel, such that writes become



