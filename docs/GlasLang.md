# Glas Language

This document describes the primary '.glas' syntax. 

The primary goal is to develop a synax *I'm* happy with, personally. I hope my taste in syntax is something many others will appreciate, but user-defined syntax is available to mitigate. Anyhow, this document will be heavily driven by *feels*. 

## Notes

- Users will mostly define operations of type `Env -> Program`. This allows the each call to receive a caller-controllable view of the caller's environment. The received arguments could either bind to a standard prefix, or be accessed via keyword.
- It would be convenient to support lightweight binding of data stack inputs and outputs to local registers.

- Avoid deep horizontal syntax and its causes:
  - parameter lists. Parameter lists hinder refactoring within larger contexts, and also add to horizontal depth.
  - structured output, e.g. deep hierarchical configs or namespaces. Provide a lightweight means to define things and operate within a deep structure or namespace. Perhaps borrow location annotations from TOML?

- Avoid explicit stack shuffling. It requires too much attention as programs grow more complicated. 
  - instead, a lightweight syntax *similar* to parameter lists could move items into local registers.
  - unlike conventional parameter lists, these little operations may be composed and refactored.

- Eliminate need for escape characters. Not in texts, not in names, etc..
  - Users may still develop macros to explicitly postprocess a name or text containing escapes. 
  - postprocessing must never be implicit, which is what causes escapes to 'explode' when, e.g. embedding a program source as text within another program.

- Multi-line texts should be supported directly without a lot of syntactic cruft. 
  - Perhaps each newline is indented one space?

- Clear boundaries for syntax errors.
  - e.g. clear 'sections' that can be processed independently.
  - ability to decide per section whether it's a full 'error' or just a warning.

- user-extensible syntax, ideally users can define keyword-like behaviors
  - this may include tags like the location annotations from TOML.
  - may need to maintain 'compiler context' in the private '%.\*' space.

