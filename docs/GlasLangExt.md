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

However, for tooling, it would be useful to support syntax highlighting, auto-formatting, simplification, projectional editing, code completion, documentation, tutorials, and so on. To support these features, language definitions should be extensible.

To ensure extensibility, Glas will define a language as a record that minimally include the `parse` function. Other features may be added as-needed.

## The Parse Function

An object is passed to the `parse` function providing methods for abstract constructors and parser combinators on the input stream.

The parser combinators will include several methods to simplify debugging, such as recording intentions, expectations, suspicions, proposed corrections. Thus, parsing can produce an annotated input. It's also easier to detect whether the full input was parsed, or detect ambiguity when composing choices.

The abstract constructors may implicitly record parser locations, preserving location metadata for purpose of reporting type errors, stack traces, profiling, breakpoints, etc.. Abstract constructors also support 'unique' labels and defer type checking or static evaluation.

The weakness of this approach is performance. It's difficult to optimize the parser, or combine similar search paths. This can be mitigated by developing a declarative grammar language that can be compiled into a parser combinator with minimal backtracking.

The remainder of this document is mostly a discussion of the methods required for the `parse` function.

## Abstract Constructors

### Labels

We can model a first-class 'label' as an abstract record of row-polymorphic functions to access or update the label's value within a record.

Glas will support two label constructors: one for regular labels like `foo`, and another for allocation of unique labels.

        !label("foo")
        !gensym()

Use of unique labels can enable Glas to model nominative types. Labels and gensym are both instances of 'paths', representing edges in the graph. In general, labels should compose into dotted paths like `foo.(m.class).bar`.

        let a = !label("foo")
        let b = ... eval m.class
        let c = !label("bar")
        !path_compose[a,b,c]

In general, specific methods will also exist to use labels, e.g. to manipulate a closed record.

### Module or Package Reference

External dependencies are named abstract values. Currently, only packages and modules are supported. 

        !package("foo")
        !module("bar")

These return an abstract value representing the value from a named module or package. See [Glas module system](GlasModules.md). The type or concrete value is not visible to the parser. Thus, parsing does not require that external dependencies are implemented yet (excepting the language module).

### Literal Values

Basic constructors will exist of numbers and binaries. Glas does not currently support injection of arbitrary values. But a few specific types can be injected.


## Parser Combinators

### Scope

We can use one parser to impose scope upon another.

        !scoped(scope:P1, parser:P2)

This would first parse with `P1`, then parse `P2` within the same range consumed by `P1`. We could augment this by passing the result of parsing `P1` as a parameter to `P2`. The main motive for this is error isolation, especially with DSLs or distrusted parser functions.

### Code Correction or Completion

If we simply read characters without describing expectations, it can be difficult to propose corrections to code. Hence, all byte-level reads should indicate expectations in some useful way, such as regular expressions. Then, if no expectations are met, we can propose some byte-level corrections that would enable parsing to continue.

## Abstract Value Constructor Methods

Abstract values are hidden from the parser and their representation is controled by the compiler. This supports deferred type checking and evaluation, and maintenance of metadata about origin. Compiler built-in types and functions will be available via abstract value constructors.


### Function Construction



### Record Construction

### Function Application






## Parser Combinator Methods

### Scope Control


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


