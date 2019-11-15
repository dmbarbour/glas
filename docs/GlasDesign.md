# Glas Language Design

## Underlying Model

A Glas program is a pure function modeled by a directed graph with labeled edges. Evaluation rewrites this graph using two primary rewrite rules: *unification* and *application*. These rewrite rules are monotonic and confluent.

*Application* is the basis for computation. A function is applied to a node, called the applicand. Input and output parameters are modeled using labeled edges from the applicand. For example, if we apply a `multiply` function to a node, the function might read edges `x -> 6` and `y -> 7` then write edge `result -> 42`.

*Unification* is the basis for dataflow. Unification merges two nodes. For inner nodes in the graph, unification propagates on matching edge labels. For terminal nodes, such as numbers or functions, unification is a type error. Thus, Glas effectively has single-assignment unification semantics.

Edges are labeled from a finite alphabet, but larger labels can trivially be modeled via composition, e.g. `result` may involve a label for each character plus an implicit terminal. Together with unification of matching labels, this implicitly produces a trie-like structure for multiple labels on a node. A compiler may use a more efficient representation based on known types and static knowledge of interactions.

A Glas function is defined by a bounded subgraph with a designated public node. When applied, the function subgraph will be copied, and the public node is unified with the applicand.

## Records and Variants

Glas has built-in support for records, e.g. `(x:6, y:7)`. Records are encoded in the underlying model as a graph node with an outbound labeled edge for each field. Glas provides functions that compute an 'updated' record, copying all fields except where requested.

Glas distinguishes open and closed records. Closed records prevent adding new fields via unification, but contain an implicit summary of which fields are defined, allowing limited reflection. Glas records are closed by default. Open records are indicated by an ellipsis as the last field, e.g. `(x:6, y:7, ...)`. 

The empty, closed record `()` serves as the Glas unit value. The empty, open record `(...)` is effectively an anonymous unfication variable.

A variant is encoded as a closed, singleton record, e.g. `circle:(radius:3, color:red)`. Variants are the primary basis for conditional behavior with pattern matching. Logically, pattern matching relies on the implicit summary field. Glas has special support for variants in the type system. The unit variant `foo:()` is common for simple enumerations, but ugly and noisy, so Glas provides a convenient shorthand `'foo`.

Closed records are frequently used as function arguments in Glas. Closed records conveniently support named parameters, optional parameters, and `'flag` parameters.

## Lists, Tuples, Arrays

Lists are easily modeled as a recursive structure of records and variants:

        type List a = list:(head: a, tail: List a) | 'null

Glas provides a syntactic shorthand for lists. For example, `[1,2,3]` expands to a structure of record and variants of form `list:(head:1, tail:[2,3])`, and `[]` expands to `'null`. Also, `[x|rem]` expands to `list:(head:x, tail:rem)`.

Tuples in Glas are modeled as invariant-length, heterogeneous lists, e.g. `[1, "hello", (x:6, y:7)]`. Functions on lists that don't require homogeneous type, e.g. to compute length or zip lists, also work on tuples. A Glas compiler should optimize tuple representation - any short, invariant-length list.

Arrays in Glas are also modeled by lists. However, arrays are distinguished from regular lists in the type-system to robustly support adjacent representation in memory, offset-based indexing, and linear in-place updates.

## Channels and Rendezvous


## Lexical Environment




## Unification Variables


## Object-Oriented, Concurrent, Imperative Programming

Glas programs have a very imperative, object-oriented style. A program environment is implicitly threaded through a sequence of statements and method calls, modeling lexical variables and stateful manipulations. Concurrent computations may explicitly fork this sequence, and communicate via channels or unification variables.

Glas programs ultimately have a purely functional behavior and dataflow semantics. However, it would not be obvious by looking at the program, and most reasoning about a program will be imperative. The main difference from a conventional imperative control is that programmers cannot easily represent shared mutable objects. Sharing must be carefully modeled.




## Linear Objects

Linear objects arise naturally with single-assignment semantics. For example, the write-end of a channel is implicitly linear. However, 

Linearity is also convenient for high-performance memory-management. For example, if we know an array is manipulated 

 computation will apply linear updates to an array, we can copy 

, to ensure purely functional 'update' to a record or array is implemented by as an in-place mutation.


## Glas Environment

Glas will leverage records to model the definitions environment of a program. 

For example, when we declare `let foo = 42`, field `foo` should be added to a record that is accessible by later statements within the current scope. Like many imperative-functional languages, Glas will permit update of a variable only if it was explicitly declared mutable, e.g. `let mut foo = 42` might permit a subsequent `foo = foo + 1`.

Mutation in Glas is necessarily linear. But that's sufficient for many use cases. Glas will have many linear objects when modeling channels for concurrency or effects. Linear update is adequate for many imperative algorithms. And a compiler can potentially take advantage to implement linear update by in-place mutation.

However, holding the entire record to update one element hinders concurrency. If we have a loop like `while(x > 1) { ++stepct; collatz(&x); }`, it should not block subsequent operations that do not depend on `x` or `stepct`.




        


Glas shall support records in standard syntax. The current proposal uses parentheses and commas, e.g. `(x:6, y:7)`. This aligns the empty record `()` with a unit value, and does not interfere with use of parentheses for other expressions. It's also convenient for record-based parameters to function calls, such as `foo(x:6, y:7)`.





We can also support 'mutable' 


 'environment' of a program. 



There are no variables, but

## Modeling Shared Objects

## Effects Modeling

## Concurrency

## Content-Addressed Modules and Binaries



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

