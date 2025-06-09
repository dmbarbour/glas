# Glas Object Channels (Deprecated)

Glas systems will mostly use remote procedure calls between applications, together with publish-subscribe of RPC 'objects'. That said, channels are also a decent fit for asynchronous, transactional interactions. The main weakness is that channels are very stateful, which can hinder reactivity in an open system. In case of built-in channels, it's also unclear how errors and cleanup should be handled, and making such options explicit overly complicates the API. 

For now, I propose to model channels - and similar patterns, such as a databus - using either queues in a shared database or RPC communication with an intermediate glas application operating as a service. These approaches may be freely combined. Error handling and cleanup are left to the developer.
