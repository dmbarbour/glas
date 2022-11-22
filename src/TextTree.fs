namespace Glas


// Describes the lightweight structured data used in sources.txt.
// This is a fair bit simpler than XML, but limits data to single
// lines of text. There are no escape characters or punctuation,
// but line comments are supported.
//
// Structure:
//
//   # line comment
//   hdr1 data is remainder of line
//     attrib1 data is remainder of line
//     attrib2 data is remainder of line
//        subattrib data is remainder of line 
//   hdr2 data is remainder of line
//   ...
//
// Blank lines and lines starting with '#' are ignored.
// Both \r and \n are recognized as line terminals. 
// Only spaces for indent. Data is trimmed of spaces.
module TextTree =

    [<Struct>]
    type TTEnt = 
        { Label : string
        ; Data  : string
        ; Attrib : TTEnt list
        }

    type Line = (struct(int * string * string)) // index, header, data

    // tryParseLine will filter out comments and blank lines.
    // all other lines are valid.
    let tryParseLine (s : string) : Line voption =
        let mutable ix = 0
        while((ix < s.Length) && (s[ix] = ' ')) do
            ix <- ix + 1
        if((ix = s.Length) || ('#' = s[ix])) then ValueNone else
        let indent = ix
        while((ix < s.Length) && (s[ix] <> ' ')) do
            ix <- ix + 1
        ValueSome(struct(indent, s.Substring(indent,ix-indent), s.Substring(ix).Trim(' ')))

    let toLines (s : string) : Line list =
        let sepBy = [| '\r'; '\n' |]
        s.Split(sepBy, System.StringSplitOptions.None) 
         |> Seq.map tryParseLine
         |> Seq.collect ValueOption.toList
         |> Seq.toList

    let rec parseEntList (acc : TTEnt list) (indent : int) (ll : Line list) : struct(TTEnt list * Line list) =
        match ll with
        | (struct(ix, lbl, data)::llAttrib) when (ix >= indent) ->
            let struct(attribs, ll') = parseEntList [] (ix + 1) llAttrib
            let ent = { Label = lbl; Data = data; Attrib = attribs }
            parseEntList (ent::acc) indent ll'
        | _ -> struct((List.rev acc), ll)

    let parseEnts (s : string) : TTEnt list =
        let struct(ents, llRem) = parseEntList [] 0 (toLines s)
        assert(List.isEmpty llRem)
        ents

