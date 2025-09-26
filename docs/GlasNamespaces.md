# Glas Namespaces

In my vision for glas systems, huge namespaces define runtime configuration, shared libraries, and applications. Definitions can be distributed across dozens of DVCS repositories, referencing stable branches or hashes for horizontal version control. We rely on laziness to load and extract only what we need, and incremental compilation to reduce rework. 

Applications should be expressed as OO class-like namespaces: methods with open recursion, supporting inheritance and overrides. The final step is binding an application to a namespace of system APIs and runtime state. 

## Design Overview

The lambda calculus awkwardly supports namespaces, e.g. `let x = X in Expr` as syntactic sugar for `((λx.Expr) X)`. With fixpoints, we can also support recursive definitions.

We can extend the lambda calculus with reified environments. A keyword, say `__env__`, lazily extracts visible definitions into an abstract environment record. To utilize this environment, we can also introduce a prefix binding: `((νPrefix.Expr) Env)`. This makes names in Env available through Prefix, and implicitly shadows all existing names with Prefix. Prefix may be the empty string, thus we might express dotted-path access as `((ν.foo) Env)`.

To support scoping and interface adapters, we introduce a scoping rule consisting of a set of prefix-to-prefix rewrites, e.g. `{ "a" => "b", "b" => "c", "c" => "a", "d" => NULL }` rotates prefixes a, b, c and hides prefix d. In general, we apply the rule with the longest matching prefix (if any) to each name in the current environment, and we'll support full-name matches too. We can apply scopes to first-class environments through `(ν.(apply scope to __env__) Env)`.

Other useful namespace computation primitives include ifdef and union. With ifdef, we can more easily implement default definitions or compose partial interfaces. With union, we can conveniently integrate definitions into a shared namespace without giving everything a unique prefix. Union does risk ambiguity, but we can raise an error when we detect a name has multiple definitions, forcing developers to resolve the conflict.

Modularity in glas systems is file-oriented, albeit including DVCS files. Each file is processed by a front-end compiler, generating an AST representation of type `Env -> Env`, typically of form `ν.(Env Expr)`. The initial environment for the AST is empty, i.e. implicit `{ "" => NULL }` scope, thus it can access external names only through the provided environment.

By convention, modules receive a pseudo-global namespace '%\*'. This provides access to [program-model primitives](GlasProg.md), a method to load more modules, and a user-configurable '%env.\*' that via fixpoint to the user-configuration's 'env.\*'. The latter supports definition of shared libraries and applications.

The glas system supports user-defined syntax. The loader selects a front-end compiler based on file extension, '%env.lang.FileExt'. To bootstrap this, '%env.lang.glas' and '%env.lang.glob' are initially linked to built-ins.

Applications are typically definitions within a configuration, e.g. 'env.AppName.app'. Users may also load scripts that define 'app' (still using the configured '%env.\*'). Like modules, the application type is also `Env -> Env`. In this case the input environment contains 'sys.\*' definitions for runtime effects APIs, 'db.\*' and 'g.\*' registers, and 'app.\*' via fixpoint. The last-second fixpoint supports flexible inheritance and override when composing applications.

Applications typically define a pure 'settings' method to guide runtime configuration, a 'main' method for primary behavior, and 'http' or 'rpc' event handlers that the runtime knows how to integrate. Methods have an `Env -> Program` AST type. The Env input, in this case, binds to call-site specific registers and algebraic-effects handlers, while Program is abstract data constructed through program-model primitives in '%\*'. 

