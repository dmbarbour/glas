# Glas Language Design

## Underlying Model

A Glas program is modeled by a directed graph with labeled edges. Evaluation rewrites this graph using two primary rules: *unification* and *application*. Evaluation is monotonic and confluent.

*Application* is the basis for computation. A function is applied to a node, called the applicand. Input and output parameters are modeled using labeled edges from the applicand. For example, if we apply a `multiply` function to a node, the function might read edges `x -> 6` and `y -> 7` then write edge `result -> 42`.

*Unification* is the basis for dataflow. Unification merges two nodes. Unification propagates on matching edge labels. For terminal nodes, such as numbers or functions, unification is a type error. Thus, Glas effectively has single-assignment unification semantics.

A Glas function is defined by a bounded subgraph with a designated public node. When applied, the subgraph is copied then the public node is unified with the applicand. 

Edges are labeled from a finite alphabet. However, arbitrary labels can be modeled via composition. For example, a label `result:` may involve seven nodes in a sequence - one for each character, with `:` as sentinel. Together with unification, this implies a trie-like structure when multiple labels start with the same character.

Glas syntax favors the imperative-OO style to work with this model.

## Records and Variants

Glas has built-in support for records using a syntax of form `(x:6, y:7)`. In the underlying model, records are encoded as a trie with one node per character in the label, and `:` as a sentinel. The compiler may use a more optimal representation.

