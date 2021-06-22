# g0 Syntax

The bootstrap syntax for Glas is g0, with the eponymous file extension `.g0`. This syntax is structured UTF-8 text to work well with conventional development environments. Design goals include: simple, unambiguous, easy to parse, obvious runtime behavior, and refactors redundant expression. 

Viable desiderata: 

* local variables to automate data plumbing where desired
* implicit 'environment' parameter(s), with dynamic scope 
* effective syntax for local references (namespace constants?)
* local general recursion with implicit continuation stack
* tail recursive loops, explicit tail recursion (e.g. goto)
* lightweight namespaces with import and export control
* unambiguous provenance; single inheritance for unqualified imports
* namespace indicates type - `prog`, `data`, `type`, `macro`, etc.
* usage of names depends on its exposed variant type in namespace. 
* support for importing grammars, rules, language extensions.
* static eval for macros, higher-order programming, metaprogramming
* effective syntax for annotations for programs or sequences
* language incorporates named resources based on type and context
* support for regular expressions and grammars

I think we might need to parse g0 to an intermediate AST that is subsequently compiled to Glas programs. The intermediate AST would support variables, recursion groups, etc. locally within the file.

## Local Variables

If g0 syntax makes the stack directly accessible, we can define local variables as taking items from the stack then plumbing them through a program via transform. Alternatively, g0 could fully hide the stack and use keyword parameters and results via records. Transform to a stack-based dataflow would be a compilation step.

Between these two options, I prefer keyword parameters and results. 

We can define local variables as taking a result from the stack, or w

## Namespace

The g0 syntax enables programmers to define functions and data. By default, a g0 module will output a value that concretely represents the namespace at the end of the file. 

## Static Eval

Static eval in the syntax mitigates need for an eval operator within the Glas program model. The g0 language module will need to include a Glas interpreter, but this simplifies metaprogramming because we can use programs to manipulate programs. 

## Rejected Features

My goal with g0 is ease of both use and implementation. Some features I'd like to explore are not good for this:

* yield - requires implicit stack defunctionalization; useful for asynch and coroutines 
* typeful overloading - requires mixed type analysis, function tables, and search; convenient for generic programming.



##


## Data

## Functions

## 




Desiderata:

* data - lightweight data embedding.
* local vars - automatic data plumbing on stack where feasible.
* tail recursion - loops can be expressed as tail-recursive function calls
* pattern matching - lightweight pattern matching for lists, records.
* namespaces - static programs and data, imports

Under consideration:
* staged programming - evaluate some expressions at parse-time; might be too complicated
* yield - automatic defunctionalization of continuations; might be too complicated

a subset of functions can 'yield' a value and defunctionalized continuation. This might be more complicated than I desire (compilation strategy is not obvious).



## Basics






* single parameter and return - g0 functions have 1-to-1 arity by construction.


