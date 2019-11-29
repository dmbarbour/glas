# Glas Language Design

## Underlying Model

A Glas program is modeled by a directed graph with labeled edges. Evaluation rewrites this graph using two primary rules: *unification* and *application*. Evaluation is monotonic and confluent.

*Application* is the basis for computation. A function is applied to a node, called the applicand. Input and output parameters are modeled using labeled edges from the applicand. For example, if we apply a `multiply` function to a node, the function might read edges `x -> 6` and `y -> 7` then write edge `result -> 42`.

*Unification* is the basis for dataflow. Unification merges two nodes. Unification propagates on matching edge labels. For terminal nodes, such as numbers or functions, unification is a type error. Thus, Glas effectively has single-assignment unification semantics.

A Glas function is defined by a bounded subgraph with a designated public node. When applied, the subgraph is copied then the public node is unified with the applicand. 

Edges are labeled from a finite alphabet. However, arbitrary labels can be modeled via composition. For example, a label `result:` may involve seven nodes in a sequence - one for each character, with `:` as sentinel. Together with unification, this implies a trie-like structure when multiple labels start with the same character.

Glas provides an imperative-style syntax to work conveniently with this model.

## Records and Variants

Glas has built-in support for records using a syntax of form `(x:6, y:7)`. 

In the underlying model, records are encoded as a trie of graph nodes, with outbound labeled edges for each byte in the label and `:` as the sentinel label. However, the Glas compiler will favor a flat, unboxed representation similar to C structures.

