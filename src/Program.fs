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
/// maintenance of structure sharing. Perhaps I should instead model
/// programs as an abstract type? But this is adequate for bootstrap. 
type Program =
    | Op of SymOp
    | Dip of Program
    | Data of Value
    | Seq of Program list
    | Cond of Try:Program * Then:Program * Else:Program
    | Loop of Try:Program * Then:Program
    | Env of Do:Program * Eff:Program
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

    module private Printer =
        // we might try to improve printer with a cache 
        // for structure sharing.

        // for tail-recursive print, model a continuation.
        type K =
            | Done
            | V of K * string
            | R of K * Value * string * List<string> * List<Program>
            | S of K * FTList<Value> * List<Program>

        let rec appK (k:K) (v:Value) : Value =
            match k with
            | Done -> v
            | V (k', s) -> appK k' (variant s v)
            | R (k', r, s, ls, lp) -> 
                let r' = record_insert (label s) v r
                pR k' r' ls lp
            | S (k', vs, lp) ->
                let vs' = FTList.snoc vs v
                pS k' vs' lp
        and pR k r ls lp =
            match ls, lp with
            | [], [] -> appK k r
            | (s::ls'), (p::lp') -> print (R(k, r, s, ls', lp')) p
            | _ -> failwith "size mismatch for record print"
        and pS k vs lp =
            match lp with
            | (p::lp') -> print (S(k,vs,lp')) p
            | [] -> appK k (ofFTList vs)
        and print (k:K) (p:Program) : Value =
            match p with
            | Op(op) -> appK k (symbol (opStr op))
            | Dip(p) -> print (V(k,"dip")) p
            | Data(v) -> appK k (variant "data" v)
            | Seq(ps) -> pS (V(k,"seq")) (FTList.empty) ps
            | Cond (Try=c; Then=a; Else=b) ->
                pR (V(k,"cond")) unit ["try"; "then"; "else"] [c; a; b]
            | Loop (Try=c; Then=a) ->
                pR (V(k,"loop")) unit ["try"; "then"] [c; a]
            | Env (Do=p; Eff=e) ->
                pR (V(k,"env")) unit ["do"; "eff"] [p; e]
            | Prog (Do=p; Note=v) ->
                // the 'do' here is added into 'v'
                pR (V(k,"prog")) v ["do"] [p]
            | Note v -> appK k (variant "note" v)

    /// Print program to value. 
    let print (p : Program) : Value =
        Printer.print (Printer.K.Done) p


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
    /// if this value can be computed. This requires effect handlers are 1-1.
    /// Ignores annotations. 
    ///
    /// Currently not tail-recursive. This shouldn't be a problem for most programs.
    /// TODO: consider including a reason when arity fails. Exception?
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
        | Loop (Try=c; Then=a) ->
            // seq:[c,a] must be stack invariant.
            let s = static_seq_arity [c;a]
            match s with
            | Some struct (i,o) when (i = o) -> s
            | _ -> None
        | Env (Do=p; Eff=e) -> 
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

    // should a parser attempt to preserve common substructure?
    // at least for now, it seems low priority to do so.

    let private _opMap =
        let ins m op = Map.add (label (opStr op)) op m
        List.fold ins (Map.empty) op_list

    /// Parse SymOp from value.
    let parseOp (v:Value) : SymOp option =
        if not (isBits v) then None else
        Map.tryFind (v.Stem) _opMap


    /// Parse from value.
    // let parse (v:Value) : Program option =
    //     match v with
    //     | Variant 


    // initial interpreter or compiler.

