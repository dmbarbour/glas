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
            between (pchar '[' .>> ws) (pchar ']' .>> ws) (many parseAction)

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
                parseDataDef
                parseStaticAssert 
            ]

        let parseTopLevel = parse {
            do! ws
            let! optOpen = opt (parseOpen)
            let! lEnts = many (parseEnt)
            let! exportFn = opt (parseExport) |>> Option.defaultValue []
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
        // - Allow client to continue with errors at its own discretion.
        //
        // I'm still considering static arity checks or annotations. 
        //

        [<System.FlagsAttribute>]
        type ErrorFlags =
            | NoError = 0
            | SyntaxError = 1           // the program doesn't parse
            | WordShadowed = 2          // at least one word is shadowed
            | LoadError = 4             // 'open' or 'from' fails to load 
            | WordUndefined = 8         // called or imported word is undefined
            | UnknownDefType = 16       // called word has unrecognized def type
            | MacroFailure = 64         // evaluation of macro failed for any reason
            | AssertionFail = 128       // evaluation of an assertion failed 
            | DataEvalFail = 256        // evaluation of data block failed
            | ExportFailed = 512        // evaluation of export failed.
            | BadStaticArity = 1024     // failed static arity check

        [<Struct>]
        type CTE =
            { Dict : Value
            ; CallWarn : Set<Word>   // to resist duplicate call warnings 
            ; Errors : ErrorFlags
            ; LogLoad : IEffHandler
            ; DbgCx : string
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

        let private addDataOpsRev (revOps:Program list) (ds:Value list) =
            List.append (List.map (ProgVal.Data) ds) revOps

        let private checkArity (struct(cte,p)) =
            let ar = stackArity (Arity(1,1)) p
            let eArity =
                if ArityDyn <> ar then ErrorFlags.NoError else 
                logError (cte.LogLoad) (sprintf "%s does not have static arity" (cte.DbgCx))
                ErrorFlags.BadStaticArity
            let cte' = { cte with Errors = eArity ||| cte.Errors }
            struct(cte', p)

        // unwraps 'prog:do:P' to 'P' when annotations are empty
        let unwrapProg p =
            match p with
            | Prog (anno, pDo) when (Value.unit = anno) -> pDo
            | _ -> p

        // Compile a program block into a value. 
        let rec compileBlock (cte:CTE) (b:Block) =
            _compileBlock cte [] [] b
        and private _compileCallProg cte revOps ds p b =
            let tryEval =
                try eval p forbidEffects ds 
                with 
                | ForbiddenEffectException _ -> None
            match tryEval with
            | Some ds' ->
                _compileBlock cte revOps ds' b
            | None ->
                let revOps' = (unwrapProg p) :: (addDataOpsRev revOps ds)
                _compileBlock cte revOps' [] b
        and private _compileFailedCall cte revOps ds w eType b =
            let revOps' = (Op lFail) :: addDataOpsRev revOps ds
            let cte' = 
                { cte with 
                    Errors = (eType ||| cte.Errors) 
                    CallWarn = Set.add w (cte.CallWarn)
                }
            _compileBlock cte' revOps' [] b
        and private _compileBlock (cte:CTE) (revOps:Program list) (ds:Value list) (b:Block) =
            match b with
            | ((Block p)::b') ->
                let ixEnd = 1 + List.length b'
                let dbg = cte.DbgCx + (sprintf " block -%d" ixEnd)
                let struct(cte', pv) = compileBlock { cte with DbgCx = dbg } p
                _compileBlock { cte' with DbgCx = cte.DbgCx } revOps (pv::ds) b'
            | ((Const v)::b') ->
                _compileBlock cte revOps (v::ds) b'
            | ((Call w)::b') ->
                match Value.record_lookup (Value.label w) (cte.Dict) with
                | Some (ProgVal.Data v) ->
                    _compileBlock cte revOps (v::ds) b'
                | Some ((ProgVal.Prog _) as p) ->
                    _compileCallProg cte revOps ds p b'
                | Some (Value.Variant "macro" m) ->
                    match eval m (cte.LogLoad) ds with
                    | Some (p :: ds') ->
                        _compileCallProg cte revOps ds' p b'
                    | _ ->
                        let ixEnd = 1 + List.length b' 
                        if not (Set.contains w (cte.CallWarn)) then
                            let msg = sprintf "macro %s failed in %s at -%d" w (cte.DbgCx) ixEnd
                            logError (cte.LogLoad) msg
                        _compileFailedCall cte revOps ds w (ErrorFlags.MacroFailure) b'
                | Some v -> 
                    // unrecognized deftype, will warn on first call
                    if not (Set.contains w (cte.CallWarn)) then 
                        logError (cte.LogLoad) (sprintf "word %s has unhandled deftype (%s)" w (prettyPrint v)) 
                    _compileFailedCall cte revOps ds w (ErrorFlags.UnknownDefType) b'
                | None ->
                    // no need to report undefined words here, reported on 'open' or 'import'
                    _compileFailedCall cte revOps ds w (ErrorFlags.WordUndefined) b'
            | [] -> 
                let p = 
                    match List.rev (addDataOpsRev revOps ds) with
                    | [op] -> op
                    | ops -> PSeq (FTList.ofList ops)
                checkArity (struct(cte,p))

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

        // add 'prog:do' prefix if it does not already exist
        let private wrapProg p =
            match p with
            | Prog _ -> p 
            | _ -> Prog(Value.unit, p)

        let applyProgDef cte w b =
            let dbg = sprintf "prog %s" w
            let struct(cte', p) = compileBlock { cte with DbgCx = dbg } b
            { cte' with 
                Dict = Value.record_insert (Value.label w) (wrapProg p) (cte'.Dict) 
                DbgCx = cte.DbgCx
            }

        let applyMacroDef cte w b = 
            let dbg = sprintf "macro %s" w
            let struct(cte', p) = compileBlock { cte with DbgCx = dbg } b 
            let m = Value.variant "macro" (wrapProg p)
            { cte' with 
                Dict = Value.record_insert (Value.label w) m (cte'.Dict) 
                DbgCx = cte.DbgCx
            }

        let applyDataDef cte w b =
            let ll = cte.LogLoad
            let dbg = sprintf "data %s" w
            let struct(cte', pData) = compileBlock { cte with DbgCx = dbg } b
            let vDataOpt = 
                match eval pData ll [] with
                | Some [vData] -> Some (Value.variant "data" vData)
                | Some _ ->
                    logError ll (sprintf "%s produces too much data" dbg) 
                    None
                | _ ->
                    logError ll (sprintf "%s evaluation failed" dbg)
                    None
            let eDataEval = 
                if Option.isSome vDataOpt 
                    then ErrorFlags.NoError 
                    else ErrorFlags.DataEvalFail
            { cte' with
                DbgCx = cte.DbgCx
                Dict = Value.record_set (Value.label w) vDataOpt (cte.Dict)
                Errors = eDataEval ||| cte.Errors
            }

        let applyStaticAssert cte ln b =
            let ll = cte.LogLoad
            let dbg = sprintf "assert at line %d" ln
            let struct(cte', pAssert) = compileBlock { cte with DbgCx = dbg } b
            let bSuccess =
                match eval pAssert ll [] with
                | Some _ -> true // any number of results is okay
                | None ->
                    logError ll (sprintf "%s failed" dbg)
                    false
            let eAssert = 
                if bSuccess 
                    then ErrorFlags.NoError 
                    else ErrorFlags.AssertionFail
            { cte' with
                DbgCx = cte.DbgCx
                Errors = eAssert ||| cte.Errors
                // no Dict changes
            } 
            
        let applyEnt (cte:CTE) (ent:Ent) : CTE =
            match ent with
            | ImportFrom (m, lImports) -> applyImportFrom cte m lImports
            | ProgDef (w, b) -> applyProgDef cte w b
            | MacroDef (w, b) -> applyMacroDef cte w b
            | StaticAssert (ln, b) -> applyStaticAssert cte ln b
            | DataDef (w, b) -> applyDataDef cte w b

        let applyExport cte b =
            let ll = cte.LogLoad
            let struct(cte', pExport) = compileBlock { cte with DbgCx = "export" } b
            let vExportOpt =
                match eval pExport ll [cte.Dict] with
                | Some [vExport] -> Some vExport
                | Some _ -> // requires arity error
                    logError ll "export produces too much data"
                    None
                | None ->
                    logError ll "export evaluation failed"
                    None
            let eExport =
                if Option.isSome vExportOpt
                    then ErrorFlags.NoError
                    else ErrorFlags.ExportFailed
            { cte' with
                DbgCx = cte.DbgCx
                Dict = Option.defaultValue (Value.unit) vExportOpt
                Errors = eExport ||| cte.Errors
            }

        let initCTE ll err d0 =
            { Dict = d0
            ; LogLoad = ll
            ; Errors = err
            ; CallWarn = Set.empty
            ; DbgCx = ""
            }

        let compile (ll:IEffHandler) (s:string) : CTE =
            match FParsec.CharParsers.run (Parser.parseTopLevel) s with
            | FParsec.CharParsers.Success (tlv, _, _) ->
                let eShadow = 
                    let wsSh = wordsShadowed tlv
                    if Set.isEmpty wsSh then ErrorFlags.NoError else
                    logError ll (sprintf "shadowed words: %s" (String.concat ", " wsSh))
                    ErrorFlags.WordShadowed
                let struct(dOpen,eOpen) = initOpen ll tlv 
                let cte0 = initCTE ll (eShadow ||| eOpen) dOpen
                let cte' = List.fold applyEnt cte0 (tlv.Ents)
                applyExport cte' (tlv.Export)
            | FParsec.CharParsers.Failure (msg, _, _) ->
                logError ll (sprintf "syntax error: %s" msg)
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
