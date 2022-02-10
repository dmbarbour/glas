# Glas Command Line Interface

The [Glas Design](GlasDesign.md) document describes a Glas command-line interface (CLI) that is extensible with user-defined verbs via simple syntactic sugar: `glas foo a b c` is rewritten to `glas --run glas-cli-foo.run -- a b c`. Module 'glas-cli-foo' should compile to a record value of form `(run:Program, ...)`. This program must be 1--1 arity and represents the body of a *transaction machine* application (see [Glas Apps](GlasApps.md)).

        type App = (init:Params | step:State) → [Effects] (halt:ExitCode | step:State | FAILURE)

Concretely, for Glas CLI verbs, initial parameters is a list of strings such as `init:["a", "b", "c"]`, and the halting value should represent an exit code. If the application returns `step:State`, it will implicitly commit effects then loop. If evaluation fails, effects are transactionally aborted and evaluation retries, effectively waiting for changes.

Under this design, application state is accessible for orthogonal persistence, debug views, live coding, and application composition or extension. Reactive, concurrent, and even distributed evaluation become performance optimizations of a sequential loop, which simplifies expression and reasoning. However, we'll also want an intermediate language to compile procedural scripts into state machines with transactional steps.

## Bootstrap

Glas systems will bootstrap the command line executable from the module system. Minimizing the effects API required for bootstrap will reduce overheads for developing an initial executable. For example, it is sufficient to write bytes to standard output, no need to implement a complete filesystem API. Proposed bootstrap effects API:

* **write:Binary** - write a list of bytes to standard output. 
* **log:Message** - same as used by language modules. For bootstrap, just write messages to stderr. 
* **load:ModuleName** - same as used by language modules. For bootstrap, assume source is constant.

Assuming suitable module definitions, bootstrap can be expressed in a few lines of bash:

    # build
    glas bootstrap linux-x64 > glas
    chmod +x glas

    # verify
    ./glas bootstrap linux-x64 | cmp glas

    # install
    sudo mv glas /usr/bin/

The generated executable might provide access to different effects than the initial executable. But the three bootstrap effects are critical and should always be supported.

## Values and Programs

The program argument to `--run` is currently specified by dotted path into the module system, i.e. `ModuleName(.Label)*`, generally assuming ASCII module names and labels. This is sufficient for verbs, and should be adequate for most use-cases. But, if there is a strong use-case, I might later extend this to support a simple expression language.

## Useful Verbs

I have a some thoughts for verbs that might be convenient to develop early on. 

* **print** - support for module system values and printing to standard output. Deterministic. 
* **arity** - report arity of a named Glas program.
* **test** - support for automated testing. Relies mostly on 'log' and 'fork' effects.
* **repl** - support for a read-eval-print loop using a syntax of choice. 
* **type** - infer and report type of an indicated Glas program or value.
* *language server* - support the [Language Server Protocol](https://en.wikipedia.org/wiki/Language_Server_Protocol).
* **ide** - support for a web-based IDE. This might include the language server.
* *notebook apps* - a graphical, incremental variation on REPLs, via web services. Perhaps part of IDE.

These verbs use `--run` via the Glas module system, but it might be useful to have built-in implementations simpler debugging verbs such as `--print` and `--arity` and `--test`. 

Several verbs such as print, type, arity can use the same effects as bootstrap. However, others such as test, repl, or web-based would require an extended effects API.

## Extended Effects API

### General Effects

See [Glas Apps](GlasApps.md) for discussion on some effects APIs that are suitable for transaction machine applications in general, such as 'fork' for concurrency and 'time' to model waits and sleeps.

### Standard Input and Output

We need **write** for Bootstrap. For symmetry, perhaps include **read** effect for access to standard input. This would be convenient for defining a REPL, for example. 

* **write:Binary** - write a list of bytes to standard output.
* **read:Count** - read up to Count bytes from standard input as available.

In context of a filesystem API, we might implicitly rewrite 'read' and 'write' effects into file operations on references 'std:out' and 'std:in'.

### Environment Variables

In addition to command-line arguments, a console application typically has access to a set of 'environment variables'. The effects API can provide relatively lightweight read-only access to these variables:

* **env:get:Variable** - response is a value for variable, or failure if the variable is unrecognized or undefined. Variables are usually strings, e.g. `env:get:"GLAS_PATH"`.
* **env:list** - response is the list of defined Variables

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
 * **read:(from:FileRef, count:Nat)** - Response is list of up to Count available bytes taken from input stream. Returns fewer than Count if input buffer is empty. 
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

## Rejected or Deferred Effects APIs

### Extended Module System Access? Defer.

The 'load' effect is sufficient for common module system access, but it might be convenient to provide more reflection on the module system - e.g. identify which modules are visible in the filesystem, identifying which files are used, finding which modules currently fail to build. 

### Graphical User Interface? Defer.

It might be useful to support a lightweight API for native GUI integration, perhaps something like TK. Seems difficult to make general across operating systems. Low priority.

### Runtime Reflection? Defer.

It might be useful to provide some reflection on the environment. This could include call stacks, performance metrics, traces of failed transactions. However, I'm uncertain what this API should look like, and it is also low priority. 

### OS Exec? Defer.

If I ever want to express a command shell as a normal glas verb, I'll need an effects API that supports execution of OS commands, including integration with file streams and environments. However, there is no pressing need.

### Eval Effect? Reject!

I've repeatedly had the idea of supporting eval as an effect. However, acceleration of an eval subprogram seems the wiser choice. Only writing this down so I am reminded of it. Additionally, we might want to accelerate eval of other models, such as Kahn process networks.
