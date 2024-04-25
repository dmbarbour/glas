# Glas Configuration Language?

I want a lightweight syntax for configuration of glas systems. Earlier, I developed [text tree](TextTree.md) for this role, but it lacks support for multiple inheritance and backtracking, which I think would be useful for a good configuration language.

Desiderata:

* avoids punctuation and escape characters
* supports text, list, and dict data types
* support inheritance, overrides, mixins
* abstraction over data values and configs
* lightweight guarantee of termination

It seems feasible to leverage [namespaces](GlasProgNamespaces.md) as an intermediate representation for a final dictionary. Each name in the namespace could represent a text, list, or dict. Expressions could be very limited, perhaps allowing filters and relational joins but avoiding general loops. The system can express some data used in computing the configuration as a mixin.


