namespace Glas

// TODO Performance Improvements:
//
// .NET DYNAMIC PROGRAMMING
//
// Instead of compiling a program to continuations or an array of steps, it would
// be much more efficient to emit a Dynamic Method or Assembly that the CLR knows
// how to further just-in-time compile for the host machine. 
//
// REUSE OF COMMON SUBPROGRAMS
//
// Glas programs benefit from a 'compression' pass to identify common subprograms
// that can be reused. Benefits include reuse of compiler effort and space savings
// for compiled programs. This might be guided by heuristics and annotations.
//
// REDUCE TRANSACTION OVERHEADS
//
// Conditional behaviors in cond or loop are often pure, observing only the data
// stack. We can avoid calling try/commit/abort on the IEffHandler in these cases.
// If we know ahead of time, we can further limit restore info to the data stack.
//  
// REGISTER MACHINE
//
// Modeling the data stack as a list simplifies conditional backtracking but has
// significant overheads in the form of allocation, garbage collection, and data
// plumbing. I'd like to instead record data into 'registers' or variables that
// can be precisely saved and restored as needed for transactions. 
//
// Relatedly, we can use abstract interpretation or type inference to split static
// data structures into multiple registers. This enables programs to efficiently 
// model a more flexible evaluation context.
//
// However, optimal register allocation is not trivial. So it may be necessary to
// handle this as a dedicated compiler pass, producing a new intermediate program
// representation.
// 
// PARTIAL EVAL OF EFFECTS
//
// Currently I centralize all effects to one IEffHandler.Eff() call. But this 
// hinders partial evaluation and precise transactions. What I'd like to do is
// model the runtime-level effects handler as a special program that uses fine
// grained operators such as 'eff:file:read' instead of a global 'eff'. The 
// handler manages routing of effects and results, in a manner more amenable
// to partial evaluation.
//
// STOWAGE AND MEMOIZATION
//
// Support for stowage and memoization could reduce rework. It should be feasible
// to make stowage into a lazy operation, e.g. by extending data with mutable 
// fields for caching or clearing the value.
//


