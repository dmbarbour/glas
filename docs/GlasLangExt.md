# Glas Language Extension

Glas is a general purpose language designed for text-based development environments. Consequently, Glas is neither optimal for any specific purpose, nor suitable for alternative development environments (e.g. visual, augmented reality). 

To address this weakness, Glas provides a mechanisms for users to extend and manipulate the language.

## Design Constraints and Considerations

The meaning of a program should be under the author's control. The client of a module or function should not need to know implementation details, such as which language extensions it uses. Between these, language must never depend on parameters from a client. This excludes fexpr-like mechanisms.

Developers should be able to abstract, reuse, and share useful language manipulations. Language extensions in Glas must be represented by first-class values so they may be shared and reused via the Glas module system, and optionally abstracted through parameterized functions.

Language extensions should be composable. Extensions should not interfere in unpredictable ways or have strange interactions with scoping. It would be convenient if we can analyze a set of extensions for conflicts ahead of time, e.g. by knowing statically which tokens are expected.

External tooling, such as syntax highlighting, source mapping, debugging, and auto-formatting, are important features of a language's ecosystem. Language extension should be carefully designed to support tooling. This excludes many `AST -> AST` or `Text -> Text` approaches, which easily lose metadata about origin or intention.

## Parser Combinators





Use of multiple languages within a modules is a significant source of boiler-plate and complications. For example, we must ensure lexical scope for one language is visible to other languages. 
 be accessible to another. However, we can mitigate this by defining a single language with multiple sublanguages built-in. 

Incremental manipulation of a language is problematic for tooling if it must reproduce the manipulation logic. Thus, we should either avoid incremental manipulation, or provide means to easily share the logic.





 language they want for reuse in many modules, as a one-liner. Th


The Glas module system defines modules as values. This may hinder 

Glas cannot 'import' macros from the module system



Ideally, extensions should compose in a comprehensible way, without interfering. 


Languages will often require access to compile-time effects, such as module imports or generation of unique symbols.

To track provenance metadata, it's very convenient if we have some insight about the parser's expectations, and how parsed objects are being composed. 

 It should not be difficult to provide access to these e





 is a recipe for boiler-plate, and will complicate tooling (such as syntax highlighting).











 at the top of the file, e.g. via `%lang` (or perhaps `%lang` - still thinking about aesthetics).

A file will indicate its language based on file extension and compiler configuration. Binary files are the most trivial module-level language.

A 'language' will be defined by parser combinator operating on an abstract linear object representing the compile-time environment. By invoking methods, programmers can incrementally parse the input and construct an abstract result, meanwhile annotating things to simplify tooling and debugging. 

Compile-time effects, such as imports or generating unique labels, are also supported.

Glas is defined as a module-level language, and will provides mechanisms for its own extension. Glas is intended to be effective for general purpose programming within the limits of the underlying model.

Alternative languages should usually be domain-specific, e.g. optimized for constraint programming or machine learning. One exception is adapting the Glas system to alternative development environments, e.g. developing a syntax optimized for convenient rendering and layout.

*Aside:* A weakness of parser combinators is that they're often slow, because they prevent a lot of optimizations. This could be mitigated by defining module-level BNF language that can be optimized before generating the parser combinator.

## Environment Methods

We might benefit from two 'layers' of extensions, e.g. the foundational compile-time environment, then a glas-extensions environment wrapping the foundation.

* special support for parsing binary modules
* logical, direct 'inclusion' of binary data, or pushback in general
* scope control: capture a range with one combinator, parse with another
* static evaluation
* RAII cleanup on scope exit
* 


* annotate current task / expectation
* score the current interpretation
* alternatives: should allow multi-path evaluation with scoring, not just first match wins
* 

A goal for the parser combinator is to simulataneously annotate the program input with how it is interpreted, to support tooling. Additionally, when working with composite languages, we'll want to preserve basic scoping rules. 


