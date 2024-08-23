# Program Model for Glas

The intermediate language for glas systems is structured as a [namespace](GlasNamespaces.md) with definitions written in [abstract assembly](AbstractAssembly.md), but an essential remaining detail is the specific set of `%*` AST constructors recognized by a glas runtime. This document focuses on that final facet.

Like everything else, the program model must be designed to support my vision for glas systems. For example, we describe in namespaces how careful support from the definition layer enables *logical overlays*, resulting in a more flexible system. Potential support for user-defined AST constructors also deserves attention.

## Design Considerations

### Eval to Abstract AST

As a general rule, AST constructors should truly 'evaluate' to an abstract AST under the hood, as opposed to processing the abstract assembly directly. This lets us directly evaluate user-defined AST constructors. We can potentially drop more duplicate definitions by eliminating after evaluation.

We can still support macro-like calls through virtual tables depending on exactly how we specify our AST constructors, but 

### Calls via Virtual Table

With an exception for AST constructors, we might require most 'calls' to methods be indirect through a Localization to support logical overlays, as described for namespaces. 

###






 case of multiple definitions, we might further reduce the 'set' of duplicate definitions (e.g. in context of macr) because we're comparing the evaluated AST nodes instead of the abstract assembly.
* 


