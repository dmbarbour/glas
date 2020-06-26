# Glas Language Extension

The Glas language supports multiple syntaxes. Syntax is per-file, selected based on file extension. Syntaxes may thus be defined for external resources or DSLs, or to support projectional editors, in addition to user-defined language tweaks. 

However, Glas languages must still map to the same underlying model, and share the Glas language restrictions on links between modules and packages. This is achieved by defining a parser combinator for each syntax, with a limited API. 



An alternative syntax can support various roles: syntax can be optimized for a specific domain (e.g. natural language processing), or for alternative development environments (e.g. projectional editors, augmented reality), or to integrate external resources (e.g. parse JSON and load databases as regular modules).

The Glas syntax model is carefully designed to support robust tooling, e.g. enabling external tools to reuse the same parser for syntax highlighting or code completion. Even without FFI, this can be leveraged with some special compiler flags.

However, regardless of syntax, the underlying model is the same: Glas syntax extensions cannot represent anything new. Glas defines a standard syntax, which is normally assigned to the `.g` file extension and bootstrapped.

## Mapping File Extensions

Glas associates file extensions to [modules or packages](GlasModules.md). To find definition for file extension `.x`, Glas will first search for the `language_x` module in the local folder. If this module not defined, Glas will search instead for the `language_x` package. If there is no such package, the file's value is opaque.

Language packages are preferred. For minor variations (like a versioned language) a simple system of pragmas and flags for extensions could be built-in to the language itself. But language modules are suitable for didactic purposes, experimental designs, or specialization by schema.

*Note:* Multi-part languages such as `.x.y` currently aren't supported, because a suitable semantics is not clear.

## Language Definition

Minimally, a language definition must parse a file and produce a value. 

However, language definition should be extensible to support ad-hoc tooling. For example, we could support auto-formatting, projectional editing, command-line editing tools, templates, documentation, etc..

To keep it simple, we'll require a language package to return a record with a `parse` function. This is the main function defined here. But a Glas compiler could also support other utilities via command line parameters.

## The Parse Functor

The parser must be robust, able to continue despite errors. It also must support debugging, preserve a source mapping. Ideally, the same parser function can be reused for syntax highlighting, proposed corrections, and code completion, to ensure consistency with any changes to the language.

The proposed design is based on parser combinators and abstract constructors.

Parser combinators enable developers to expose internal behavior - especially byte-level expectations and choice - in order to simplify external tooling such as code completion or correction, and enables external decisions about search strategy.

Abstract value constructors defer typechecking, preserve useful metadata for source mapping, and may generally represent abandoned parse efforts. Abstract constructors may include loading packages, applying a function, selecting a field from a record. In general, the Glas underlying model is the target for abstract value constructors, but with features for optimization.

### Performance

The main cost of this design is performance. Parser combinators involve a large number of first-class functions. This can be mitigated by separate compilation of the parse function, specialized to the compiler. We can also develop intermediate languages that compile to an 'optimized' combinator with minimal backtracking.

### Parse Failure

Parse failure is not visible within a parse function. The decision to continue or abort is externalized. The parser API may heuristically attempt to correct code and continue parsing based on expectations. To abort a bad parse, it's sufficient to short-circuit further invocations to parser combinators.

## Abstract Parser Combinators

### Token Reads




### Abstract Binary Reads

We can read a specific number of bytes, or read all remaining bytes in a program. However, this will not return an observable binary value for branching. It returns an abstract label or binary, opaque to the parse function.

### Significant Whitespace





## Abstract Value Constructors

## OLD


## Parser Combinators

This section describes available parser combinators over the input stream, with interface and purpose. 

Errors: it's considered a parse error if the parser returns before the end of input, or if a parse error is explicitly emitted. There is no distinct 'error' return value.




### Input Annotation

The first pattern is to 



## Abstract Constructors

### Labels

A label can be used to access or update a record value, or in pattern matching. However, 



Glas will support two label constructors: one for regular labels like `foo`, and another for allocation of unique labels.

        !label("foo")
        !gensym()

Use of unique labels can enable Glas to model nominative types. Labels and gensym are both instances of 'paths', representing edges in the graph. In general, labels should compose into dotted paths like `foo.(m.class).bar`.

        let a = !label("foo")
        let b = ... eval m.class
        let c = !label("bar")
        !path_compose[a,b,c]

In general, specific methods will also exist to use labels, e.g. to manipulate a closed record.

### Unique Labels (Rejected)

A potentially useful idea is to support construction of unique labels, e.g. some form of `gensym` effect at compile-time. However, use of gensym is awkward in context of serialization, caching. I favor an ML approach: use of ascription and annotations to hide data, using only normal labels.

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


