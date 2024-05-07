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
* access to names, i.e. 'nameof(name)' would expand to the name's text representation after all renames and translations are applied. 
* simple conditions or ternary expressions, i.e. 'if name is "foo"'
* simple loops over regions in the namespace (key-value with a 'key' split?)
* simple loops over elements in a text? we could support line-oriented, tab-oriented, etc.
* simple arithmetic on texts that represent decimal numbers.