- macros without special calling conventions (don't like the Rust '!')
  - tagged definitions can help with this
  - ability to define new syntax section types, 
   
- Inheritance and override of modules and apps, especially of mutually recursive definitions.
  - introduced implicit '%self' to help
  - recursive definitions by default
  - OO inspirations here, minus state

- integrate nicely with REPL and notebook-style programming
  - ability to 'extend' a program by simply adding more content at the end
  - clear boundaries for editing prior 'commands'
  - toplevel namespace may need access to 'effects' in this context, but we don't have access to runtime effects in the primitive namespace.
    - option one: integrate via compile-time effects, mostly fetching data but stable publish-subscribe is also viable. Very awkward.
    - option two: implicit construction of an 'app' per module that represents behavior as a REPL. 
  - for notebooks, must support GUI output and interaction
  - intriguingly, could model REPL and notebook 'outputs' via reflection (similar to logging), and user inputs as debugging. This allows transparently running a REPL or notebook without the overheads of user interaction.

- type annotations: get the syntax working asap, even if they aren't fully checked.
  - support user definitions of type descriptions (distinct definition tag from programs, etc.)
  - support higher-order type description, in general, e.g. `Env -> TypeDesc`. 
    - Similar for user-defined annotations.
  - ensure opportunity exists for fixed-width numbers and unboxed arrays and such
  - define a reasonable collection of types to get started.

- flexible definitions - not limited to programs, can define anything the namespace AST supports
  - tag definitions for flexible integration, but this may be convention (not enforced by syntax)
  - for abstract data types at compile time, extend seal/unseal to work with Src or plain old data

- idea: a program is a vertical sequence of 'blocks'. 
  - block start is marked by a line starting without indentation, 
  - every following line within the block must be indented by SP.
    - empty lines add implicit SP.
  - each block is 'compiled' independently 
    - text input as binary, removing the extra SP, normalizing newlines
    - each block returns AST representation, perhaps tagged `Env -> Env` 
      - simplifies caching, separates linking
  - block compiler internally dispatched by first word
    - or initial character, for punctuation
  - block compiler is constant within each file
    - implicitly reprogrammable across files via shadowing '%env.lang.\*'
    - but we could separately bind block compiler to shared library, too
  - parallel processing of blocks
    - each gets its own local copy of the '%\*' namespace
    - may provide location within file to the block compiler
    - might be worth providing '%\*' namespace independently to each block
      - plus some extra line number info
    - could process blocks of definitions lazily per file
  - compiler will adapt each block, could be based on the tag
    - adapter may be sensitive to context, instructions from prior blocks
    - can logically insert operations before blocks
  - can treat eof as a final block per file for some purposes.

- idea: to support more flexible inheritance, finalizer before fixpoint
  - provides opportunity to cleanup, insert tests, manage aggregators
  - applied externally by module client, coupled with the fixpoint
  - proposed definition of '%fin' for this role

- aggregation - ability to build up flexible tables via overrides
  - Church-encoded lists (or writer monad) of tagged AST elements

- annotations - attaching them to a definition or similar
  - easiest to include annotations within the definition block
  - could add header annotations if the 'define' block knows where to look
  - could add footer annotations if we're willing to name the target

- Kahn Process Networks (KPNs) or dataflow languages
  - it isn't difficult to compile KPNs into glas coroutines
  - local registers for queues withinin composite processes
  - it seems feasible to prove confluence within KPN up to input

- logic and constraint systems
  - it's feasible to express constraints via aggregation
  - this could be supported as a design pattern

- refactoring of pattern matching
  - it is feasible to build DSLs around %br, %bt, and %sel ops
  - this supports flexible refactoring and composition of pattern-matching
  - there aren't many languages that support this effectively!
    - closest I can think of is active patterns (F#) or view patterns (Haskell)
    - but neither of those comes all that close

- unit types: I want them, but I'm still not sure how to model them in context

## Glas

## Toplevel Syntax

The relative order of imports and definitions is ignored. Instead, we'll enforce that definitions are unambiguous and that dependencies are acyclic. This gives a 'declarative' feel to the configuration language.

The use of block structure at multiple layers ('@' blocks in toplevel, ':' blocks in namespace definition) is intended to reduce need for indentation. We'll still use a little indentation in some larger data expressions (pattern matching, loops, multi-line texts, etc.) but it should be kept shallow.

Line comments start with '#' and are generally permitted where whitespace is insignificant, which is most places whitespace is accepted outside of text literals. In addition to line comments, there is explicit support for ad-hoc annotations within namespaces.

## Imports and Exports

Imports and exports must be placed at the head the configuration file, prior to the first '@' block separator. Proposed syntax:

        # statements (commutative, logical lines, '#' line comments)
        open Source                     # implicit defs (limit one)
        from Source import Aliases      # explicit defs
        import Source as Word           # hierarchical defs
        export Aliases                  # export control (limit one)

        # grammars
        Aliases <= (Word|(Path 'as' Word))(',' Aliases)?
        Word <= ([a-z][a-z0-9]*)('-'Word)?
        Path <= Word('.'Path)?
        Source <= 'file' InlineText | 'loc' Path 

Explicit imports forbid name shadowing and are always prioritized over implicit imports using 'open'. We can always determine *where* every import is coming from without searching outside the configuration file. This supports lazy loading and processing of imports.

The Source is currently limited to files or a dotted path that should evaluate to a Location. The Location type may specify computed file paths, DVCS resources with access tokens and version tags, and so on. 

*Note:* When importing definitions, we might want the option to override instead of shadow definitions. This might need to be represented explicitly in the import list, and is ideally consistent with how we distinguish override versus shadowing outside the list. Of course, this is a non-issue if we omit 'open' imports.

## Implicit Parameters and Algebraic Effects



## Limited Higher Order Programming

The language will support limited higher order programming in terms of overriding functions within a namespace, and in terms of algebraic effects. These are structurally restricted to simplify termination analysis.

These are always static, thus won't interfere with termination analysis. Some higher order loops may be built-in to the syntax for convenience.

The Localization type isn't used for higher order programming in this language because dynamic . It is used only for translating global module names back into the configuration namespace.


## Function Definitions


## Namespace Blocks

The namespace will define data and functions. We might override definitions by default, providing access to the 'prior' definition, but we could support explicit introduction where we want to assume a name is previously unused.

We can support some local shadowing of implicit definitions in context, so long as we don't refer to those implicit definitions. 






Data can be modeled as a function that doesn't match any input. 

Name shadowing is only permitted for implicit definitions, and a namespace block must 

In general, a 'complete match' of input is required for a function, meanin



## Explicit Introduction

By default, definitions will be overrides. If we want to assume we 'introduce' a definition for the first time, we might specify 'intro ListOfNames'. I think this will better fit the normal use case.


## Data Expression Language

Definitions within each namespace allow for limited computation of data. The language is not Turing complete, but is capable of simple arithmetic, pattern matching, and [primitive recursion](https://en.wikipedia.org/wiki/Primitive_recursive_function). Data definitions must be acyclic, forming a directed acyclic graph.

There is a sublanguage for 

I want to keep this language very simple. There are no user-defined functions at this layer, but we might, only user-defined data. We'll freely use keywords and dedicated syntax for arithmetic, conditions, etc.

### Multi-Line Texts

### Importing File Data 

### Conditional Expression

Might be based mostly on pattern matching.

### Arithmetic

I'm torn a bit on how much support for arithmetic in configurations should be provided. Integers? Rationals? Vectors and matrices? I'm leaning towards support for ad-hoc polymorphism based on the shape of data.

### Lists and Tables

I don't plan to support full loops, but it might be convenient to support some operations to filter, join, zip, and summarize lists similar to a relational algebra. 

### Structured Data

Support for pairs, lists, and and labeled variants or dictionaries is essential. We could also make it feasible to lift a configuration namespace into data.


