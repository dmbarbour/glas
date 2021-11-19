# Glas Command Line Interface

The [Glas Design](GlasDesign.md) document describes a Glas command-line interface (CLI) that is extensible with user-defined verbs, e.g. `glas foo a b c` is rewritten to `glas --run glas-cli-foo.run -- a b c`. Module 'glas-cli-foo' should compile to a record value of form `(run:Program, ...)`. This program is then evaluated in a procedural style, with access to filesystem and network effects. 

Input on the data stack is `cmd:["a", "b", "c"]`. The 'cmd' header supports potential extension of applications with new methods or modes. Final value on the data stack for the cmd method should be a bitstring of up to 32 bits, which will be cast to an exit code (a 32-bit int). Failure results in a -1 exit code.

The biggest difference from conventional procedural code is the approach to concurrency. A concurrent program should be expressed as a transaction loop. See *Procedural Embedding of Transaction Machines* in [Glas Apps](GlasApps.md). 

## Useful Verbs

I have a few vague ideas for `glas verb` options that should possibly exist. 

* **print** - support for querying and printing values from the module system. Deterministic. 
* **test** - support for automated testing with 'log' and 'fork' effects.
* *language server* - support for the [Language Server Protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol).
* **ide** - support for a web-based IDE. This might include the language server.
* **repl** - support for a read-eval-print loop using a syntax of choice. Should also have available in IDE.
* *notebook apps* - a graphical, incremental variation on REPLs, via web service. Perhaps part of IDE.
* **arity** - report arity of Glas programs.
* **type** - report type of Glas programs.
* **boot** - use a bootstrap implementation of the command line interface
* **shell** - consider developing a shell? Might focus on Glas programs instead of arbitrary binaries, or perhaps interpretation of binaries.

In some of these cases, like the repl or notebook apps, we might require that language modules provide some additional options for operating in a more interactive mode.

## Effects API

### General Effects

Access to time (now or check), fork, and log APIs as described in [Glas Apps](GlasApps.md). Use of 'log' replaces access to stderr, at least by default.

### Module System Access

Many command-line verbs such as printers or REPLs will require lightweight access to the module system, preferably reusing the bootstrap and caching implicit to the command line executable. So I provide access via effects.

* **m:load:ModuleId** - load a module. Equivalent behavior as for language modules. May fail. Cause for failure is implicitly logged.

I might later add ops to browse the module system without reference to the filesystem, e.g. 'm:list' or similar. I'm reserving the 'm:' prefix as a namespace for this purpose. Access to the module system might be omitted in certain contexts, e.g. if compiling a CLI app to run separately from a `glas` executable.

### Graphical User Interface - Indirect

The Glas CLI currently does not provide effects for opening windows, drawing buttons, etc.. A GUI can be supported indirectly via network, e.g. web applications or remote desktop. True GUI might eventually be supported when we're easily able to compile Glas apps into executables with GUI access, but is very low priority.

### Environment Variables

In addition to command-line arguments, a console application usually has access to environment variables where variables and values are both strings. An API can provide relatively lightweight access to these variables, e.g. `env:get:"GLAS_PATH"`. And `env:list` could provide a list of defined variables.

* **env:get:Variable** - response is a value for the variable, or failure if the variable is unrecognized or undefined. 
* **env:list** - response is a list of defined Variables

Currently this is a read-only API. I don't want the complication of correctly handling mutation of GLAS_PATH. The primary use of environment variables is to support ad-hoc configuration, such as access to GLAS_PATH, or to HOME where more configs might be kept.

### Filesystem

The Glas CLI needs just enough access to the filesystem to support bootstrap and live-coding. This includes reading and writing files, browsing the filesystem, and watching for changes. The API must also be adapted for asynchronous interaction. 

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:Interaction)** - Create a new system object to interact with the specified file resource. Fails if FileRef is already in open, otherwise returns unit. Use 'status' The intended interaction must be specified:
  * *read* - read file as stream. Status is set to 'done' when last byte is available, even if it hasn't been read yet.
  * *write* - open file and write from beginning. Will delete content in existing file.
  * *append* - open file and write start writing at end. Will create a new file if needed.
  * *delete* - remove a file. Use status to observe potential error.
  * *rename:NewFileName* - rename a file. 
 * **close:FileRef** - Release the file reference.
 * **read:(from:FileRef, count:Nat)** - Response is list of 1..Count bytes taken from input stream. Returns fewer than Count if input buffer is empty. Fails if would respond with empty list. 
 * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. 
 * **status:FileRef** - Return a representation of the state of the system object. 
  * *init* - the 'open' request hasn't been fully processed yet.
  * *live* - no obvious errors, and obviously not done. normal status for reading or writing data.
  * *done* - successful completion of task. Does not apply to all interactions.
  * *error:Value* - any error state. Value can be a record of ad-hoc fields describing the error (possibly empty).
 * **ref:list** - return a list of open file references. 
 * **ref:move:(from:FileRef, to:FileRef)** - reorganize references. Fails if 'to' ref is in use. 

