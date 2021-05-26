namespace Glas

/// The Glas standard program model.
///
/// Glas systems can support more than one program model, but we 
/// need a base case for defining language modules and bootstrap.
/// It is also suitable for transaction machine applications.
type Program =
    // stack operators
    | Copy
    | Drop
    | Dip of Program
    | Swap
    | Data of Value
    // control operators
    | Seq of Program list
    | Cond of Try:Program * Then:Program * Else:Program
    | Loop of Try:Program * Then:Program
    | Eq
    | Fail
    // algebraic effects
    | Env of Do:Program * Eff:Program
    | Eff
    // record operations
    | Get | Put | Del
    // list operators
    | Pushl | Popl | Pushr | Popr | Join | Split | Len
    // bitstring operators
    | BJoin | BSplit | BLen | BNeg | BMax | BMin | BEq
    // arithmetic operators
    | Add | Mul | Sub | Div
    // annotation operators
    | Prog of Anno:Value * Do:Program
    | Note of Anno:Value

// module Program