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

An interpretation is specified for program values, a structured [intermediate representation](https://en.wikipedia.org/wiki/Intermediate_representation) of form `g:(ns:Namespace, ...)`. The [program namespace](GlasNamespaces.md) lets us compose, shadow, abstract, and override definitions. Each definition is expressed in an [abstract assembly](AbstractAssembly.md), letting us constrain, extend, or sandbox language features through the namespace. A set of [primitive AST constructors](GlasProg.md) is implicitly added by the runtime. The 'ns' dict and 'g' variant headers ensure opportunity for extension.

A [glas application](GlasApps.md) defines transactional methods for 'step', 'http', and other interfaces recognized by a runtime. Compared to conventional a 'main' procedure, transactional methods are more amenable to live coding and orthogonal persistence. We'll also model runtime effects algebraically as implicit parameters to each methods. Intriguingly, a repeating 'step' transaction can also optimize with incremental computing and replication of a stable non-deterministic choice, resulting in a highly reactive and concurrent system. This extends easily to distributed transactions and distributed computing. Orthogonal persistence is based on effectfully binding some application state to an external key-value database.

The front-end syntax for glas is user-defined. To simplify external tooling, this is aligned with file-extensions: to compile a ".x" file, we'll search the local environment for module "lang.x", which must define the front-end compiler. A special exception is the [".g" syntax](GlasLang.md), which will use a built-in implementation as needed. We'll attempt to bootstrap "lang.g" if defined in terms of ".g" files. This initial language is suitable for general purpose programming.

In my vision for glas systems, most applications shoud support interactive development, projectional editing, and live coding through a [notebook interface](GlasNotebooks.md). To achieve this, a compiler will integrate a projectional editor with compiled applications by default, and provide integrate interfaces to simplify notebook composition (pages, tables of contents, etc.). Where we don't need this expensive notebook view, it should be subject to override and dead code elimination.

## Modularity

In glas systems, modularity begins with the configuration. Every user has an root configuration file, indicated by `GLAS_CONF` environment variable with OS-specific defaults, e.g. `"~/.config/glas/conf.g"` on Linux or `"%AppData%\glas\conf.g"` on Windows. A typical user configuration file import a community or company configuration from DVCS then apply overrides for user-specific preferences, projects, resources, and authorizations.

Instead of configuring a package manager or filesystem search path, applications and libraries are directly defined and computed within the configuration namespace. Thus, a remote DVCS serves the roles of package manager, curator, and whole system versioning. A configuration may inherit and integrate definitions from multiple DVCS to distribute responsibility. This design easily results in *very large* configurations, but performance is mitigated by lazy loading and caching.

A typical application or libary definition is expressed in terms of compiling a program file. In addition to the file, compilation is parameterized by a *localization*, providing controlled access to the configuration namespace through a translation, and thus to other applications and libraries. The file is processed based on file extension, searching for "lang.FileExt" in the localization, with special exceptions for "lang.g" and bootstrapping. 

Applications and libraries should compile to program values, i.e. `g:(ns:Namespace, Metadata)`. The namespace type is very modular, supporting flexible composition and extension without rewriting namespace values. Automatic composition can be augmented with metadata. For example, if modules might represent pages or chapters in a notebook, we might automatically compose the table of contents. In some cases, it is reasonable to omit the program file and directly compute a program value.

To simplify security, packaging, and reproducibility, file paths are abstract (see *Data Abstraction*) and their construction is constrained. When loading a file from a DVCS repository, we forbid relative "../" paths that would escape the repository. When compiling a program file for an application, we generally isolate file dependencies to the same folder and subfolders. With runtime support, this abstraction also provides an opportunity to treat a dict of binaries as a read-only folder, model logical filesystem overlays, or simulate a filesystem within a key-value database.

## User-Defined Syntax

A user-defined language module should compile to a program value that defines `compile : f:AbstractFileRef -> CompiledValue` that assumes a limited effects API. Compile-time effects are restricted to read-only queries on the environment to simplify caching, lazy loading, and parallel compilation. The CompiledValue is often another program value, especially in context of notebook interfaces. 

Warnings or errors should be reported through logging annotations, and may also be captured in the CompiledValue. In context of live coding and notebook interfaces, it is most convenient if programs with syntax errors still compile to a projectional editor that provides detailed complaints and recommendations, and perhaps makes a best effort, rather than diverging.

### Syntax Bootstrap

The glas system defines an [initial syntax](GlasLang.md) associated with the ".g" file extension. When "lang.g" is undefined, we use the built-in compiler. If "lang.g" is defined in terms of ".g" files, we'll attempt to bootstrap. Bootstrap involves building "lang.g" with the built-in, then again with itself, then verifying a fixpoint is reached. Verifying the fixpoint generally requires one more build.

A glas executable may support other initial syntax. For example, [".glob" files](GlasObject.md) may prove more convenient for automated configurations. In this case, we may need to bootstrap multiple built-in languages together if they are mutually defined in terms of each other.

The toplevel configuration file *must* use an initial syntax recognized by the system. And we'll generally bootstrap the configuration by first building `"module.lang.g"` within the configuration (assuming `"module."` prefix is the default localization).

## Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, testing, debugging, type-checking, optimizations, and other non-semantic features. As a general rule, annotations should not influence observable behavior except through reflection. Thus, it should be safe to ignore unrecognized annotations, albeit with a warning to resist silent degradation of system consistency or performance. Within these limits, the glas system favors annotations over semantic operations where feasible.

In abstract assembly, annotations might be generally represented using `(%an AnnoAST ProgAST)`, scoping over a subprogram. The AnnoAST might be something like `(%log ChanExpr MessageExpr)`. Ignoring the annotation, this should be equivalent to ProgAST. Annotations purely on context might use a no-op like an empty sequence as the ProgAST.

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

I'm interested in a style of metaprogramming where programmers express hard constraints and preferences while building a call graph. This might be expressed as path-dependent 'costs' based on non-deterministic choices. Costs could be described by emitting ad-hoc values at compile time, which are then translated to positive rational numbers, allowing users to explore different cost heuristics. 

To support this, of course, we need static bi-directional computation with a notion of weighted choice, and we'll need a caching model and modified A* search to support incremental compilation. Intriguingly, this search could be applied to both construction of namespaces and choices within a call graph.

I think it's best to solve the problem of static bi-directional computations first. That would be very useful even without search, e.g. to provide extra context over a call graph. Perhaps it could solve shadow types, or involve them, e.g. units for numbers.

### Provenance Tracking

I need to explore how to debug problems and trace them back to their original sources. In glas systems, this is complicated by metaprogramming at multiple layers, but also somewhat simplified by disfavoring first-class functions or other 'mobile' code abstractions. I like the idea of [SHErrLoc project's](https://research.cs.cornell.edu/SHErrLoc/) blame heuristics.