Records are immutable. However, we can model record 'update' by a function that produces a copy of the record except for specified labels, which are added, removed, or have a value replaced. Glas provides syntax for record updates, and supports [row-polymorphic updates](https://en.wikipedia.org/wiki/Row_polymorphism).

Glas distinguishes 'open' and 'closed' records. Closed records are the default. An open record is represented as `(x:6, y:7, ...)`. Open records allow fields to be added via unification. Closed records include an implicit field to support reflection on the set of defined fields, but forbid adding new fields via unification. Reflection on closed records is the basis for pattern matching and optional parameters. 

Glas variants can be modeled as closed, singleton records, e.g. `circle:(radius:3, color:red)` vs. `square:(side:3, color:blue)`. Variants are very common for modeling tree-structured data, and may receive specialized support from the Glas type system and compiler. A 'singleton' variant, having only one choice, can be useful as a type wrapper to guard against accidental use of data in the wrong context.

The empty, closed record `()` serves as the Glas unit type and value. The unit variant `foo:()` is convenient for modeling simple enumerations or flag parameters. Glas provides a convenient shorthand: `'foo` is equivalent to `foo:()`.

*Aside:* The empty, open record `(...)` serves as an anonymous future. Use of parentheses around an unlabeled expression, such as `(expr)`, can usefully serve as a precedence indicator.

## Lists, Tuples, Arrays

Lists can be modeled as a recursive structure of variants and records:

        type List a = cons:(head: a, tail: List a) | 'null

Glas will provide a convenient shorthand: `[1,2,3]` essentially expands to `cons:(head:1, tail:[2,3])`, and `[]` expands to `'null`. Support for list comprehensions or methods is still under consideration.

Tuples can be modeled as invariant-length, heterogeneous lists, e.g. `[1, "hello", (x:6, y:7)]`. Tuples would be typed as records, rather than as lists. Many methods on lists - length, zip, index, split, concatenation, etc. - will apply to tuples. Some, such as map and fold, may work for tuples with homogeneous type.

Arrays are essentially lists with an explicitly optimized representation. In many cases, Glas requires explicit conversion from list to array representation or back to prevent accidental conversions.

## Numerics

Glas will support many numeric literals such as `6`, `2/3`, and `3.14`. Complex numbers may also be supported.

I'm still contemplating how to deal with representation concerns - bit width, fixed point, rationals, floating point, etc.. I'm not satisfied with convention in this regard - too many unintuitive features and abstraction leaks. However, fixed-width representation is essential for performance and memory control. It seems feasible to approach this in terms of refinement types, operating on well-defined ranges of numbers, much like we might support fixed-width lists.

I intend to maintain full precision for numbers as the default, and require explicit conversions for any loss (e.g. converting `2/3` to a float). I may also desire support for symbolic numbers like pi.

Should the default type for number be a 'numeric formula' DSL?

### Units of Measure

Units of measure are convenient for type-safe use of numbers. To a first approximation, they're also easy to model: e.g. `(watts:1, m:-2)` would represent `watts/m^2`. 

This is purely symbolic. Glas won't have any built-in knowledge of units. To convert from watts to joules/second would require an explicit conversion. To convert from `kilometers` to `feet` would similarly require an explicit conversion. 

Symbolic units of measure can be used for counting in radians, or to distinguish a count of apples vs. oranges. But users will need to beware of limitations, e.g. if working with non-linear or non-zero-based units such as decibels. 

The current idea is that units of measure will be supported as shadow types in the type system, requiring suitable type annotations.

## Imperative Programming

Glas syntax has a familiar, imperative style. Glas supports imperative functions, objects and methods, exceptions, and multi-threading. A Glas function consists largely of declared variables, sequential statements, loops, and conditional behaviors. 

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

In the underlying model, an environment of variables is threaded from one statement to the next. Mutation of variables is modeled by statements returning an updated environment. This is similar to record update - we're actually returning a fresh environment with some differences. 

Glas uses `const` instead of `var` to declare constant variables. Constant variables forbid update. Programmers should be encouraged to use constants instead of vars where feasible, because it simplifies reasoning about code.

Glas cannot directly share mutable variables. Glas deviates from conventional imperative programming mostly where sharing is a requirement, e.g. for aliasing, callbacks, sharing a log between threads. I'll return to sharing in a later section.

## Transparent Futures

The empty, open record `(...)` serves as an anonymous placeholder for a value. This placeholder may later be 'assigned' via unification. This pattern is historically called a [future](https://en.wikipedia.org/wiki/Futures_and_promises). In Glas, futures are transparent in the sense that any variable or data field may be a future without requiring a change in the reader's syntax (e.g. no `force` action).

Glas will support futures. Programmers can declare variables like `var x;` as shorthand for `var x = (...);` or `vars x,y,z;` as shorthand for `var x = (...); var y = (...); var z = (...);`. We can write `x := 42` to explicitly unify `x` with `42`. Due to the underlying semantics, Glas can support partial unification via open records. For example, `(x:6, ...)` and `(y:7, ...)` would unify as `(x:6, y:7, ...)`. However, constants such as `6` or `7` do not unify further, not even with themselves. Effectively, Glas has a single-assignment semantics for futures.

Static analysis with futures will leverage session types and linear types, and perhaps constraint satisfaction. There is a paper ["Transparent First-class Futures and Distributed Components" by Cansando et al.](https://www.sciencedirect.com/science/article/pii/S1571066109005180) that also seems relevant. Static analysis comes with a cost to expressiveness because there are many valid patterns that cannot easily be analyzed.

## Pass-by-Reference Parameters

Pass-by-reference parameters are convenient for abstract manipulation of mutable variables. As a trivial example, we could abstract `x = x + 1` to `increment(&x)`. Although the underlying model cannot represent true variable references, Glas can simulate pass-by-reference via in-out parameters and futures. Glas will effectively expand `increment(&x)` to:

        const tmp_x = (in:x, out:(...));
        x = tmp_x.out;
        increment(tmp_x)

Here, `tmp_x` should be a fresh variable that does not conflict with other variables in scope. Unification with output is leveraged to capture the updated `x`. Finally, `increment` is called, and may have a return value in addition to the effect of updating its parameter. Programmers may explicitly define `increment` in terms of in-out parameters. However, for clarity and consistency, Glas will support pass-by-reference patterns. 

        fn increment(x) { x.out := x.in + 1; }
        fn increment(&x) { x = x + 1; }

In the latter case, we unify the output just after the final update to `x`.

## Object-Oriented Programming

Objects in Glas are simply modeled as records of fields and methods. There is no strong distinction between records and objects. Methods are simply functions with a `(self:, args:)` parameter pair. Glas provides syntactic sugar to invoke methods: `foo!bar(x)` desugars to `foo.bar(self:&foo, args:(x))`. Similarly, `foo?baz(y)` desugars to `foo.baz(self:foo, args:(y))` for queries on constant objects.

To improve concision, Glas will allow implicit `self` in most cases, via specialized function declarations for action and query methods, and via implicit self when the object variable is elided:

        .field => self.field
        ?query(args) => self?query(args)
        !action(args) => self!action(args)

Finally, Glas will provide some flexible abstractions to construct and type objects, e.g. with interface types and mixin-based inheritance. The details will need more attention, of course.

Glas may weakly support *everything is an object*. When we write `6 * 7` it might mean `6 ?times (7)` or similar, allowing for operator overloading. Lists and strings are also objects.

## Implicit Environment

Imperative programs typically operate on an implicit environment containing console, filesystem, network, etc.. To achieve a similar interface, Glas will implicitly thread a pass-by-reference parameter, `env`, to every function and method call. Thus, `env` may serve roles such as thread-local storage or dynamic scope.

It is left to developers to provide a usable and extensible model for this environment. For example, we could use `env.stack` to model `push, pop, dup, drop` and a stack-based programming API, then introduce `env.canvas` to model turtle graphics. With unification-based channels, we can model concurrent reads and writes to the outside world.

The environment type will be included in function type. Effects can be controlled typefully, and it's possible to enforce purity for certain subprograms.

## Error Handling

For programs, it's convenient if we can focus program logic on a 'happy path' without repetitive, conditional error-handling code. Glas provides several features suitable for error handling. 

First, the implicit environment can carry a set of error handlers, logging, etc.. This can model effectful or resumable error handling.

Second, Glas provides a syntactic sugar `^expr` which implements a convenient early-return pattern:

        ^expr => 
            match(expr) {
            | val:v => v
            | other => return other;     
            }

That is, `^expr` will either extract a value or return early with to the function's caller with an `other` variant. This is not the same as exceptions because propagation must be explicit at each step. But the pattern is concise, catchable, and extensible, so may serve a similar role. 

Third, Glas will support [RAII](https://en.wikipedia.org/wiki/Resource_acquisition_is_initialization) patterns to avoid repetitive cleanup code otherwise common for early returns. Programmers may write `unwind { actions }` to specify that `actions` should be evaluated just before leaving the current block scope. The statement `use x = expr` is shorthand for `var x = expr; unwind { x!dispose() }`. Anonymous `use expr` is also supported, and is convenient for managing the implicit environment.

Finally, Glas program may `abort` with some message. Abort cannot be caught or observed, so the error information is intended for developers. Relatedly, `absurd` can document conditions that should be provably unreachable at runtime. If an assumption of absurdity has not been discharged, it behaves as `abort` at runtime. Annotations can control runtime use of abort, e.g. for mission-critical or embedded systems.

Overall, Glas programs are not too different from other imperative-OO languages for error handling.

## Concurrency

Glas naturally supports fine-grained dataflow concurrency. However, between early returns and the implicit environment, dataflow is sequential by default. To solve this, Glas uses `async (e) { actions }` to forbid early returns, control the environment, and document the intention of concurrency. This immediately returns an `(env:, result:)` pair of futures, while computing in a background thread.

## Concurrent Channels

With futures, concurrent interactions can be modeled as data structures. For example, a channel can be naively modeled as a future list. A producer thread can write the tail of the list via unification, while a separate consumer thread reads the head of the list. We could also read and write the channel within the same loop.

        fn write(channel:ch, data:x) = 
            // assume a pass-by-reference channel
            ch.in := list:(head: x, tail: ch.out);

        fn read(channel:ch) =
            match(ch.in) {
            | list:(head:x, tail:xs) -> ch.out := xs; x
            | 'null -> ch.out := (); raise 'eoc;
            }

I describe this model of channels as 'naive' because it lacks support for bounded-buffers, early termination by the reader, or other useful features. But it does demonstrate how channels can be modeled above futures for sharing between threads. Glas will have built-in support for abstract channels with nicer properties, with protocols modeled by session types. Abstract channels will be modeled as objects.



## Postfix Function Calls




## Pattern Matching

Various patterns including:

        foo:    - same as `foo:foo`




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


## Channels and Rendezvous

Naively, a channel can be modeled as a deferred list. For example, 

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

