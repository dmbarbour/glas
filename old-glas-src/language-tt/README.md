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

Entries that start with backslash ('\', codepoint 92) are reserved by the text tree parser for escapes and extensions. This can potentially support modularity (e.g. via '\include' or similar), macro calls, inline schema definitions, and other ad-hoc features. Currently, escapes are used only for multi-line texts.

*Aside:* An important design constraint for escapes is that their effect must be scoped to the entry they are embedded within. This simplifies local reasoning.

## Multi-Line Texts

Multi-line text may immediately follow an entry with an empty inline text. 

        para 
          \ text starts here
          \ and continues on
          \ yet another line

Unlike inline text, multi-line text is not trimmed. The indentation, '\' label, and a single space following the label is removed from each line, but further prefix or trailing spaces are preserved. In the parsed result, lines are separated by LF even if the text tree file uses CR or CRLF.

## Comments

The text tree format proposes a lightweight convention for comments: any entry whose label starts with '#' should be interpreted as a comment or annotation or metadata. This includes '#author', '#date', and so on. These entries are left accessible for post-processing, but should be understood as providing or supporting context without influence on meaning.

*Aside:* It is feasible to use escapes to indicate comments, perhaps labels starting with '\\'. But I'm currently avoiding this because it's often useful to process comments in some way.
