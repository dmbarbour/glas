# Glas Design

Glas is named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. As a backronym, it can also be taken as 'General LAnguage System'.

Design goals of Glas include: Easy reproduction, sharing, and integration of software artifacts. Flexible integration of text-based, graphical, and structural programming environments. Accessible staged metaprogramming so programmers can express logic at the correct level to reduce implementation details. An exploration of non-conventional application models to simplify concurrency, distribution, deployments, and live software update.

## Module System and Syntax

Glas modules are typically represented by files and folders. Dependencies between valid Glas modules must form a directed acyclic graph. Every module will deterministically compute a value depending only on other values from the module system.

To compute the value for a file `foo.ext`, the Glas system will compile the file binary using a program defined in module `language-ext`. The [g0](GlasZero.md) language (a Forth variant) is predefined to support bootstrap. File extensions compose, e.g. for `foo.xyz.json` we apply `language-xyz` to further compile the value output by `language-json`. The value for a folder `foo/` is the value of its contained 'public' module, such as `foo/public.g0`. 

Modules cannot reference across folder boundaries. Files can reference local modules defined in the same subfolder or global modules found via GLAS_PATH. Global modules should always be represented as folders. Later, we might extend module search or GLAS_PATH to include network resources.

*Note:* Glas systems encourage modules to output fully linked values, i.e. directly inlining content of other modules or subprograms instead of using references to be linked later. This simplifies direct interpretation, metaprogramming, and staging, but it does result in redundant structure and computations. Performance can be mitigated by memoization, content-addressed storage (see *Stowage*), and compression pass by a compiler.

## Data Model

Glas data logically consists of immutable binary trees. A naive representation is:

        type T = ((1+T) * (1+T))

