# Glas Object Channels

The [Glas Apps](GlasApps.md) description includes a preliminary API for data channels within and between Glas systems. This API includes an option for binding to TCP, in which case data will be communicated via [Glas Object](GlasObject.md).

## Proposed Channels API

Summary of API described for Glas Apps, albeit abstracting various reference management issues.

What we can do with channels:

* *send* - send data over a channel.
* *recv* - receive data from a channel 
* *attach* - send a new subchannel over a channel
* *accept* - receive a subchannel from a channel
* *pipe* - ask system to connect two channels, piping all future communications. This allows some optimizations by removing the program as a middleman.
* *copy* - copy a channel such that inputs (recv/accept) is copied are copied and outputs (send/attach) are merged in non-deterministic order.
* *drop* - tell system you won't be reading or writing from a channel in future. 
* *test* - ask system whether channel is active - test fails if remote endpoints are closed and there is no pending input in the buffer. (More than one remote endpoint is possible due to 'copy'.)
* *tune* - inform the system that a channel will be used in some refined manner (read-only, write-only, attach/accept only, buffer size hints, etc.). 

Note that 'accept' and 'recv' must preserve corresponding order with 'attach' and 'send'. 

How we obtain our channels:

* *create* - create a new, associated pair of channels. These channels are local to the runtime, thus implementation is a lot more ad-hoc. 

* *tcp binding* - create a channel by wrapping a TCP listener or TCP connection. This wrapper will implicitly handle subchannels, data serialization, content-addressed storage, and integration with content distribution networks. 
  * *listener* - a channel bound to a TCP listener will only receive subchannels corresponding to TCP connections. Close the listener via 'drop'. 
  * *connection* - a channel bound to a TCP connection will handle serialization and marshalling of data and protocol issues for remote communication. The connection will implicitly break if the associated TCP connection breaks.

* *global databus* - (idea). I could support a notion of a global databus for glas systems. Binding to the bus would return a channel that receives a copy of future messages sent to the bus. Discovery could use distributed hashtables to find other participants in the bus. Use something like Tahoe-LAFS style bearer tokens for bus names, representing different levels of authority.

Channel API shouldn't be too difficult within a single runtime. So, this document is primarily about how to implement the TCP bindings for channels. If channels become popular, it should also be feasible to extract these bindings and required runtime components as a library for use within non-glas apps. 

A global databus API could feasibly be implemented indirectly above TCP bindings. But it might be a more convenient and extensible means to glue apps together using publish-subscribe databuses instead of point-to-point connections.

A runtime may need to keep an extra TCP channel open to handle pipeline optimization.

## Desired Features

### Overlay Networks and Brokering

Support for 'pipe' enables the TCP connections to begin forming an overlay network. Ideally, pipes can be optimized, i.e. if we start with connections {(A,B), (B,C)} and B decides to 'pipe' these connections, we'll logically have a new connection {(A,C)}. 

          B                 B               B
         / \     =pipe=>   / \  =detach=>
        A   C             A---C           A---C  

Actually implementing this feature will be challenging because it requires initiating a new TCP connection between A and C - a process that may easily fail due to firewalls. This will usually require some negotiation between A and C via B before the pipe is fully detached.

If B is a broker that repeatedly establishes connections between muliple clients and A, then A might want to delegate some negotiation authority to B then open a TCP listener to receive multiple new connections. Ideally, B can simulate a few non-interactive copy/attach/send operations on the original (A,B) connection to provide context without directly contacting A, instead handing C a single-use, time-sensitive, signed ticket for a new connection with A that specifies initial ops.

### Content Distribution Network (CDNs)

When large values are serialized, we'll use content-addressed references to components of those values, i.e. via SHA3-512 of their binary representation (see [Glas Object](GlasObject.md)). It should be possible to query the TCP connection for the component data if it is not already known. 

However, to ameliorate traffic, it should also be feasible to negotiate an intermediate proxy cache. The data can be stored compressed and encrypted in the proxy. The client would use the first half of that secure hash as a lookup key and the second half as a decryption key.

However, a proxy cache isn't free. As part of this negotiation, we will need to provide credentials to access 'session' with the proxy cache. Session info would help scope access to data and control charges. It might be useful to support hierarchical subsessions.

So, there is a lot of fine detail to deal with CDNs that should be designed carefully and integrated with Glas Channels. It should also be extensible with new CDN features.

### Remote Evaluation

Instead of only communicating data over a channel, we could implicitly migrate some code and private state to evaluate nearer to the data source. This would be subject to negotiation and live coding (i.e. updating the remote code between transactions). The remote system might deny service depending on resource constraints, returning to a more conventional data channel. Some communications would report updates to the private state.

Support for code distribution, and resource constraints (such as quotas), could be expressed as an extra option when binding a channel over a TCP connection or TCP listener. This would directly support my vision of glas applications as distributed, live-coded overlay networks. 

### Transaction Alignment

As a convenient feature, messages written to a channel within a single transaction should also be readable within a single transaction. This might require tracking transaction frames within the TCP communications.

## Subchannels

Every TCP connection will host multiple subchannels. The names for those subchannels will be local to the TCP connection, and perhaps even local to each endpoint of the connection. Need to consider the details here.

New ones can be created via 'attach'. Subchannels can be re-routed to another TCP connection (or to a local in-memory channel) via 'pipe'. Every message we send must include some metadata abou

Whenever we send a message, it must be obvious which subchannel is receiving that message. Thus, we need some channel metadata as part of the send. 


## TCP Messages

## Message Frame


