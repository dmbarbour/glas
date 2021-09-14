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
        | Data of Value     // numbers, strings, etc.
        | Block of Block    // [foo]

    type Ent =
        | From of ModuleName * ((Word * Word) list) 
        | Prog of Word * Block
        | Macro of Word * Block
        | Assert of Block

    /// AST for g0 toplevel.
    type TopLevel = 
        { Open : ModuleName option
        ; Ents : Ent list
        }

    module Parser =
        // I'm using FParsec to provide decent error messages without too much effort.

        open FParsec
        type P<'T> = Parser<'T,unit>

        let lineComment : P<unit> =
            (pchar ';' <?> "`; line comment`") >>. skipManyTill anyChar newline 

        let ws : P<unit> =
            spaces >>. (skipMany (lineComment >>. spaces) <?> "spaces and comments")

        let sol : P<unit> =
            getPosition >>= fun p ->
                if (1L >= p.Column) 
                    then preturn () 
                    else fail "expecting start of line"

        let wsepChars = " \n\r\t[]"

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
                parseData |>> Data
                parseBlock |>> Block
                parseWord |>> Call
            ]

        let parseOpen : P<ModuleName> =
            kwstr "open" >>. parseWord

        let parseImport : P<Word * Word> =
            parseWord .>>. (opt (kwstr "as" >>. parseWord)) |>> 
                fun (w,optAsW) ->
                    let aw = Option.defaultValue w optAsW
                    (w,aw)

        let parseFrom : P<Ent> =
            kwstr "from" >>. parseWord .>>. (kwstr "import" >>. sepBy1 parseImport (pchar ',' .>> ws)) 
                |>> From

        let parseProg : P<Ent> = 
            kwstr "prog" >>. parseWord .>>. parseBlock |>> Prog

        let parseMacro : P<Ent> =
            kwstr "macro" >>. parseWord .>>. parseBlock |>> Macro

        let parseAssert : P<Ent> = 
            kwstr "assert" >>. parseBlock |>> Assert

        let parseEnt : P<Ent> =
            sol >>. choice [parseFrom; parseProg; parseMacro; parseAssert ]

        let parseTopLevel = parse {
            do! ws
            let! optOpen = opt (sol >>. parseOpen)
            let! lEnts = many parseEnt
            do! eof
            return { Open = optOpen; Ents = lEnts } 
        }

    let rec private findWordsCalledAcc ws b =
        match b with
        | [] -> ws
        | (op :: b') ->
            let ws' =
                match op with
                | Call w -> Set.add w ws
                | Data _ -> ws
                | Block p -> findWordsCalledAcc ws p
            findWordsCalledAcc ws' b'

    let wordsCalled (b:Block) =
        findWordsCalledAcc (Set.empty) b

    /// I'll discourage shadowing of definitions via issuing a warning.
    /// A word is observably shadowed if defined or called before a
    /// later definition within the same file.
    ///
    /// This also detects duplicate definitions, call before definition, and
    /// accidental recursion. Everything shows up as shadowing.
    let wordsShadowed (tlv : TopLevel) : Set<Word> =
        let fnEnt ent (wsSh,wsDef) =
            match ent with
            | From (_, lImports) ->
                // detect shadowing within the imports list, too.
                let fnImp (_,aw) (wsSh, wsDef) =
                    let wsDef' = Set.add aw wsDef
                    let wsSh' = if Set.contains aw wsDef then Set.add aw wsSh else wsSh
                    (wsSh', wsDef')
                List.foldBack fnImp lImports (wsSh, wsDef)
            | Prog (w, b) | Macro (w, b) ->
                let wsDef' = Set.add w wsDef
                let wsSh' = wordsCalled b |> Set.intersect wsDef' |> Set.union wsSh
                (wsSh', wsDef')
            | Assert b ->
                let wsSh' = wordsCalled b |> Set.intersect wsDef |> Set.union wsSh
                (wsSh', wsDef)
        List.foldBack fnEnt tlv.Ents (Set.empty, Set.empty) |> fst


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