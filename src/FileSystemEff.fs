
namespace Glas

module FileSystemEff = 
    // Adapter for single-threaded transactional access to filesystem.

    // Design Concerns:
    //
    // Reference Updates within Transactions.
    //
    //  It is possible for a transaction to close one file reference and open another,
    //  or perhaps to move references around. We'll need to handle this correctly, i.e.
    //  the set of references itself is a transactional memory of some sort.
    //  should fail, even if the reference is reopened by transaction failure. 
    //
    //  In any case, this suggests references must be tracked per-transaction. 
    //  
    //  When freshly opened, most ops except 'close' and 'status' should fail. This
    //  might simplify our implementation a little compared to trying to queue
    //  up writes.
    //
    // Deferred Writes and Undo Reads.
    //  
    //  When reading a file, we either allow the reader to track transactions or we
    //  track in the environment only. 
    //  or we can wrap the file stream with an 'unread' feature. An unread might
    //  be more convenient in other contexts, and reduces need to 'try' every open
    //  file. 
    //
    //  OTOH, if readers are transactional, they can more directly track status, 
    //  wait on read, and defer close-on-commit operations. Without this, we can
    //  still close a file as a generic deferred operation, but it's awkward for
    //  to track whether a reader has failed to read already.
    //
    //  If readers are not directly transactional, we'll also need to track when
    //  a read fails or has partial results within a transaction.
    //
    //
    // Writer Transactions?
    //
    //  When writing a file, we can either have the writer track transactions, or we
    //  can defer writes until final commit. The latter option is more convenient for
    //  most use-cases.
    //
    // Transaction Tasks:
    //  
    //  In general, when we 
    //  



    (*
* **file:FileOp** - namespace for file operations. An open file is essentially a cursor into a file resource, with access to buffered data. 
 * **open:(name:FileName, as:FileRef, for:Interaction)** - Create a new system object to interact with the specified file resource. Fails if FileRef is already in open, otherwise returns unit. Use 'status' The intended interaction must be specified:
  * *read* - read file as stream.
  * *write* - erase current content of file or start a new file.
  * *create* - same as write, except fails if the file already exists.
  * *append* - extend current content of file.
  * *delete* - remove a file. Use status to observe potential error.
  * *rename:NewFileName* - rename a file. 
 * **close:FileRef** - Release the file reference.
 * **read:(from:FileRef, count:Nat, exact?)** - read a list of count bytes or fewer if not enough data is available. Fails if the fileref is in an error state. Options:
  * *exact* - fail if fewer than count bytes available.
 * **write:(to:FileRef, data:Binary, flush?)** - write a list of bytes to file. Writes are buffered, so this doesn't necessarily fail even if the write will eventually fail.
 * **status:FileRef** - Return a representation of the state of the system object. 
  * *init* - initial status before 'open' request has been fully processed.
  * *wait* - empty read buffer or a full write buffer, either way program should wait.
  * *ready* - seems to be in a valid state to read or write data.
  * *done* - final status if file has been fully read, deleted, renamed, etc.
  * *error:Value* - any error state, description provided.
 * **refl** - return a list of open file references.

**dir:DirOp** - namespace for directory/folder operations. This includes browsing files, watching files. 
 * **open:(name:DirName, as:DirRef, for:Interaction)** - create new system objects to interact with the specified directory resource in a requested manner. Fails if DirRef is already in use, otherwise returns unit. Interactions:
  * *list:(recursive?)* - one-off read a list of entries from the directory. The 'recursive' flag may be included to list all child directories, too.
  * *watch:(list?, recursive?)* - watch for changes in a directory. If 'list' flag is set, we'll provide an initial set of events as if every file and folder was created just after watching. 
  * *rename:NewDirName* - rename or move a directory
  * *delete* - remove an empty directory.
 * **close:DirRef** - release the directory reference.
 * **read:DirRef** - read a file system entry. This is a record with ad-hoc fields, similar to a log message. Some defined fields:
  * *type:Symbol* (always) - usually a symbol 'file' or 'dir'
  * *name:Path* (always) - a full filename or directory name, usually a string
  * *deleted* (conditional) - flag, indicates this entry was deleted (mostly for 'watch').
  * *mtime:TimeStamp* (if available) - modify-time, uses Windows NT timestamps 
 * **status:FileRef**
 * **refl** - return a list of open directory references.

    *)    

    


    (*
    /// Transactional Stream (Wrapper)
    /// 
    /// Adds backtracking support to a dotnet Stream. Within a transaction, 
    /// writes are deferred and reads are recorded for undo. Reads are 
    type TXStream =
        // The stream being wrapped.
        val private Stream : System.IO.Stream

        // Bytes available for read due to prior, aborted transactions are
        // put back into the ReadBuffer for future reads.
        val mutable private ReadBuffer : FTList<byte>

        // Bytes currently being read by a background task are stored into
        // an intermediate buffer. 
        val mutable private BGReadBuffer : FTList<byte>

        // The stack of hierarchical transactions. Each transaction records 
        // bytes (written, read). 
        val mutable private TXStack : struct(FTList<byte> * FTList<byte>) list

        // A potential pending read operation, in case a prior read has timed out
        // or returned a partial result.
        val mutable private PendingRead : Task<FTList<byte>> option

        new(stream) =
            { Stream = stream
            ; ReadBuffer = FTList.empty
            ; TXStack = []
            ; PendingRead = None
            }

        member private self.AwaitPendingRead(wait_millis : int) : unit =
            match self.PendingRead with
            | Some task when task.Wait(wait_millis) ->
                self.PendingRead <- None
                self.ReadBuffer <- FTList.append (self.ReadBuffer) (task.Result)
            | _ -> ()

        member private self.SetPartialRead() : unit =
            // in case of a partial read, if there is no pending read we can
            // add a fake one to represent the partial read. This prevents 
            // further reads within the current transaction.
            if Option.isNone self.PendingRead then
                self.PendingRead <- Some (Task.FromResult (FTList.empty))

        member private self.TakeReadBuffer() =
            let result = self.ReadBuffer
            self.ReadBuffer <- FTList.empty
            result

        member private self.PartialRead(amt : uint64, wait_millis : int) : FTList<byte> =
            if List.isEmpty self.TXStack then
                // outside of a transaction, we can continue pending reads.
                self.AwaitPendingRead(wait_millis) 

            if (FTList.length self.ReadBuffer >= amt) then
                // sufficient data from prior aborted reads
                let (result,rem) = FTList.splitAt amt (self.ReadBuffer)
                self.ReadBuffer <- rem
                result
            elif (Option.isSome self.PendingRead) then
                // pending read, thus cannot start a new read
                self.TakeReadBuffer()
            else 
                // start a new read task.
                let rdAmt = amt - FTList.length self.ReadBuffer
                let readTask = 
                    async {
                        let arr = Array.zeroCreate (int rdAmt)
                        let rdCt = self.Stream.Read(arr,0,arr.Length)
                        let bytes = if (arr.Length = rdCt) then arr else Array.take rdCt arr
                        return bytes |> FTList.ofArray
                    } |> Async.StartAsTask 
                self.PendingRead <- Some readTask
                self.AwaitPendingRead(wait_millis)
                self.TakeReadBuffer()

        member self.Read(amt : int, ?wait_millis : int) : byte[] =
            if (amt < 0) then invalidArg (nameof amt) "read amt must be >= 0" else
            let millis = defaultArg wait_millis 10
            let bytesRead = self.PartialRead(uint64 amt, millis)
            if (FTList.length bytesRead < uint64 amt) then
                self.SetPartialRead() 
            match self.TXStack with
            | struct(wl,rl)::txs ->
                let rl' = FTList.append rl bytesRead
                self.TXStack <- struct(wl,rl')::txs
            | _ -> ()
            FTList.toArray bytesRead
            

        member self.Write(data : byte []) : unit =
            if not self.Stream.CanWrite then invalidOp "stream does not permit writes" else
            match self.TXStack with
            | struct(wl,rd)::txs ->
                let wl' = FTList.append wl (FTList.ofArray data)
                self.TXStack <- struct(wl',rd)::txs
            | [] -> // non-transactional write
                self.Stream.Write(data, 0, data.Length)

        interface ITransactional with
            member self.Try () =
                if List.isEmpty self.TXStack then
                    self.AwaitPendingRead(10)  // try to finish reads prior to transaction
                self.TXStack <- struct(FTList.empty, FTList.empty) :: self.TXStack
            member self.Commit () =
                match self.TXStack with
                | struct(w0,r0)::struct(ws,rs)::txs ->
                    // join hierarchical transaction
                    let ws' = FTList.append ws w0
                    let rs' = FTList.append rs r0
                    self.TXStack <- struct(ws',rs')::txs
                | [struct(wl,_)] ->
                    // commit transaction
                    self.TXStack <- []
                    let arr = FTList.toArray wl
                    self.Stream.Write(arr,0,arr.Length)
                | [] -> invalidOp "cannot commit, no active transaction."
            member self.Abort () = 
                match self.TXStack with
                | struct(_, r0)::txs ->
                    // putback reads performed by this transaction.
                    self.TXStack <- txs
                    self.ReadBuffer <- FTList.append r0 (self.ReadBuffer)
                | [] -> invalidOp "cannot abort, no active transaction."

    *)




