# Interactive Development 

Extending a read-eval-print loop (REPL) to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). In my vision for glas systems, this interactive development experience should be the default. Even when applications present a conventional front-end, users should be able to peek under the hood to understand system behavior, and potentially modify code and behavior.

Ideally, notebooks are modular, and every glas module is also a notebook. In the notebook metaphor, a module might represent a page or chapter or widget. Pages could be hyperlinked, and we could automatically maintain a table of contents. In context of user-defined syntax, we'll also need a common intermediate representation for composing notebooks. This might be achieved by compiling glas modules to `g:(ns:Namespace, Metadata)` program values, where the compiler adds hooks to the namespace for the notebook view, and adds a little metadata to simplify integration. 

Although not every user-defined syntax will be suitable for mixed source and output, we'll at least want the projectional editor. For example, instead of compiling ".json" files directly to structured data, we might compile to a program value that defines 'data' but also includes hooks for rendering and editing the file. 

In case of syntax errors, we should still compile the notebook view. For example, if a ".json" file has errors, we might render that file with the errors highlighted. In this case, 'data' might diverge or could return a best-effort corrected version based on implicit parameters. Regardless, we should also log warnings or errors at compile time to inform the developer before the application is run.

Notebooks can support a conventional application view. If the developer overrides 'gui' or 'http' they may choose to drop the notebook view or integrate it. If dropped, the unused code for projectional editing is subject to dead code elimination by an optimizer. Consistency of integration across applications and languages deserves some attention but isn't essential. It is also feasible to support separate compilation of notebooks, presenting a read-only view of the relevant sources.

## Compilation Performance

A point of concern is that projectional editors are huge and also very redundant. This can be mitigated using the *Shared Libraries* pattern described in [glas namespaces](GlasNamespaces.md).

## Cooperative Work

In the general case, a file is shared. Even within a single application, a file might be imported multiple times. Each import may have a distinct compilation environment and integration context, resulting in different output being rendered. A file may also be shared between applications and between multiple users.

To mitigate this, we might extend our filesystem APIs to better integrate DVCS and cooperative work - cursors, comments, coordination, and curation - while allowing file paths to remain abstract. Integration can be best-effort, using auxilliary files as needed, assuming it's well documented.

## Composition

It is feasible to compose interactive development sessions or notebooks in various ways in accordance with the metaphor: we could continue a prior session, include pages or link chapters into a notebook, or merely import some definitions. Ideally, we'd also automatically build a table of contents and some useful indices. 

To support concise composition, we should extend our program values with a little metadata, e.g. `g:(ns:ProgramNamespace, app:(http, step), nbi:(toc))` to let the compilers easily identify which resources are available for composition. This would be used when including or linking conent to automatically compose things.

## Indices

For larger notebooks and REPL sessions, it is convenient to automatically index the content. Useful indices include both tables of contents and reverse lookups for keywords or topics. Efficient indexing benefits from composing a structured intermediate representation. Thus, we may introduce a separate, efficiently composable 'index' view within the application. This view might be parameterized by filters, level of detail, and language localization.

## Extension to AR or VR

The notebook metaphor is useful for literate programming or a graphical, live coded REPL. However, I have also imagined interactive environments that users are immersed within, something feasible only with augmented reality (AR) or virtual reality (VR) devices. In my vision, users would integrate curated content from companies and communities, applying personalized extensions and overrides. Some user edits may propagate back to the community via DVCS or RPC. 

VR should be easier. AR especially has extra challenges for indexing, level-of-detail, and security. We must index on stable visual fingerprints and locations. AFAIK, it is basically impossible to integrate community content into AR without giving away information about your location and view. At best, we could develop private spaces - e.g. within one's home, you only add personalized content and widgets, though the widgets themselves may be portals to a community.

## Alternatives?

It might be preferable to separate some notebook features, such as style and layout, from the application itself. Instead, move them to a browser or IDE layer. This is feasible if we develop more standard hooks and intermediate representations, e.g. for the table of contents, indices in general, and other features. We could still provide a default view via 'http' and 'gui', but it might be useful to put all the intermediate hooks into `nbi.*` or similar.