module ProgEval =
    open Value
    open ProgVal

    // If a runtime is forced to halt early for various reasons, it will
    // raise an exception. Failure is not handled this way, only halt.
    exception RTError of Value
    let lUnderflow = Value.symbol "underflow"
    let lTypeError = Value.symbol "type-error"

    // lighweight continuations for mutable environment
    type K = unit -> unit

    // lightweight wrapper for lazy continuations.
    let ofLazyK (lazyK : Lazy<K>) () = 
        (lazyK.Force()) ()

    module Accel =
        // support functions for acceleration (independent of runtime)

        let rec bits_negate_term t =
            match t with
            | Leaf -> Leaf
            | Stem64(bits, t') ->
                Stem64(~~~bits, bits_negate_term t')
            | _ -> failwith "input is not bits"
        
        let bits_negate_stem n =
            let lb = StemBits.lenbit (StemBits.len n)
            let ndb = (lb ||| (lb - 1UL))
            ((~~~n) &&& (~~~ndb)) ||| (n &&& ndb)

        let bits_negate bits =
            { Stem = bits_negate_stem bits.Stem 
            ; Term = bits_negate_term bits.Term
            }

        let rec bits_reverse_append acc bits =
            if not (isStem bits) then acc else
            let acc' = consStemBit (stemHead bits) acc
            let bits' = stemTail bits
            bits_reverse_append acc' bits'

        let rec bits_len_t acc t =
            match t with
            | Stem64 (_, t') -> bits_len_t (64+acc) t'
            | Leaf -> acc
            | _ -> failwith "input is not bits"

        let bits_len v =
            bits_len_t (StemBits.len (v.Stem)) (v.Term)


        let lAccel = Value.label "accel"
        let lOpt = Value.label "opt"

        let prefix (s:string) =
            let utf8_bytes = System.Text.Encoding.UTF8.GetBytes(s)
            Array.foldBack Value.consStemByte utf8_bytes Value.unit

        [<return: Struct>]
        let inline (|Prefix|_|) s = 
            Value.(|Stem|_|) (prefix s)

    module Interpreter = 

        // Runtime serves as an active evaluation environment for a program.
        // At the moment, runtime is separated from the program interpreter.
        type Runtime =
            val         private EffHandler  : Effects.IEffHandler
            val mutable private DeferTry    : int  // for lazy try/commit/abort
            val mutable private DataStack   : Value list
            val mutable private EnvStack    : Value list
            val mutable private TXStack     : (struct(Value list * Value list)) list
            //val         private Profile     : System.Collections.Generic.Dictionary<Value, Ref<Stats.S>>

            // possibility: we could add a 'pure' TX stack that only holds copies of DataStack.
            // But the current lazy approach is simple and effective.

            new (ds, io) =
                { EffHandler = io
                ; DeferTry = 0
                ; DataStack = ds
                ; EnvStack = []
                ; TXStack = []
                //; Profile = System.Collections.Generic.Dictionary<Value, Ref<Stats.S>>()
                }

            // halt will also unwind effects.
            member rt.Halt(msg : Value) : unit =
                rt.UnwindTX()
                raise (RTError(msg))

            member private rt.UnwindTX() : unit  =
                while(not (List.isEmpty rt.TXStack)) do
                    rt.TXAbort()

            member private rt.ActivateTX() : unit =
                while(rt.DeferTry > 0) do
                    rt.DeferTry <- rt.DeferTry - 1
                    rt.EffHandler.Try()

            member rt.TopLevelEffect() : bool =
                match rt.DataStack with
                | (a::ds') ->
                    rt.ActivateTX() // cannot further defer 'Try()' 
                    match rt.EffHandler.Eff(a) with
                    | ValueSome a' ->
                        rt.DataStack <- (a' :: ds')
                        true
                    | ValueNone -> false
                | _ -> rt.Underflow(); false

            member private rt.Underflow() : unit  =
                rt.Halt(lUnderflow)
            
            member private rt.TypeError() : unit  =
                rt.Halt(lTypeError)

            // common operations
            member rt.Copy() =
                match rt.DataStack with
                | (a::_) ->
                    rt.DataStack <- a :: (rt.DataStack)
                | _ -> rt.Underflow()

            member rt.Drop() =
                match rt.DataStack with
                | (_::ds') ->
                    rt.DataStack <- ds'
                | _ -> rt.Underflow()

            member rt.Swap() =
                match rt.DataStack with
                | (a::b::ds') ->
                    rt.DataStack <- (b::a::ds')
                | _ -> rt.Underflow()

            member rt.EnvPush() =
                match rt.DataStack with
                | (a::ds') ->
                    rt.DataStack <- ds'
                    rt.EnvStack <- a :: (rt.EnvStack)
                | _ -> rt.Underflow()
            
            member rt.EnvPop() =
                match rt.EnvStack with
                | (a::es') ->
                    rt.EnvStack <- es'
                    rt.DataStack <- a :: (rt.DataStack)
                | _ -> failwith "compiler error: imbalanced eff/env"

            member rt.PushData(v : Value) =
                rt.DataStack <- v :: (rt.DataStack)

            member rt.PopData() : Value =
                match rt.DataStack with
                | (a::ds') ->
                    rt.DataStack <- ds'
                    a
                | [] ->
                    rt.Underflow(); Value.unit

            member rt.ViewDataStack() : Value list =
                rt.DataStack

            member rt.TXBegin() =
                // defer 'Try()' as much as feasible.
                rt.DeferTry <- rt.DeferTry + 1
                rt.TXStack <- struct(rt.DataStack, rt.EnvStack) :: rt.TXStack
            
            member rt.TXAbort() =
                match rt.TXStack with
                | struct(dataS,envS)::txS' -> 
                    rt.DataStack <- dataS
                    rt.EnvStack <- envS
                    rt.TXStack <- txS'
                    if (rt.DeferTry > 0) 
                        then rt.DeferTry <- rt.DeferTry - 1
                        else rt.EffHandler.Abort()
                | _ -> failwith "compiler error: imbalanced transaction (abort)"
            
            member rt.TXCommit() =
                match rt.TXStack with
                | (_::txS') ->
                    rt.TXStack <- txS'
                    if(rt.DeferTry > 0)
                        then rt.DeferTry <- rt.DeferTry - 1
                        else rt.EffHandler.Commit()
                | _ -> failwith "compiler error: imbalanced transaction (commit)"

            member rt.EqDrop() : bool =
                match rt.DataStack with
                | (a::b::ds') ->
                    if (a = b) then
                        rt.DataStack <- ds'
                        true
                    else false
                | _ -> rt.Underflow(); false

            member rt.TryGet() : bool =
                match rt.DataStack with
                | ((Bits k)::r::ds') ->
                    match Value.record_lookup k r with
                    | ValueSome v -> 
                        rt.DataStack <- (v :: ds')
                        true
                    | ValueNone -> false
                | (_::_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false
            
            member rt.Put() =
                match rt.DataStack with
                | ((Bits k)::r::v::ds') -> 
                    rt.DataStack <- (Value.record_insert k v r)::ds'
                | (_::_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.Del() =
                match rt.DataStack with
                | ((Bits k)::r::ds') ->
                    rt.DataStack <- (Value.record_delete k r)::ds'
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // 'fail', 'cond', 'loop', and 'prog' are compiler continuation magic.

            // Accelerated operations!
            member rt.AccelBitsNegate() =
                match rt.DataStack with
                | ((Bits b)::ds') ->
                    rt.DataStack <- (Accel.bits_negate b)::ds'
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow() 

            member rt.AccelBitsReverseAppend() =
                match rt.DataStack with
                | ((Bits b)::(Bits acc)::ds') ->
                    rt.DataStack <- (Accel.bits_reverse_append acc b)::ds'
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelBitsVerify() : bool =
                match rt.DataStack with
                | (v::ds') -> (Value.isBits v)
                | _ -> rt.Underflow(); false
            
            // true - ok; false - inputs or result are not small nats
            member rt.AccelSmallNatAdd() : bool =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') when ((System.UInt64.MaxValue - n) >= m) ->
                    rt.DataStack <- (Value.ofNat (m+n))::ds'
                    true
                | (_::_::_) -> false
                | _ -> rt.Underflow(); false

            // three cases here:
            //   ValueSome true - successful subtraction
            //   ValueSome false - failed; result would be negative 
            //   ValueNone - inputs are not small nats
            member rt.AccelSmallNatSub() : bool voption =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m >= n) then
                        rt.DataStack <- (Value.ofNat (m - n))::ds'
                        ValueSome true
                    else
                        ValueSome false
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            // true - success; false - inputs or result are not small nats
            member rt.AccelSmallNatMul() : bool =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    try
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) n m
                        rt.DataStack <- (Value.ofNat prod)::ds'
                        true
                    with 
                    | :? System.OverflowException -> false
                | (_::_::_) -> false
                | _ -> rt.Underflow(); false

            //   vsome true - success
            //   vsome false - div by zero
            //   vnone - inputs are not small nats
            member rt.AccelSmallNatDivMod() : bool voption =
                match rt.DataStack with
                | ((Nat64 divisor)::(Nat64 dividend)::ds') ->
                    if(0UL = divisor) then ValueSome false else
                    // (note) just leaving div-by-zero behavior to original source
                    let struct(quot,rem) = System.Math.DivRem(dividend,divisor)
                    rt.DataStack <- (Value.ofNat rem)::(Value.ofNat quot)::ds'
                    ValueSome true
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            // vnone - inputs are not small nats; vsome (result) otherwise 
            member rt.AccelSmallNatGT() : bool voption =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    ValueSome (m > n)
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            // vnone - inputs are not small nats; vsome (result) otherwise 
            member rt.AccelSmallNatGTE() : bool voption =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    ValueSome (m >= n)
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            member rt.AccelSmallIntIncrement() : bool =
                match rt.DataStack with
                | ((Int64 n)::ds') when (n < System.Int64.MaxValue) ->
                    rt.DataStack <- (Value.ofInt (n + 1L))::ds'
                    true
                | (_::_) -> false
                | _ -> rt.Underflow(); false
            
            member rt.AccelSmallIntDecrement() : bool =
                match rt.DataStack with
                | ((Int64 n)::ds') when (n > System.Int64.MinValue) ->
                    rt.DataStack <- (Value.ofInt (n - 1L))::ds'
                    true
                | (_::_) -> false
                | _ -> rt.Underflow(); false

            // true - ok; false - argument or results aren't small ints
            member rt.AccelSmallIntAdd() : bool =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try 
                        let sum = Microsoft.FSharp.Core.Operators.Checked.(+) m n
                        rt.DataStack <- (Value.ofInt sum)::ds'
                        true
                    with
                    | :? System.OverflowException -> false
                | (_::_::_) -> false
                | _ -> rt.Underflow(); false

            member rt.AccelSmallIntMul() : bool =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) m n
                        rt.DataStack <- (Value.ofInt prod)::ds'
                        true
                    with 
                    | :? System.OverflowException -> false
                | (_::_::_) -> false
                | _ -> rt.Underflow(); false

            member rt.AccelSmallIntGT() : bool voption =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m > n) 
                        then ValueSome true 
                        else ValueSome false
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            member rt.AccelSmallIntGTE() : bool voption =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m >= n) 
                        then ValueSome true 
                        else ValueSome false
                | (_::_::_) -> ValueNone
                | _ -> rt.Underflow(); ValueNone

            member rt.AccelListVerify() : bool =
                match rt.DataStack with
                | ((List t)::ds') -> 
                    rt.DataStack <- (Value.ofTerm t)::ds'
                    true
                | (_::_) -> false
                | _ -> rt.Underflow(); false
            
            member rt.AccelListLength() =
                match rt.DataStack with
                | ((List t)::ds') ->
                    let nLen = Value.Rope.len t
                    rt.DataStack <- (Value.ofNat nLen)::ds'
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelListAppend() =
                match rt.DataStack with
                | ((List r)::(List l)::ds') ->
                    let result = Rope.append l r
                    rt.DataStack <- (Value.ofTerm result)::ds'
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // true - ok; false - index too large
            member rt.AccelListTake() : bool =
                match rt.DataStack with
                | ((Nat64 n)::(List t)::ds') ->
                    if (n > (Rope.len t)) then false else
                    let result = Rope.take n t
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    true
                | (_::_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false

            // true - ok; false - index too large
            // assumes we'll never have lists larger than 2**64 - 1 items
            member rt.AccelListSkip() : bool =
                match rt.DataStack with
                | ((Nat64 n)::(List t)::ds') ->
                    if (n > (Rope.len t)) then false else
                    let result = Rope.drop n t
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    true
                | (_::_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false

            // true - ok; false - index too large
            // assumes we'll never have lists larger than 2**64 - 1 items
            member rt.AccelListItem() : bool =
                match rt.DataStack with
                | ((Nat64 ix)::(List t)::ds') ->
                    if (ix >= (Rope.len t)) then false else
                    let result = Rope.item ix t
                    rt.DataStack <- result :: ds'
                    true
                | (_::_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false

            member rt.AccelListPushl() =
                match rt.DataStack with
                | ((List l)::v::ds') ->
                    let result = Rope.cons v l
                    rt.DataStack <- (Value.ofTerm result)::ds'
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelListPushr() =
                match rt.DataStack with
                | (v::(List l)::ds') ->
                    let result = Rope.snoc l v 
                    rt.DataStack <- (Value.ofTerm result)::ds'
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()
            
            member rt.AccelListPopl() : bool =
                match rt.DataStack with
                | ((List l)::ds') ->
                    match l with
                    | Rope.ViewL(struct(v, l')) ->
                        rt.DataStack <- ((Value.ofTerm l') :: v :: ds')
                        true
                    | Leaf -> false
                    | _ -> rt.TypeError(); false
                | (_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false
            
            member rt.AccelListPopr() : bool =
                match rt.DataStack with
                | ((List l)::ds') ->
                    match l with
                    | Rope.ViewR(struct(l', v)) ->
                        rt.DataStack <- (v :: (Value.ofTerm l') :: ds')
                        true
                    | Leaf -> false
                    | _ -> rt.TypeError(); false
                | (_::_) -> rt.TypeError(); false
                | _ -> rt.Underflow(); false


        let rec rtErrMsg (rt : Runtime) (msg : Value) =
            let ds = rt.ViewDataStack() |> Value.ofList
            Value.variant "rte" <| Value.asRecord ["data";"event"] [ds; msg]


        // A relatively simple evaluator to get started.
        // - returns true/false for success/failure. 
        // - provides stack of env handlers directly.
        let rec interpret (rt : Runtime) (env : Program list) (p0 : Program) : bool =
            // order matters in this case, due to interpreter trying each case.
            match p0 with
            | PSeq (List lP) -> 
                interpretSeq rt env lP
            | Data v -> rt.PushData(v); true
            | Cond (c, a, b) -> 
                rt.TXBegin()
                let bCond = interpret rt env c
                if bCond then
                    rt.TXCommit()
                    interpret rt env a
                else
                    rt.TXAbort()
                    interpret rt env b
            | Dip p ->
                let v = rt.PopData()
                let bOK = interpret rt env p
                rt.PushData(v)
                bOK 
            | Stem lCopy U -> rt.Copy(); true
            | Stem lSwap U -> rt.Swap(); true
            | Stem lDrop U -> rt.Drop(); true
            | Stem lEq U -> rt.EqDrop()
            | Stem lGet U -> rt.TryGet()
            | Stem lPut U -> rt.Put(); true
            | Stem lDel U -> rt.Del(); true
            | Prog (anno, p) ->
                // Acceleration is supported, but not profiling.
                match Value.record_lookup (Accel.lAccel) anno with
                | ValueSome vAccel ->
                    match vAccel with
                    | Stem (Accel.lOpt) vAccel' ->
                        interpretAccel rt env true vAccel' p
                    | _ ->
                        interpretAccel rt env false vAccel p
                | ValueNone -> interpret rt env p
            | While (c, a) -> 
                let mutable bOK = true
                rt.TXBegin()
                while(bOK && (interpret rt env c)) do
                    rt.TXCommit()
                    bOK <- interpret rt env a
                    rt.TXBegin()
                rt.TXAbort()
                bOK
            | Until (c, a) ->
                let mutable bOK = true
                rt.TXBegin()
                while(bOK && not (interpret rt env c)) do
                    rt.TXAbort()
                    bOK <- interpret rt env a
                    rt.TXBegin()
                rt.TXCommit()
                bOK
            | Stem lEff U -> 
                match env with
                | (h::env') ->
                    rt.EnvPop()
                    let bOK = interpret rt env' h
                    rt.EnvPush()
                    bOK
                | [] ->
                    rt.TopLevelEffect()
            | Env (w, p) -> 
                rt.EnvPush()
                let bOK = interpret rt (w::env) p
                rt.EnvPop()
                bOK
            | Stem lFail U ->
                false 
            | Stem lHalt eMsg -> 
                rt.Halt(eMsg) 
                false
            | _ -> rt.Halt(lTypeError); false
        and interpretSeq rt env l =
            match l with
            | Rope.ViewL(op, l') ->
                let bOK = interpret rt env op
                if not bOK then false else
                interpretSeq rt env l'
            | Leaf -> true
            | _ -> rt.Halt(lTypeError); false
        and interpretAccel rt env bOpt vModel p =
            let inline accelFail () =
                if bOpt then interpret rt env p else
                rt.Halt(Value.variant "accel" vModel); false
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    rt.AccelListPushl(); true
                | Value.Variant "pushr" U ->
                    rt.AccelListPushr(); true
                | Value.Variant "popl" U ->
                    rt.AccelListPopl()
                | Value.Variant "popr" U -> 
                    rt.AccelListPopr()
                | Value.Variant "append" U -> 
                    rt.AccelListAppend(); true
                | Value.Variant "verify" U -> 
                    rt.AccelListVerify()
                | Value.Variant "length" U -> 
                    rt.AccelListLength(); true
                | Value.Variant "take" U -> 
                    rt.AccelListTake()
                | Value.Variant "skip" U -> 
                    rt.AccelListSkip()
                | Value.Variant "item" U -> 
                    rt.AccelListItem()
                | _ -> accelFail ()
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    rt.AccelBitsVerify()
                | Value.Variant "negate" U -> 
                    rt.AccelBitsNegate(); true
                | Value.Variant "reverse-append" U -> 
                    rt.AccelBitsReverseAppend(); true
                //| Value.Variant "length" U -> accelFail ()
                | _ -> accelFail ()
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    // might fail for large integers or results
                    match rt.AccelSmallIntAdd() with
                    | true -> true
                    | false -> interpret rt env p
                | Value.Variant "mul" U ->
                    match rt.AccelSmallIntMul() with
                    | true -> true
                    | false -> interpret rt env p
                //| Value.Variant "sub" U -> accelFail ()
                //| Value.Variant "divmod" U -> accelFail ()
                | Value.Variant "increment" U -> 
                    match rt.AccelSmallIntIncrement() with
                    | true -> true
                    | false -> interpret rt env p
                | Value.Variant "decrement" U -> 
                    match rt.AccelSmallIntDecrement() with
                    | true -> true
                    | false -> interpret rt env p
                | Value.Variant "gt" U -> 
                    match rt.AccelSmallIntGT() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p
                | Value.Variant "gte" U -> 
                    match rt.AccelSmallIntGTE() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p
                | _ -> accelFail ()
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    match rt.AccelSmallNatAdd() with
                    | true -> true
                    | false -> interpret rt env p
                | Value.Variant "sub" U -> 
                    match rt.AccelSmallNatSub() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p
                | Value.Variant "mul" U -> 
                    match rt.AccelSmallNatMul() with
                    | true -> true
                    | false -> interpret rt env p
                | Value.Variant "divmod" U ->
                    match rt.AccelSmallNatDivMod() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p 
                | Value.Variant "gt" U -> 
                    match rt.AccelSmallNatGT() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p 
                | Value.Variant "gte" U -> 
                    match rt.AccelSmallNatGTE() with
                    | ValueSome b -> b
                    | ValueNone -> interpret rt env p 
                | _ -> accelFail ()
            | _ -> accelFail ()

        let eval p io args =
            let rt = new Runtime(args, io)
            try 
                let bSuccess = interpret rt [] p 
                if not bSuccess then None else 
                Some (rt.ViewDataStack())
            with
            | RTError(eMsg) -> 
                Effects.logErrorV io "runtime error" (rtErrMsg rt eMsg)
                None

    // todo: reimplement the finally tagless interpreter?
    // or jump straight to .NET dynamic assemblies and methods?

    module FinallyTaglessInterpreter =
        // variation on Interpreter that compiles everything into
        // continuations to avoid re-parsing the program or directly
        // observing results at runtime.

        // status: works, faster than basic interpreter in many tests

        type Runtime = Interpreter.Runtime
        type Cont = Runtime -> obj
        type Profile = System.Collections.Generic.Dictionary<Value, Ref<Stats.S>>

        let lProf = Value.label "prof"

        [<Struct>]
        type CTE =
            { OnOK : Cont
            ; OnFail : Cont
            ; OnEff : CTE -> Cont
            ; Prof : Profile
            }

        let getProfReg cte vChan =
            match cte.Prof.TryGetValue(vChan) with
            | true, reg -> reg
            | false, _ ->
                let reg = ref (Stats.s0)
                cte.Prof.Add(vChan, reg)
                reg

        // utility
        let inline bracket (prep : Cont -> Cont) (mkOp : CTE -> Cont) (wrapOK : Cont -> Cont) (wrapFail : Cont -> Cont) (cte : CTE) : Cont =
            prep (mkOp { cte with OnOK = wrapOK (cte.OnOK); OnFail = wrapFail (cte.OnFail) })

        let rec compile (p0:Program) (cte : CTE) : Cont = 
            match p0 with
            | PSeq (List lP) ->
                let compileStep p onOK = compile p { cte with OnOK = onOK }
                Rope.foldBack compileStep lP (cte.OnOK)
            | Data v -> 
                fun rt -> rt.PushData(v); cte.OnOK rt
            | Cond (c, a, b) -> 
                let lazyB = lazy(compile b cte) 
                let lazyA = lazy(compile a cte)
                let onFail (k : Cont) (rt : Runtime) = rt.TXAbort(); lazyB.Force() rt
                let onOK (k : Cont) (rt : Runtime) = rt.TXCommit(); lazyA.Force() rt
                let preCond (k : Cont) (rt : Runtime) = rt.TXBegin(); k rt
                bracket preCond (compile c) onOK onFail cte
            | Dip p ->
                let reg = ref Value.unit
                let onExit (k : Cont) (rt : Runtime) =
                    rt.PushData(reg.Value)
                    reg.Value <- Value.unit
                    k rt
                let onEnter (k : Cont) (rt : Runtime) =
                    reg.Value <- rt.PopData()
                    k rt
                bracket onEnter (compile p) onExit onExit cte
            | Stem lCopy U -> 
                fun rt -> rt.Copy(); cte.OnOK rt
            | Stem lSwap U -> 
                fun rt -> rt.Swap(); cte.OnOK rt
            | Stem lDrop U -> 
                fun rt -> rt.Drop(); cte.OnOK rt
            | Stem lEq U -> 
                fun rt ->
                    if rt.EqDrop() 
                        then cte.OnOK rt
                        else cte.OnFail rt
            | Stem lGet U -> 
                fun rt ->
                    if rt.TryGet()
                        then cte.OnOK rt
                        else cte.OnFail rt
            | Stem lPut U ->
                fun rt -> rt.Put(); cte.OnOK rt
            | Stem lDel U -> 
                fun rt -> rt.Del(); cte.OnOK rt
            | Prog (anno, p) ->
                compileAnno anno p cte
            | While (c, a) -> 
                let loRef = ref (cte.OnOK) 
                let runLoop rt = loRef.Value rt
                let lazyBody = lazy(compile a { cte with OnOK = runLoop })
                let onCondOK (rt : Runtime) = rt.TXCommit(); lazyBody.Force() rt
                let onCondFail (rt : Runtime) = rt.TXAbort(); cte.OnOK rt
                let tryCond = compile c { cte with OnOK = onCondOK; OnFail = onCondFail }
                loRef.Value <- fun rt -> rt.TXBegin(); tryCond rt
                runLoop
            | Until (c, a) ->
                let loRef = ref (cte.OnOK) 
                let runLoop rt = loRef.Value rt
                let lazyBody = lazy(compile a { cte with OnOK = runLoop })
                let onCondOK (rt : Runtime) = rt.TXCommit(); cte.OnOK rt
                let onCondFail (rt : Runtime) = rt.TXAbort(); lazyBody.Force() rt
                let tryCond = compile c { cte with OnOK = onCondOK; OnFail = onCondFail }
                loRef.Value <- fun rt -> rt.TXBegin(); tryCond rt
                runLoop
            | Stem lEff U -> 
                cte.OnEff cte
            | Env (w, p) -> 
                // to avoid recompiling 'w' per 'eff' operation, will use
                // intermeidate register to track the current continuation.
                let reg = ref cte // placeholder
                let dynCTE = 
                    { cte with 
                        OnOK = fun rt -> rt.EnvPush(); reg.Value.OnOK rt
                        OnFail = fun rt -> rt.EnvPush(); reg.Value.OnFail rt 
                    }
                let opHandler = compile w dynCTE
                let runHandler (cte' : CTE) (rt : Runtime) =
                    reg.Value <- cte'
                    rt.EnvPop()
                    opHandler rt
                let progCTE =
                    { cte with
                        OnOK = fun rt -> rt.EnvPop(); cte.OnOK rt
                        OnFail = fun rt -> rt.EnvPop(); cte.OnFail rt
                        OnEff = runHandler
                    }
                let op = compile p progCTE
                fun rt -> rt.EnvPush(); op rt
            | Stem lFail U ->
                cte.OnFail
            | Stem lHalt eMsg -> 
                fun rt ->
                    rt.Halt(eMsg)
                    cte.OnFail rt                    
            | _ -> 
                fun rt ->
                    rt.Halt(lTypeError)
                    cte.OnFail rt
        and compileAnno (anno : Value) (p : Program) (cte : CTE) : Cont =
            // not well factored at the moment due to profiler and
            // continuations being passed together. 
            match anno with
            | Record ["prof"] struct([ValueSome profOptions],anno') ->
                let vChan = Value.record_lookup (Value.label "chan") profOptions
                          |> ValueOption.defaultValue (Value.unit)
                let reg = getProfReg cte vChan // shared register
                let sw = new System.Diagnostics.Stopwatch() // per profiled location
                let onExit k rt =
                    sw.Stop()
                    reg.Value <- Stats.add (reg.Value) (sw.Elapsed.TotalSeconds)
                    k rt
                let onEnter k rt =
                    sw.Restart()
                    k rt
                bracket onEnter (compileAnno anno' p) onExit onExit cte
            | Record ["stow"] struct([ValueSome vOpts], anno') ->
                // nop for now
                compileAnno anno' p cte
            | Record ["memo"] struct([ValueSome memoOpts], anno') ->
                // nop for now
                compileAnno anno' p cte
            | Record ["accel"] struct([ValueSome vModel], anno') ->
                let opNoAccel = compileAnno anno' p
                match vModel with
                | Variant "opt" vModel' ->
                    compileAccel true  vModel' opNoAccel cte
                | _ ->
                    compileAccel false vModel  opNoAccel cte
            | _ -> // ignoring other annotations 
                compile p cte
        and compileAccel (bOpt : bool) (vModel : Value) (opNoAccel : CTE -> Cont) (cte : CTE) : Cont =
            let lazyOp = lazy(opNoAccel cte)
            let inline accelFail rt =
                if bOpt then lazyOp.Force() rt else 
                rt.Halt(Value.variant "accel" vModel)
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    fun rt -> rt.AccelListPushl(); cte.OnOK rt
                | Value.Variant "pushr" U ->
                    fun rt -> rt.AccelListPushr(); cte.OnOK rt
                | Value.Variant "popl" U ->
                    fun rt -> 
                        if rt.AccelListPopl()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "popr" U -> 
                    fun rt ->
                        if rt.AccelListPopr()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "append" U -> 
                    fun rt -> rt.AccelListAppend(); cte.OnOK rt
                | Value.Variant "verify" U -> 
                    fun rt ->
                        if rt.AccelListVerify() 
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "length" U -> 
                    fun rt -> rt.AccelListLength(); cte.OnOK rt
                | Value.Variant "take" U -> 
                    fun rt ->
                        if rt.AccelListTake()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "skip" U -> 
                    fun rt ->
                        if rt.AccelListSkip()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "item" U -> 
                    fun rt ->
                        if rt.AccelListItem()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | _ -> accelFail 
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    fun rt ->
                        if rt.AccelBitsVerify()
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "negate" U -> 
                    fun rt -> rt.AccelBitsNegate(); cte.OnOK rt
                | Value.Variant "reverse-append" U -> 
                    fun rt -> rt.AccelBitsReverseAppend(); cte.OnOK rt
                // other good options: length, or, and, xor
                | _ -> accelFail
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    fun rt ->
                        match rt.AccelSmallIntAdd() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                | Value.Variant "mul" U ->
                    fun rt ->
                        match rt.AccelSmallIntMul() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                //| Value.Variant "sub" U -> accelFail
                //| Value.Variant "divmod" U -> accelFail 
                | Value.Variant "increment" U -> 
                    fun rt ->
                        match rt.AccelSmallIntIncrement() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                | Value.Variant "decrement" U -> 
                    fun rt ->
                        match rt.AccelSmallIntDecrement() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                | Value.Variant "gt" U -> 
                    fun rt ->
                        match rt.AccelSmallIntGT() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | Value.Variant "gte" U -> 
                    fun rt ->
                        match rt.AccelSmallIntGTE() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | _ -> accelFail
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    fun rt ->
                        match rt.AccelSmallNatAdd() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                | Value.Variant "sub" U -> 
                    fun rt ->
                        match rt.AccelSmallNatSub() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | Value.Variant "mul" U -> 
                    fun rt ->
                        match rt.AccelSmallNatMul() with
                        | true -> cte.OnOK rt
                        | false -> lazyOp.Force() rt
                | Value.Variant "divmod" U ->
                    fun rt ->
                        match rt.AccelSmallNatDivMod() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | Value.Variant "gt" U -> 
                    fun rt ->
                        match rt.AccelSmallNatGT() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | Value.Variant "gte" U -> 
                    fun rt ->
                        match rt.AccelSmallNatGTE() with
                        | ValueSome true -> cte.OnOK rt
                        | ValueSome false -> cte.OnFail rt
                        | ValueNone -> lazyOp.Force() rt
                | _ -> accelFail
            | _ -> accelFail

        let statsMsg (s : Stats.S) : Value =
            if(0UL = s.Cnt) then Value.unit else
            let nCount = Value.ofNat (s.Cnt)
            let vUnits = Value.symbol "nsec" // indicate NT time ticks.
            let inline toUnit f = // convert to nanoseconds
                Value.ofInt (int64(ceil(1000000000.0 * f)))
            let nMax = toUnit (s.Max)
            if(1UL = s.Cnt) then
                Value.asRecord 
                    ["count"; "val"; "units"] 
                    [nCount ; nMax ; vUnits ]
            else 
                let nSDev = toUnit (Stats.sdev s)
                let nMin = toUnit (s.Min)
                let nAvg = toUnit (Stats.average s)
                Value.asRecord 
                    ["count"; "avg"; "min"; "max"; "sdev"; "units"]
                    [nCount ; nAvg ; nMin ; nMax ; nSDev ; vUnits ]

        let logProfile (io:Effects.IEffHandler) (prof:Profile) =
            let vProf = Value.symbol "prof"
            for k in prof.Keys do
                let s = prof[k].Value
                if (s.Cnt > 0UL) then
                    let vStats = statsMsg s
                    //printfn "chan: %s, stats: %s (from %A)" (Value.prettyPrint k) (Value.prettyPrint vStats) (s)
                    let vMsg =
                        Value.asRecord
                            ["lv" ; "chan"; "stats"]
                            [vProf;    k  ; vStats ]
                    Effects.log io vMsg

        let eval (p : Program) (io : Effects.IEffHandler) : Value list -> Value list option =
            let onOK (rt : Runtime) = 
                box (Some (rt.ViewDataStack()))
            let onFail (rt : Runtime) = 
                box None
            let onEff (cte : CTE) (rt : Runtime) =
                match rt.TopLevelEffect() with
                | true -> cte.OnOK rt
                | false -> cte.OnFail rt
            let cte0 = 
                { OnOK = onOK
                ; OnFail = onFail
                ; OnEff = onEff 
                ; Prof = new Profile()
                }
            let lazyOp = // compile stuff once only
                lazy(compile p cte0)
            fun ds ->
                let rt = new Runtime(ds, io)
                let result = 
                    try 
                        unbox<Value list option> <| lazyOp.Force() rt
                    with
                    | RTError(eMsg) -> 
                        let v = Interpreter.rtErrMsg rt eMsg
                        Effects.logErrorV io "runtime error" v
                        None
                logProfile io (cte0.Prof)
                result


    /// The value list argument is top of stack at head of list.
    /// Returns ValueNone if the program either halts or fails. 
    let eval (p:Program) (io:Effects.IEffHandler) : (Value list) -> (Value list option) =
        FinallyTaglessInterpreter.eval p io

(*        
        let ccEvalOK rte = 
            assert((List.isEmpty rte.DipStack)
                && (List.isEmpty rte.EffStateStack)
                && (Option.isNone rte.FailureStack)) 
            box (Some (rte.DS))
        let ccEvalFail rte = box None
        let cte = { FK = ccEvalFail; EH = ioEff wrappedIO; TX = io; Prof = new ProfChans() }
        let runLazy = lazy ((compile p) cte ccEvalOK)
        fun ds ->
            try 
                let result = ds |> dataStack |> (runLazy.Force()) |> unbox<Value list option>
                logProfile (io) (cte.Prof) 
                result
            with
            | RTError(rte,msg) ->
                io.UnwindTXStack()
                let v = rtErrMsg rte msg
                logErrorV io "runtime error" (rtErrMsg rte msg)
                logProfile io (cte.Prof)
                None
*)
    module private Pure = 
        // For 'pure' functions, we'll also halt on first *attempt* to use effects.
        exception ForbiddenEffectException of Value
        let forbidEffects = {   
            new Effects.IEffHandler with
                member __.Eff v = 
                    raise <| ForbiddenEffectException(v)
            interface Effects.ITransactional with
                member __.Try () = ()
                member __.Commit () = ()
                member __.Abort () = ()
        }

    let pureEval (p : Program) : (Value list) -> (Value list option) =
        let evalNoEff = eval p Pure.forbidEffects
        fun args -> 
            try evalNoEff args
            with 
            | Pure.ForbiddenEffectException _ -> None

// Considering reimplementation based on performance goals.
//
// A program becomes multi-stage, closer to:
//
//   CTE -> (CTEFB, CCX -> (CCXFB, RTE -> RTE))
//   CTE - compile-time environment. Might not do much, but useful for
//      caching, profiling, certain debug aspects.
//   CTEFB - compile-time environment static feedback
//   CCX - caller or client context 
//      continuations, effects handlers, partial values, static failure, etc.
//   CCXFB - caller or client context feedback
//   RTE - final runtime environment 
//
// Ideally, we shift most runtime environment manipulations into operating
// on an array of registers. So we might want an explicit stage for register
// allocation and so on.
