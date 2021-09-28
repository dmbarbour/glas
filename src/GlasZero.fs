namespace Glas

/// This file implements the g0 (Glas Zero) bootstrap parser and compiler. 
/// This implementation of g0 should be replaced by a language-g0 module
/// 'compile' program after bootstrap completes. 
/// 
/// The g0 language is similar to a Forth. A program is a sequence of words
/// and data, which are applied to manipulate a data stack. Features include
/// access to macros for metaprogramming, and loading definitions from modules.
/// The g0 language has no built-in definitions, instead using macros as the
/// foundation to represent primitive operations.
///
/// The goal for g0 is to be reasonably comfortable for programming a foundation
/// of the Glas language system.
///
module Zero =
    type Word = string
    type ModuleName = Word

    /// AST for g0 programs.
    /// Comments are dropped. Words are not linked yet.
    type Block = Action list
    and Action =
        // initial parse
        | Call of Word      // word or macro call
        | Const of Value    // numbers, strings, etc.
        | Block of Block    // [foo]

    type Ent =
        | ImportFrom of ModuleName * ((Word * Word) list) 
        | ProgDef of Word * Block
        | MacroDef of Word * Block
        | DataDef of Word * Block
        | StaticAssert of int64 * Block   // (line number recorded for error reporting)

    /// AST for g0 toplevel.
    type TopLevel = 
        { Open : ModuleName option
        ; Ents : Ent list
        ; Export : Block
        }

    module Parser =
        // I'm using FParsec to provide decent error messages without too much effort.

        open FParsec
        type P<'T> = Parser<'T,unit>

        let lineComment : P<unit> =
            pchar '#' >>. skipManyTill anyChar newline 

        let ws : P<unit> =
            spaces >>. skipMany (lineComment >>. spaces)

        let sol : P<unit> =
            getPosition >>= fun p ->
                if (1L >= p.Column) 
                    then preturn () 
                    else fail "expecting start of line"

        let wsepChars = " \n\r\t[],"

        let wsep : P<unit> =
            nextCharSatisfiesNot (isNoneOf wsepChars) .>> ws

        let kwstr s : P<unit> = 
            pstring s >>. wsep

        let parseWord : P<string> =
            let frag = many1Satisfy2 isAsciiLower (fun c -> isAsciiLower c || isDigit c)
            (stringsSepBy1 frag (pstring "-") .>> wsep) <?> "word"

        let parseSymbol : P<Bits> =
            pchar '\'' >>. parseWord |>> Value.label

        let hexN (cp : byte) : byte =
            if ((48uy <= cp) && (cp <= 57uy)) then (cp - 48uy) else
            if ((65uy <= cp) && (cp <= 70uy)) then (cp - 55uy) else
            if ((97uy <= cp) && (cp <= 102uy)) then (cp - 87uy) else
            invalidArg (nameof cp) "not a valid hex byte"

        let consNbl (nbl : byte) (b : Bits) : Bits =
            let inline cb n acc = Bits.cons (0uy <> ((1uy <<< n) &&& nbl)) acc
            b |> cb 0 |> cb 1 |> cb 2 |> cb 3

        let parseHex : P<Bits> =
            pstring "0x" >>. many1Satisfy isHex .>> wsep |>> fun s -> 
                let arr = System.Text.Encoding.ASCII.GetBytes(s)
                Array.foldBack (fun cp acc -> consNbl (hexN cp) acc) arr (Bits.empty)

        let isBinChar c = 
            (('0' = c) || ('1' = c))

        let parseBin : P<Bits> =
            pstring "0b" >>. many1Satisfy isBinChar .>> wsep |>> fun s ->
                let arr = System.Text.Encoding.ASCII.GetBytes(s)
                Array.foldBack (fun cp acc -> Bits.cons (48uy <> cp) acc) arr (Bits.empty)

        let parsePosNat : P<Bits> =
            many1Satisfy2L (fun c -> isDigit c && (c <> '0')) isDigit "non-zero number" .>> wsep |>> 
                (System.Numerics.BigInteger.Parse >> Bits.ofI) 

        let parseNat : P<Bits> =
            parsePosNat <|> (pchar '0' .>> wsep >>% Bits.empty)

        let isStrChar (c : char) : bool =
            let cp = int c
            (cp >= 32) && (cp <= 126) && (cp <> 34)

        let parseString : P<string> =
            pchar '"' >>. manySatisfy isStrChar .>> pchar '"' .>> wsep

        let parseBitString : P<Bits> =
            parseBin <|> parseHex <|> parseSymbol <|> parseNat

        let parseData : P<Value> =
            choice [
                parseBitString |>> Value.ofBits
                parseString |>> Value.ofString
            ]

        // FParsec's approach to recursive parser definitions is a little awkward.
        let parseAction, parseActionRef = createParserForwardedToRef<Action, unit>()

        let parseBlock : P<Block> = 
            between (pchar '[' .>> ws) (pchar ']' .>> wsep) (many parseAction)

        parseActionRef := 
            choice [
                parseData |>> Const
                parseBlock |>> Block
                parseWord |>> Call
            ]

        let parseOpen : P<ModuleName> =
            kwstr "open" >>. parseWord

        let parseExport : P<Block> =
            kwstr "export" >>. parseBlock 

        let parseImport : P<Word * Word> =
            parseWord .>>. (opt (kwstr "as" >>. parseWord)) |>> 
                fun (w,optAsW) ->
                    let aw = Option.defaultValue w optAsW
                    (w,aw)

        let parseImportFrom : P<Ent> =
            kwstr "from" >>. parseWord .>>. (kwstr "import" >>. sepBy1 parseImport (pchar ',' .>> ws)) 
                |>> ImportFrom

        let parseProgDef : P<Ent> = 
            kwstr "prog" >>. parseWord .>>. parseBlock |>> ProgDef

        let parseMacroDef : P<Ent> =
            kwstr "macro" >>. parseWord .>>. parseBlock |>> MacroDef

        let parseDataDef : P<Ent> =
            kwstr "data" >>. parseWord .>>. parseBlock |>> DataDef

        let parseStaticAssert : P<Ent> = 
            kwstr "assert" >>. getPosition .>>. parseBlock |>> 
                fun (p,b) ->
                    StaticAssert (p.Line, b)

        let parseEnt : P<Ent> =
            choice [
                parseImportFrom 
                parseProgDef 
                parseMacroDef 
                parseStaticAssert 
            ]

        let parseTopLevel = parse {
            do! ws
            let! optOpen = opt (sol >>. parseOpen)
            let! lEnts = many (sol >>. parseEnt)
            let! exportFn = opt (sol >>. parseExport) |>> Option.defaultValue []
            do! eof
            return { Open = optOpen; Ents = lEnts; Export = exportFn } 
        }

    /// Words called from a single block.
    let rec wordsCalledBlock (b:Block) =
        let fnEnt ent =
            match ent with
            | Call w -> Set.singleton w
            | Const _ -> Set.empty
            | Block p -> wordsCalledBlock p
        Set.unionMany (Seq.map fnEnt b)

    /// All words called from all top-level entries.
    let wordsCalled (tlv : TopLevel) : Set<Word> =
        let fnEnt ent =
            match ent with
            | ProgDef (_, b) | MacroDef (_, b) | StaticAssert (_, b) | DataDef (_, b) -> 
                wordsCalledBlock b
            | ImportFrom _ -> 
                Set.empty
        Set.unionMany (Seq.map fnEnt tlv.Ents)

    /// All words explicitly defined in the top-level entries.
    let wordsDefined (tlv : TopLevel) : Set<Word> = 
        let fnEnt ent =
            match ent with
            | ImportFrom (_, lImports) -> 
                Set.ofList (List.map snd lImports)
            | ProgDef (w, _) | MacroDef (w, _) | DataDef (w, _) -> 
                Set.singleton w
            | StaticAssert _ -> 
                Set.empty
        Set.unionMany (Seq.map fnEnt tlv.Ents)

    /// I'll discourage shadowing of definitions via issuing a warning.
    /// A word is observably shadowed if defined or called before a
    /// later definition within the same file.
    ///
    /// This check detects duplicate definitions, call before definition, and
    /// accidental recursion. But undefined words are not detected.
    let wordsShadowed (tlv : TopLevel) : Set<Word> =
        let fnEnt ent (wsSh,wsDef) =
            match ent with
            | ImportFrom (_, lImports) ->
                // detect shadowing within the imports list, too.
                let fnImp (_,aw) (wsSh, wsDef) =
                    let wsDef' = Set.add aw wsDef
                    let wsSh' = if Set.contains aw wsDef then Set.add aw wsSh else wsSh
                    (wsSh', wsDef')
                List.foldBack fnImp lImports (wsSh, wsDef)
            | ProgDef (w, b) | MacroDef (w, b) | DataDef (w, b) ->
                let wsDef' = Set.add w wsDef
                let wsSh' = wordsCalledBlock b |> Set.intersect wsDef' |> Set.union wsSh
                (wsSh', wsDef')
            | StaticAssert (_, b) ->
                let wsSh' = wordsCalledBlock b |> Set.intersect wsDef |> Set.union wsSh
                (wsSh', wsDef)
        List.foldBack fnEnt tlv.Ents (Set.empty, Set.empty) |> fst

    /// List of modules loaded via 'open' or 'from'.
    /// This doesn't detect modules loaded via compile-time effects from macros.
    let modulesLoaded (tlv : TopLevel) : List<ModuleName> =
        let fnEnt ent =
            match ent with
            | ImportFrom (m, _) -> [m]
            | ProgDef _ | MacroDef _ | StaticAssert _ | DataDef _  -> []
        let lOpen = Option.toList tlv.Open
        let lFrom = List.collect fnEnt tlv.Ents
        List.append lOpen lFrom 

    module Compile =
        open Effects
        open ProgVal
        open ProgEval
        open Value

        // Goals for compilation:
        // - Report as many errors as feasible while compiling, even after
        //   we know that compilation will fail. 
        // - Static arity checks for newly defined words by default. 
        // - Enable client to continue with errors.

        [<System.FlagsAttribute>]
        type ErrorFlags =
            | NoError = 0
            | SyntaxError = 1           // the program doesn't parse
            | WordShadowed = 2          // at least one word is shadowed
            | LoadError = 4             // 'open' or 'from' fails to load 
            | WordUndefined = 8         // called or imported word is undefined
            | UnknownDefType = 16       // called word has unrecognized def type
            | UncalledMacro = 32        // need static parameters for macro call
            | MacroFailed = 64          // top-level failure within a macro call
            | BadMacroResult = 128      // first result from macro is not a program
            | AssertionFail = 256       // evaluation of an assertion failed 
            | DynamicArity = 512        // program does not have static arity or failure

        [<Struct>]
        type CTE =
            { Dict : Value
            ; CallWarn : Set<Word>   // to resist duplicate call warnings 
            ; Errors : ErrorFlags
            ; LogLoad : IEffHandler
            }
        type AR = (struct(int*int)) option

        exception ForbiddenEffectException of Value
        let forbidEffects = 
            { new IEffHandler with
                member __.Eff v = raise (ForbiddenEffectException(v))
              interface ITransactional with
                member __.Try () = ()
                member __.Commit () = ()
                member __.Abort () = ()
            }

        // Obtain arity via top-level annotation if possible. If not, compute arity.
        let progArity (p:Program) : AR =
            match p with
            | Prog (vAnno, pDo) -> 
                match vAnno with
                | FullRec ["arity"] ([FullRec ["i";"o"] ([Nat i; Nat o], _)], _) ->
                    Some struct(int i, int o)
                | FullRec ["arity"] ([Value.Variant "invalid" U], _) -> 
                    None
                | _ -> static_arity pDo
            | _ -> static_arity p

        // macros adjust program arity to require at least one output, even
        // if that output is from the data stack. Mostly this affects the
        // basic 'apply' macro.
        let macroArity (p:Program) : AR =
            match progArity p with
            | Some struct(i,0) -> 
                Some struct(i+1, 1)
            | other -> other

        // evaluate with effects forbidden.
        // currently returns None if:
        //   insufficient parameters
        //   top-level effect is requested
        //   failure during evaluation
        //
        // The caller should preserve a failing call rather than reduce to 'fail'.
        // This would have greater potential to simplify runtime debugging.
        let tryPartialEval p ds =
            // todo: consider caching of `eval p forbidEffects`. Lower priority
            // due to lazy compilation of conditional behavior.
            match progArity p with
            | Some struct(i,o) when (i >= List.length ds) -> 
                try eval p forbidEffects ds 
                with 
                | ForbiddenEffectException _ -> None
            | _ -> None

        let private stepArity ar1 ar2 = 
            match ar1, ar2 with
            | Some struct(i1,o1), Some struct(i2,o2) ->
                let d = max 0 (i2 - o1)
                Some struct(i1 + d, o1 + d + (o2 - i2))
            | _ -> None

        // translate block into a program value.
        let private toProg (ops:Program list) : Program =
            let addOpArity ar op = stepArity ar (progArity op)
            let arOps = List.fold addOpArity (Some struct(0,0)) ops
            let annoArity =
                match arOps with
                | None -> 
                    // annotate for invalid static arity
                    [("arity", Value.symbol "invalid")]
                | Some struct(i,o) ->
                    let aio = 
                        Value.asRecord ["i";"o"] 
                            [ Value.nat (uint64 i)
                            ; Value.nat (uint64 o) 
                            ]
                    [("arity", aio)]
            let allAnno = List.concat [annoArity]
            let vAnno = allAnno |> Map.ofList |> Value.ofMap
            // eliminate singleton 'seq'.
            match ops with
            | [op] -> Prog (vAnno, op)
            | _ -> Prog (vAnno, PSeq (FTList.ofList ops))

        let private addDataOpsRev (revOps:Program list) (ds:Value list) =
            List.append (List.map (ProgVal.Data) ds) revOps

        // Compile a program block into a value. 
        let rec compileBlock (cte:CTE) (b:Block) =
            _compileBlock cte [] [] b
        and private _compileBlock (cte:CTE) (revOps:Program list) (ds:Value list) (b:Block) =
            match b with
            | [] -> 
                struct(cte, toProg (List.rev (addDataOpsRev revOps ds)))
            | ((Block p)::b') ->
                let struct(cte', pVal) = compileBlock cte p
                _compileBlock cte' revOps (pVal::ds) b'
            | ((Const v)::b') ->
                _compileBlock cte revOps (v::ds) b'
            | ((Call w)::b') ->
                match Value.record_lookup (Value.label w) (cte.Dict) with
                | Some (ProgVal.Data v) ->
                    _compileBlock cte revOps (v::ds) b'
                | Some ((ProgVal.Prog _) as p) ->
                    failwith "todo: apply prog"
                | Some (Value.Variant "macro" p) ->
                    failwith "todo: apply macro"
                | Some _ -> // unrecognized deftype
                    if not (Set.contains w (cte.CallWarn)) then 
                        logError (cte.LogLoad) (sprintf "word %s has unhandled deftype" w) 
                    let revOps' = (Op lFail) :: addDataOpsRev revOps ds
                    let cte' = 
                        { cte with
                            CallWarn = Set.add w (cte.CallWarn) 
                            Errors = ErrorFlags.UnknownDefType ||| cte.Errors 
                        }
                    _compileBlock cte' revOps' [] b'
                | None ->
                    // undefined words are reported earlier.
                    let revOps' = (Op lFail) :: addDataOpsRev revOps ds
                    let cte' = { cte with Errors = ErrorFlags.WordUndefined ||| cte.Errors }
                    _compileBlock cte' revOps' [] b'


        let loadModule (ll:IEffHandler) (m:string) = 
            ll.Eff(Value.variant "load" (Value.ofString m))

        let private tryOptOpen ll optOpen =
            match optOpen with
            | None -> 
                // no 'open', just return empty dict
                Some (Value.unit)   
            | Some m ->
                match loadModule ll m with
                | None ->
                    logError ll (sprintf "failed to load module %s for 'open'" m)
                    None
                | Some d0 -> Some d0

        let initOpen ll tlv =
            match tryOptOpen ll tlv.Open with
            | None -> struct(Value.unit, ErrorFlags.LoadError)
            | Some d0 ->
                // detect any words we expected in 'open'
                // we may compile with shadowing, so we don't erase any words here.
                let haveWord w = Option.isSome (Value.record_lookup (Value.label w) d0)
                let wsExpect = Set.difference (wordsCalled tlv) (wordsDefined tlv)
                let wsMissing = Set.filter (haveWord >> not) wsExpect
                if Set.isEmpty wsMissing then
                    struct(d0, ErrorFlags.NoError)
                else 
                    logError ll (sprintf "missing definitions for %s"  (String.concat ", " wsMissing))
                    struct(d0, ErrorFlags.WordUndefined)

        let applyImportFrom (cte:CTE) (m:ModuleName) (lImports:(Word * Word) list) =
            let ll = cte.LogLoad
            match loadModule ll m with
            | None ->
                logError ll (sprintf "failed to load module %s" m)
                { cte with Errors = (cte.Errors ||| ErrorFlags.LoadError) }
            | Some dSrc ->
                let haveWord (w:string) = Option.isSome <| Value.record_lookup (Value.label w) dSrc
                let wsMissing = lImports |> List.map fst |> List.filter (haveWord >> not) 
                let errNew = 
                    if List.isEmpty wsMissing then ErrorFlags.NoError else 
                    logError ll (sprintf "module %s does not define %s" m (String.concat ", " wsMissing))
                    ErrorFlags.WordUndefined
                let addWord dDst (w,aw) =
                    match Value.record_lookup (Value.label w) dSrc with
                    | None -> Value.record_delete (Value.label aw) dDst
                    | Some vDef -> Value.record_insert (Value.label aw) vDef dDst
                { cte with 
                    Dict = List.fold addWord (cte.Dict) lImports
                    Errors = errNew ||| cte.Errors 
                }

        
        //let compileBlock (ll:IEffHandler) (d0:Value) (b:Block) =

        let applyProgDef cte w b =
            failwith "todo: compile program, define prog word"

        let applyMacroDef cte w b = 
            failwith "todo: compile program, define macro word"

        let applyDataDef cte w b = 
            failwith "todo: compile program, define data word"


        let applyStaticAssert cte ln b =
            failwith "todo: compile program, check assertion"
            (*
            match tryCompileBlock ll (st0.Dict) b with
            | Some (p, eComp) when checkAssertion ll p ->
                { st0 with Errors = st0.Errors ||| eComp }
            | Some (p, eComp) ->
                logError ll (sprintf "assertion on line %d fails" ln)
                { st0 with Errors = st0.Errors ||| eComp ||| ErrorFlags.Assertion }
            | None ->
                logError ll (sprintf "failed to compile assertion at line %d" ln)
                { st0 with Errors = st0.Errors ||| ErrorFlags.Assertion ||| ErrorFlags.CompBlock }
                *)

        let applyEnt (cte:CTE) (ent:Ent) : CTE =
            match ent with
            | ImportFrom (m, lImports) -> applyImportFrom cte m lImports
            | ProgDef (w, b) -> applyProgDef cte w b
            | MacroDef (w, b) -> applyMacroDef cte w b
            | StaticAssert (ln, b) -> applyStaticAssert cte ln b
            | DataDef (w, b) -> applyDataDef cte w b

        let initCTE ll err d0 =
            { Dict = d0
            ; LogLoad = ll
            ; Errors = err
            ; CallWarn = Set.empty
            }

        let compile (ll:IEffHandler) (s:string) : CTE =
            match FParsec.CharParsers.run (Parser.parseTopLevel) s with
            | FParsec.CharParsers.Success (tlv, _, _) ->
                let eShadow = 
                    let wsSh = wordsShadowed tlv
                    if not (Set.isEmpty wsSh) then ErrorFlags.NoError else
                    logError ll (sprintf "shadowed words: %s" (String.concat ", " wsSh))
                    ErrorFlags.WordShadowed
                let struct(dOpen,eOpen) = initOpen ll tlv 
                let cte0 = initCTE ll (eShadow ||| eOpen) dOpen
                List.fold applyEnt cte0 (tlv.Ents)
            | FParsec.CharParsers.Failure (msg, _, _) ->
                logError ll msg
                initCTE ll (ErrorFlags.SyntaxError) (Value.unit)

    /// Compile a g0 program, returning the final dictionary only if
    /// there are no errors during the compilation process. 
    ///
    /// Effects handler must support 'log' and 'load' effects. Any
    /// errors are logged implicitly.
    let compile (ll:Effects.IEffHandler) (s:string) : Value option =
        let cte = Compile.compile ll s 
        if Compile.ErrorFlags.NoError = cte.Errors 
            then Some (cte.Dict) 
            else None
