# Glas Language Design

Glas is now a backronym for 'General LAnguage System'. It was originally named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and the human mastery over glass as a material. 

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between Glas modules must form a directed acyclic graph. Every module will deterministically compute a value depending on file extension, file content, and definition of language modules.

To compute the value for a file `foo.ext`, the Glas system will compile the file binary using a program defined in module `language-ext`. To bootstrap, the [g0](GlasZero.md) language (a Forth variant) is predefined. File extensions compose, e.g. for `foo.xyz.json` we apply `language-json` then `language-xyz`. The value for a folder `foo/` is the value from its contained public file, such as `foo/public.g0`. Modules cannot reference across folder boundaries. Files and folders whose names start with `.` are hidden from the Glas module system.

Global filesystem modules should be subfolders found using the GLAS_PATH environment variable, whose value should be a list of folders separated by semicolons. Additionally, files can reference local modules within the same folder. By default, this uses a simple fallback: search locally, then globally if a local resource is not specified. Later, we might extend module search to use network resources, such as a package distribution.

## Data Model

Glas data is immutable binary trees. A naive representation is:

        type T0 = ((1+T0) * (1+T0))

However, for extensibility and self-documentation, Glas encourages use of *labeled* data structures. Labels are encoded as paths through a sparse tree. For example, the label 'data' will normally be represented by path `01100100 01100001 01110100 0110001 00000000`, using null-terminated UTF-8 text. The labeled data is the data we reach by following this path. A 'variant' has a single label followed by data, while a 'record' forms a [radix tree](https://en.wikipedia.org/wiki/Radix_tree) with different labels sharing path prefixes.

To efficiently represent labeled data, Glas systems favor a tree representation that compact non-branching paths, such as:

        type BitString = (compact) Bool list
        type T1 = (BitString * (1 + (T1*T1)))

Glas systems can also work with fixed-width labels. For example, `(A * B)` pairs and `(A + B)` sum types are essentially records and variants with one-bit fixed-width labels, while a 32-bit address space can be modeled by a record with 32-bit fixed-width labels.

Unit `()` is the tree with no children. By Glas conventions, a bitstring terminating in unit will encode bytes, words, numbers, and symbols. For example, `00010111` encodes byte 23, and `10111` encodes natural number 23.

Glas uses lists to encode most sequential structures. A binary is a list of bytes, and texts are usually UTF-8 binaries. Logically, a list has type `type List a = (a * List a) | ()`, i.e. constructed of `(Head * Tail)` pairs and terminating in unit. However, to support efficient split, concat, and double-ended queue operations, Glas systems often *accelerate* lists under-the-hood using a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation.

To work with very large trees, Glas systems may offload subtrees into content-addressed storage. I call this pattern *stowage*. Of course, Glas applications may also use effects to interact with external storage. Stowage has a benefit of working nicely with pure computations, serves as a virtual memory and compression layer, and has several benefits for incremental computation and communication. Stowage can be guided by program annotations.

## Command Line

The Glas command line interface supports practical use of the Glas module system. The command line is extensible with user-defined verbs via naming modules with a `glas-cli-*` prefix. 

        glas foo Parameters 
            # rewrites to
        glas --run glas-cli-foo.run -- Parameters

The glas executable bootstraps the language-g0 module, builds all transitive dependencies to compile the glas-cli-foo module, extracts the 'run' label, verifies static arity, then evaluates the extracted program with access to ad-hoc effects. Unnecessary rework should be mitigated by maintaining a cache. 

Effects available to verbs will include conventional access to filesystem, network, environment variables, and standard input and output. The standard error output stream is reserved for printing `log:Message` effects, including warnings or errors during bootstrap or build of the verb. See [Glas CLI](GlasCLI.md) for details.

Some applications can be implemented using the command line's effects handler - including REPLs, package managers, language servers, or web-based IDEs. However, a primary intention is to develop verbs to deterministically and reproducibly extract useful binaries from the Glas module system, including compilation of independent executables. The command line interface should (eventually) be bootstrapped by extracting an executable binary.

*Note:* Besides `--run`, the Glas command line interface will support `--version`, `--help`, and perhaps a few useful ad-hoc functions. Additionally, environment variable `GLAS_PATH` is used during lookup of modules.

## Glas Programs

Glas programs are a subset of values with a standard interpretation. The program model is designed for convenient composition, extension, staging, and analysis. Glas programs are required when defining language modules or command-line verbs, and are effective for representing simple user applications.

Glas programs manipulate a data stack. Interaction with the outside world is via request-response effects. Effects handlers can implement a sandbox or adapter layer for effects. Glas programs are minimally characterized by a static stack arity (e.g. 2--3 meaning 2 inputs, 3 outputs) and an effects API that describes expected requests and resulting behavior.

Glas conditionals are backtracking. If an operation fails within a conditional context, we undo the effects then evaluate the alternative branch. If a branch obviously always fails, it might be eliminated by a compiler. This backtracking behavior is convenient for composable pattern matching and transactional behavior. 

Glas programs have a weakness: there is no built-in namespace. Code reuse is by replication, not by reference. This simplifies composition but complicates performance. Performance might be mitigated by structure sharing and memoization (work sharing) at the value layer, or by a compression pass that packages common subprograms into an effects handler.

### Stack Operators

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
 * move top item from data stack to top of dip stack
 * run P
 * move top item from dip stack to top of data stack
* **swap** - switch the top two items on stack
* **data:V** - copy V to top of stack

My type notation is a little ad-hoc, but might help clarify.

        copy        ∀S,V . (S * V) → ((S * V) * V)
        drop        ∀S,V . (S * V) → S
        dip:P       
            P ⊲ (S → S')
            -------------------------- 
            dip:P ⊲ ∀V. (S * V) → (S' * V)
        swap        ∀S,A,B . ((S * A) * B) → ((S * B) * A)
        data:V      
            V ⊲ A
            ------------------------- 
            data:V ⊲ ∀S . S → (S * A)

The stack in Glas is really an intermediate data plumbing model. User syntax could hide stack shuffling behind local variables. The Glas compiler can replace the stack with static memory and register allocations, leveraging static arity of valid Glas programs. The main reason for the stack is that it simplifies operators, which don't need to specify input sources or output targets.

### Control Operators

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
 * Empty sequence serves as nop.
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
 * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat. Otherwise, exit loop. Variants:
 * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat. Otherwise, exit loop.
 * 'do' field is optional, defaults to nop.  
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail

        seq:[]              ∀S . S → S
        seq:(Op :: Ops)     (as SEQ)
            Op ⊲ S → S'
            seq:Ops ⊲ S' → S''
            -------------------
            SEQ ⊲ S → S''
        cond:(try:P, then:Q, else:R)    (as COND)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S''
            R ⊲ S → S''
            -------------------
            COND ⊲ S → S''
        loop:(while:P, do:Q)    (as LOOP-WHILE)
            P ⊲ S → S' | FAIL
            Q ⊲ S' → S
            -------------------
            LOOP-WHILE ⊲ S → S
        loop:(until:P, do:Q)    (as LOOP-UNTIL)
            P ⊲ S → S' | FAIL
            Q ⊲ S → S
            ------------------
            LOOP-UNTIL ⊲ S → S'
        eq : ∀S,A,B . ((S * A) * B) → S | FAIL
        fail : ∀S . S → FAIL

User syntax can extend the effective set of control operators, e.g. compiling a mutually recursive function group into a central loop. 

*Note:* Glas does not provide an 'eval' operator. However, it is feasible to accelerate evaluation, and to memoize JIT compilation of programs, such that we effectively have first-class functions in Glas. 

### Effects Handler

Use of 'env' enables a program to conveniently implement a sandbox or effects adapter for a subprogram. User-defined effects handlers must have static arity `Request State -- Response State`. Initial handler state is captured by the 'env' call.

* **env:(do:P, with:H)** - arranges for operator 'eff' within subprogram P to call handler H. Top item from data stack is captured for use as handler state, and is returned upon exiting env.
* **eff** - invoke current effects handler, taking top stack item as Request and returning a Response.

    env:(do:P, with:H)          (as ENV)
        H ⊲ ∀S. ((S * Request) * State) → ((S * Response) * State)
        eff ⊲ ∀S. (S * Request) → (S * Response) ⊢ P ⊲ E → E'
        -----------------------------
        ENV ⊲ (E * State) → (E' * State)
    eff         type determined by env

Routing all effects through a single handler simplifies reasoning about control of effects, but direct interpretation requires a lot of runtime routing. Partial evaluation of handlers can mitigate runtime routing overheads. 

A top-level effects handler is implicitly provided by the compiler or runtime. However, backtracking conditional behavior (i.e. try, while, and until clauses) should undo real-world effects. Thus, top-level effect APIs are normally safe or asynchronous, such that undo is easy.

### Record Operators

A set of operations useful for records and variants. These are essentially the only data operations in Glas programs; everything else is constructed from these, data plumbing, and control ops. 

Operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not in record.
* **put** (V (label?_|R) label -- (label:V|R)) - given a label, record, and value on the data stack, create new record with the given label associated with the given value. Will replace existing label in record.
* **del** ((label?_|R) label -- R) - remove label from record. Equivalent to adding label then removing it except for any prefix shared with other labels in the record.

Variants are essentially singleton records. The distinction between records and variants will mostly be handled during by static analysis instead of runtime ops.

### Annotations Operators

Annotations can support performance (acceleration, stowage, memoization, optimization), static analysis (types, preconditions, postconditions), debugging (tracing, profiling, assertions, breakpoints), decompilation, and other external tooling. However, annotations should not affect the meaning or outcome of a program, with an exception for reflection effects that might observe timings or traces.

* **prog:(do:P, ...)** - runs program P. All fields other than 'do' are annotations. 

The set of annotations is openly extensible and subject to de-facto standardization. If a Glas compiler or interpreter encounters any annotations it does not recognize, it can log a warning then ignore. Some annotations in use:

* *accel:Model* - accelerate the program. The model is often a symbol indicating that the program implements a specific accelerated function that the compiler should recognize. However, more general models are feasible.
* *arity:(i:Nat, o:Nat)* - effective arity, usually based on a program *before* it was optimized. This may be checked, ignored, or assumed to be correct depending on context.

The 'prog' header also serves as the primary variant for programs within a *Dictionary* value.

### List, Arithmetic, Bitwise Operators, Etc..

I've dropped most Glas program operators on data representations. Instead, the idea is to implement these functions within Glas then annotate for *Acceleration*.

## Bootstrap Syntax

Glas requires an initial syntax for bootstrap. To serve this role, I define [the g0 language](GlasZero.md). The g0 language is essentially a Forth variant with staged metaprogramming, effects handlers, and immutable tree-structured data. The glas command line interface will bootstrap the language-g0 module upon every command, or at least validate a cached bootstrap.

## Application Models

### Language Modules

Language modules have a module name of form `language-(ext)`, binding to files with extension `.(ext)`. The language module shall comile to a record value of form `(compile:Program, ...)`. Aside from 'compile', other properties may provide description, documentation, linters, decompiler, code completion support, [language server](https://en.wikipedia.org/wiki/Language_Server_Protocol) support, REPL support, etc..

The compile program must have arity 1--1 and implements a function from source (usually a file binary) to a compiled value on the stack. The compile program can also access other module values and generate some log outputs. Effects API:

* **load:ModuleID** - Modules are usually identified by UTF-8 strings such as `"foo"`. File extension is elided. By default, we search for the named module locally then on `GLAS_PATH`. 
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports, debugging, code change proposals, etc.. 

Load failures may occur due to missing modules, ambiguous names (e.g. if we have both `foo.g0` and subdirectory `foo/`), detection of dependency cyles, failure of the compiler function, etc. A compiler can continue in presence of most load failures. The cause of failure is not visible to the client module but should be visible to developers, e.g. via log. 

A language may expose these effects to the programmer in context of compile-time metaprogramming. For example, these effects are explicitly supported by language-g0 macro calls.

### Data Printer 

To support reproducible extraction of useful binaries from the module system, we might introduce a verb `glas print Value with Printer`, where Value and Printer are dotted path references into the module system. The printer should reference a program with arity 1--0 (receiving the value) and very limited access to effects. Effects API:

* **write:Binary** - Write binary data (a list of bytes) to stdout. Response is unit. Fails if argument is not a binary.
* **log:Message** - Arbitrary log message for debugging or progress. Will be pretty-printed to stderr. 

It is possible that a printer will fail partway through the job. In that case, we'll still print the stream to that point, then halt with a non-zero error code.

### Automated Testing

Static assertions when compiling modules are very useful for automated testing. However, a build-time test is under pressure to resolve swiftly thus usually cannot explore a large search space. There leaves open a niche for long-running or non-deterministic tests, such as fuzz-testing.

To support this, we might express tests as arity 0--Any Glas programs with access to 'fork' effect for non-deterministic choice input. 

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' single-edge bitstring.
* **log:Message** - Response is unit. Write an arbitrary message to support debugging of tests.

The primary output from a test is pass/fail of evaluation. Log messages are a secondary output for debugging. In context of testing, non-deterministic fork should not be fair or random. A good test system will apply heuristics and program analysis to more effectively search for failing tests. We can also use forks as checkpoints for backtracking and incremental computing.

We could develp a command line verb, `glas test ...`, to manage running of tests. The limited effects would pressure developers to simulate the effects environment for testing.

### User Applications

This is the subject of the [Glas Apps](GlasApps.md) document.

## Performance

### Stowage via Content-Addressed Storage

Glas systems will support large data using content-addressed storage. A subtree can be serialized to cheap, high-latency storage and referenced by secure hash. I call this pattern 'stowage'. Stowage serves a similar role as virtual memory, but there are several benefits related to semantic data alignment and content-addressed storage:

* implicit deduplication and structure sharing
* incremental upload, download, and durability
* provider-independent, validated distribution
* memoization over large trees can use hashes
* value-level alignment simplifies control

Glas programs can use annotations to guide use of stowage. It is also feasible to extend the module system with access to some content-addressed data. And a garbage collector could heuristically use stowage to recover volatile memory resources. Use of stowage is not directly observable within a Glas program modulo reflection effects.

*Security Note:* In context of open systems, sensitive content should be secured by annotating the binary with a cryptographic salt. To support deduplication, we could take inspiration from [Tahoe's convergence secret](https://tahoe-lafs.readthedocs.io/en/tahoe-lafs-1.12.1/convergence-secret.html).

### Staged Metaprogramming and Partial Evaluation

Glas has implicit, pervasive staging via its module system. Additionally, language modules can provide staging within a module, assuming the language includes a program interpreter. The g0 language, used in bootstrap, has support for staged metaprogramming via macros and an export function.

Staging supports manual partial evaluation. More implicitly, the Glas program model is also designed to support partial evaluation: Linking is static. Stack allocation is static. It is feasible to distinguish static record fields. Effects handlers can be inlined and partially applied, including the top-level compiler-provided handler.

Further, annotations can indicate where partial evaluation is assumed so we can properly warn programmers when it fails.

### Acceleration

Acceleration is an optimization pattern. The idea to annotate specific subprograms for accelerated evaluation, then a compiler or interpreter should recognize the annotation then silently substitute a specialized implementation. Essentially, the provided code is a reference implementation for a built-in.

Glas will accelerate basic list functions to use a finger-tree representation for lists under-the-hood, enabling efficient list split, append, length, array indexing, and deque operations. Glas programs don't have a primitive eval operator, but it is feasible to accelerate an eval function; together with JIT compilation and caching, we can effectively support first-class functions. To leverage a GPGPU, we can develop a specialized program model for the GPGPU then accelerate its evaluation using an actual GPGPU.

Accelerators extend performance primitives without affecting formal semantics. Of course, performance is an important part of correctness for most programs. To resist silent performance degradation, the compiler must complain when acceleration is not recognized or cannot be implemented. Also, the compiler must never apply acceleration where not explicitly annotated. 

The cost of acceleration is implementation complexity and risk to correctness, security, and portability. This risk is mitigated by the reference implementation, which can be compared in fuzz testing or analyzed for model-based tests, and provides a fallback implementation (albeit with warnings). The complexity tradeoff is most worthy when it enables use of Glas in a problem domain that is otherwise performance prohibitive. 

*Aside:* It is feasible to support accelerators without a valid reference implementation, e.g. `prog:(do:fail, accel:list-append)`. This might be convenient short-term for development of experimental accelerators, but is not recommended long-term.

### Distributed Computation

For computation at large scales, it is feasible to accelerate evaluation of confluent concurrency models such as [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) or [Lafont Interaction Nets](https://en.wikipedia.org/wiki/Interaction_nets). The accelerator would distribute the computation across multiple processors, e.g. leveraging cloud or mesh as needed. Processes within the network would have access to local accelerators. 

Of course, we can also use effects for non-deterministic concurrency and distribution. See [Glas Apps](GlasApps.md) for more on this subject.

## Thoughts

### Language Compatibility

Modulo export function, default output from a g0 module is a record of form `(swap:prog:(do:swap, ...), try-then-else:macro:Program, pi:data:Value, ...)`, representing a dictionary of definitions. Each entry in the record is a word:deftype:Def triple, with symbolic words and deftype. The enables g0 to call macros and programs the same way, without syntactic distinction at the call site.

It would be convenient if most languages adopt a convention of compiling compatible dictionaries. As needed, languages could extend the set of deftype or reserve certain symbols for multi-method lookup tables or other aggregators.

## Logging Conventions

Almost every application model will support a **log:Message** effect to support debugging. In general, we'll often want the ability to filter which messages we're seeing based on task, level, role, etc.. These properties are mostly independent of content. Instead of a variant type, log messages will normally use a record of ad-hoc fields, with de-facto standardization. For example:

        (lv:warn, role:debug, text:"English message")

This allows gradual and ad-hoc structured extension to log messages, e.g. with provenance metadata, without breaking existing routes or filters. It is feasible to extend log messages with images or even applets to better explain issues.

*Note:* Efficient logging with large values require support for structure sharing within the log storage, e.g. using the stowage model.

### Database Modules

It is feasible to design a language module that knows how to parse a structured binary or database file such as MySQL or LMDB or MessagePack. Such languages have several potential benefits. Databases are more scalable than text files. Support for multiple sublanguages is easier with databases instead of indenting or distinguishing texts. Databases are easier to tool with multiple views and visual programming.

A concern with database modules is that we'll often need to parse and 'compile' the whole database into a value for Glas modules. This will become more expensive as the database grows larger or is updated more frequently. This can be mitigated by memoization and by modular use of databases.

### Glas Object

The idea is a standard language for stowage and sharing of Glas values at a large scale. Even better if streamable for use in a network context.

See [Glas Object](GlasObject.md).

### Gradual Types

By default, Glas does lightweight static arity checkss, but there is no sophisticated type system built-in. Language modules can introduce their own type system, type annotations, and safety analyses. Thus, the type system is effectively user-defined.

Glas should work very well with gradual types. Access to program definitions as data enables type checks to be performed post-hoc, e.g. via assertions in a later module, without invasive modification of existing modules to add annotations. It is also feasible to overlay several type systems, with independent analyses. Memoization and stowage can mitigate redundant checks.

### Graph Based Data

I rejected general graphs as the basic data structure for Glas because trees are more simple and adequate for most use-cases. However, I think it's still a good idea to develop some language modules around the observation and manipulation of abstract graphs. We could implement and eventually accelerate these languages.

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints and search tactics for a program. For example, each program component would declare constraint variables, describe how to compute a result (or fail) based on the values of those variables, and describe potential ways to assign those variables. Separately, we search for solutions. Glas provides a viable foundation, though the g0 syntax is not suitable for this role.

A challenge will be achieving effective performance from search in context of deterministic computations. Memoization may help if applied carefully.

### Provenance Tracking

I'm very interested in automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. I still don't have a good idea about how to approach this without huge overheads.
 
