namespace Glas

/// This file implements the g0 (Glas Zero) bootstrap parser and compiler. 
/// This implementation of g0 should be replaced by a language-g0 module
/// 'compile' program after bootstrap completes. 
/// 
/// The g0 language is similar to a Forth. A program is a sequence of words
/// and data, which are applied to manipulate a data stack. Features include
/// access to macros for metaprogramming, importing definitions from modules,
/// and defining local variables for data plumbing within a program.
///
/// The g0 language has no built-in definitions. We use macros to define the
/// primitive Glas program operators. Side-effects are also controlled via an
/// algebraic effects model.
///
/// The goal for g0 is to be reasonably comfortable for programming a foundation
/// of the Glas language system.
///
module Zero =
    type Word = string
    type ModuleName = Word

    /// AST for g0 programs.
    /// Comments are dropped. Words are not linked yet.
    type Prog = ProgStep list
    and ProgStep =
        | Call of Word  // word
        | Sym of Bits // 'word, 42, 0x2A, 0b101010
        | Str of string // "hello, world!"
        | Env of With:Prog * Do:Prog        // with [H] do [P]
        | Loop of While:Prog * Do:Prog      // while [C] do [P]
        | Cond of Try:Prog * Then:Prog * Else:Prog  // try [C] then [A] else [B]
        | Dip of Prog   // dip [P]

    [<Struct>]
    type Import = Import of Word:Word * As: Word option

    [<Struct>]
    type ImportFrom = From of Src : ModuleName * Imports : Import list

    [<Struct>]
    type Def = Prog of Name: Word * Body: Prog

    /// AST for g0 toplevel.
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

    type ShallowValidation = 
        | DuplicateDefs of Word list
        | UsedBeforeDef of Word list
        | LooksOkay 

    let rec private _findDupWords acc l =
        match l with
        | (hd::l') ->
            let acc' = if List.contains hd l' then (hd::acc) else acc
            _findDupWords acc' l'
        | [] -> Set.ofList acc

    let rec private _findUseBeforeDef acc defs =
        match defs with
        | (d::defs') -> 
            let calls = wordsCalledFromDef d  
            // to catch recursive definitions, word being defined is also undefined.
            let ubd = defs  |> List.map wordDefined 
                            |> List.filter (fun w -> Set.contains w calls) 
                            |> Set.ofList
            _findUseBeforeDef (Set.union acc ubd) defs'
        | [] -> acc

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
        let lDups = _findDupWords [] lDefs |> Set.toList
        if not (List.isEmpty lDups) then DuplicateDefs lDups else
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

        let load (ll:IEffHandler) (m:ModuleName) : Value option =
            let r = ll.Eff(Value.variant "load" (Value.symbol m))
            if Option.isNone r then
                logWarn ll (sprintf "failed to load module %s" m)
            r

        // open a module for initial namespace
        // (g0 no longer has reserved words)        
        let openModule ll m =
            match load ll m with
            | None -> Value.unit
            | Some d0 -> d0

        let applyFrom (ll:IEffHandler) (d0:Value) (From (Src=m; Imports=lImp)) = 
            match load ll m with
            | None -> d0 
            | Some dSrc ->
                let mutable d = d0
                for Import (Word=wSrc; As=asOpt) in lImp do
                    let wDst = Option.defaultValue wSrc asOpt
                    match Value.record_lookup (Value.label wSrc) dSrc with
                    | Some v -> 
                        d <- Value.record_insert (Value.label wDst) v d
                    | None ->
                        d <- Value.record_delete (Value.label wDst) d
                        logWarn ll (sprintf "module %s does not define word '%s'" m wSrc)
                d

        (*
        let tryLinkCall d0 w =
            let r = Value.record_lookup (Value.label w) d0
            match r with
            | Some (Value.Variant "prog" p) ->
                match p with
                | Value.Variant "do" pBody -> Some pBody // flatten `prog:do:P => P`
                | _ -> r // preserve annotations
            | Some (Value.Variant "data" _) -> r
            | _ -> None

        let rec linkProg (d0:Value) (p:Prog) =
            match p with
            | [step] -> linkStep d0 step
            | lSteps -> 
                let lV = List.map (linkStep d0) lSteps
                Value.variant "seq" (Value.ofFTList (FTList.ofList lV))
        and linkStep d0 step =
            match step with
            | Call w ->
                match tryLinkCall d0 w with
                | Some def -> def
                | None -> Value.symbol "fail" 
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

        let private uncheckedCompileTLV (ll:IEffHandler) (tlv:TopLevel) : Value =
            let mutable d = 
                match tlv.Open with
                | None -> Value.unit
                | Some m -> openModule ll m
            for f in tlv.From do
                d <- applyFrom ll d f
            for Prog (Name=w; Body=pBody) in tlv.Defs do
                let vBody = linkProg d pBody
                let vProg = Value.variant "prog" (Value.variant "do" vBody)
                // TODO: consider optimizing the program. 
                // Could leave this to language-g0 bootstrap. Depends on performance.
                d <- Value.record_insert (Value.label w) vProg d
            d

        /// The output for compiling a g0 program is the dictionary representing
        /// the namespace available at the end of file. This compiler does not 
        /// handle the parsing step or detection of load cycles, nor arity errors.
        let compileTLV (ll:IEffHandler) (tlv:TopLevel) : Value option =
            //logInfo ll "performing shallow validation of g0 program"
            match shallowValidation tlv with
            | LooksOkay -> 
                let d = uncheckedCompileTLV ll tlv
                let lUndef = 
                        List.map wordsCalledFromDef (tlv.Defs) 
                            |> Set.unionMany
                            |> Set.filter (tryLinkCall d >> Option.isNone)
                            |> Set.toList
                if not (List.isEmpty lUndef) then
                    logWarn ll (sprintf "undefined words in use: %A" lUndef)
                Some d
            | issues -> 
                logError ll (sprintf "validation error: %A" issues)
                None

        let compile (ll:IEffHandler) (s:string) : Value option =
            //logInfo ll "using built-in g0 compile function"
            match FParsec.CharParsers.run Parser.parseTopLevel s with
            | FParsec.CharParsers.Success (tlv, _, _) -> 
                //logInfo ll "parse successful!"
                compileTLV ll tlv
            | FParsec.CharParsers.Failure (msg, _, _) ->
                logError ll (sprintf "built-in g0 parse error:\n%s" msg)
                None

*)
        // stub
        let compile (ll:IEffHandler) (s:string) : Value option =
            None