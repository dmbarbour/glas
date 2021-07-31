namespace Glas

/// This file implements the g0 (Glas Zero) bootstrap syntax. This implementation
/// of g0 should be replaced by a language-g0 module after bootstrap completes. 
///
/// The g0 language is not good for day to day use. There is no support for static 
/// higher order programs or macros, thus list processing and parser combinators 
/// are awkward to express. Embedded data is limited to bitstrings. Data plumbing
/// is often inconvenient due to lack of variables.
/// 
/// The extreme simplicity of g0 is intentional. The many deficiencies of g0 can
/// be resolved by defining better languages and implementing langage modules.
///
module Zero =
    type Word = string
    type ModuleName = Word

    /// AST for Zero is very close to Glas program model. 
    /// However, we still preserve words at this layer.
    type Prog = ProgStep list
    and ProgStep =
        | Call of Word  // word
        | Sym of Bits // 'word, 42, 0x2A, 0b101010, etc.
        | Str of string // "hello, world!"
        | Env of With:Prog * Do:Prog
        | Loop of While:Prog * Do:Prog
        | Cond of Try:Prog * Then:Prog * Else:Prog
        | Dip of Prog

    [<Struct>]
    type Import = Import of Word:Word * As: Word option

    [<Struct>]
    type ImportFrom = From of Src : ModuleName * Imports : Import list

    [<Struct>]
    type Def = Prog of Name: Word * Body: Prog

    [<Struct>]
    type TopLevel = 
        { Open : ModuleName option
        ; From : ImportFrom list
        ; Defs : Def list
        }

    module Parser =
        // I'm using FParsec to provide decent error messages without too much effort.

        open FParsec
        type P<'T> = Parser<'T,unit>

        let lineComment : P<unit> =
            (pchar ';' <?> "`; line comment`") >>. skipManyTill anyChar newline 

        let ws : P<unit> =
            spaces >>. (skipMany (lineComment >>. spaces) <?> "spaces and comments")

        let isBadWordSep c =
            isAsciiLetter c || isDigit c || 
            (c = '-') || (c = '\'') || (c = '"')

        let wsep : P<unit> =
            nextCharSatisfiesNot isBadWordSep .>> ws

        let kwstr s : P<unit> = 
            pstring s >>. wsep

        let parseWord : P<string> =
            let frag = many1Satisfy2 isAsciiLower (fun c -> isAsciiLower c || isDigit c)
            stringsSepBy1 frag (pstring "-") .>> wsep 

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

        let parseData : P<Bits> =
            parseBin <|> parseHex <|> parseSymbol <|> parseNat  

        let isStrChar (c : char) : bool =
            let cp = int c
            (cp >= 32) && (cp <= 126) && (cp <> 34)

        let parseString : P<string> =
            pchar '"' >>. manySatisfy isStrChar .>> pchar '"' .>> wsep

        // FParsec's approach to recursive parser definitions is a little awkward.
        let parseProgStep, parseProgStepRef = createParserForwardedToRef<ProgStep, unit>()

        let parseProgBody = 
            many parseProgStep

        let parseProgBlock : P<Prog> = 
            between (pchar '[' .>> ws) (pchar ']' .>> ws) parseProgBody

        //dip [ Program ]  
        let parseDip = parse {
            do! kwstr "dip"
            let! pDip = parseProgBlock
            return Dip pDip
        }

        // while [ C ] do [ B ]
        let parseLoop = parse {
            do! kwstr "while"
            let! pWhile = parseProgBlock
            do! kwstr "do"
            let! pDo = parseProgBlock
            return Loop (While=pWhile, Do=pDo)
        }

        // try [ C ] then [ A ] else [ B ]
        let parseCond = parse {
            do! kwstr "try"
            let! pTry = parseProgBlock
            do! kwstr "then"
            let! pThen = parseProgBlock
            do! kwstr "else"
            let! pElse = parseProgBlock
            return Cond (Try=pTry, Then=pThen, Else=pElse)
        }

        // with [ H ] do [ P ]
        let parseEnv = parse {
            do! kwstr "with"
            let! pWith = parseProgBlock
            do! kwstr "do"
            let! pDo = parseProgBlock
            return Env (With=pWith, Do=pDo)
        }

        parseProgStepRef := 
            choice [
                parseData |>> Sym
                parseString |>> Str
                parseEnv
                parseLoop
                parseCond
                parseDip
                // NOTE: word is last to avoid conflict with with/while/try/dip keywords
                parseWord |>> Call 
            ]

        let parseOpen : P<ModuleName> =
            kwstr "open" >>. parseWord

        let parseImport : P<Import> =
            parseWord .>>. (opt (kwstr "as" >>. parseWord)) 
                |>> fun (w,aw) -> Import(Word=w,As=aw)

        let parseFrom : P<ImportFrom> =
            kwstr "from" >>. parseWord .>>. (kwstr "import" >>. sepBy1 parseImport (pchar ',' .>> ws)) 
                |>> fun (src,l) -> From (Src=src, Imports=l)

        let parseProgDef : P<Def> = 
            kwstr "prog" >>. parseWord .>>. parseProgBlock 
                |>> fun (w, p) -> Prog (Name=w, Body=p)

        let parseDef : P<Def> =
            // only one definition type for now.
            // The g0 language is unlikely to add support for defining types, macros, etc.
            parseProgDef 

        let parseTopLevel = parse {
            do! ws
            let! optOpen = opt parseOpen
            let! lFrom = many parseFrom
            let! lDefs = many parseDef
            do! eof
            return { Open = optOpen; From = lFrom; Defs = lDefs } 
        }

    let wordImported imp =
        match imp with
        | Import(As=Some w) -> w
        | Import(Word=w) -> w 

    let wordsFrom src = 
        match src with
        | From (Imports=imps) -> List.map wordImported imps

    let wordDefined def =
        match def with
        | Prog (Name=w) -> w

    /// Obtain a list of explicitly defined words from a g0 program.
    /// A word defined twice will be listed twice.
    let definedWords tlv =
        let oldDefs = List.collect wordsFrom (tlv.From)
        let newDefs = List.map wordDefined (tlv.Defs)
        List.append oldDefs newDefs 

    let rec private _wordsCalledBlock acc block =
        List.fold _wordsCalledStep acc block
    and private _wordsCalledStep acc step =
        match step with
        | Call w -> acc |> Set.add w
        | Sym _ -> acc
        | Str _ -> acc
        | Env (With=pWith; Do=pDo) -> 
            List.fold _wordsCalledBlock acc [pWith; pDo]
        | Loop (While=pWhile; Do=pDo) ->
            List.fold _wordsCalledBlock acc [pWhile; pDo]
        | Cond (Try=pTry; Then=pThen; Else=pElse) ->
            List.fold _wordsCalledBlock acc [pTry; pThen; pElse]
        | Dip p -> _wordsCalledBlock acc p

    /// Obtain a set of all words called from a program.
    let wordsCalledFromProg (p : Prog) : Set<Word> =
        _wordsCalledBlock (Set.empty) p 

    let wordsCalledFromDef (d : Def) : Set<Word> =
        match d with
        | Prog (Body=p) -> wordsCalledFromProg p

    /// The g0 syntax re a ton of reserved words.
    /// This includes all the basic ops.
    let reservedWords = 
        [ "dip"
        ; "cond"; "try"; "then"; "else"
        ; "loop"; "while"; "do"
        ; "env"; "with"; "do"
        ; "prog"
        // namespace
        ; "from"; "open"; "import"; "as"
        ] 
        |> List.append (List.map Program.opStr Program.op_list) 
        |> Set.ofList

    let isReservedWord w =
        Set.contains w reservedWords

    type ShallowValidation = 
        | DuplicateDefs of Word list
        | ReservedDefs of Word list
        | UsedBeforeDef of Word list
        | LooksOkay 

    let rec private _findDupWords acc l =
        match l with
        | (hd::l') ->
            let acc' = if List.contains hd l' then (hd::acc) else acc
            _findDupWords acc' l'
        | [] -> acc

    let rec private _findUseBeforeDef acc defs =
        match defs with
        | (d::defs') when not (List.isEmpty defs') -> 
            let calls = wordsCalledFromDef d  
            let ubd = defs' |> List.map wordDefined 
                            |> List.filter (fun w -> Set.contains w calls) 
                            |> Set.ofList
            _findUseBeforeDef (Set.union acc ubd) defs'
        | _ -> acc

    /// A shallow validation of a g0 program:
    ///   - detects duplicate explicit imports/definitions  
    ///   - detects if reserved words are imported or defined 
    ///   - detects if defined word is is used before defined 
    /// The g0 syntax doesn't support shadowing: each word must have a
    /// constant meaning within a file. Additionally, within a file, a
    /// word must be defined before it is used.
    ///
    /// Shallow validation will be performed by the compiler. If it fails,
    /// compilation will also fail.
    let shallowValidation tlv =
        let lDefs = definedWords tlv
        let lDups = _findDupWords [] lDefs
        if not (List.isEmpty lDups) then DuplicateDefs lDups else
        let lReserved = List.filter isReservedWord lDefs 
        if not (List.isEmpty lReserved) then ReservedDefs lReserved else
        let lUBD = Set.toList <| _findUseBeforeDef (Set.empty) (tlv.Defs)
        if not (List.isEmpty lUBD) then UsedBeforeDef lUBD else
        LooksOkay

    // The namespace during compilation is represented by a dictionary Value.
    // The g0 language can understand 'prog' and 'data' definitions, and will
    // treat other definitions same as undefined (equal to 'fail'). Load and
    // log operations during compile are supported by abstract effects handler,
    // same as they would be for a user-defined language module. Bootstrap is
    // mostly about defining special effects handlers.
    //
    // No transactional effects are required in this use-case. But it is useful
    // to track context for log messages, e.g. which file we're processing.
    //
    // The main options for compilation are:
    //
    // - directly build the Glas program model in F#. This requires parsing
    //   programs from imported values.
    // - build the Glas value that represents the program. We might parse the
    //   final results only if we need to validate and run a program.
    //
    // The latter option is superior for performance, since it avoids a lot of
    // rework. So it's the path I'll take here.
    module Compile =
        open Glas.Effects

        type Dict = Value

        // log and load
        type LL = IEffHandler

        let logMsg hdr msg =
            Value.variant hdr (Value.ofString msg)

        let info = "info"
        let warn = "warn"
        let error = "error"

        // log:Msg effect - response should always be unit
        let log (ll:LL) (msg:Value) : unit =
            let r = ll.Eff(Value.variant "log" msg)
            match r with
            | Some Value.U -> ()
            | _ -> failwithf "unexpected result when logging: %A" r

        let load (ll:LL) (m:ModuleName) : Dict option  =
            let r = ll.Eff(Value.variant "load" (Value.symbol m))
            if Option.isNone r then
                let msg = sprintf "failed to load module %s" m
                log ll <| logMsg warn msg
            r

        let applyImport ll dSrc m dDst imp =
            match imp with
            | Import (Word=wSrc; As=asOpt) ->
                let wDst = Option.defaultValue wSrc asOpt
                match Value.record_lookup (Value.label wSrc) dSrc with
                | Some v ->
                    Value.record_insert (Value.label wDst) v dDst
                | None ->
                    let msg = sprintf "module %s does not export word %s" m wSrc
                    log ll <| logMsg warn msg
                    dDst

        let applyFrom ll d0 f =
            match f with
            | From (Src=m; Imports=lImp) ->
                match load ll m with
                | Some dSrc -> List.fold (applyImport ll dSrc m) d0 lImp
                | None -> d0

        let okDefType v =
            match v with
            | Value.Variant "prog" _ -> true
            | Value.Variant "data" _ -> true
            | _ -> false

        let linkCall d0 w =
            match Value.record_lookup (Value.label w) d0 with
            | Some v when okDefType v -> v 
            | _ -> Value.symbol (Program.opStr Fail)

        let rec linkProg (d0:Dict) (p:Prog) =
            match p with
            | [step] -> linkStep d0 step
            | lSteps -> 
                let lV = List.map (linkStep d0) lSteps
                Value.variant "seq" (Value.ofFTList (FTList.ofList lV))
        and linkStep d0 step =
            match step with
            | Call w -> linkCall d0 w
            | Sym b -> Value.variant "data" (Value.ofBits b)
            | Str s -> Value.variant "data" (Value.ofString s)
            | Env (With=pWith; Do=pDo) ->
                let lV = List.map (linkProg d0) [pWith; pDo]
                Value.variant "env" (Value.asRecord ["with";"do"] lV)
            | Loop (While=pWhile; Do=pDo) ->
                let lV = List.map (linkProg d0) [pWhile; pDo]
                Value.variant "loop" (Value.asRecord ["while";"do"] lV)
            | Cond (Try=pTry; Then=pThen; Else=pElse) ->
                let lV = List.map (linkProg d0) [pTry; pThen; pElse]
                Value.variant "cond" (Value.asRecord ["try";"then";"else"] lV)
            | Dip pDip ->
                let vDip = linkProg d0 pDip
                Value.variant "dip" vDip

        let applyDef d0 def =
            match def with
            | Prog (Name=wDef; Body=pBody) ->
                let vBody = linkProg d0 pBody
                Value.record_insert (Value.label wDef) vBody d0

        /// Obtain all definitions from imports. 
        let importDefs (ll:LL) (tlv:TopLevel) : Dict =
            let rm d w = Value.record_delete (Value.label w) d
            // load the implicitly defined words from the opened module.
            let dOpen = 
                match tlv.Open with
                | Some m -> 
                    match load ll m with
                    | Some d -> d
                    | None -> Value.unit
                | None -> Value.unit
            // remove all explicitly defined words to resist shadowing.
            let dTrim = List.fold rm dOpen (definedWords tlv)
            // add all explicitly imported words.
            List.fold (applyFrom ll) dTrim (tlv.From)

        /// Warn for any undefined words that we actually call. The g0
        /// language allows for undefined words, simply warns then uses
        /// 'fail' in place of their definition. 
        /// 
        /// If a word is defined but is not 'prog' or 'data' we have the
        /// same behavior, so a warning is also given in these cases.
        let warnForTroublesomeWords (ll:LL) (d0:Value) (lDefs : Def list) =
            let defWords = List.map wordDefined lDefs |> Set.ofList 
            let calledWords = List.map wordsCalledFromDef lDefs |> Set.unionMany
            for w in calledWords do
                if Set.contains w defWords then () else
                match Value.record_lookup (Value.label w) d0 with
                | Some v ->
                    if okDefType v then () else
                    let msg = sprintf "calls to word %s fail: not a 'prog' or 'data'" w
                    log ll <| logMsg warn msg
                | None ->
                    let msg = sprintf "calls to word %s fail: undefined" w
                    log ll <| logMsg warn msg

        /// The output for compiling a g0 program is the dictionary representing
        /// the namespace available at the end of file. This compiler does not 
        /// handle the parsing step or detection of load cycles.
        let compileTLV (ll:LL) (tlv:TopLevel) : Value option =
            log ll (logMsg info "shallow validation of g0 program")
            match shallowValidation tlv with
            | LooksOkay -> 
                let d0 = importDefs ll tlv
                warnForTroublesomeWords ll d0 (tlv.Defs)
                Some <| List.fold applyDef d0 (tlv.Defs)
            | issues -> 
                let msg = sprintf "shallow validation error: %A" issues
                log ll <| logMsg error msg
                None

