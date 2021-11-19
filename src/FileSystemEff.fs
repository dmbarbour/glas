
namespace Glas

module FileSystemEff = 
    open Value
    open System.Threading
    open System.Threading.Tasks
    open System.IO

    // Design Overview:
    //
    // All effects are written to an intermediate state then processed by a 
    // background loop. All feedback is also via the intermediate state. The
    // state is locked for the duration of a transaction. See SharedStateEff.
    //
    // The current implementation has a few known deficiencies:
    //
    // * The background loop touches every open file on every cycle, even
    //   if there is nothing to do for that file. This hinders scaling to
    //   a large number of open files. In practice, not a significant 
    //   concern.
    //
    // * The .NET `System.Console.OpenStandardInput()` implementation has
    //   a known bug in interactive mode. It will raise an exception if it
    //   attempts to read a line larger than the read buffer, rather than
    //   buffering remaining content of the line internally. This is mitigated
    //   by using a huge read buffer that is probably much bigger than a line
    //   of input.
    //
    // * The .NET Stream abstraction does not provide useful guarantees 
    //   about how long Reading a large buffer will wait with just a few
    //   bytes available before returning. The implementation behavior
    //   seems okay for most uses. But programs should depend on interfaces,
    //   not implementation.
    //
    // On the plus side, read and write efficiency are not too poor with
    // the current implementation. 
    //
    // These deficiencies are acceptable in the short term (which this is),
    // but should be resolved after bootstrap, e.g. bypassing the .NET API
    // and Glas systems implementing their own runtime environments.
    //

    module File = // for namespace
        // Number of bytes to try reading at once. Needs to be a lot larger
        // than a user input on interactive console, otherwise a .NET bug
        // will eat the user's input.
        let READ_BUFFER_SIZE = 10000
        let REF_STDIN = variant "std" (symbol "in")
        let REF_STDOUT = variant "std" (symbol "out")

        type Ref = Value
        type Path = string

        type Interaction = 
            // interactions for files
            | ForWrite of append: bool
            | ForRead
            | ForDelete
            | ForRename of Path

        let (|Interaction|_|) v =
            match v with
            | Variant "write" U -> Some (ForWrite (append = false))
            | Variant "append" U -> Some (ForWrite (append = true))
                // maybe add create, truncate options?
            | Variant "read" U -> Some ForRead
            | Variant "delete" U -> Some ForDelete
            | Variant "rename" (Value.String p') -> Some (ForRename p')
            | _ -> None

        

        let readerInteraction ia =
            match ia with
            | ForRead -> true
            | _ -> false
        
        let writerInteraction ia =
            match ia with
            | ForWrite _ -> true
            | _ -> false

        type Status =
            | Init              // runtime hasn't noticed yet
            | Live              // status during read/write
            | Done              // task complete
            | Error             // task halted on error

        let isFinalStatus st =
            match st with
            | Init | Live -> false
            | Done | Error _ -> true

        let printStatus s =
            match s with
            | Init -> symbol "init"
            | Live -> symbol "live"
            | Done -> symbol "done"
            | Error ->
                // currently not detailing errors, could extend this later 
                symbol "error" 

        type Action =
            | Read of Ref * uint64
            | Write of Ref * FTList<uint8>
            | Status of Ref
            | Close of Ref
            | Open of Ref * Path * Interaction
            | RefList 
            | RefMove of Ref * Ref

        let (|Action|_|) v =
            match v with
            | Variant "read" (FullRec ["ref"; "count"] ([vRef; Nat ct], U)) ->
                Some (Read (vRef,ct))
            | Variant "status" vRef -> 
                Some (Status vRef)
            | Variant "write" (FullRec ["ref"; "data"] ([vRef; Binary data], U)) ->
                Some (Write (vRef, FTList.ofArray data))
            | Variant "close" vRef -> 
                Some (Close vRef)
            | Variant "open" (FullRec ["as"; "name"; "for"] ([vRef; String p; Interaction fi], U)) ->
                Some (Open (vRef, p, fi))
            | Variant "ref" onRef -> 
                match onRef with
                | Variant "list" U -> Some RefList
                | Variant "move" (FullRec ["from"; "to"] ([srcRef; dstRef], U)) ->
                    Some (RefMove (srcRef, dstRef))
                | _ -> None
            | _ -> None


        // An intermediate reference layer simplifies interface with user-allocated
        // references. User-allocated references are a good idea for security and
        // stability reasons. The EntryId is never directly exposed to the program.
        type EntryId = int

        // Status for an opened file. 
        [<Struct>]
        type Ent =
            { Path          : Path
            ; Interaction   : Interaction       
            ; Status        : Status            // status for reporting to program
            ; Buffer        : FTList<uint8>     // input or output buffer, depending on interaction
            ; Detach        : bool              // if true, can close or disrupt connection to file
            } 

        let initEnt p ia =
            { Path = p
            ; Interaction = ia
            ; Status = Init
            ; Buffer = FTList.empty 
            ; Detach = false
            }

        let inline readOK ent =
            readerInteraction (ent.Interaction) && (Live = ent.Status)
        let inline writeOK ent =
            writerInteraction (ent.Interaction)

        // For now, using a minimalist implementation. This does not optimize for
        // background activity very well, i.e. the background loop must scan each
        // entry for changes in data buffers. This doesn't scale to having a large
        // number of open files, but shouldn't be a problem for bootstrap apps.
        [<Struct>]
        type State =
            { Bindings : Map<Ref, EntryId>      // program references to runtime entities
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime entities
            }

        let inline updEntById st entId fn =
            let ent' = fn <| Map.find entId (st.Entries)
            let entries' = Map.add entId ent' (st.Entries)
            { st with Entries = entries' }

        let inline private updEntObj (stateRef : State ref) (entId : EntryId) (fn : Ent -> Ent) =
            lock stateRef (fun () -> 
                stateRef := updEntById (!stateRef) entId fn
            )
        
        // Returns an unused EntryId. 
        // O(N) where N is number of concurrently open files.
        // Assumes number of open files is relatively small. 
        let unusedEntId (st:State) : EntryId =
            let rec searchLoop n =
                if (n < 0) then failwith "open file overflow" else
                if not (Map.containsKey n (st.Entries)) then n else
                searchLoop (n + 1)
            searchLoop 1000

        let inline tryUpdEntByRef st vRef fn =
            match Map.tryFind vRef (st.Bindings) with
            | None -> None // entry is not open.
            | Some entId -> 
                let ent = Map.find entId (st.Entries) // never fails for valid state
                match fn ent with
                | None -> None // update canceled
                | Some (ent', result) ->
                    let entries' = Map.add entId ent' (st.Entries)
                    let st' = { st with Entries = entries' }
                    Some (st', result)

        let tryUpdate (op:Action) (st:State) : (State * Value) option =
            match op with
            | Read (vRef, ct) -> 
                // Read from buffer of associated entry. 
                tryUpdEntByRef st vRef (fun ent ->
                    if not (readOK ent) then None else
                    let amtRead = min ct (FTList.length ent.Buffer)
                    if (0UL = amtRead) then None else
                    let (bytesRead, buffer') = FTList.splitAt amtRead (ent.Buffer)
                    let ent' = { ent with Buffer = buffer' }
                    let result = Value.ofFTList <| FTList.map (Value.u8) bytesRead 
                    Some (ent', result)
                )
            | Write (vRef, bytes) -> 
                tryUpdEntByRef st vRef (fun ent ->
                    if not (writeOK ent) then None else
                    let ent' = { ent with Buffer = FTList.append (ent.Buffer) bytes }
                    Some (ent', Value.unit)
                )
            | Status (vRef) -> 
                tryUpdEntByRef st vRef (fun ent ->
                    Some (ent, printStatus (ent.Status))
                )
            | Close (vRef) ->
                // Detach reference from Bindings; ask background loop to clean up later.
                match Map.tryFind vRef (st.Bindings) with
                | None -> None
                | Some entId ->
                    let ent = Map.find entId (st.Entries)
                    let ent' = { ent with Detach = true }
                    let entries' = Map.add entId ent' (st.Entries)
                    let bindings' = Map.remove vRef (st.Bindings)
                    let st' = { st with Bindings = bindings'; Entries = entries' }
                    Some (st', Value.unit)
            | Open (vRef, p, ia) ->
                // Create a new reference. Will be processed by background loop after commit.
                if Map.containsKey vRef (st.Bindings) then None else
                let entId = unusedEntId st
                let entries' = Map.add entId (initEnt p ia) (st.Entries)
                let bindings' = Map.add vRef entId (st.Bindings)
                let st' = { st with Bindings = bindings'; Entries = entries' }
                Some (st', Value.unit)
            | RefList ->
                // return a list of open file references.
                let keys = st.Bindings |> Map.toSeq |> Seq.map fst |> FTList.ofSeq |> Value.ofFTList
                Some (st, keys)
            | RefMove (srcRef, dstRef) ->
                // linearly reorganize file references.
                if Map.containsKey dstRef (st.Bindings) then None else
                match Map.tryFind srcRef (st.Bindings) with
                | None -> Some (st, Value.unit) // nop
                | Some entId ->
                    let bindings' = st.Bindings |> Map.remove srcRef |> Map.add dstRef entId
                    let st' = { st with Bindings = bindings' }
                    Some (st', Value.unit)

        // task to write some data
        let private writeData (stream : Stream) (bytes : FTList<uint8>) : unit =
            let arr = FTList.toArray bytes
            stream.Write(arr, 0, arr.Length)

        // task to read data from stream and buffer it into the associated entry.
        let rec private readData (stream : Stream) (cc : FTList<uint8> -> 'A) : 'A =
            let arr = Array.zeroCreate READ_BUFFER_SIZE
            let nBytes = stream.Read(arr, 0, arr.Length)
            let mutable lst = FTList.empty
            for ix = 0 to nBytes - 1 do
                lst <- FTList.snoc lst (arr.[ix])
            cc lst

        // add bytes to read buffer, or set status to Done if no bytes read
        let private addBytesReadToBuffer stateRef entId bytesRead =
            updEntObj stateRef entId (fun ent ->
                assert (readOK ent)
                if FTList.isEmpty bytesRead then
                    { ent with Status = Done }
                else
                    { ent with Buffer = FTList.append (ent.Buffer) bytesRead }
            )

        // BACKGROUND LOOPS
        //
        // The background loop will periodically scan entries and initiate background tasks to
        // perform any significant processing. It creates at most one task per entry at a time.
        // Thus, each open file can be processed in parallel.
        type BGState = Map<EntryId, Task<Stream>>

        let bgloop (stateRef : State ref) (bgState0 : BGState) : unit =
            failwith "todo: BG File Loop"

        // our initial background state includes stdin and stdout for console IO.
        let initialStates () : struct(State * BGState) =
            let eid_stdin = 0
            let eid_stdout = 1
            let ent_stdin = { initEnt "(stdin)" ForRead with Status = Live }
            let ent_stdout = { initEnt "(stdout)" (ForWrite (append=true)) with Status = Live }
            let st = 
                { Bindings = Map.ofList [(REF_STDIN, eid_stdin); (REF_STDOUT, eid_stdout)]
                ; Entries = Map.ofList [(eid_stdin, ent_stdin); (eid_stdout, ent_stdout)]
                }
            let s_stdin = System.Console.OpenStandardInput() |> Task.FromResult
            let s_stdout = System.Console.OpenStandardOutput() |> Task.FromResult
            let bg = Map.ofList [(eid_stdin, s_stdin); (eid_stdout, s_stdout)]
            struct(st,bg)









    module Dir = 
        type Ref = Value
        type Path = string

        type Interaction =
            | ForList of recursive:bool * watch:bool
            | ForDelete 
            | ForRename of Path

        let (|Interaction|_|) v =
            match v with
            | Variant "list" (Flags ["recursive"; "watch"] ([bRec; bWatch], U)) ->
                Some (ForList (recursive=bRec, watch=bWatch))
            | Variant "delete" U -> Some ForDelete
            | Variant "rename" (String p') -> Some (ForRename p')
            | _ -> None

        type EType =
            | EExist
            | EType
            | EAuth 
            | EPath 

        let printEType e =
            match e with
            | EExist -> symbol "exist"
            | EType -> symbol "type"
            | EAuth -> symbol "auth"
            | EPath -> symbol "path"

        type Status =
            | Init
            | Wait 
            | Ready
            | Done
            | Error of EType

        let printStatus s =
            match s with
            | Init -> symbol "init"
            | Wait -> symbol "wait"
            | Ready -> symbol "ready"
            | Done -> symbol "done"
            | Error e -> variant "error" (printEType e)

        type Action = 
            | Read of Ref
            | Open of Ref * Path * Interaction
            | Status of Ref
            | Refl
            | CWD 
            | Sep

        let (|Action|_|) v =
            match v with
            | Variant "read" vRef -> Some (Read vRef)
            | Variant "open" (FullRec ["name"; "as"; "for"] ([String p; vRef; Interaction di], U)) ->
                Some (Open (vRef, p, di))
            | Variant "status" vRef -> Some (Status vRef)
            | Variant "refl" U -> Some Refl
            | Variant "cwd" U -> Some CWD
            | Variant "sep" U -> Some Sep
            | _ -> None 




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




