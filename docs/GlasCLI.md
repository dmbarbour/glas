# Glas Command Line Interface

The glas command line supports only a few built-in operations:

        glas --run ValueRef -- Args
        glas --extract ValueRef
        glas --version
        glas --help

User-defined commands are supported via lightweight syntactic sugar:

        glas opname a b c 
            # implicitly rewrites to
        glas --run glas-cli-opname.main -- a b c

Ideally, most commands used in practice should be user-defined:

        glas print ValueRef
        glas type ValueRef
        glas repl

## Bootstrap

The bootstrap implementation for glas command line only needs to support the '--extract' command. Assuming suitable module definitions, bootstrap can be expressed with just a few lines of bash:

    # build
    /usr/bin/glas --extract glas-binary > /tmp/glas
    chmod +x /tmp/glas

    # verify
    /tmp/glas --extract glas-binary | cmp /tmp/glas

    # install
    sudo mv /tmp/glas /usr/bin/

In practice, we need different binaries for different operating systems and machine architectures. This can be conveniently supported by defining a global 'target' module that describes our compilation target. (Alternatively, we could define specific modules such as 'glas-binary-linux-x64'.)

## Value References

The '--run' and '--extract' commands must reference values in the Glas module system. However, it isn't necessary to reference arbitrary values, just enough to support user-defined commands and early development.

        ValueRef = ModuleRef ComponentPath
        ModuleRef = LocalModule | GlobalModule
        LocalModule = './' Word
        GlobalModule = Word
        ComponentPath = ('.' Word)*
        Word = WFrag('-'WFrag)*
        WFrag = [a-z][a-z0-9]*

A value reference starts by identifying a specific module, local or global. Global modules are folders found by searching GLAS_PATH environment variable, while local modules identify files or subfolders within the current working directory. The specified module is compiled to a Glas value, then a component value is extracted by following a dotted path (assuming null-terminated labels).

User-defined commands may use an extended ValueRef parser. However, I caution against sophisticated command-line parameters. Better to keep logic in the Glas module system rather than bury it within external scripts as parameters to glas commands.

## Extracting Binaries

The command-line tool will always include an option to directly extract binary data. 

        glas --extract ValueRef

The reference must evaluate to a binary (a list of bytes). This binary is written to stdout, then the command halts.

## Running Applications

        glas --run ValueRef -- List Of Args

This will interpret the referenced value as an [application](GlasApps.md). Initial parameter is `init:["List", "Of", "Args"]`. Final 'halt' value should be a bitstring, which we'll truncate and cast to a 32-bit int for the final exit code. At the moment, this requires a 'prog' header and 1--1 arity for the app. Eventually, we might support other app representations. 

## Standard Exit Codes

Mostly, we'll just return 0 or -1 depending on whether things are all good or not. If there are any problems, we'll write the details in the log, which is normally written to stderr.

        0       OK
        -1      Not OK (see log!)

## Proposed Effects API

The effects API is subject to gradual development.

### Language Module Effects

* **log:Message** - write a message to the log. Usually, this is written to the standard error stream.
* **load:ModuleRef** - load value of referenced module. ModuleRef may currently be 'local:String | global:String'. In context of live coding, module values may update between transactions.

### Environment Variables

* **env:get:String** - read-only access to environment variables. 
* **env:list** - returns a list of defined environment variables.

## Time

* **time:now** - Response is an estimated, logical time of commit, as a TimeStamp value.
* **time:check:TimeStamp** - If 'now' is equal or greater to TimeStamp, respond with unit. Otherwise fail. Useful for expressing waits, but only if we also optimize for incremental computing.

TimeStamp will be given as NT time: a natural number of 0.1 microsecond intervals since midnight, January 1, 1601 UT.

### Noise

* **fork:[List, Of, Values]** - returns a value chosen non-deterministically from the list. Is not necessarily a random choice. Can model concurrency, but requires several optimizations (incremental computing, replication on stable choice) to do so efficiently. 
* **random:N** - response is cryptographically random binary of N bytes. Weak stability (i.e. usually same bytes after failed transaction).

