# Text Tree Language

Text tree (".tt" file extension) is intended as a lightweight alternative to XML or JSON for configuration or structured data entry. Compared to XML and JSON, text tree uses less punctuation and fewer escapes, is better at isolating errors, and has similar extensibility. 

A typical text tree might look like this:

        person
          name      David
            alias   Dave
          e-mail    david@example.com

        person
          ...

A text tree is represented by a list of entries. Every entry consists of a label, a main text (inline or multi-line), and a subtree for attributes. The subtree is distinguished by indentation. Order of entries is preserved. The text should be ASCII or UTF-8. Line separators may be CR, LF, or CRLF. Indentation should use spaces.  

The text tree may parse into a list of triples:

        type TT = List of { String, String, TT }

For example, the example text tree might parse to:

        [["person", "", 
            [["name", "David", 
              [["alias", "Dave", []]]]
             ["e-mail", "david@example.com", []]
            ]]
         ["person", "", [...]]
        ]

Spaces are trimmed, with a special exception for multi-line texts (see below). Similar to XML or JSON, text tree may benefit from a schema for validation in a given context. 

## Escape Sequences

Entries that start with backslash ('\', codepoint 92) are reserved by the text tree parser for escapes and extensions. This can potentially support modularity (e.g. via '\include' or similar), macro calls, inline schema definitions, and other ad-hoc features. Currently, escapes are used only for multi-line texts and comments.

*Aside:* An important design constraint for escapes is that their effect must be scoped to the entry they are embedded within. This simplifies local reasoning.

## Multi-Line Texts

Multi-line text may immediately follow an entry with an empty inline text. 

        para 
          \ text starts here
          \ and continues on
          \ yet another line

Unlike inline text, multi-line text is not trimmed. The indentation, '\' label, and a single space following the label is removed from each line, but further prefix or trailing spaces are preserved. In the parsed result, lines are separated by LF even if the text tree file uses CR or CRLF.

## Comments

Entries whose labels start with two backslashes '\\' are (usually) dropped by the parser. This can be used similar to inline comments in many programming languages, but entries may form structured comments with attributes, multi-line texts, etc..

As a convention, I also propose that entries whose labels start with '#' be 'semantic' comments that are processed normally by the text tree parser but represent ad-hoc annotations to the contextual interpreter of the text tree.
