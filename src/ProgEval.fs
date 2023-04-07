namespace Glas

// TODO Performance Improvements:
//
// .NET DYNAMIC PROGRAMMING
//
// Instead of compiling a program to continuations or an array of steps, it would
// be much more efficient to emit a Dynamic Method or Class that the CLR knows
// how to further just-in-time compile for the host machine. This is likely the
// single most profitable performance improvement I can easily apply. 
//
// Thoughts: I might favor dynamic Class to simplify code reuse, i.e. modeling each
// reusable subprogram as a different method within a single class. 
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

    module Profiler = 
        // separating index from runtime data.
        type ProfIndex = System.Collections.Generic.Dictionary<Value, int>
        type ProfData = Stats.S array

        let initProfile (profIndex : ProfIndex) : ProfData =
            Array.create (profIndex.Count) (Stats.s0) 

        let getProfReg (profIndex : ProfIndex) (vChan : Value) : int =
            match profIndex.TryGetValue(vChan) with
            | true, ix -> ix
            | false, _ ->
                let ix = profIndex.Count
                profIndex.Add(vChan, ix)
                ix

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

        let logProfile (io:Effects.IEffHandler) (profIndex : ProfIndex) (profData : ProfData) : unit =
            let vProf = Value.symbol "prof"
            for kvp in profIndex do
                let k = kvp.Key
                let s = profData[kvp.Value]
                if (s.Cnt > 0UL) then
                    let vStats = statsMsg s
                    //printfn "chan: %s, stats: %s (from %A)" (Value.prettyPrint k) (Value.prettyPrint vStats) (s)
                    let vMsg =
                        Value.asRecord
                            ["lv" ; "chan"; "stats"]
                            [vProf;    k  ; vStats ]
                    Effects.log io vMsg

    module RT1 = 
        // NOTE: F# does a lot of 'magic' with unit values that leads
        // to inconsistent calls in context of System.Reflection.Emit.
        //
        // To mitigate this, I'm switching to always having a return
        // for ops that I want to call using System.Reflection.Emit.

        type Status = int32
        let opOK = 0            // operation succeeded in an expected way
        let opFail = 1          // operation failed in an expected way
        let accelFail = 2       // acceleration failed due to range/domain limits
        let (|OpOK|OpFail|AccelFail|) n =
            match n with
            | 0 -> OpOK
            | 1 -> OpFail
            | 2 -> AccelFail
            | _ -> failwithf "unexpected Runtime status %d" n
        let inline isOK n = (n = opOK)

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
            member rt.Halt(msg : Value) : Status =
                rt.UnwindTX()
                raise (RTError(msg))

            member private rt.UnwindTX() : unit  =
                while(not (List.isEmpty rt.TXStack)) do
                    rt.TXAbort() |> ignore

            member private rt.ActivateTX() : unit =
                while(rt.DeferTry > 0) do
                    rt.DeferTry <- rt.DeferTry - 1
                    rt.EffHandler.Try()

            member rt.TopLevelEffect() : Status =
                match rt.DataStack with
                | (a::ds') ->
                    rt.ActivateTX() // cannot further defer 'Try()' 
                    match rt.EffHandler.Eff(a) with
                    | ValueSome a' ->
                        rt.DataStack <- (a' :: ds')
                        opOK
                    | ValueNone -> opFail
                | _ -> rt.Underflow() 

            member private rt.Underflow() : Status  =
                rt.Halt(lUnderflow)
            
            member private rt.TypeError() : Status  =
                rt.Halt(lTypeError)

            // common operations
            member rt.Copy() : Status =
                match rt.DataStack with
                | (a::_) ->
                    rt.DataStack <- a :: (rt.DataStack)
                    opOK
                | _ -> rt.Underflow()

            member rt.Drop() : Status =
                match rt.DataStack with
                | (_::ds') ->
                    rt.DataStack <- ds'
                    opOK
                | _ -> rt.Underflow()

            member rt.Swap() : Status =
                match rt.DataStack with
                | (a::b::ds') ->
                    rt.DataStack <- (b::a::ds')
                    opOK
                | _ -> rt.Underflow()

            member rt.EnvPush() : Status =
                match rt.DataStack with
                | (a::ds') ->
                    rt.DataStack <- ds'
                    rt.EnvStack <- a :: (rt.EnvStack)
                    opOK
                | _ -> rt.Underflow()
            
            member rt.EnvPop() : Status =
                match rt.EnvStack with
                | (a::es') ->
                    rt.EnvStack <- es'
                    rt.DataStack <- a :: (rt.DataStack)
                    opOK
                | _ -> 
                    failwith "compiler error: imbalanced eff/env"

            member rt.PushData(v : Value) =
                rt.DataStack <- v :: (rt.DataStack)

            member rt.PopData() : Value =
                match rt.DataStack with
                | (a::ds') ->
                    rt.DataStack <- ds'
                    a
                | [] ->
                    rt.Underflow() |> ignore
                    Value.unit

            member rt.ViewDataStack() : Value list =
                rt.DataStack

            member rt.TXBegin() : Status =
                // defer 'Try()' as much as feasible.
                rt.DeferTry <- rt.DeferTry + 1
                rt.TXStack <- struct(rt.DataStack, rt.EnvStack) :: rt.TXStack
                opOK
            
            member rt.TXAbort() : Status =
                match rt.TXStack with
                | struct(dataS,envS)::txS' -> 
                    rt.DataStack <- dataS
                    rt.EnvStack <- envS
                    rt.TXStack <- txS'
                    if (rt.DeferTry > 0) 
                        then rt.DeferTry <- rt.DeferTry - 1
                        else rt.EffHandler.Abort()
                    opOK
                | _ -> 
                    failwith "compiler error: imbalanced transaction (abort)"
            
            member rt.TXCommit() : Status =
                match rt.TXStack with
                | (_::txS') ->
                    rt.TXStack <- txS'
                    if(rt.DeferTry > 0)
                        then rt.DeferTry <- rt.DeferTry - 1
                        else rt.EffHandler.Commit()
                    opOK
                | _ -> failwith "compiler error: imbalanced transaction (commit)"

            member rt.EqDrop() : Status =
                match rt.DataStack with
                | (a::b::ds') ->
                    if (a = b) then
                        rt.DataStack <- ds'
                        opOK
                    else opFail
                | _ -> rt.Underflow()

            member rt.TryGet() : Status =
                match rt.DataStack with
                | ((Bits k)::r::ds') ->
                    match Value.record_lookup k r with
                    | ValueSome v -> 
                        rt.DataStack <- (v :: ds')
                        opOK
                    | ValueNone -> opFail
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()
            
            member rt.Put() : Status =
                match rt.DataStack with
                | ((Bits k)::r::v::ds') -> 
                    rt.DataStack <- (Value.record_insert k v r)::ds'
                    opOK
                | (_::_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.Del() : Status =
                match rt.DataStack with
                | ((Bits k)::r::ds') ->
                    rt.DataStack <- (Value.record_delete k r)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // 'fail', 'cond', 'loop', and 'prog' are compiler continuation magic.

            // Accelerated operations!
            member rt.AccelBitsNegate() : Status =
                match rt.DataStack with
                | ((Bits b)::ds') ->
                    rt.DataStack <- (Accel.bits_negate b)::ds'
                    opOK
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow() 

            member rt.AccelBitsReverseAppend() : Status =
                match rt.DataStack with
                | ((Bits b)::(Bits acc)::ds') ->
                    rt.DataStack <- (Accel.bits_reverse_append acc b)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelBitsVerify() : Status =
                match rt.DataStack with
                | (v::ds') -> 
                    if (Value.isBits v) 
                        then opOK 
                        else opFail
                | _ -> rt.Underflow()
            
            member rt.AccelSmallNatAdd() : Status =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') when ((System.UInt64.MaxValue - n) >= m) ->
                    rt.DataStack <- (Value.ofNat (m+n))::ds'
                    opOK
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallNatSub() : Status =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m >= n) then
                        rt.DataStack <- (Value.ofNat (m - n))::ds'
                        opOK
                    else
                        opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallNatMul() : Status =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    try
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) n m
                        rt.DataStack <- (Value.ofNat prod)::ds'
                        opOK
                    with 
                    | :? System.OverflowException -> accelFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallNatDivMod() : Status =
                match rt.DataStack with
                | ((Nat64 divisor)::(Nat64 dividend)::ds') ->
                    if(0UL = divisor) then opFail else
                    // (note) just leaving div-by-zero behavior to original source
                    let struct(quot,rem) = System.Math.DivRem(dividend,divisor)
                    rt.DataStack <- (Value.ofNat rem)::(Value.ofNat quot)::ds'
                    opOK
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            // vnone - inputs are not small nats; vsome (result) otherwise 
            member rt.AccelSmallNatGT() : Status =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m > n)
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            // vnone - inputs are not small nats; vsome (result) otherwise 
            member rt.AccelSmallNatGTE() : Status  =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    if (m >= n)
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallIntIncrement() : Status  =
                match rt.DataStack with
                | ((Int64 n)::ds') when (n < System.Int64.MaxValue) ->
                    rt.DataStack <- (Value.ofInt (n + 1L))::ds'
                    opOK
                | (_::_) -> accelFail
                | _ -> rt.Underflow()
            
            member rt.AccelSmallIntDecrement() : Status  =
                match rt.DataStack with
                | ((Int64 n)::ds') when (n > System.Int64.MinValue) ->
                    rt.DataStack <- (Value.ofInt (n - 1L))::ds'
                    opOK
                | (_::_) -> accelFail
                | _ -> rt.Underflow()

            // true - ok; false - argument or results aren't small ints
            member rt.AccelSmallIntAdd() : Status  =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try 
                        let sum = Microsoft.FSharp.Core.Operators.Checked.(+) m n
                        rt.DataStack <- (Value.ofInt sum)::ds'
                        opOK
                    with
                    | :? System.OverflowException -> accelFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallIntMul() : Status  =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    try
                        let prod = Microsoft.FSharp.Core.Operators.Checked.(*) m n
                        rt.DataStack <- (Value.ofInt prod)::ds'
                        opOK
                    with 
                    | :? System.OverflowException -> accelFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallIntGT() : Status  =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m > n) 
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallIntGTE() : Status  =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    if (m >= n) 
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelListVerify() : Status  =
                match rt.DataStack with
                | ((List t)::ds') -> 
                    rt.DataStack <- (Value.ofTerm t)::ds'
                    opOK
                | (_::_) -> opFail
                | _ -> rt.Underflow()
            
            member rt.AccelListLength() : Status =
                match rt.DataStack with
                | ((List t)::ds') ->
                    let nLen = Value.Rope.len t
                    rt.DataStack <- (Value.ofNat nLen)::ds'
                    opOK
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelListAppend() : Status =
                match rt.DataStack with
                | ((List r)::(List l)::ds') ->
                    let result = Rope.append l r
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // true - ok; false - index too large
            member rt.AccelListTake() : Status  =
                match rt.DataStack with
                | ((Nat64 n)::(List t)::ds') ->
                    if (n > (Rope.len t)) then opFail else
                    let result = Rope.take n t
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // true - ok; false - index too large
            // assumes we'll never have lists larger than 2**64 - 1 items
            member rt.AccelListSkip() : Status =
                match rt.DataStack with
                | ((Nat64 n)::(List t)::ds') ->
                    if (n > (Rope.len t)) then opFail else
                    let result = Rope.drop n t
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            // true - ok; false - index too large
            // assumes we'll never have lists larger than 2**64 - 1 items
            member rt.AccelListItem() : Status =
                match rt.DataStack with
                | ((Nat64 ix)::(List t)::ds') ->
                    if (ix >= (Rope.len t)) then opFail else
                    let result = Rope.item ix t
                    rt.DataStack <- result :: ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelListPushl() : Status =
                match rt.DataStack with
                | ((List l)::v::ds') ->
                    let result = Rope.cons v l
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

            member rt.AccelListPushr() : Status =
                match rt.DataStack with
                | (v::(List l)::ds') ->
                    let result = Rope.snoc l v 
                    rt.DataStack <- (Value.ofTerm result)::ds'
                    opOK
                | (_::_::_) -> rt.TypeError()
                | _ -> rt.Underflow()
            
            member rt.AccelListPopl() : Status =
                match rt.DataStack with
                | ((List l)::ds') ->
                    match l with
                    | Rope.ViewL(struct(v, l')) ->
                        rt.DataStack <- ((Value.ofTerm l') :: v :: ds')
                        opOK
                    | Leaf -> opFail
                    | _ -> rt.TypeError()
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow()
            
            member rt.AccelListPopr() : Status =
                match rt.DataStack with
                | ((List l)::ds') ->
                    match l with
                    | Rope.ViewR(struct(l', v)) ->
                        rt.DataStack <- (v :: (Value.ofTerm l') :: ds')
                        opOK
                    | Leaf -> opFail
                    | _ -> rt.TypeError()
                | (_::_) -> rt.TypeError()
                | _ -> rt.Underflow()

        // I've experimented with a runtime variant that uses an array for the
        // data stack and copies the data stack per transaction, but it's a bit
        // slower.

        let inline rtErrMsg (rt:Runtime) (msg : Value) =
            let ds = rt.ViewDataStack() |> Value.ofList
            Value.variant "rte" <| Value.asRecord ["data";"event"] [ds; msg]

    module Interpreter = 

        open RT1

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
                rt.TXBegin() |> ignore
                let bCond = interpret rt env c
                if bCond then
                    rt.TXCommit() |> ignore
                    interpret rt env a
                else
                    rt.TXAbort() |> ignore
                    interpret rt env b
            | Dip p ->
                let v = rt.PopData()
                let bOK = interpret rt env p
                rt.PushData(v) 
                bOK 
            | Stem lCopy U -> ignore (rt.Copy()); true
            | Stem lSwap U -> ignore (rt.Swap()); true
            | Stem lDrop U -> ignore (rt.Drop()); true
            | Stem lEq U -> isOK (rt.EqDrop())
            | Stem lGet U -> isOK (rt.TryGet())
            | Stem lPut U -> ignore (rt.Put()); true
            | Stem lDel U -> ignore (rt.Del()); true
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
                rt.TXBegin() |> ignore
                while(bOK && (interpret rt env c)) do
                    rt.TXCommit() |> ignore
                    bOK <- interpret rt env a
                    rt.TXBegin() |> ignore
                rt.TXAbort() |> ignore
                bOK
            | Until (c, a) ->
                let mutable bOK = true
                rt.TXBegin() |> ignore
                while(bOK && not (interpret rt env c)) do
                    rt.TXAbort() |> ignore
                    bOK <- interpret rt env a
                    rt.TXBegin() |> ignore
                rt.TXCommit() |> ignore
                bOK
            | Stem lEff U -> 
                match env with
                | (h::env') ->
                    rt.EnvPop() |> ignore
                    let bOK = interpret rt env' h
                    rt.EnvPush() |> ignore
                    bOK
                | [] ->
                    isOK (rt.TopLevelEffect())
            | Env (w, p) -> 
                rt.EnvPush() |> ignore
                let bOK = interpret rt (w::env) p
                rt.EnvPop() |> ignore
                bOK
            | Stem lFail U ->
                false 
            | Stem lHalt eMsg -> 
                rt.Halt(eMsg) |> ignore
                false
            | _ -> ignore(rt.Halt(lTypeError)); false
        and interpretSeq rt env l =
            match l with
            | Rope.ViewL(op, l') ->
                let bOK = interpret rt env op
                if not bOK then false else
                interpretSeq rt env l'
            | Leaf -> true
            | _ -> 
                rt.Halt(lTypeError) |> ignore
                false
        and interpretAccel rt env bOpt vModel p =
            let inline accelFail () =
                if bOpt then interpret rt env p else
                rt.Halt(Value.variant "accel" vModel) |> ignore
                false
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    rt.AccelListPushl() |> isOK
                | Value.Variant "pushr" U ->
                    rt.AccelListPushr() |> isOK
                | Value.Variant "popl" U ->
                    rt.AccelListPopl() |> isOK
                | Value.Variant "popr" U -> 
                    rt.AccelListPopr() |> isOK
                | Value.Variant "append" U -> 
                    rt.AccelListAppend() |> isOK
                | Value.Variant "verify" U -> 
                    rt.AccelListVerify() |> isOK
                | Value.Variant "length" U -> 
                    rt.AccelListLength() |> isOK
                | Value.Variant "take" U -> 
                    rt.AccelListTake() |> isOK
                | Value.Variant "skip" U -> 
                    rt.AccelListSkip() |> isOK
                | Value.Variant "item" U -> 
                    rt.AccelListItem() |> isOK
                | _ -> accelFail ()
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    rt.AccelBitsVerify() |> isOK
                | Value.Variant "negate" U -> 
                    rt.AccelBitsNegate() |> isOK
                | Value.Variant "reverse-append" U -> 
                    rt.AccelBitsReverseAppend() |> isOK
                //| Value.Variant "length" U -> accelFail ()
                | _ -> accelFail ()
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    // might fail for large integers or results
                    match rt.AccelSmallIntAdd() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "mul" U ->
                    match rt.AccelSmallIntMul() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                //| Value.Variant "sub" U -> accelFail ()
                //| Value.Variant "divmod" U -> accelFail ()
                | Value.Variant "increment" U -> 
                    match rt.AccelSmallIntIncrement() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "decrement" U -> 
                    match rt.AccelSmallIntDecrement() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "gt" U -> 
                    match rt.AccelSmallIntGT() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "gte" U -> 
                    match rt.AccelSmallIntGTE() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | _ -> accelFail ()
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    match rt.AccelSmallNatAdd() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "sub" U -> 
                    match rt.AccelSmallNatSub() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "mul" U -> 
                    match rt.AccelSmallNatMul() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "divmod" U ->
                    match rt.AccelSmallNatDivMod() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "gt" U -> 
                    match rt.AccelSmallNatGT() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
                | Value.Variant "gte" U -> 
                    match rt.AccelSmallNatGTE() with
                    | AccelFail -> interpret rt env p
                    | status -> isOK status 
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
        // observing results at runtime. Over 10x faster than plain
        // Interpreter in cases involving tight, long-running loops. 

        open RT1
        open Profiler

        type Cont = Runtime -> obj

        let lProf = Value.label "prof"

        [<Struct>]
        type CTE =
            { OnOK : Cont
            ; OnFail : Cont
            ; OnEff : CTE -> Cont
            }

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
                let onFail (k : Cont) (rt : Runtime) = ignore(rt.TXAbort()); lazyB.Force() rt
                let onOK (k : Cont) (rt : Runtime) = ignore(rt.TXCommit()); lazyA.Force() rt
                let preCond (k : Cont) (rt : Runtime) = ignore(rt.TXBegin()); k rt
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
                fun rt -> ignore(rt.Copy()); cte.OnOK rt
            | Stem lSwap U -> 
                fun rt -> ignore(rt.Swap()); cte.OnOK rt
            | Stem lDrop U -> 
                fun rt -> ignore(rt.Drop()); cte.OnOK rt
            | Stem lEq U -> 
                fun rt ->
                    if isOK (rt.EqDrop()) 
                        then cte.OnOK rt
                        else cte.OnFail rt
            | Stem lGet U -> 
                fun rt ->
                    if isOK (rt.TryGet())
                        then cte.OnOK rt
                        else cte.OnFail rt
            | Stem lPut U ->
                fun rt -> ignore(rt.Put()); cte.OnOK rt
            | Stem lDel U -> 
                fun rt -> ignore(rt.Del()); cte.OnOK rt
            | Prog (anno, p) ->
                compileAnno anno p cte
            | While (c, a) -> 
                let loRef = ref (cte.OnOK) 
                let runLoop rt = loRef.Value rt
                let lazyBody = lazy(compile a { cte with OnOK = runLoop })
                let onCondOK (rt : Runtime) = ignore(rt.TXCommit()); lazyBody.Force() rt
                let onCondFail (rt : Runtime) = ignore(rt.TXAbort()); cte.OnOK rt
                let tryCond = compile c { cte with OnOK = onCondOK; OnFail = onCondFail }
                loRef.Value <- fun rt -> ignore(rt.TXBegin()); tryCond rt
                runLoop
            | Until (c, a) ->
                let loRef = ref (cte.OnOK) 
                let runLoop rt = loRef.Value rt
                let lazyBody = lazy(compile a { cte with OnOK = runLoop })
                let onCondOK (rt : Runtime) = ignore(rt.TXCommit()); cte.OnOK rt
                let onCondFail (rt : Runtime) = ignore(rt.TXAbort()); lazyBody.Force() rt
                let tryCond = compile c { cte with OnOK = onCondOK; OnFail = onCondFail }
                loRef.Value <- fun rt -> ignore(rt.TXBegin()); tryCond rt
                runLoop
            | Stem lEff U -> 
                cte.OnEff cte
            | Env (w, p) -> 
                // to avoid recompiling 'w' per 'eff' operation, will use
                // intermeidate register to track the current continuation.
                let reg = ref cte // placeholder
                let dynCTE = 
                    { cte with 
                        OnOK = fun rt -> ignore(rt.EnvPush()); reg.Value.OnOK rt
                        OnFail = fun rt -> ignore(rt.EnvPush()); reg.Value.OnFail rt 
                    }
                let opHandler = compile w dynCTE
                let runHandler (cte' : CTE) (rt : Runtime) =
                    reg.Value <- cte'
                    rt.EnvPop() |> ignore
                    opHandler rt
                let progCTE =
                    { cte with
                        OnOK = fun rt -> ignore(rt.EnvPop()); cte.OnOK rt
                        OnFail = fun rt -> ignore(rt.EnvPop()); cte.OnFail rt
                        OnEff = runHandler
                    }
                let op = compile p progCTE
                fun rt -> ignore(rt.EnvPush()); op rt
            | Stem lFail U ->
                cte.OnFail
            | Stem lHalt eMsg -> 
                fun rt ->
                    rt.Halt(eMsg) |> ignore
                    cte.OnFail rt                    
            | _ -> 
                fun rt ->
                    rt.Halt(lTypeError) |> ignore
                    cte.OnFail rt
        and compileAnno (anno : Value) (p : Program) (cte : CTE) : Cont =
            // not well factored at the moment due to profiler and
            // continuations being passed together. 
            match anno with
            | Record ["prof"] struct([ValueSome profOptions],anno') ->
                // nop for now
                compileAnno anno' p cte 
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
            let inline onAccelFail rt =
                if bOpt then lazyOp.Force() rt else 
                rt.Halt(Value.variant "accel" vModel) |> ignore
                cte.OnFail rt
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    fun rt -> ignore(rt.AccelListPushl()); cte.OnOK rt
                | Value.Variant "pushr" U ->
                    fun rt -> ignore(rt.AccelListPushr()); cte.OnOK rt
                | Value.Variant "popl" U ->
                    fun rt -> 
                        if isOK (rt.AccelListPopl())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "popr" U -> 
                    fun rt ->
                        if isOK (rt.AccelListPopr())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "append" U -> 
                    fun rt -> ignore(rt.AccelListAppend()); cte.OnOK rt
                | Value.Variant "verify" U -> 
                    fun rt ->
                        if isOK(rt.AccelListVerify()) 
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "length" U -> 
                    fun rt -> ignore(rt.AccelListLength()); cte.OnOK rt
                | Value.Variant "take" U -> 
                    fun rt ->
                        if isOK(rt.AccelListTake())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "skip" U -> 
                    fun rt ->
                        if isOK(rt.AccelListSkip())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "item" U -> 
                    fun rt ->
                        if isOK(rt.AccelListItem())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | _ -> onAccelFail 
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    fun rt ->
                        if isOK(rt.AccelBitsVerify())
                            then cte.OnOK rt
                            else cte.OnFail rt
                | Value.Variant "negate" U -> 
                    fun rt -> ignore(rt.AccelBitsNegate()); cte.OnOK rt
                | Value.Variant "reverse-append" U -> 
                    fun rt -> ignore(rt.AccelBitsReverseAppend()); cte.OnOK rt
                // other good options: length, or, and, xor
                | _ -> onAccelFail
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    fun rt ->
                        match rt.AccelSmallIntAdd() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "mul" U ->
                    fun rt ->
                        match rt.AccelSmallIntMul() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                //| Value.Variant "sub" U -> onAccelFail
                //| Value.Variant "divmod" U -> onAccelFail 
                | Value.Variant "increment" U -> 
                    fun rt ->
                        match rt.AccelSmallIntIncrement() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "decrement" U -> 
                    fun rt ->
                        match rt.AccelSmallIntDecrement() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "gt" U -> 
                    fun rt ->
                        match rt.AccelSmallIntGT() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "gte" U -> 
                    fun rt ->
                        match rt.AccelSmallIntGTE() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | _ -> onAccelFail
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    fun rt ->
                        match rt.AccelSmallNatAdd() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "sub" U -> 
                    fun rt ->
                        match rt.AccelSmallNatSub() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "mul" U -> 
                    fun rt ->
                        match rt.AccelSmallNatMul() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "divmod" U ->
                    fun rt ->
                        match rt.AccelSmallNatDivMod() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "gt" U -> 
                    fun rt ->
                        match rt.AccelSmallNatGT() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | Value.Variant "gte" U -> 
                    fun rt ->
                        match rt.AccelSmallNatGTE() with
                        | OpOK -> cte.OnOK rt
                        | OpFail -> cte.OnFail rt
                        | _ -> lazyOp.Force() rt
                | _ -> onAccelFail
            | _ -> onAccelFail

        let eval (p : Program) : Effects.IEffHandler -> Value list -> Value list option =
            let onOK (rt : Runtime) = 
                box (Some (rt.ViewDataStack()))
            let onFail (rt : Runtime) = 
                box None
            let onEff (cte : CTE) (rt : Runtime) =
                match rt.TopLevelEffect() with
                | OpOK -> cte.OnOK rt
                | OpFail -> cte.OnFail rt
                | s -> failwithf "unexpected effect status %d" s
            let cte0 = 
                { OnOK = onOK
                ; OnFail = onFail
                ; OnEff = onEff 
                }
            let lazyOp = // compile stuff once only
                lazy(compile p cte0)
            fun io ds ->
                let rt = new Runtime(ds, io)
                let result = 
                    try 
                        unbox<Value list option> <| lazyOp.Force() rt
                    with
                    | RTError(eMsg) -> 
                        let v = rtErrMsg rt eMsg
                        Effects.logErrorV io "runtime error" v
                        None
                // logProfile io prof
                result

    module CompiledInterpreter =
        // This implementation of 'eval' uses System.Reflection.Emit to produce
        // code using large methods.
        //
        // Outcome: This adds significant overhead for initializing the app,
        // and doesn't offer significant performance benefits over FTI. So, this
        // is a failed experiment, other than it taught reflection and MSIL.

        open System.Reflection
        open System.Reflection.Emit
        open Profiler
        open RT1

        //
        // Features of the generated class:
        //
        // - `data:Value` fields represented as static data in type
        // - reusable subprograms via methods, initially for 'env' but
        //   later may also support 'prog' annotated for subroutines
        // - effects env passed as list parameter at runtime on dotnet
        //   data stack
        //

        // Our runtime stack of effects handlers, via method pointers.
        // This parameter helps support reusable subprograms. 
        type Addr = nativeint
        type RTE = Addr list
        let argsRTE = [| typeof<RTE> |]

        type SW = System.Diagnostics.Stopwatch

        // the dotnet reflection API does not provide convenient access to
        // methods within modules, so I'll just use a type. 
        type HelperOps = 
            static member InitRTE(addr:Addr) : RTE = 
                //printfn "(Debug) initial handler = %A" addr
                [addr] 
            static member PushHandler(rte:RTE, addr:Addr) : RTE = 
                //printfn "(Debug) adding handler %A to %A" addr rte
                (addr::rte)
            static member CurrHandler(rte:RTE) : Addr =
                //printfn "(Debug) accessing handler from %A" rte
                List.head rte
            static member DropHandler(rte:RTE) : RTE = 
                //printfn "(Debug) dropping handler from %A" rte
                List.tail rte
            static member PrintHandlers(rte:RTE) : RTE =   
                printfn "(Debug) current handler list = %A" rte
                rte
            static member DebugPrint(n:int) : int =
                printfn "(Debug) %d" n
                n

        // Reusable subprograms, including effects handlers.
        type MethodDict = System.Collections.Generic.Dictionary<Program, MethodInfo>

        // 'static data' - I'll represent static data as an array of values
        // in a static field of the object. Each value will have an index in
        // this array. The static values will be updated AFTER the type is
        // created.
        type DataDict = System.Collections.Generic.Dictionary<Value, int>

        type CTE = 
            { 
                AsmB     : AssemblyBuilder  // assembly being constructed
                ModB     : ModuleBuilder    // the only module in the assembly
                TypB     : TypeBuilder      // the only type in the module
                FldRT    : FieldBuilder     // the runtime field within the type
                FldData  : FieldBuilder     // array of static data
                Methods  : MethodDict       // reusable methods
                Statics  : DataDict         // tracks static data
                NextID   : Ref<int>         // simple internally unique identifiers
            }

        let genID (cte:CTE) : int =
            let n = cte.NextID.Value
            assert(n < System.Int32.MaxValue) 
            cte.NextID.Value <- (n + 1)
            n

        let addStatic (cte:CTE) (v:Value) : int =
            match cte.Statics.TryGetValue(v) with
            | true, ix -> ix // reuse data
            | false, _ ->
                let ix = cte.Statics.Count
                cte.Statics.Add(v, ix)
                ix

        let newSub (cte : CTE) : MethodBuilder = 
            let name = "sub" + string(genID cte)
            cte.TypB.DefineMethod(
                name,
                MethodAttributes.Private ||| MethodAttributes.Final,
                CallingConventions.Standard,
                typeof<bool>, argsRTE)

        let addSub (cte:CTE) (p:Program) (mkSub : ILGenerator -> unit) : MethodInfo =
            match cte.Methods.TryGetValue(p) with
            | true, m -> m
            | false, _ ->
                let m = newSub cte
                cte.Methods.Add(p, m)
                mkSub (m.GetILGenerator())
                m

        // create the type and initialize statics.
        let createType (cte : CTE) : System.Type =
            let staticData : Value array = Array.create (cte.Statics.Count) (Value.unit)
            for kvp in cte.Statics do
                staticData[kvp.Value] <- kvp.Key
            let newType = cte.TypB.CreateType()
            newType.GetField(cte.FldData.Name).SetValue(null, staticData)
            newType

        // initCTE prepares an initial CTE with a type that is constructed by
        // giving it the Runtime argument. The profile is shared across all 
        // instances of this type, currently.
        let initCTE () : CTE =
            // for now, all named the same. Might need to distinguish later
            // to support debugging of exception stacks, but uncertain.
            let asmName = AssemblyName("Glas.ProgEval.DynCI")
            let asmOp = AssemblyBuilderAccess.RunAndCollect
            let asmB = AssemblyBuilder.DefineDynamicAssembly(asmName, asmOp)
            let modB = asmB.DefineDynamicModule("M")
            let typeAttr = TypeAttributes.Public ||| TypeAttributes.Sealed
            let typB = modB.DefineType("Program", typeAttr)
            let fldAttrRT = FieldAttributes.Private ||| FieldAttributes.InitOnly
            let fldRT = typB.DefineField("Runtime", typeof<Runtime>, fldAttrRT)
            let fldAttrData = FieldAttributes.Public ||| FieldAttributes.Static
            let fldData = typB.DefineField("StaticData", typeof<Value array>, fldAttrData)

            // the only constructor takes a Runtime as an argument.
            let ctor = typB.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    [| typeof<Runtime> |])
            do
                let il = ctor.GetILGenerator()
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Stfld, fldRT)
                il.Emit(OpCodes.Ret)
            { AsmB = asmB
            ; ModB = modB
            ; TypB = typB
            ; FldRT = fldRT
            ; FldData = fldData
            ; Methods = new MethodDict()
            ; Statics = new DataDict()
            ; NextID = ref 1000
            }

        let inline loadRT (cte:CTE) (il : ILGenerator) =
            il.Emit(OpCodes.Ldarg_0) // this
            il.Emit(OpCodes.Ldfld, cte.FldRT) // this.RT

        // call RT with zero arguments (very common)
        let inline rtCall0 (cte:CTE) (opName : string) (il : ILGenerator) =
            loadRT cte il
            il.Emit(OpCodes.Call, typeof<Runtime>.GetMethod(opName))

        // call RT with zero arguments then drop result (common)
        let inline rtCall0_ cte opName il =
            rtCall0 cte opName il
            il.Emit(OpCodes.Pop) // ignore result

        let inline rtCall0b cte opName (lblFail : Label) il =
            rtCall0 cte opName il
            assert(0 = opOK)
            il.Emit(OpCodes.Brtrue, lblFail) 

        // primary method to run the compiled program
        let entryMethod = "Run"

        let emitDebugPrint (n : int) (il : ILGenerator) =
            il.Emit(OpCodes.Ldc_I4, n)
            il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DebugPrint"))
            il.Emit(OpCodes.Pop)

        let emitDebugHandlers (il : ILGenerator) = 
            il.Emit(OpCodes.Ldarg_1)
            il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("PrintHandlers"))
            il.Emit(OpCodes.Pop)

        let inline emitSubExit (lblFail : Label) (il : ILGenerator) =
            // nothing should be on stack when we reach exit.
            // But we might jump to the final failure case.
            // return true
            //emitDebugPrint 301 il
            il.Emit(OpCodes.Ldc_I4_1) 
            il.Emit(OpCodes.Ret)
            // or if operation branches to fail, return false
            il.MarkLabel(lblFail)
            //emitDebugPrint 302 il
            il.Emit(OpCodes.Ldc_I4_0)
            il.Emit(OpCodes.Ret)

        let rec compile (cte:CTE) (p0:Program) (il:ILGenerator) : unit =
            let lblFail = il.DefineLabel()
            //emitDebugPrint 300 il
            compileOp cte p0 lblFail il
            emitSubExit lblFail il
        and compileOp (cte:CTE) (p0:Program) (lblFail : Label) (il:ILGenerator) : unit =
            let inline debugPrint (n : int) = emitDebugPrint n il
            match p0 with
            | PSeq (List lP) -> 
                compileSeq cte lP lblFail il
            | Data v -> 
                let ix = addStatic cte v // index of data in the "StaticData" array
                loadRT cte il
                il.Emit(OpCodes.Ldsfld, cte.FldData)
                il.Emit(OpCodes.Ldc_I4, ix)
                il.Emit(OpCodes.Ldelem, typeof<Value>)
                il.Emit(OpCodes.Call, typeof<Runtime>.GetMethod("PushData"))
                // no status to pop
            | Cond (c, a, b) -> 
                let lblEndCond = il.DefineLabel()
                let lblOnCondFail = il.DefineLabel()
                // first run the cond
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblOnCondFail il  
                // if we reach this point, we're on the true path
                // but our failure destination changes.
                rtCall0_ cte "TXCommit" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblEndCond)
                // failure path
                il.MarkLabel(lblOnCondFail)
                rtCall0_ cte "TXAbort" il
                compileOp cte b lblFail il
                il.MarkLabel(lblEndCond)
            | Dip p ->
                il.BeginScope()
                let x = il.DeclareLocal(typeof<Value>)
                // store top stack item into this local
                loadRT cte il
                il.Emit(OpCodes.Call, typeof<Runtime>.GetMethod("PopData"))
                il.Emit(OpCodes.Stloc, x) 
                compileOp cte p lblFail il 
                loadRT cte il
                il.Emit(OpCodes.Ldloc, x)
                il.Emit(OpCodes.Call, typeof<Runtime>.GetMethod("PushData"))
                il.EndScope()
            | Stem lCopy U ->
                rtCall0_ cte "Copy" il
            | Stem lSwap U -> 
                rtCall0_ cte "Swap" il
            | Stem lDrop U ->
                rtCall0_ cte "Drop" il
            | Stem lEq U -> 
                rtCall0b cte "EqDrop" lblFail il
            | Stem lGet U -> 
                rtCall0b cte "TryGet" lblFail il
            | Stem lPut U -> 
                rtCall0_ cte "Put" il
            | Stem lDel U -> 
                rtCall0_ cte "Del" il
            | Prog (anno, p) ->
                compileAnno cte anno p lblFail il
            | While (c, a) -> 
                let lblRepeat = il.DefineLabel()
                let lblExitLoop = il.DefineLabel()
                il.MarkLabel(lblRepeat)
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblExitLoop il
                rtCall0_ cte "TXCommit" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblRepeat)
                il.MarkLabel(lblExitLoop)
                rtCall0_ cte "TXAbort" il
            | Until (c, a) ->
                let lblRepeat = il.DefineLabel()
                let lblContLoop = il.DefineLabel()
                let lblExitLoop = il.DefineLabel()
                il.MarkLabel(lblRepeat)
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblContLoop il
                il.Emit(OpCodes.Br, lblExitLoop)
                il.MarkLabel(lblContLoop)
                rtCall0_ cte "TXAbort" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblRepeat)
                il.MarkLabel(lblExitLoop)
                rtCall0_ cte "TXCommit" il 
            | Stem lEff U -> 
                // this.method(tail rt) where method is determined by head(rt)
                // using arg_1 to store jumps for env
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DropHandler"))
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("CurrHandler"))
                let callConv = CallingConventions.HasThis ||| CallingConventions.Standard
                il.EmitCalli(OpCodes.Calli, callConv, typeof<bool>, argsRTE, null)
                il.Emit(OpCodes.Brfalse, lblFail)
            | Env (w, p) -> 
                // compile handler as a subroutine
                let wSub = newSub cte
                do
                    let ilW = wSub.GetILGenerator()
                    let wFail = ilW.DefineLabel()
                    rtCall0_ cte "EnvPop" ilW
                    compileOp cte w wFail ilW
                    rtCall0_ cte "EnvPush" ilW
                    emitSubExit wFail ilW
                // to ensure handlers are in 'arg_1' position, we'll represent
                // the called program as a subprogram.
                let pSub = newSub cte
                compile cte p (pSub.GetILGenerator()) 

                rtCall0_ cte "EnvPush" il   
                il.Emit(OpCodes.Ldarg_0) // 'this' arg for this.pSub(handlers) call
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Ldftn, wSub) // adding address of wSub to head of handlers list
                il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("PushHandler"))
                il.Emit(OpCodes.Call, pSub) // returns bool pass/fail
                rtCall0_ cte "EnvPop" il
                il.Emit(OpCodes.Brfalse, lblFail) // the 'pSub' call may fail
            | Stem lFail U ->
                il.Emit(OpCodes.Br, lblFail)
            | Stem lHalt eMsg -> 
                let ixMsg = addStatic cte eMsg
                loadRT cte il
                il.Emit(OpCodes.Ldsfld, cte.FldData)
                il.Emit(OpCodes.Ldc_I4, ixMsg)
                il.Emit(OpCodes.Ldelem, typeof<Value>)
                il.Emit(OpCodes.Call, typeof<Runtime>.GetMethod("Halt"))
                // halt will throw an exception, so we don't actually reach the 
                // following codes. However, keeping them for MSIL validation.
                il.Emit(OpCodes.Pop) // ignore status
                il.Emit(OpCodes.Br, lblFail)
            | _ ->
                // if an invalid program reaches our compiler, just treat it as a halt.
                let eMsg = Value.variant "prog-invalid" p0 
                compileOp cte (Value.variant "halt" eMsg) lblFail il  
        and compileSeq (cte : CTE) (l : Value.Term) (lblFail : Label) (il : ILGenerator) : unit =
            match l with
            | Rope.ViewL(op, l') ->
                compileOp cte op lblFail il
                compileSeq cte l' lblFail il
            | Leaf -> () 
            | _ -> 
                compileOp cte (Value.variant "halt" lTypeError) (lblFail) il
        and compileAnno (cte : CTE) (anno : Value) (p : Program) (lblFail:Label) (il:ILGenerator) : unit =
            // not well factored at the moment due to profiler and
            // continuations being passed together. 
            match anno with
            | Record ["prof"] struct([ValueSome profOptions],anno') ->
                // I could feasibly allocate and handle the stopwatch, but dealing with
                // the profile registers is a pain. Might need to use static fields same
                // as for the value fields. 
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["stow"] struct([ValueSome vOpts], anno') ->
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["memo"] struct([ValueSome memoOpts], anno') ->
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["accel"] struct([ValueSome vModel], anno') ->
                let p' = Prog(anno', p)
                match vModel with
                | Variant "opt" vModel' ->
                    compileAccel cte true  vModel' p' lblFail il
                | _ ->
                    compileAccel cte false vModel  p' lblFail il
            | _ -> // ignoring other annotations 
                // handle as a reusable subroutine call
                let pSub = addSub cte p (compile cte p)
                il.Emit(OpCodes.Ldarg_0) // this
                il.Emit(OpCodes.Ldarg_1) // effects handlers
                il.Emit(OpCodes.Call, pSub) // call the reusable method
                il.Emit(OpCodes.Brfalse, lblFail) // handle failure
        and compileAccel cte bOpt vModel p lblFail il =
            let inline accelFail () =
                // if acceleration is unavailable and optional, 
                // just run p directly.
                if bOpt then compileOp cte p lblFail il else
                // otherwise, to avoid silent performance degradation,
                // halt and indicate the accelerator.
                let eMsg = Value.variant "accel" vModel
                compileOp cte (Value.variant "halt" eMsg) lblFail il 
            let emitPartialAccel (rtMethod : string) =
                // for cases where we need to handle 'accelFail'. 
                // Also handles OK/Fail results.
                let subNonAccel = addSub cte p (compile cte p)
                let lblFin = il.DefineLabel()
                il.BeginScope()
                let result = il.DeclareLocal(typeof<int>)
                rtCall0 cte rtMethod il // call accelerated method
                il.Emit(OpCodes.Dup)
                il.Emit(OpCodes.Stloc, result)  
                il.Emit(OpCodes.Brfalse, lblFin) // on OK, skip the rest
                assert(opFail = 1) 
                il.Emit(OpCodes.Ldc_I4_1)
                il.Emit(OpCodes.Ldloc, result)
                il.Emit(OpCodes.Beq, lblFail) // on Fail, jump to failure 
                il.EndScope()
                // assuming accelFail; call the subroutine!
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Call, subNonAccel)
                il.Emit(OpCodes.Brfalse, lblFail)
                il.MarkLabel(lblFin)
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    rtCall0_ cte "AccelListPushl" il
                | Value.Variant "pushr" U ->
                    rtCall0_ cte "AccelListPushr" il
                | Value.Variant "popl" U ->
                    rtCall0b cte "AccelListPopl" lblFail il
                | Value.Variant "popr" U -> 
                    rtCall0b cte "AccelListPopr" lblFail il
                | Value.Variant "append" U -> 
                    rtCall0_ cte "AccelListAppend" il
                | Value.Variant "verify" U -> 
                    rtCall0b cte "AccelListVerify" lblFail il
                | Value.Variant "length" U -> 
                    rtCall0_ cte "AccelListLength" il
                | Value.Variant "take" U -> 
                    rtCall0b cte "AccelListTake" lblFail il
                | Value.Variant "skip" U -> 
                    rtCall0b cte "AccelListSkip" lblFail il
                | Value.Variant "item" U -> 
                    rtCall0b cte "AccelListItem" lblFail il
                | _ -> accelFail ()
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    rtCall0b cte "AccelBitsVerify" lblFail il
                | Value.Variant "negate" U -> 
                    rtCall0_ cte "AccelBitsNegate" il
                | Value.Variant "reverse-append" U ->
                    rtCall0_ cte "AccelBitsReverseAppend" il
                //| Value.Variant "length" U -> accelFail ()
                | _ -> accelFail ()
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    emitPartialAccel "AccelSmallIntAdd"
                | Value.Variant "mul" U ->
                    emitPartialAccel "AccelSmallIntMul"
                //| Value.Variant "sub" U -> accelFail ()
                //| Value.Variant "divmod" U -> accelFail ()
                | Value.Variant "increment" U -> 
                    emitPartialAccel "AccelSmallIntIncrement"
                | Value.Variant "decrement" U -> 
                    emitPartialAccel "AccelSmallIntDecrement"
                | Value.Variant "gt" U -> 
                    emitPartialAccel "AccelSmallIntGT"
                | Value.Variant "gte" U -> 
                    emitPartialAccel "AccelSmallIntGTE"
                | _ -> accelFail ()
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    emitPartialAccel "AccelSmallNatAdd"
                | Value.Variant "sub" U -> 
                    emitPartialAccel "AccelSmallNatSub"
                | Value.Variant "mul" U -> 
                    emitPartialAccel "AccelSmallNatMul"
                | Value.Variant "divmod" U ->
                    emitPartialAccel "AccelSmallNatDivMod"
                | Value.Variant "gt" U -> 
                    emitPartialAccel "AccelSmallNatGT"
                | Value.Variant "gte" U -> 
                    emitPartialAccel "AccelSmallNatGTE"
                | _ -> accelFail ()
            | _ -> accelFail ()

        let compileEntry (cte:CTE) (p:Program) : unit =
            let rootProg = newSub cte
            compile cte p (rootProg.GetILGenerator())

            // top-level effect handler must call this.RT.TopLevelEffect()
            let eff0 = cte.TypB.DefineMethod(
                        "eff0",
                        MethodAttributes.Private ||| MethodAttributes.Final,
                        CallingConventions.Standard,
                        typeof<bool>, argsRTE)
            do
                let il = eff0.GetILGenerator()
                rtCall0 cte "TopLevelEffect" il
                assert(opOK = 0) // validate Ldc_I4_0 is still okay
                il.Emit(OpCodes.Ldc_I4_0)
                il.Emit(OpCodes.Ceq) // true if status is OK
                il.Emit(OpCodes.Ret)
            
            let entryB = cte.TypB.DefineMethod(
                        entryMethod, 
                        MethodAttributes.Public ||| MethodAttributes.Final,
                        CallingConventions.Standard,
                        typeof<bool>, null)
            do 
                let il = entryB.GetILGenerator()
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldftn, eff0)
                il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("InitRTE"))
                il.Emit(OpCodes.Tailcall)
                il.Emit(OpCodes.Call, rootProg)
                il.Emit(OpCodes.Ret)

        let eval (p : Program) : Effects.IEffHandler -> Value list -> Value list option =
            let lazyType = lazy ( // defer compile to first call
                //printfn "compiling program"
                let cte = initCTE ()
                compileEntry cte p
                let result = createType cte
                //printfn "compile completed"
                result
                )
            fun io args ->
                let myType = lazyType.Force()
                let rt = new Runtime(args, io)
                let instArgs : obj array = [| rt |]
                let myObj = System.Activator.CreateInstance(myType, instArgs)
                let mRun = myType.GetMethod(entryMethod)
                let bOK =
                    try 
                        let invokeAttr = BindingFlags.DoNotWrapExceptions
                        mRun.Invoke(myObj, invokeAttr, null, null, null) |> unbox<bool>
                    with
                    | RTError(eMsg) ->
                        let v = rtErrMsg rt eMsg
                        Effects.logErrorV io "runtime error" v
                        false
                if bOK
                    then Some (rt.ViewDataStack())
                    else None


    module CompiledStackMachine =
        open System.Reflection
        open System.Reflection.Emit
        open Value
        open ProgVal

        // This implementation uses the CLR data stack (and locals, etc.) to
        // avoid allocation and GC and bounds checks at runtime. Results are
        // returned by-ref, except for pass/fail, which uses the main return
        // value.
        // 
        // Reusable programs must either be pure or compiled in context of 
        // a known stack of effects handlers. This allows saving of effects
        // handler state to also be statically prepared.

        // Lightweight effect wrapper. 
        // Features:
        //   support 'unwind' of transaction after exception (need this!)
        //   lazy try/commit/abort in case no effect is used (performance)
        //   simplify access from the compiled code (no 'voption!')
        type EWrap = 
            val private IO : Effects.IEffHandler
            val mutable private DeferTry : int
            val mutable private ActiveTry : int
            new(io) = 
                { IO = io
                ; DeferTry = 0 
                ; ActiveTry = 0
                }

            member ewrap.Try() =
                ewrap.DeferTry <- ewrap.DeferTry + 1

            member ewrap.Commit() =
                if (ewrap.DeferTry > 0) then 
                    ewrap.DeferTry <- ewrap.DeferTry - 1
                else 
                    assert(ewrap.ActiveTry > 0)
                    ewrap.ActiveTry <- ewrap.ActiveTry - 1
                    ewrap.IO.Commit()

            member ewrap.Abort() =
                if (ewrap.DeferTry > 0) then 
                    ewrap.DeferTry <- ewrap.DeferTry - 1
                else 
                    assert(ewrap.ActiveTry > 0)
                    ewrap.ActiveTry <- ewrap.ActiveTry - 1
                    ewrap.IO.Abort()

            member private ewrap.Activate() =
                while (ewrap.DeferTry > 0) do
                    ewrap.DeferTry <- ewrap.DeferTry - 1
                    ewrap.ActiveTry <- ewrap.ActiveTry + 1
                    ewrap.IO.Try()

            member ewrap.Eff(arg:Value, result:outref<Value>) : bool =
                ewrap.Activate()
                match ewrap.IO.Eff(arg) with
                | ValueNone -> 
                    false
                | ValueSome vResult ->
                    result <- vResult
                    true

            member ewrap.Unwind() =
                ewrap.DeferTry <- 0
                while (ewrap.ActiveTry > 0) do
                    ewrap.ActiveTry <- ewrap.ActiveTry - 1
                    ewrap.IO.Abort()


        // Static analysis and lightweight optimizations to support compilation.
        // This is specialized to the compile phase, e.g. max stack must know how
        // 'eff' is handled (and possibly other reusable subprograms).
        [<Struct>]
        type Lim =
            { ArityIn : int16
            ; ArityOut : int16
            ; MaxStack : int16
            ; Effectful : bool
            ; Aborted : bool  // if true, ArityOut is invalid
            }

        let limAddStack (d : int16) (a : Lim) : Lim =
            if (d < 1s) then a else
            { a with 
                ArityIn = a.ArityIn + d
                ArityOut = a.ArityOut + d 
                MaxStack = a.MaxStack + d
            }
        
        
        let limSeq a b =
            let d = a.ArityOut - b.ArityIn 
            let bEff = a.Effectful || b.Effectful
            let bAbort = a.Aborted || b.Aborted
            if (d >= 0s) then
                // add d unused inputs to b
                { ArityIn = a.ArityIn
                ; ArityOut = b.ArityOut + d
                ; MaxStack = max (a.MaxStack) (b.MaxStack + d)
                ; Effectful = bEff
                ; Aborted = bAbort
                }
            else
                // add -d unused inputs to a
                { ArityIn = a.ArityIn - d
                ; ArityOut = b.ArityOut
                ; MaxStack = max (a.MaxStack - d) (b.MaxStack)
                ; Effectful = bEff
                ; Aborted = bAbort
                }

        let inline limOfArity (i:int) (o:int) : Lim = 
            { ArityIn = int16 i
            ; ArityOut = int16 o
            ; MaxStack = int16 (max i o)
            ; Effectful = false 
            ; Aborted = false
            } 
        
        let acceptArity p =
            match p with
            | Stem lFail U | Stem lHalt _ -> false
            | _ -> true

        // to support caching of arity
        let lPure = Value.label "pure"
        let lAbort = Value.label "abort"
        let lIn = Value.label "in"
        let lOut = Value.label "out"
        let lMax = Value.label "max"
        let lLim = Value.label "lim"

        let limVal (l : Lim) : Value =
            let inline ar_out v =
                if l.Aborted then record_insert lAbort unit v else
                record_insert lOut (ofInt (int64 l.ArityOut)) v
            let inline ar_in v =
                record_insert lIn (ofInt (int64 l.ArityIn)) v
            let inline max_stack v =
                record_insert lMax (ofInt (int64 l.MaxStack)) v
            let inline effectful v =
                if l.Effectful then v else
                record_insert lPure unit v
            unit |> ar_out 
                 |> ar_in 
                 |> max_stack 
                 |> effectful

        [< return: Struct >]
        let (|Lim|_|) (v : Value) : Lim voption = 
            match v with
            | RecL [lPure; lAbort; lIn; lOut; lMax] ([optPure; optAbort; ValueSome (Int32 arIn); optOut; ValueSome (Int32 nMax)], U) ->
                let bEffectful = ValueOption.isNone optPure
                match optAbort, optOut with
                | ValueNone, ValueSome (Int32 arOut) ->
                    { ArityIn = int16 arIn
                    ; ArityOut = int16 arOut
                    ; MaxStack = int16 nMax
                    ; Aborted = false
                    ; Effectful = bEffectful
                    } |> ValueSome
                | ValueSome U, ValueNone ->
                    { ArityIn = int16 arIn
                    ; ArityOut = 0s
                    ; MaxStack = int16 nMax
                    ; Aborted = true
                    ; Effectful = bEffectful
                    } |> ValueSome
                | _ -> ValueNone
            | _ -> ValueNone

        // lightweight caching of limits
        let cacheLim (struct(lim, p)) =
            let p' = addAnno lLim (limVal lim) p
            struct(lim, p')

        let rec precomp (p0:Program) : struct(Lim * Program) =
            match p0 with
            | Stem lCopy U -> 
                struct(limOfArity 1 2, p0)
            | Stem lSwap U -> 
                struct(limOfArity 2 2, p0)
            | Stem lDrop U -> 
                struct(limOfArity 1 0, p0)
            | Stem lEq U -> 
                struct(limOfArity 2 0, p0)
            | Stem lGet U -> 
                struct(limOfArity 2 1, p0)
            | Stem lPut U -> 
                struct(limOfArity 3 1, p0)
            | Stem lDel U -> 
                struct(limOfArity 2 1, p0)
            | Stem lEff U -> 
                // I'll assume the 'eff' handler is a separate method.
                // Thus, influence on the local data stack is just one item.
                struct({ limOfArity 1 1 with Effectful = true }, p0)
            | Stem lFail U | Stem lHalt _ ->
                struct({ limOfArity 0 0 with Aborted = true }, p0) 
            | Dip p ->
                let struct(l,p') = precomp p
                struct(limAddStack 1s l, Dip p')
            | Data _ ->
                struct(limOfArity 0 1, p0)
            | PSeq (List lP) ->
                // several special cases here, so seq gets its own function.
                let struct(lim, lP') = precompSeq (limOfArity 0 0) (Rope.empty) lP 
                struct(lim, PSeq (Value.ofTerm lP'))
            | Cond (pc, pa, pb) ->
                let struct(c0, pc') = cacheLim <| precomp pc
                let struct(a0, pa') = precomp pa
                let struct(b0, pb') = precomp pb
                let cond' = Cond(pc', pa', pb') 
                let ca0 = limSeq c0 a0
                let arIn = max (ca0.ArityIn) (b0.ArityIn)
                let ca = limAddStack (arIn - ca0.ArityIn) ca0
                let b = limAddStack (arIn - b0.ArityIn) b0
                let bBalanced = (ca.ArityOut - ca.ArityIn) = (b.ArityOut - b.ArityIn)
                let bEffectful = ca.Effectful || b.Effectful
                let nMaxStack = max (ca.MaxStack) (b.MaxStack)
                if bBalanced || ca.Aborted then
                    // using arity from 'b' which is okay if balanced or if
                    // the ca path is incomplete.
                    let limCond = 
                        { ArityIn = b.ArityIn
                        ; ArityOut = b.ArityOut
                        ; MaxStack = nMaxStack
                        ; Effectful = bEffectful
                        ; Aborted = ca.Aborted && b.Aborted
                        }
                    struct(limCond, cond')
                elif b.Aborted then
                    let limCond =
                        { ArityIn = ca.ArityIn
                        ; ArityOut = ca.ArityOut
                        ; MaxStack = nMaxStack
                        ; Effectful = bEffectful
                        ; Aborted = false // from context, we know success path is not aborted
                        }
                    struct(limCond, cond')
                else 
                    // imbalanced AND neither side is aborted
                    // this is just a plain error. Convert to 
                    // a failed program.
                    precomp <| Halt (Value.variant "cond-imbal" p0)
            | While (pc, pa) ->
                let struct(c0, pc') = cacheLim <| precomp pc
                let struct(a0, pa') = precomp pa
                let ca = limSeq c0 a0
                let okBal = (ca.ArityIn = ca.ArityOut)
                if okBal || ca.Aborted then
                    let loop' = While(pc', pa') 
                    struct(ca, loop')
                else 
                    precomp <| Halt (Value.variant "loop-arity" p0)
            | Until (pc, pa) ->
                let struct(c0, pc') = cacheLim <| precomp pc
                let struct(a0, pa') = precomp pa
                let okBal = (a0.ArityIn = a0.ArityOut)
                if okBal || a0.Aborted then
                    let loop' = Until(pc', pa')
                    struct(limSeq a0 c0, loop')
                else 
                    precomp <| Halt (Value.variant "loop-arity" p0)
            | Env (pwith, pdo) ->
                // assume the 'with' handler is evaluated in a 
                // separate method call, to simplify local stack
                let struct(w0, pw') = cacheLim <| precomp pwith
                let struct(d0, pd') = precomp pdo
                let okHandler = (2s >= w0.ArityIn) && ((w0.ArityIn = w0.ArityOut) || (w0.Aborted))
                if not okHandler then
                    precomp <| Halt (Value.variant "env-arity" p0)
                else 
                    let env' = Env(pw', pd') 
                    let bEff = d0.Effectful && w0.Effectful
                    let envLim = limAddStack 1s { d0 with Effectful = bEff }
                    struct(envLim, env')
            | Prog (anno, pdo) ->
                let struct(pLim, pdo') = precomp pdo
                cacheLim (struct(pLim, Prog(anno, pdo')))
            | _ ->
                precomp <| Halt (Value.variant "prog-inval" p0)

        and precompSeq l xs ys =
            // short-circuit aborted sequences.
            // also set arity out to zero in these cases.
            // (but we can still evaluate for effects.)
            if l.Aborted then struct( { l with ArityOut = 0s }, xs ) else
            match ys with
            | Leaf -> // completed
                struct(l, xs)
            | Rope.ViewL struct(op, ys') ->
                let struct(opLim, op') = precomp op
                let l' = limSeq l opLim
                let xs' = joinSeqs xs (asSeq op') // minor optimizations
                precompSeq l' xs' ys'
            | _ -> 
                // unreachable (in theory)
                failwith "invalid rope"

        let (|ProgLim|) (p:Program) =
            match p with
            | Prog (RecL [lLim] ([ValueSome (Lim lim)], anno'), p') ->
                // cached limit
                if isUnit anno' 
                    then (p', lim)
                    else (Prog(anno', p'), lim)
            | _ ->
                // recompute limit (shouldn't appear if caching is good)
                printfn "(debug) recomputing limits" 
                let struct(lim, p') = precomp p
                (p', lim)

        module Emit = 
            // some helper utils to favor shorter opcodes
            // (I should probably find a library for this)
            let ldarga (n : int16) (il : ILGenerator) =
                if (n < 256s) 
                    then il.Emit(OpCodes.Ldarga_S, uint8 n)
                    else il.Emit(OpCodes.Ldarga, n)

            let ldarg (n : int16) (il : ILGenerator) =
                if (n = 0s) then il.Emit(OpCodes.Ldarg_0) else
                if (n = 1s) then il.Emit(OpCodes.Ldarg_1) else
                if (n = 2s) then il.Emit(OpCodes.Ldarg_2) else
                if (n = 3s) then il.Emit(OpCodes.Ldarg_3) else
                if (n < 256s) then il.Emit(OpCodes.Ldarg_S, uint8 n) else
                il.Emit(OpCodes.Ldarg, n)

            let ldloca (v : LocalBuilder) (il : ILGenerator) =
                if v.LocalIndex < 256 
                    then il.Emit(OpCodes.Ldloca_S, v)
                    else il.Emit(OpCodes.Ldloca, v)

            let ldloc (v : LocalBuilder) (il : ILGenerator) =
                let n = v.LocalIndex
                if (n = 0) then il.Emit(OpCodes.Ldloc_0) else
                if (n = 1) then il.Emit(OpCodes.Ldloc_1) else
                if (n = 2) then il.Emit(OpCodes.Ldloc_2) else
                if (n = 3) then il.Emit(OpCodes.Ldloc_3) else
                if (n < 256) then il.Emit(OpCodes.Ldloc_S, v) else
                il.Emit(OpCodes.Ldloc, v)

            let ldc_i4 (n : int) (il : ILGenerator) =
                if (n = -1) then il.Emit(OpCodes.Ldc_I4_M1) else
                if (n =  0) then il.Emit(OpCodes.Ldc_I4_0) else
                if (n =  1) then il.Emit(OpCodes.Ldc_I4_1) else
                if (n =  2) then il.Emit(OpCodes.Ldc_I4_2) else
                if (n =  3) then il.Emit(OpCodes.Ldc_I4_3) else
                if (n =  4) then il.Emit(OpCodes.Ldc_I4_4) else
                if (n =  5) then il.Emit(OpCodes.Ldc_I4_5) else
                if (n =  6) then il.Emit(OpCodes.Ldc_I4_6) else
                if (n =  7) then il.Emit(OpCodes.Ldc_I4_7) else
                if (n =  8) then il.Emit(OpCodes.Ldc_I4_8) else
                let isSmall = ((-128 <= n) && (n <= 127))
                if isSmall then il.Emit(OpCodes.Ldc_I4_S, int8 n) else
                il.Emit(OpCodes.Ldc_I4, n)

            let starg (n : int16) (il : ILGenerator) =
                if (n < 256s) 
                    then il.Emit(OpCodes.Starg_S, uint8 n)
                    else il.Emit(OpCodes.Starg, n)
            
            let stloc (v : LocalBuilder) (il : ILGenerator) =
                let n = v.LocalIndex
                if (n = 0) then il.Emit(OpCodes.Stloc_0) else
                if (n = 1) then il.Emit(OpCodes.Stloc_1) else
                if (n = 2) then il.Emit(OpCodes.Stloc_2) else
                if (n = 3) then il.Emit(OpCodes.Stloc_3) else
                if (n < 256) then il.Emit(OpCodes.Stloc_S, v) else
                il.Emit(OpCodes.Stloc, v)

        // F# does not make it convenient to obtain TypeInfo objects for 
        // byref parameters. I work around this by accessing parameter 
        // types. But it feels like a hack.
        type M =
            static member Op(a:byref<Value>, b:outref<Value>) : unit = ()

        let ty_ValueByRef =
            typeof<M>.GetMethod("Op").GetParameters().[0].ParameterType

        let ty_ValueOutRef =
            typeof<M>.GetMethod("Op").GetParameters().[1].ParameterType

        let ty_Value = 
            typeof<Value>
        
        // Because programs are statically compiled with their effect context,
        // we may need to recompile methods for different contexts. Exception is
        // if we know the method is pure.
        type EffMethod = string
        type MethodName = string
        type MethodBody = (struct(Program * EffMethod)) // program and current effect method
        type MethodDict = System.Collections.Generic.Dictionary<MethodBody, MethodName>
        let noEffMethod = "(pure)" // not an actual method

        // 'static data' - static data as an array of values to assign
        // after the class is created (but before instances are created).
        type DataDict = System.Collections.Generic.Dictionary<Value, int>

        // global compile time env
        type CTE = 
            { 
                AsmB     : AssemblyBuilder  // assembly being constructed
                ModB     : ModuleBuilder    // the only module in the assembly
                TypB     : TypeBuilder      // the only type in the module
                FldEWrap : FieldBuilder     // object for top-level effects.
                FldData  : FieldBuilder     // an array for static data
                Statics  : DataDict         // tracks static data
                Methods  : MethodDict       // reusable subprograms
                NextId   : Ref<int>         // global id gen
            }

        let genID (cte:CTE) : int =
            let n = cte.NextId.Value
            assert(n < System.Int32.MaxValue) 
            cte.NextId.Value <- (n + 1)
            n

        let addStatic (cte:CTE) (v:Value) : int =
            match cte.Statics.TryGetValue(v) with
            | true, ix -> ix // reuse data
            | false, _ ->
                let ix = cte.Statics.Count
                cte.Statics.Add(v, ix)
                ix

        // create the type and initialize statics.
        let createType (cte : CTE) : System.Type =
            let staticData : Value array = Array.zeroCreate (cte.Statics.Count)
            for kvp in cte.Statics do
                staticData[kvp.Value] <- kvp.Key
            let newType = cte.TypB.CreateType()
            newType.GetField(cte.FldData.Name).SetValue(null, staticData)
            newType

        let loadEWrap (cte : CTE) (il : ILGenerator) =
            il.Emit(OpCodes.Ldarg_0) 
            il.Emit(OpCodes.Ldfld, cte.FldEWrap)

        let callEWrapTry (cte : CTE) (il : ILGenerator) =
            loadEWrap cte il
            il.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Try"))
        
        let callEWrapCommit (cte : CTE) (il : ILGenerator) =
            loadEWrap cte il
            il.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Commit"))

        let callEWrapAbort (cte : CTE) (il : ILGenerator) =
            loadEWrap cte il
            il.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Abort"))



        // stack data is stored in either the arguments or a few extra
        // locals that are allocated at the start of a method. (The 
        // main alternative is to move everything into locals.)
        type SP =
            | UsingArg of int16
            | LocalVar of LocalBuilder
        type SC = int // number of items currently on data stack

        let loadSPAddr (sp : SP) (il : ILGenerator) =
            match sp with
            | UsingArg n -> Emit.ldarga n il
            | LocalVar v -> Emit.ldloca v il
        
        let loadSPVal (sp : SP) (il : ILGenerator) =
            match sp with
            | UsingArg n -> Emit.ldarg n il
            | LocalVar v -> Emit.ldloc v il

        let storeSPVal (sp : SP) (il : ILGenerator) =
            match sp with
            | UsingArg n -> Emit.starg n il
            | LocalVar v -> Emit.stloc v il

        // extra effect handler context
        type ECX =
            { EffMethod : EffMethod // method to call upon 'eff' (toplevel via 'eff0')
            ; EffState  : FieldBuilder list // internal state to save on transactions
            ; IOState   : bool   // need to save external state on transactions?
            }
        let eff0 = "(toplevel)" // special 'inlined' EffMethod

        // stable method construction context
        type MCX =
            { CTE       : CTE                   // extends this global context
            ; Lim       : Lim                   // computed
            ; OnFail    : Label                 // target on failure
            ; IL        : ILGenerator           // output
            ; Stack     : SP array              // args and locals allocated for stack
            ; ECX       : ECX
            }

        let argsFromArity (mcx:MCX) (arIn : int) (arOut : int) (sc : SC) : SC =
            let sc' = sc - arIn + arOut
            assert((arIn >= 0) && (arOut >= 0) && (sc >= arIn)
                && (mcx.Stack.Length >= max sc sc'))
            for ix in 1 .. arIn do
                loadSPVal (mcx.Stack[sc - ix]) (mcx.IL)
            for ix in 1 .. arOut do
                loadSPAddr (mcx.Stack[sc' - ix]) (mcx.IL)
            sc'
                
        // Helper ops are needed because it's difficult to reference F#
        // functions directly
        type HelperOps =
            static member OpEq(a : Value, b : Value) : bool =
                Value.eq a b

            static member OpGet(p : Value, r : Value, result : outref<Value>) : bool =
                match p with
                | Bits k ->
                    match record_lookup k r with
                    | ValueSome v ->
                        result <- v
                        true
                    | ValueNone -> false
                | _ -> raise (RTError(lTypeError))

            static member OpPut(p : Value, r : Value, v : Value, result : byref<Value>) : unit =
                match p with
                | Bits k -> 
                    result <- record_insert k v r
                | _ -> raise (RTError(lTypeError))
            
            static member OpDel(p : Value, r : Value, result : byref<Value>) : unit =
                match p with
                | Bits k ->
                    result <- record_delete k r
                | _ -> raise (RTError(lTypeError))

            static member OpHalt(eMsg : Value) : unit =
                raise (RTError(eMsg))
                
            static member DebugPrintStart() =
                printfn "(debug) STACK"
            static member DebugPrint(ix:int, v:Value) : unit =
                printfn "(debug) [%d] %s" ix (Value.prettyPrint v)

        let debugPrintStack (mcx:MCX) (sc:SC) = 
            mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DebugPrintStart"))
            for ix in 1 .. sc do
                Emit.ldc_i4 ix (mcx.IL)
                loadSPVal (mcx.Stack[sc - ix]) (mcx.IL)
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DebugPrint"))

        let rec compileOp (mcx:MCX) (sc:SC) (p0:Program) : SC =
            match p0 with
            | PSeq (List lP) -> 
                // compileOp signature was arranged for this to work.
                Rope.fold (compileOp mcx) sc (lP)
            | Data v -> 
                let ix = addStatic (mcx.CTE) v
                mcx.IL.Emit(OpCodes.Ldsfld, mcx.CTE.FldData)
                Emit.ldc_i4 ix (mcx.IL)
                mcx.IL.Emit(OpCodes.Ldelem, typeof<Value>)
                storeSPVal (mcx.Stack[sc]) mcx.IL
                (sc + 1)
            | Cond (ProgLim(c, cLim), a, b) -> 
                // this is complicated, leave for later
                failwith "todo: cond"
                (*
                let lblEndCond = il.DefineLabel()
                let lblOnCondFail = il.DefineLabel()
                // first run the cond
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblOnCondFail il  
                // if we reach this point, we're on the true path
                // but our failure destination changes.
                rtCall0_ cte "TXCommit" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblEndCond)
                // failure path
                il.MarkLabel(lblOnCondFail)
                rtCall0_ cte "TXAbort" il
                compileOp cte b lblFail il
                il.MarkLabel(lblEndCond)
                *)
            | Dip p ->
                let il = mcx.IL
                assert(sc > 0)
                il.BeginScope()
                let varTmp = il.DeclareLocal(typeof<Value>)
                // hide top stack value in varTmp, run 'p', restore top stack val
                loadSPVal (mcx.Stack[sc - 1]) il
                Emit.stloc varTmp il
                let sc' = compileOp mcx (sc - 1) p
                assert(sc' < mcx.Stack.Length)
                Emit.ldloc varTmp il
                storeSPVal (mcx.Stack[sc']) il
                il.EndScope()
                (sc' + 1)
            | Stem lCopy U ->
                assert((sc > 0) && (sc < mcx.Stack.Length))
                loadSPVal (mcx.Stack[sc - 1]) (mcx.IL)
                storeSPVal (mcx.Stack[sc]) (mcx.IL)
                (sc + 1)
            | Stem lSwap U -> 
                assert(sc >= 2)
                // using CLR stack temporarily
                loadSPVal (mcx.Stack[sc - 2]) (mcx.IL)
                loadSPVal (mcx.Stack[sc - 1]) (mcx.IL)
                storeSPVal (mcx.Stack[sc - 2]) (mcx.IL)
                storeSPVal (mcx.Stack[sc - 1]) (mcx.IL)
                sc
            | Stem lDrop U ->
                // I could clear the location to provide early gc opportunity
                // but it isn't essential to do so. 
                assert(sc >= 1)
                (sc - 1)
            | Stem lEq U -> 
                let sc' = argsFromArity mcx 2 0 sc
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpEq"))
                mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                sc'
            | Stem lGet U -> 
                let sc' = argsFromArity mcx 2 1 sc
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpGet"))
                mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                sc'
            | Stem lPut U -> 
                let sc' = argsFromArity mcx 3 1 sc
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpPut"))
                sc' 
            | Stem lDel U -> 
                let sc' = argsFromArity mcx 2 1 sc
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpDel"))
                sc'
            | While (c, a) -> 
                failwith "todo: while loops"
                (*
                let lblRepeat = il.DefineLabel()
                let lblExitLoop = il.DefineLabel()
                il.MarkLabel(lblRepeat)
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblExitLoop il
                rtCall0_ cte "TXCommit" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblRepeat)
                il.MarkLabel(lblExitLoop)
                rtCall0_ cte "TXAbort" il
                *)
            | Until (c, a) ->
                failwith "todo: until loops"
                (*
                let lblRepeat = il.DefineLabel()
                let lblContLoop = il.DefineLabel()
                let lblExitLoop = il.DefineLabel()
                il.MarkLabel(lblRepeat)
                rtCall0_ cte "TXBegin" il
                compileOp cte c lblContLoop il
                il.Emit(OpCodes.Br, lblExitLoop)
                il.MarkLabel(lblContLoop)
                rtCall0_ cte "TXAbort" il
                compileOp cte a lblFail il
                il.Emit(OpCodes.Br, lblRepeat)
                il.MarkLabel(lblExitLoop)
                rtCall0_ cte "TXCommit" il
                *) 
            | Stem lEff U -> 
                if eff0 = mcx.ECX.EffMethod then
                    // call the toplevel effect handler
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    mcx.IL.Emit(OpCodes.Ldfld, mcx.CTE.FldEWrap)
                    let sc' = argsFromArity mcx 1 1 sc
                    mcx.IL.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Eff"))
                    mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                    assert(sc = sc')
                    sc'
                else
                    // call user-defined effect handler
                    let m = mcx.CTE.TypB.GetMethod(mcx.ECX.EffMethod)
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    let sc' = argsFromArity mcx 1 1 sc
                    mcx.IL.Emit(OpCodes.Call, m)
                    mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                    assert(sc = sc')
                    sc'
            | Env (w, p) -> 
                failwith "todo: env"
                (*
                // compile handler as a subroutine
                let wSub = newSub cte
                do
                    let ilW = wSub.GetILGenerator()
                    let wFail = ilW.DefineLabel()
                    rtCall0_ cte "EnvPop" ilW
                    compileOp cte w wFail ilW
                    rtCall0_ cte "EnvPush" ilW
                    emitSubExit wFail ilW
                // to ensure handlers are in 'arg_1' position, we'll represent
                // the called program as a subprogram.
                let pSub = newSub cte
                compile cte p (pSub.GetILGenerator()) 

                rtCall0_ cte "EnvPush" il   
                il.Emit(OpCodes.Ldarg_0) // 'this' arg for this.pSub(handlers) call
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Ldftn, wSub) // adding address of wSub to head of handlers list
                il.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("PushHandler"))
                il.Emit(OpCodes.Call, pSub) // returns bool pass/fail
                rtCall0_ cte "EnvPop" il
                il.Emit(OpCodes.Brfalse, lblFail) // the 'pSub' call may fail
                *)
            | Stem lFail U ->
                mcx.IL.Emit(OpCodes.Br, mcx.OnFail)
                sc
            | Stem lHalt eMsg -> 
                let ixMsg = addStatic (mcx.CTE) eMsg
                mcx.IL.Emit(OpCodes.Ldsfld, mcx.CTE.FldData)
                Emit.ldc_i4 ixMsg mcx.IL
                mcx.IL.Emit(OpCodes.Ldelem, typeof<Value>)
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpHalt"))
                sc
            | Prog (anno, p) ->
                compileAnno mcx sc anno p
            | _ ->
                // if an invalid program reaches our compiler, just treat it as a halt.
                let eMsg = Value.variant "unhandled" p0 
                compileOp mcx sc (Value.variant "halt" eMsg)  
        and compileAnno (mcx : MCX) (sc : SC) (anno : Value) (p : Program) : SC =
            // TODO: handle acceleration (at least)
            compileOp mcx sc p
            (*
            // not well factored at the moment due to profiler and
            // continuations being passed together. 
            match anno with
            | Record ["prof"] struct([ValueSome profOptions],anno') ->
                // I could feasibly allocate and handle the stopwatch, but dealing with
                // the profile registers is a pain. Might need to use static fields same
                // as for the value fields. 
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["stow"] struct([ValueSome vOpts], anno') ->
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["memo"] struct([ValueSome memoOpts], anno') ->
                // nop for now
                compileAnno cte anno' p lblFail il
            | Record ["accel"] struct([ValueSome vModel], anno') ->
                let p' = Prog(anno', p)
                match vModel with
                | Variant "opt" vModel' ->
                    compileAccel cte true  vModel' p' lblFail il
                | _ ->
                    compileAccel cte false vModel  p' lblFail il
            | _ -> // ignoring other annotations 
                // handle as a reusable subroutine call
                let pSub = addSub cte p (compile cte p)
                il.Emit(OpCodes.Ldarg_0) // this
                il.Emit(OpCodes.Ldarg_1) // effects handlers
                il.Emit(OpCodes.Call, pSub) // call the reusable method
                il.Emit(OpCodes.Brfalse, lblFail) // handle failure
        and compileAccel cte bOpt vModel p lblFail il =
            let inline accelFail () =
                // if acceleration is unavailable and optional, 
                // just run p directly.
                if bOpt then compileOp cte p lblFail il else
                // otherwise, to avoid silent performance degradation,
                // halt and indicate the accelerator.
                let eMsg = Value.variant "accel" vModel
                compileOp cte (Value.variant "halt" eMsg) lblFail il 
            let emitPartialAccel (rtMethod : string) =
                // for cases where we need to handle 'accelFail'. 
                // Also handles OK/Fail results.
                let subNonAccel = addSub cte p (compile cte p)
                let lblFin = il.DefineLabel()
                il.BeginScope()
                let result = il.DeclareLocal(typeof<int>)
                rtCall0 cte rtMethod il // call accelerated method
                il.Emit(OpCodes.Dup)
                il.Emit(OpCodes.Stloc, result)  
                il.Emit(OpCodes.Brfalse, lblFin) // on OK, skip the rest
                assert(opFail = 1) 
                il.Emit(OpCodes.Ldc_I4_1)
                il.Emit(OpCodes.Ldloc, result)
                il.Emit(OpCodes.Beq, lblFail) // on Fail, jump to failure 
                il.EndScope()
                // assuming accelFail; call the subroutine!
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Call, subNonAccel)
                il.Emit(OpCodes.Brfalse, lblFail)
                il.MarkLabel(lblFin)
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    rtCall0_ cte "AccelListPushl" il
                | Value.Variant "pushr" U ->
                    rtCall0_ cte "AccelListPushr" il
                | Value.Variant "popl" U ->
                    rtCall0b cte "AccelListPopl" lblFail il
                | Value.Variant "popr" U -> 
                    rtCall0b cte "AccelListPopr" lblFail il
                | Value.Variant "append" U -> 
                    rtCall0_ cte "AccelListAppend" il
                | Value.Variant "verify" U -> 
                    rtCall0b cte "AccelListVerify" lblFail il
                | Value.Variant "length" U -> 
                    rtCall0_ cte "AccelListLength" il
                | Value.Variant "take" U -> 
                    rtCall0b cte "AccelListTake" lblFail il
                | Value.Variant "skip" U -> 
                    rtCall0b cte "AccelListSkip" lblFail il
                | Value.Variant "item" U -> 
                    rtCall0b cte "AccelListItem" lblFail il
                | _ -> accelFail ()
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    rtCall0b cte "AccelBitsVerify" lblFail il
                | Value.Variant "negate" U -> 
                    rtCall0_ cte "AccelBitsNegate" il
                | Value.Variant "reverse-append" U ->
                    rtCall0_ cte "AccelBitsReverseAppend" il
                //| Value.Variant "length" U -> accelFail ()
                | _ -> accelFail ()
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    emitPartialAccel "AccelSmallIntAdd"
                | Value.Variant "mul" U ->
                    emitPartialAccel "AccelSmallIntMul"
                //| Value.Variant "sub" U -> accelFail ()
                //| Value.Variant "divmod" U -> accelFail ()
                | Value.Variant "increment" U -> 
                    emitPartialAccel "AccelSmallIntIncrement"
                | Value.Variant "decrement" U -> 
                    emitPartialAccel "AccelSmallIntDecrement"
                | Value.Variant "gt" U -> 
                    emitPartialAccel "AccelSmallIntGT"
                | Value.Variant "gte" U -> 
                    emitPartialAccel "AccelSmallIntGTE"
                | _ -> accelFail ()
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    emitPartialAccel "AccelSmallNatAdd"
                | Value.Variant "sub" U -> 
                    emitPartialAccel "AccelSmallNatSub"
                | Value.Variant "mul" U -> 
                    emitPartialAccel "AccelSmallNatMul"
                | Value.Variant "divmod" U ->
                    emitPartialAccel "AccelSmallNatDivMod"
                | Value.Variant "gt" U -> 
                    emitPartialAccel "AccelSmallNatGT"
                | Value.Variant "gte" U -> 
                    emitPartialAccel "AccelSmallNatGTE"
                | _ -> accelFail ()
            | _ -> accelFail ()

        *)
        and compileMethod (methodName:string) (cte:CTE) (ecx:ECX) (lim:Lim) (p0:Program) =
            let methodArgs = 
                let argsIn = Array.create (int lim.ArityIn) ty_Value
                let argsOut = Array.create (int lim.ArityOut) ty_ValueOutRef
                Array.append argsIn argsOut
            let methodAttr = MethodAttributes.Public ||| MethodAttributes.Final
            let m = cte.TypB.DefineMethod(methodName, methodAttr, 
                        CallingConventions.Standard,
                        typeof<bool>, methodArgs)
            let il = m.GetILGenerator()
            // We'll need to declare a few things before adding program behavior.
            //  - final failure target
            //  - extra variables for data stack 
            let lblFail = il.DefineLabel()
            let stackReg = 
                assert(lim.MaxStack >= lim.ArityIn)
                let inputArgs = 
                    // top of stack is arg 1, but highest 'sc'.
                    // so I need to reorder things logically.
                    let arr = Array.zeroCreate (int lim.ArityIn)
                    for ix in 1 .. arr.Length do
                        arr[arr.Length - ix] <- UsingArg (int16 ix)
                    arr
                let extraSpace =
                    let arr = Array.zeroCreate (int (lim.MaxStack - lim.ArityIn))
                    for ix in 1 .. arr.Length do
                        let v = il.DeclareLocal(typeof<Value>)
                        arr[ix - 1] <- LocalVar v
                    arr
                Array.append inputArgs extraSpace
            let mcx =   
                { CTE = cte
                ; Lim = lim
                ; OnFail = lblFail
                ; IL = il
                ; Stack = stackReg 
                ; ECX = ecx
                }
            let sc' = compileOp mcx (int lim.ArityIn) p0
            assert(sc' = int lim.ArityOut)
            // (on success path)
            // move results into output parameters
            for ix in 1s .. lim.ArityOut do
                Emit.ldarg (ix + lim.ArityIn) il        // address
                loadSPVal (mcx.Stack[sc' - int ix]) il  // value from stack
                il.Emit(OpCodes.Stobj, typeof<Value>)   // write
            // return true 
            il.Emit(OpCodes.Ldc_I4_1)
            il.Emit(OpCodes.Ret)
            // (on failure) just return false
            il.MarkLabel(lblFail)
            il.Emit(OpCodes.Ldc_I4_0)
            il.Emit(OpCodes.Ret)

        // initCTE prepares an initial CTE with a type that is constructed by
        // giving it the Runtime argument. The profile is shared across all 
        // instances of this type, currently.
        let initCTE () : CTE =
            // for now, all named the same. Might need to distinguish later
            // to support debugging of exception stacks, but uncertain.
            let asmName = AssemblyName("Glas.ProgEval.DynCSM")
            let asmOp = AssemblyBuilderAccess.RunAndCollect
            let asmB = AssemblyBuilder.DefineDynamicAssembly(asmName, asmOp)
            let modB = asmB.DefineDynamicModule("M")
            let typeAttr = TypeAttributes.Public ||| TypeAttributes.Sealed
            let typB = modB.DefineType("P", typeAttr)
            let fldAttrEW = FieldAttributes.Private ||| FieldAttributes.InitOnly
            let fldEW = typB.DefineField("IO", typeof<EWrap>, fldAttrEW)
            let fldAttrData = FieldAttributes.Public ||| FieldAttributes.Static
            let fldData = typB.DefineField("StaticData", typeof<Value array>, fldAttrData)

            // the object constructor receives a wrapped EffHandler.
            let ctor = typB.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    [| typeof<EWrap> |])
            do
                let il = ctor.GetILGenerator()
                il.Emit(OpCodes.Ldarg_0)
                il.Emit(OpCodes.Ldarg_1)
                il.Emit(OpCodes.Stfld, fldEW)
                il.Emit(OpCodes.Ret)
            { AsmB = asmB
            ; ModB = modB
            ; TypB = typB
            ; FldEWrap = fldEW
            ; FldData = fldData
            ; Methods = new MethodDict()
            ; Statics = new DataDict()
            ; NextId = ref 1000
            }

        let mainMethod = "EvalProg"

        let compiled (p : Program) : Effects.IEffHandler -> Value list -> Value list option =
            let struct(lim, p') = precomp p
            let progType =             
                let cte = initCTE ()
                let ecx0 = 
                    { EffMethod = eff0
                    ; EffState = []
                    ; IOState = lim.Effectful 
                    }
                compileMethod mainMethod cte ecx0 lim p'
                createType cte

            fun (io : Effects.IEffHandler) (ds : Value list) ->
                if (int lim.ArityIn) > (List.length ds) then
                    Effects.logError io "underflow"
                    None
                else
                    let ewrap = EWrap(io) // outside of 'try' to support unwind
                    try 
                        let ctorArgs : obj array = [| ewrap |]
                        let invokeArgs : obj array = 
                            let inArgs = ds |> List.take (int lim.ArityIn) |> Array.ofList |> Array.map box
                            let outArgs = Array.create (int lim.ArityOut) (box (Value.symbol "undef"))
                            Array.append inArgs outArgs
                        let progInst = System.Activator.CreateInstance(progType, ctorArgs)
                        let invokeAttr = BindingFlags.DoNotWrapExceptions
                        let pass = progType.GetMethod(mainMethod).Invoke(progInst, invokeAttr, null, invokeArgs, null)
                        if not (unbox<bool>(pass)) then None else
                        let results = invokeArgs |> Array.skip (int lim.ArityIn) |> Array.map (unbox<Value>)
                        assert((int lim.ArityOut) = (Array.length results))
                        let resultList = Array.foldBack (fun x xs -> (x :: xs)) results (List.skip (int lim.ArityIn) ds)
                        Some resultList
                    with
                    | RTError(vMsg) ->
                        ewrap.Unwind()
                        Effects.logErrorV io "runtime error" vMsg
                        None



        let eval (p : Program) : Effects.IEffHandler -> Value list -> Value list option =
            let lazyCompile = lazy (compiled p)
            fun io ds -> lazyCompile.Force() io ds


    // OTHER OPTIONS:
    //  
    // - Lightweight Program -> Program optimizing pass.
    // - Incrementally move evaluation logic into glas modules:
    //   - optimizing pass
    //   - compiler to register machine
    //

    let defaultEval = FinallyTaglessInterpreter.eval
    let private selectEval () =
        match System.Environment.GetEnvironmentVariable("GLAS_EVAL") with
        | s when System.String.IsNullOrEmpty(s) -> defaultEval
        | "Interpreter" | "I" -> Interpreter.eval
        | "FinallyTaglessInterpreter" | "FTI" -> FinallyTaglessInterpreter.eval
        | "CompiledInterpreter" | "CI" -> CompiledInterpreter.eval
        | "CompiledStackMachine" | "CSM" -> CompiledStackMachine.eval
        | s ->
            failwithf "unrecognized evaluator %s" s
    let configuredEval = lazy (selectEval ())

    /// The value list argument is top of stack at head of list.
    /// Returns ValueNone if the program either halts or fails. 
    let eval (p:Program) (io:Effects.IEffHandler) : (Value list) -> (Value list option) =
        configuredEval.Force() p io

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

