# Text Tree Language

Text tree is intended as a lightweight language for configuration or structured data. Compared to XML and JSON, text tree uses less punctuation and fewer escapes, is better at isolating errors, and has similar extensibility. Essentially, it records a recursive list of labeled elements where each element may have a primary text and a sublist of elements. (Unlike markup languages, there is no direct mixing of text and structure.)

A typical text tree might look like this:

        person
          name      David
            alias   Dave
          e-mail    david@example.com

        person
          ...

A text tree is represented by a list of entries. Every entry consists of a label, a main text (inline or multi-line), and a subtree for attributes. The subtree is distinguished by indentation. Order of entries is preserved. The text should be ASCII or UTF-8. Line separators may be CR, LF, or CRLF. Indentation should use spaces.  

The text tree may parse into a list of triples:

        type TT = List of { Text, Text, TT }

For example, the example text tree might parse to:

        [{"person", "", 
            [{"name", "David", 
               [{"alias", "Dave", []}
               ]}
             {"e-mail", "david@example.com", []}
            ]}
         {"person", "", [...]}
        ]

Spaces are trimmed with a special exception for multi-line texts (see below). Similar to XML or JSON, text tree may benefit from a schema for validation in many contexts, but that won't be developed immediately.

## Escape Sequences

All entries that start with backslash ('\', codepoint 92) are reserved for escapes and extensions. Escapes are scoped to their containing entry to simplify local reasoning. Currently used for multi-line texts, potential use for inclusions and transclusions.

## Multi-Line Texts

Multi-line text may follow an entry with an empty inline text. 

        para 
          \ text starts here
          \ and continues on
          \ yet another line

Unlike inline text, multi-line text is not trimmed. The indentation, '\' label, and a single space following the label is removed from each line, but further prefix or trailing spaces are preserved. In the parsed result, lines are separated by LF even if the text tree file uses CR or CRLF.

## Comments

The text tree format proposes a lightweight convention for comments: any entry whose label starts with '#' should be interpreted as a comment or annotation or metadata. This includes '#author', '#date', and so on. These entries are left accessible for post-processing, but should be understood as providing or supporting context without influence on meaning.

## Thoughts

### Inclusion? Defer.

Text tree could easily be extended to support logical inclusion of other text-tree and text file, e.g. `\include FilePath` within an entry, or `\text FilePath` within a multi-line text. This is much more limited than a programmable configuration. In theory, this is useful for representing large configurations. But I propose to defer them until I have a clear use case in practice, e.g. in context of post-processing and dhall. I would not be surprised to discover that some form of selection and transclusion is needed to effectively leverage inclusions.

### Programmable Configurations

I would have liked a lightweight, generic approach to inheritance and mixin-like abstractions of text trees. But, even with escape sequences, I don't see this happening easily with text tree. Too much code is required, and text tree is designed as a structured ".txt" rather than a programmable configuration language. Users could adapt [dhall](https://dhall-lang.org/) or similar to mitigate these limitations. Of course, we can also support configuration-specific solutions via post-processing.

