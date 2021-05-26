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

    /// Print program to value. 
    ///
    /// This has unbounded recursion, which is not ideal. 
    let rec print (p : Program) : Value =
        match p with
        | Op(op) -> symbol (opStr op)
        | Dip(p) -> variant "dip" (print p)
        | Data(v) -> variant "data" v
        | Seq(ps) -> 
            let pps = List.fold (fun l p -> FTList.snoc l (print p)) (FTList.empty) ps
            variant "seq" (ofFTList pps)
        | Cond (Try=c; Then=a; Else=b) ->
            let ks = ["try"; "then"; "else"]
            let vs = List.map print [c;a;b]
            variant "seq" <| asRecord ks vs
        | Loop (Try=c; Then=a) ->
            let ks = ["try";"then"]
            let vs = List.map print [c;a]
            variant "loop" <| asRecord ks vs
        | Env (Do=p; Eff=e) ->
            let ks = ["do"; "eff"]
            let vs = List.map print [p;e]
            variant "env" <| asRecord ks vs
        | Prog (Do=p; Note=v) ->
            // prog:(do:P, ... Annotations ...)
            let r = record_insert (label "do") (print p) v
            variant "prog" r
        | Note v -> variant "note" v


    /// Parse from value.
    //let parse (v:Value) : Program option =


    // program validation of value
    // parsing program from value
    // printing program to value
    // static arity analysis.
    // initial program interpreter.
    // eq and hash on programs

