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
type Program =
    | Dip of Program
    | Data of Value
    | Seq of Program list
    | Cond of Try:Program * Then:Program * Else:Program
    | Loop of Try:Program * Then:Program
    | Env of Do:Program * Eff:Program
    | Prog of Do:Program * Note:Value 
    | Note of Value
    | Op of SymOp

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
        // for tail-recursive print, I defunctionalize the continuation.
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
                pR (V(k,"prog")) v ["do"] [p]
            | Note v -> appK k (variant "note" v)




    /// Print program to value. 
    let print (p : Program) : Value =
        Printer.print (Printer.K.Done) p

    /// Parse from value.
    //let parse (v:Value) : Program option =


    // static arity analysis.
    // initial interpreter or compiler.

