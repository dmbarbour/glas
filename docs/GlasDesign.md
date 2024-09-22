# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and phased liquid-to-solid creation with glass being vaguely analogous to staged metaprogramming. It can also be read as a backronym for 'general language system', which is what glas aspires to be.

Design goals for glas include compositionality, extensibility, reproducibility, modularity, staged metaprogramming, live coding, and distributed computing. Compared to conventional languages, there is much more focus on compile-time computation and many design constraints to simplify liveness. 

Interaction with the glas system is initially through a command line 'glas' executable. See [Glas CLI](GlasCLI.md) for details.

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data, align well with needs for parsing and processing languages, and are relatively convenient for persistent data structures and content addressing of very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)`, either type `(a + b)`, or unit `()`. However, glas systems will favor labeled data as more human meaningful and extensible. We will encode labels such as 'height' and 'weight' into the left-right-left path into a binary tree, using UTF-8. We add a NULL separator between label and value, then encode dictionaries such as `(height:180, weight:200)` as [radix trees](https://en.wikipedia.org/wiki/Radix_tree). A variant is encoded as a singleton dictionary. 

To efficiently represent dictionaries and variants, we must compactly encode non-branching sequences. A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encodes 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     // branch point
            | Stem64 of uint64 * Node   // all 64 bits!

        Stem Encoding (0 .. 63 bits)
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits
            00000..0     unused

This can also efficiently encode bitstrings of arbitrary length as a Stem terminating in a Leaf, which is useful when encoding integers. 

### Integers

Integers in glas systems are typically encoded as variable length bitstrings, msb to lsb, with negatives in one's complement:

        Integer  Bitstring
         4       100
         3        11
         2        10
         1         1
         0               // empty   
        -1         0
        -2        01
        -3        00
        -4       011

See also *Numbers*, below.

### Lists, Arrays, Queues, Binaries

Sequential structure in glas is usually encoded as a list. A list is either a `(head, tail)` pair or a leaf node, similar in style to Lisp or Scheme lists.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases, such as random access, double-ended queues, or binaries. To enable lists to serve many sequential data roles, lists are often represented under-the-hood using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the 'Node' type described earlier with logical concatenation and array or binary fragments.

Binaries receive special handling because they're a very popular type at system boundaries (reading files, network communication, etc.). Logically, a binary is a list of small integers (0..255). For byte 14, we'd use `0b1110` not `0b00001110`. But under the hood, binaries will be encoded as compact byte arrays.

### Numbers, Vectors, and Matrices

Arbitrary rational numbers can be encoded as a dict `(n, d)` of integers. The rational subset of complex or hypercomplex numbers can be modeled as dicts `(r, i)` or `(r, i, j, k)` of rationals or integers. A vector is encoded as a list of numbers. A matrix is encoded as list of vectors or matrices of identical dimensions. Arithmetic operators in glas systems should be overloaded to handle these different number types where it makes sense to do so. 

For performance, a runtime may support optimized internal representations. A useful subset of rationals could be efficiently represented and processed as floating point numbers. Matrices could be implemented as an array with dimension metadata.

*Note:* Arithmetic in glas is exact by default, but there will be workarounds for performance.

## Programs and Applications

A program is a value with a known interpretation. An application is a program with a known integration.

Program semantics are specified for a structured plain old data intermediate representation, `g:(ns:ProgramNamespace, ...)`, that primarily encodes a namespace of methods. The [namespace model](GlasNamespaces.md) lets us compose, shadow, abstract, and override definitions. Methods use an [abstract assembly](AbstractAssembly.md), letting us constrain, extend, or sandbox language features through the namespace. A set of [primitive AST constructors](GlasProg.md) and abstract `sys.*` effects APIs will be added to the namespace by a late-stage compiler or runtime. The 'g' variant and 'ns' dict headers provide opportunity for future extensions and integration options.

Instead of a 'main' procedure, a [glas application](GlasApps.md) defines transactions for 'start', 'step', 'http', and other methods recognized by a runtime. The primary loop is to repeatedly evaluate 'step' in separate transactions. Assuming optimizations for incremental computing and replication on stable non-deterministic choice, a repeating transaction can represent reactive systems and multi-threading. With a mirrored runtime, applications may be distributed and resilient to network partitioning. By optionally binding state to an external database, we gain orthogonal persistence.

Front-end syntax is user-defined, aligned with file-extensions to support external tooling. Eventually, we may develop syntaxes suitable for structured or graphical programming. We can also implement compilers for multi-media or resource files, integrating them as modules with compile-time checks and partial evaluation. To bootstrap this system, we specify a [".g" syntax](GlasLang.md) for general purpose programming.

In my vision for development and user experience, glas applications may present an [interactive, live coding view](GlasNotebooks.md) on demand, allowing interested users to peek and poke under the hood. This should be the default, though could also disable this view if we want a more conventional application.

## Modularity

Modules in the glas system are closely aligned with files, albeit extended to remote DVCS repositories. Modularity is supported for both configuration files and program files.

A typical user configuration will import a community or company configuration from DVCS, then override a few definitions to integrate user preferences, projects, resources, and authorizations. In addition to specifying runtime options such as persistent data or mirroring, the configuration serves as a curated package manager and build system, defining a vast library of applications and reusable software components. Performance is mitigated by lazy loading and caching.

In addition to a file or other source, compilation is parameterized by an environment: a read-only, localized view of the configuration namespace. This environment provides access to reusable components. Utilities for maths, graphs, xml, datetimes, network protocols, and so on should be separately compiled and integrated through the configuration namespace. This environment can be localized per compile, and we can leverage this for implicit parameters like feature flags. But in practice we'll usually want a 'global' module namespace per project or community.

We will control dependencies between files. For security reasons, a remote DVCS file must not reference local files. To simplify packaging and sharing of code, program files will generally be restricted to reference other files in the same folder or subfolders. To enforce these restrictions, we'll abstract over file locations and encode restrictions on constructing relative paths (see *Data Abstraction*).

It is possible to compute application programs directly in the configuration without separate source files. Doing so might be convenient when the application is a trivial extension, refinement, or composition of other applications.

## User-Defined Syntax

To process a file with extension ".x.y", we'll search for compiler "lang.x.y" in the compilation environment. To simplify projectional editing or notebook interfaces, file extensions do not implicitly compose: a ".x.y" files uses "lang.x.y". As a special case, ".g" files may be processed using a built-in compiler even if "lang.g" is undefined, and we'll attempt to bootstrap "lang.g" if it is defined using the built-in. 

A compiler is represented as a program (i.e. `g:(ns:ProgramNamespace, ...)`) with a partial namespace. It is possible to encode functions as partial namespaces with simple conventions. A typical integration provides `lhs = file:AbstractFile`, binds `env.*` to the compilation environment, and assumes `rhs` represents the compiled result, often another program. We also provide computation primitives via `%*`. Both `env.*` and `%*` should be read-only, enforced via 'remove with warnings' translation. In the general case, both `lhs.*` and `rhs.*` are writable to support bi-directional dataflow with multiple inputs and outputs, we aren't restricted to plain old data. We might eventually support alternative integration based on program metadata in the dict beside 'ns'.

When importing a file, 'rhs' must define a program value, i.e. type `rhs : unit -> Program`. We'll evaluate `rhs()` in the compilation environment then integrate the program namespace into the client namespace (with translations and overrides). When compiling an application or reusable component, we usually also want a program value. Thus, in most cases, 'rhs' should define a program. Nonetheless, a compiler for ".json" files might simply output structured data, and it would simply be an error to directly import this file or run it as an application.

### Syntax Bootstrap

We define an [initial syntax](GlasLang.md) to support bootstrap, with the ".g" file extension. If "lang.g" is undefined, we'll use a built-in compiler. If it is defined in terms of ".g" files, we'll attempt to bootstrap. 

Bootstrap consists of first building "lang.g" using the built-in, then using this version to build "lang.g" again. Then we should verify a fixpoint. The last two versions may be distinct because the built-in compiler uses different internal names, annotations, optimizations, etc.. But we can verify whether the final "lang.g" will exactly compile itself.

Bootstrap will usually occur at the configuration layer. The initial configuration file will usually be a ".g" file because it must be immediately understood, but then we'll attempt to evaluate `module.lang.g` to bootstrap this configuration file (taking `module.*` as our default compilation environment).

It is feasible to support other built-in languages. Perhaps we add support for [".glob" files](GlasObject.md). This would be convenient for automating configurations. In theory, we can bootstrap several built-in languages together, allowing mutual definition in terms of each other. However, I think it's better to limit the number of bootstrap dependencies and favor something human-readable as the starting point.

## Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, testing, debugging, type-checking, optimizations, and other non-semantic features. As a general rule, annotations should not influence observable behavior modulo reflection. Thus, it should be safe to ignore unrecognized annotations, albeit with a warning to resist silent degradation of system consistency or performance. Within these limits, the glas system favors annotations over semantic operations where feasible.

In abstract assembly, annotations might be generally represented using `(%an AnnoAST ProgAST)`, scoping over a subprogram. The AnnoAST might be something like `(%log ChanExpr MessageExpr)`. Ignoring the annotation, this should be equivalent to ProgAST. 

Annotations may also be embedded in the program, namespace, or data layers. At the program layer, we might use `g:(ns:Namespace, an:Annotations, ...)` to guide integration. In the namespace, we can easily introduce a simple naming convention where `foo.#doc` is an annotation for 'foo', though this would only be useful for tools that browse the namespace. In the underlying data representation, we might embed hidden metadata to support data abstraction or tracing.

