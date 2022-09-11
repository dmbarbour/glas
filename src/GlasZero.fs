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
    type ModuleRef = 
        | Local of Word
        | Global of Word
    type HWord = (Word * Word list) // m/trig/sine
    type ImportList = (Word * Word) list

    /// AST for g0 programs.
    /// Comments are dropped. Words are not linked yet.
    type Block = Action list
    and Action =
        | Call of HWord     // word or macro call
        | Const of Value    // numbers, strings, etc.
        | Block of Block    // [foo]

    type Ent =
        | ImportAs of ModuleRef * Word          // import modulename as m
        | FromModule of ModuleRef * ImportList  // from modulename import foo, bar as baz, ...
        | FromData of Block * ImportList        // from [ Program ] import qux, baz as bar, ...
        | ProgDef of Word * Block               // prog word [ Program ]
        | MacroDef of Word * Block              // macro word [ Program ]
        | DataDef of Word * Block               // data word [ Program ]
        | StaticAssert of int64 * Block         // (line number recorded for error reporting)

    type ExportOpt =
        | ExportFn of Block
        | ExportList of (Word list)

    /// AST for g0 toplevel.
    type TopLevel = 
        { Open : ModuleRef option
        ; Ents : Ent list
        ; Export : ExportOpt option
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

        let wordFrag : P<string> =
            many1Satisfy2 isAsciiLower (fun c -> isAsciiLower c || isDigit c)
        
        let wordBody : P<string> =
            stringsSepBy1 wordFrag (pstring "-") 

        let parseWord : P<string> =
            (wordBody .>> wsep) <?> "word"

        let parseHWord : P<HWord> = 
            let hword = wordBody .>>. many (pchar '/' >>. wordBody) 
            (hword .>> wsep) <?> "d/word"
        
        let parseSymbol : P<Bits> =
            ((pchar '\'' >>. parseWord) <?> "symbol") |>> Value.label

        let hexN (cp : byte) : byte =
            // 0 to 9
            if ((48uy <= cp) && (cp <= 57uy)) then (cp - 48uy) else
            // A to F
            if ((65uy <= cp) && (cp <= 70uy)) then ((cp - 65uy) + 10uy) else
            // a to f
            if ((97uy <= cp) && (cp <= 102uy)) then ((cp - 97uy) + 10uy) else
            // wat?
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
        let parseAction, parseActionRef = 
            createParserForwardedToRef<Action, unit>()

        let parseBlock : P<Block> = 
            between (pchar '[' .>> ws) (pchar ']' .>> ws) (many parseAction)

        parseActionRef.Value <- 
            choice [
                parseData |>> Const
                parseBlock |>> Block
                parseHWord |>> Call
            ]

        let parseModuleRef : P<ModuleRef> =
            choice [
                pstring "./" >>. parseWord |>> Local
                parseWord |>> Global
            ]

        let parseOpen : P<ModuleRef> =
            kwstr "open" >>. parseModuleRef

        let parseImportWord : P<(Word * Word)> =
            parseWord .>>. (opt (kwstr "as" >>. parseWord)) |>> 
                fun (w,optAsW) ->
                    let aw = Option.defaultValue w optAsW
                    (w,aw)

        let parseImportFrom : P<Ent> =
            let wordList = kwstr "import" >>. sepBy1 parseImportWord (pchar ',' .>> ws)
            kwstr "from" >>. choice [
                parseBlock .>>. wordList |>> FromData
                parseModuleRef .>>. wordList |>> FromModule
            ]
        
        let parseImportAs : P<Ent> =
            kwstr "import" >>. parseModuleRef .>>. (kwstr "as" >>. parseWord) |>> ImportAs

        let parseExport : P<ExportOpt> =
            let wordList = sepBy1 parseWord (pchar ',' .>> ws)
            kwstr "export" >>. choice [
                parseBlock |>> ExportFn
                wordList |>> ExportList
            ]

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
                parseImportAs
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
            let! exportFn = opt (parseExport)
            do! eof
            return { Open = optOpen; Ents = lEnts; Export = exportFn } 
        }

    /// Words called from a single block.
    let rec wordsCalledBlock (b:Block) =
        let fnEnt ent =
            match ent with
            | Call (w,_) -> Set.singleton w // toplevel word only
            | Const _ -> Set.empty
            | Block p -> wordsCalledBlock p
        Set.unionMany (Seq.map fnEnt b)

    /// All words called from all top-level entries.
    let wordsCalled (tlv : TopLevel) : Set<Word> =
        let fnEnt ent =
            match ent with
            | ProgDef (_, b) | MacroDef (_, b) | StaticAssert (_, b) | DataDef (_, b) | FromData (b, _) -> 
                wordsCalledBlock b
            | FromImport _ | ImportAs _ -> Set.empty
        let entCalls = tlv.Ents |> Seq.map fnEnt |> Set.unionMany
        let exportCalls =
            match tlv.Export with
            | ExportFn b -> wordsCalledBlock b
            | ExportList _ -> Set.empty
        Set.union entCalls exportCalls

    /// All words explicitly defined in the top-level entries.
    let wordsDefined (tlv : TopLevel) : Set<Word> = 
        let fnEnt ent =
            match ent with
            | FromModule (_, lImports) | FromData (_, lImports) -> 
                Set.ofList (List.map snd lImports)
            | ProgDef (w, _) | MacroDef (w, _) | DataDef (w, _) | ImportAs (_,w) -> 
                Set.singleton w
            | StaticAssert _ -> 
                Set.empty
        Set.unionMany (Seq.map fnEnt tlv.Ents)

    /// Shadowing of words is (usually) not permitted within g0. This includes
    /// defining any word after it has been used, or defining a word twice.
    /// This function finds shadowing.
    let wordsShadowed (tlv : TopLevel) : Set<Word> =
        let rec fnImp wsSh wsDef lImports =
            match lImports with
            | [] -> (wsSh, wsDef)
            | ((_,w)::lImports') ->
                let wsSh' = if Set.contains w wsDef then Set.add w wsSh else wsSh
                let wsDef' = Set.add w wsDef
                fnImp wsSh' wsDef' lImports'
        let fnEnt (wsSh,wsDef) ent =
            match ent with
            | FromImport (_, lImports) -> fnImp wsSh wsDef lImports
            | FromData (b, lImports) ->
                let wsDef0 = Set.union (wordsCalledBlock b) wsDef
                fnImp wsSh wsDef0 lImports
            | ProgDef (w, b) | MacroDef (w, b) | DataDef (w, b) ->
                let wsDef0 = Set.union (wordsCalledBlock b) wsDef // treat called words as defined
                let wsSh' = if Set.contains w wsDef0 then Set.add w wsSh else wsSh
                let wsDef' = Set.add w wsDef0
                (wsSh', wsDef')
            | StaticAssert (_, b) ->
                let wsDef' = Set.union (wordsCalledBlock b) wsDef
                (wsSh, wsDef')
        List.fold fnEnt (Set.empty, Set.empty) tlv.Ents |> fst

    /// Set of modules directly loaded (not counting compile-time effects).
    let modulesLoaded (tlv : TopLevel) : Set<ModuleRef> =
        let fnEnt ent =
            match ent with
            | FromImport (m, _) | ImportAs (m,_) -> Set.singleton m
            | ProgDef _ | MacroDef _ | StaticAssert _ | DataDef _ | FromData _ -> Set.empty
        let lOpen = tlv.Open |> Option.toList |> Set.ofList
        let lFrom = List.map fnEnt tlv.Ents |> Set.unionMany
        Set.union lOpen lFrom 

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
            let ar = stackArity p
            let eArity =
                if ArityDyn <> ar then ErrorFlags.NoError else 
                logError (cte.LogLoad) (sprintf "%s does not have static arity" (cte.DbgCx))
                ErrorFlags.BadStaticArity
            let cte' = { cte with Errors = eArity ||| cte.Errors }
            struct(cte', p)

        // The resulting values tend to be a little messy. Trying to prettify
        // them heuristically. Removing extraneous 'prog' headers. Flattening
        // tree-structured 'seq' calls. But only at the surface layer.
        //
        //   prog:do:Program                        non-annotated prog ops.
        //   seq:[seq:[...], op, seq:[...], ...]    deep sequences
        //
        let rec expandOp op =
            match op with
            | Prog (U, pDo) -> expandOp pDo
            | PSeq l -> l
            | _ -> FTList.singleton op

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
                let revOps' = p :: (addDataOpsRev revOps ds)
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
                let ops = addDataOpsRev revOps ds |> List.rev |> FTList.collect expandOp 
                let p = 
                    match ops.T with
                    | FT.Single op -> op.V
                    | _ -> PSeq ops
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
                | Some results ->
                    if not (List.isEmpty results) then
                        logInfoV ll (sprintf "%s passed with outputs" dbg) 
                                    (Value.ofFTList (FTList.ofList results))  
                    true // any number of results is okay
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
