# Glas Language Design

## Module System

Large Glas programs are represented by multiple files and folders. Each file or folder deterministically computes a value.

To compute the value for a file `foo.ext`, search for a language module or package `language-ext`, which should include a program to process the file's binary. To compute a value for a folder `foo/`, use the value of the contained `public` module if one exists, otherwise a simple dictionary reflecting the folder's structure.

Language modules have access to limited compile-time effects, including to load values from external modules or packages. File extensions are elided. For example, loading `module:foo` could refer to a local file `foo.g` or `foo.xyz`, or a local subfolder `foo/`. Loading `package:foo` instead searches for folder `foo/` based on a `GLAS_PATH` environment variable.

Glas specifies a *Namespace Model* for definition-level programming. To support the conventional programming style where modules 'import' and 'export' definitions and manage scopes of symbols, most Glas modules should compute values that represent namespaces.

Ambiguous references, directed dependency cycles, invalid language modules, and processing errors may cause the build to fail.

*Note:* Files and folders whose names start with `.` are hidden from the module system. A `.glas/` folder might be used for extra input to the Glas command-line utility, such as quotas.

## Data Model

Glas data is immutable. Basic data is composed of dictionaries, lists, and natural numbers, such as `(arbid:42, data:[1,2,3], xid:true)`.

Dictionaries are a set of `symbol:Value` pairs with unique symbols. Symbols are short binary strings. The empty dictionary `()` frequently serves as a unit value. Glas programs may iterate over symbols and test for presence of a symbol, thus flags and optional fields are supported.

Variant data is encoded by singleton dictionaries. For example, a value of `type color = rgb:(...) | hsl:(...)` could be represented by a dictionary exclusively containing `rgb` or `hsl`. Symbol `foo` is often shorthand for `foo:()`.

