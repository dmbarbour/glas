# Glas Language Extension

The primary Glas language extension model is based on file extensions specifying the language for a module. Compared to macros, this design reduces boilerplate, improves performance, and simplifies tooling.

To extend the Glas language involves creating a new language that's mostly the same as Glas except with a few extra features. To create this language, we would first develop a package that implements Glas in an openly extensible way. The new language would then be mapped to a file extension, such as `.gg`, so we can write code in this language.

A new language doesn't need to look anything like Glas. Developers may even develop parsers for ad-hoc binaries produced by other tools.

Glas may be defined as a Glas language extension after bootstrapping. 

## Mapping File Extensions

The Glas distribution should contain a 'language' package whose value is a record mapping file extensions to language definitions. By default, to find definition for file extension `.gg`, the compiler will try `(package language).gg`. If the package or extension is not found, the compiler will value the file as its binary content.

The language package may be overridden within a folder by defining a language module. This offers more control to the programmer, to personalize languages or support project-specific languages.

See [Glas module system document](GlasModules.md) for more information.

## Language Definition

Minimally, the language definition must parse a file and produce a value.

To support debugging, the parser and produced value should preserve location metadata for source mapping, isolate errors, and robustly continue to report many likely errors rather than stopping on the first error.

Beyond compiler support, a complete language definition should support external tooling: syntax highlighting, auto-formatting, projectional editing, code completion, code reduction. Because there is no limit to potential tooling, language definitions must also be extensible. Further, a language definition could feasibly include documentation, examples, tutorials.

Glas will define languages as records that minimally define the `parse` function.

## The Parse Function

Naively, a parser could implement a pure `Binary -> Value` function. However, this would not support module references, generation of unique labels, or effective debugging.

A Glas parse function instead is parameterized by an opaque 'compile-time environment' object, with methods for construction of the abstract value and parsing the input. 

This design allows us to maintain metadata about where values come from. We can also try multiple alternatives to detect ambiguity, verify the full input is consumed, continue in presence of errors, and report which grammar types and tokens are expected at a given step. This design is also extensible: we can add new methods to the object. Type safety analysis and partial evaluation can be deferred until after parsing.

A disadvantage of this approach is lackluster performance. This can be ameliorated by developing a BNF-like intermediate language that can be optimized to minimize backtracking and deep recursion.

## Abstract Value Constructor Methods

Abstract values are hidden from the parser, and have an arbitrary representation decided by the compiler. Generally, we'll record the inputs and metadata about origin.

### Value Injection

        !inject(any)

It is feasible to inject values into the abstract runtime. Support for injection of first-class functions will require reflection, so language modules and packages must be compiled adequate reflection support.

We cannot extract values at parse time. Abstract values will remain abstract.

## Parser Combinator Methods

### Scope Control

        !scoped(scope:P1, parser:P2)

Parser combinator. We can use one parser to define the scope of another. Both parsers 

 parse action relative to another, e.g. `!scoped(scope:P1, parser:P2)` extracts a range of input based on `P1` then runs `P2` within that scope, and returns the result from each parse.

The main motive for this is error isolation, especially with DSLs or distrusted parser functions.

### Warnings and Errors



### Module and Package Access

The compilet-ime en

### Compiler Type Constructors?

Arrays, fixed-width integers?

### Region Annotation

### Type Annotations

### Static Evaluation

### Function Definition

### Unification


### Unique Symbol Generation


## Glas Compile Time

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


