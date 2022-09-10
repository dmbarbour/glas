# Glas KPN

[Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) express deterministic computation in terms of concurrent processes that communicate by reading and writing channels. KPNs are very scalable, able to leverage distributed processors in a controlled manner. To support most use-cases, KPNs are readily extended with temporal semantics and dynamic connections.

I initially intended to support KPNs as the initial program model for Glas systems. I eventually decided against this for reasons of simplicity, but I still believe KPNs can play a very useful role in Glas systems whether we compile KPNs into Glas applications or to an accelerated virtual machine.

## Temporal KPNs

A temporal KPN assigns an implicit logical time to every process and message. Temporal semantics support flexible composition of concurrent systems, for example to deterministically merge data from several asynchronous channels into one.

A process can detect when there are no messages 'immediately' available on the channel (without waiting). A process can explicitly wait for a message to be available on any/all of a list of channels, or explicitly sleep. Whenever process time advances, all outbound channels are implicitly notified that there will never be any more messages for prior logical times.

## Dynamic KPNs

Dynamic networks can be expressed if our channel API supports:

* attaching channel endpoints over channels
* accepting channel endpoints from channels.
* closing channel endpoints 
* allocating new channel pairs
* wiring channels together permanently

This could be sombined with some operations to spawn processes, handing control for a subset of channels to the process until it terminates. 