Glas uses lists for all sequential structure: arrays, binaries, deques, stacks, tables, tuples, queues. To efficiently support a gamut of applications with immutable data, Glas systems will represent large lists as [finger trees](https://en.wikipedia.org/wiki/Finger_tree). Glas further uses [rope-style chunking](https://en.wikipedia.org/wiki/Rope_%28data_structure%29) for large binaries to minimize overhead.

Glas natural numbers do not have a hard upper limit, and do support bignum arithmetic. Glas does not have built-in support for negative integers or rationals or floating point or other numeric types, but they could be modeled. Note that Glas is not suitable for high-performance numeric computing without *Acceleration*.

### Content-Addressed Storage

Glas is intended to work at very large scales, with data that may be larger than a computer's working memory. 

Glas will support big data structures using content-addressed storage: a subtree may be serialized for external storage on a large, high-latency medium such as disk or network. This binary representation should be referenced by secure hash. 

Use of secure hashes simplifies incremental and distributed computing:

* persistent data structures, structure sharing
* incremental upload and download by hash cache
* efficient memoization, matches on tree hashes
* provider-independent distribution, validation

A Glas runtime may heuristically store values to mitigate memory pressure, similar to virtual memory paging. However, programmers have a more holistic view of which values should be stored or speculatively loaded. Thus, Glas programs will support operators to guide use of storage. 

[Glas Object](GlasObject.md) is designed to serve as a standard representation for large Glas values with content-addressed storage.

*Note:* For [security reasons](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html), content-addressed binaries will include a cryptographic salt (among other metadata). To support incremental computing, this salt must be computed based on a convergence secret. However, it prevents global deduplication.

## Compilation Model

A subset of Glas modules compute externally useful binaries, perhaps representing music, images, documents, tarballs, or an executable. The Glas command-line tool provides methods to extract binary values from a module from a list or a program generating a stream of bytes.

To produce an executable binary, the static analysis, optimization, and code generation will be modeled as Glas programs, ultimately driven by a language module. To produce binaries specific to a system, a system-info package can describe the default compilation target.

As a convention, appname-model and appname-exe packages should be separated to simplify extension or composition of applications, model testing, experimentation with compiler parameters, etc..

*Note:* The Glas command-line tool may privately compile and cache programs for performance, e.g. compile language modules into plugins specific to the command-line tool. This should be hidden from normal users of the tool.

### Staged Programming

Glas supports metaprogramming at two layers.

First, the module system supports ad-hoc metaprogramming. Language modules represent functions. By integrating an interpreter function, language modules can support macros or arbitrary staging.

Second, a compiler will partially evaluate programs. For robust performance, programmers should annotate their intention that certain values are statically computed. The compiler should recognize the annotation and report an error if partial evaluation fails.

These mechanisms cover each other's weaknesses.

### Acceleration

Acceleration is a pattern to support high performance computing. 

For example, we can develop a subprogram that simulates a programmable processor. This abstract processor may have a static set of registers, binary memory, support for fixed-width integers and bit banging and floating point operations. We annotate this subprogram for acceleration. 

A compiler should recognize the annotated subprogram and substitute an actual processor, performing translation as needed. Favoring Harvard architecture or other model for behavior-data separation can simplify the translation. If acceleration fails for any reason (unrecognized, deprecated, no support on target, resource constraints, etc.), the compiler should alert programmers to resist invisible performance degradation.

Acceleration of abstract CPUs would open a variety of problem domains where performance is a deciding factor: compression, cryptography, signal processing, etc.. Abstract GPGPUs or FPGAs are also valuable targets.

*Note:* Accelerators are major investments involving design and development, maintenance costs, portability challenges, and security hazards. Fixed functions have poor return on investment compared to abstract programmable hardware.

### Memoization

In Glas, [incremental computing](https://en.wikipedia.org/wiki/Incremental_computing) will be supported primarily by [memoization](https://en.wikipedia.org/wiki/Memoization). Content-addressed storage also contributes, enabling memoization over large value. 

Glas subprograms can be annotated for memoization. Annotations can include ad-hoc heuristic parameters such as how large a table to use, sharing, volatile vs persistent storage, expiration strategies, precomputation for specified inputs, etc.. 

Persistent memoization could use a secure hash of the memoized subprogram and its transitive dependencies as a content-addressed table. This can support incremental compilation or indexing involving similar data across multiple executions. Volatile memoization is more efficient and useful for fine-grained [dynamic programming](https://en.wikipedia.org/wiki/Dynamic_programming). 

Memoization and incremental computing are deep subjects with many potential optimization and implementation strategies. Fortunately, even naive implementation can be effective with precise application by programmers.

## Computation Model

The Glas computation model is based on [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) (KPNs) to support scalable computation and expressive composition under constraint of determinism.

KPNs consist of concurrent processes that communicate by reading and writing channels. Channels may be externally wired between processes, supporting open composition of cyclic dependencies. Use of channels are restricted to ensure a deterministic outcome. 

Glas channels use bounded-buffers, enabling a fast producer to wait on slow consumers. Conceptually, buffered channels are a coupled `(ready, data)` pair of unbuffered channels with dataflow in opposite directions. Writer reads ready token then writes data. Reader writes ready token then reads data. Buffer size is the maximum number of ready tokens. Zero-buffer supports rendezvous pattern.

Glas channels may also be 'closed' or terminated from either end. This enables Glas to easily represent sequential composition of channels, and to effectively model short-circuiting behavior.

Glas implicitly models future values as singleton channels.

### Program Model

The Glas program model is based on [arrowized](https://en.wikipedia.org/wiki/Arrow_%28computer_science%29) [Kahn process networks](https://en.wikipedia.org/wiki/Kahn_process_networks) via [concatenative](https://en.wikipedia.org/wiki/Concatenative_programming_language) [combinatory logic](https://en.wikipedia.org/wiki/Combinatory_logic).

Concretely, a Glas program is represented by a list of operators. Operators are represented by variant data. The list models sequential composition. Each operator represents an abstract function, often a combinator whose argument is a static subprogram.

Glas programs operate on data structures extended with transparent futures. Bounded-buffer channels are a disciplined application of lists with future tails. Unbounded channels are normal lists. Functions over partial data are become long-lived processes that compute output incrementally, monotonically, opportunistically based on readiness of readers and availability of input.

Cyclic process networks are modeled by a fixpoint loop combinator. This routes a future output from a subprogram to its input. If not used carefully, this easily results in deadlock. Ready tokens for buffered channels are implicitly fixpointed.

Most Glas operators assume input environment is structured a Forth-like data stack, represented by a list. This simplifies operators, eliminating explicit paths or addressing. A compiler can eliminate most stack data shuffling.

Program operators are detailed in a later section.

### Namespace Model

Definition-level programming is concise, accessible, extensible, scalable, and human-friendly relative to lists of primitive operators. Glas supports definition-level programming with two operators:

* **ns:Namespace** - adjust namespace within a subprogram
* **op:Name** - apply symbol defined in current namespace

A Glas namespace is concretely represented as a list of namespace operators to extend or abstract a prior namespace. Definitions in Glas are lexically scoped within the namespace. Higher-order namespaces are supported, with default parameters.

Operator names may be hierarchical, such as `op:foo:bar`. Operators are not parameterized at the call site, but may be indirectly parameterized via higher-order namespaces. 

Names are kept abstract. The client cannot inspect the definition from within the program model. In most cases, the Glas compiler will inject a few initial names representing effectful operators. Effectful operations can be attenuated or abstracted via the namespace model. (See *Effects Model*.)

Higher-order namespaces can support generic programming. However, they are unsuitable for staged metaprogramming because the namespace model does not support conditional logic or loops.

Namespace operators are detailed in a later section.

### Effects Model

Effects are injected via the namespace model. A compiler or runtime can provide an initial namespace of effectful operators.

This design is similar to procedural effects. Performance can be good, with much effect code being inlined and compiled to machine code. 

Two noteworthy distinctions:

Effects in Glas are aggressively concurrent, opportunistic based on available data. Blocking calls will require too many threads. Glas effects systems should build on asynchronous IO, such as Linux epoll. Static safety for conventional effects may benefit from abstract data types and substructural types, e.g. linear file handles.

Higher-order namespaces can abstract, restrict, or attenuate effects available to subprograms. For example, we can sandbox a subprogram's access to the filesystem, or map abstract store/load effects to a directory within a filesystem. This is convenient for security analysis, software extension, automated testing, and debugging. 

Glas systems can also use effects to extend the program model in ad-hoc ways, e.g. with reflection, shared state, or a select operation to observe race conditions. 

*Note:* Glas can support alternative runtime behavior models. But Glas program model plus effects is adequate for most use cases.

### Notable Exclusions

To keep it simple, the Glas program model *does not* have primitive operators for first-class functions (`behavior -> data`) or dynamic evaluation (`data -> behavior`). There is a clear data-behavior separation at runtime.

Absence of first-class functions is mitigated by other features:

* Channels serve many roles of continuations: await input, emit output, cyclic interactions.
* Namespaces support higher-order and generic programming under constraint of static linking.

Glas can abstract loops over lists or implement stream generators. First-class existential types or OOP objects aren't feasible.

Evaluation can be explicitly defined for use within Glas. It can feasibly be accelerated, perhaps indirectly by compilation of programs to accelerated abstract machines.

## Language Modules

Modulo bootstrapping, to process a file with extension `.xyz`, Glas system tools will search for a corresponding module or package `language-xyz`, favoring a local module. 

Developing language modules can support syntax extension, domain-specific languages, stylistic preference (Lisp-like or Forth-like Glas), projectional editing, and integration with external tools (file output as source).

A language module's value must represent a Glas namespace that defines a 'read' operation. The language module is also a central locus for utilities such as auto-formatting, syntax highlighting, REPL support, projectional editing support, language server protocol, documentation, tutorials, etc..

The read operation is abstract over a few effectful operators to load modules and log messages:

* **op:load** - `Name -- Value`. Access external dependency such as `package:foo`. Returns unit `()` on any error.
* **op:log** - `Message -- `. Emit a message. Message may have any data type, usually variant such as `error:(...)`. 

The runtime input is a binary, a list of small numbers representing file content. Final output becomes the module's value, albeit with output channels expanded to lists. The read operation is considered a failure if output deadlocks, an `error:(...)` message is logged, or a dependency cycle is detected.

Builds can be expensive. Logging should be leveraged to report progress, e.g. by emitting `progress:(task:regalloc, step:37, max:100)` or similar, which could be displayed as progress bars.

The read operation will compute a deterministic value based on language definition, file content, and referenced modules. The set of logged messages is also deterministic but may be emitted in non-deterministic order.

*Note:* Load actions are logged to a dedicated space to support cyclic dependency checks. Load errors are also reported.

## Glas System Patterns

This section describes some high-level visions for how Glas systems are managed or used. These ideas indirectly influence Glas design.

### Automated Testing

Within a Glas folder, any module with name `test-*` should be processed even if its value is not loaded by the `public` module. Language modules support ad-hoc expression of tests via files, logging `error:(...)` to report issues. Static analysis can be performed as normal tests.

Testing of computed executable binaries is theoretically feasible via accelerated simulation and fuzz-testing. However, accelerators won't be immediately accessible, so more conventional methods are required short-term.

### Graphical Programming

Language modules enable Glas to bridge textual and graphical programming. Graphical programming can be supported by developing a specialized syntax, nodes annotated with graphical markup for layout and presentation.

Presentation might involve calendar widgets for date values, color pickers for color values, sliders for numbers, etc.. In the more general case, an entire file might be rendered as a big widget. Projections feasibly integrate multiple files, via embedding, portals, or links. At an extreme, it is theoretically feasible to project programs as video games.

Language modules could make the presentation more explicit by defining utility functions suitable for presentation of languages in various media, such as a web-app.

### Live Coding Applications

In a living Glas system, programs should represent active intentions of users. Changes to the program should immediately be deployed, providing real-world feedback.

A viable application model is based on repeatedly applying deterministic, hierarchical `State -> State` transactions. Access to external resources might be modeled as stateful requests added to a queue or bulletin board. Potential API:

* **op:try** - hierarchical transaction, higher order:
 * **task** - task to transact, with backtracking
 * **then** - runs with output from task on success
 * **else** - runs with input to task on failure
* **op:fail** - abort task, implicit for errors

A compiler can use static analysis or profiling to determine which elements of input state are observed or updated by a transaction. Transactions can run in parallel when there is no conflict. If a transaction fails, its next repetition can wait for a relevant state change. This implicitly gives us concurrency control (locks, etc.).

A single transaction is deterministic, but multiple concurrent cyclic transactions may be non-deterministic.

A language syntax built around this model can represent hierarchical transactions with logical lines and indentation. A syntax error then only affects the subprogram's transaction instead of the full program.

This model is significantly more accessible, extensible, composable, comprehensible, and controllable than OS processes. It should make a good default for modeling most long-running application behaviors, including console or network. Application GUIs could be modeled as graphical projections over the state.

### Program Search

As the Glas system matures, it might be useful to shove more decision making to expert systems encoded into the module system.

At a lower level, automate some data-plumbing. At a high level, describe programs with hard requirements, heuristic preferences, and a search space for potential solutions. A staged program can search for programs that achieve these goal, leveraging the limited intelligence we can integrate via rules or machine learning.

To support search, Glas programmers can define packages that catalog and curate other packages. Catalogs should include names and summaries of other package, and perhaps a reverse-lookup index. Summaries might include tags and types.

## ACTIVE OPERATOR DESIGN

I haven't found a solid set of guiding principals for choice of operators. But some hand-wavy goals: 

* operators are widely useful and easy to understand
* performance is robust, predictable, controllable
* scalable computations over distributed processors

Implicit representations should be avoided. 

### Deep Equality? No.

Glas could support a structural equality comparison for arbitrary values. However, this would have unpredictable performance when comparing channels.

### Pattern Matching? No.

I'd like to support pattern matching, including view patterns and pattern guards. However, it does not seem feasible to support pattern matching at the level of primitive operators. This will need to become a feature of the language modules.

### Namespace Conditionals? No.

A least-expressive option for 'defaults' is to apply a backup definition in case a particular symbol is undefined. However, this doesn't cover a lot of cases where we might wish to define our defaults based on which other elements are defined.

An interesting option is to support conditionals based on an arbitrary set of defined symbols. E.g. if `(x,y)` is defined, then apply a namespace operation, else another operation. This would limit namespaces to finite boolean logic, capable of expressing flags and defaults.

However, if we're capable of boolean logic over fields in a dictionary, we could indirectly represent natural numbers up to some arbitrary boundary via bitfields. So, perhaps we should support basic arithmetic and numeric operations, too. A similar argument applies to representation of lists using recursive record structures.

If we begin to model rich data structures in our namespace logic, we should be able to abstract this logic. At this point, we'd need another namespace to access the namespace. This is feasible, but is NOT a path towards simplicity or user comprehension.

So, my proposal is to limit namespace computations to unconditional defaults. In this case, we might support defaults for a set of symbols in terms of each other. Static analysis could then identify minimal 'sets' of definitions to avoid recursion.

### Recursive Definitions? No.

It is feasible to locally rewrite let-rec groups to loops. However, it's too difficult to make recursion work nicely with the KPN model with incremental, partial outputs.

### Staged Programming? Indirect.

I rejected staged computing within the namespace model above. It is possible to add an operation `stage:([] -> Prog))` to the program model. A benefit of this is that we could integrate data from the namespace to compute a program. However, this would make it very difficult to reason about *effectful* operators.

Fortunately, even without explicit staging in the program, we can support staging via the module system and partial-evaluation at the compiler. 

Partial evaluation could be augmented with static types to insist that certain values are compile-time computable, perhaps session types. 

### Arithmetic? Minimal.

There is no end to the number of arithmetic functions we could model. However, I'd prefer to keep this relatively minimal within the bounds of convenience.

### Annotations? Namespace op.

I've mentioned annotations more than once. In Glas, we will annotate programs via namespace operator. Consequently, runtime parameters are never annotated.

### Logical List Reversal? Maybe.

Reversing a list takes O(N) time. But, logically, it is feasible to reverse a list in O(1) time then operate on it with an extra check at the reference. 

A concern is that it becomes difficult to reason about how a particular implementation will represent the logical reversals, or how aggressively this is performed. Performance becomes unpredictable, which is something I'd prefer to avoid.

However, performance predictability is not the top priority for Glas, and mostly-predictable is acceptable. Comprehensible performance is the issue.

### List Collections Processing?

I'm inclined to support a variety of list-processing operators such as zip/unzip, transpose, map, filter, scan, fold, flatmap, concat, sum, etc. I would like to have good support for structure-of-arrays vs array-of-structures.

It's less clear to me whether these operations should also apply to channels, or whether a different set of similar operations should apply.

### Dictionaries and Symbols?

Glas programs will have full ability to inspect and construct dictionaries, e.g. iteration over symbols, composition of dictionaries. A compiler should use static analysis and annotations to determine when certain dictionaries should be represented as C-like structs. 

## Program Operators

### Stack Manipulation 

Glas uses a data stack for most primitive operators. This supports implicit source and destination. 

        data:X                      -- X
        copy                      X -- X X
        drop                      X --
        swap                    Y X -- X Y
        dip:(S. -- S'.)        S. X -- S'. X
        sip:(X -> Y)              X -- Y
        box                      X  -> [X]
        unbox                   [X] ->  X

* **data** - push constant onto stack
* **copy** - copy top stack value
* **drop** - remove top stack value
* **swap** - switch the top two stack values
* **sip** - apply subprogram to the top stack value
* **dip** - apply subprogram below top stack value
* **box** - capture environment onto singleton stack
* **unbox** - release environment from singleton stack

Data plumbing on a stack involves an ad-hoc combination of dip, swap, copy, and drop operations. Language modules can model syntax with  conventional variables and lambdas then compile to stack machine. Of course, some users might favor a Forth-like programming style.

The sip and dip operators support structural scoping of subprograms. The box and unbox operators enable programs to work with non-stack environments and capture of the current stack as a value on a new stack. 

A stack is concretely represented by heterogeneous list. Head of list is top of stack. However, use of data stacks should be constrained more than dynamic lists. Safety analysis should reject Glas programs where stack type or size is difficult to statically predict.

### Arithmetic

Glas supports natural numbers and bignum arithmetic.

        add                     N N -- N
        mul                     N N -- N
        sub                     N N -- N
        div                     N N -- N
        mod                     N N -- N

Subtraction will return 0 if the second parameter is equal to or greater than the first. Division and modulo fail if the second argument, divisor, is zero. We can consider adding new functions for convenience.

Glas is not designed for high performance numeric computing. The expectation is that Glas systems should *accelerate* an abstract CPU or GPGPU with access to fixed-width integers, floating point, vectors and SSE, etc. The Glas language then supports communication between abstract processors, and their reference model for fuzz-testing.

### Dictionary Operations

### List Processing

### Channel Processing

### Fixpoint Loop

### Conditional Behavior

### Annotations

### Component System

### Synchronization

Ways we can synch:

* static - insist a value is statically computable
* latent - defer a value until there is demand for it
* 


* wait for a value before starting subprogram
* wait for channel to finish before producing value
* wait for multiple values before producing value
* 


        synch:[X->Y]   - wait for an unrelated input before continuing

### Content-Addressed Storage

### Memoization


*Note:* A relevant concern for Glas is to support memoization over large lists. This may require specialized support to align memoization the finger-tree structure, primarily a memoized flatmap or mapsum. 

### Distribution

## Namespace Operators



## Evaluation Strategies

Graph Rewriting vs Abstract Evaluation

A predictable graph-rewrite semantics might simplify presentation of evaluation. The simplest model of rewriting is erasure: when a process is finished making decisions, it might 'become' a simple rename operation.

Glas does not have a strong use-case for a graph rewrite semantics. However, it is useful and not difficult to ensure a rewrite semantics can be expressed: it is sufficient that primitive processes can be  expressed by local rewriting. 

A graph rewrite semantics will essentially allow Glas programs to represent 'interaction networks'. 

## Provenance Tracking

Glas modules hides the provenance of a value; the client of a module  only observes its computed value, not how it was derived. However, it is feasible to augment a Glas evaluator to trace the primary influences on values and produce corresponding files.

This could be augmented by annotations within Glas programs to explicitly support provenance tracking, e.g. to support user-defined equivalence regions, human meaningful names.

