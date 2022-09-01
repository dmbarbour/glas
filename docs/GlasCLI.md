# Glas Command Line Interface

The glas command line will support a few built-in operations, such as:

        glas --run 
        glas --help
        glas --version
        glas --extract ValueRef


In most cases users will invoke operations such as: 

        glas print ValueRef
        glas type ValueRef
        glas repl

These operations are implemented by defining modules with a simple naming convention. Relevantly, we apply a very simple rewrite rule:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

In this case module glas-cli-opname must compile to a value of form `(main:Program, ...)`. This program is evaluated as a [Glas Application](GlasApps.md). It is initialized with the list of strings from the command line, i.e. `init:["a", "b", "c"]`. The final `halt:ExitCode` result should contain a bitstring that we can cast to a 32-bit integer.

Besides '--run' there are a few standard built-in operations:

The '--help' and '--version' operations are standard options and print messages. The '--extract' operation shall compile and print a binary value (a list of 8-bit bytes) to standard output, and is provided as a built-in only to simplify bootstrap. More built-in operations are possible, but the intention is to develop a minimal executable and move most logic into the module system. Performance can be mitigated by caching JIT-compiled representations of programs.

## Built-In File Extensions

Support for the `.g0` extension (for [Glas Zero](GlasZero.md)) is a required built-in for bootstrap. The `.glob` format (for [Glas Object](GlasObject.md)) is likely to also become a standard built-in, with special support for verifying . Although these are built-in, we should still bootstrap the associated language-g0 and language-glob modules.

## Bootstrap

The command line tool should be bootstrapped from the module system via extraction of an executable binary. Thus, an initial implementation doesn't need to support '--run' or a full effects API. Assuming suitable module definitions, bootstrap of the command line tool might be expressed using just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, we will need multiple versions of 'glas-binary' for multiple operating systems and processors. This might be achieved by defining a 'target' module that can be adjusted via GLAS_PATH. Alternatively, we can define separate modules such as 'glas-binary-linux-x64'. 

## Versioning

Different versions of the glas executable should mostly vary in performance. If the g0 language module bootstraps, the outcome from '--extract' should be deterministic independent of 'glas' version. The behavior for '--run' may change due to different effects APIs, but the effects APIs and their associated behavior should be standardized to control variation.

## Useful Verbs

A few verbs that might be convenient to develop early on:

* **print** - pretty-print values from module system.  
* **arity** - report arity of referenced Glas program. 
* **type** - infer and report type info for indicated Glas value.
* **test** - support an automated fuzz-testing environment.
* **repl** - a read-eval-print loop, perhaps configurable syntax. 

Beyond these, applications may support language server protocol, or provide a web-app based IDE.


## Effects API

### General Effects

See [Glas Apps](GlasApps.md) for discussion on some effects APIs that are suitable for transaction machine applications in general, such as 'fork' for concurrency and 'time' to model waits and sleeps. The 'log' effect will output to standard error, but this might be tweakable via reflection APIs. 

### Log and Load

We can directly provide the 'log' and 'load' effects from language modules. To support live coding, 'load' will be live, such that changes in source code at runtime are properly reflected in future evaluations of 'load'. Log messages will initially be streamed to standard error, but might later be configurable via environment variables.

### Standard Input and Output

We can provide `std:in` and `std:out` as initially open file streams.

The standard error stream is usually reserved for log effects. Programs may only write to it indirectly, via 'log:Message'.

### Environment Variables

In addition to command-line arguments, a console application typically has access to a set of 'environment variables'. The effects API can provide relatively lightweight read-only access to these variables:

* **env:get:Variable** - response is a value for variable, or failure if the variable is unrecognized or undefined. Variables are usually represented as strings, e.g. `env:get:"GLAS_PATH"`.
* **env:list** - response is a list of defined variables

I'd prefer to avoid the complications from mutating environment variables. But it is feasible to indirectly represent mutation via effects handler.

### Filesystem

Console apps are unavoidably related to the filesystem.

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

Later, I might extend directory operations with option to watch a directory.

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

A port is a fixed-width 16-bit number. An addr is a fixed-width 32-bit or 128-bit number (IPv4 or IPv6) or optionally a string such as "www.google.com" or "192.168.1.42" or "2001:db8::2:1". Later, I might add a dedicated API for DNS lookup, or perhaps for 'raw' Ethernet.

## Rejected or Deferred Effects APIs

### Extended Module System Access? Defer.

The 'load' effect is sufficient for common module system access, but it might be convenient to provide more reflection on the module system - e.g. identify which modules are visible in the filesystem, identifying which files are used, finding which modules currently fail to build. 

### Graphical User Interface? Defer.

I like the idea that applications should 'serve' GUI connections, which can allow for multiple users and views and works well with orthogonal persistence. A GUI connection to applications could be initiated by default, perhaps configurable by defining an environment variable. But I'm uncertain what such a connection should look like. 

I'm not in a hurry to solve this. Short term, we can support GUI via web-app, or perhaps use a existing remote desktop protocol. 

### Runtime Reflection? Defer.

It might be useful to provide some reflection on the environment. This could include call stacks, performance metrics, or traces of failed transactions. However, I'm uncertain what this API should look like, and it is also low priority. 
