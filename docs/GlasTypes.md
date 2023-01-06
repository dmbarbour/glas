# Type Annotations for Glas Programs

Glas doesn't have a built-in type system, but supports ad-hoc annotations. So, what sort of annotations would be most useful for static analysis or typechecking? This document explores this a bit and sketches out some preliminary designs.

## Stack Arity

Perhaps the simplest type. Just count the number of stack inputs and stack outputs. Sometimes written as `2 -- 3` to mean 2 stack inputs, 3 stack outputs. As a program annotation:

* *arity:(i:Nat, o:Nat)* - indicate how many values from the data stack are input and output (with outputs counted as if inputs were first removed).

## Value Type Descriptions

To do much more than describe numbers of things, we'll need the ability to describe data and program behavior program types effectively. I'd prefer to have something more declarative and decidable than running an arbitrary program. However, the ability to include some sizes and computations in types is still useful.




## Pre and Post Conditions

This model is very expressive but also very difficult to verify statically. 

Viable annotations:

* *preconds:[List of prog:(do:Program, ...)]* - set of pass/fail programs that should be able to pass before we evaluate the annotated program.
* *postconds:[List of prog:(do:Program, ...)]* - set of pass/fail programs that should be able to pass after we evaluate the annotated program.

Eventually, we might extend precond and postcond to accept descriptions other than 'prog'. The conditions would ideally be verified statically, but in practice it's more likely we'll only check them in debug modes, and further might only spot-check them.

The weakness of conditions like this is that the postconditions cannot depend on the preconditions. Use of *Behavioral Equivalence* is even more expressive.

## Behavioral Equivalence 

We could annotate one program as being behaviorally equivalent to another, or perhaps to a list of other programs. For example, we could specify that a program is equivalent to the same program integrated with a bunch of runtime checks. 

 extra precondition and postcondition checks, in which case the wrapper might carry some state. Or we could say that the program is equivalent to a low-performance reference implementation of the same program.





# OLD STUFF

## Type System

Glas doesn't specify a type system, but a type system is naturally influenced by the data and program models. Ideally, they type system should support partial type annotations, i.e. such that programmers can express just their local intentions without capturing the entire type at one location. 

Types should capture both representation and meaning (e.g. units for numbers).

Representation types can include:

* symbols, enums
* records (open or closed)
* lists and tuples
 * non-homogeneous, grammar patterned lists?
* natural numbers
* other numbers (floating point? complex/vectors?)
* 


All data types are essentially refinements of binary trees. A type system might specialize list, record, and variant types explicitly. Programs will have stack types and effect types, both of which have significant integration with data types.

Glas programs can support abstract types by asserting that certain data is only indirectly observed via external 'eff' handlers. Abstract types can be extended to linear types by also restricting a subprogram's ability to drop or copy the data.

Interaction between data and subprograms can potentially be expressed by tracking 'static' vs. 'dynamic' data types, refinement types, and dependent data types. It is also feasible to support associated types (aka phantom types) based on annotations and some abstract interpretation. For example, a given number might have an associated unit type so we don't accidentally add meters to kilograms.

Glas also supports staged programming. We might desire to analyze whether the staged program will produce a valid program based on reasonable or verifiable assumptions about its inputs. 

Above the program is the application layer. We might want session types or state-machine types to describe asynchronous interactions, to check whether the application is meta-stable or has other nice properties.

Types are a very deep subject. Glas barely touches the surface with static stack arity checks. Everything else must be supported via program annotations and user-defined analysis. For this reason, Glas programs provide a feature for extensible annotations. But

### Overloads and Generics

A popular use of type systems is to automate selection of functions, so we can use the same syntax `a + b` for adding ints vs. floats vs. vectors. 

Glas doesn't have any built-in support for this feature, but it is feasible to build it in at the user-defined syntax layer together with some staged programming. Support for overloads is low priority, left as an exercise for the future.

### User-Defined Analysis

Glas system supports user-defined static analysis. Language modules can analyze local code and the value of loaded dependencies. Where a module doesn't adequately analyze itself, developers can introduce an intermediate module to perform the analysis, or analyze at point of use.

Compilers are also user-defined in Glas - simply a language module whose output is binary. Compilers will often perform static analysis to check assumptions and improve optimizations. 

### Provenance and Error Reporting

Error reporting is a little troublesome in Glas because analysis mostly operates on an intermediate program model instead of original source code.  

This could be mitigated by having language modules add some provenance annotations to program outputs. However, it is awkward to preserve provenance explicitly. An alternative is to design a Glas intepreter that can maintain provenance implicitly.

### Gradual Typing vs. Package Systems

A goal of Glas design is to simplify gradual typing of the codebase and evolution of the type system. Naturally, this results in old modules and applications failing under new analyses.

Those broken modules should be readily discovered and repaired or replaced. This influences design of the package system, e.g. favoring community and company distributions like artifactory or docker instead of a central server like nuget or hackage.

We might identify broken modules and estimate the health of a distribution by identifying which modules fail and which emit 'error' or 'warn' log messages. With this feedback, we could develop a safe approach to gradually integrate breaking changes, or directly fix the broken modules when there are only a few.

## Techniques

### SHErrLoc

The [SHErrLoc project](https://www.cs.cornell.edu/projects/SHErrLoc/) of Cornell is an inspiring approach to static analysis based on producing a constraint set that is subsequently analyzed by a general constraint solver, with some probabilistic inference of blame.

