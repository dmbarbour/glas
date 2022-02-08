
namespace Glas

module NetworkEff =
    open Value
    open System.Threading
    open System.Threading.Tasks
    open System.IO
    open System.Net
    open System.Net.Sockets

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
        | Error ->
            // currently not detailing errors, could extend this later 
            symbol "error" 

    type Port = uint16
    type IPAddr = 
        | IPv4 of Bits
        | IPv6 of Bits
        | DN of string

    let (|Port|_|) v = 
        match v with
        | Bits (Bits.U16 n) -> Some n
        | _ -> None

    let (|IPAddr|_|) v =
        match v with
        | String s -> Some (DN s)
        | Bits b when (32 = Bits.length b) -> Some (IPv4 b)
        | Bits b when (128 = Bits.length b) -> Some (IPv6 b)
        | _ -> None

    let inline private OptField fn vOpt =
        match vOpt with
        | None -> Some None
        | Some v ->
            match fn v with 
            | None -> None
            | r -> Some r

    let (|OptPort|_|) = OptField (|Port|_|)
    let (|OptIPAddr|_|) = OptField (|IPAddr|_|)

    // port as a 16-bit number.
    let printPort =
        Bits.ofU16 >> Value.ofBits
 
    let private epIPAddr (ep : System.Net.EndPoint) : IPEndPoint =
        match ep with
        | :? IPEndPoint as ipEP -> ipEP
        | _ -> failwith "not an IP address"

    
    // Design Overview
    //
    // Outgoing bytes or messages are buffered until commit, then handled
    // by a background thread for actual sending. 
    //
    // TCP Listener uses 'Pending()' to accept immediately available connections,
    // eliminating need for a background task just to accept connections. Similarly,
    // we'll use DataAvailable on the TCP Client's NetworkStream to read data without
    // using a background task. This reduces background tasks to writing, which allows
    // for arbitrary pushback.
    //
    // UDP deferred for now (low priority).

    module TCP =

        module Listener =
            type Ref = Value
            type EntryId = int

            type Action = 
                | Create of Ref * (Port option) * (IPAddr option)
                | Accept of Ref * Value 
                | Status of Ref 
                | Info of Ref
                | Close of Ref
                | RefList 
                | RefMove of Ref * Ref

            let (|Action|_|) v =
                match v with
                | Variant "create" (Record ["as"; "port"; "addr"] ([Some vRef; OptPort port; OptIPAddr addr], U)) ->
                    Some <| Create (vRef, port, addr)
                | Variant "accept" (FullRec ["from"; "as"] ([lRef; tcpRef], U)) ->
                    Some <| Accept (lRef, tcpRef)
                | Variant "status" vRef -> Some <| Status vRef
                | Variant "info" vRef -> Some <| Info vRef
                | Variant "close" vRef -> Some <| Close vRef
                | Variant "ref" onRef -> 
                    match onRef with
                    | Variant "list" U -> Some RefList
                    | Variant "move" (FullRec ["from"; "to"] ([srcRef; dstRef], U)) ->
                        Some (RefMove (srcRef, dstRef))
                    | _ -> None
                | _ -> None


            [<Struct>]
            type Ent =
                {
                    LocalPort : Port option
                    LocalAddr : IPAddr option
                    Listener : TcpListener option   // none during init
                    Accepting : FTList<TcpClient>
                    Status : Status
                    Detach : bool
                }

            let initEnt portOpt addrOpt =
                { LocalPort = portOpt
                ; LocalAddr = addrOpt
                ; Listener = None
                ; Accepting = FTList.empty
                ; Status = Init
                ; Detach = false
                }
            
            [<Struct>]
            type State =
                { Bindings : Map<Ref, EntryId>      // program references to runtime objects
                ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
                }

            let initialState () =
                { Bindings = Map.empty
                ; Entries = Map.empty
                }

            let unusedEntId (st:State) : EntryId =
                let rec searchLoop n =
                    if (n < 0) then failwith "listener overflow" else
                    if not (Map.containsKey n (st.Entries)) then n else
                    searchLoop (n + 1)
                searchLoop 1000
                

        type Ref = Value   // program reference to a TCP connection
        type EntryId = int // runtime reference to a TCP connection

        type Action =
            | ListenerOp of Listener.Action
            | Connect of asRef:Ref * dstPort:Port * dstAddr:IPAddr * srcPort:(Port option) * srcAddr:(IPAddr option)
            | Read of Ref * int
            | Write of Ref * byte array
            | Limit of Ref * int
            | Status of Ref
            | Info of Ref
            | Close of Ref
            | RefList 
            | RefMove of Ref * Ref

        [<Struct>]
        type Ent =
            {
                TCP : TcpClient option // none only during init of outgoing connections
                SendBuf : FTList<byte>
                RecvBuf : FTList<byte>  
                SendTask : Task<unit> // one background write at a time.
                // RecvTask : Task<unit> // DataAvailable + synchronous reads, instead.

                Status : Status 
                Detach : bool
            }

        let initEnt tcpOpt =
            {
                TCP = tcpOpt
                SendBuf = FTList.empty
                RecvBuf = FTList.empty
                SendTask = Task<unit>.FromResult ()
                Status = Init
                Detach = false
            }


        [<Struct>]
        type State =
            { Listeners : Listener.State
            ; Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            }

        let private unusedEntId (st:State) : EntryId =
            let rec searchLoop n =
                if (n < 0) then failwith "TCP connection overflow" else
                if not (Map.containsKey n (st.Entries)) then n else
                searchLoop (n + 1)
            searchLoop 9000


        let (|Action|_|) v =
            match v with
            | Variant "listener" v' -> 
                match v' with
                | Listener.Action lOp -> Some <| ListenerOp lOp
                | _ -> None
            | Variant "connect" (Record ["as"; "dst\x00port"; "dst\x00addr"; "src\x00port"; "src\x00addr"] 
                                       ([Some vRef; Some (Port dstPort); Some (IPAddr dstAddr); 
                                                    OptPort srcPort; OptIPAddr srcAddr], U)) ->
                Some <| Connect (vRef, dstPort, dstAddr, srcPort, srcAddr)
            | Variant "read" (FullRec ["from"; "count"] ([vRef; Nat n], U)) when (n <= uint64 System.Int32.MaxValue) ->
                Some <| Read (vRef, int n)
            | Variant "write" (FullRec ["to"; "data"] ([vRef; Binary b], U)) ->
                Some <| Write (vRef, b)
            | Variant "limit" (FullRec ["of"; "cap"] ([vRef; Nat n],U)) when (n <= uint64 System.Int32.MaxValue) ->
                Some <| Limit (vRef, int n)
            | Variant "status" vRef -> Some <| Status vRef
            | Variant "info" vRef -> Some <| Info vRef
            | Variant "close" vRef -> Some <| Close vRef
            | Variant "ref" onRef -> 
                match onRef with
                | Variant "list" U -> Some RefList
                | Variant "move" (FullRec ["from"; "to"] ([srcRef; dstRef], U)) ->
                    Some (RefMove (srcRef, dstRef))
                | _ -> None
            | _ -> None

        let tryUpdate (op:Action) (st:State) : (Value * State) option =
            match op with
            | ListenerOp lOp ->
                match lOp with 
                | Listener.Create (vRef, portOpt, addrOpt) ->
                    match Map.tryFind vRef (st.Listeners.Bindings) with
                    | Some _ -> None // vRef already in use, so this fails. 
                    | None -> // add listener to pool to initialize later.
                        let eid = Listener.unusedEntId (st.Listeners)
                        let bind' = Map.add vRef eid (st.Listeners.Bindings)
                        let ent = Listener.initEnt portOpt addrOpt
                        let ents' = Map.add eid ent (st.Listeners.Entries)
                        let st' = { st with Listeners = { st.Listeners with Entries = ents'; Bindings = bind' } }
                        Some (Value.unit, st')
                | Listener.Close vRef -> 
                    match Map.tryFind vRef (st.Listeners.Bindings) with
                    | None -> None // not open, so cannot close
                    | Some eid ->
                        let bind' = Map.remove vRef (st.Listeners.Bindings)
                        let ent = Map.find eid (st.Listeners.Entries)
                        let ent' = { ent with Detach = true }
                        let ents' = Map.add eid ent' (st.Listeners.Entries)
                        let st' = { st with Listeners = { st.Listeners with Entries = ents'; Bindings = bind' } }
                        Some (Value.unit, st')
                | Listener.Accept (listenerRef, tcpRef) -> 
                    // block binding if TCP ref is in use
                    if Map.containsKey tcpRef (st.Bindings) then None else
                    match Map.tryFind listenerRef (st.Listeners.Bindings) with
                    | None -> None // not listening on this ref
                    | Some lEid -> 
                        let lEnt = Map.find lEid (st.Listeners.Entries)
                        match FTList.tryViewL lEnt.Accepting with
                        | None -> None // no incoming connections available
                        | Some (conn, accepting') ->
                            let lEnt' = { lEnt with Accepting = accepting' }
                            let lEnts' = Map.add lEid lEnt' (st.Listeners.Entries)
                            let tEid = unusedEntId st
                            let tBind' = Map.add tcpRef tEid (st.Bindings)
                            let tEnt = initEnt (Some conn)
                            let tEnts' = Map.add tEid tEnt (st.Entries)
                            let st' = { st with 
                                         Entries = tEnts'
                                         Bindings = tBind'
                                         Listeners = { st.Listeners with Entries = lEnts' } 
                                      }
                            Some (Value.unit, st')



                | Listener.Status vRef -> None
                | Listener.Info vRef -> None
                | Listener.RefList -> None
                | Listener.RefMove (src, dst) -> None
            | Connect (tcpRef, dstPort, dstAddr, srcPortOpt, srcAddrOpt) -> None
            | Read (tcpRef, nAmt) -> None
            | Write (tcpRef, data) -> None
            | Limit (tcpRef, cap) -> None
            | Status (tcpRef) -> None
            | Info (tcpRef) -> None
            | Close (tcpRef) -> None
            | RefList -> None
            | RefMove (src, dst) -> None


        let mainLoop (st : State ref) : unit = lock st (fun () -> 
            while true do 
                ignore <| Monitor.Wait(st, 1000) // 1 Hz if not triggered by program.
                for (entId, ent) in Map.toSeq (st.Value.Listeners.Entries) do
                    // handle listeners 
                    ()
                for (entId, ent) in Map.toSeq (st.Value.Entries) do
                    // handle connections
                    ()
        )

        let initialState () : State = 
            { Listeners = Listener.initialState () 
            ; Bindings = Map.empty
            ; Entries = Map.empty
            }

        let initEff () : Effects.IEffHandler =
            let state = ref (initialState ())
            let parser = (|Action|_|)
            let action = tryUpdate
            let writer = id
            let bgThread () = mainLoop state
            Thread(bgThread).Start()
            Effects.SharedStateEff(state,parser,action,writer) |> Effects.selectHeader "tcp"

    module UDP =
        type Ref = Value
        type EntryId = int

        [<Struct>]
        type Msg =
            { RemoteAddr : IPAddr
            ; RemotePort : Port
            ; Data : byte array
            }

        type Action =
            | Connect of Ref * Port option * IPAddr option 
            | Read of Ref
            | Write of Ref * Msg
            | Status of Ref
            | Info of Ref
            | Close of Ref
            | RefList 
            | RefMove of Ref * Ref

        [<Struct>]
        type Ent =
            {
                LocalAddr : IPAddr option
                LocalPort : Port option
                UDP  : UdpClient option     // none during init
                Send : FTList<Msg>
                Recv : FTList<Msg>

                Status : Status
                Detach : bool
            }

        let initEnt portOpt addrOpt =
            { LocalPort = portOpt
            ; LocalAddr = addrOpt
            ; UDP = None
            ; Send = FTList.empty
            ; Recv = FTList.empty
            ; Status = Init
            ; Detach = false
            }

        [<Struct>]
        type State =
            { Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            }

        let private unusedEntId (st:State) : EntryId =
            let rec searchLoop n =
                if (n < 0) then failwith "UDP connection overflow" else
                if not (Map.containsKey n (st.Entries)) then n else
                searchLoop (n + 1)
            searchLoop 9000

        let (|Message|_|) v = 
            match v with
            | FullRec ["port"; "addr"; "data"] ([Port p; IPAddr a; Binary b], U) ->
                Some <| { RemoteAddr = a; RemotePort = p; Data = b }
            | _ -> None

        let (|Action|_|) v =
            match v with
            | Variant "connect" (Record ["as"; "port"; "addr"] ([Some vRef; OptPort port; OptIPAddr addr],U)) ->
                Some <| Connect (vRef, port, addr)
            | Variant "read" (Record ["from"] ([Some vRef], U)) ->
                Some <| Read vRef
            | Variant "write" (FullRec ["to"; "data"] ([vRef; Message m], U)) ->
                Some <| Write (vRef, m)
            | Variant "status" vRef -> Some <| Status vRef
            | Variant "info" vRef -> Some <| Info vRef
            | Variant "close" vRef -> Some <| Close vRef
            | Variant "ref" onRef -> 
                match onRef with
                | Variant "list" U -> Some RefList
                | Variant "move" (FullRec ["from"; "to"] ([srcRef; dstRef], U)) ->
                    Some (RefMove (srcRef, dstRef))
                | _ -> None
            | _ -> None

        let tryUpdate (op:Action) (st:State) : (Value * State) option =
            match op with
            | Connect (udpRef, portOpt, addrOpt) -> None
            | Read (udpRef) -> None
            | Write (udpRef, msg) -> None
            | Status (udpRef) -> None
            | Info (udpRef) -> None
            | Close (udpRef) -> None
            | RefList -> None
            | RefMove (src, dst) -> None

        let mainLoop (st : State ref) : unit = lock st (fun () -> 
            while true do 
                ignore <| Monitor.Wait(st, 1000) // 1 Hz if not triggered by program.
                for (entId, ent) in Map.toSeq (st.Value.Entries) do
                    // handle UDP connection
                    ()
        )

        let initialState () : State = 
            { Bindings = Map.empty
            ; Entries = Map.empty
            }

        let initEff () : Effects.IEffHandler =
            let state = ref (initialState ())
            let parser = (|Action|_|)
            let action = tryUpdate
            let writer = id
            let bgThread () = mainLoop state
            Thread(bgThread).Start()
            Effects.SharedStateEff(state,parser,action,writer) |> Effects.selectHeader "udp"


    let networkEff () =
        Effects.composeEff (TCP.initEff ()) (UDP.initEff ())
