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
                    rt.DataStack <- ds'
                    if (m > n)
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            // vnone - inputs are not small nats; vsome (result) otherwise 
            member rt.AccelSmallNatGTE() : Status  =
                match rt.DataStack with
                | ((Nat64 n)::(Nat64 m)::ds') ->
                    rt.DataStack <- ds'
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
                    rt.DataStack <- ds'
                    if (m > n) 
                        then opOK
                        else opFail
                | (_::_::_) -> accelFail
                | _ -> rt.Underflow()

            member rt.AccelSmallIntGTE() : Status  =
                match rt.DataStack with
                | ((Int64 n)::(Int64 m)::ds') ->
                    rt.DataStack <- ds'
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

    module FinallyTaglessInterpreter =
        // compiles everything into continuations to avoid re-parsing
        // the program or directly observing results at runtime. Over
        // 10x faster than a direct interpreter.

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

    module CompiledStackMachine =
        // Uses dotnet reflection to build a dynamic assembly. The data
        // stack is represented using local vars within a method. Results
        // (other than pass/fail) are returned via outref parameters. 
        //
        // This avoids a lot of allocations at runtime, but its performance
        // isn't better than FinallyTagless in most cases. For tight long
        // running loops, it can perform roughly twice as well. For short
        // tasks, overheads dominate and this performs worse.
        //
        // Overall, this is essentially a failed experiment.

        open System.Reflection
        open System.Reflection.Emit
        open Value
        open ProgVal

        //
        // It turns out this hardly makes a difference for performance. It is
        // the same performance as FinallyTaglessInterpreter for several tests.
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
                let fn (struct(lim, ops')) op =
                    if lim.Aborted then struct(lim, ops') else
                    let struct(limOp, op') = precomp op
                    struct(limSeq lim limOp, Rope.snoc ops' op')
                let struct(lim, lP') = Rope.fold fn (struct(limOfArity 0 0, Rope.empty)) lP
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
                let okBal = (a0.ArityIn = a0.ArityOut) || (a0.Aborted)
                if okBal then
                    let loop' = Until(pc', pa')
                    let arIn = max c0.ArityIn a0.ArityIn
                    let lim' = 
                        { ArityIn  = arIn
                        ; ArityOut = arIn - c0.ArityIn + c0.ArityOut
                        ; MaxStack = max (arIn - c0.ArityIn + c0.MaxStack)
                                         (arIn - a0.ArityIn + a0.MaxStack)
                        ; Aborted = c0.Aborted // aborted or an infinite loop
                        ; Effectful = c0.Effectful || a0.Effectful
                        }
                    struct(lim', loop')
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

        let methodArgsFromArity (arIn : int) (arOut : int) : System.Type array = 
            assert((arIn >= 0) && (arOut >= 0))
            let argsIn = Array.create arIn ty_Value
            let argsOut = Array.create arOut ty_ValueOutRef
            Array.append argsIn argsOut

        // 'static data' - static data as an array of values to assign
        // after the class is created (but before instances are created).
        type DataDict = System.Collections.Generic.Dictionary<Value, int>
        type HandlerName = string
        type Handlers = System.Collections.Generic.Dictionary<HandlerName, struct(MethodBuilder * FieldBuilder)>

        // support for subroutines. Subroutines are compiled in a static effect
        // context, so the extra string arg indicates the effect context.
        type SubProgs = System.Collections.Generic.Dictionary<struct(Program * string), MethodBuilder>

        // global compile time env
        type CTE = 
            { 
                AsmB     : AssemblyBuilder  // assembly being constructed
                ModB     : ModuleBuilder    // the only module in the assembly
                TypB     : TypeBuilder      // the only type in the module
                FldEWrap : FieldBuilder     // object for top-level effects.
                FldData  : FieldBuilder     // an array for static data
                Statics  : DataDict         // tracks static data
                Handlers : Handlers         // effects handlers
                SubProgs : SubProgs         // reusable subroutines
            }

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

        // Originally I used a choice of LocalBuilder and set/load on args.
        // But the current dotnet performs MUCH better using just local vars.
        type SP = LocalBuilder
        type SC = int // number of items currently on data stack

        let inline loadSPAddr (sp : SP) (il : ILGenerator) =
            Emit.ldloca sp il
        
        let inline loadSPVal (sp : SP) (il : ILGenerator) =
            Emit.ldloc sp il

        let inline storeSPVal (sp : SP) (il : ILGenerator) =
            Emit.stloc sp il

        // extra effect handler context
        type ECX =
            { EffMethod : MethodBuilder option
            ; EffState  : FieldBuilder list // internal state to save on transactions
            ; IOState   : bool   // need to save external state on transactions?
            }

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

        // for partial acceleration, we may need to fallback
        // to the original program when inputs are outside the
        // accepted range.
        type PartialAccel = int
        let accelPass = 0
        let accelFail = 1
        let accelFallback = -1 

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

            static member OpPut(p : Value, r : Value, v : Value, result : outref<Value>) : unit =
                match p with
                | Bits k -> 
                    result <- record_insert k v r
                | _ -> raise (RTError(lTypeError))
            
            static member OpDel(p : Value, r : Value, result : outref<Value>) : unit =
                match p with
                | Bits k ->
                    result <- record_delete k r
                | _ -> raise (RTError(lTypeError))

            static member OpHalt(eMsg : Value) : unit =
                raise (RTError(eMsg))

            static member AccelListPushl(l:Value, v:Value, result : outref<Value>) : unit =
                match l with
                | (List ll) ->
                    result <- Value.ofTerm (Rope.cons v ll)
                | _ -> 
                    raise (RTError(lTypeError))

            static member AccelListPushr(v:Value, l:Value, result : outref<Value>) : unit =
                match l with
                | (List ll) ->
                    result <- Value.ofTerm (Rope.snoc ll v)
                | _ -> 
                    raise (RTError(lTypeError))

            static member AccelListPopl(l:Value, l' : outref<Value>, v : outref<Value>) : bool =
                match l with
                | (List ll) ->
                    match ll with
                    | Rope.ViewL(struct(vv, ll')) ->
                        l' <- Value.ofTerm ll'
                        v  <- vv
                        true
                    | _ -> false
                | _ -> 
                    raise (RTError(lTypeError))

            static member AccelListPopr(l:Value, v : outref<Value>, l' : outref<Value>) : bool =
                match l with
                | (List ll) ->
                    match ll with
                    | Rope.ViewR(struct(ll', vv)) ->
                        v  <- vv
                        l' <- Value.ofTerm ll'
                        true
                    | _ -> false
                | _ -> 
                    raise (RTError(lTypeError))
            
            static member AccelListAppend(r:Value, l:Value, lr : outref<Value>) : unit =
                match l, r with
                | List ll, List rr ->
                    lr <- Value.ofTerm (Rope.append ll rr)
                | _ ->
                    raise (RTError(lTypeError))

            static member AccelListVerify(l:Value, l':outref<Value>) : bool =
                match l with
                | List ll ->
                    l' <- Value.ofTerm ll
                    true
                | _ -> 
                    false

            static member AccelListLength(l:Value, n:outref<Value>) : unit =
                match l with
                | List ll ->
                    n <- Value.ofNat (Rope.len ll) 
                | _ ->
                    raise (RTError(lTypeError))

            static member AccelListTake(n:Value, l:Value, l':outref<Value>) : bool =
                match n, l with
                | Nat64 nn, List ll ->
                    if nn > Rope.len ll then false else
                    l' <- Value.ofTerm (Rope.take nn ll)
                    true
                | _ ->
                    raise (RTError(lTypeError))

            static member AccelListSkip(n:Value, l:Value, l':outref<Value>) : bool =
                match n, l with
                | Nat64 nn, List ll ->
                    if nn > Rope.len ll then false else
                    l' <- Value.ofTerm (Rope.drop nn ll)
                    true
                | _ ->
                    raise (RTError(lTypeError))

            static member AccelListItem(n:Value, l:Value, v:outref<Value>) : bool =
                match n, l with
                | Nat64 ix, List t ->
                    if (ix >= Rope.len t) then false else
                    v <- Rope.item ix t
                    true
                | _ ->
                    raise (RTError(lTypeError))

            static member AccelBitsVerify(b:Value, b':outref<Value>) : bool =
                if not (isBits b) then false else
                b' <- b // in theory, this could be an accelerated rep.
                true

            static member AccelBitsNegate(b:Value, b':outref<Value>) : unit =
                if not (isBits b) then raise (RTError(lTypeError)) else 
                b' <- Accel.bits_negate b

            static member AccelBitsReverseAppend(b:Value, acc:Value, b':outref<Value>) : unit =
                if not (isBits b && isBits acc) then raise (RTError(lTypeError)) else
                b' <- Accel.bits_reverse_append acc b

            static member AccelBitsLength(b:Value, n:outref<Value>) : unit =
                if not (isBits b) then raise (RTError(lTypeError)) else
                n <- Value.ofInt (int64 (Accel.bits_len b))
            
            static member AccelSmallIntAdd(n:Value, m:Value, sum:outref<Value>) : PartialAccel =
                match n, m with
                | Int64 ni, Int64 mi ->
                    try 
                        sum <- Value.ofInt <| Microsoft.FSharp.Core.Operators.Checked.(+) mi ni
                        accelPass
                    with
                    | :? System.OverflowException -> accelFallback
                | _ -> accelFallback

            static member AccelSmallIntMul(n:Value, m:Value, prod:outref<Value>) : PartialAccel =
                match n, m with
                | Int64 ni, Int64 mi ->
                    try 
                        prod <- Value.ofInt <| Microsoft.FSharp.Core.Operators.Checked.(*) mi ni
                        accelPass
                    with
                    | :? System.OverflowException -> accelFallback
                | _ -> accelFallback

            static member AccelSmallIntSub(n:Value, m:Value, diff:outref<Value>) : PartialAccel =
                match n, m with
                | Int64 ni, Int64 mi ->
                    try
                        diff <- Value.ofInt <| Microsoft.FSharp.Core.Operators.Checked.(-) mi ni
                        accelPass
                    with 
                    | :? System.OverflowException -> accelFallback
                | _ -> accelFallback
            
            static member AccelSmallIntIncrement(n:Value, n':outref<Value>) : PartialAccel =
                match n with
                | Int64 ni when ni < System.Int64.MaxValue ->
                    n' <- Value.ofInt (ni + 1L)
                    accelPass
                | _ -> accelFallback

            static member AccelSmallIntDecrement(n:Value, n':outref<Value>) : PartialAccel =
                match n with
                | Int64 ni when ni > System.Int64.MinValue ->
                    n' <- Value.ofInt (ni - 1L)
                    accelPass
                | _ -> accelFallback

            static member AccelSmallIntGT(n:Value, m:Value) : PartialAccel =
                match n, m with
                | Int64 ni, Int64 mi ->
                    if (mi > ni) 
                        then accelPass 
                        else accelFail
                | _ -> accelFallback

            static member AccelSmallIntGTE(n:Value, m:Value) : PartialAccel =
                match n, m with
                | Int64 ni, Int64 mi ->
                    if (mi >= ni) 
                        then accelPass
                        else accelFail
                | _ -> accelFallback

            static member AccelSmallNatAdd(n:Value, m:Value, sum:outref<Value>) : PartialAccel =
                match n, m with
                | Nat64 ni, Nat64 mi when ((System.UInt64.MaxValue - ni) >= mi) ->
                    sum <- Value.ofNat (ni + mi)
                    accelPass
                | _ -> accelFallback
            
            static member AccelSmallNatSub(n:Value, m:Value, diff:outref<Value>) : PartialAccel =
                match n, m with
                | Nat64 ni, Nat64 mi ->
                    if (mi >= ni) then 
                        diff <- Value.ofNat (mi - ni)
                        accelPass
                    else
                        accelFail
                | _ -> accelFallback

            static member AccelSmallNatMul(n:Value, m:Value, prod:outref<Value>) : PartialAccel =
                match n, m with
                | Nat64 ni, Nat64 mi ->
                    try
                        prod <- Value.ofNat <| Microsoft.FSharp.Core.Operators.Checked.(*) mi ni
                        accelPass
                    with
                    | :? System.OverflowException -> accelFallback
                | _ -> accelFallback

            static member AccelSmallNatDivMod(divisor:Value, dividend:Value, 
                                              remainder:outref<Value>, quotient:outref<Value>) : PartialAccel =
                match divisor, dividend with
                | Nat64 nDivisor, Nat64 nDividend ->
                    if (0UL = nDivisor) then accelFail else
                    let struct(nQuot, nRem) = System.Math.DivRem(nDividend, nDivisor)
                    remainder <- Value.ofNat nRem
                    quotient <- Value.ofNat nQuot
                    accelPass
                | _ -> accelFallback

            static member AccelSmallNatGT(n:Value, m:Value) : PartialAccel =
                match n, m with
                | Nat64 nn, Nat64 mm ->
                    if (mm > nn)
                        then accelPass
                        else accelFail
                | _ -> accelFallback

            static member AccelSmallNatGTE(n:Value, m:Value) : PartialAccel =
                match n, m with
                | Nat64 nn, Nat64 mm ->
                    if (mm >= nn) 
                        then accelPass
                        else accelFail
                | _ -> accelFallback

            static member DebugPrintIxV(ix:int, v:Value) : unit =
                printfn "(debug) [%d] %s" ix (Value.prettyPrint v)

            static member DebugPrintStr(s:string) : unit =
                printfn "(debug) %s" s

        let debugPrintStr (mcx:MCX) (s:string) =
            mcx.IL.Emit(OpCodes.Ldstr, s)
            mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DebugPrintStr"))

        let debugPrintStack (mcx:MCX) (sc:SC) = 
            for ix in 1 .. sc do
                Emit.ldc_i4 ix (mcx.IL)
                loadSPVal (mcx.Stack[sc - ix]) (mcx.IL)
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("DebugPrintIxV"))

        // attempting to abstract out the transaction steps
        [<Struct>]
        type TXCC =
            { OnAbort : unit -> unit
            ; OnCommit : unit -> unit
            }

        let txBegin (mcx:MCX) (sc:SC) : TXCC =
            // save data for ECX
            let mutable onCommit : unit -> unit = id
            let mutable onAbort : unit -> unit = id
            if mcx.Lim.Effectful then
                // save anything we might change via effects
                // save external IO data
                if mcx.ECX.IOState then
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    mcx.IL.Emit(OpCodes.Ldfld, mcx.CTE.FldEWrap)
                    mcx.IL.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Try"))
                    onAbort <- onAbort << fun () ->
                        mcx.IL.Emit(OpCodes.Ldarg_0)
                        mcx.IL.Emit(OpCodes.Ldfld, mcx.CTE.FldEWrap)
                        mcx.IL.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Abort"))
                    onCommit <- onCommit << fun () ->
                        mcx.IL.Emit(OpCodes.Ldarg_0)
                        mcx.IL.Emit(OpCodes.Ldfld, mcx.CTE.FldEWrap)
                        mcx.IL.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Commit"))
                // save effect handler stack
                for effStFld in mcx.ECX.EffState do
                    let varTmp = mcx.IL.DeclareLocal(typeof<Value>)
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    mcx.IL.Emit(OpCodes.Ldfld, effStFld)
                    Emit.stloc varTmp mcx.IL
                    onAbort <- onAbort << fun () ->
                        mcx.IL.Emit(OpCodes.Ldarg_0)
                        Emit.ldloc varTmp mcx.IL
                        mcx.IL.Emit(OpCodes.Stfld, effStFld)
            let arSave = int mcx.Lim.ArityIn
            assert(sc >= arSave)
            for ix in 1 .. arSave do
                let sp = mcx.Stack[sc - ix]
                let varTmp = mcx.IL.DeclareLocal(typeof<Value>)
                loadSPVal sp mcx.IL
                Emit.stloc varTmp mcx.IL
                onAbort <- onAbort << fun () ->
                    Emit.ldloc varTmp mcx.IL
                    storeSPVal sp mcx.IL
            { OnCommit = onCommit; OnAbort = onAbort }
        
        let rec compileOp (mcx:MCX) (sc:SC) (p0:Program) : SC =
            // note: sc < 0 indicates computation has already aborted
            if (sc < 0) then sc else
            assert(sc <= mcx.Stack.Length)
            match p0 with
            | PSeq (List lP) -> 
                // compileOp signature was arranged for this to work.
                Rope.fold (compileOp mcx) sc (lP)
            | Data v -> 
                assert(sc < mcx.Stack.Length)
                let ix = addStatic (mcx.CTE) v
                mcx.IL.Emit(OpCodes.Ldsfld, mcx.CTE.FldData)
                Emit.ldc_i4 ix (mcx.IL)
                mcx.IL.Emit(OpCodes.Ldelem, typeof<Value>)
                storeSPVal (mcx.Stack[sc]) mcx.IL
                (sc + 1)
            | Cond (ProgLim(c, cLim), a, b) -> 
                // this is complicated, leave for later conditional
                mcx.IL.BeginScope()
                let lblCondEnd = mcx.IL.DefineLabel() // after success path
                let lblCondFail = mcx.IL.DefineLabel() // where to go on failure
                let mcxCond = { mcx with OnFail = lblCondFail; Lim = cLim;  } 
                let txcc = txBegin mcxCond sc
                let scC = compileOp mcxCond sc c
                assert(cLim.Aborted = (scC < 0))
                // fall through to success case
                txcc.OnCommit ()
                mcx.IL.Emit(OpCodes.Ldc_I4_1) // record success
                mcx.IL.Emit(OpCodes.Br, lblCondEnd)
                mcx.IL.MarkLabel(lblCondFail)
                txcc.OnAbort ()
                mcx.IL.Emit(OpCodes.Ldc_I4_0) // record failure
                mcx.IL.MarkLabel(lblCondEnd)
                mcx.IL.EndScope() // enable GC of saved data
                let lblSkipPass = mcx.IL.DefineLabel ()
                let lblSkipFail = mcx.IL.DefineLabel ()
                mcx.IL.Emit(OpCodes.Brfalse, lblSkipPass)
                let scA = compileOp mcx scC a
                mcx.IL.Emit(OpCodes.Br, lblSkipFail)
                mcx.IL.MarkLabel(lblSkipPass)
                let scB = compileOp mcx sc b
                mcx.IL.MarkLabel(lblSkipFail)
                assert((scA = scB) || (scA < 0) || (scB < 0))
                max scA scB
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
            | While (ProgLim(c, cLim), a) -> 
                let lblRepeat = mcx.IL.DefineLabel()
                let lblRunBody = mcx.IL.DefineLabel()
                let lblTerminate = mcx.IL.DefineLabel()
                mcx.IL.MarkLabel(lblRepeat)
                // conditional
                mcx.IL.BeginScope()
                let lblOnCondFail = mcx.IL.DefineLabel()
                let mcxCond = { mcx with Lim = cLim; OnFail = lblOnCondFail }
                let txcc = txBegin mcxCond sc
                let scC = compileOp mcxCond sc c 
                assert(cLim.Aborted = (scC < 0))
                txcc.OnCommit ()
                mcx.IL.Emit(OpCodes.Br, lblRunBody)
                mcx.IL.MarkLabel(lblOnCondFail)
                txcc.OnAbort ()
                mcx.IL.Emit(OpCodes.Br, lblTerminate)
                mcx.IL.EndScope()
                // loop body
                mcx.IL.MarkLabel(lblRunBody)
                let scA = compileOp mcx scC a
                assert((scA = sc) || (scA < 0))
                mcx.IL.Emit(OpCodes.Br, lblRepeat)
                // exit loop
                mcx.IL.MarkLabel(lblTerminate)
                scA
            | Until (ProgLim(c, cLim), a) ->
                let lblExitLoop = mcx.IL.DefineLabel()
                let lblRepeat = mcx.IL.DefineLabel()
                mcx.IL.MarkLabel(lblRepeat)
                mcx.IL.BeginScope()
                let lblOnCondFail = mcx.IL.DefineLabel()
                let mcxCond = { mcx with OnFail = lblOnCondFail; Lim = cLim }
                let txcc = txBegin mcxCond sc
                let scC = compileOp mcxCond sc c
                assert(cLim.Aborted = (scC < 0))
                // condition succeeds - commit then exit loop.
                txcc.OnCommit ()
                mcx.IL.Emit(OpCodes.Br, lblExitLoop)
                // condition fails - abort, run body, then repeat loop
                mcx.IL.MarkLabel(lblOnCondFail)
                txcc.OnAbort ()
                mcx.IL.EndScope()
                let scA = compileOp mcx sc a 
                assert((scA = sc) || (scA < 0))
                mcx.IL.Emit(OpCodes.Br, lblRepeat)
                mcx.IL.MarkLabel(lblExitLoop)
                scC
            | Stem lEff U -> 
                match mcx.ECX.EffMethod with
                | None -> // call the toplevel effect handler
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    mcx.IL.Emit(OpCodes.Ldfld, mcx.CTE.FldEWrap)
                    let sc' = argsFromArity mcx 1 1 sc
                    mcx.IL.Emit(OpCodes.Call, typeof<EWrap>.GetMethod("Eff"))
                    mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                    assert(sc = sc')
                    sc'
                | Some m -> // call user-provided effect handler
                    mcx.IL.Emit(OpCodes.Ldarg_0)
                    let sc' = argsFromArity mcx 1 1 sc
                    mcx.IL.Emit(OpCodes.Call, m)
                    mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                    assert(sc = sc')
                    sc'
            | Env (ProgLim(w, wLim), pDo) -> 
                assert(sc > 0)
                // separate effect handler into another method 
                let struct(effMethod, effStFld) = 
                    compileEffHandler (mcx.CTE) (mcx.ECX) wLim w
                let ecxDo = 
                    if wLim.Effectful then
                        { EffMethod = Some effMethod
                        ; EffState = effStFld :: mcx.ECX.EffState
                        ; IOState = mcx.ECX.IOState
                        }
                    else // mask prior eff state
                        { EffMethod = Some effMethod
                        ; EffState = [effStFld]
                        ; IOState = false 
                        }
                let mcxDo = { mcx with ECX = ecxDo } 
                // store top stack item into effStFld
                mcx.IL.Emit(OpCodes.Ldarg_0)
                loadSPVal (mcx.Stack[sc - 1]) mcx.IL
                mcx.IL.Emit(OpCodes.Stfld, effStFld)
                let scDo = compileOp mcxDo (sc - 1) pDo
                // unless aborted, restore eff state to stack
                if scDo < 0 then scDo else
                assert(scDo < mcx.Stack.Length)
                mcx.IL.Emit(OpCodes.Ldarg_0)
                mcx.IL.Emit(OpCodes.Ldfld, effStFld)
                storeSPVal (mcx.Stack[scDo]) mcx.IL
                (scDo + 1)
            | Stem lFail U ->
                mcx.IL.Emit(OpCodes.Br, mcx.OnFail)
                -1
            | Stem lHalt eMsg -> 
                let ixMsg = addStatic (mcx.CTE) eMsg
                mcx.IL.Emit(OpCodes.Ldsfld, mcx.CTE.FldData)
                Emit.ldc_i4 ixMsg mcx.IL
                mcx.IL.Emit(OpCodes.Ldelem, typeof<Value>)
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod("OpHalt"))
                -1
            | Prog (anno, p) ->
                compileAnno mcx sc anno p
            | _ ->
                // if an invalid program reaches our compiler, just treat it as a halt.
                let eMsg = Value.variant "unhandled" p0 
                compileOp mcx sc (Value.variant "halt" eMsg)  
        and compileAnno (mcx : MCX) (sc : SC) (anno : Value) (p : Program) : SC =
            // TODO: handle acceleration (at least)
            match anno with
            | Record ["prof"] struct([ValueSome profOptions],anno') ->
                // TODO: support for stopwatches and so; perhaps an extra argument
                // to the compiled object when it is constructed. nop for now
                compileAnno mcx sc anno' p
            | Record ["stow"] struct([ValueSome vOpts], anno') ->
                // nop for now
                compileAnno mcx sc anno' p
            | Record ["memo"] struct([ValueSome memoOpts], anno') ->
                // nop for now
                compileAnno mcx sc anno' p
            | Record ["accel"] struct([ValueSome vModel], anno') ->
                let p' = Prog(anno', p)
                match vModel with
                | Variant "opt" vModel' ->
                    compileAccel mcx sc true  vModel' p' 
                | _ ->
                    compileAccel mcx sc false vModel  p' 
            | Record ["lim"] struct([ValueSome (Lim lim)], anno') ->
                // compile a reusable subroutine
                let m = compileSub (mcx.CTE) (mcx.ECX) lim p
                mcx.IL.Emit(OpCodes.Ldarg_0)
                let sc' = argsFromArity mcx (int lim.ArityIn) (int lim.ArityOut) sc
                mcx.IL.Emit(OpCodes.Call, m)
                mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                sc'
            | _ ->
                // ignore annotation and inline
                compileOp mcx sc p
        and compileAccel (mcx:MCX) (sc:SC) (bOpt:bool) (vModel:Value) (p:Program) : SC =
            let inline helperOp methodName arIn arOut =
                // for helper ops with no failure
                let sc' = argsFromArity mcx arIn arOut sc
                mcx.IL.Emit(OpCodes.Call, typeof<HelperOps>.GetMethod(methodName))
                sc'
            let inline helperOpB methodName arIn arOut =
                // for helper ops that return pass/fail boolean
                let sc' = helperOp methodName arIn arOut
                mcx.IL.Emit(OpCodes.Brfalse, mcx.OnFail)
                sc'
            let inline helperOpP methodName arIn arOut =
                // for helper ops that return pass/fail/fallback integer
                let lblFin = mcx.IL.DefineLabel()
                mcx.IL.BeginScope()
                let resultVar = mcx.IL.DeclareLocal(typeof<int>)
                let sc' = helperOp methodName arIn arOut
                mcx.IL.Emit(OpCodes.Dup)
                Emit.stloc resultVar mcx.IL
                mcx.IL.Emit(OpCodes.Brfalse, lblFin)
                Emit.ldloc resultVar mcx.IL
                Emit.ldc_i4 accelFail mcx.IL
                mcx.IL.Emit(OpCodes.Beq, mcx.OnFail)
                mcx.IL.EndScope()
                // fallback to program (usually as a subroutine)
                let scFB = compileOp mcx sc p
                assert((scFB = sc') || (scFB < 0))
                mcx.IL.MarkLabel(lblFin)
                sc'
            let inline notImplemented () =
                if bOpt then compileOp mcx sc p else
                let eMsg = Value.variant "accel" vModel
                compileOp mcx sc (Value.variant "halt" eMsg) 
            match vModel with
            | Accel.Prefix "list-" vSuffix ->
                match vSuffix with
                | Value.Variant "pushl" U -> 
                    helperOp "AccelListPushl" 2 1
                | Value.Variant "pushr" U ->
                    helperOp "AccelListPushr" 2 1
                | Value.Variant "popl" U ->
                    helperOpB "AccelListPopl" 1 2
                | Value.Variant "popr" U -> 
                    helperOpB "AccelListPopr" 1 2
                | Value.Variant "append" U -> 
                    helperOp "AccelListAppend" 2 1
                | Value.Variant "verify" U -> 
                    helperOpB "AccelListVerify" 1 1
                | Value.Variant "length" U -> 
                    helperOp "AccelListLength" 1 1
                | Value.Variant "take" U -> 
                    helperOpB "AccelListTake" 2 1
                | Value.Variant "skip" U -> 
                    helperOpB "AccelListSkip" 2 1
                | Value.Variant "item" U -> 
                    helperOpB "AccelListItem" 2 1
                | _ -> notImplemented ()
            | Accel.Prefix "bits-" vSuffix ->
                match vSuffix with
                | Value.Variant "verify" U -> 
                    helperOpB "AccelBitsVerify" 1 1
                | Value.Variant "negate" U -> 
                    helperOp "AccelBitsNegate" 1 1
                | Value.Variant "reverse-append" U ->
                    helperOp "AccelBitsReverseAppend" 2 1
                | Value.Variant "length" U -> 
                    helperOp "AccelBitsLength" 1 1
                | _ -> notImplemented ()
            | Accel.Prefix "int-" vSuffix ->
                match vSuffix with
                | Value.Variant "add" U ->
                    helperOpP "AccelSmallIntAdd" 2 1 
                | Value.Variant "mul" U ->
                    helperOpP "AccelSmallIntMul" 2 1
                | Value.Variant "sub" U -> 
                    helperOpP "AccelSmallIntSub" 2 1 
                //| Value.Variant "divmod" U -> notImplemented ()
                | Value.Variant "increment" U -> 
                    helperOpP "AccelSmallIntIncrement" 1 1
                | Value.Variant "decrement" U -> 
                    helperOpP "AccelSmallIntDecrement" 1 1
                | Value.Variant "gt" U -> 
                    helperOpP "AccelSmallIntGT" 2 0
                | Value.Variant "gte" U -> 
                    helperOpP "AccelSmallIntGTE" 2 0
                | _ -> notImplemented ()
            | Accel.Prefix "nat-" vSuffix ->
                match vSuffix with 
                | Value.Variant "add" U -> 
                    helperOpP "AccelSmallNatAdd" 2 1
                | Value.Variant "sub" U -> 
                    helperOpP "AccelSmallNatSub" 2 1
                | Value.Variant "mul" U -> 
                    helperOpP "AccelSmallNatMul" 2 1
                | Value.Variant "divmod" U ->
                    helperOpP "AccelSmallNatDivMod" 2 2
                | Value.Variant "gt" U -> 
                    helperOpP "AccelSmallNatGT" 2 0 
                | Value.Variant "gte" U -> 
                    helperOpP "AccelSmallNatGTE" 2 0
                | _ -> notImplemented ()
            | _ -> notImplemented ()

        and compileSub (cte:CTE) (ecx0:ECX) (lim:Lim) (p:Program) : MethodBuilder = 
            let ecx = // erase effect context if pure
                if lim.Effectful then ecx0 else
                { EffMethod = None; EffState = []; IOState = false }
            let effHandler = // represent effect context as a string
                match ecx.EffMethod with
                | None -> ""
                | Some m -> m.Name
            let subId = struct(p, effHandler)
            match cte.SubProgs.TryGetValue(subId) with
            | true, m -> m
            | false, _ ->
                let name = "Sub" + string(1 + cte.SubProgs.Count)
                let m = cte.TypB.DefineMethod(name, MethodAttributes.Private ||| MethodAttributes.Final)
                cte.SubProgs.Add(subId, m)
                buildMethod m cte ecx lim p
                m

        and compileEffHandler (cte:CTE) (ecx:ECX) (lim0:Lim) (p:Program) : (struct(MethodBuilder * FieldBuilder)) =
            // effHandler is always 2--2, but produced method is 1--1. 
            // One parameter is input as a field in the compiled class.
            let lim = limAddStack (2s - lim0.ArityIn) lim0
            assert((lim.ArityIn = 2s) && ((lim.ArityOut = 2s) || lim.Aborted))
            let idSuffix = string (1 + cte.Handlers.Count)

            let effStFldName = "EffSt" + idSuffix
            let effStFldAttr = FieldAttributes.Private
            let effStFld = cte.TypB.DefineField(effStFldName, typeof<Value>, effStFldAttr)

            let methodArgs = methodArgsFromArity 1 1
            let methodAttr = MethodAttributes.Private ||| MethodAttributes.Final
            let methodName = "Eff" + idSuffix

            let m = cte.TypB.DefineMethod(methodName, methodAttr,
                        CallingConventions.Standard,
                        typeof<bool>, methodArgs)

            let result = struct(m, effStFld)
            cte.Handlers.Add(methodName, result)

            let il = m.GetILGenerator()
            let lblFail = il.DefineLabel()
            let stackReg =
                assert(lim.MaxStack >= lim.ArityIn)
                // trivial input args 
                let inputArgs =
                    let v = il.DeclareLocal(typeof<Value>)
                    il.Emit(OpCodes.Ldarg_1)
                    Emit.stloc v il
                    [| v |] 
                let extraSpace = 
                    let arr = Array.zeroCreate (int (lim.MaxStack - 1s))
                    for ix in 1 .. arr.Length do
                        arr[ix - 1] <- il.DeclareLocal(typeof<Value>)
                    arr
                Array.append inputArgs extraSpace
            assert(stackReg.Length = int lim.MaxStack)
            let mcx =
                { CTE = cte
                ; Lim = lim
                ; OnFail = lblFail
                ; IL = il
                ; Stack = stackReg 
                ; ECX = ecx
                }
            il.Emit(OpCodes.Ldarg_0)
            il.Emit(OpCodes.Ldfld, effStFld)
            storeSPVal (stackReg[1]) il
            let sc' = compileOp mcx 2 p
            assert((sc' = 2) || ((sc' < 0) && (lim.Aborted)))
            // success path 
            il.Emit(OpCodes.Ldarg_0)                
            loadSPVal (stackReg[1]) il
            il.Emit(OpCodes.Stfld, effStFld)        // save effect handler state
            il.Emit(OpCodes.Ldarg_2)
            loadSPVal (stackReg[0]) il
            il.Emit(OpCodes.Stobj, typeof<Value>)   // return result via outref
            il.Emit(OpCodes.Ldc_I4_1)               
            il.Emit(OpCodes.Ret)                    // return true
            // failure path 
            il.MarkLabel(lblFail)
            il.Emit(OpCodes.Ldc_I4_0)
            il.Emit(OpCodes.Ret)                    // return false

            // return the relevant objects
            result

        and buildMethod (m:MethodBuilder) (cte:CTE) (ecx:ECX) (lim:Lim) (p0:Program) = 
            let argTypes = methodArgsFromArity (int lim.ArityIn) (int lim.ArityOut)
            m.SetParameters(argTypes)
            m.SetReturnType(typeof<bool>)
            let il = m.GetILGenerator()
            let lblFail = il.DefineLabel()
            let stackReg = 
                let inputArgs = 
                    // top of stack is arg 1, but highest 'sc'.
                    // so I need to reorder things logically.
                    let arr = Array.zeroCreate (int lim.ArityIn)
                    for ix in 1 .. arr.Length do
                        let v = il.DeclareLocal(typeof<Value>)
                        arr[arr.Length - ix] <- v
                        Emit.ldarg (int16 ix) il
                        Emit.stloc v il
                    arr
                let extraSpace =
                    let arr = Array.zeroCreate (int (lim.MaxStack - lim.ArityIn))
                    for ix in 1 .. arr.Length do
                        arr[ix - 1] <- il.DeclareLocal(typeof<Value>)
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
            assert((lim.Aborted && (sc' < 0)) || (sc' = int lim.ArityOut))
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
            ; Handlers = new Handlers()
            ; Statics = new DataDict()
            ; SubProgs = new SubProgs()
            }

        let mainMethod = "Run"

        let compiled (p : Program) : Effects.IEffHandler -> Value list -> Value list option =
            let struct(lim, p') = precomp p
            let progType =
                let cte = initCTE ()
                let m = cte.TypB.DefineMethod(mainMethod, MethodAttributes.Public ||| MethodAttributes.Final)
                let ecx = { EffMethod = None; EffState = []; IOState = true }
                buildMethod m cte ecx lim p'
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
        | "FinallyTaglessInterpreter" | "FTI" -> FinallyTaglessInterpreter.eval
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

