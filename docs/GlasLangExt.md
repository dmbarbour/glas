# Glas Language Extension

The primary Glas language extension model is based on file extensions specifying the language for a module. Compared to macros, this design reduces boilerplate, improves performance, and simplifies tooling.

To extend the Glas language involves creating a new language that's mostly the same as Glas except with a few extra features. To create this language, we would first develop a package that implements Glas in an openly extensible way. The new language would then be mapped to a file extension, such as `.gg`, so we can write code in this language.

We are not limited to 'extensions'. A new language doesn't need to look anything like Glas. Because language manipulation is non-invasive, we may even develop parsers for ad-hoc files produced by other tools.

## Mapping File Extensions

We map a file extension to languages by defining a 'language' module whose value is a record of defined file extensions. For example, the `foo` language would be defined at `module language.foo`. See the [Glas module system](GlasModules.md). Language extensions only apply within the same folder. 

If a language becomes popular, it should reference a corresponding package - e.g. the content of `language/foo.g` might simply be `package lang_foo`. 

Undefined languages will return the binary content of a file. However, it can be useful to define a language and still return the binary, mostly to support debugging and tooling. For example, we could configure the `.txt` language to warn about spelling errors or inconsistent line endings.

*Aside:* Language module per folder does become a form of repetitive boilerplate, but it's less repetitive than defining or importing macros at the top of each file.

## Language Definition

Minimally, a language must parse a file and produce a value while supporting debuggers. However, a complete language package may provide several more features - auto-formatting, code completion, code reduction, projectional editing, and more. Extensibility is a relevant concern.

So, a language will be defined as a record, which contains at least the `parse` function.

## The Parse Function

Naively, we could implement a pure `Binary -> Value` function. However, this would not support module access or generation of unique labels for opaque types. Also, it would not preserve metadata for effective debugging.

My proposal that a parser is defined by a function that operates on an opaque compile time environment object. This opaque object will provide methods for reading input, abstract value constructors, imports and unique label generation, annotations for optimization or debugging, and alternative choice with backtracking. An abstract parser combinator, of sorts.

This design allows us to maintain metadata about where values come from. We can also try multiple alternatives to detect ambiguity, guarantee ensure the full input is consumed, report which types or tokens are expected at a given step, and even recommend changes to code.

A disadvantage of this approach is lackluster performance. This can be mitigated by developing an intermediate language can be optimized and compiled into a state-machine-like parser function, avoiding deep recursion and backtracking.

## Compile Time Methods 

### Value Injection

The compile-time environment shall provide a method to inject arbitrary values into the abstract runtime. Perhaps simply `!inject(value)`. This is a one-way path, we cannot extract abstract values because that would hinder parsing in the presence of errors for debugging.

### Scope Control

The compile-time environment shall provide a method to limit scope of one parse action relative to another, e.g. `!scoped(scope:P1, parser:P2)` extracts a range of input based on `P1` then returns the result of `P2` exposing only this input range.

The main motive for this is error isolation, especially with DSLs or distrusted parser functions.

### Region Annotation

### Type Annotations

### Static Evaluation

### Function Definition

### Unification

### Module and Package Access

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