Records are immutable. However, we can model record 'update' by a function that produces a copy of the record except for specified labels, which are added, removed, or have a value replaced. Glas provides syntax for record updates, and supports [row-polymorphic updates](https://en.wikipedia.org/wiki/Row_polymorphism).

Glas distinguishes 'open' and 'closed' records. Closed records are the default. An open record is represented as `(x:6, y:7, ...)`. Open records allow fields to be added via unification. Closed records include an implicit field to support reflection on the set of defined fields, but block the adding of new fields via unification. We can unify closed records, so long as no new fields are added.

Glas variants are modeled as closed, singleton records, e.g. `circle:(radius:3, color:red)`. Glas leverages the implicit field for reflection on closed records to support pattern matching on variants and records.

Records are often used as function parameters in Glas. By leveraging closed records, functions may conveniently support *optional* parameters.

The empty, closed record `()` serves as the Glas unit type and value. The unit variant `foo:()` is very convenient for modeling simple enumerations and optional flag-like parameters, so Glas provides a convenient shorthand: `'foo` is equivalent to `foo:()`.

*Aside:* The empty, open record `(...)` serves as an anonymous future. Use of parentheses around an unlabeled expression, such as `(expr)`, can usefully serve as a precedence indicator.

## Lists, Tuples, Arrays

Lists are modeled as a recursive structure of variants and records:

        type List a = list:(head: a, tail: List a) | 'null

Lists are very useful, so Glas provides a convenient shorthand: `[1,2,3]` expands to `list:(head:1, tail:[2,3])`, and `[]` trivially expands to `'null`.

Tuples can be modeled as invariant-length, heterogeneous lists, e.g. `[1, "hello", (x:6, y:7)]`. Tuples would generally be typed as records, rather than lists. Many functions on lists - length, zip, index, split, concatenation, etc. - should be carefully typed so they also work for tuples.

Arrays are essentially lists with an optimized representation. Glas does not distinguish arrays from lists in syntax, but does distinguish arrays the type system to support array-optimized list functions and prevent accidental conversions.

*Aside:* Support for list comprehensions is still under consideration. Support for abstraction of the list tail, e.g. `[1, ... rem]` as `list:(head:1, tail:rem)` is viable, but also not fully accepted yet.

## Imperative Programming

Glas syntax has a familiar, imperative style. Programs consist largely of declared variables, sequential statements, loops, and conditional behaviors.

        fn collatz(n) {
          var x = n;
          var ct = 0;
          while(x > 1) {
            ct = ct + 1;
            if(even(x)) {
              x = x / 2;
            } else { 
              x = 3*x + 1;
            }
          }
          return ct;
        }

In the underlying model, a record-based environment of variables is threaded through loops and conditionals. Each statement may return an updated record, and thus model mutation. Variables may be immutable depending on declaration. As a consequence of the underlying model, Glas cannot directly share mutable variables. Special attention is required for closures or concurrency.

## Unification Variables

Glas exposes underlying unification semantics via deferred assignment. Effectively, all variables may be used as transparent [futures](https://en.wikipedia.org/wiki/Futures_and_promises). And the empty, open record `(...)` serves as an anonymous future. 

Within a single thread, futures are useful for recursive dependencies. With multiple threads of control, data structures constructed from futures also form a convenient foundation for concurrent interactions. For example, a list with a future tail can serve as a simple channel, where reading the next element may wait. Each element within the list is also potentially interactive.

Glas uses a distinct syntax for deferred assignment vs. variable update. Current proposal is `x := value` for deferred assignment vs. `x = value` for update. Partial unification is supported. For example, `(x:6, ...)` and `(y:7, ...)` would unify as `(x:6, y:7, ...)`. Similarly, `pt:(x:6, ...)` and `pt:(x:(...), y:7)` would unify as `pt:(x:6, y:7)`. Unification with closed records cannot add new fields, but doesn't need to specify every field.

Glas performs static analysis to ensure progress of the computation, e.g. guarding against cyclic dependencies that can cause deadlock. This analysis comes at a cost to expressiveness. Glas attempts to minimize this cost via sufficiently advanced static analysis models, e.g. using multi-party session types.

## Pass-by-Reference Parameters

Pass-by-reference parameters are convenient for abstract manipulation of mutable variables. As a trivial example, we could abstract `x = x + 1` to `increment(&x)`. Although the underlying model cannot represent true variable references, Glas can simulate pass-by-reference via in-out parameters. Glas will effectively expand `increment(&x)` to:

        { 
            let tmp_x = (in:x, out:(...));
            x = tmp_x.out;
            increment(tmp_x)
        }

Here, `tmp_x` is a fresh variable that will not conflict with other variables in scope. Unification with output is leveraged to capture the updated `x`. Finally, `increment` is called, and may have a return value in addition to the effect of updating its parameter.

Programmers may explicitly define `increment` in terms of in-out parameters:

        fn increment(x) { x.out := x.in + 1; }

However, for clarity and consistency, Glas has pass-by-reference patterns:

        fn increment(&x) { x = x + 1; }

In this case, we'll unify the output after the final update to `x`.

To prevent problems, Glas performs a Rust-like analysis of 'ownership' for variables. A reference would temporarily hold ownership of the variable. However, the exact rules will be different from Rust's.

## Effects Model

Glas programs usually interact with an external environment - e.g. console, filesystem, network. This is achieved by an implicit pass-by-reference parameter to provide access to the environment, and unification variables to model the interaction. 

Programmers can control the environment used by a subprogram. Thus, it is possible to provide a sandboxed or simulated console and filesystem, or to model an entirely different environment with a canvas and turtle graphics as the only 'effects'.

### Controlled Non-determinism

Evaluation of Glas is deterministic. Non-determinism must be an effect. 

The 'standard' Glas effects model will include a reflective effect to determine which input channels are ready. This supports arrival-order non-determinism, which is useful for modeling actor mailboxes or multi-threaded effects.

That said, Glas does not encourage use of non-determinism. Synchronous reactive or temporal reactive models could support concurrency without sacrificing determinism.

## Exceptions

## Closures

## Loops

Glas programs mostly use imperative-style loops - e.g. `for` loops over collections, and `while` loops over arbitrary boolean conditions. As a common case, `loop { op }` acts as `do { op } while(true);`. 

        for var in (expr) {
            ...
        }

        while (cond) {
            ...
        }

        do {
            ...
        } while(cond);

Loops support continue, break, and early return statements. Glas may also support labeled break and continue to support nested loops, but I haven't decided a syntax for this yet.

Glas can also support recursive functions. However, in context of exceptions and unwind protection, it may be difficult to support tail-call optimization. This will need to be examined.

## Threads



The model underlying Glas allows deterministic concurrency based on dataflow. But dataflow in Glas is usually sequential due to the imperative environment. Thus, Glas will provide means to restrict dependency on the imperative envrionment and expose the underlying concurrency.

A promising option is `fork fn` to wrap a function. The wrapper immediately returns the dynamic environment, and also a unification variable for the result. The function is evaluated instead in a fresh dynamic environment, e.g. the unit value. Parameters to the wrapped function are still evaluated in the caller's environment, but may involve pass-by-reference, channels, rendezvous, and similar patterns. 

This design has an advantage of being relatively easy to abstract. For example, we can build a `forkenv` function above `fork` that provides and returns an alternative dynamic environment.


## Channels and Rendezvous

Naively, a channel can be modeled as a deferred list. For example, 

## Effects

In addition to the lexical environment, functions in Glas are implicitly parameterized by a pass-by-reference parameter representing [dynamic scope](https://en.wikipedia.org/wiki/Scope_%28computer_science%29#Dynamic_scoping). This dynamic environment object conveniently models 'effects' such as access to a console or network, drawing turtle graphics to a canvas, or construction of a constraint model for staged programming.

A consequence of this design is that Glas has a very imperative programming style. Programmers will generally think about programs imperatively, with control-flow semantics. However, functions are still 'pure' with regards to partial evaluation, testing, refactoring, reuse in a different context.

## Exceptions

## Disposable Objects


## Channels and Rendezvous

## Modeling Effects

## Modeling Shared Objects

## Modeling Reactive Systems

## Direct Application

In the underlying model, functions are 'applied' to a single node, and interact with that node via unification. In Glas, this underlying node includes the imperative environment, parameter, and return value and is not directly exposed to the programmer. However, we can simulate this: we can develop identity functions that return their argument, and also unify with it. 

## Syntactic Abstraction


By explicitly controlling the lexical and dynamic environment, Glas language can be adapted to different problems, although this is certainly weaker than syntactic abstraction. 




## Content-Addressed Modules and Binaries

# OLD STUFF, NEEDS REVIEW



## Object-Oriented, Concurrent, Imperative Programming

Glas programs have a very imperative, object-oriented style. A program environment is implicitly threaded through a sequence of statements and method calls, modeling lexical variables and stateful manipulations. Concurrent computations may explicitly fork this sequence, and communicate via channels or unification variables.

Glas programs ultimately have a purely functional behavior and dataflow semantics. However, it would not be obvious by looking at the program, and most reasoning about a program will be imperative. The main difference from a conventional imperative control is that programmers cannot easily represent shared mutable objects. Sharing must be carefully modeled.


## Effects Model

Glas programs are pure functions. To produce effects, program output must be interpreted by an external agent. Unification-based dataflow makes this easy. For example, a program can directly output a stream of request-response pairs. The agent could loop over this list, handle each request then directly write the response. The response would unify with the correct destination in the program. 

A Glas compiler can tightly integrate a simple effects interpreter. The agent loop can be optimized via loop fusion and inlining, to minimize context-switching. It is feasible to specify intended interpretation by annotation, using an embedded language if appropriate.

In practice, applications should specify effects at a high level, based on application-level events. This allows application behavior to be tested independently, and ported more easily to another context. There may be many layers of interpretation, in general. 

## Modules and Binaries

Glas programs may contain references to external modules and binary data.

        %module(file:"useful code.g")
        %binary(file:"images/cat_picture.jpg")

Before compilation or package distribution, Glas systems will transitively 'freeze' references by adding a secure hash for each item, then copying the binary to a content-addressed storage. It is feasible to subsequently 'thaw' the binary, to reproduce the original environment (modulo reformatting). 

        %module(file:"useful code.g", hash:"...")
        %binary(file:"images/cat_picture, hash:"...")

Use of content-addressed references for compilation or deployment simplifies concurrent versions, configuration management, incremental compilation, separate compilation, distributed computing, and many related features. Support for binaries is convenient for capturing static program data without awkwardly escaped, embedded text.

Glas modules are programs and represent pure functions, and may be parameterized. Instead of sharing modules or binaries by reference, Glas encourages use of module parameters. A package aggregator module can configure and integrate them. Existential types are specific to each application of the function. To simplify freeze-thaw, it's convenient if there is only one reference to a file from a given program.

Favored hash: [BLAKE2b](https://blake2.net/), 512-bit, encoded as 128 base-16 characters with alphabet `bcdfghjkmnlpqrst`. Hashes are not normally seen while editing, so there is no need to compromise length. The base-16 consonant encoding will resist accidental spelling of offensive words.

Security Note: Content-address can be understood as an object capability for lookup, and it should be protected. To resist time-leaks, content-address should not directly be used as a lookup key. However, preserving a few bytes in prefix is convenient for manual lookup when desperately debugging. I propose `take(8,content-address) + take(24,hash(content-address))` as a lookup key.

## Primitive Functions and Data

Glas supports common fixed-width numeric types as built-in. Embedded strings are permitted, with conventional escapes. Glas developers are encouraged to favor external binary references for binary data or large texts. 

Primitive functions must be sufficient for anything, modulo effects, that an external agent might do with exposed data: adding and multiplying numbers, parsing strings, data plumbing, etc..

## Annotations

Glas models 'annotations' as special functions with identity semantics. Annotations use a distinct symbol prefix such as `#author` or `#origin` or `#type`. Annotations may influence static safety analysis, performance optimizations, program visualization, automatic testing, debugger output, and etc.. However, annotations shall not affect observable behavior within the program. 

## Records and Variants

Records in Glas can be modeled by a node with labeled edges. Each label serves as a record field. To update a record, we will need a primitive function that can combine a record and a variant into an updated record. This can feasibly be implemented by a prototyping strategy.

Variants in Glas can be modeled as specialized, dependently-typed pairs: an inner node has a special edge to indicate the choice of label, then the second label depends on the choice. Operations on variants may involve matching another variant, or selecting an operation from a record.

Records and variants can support row-polymorphic structural types. With type annotations, we could also require compatibility with a GADT.

## Definition Environment

## Loops


## Syntax

## (Topics)

* Syntax
* Concurrency, KPNs
* Projectional Editing
* Embedded DSLs and GADTs
* Direct Manipulation
* Reactive Streams
* Type System Details


## Type System

Glas will heavily emphasize static analysis. Glas must support universal types, existential types, row-polymorphic types, multi-party session types, and linear types. Beyond these, desiderata include dependent types, exception types, performance and allocation types, and general model checking.

Advanced types cannot be fully inferred. Type annotations are required.

Glas does not have any semantic dependency on the type system: no type-indexed generics, no `typeof`. Thus, types primarily support safety and performance. 


## ....


## Syntax Overview

Glas syntax has an imperative and object-oriented style. Although Glas programs are pure functions, you wouldn't know just by looking. There are blocks, statements, in-out parameters, and object methods.

In addition to lexical scope, functions operate on an implicit context. This enables programmers to model and manipulate a linear mutable environment. It simplifies data-plumbing, dependency injection, and extension of a program.


 invocations. As much as feasible, Glas syntax looks and feels like conventional imperative or OO languages. The imperative style is convenient for working with linear types.

*Thought:* Do I really want implicit state? Hmm. Well, I cannot support 'effects' via objects in lexical scope. And I don't want to explicitly thread an object through every method...

But it might be best to limit implicit state to a linear, existential 'object'. Operate on it via methods rather than 


Every statement or expression has arguments and a result. However, statements operate on three more implicit parameters: read-only context, linear context in, and linear context out. The linear context enables computations to carry and update data. 

because Glas must work with a lot of linear objects, and it's often convenient to support implicit parameters. 

Glas distinguishes statements from expressions based mostly on how they're used. 

. If they don't touch these parameters, they'll behave in a pure manner. 



 reifies the environment.

 Functions generally receive several implicit parameters. 

function has four implicit parameters, representing the lexical scope, a reader monad, and a state monad (in,o
The lexical 



The lexical context is bound at the point of definition. 



Static sco



Glas functions are defined in an implicit [monad](https://en.wikipedia.org/wiki/Monad_%28functional_programming%29).


However, Glas is limited to *linear* objects.



This style is convenient for working with 


The motive for this is to simplify work wit 


 Glas functions will thread hidden parameters through a computation. 


This style is convenient for working with linear types, and  
 This style convenient for working with linear types, which are very common 

(albeit limited to *linear* objects). The basic


It supports in-out parameters. There 



There are expressions and s





Where feasible, Glas provides a conventional syntax that should look and feel familiar to programmers of procedural and object-oriented languages. 


## Session Types

[Session types](https://groups.inf.ed.ac.uk/abcd/) essentially describe patterns of input and output

input-output patterns. In Glas, we apply session types to input-output parameters of pure functions. For example, a session type of form `(?A . !B . ?C . !D)` might say that `A` is required as an input, then `B` is available as an output, followed by `C` is an input and `D` as an output. With conventional FP, we might represent this type using continuation passing, e.g. `A -> (B, (C -> D))`. 

More sophisticated session types further describe *recursion* and *choice* patterns, enabling expression of long-lived, branching interactions. Conventional data types are easily subsumed by session types: they're effectively output-only (or input-only) sessions.

There are several significant benefits to using session types. First, session types enable interactive computations to be expressed in a direct style, without explicit closures. Second, session types are easily *subtyped*, e.g. the above type is compatible with `(!D . ?A . !B)`. This enables wide compatibility between programs and reduces need for explicit glue code. Third, by more precisely representing data dependencies, session types greatly simplify partial evaluation optimizations.

## Allocation Types

Static allocation and linking is a valuable property in domains of embedded or real-time systems, FPGAs and hardware synthesis. However, this constraint is unsuitable for higher-level applications or metaprogramming.

Glas language will enable developers to assert within session types that functions are 'static' after a subset of input parameters are provided. Glas might also support a weaker constraint of 'stack' allocation, to simplify reasoning about performance and memory management for many higher applications.

## Session Types

The [simple session](http://simonjf.com/2016/05/28/session-type-implementations.html) type `{a?int . x!int . b?int . y!int}` tells us that it's perfectly safe to increment output `x` then feed it as input `b`. That is, we can safely compose interactions without risk of deadlock, without deep knowledge of subgraph implementation details. But for complex or long-lived interactions, this is far from sufficient for systems programming. Fortunately, session types offer a viable path forward, with *choice* and *recursion*:

        type ArithServer =
            &{ mul_int: a?int . b?int . x!int . more:ArithServer
             | add_dbl: a?double . x!double . more:ArithServer
             | quit: status!int 
             }

In session type systems, we often distinguish between 'external' choice `&` vs 'internal' choice `⊕`. External choice is analogous to a method call: the caller chooses the method. Internal choice is like a variant or tagged union: the provider chooses the value. In the example above, we have an external choice of methods including `add_dbl` or `mul_int`. Recursive session types can support unbounded interactions with remote loops.

We can interpret sophisticated session types for purely functional programming. A 'choice' is a promise to use only a subset of a function's input and output parameters. For example, if we choose `add_dbl`, we'll use `add_dbl.a` and `add_dbl.x`. But we won't use the `mul_int.a` or `quit.status`. A recursive session corresponds roughly to a function with unbounded (variable-args) input-output parameter lists.

Intriguingly, session types fully subsume conventional data types. A conventional FP tree is simply a pure-output (or pure-input) recursive session type. We could process that tree lazily or in parallel. We can represent structure-preserving maps as a function that receives inputs then provides outputs at each node in a tree. We can represent push-back behavior for slow consumers by requiring a `sync?unit` input at certain steps. 

Glas adapts session types to support interactive computations between functions, and to improve expressiveness over conventional FP languages.

## Deterministic Concurrency

