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

In addition to command-line arguments, a console application typically has access to a set of 'environment variables'. The effects API can provide relatively lightweight read-only access to these variables:

* **env:get:Variable** - response is a value for variable, or failure if the variable is unrecognized or undefined. Variables are usually strings, e.g. `env:get:"GLAS_PATH"`.
* **env:list** - response is the list of defined Variables

Currently this is a read-only API because I don't want the complication of correctly handling runtime mutation of GLAS_PATH. 

### Standard Input and Output

Consoles start with streams open for standard input and output. 

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
  * *live* - no obvious errors, and not done. normal status for reading or writing data.
  * *done* - successful completion of task. Does not apply to all interactions.
  * *error:Value* - any error state. Value can be a record of ad-hoc fields describing the error (possibly empty).
 * **ref:list** - return a list of open file references. 
 * **ref:move:(from:FileRef, to:FileRef)** - reorganize references. Fails if 'to' ref is in use. 

I'm uncertain how to best handle buffering and pushback. Perhaps buffer size could be explicitly configurable, or we could provide a simple method to check dynamically whether the write buffer is smaller than a limit. But it's a low priority feature for now. 

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:DirName, as:DirRef, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Fails if DirRef is already in use, otherwise returns unit. Potential Interactions:
  * *list* - read a list of entries from the directory. Reaches Done state after all items are read.
  * *rename:NewDirName* - rename or move a directory. Remains open until attempted by runtime.
  * *delete:(recursive?)* - remove an empty directory. Can be flagged for recursive deletion.
 * **close:DirRef** - release the directory reference.
 * **read:DirRef** - read a file system entry, or fail if input buffer is empty. This is a record with ad-hoc fields including at least 'type' and 'name'. Some potential fields:
  * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
  * *name:Path* (always) - a full filename or directory name, usually a string
  * *mtime:TimeStamp* (optional) - modify time 
  * *ctime:TimeStamp* (optional) - creation time 
  * *size:Nat* (optional) - number of bytes
 * **status:FileRef** - ~ same as file status
 * **ref:list** - return a list of open directory references.
 * **ref:move:(from:DirRef, to:DirRef)** - reorganize directory references. Fails if 'to' ref is in use.
 * **cwd** - return current working directory. Non-rooted file references are relative to this.
 * **sep** - return preferred directory separator substring for current OS, usually "/" or "\".

Later, I might extend directory operations with option to watch a directory, or list content recursively. This might be extra flags on 'list'. However, my current goal is to get this into a usable state. Advanced features can wait until I'm trying to integrate live coding.

### Standard IO

Following convention, standard input and output are modeled as initially open file references. However, instead of integers, I propose to use `std:in` and `std:out` to identify these initially open references. We could also allow `std:err` if it wasn't used for logging.

### Network

I can cover needs of most applications with support for just the TCP and UDP protocols. Network operations are already mostly asynchronous in conventional code, so the adaption here is smaller than for filesystem API. Mostly, we need asynchronous initialization and lose synchronous reads.  For pushback on TCP connections, it might also be useful to provide some view of pending writes. Viable effects API:

