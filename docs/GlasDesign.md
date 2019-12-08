# Glas Language Design

## Underlying Model

A Glas program is modeled by a directed graph with labeled edges. Evaluation rewrites this graph using two primary rules: *unification* and *application*. Evaluation is monotonic and confluent.

*Application* is the basis for computation. A function is applied to a node, called the applicand. Input and output parameters are modeled using labeled edges from the applicand. For example, if we apply a `multiply` function to a node, the function might read edges `x -> 6` and `y -> 7` then write edge `result -> 42`.

*Unification* is the basis for dataflow. Unification merges two nodes. Unification propagates on matching edge labels. For terminal nodes, such as numbers or functions, unification is a type error. Thus, Glas effectively has single-assignment unification semantics.

A Glas function is defined by a bounded subgraph with a designated public node. When applied, the subgraph is copied then the public node is unified with the applicand. 

Edges are labeled from a finite alphabet. However, arbitrary labels can be modeled via composition. For example, a label `result:` may involve seven nodes in a sequence - one for each character, with `:` as sentinel. Together with unification, this implies a trie-like structure when multiple labels start with the same character.

Glas syntax favors the imperative-OO style to work conveniently with this model.

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

I'm still contemplating how to deal with representation concerns - bit width, fixed point, rationals, floating point, etc.. I'm not satisfied with convention in this regard - too many unintuitive features and abstraction leaks.

Fixed-width representations are essential for performance reasons. Perhaps I can approach this in terms of refinement types, operating on well-defined ranges of numbers, much like we should support fixed-width lists.

At least for now, I intend to maintain full precision for numbers as the default. This may also require some support for symbolic numbers like pi.

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

Glas should also support convenient construction of objects, e.g. using delegation, composition, mixins, or inheritance. Functions declared as methods or queries will implicitly have the `self` argument. Private fields or methods should be protected by the type system. 

Glas may weakly support *everything is an object*. When we write `6 * 7` it might mean `6 ?times (7)` or similar, allowing for operator overloading. Lists and strings are also objects.

## Dynamic Environment

In addition to a lexical environment for declared variables, Glas supports a dynamic environment for effects handlers. This environment is threaded implicitly into every function call, and serves a similar role as thread-local storage or dynamic scope.

### Candidate A - Stack of Second-Class Objects

Effects are invoked as `!action(args)` - i.e. a method invocation, but without a specified object. This operates on the implicit environment. The environment is accessed only through object methods. 

Programmers can control the environment using `with &obj { actions }`. Here, `&obj` must be a pass-by-reference variable or an in-out pair. When invoked, the `self` parameter will contain the object, while effects used within handlers will pass onwards to the the parent environment.

Importantly, this solves a problem: if we're calling first-class functions, we can choose to use `with &self { args.fn(y) }` or similar, to ensure we restore the caller's environment. Unfortunately, for this reason, we cannot support simple extension of effects.

It's unclear how to support `?query(args)` with composable or transitive read-only behavior. We'd be unable to represent `with &self { effects }` within a query.

### Candidate B - Singular First-Class Object

In this option, clients have access to the object as a first-class value, perhaps using `getenv` and `setenv` operations if not something more sophisticated. Because we aren't hiding anything, we may require more typeful control over the environment object to guard it from abuse.

In context of exceptions, `getenv` and `setenv` may prove awkward. A scoped option might be closer to `withenv e { actions }`, or simply define `$` to be the environment. 




* implicit parameter `env` - becomes awkward for `env!foo()`, environment is passed-by-reference as both `self` and `env`. 
* `getenv` and `setenv` - non-linear treatment of environment...
* `swapenv` - linear, but for temp access is not very robust for exceptions
* `withenv e { ... }` - like a scoped `var e = swapenv (); unwind { swapenv(e);}`. This is not bad at all. It
* `withenv (pattern) { ... }` - swapenv to pattern within scope, but unclear what to swap back... 
* `withenv var { ... }` - not bad, can set environment to unit within scope.  esp. if we also use `withenv { ... }` as shorthand for `withenv env { ... }`.





and `withenv { ... }` as shorthand for `withenv env { ... }`

 - scoped var, not too bad, though a little awkward that 90% of cases we'll want to just use `env`.
* `withenv { ... }` - not

not too bad, a little awkward syntactically.
* special function declarations - 
* `getenv` and `setenv` - main issue is that these don't treat the environment a linear object. 

Additionally, Glas may provide convenient shorthand via `!action(args)`, `?query(args)`, and `.field`, eliding lexical variables.



The environment parameter could be leveraged for tacit stack-based programming, or specialized for a subprogram such as adding a canvas for for turtle graphics.

## Exceptions

Statements in Glas may fail with `raise (exception)`. Exceptions can be caught via a `try { block } catch (pattern) { block } ...` sequences. Glas also supports `finally` clauses like Java, and `unwind {}` actions that will execute upon leaving the current scope (whether by return or exception). 

Glas also supports a `use x = ...` variable declaration for RAII patterns. This effective desugars to `var x = ...; unwind { x!dispose(); };` 



 Glas will support the conventional gamut of `try`, `raise`, `catch` keywords and support for `unwind` actions. 

In the underlying model, exceptions are modeled as a variant return value. The Glas compiler should optimize such exception handling has near-zero runtime overhead when no exception is raised.

Exceptions in Glas are not resumable. However, Glas can pass error handlers via the implicit environment, which can solve a similar problem.

Programmers may require via the type-system that some subprograms do not raise exceptions.

*Aside:* Glas is effectively programmed in a state-error monad: state for implicit environment, error for exceptions.

## Concurrency

The underlying model for Glas naturally supports fine-grained concurrency, limited by unification dataflow. However, Glas has several features, most notably the implicit environment and exceptions, that imply sequential computation by default.

Instead, Glas supports concurrent evaluation explicitly via `async (env) {}` block. This evaluates `(env)` synchronously, then evaluates the block asynchronously. All mutable variables are implicitly pass-by-reference to the async block, effectively returned after final assignment.

The return value from `async` is a future `(env:, result:)` pair. The `env` field will contain the final implicit environment value. The `result` is modeled as `return:(val) | raise:(exception)`. Glas may provide a standard function `force`:

        fn force(x) {
                
        }

 such as `force (async)` that will  re-raise the exception (if any)



returning an `(env:, result:)` pair for future output. The `result` will be modeled as a variant `return:val | raise:exception`.



they'll be returned 

The Glas compiler is free to evaluate the `async` block immediately, in another thread, or even as a coroutine. 

This `async` block may capture mutable variables in lexical scope. These will implicitly be passed-by-reference to the async computation, and will be available as output 





The model underlying Glas allows deterministic concurrency based on dataflow. But dataflow in Glas is usually sequential due to the imperative environment. Thus, Glas will provide means to restrict dependency on the imperative envrionment and expose the underlying concurrency.

A promising option is `fork fn` to wrap a function. The wrapper immediately returns the dynamic environment, and also a unification variable for the result. The function is evaluated instead in a fresh dynamic environment, e.g. the unit value. Parameters to the wrapped function are still evaluated in the caller's environment, but may involve pass-by-reference, channels, rendezvous, and similar patterns. 

This design has an advantage of being relatively easy to abstract. For example, we can build a `forkenv` function above `fork` that provides and returns an alternative dynamic environment.



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



## Postfix and Infix Function Calls

We 






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

