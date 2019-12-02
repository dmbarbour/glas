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

Glas distinguishes 'open' and 'closed' records. Closed records are the default. An open record is represented as `(x:6, y:7, ...)`. Open records allow fields to be added via unification. Closed records include an implicit field to support reflection on the set of defined fields, but forbid adding new fields via unification. We can unify closed records, so long as no new fields are added.

Glas variants are modeled as closed, singleton records, e.g. `circle:(radius:3, color:red)`. Glas can leverage the closed record reflection field for pattern matching on variants and records. The trivial case of a singleton variants can be recognized by a compiler and encoded with zero overhead, and is suitable as a symbolic type wrapper. 

The empty, closed record `()` serves as the Glas unit type and value. The unit variant `foo:()` is very convenient for modeling simple enumerations, so Glas provides a convenient shorthand: `'foo` is equivalent to `foo:()`.

*Aside:* The empty, open record `(...)` serves as an anonymous future. Use of parentheses around an unlabeled expression, such as `(expr)`, can usefully serve as a precedence indicator.

## Lists, Tuples, Arrays

Glas lists are modeled as a recursive structure of variants and records:

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

In the underlying model, a record-based environment of variables is threaded through loops and conditionals. Each statement may return an updated record, and thus model mutation. Some variables may be immutable, depending on declaration. The current proposal is `const x` for declaration of constants.

Due to the underlying model, Glas must restrict how mutable variables are 'shared', especially in context of closures, concurrency, and simulated pass-by-reference. Instead of structural restrictions, Glas shall use static analysis to prevent variables from being used in an ambiguous manner, where order of assignment is unclear.

*Note:* Glas does not require an invariant 'type' for variables, e.g. it's permissible to say `foo = (x:6); foo.y = 7` resulting in `foo = (x:6, y:7)`. However, all branches of a conditional statement should have a consistent output type for variables that are later read.

## Transparent Futures