### Automated Testing

In my vision for glas systems, users can easily view health of glas systems based on automated testing across entire community distributions of applications and modules. Further, developers should be able to determine how potential changes to modules would propagate and influence system health before committing. 

To express tests, we might use compile-time assertions. Where assertions are awkward or limiting, we can introduce other annotations for property testing or fuzz testing. Alternatively, we could use compile-time logging to emit test programs to specific channels. 

In context of lazy loading, users would notice a subset of tests when first loading an application, and they could configure whether they run these tests. To identify *all* the tests would require expanding the full configuration namespace, and perhaps further evaluating individual applications. But this can be mitigated by caching a dependency graph, such that we don't need to recompute all the things every time. 

A community could easily maintain a server that performs all the tests across multiple proposed DVCS branches, and builds a report based on these test results and test outputs (pass/fail plus logs, profiles, and so on). 

## Performance

### Acceleration

I touch on the notion of acceleration when describing the *List* and *Number* types for glas data. More generally, a runtime can develop specialized data representations and functions to leverage them, then let programmers replace a reference implementation with a more efficient version via annotation. In addition to triggering this exchange, the annotation resists silent performance degradation by warning the user if the exchange fails. 

Intriguingly, it is feasible to accelerate simulation of an abstract CPU or GPGPU or process network. With some careful design and ahead-of-time safety checks, we can compile a simulation to run unchecked on actual hardware. This is a viable path to high performance computing in glas systems.

