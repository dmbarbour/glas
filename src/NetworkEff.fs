
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
            
            [<Struct>]
            type State =
                { Bindings : Map<Ref, EntryId>      // program references to runtime objects
                ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
                }


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


        [<Struct>]
        type State =
            { Listeners : Listener.State
            ; Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            }

        let (|Action|_|) v =
            match v with
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



    module UDP =
        type Ref = Value
        type EntryId = int

        [<Struct>]
        type Msg =
            { RemoteAddr : IPAddr
            ; RemotePort : Port
            ; Data : FTList<byte>
            }

        type Action =
            | Connect of Ref * Port option * IPAddr option 
            | Read of Ref
            | Write of Ref * Msg
            | Status of Ref
            | Info of Ref
            | Close of Ref
            | RefList of Ref
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

        [<Struct>]
        type State =
            { Bindings : Map<Ref, EntryId>      // program references to runtime objects
            ; Entries  : Map<EntryId, Ent>      // sparse table of runtime objects
            }