The empty, open record `(...)` serves as an anonymous placeholder for a value. This placeholder may later be 'assigned' via unification. This pattern is historically called a [future](https://en.wikipedia.org/wiki/Futures_and_promises). In Glas, futures are transparent in the sense that any variable or data field may be a future without requiring a change in reader syntax.

Glas will support futures. Programmers can declare variables like `var x;` as shorthand for `var x = (...);` or `vars x,y,z;` as shorthand for `var x = (...); var y = (...); var z = (...);`. We can write `x &= 42` to explicitly unify `x` with `42`. Due to the underlying semantics, Glas can support partial unification via open records. For example, `(x:6, ...)` and `(y:7, ...)` would unify as `(x:6, y:7, ...)`. However, constants such as `6` or `7` do not unify further, not even with themselves. Effectively, Glas has a single-assignment semantics for futures. 

Futures are suitable for *interaction* models. We can define 'interaction' as future inputs depending on partial outputs. With transparent futures, we can directly model partial outputs and future inputs as normal data structures. For example, a 'channel' can naively be modeled as a list with a future tail.

Glas will ensure 'safe' use of futures via static analysis, leveraging session types and linear types. There is a paper ["Transparent First-class Futures and Distributed Components" by Cansando et al.](https://www.sciencedirect.com/science/article/pii/S1571066109005180) that also seems relevant. Static analysis comes with a cost to expressiveness: there are many valid programs that cannot easily be analyzed.

## Pass-by-Reference Parameters

Pass-by-reference parameters are convenient for abstract manipulation of mutable variables. As a trivial example, we could abstract `x = x + 1` to `increment(&x)`. Although the underlying model cannot represent true variable references, Glas can simulate pass-by-reference via in-out parameters and futures. Glas will effectively expand `increment(&x)` to:

        const tmp_x = (in:x, out:(...));
        x = tmp_x.out;
        increment(tmp_x)

Here, `tmp_x` should be a fresh variable that does not conflict with other variables in scope. Unification with output is leveraged to capture the updated `x`. Finally, `increment` is called, and may have a return value in addition to the effect of updating its parameter. Programmers may explicitly define `increment` in terms of in-out parameters. However, for clarity and consistency, Glas has pass-by-reference patterns. 

        fn increment(x) { x.out &= x.in + 1; }
        fn increment(&x) { x = x + 1; }

In the latter case, we'll unify the output after the final update to `x`.

*Note:* Glas will perform a Rust-like static analysis for 'ownership' of variables to prohibit problematic aliasing. However, there are significant differences regarding closures.

## Objects and Interfaces

For this document, define 'object' as an abstract value accessed by interface. Relevantly, concrete implementation details such as the data structure used under the hood are hidden from the client. This enables the client to be generic over certain implementation details.

In object-oriented languages, 'objects' are usually modeled as a `(vtable:, field1:, field2:, ...)` structure. Here, the `vtable` (virtual method table) has a collection of methods, and each `field` represents data within the object. Abstraction is achieved via support from a type-system, which rejects programs that directly touch private fields or methods. The object is passed as an implicit argument for every method call, leveraging pass-by-reference for methods that update the object.

Glas will support data-objects with dedicated syntax. One proposal is to treat `foo!bar(a)` as desugaring to `foo.vtable.bar(self:&foo, args:a)`, or similar. Glas will also provide syntax for defining and constructing objects. 

Objects in Glas are mostly useful for limited forms of generic programming. Glas cannot directly model shared mutable objects. Also, I have been unable to unify the syntax for 'everything is an object' without sacrificing other desired features of Glas, such as avoiding type-driven behavior.

## Concurrent Channels

Glas can model concurrent interactions as data structures with futures. For example, a channel can naively be modeled as a future list, where one thread writes the tail and another reads the head:

        fn write(channel:ch, data:x) = 
            // assume a pass-by-reference channel
            ch.in &= list:(head: x, tail: ch.out);

        fn close(channel:ch) =
            ch.in &= 'null;

        write(&c, 1);
        write(&c, 2);
        ...

        // another thread
        fn read(channel:ch) =
            match(ch.in) {
            | list:(head:x, tail:xs) -> ch.out &= xs; x
            | 'null -> ch.out &= (); raise 'eoc;
            }

        const x = read(&c);
        const y = read(&c); 
        ...

Unfortunately, this naive model has several weaknesses: A fast producer, paired with a slow consumer, too easily results in a memory leak. The consumer cannot close the channel after it has read what it wants. Also, it could be difficult to robustly model heterogeneous channels, where type of elements vary based on a protocol.

We can tweak the model to solve some of these weaknesses. For example, we could put the reader in charge of adding to the tail of the list:

        fn write(channel:ch, data:x) =
            match(ch.in) {
            | list:cell -> 
                cell.head := x;
                cell.tail := ch.out;
            | 'null -> 
                ch.out := 'null;
                raise 'eoc;
            }

        fn read(channel:ch) =
            const x = (...);
            ch.in := list:(head:x, tail:ch.out);
            return x;

In this case, the consumer controls the buffer. The producer will wait for the consumer to signal its readiness. The consumer may 'read' several future elements in advance, but will often wait on the elements themselves. This makes it easy to control memory usage. However, the writer has lost the ability to signal when there is no more data. Also, it is awkward to buffer multiple writes ahead of the reader requesting them, which can be inconvenient for pipeline parallelism. 

A third option is to model a naive channel in each direction. The reader sends a stream of requests for data, and the writer responds. Generalizing, we ultimately have full bi-directional communication channels. Glas can model channels of many kinds. The greater difficulty is reasoning about channels, especially if we want safe use of heterogeneous types.

Glas will provide abstract, bi-directional, bounded-buffer channels as a built-in feature. This makes it easier to provide dedicated support in the type system, e.g. using [session types](https://groups.inf.ed.ac.uk/abcd/) to model heterogeneous protocols over channels. This also makes channels more accessible to the optimizer, e.g. to decide optimal buffer size, or even eliminate some buffers in favor of loop fusion.

Programmers will likely use channels more often than direct use of futures. Built-in channels will likely be abstracted as *objects*. 

## Effects Model

Glas programs usually interact with an external environment - e.g. console, filesystem, network. This is achieved by an implicit pass-by-reference parameter to provide access to the environment, and unification variables to model the interaction. 

Programmers can control the environment used by a subprogram. Thus, it is possible to provide a sandboxed or simulated console and filesystem, or to model an alternative environment with a canvas and turtle graphics as the only 'effects'. The environment expected by a function will be described in the function's type.

A limitation of Glas is that we cannot directly model 'shared' resources, such as 

## Exceptions

## Closures

## Functions


Functions in Glas have a single explicit argument. But we'll often model this argument as a closed record. By leveraging reflection over closed records, functions can support optional parameters. We can further leverage `('foo, 'bar, 'baz)` as shorthand for `(foo:(), bar:(), baz:())` to concisely represent optional flag parameters. In addition to the explicit argument, functions may have an implicit in-out  representing the external environment.  


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

## Disposable Objects


## Channels and Rendezvous

## Modeling Shared Objects


### Controlled Non-determinism

Evaluation of Glas is deterministic, so non-determinism must be an effect. The 'standard' Glas effects model will include a reflective effect to determine which input channels are ready. This supports arrival-order non-determinism, which is useful for modeling actor mailboxes or multi-threaded effects. Whether access to non-determinism is passed to subprograms is under control of the programmer.

That said, Glas does not encourage use of non-determinism. Synchronous reactive or temporal reactive models could support concurrency without sacrificing determinism.


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

