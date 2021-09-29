namespace Glas

/// This module (will eventually) support running of Glas programs as console
/// applications with access to network, filesystem, and transactional memory.
module Running =

    /// Simple associative memory model. This allows 'addresses' to be arbitrary
    /// values, but they should preferably be short values. The default value for
    /// memory is unit, so Memory may drop associations to unit values.
    type Memory = Map<Value, Value>

    /// Memory Operations cover the API described in GlasApps.md
    type MemOp =
        | Get
        | Put of Value
        | Swap of Value
        | Read of Count:int * Exact:bool * Tail:bool * Peek:bool
        | Write of Data:FTList<Value> * Head:bool
        | Path of On:Bits * Op:MemOp
        | Elem of At:int * Op:MemOp
        | Del // special case, use only within 'Path' or 'Elem'


    let rec applyMemOpToVal op v0 =
        match op with
        | Get -> Some struct(v0, v0)
        | Put v' -> Some struct(v', Value.unit)
        | Swap v' -> Some struct(v', v0)
        | Del -> Some struct(Value.unit, Value.unit)
        | Path (p, Put pv') ->
            let v' = Value.record_insert p pv' v0
            Some struct(v', Value.unit)
        | Path (p, Del) ->
            let v' = Value.record_delete p v0
            Some struct(v', Value.unit)
        | Path (p, op') ->
            match Value.record_lookup p v0 with
            | None -> None
            | Some pv ->
                match applyMemOpToVal op' pv with
                | None -> None
                | Some struct(pv', result) ->
                    let v' = Value.record_insert p pv' v0
                    Some struct(v', result) 
        | Read _ -> None // todo
        | Write _ -> None // todo
        | Elem _ -> None // todo

    let applyMemOp (mem:Memory) (addr:Value) (op:MemOp) : struct(Memory * Value) option =
        let v0 = Option.defaultValue (Value.unit) (Map.tryFind addr mem)
        match applyMemOpToVal op v0 with
        | Some struct(Value.U, ret) -> Some struct(Map.remove addr mem, ret)
        | Some struct(v', ret) -> Some struct(Map.add addr v' mem, ret)
        | None -> None



    let rec tryParseMemOp (v:Value) : MemOp option =
        match v with
        | Value.Variant "get" Value.U -> Get |> Some 
        | Value.Variant "put" v -> Put v |> Some
        | Value.Variant "swap" v -> Swap v |> Some
        | Value.Variant "read" r -> None
        | Value.Variant "write" r -> None
        | Value.Variant "path" r -> None
        | Value.Variant "elem" r -> None
        | _ -> None

        



    /// Transactional Memory
    /// 
    /// This implementation does not support concurrency. Thus, it only has some
    /// conditional backtracking.
    type TransactionalMemory =
        val mutable private Mem : Memory
        val mutable private TXStack : Memory list
