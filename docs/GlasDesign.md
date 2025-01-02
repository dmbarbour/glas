# Glas Design

Glas is named in allusion to transparency of glass, human mastery over glass as a material, and phased liquid-to-solid creation with glass being vaguely analogous to staged metaprogramming. It can also be read as a backronym for 'general language system', which is what glas aspires to be.

Design goals for glas include compositionality, extensibility, reproducibility, modularity, staged metaprogramming, live coding, and distributed computing. Compared to conventional languages, there is much more focus on compile-time computation and many design constraints to simplify liveness. 

Interaction with the glas system is initially through a command line 'glas' executable. See [Glas CLI](GlasCLI.md) for details.

## Data

The 'plain old data' type for glas is the finite, immutable binary tree. Trees can directly represent structured and indexed data and align well with needs for parsing and processing languages. They are convenient for persistent data structures via structure sharing, and content addressing for very large values. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

This can generally encode a pair `(a, b)`, a choice `(a + b)`, or a leaf `()`. Alternatively, we could encode these options more directly as a sum type:

        type Tree = 
            | Branch of Tree * Tree
            | Stem of (bool * Tree)  # bool is left/right label
            | Leaf

However, glas systems will often encode data into stems. Dictionaries such as `(height:180, weight:100)` can be encoded as [radix trees](https://en.wikipedia.org/wiki/Radix_tree), while an open variant becomes a singleton dictionary. The naive binary tree encoding is inefficient for this role because it doesn't compact stem bits. In practice, we might use something closer to:

        type Tree = (Stem * Node)       // as struct
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

This allows for reasonably efficient representation of labeled data. We can also easily encode integers into stems. However, we might further extend the Node representation to more efficiently encode arrays and other useful types. 

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

Sequential structure in glas is usually encoded as a list. A list is either a `(head, tail)` pair or a leaf node, a non-algebraic encoding similar in style to Lisp or Scheme lists.

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

A program is a value with a known interpretation. An application is a program with a known integration. In glas systems, programs and applications are carefully designed to support my visions for live coding, user extension, and community curation. 

### Modularity

Modularity begins with configuration. An initial configuration file is selected based on the `GLAS_CONF` environment variable, if defined, otherwise an OS-specific default, e.g. `"~/.config/glas/conf.g"` on Linux or `"%AppData%\glas\conf.g"` on Windows. A configuration may import other files, supporting file-based modularity. Instead of a separate package manager, applications and programs are defined in the configuration namespace. This results in a very large namespace, and performance must be mitigated by lazy loading and caching.

File paths are abstracted and extended to DVCS. A typical user configuration imports a community or company configuration from DVCS, then override definitions to integrate user-specific projects, preferences, resources, and authorizations. Similarly, a community configuration may inherit, extend, and override others. A community configuration serves the roles of package manager, curator, and atomic system versioning aligned with DVCS hashes and tags. 

To simplify sharing, copying, and editing we organize software projects into packages. Each package isolates file-based dependencies to the same folder and subfolders. Other than local files, a package only depends on a configuration-provided environment of definitions, subject to localization. 

*Note:* Restrictions on file dependencies will be enforced using smart constructors for 'relative' abstract file paths. In addition to package restrictions, we should block "../" paths from escaping a DVCS repository, and we can let developers enforce DVCS resources are transitively immutable, using version hashes instead of tags. Aside from these restrictions, abstraction of files can also support binding to a database, modeling of logical overlays, or treating binary data as a read-only file.

### Compilation

Front-end syntax is user-defined, aligned with file extensions. A front-end compiler will output definitions in a common Lisp-like intermediate representation, [abstract assembly](AbstractAssembly.md). These definitions are translated to support composition, extension via override, access control, and conflict resolution - see TL type from the [namespace model](GlasNamespace.md). A [set of primitives](GlasProg.md) is implicitly defined by the runtime system, including control structures such as conditionals or sequencing.

To support lazy loading and multi-stage programs, compilation is iterative and order is flexible. I propose to use non-deterministic choice: every terminating sequence of choices represents an atomic step and may define different components. Some steps may attempt to 'eval' an expression within the generated namespace, a simple basis for syntax-independent macros, and will implicitly wait for other steps to provide necessary definitions. To guide evaluation order, we can let users apply a translation to all future outputs from a step.

Compilation steps should be commutative, monotonic, and idempotent. A conflict is possible where a name is assigned multiple distinct definitions. Although it isn't difficult to detect conflicts during evaluation, in context of laziness conflict may be latent in the system and go undiscovered. This can be mitigated by developing a deterministic scheduler for compilation steps and supporting a diagnostic evaluation mode that actively searches for conflicts instead of lazily halting after a definition is discovered.

### Multi-Stage Programming

Compilers have an opportunity to evaluate expressions in context of the generated namespace. This can support macros independent of the front-end syntax, including namespace macros that may generate new definitions. If evaluation requires a missing definition, it can implicitly wait on that definition to be provided by another step. 

        from { Expr } import x, foo as y, z

In case of namespace macros, we'll generally want to support the use case where we translate the generated definitions, defer computation based on a translation, yet evaluate in an environment prior to the translation. This is feasible with an API that reifies the environment, similar to: `var Env = env(); translate(TL); eval(Env, Expr)`. We may extend this API with methods to logically translate or compose the Env type.

Staging is a relatively simple use case. Between 'fork' and 'eval' there is sufficient flexibility to model concurrent processes that interactively generate definitions, treating the namespace as a set of single-assignment variables for futures and promises. We can also express procedural generation of infinite namespaces. To support garbage collection of intermediate definitions, we can block both evaluation and new definitions from directly referencing 'private' names containing "~". 

### Applications

A [glas application](GlasApps.md) implements transactional methods recognized by a runtime, such as a 'step' method that is run repeatedly as a background loop, and an 'http' method to receive HTTP requests. Algebraic effects provide controlled access to the system, and application state is mapped to a key-value database. Performance is mitigated by partial evaluation, incremental computing, and parallel evaluation of non-deterministic choice.

A relevant consideration is how to represent the application object. Some viable options:

* Directly define application methods in configuration, i.e. `appname.step` and `appname.http`. Runtime searches for certain methods. This hinders extension. Apps may be built by hand or installed using macros..
* Integrate methods into a single procedure, use partial eval, i.e. `appname("http", Request)`. This simplifies composition and delegation, but is inconvenient to implement, optimize, or reflect on the API.  
* Application as a namespace macro, i.e. `appname()` defines 'step' and 'http'. This enables ad-hoc extension via override, but it greatly complicates sharing. Without this, namespace macros are still useful.

A runtime may recognize several approaches, perhaps distinguished by annotation (e.g. `appname.#run-mode`). I currently favor direct definition as the default.

### User-Defined Syntax

A compiler can recursively compile other files into the namespace. This will automatically select a compiler from current namespace environment based on the file extension, e.g. "lang-FileExt". The compiler should be a procedure that receives an abstract file path as a parameter and generates definitions, iterative via non-deterministic choice.

The glas system specifies an [initial syntax](GlasLang.md) associated with the ".g" file extension. If the associated "lang.g" compiler is undefined, we use the built-in compiler for ".g" files. However, if defined in terms of ".g" files, we'll attempt to bootstrap. Bootstrap involves building "lang.g" with the built-in, then again with itself, then verifying a fixpoint is reached with one more build.

The initial configuration file *must* use an initial syntax recognized by the glas executable. It may also define the compiler for this initial syntax, supporting bootstrap. A glas executable may support other initial syntax. In this case, we may need to bootstrap multiple built-in languages together if they are mutually defined in terms of each other.

### Notebook Interface

Most files, including configuration files, should automatically compile to applications providing a notebook interface - a live coding environment and projectional editor over the initial file. See [glas notebooks](GlasNotebooks.md).

### Annotations

Programs in glas systems will generally embed annotations to support logging, profiling, testing, debugging, type-checking, optimizations, and other non-semantic features. As a general rule, annotations must not influence observable behavior except through reflection APIs. Under these constraints, it is safe to ignore an unrecognized annotation. However, we'll generally report a warning to resist silent degradation of system performance or consistency. Reflection APIs may peek at performance, logs, code and JIT, data representations, etc. but should be easily controlled.

In context of abstract assembly, annotations might be generally represented using `(%an AnnoAST ProgAST)`, scoping over a subprogram. The AnnoAST might be something like `(%log ChanExpr MessageExpr)`. Ignoring the annotation, this should be equivalent to ProgAST. If ProgAST is omitted, we can assume a no-op.

Some annotations may be embedded in the namespace to support reflection and browsing of the namespace. This should use a simple naming convention, perhaps `foo.#doc` and `foo.#type`. 

### Automated Testing

Automated testing can be aligned with iterative compilation, such that some steps represent tests. Testing can be guided by annotations and heuristics, and can leverage non-deterministic choice to model fuzz testing and property testing. With some careful configuration, it is feasible to cache and share tests between users, and produce a system health report.

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

Annotations can 'wrap' or 'unwrap' abstraction nodes based on a matching Key. Attempting to observe data without unwrapping it, or attempting to unwrap with the wrong Key, would be a divergence error. Some reflection APIs could peek under the hood. An optimizer can potentially remove or disable wrap/unwrap annotations when it can be locally proven safe, giving us some benefits of static analysis.

We can further constrain manipulation of abstract data: 'linear' data must not be copied or dropped, and 'scoped' data restricts storage and communication. In this case, efficient dynamic enforcement may involve caching a few tag bits per Node (perhaps via packed pointers). We don't need much: plain old data vs. linear runtime-scoped data could use just one bit, and (together with algebraic effects) should adequately cover most use cases.

*Note:* Intriguingly, specific annotations for data abstraction could be enforced cryptographically, using symmetric or asymmetric keys. This would allow for data abstraction to be enforced even in context of remote procedure calls and reflection APIs. An optimizer can lazily defer encryption within a runtime or across 'trusted' RPC boundaries. However, I doubt this is worth the performance and system flexibility costs in most cases.

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

