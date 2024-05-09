# Glas Configuration Language?

I want a lightweight syntax for configuration of glas systems. Earlier, I developed [text tree](TextTree.md) for this role, but it lacks support for multiple inheritance and backtracking, which I think would be useful for a good configuration language.

Desiderata:

* avoids punctuation and escape characters
* supports structured data for configuration
* allows participation from app and runtime
* inheritance, overrides, mixins, abstraction
* lightweight guarantee of termination

It seems feasible to leverage [namespaces](GlasNamespaces.md) as an intermediate representation for a final dictionary. A few conventions around the namespace itself could provide the structure we need while individual names may evaluate to text data based on a simple computation language. 

The computation language could include:

* composing texts and including named values within a text
* 'nameof(name)' expands to a name's text representation (after rename)
* distinguish whether a name is a text, a number, a dict, or undefined
* simple conditional expressions
* possible arithmetic on texts that represent decimal numbers. 
* uncertain: support for lists, looping over lists, list names in namespace

A runtime system and application could contribute some ad-hoc variables prior to computing some parts of the configuration, but not for others.

I think this is quite doable. 

