# Glas Command Line Interface

The [Glas Design](GlasDesign.md) document describes a Glas command-line interface (CLI) that is extensible with user-defined verbs, e.g. `glas foo a b c` is rewritten to `glas --run glas-cli-foo.run -- a b c`, then we extract `glas-cli-foo.run`, which should be a Glas program. 

The program is run in a procedural style, albeit with 'try', 'while', and 'until' clauses forming implicit transactions. Input to the data stack is `cmd:["a", "b", "c"]` in the above case. The 'cmd' method selector here provides room for extension, and is followed by the command line parameters as strings. The final data stack should have a natural number exit code. If the program fails or returns an invalid number (outside range 0..2**31-1), exit code is implicitly -1.

## Common Effects

The *Logging*, *Time*, *Concurrency*, and *Environment Variable* effects described in [Glas Apps](GlasApps.md) can be readily applied to procedural code. We simply treat each top-level operation as a separate transaction. 

### Regarding Concurrency

See *Procedural Embedding of Transaction Machines* in [Glas Apps](GlasApps.md). A transactional loop such as `loop:(until:Halt, do:cond:try:Step)` can be compiled as a transaction machine. This will be the future foundation for concurrency in Glas command line interface. When eventually implemented, this will be very expressive.

However, concurrency won't be implemented immediately. Returning a random result would inevitably result in confusion and deadlock. Before it is properly implemented, at least with backtracking wait within transactions, the 'fork' effect should fail. 

### Regarding Time

Use Windows NT time for timestamps, and also for file times.

### Regarding Environment Variables

The Glas CLI will support environment variables with string variable names and string values. Additionally, a special read-only variable, indicated by the empty string, will return a list of defined variables. 

Later, I might introduce special environment variables to view OS version, Glas runtime version, etc.. However, this is low priority.

## Module System Access

Many command-line verbs require lightweight access to the module system, e.g. `glas print myApp.x with std.print` might access modules `myApp` and and `std` respectively. Although it is feasible to do this via filesystem operations, it is inconvenient (doesn't reuse bootstrap or compilation cache, doesn't implicitly extend to package managers, etc.). So we'll provide access via effects.

* **m:load:ModuleId** - load a module. Equivalent behavior as for language modules. May fail. Cause for failure is implicitly logged.

We might later add ops to browse a module system without reference to the filesystem, so I'm reserving the 'm:' prefix for the module system API.

## Console Application Effects

### Filesystem

The Glas CLI needs enough access to the filesystem to support bootstrap and live-coding. This includes reading and writing files, browsing the filesystem, and watching for changes. The API must also be adapted for asynchronous interaction. File handles should be provided by the app, per description of 'robust references' in [Glas Apps](GlasApps.md).

The conventional API for filesystems is painful, IMO, but I'd rather not invite trouble so I'll try to adapt a portable filesystem API relatively directly.

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:(read | write | append | create | delete))** - Create a new system object to interact with the specified file resource. Fails if FileRef is already in use, otherwise returns unit. Whether the file is successfully opened is observable via 'state' a short while after request is committed. The intended interaction must be specified:
  * *read* - read file as stream.
  * *write* - erase current content of file or start a new file.
  * *append* - extend current content of file.
  * *create* - same as write, except fails if the file already exists.
  * *delete* - remove a file. Use status to observe potential error.
 * **close:FileRef** - Delete the system object associated with this reference. FileRef is no longer in-use, and operations (except open) will fail.
 * **read:(from:FileRef, count?Nat)** - read a byte, or fail if at end of file or error status.
  * *count* - if specified, return a list of N bytes, or fewer if at end of file, or fail if error status.
 * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. This fails if the file is read-only or is in an error state, otherwise returns unit. It is permitted to write while in a 'wait' state.
 * **status:FileRef** - Return a representation of the state of the system object. 
  * *init* - state immediately after 'open' until initial request is committed and processed.
  * *ok* - seems to be in a good state. 
  * *done:Value* - requested interaction is complete. This currently applies to read or delete modes. 
  * *error:Value* - any error state, with ad-hoc details.
 * **refs** - return list of open file references.

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:)


### Standard IO

Standard input and output can be modeled as initially open file references, following convention. However, instead of integers, I propose to reserve `std:in` and `std:out` as file references, and to reserve the `std:` reference prefix for the system in general. Access to `std:err` reference may be unavailable, implicitly used for log output. 


### Network

