# Interactive Development 

Extending a read-eval-print loop (REPL) to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). In my vision for glas systems, this sort of interactive development experience should be the default. Even for applications that present a conventional front-end, we might provide access to the notebook view.

In the notebook metaphor, a module might represent a page or chapter or widget. Pages could be inlined or hyperlinked into a view. Ideally, we can automatically construct a table of contents, or mark some content for latent access as an appendix. Not every syntax is suitable for presenting source alongside runtime output. However, even a ".txt" file benefits from a good editable projection, and a larger notebook page could let users immediately observe the outcome of changing the text.

## Implementation Thoughts

Editable projections are defined as shared libraries, such that front-end compilers can efficiently reference these projections without reimplementing them.

We'll widely rely on '@\*' compiler dataflow definitions for generating tables of contents, providing 'prev' and 'next' navigation, and perhaps providing setters and getters for source code, abstracting the source locations.

We arrange things such that lazy loading and dead-code elimination can efficiently remove the notebook view if users do not want one.

The notebook application might define 'app.http' and such if the user does not, but could put relevant logic into 'nbi.\*' or similar such that users can easily override the notebook and still integrate it.

Instead of assuming 'ownership' of a file, we might want to support cooperative work by multiple users as the default. Thus, instead of simple 'setters' for source, we might need some notion of 'attention', 'proposed edits', 'comments', 'curation', perhaps even integrating pull requests with DVCS.

## Extension to AR or VR

The notebook metaphor is useful for literate programming or a graphical REPL. But perhaps we can move from 2D into 3D with augmented reality (AR) or virtual reality (VR) devices. 


