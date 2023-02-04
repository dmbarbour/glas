# Text Tree Language

Text tree (favoring ".tt" file extension) is intended as a lightweight alternative to XML or JSON for configuration or simple data entry. Compared to XML and JSON, text tree requires less punctuation and fewer escapes, is better at isolating errors, and has the similar extensibility. 

In practice, a typical text tree might look like this:

        person
          name      David
            alias   Dave
          e-mail    david@example.com
        person
          ...

A text tree consists of a list of entries. Each entry consists of a single line of text encoding a label-data pair, with another text tree indented recursively below. Multiple entries with the same label are possible, and order of entries is preserved. It is possible to parse a text tree into the recursive type:

        type TT = List of (label:String, data:String, attr:TT)

For example, the example text tree might parse to:

        [(label:"person", data:"", attr:
            [(label:"name", data:"David", attr:
                [(label:"alias", data:"Dave", attr:[])])
             (label:"e-mail", data:"david@example.com", attr:[])
            ])
        ,(label:"person", data:"", attr:[...])
        ]

The text is assumed to be ASCII or UTF-8. Any of the three popular line terminals (CR, LF, or CRLF) are recognized as terminating a line. Spaces are used for indentation and to separate the label from data, and spaces around data are trimmed by the parser (excepting multi-line texts - see below). Much like XML or JSON, text tree benefits from a schema for each context. Validation depends on further processing of the TT value. 

## Escape Sequences

Labels that start with backslash ('\', codepoint 92) are reserved for future extensions to the text tree language. Escapes could eventually provide a basis for modularity, macros, inline schemas, label namespaces. However, my intention is that text tree should remain close to plain old data, avoid extensions that require computation. I might later develop a ".ttx" language that makes heavier use of escapes. Thus, at least for now, text-tree only uses escapes for multi-line texts and comments.

## Multi-Line Texts

Multi-line text uses the shortest possible escape sequence. 

        para 
          \ text starts here
          \ and continues on
          \ yet another line

Multi-line text may immediately follow a label with empty data, and the lines must align vertically (same indentation). As a special case, multi-line text is not trimmed: text starts one space after '\' and runs to end of line, including trailing spaces. The lines are later combined by the parser to produce the data value, separated by LF.

*Aside:* Line separators might eventually be tunable based on other escapes. However, this is low priority.

## Comments

Comments are supported using label '\rem', representing a remark to be removed by the text tree parser. However, in many cases it would be wiser to explicitly model annotations within the schema because those annotations are more readily preserved through processing or indexing.
