# g0 Syntax

The bootstrap syntax for Glas is g0, with the eponymous file extension `.g0`. This syntax is structured UTF-8 text to work well with conventional development environments. Design goals include: simple, unambiguous, easy to parse, obvious compilation, and refactors redundant expression. 

Some desiderata: 

* namespaces for easy reuse of subprograms
* local vars to automate data plumbing within subprogram
* can selectively export definitions or a value
* mutual tail recursive groups as an expression for loops
* static eval and lightweight metaprogramming

Static eval in the syntax mitigates need for an eval operator within the Glas program model. The g0 language module will need to include a Glas interpreter, but this simplifies metaprogramming because we can use programs to manipulate programs. 




## Namespace

The g0 syntax enables programmers to define functions and data. By default, a g0 module will output a value that concretely represents the namespace at the end of the file. 


By default, a g0 module will output a value that concretely represents a namespace. 

This record will consist of `symbol:type:Rep`


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


