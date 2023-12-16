# Glas Design

As a backronym, 'General LAnguage System'. Glas was named in reference to transparency of glass, liquid and solid states to represent staged metaprogramming, and human mastery over glass as a material. 

Design goals for glas include purpose-specific syntax, compositionality, extensibility, metaprogramming, and live coding. Compared to conventional languages, there is more focus on the compile-time. However, there is a related exploration of non-conventional application models.

## Command Line

The glas system starts with a command line tool 'glas'. This tool knows how to compile modules, bootstrap language-g0, extract binary data, and run simple applications. The 'language' of command line arguments is extensible by defining 'glas-cli-(opname)' modules and use of application macros. See [Glas CLI](GlasCLI.md) for details.

## Modules and Syntax

Modules are represented by files and folders. Every module compiles to a glas value. This value usually represents a dictionary of useful definitions, but there is no restriction on type.

A file is compiled based on its file extensions. To process a file named "foo.ext", the glas command line will load the global module 'language-ext' then evaluate the compiler program defined by that module. File extensions may compose, e.g. to process file "foo.x.y" we'll first appy language-y followed by language-x.

A folder is compiled to the value of the contained 'public' module, which must be a file. Folders also serve as the boundary for local modules: a local module must be another file or subfolder within the same folder as the file being compiled. This ensures folders are relocatable within a context of global modules.

Global modules are represented by folders and are discovered based on command line configuration. Initially this configuration supports a conventional search path, but it should eventually include network repositories. Global and local modules are fully distinguished by default, with no fallback.

The compiler function has very limited access to the environment: it can load modules and log warnings or errors, but is otherwise a pure function. This restriction is intended to simplify caching and reproducibility and cyclic dependency detection. See *Language Modules* below.

## Data

Glas currently represents data using finite, immutable binary trees. 

Trees can directly represent structured and indexed data without modeling pointers, align well with the needs of parsing, and are simpler than graphs for expressing incremental construction or reasoning about termination. A relatively naive encoding:

        type Tree = ((1 + Tree) * (1 + Tree))   
            a binary tree is pair of optional binary trees

