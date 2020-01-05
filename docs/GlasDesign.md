# Glas Language Design

## Underlying Model

A Glas program is modeled by a directed graph with labeled edges. Evaluation rewrites this graph using two primary rules: *unification* and *application*. 

*Application* is the basis for computation. A function is applied to a node, called the applicand. Input and output parameters are modeled as labeled edges from the applicand. For example, a multiply function might read edges `x -> 6` and `y -> 7` then write `result -> 42`.

*Unification* is the basis for dataflow and concurrency. Unification merges two nodes and propagates over matching edge labels. For terminal values, such as numbers or functions, further unification is considered a type-error. Effectively, unification can model transparent futures with single-assignment semantics.

Functions are bounded subgraphs with a designated public node. When applied, the subgraph is copied inline, and the copy's public node is unified with the applicand. This logically results in monotonic, deterministic expansion of the graph, but subgraphs may be garbage collected when they become irrelevant.

Edges are labeled from a finite alphabet. However, arbitrary labels can be modeled via composition. For example, the label `result` may involve seven nodes in a sequence - one for each character, followed by a sentinel. This implies a trie-like structure.

## Data Models

### Records

In the underlying model, records such as `(x:6, y:7)` are encoded as a node with a labeled edge for each field. The Glas compiler should optimize representation of records to minimize unnecessary indirection.