### Console

Console IO is modeled as filesystem access with `std:in` and `std:out` as implicitly open file references. 

### Filesystem Access

Console apps are unavoidably related to the filesystem. The conventional API mostly works, but must be adapted for asynchronous interaction. Relevantly, we do not support implicit pushback on 'write'.  

* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:Interaction)** - Response is unit, or failure if the FileRef is already in use. Binds a new filesystem interaction to the given FileRef. Usually does not wait on OS (see 'status').
  * *read* - read file as stream. Status is set to 'done' when last byte is available, even if it hasn't been read yet.
  * *write* - open file and write from beginning. Will delete content in existing file.
  * *append* - open file and write start writing at end. Will create a new file if needed.
  * *delete* - remove a file. Use status to observe potential error.
  * *move:NewFileName* - rename a file. Use status to observe error.
 * **close:FileRef** - Release the file reference.
 * **read:(from:FileRef, count:Nat)** - Response is list of up to Count available bytes taken from input stream. Returns fewer than Count if input buffer is empty. 
 * **write:(to:FileRef, data:Binary)** - write a list of bytes to file. Fails if not opened for write or append. Use 'busy' status for heuristic pushback.
 * **status:FileRef** - Returns a record that may contain one or many status flags or values.
  * *init* - the 'open' request has not yet been seen by OS. Need to wait for transaction to end.
  * *ready* - further interaction is possible, e.g. read buffer has data available, or you're free to write.
  * *busy* - has an active background task, e.g. write buffer not empty at start of transaction.
  * *done* - successful termination of interaction.
  * *error:Message* - reports an error, with some extra description.
 * **ref:list** - return a list of open file references. 
 * **ref:move:(from:FileRef, to:FileRef)** - reorganize references. Fails if 'to' ref is in use. 

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:DirName, as:DirRef, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Fails if DirRef is already in use, otherwise returns unit. Potential Interactions:
  * *list* - read a list of entries from the directory. Reaches Done state after all items are read.
  * *move:NewDirName* - rename or move a directory. Use status to observe error.
  * *delete:(recursive?)* - remove an empty directory, or flag for recursive deletion.
 * **close:DirRef** - release the directory reference.
 * **read:DirRef** - read a file system entry, or fail if input buffer is empty. This is a record with ad-hoc fields including at least 'type' and 'name'. Some potential fields:
  * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
  * *name:Path* (always) - a full filename or directory name, usually a string
  * *mtime:TimeStamp* (optional) - modify time 
  * *ctime:TimeStamp* (optional) - creation time 
  * *size:Nat* (optional) - number of bytes
 * **status:DirRef** - ~ same as file status
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
   * *addr* - indicates which local network cards or ethernet interfaces to bind. Can be a string or bitstring. If omitted, attempts to bind all interfaces.
  * **accept:(from:ListenerRef, as:TcpRef)** - Receive an incoming connection, and bind the new connection to the specified TcpRef. This operation will fail if there is no pending connection. 
  * **status:ListenerRef** ~ same as file status
  * **info:ListenerRef** - For active listener, returns a list of local `(port:Port, addr:Addr)` pairs for that are being listened on. Fails in case of 'init' or 'error' status.
  * **close:ListenerRef** - Release listener reference and associated resources.
  * **ref:list** - returns list of open listener refs 
  * **ref:move:(from:ListenerRef, to:ListenerRef)** - reorganize references. Cannot move to an open ref.
 * **connect:(dst:(port:Port, addr:Addr), src?(port?Port, addr?Addr), as:TcpRef)** - Create a new connection to a remote TCP port. Fails if TcpRef is already in use, otherwise returns unit. Whether the connection is successful is observable via 'state' a short while after the request is committed. Destination port and address must be specified, but source port and address are usually unspecified and determined dynamically by the OS.
 * **read:(from:TcpRef, count:N)** - read 1 to N bytes, limited by available data, returned as a list. Fails if no bytes are available - see 'status' to diagnose error vs. end of input. 
 * **write:(to:TcpRef, data:Binary)** - write binary data to the TCP connection. The binary is represented by a list of bytes. Use 'busy' status for heuristic pushback.
 * **limit:(of:Ref, cap:Count)** - fails if number of bytes pending in the write buffer is greater than Count or if connection is closed, otherwise succeeds returning unit. Not necessarily accurate or precise. This method is useful for pushback, to limit a writer that is faster than a remote reader.
 * **status:TcpRef** ~ same as file status
 * **info:TcpRef** - Returns a `(dst:(port, addr), src:(port, addr))` pair after TCP connection is active. May fail in some cases (e.g. 'init' or 'error' status).
 * **close:TcpRef**
 * **ref:list** - returns list of open TCP refs 
 * **ref:move:(from:TcpRef, to:TcpRef)** - reorganize TCP refs. Fails if 'to' ref is in use.

