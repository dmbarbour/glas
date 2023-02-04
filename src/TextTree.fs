namespace Glas


// Lightweight structured data format used in sources.txt.
//
//   hdr1 data is remainder of line
//     attrib1 data is remainder of line
//     attrib2 data is remainder of line
//        subattrib data is remainder of line 
//   hdr2 data is remainder of line
//   ...
//
// Both \r and \n are recognized as line terminals. 
// Blank lines or lines starting with '#' are skipped.
// Indentation via spaces. Data is trimmed of spaces.
//
// Note: this has limited support for multi-line text and 
// \rem remark escapes. 
module TextTree =

    type TTEnt = 
        { Label : string
        ; Data  : string
        ; Attrib : TT
        }
    and TT = TTEnt list

    type Line = (struct(int * string * string)) // indent, header, data

    let tryParseLine (s : string) : Line voption =
        let mutable ix = 0
        while((ix < s.Length) && (s[ix] = ' ')) do
            ix <- ix + 1
        if(ix = s.Length) then ValueNone else
        let indent = ix
        while((ix < s.Length) && (s[ix] <> ' ')) do
            ix <- ix + 1
        let label = s.Substring(indent, ix-indent)
        let data1 = if (ix = s.Length) then "" else s.Substring(ix + 1)
        let data = if (label = "\\") then data1 else data1.Trim(' ')
        ValueSome(struct(indent, label, data))

    let toLines (s : string) : Line list =
        let sepBy = [| '\r'; '\n' |]
        s.Split(sepBy, System.StringSplitOptions.None) 
         |> Seq.map tryParseLine
         |> Seq.collect ValueOption.toList
         |> Seq.toList

    let rec private collectMultiLineText (acc : string list) (indent : int) (ll : Line list) : struct(string * Line list) =
        match ll with
        | (struct(ix, "\\", data)::ll') when (ix = indent) ->
            collectMultiLineText (data::acc) indent ll'
        | _ ->
            let result = acc |> List.rev |> String.concat "\n"
            struct(result, ll)

    let private tryMultiLineText (data0 : string) (indent : int) (ll : Line list) : struct(string * Line list) =
        match ll with
        | (struct(ix, "\\", data)::ll') when ((ix > indent) && (data0 = "")) -> 
            // all lines must have same indentation
            collectMultiLineText [data] ix ll'
        | _ -> struct(data0, ll)

    let rec private parseEntList (acc : TTEnt list) (indent : int) (ll : Line list) : struct(TTEnt list * Line list) =
        match ll with
        | (struct(ix, lbl, data0)::ll0) when (ix >= indent) ->
            let struct(data, llAttrib) = tryMultiLineText data0 ix ll0
            let struct(attribs, ll') = parseEntList [] (ix + 1) llAttrib
            let ent = { Label = lbl; Data = data; Attrib = attribs }
            let acc' = if (lbl = "\\rem") then acc else (ent::acc)
            parseEntList acc' indent ll'
        | _ -> struct((List.rev acc), ll)

    let parseEnts (s : string) : TTEnt list =
        let struct(ents, llRem) = parseEntList [] 0 (toLines s)
        assert(List.isEmpty llRem)
        ents

