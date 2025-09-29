# Interactive Development 

Extending a read-eval-print loop (REPL) to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). In my vision for glas systems, this sort of interactive development experience should be the default. Even for applications that present a conventional front-end, we might provide access to the notebook view.

In the notebook metaphor, a module might represent a page or chapter or widget. Pages could be inlined or hyperlinked into a view. Ideally, we can automatically construct a table of contents, or mark some content for latent access as an appendix. Not every syntax is suitable for presenting source alongside runtime output. However, even a ".txt" file benefits from a good editable projection, and a larger notebook page could let users immediately observe the outcome of changing the text.

## Implementation Thoughts

### Source Setters

We can use the abstract Src type at runtime to support setters. 

### Auxilliary Output

Tables of contents, table of illustrations or interactions, navigation support (e.g. 'prev' and 'next' buttons), etc.. 

Use namespace Aggregation patterns for these.

### Cooperative Work

Consider sophisticated Src setters that support cooperative development? Probably a distant future feature.

### Avoiding Bloat

Move most projectional editor logic into shared libraries. Don't generate this logic per module compiled.


