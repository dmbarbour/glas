# Glas Configuration Language?

I want a lightweight language for configuration of glas systems. The [*text tree*](TextTree.md) language is my first attempt at this, but it isn't everything I want. It is missing some useful features, expecially support for inheritance and abstraction. 

Desiderata:

* avoids punctuation and escape characters
* support inheritance, overrides, mixins
* lightweight guarantee of termination 
* compiles to extensible structured data 
* indexable or lazy; compute what is needed

I initially wanted to add inheritance to text tree, but *lists* (such as a list of people or search directories) were a stumbling block for this. They're a little too ad-hoc in the text tree syntax. One alternative is to borrow from a configuration language such as [dhall](https://dhall-lang.org/), based on functional programming. But I feel this moves the needle too far towards code. 

Another alternative is to build on a structure of namespaces, consistent with the grammar-logic language, but focused on definition of just a few types: texts, lists, dicts, variants, and perhaps integers. But it isn't clear how much code I'd end up adding to this anyways. Do we want to match variants? Map over lists? Support templates and dotted paths? I'm not sure I can truly escape general purpose code if I want inheritance in general.

For now, I'll use text tree and ad-hoc configuration specific post-processing.

