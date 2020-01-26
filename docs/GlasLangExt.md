# Glas Language Extension

The Glas language extension model is based on file extension specifying language for a module. Compared to macros, this design reduces boilerplate, improves performance, and simplifies tooling.

To 'extend' Glas involves creating a new language that's similar to Glas except with a few extra features. This is achievable by defining Glas in an extensible manner, using a library. 

Glas is not limited to defining languages with Glas-like syntax. DSLs can be defined, JSON can be parsed, etc.. For all languages, Glas supports a consistent approach to detecting and reporting errors. However, there are practical limits. Glas languages must be defined in terms of the underlying model for Glas. It is awkward to define behaviors that rely on shared memory, typeful reflection, or non-determinism.

The `.g` file extension represents the standard Glas language, which should be bootstrapped.

## Mapping File Extensions

Glas maps file extensions to language packages. To find definition for file extension `.x`, Glas will search for a language definition in `package language_x`. There is no default interpretation for a file. If there is no associated language package, the file's value is undefined.

File extensions should have consistent interpretation within the Glas system. However, local language package overrides could be useful for eliminating boilerplate.

See [Glas module system](GlasModules.md) documentation for more information.

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

Abstract values are hidden from the parser and their representation is controled by the compiler. This supports deferred type checking and evaluation, and maintenance of metadata about origin. Compiler built-in types and functions will be available via abstract value constructors.

### Literal Values

We'll require methods to construct numbers and strings, and other values we can reasonably represent as literals or construct directly.

*Aside:* I've contemplated a general `!inject(val)` method, which would require reflection. However, I've decided to table this until we determine how difficult it will be to support the reflection. Meanwhile, literal values would align with writing an AST. 

### Module or Package Reference

External dependencies are named abstract values. Currently, only packages and modules are supported. 

        !package("foo")
        !module("bar")

Return an abstract value representing the value from a named module or distribution package. See [Glas module system](GlasModules.md). Modules in Glas represent normal values.

### Function Construction



### Record Construction

### Function Application






## Parser Combinator Methods

### Scope Control

        !scoped(scope:P1, parser:P2)

Parser combinator. We can use one parser to define the scope of another. Both parsers 

 parse action relative to another, e.g. `!scoped(scope:P1, parser:P2)` extracts a range of input based on `P1` then runs `P2` within that scope, and returns the result from each parse.

The main motive for this is error isolation, especially with DSLs or distrusted parser functions.

### Warnings and Errors

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