I expect most computation is expressed at the program layer instead of the namespace layer. The namespace AST may embed glas data but treats it as opaque. However, it is possible to encode flexible structure, e.g. we could [encode lists into lambdas](https://en.wikipedia.org/wiki/Church_encoding), or alternatively into reified environments that define 'head' and 'tail'. This may prove useful for some forms of metaprogramming.

Lazy evaluation is essential for this design to work as intended, supporting both lazy loading and metaprogramming through shared libraries, which are bound by fixpoint. 

## Abstract Syntax Tree (AST)

We'll represent the AST as structured glas data. This serves as the primary intermediate representation for namespaces and programs.

        type AST =
            | Name                  # substitute definition (logically)
            | (AST, AST)            # application
            | a:(AnnoAST, AST)      # annotation of RHS AST 
            | d:Data                # embedded glas data
            | e:()                  # reify current environment
            | t:(TL, AST)           # logically translate names in AST
            | b:(Prefix, AST)       # environment binder (applicative)
            | f:(Name, AST)         # function (aka lambda; applicative)
            | y:AST                 # fixpoint; AST must be applicative
            | c:(Name, (AST, AST))  # ifdef condition (left AST if defined)  
            | u:(List of AST)       # union: each AST must represent Env.
        type Name = binary excluding NULL. Composition via '/'.
          # convention: printable ascii or utf-8 text, dotted paths
        type Prefix = any binary prefix of Name + ".."
        type TL = Map of Prefix to (Prefix | NULL), as radix-tree dict

This representation saves a couple header bytes per name and application at the cost of requiring a little inspection to distinguish them. These are unambiguous, although only barely.

When translating names, we logically add a ".." suffix. This allows us to translate "bar" without accidentally affecting "bard" or "barrel", e.g. `{ "bar.." => "foo.." }`. Use of a singular "." also allows us to conveniently couple translation of 'bar' together with 'bar.\*' via `{ "bar." => "foo." }`. This rule alone doesn't *guarantee* prefix-uniqueness; for that, front-end compilers may reject use of ".." in names.

When '/' appears within a name, it represents a composition of names in scope. The primary use case is access control: a name may serve as the 'key' to unseal more names. Translation is applied left to right; unless '/' is explicitly matched, we translate the suffix following. Overlapping translation rules are permitted but discouraged: results are deterministic but may confuse users. Front‑end compilers may reject overlapping translation maps as ill‑formed.

## Evaluation

Evaluation of an `AST` is defined as lazy, substitutive reduction with respect to an environment mapping `Name → AST`. Environments are extended only by binders (`b:(…)`, `f:(…)`); annotations and translations are semantically inert unless a tool chooses to observe them.  

Not all operations have AST forms; for example, %load is a compile‑time effect for modularity. See *Loading Files*.

### Core rules

1. **Names**  
   - A `Name` is resolved by looking up its definition in the current environment.  
   - If the name is bound to an `AST`, the bound expansion is substituted logically at the point of use.  
   - Undefined names are errors.

2. **Application `(F, X)`**  
   - Evaluate `F` to a function value.  
   - Lazy semantics: arguments are passed as unevaluated thunks unless forced

3. **Annotations `a:(Anno, X)`**  
   - Logically stripped for evaluation.
   - That is, `a:(Anno, X)` evaluates exactly as `X`.  
   - Annotations may nevertheless guide external analyses (types, assertions, optimizers, profilers).

4. **Data `d:Data`**  
   - A value literal; evaluates to itself.
   - Opaque within this calculus, but accessible to program-model primitives.

5. **Environment reflection `e:()`**  
   - Evaluates to a reified representation of the current environment (names and substituted definitions visible at this point).  
   - This value can be persisted, inspected, or passed within programs.

6. **Translation `t:(TL, X)`** 
   - Evaluates as `X` under a **name‑rewriting environment** defined by `TL`.
   - Translation is purely lexical. It does not otherwise impact semantics.
   - This has many subtle mechanics. See expanded section below.

7. **Binder `b:(Prefix, X)`**  
   - Represents a function awaiting an environment argument.
   - Applying b:(Prefix, X) to environment Env_arg evaluates X under an environment where:
     - All prior names beginning Prefix are masked.
     - Names in Env_arg are bound in X by concatenating Prefix.
   - No separator is inserted; Prefix is used exactly as supplied.

8. **Function `f:(Name, X)`**  
   - Represents a lazy function: in application, `Name` will be bound to the argument AST within body `X`.  
   - Equivalent to a conventional lambda abstraction.

9. **Fixpoint `y:X`**  
   - Evaluates to the fixed point of `X`.  
   - `X` must be applicative: when applied, it reproduces its own definition.  
   - Enables recursive definitions without introducing new binding forms.

10. **Conditional definition `c:(Name, (Xthen, Xelse))`**  
   - If `Name` is defined in the current environment, evaluates as `Xthen`.  
   - Otherwise, evaluates as `Xelse`.  
   - This enables conditional specialization without requiring external preprocessing.

11. **Union `u:[E1, E2, …, En]`**
   - Each `Ei` must evaluate to an environment (as produced by `e:()` or equivalent).  
   - The result is a single environment containing the combined bindings.  
   - **Conflicts are errors** unless one of the following holds:  
     - The name is never referenced (unused binding).  
     - Both definitions refer to the **same location** (reference equality).  
     - Both definitions are **structurally equivalent** ASTs.
   - Empty union serves as efficient representation of empty environment.

### Translation (Expanded) `t:(TL, X)`

`TL` is a finite translation map of the form **Prefix → (Prefix' | NULL)**.  
Evaluation of `t:(TL, X)` means evaluating `X` where every name is rewritten by the following process.

#### Implicit suffix  
- Every *full name* is conceptually suffixed with `".."` before translation.  
- After translation, the suffix is stripped. If suffix was removed, error.  
- This makes prefix rules safe against accidental overlap.  
  - Example: rule `"bar.." → "foo.."` rewrites `"bar"` → `"foo"`, but does **not** rewrite `"bard"` to `"food"`.  

#### Slash components  
- Names may contain `/` separators:  

    C1/C2/…/Ck

- Translation proceeds **left‑to‑right**:  
  1. Start from the beginning of the name string.  
  2. Find the **longest prefix** (including possible `/`) that matches an LHS in `TL`.  
  3. Apply its mapping.  
     - If the mapping is `NULL`, the entire name is masked.  
     - Otherwise, replace the matched prefix with its RHS.  
  4. Skip forward to the next `/`. If none, then done.  
  5. Repeat translation for the remainder of the name.  

- In this way, each top‑level component is considered in sequence, but rules themselves may span multiple components (e.g. `"math/util.." → "mylib/utils.."`).

#### Composition of Translations

- Translations **compose sequentially**. Given two maps `A` and `B`, we can form the composite map:

  ```
  A fby B
  ```

  meaning “apply `A`, then apply `B`.”

- To construct `A fby B`:

  1. **Expand `A`:** for every input prefix that might be matched by `B`, if possible insert a redundant rule in `A`, mapping that prefix through unchanged (identity).  
  2. **Reseed outputs:** apply `B` to all outputs of `A`.  
  3. **Simplify:** eliminate redundant rules. A rule on prefix p is redundant if its effect is implied by the rule on the longest proper prefix of p.

- Example:

  ```
  { "bar" => "fo" }
    fby { "f" => "xy", "foo" => "z" }
  ```

  **Step 1: expand A**

  ```
  { "bar" => "fo", "baro" => "foo", "f" => "f", "foo" => "foo" }
    fby { "f" => "xy", "foo" => "z" }
  ```

  **Step 2: apply B to outputs**

  ```
  { "bar" => "xyo", "baro" => "z", "f" => "xy", "foo" => "z" }
  ```

  **Step 3: remove any redundancy** (none here).

- In general, composition can produce new redundant rules; normal form is the minimal map with no redundant entries.

- **Why bother?**  
  Precomputing `A fby B` can reduce evaluation cost when the composite is applied to many names. In practice, heuristics are required to balance construction and GC overheads against lookup savings.

## Types and Annotations

The AST structure has built-in support for annotations, also expressed as ASTs. Annotations should not influence formal behavior (i.e. a compiler can safely ignore them), but are intended to guide external tools for instrumentation, optimization, verification, projections or debug views, etc..

Among those many potential cases, it is feasible to express types for namespaces and the programs they express. The glas system doesn't specify a type system - the intention is to support gradual typing. Similarly, annotations could guide theorem provers, proof-carrying code. Requesting automatic application of such tools is the domain of the user configuration.

By convention, annotations are expressed as primitive constructors in '%an.\*', e.g. `(%an.type TypeDesc)` to describe types, or `(%an.accel Accelerator)` to represent substitution of slow-code with a built-in. Annotations are frequently applied to subprograms or blocks, but may be inserted like commands within a program sequence, e.g. apply `(%an.assert Cond)` to `%pass`. 

Order of annotations may matter, e.g. profiling within memoization vs. memoization within profiling may significantly impact the output. Annotations may also have influence beyond immediate scope, e.g. a type annotation here may influence type inference there.

A proposed set of annotations are described in the [glas program model](GlasProg.md) document.

## Loading Files

An essential operation for modularity is to load files:

        (%load Env File)

Load will download a file, select a front-end compiler from Env, compile to an `Env -> Env` AST, apply, then return the resulting Env. Any errors or quota failures will be reported, though we may reduce to a compile-time warning followed by a runtime error when a name is used, contingent on configuration or annotations.

To support relative file paths, %src is carried in the environment as the current location. Load wraps Env to bind %src to the loaded file’s location before applying the compiled AST. Users (or a user-defined front-end compiler) may capture %src into other definitions as a capability to load modules relative to other locations.

To support location-independent compilation, data in %src is abstract. To support debugging, logging annotations recieve an algebraic effects handler to read %src. To support projectional editing, the runtime provides API 'sys.refl.src.\*'.

The File argument should use a little DSL of '%file.\*' constructors, such as `(%file.rel d:"foo.glas")`. I haven't fully developed these yet, but it is feasible to support virtual files or fallbacks. DVCS and authorization will need careful attention. TBD.

File paths may include wildcards, e.g. "foo.\*". In this case, we can load every matching file then union the generated environments (per the `u:[List of Env]` AST structure).

### Folders as Packages

To simplify refactoring and sharing of code, glas enforces structural restrictions between file dependencies:

  - The user configuration is implicitly loaded by absolute path.
  - Absolute file paths may only be used if %src is absolute path.
  - Anyone can load arbitrary files from DVCS via full references.
  - DVCS files may load within the repository by relative path.
  - Parent-relative file paths, i.e. "../", are forbidden.

Absolute file paths are useful for the case where a user configuration references a few projects on the local hard drive. Most cases should be remotely loading from DVCS, or DVCS files loading relative paths within a repository.

The restriction on "../" paths ensures the contents of every folder are location-independent. This makes it easy to copy and share or edit a folder.

Folders may also be treated as files directly. A folder load desugars to loading all files "package.\*" within that folder.

## Incremental Compilation

Lazy evaluation can simplify tracking of dependencies: for each thunk, we can track its dependents. Each thunk may double as a persistent memo cell and reference many other cells.

For persistence, we must assign stable names to these thunks. In general, this could be a secure hash of everything potentially contributing to a given computation, e.g. code, arguments, perhaps compiler version (e.g. for built-ins). Unfortunately, it's easy to accidentally depend on irrelevant things, or to miss some implicit dependencies. To mitigate this, we must enable programmers to annotate code with a proposed stable-name generator.

Whether we persist the *value* of a thunk may be heuristic, e.g. based on the relative size of that value and the estimated cost to recompute it. It's best to store small values with big compute costs, naturally. Like 42. For large values that are cheaply regenerated, we might omit the data and track some proxy for change - hash of data, ETAG, mtime for files, etc. Aside from this, we would track the set of dependent thunks that must be invalidated.

## Motivation for Composite Names

The special rule for "/" in translation of names is intended primarily for access control, targeted at the program model's support for *register* environments, which are expressed as:

        (%local b:("r.", ProgramAST))

Logically, this allocates an infinite environment of registers. In practice, the system analyzes recursive program definitions to guarantee allocation is finite, then allocates the finite block of registers on a call stack.

Further, the system logically allocates a register for every path between base registers. For example, if "a" and "b" name distinct registers, then "a/b", "b/a", "a/a", and "a/a/a" name four more registers. The translation rule is designed to preserve these paths.

This supports abstract data environments as a reference-free alternative to abstract data types. Example:

        sys.file.open(Filename) : [loc] ()
            # loc is a caller-provided register

*Aside:* This informal notation describes that 'sys.file.open' pops Filename off the data stack, uses 'loc' from the caller's environment, then pushes nothing onto the stack.

Assume 'loc' is bound to prefix '$' within 'sys.file.open'. Instead of writing a file-descriptor to `"$loc"`, we can use `"$loc/filesys.fd"` where `"filesys.fd"` is inaccessible outside the runtime. This lets a runtime place state inside the client’s environment without exposing register names, ensuring allocations remain private.

We may discover other use-cases for "/", but this is the motivating one!

*Note:* See prior section on *Translation* for mechanical details.

## Aggregation

Language features such as multimethods, typeclasses, declaring HTTP routes, etc. don't neatly fit the functional paradigm. Conventional implementations involve construction of global tables. However, I propose an approach that is feasible in glas namespaces.

Assuming support from the front-end syntax, it isn't difficult to repeatedly 'update' a definition, each update shadowing prior definition. By carefully applying a fixpoint, we can feed the final table back into the top of the module.  

We can Church-encode a list of ASTs. Instead of 'wrapping' a prior definition with some functional code each update, we can build the list of ASTs for later processing. This might be expressed as `table <= Expr` adding an AST to the list.

Aggregation can be expanded across multiple modules or application components. However, this pattern is extremely hostile to lazy loading, thus scope must be carefully controlled at larger scales. Aggregation within composite applications is relatively friendly, given that we load the full application regardless.

Safety becomes a greater concern as we cross module boundaries and integrate multiple front-end compilers. This can be mitigated by centralizing shared logic into shared libraries, e.g. developing smart constructors for AST nodes and abstracting the 'table' representation. Type annotations may also help.

Incremental computing also becomes a concern at larger scales. We'll want to arrange for cache barriers after significant 'reducer' operations to block unnecessary propagation of changes.