Glas encourages use of *labeled* data structures, encoding labels as a path through the tree. For example, the label 'data' is represented by path `01100100 01100001 01110100 0110001 00000000`, using null-terminated UTF-8 text, with `0` and `1` representing left and right branches respectively. Labeled data is data reached by following the label path. A 'variant' has a single label followed by data, while a 'record' forms a [radix tree](https://en.wikipedia.org/wiki/Radix_tree) (aka trie) with labels sharing common path prefixes. The unit value can be represented by a singleton tree. Symbols and numbers will often be represented as variants terminating in unit. For example, `00010111` encodes byte 23, and `10111` encodes natural number 23. 

To efficiently represent labeled data and numbers, the underlying data representation may be closer to:

        type Bits = (compact) Bool list
        type T = (Bits * (1 + (T*T)))

Details of underlying representation are not directly exposed to programs. Relatedly, a list has type `type List a = (a * List a) | ()`, i.e. constructed of `(Head * Tail)` pairs (nodes with two children) terminating in unit. However, to support efficient split, concat, and double-ended queue operations, Glas systems will *accelerate* lists under-the-hood using a [finger tree](https://en.wikipedia.org/wiki/Finger_tree) representation. See *Acceleration* below.

To work with very large trees, Glas systems may offload subtrees into content-addressed storage. I call this pattern *Stowage*. Of course, Glas applications may also use effects to interact with external storage. Stowage has a benefit of working nicely with pure computations, serves as a virtual memory and compression layer, and has several benefits for incremental computation and communication. Stowage can be guided by program annotations.

## Command Line

The Glas command line interface supports practical use of the Glas module system. The command line is extensible with user-defined verbs via naming modules with a `glas-cli-*` prefix. 

        glas foo Parameters 
            # implicitly rewrites to
        glas --run glas-cli-foo.run -- Parameters

The glas executable must bootstrap the language-g0 module, build all transitive dependencies to compile the glas-cli-foo module, extracts the 'run' program, verifies arity, then evaluates the program with access to Parameters and ad-hoc effects. Unnecessary rework, such as bootstrapping language-g0 every time, can be mitigated by caching. See [Glas CLI](GlasCLI.md) for details.

*Note:* Besides `--run`, the Glas command line interface should support `--version`, `--help`, and perhaps a few other useful built-in functions. However, the intention is that most logic should be separated from the executable and instead represented within the module system.

## Glas Programs

Glas programs are Glas values with a standard interpretation. The program model is designed for convenient composition, extension, staging, analysis, and simplicity of implementation. Glas programs are necessary when defining language modules or command-line verbs. User applications can optionally be represented by Glas programs, assuming command-line verbs to compile and extract a binary.

Glas programs manipulate a data stack. Valid Glas programs have an easily computed a static stack arity (e.g. 2--3 meaning 2 inputs, 3 outputs) and an effects API that describes expected requests and resulting behavior. Interaction with the outside world can be controlled or adapted via effects handlers. 

Glas conditionals are backtracking. If an operation fails within a conditional context, we undo the effects then evaluate the alternative branch. If a branch obviously always fails, it might be eliminated by a compiler. This backtracking behavior is convenient for composable pattern matching and transactional behavior, but does complicate integration with synchronous effects APIs (such as filesystem access).

Glas programs do not have a namespace. Logically, code is inlined and replicated. Replication can be mitigated by structure sharing, memoization, or perhaps a compression pass by a compiler. The advantage is that subprograms can be understood or manipulated in isolation, without contextual complications.

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
 * *nop* - do nothing - is represented by empty seq. 
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

Use of 'env' enables a program to conveniently implement a sandbox or effects adapter for a subprogram. User-defined effects handlers must have static arity `Request State -- Response State`, also allowing failure. Initial handler state is captured by the 'env' call.

* **env:(do:P, with:H)** - arranges for operator 'eff' within subprogram P to call handler H. Top item from data stack is captured for use as handler state, and is returned upon exiting env.
* **eff** - invoke current effects handler, taking top stack item as Request and returning a Response.

    env:(do:P, with:H)          (as ENV)
        H ⊲ ∀S. ((S * Request) * State) → ((S * Response) * State)
        eff ⊲ ∀S. (S * Request) → (S * Response) ⊢ P ⊲ E → E'
        -----------------------------
        ENV ⊲ (E * State) → (E' * State)
    eff         type determined by env

Routing all effects through a single handler simplifies reasoning about control of effects, but direct interpretation requires a lot of runtime routing. Partial evaluation of handlers can mitigate runtime routing overheads. 

A top-level effects handler will ultimately be provided by the compiler or runtime. Backtracking conditional behavior (try, while, and until clauses) must undo top-level effects. Thus, the top-level effect API must be designed to simplify backtracking. Asynchronous APIs (e.g. channels, message passing) are convenient for this because a runtime can buffer and defer writes until commit.

*Aside:* Session types would be convenient for encoding rules into request patterns, e.g. to enforce bracketing.

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
* *arity:(i:Nat, o:Nat)* - a simplistic stack-effect type, representing the number of values input from stack and output to stack. Also helps stabilize partial evaluation of macros, e.g. in context of the g0 language.

The 'prog' header also serves as the primary variant for programs within a *Dictionary* value.

### List, Arithmetic, Bitwise Operators, Etc..

Glas does not have operators for most data representations. Other than the few record operators, the idea is to implement these functions within Glas then annotate for *Acceleration*.

## Type Checking

Glas does not have a built-in type system. Runtime type errors simply cause evaluation to fail. However, many Glas language modules will support syntax to express programmer assumptions or static assertions. These can be checked when the module compiles, or embedded as program annotations for later verification. Redundant checks are mitigated by memoization.

## Application Models

### Language Modules

Language modules have a module name of form `language-(ext)`, binding to files with extension `.(ext)`. For example, module `language-g` would be used to process file `foo.g`. The language module must compile to a record value of form `(compile:Program, ...)`. The compile program will apply to the file binary to produce a Glas value. Other record fields can provide linters, decompiler, code completion support, [language server](https://en.wikipedia.org/wiki/Language_Server_Protocol) support, REPL support, documentation, etc. for the language.

The compile program must have arity 1--1, and has a limited effects API to guarantee deterministic compilation based on source and state of other modules. A relevant assumption is that dependencies between modules are acyclic (forming a directed acyclic graph). Effects API:

* **load:ModuleID** - Response is compiled value for the indicated module, or the request may fail. Currently, modules are usually identified by strings (a list of bytes) such as `"foo"`, eliding file extension. We search for the named module locally then fallback to `GLAS_PATH`. 
* **log:Message** - Response is unit. Arbitrary output message, useful for progress reports, debugging, code change proposals, etc.. 

Load failures may occur due to missing modules, ambiguous names (e.g. if we have both `foo.g0` and subdirectory `foo/`), detection of dependency cyles, failure of a compiler function, etc. A compiler can continue in presence of most load failures. The cause of failure is not visible to the client module but should be visible to developers, e.g. via log. 

*Note:* To support bootstrap, a compile function for a Forth-like [language-g0](GlasZero.md) is built into the Glas command line interface. A language-g0 module should be defined using the g0 language.

### Data Printer 

To support reproducible extraction of useful binaries from the module system, we might introduce a verb `glas print Value with Printer`, where Value and Printer are dotted path references into the module system. The printer should reference a program with arity 1--0 (receiving the value) and limited access to effects to guarantee determinism. Proposed effects API:

* **write:Binary** - Write binary data (a list of bytes) to stdout. Response is unit. Fails if argument is not a binary.
* **log:Message** - Arbitrary log message for debugging or progress. Will usually be pretty-printed to stderr. 

It is possible that a printer will fail partway through the job. In that case, we'll still print the stream to that point, then halt with a non-zero error code.

### Automated Testing

Static assertions when compiling modules are very useful for automated testing. However, build-time is deterministic and under pressure to resolve swiftly. This leaves an open niche for long-running or non-deterministic tests, such as overnight fuzz-testing.

To support this, we might express tests as arity 0--Any Glas programs with access to 'fork' effect to represent non-deterministic choice and search for failing tests. Additionally, tests could include an output log for progress and debugging. Viable effects API:

* **fork** - Response is a non-deterministic boolean - i.e. a '0' or '1' bitstring. In context of testing, this response should not be far or random, but rather guided by heuristics, memory, and program analysis to search for failing tests.
* **log:Message** - Response is unit. Write an arbitrary message to support debugging of tests.

We might develop a command line verb, `glas test ...` to automate running of tests.

### User Applications

This is the subject of the [Glas Apps](GlasApps.md) document. Intriguingly, concurrency can be modeled by a non-deterministic transactional loop without need for explicit multi-threading effects.

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

Acceleration is an optimization pattern. The idea to annotate specific subprograms for accelerated evaluation, then a compiler or interpreter should recognize the annotation then silently substitute a specialized implementation. Accelerated functions are often coupled with specialized data representations. For example, a Glas runtime may represent lists using finger trees, such that the accelerated list append function evaluates in logarithmic time with the smaller list.

Essentially, accelerators extend performance primitives without affecting formal semantics. Of course, performance is an important part of correctness for most programs. To prevent silent performance degradation, a compiler must report when requested acceleration is not recognized or cannot be implemented. Also, the compiler must apply acceleration only where explicitly annotated. 

The cost of acceleration is implementation complexity and risk to correctness, security, and portability. This risk is mitigated by the reference implementation, which can be verified in automatic tests and provides a fallback implementation. The complexity tradeoff is most worthy when acceleration enables use of Glas for problem domains that are otherwise performance prohibitive. 

*Aside:* It is feasible to support accelerators without a valid reference implementation, e.g. `prog:(do:fail, accel:list-append)`. This might be convenient short-term for development of experimental accelerators, but is not recommended long-term.

### Distributed Computation

For computation at large scales, it is feasible to accelerate evaluation of confluent concurrency models such as [Kahn Process Networks](https://en.wikipedia.org/wiki/Kahn_process_networks) or [Lafont Interaction Nets](https://en.wikipedia.org/wiki/Interaction_nets). The accelerator would distribute the computation across multiple processors, e.g. leveraging cloud or mesh as needed. Processes within the network would have access to local accelerators. 

Of course, we can also use effects for non-deterministic concurrency and distribution. See [Glas Apps](GlasApps.md) for more on this subject.

### Self Interpretation

Glas does not have first-class functions. But it is not difficult to write an 'eval' function for Glas programs within a Glas program. The 'eval' function could be accelerated to use the underlying JIT-compiler or interpreter. Together with adequate caching, this would effectively have the performance of first-class functions.

## Thoughts

## Logging Conventions

Almost every application model will support a **log:Message** effect to support debugging. In general, we'll often want the ability to filter which messages we're seeing based on task, level, role, etc.. These properties are mostly independent of content. Instead of a variant type, log messages will normally use a record of ad-hoc fields, with de-facto standardization. For example:

        (lv:warn, role:debug, text:"message for developer")

This allows gradual and ad-hoc structured extension to log messages, e.g. with provenance metadata, without breaking existing routes or filters. It is feasible to extend log messages with images or even applets to better explain issues.

*Note:* Efficient logging with large values require support for structure sharing within the log storage, e.g. using the stowage model.

### Bracketed Effects? No.

A useful pattern for effects is bracketed effects, e.g. increment-operation-decrement, where some operation is performed in context of a background effect. This can always be modeled by session types, but structural support would make it much easier to reason about. This might be represented by extending `eff` to `eff:P`, and also extending `env` with a clause to run after the bracketed subprogram P.

For now, leaving this out because I think it's less useful with asynchronous effects APIs. 

### Database Modules

It is feasible to design a language module that knows how to parse a structured binary or database file such as MySQL or LMDB or MessagePack. Such languages have several potential benefits. Databases are more scalable than text files. Support for multiple sublanguages is easier with databases instead of indenting or distinguishing texts. Databases are easier to tool with multiple views and visual programming.

A concern with database modules is that we'll often need to parse and 'compile' the whole database into a value for Glas modules. This will become more expensive as the database grows larger or is updated more frequently. This can be mitigated by memoization and by modular use of databases.

### Glas Object

The idea is a standard language for stowage and sharing of Glas values at a large scale. Even better if streamable for use in a network context.

See [Glas Object](GlasObject.md).

### Program Search

I'm very interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for a program. This would include overloading definitions based on types, e.g. we might search for subprogram that matches the input and desired output types. But it also includes automatic layout of GUIs, stitching service APIs together, and so on. It is feasible to develop a higher level programming model and suitable language modules around this idea.

### Provenance Tracking

I'm very interested in automatic provenance tracking, i.e. such that we can robustly trace a value to its contributing sources. I still don't have a good idea about how to approach this without huge overheads.
 