A binary tree can easily represent a pair `(a, b)` or either type `(Left a | Right b)`. However, glas systems favor labeled data because labels are more meaningful and extensible. Labels are encoded into a *path* through a tree, favoring null-terminated UTF-8. For example, label 'data' would be encoded into the path `01100100 01100001 01110100 01100001 00000000` where '0' and '1' respectively represent following the left or right branch. A record such as `(height:180, weight:200)` may have many such paths with shared prefixes, forming a [radix tree](https://en.wikipedia.org/wiki/Radix_tree). A variant would have exactly one label.

To efficiently represent labeled data, non-branching paths are compactly encoded by the glas runtime system or [glas object serialization format](GlasObject.md). A viable runtime representation is closer to:

        type Tree = (Stem * Node)       // as a struct
        type Stem = uint64              // encoding 0..63 bits
        type Node = 
            | Leaf 
            | Branch of Tree * Tree     // branch point
            | Stem64 of uint64 * Node   // all 64 bits

        Stem Encoding
            10000..0     0 bits
            a1000..0     1 bit
            ab100..0     2 bits
            abc10..0     3 bits
            abcde..1    63 bits

It is feasible to could further extend Node with specialized variants to support efficient binary data, list processing, records as structs instead of radix trees, etc.. The above offers a reasonable starting point, but the intention is that data in practice should be represented much more efficiently than the naive encoding of binary trees.

Integers in glas systems are usually encoded as variable length bitstrings, msb to lsb, with negatives in one's complement:

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

Bytes and fixed-size words are instead encoded as bitstrings of exact size, e.g. 8 bits per byte, msb to lsb. For example, byte 4 would be '00000100'. 

Other than bitstrings, sequential structure is usually encoded as a list. A list is represented as a binary tree where the left nodes are elements and the right nodes form the spine of the tree, terminating with a leaf node.

        type List a = (a * List a) | () 

         /\
        1 /\     the list [1,2,3]
         2 /\
          3  ()  

Direct representation of lists is inefficient for many use-cases. To enable lists to serve most roles, lists will often be represented using [finger tree](https://en.wikipedia.org/wiki/Finger_tree) [ropes](https://en.wikipedia.org/wiki/Rope_%28data_structure%29). This involves extending the earlier 'Node' type with array and binary fragments and logical concatenation, then accelerating list operations to slice or append large lists.

To support larger-than-memory data, glas systems will also leverage content-addressed storage to offload volumes of data to disk. I call this pattern *Stowage*, and it will be heavily guided by program annotations. Stowage simplifies efficient memoization, and network communication in context of large data and structure-sharing update patterns. Stowage also helps separate the concerns of data size and persistence.

## Programs

Programs are values with a known interpretation. There are many desirable qualities for a model of programs - simplicity, composability, extensibility, usability, debuggability, discoverability, efficiency, scalability, cacheability, etc.. Alas, tradeoffs are necessary. The glas system can eventually support multiple program models, but the initial model for bootstrap will greatly impact development of the glas system.

I initially proposed a concatenative functional programming model, the 'prog' model below. This model is easy to implement but is more difficult to use than I'd prefer. I'm exploring a few alternatives based on process networks, grammars, and term rewriting.

Regarding performance, there is a work-around to program models with weaker performance: the *acceleration* pattern enables a runtime to replace a function with a high-performance implementation. All glas program models should support annotations to guide acceleration and other performance or debugging extensions.

## The 'Prog' Model

The 'prog' model of programs is minimalist, stack-oriented, dynamically typed, with backtracking conditional behavior and a flexible effects handler. Design priorities are simplicity and compositionality.

### Stack Operators

Operations manipulate a data stack instead of named variables or registers. There is no direct access to a 'heap', but large values on the stack may implicitly be represented using heap-allocated memory.

* **copy** - copy top item on data stack
* **drop** - remove top item on data stack
* **dip:P** - run P below top item on data stack
  * move top item from data stack to top of dip stack
  * run P
  * move top item from dip stack to top of data stack
* **swap** - switch the top two items on stack
* **data:V** - add copy of V to top of stack

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

The data stack in glas is essentially an intermediate data plumbing model to avoid explicit variables. A compiler can feasibly replace the stack with variables, or something even more fine-grained based on static knowledge of data structures.

### Control Operators

* **seq:\[List, Of, Operators\]** - sequential composition of operators. 
  * *nop* - identity function can be represented by empty seq  
* **cond:(try:P, then:Q, else:R)** - run P; if P does not fail, run Q; if P fails, undo P then run R. Variants:
  * 'then' and 'else' clauses are optional, default to nop.
* **loop:(while:P, do:Q)** - run P. If successful, run Q then repeat loop. Otherwise, exit loop. Variants:
  * *loop:(until:P, do:Q)* - run P. If that fails, run Q then repeat loop. Otherwise, exit loop.
  * 'do' field is optional, defaults to nop.
* **eq** - Remove two items from data stack. If identical, continue, otherwise fail.
* **fail** - always fail; causes backtracking in context of cond/loop
* **halt:Message** - logically diverges, like an infinite loop but more explicit in its intention. Prevents backtracking. Message should indicate cause to a human programmer: undefined behavior, type-error, invalid accelerator, todo, etc.. 

Static stack arity is required. For example, 'try-then' arity must match 'else' arity. A loop must have invariant arity for a complete cycle. Arity is easy to compute even with the complication of 'fail' or 'halt'.

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
        halt:_ : ∀S,S' . S → S'

User syntax can extend the effective set of control operators. For example, it is feasible to compile a local set of mutually recursive definitions into a state machine loop. Also, there is no built-in 'eval' but it is feasible to implement one.

### Data Operators

These operators are directly applicable to manipulation of radix trees but can be leveraged together with control operators to achieve arbitrary data manipulation. A relevant assumption is that arithmetic, list processing, and other useful performance primitives will be supported via acceleration. 

Operators:

* **get** ((label:V|R) label -- V) - given label and record, extract value from record. Fails if label is not present in record.
* **put** (V (label?_|R) label -- (label:V|R)) - given a label, record, and value on the data stack, create new record with the given label associated with the given value. Will replace existing label in record, if present.
* **del** ((label?_|R) label -- R) - returns record minus the label and any vestigial suffix of the label, i.e. bits not shared by other labels in the record. This is equivalent to first putting the label into the record then removing it.

Any bitstring may be treated as a label. For example, a pair `(A, B)` can be viewed as a record using one-bit labels, e.g. `(0:A, 1:B)`. However, glas systems will usually favor symbolic labels for reasons described in *Values*. If a label argument is not a valid bitstring, these operators halt (divergence, not backtracking failure).

### Effects and Environments

We model effects as an ad-hoc exchange of data, interacting with the environment. An effects API might be described in terms of which values are recognized by the environment and how the environment is expected to respond.

Operators:

* **eff** - interact with environment by exchange of data (1--1 arity, `Request -- Response`).
* **env:(do:P, with:Handler)** - sandbox the environment while evaluating subprogram P. The Handler should have 2--2 arity, `Request State -- Response State`. The top value from the data stack is hidden from P and treated as initial `State`. Every 'eff' call in P instead calls Handler. Final `State` is returned at top of data stack. 

It is possible in theory to partially evaluate effects via abstract interpretation of requests and state, but it likely won't be easy in practice.

### Annotations

The 'prog' header is used for both annotations and as the toplevel header for values in this model.

* **prog:(do:P, ...)** - run program P. Labels other than 'do' are annotations. Annotations can affect performance, verification and validation, integration with development environments, and other useful properties. But behavior observable within a valid program must be same as P.

Some potential annotations:

* *accel:Op* - ask interpreter or compiler to accelerate a subprogram. Here, 'Op' might be a symbol specifying a runtime built-in function (e.g. 'accel:list-append') or might represent code for a specialized target (e.g. 'accel:gpu:GpuCode' for eval on a GPGPU). The 'do:P' field should express equivalent behavior for verification purposes. 
* *refl:Program* - `prog:(refl:Prog1, do:Prog2)` ask runtime to evaluate Prog1 with access to an ad-hoc, effectful reflection API, then evaluate Prog2. This reflection API must be designed such that it does not influence formal behavior of Prog2, though it could affect performance or debugging. In cases where runtime tweaks should be scoped, they're implicitly scoped to Prog2.
* *stow:Options* - ask runtime to move a large value at the top of the data stack from RAM to disk. Typically uses content-addressed references under the hood (i.e. secure hashes).
* *memo:Options* - ask runtime to memoize a function, such that repeated evaluation is more efficient. Options might include persistent storage, replacement strategies, etc.. 
* *prof:Options* - ask runtime or compiler to add some code profiling support around the given subprogram. 
* *name:Value* - name a given subprogram to support debugging or profiling. This might affect performance if there is any attempt by the optimizer to preserve names.
* *arity:(i:Nat, o:Nat)* - a super lightweight static type, indicating a number of inputs and outputs on the data stack. Easily checked. 
* *type:TypeDescriptor* - represent ad-hoc type assumptions for the subprogram. Might also include proof hints to support a type checker.

Annotations have the potential to be very flexible. But to fully leverage annotations will require a lot of non-trivial design and development work. 

### Known Weaknesses and Mitigation Strategies

The 'prog' model has several known weaknesses, but there are mitigation strategies.

* There is no 'call function by name' operator. Common subroutines will be replicated in a program many times. This can be mitigated: 
  * structure sharing can reduce memory overheads for repetition
  * annotations can guide a compiler in breaking a program into subroutines
  * dictionary compression pass can automatically break a program into subroutines
  * memoization can reduce repetitive typechecking and on common subprograms
* There are no first-class functions. This simplifies reasoning, especially about effects, but it does complicate some tasks. This can be mitigated via accelerated eval.
* The prog model has no built-in support for parallel, concurrent, or distributed computation. The effects system is an awkward bottleneck for introducing these features.

I might be replacing 'prog' with a [grammar-logic based programming model](GrammarLogicProg.md) soon.

## Applications

The glas command line can run some applications with access to filesystem and network. The initial application model is designed around the idea of a repeating transactional step function:

        type Step = init:Params | step:State -> [Effects] (halt:Result | step:State)

The step function is evaluated repeatedly by an implicit top-level loop until it returns 'halt'. If it returns 'step' that becomes input for future repetitions. A failed step is aborted and retried, but this can be optimized as awaiting changes to effectful observations. The effects API must be designed to work nicely with transactions, e.g. when interacting with a remote service, a request must be committed before a response can be expected.

Concurrent applications can be expressed if we combine incremental computing optimizations with a non-deterministic choice effect. Essentially, given a **fork:List** request that is stable over many steps, a runtime can clone the app for each element in the list and treat each clone as an independent thread. See [glas applications](GlasApps.md) for more detail.

Beyond this initial application model, [glas command line](GlasCLI.md) enables extraction of executable binaries and may eventually be extended to 'run' alternative application models.

## Language Modules

Language modules are global modules with a simple naming convention: `language-xyz` provides the compiler function for files with extension `".xyz"`. Initial language modules should compile to a dictionary of form `(compile:prog:(do:Program, ...), ...)` with a 1--1 arity program. 

Input to the compiler function is (usually) a file binary. Final output is the compiled module value. Compilation may fail, hopefully after logging some error messages. Compile-time effects are constrained to simplify caching, sharing, and reproducibility. Effects API:

* **load:ModuleRef** - Response is compiled value for the indicated module. The request may fail, e.g. if the module cannot be found or compiled, with cause implicitly logged. Currently propose a few forms of ModuleRef: 
  * *global:String* - search for global module based on configuration of CLI
  * *local:String* - search for module in same folder as file being compiled
* **log:Message** - Message should be a record, e.g. `(text:"Uh oh, you messed up!", lv:warn)`. This simplifies extension with contextual metadata or switching from texts to more structured content. Response is unit. Log messages should target the human user or the development environment.

The glas command line will include a built-in compiler for [language-g0](GlasZero.md), a Forth-like language with staged metaprogramming. This built-in compiler is used to bootstrap the actual language-g0 module if possible, emitting a warning on bootstrap failure. Other glas languages can build upon language-g0.

Definitions other than 'compile' in the language module may be defined to support tooling, such as IDE or REPL integration, a standard linter or formatter, etc.. And glas systems may eventually recognize compiler functions expressed in models other than 'prog'.

## Automated Testing

Glas system tools will assume all modules named with prefix 'test-' represent test programs, i.e. of form `(test:Program, ...)` where the Program has a recognized type like 'prog'. Test programs can be expressed as deterministic 0--0 arity functions with access to 'load', 'log', and 'fork' effects.

* **fork:N** - returns a natural number smaller than N.

The fork effect represents non-deterministic choice and provides a basis for long-running fuzz testing. The system may guarantee some quota of forks is tested exhaustively - e.g. first fork by default, adjustable via annotation. Failures can be remembered to adjust future priorities. (Load and log effects behave the same as for language modules.)

Tests can be evaluated explicitly via CLI, or implicitly and continuously as software distributions are updated. The latter allows the system to maintain a distribution-level health report.

*Note:* Users can also express static assertions to be evaluated by language modules. Failed modules would also be part of a distribution's health report. This is very convenient for lightweight unit tests, yet unsuitable for long-running or probabilistic testing. 

## Type Checking

Glas systems will at least check for stack arity ahead of time. A more precise static type analysis is optional, but can be supported via annotations. Memoization is useful to mitigate rework. In some cases, types might be checked at runtime, as we do with label inputs to get/put/del. Use of `halt:type-error` is an effective way to indicate a runtime type error without treating it as conditional.

## Performance

### Acceleration

        prog:(do:P, accel:Op, ...)

Acceleration is an optimization pattern where we annotate a subprogram to be replaced by a more efficient or scalable implementation known to the interpreter or compiler. 

In the simplest use case, users reference built-in functions such as 'accel:list-append' or 'accel:i64-add'. The runtime can specialize data representations to support these operations, e.g. use a rope structure to represent larger lists to ensure list-append is efficient. Relatedly, we can manually guide representation via 'accel:array-type' or similar, to indicate that a list should be represented under-the-hood by an array. This pattern extends glas systems with new performance primitives (without extending semantics).

As a more sophisticated use case, we might accelerate evaluation of code on an abstract, simulated GPGPU, CPU, Kahn Process Network. This might be expressed via 'accel:gpu:GpuCode'. The runtime compiles GpuCode to run on an actual GPGPU, so the GpuCode type should be easily translated, reasonably portable, memory-safe, and have other nice qualities. This allows acceleration to handle roles that would otherwise require FFI.

Further, dynamic code can be indirectly supported via 'accel:prog-eval' and 'accel:prog-type'. Here 'accel:prog-type' simplifies reuse of a prog value by caching arity analysis, JIT compilation, and other one-off steps. 

To resist silent performance degradation (across ports, runtime versions, etc.), the runtime or compiler should report an error where requested acceleration is unrecognized or unsupported. To recover portability, 'accel:opt:Op' permits silent fallback to the 'do:P' implementation. A prioritized list of fallbacks can be represented as `prog:(accel:opt:Option1, do:prog:(accel:opt:Option2, do:EtCetra))`.

Ideally, the glas system will verify that 'accel:Op' and 'do:P' truly represent the same behavior. Although full verification via static analysis or exhaustive testing is often difficult, we can at least introduce unit tests and perhaps some fuzz testing or random sample testing.

### Stowage

I use the word 'stowage' to describe systematic use of content-addressed storage (addressed by secure hash) to manage larger-than-memory data. Stowage is a variation on virtual memory paging, i.e. large subtrees can be moved from local memory to disk or a remote service if not immediately needed. Stowage simplifies support for large persistent variables, memoization, and incremental communication.

In context of glas systems, stowage is semi-transparent - invisible to pure functions and most effects, but guided by annotations and potentially accessible via runtime reflection effects. [Glas Object](GlasObject.md) is intended to be an efficient representation for stowage and serialization of glas data.

### Memoization

Purely functional subprograms in glas can be annotated for memoization. This can be implemented by storing a lookup table mapping inputs to outputs. This lookup table can be persistent to support reuse across builds. 

In combination with stowage, it is possible to incrementally process large data by memoizing recursive computations. This includes indexing of data, insofar as indexing can be expressed in terms of merging the indices of each component. Lists require special attention for stable chunking but it is possible to align memoization with the underlying finger-tree if we annotate a reducing function as associative (this annotation would be subject to proof or random testing).

Indexing large data via memoization and stowage is an underlying assumption for my vision of glas systems. Without this, we would rely entirely on stateful indices, which are locally efficient but error prone and difficult to share, compose, extend, or update at runtime.

### Content Distribution

Networked glas systems can potentially support [content distribution networks (CDNs)](https://en.wikipedia.org/wiki/Content_delivery_network) to improve performance when repeatedly communicating large stowed data values (see *Stowage*). A CDN service is not fully trusted, but it is feasible to derive a decryption and lookup key from the original secure hash of content. 

Usefully, we might support garbage collection of the CDN. With encrypted data, it cannot directly read the data to find dependencies. But we could upload some metadata such as a list of lookup keys that should be present together with the data.

### Compression Pass

When compiling glas programs, a useful optimization pass is to identify common subprograms and translate those to reusable function calls. This pass may be guided by annotations and is not necessarily aligned with user defined functions. Usefully, compression may occur after partial evaluation and other specialization passes. 

### Application Layer 

The initial 'prog' model isn't the best for scalability. The effects system imposes some structural limits. I'm currently developing [grammar logic programming](GrammarLogicProg.md) for glas that could serve as a more flexible and scalable basis for computation. 

## Reflection

A runtime can directly provide a reflection API as part of the application effects API. This would enable an application to review its resource consumption, performance profile, or debug outputs then adjust behavior appropriately. With sufficient runtime support, an application might also be able to update application code at runtime, e.g. requesting reload from source to support live coding.

Additionally, a runtime could support annotations that provide limited access to a reflection API, e.g. the 'refl' annotation described earlier. This would also be capable of debug outputs and performance tuning. But annotations must not directly affect formal behavior of a program. At most, annotations could halt a problematic program early or provide useful hooks for the effectful reflection API.

## Thoughts

### Computation Models

I'm tempted to switch to a program model more suitable for lazy, concurrent, and distributed computation than the initial glas 'prog' model. Perhaps [something based around (temporal) Kahn process networks](GlasKPN.md), for example. Abstract effects could be modeled as concurrent interactions.

So far, my conclusion is that the simpler model is wiser for bootstrap. However, we also aren't locked in to the 'prog' model. Other models are accessible via acceleration, extension to the post-bootstrap glas command line, or compilation to executable binaries.

### Useful Languages

The initial g0 language is simple and supports metaprogramming well, but has many deficiencies. The intention is to develop more languages within the glas system. Some possibilities:

* languages for embedding data - text files, JSON, XML, CSV, MsgPack, SQLite files, even [Glas Object](GlasObject.md). We might want to adapt or extend some of these for glas modularity.
* languages with more flexible definitions - recursion, type-driven overloading, await/async compiled to state machines, more explicit type system and type annotations than g0, etc..
* text preprocessor languages, i.e. where the compiler function is text->text. This would enable flexible metaprogramming of the text layer for any program, e.g. ".json.m4" could logically produce a JSON string much larger than the input using the text preprocessor.
* graphical programming support, perhaps building upon a structured representation like JSON or database files.

Getting to the point where this is viable ASAP seems worthwhile.

### Abstract and Linear Data

Abstraction is a property of a subprogram. Data is abstract *in context of* a subprogram that constructs and observes data only indirectly via externally provided functions. Substructural types, such as linear types, extend this by also restricting copy and drop operations except via provided functions.

It is feasible to annotate subprograms with linear types and leverage this to support in-place updates and reduce garbage collection. However, this optimization is difficult to achieve in context of backtracking conditionals or debugger views. The more general motive for linear types is to typefully enforce protocols, such as closing a channel when done.

### Databases as Modules

It is feasible to design language modules that parse binary database formats (LMDB, MessagePack, [Glas Object](GlasObject.md), MySQL or SQLite files, etc.). Doing so should, in theory, simplify development of visual or graphical programming environment. My vision for glas systems is that code should be a flexible mix of text and structured input, using diagrams (boxes and wires, Kripke state machines, etc.) where convenient.

Incremental compilation over large databases is feasible via memoization or partitioning into multiple files. But I think it would be better if we favor support small, composable, modular databases for most use cases.

### Program Search

I'm interested in a style of metaprogramming where programmers express hard and soft constraints, search spaces, and search tactics for programs. Type safety can be treated as a hard constraint to support type-driven overloading. But the emphasis will be modular, heuristic decisions expressed as soft constraints, with ability to prioritize some search paths over others. Incremental computing and caching are also essential.

Something like an [A-star search algorithm](https://en.wikipedia.org/wiki/A*_search_algorithm) might work, assuming we can express soft constraints as costs with a [consistent heuristic](https://en.wikipedia.org/wiki/Consistent_heuristic), i.e. monotonic costs for various choices, preferably with costs adjusted based on context (perhaps indicate costs via effect that takes an arbitrary value, which is interpreted by the context).

This will likely also require a specialized program model.

### Provenance Tracking

The glas module system currently hinders manual provenance tracking, e.g. we cannot access module names or file paths from the 'compile' function. Also, metaprogramming is widespread so we'd need to trace influence through macros. 

A partial mitigation strategy is that log messages can be associated with each file as it compiles. This is likely the only option short-term. A more complete solution will require tracing compiled output back to the inputs that influenced it, preferably to the precision of binary ranges within files. This is probably too much to trace efficiently, but we might try some heuristics around the notion of spreading and diluting blame similar to [SHErrLoc project](https://research.cs.cornell.edu/SHErrLoc/). 

### Alternative Data

I've often considered extending glas data to support graph structures or unordered sets. I think these could give some benefits to users, but it isn't clear to me how to effectively and efficiently work with them yet. For now, perhaps keep it to accelerated models.
