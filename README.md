# Glas Language

Glas is a programming language designed for concurrency, functional purity, partial evaluation, and static allocation.

## What makes Glas language different?

There are a lot of languages vying for your attention. Why should you consider Glas?

### Session Types for Concurrent Partial Evaluation

Glas safely exposes partial evaluation of pure functions via an adaptation of [session types](http://simonjf.com/2016/05/28/session-type-implementations.html). Session types are very expressive, capable of representing multi-step interactions with decision trees. In the general case, unbounded recursive interactions can be represented. In Glas, these interactions logically occur between a function and its caller. However, these interactions may be abstracted, handled by other function calls. Together with dataflow optimization, the resulting system may easily grow into a network of flexibly connected processes.

Exposure of partial evaluation greatly improves the expressiveness of purely functional programming. Concurrent compositions can be expressed that cannot conveniently be represented in conventional functional programming languages. Multi-step sessions with choices are more flexible than object-oriented interfaces. Concurrent interaction loops provide a flexible basis for an effects model.

Unlike many concurrency models, concurrency based on partial evaluation is relatively easy to erase: a compiler can unravel and reweave the component functions such that every variable is assigned in a sequential order. Parallelism can be recovered by analyzing the data dependency graph, and selecting sufficiently large subgraphs. By use of these techniques, a Glas compiler can achieve much greater parallelism than would typically be achieved by fine-grained concurrent computations.

### Solid Computation for Hardware Synthesis

Glas is a two-phase language, fluid and solid, distinguished in the type system. 

Fluid computation supports first-class functions and heap allocation of memory. In contrast, solid computation ensures that code uses a constant amount of memory and may be implemented using constant dataflows. Solid functions are suitable for hard real-time and embedded systems, and for synthesis of FPGA or ASIC models. By leveraging a concurrent interactions loop, effectful and deeply stateful applications can be modeled.

Glas is intended to be an expressive hardware description language, an alternative to VHDL or Verilog. The fluid computation phase can support modeling of rich constraints and preferences, and eventually compute a solid module. This module may be freely tested or reused a pure function, and eventually synthesized to hardware.

### Fluid Computation for General Applications

Solid computation is unsuitable for many applications. For example, it's awkward to parse HTML or JSON into an abstract syntax tree under the constraint of statically allocated memory. A solid parser will necessarily make assumptions about the maximum depth and breadth of the inputs, and preallocate for the assumed worst case.

For this reason, fluid computation in Glas is not restricted to compile-time. Applications may have a fluid type. Glas will still offer an expressive basis for concurrency and effects, deep analysis for parallelism, and competitive performance. 

### Imperative Code with Purely Functional Semantics 

Glas syntax is extremely imperative in look and feel, even more so than most imperative languages. However, Glas programs may be reasoned about and tested as pure functions.

Conventional imperative languages use a syntax for function calls of the form `let r = f(x,y,z)`. However, this syntax is often unsuitable for Glas, where we'll frequently defer or omit assignment of a parameter, and we'll frequently access results before computation of the function has completed. 

Instead, Glas syntax is based on keyword parameters. Outputs are generally represented as output parameters. This separation is convenient for interleaving partial inputs and outputs. A trivial example:

        var c1 = fn1    \\ type a?c!b?d!
        var c2 = fn2    \\ type w?x!y?z!
        c1.a = expr1
        c2.w = c1.c
        c1.b = c2.y
        c2.x = c1.d
        var result = c2.z

A function call is represented by assigning the function to a variable, then 'assigning' input parameters and 'accessing' results similar to a record value. Variables in Glas have a [static single assignment](https://en.wikipedia.org/wiki/Static_single_assignment_form) semantics, compatible with purely functional computation. The above computation represents a very trivial concurrent operation, where evaluation of `fn1` and `fn2` interact concurrently. The program itself can be understood as a thread scheduler.

Glas will statically guarantee that input parameters are assigned at most once, and that outputs are not accessed before they can be computed. 

*Note:*  Type comments correspond to session types, e.g. `a?c!` roughly means `receives a then produces c`. This trivial session type can be represented in a conventional language, e.g. `fn1 : a -> (c, b -> d)`. However, implementation of this type requires an indirect style with continuations often represented by closures. With Glas, the component functions are also represented in a direct style.

### Sequences as Channels

To support long-lived solid computation, Glas language has specialized support for sequences and stream fusion. It is easy for functions to process and produce unbounded sequences in a loop, using bounded memory. The concurrent systems we can represent by partial evaluation of sequences essentially subsumes [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks), while being much more convenient for abstraction and singleton inputs or outputs.

### Interaction Loops for Effects

With session types, we are not limited to pure data types. An output from a function may request additional inputs, or vice versa. Interactions may generally be recursive. In Glas, we might understand these interactions as operating on a tree or sequence of parameters. These interactions are much more flexible than we can easily represent using data channels. 

Glas applications will operate on a sequence of requests, perhaps to access the network or filesystem. The set of supported effects would depend on request type, which would depend on requirements and the context of use.

### Content-Addressed Dependencies

Glas programs will reference dependencies by secure hash. This significantly simplifies versioning, distribution, caching, and security. Glas programs also use secure hashes to integrate arbitrary binary data resources, such as font files or music data.

However, Glas will discourage hard-coding of dependency references. Instead, modules are parameterized by their dependencies. Secure hashes can thus be isolated to a few top-level configuration modules, which parameterize component modules as needed. For development in a filesystem, command-line tools might maintain these top-level modules based on a more conventional package configuration file.

## Language Status

Glas is a very new language as of mid July 2019. 

At this time, formalization of syntax and type system has barely started. I intend to develop and refine details using the wiki. Some challenges remain - e.g. the sequence model is just a glimmer in my eye, and I haven't figured out Glas's approach to generic programming quite yet. Glas will remain in a fluid state for a while. 

Glas is also a relatively low level language with its imperative style, standard numeric types. Developing a high-performance compiler, initially via Clang, should be much easier for Glas than for my prior languages (which have often died due to performance concerns). 

If you're interested, invite yourself to participate. 

