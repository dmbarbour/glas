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

        /// Returns a list of component values to parse into programs, and a
        /// continuation function to combine the component programs. Separates
        /// parse logic from cache and recursion logic.
        let parseShallow (v : Value) : (struct(Value list * K)) option =
            match v with
            | ParseOp op -> Some struct([], fun _ -> Op op)
            | Variant "dip" vDip -> Some struct([vDip], List.head >> Dip)
            | Variant "data" vData -> Some struct([], fun _ -> Data vData)
            | Variant "seq" (FTList vs) -> Some struct(FTList.toList vs, Seq)
            | Variant "cond" (FullRec ["try";"then";"else"] (vs, U)) -> 
                Some struct(vs, fun ps -> Cond (Try=ps.[0], Then=ps.[1], Else=ps.[2]))
            | Variant "loop" (FullRec ["while"; "do"] (vs, U)) ->
                Some struct(vs, fun ps -> Loop (While=ps.[0], Do=ps.[1]))
            | Variant "env" (FullRec ["do"; "with"] (vs, U)) ->
                Some struct(vs, fun ps -> Env (Do=ps.[0], With=ps.[1]))
            | Variant "prog" (FullRec ["do"] (vs, vNote)) ->
                Some struct(vs, fun ps -> Prog (Do = ps.[0], Note = vNote))
            | Variant "note" vNote -> Some struct([], fun _ -> Note vNote)
            | _ -> None // failed parse

        /// A simple parse function with cache support. 
        /// (Non-struct output type to work with List.mapFold)
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

        // TODO: Write parser function for diagnosis of parse errors.
        // This is relatively low priority compared to similar for g0.

    /// Attempt to parse a value into a program.
    let tryParse (v : Value) : Program option =
        let (pParsed, _) = Parser.parse (Map.empty) v
        pParsed

    /// Use program parser with F# pattern matching.
    let inline (|Program|_|) v = 
        tryParse v


    /// A lightweight interpreter for the Glas program model. Trades performance
    /// for simplicity of the implementation. Not recommended for long-term use,
    /// but suitable as a reference or bootstrap implementation.
    /// 
    /// This interpreter does not assume the program is arity safe or type safe.
    module Interpreter =
        open Effects

        /// The interpreter's runtime environment. 
        [<Struct>]
        type RTE =
            { DS : Value list                       //< data stack
            ; ES : struct(Value * Program) list     //< env/eff stack
            ; IO : IEffHandler                      //< top-level effect
            }

        let inline copy e = 
            match e.DS with
            | x::_ -> Some { e with DS = x::(e.DS) }
            | _ -> None

        let inline drop e =
            match e.DS with
            | _::ds -> Some { e with DS = ds }
            | _ -> None

        let inline swap e = 
            match e.DS with
            | x::y::ds -> Some { e with DS = y::x::ds }
            | _ -> None

        let inline eq e = 
            match e.DS with 
            | x::y::_ when (x = y) -> Some e
            | _ -> None

        let inline get e = 
            match e.DS with
            | ((Bits k)::r::ds) ->
                match record_lookup k r with
                | Some v -> Some { e with DS = (v::ds) } 
                | None -> None
            | _ -> None

        let inline put e = 
            match e.DS with
            | ((Bits k)::v::r::ds) ->
                let r' = record_insert k v r
                Some { e with DS = (r'::ds) }
            | _ -> None

        let inline del e = 
            match e.DS with
            | ((Bits k)::r::ds) ->
                let r' = record_delete k r
                Some { e with DS = (r'::ds) }
            | _ -> None

        let inline pushl e = 
            match e.DS with
            | (v::(FTList l)::ds) ->
                let l' = FTList.cons v l
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline popl e = 
            match e.DS with
            | (FTList (FTList.ViewL (v,l')))::ds ->
                Some { e with DS = (v::(ofFTList l')::ds) }
            | _ -> None

        let inline pushr e = 
            match e.DS with
            | (v::(FTList l)::ds) -> 
                let l' = FTList.snoc l v
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline popr e = 
            match e.DS with
            | ((FTList (FTList.ViewR (l',v)))::ds) ->
                Some { e with DS = (v::(ofFTList l')::ds) }
            | _ -> None

        let inline join e = 
            match e.DS with
            | ((FTList l2)::(FTList l1)::ds) ->
                let l' = FTList.append l1 l2
                Some { e with DS = ((ofFTList l')::ds) }
            | _ -> None

        let inline split e =
            match e.DS with
            | ((Nat n)::(FTList l)::ds) when (FTList.length l >= n) ->
                let (l1,l2) = FTList.splitAt n l
                Some { e with DS = ((ofFTList l2)::(ofFTList l1)::ds) }
            | _ -> None
            
        let inline len e = 
            match e.DS with
            | ((FTList l)::ds) ->
                let len = nat (FTList.length l)
                Some { e with DS = (len::ds) }
            | _ -> None

        let inline bjoin e = 
            match e.DS with
            | ((Bits b)::(Bits a)::ds) ->
                let ab = Bits.append a b
                Some { e with DS = ((ofBits ab)::ds) }
            | _ -> None

        let inline bsplit e = 
            match e.DS with
            | ((Nat n)::(Bits ab)::ds) when (uint64 (Bits.length ab) >= n) ->
                let (a,b) = Bits.splitAt (int n) ab
                Some { e with DS = ((ofBits b)::(ofBits a)::ds) }
            | _ -> None

        let inline blen e = 
            match e.DS with
            | ((Bits b)::ds) -> 
                let len = nat (uint64 (Bits.length b))
                Some { e with DS = (len::ds) }
            | _ -> None

        let inline bneg e = 
            match e.DS with
            | ((Bits b)::ds) ->
                let b' = Bits.bneg b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline bmax e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.bmax a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline bmin e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.bmin a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline beq e = 
            match e.DS with
            | ((Bits a)::(Bits b)::ds) when (Bits.length a = Bits.length b) ->
                let b' = Bits.beq a b
                Some { e with DS = ((ofBits b')::ds) }
            | _ -> None

        let inline add e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                let struct(sum,carry) = Arithmetic.add n1 n2
                Some { e with DS = ((ofBits carry)::(ofBits sum)::ds) }
            | _ -> None

        let inline mul e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                let struct(prod,overflow) = Arithmetic.mul n1 n2
                Some { e with DS = ((ofBits overflow)::(ofBits prod)::ds) }
            | _ -> None

        let inline sub e =
            match e.DS with
            | ((Bits n2)::(Bits n1)::ds) ->
                match Arithmetic.sub n1 n2 with
                | Some diff -> Some { e with DS = ((ofBits diff)::ds) }
                | None -> None
            | _ -> None

        let inline div e = 
            match e.DS with
            | ((Bits divisor)::(Bits dividend)::ds) ->
                match Arithmetic.div dividend divisor with
                | Some struct(quotient,remainder) ->
                    Some { e with DS = ((ofBits remainder)::(ofBits quotient)::ds) }
                | None -> None
            | _ -> None

        let inline data v e = 
            Some { e with DS = (v::e.DS) }

        let rec eff e =
            match e.ES with
            | struct(v,p)::es ->
                let ep = { e with DS = (v::e.DS); ES = es; IO = e.IO }
                match interpret p ep with 
                | Some { DS = (v'::ds'); ES = es'; IO = io' } ->
                    Some { DS = ds'; ES = struct(v',p)::es'; IO = io' }
                | _ -> None
            | [] -> 
                match e.DS with
                | request::ds ->
                    match e.IO.Eff request with
                    | Some response ->
                        Some { e with DS = (response::ds) }
                    | None -> None
                | [] -> None
        and interpretOp (op:SymOp) (e:RTE) : RTE option =
            match op with
            | Copy -> copy e
            | Drop -> drop e 
            | Swap -> swap e
            | Eq -> eq e
            | Fail -> None
            | Eff -> eff e 
            | Get -> get e
            | Put -> put e 
            | Del -> del e
            | Pushl -> pushl e
            | Popl -> popl e
            | Pushr -> pushr e
            | Popr -> popr e
            | Join -> join e
            | Split -> split e
            | Len -> len e 
            | BJoin -> bjoin e
            | BSplit -> bsplit e
            | BLen -> blen e 
            | BNeg -> bneg e 
            | BMax -> bmax e
            | BMin -> bmin e
            | BEq -> beq e
            | Add -> add e
            | Mul -> mul e
            | Sub -> sub e 
            | Div -> div e 
        and dip p e = 
            match e.DS with
            | (x::ds) -> 
                match interpret p { e with DS = ds } with
                | Some e' -> Some { e' with DS = (x::e'.DS) } 
                | None -> None
            | [] -> None
        and seq s e =
            match s with
            | (p :: s') ->
                match interpret p e with
                | Some e' -> seq s' e'
                | None -> None
            | [] -> Some e
        and cond pTry pThen pElse e =
            use tx = withTX (e.IO)
            match interpret pTry e with
            | Some e' -> tx.Commit(); interpret pThen e'
            | None -> tx.Abort(); interpret pElse e
        and loop pWhile pDo e = 
            use tx = withTX (e.IO)
            match interpret pWhile e with
            | Some e' -> 
                tx.Commit(); 
                match interpret pDo e' with
                | Some ef -> loop pWhile pDo ef
                | None -> None
            | None -> tx.Abort(); Some e 
        and env pWith pDo e = 
            match e.DS with
            | (v::ds) ->
                let eWith = { DS = ds; ES = struct(v,pWith)::e.ES; IO = e.IO }
                match interpret pDo eWith with
                | Some { DS = ds'; ES= struct(v',_)::es'; IO=io' } ->
                    Some { DS = (v'::ds'); ES=es'; IO=io' }
                | _ -> None
            | [] -> None
        and interpret (p:Program) (e:RTE) : RTE option =
            match p with
            | Op op -> interpretOp op e 
            | Dip p' -> dip p' e 
            | Data v -> data v e
            | Seq s -> seq s e
            | Cond (Try=pTry; Then=pThen; Else=pElse) -> cond pTry pThen pElse e
            | Loop (While=pWhile; Do=pDo) -> loop pWhile pDo e 
            | Env (With=pWith; Do=pDo) -> env pWith pDo e 
            | Prog (Do=p'; Note=_) -> interpret p' e 
            | Note _ -> Some e


    // TODO: a compiler, of finally tagless interpreter that JIT can optimize easily.
    //  ideally, also should eliminate runtime data plumbing, e.g. alloc refs instead.
