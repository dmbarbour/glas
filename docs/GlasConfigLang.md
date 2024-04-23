# Glas Configuration Language?

I want a lightweight syntax for configuration of glas systems. Earlier, I developed [text tree](TextTree.md) for this role, but it lacks support for multiple inheritance and backtracking, which I think would be useful for a good configuration language.

Desiderata:

* avoids punctuation and escape characters
* supports text, list, and dict data types
* support inheritance, overrides, mixins
* abstraction over data values and configs
* lightweight guarantee of termination

It seems feasible to leverage [namespaces](GlasProgNamespaces.md) as an intermediate representation for the final dictionary. Each item in the namespace could be a text, list, or dict. A system could provide some data as a mixin to compute the final configuration. A namespace does risk accidental expression of dependency cycles, but those should be easy to detect.

## Namespace or Library of Namespaces?

The toplevel of a configuration could either be 'within' a namespace, or it could be a library of namespaces. 



We could place the configuration namespace at the toplevel of a file, such that 'importing' a file is equivalent inheritance (ns) or even directly to applying a mixin (mx). Alternatively, we can 


