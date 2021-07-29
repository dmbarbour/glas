namespace Glas

/// This file implements the g0 (Glas Zero) bootstrap syntax. This implementation
/// of g0 should be replaced by the language-g0 module when bootstrap completes. 
/// I'm using FParsec to provide decent error messages without too much effort.
/// 
/// Unlike most languages, g0 does not support embedded data other t
module Zero =
    type Word = string
    type ModuleName = Word

    /// AST for Zero is very close to Glas program model. 
    /// However, we still preserve words at this layer.
    type Prog = ProgStep list
    and ProgStep =
        | Call of Word    // word
        | Sym of Bits // 'word, 42, 0x2A, 0b101010, etc.
        | Str of byte list // "hello, world!"
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
    type AST = 
        { Open : ModuleName option
        ; From : ImportFrom list
        ; Defs : Def list
        }

    module Parser =
        open FParsec
        type P<'T> = Parser<'T,unit>

        let isWordChar c = 
            isAsciiLetter c || isDigit c || (c = '-')

        let lineComment : P<unit> =
            pchar ';' >>. skipManyTill anyChar newline 

        let ws : P<unit> =
            spaces >>. skipMany (lineComment >>. spaces)

        let str s : P<unit> = 
            pstring s >>. ws

        let wsep : P<unit> =
            nextCharSatisfiesNot isWordChar .>> ws

        let parseWord : P<string> =
            let frag = manySatisfy2 isAsciiLower (fun c -> isAsciiLower c || isDigit c)
            (stringsSepBy1 frag (pstring "-")) <??> "word"

        let parseSymbol : P<Bits> =
            (pchar '\'' >>. parseWord) <??> "symbol" |>> Value.label

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

        let parseProgBody : P<Prog> =
            fail "todo: parse programs"

        let progBlock : P<Prog> = 
            between (str "[") (str "]") parseProgBody

        let startOfLine : Parser<unit,'a> =
            fun stream ->
                if (1L = stream.Column) then Reply(()) else
                Reply(Error, expected "start of line")

        let parseOpen : P<ModuleName> =
            startOfLine >>. str "open" >>. parseWord

        let parseImport : P<Import> =
            parseWord .>>. (opt (str "as" >>. parseWord)) 
                |>> fun (w,aw) -> Import(Word=w,As=aw)

        let parseFrom : P<ImportFrom> =
            startOfLine >>. str "from" >>. parseWord .>>. (str "import" >>. sepBy1 parseImport (pchar ',')) 
                |>> fun (src,l) -> From (Src=src, Imports=l)

        let parseProgDef : P<Def> = 
            str "prog" >>. parseWord .>>. progBlock 
                |>> fun (w, p) -> Prog (Name=w, Body=p)

        let parseDef : P<Def> =
            // only one definition type for now
            parseProgDef 

        let parseTopLevel =
            ws >>. opt parseOpen .>>. many parseFrom .>>. many parseDef .>> eof 
                |>> fun ((optOpen, lFrom), lDefs) -> 
                    { Open = optOpen; From = lFrom; Defs = lDefs } 

    // after parsing g0, still need to validate:
    //  no duplicate imports or definitions (modulo open)
    //  no defining of the keywords




    // current goal: parser for AST.

    /// The g0 syntax has a ton of reserved words.
    /// This includes all the basic ops.
    let reservedWords = 
        [ "dip"; "data"; "seq"
        ; "cond"; "try"; "then"; "else"
        ; "loop"; "while"; "do"
        ; "env"; "with"; "do"
        ; "prog"
        // namespace
        ; "from"; "open"; "import"; "as"
        ] 
        |> List.append (List.map Program.opStr Program.op_list) 
        |> Set.ofList

    // During parse, we'll often import a few modules.
    type LoadedModules = Map<string, Value>



    /// To detect cyclic dependencies, record which module we're loading.
    type Loading = string list


    /// A word is a string that re