We can cover the needs of most applications with support for TCP and UDP protocol layers. Instead of touching the mess that is sockets, I propose to specialize the API for each protocol required. Later, we might add raw IP sockets support. 

* **tcp:TcpOp** - namespace for TCP operations
 * **listener:ListenerOp** - namespace for TCP listener operations.
  * **create:(port?Port, addrs?[List, Of, Addr], as:ListenerRef)** - Create a new ListenerRef. Return unit. Whether listener is successfully created is observable via 'state' a short while after the request is committed.
   * *port* - indicates which local TCP port to bind; if excluded, leaves dynamic allocation to OS. 
   * *addrs* - indicates which local network cards or ethernet interfaces to bind; if excluded, attempts to bind all of them.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **state:ListenerRef**
   * *init* - create request hasn't been fully processed yet.
   * *ok* - normal state
   * *error:Value* - failed to create or detached, with details. 
  * **info:ListenerRef** - After successful creation of listener, returns `(port:Port, addrs:[List , Of, Addr])`. Fails if listener is not successfully created.
  * **close:ListenerRef** - Release listener reference and associated resources.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N, exact?)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'state' to diagnose. Option:
  * *exact* - flag. If set, fail if fewer than N bytes are available.
 * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes.
 * **status:TcpRef**
  * *init* 
  * *ok*
  * *error:Value*
 * **info:TcpRef** - For a successful TCP connection (whether via 'tcp:connect' or 'tcp:listener:accept'), returns `(dst:(port:Port, addr:Addr), src:(port:Port, addr:Addr))`. Fails if TCP connection is not successful.
 * **close:TcpRef**

* **udp:UdpOp** - namespace for UDP operations.
 * **connect:(port?Port, addrs?[List, Of, Addr], as:UdpRef)** - Bind a local UDP port, potentially across multiple ethernet interfaces. Fails if UdpRef is already in use, otherwise returns unit. Whether binding is successful is observable via 'state' after the request is committed. Options:
  * *port* - normally included to determine which port to bind, but may be left to dynamic allocation. 
  * *addr* - indicates which local ethernet interfaces to bind; if unspecified, binds all of them.
 * **read:(from:UdpRef)** - returns the next available Message value, consisting of `(port:Port, addr:Addr, data:Binary)`. This refers to the remote UDP port and address, and the binary data payload. Fails if there is no available message.
 * **write(to:UdpRef, data:Message)** - output a message using same `(port, addr, data)` record as messages read. Returns unit. Write may fail if the connection is in an error state, and attempting to write to an invalid port or address or oversized packets may result in an error state.
 * **move:(from:UdpRef, to:UdpRef)** - rename a reference. Fails if 'to' ref already in use, or 'from' ref is unused. Returns unit. After move, the roles of these references is reversed.
 * **status:UdpRef**
  * *init*
  * *ok*
  * *error:Value*
 * **info:UdpRef** - For a successfully connected UDP connection, returns a `(port:Port, addrs:[List, Of, Addr])` pair. Fails if still initializing, or if there was an error during initialization.
 * **close:UdpRef** - Return reference to unused state, releasing system resources.

An Addr could be a 32-bit number (IPv4), a 128-bit number (IPv6), or a string such as "www.google.com" or "192.168.1.42". Similarly, a Port can be a 16-bit number or a string such as "ftp" that is externally associated with a port (cf. `/etc/services` in Linux). 

An API for DNS access would also be convenient, but its implementation isn't critical at this time. In theory, we could implement this using network access to DNS servers (potentially including a localhost DNS) and file access to DNS configurations (e.g. `/etc/resolv.conf`). But most apps won't need direct use of lookup services if we integrate it implicitly into the addressing.

I've excluded the half-close TCP feature because too many routers disable this feature by applying a short timeout after half-close. A TCP connection should be closed when the full TCP interaction is complete.

I'm very interested the raw network socket API, but I've decided to elide this for now.

## Rejected Effects

### Eval? Use Accelerator!

I find it tempting to provide 'eval' as an effect for convenient access to host accelerators and just-in-time compilation. However, I believe it wiser to instead support 'eval' itself as an accelerated function. Acceleration of eval effectively extends Glas programs with first-class functions, especially assuming we can memoize the computation.

### Runtime Info? Defer.

The glas executable and the verbs are updated independently. It may be useful for verbs to gather some metadata about the executable's version or effects API. OTOH, this is not critical short-term, and I'm uncertain exactly how to represent this metadata. Perhaps return to this when we've developed some conventions for representation of annotated program types as values.

