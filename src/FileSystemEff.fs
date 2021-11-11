
namespace Glas

module FileSystemEff = 
    open System.Threading

    // Design Overview:
    //
    // I'm planning to build upon the SharedStateEff framework from Effects.
    // This means I need:
    //
    // - a request type and parser (Value -> Maybe Request)
    // - a response type and printer (Response -> Value). Could use Value and id.
    // - a state type (State) and ref, State should be value type.
    // - a request handler `Request -> State -> (Response * State) option`.
    // - a background thread that operates entirely on the State reference.
    //
    // We can optionally wait on the State reference; it will be pulsed upon
    // update. 

    // Just building up an API.
    
    // Aliases to integrate some documentation into the types.
    type FileRef = Value
    type FilePath = String
    type DirRef = Value
    type DirPath = String

    /// Recognized errors. Shared for files and directories.
    type EType =
        | EDoesNotExist
        | EAlreadyExists
        | ENotAFile 
        | ENotADir 
        | EUnauthorizedAccess 
        | EPathTooLong 

    /// Status is reused for files and directories.
    type Status = 
        | Init
        | Wait 
        | Ready
        | Done
        | Error of EType

    /// Different ways of opening a file.
    type FileInteraction = 
        // interactions for files
        | FileWrite
        | FileRead
        | FileAppend
        | FileDelete
        | FileRename of FilePath
    
    type FileAction =
        | FileRead of { Ref: FileRef; Count: int }
        | FileStatus of FileRef
        | FileWrite of FileRef * FTList<byte>
        | FileClose of FileRef
        | FileOpen of FileRef * FilePath * FileInteraction
        | FileRefl // list open file refs.

    /// Different ways of opening a directory.
    type DirInteraction =
        // interactions for directories
        | DirList
        | DirWatch
        | DirDelete
        | DirRename of DirPath

    type DirAction =
        | DirRead of DirRef
        | DirOpen of DirRef * DirPath * DirInteraction
        | DirStatus of DirRef
        | DirRefl

    type Action =
        | OnFile of FileAction
        | OnDir of DirAction 

    /// The Shared State
    ///
    /// The state in this case consists of:
    ///
    ///  - a todo list of tasks to be executed by runtime thread; includes writes
    ///  - input buffers for available input data. A runtime can observe whether
    ///    these need to be refilled as basis for pushback. It might be useful to
    ///    add an 'unread' operation.
    ///  - status information for all open files and directories.
    ///
    ///




    /// Shared state is mostly a list of tasks for the background thread to run
    /// after the transaction is committed, plus a record for status information
    /// provided by the runtime. 






    (*
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