I'm uncertain how to best handle buffering and pushback. Perhaps buffer size could be explicitly configurable, or we could provide a simple method to check dynamically whether the write buffer is smaller than a limit. But it's a low priority feature for now. 

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:DirName, as:DirRef, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Fails if DirRef is already in use, otherwise returns unit. Potential Interactions:
  * *list* - read a list of entries from the directory. Reaches Done state after all items are read.
  * *rename:NewDirName* - rename or move a directory. Remains open until attempted by runtime.
  * *delete* - remove an empty directory.
 * **close:DirRef** - release the directory reference.
 * **read:DirRef** - read a file system entry, or fail if input buffer is empty. This is a record with ad-hoc fields, similar to a log message. Some defined fields:
  * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
  * *name:Path* (always) - a full filename or directory name, usually a string
  * *mtime:TimeStamp* (if available) - modify-time, uses Windows NT timestamps 
 * **status:FileRef** - ~ same as file status
 * **ref:list** - return a list of open directory references.
 * **ref:move:(from:DirRef, to:DirRef)** - reorganize directory references. Fails if 'to' ref is in use.
 * **cwd** - return the current working directory.
 * **sep** - return the directory separator string, usually "/" or "\".

Later, I might extend directory operations with option to watch a directory, or list content recursively. This might be extra flags on 'list'. However, my current goal is to get this into a usable state. Advanced features can wait until I'm trying to integrate live coding.

### Standard IO

Following convention, standard input and output are modeled as initially open file references. However, instead of integers, I propose to use `std:in` and `std:out` to identify these file references. 

*Note:* Could also provide `std:err`, but it is currently claimed for logging.

### Network

We can cover the needs of most applications with support for TCP and UDP protocol layers. Instead of touching the mess that is sockets, I propose to specialize the API for each protocol required. Later, we might add raw IP sockets support. 

* **tcp:TcpOp** - namespace for TCP operations
 * **l:ListenerOp** - namespace for TCP listener operations.
  * **create:(port?Port, addrs?[List, Of, Addr], as:ListenerRef)** - Create a new ListenerRef. Return unit. Whether listener is successfully created is observable via 'state' a short while after the request is committed.
   * *port* - indicates which local TCP port to bind; if excluded, leaves dynamic allocation to OS. 
   * *addrs* - indicates which local network cards or ethernet interfaces to bind; if excluded, attempts to bind all of them.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **state:ListenerRef**
   * *init* - initial status, 'create' not yet processed by OS/runtime.
   * *live*
   * *error:Value* - indicates error. Value may be a record with ad-hoc fields describing the error, possibly empty. 
  * **info:ListenerRef** - After successful creation of listener, returns `(port:Port, addrs:[List , Of, Addr])`. Fails if listener is not successfully created.
  * **close:ListenerRef** - Release listener reference and associated resources.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'state' to diagnose.
 * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes.
 * **status:TcpRef**
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
 * **info:UdpRef** - For a successfully connected UDP connection, returns a `(port:Port, addrs:[List, Of, Addr])` pair. Fails if still initializing, or if there was an error during initialization.
 * **close:UdpRef** - Return reference to unused state, releasing system resources.

An Addr could be a 32-bit number (IPv4), a 128-bit number (IPv6), or a string such as "www.google.com" or "192.168.1.42". Similarly, a Port can be a 16-bit number or a string such as "ftp" that is externally associated with a port (cf. `/etc/services` in Linux). 

An API for DNS access would also be convenient, but its implementation isn't critical at this time. In theory, we could implement this using network access to DNS servers (potentially including a localhost DNS) and file access to DNS configurations (e.g. `/etc/resolv.conf`). But most apps won't need direct use of lookup services if we integrate it implicitly into the addressing.

I've excluded the half-close TCP feature because too many routers disable this feature by applying a short timeout after half-close. A TCP connection should be closed when the full TCP interaction is complete.

I'm very interested the raw network socket API, but I've decided to elide this for now.

## Rejected Effects

### Eval Effect? Use Accelerator!

I believe it wiser to support 'eval' as an accelerated function rather than as an effect. Acceleration of eval, together with some memoization, effectively extends Glas programs with first-class functions.

### Runtime Info? Defer.

It might be useful to provide some reflection on the environment via environment variables or special methods. However, no need to rush this.