Records are immutable. However, we can model record 'update' by a function that produces a copy of the record except for specified labels, which are added, removed, or have a value replaced. Glas provides syntax for record updates, and supports [row-polymorphic updates](https://en.wikipedia.org/wiki/Row_polymorphism).

Glas distinguishes 'open' and 'closed' records. An open record is represented as `(x:6, y:7, ...)`. Open records allow new fields to be added via unification. Closed records are the default, such as `(x:6, y:7)`. Closed records will include a hidden, implicit field to support limited reflection over the set of defined fields. Closed records may not add new fields via unification!

The empty, closed record `()` serves as the Glas unit type and value. The empty, open record `(...)` effectively serves as an anonymous future. It is possible to reflect on closed records, e.g. to support optional fields.

*Aside:* Use of parentheses around an unlabeled expression, such as `(expr)`, can serve as a precedence indicator.

### Variants

Glas variants can be modeled as closed, singleton records, e.g. `circle:(radius:3, color:red)` vs. `square:(side:3, color:blue)`. As closed records, a case function may reflect over which field is defined. Additionally, variants may receive special support from type system and compiler.

The unit variant `foo:()` is convenient for modeling enumerations or flag parameters. Glas will provide a convenient shorthand: `'foo` is equivalent to `foo:()`.

*Aside:* A singleton variant can be optimized to use zero runtime overhead. This can be leveraged to resist accidents.

### Lists

List data can be modeled as a recursive structure of variants and records:

        type List a = cons:(head: a, tail: List a) | 'null

Glas will provide a convenient syntactic sugar, such that: `[1,2,3]` essentially expands to `cons:(head:1, tail:[2,3])`, and `[]` expands to `'null`.

### Tuples

Glas will model tuples as invariant-length, heterogeneous lists, e.g. `[1, "hello", (x:6, y:7)]`. Many operations on lists - such as length, zip, concatenation, indexed access - should also work for tuples. Glas type system and compiler will give special support for invariant-length lists and static indexing.

### Arrays

Arrays are essentially lists with a specialized representation - one that optimizes certain operations, such as indexed access, at expense of other operations. Glas may support explicit conversion of representations between arrays and lists.

#### In-Place Mutation

Records, tuples, and arrays can be updated in-place if the compiler can guarantee the prior value will not be read. This can be understood as an optimization of a garbage collection process: we're recycling memory for the old value at the same time that we compute a new value.

This optimization is mostly valuable for arrays, where update otherwise has a cost proportional to array length. In contrast, records and tuples usually have a small, constant size.

Glas shall robustly support this optimization via the type system, i.e. distinguishing linear mutable arrays from shared frozen arrays.

### Nominal and Opaque Data

It is feasible to protect data in Glas using unique, unforgeable labels. Such labels cannot be directly written in source code, but may be produced as a compile-time effect while parsing a program. See *Module Level Languages*, below.

Glas will support nominal and opaque data types based on this mechanisms, together with support from the type system. This allows developers to enforce implementation hiding and smart-constructor patterns. Unique labels could feasibly also be leveraged to model type-driven overloading.

### Data Objects

We can model objects as an opaque record containing fields and method functions, together with syntactic sugar suitable for convenient construction and method invocation. This will be detailed in a later section.

### Numerics (Incomplete)

Glas will support numeric literals such as `6`, `2/3`, and `3.14`, and arithmetic functions. 

However, I'm still contemplating how numbers should be represented concretely, and how programmers should control this representation. I would prefer to avoid the ad-hoc and awkward abstraction leaks of numeric representations in conventional languages. I have some ideas to use of refinement and shadow types, and explicit modulus, to restrict ranges precisely and typefully.

I'm also interested in support for units of measure. This might also involve shadow types.

## Glas Module System

The module system is an important part of a language's user experience.

A Glas 'module' is a value defined externally, usually in another file. Glas modules do not export symbols or definitions in the general case, e.g. the value defined could be a function or binary. But it is feasible to import symbols from closed records into lexical scope, like a conventional module system.

*Note:* Futures or open records in a module's value cannot be assigned by the client of the module. They simply remain opaque and undefined.

### Binary File Modules

For a subset of files, their 'value' is simply their content as a binary array. This ability to modularize binary data, without relying on filesystem effects, is convenient for partial evaluation, caching, DSLs, and distributing resources such as documentation or training sets for machine learning.

Which files are treated as binary data is determined by file extension. Files with a `.g` suffix use the Glas language, while all other files are valued by their binary content. Currently, this is not configurable. However, it is feasible to load and process binary data at compile-time.

File extensions are not exposed to clients of a file module. Thus, a developer could switch transparently between including a binary or computing it without modifying module clients.

### Filesystem Directories

In Glas, a module may be represented by a directory containing files and subdirectories. 

The default value for a directory is to map the files and subdirectories into an open record. However, if a directory contains a `public.g` file module, the value of the directory module becomes the value defined by this file, hiding the directory structure from the client.

*Note:* If filesystem names are problematic, e.g. if ambiguous or not portable, the compiler should issue a warning or error. Also, file or directory names starting with `.` are hidden and not considered part of the module. 

### Module Dependencies

Dependencies are restricted: a file may only access modules within the same directory, or distribution packages from the network (see below). Relevantly, it is not permitted to reference modules in the parent directory, and Glas does not use a filesystem search path.

This restriction ensures that Glas directories are effectively stand-alone, easily shared and reused assuming they don't depend on any uncommon packages.

### Distribution Packages

Glas packages are essentially modules from the network instead of the filesystem. A package will be always represented by filesystem directory. This simplifies inclusion of documentation, unit tests, digital signatures, and other metadata.

However, there are too many problems with managing packages individually. An update to a package can silently break other packages that depend upon it. If we depend on several packages, it becomes a constraint problem to ensure all versions work together. Further, it can be difficult for anyone other than the package 'owner' to maintain packages provided via centralized server.

To avoid these problems, Glas packages will come from distributions, which have only one version of each package. Packages in a distribution can be typed, tested, and verified to work cohesively. Any problems can be made visible, and the programmer could choose to fix or remove broken packages. Distributions will support DVCS patterns such as fork, merge, and pull requests.

A Glas development environment will be configured with target distributions for a package under development. Dependencies can be downloaded for local evaluation.

*Note:* A package may be shared between several distributions. There is a possibility of naming conflicts. However, in practice, this should not be a significant problem. A developer should simply work with community distributions to stake a claim on a name if needed.

## Glas Language Manipulation

Glas is a general purpose language, designed for a text-based development environment. Consequently, Glas is not optimal for any specific purpose, and is not suitable for alternative development environments.

To solve this, Glas provides a mechanism for ad-hoc language manipulation.



*Note:* I use the word 'manipulation' instead of 'extension' because this mechanism is not incremental or compositional in nature. 


Glas supports user-defined languages within a module, based on parser combinators.


 at the top of the file, e.g. via `%lang` (or perhaps `%lang` - still thinking about aesthetics).

A file will indicate its language based on file extension and compiler configuration. Binary files are the most trivial module-level language.

A 'language' will be defined by parser combinator operating on an abstract linear object representing the compile-time environment. By invoking methods, programmers can incrementally parse the input and construct an abstract result, meanwhile annotating things to simplify tooling and debugging. 

Compile-time effects, such as imports or generating unique labels, are also supported.

Glas is defined as a module-level language, and will provides mechanisms for its own extension. Glas is intended to be effective for general purpose programming within the limits of the underlying model.

Alternative languages should usually be domain-specific, e.g. optimized for constraint programming or machine learning. One exception is adapting the Glas system to alternative development environments, e.g. developing a syntax optimized for convenient rendering and layout.

*Aside:* A weakness of parser combinators is that they're often slow, because they prevent a lot of optimizations. This could be mitigated by defining module-level BNF language that can be optimized before generating the parser combinator.


## User Experience

Glas favors a linear imperative-OO style to work with the underlying model. The standard Glas language shall support mutable variables, while loops, and method calls. There is an implicit parameter to represent the ambient environment, e.g. access to console or network.

Imperative programming style is convenient in context of single-assignment semantics. Effects, such as reading or writing to console, are ultimately based on futures and partial evaluation. 

However, linearity hinders modeling of software design patterns that involve shared mutable state. In these cases, developers may simulate a shared environment or adopt design patterns originally developed for purely functional languages.

Glas programs are deterministic by default. Non-determinism should be modeled as an effect, if necessary.


## Booleans

Booleans could trivially be modeled as `'true | 'false`, but I'm uncertain whether this is the best way to model them, e.g. in context of operator overloading. Instead, developers may have access to `true` and `false` primitive objects.

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

In the underlying model, the environment of lexical variables can be modeled as a record, threaded from one statement to the next with updates for mutable variables. The actual representation of the lexical environment is abstract.

Variable declarations may also use `let` for pattern-matching and constants. Use of constants where feasible is encouraged. Additionally, Glas supports reference variables and RAII pattern variables.

However, unlike 'true' imperative languages, Glas cannot alias and share mutable variables. Sharing requires careful design in Glas, discussed in a later section. Not all imperative design patterns work in Glas.

## Transparent Futures

The empty, open record `(...)` serves as an anonymous placeholder for a value. This placeholder may later be 'assigned' via unification. This pattern is historically called a [future](https://en.wikipedia.org/wiki/Futures_and_promises). In Glas, futures are transparent in the sense that any variable or data field may be a future without requiring a change in the reader's syntax (e.g. no `force` action).

Glas will support futures. Programmers can declare variables like `var x;` as shorthand for `var x = (...);` or `vars x,y,z;` as shorthand for `var x = (...); var y = (...); var z = (...);`. We can write `x := 42` to explicitly unify `x` with `42`, distinct from updating `x` as a mutable variable.

Due to the underlying semantics, Glas supports partial unification between open records. For example, `(x:6, ...)` and `(y:7, ...)` would unify as `(x:6, y:7, ...)`. However, constants such as `6` or `7` may not unify further. 

Static analysis will prevent Glas programs from even trying. The challenge is providing a reasonable compromise between expressiveness, safety, and modularity. Session types, constraint satisfaction, and other techniques will be used to improve expressiveness.

## Pass-by-Reference Parameters

Pass-by-reference parameters are convenient for abstract manipulation of mutable variables. As a trivial example, we could abstract `x = x + 1` to `increment(&x)`. Glas simulates pass-by-reference via in-out parameters and futures. 

Glas will effectively rewrite the `&x` to an `(in:, out:)` pair with the `out:` parameter unifying with the future `x`. That is effectively `{ const prev = x; x = (...); (in:prev_x, out:x) }`.

This is not abstract, just a lightweight syntactic sugar. Programmers may use an expression returning an in-out pair anywhere a reference is permitted. The receiving function may explicitly access the in-out pair:

        fn increment(x) { x.out := x.in + 1; }

However, Glas also supports `&x` in patterns: 

        fn increment(&x) { x = x + 1; }
        ref x = &annoyingly_large_name;

In these cases, the `out:` field is implicitly assigned just after the final update to the variable `x`. This assignment is injected by the compiler. 
`ref x = (in:(...), out:(...))`, or any expression returning an in-out pair.

*Aside:* We can relate variable declarations through this reference pattern. For example, `var x = 0`, `ref x = (in:0, out:(...))`, and `let &x = (in:0, out:(...))` all have the same behavior.

## Ambient Environment

Imperative programs conventionally operate in an ambient environment with access to console, filesystem, network, etc.. For programmers, this environment is convenient because there is no need to explicitly route access to the resources through the program. However, it can also be difficult to control.

Glas simulates this ambient environment by implicit pass-by-reference parameter `env`. Every function will implicitly receive and return `env`.

Exactly how this is used is left to developers, e.g. we could use `env.stack` for stack-based programming with `push`, `pop`, `dup`, `drop`. We could add `env.canvas` for turtle graphics. We could leverage channels to model access to console or network. 

Use of environment will be tracked in a function's type. A 'pure' expression can be typefully characterized as working universally in any environment. Thus, it is easy to typefully enforce purity or access to effects. Further, this environment can be directly controlled, as a whole value. 

However, unlike the conventional imperative environment, the Glas environment is linear, single-threaded. Sharing a console between multiple threads in Glas would require an explicit model to fork and merge interactions. 

## Object-Oriented Programming

Objects in Glas are simply modeled as records of fields and methods. There is no strong distinction between records and objects. Methods are simply functions with a `(self:, args:)` parameter pair. Glas provides syntactic sugar to invoke methods: `foo!bar(x)` desugars to `foo.bar(self:&foo, args:(x))`. Similarly, `foo?baz(y)` desugars to `foo.baz(self:foo, args:(y))` for queries on constant objects.

To improve concision, Glas will allow implicit `self` in most cases, via specialized function declarations for action and query methods, and via implicit self when the object variable is elided:

        .field => self.field
        ?query(args) => self?query(args)
        !action(args) => self!action(args)

A useful pattern for OOP is method call chaining, e.g. we would say something like `obj.with_x(a).with_y(b).with_z(c);` This cannot directly be achieved in Glas due to how pass-by-reference is simulated. However, it is feasible to say `{ ref self = &obj; !with_x(a); !with_y(b); !with_z(c); }`, overriding `self` within a limited scope. If concision is the goal, use of `ref o = &large_object_name` should also prove convenient.

Glas will also provide flexible abstractions to construct and type objects, e.g. with interface types and mixin-based inheritance. The details will receive more attention under *Object Definition*. 

*Aside:* Conceptually, we can can model functions as taking four parameters: `(self:, env:, args:, return:)`. Glas syntax makes `env:` and `return:` fully implicit, `self:` optionally implicit, and `args:` is always explicit.

*Note:* We can observe a relationship that `foo!method(args)` is equivalent to `&foo?in.method(args)`. It might be useful to support an intermediate syntax for the `?in.`, perhaps `!&` or similar.

## Error Handling

It's convenient if we can focus program logic on a 'happy path' without repetitive, conditional error-handling code. Glas provides several features suitable for error handling. 

First, the implicit environment may carry error handlers, logging, etc.. This can model effectful or resumable error handling.

Second, Glas will support a lightweight early-return pattern. Use of `try (expr)` requires `(expr)` returns a variant `ok:a | err:e`, then either returns `a` or will early-return from the calling function with `err:e`.

        var f = try read_file("foo.txt");

This is roughly equivalent to:

        var f = match(read_file("foo.txt")) { 
            | ok:a => a 
            | err:e => return err:e 
            }; 

Note that of `try` is inverted from conventional `try/catch` exceptions-based error handling. Early returns in Glas are explicit, no exceptions. Without `try`, we would just store the `(ok:|err:)` variant. However, as with exceptions, use of RAII patterns enables early return without cleanup clutter.

Third, for errors that should never occur at runtime, a Glas program may `abort` or `absurd` with some message. Abort cannot be observed or caught within the program (modulo advanced use of reflection). Use of `absurd` documents that a condition should be *proven* to never occur, via global static analysis, but has the same behavior as `abort` when this proof is not performed.

Overall, Glas is not too different from other imperative-OO languages for error handling, modulo use of explicit early-return instead of implicit exceptions.

*Aside:* We can support annotations like `noabort` to document intended safety properties of the program. We can also use `ok:` as a singleton variant, for code that never fails.

## RAII Patterns

Glas will support [RAII](https://en.wikipedia.org/wiki/Resource_acquisition_is_initialization) patterns to simplify cleanup in context of early returns and error handling. The following features are supported via local rewriting:

        unwind { action }
        use x = expr; 
        use expr;

All `unwind { action }` statements execute in reverse-order, as the program leaves the scope in which they were defined. Relevantly, this works for early returns, too. The `use x = expr;` is shorthand for `var x = expr; unwind { x!dispose(); }`. And `use expr;` has the same behavior but with an anonymous variable.

*Aside:* Reference variables and patterns may also be understood in terms of RAII, i.e. `ref x = expr;` as shorthand for `const _ref_x = expr; var x = _ref_x.in; unwind { _ref_x.out := x; }` where `_ref_x` is anonymous to the client.

## Loops

In the underlying model, loops are modeled as recursive functions that fractally, monotonically expand the graph. However, in practice we cannot expand the graph without limit, we should control memory consumption. Further, we'll want loops that work nicely with imperative variables. So, Glas supports imperative loops:

### Conventional Imperative Loops

Glas supports conventional imperative loops. 

        while(cond) { }
        do { } while(cond);
        for (pattern) in (seq) { }

The `while` and `do-while` loops behave as one familiar with imperative programming might expect. The `for` loop requires a sequence object (details TBD), rather than an index-based approach. Loops will support the conventional `break` and `continue` statements for early exit.

Glas will likely support variation loops for convenience and clarity:

        until(cond) { } => while(not (cond)) { }
        do { } until(cond); => do { } while(not (cond));
        loop { } => while(true) { }

### Labeled Loops

With nested imperative loops, it is convenient if we can `break` or `continue` an external loop. For this, we could support labeled loops. A proposed syntax, borrowing from Rust:

        'foo: while(cond1) {
            while(cond2) {
                if(cond3) { continue 'foo; }
            }
        }

Glas currently uses `foo:` and `'foo` for variants, with `'foo = foo:()`. So, there shouldn't be any syntax conflict. 

*Aside:* We can also leverage this with a pseudo-loop: `'foo: cyclic { }` would allow use of `break 'foo` to exit a block. We could define `cyclic { }` as equivalent to `do { } while(false);`. 

### Tail-Recursive Loops

Tail-call optimization (TCO) is the name for a useful optimization where the memory allocated to a function call is recycled in-place. For example, with `return foo(args)` we might be able to recycle the current function call. When every function in a recursive loop is tail-call optimized, we can achieve bounded memory consumption.

Unfortunately, TCO is easily hindered by RAII patterns.

I propose `become` as an alternative `return`, explicitly for tail-calls in context of RAII. Use of `become foo(args)` will perform RAII cleanup after evaluation of `foo` and `args` but before the application, and clearly documents that TCO is required by the programmer.

*Note:* Ideally, the Glas type system should also support static reasoning about memory usage, including both parameters and tail-recursive loops. However, I don't have a solution for this at the moment.

### State Machine Loops

There are several ways to model state machines. Consider two. First, every tail-recursive loop is implicitly a state-machine, with the 'state' being represented by the current function. Second, we can use a state-loop-match pattern:

        var state = 'init; 
        loop { 
            match(state) { 
            | 'init => 
                ... state = a:("hello");
            | a:(s) => 
            ...
            } 
        }

The tail-recursive loop has a few advantages: the environment type (e.g. the type of `env`) can easily vary based on which state we're in, and we also have a convenient termination behavior with a return value. However state-loop-match is easier to visualize, control, and provides more convenient access to other variables in lexical scope.

This is a common and useful pattern, so I'm inclined to provide a dedicated loop in Glas for state machines, with the combined features of tail recursion and state-loop-match patterns. A candidate syntax:

        whilst ('ini) {
        | 'ini => ...; goto a:("hello");
        | a:(s) => ...;
        }

The word `whilst` is chosen here as a portmanteau of `while state` and also punning with the British alternative spelling of `while`.

## Macros and Language Extension

Glas has many second-class features, e.g. `if` and `while` and `try` statements cannot be defined as first-class functions. Consequently, Glas benefits from a syntactic abstraction model, such that programmers can create their own keywords or embedded problem-specific languages.

## Conditionals and Pattern Matching

## Function Definition



## Postfix Function Calls


## Object Definition

## Operator Overloading?

It is feasible to support operator overloading by rewriting `6 * 7` to `6 ?times (7)` or similar. However, this will require a lot of careful design work, and I'm uncertain whether it's the best approach. Also, it doesn't generalize very nicely to short-circuiting boolean operators.

Ultimately, we might need to model operators as a language extension of sorts. 


## Concurrency

Glas naturally supports fine-grained dataflow concurrency. However, between early returns and the implicit environment, dataflow is sequential by default. To solve this, Glas uses `async &e { ... }` to control the environment, forbid or isolate early returns to limited scope, and document the intention for concurrency. This statement immediately returns a future result and environment (the latter by reference), and may evaluate the body in a separate thread.

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

## Safety

TODO:

* read Alceste Scalas, Nobuko Yoshida. "Less Is More: Multiparty Session Types Revisited." POPL 2019.
* read "Transparent First-class Futures and Distributed Components" by Cansando et al.


## Closures

## Functions


Functions in Glas have a single explicit argument. But we'll often model this argument as a closed record. By leveraging reflection over closed records, functions can support optional parameters. We can further leverage `('foo, 'bar, 'baz)` as shorthand for `(foo:(), bar:(), baz:())` to concisely represent optional flag parameters. In addition to the explicit argument, functions may have an implicit in-out  representing the external environment.  



## Channels and Rendezvous

Naively, a channel can be modeled as a deferred list. For example, 

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
