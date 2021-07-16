namespace Glas

/// all program operators that consist of a single symbol
type SymOp =
    | Copy | Drop | Swap
    | Eq | Fail
    | Eff
    | Get | Put | Del
    | Pushl | Popl | Pushr | Popr | Join | Split | Len
    | BJoin | BSplit | BLen | BNeg | BMax | BMin | BEq
    | Add | Mul | Sub | Div

/// The Glas standard program model.
///
/// Glas systems can support more than one program model, but we 
/// need a base case for defining language modules and bootstrap.
/// It is also suitable for transaction machine applications.
/// 
/// Note: Converting between F# and Glas program values complicates
/// maintenance of structure sharing. To resolve this, I currently
/// use a memo-cache for interning, but it isn't optimal due to lack
/// of  
type Program =
    | Op of SymOp
    | Dip of Program
    | Data of Value
    | Seq of Program list
    | Cond of Try:Program * Then:Program * Else:Program
    | Loop of While:Program * Do:Program
    | Env of Do:Program * With:Program
    | Prog of Do:Program * Note:Value 
    | Note of Value

module Program =
    open Value

    let nop = Seq []

    let op_list = [ Copy; Drop; Swap
                  ; Eq; Fail
                  ; Eff
                  ; Get; Put; Del
                  ; Pushl; Popl; Pushr; Popr; Join; Split; Len
                  ; BJoin; BSplit; BLen; BNeg; BMax; BMin; BEq
                  ; Add; Mul; Sub; Div]

    let opStr (op : SymOp) : string =
        match op with
        | Copy -> "copy"
        | Drop -> "drop"
        | Swap -> "swap"
        | Eq -> "eq"
        | Fail -> "fail"
        | Eff -> "eff"
        | Get -> "get"
        | Put -> "put"
        | Del -> "del"
        | Pushl -> "pushl"
        | Popl -> "popl"
        | Pushr -> "pushr"
        | Popr -> "popr"
        | Join -> "join"
        | Split -> "split"
        | Len -> "len"
        | BJoin -> "bjoin"
        | BSplit -> "bsplit"
        | BLen -> "blen"
        | BNeg -> "bneg"
        | BMax -> "bmax"
        | BMin -> "bmin"
        | BEq -> "beq"
        | Add -> "add"
        | Mul -> "mul"
        | Sub -> "sub"
        | Div -> "div"

    module Printer =
        // continuation for a printer
        type K = Value list -> Value

        // for round-trip structure sharing, intern common subtrees. 
        type C = Map<Program,Value> 

        // to separate op handling from caching and recursion details...
        let split (p : Program) : struct(Program list * K) =
            match p with
            | Op op -> struct([], fun _ -> symbol (opStr op))
            | Dip pDip -> struct([pDip], asRecord ["dip"])
            | Data v -> struct([], fun _ -> variant "data" v)
            | Seq ps -> struct(ps, FTList.ofList >> Value.ofFTList >> variant "seq")
            | Cond (Try=pTry; Then=pThen; Else=pElse) ->
                struct([pTry; pThen; pElse], asRecord ["try"; "then"; "else"] >> variant "cond")
            | Loop (While=pWhile; Do=pDo) ->
                struct([pWhile; pDo], asRecord ["while"; "do"] >> variant "loop")
            | Env (Do=pDo; With=pWith) ->
                struct([pDo; pWith], asRecord ["do"; "with"] >> variant "env")
            | Prog (Do=pDo; Note=vNote) ->
                struct([pDo], fun vs -> variant "prog" (record_insert (label "do") (vs.[0]) vNote))
            | Note vNote -> struct([], fun _ -> variant "note" vNote)

        // printer handles recursion and caching at the moment.
        // currently not tail-recursive.
        let rec print (c0:C) (p:Program) : (Value * C) =
            match Map.tryFind p c0 with
            | Some v -> (v, c0)
            | None ->
                let struct(ps, k) = split p
                let (vs, c') = List.mapFold print c0 ps
                let v = k vs
                (v, Map.add p v c')

    /// Print program to value. 
    let print (p : Program) : Value =
        let (v, _) = Printer.print (Map.empty) p
        v

    let opArity (op : SymOp) : struct(int * int) =
        match op with 
        | Copy -> struct(1,2)
        | Drop -> struct(1,0)
        | Swap -> struct(2,2)
        | Eq -> struct(2,2)
        | Fail -> struct(0,0)
        | Eff -> struct(1,1) // assumes handler is 2-2.
        | Get -> struct(2,1)
        | Put -> struct(3,1)
        | Del -> struct(2,1)
        | Pushl -> struct(2,1)
        | Popl -> struct(1,2)
        | Pushr -> struct(2,1)
        | Popr -> struct(1,2)
        | Join -> struct(2,1)
        | Split -> struct(1,2)
        | Len -> struct(1,1)
        | BJoin -> struct(2,1)
        | BSplit -> struct(1,2)
        | BLen -> struct(1,1)
        | BNeg -> struct(1,1)
        | BMax -> struct(2,1)
        | BMin -> struct(2,1)
        | BEq -> struct(2,1)
        | Add -> struct(2,2)
        | Mul -> struct(2,2)
        | Sub -> struct(2,1)
        | Div -> struct(2,2)

    /// Compute static stack arity, i.e. number of stack inputs and outputs,
    /// if this value can be computed. This requires effect handlers have a
    /// static arity of 2--2 including the handler state (1--1 from 'Eff' call).
    ///
    /// Ignores annotations.
    ///
    /// TODO: consider error message when arity fails.
    let rec static_arity (p : Program) : struct(int * int) option =
        match p with
        | Op (op) -> Some (opArity op)
        | Dip p ->
            match static_arity p with
            | Some struct(a,b) -> Some struct(a+1, b+1)
            | None -> None
        | Data _ -> Some struct(0,1)
        | Seq ps -> static_seq_arity ps
        | Cond (Try=c; Then=a; Else=b) ->
            // seq:[c,a] and b must have same stack balance. 
            let l = static_seq_arity [c;a]
            let r = static_arity b
            match l,r with
            | Some struct(li, lo), Some struct(ri, ro) when ((li - lo) = (ri - ro)) ->
                if (li > ri) then l else r
            | _, _ -> None
        | Loop (While=c; Do=a) ->
            // seq:[c,a] must be stack invariant.
            let s = static_seq_arity [c;a]
            match s with
            | Some struct (i,o) when (i = o) -> s
            | _ -> None
        | Env (Do=p; With=e) -> 
            // constraining bootstrap eff handlers to be 2-2 including state.
            // i.e. forall S . ((S * Request) * St) -> ((S * Response) * St)
            match static_arity e with
            | Some struct(i,o) when ((i = o) && (2 >= i)) -> 
                static_arity (Dip p)
            | _ -> None
        | Prog (Do=p) -> static_arity p
        | Note _ -> Some struct(0,0)
    and static_seq_arity ps =
        _static_seq_arity 0 0 ps
    and private _static_seq_arity i o ps =
        match ps with
        | [] -> Some struct(i,o)
        | (p::ps') ->
            match static_arity p with
            | None -> None
            | Some struct(a,b) ->
                let d = max 0 (a - o) 
                let i' = i + d
                let o' = o + d + (b - a) 
                _static_seq_arity i' o' ps'

    module Parser =
        // memo-cache to support structure sharing
        type C = Map<Value, Program>

        // when all components parse, build composite.
        type K = Program list -> Program

        let private _opMap =
            let ins m op = Map.add (label (opStr op)) op m
            List.fold ins (Map.empty) op_list

        /// Parse SymOp from value.
        let (|ParseOp|_|) v = 
            match v with 
            | Bits b -> Map.tryFind b _opMap
            | _ -> None

        // single-step pattern maching; returns list of component vals to parse
        let parseShallow (v : Value) : (Value list * K) option =
            match v with
            | ParseOp op -> Some ([], fun _ -> Op op)
            | Variant "dip" vDip -> Some ([vDip], List.head >> Dip)
            | Variant "data" vData -> Some ([], fun _ -> Data vData)
            | Variant "seq" (FTList vs) -> Some (FTList.toList vs, Seq)
            | Variant "cond" (Record ["try";"then";"else"] ([Some vTry; Some vThen; Some vElse], U)) -> 
                Some ([vTry; vThen; vElse], fun ps -> Cond (Try=ps.[0], Then=ps.[1], Else=ps.[2]))
            | Variant "loop" (Record ["while"; "do"] ([Some vWhile; Some vDo], U)) ->
                Some ([vWhile; vDo], fun ps -> Loop (While=ps.[0], Do=ps.[1]))
            | Variant "env" (Record ["do"; "with"] ([Some vDo; Some vWith], U)) ->
                Some ([vDo; vWith], fun ps -> Env (Do=ps.[0], With=ps.[1]))
            | Variant "prog" (Record ["do"] ([Some vDo], vNote)) ->
                Some ([vDo], fun ps -> Prog (Do = ps.[0], Note = vNote))
            | Variant "note" vNote -> Some ([], fun _ -> Note vNote)
            | _ -> None // failed parse

        let rec parse (c0:C) (v:Value) : ((Program option) * C) =
            let fromCache = Map.tryFind v c0
            if Option.isSome fromCache then (fromCache, c0) else
            match parseShallow v with
            | None -> (None, c0) 
            | Some (vs, k) -> 
                let (psOpts, c') = List.mapFold parse c0 vs
                if List.exists Option.isNone psOpts then (None, c') else
                let pParsed = k (List.map Option.get psOpts)
                (Some pParsed, Map.add v pParsed c')

    let tryParse (v : Value) : Program option =
        let (pParsed, _) = Parser.parse (Map.empty) v
        pParsed

    let inline (|Program|_|) v = 
        tryParse v

    // TODO: simple interpreter