Developing an accelerator is a high risk endeavor due to potential for bugs or security holes. As a rule, we should introduce acceleration only where the reward is similarly great. Aside from lists, numbers, and abstract machines, effective support for graphs, bags, sets, and relational algebras may prove worthy.

### Laziness and Parallelism

We can introduce annotations to guide use of laziness and parallelism. If intelligently applied, parallelism can enhance utilization while laziness avoids wasted efforts. Both can keep transactions shorter, reducing risk of concurrent interference. However, laziness and parallelism introduce their own performance risks. This can be mitigated by heuristically 'forcing' computations. I suggest automatically forcing computations at RPC boundaries to isolate performance risks to each application.

### Memoization

Any computation that can run lazily or in parallel can potentially be memoized. Persistent memoization can be useful for compilation of applications or indexing of a database. We can introduce annotations to guide memoization. Memoization is most easily applied to tree structures, where we compute some monoidal property for a tree node based on the same property of each subtree. But memoizing computations on lists is also very valuable, and may need special attention.

In any case, the use of memoization is assumed in design of glas. Without it, many 'pure' computations such as compilation would require explicit use of state for manual caching.

### Content Addressed Data

To support larger-than-memory data, glas systems may leverage content-addressed storage to offload subtrees to network or disk. This optimization can be guided by program annotations, but should be transparent modulo use of runtime reflection APIs.

