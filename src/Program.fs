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
/// maintenance of structure sharing. Within Glas, we'd resolve via
/// stow and memo annotations. For F#, I have an ad-hoc partial model
/// for this.
type Program =
    | Op of SymOp
    | Dip of Program
    | Data of Value
    | Seq of Program list
    | Cond of Try:Program * Then:Program * Else:Program
    | Loop of While:Program * Do:Program
    | Env of Do:Program * With:Program
    | Prog of Do:Program * Note:Value 

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

    type StackArity =
        | Static of int * int
        | Failure // arity of subprogram that always Fails
        | Dynamic // arity of inconsistent subprogram

    let op_arity (op : SymOp) : StackArity =
        match op with 
        | Copy -> Static(1,2)
        | Drop -> Static(1,0)
        | Swap -> Static(2,2)
        | Eq -> Static(2,2)
        | Fail -> Failure
        | Eff -> Static(1,1) // assumes handler is 2-2.
        | Get -> Static(2,1)
        | Put -> Static(3,1)
        | Del -> Static(2,1)
        | Pushl -> Static(2,1)
        | Popl -> Static(1,2)
        | Pushr -> Static(2,1)
        | Popr -> Static(1,2)
        | Join -> Static(2,1)
        | Split -> Static(1,2)
        | Len -> Static(1,1)
        | BJoin -> Static(2,1)
        | BSplit -> Static(1,2)
        | BLen -> Static(1,1)
        | BNeg -> Static(1,1)
        | BMax -> Static(2,1)
        | BMin -> Static(2,1)
        | BEq -> Static(2,1)
        | Add -> Static(2,2)
        | Mul -> Static(2,2)
        | Sub -> Static(2,1)
        | Div -> Static(2,2)


    let rec stack_arity (p : Program) : StackArity =
        match p with
        | Op (op) -> op_arity op
        | Dip p ->
            match stack_arity p with
            | Static (a,b) -> Static (a+1, b+1)
            | Failure -> Failure
            | Dynamic -> Dynamic
        | Data _ -> Static(0,1)
        | Seq ps -> stack_arity_seq ps
        | Cond (Try=c; Then=a; Else=b) ->
            let l = stack_arity_seq [c;a]
            let r = stack_arity b
            match l,r with
            | Static (li,lo), Static(ri,ro) when ((li - lo) = (ri - ro)) ->
                if (li > ri) then l else r
            | Failure, Static (ri, ro) -> 
                match stack_arity c with
                | Failure -> r
                | Static (ci, _) ->
                    let d = (max ci ri) - ri
                    Static (ri + d, ro + d)
                | Dynamic -> Dynamic
            | _, Failure -> l
            | _, _ -> Dynamic
        | Loop (While=c; Do=a) ->
            // seq:[c,a] must be stack invariant.
            match stack_arity_seq [c;a] with
            | Static (i,o) when (i = o) -> Static(i,o)
            | Failure -> 
                match stack_arity c with
                | Failure -> Static(0,0)
                | _ -> Dynamic
            | _ -> Dynamic
        | Env (Do=p; With=e) -> 
            // constraining bootstrap eff handlers to be 2-2 including state.
            // i.e. forall S . ((S * Request) * St) -> ((S * Response) * St)
            match stack_arity e with
            | Static(i,o) when ((i = o) && (2 >= i)) -> 
                stack_arity (Dip p)
            | _ -> Dynamic
        | Prog (Do=p) -> stack_arity p
    and stack_arity_seq ps =
        _stack_arity_seq 0 0 ps
    and private _stack_arity_seq i o ps =
        match ps with
        | [] -> Static(i,o)
        | (p::ps') ->
            match stack_arity p with
            | Static (a,b) -> 
                let d = max 0 (a - o) 
                let i' = i + d
                let o' = o + d + (b - a) 
                _stack_arity_seq i' o' ps'
            | ar -> ar


    /// Compute static stack arity, i.e. number of stack inputs and outputs,
    /// if this value can be computed. This requires effect handlers have a
    /// static arity of 2--2 including the handler state (1--1 from 'Eff' call).
    ///
    /// Ignores annotations.
    ///
    /// Currently does not compute arity for programs that contain 'fail'. This
    /// may need to be corrected later, but shouldn't be critical pre-bootstrap.
    let static_arity (p : Program) : struct(int * int) option =
        match stack_arity p with
        | Static (a,b) -> Some struct(a,b) 
        | _ -> None

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
            | Variant "prog" (FullRec ["do"] (vs, vNote)) when isRecord vNote ->
                Some struct(vs, fun ps -> Prog (Do = ps.[0], Note = vNote))
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


    /// A lightweight, direct-style interpreter for the Glas program model.
    /// Perhaps useful as a reference, and for getting started. However, this
    /// is really awkward for incremental computing, stack traces, etc.. 
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
            use tx = withTX (e.IO) // backtrack effects in pTry
            match interpret pTry e with
            | Some e' -> tx.Commit(); interpret pThen e'
            | None -> tx.Abort(); interpret pElse e
        and loop pWhile pDo e = 
            use tx = withTX (e.IO) // backtrack effects in pWhile
            match interpret pWhile e with
            | Some e' -> 
                tx.Commit(); 
                match interpret pDo e' with
                | Some ef -> loop pWhile pDo ef
                | None -> None // failure in main loop body is elevated.
            | None -> tx.Abort(); Some e  // failure in loop condition ends loop.
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


    (* incomplete, and I might defer this effort to post-bootstrap.
    
    /// A continuation-passing free-monadic interpreter is more flexible 
    /// for which sorts of effects we can express. Uses defunctionalized 
    /// continuation, which is also convenient for debugging purposes.
    /// 
    /// Note: this is currently incomplete. I've 
    module CPI =

        /// Our continuation, at any given step. This does not include the
        /// data stack or effect handler stacks. 
        type CC = 
            | Halt                              // program is done
            | Run of Program * CC               // evaluate a program, then continue
            | PushEnv of Program * CC           // to restore effects stack
            | PopEnv of CC                      // to escape effects stack
            | SeqCC of CC * CC                  // for flexible composition

        /// We'll interpret the program until we hit a stopping point. 
        /// Effects stack will be captured in continuations, and the
        /// data stack is returned separately.
        type Yield =
            | Done                  // program exited successfully
            | Fail of CC            // stopped on failure. Remaining program is listed.
            | Eff of CC             // stopped for external effects
            | Try of CC * CC * CC   // begin a hierarchical transaction.

        type EffStack = (struct(Value * Program)) list
        type DataStack = Value list


        //let interpret (cc:CC) : 
        
    *)
