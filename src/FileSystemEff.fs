
namespace Glas

module FileSystemEff = 
    open Value
    open System.Threading
    open System.Threading.Tasks
    open System.IO

    // Design Overview:
    //
    // Effects are written to an intermediate shared state then processed by a 
    // background loop. Feedback is also provided through the shared state. The
    // state is synchronized by the .NET monitor object. (See SharedStateEff.)
    //
    // The current implementation has a few known deficiencies:
    //
    // * The background loop touches every open file on every cycle, even when
    //   there is nothing to do for a file. Thus, the loop becomes inefficient
    //   if there are too many open files. I could fix this by introducing an
    //   action queue if required, but it's low priority for the moment.
    //
    // * The .NET `System.Console.OpenStandardInput()` implementation has known
    //   bug in interactive mode, at least for Linux. It will raise an exception
    //   if it attempts to read a line larger than the read buffer. To mitigate,
    //   I use a read buffer that is likely larger than interactive user input.
    //
    // * The .NET Stream abstraction does not provide useful interfaces to express
    //   read or write timeouts. Thus, we're forced to dedicate async tasks or
    //   threads to reads and writes. 
    //  
    // These deficiencies are acceptable for short term use (which this is), but
    // should be around the same time we optimize transaction loops.
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


        // EntryId provides a stable reference for the runtime 'object', separate
        // from the program's reference to the object. This avoids issues related  
        // to exposing runtime allocations to the program or reorganizing program
        // references.
        type EntryId = int

        // Record for an individual open file. 
        [<Struct>]
        type Ent =
            { Path          : Path                  // filesystem reference (from open)
            ; Interaction   : Interaction           // activity (from open)
            ; Status        : Status                // status for reporting to program
            ; Buffer        : FTList<uint8>         // input or output buffer, depending on interaction
            ; Stream        : Stream option         // stream used for reads and writes
            ; Activity      : Task<unit>            // background operation for entry (one at a time!)
            ; Detach        : bool                  // if true, no future interaction from program
            } 

        let initEnt p ia =
            { Path = p
            ; Interaction = ia
            ; Status = Init
            ; Buffer = FTList.empty 
            ; Stream = None
            ; Activity = Task.FromResult(())
            ; Detach = false
            }

        let inline readOK ent =
            readerInteraction (ent.Interaction) && (Live = ent.Status)
        let inline writeOK ent =
            writerInteraction (ent.Interaction)

        // Tracking multiple open files. 
        [<Struct>]
        type State =
            { Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            //; Active   : Set<EntryId>
            }


        let inline private onRef (v : 'A ref) (fn : 'A -> ('A * 'B)) : 'B =
            lock v (fun () ->
                let (s,r) = fn (v.Value)
                v.Value <- s
                r
            )

        let inline updEntById entId fn st =
            let ent = Map.find entId (st.Entries)
            let (ent', result) = fn ent
            let entries' = Map.add entId ent' (st.Entries)
            ({ st with Entries = entries' }, result)

        let inline private updEntObj (st : State ref) (entId : EntryId) (fn : Ent -> (Ent * 'A)) : 'A =
            onRef st (updEntById entId fn)
        
        // Returns an unused EntryId. 
        // O(N) where N is number of concurrently open files. This is not
        // efficient, but is adequate in context of bootstrapping.
        let private unusedEntId (st:State) : EntryId =
            let rec searchLoop n =
                if (n < 0) then failwith "open file overflow" else
                if not (Map.containsKey n (st.Entries)) then n else
                searchLoop (n + 1)
            searchLoop 1000

        let inline private tryUpdEntByRef st vRef fn =
            match Map.tryFind vRef (st.Bindings) with
            | None -> None // entry is not open.
            | Some entId -> 
                let ent = Map.find entId (st.Entries) // never fails for valid state
                match fn ent with
                | None -> None // update canceled
                | Some (result, ent') ->
                    let entries' = Map.add entId ent' (st.Entries)
                    let st' = { st with Entries = entries' }
                    Some (result, st')

        let tryUpdate (op:Action) (st:State) : (Value * State) option =
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
                    Some (result, ent')
                )
            | Write (vRef, bytes) -> 
                tryUpdEntByRef st vRef (fun ent ->
                    if not (writeOK ent) then None else
                    let ent' = { ent with Buffer = FTList.append (ent.Buffer) bytes }
                    Some (Value.unit, ent')
                )
            | Status (vRef) -> 
                tryUpdEntByRef st vRef (fun ent ->
                    Some (printStatus (ent.Status), ent)
                )
            | Close (vRef) ->
                // Detach reference from Bindings; background loop will clean up later.
                match Map.tryFind vRef (st.Bindings) with
                | None -> None
                | Some entId ->
                    let ent = Map.find entId (st.Entries)
                    let ent' = { ent with Detach = true }
                    let entries' = Map.add entId ent' (st.Entries)
                    let bindings' = Map.remove vRef (st.Bindings)
                    let st' = { st with Bindings = bindings'; Entries = entries' }
                    Some (Value.unit, st')
            | Open (vRef, p, ia) ->
                // Create a new reference. Will be processed by background loop after commit.
                if Map.containsKey vRef (st.Bindings) then None else
                let entId = unusedEntId st
                let entries' = Map.add entId (initEnt p ia) (st.Entries)
                let bindings' = Map.add vRef entId (st.Bindings)
                let st' = { st with Bindings = bindings'; Entries = entries' }
                Some (Value.unit, st')
            | RefList ->
                // return a list of open file references.
                let keys = st.Bindings |> Map.toSeq |> Seq.map fst |> FTList.ofSeq |> Value.ofFTList
                Some (keys, st)
            | RefMove (srcRef, dstRef) ->
                // linearly reorganize file references.
                if Map.containsKey dstRef (st.Bindings) then None else
                match Map.tryFind srcRef (st.Bindings) with
                | None -> Some (Value.unit, st) // nop
                | Some entId ->
                    let bindings' = st.Bindings |> Map.remove srcRef |> Map.add dstRef entId
                    let st' = { st with Bindings = bindings' }
                    Some (Value.unit, st')


        let inline private detachEnt (st : State ref) entId ent =
            st.Value <- { st.Value with Entries = Map.remove entId (st.Value.Entries) }
            match ent.Stream with
            | None -> ()
            | Some s -> s.Dispose()

        let inline private updateEnt (st : State ref) entId ent =
            st.Value <- { st.Value with Entries = Map.add entId ent (st.Value.Entries) }



        // write from FTList of bytes.
        let private asyncWrite (stream : Stream) (bytes : FTList<uint8>) : Async<unit> =
            async {
                let arr = FTList.toArray bytes
                do! stream.WriteAsync(arr, 0, arr.Length) |> Async.AwaitTask 
            } 

        // async read to FTList of bytes.
        let private asyncRead (stream : Stream) (count : int) : Async<FTList<uint8>> =
            async {
                let arr = Array.zeroCreate count
                let! nBytes = stream.ReadAsync(arr, 0, arr.Length) |> Async.AwaitTask
                let mutable lst = FTList.empty
                for ix = 0 to nBytes - 1 do
                    lst <- FTList.snoc lst (arr.[ix])
                return lst
            } 

        let private bgFillBuffer stream (st : State ref) entId =
            async {
                let mutable halt = false
                while not halt do
                    let! bytes = asyncRead stream READ_BUFFER_SIZE
                    halt <- lock st (fun () ->
                        let ent = Map.find entId (st.Value.Entries)
                        let ent', bDone = 
                            if FTList.isEmpty bytes then
                                ({ ent with Status = Done }, true)
                            else
                                let buff' = FTList.append (ent.Buffer) bytes
                                ({ ent with Buffer = buff' }, FTList.length buff' >= uint64 READ_BUFFER_SIZE)
                        updateEnt st entId ent'
                        bDone
                        )
            } |> Async.StartAsTask

        // Note: Currently I simply scan all entries for activity. This doesn't scale nicely
        // if there are hundreds of open files, but it's fine for tens of open files. I think
        // bootstrap apps will have fewer than ten open at a time.
        let mainLoop (st : State ref) : unit = lock st (fun () -> 
            while true do 
                ignore <| Monitor.Wait(st, 1000) // 1 Hz if not triggered by program.
                for (entId, ent) in Map.toSeq (st.Value.Entries) do
                    match ent.Interaction with
                    | ForWrite (append=bAppend) ->
                        match ent.Status with
                        | Init ->
                            assert(Option.isNone ent.Stream)
                            try 
                                let fmode = if bAppend then FileMode.Append else FileMode.Create
                                let fstream = System.IO.File.Open(ent.Path, fmode, FileAccess.Write)
                                let s = fstream :> Stream 
                                let task0 = asyncWrite s (ent.Buffer) |> Async.StartAsTask
                                updateEnt st entId { ent with Status = Live; Buffer = FTList.empty; Stream = Some s; Activity = task0 }
                            with
                            | _ -> 
                                updateEnt st entId { ent with Status = Error }
                        | Live when not (FTList.isEmpty ent.Buffer) ->
                            let task' = Async.StartAsTask <| async { 
                                do! Async.AwaitTask ent.Activity
                                do! asyncWrite (Option.get ent.Stream) (ent.Buffer)
                                } 
                            updateEnt st entId { ent with Buffer = FTList.empty; Activity = task' }
                        | _ when ent.Detach && ent.Activity.IsCompleted ->
                            // NOTE: This must appear after Live to ensure we flush remaining bytes in buffer.
                            detachEnt st entId ent
                        | _ -> () // nop
                    | ForRead ->
                        match ent.Status with
                        | _ when ent.Detach ->
                            if ent.Activity.IsCompleted then
                                detachEnt st entId ent
                        | Init ->
                            try
                                let fstream = File.Open(ent.Path, FileMode.Open, FileAccess.Read)
                                let s = fstream :> Stream
                                let task0 = bgFillBuffer s st entId
                                updateEnt st entId { ent with Status = Live; Stream = Some s; Activity = task0 }
                            with
                            | _ -> 
                                updateEnt st entId { ent with Status = Error }
                        | Live when (ent.Activity.IsCompleted) && (FTList.length ent.Buffer < uint64 READ_BUFFER_SIZE) ->
                            let s = Option.get ent.Stream
                            let task' = bgFillBuffer s st entId 
                            updateEnt st entId { ent with Activity = task' }
                        | _ -> ()
                    | ForDelete ->
                        match ent.Status with
                        | Init ->
                            try 
                                File.Delete(ent.Path)
                                updateEnt st entId { ent with Status = Done }
                            with 
                            | _ -> 
                                updateEnt st entId { ent with Status = Error }
                        | Live -> failwith "unexpected state for file deletion" 
                        | Done | Error -> ()
                    | ForRename (sNewName) ->
                        match ent.Status with
                        | Init ->
                            try 
                                File.Move(ent.Path, sNewName, false)
                                updateEnt st entId { ent with Status = Done }
                            with 
                            | _ ->
                                updateEnt st entId { ent with Status = Error } 
                        | Live -> failwith "unexpected state for file rename"
                        | Done | Error -> () // nop
          ) // end lock

        // our initial background state includes stdin and stdout for console IO.
        let initialState () : State =
            let eid_stdin = 0
            let eid_stdout = 1
            let s_stdin = System.Console.OpenStandardInput() 
            let s_stdout = System.Console.OpenStandardOutput()
            let ent_stdin = { initEnt "(stdin)" ForRead with Status = Live; Stream = Some s_stdin }
            let ent_stdout = { initEnt "(stdout)" (ForWrite (append=true)) with Status = Live; Stream = Some s_stdout }
            { Bindings = Map.ofList [(REF_STDIN, eid_stdin); (REF_STDOUT, eid_stdout)]
            ; Entries = Map.ofList [(eid_stdin, ent_stdin); (eid_stdout, ent_stdout)]
            }

        let initEff () : Effects.IEffHandler =
            let state = ref (initialState ())
            let parser = (|Action|_|)
            let action = tryUpdate
            let writer = id
            let bgThread () = mainLoop state
            Thread(bgThread).Start()
            Effects.SharedStateEff(state,parser,action,writer) |> Effects.selectHeader "file"

        // TODO: Consider providing method to halt bg thread (low priority)

    module Dir = 
        type Ref = Value
        type Path = string

        type Interaction =
            | ForList
            | ForDelete of recursive:bool
            | ForRename of Path

        let isReadInteraction ia =
            match ia with
            | ForList -> true
            | ForDelete _ | ForRename _ -> false

        let (|Interaction|_|) v =
            match v with
            | Variant "list" U -> Some ForList
            | Variant "delete" (Flags ["recursive"] ([bRec],U)) -> Some (ForDelete (recursive=bRec))
            | Variant "rename" (String p') -> Some (ForRename p')
            | _ -> None

        type Status =
            | Init
            | Live
            | Done
            | Error

        let printStatus s =
            match s with
            | Init -> symbol "init"
            | Live -> symbol "live"
            | Done -> symbol "done"
            | Error -> symbol "error"

        type Action = 
            | Read of Ref
            | Open of Ref * Path * Interaction
            | Close of Ref
            | Status of Ref
            | CWD 
            | Sep
            | RefList 
            | RefMove of Ref * Ref
            // support to rename or delete directories is deferred

        let (|Action|_|) v =
            match v with
            | Variant "read" vRef -> Some (Read vRef)
            | Variant "open" (FullRec ["name"; "as"; "for"] ([String p; vRef; Interaction di], U)) ->
                Some (Open (vRef, p, di))
            | Variant "close" vRef -> Some (Close vRef)
            | Variant "status" vRef -> Some (Status vRef)
            | Variant "cwd" U -> Some CWD
            | Variant "sep" U -> Some Sep
            | Variant "ref" onRef -> 
                match onRef with
                | Variant "list" U -> Some RefList
                | Variant "move" (FullRec ["from"; "to"] ([srcRef; dstRef], U)) ->
                    Some (RefMove (srcRef, dstRef))
                | _ -> None
            | _ -> None 

        // EntryId provides a stable reference for the runtime 'object', separate
        // from the program's reference to the object. This avoids issues related  
        // to exposing runtime allocations to the program or reorganizing program
        // references.
        type EntryId = int

        // Record for an individual open directory. 
        [<Struct>]
        type Ent =
            { Path          : Path                  // filesystem reference (from open)
            ; Interaction   : Interaction           // activity (from open)
            ; ReadBuf       : Value list            // values available for reading
            ; Status        : Status                // status for reporting to program
            ; Detach        : bool                  // if true, no future interaction from program
            } 


        let initEnt p ia =
            { Path = p
            ; Interaction = ia
            ; ReadBuf = []
            ; Status = Init
            ; Detach = false
            }

        // Tracking multiple open files. 
        [<Struct>]
        type State =
            { Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            }

        let initialState () =
            { Bindings = Map.empty
            ; Entries = Map.empty
            }

        // Returns an unused EntryId. 
        // O(N) where N is number of concurrently open files. This is not
        // efficient, but is adequate in context of bootstrapping.
        let private unusedEntId (st:State) : EntryId =
            let rec searchLoop n =
                if (n < 0) then failwith "open directory overflow" else
                if not (Map.containsKey n (st.Entries)) then n else
                searchLoop (n + 1)
            searchLoop 1000

        let inline private tryUpdEntByRef st vRef fn =
            match Map.tryFind vRef (st.Bindings) with
            | None -> None // entry is not open.
            | Some entId -> 
                let ent = Map.find entId (st.Entries) // never fails for valid state
                match fn ent with
                | None -> None // update canceled
                | Some (result, ent') ->
                    let entries' = Map.add entId ent' (st.Entries)
                    let st' = { st with Entries = entries' }
                    Some (result, st')

        let inline private readBufStatus l =
            if List.isEmpty l then Done else Live 



        let tryUpdate (op:Action) (st:State) : (Value * State) option =
            match op with
            | Read vRef -> 
                // Read from buffer of associated entry. 
                tryUpdEntByRef st vRef (fun ent ->
                    let readOk = not (List.isEmpty (ent.ReadBuf))
                    if not readOk then None else
                    let vRead = List.head (ent.ReadBuf)
                    let rdBuf' = List.tail (ent.ReadBuf)
                    let status' = readBufStatus rdBuf'
                    let ent' = { ent with Status = status'; ReadBuf = rdBuf' }
                    Some (vRead, ent')
                )
            | Status (vRef) -> 
                tryUpdEntByRef st vRef (fun ent ->
                    Some (printStatus (ent.Status), ent)
                )
            | Close (vRef) ->
                // Detach reference from Bindings; background loop will clean up later.
                match Map.tryFind vRef (st.Bindings) with
                | None -> None
                | Some entId ->
                    let ent = Map.find entId (st.Entries)
                    let ent' = { ent with Detach = true }
                    let entries' = Map.add entId ent' (st.Entries)
                    let bindings' = Map.remove vRef (st.Bindings)
                    let st' = { st with Bindings = bindings'; Entries = entries' }
                    Some (Value.unit, st')
            | Open (vRef, p, ia) ->
                // Create a new reference. Will be processed by background loop after commit.
                if Map.containsKey vRef (st.Bindings) then None else
                let entId = unusedEntId st
                let entries' = Map.add entId (initEnt p ia) (st.Entries)
                let bindings' = Map.add vRef entId (st.Bindings)
                let st' = { st with Bindings = bindings'; Entries = entries' }
                Some (Value.unit, st')
            | CWD ->
                let s = Directory.GetCurrentDirectory()
                Some (ofString s, st)
            | Sep ->
                let s = Path.DirectorySeparatorChar |> System.Char.ToString
                Some (ofString s, st)
            | RefList ->
                // return a list of open file references.
                let keys = st.Bindings |> Map.toSeq |> Seq.map fst |> FTList.ofSeq |> Value.ofFTList
                Some (keys, st)
            | RefMove (srcRef, dstRef) ->
                // linearly reorganize file references.
                if Map.containsKey dstRef (st.Bindings) then None else
                match Map.tryFind srcRef (st.Bindings) with
                | None -> Some (Value.unit, st) // nop
                | Some entId ->
                    let bindings' = st.Bindings |> Map.remove srcRef |> Map.add dstRef entId
                    let st' = { st with Bindings = bindings' }
                    Some (Value.unit, st')

        let inline private detachEnt (st : State ref) (entId : EntryId) : unit =
            st.Value <- { st.Value with Entries = Map.remove entId (st.Value.Entries) }

        let inline private updateEnt (st : State ref) (entId : EntryId) (ent : Ent) : unit =
            st.Value <- { st.Value with Entries = Map.add entId ent (st.Value.Entries) }

        let dirEntry (p : Path) =
            let mtime = Directory.GetLastWriteTimeUtc(p).ToFileTime() |> uint64
            let ctime = Directory.GetCreationTimeUtc(p).ToFileTime() |> uint64
            //let di = System.IO.DirectoryInfo(p)
            //attributes for tracing links might be useful
            Value.asRecord ["type"; "name"; "mtime"; "ctime"] 
                           [Value.symbol "dir"; Value.ofString p; Value.nat mtime; Value.nat ctime]

        let fileEntry (p : File.Path) =
            //let atime = File.GetLastAccessTimeUtc(p).ToFileTime()
            let mtime = File.GetLastWriteTimeUtc(p).ToFileTime() |> uint64
            let ctime = File.GetCreationTimeUtc(p).ToFileTime() |> uint64
            // consider file attributes
            // attributes for tracing links might be useful 
            Value.asRecord ["type"; "name"; "mtime"; "ctime"]
                           [Value.symbol "file"; Value.ofString p; Value.nat mtime; Value.nat ctime]

        let readDir (p : Path) : Value list =
            let subdirs = System.IO.Directory.EnumerateDirectories(p) |> Seq.map dirEntry
            let files = System.IO.Directory.EnumerateFiles(p) |> Seq.map fileEntry
            Seq.toList (Seq.append subdirs files)

        // Note: Currently I simply scan all entries for activity. 
        let mainLoop (st : State ref) : unit = lock st (fun () -> 
            while true do 
                ignore <| Monitor.Wait(st, 1000) // 1 Hz if not triggered by program.
                for (entId, ent) in Map.toSeq (st.Value.Entries) do
                    match ent.Interaction with
                    | ForList ->
                        match ent.Status with
                        | _ when ent.Detach ->
                            detachEnt st entId 
                        | Init ->
                            try
                                let rdBuf = readDir (ent.Path)
                                let status = readBufStatus rdBuf
                                updateEnt st entId { ent with Status = status; ReadBuf = rdBuf }
                            with
                            | _ -> 
                                updateEnt st entId { ent with Status = Error }
                        | _ -> ()
                    | ForDelete (recursive=bRec) ->
                        match ent.Status with
                        | Init ->
                            try 
                                Directory.Delete(ent.Path, bRec)
                                updateEnt st entId { ent with Status = Done }
                            with 
                            | _ -> 
                                updateEnt st entId { ent with Status = Error }
                        | _ when ent.Detach ->
                            detachEnt st entId
                        | _ -> () // nop
                    | ForRename (sNewName) ->
                        match ent.Status with
                        | Init ->
                            try 
                                Directory.Move(ent.Path, sNewName)
                                updateEnt st entId { ent with Status = Done }
                            with 
                            | _ ->
                                updateEnt st entId { ent with Status = Error } 
                        | _ when ent.Detach ->
                            detachEnt st entId
                        | _ -> () // nop
          ) // end lock

