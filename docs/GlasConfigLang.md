# Glas Configuration Language?

I want a lightweight syntax for configuration of glas systems. Earlier, I developed [text tree](TextTree.md) for this role, but it lacks support for many features I want from a good configuration system.

## Desiderata

* clean syntax
  * avoids punctuation and escape characters
  * minimal boiler-plate for common use case
* support for inheritance from other files
  * both local files and remote git etc.
  * flexible composition and overrides
* effective for configuring many things
  * global module namespace (with its own overrides)
  * RPC registries (composition, tags and filters)
  * runtime database, possibly distributed
  * ad-hoc configuration vars for apps
* limited abstraction
  * mixins and perhaps simple functions
  * rules for specific apps or runtimes 
  * can comprehend and control variation
  * lightweight guarantee of termination

## Structure

I propose that a configuration compile to a record of [namespaces](GlasNamespaces.md), similar to the ".g" programs. This is convenient both for consistency and for abstraction of locations. In case of `from Location import ...` we can potentially abstract Location as a reference to a namespace or hierarchical namespace component.

But compared to the primary glas language, configurations will have a more limited computation language. This might be expressed as an [abstract assembly](AbstractAssembly.md), even if we don't immediately take advantage of the abstraction.

## Locations

The configuration language layer deal with filesystem and network locations. Locations are used in many cases: import of configuration files, reference to global modules, database persistence, etc..

I propose to model locations as an abstract data type. We won't manipulate locations as strings or as structured namespace components. Instead, locations are manipulated through a handful of abstract AST constructors.









Locations are ne includes for import of other configuration files. 

The simplest location is a file path. A more sophisticated location might name a remote DVCS repository, including access tokens and version tags, together with a relative file path. 

Although it is feasible to model location as a structured namespace component, I think it would be more convenient to model locations as an abstract data type. This might involve explicit support from the abstract assembly layer.
It would probably be convenient if a 'location' is modeled as an abstract data type - a feature supported by the abstract assembly - instead of modeling a location as a st


* composing texts and including named values within a text
* 'nameof(name)' expands to a name's text representation (after rename)
* distinguish whether a name is a text, a number, a dict, or undefined
* simple conditional expressions
* possible arithmetic on texts that represent decimal numbers. 
* uncertain: support for lists, looping over lists, list names in namespace

A runtime system and application could contribute some ad-hoc variables prior to computing some parts of the configuration, but not for others.

I think this is quite doable. 