Compared to virtual memory backed by disk, content addressing has benefits for incremental computation, orthogonal persistence, and structure sharing. A lot of work doesn't need to be repeated when a hash is known. When communicating large values, it also works nicely with [content delivery networks (CDN)](https://en.wikipedia.org/wiki/Content_delivery_network).

## Thoughts

### Data Abstraction 

Data abstraction is a property of a program, not of data. Data is abstract in context of a program insofar as that program does not directly observe the data, instead observing it only through provided interfaces. Ideally, data abstraction is enforced based on compile-time analysis. However, such analysis is difficult in many cases. Dynamic enforcement is feasible by adding annotations to data:

        type Node =
            | ...
            | Abstract of Key * Tree

Annotations can 'wrap' or 'unwrap' abstraction nodes based on a matching Key. Attempting to observe data without unwrapping it, or attempting to unwrap with the wrong Key, would be a divergence error modulo use of reflection APIs. An optimizer can potentially remove or disable wrap/unwrap annotations when it can be locally proven safe, giving us some benefits of static analysis.

We can further constrain manipulation of abstract data: 'linear' data must not be copied or dropped, and 'scoped' data restricts storage and communication. In this case, dynamic enforcement might involve a few tag bits per Node, perhaps via packed pointers. In general, we could use one bit for linearity and two bits for scope: universal, database, runtime, transaction. In this case, database scope forbids transport over remote procedure calls, while runtime scope prevents persistent storage.

Support for abstract 'universal' data is possible, but relatively expensive to enforce beyond the remote procedure call horizon. We could use encryption and decryption for wrap and unwrap, or allocate read-once variables for linearity. To mitigate costs, we might favor 'weak' types at the universal scope to express intentions and resist accidents. In that case, the Key might be a GUID or URL.

*Note:* Holding abstract data in persistent storage can easily hinder schema update. We could feasibly restrict abstraction to the runtime and transaction scopes by default.

### Type System

We can support type annotations within the abstract assembly, and typechecking on the final namespace. To avoid harming extensibility, it should be feasible to express just fragments of a type, a subset of type assumptions we make locally, leaving details to be refined contextually. Gradual typing should be feasible.

In addition to structural types and data abstraction, I'm very interested in support for 'shadow' types that aren't represented in runtime data as a possible basis for unit types on numbers, staging types, logical locations or latency, and other contextual properties. Perhaps instead of shadow types per se, we could model an ad-hoc static computation that propagates bi-directionally through a call graph. This could feasibly be expressed as yet another namespace.

Aside from types, I like the idea of annotating other properties and developing a system of 'proof hints' and 'proof tactics'. Ideally, types would be expressed within this system instead of separately from it. In case of shadow types, we'd add 'proof assumptions'. But at the moment, I don't have concrete ideas on how to approach this idea.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. In context of a namespace, these constraints would be subject to override through the namespace and should propagate through the 'call graph', perhaps as an extra compile-time evaluation stage. We could propagate type information to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. 

Incremental computing and caching are also essential. It seems difficult to 'cache' search results in a way that we can deterministically determine whether we need to search again. We might need modular, confluent computations (flexible order, deterministic result) with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic) to filter our options without without recomputing the full system.

Could we build a language around this idea? It's an idea to explore further.

### Provenance Tracking

I need to explore how to debug problems and trace them back to their original sources. In glas systems, this is complicated by metaprogramming at multiple layers, but also somewhat simplified by disfavoring first-class functions or other 'mobile' code abstractions. I like the idea of [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics.