* **udp:UdpOp** - namespace for UDP operations. UDP messages use `(port, addr, data)` triples, with port and address refering to the remote endpoint.
 * **connect:(port?Port, addr?Addr, as:UdpRef)** - Bind a local UDP port, potentially across multiple ethernet interfaces. Fails if UdpRef is already in use, otherwise returns unit. Whether binding is successful is observable via 'state' after the request is committed. Options:
  * *port* - normally included to determine which port to bind, but may be left to dynamic allocation. 
  * *addr* - indicates which local ethernet interfaces to bind; if unspecified, attempts to binds all interfaces.
 * **read:(from:UdpRef)** - returns the next available UDP message value. 
 * **write(to:UdpRef, data:Message)** - output a UDP message
 
  using same `(port, addr, data)` record as messages read. Returns unit. Write may fail if the connection is in an error state, and attempting to write to an invalid port or address or oversized packets may result in an error state.
 * **status:UdpRef** ~ same as file status
 * **info:UdpRef** - Returns a list of `(port:Port, addr:Addr)` pairs for the local endpoint.
 * **close:UdpRef** - Return reference to unused state, releasing system resources.
 * **ref:list** - returns list of open UDP refs.
 * **ref:move:(from:UdpRef, to:UdpRef)** - reorganize UDP refs. Fails if 'to' ref is in use.

A port is a fixed-width 16-bit number. An addr is a fixed-width 32-bit or 128-bit bitstring (IPv4 or IPv6) or a text string such as "www.example.com" or "192.168.1.42" or "2001:db8::2:1". Later, I might add a dedicated API for DNS lookup, or perhaps for 'raw' Ethernet.

## Rejected or Deferred Effects APIs

### Extended Module System Access? Defer.

The 'load' effect is sufficient for common module system access, but it might be convenient to provide more reflection on the module system - e.g. identify which modules are visible in the filesystem, identifying which files are used, finding which modules currently fail to build. 

### Graphical User Interface? Defer.

Assuming network support, we can indirectly support GUI applications via X11, RDP, HTTP, VNC, or other protocols. Similarly for sound. This should be sufficient for many use-cases and avoids the challenge of reinventing UI models.

However, it is potentially interesting to reinvent UI models and sound in context of transaction machines and reference to content-addressed storage. We can potentially simplify many things.

### Runtime Reflection? Defer.

It might be useful to provide some reflection on the environment. 

* API information
* access to Glas Object representations of data, including stowage references
* performance analysis support, e.g. know how often a transaction fails or is interrupted.

This is something to return to when I have a better idea of what is needed.

### Foreign Function Interface? Defer.

Reference to external DLLs is a terrible fit for my vision of Glas systems. However, Glas systems can feasibly embed DLLs (and headers) within the module system. This would make the DLL code accessible in the module system for analysis, synthesis, simulation, optimization, and so on. Further, it would simplify precise reference and distribution via content-addressed storage.

Applications represented as Glas programs, i.e. with the 'prog' header, are too high level to leverage embedded DLLs. But it is feasible to eventually extend 'glas --run' to support other representations. This could include an option to interpret a low-level app representation without compiling a separate executable.