* **tcp:TcpOp** - namespace for TCP operations
 * **l:ListenerOp** - namespace for TCP listener operations.
  * **create:(port?Port, addr?Addr, as:ListenerRef)** - Create a new ListenerRef. Return unit. Whether listener is successfully created is observable via 'state' a short while after the request is committed.
   * *port* - indicates which local TCP port to bind. If omitted, OS chooses port.
   * *addr* - indicates which local network cards or ethernet interfaces to bind. If omitted, attempts to bind all addresses.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **status:ListenerRef**
   * *init* - initial status, 'create' not yet processed by OS/runtime.
   * *live*
   * *error:Value* - indicates error. Value may be a record with ad-hoc fields describing the error, possibly empty. 
  * **info:ListenerRef** - After successful creation of listener, returns `(port:Port, addr:Addr)`. Fails if listener is not successfully created.
  * **close:ListenerRef** - Release listener reference and associated resources.
  * **ref:list** - returns list of open listener refs
  * **ref:move:(from:ListenerRef, to:ListenerRef)** - reorganize references. Cannot move to an open ref.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'status' to diagnose error vs. end of input. 
 * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes.
 * **limit:(of:Ref, cap:Count)** - fails if number of bytes pending in the write buffer is greater than Count or if connection is closed, otherwise succeeds returning unit. Not necessarily accurate or precise. This method is useful for pushback, to limit a writer that is faster than a remote reader.
 * **status:TcpRef**
  * *init* - initial status, 'create' not yet processed by OS/runtime.
  * *live*
  * *done* - remote has closed connection, but might still receive/send just a little more.
  * *error:Value* - indicates error. Value may be a record with ad-hoc fields describing the error, possibly empty. 
 * **info:TcpRef** - For a successful TCP connection (whether via 'tcp:connect' or 'tcp:listener:accept'), returns `(dst:(port:Port, addr:Addr), src:(port:Port, addr:Addr))`. Fails if TCP connection is not successful.
 * **close:TcpRef**
 * **ref:list** - returns list of open TCP refs
 * **ref:move:(from:TcpRef, to:TcpRef)** - reorganize TCP refs. Fails if 'to' ref is in use.

* **udp:UdpOp** - namespace for UDP operations.
 * **connect:(port?Port, addr?Addr, as:UdpRef)** - Bind a local UDP port, potentially across multiple ethernet interfaces. Fails if UdpRef is already in use, otherwise returns unit. Whether binding is successful is observable via 'state' after the request is committed. Options:
  * *port* - normally included to determine which port to bind, but may be left to dynamic allocation. 
  * *addr* - indicates which local ethernet interfaces to bind; if unspecified, binds all of them.
 * **read:(from:UdpRef)** - returns the next available Message value, consisting of `(port:Port, addr:Addr, data:Binary)`. This refers to the remote UDP port and address, and the binary data payload. Fails if there is no available message.
 * **write(to:UdpRef, data:Message)** - output a message using same `(port, addr, data)` record as messages read. Returns unit. Write may fail if the connection is in an error state, and attempting to write to an invalid port or address or oversized packets may result in an error state.
 * **status:UdpRef**
  * *init* - initial status, 'create' not yet processed by OS/runtime.
  * *live*
  * *error:Value* - indicates error. Value may be a record with ad-hoc fields describing the error, possibly empty. 
 * **info:UdpRef** - For a live UDP connection, returns a `(port:Port, addr:Addr)` pair. Fails if still initializing, or if there was an error during initialization.
 * **close:UdpRef** - Return reference to unused state, releasing system resources.
 * **ref:list** - returns list of open UDP refs.
 * **ref:move:(from:UdpRef, to:UdpRef)** - reorganize UDP refs. Fails if 'to' ref is in use.

A port is a fixed-width 16-bit number. An addr is a fixed-width 128-bit or 32-bit number (IPv4 or IPv6) or optionally a string such as "www.google.com" or "192.168.1.42". Later, I might add a dedicated API for DNS lookup, and perhaps for 'raw' Ethernet.

## Rejected Effects

### Eval Effect? Use Accelerator!

I believe it wiser to support 'eval' as an accelerated function rather than as an effect. Acceleration of eval, together with some memoization, effectively extends Glas programs with first-class functions.

### Runtime Info? Defer.

It might be useful to provide some reflection on the environment via environment variables or special methods. This could include call stacks, performance metrics, and traces of failed transactions. However, this is low priority.

### Executables? Defer.

It might be useful to write a full shell within Glas at some point. In this case, we'd need to start other processes with arguments, suitable environments, and access to open files representing stdin/stdout/etc.. However, this is low priority, and it might be better to defer this until we're compiling Glas applications from within Glas.

