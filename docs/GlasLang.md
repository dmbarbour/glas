# Glas Language

This document describes the primary '.glas' syntax. 

This syntax shall be slightly object-oriented, to account for modules, applications, and front-end compilers supporting mixins, overrides, inheritance. 
- A relevant challenge is that, when defining one object within another, it can be difficult to determine which 'self' we should refer to (module or object layer) without clear syntactic distinctions. Might need to accept the syntactic overhead here.

The primary goal is to develop a synax *I'm* happy with, personally. I hope my taste in syntax is something many others will appreciate, but user-defined syntax is available to mitigate. Anyhow, this document will be heavily driven by *feels*. 

## Design Notes

- Users will mostly define operations of type `Env -> Program`. 
  - This allows the each call to receive a caller-controlled view of the caller's environment. 
  - The "mostly" is important. We'll want support for other 'types' of programs, e.g. macros or grammars.
  - Use tagged definitions for adaptivity, e.g. tag "call" for `Env -> TaggedDef` and returning tagged "prog"

- For macros or templates, we'll also need the ability to pass anonymous AST structures
  - Church-encoded lists of ASTs for "..." var-args? 
  - Syntax should cleanly support ASTs separated by whitespace or commas, without ambiguity.
  - Syntax might benefit from explicit support for Church-encoded lists of ASTs.

- Abstraction of pattern or case matching, grammars, etc.

- Programs pass data on the stack, but users don't necessarily need to think about stacks
  - It may be convenient to support lightweight binding of data stack inputs and outputs to local registers.

- Avoid or mitigate deep horizontal syntax and its causes:
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
  - treat eof as a final block per file?
    - dubious! conflict with open inheritance and overrides
    - instead, externalize; apply finalizer before fixpoint
    - added tentative '%fin', but must see test if it works

- aggregation - ability to build up flexible tables via overrides
  - clear access to 'prior' definitions when shadowing/overriding
  - access to 'final' definitions via implicit module fixpoint
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
  - develop a decent syntax for pattern matching-like behavior
  - it is feasible to build DSLs around %br, %bt, and %sel ops
  - this supports flexible refactoring and composition of pattern-matching
  - there aren't many languages that support this effectively!
    - closest I can think of is active patterns (F#) or view patterns (Haskell)
    - but neither of those comes all that close

- unit types: I want them, but I'm still not sure how to model them in context
  - associative registers don't propagate nicely through data stack ops
  - 

## Imports

See [namespaces](GlasNamespaces.md) for the mechanics. This section is more about the syntax. Questions:

How to nicely represent a DVCS resource? An inline rep seems ugly. It might be more convenient to describe the resource separately from importing it. This would also provide the opportunity to develop libraries that 'index' other repos.

Do we default to closed fixpoints or open composition? I don't like 'extends' or 'mix' for inheritance. But 'include' seems acceptable and familiar. We may need to clarify that we're not including raw source text, just the final `Env -> Env` op, but we can design for the two to be roughly equivalent in glas syntax (modulo '%src' and '%arg.\*').

        include Src
        include Src at Prefix
        import Src
        import Src as Prefix
        from Src import alias-list

In addition, we might add some arguments? This could feasibly be expressed as an alias list, too. Perhaps optional keyword 'with' just after Src, that takes some expression of an Env of args. As a special case, bind a prefix to args with an `prefix as *` special syntax.

        include Src with input-alias-list
        from Src with alias-list import ...

We might also want the ability to treat import as a first-class definition within the namespace. OTOH, this is also true for most other features of glas. Perhaps we can support a generic solution here for binding modules into a definition.

Aside from these options, we might want the option to separate import of a module from immediate integration, i.e. treating the import as an expression. 



