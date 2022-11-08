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
    type HWord = (struct(Word * Word list)) // m/trig/sine
    type ImportList = (struct(Word * Word)) list

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
        | ExportList of ImportList

    /// AST for g0 toplevel.
    type TopLevel = 
        { Open : ModuleRef option
        ; Ents : Ent list
        ; Export : ExportOpt option
        }

    let hwstr (struct(w,ws)) =
        String.concat "/" (w::ws)
    
    let mstr m =
        match m with
        | Global s -> s
        | Local s -> "./" + s

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
            (hword .>> wsep) <?> "d/word" |>> fun (w,ws) -> struct(w,ws)
        
        let parseSymbol : P<Value> =
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

        let consNbl (nbl : byte) (b : Value) : Value =
            let inline cb n acc = Value.consStemBit (0uy <> ((1uy <<< n) &&& nbl)) acc
            b |> cb 0 |> cb 1 |> cb 2 |> cb 3

        let parseHex : P<Value> =
            pstring "0x" >>. manySatisfy isHex .>> wsep |>> fun s -> 
                let fn cp acc = consNbl (hexN cp) acc
                let arr = System.Text.Encoding.ASCII.GetBytes(s)
                Array.foldBack fn arr Value.unit

        let isBinChar c = 
            (('0' = c) || ('1' = c))

        let parseBin : P<Value> =
            pstring "0b" >>. manySatisfy isBinChar .>> wsep |>> fun s ->
                let fn cp acc = Value.consStemBit (48uy <> cp) acc
                let arr = System.Text.Encoding.ASCII.GetBytes(s)
                Array.foldBack fn arr Value.unit

        let parseInt : P<Value> =
            let pzero = pstring "0"
            let isNzDigit c = (isDigit c) && (c <> '0')
            let ppos = many1Satisfy2 (isNzDigit) (isDigit)
            let pneg = pstring "-" .>>. ppos |>> fun (s,n) -> s + n
            choice [pzero; ppos; pneg ] .>> wsep |>> fun (s:string) -> 
                Value.ofInt (int64(s))

        let isStrChar (c : char) : bool =
            let cp = int c
            (cp >= 32) && (cp <= 126) && (cp <> 34)

        let parseString : P<string> =
            pchar '"' >>. manySatisfy isStrChar .>> pchar '"' .>> wsep

        let parseBitString : P<Value> =
            choice [parseBin; parseHex; parseSymbol; parseInt ]

        let parseData : P<Value> =
            choice [
                parseBitString 
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

        let parseImportList : P<ImportList> =
            let pw = parseWord .>>. (opt (kwstr "as" >>. parseWord)) |>> 
                        fun (w0,optAsW) -> struct(w0, Option.defaultValue w0 optAsW)
            sepBy1 pw (pchar ',' .>> ws)

        let parseImportFrom : P<Ent> =
            let wordList = kwstr "import" >>. parseImportList
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
                parseImportList |>> ExportList
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
    let rec wordsCalledBlock (b:Block) : Set<Word> =
        let fnEnt ent =
            match ent with
            | Call (struct(w,_)) -> Set.singleton w // toplevel word only
            | Const _ -> Set.empty
            | Block p -> wordsCalledBlock p
        b |> Seq.map fnEnt |> Set.unionMany

    /// All words called from all top-level entries.
    let wordsCalled (tlv : TopLevel) : Set<Word> =
        let fnEnt ent =
            match ent with
            | ProgDef (_, b) | MacroDef (_, b) | StaticAssert (_, b) | DataDef (_, b) | FromData (b, _) -> 
                wordsCalledBlock b
            | FromModule _ | ImportAs _ -> Set.empty
        let openCalls = Set.empty
        let entCalls = tlv.Ents |> Seq.map fnEnt |> Set.unionMany
        let exportCalls =
            match tlv.Export with
            | Some (ExportFn b) -> wordsCalledBlock b
            | _ -> Set.empty
        Set.unionMany [openCalls; entCalls; exportCalls]

    /// All words explicitly defined in the top-level entries.
    let wordsDefined (tlv : TopLevel) : Set<Word> = 
        let fnEnt ent =
            match ent with
            | FromModule (_, lImports) | FromData (_, lImports) -> 
                let ssnd (struct(_,b)) = b
                Set.ofList (List.map ssnd lImports)
            | ProgDef (w, _) | MacroDef (w, _) | DataDef (w, _) | ImportAs (_,w) -> 
                Set.singleton w
            | StaticAssert _ -> 
                Set.empty
        Set.unionMany (Seq.map fnEnt tlv.Ents)

    /// Shadowing of words is not permitted within g0. This includes defining
    /// a word after it has been used, or defining a word twice.
    let wordsShadowed (tlv : TopLevel) : Set<Word> =
        let addDef w (struct(wsSh,wsDef))  = 
            let wsSh' = if Set.contains w wsDef then Set.add w wsSh else wsSh
            let wsDef' = Set.add w wsDef
            struct(wsSh', wsDef')
        let addBlock b (struct(wsSh, wsDef)) =
            // any called word is implicitly defined earlier, but cannot shadow
            let wsDef' = Set.union (wordsCalledBlock b) wsDef
            struct(wsSh, wsDef')
        let addImportList lImports acc0 =
            let fn acc (struct(_,w)) = addDef w acc 
            List.fold fn acc0 lImports
        let addEnt acc ent =
            match ent with
            | FromModule (_, lImports) -> 
                acc |> addImportList lImports
            | FromData (b, lImports) -> 
                acc |> addBlock b |> addImportList lImports
            | ProgDef (w, b) | MacroDef (w, b) | DataDef (w, b) ->
                acc |> addBlock b |> addDef w   // b before w to catch recursion
            | StaticAssert (_, b) ->
                acc |> addBlock b
            | ImportAs (_, w) ->
                acc |> addDef w
        let acc0 = struct(Set.empty, Set.empty)
        let inline shadowedWords (struct(wsSh, _)) = wsSh
        let wsShOpen = 
            // 'open' cannot shadow anything 
            Set.empty 
        let wsShEnts = 
            List.fold addEnt acc0 tlv.Ents |> shadowedWords
        let wsShExport = 
            match tlv.Export with
            | Some (ExportList lImports) -> 
                // duplicate 'as' entries result in shadowing
                addImportList lImports acc0 |> shadowedWords
            | Some (ExportFn _) -> Set.empty
            | None -> Set.empty
        Set.unionMany [wsShOpen; wsShEnts; wsShExport]
        
    /// Set of modules loaded via syntax (not including compile-time effects).
    let modulesLoaded (tlv : TopLevel) : Set<ModuleRef> =
        let fnEnt ent =
            match ent with
            | FromModule (m, _) | ImportAs (m,_) -> Set.singleton m
            | ProgDef _ | MacroDef _ | StaticAssert _ | DataDef _ | FromData _ -> Set.empty
        let openModule = tlv.Open |> Option.toList |> Set.ofList
        let entModules = tlv.Ents |> List.map fnEnt |> Set.unionMany
        let exportModules = Set.empty
        Set.unionMany [openModule; entModules; exportModules]

    module Compile =
        open Effects
        open ProgVal
        open ProgEval
        open Value

        [<System.FlagsAttribute>]
        type ErrorFlags =
            | NoError           = 0b0000000000000000
            | SyntaxError       = 0b0000000000000001    // the program doesn't parse
            | WordShadowed      = 0b0000000000000010    // at least one word is shadowed
            | LoadError         = 0b0000000000000100    // 'open' or 'from' fails to load 
            | WordUndefined     = 0b0000000000001000    // called or imported word is undefined
            | UnknownDefType    = 0b0000000000010000    // called word has unrecognized def type
            | BadStaticArity    = 0b0000000000100000    // failed static arity check
            | BadStaticEval     = 0b0000000001000000    // static eval of assert/macro/export/data.

        type LinkOutcome =
            | LinkProg of Value
            | LinkMacro of Value
            | LinkData of Value
            | LinkFail of ErrorFlags

        // lookup, allowing for hierarchical words.
        let rec tryLink (struct(w,ws)) d =
            match Value.record_lookup (Value.label w) d with
            | ValueSome (Value.Variant "data" d') ->
                match ws with
                | (w'::ws') -> tryLink (struct(w',ws')) d'
                | [] -> LinkData d'
            | ValueSome ((Value.Variant "prog" _) as p) when (List.isEmpty ws) -> 
                LinkProg p
            | ValueSome (Value.Variant "macro" m) when (List.isEmpty ws) -> 
                LinkMacro m
            | ValueSome v when (List.isEmpty ws) -> 
                LinkFail (ErrorFlags.UnknownDefType)
            | _ ->
                LinkFail (ErrorFlags.WordUndefined)

        [<Struct>]
        type CTE =
            { Dict   : Value
            ; Alerts : Set<HWord>  
            ; Errors : ErrorFlags
            ; LL     : IEffHandler    // log and load effects
            ; DbgCx  : string         // metadata for error reporting
            }

        let inline addErr e (cte : CTE) = 
            { cte with 
                Errors = (e ||| cte.Errors) 
            }

        let addAlert (hw:HWord) (e:ErrorFlags) (cte:CTE) : CTE =
            if not (Set.contains hw cte.Alerts) then
                logError (cte.LL) (sprintf "issue with %s: %s" (hwstr hw) (string e))
            { cte with 
                Errors = (e ||| cte.Errors) 
                Alerts = (Set.add hw cte.Alerts) 
            }

        let inline addDef (w:Word) (vDef:Value) (cte:CTE) : CTE =
            let wL = Value.label w
            { cte with Dict = Value.record_insert wL vDef (cte.Dict) }

        let private addDataOpsRev (revOps:Program list) (ds:Value list) =
            List.append (List.map (ProgVal.Data) ds) revOps

        // Very lightweight simplification:
        //
        //   prog:do:Program                        non-annotated prog.
        //   seq:[seq:[...], op, seq:[...], ...]    deep sequences
        //
        let rec expandOp op =
            match op with
            | Prog (U, pDo) ->
                expandOp pDo
            | PSeq (List l) -> l
            | _ -> Rope.singleton op

        // list of ops to a program with a few simplifications
        let opsToProg ops = 
            let ops' = ops |> Seq.map expandOp |> Rope.concat
            match ops' with
            | Rope.ViewL(op, Leaf) -> op
            | _ -> PSeq (ofTerm ops')

        // add any extra compile-time effects
        let ctEval progVal cte =
            // ability to access dictionary via 'load:dict:Path'
            let lld = 
                { new IEffHandler with
                    member __.Eff v = 
                        match v with
                        | Variant "load" (Variant "dict" (Bits path)) ->
                            Value.record_lookup path cte.Dict
                        | _ -> cte.LL.Eff(v)
                interface ITransactional with
                    member __.Try () = cte.LL.Try()
                    member __.Commit () = cte.LL.Commit()
                    member __.Abort () = cte.LL.Abort()
                }
            eval progVal lld

        // Compile a program block into a value. 
        let rec compileBlock (dbgCx:string) (cte0:CTE) (b:Block) : struct(CTE * Program) =
            let cte1 = { cte0 with DbgCx = dbgCx }
            let struct(cte2,p) = _compileBlock cte1 [] [] b
            struct({cte2 with DbgCx = cte0.DbgCx }, p)
        and private _compileCallProg cte revOps ds p b =
            // any effect will cause failure here.
            match pureEval p ds with
            | Some ds' ->
                _compileBlock cte revOps ds' b
            | None ->
                let revOps' = p :: (addDataOpsRev revOps ds)
                _compileBlock cte revOps' [] b
        and private _compileBadCall cte revOps ds b =
            // replace failed operation with 'tbd'
            let tbdOp = TBD (Value.symbol "undefined")
            let revOps' = tbdOp :: addDataOpsRev revOps ds
            _compileBlock cte revOps' [] b
        and private _compileBlock (cte0:CTE) (revOps:Program list) (ds:Value list) (b:Block) =
            match b with
            | ((Block p)::b') ->
                let ixEnd = 1 + List.length b'
                let dbg = cte0.DbgCx + (sprintf " block -%d" ixEnd)
                let struct(cte', pv) = compileBlock dbg cte0 p
                _compileBlock cte' revOps (pv::ds) b'
            | ((Const v)::b') ->
                _compileBlock cte0 revOps (v::ds) b'
            | ((Call hw)::b') ->
                match tryLink hw (cte0.Dict) with
                | LinkData d ->
                    _compileBlock cte0 revOps (d::ds) b'
                | LinkProg p ->
                    _compileCallProg cte0 revOps ds p b'
                | LinkMacro m ->
                    match ctEval m cte0 ds with
                    | Some (p :: ds') ->
                        _compileCallProg cte0 revOps ds' p b'
                    | _ ->
                        logError (cte0.LL) (sprintf "macro call %s failed in %s" (hwstr hw) (cte0.DbgCx))
                        let cte' = addErr (ErrorFlags.BadStaticEval) cte0
                        _compileBadCall cte' revOps ds b'
                | LinkFail e ->
                    _compileBadCall (addAlert hw e cte0) revOps ds b'
            | [] ->
                let p = addDataOpsRev revOps ds |> List.rev |> opsToProg
                let ar = stackArity p
                let cte' =
                    if ArityDyn <> ar then cte0 else
                    logError (cte0.LL) (sprintf "%s does not have static arity" (cte0.DbgCx))
                    addErr (ErrorFlags.BadStaticArity) cte0
                struct(cte',p) 

        let loadModule (ll:IEffHandler) (m:ModuleRef) =
            let mRefVal = 
                match m with
                | Global s -> Value.variant "global" (Value.ofString s)
                | Local s -> Value.variant "local" (Value.ofString s)
            ll.Eff(Value.variant "load" mRefVal)

        let initDict ll tlv =
            match tlv.Open with
            | None -> struct(Value.unit, ErrorFlags.NoError)
            | Some m ->
                match loadModule ll m with
                | ValueNone ->
                    logError ll (sprintf "'open' failed to load %s" (mstr m))
                    struct(Value.unit, ErrorFlags.LoadError)
                | ValueSome d0 ->
                    // erase words defined within this file.
                    // this simplifies 'load:dict' effects.
                    let ws = wordsDefined tlv
                    let fn d w = Value.record_delete (Value.label w) d 
                    let d' = Set.fold fn d0 ws 
                    struct(d', ErrorFlags.NoError)

        let applyImportAs (cte0:CTE) (m:ModuleRef) (w:Word) : CTE =
            let ll = cte0.LL
            match loadModule ll m with
            | ValueNone ->
                logError ll (sprintf "failed to load module %s" (mstr m))
                addErr (ErrorFlags.LoadError) cte0
            | ValueSome v ->
                addDef w (Value.variant "data" v) cte0

        let applyImportList (cte0:CTE) (ws:ImportList) (dSrc:Value) =
            let applyImport cte (struct(w,asW)) =
                match Value.record_lookup (Value.label w) dSrc with
                | ValueSome wDef ->
                    addDef asW wDef cte
                | ValueNone ->
                    addAlert (struct(asW,[])) (ErrorFlags.WordUndefined) cte
            List.fold applyImport cte0 ws

        let applyFromModule (cte0:CTE) (m:ModuleRef) (lImports:ImportList) =
            let ll = cte0.LL
            match loadModule ll m with
            | ValueNone ->
                logError ll (sprintf "failed to load module %s" (mstr m))
                addErr ErrorFlags.LoadError cte0
            | ValueSome dSrc ->
                applyImportList cte0 lImports dSrc

        let applyFromData (cte0:CTE) (b:Block) (lImports:ImportList) =
            let ll = cte0.LL
            let dbg = 
                match lImports with
                | struct(_,w)::_ -> (sprintf "def %s" w)
                | [] -> failwith "invalid from data"
            let struct(cte', pData) = compileBlock dbg cte0 b
            match ctEval pData cte' [] with
            | Some [dSrc] ->
                applyImportList cte' lImports dSrc
            | Some _ ->
                logError ll (sprintf "%s arity error" dbg)
                addErr ErrorFlags.BadStaticArity cte'
            | None -> 
                logError ll (sprintf "%s eval failure" dbg)
                addErr ErrorFlags.BadStaticEval cte'

        let applyProgDef cte0 w b =
            let dbg = sprintf "def %s" w
            let struct(cte', p) = compileBlock dbg cte0 b
            let pDef = // avoid doubling "prog" header if present.
                match p with
                | Prog _ -> p
                | _ -> Prog(Value.unit, p)
            addDef w pDef cte'

        let applyMacroDef cte0 w b = 
            let dbg = sprintf "def %s" w
            let struct(cte', p) = compileBlock dbg cte0 b 
            addDef w (Value.variant "macro" p) cte'

        let applyDataDef cte0 w b =
            let ll = cte0.LL
            let dbg = sprintf "def %s" w
            let struct(cte', pData) = compileBlock dbg cte0 b
            match ctEval pData cte' [] with
            | Some [vData] -> 
                addDef w (Value.variant "data" vData) cte'
            | Some _ ->
                logError ll (sprintf "arity error %s" dbg) 
                addErr (ErrorFlags.BadStaticArity) cte'
            | None ->
                logError ll (sprintf "eval failed %s" dbg)
                addErr (ErrorFlags.BadStaticEval) cte'

        let applyStaticAssert cte0 ln b =
            let ll = cte0.LL
            let dbg = sprintf "assert ln %d" ln
            let struct(cte', pAssert) = compileBlock dbg cte0 b
            match ctEval pAssert cte' [] with
            | Some results ->
                if not (List.isEmpty results) then
                    logInfoV ll (sprintf "%s passed with outputs" dbg) 
                                (Value.ofList  results) 
                cte'
            | None ->
                logError ll (sprintf "%s failed" dbg)
                addErr (ErrorFlags.BadStaticEval) cte'
            
        let applyEnt (cte:CTE) (ent:Ent) : CTE =
            match ent with
            | ImportAs (m, w) -> applyImportAs cte m w
            | FromModule (m, lImports) -> applyFromModule cte m lImports
            | FromData (b, lImports) -> applyFromData cte b lImports
            | ProgDef (w, b) -> applyProgDef cte w b
            | MacroDef (w, b) -> applyMacroDef cte w b
            | DataDef (w, b) -> applyDataDef cte w b
            | StaticAssert (ln, b) -> applyStaticAssert cte ln b

        let applyExportFn cte0 b = 
            let ll = cte0.LL
            let struct(cte', pExport) = compileBlock "export" cte0 b
            match ctEval pExport cte' [] with
            | Some [vExport] -> 
                { cte' with Dict = vExport }
            | Some vs -> // requires arity error
                logError ll "export arity error"
                let d' = vs |> List.tryHead |> Option.defaultValue (Value.unit)
                { cte' with Dict = d' } |> addErr ErrorFlags.BadStaticArity
            | None ->
                logError ll "export eval failed"
                { cte' with Dict = Value.unit } |> addErr ErrorFlags.BadStaticEval  

        let applyExportList cte ws =
            // clear Dict then re-import from original Dict.
            applyImportList { cte with Dict = Value.unit } ws (cte.Dict)

        let applyExport cte exportOpt =
            match exportOpt with
            | Some (ExportFn b) -> applyExportFn cte b
            | Some (ExportList ws) -> applyExportList cte ws
            | None -> cte


        let initCTE ll err d0 =
            { Dict = d0
            ; LL = ll
            ; Errors = err
            ; Alerts = Set.empty
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
                let struct(dOpen,eOpen) = initDict ll tlv 
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
    let compile (ll:Effects.IEffHandler) (s:string) : Value voption =
        let cte = Compile.compile ll s 
        if Compile.ErrorFlags.NoError = cte.Errors 
            then ValueSome (cte.Dict) 
            else ValueNone
