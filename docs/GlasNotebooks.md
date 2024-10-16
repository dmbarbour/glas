# Interactive Development 

Extending a read-eval-print loop (REPL) to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). In my vision for glas systems, this interactive development experience should be the default. Even when applications present a conventional front-end, users should be able to peek under the hood to understand system behavior, and potentially modify code and behavior.

Ideally, notebooks are modular, and every glas module is also a notebooks. The syntax for 'import' might select between including pages or linking to them. In context of user-defined front-end syntax, we compose notebooks based on a common intermediate representation: the `g:(ns:Namespace, Metadata)` program value. The namespace includes compiler-provided hooks for rendering a projectional editor, while metadata indicates which hooks are implemented and other hints for compile-time integration.

This has some implications. For example, it's preferable that every module successfully compiles. If a module is full of syntax errors, we still compile to a projectional editor that renders these errors and includes recommendations, and we can still run parts of the application that don't directly reference definitions from this module.





  automatically integrate indices or tables of contents into a composite index or table.

 The program namespace should include hooks to support a projectional editor, table of contents, and other useful features. The metadata should include hints for default composition of modular notebooks upon 'import'. Ideally, this is arranged such that developers can still provide a conventional user interface, control how the notebook view is integrated, and the notebook view is subject to dead-code elimination if ignored.


A notebook view that mixes source and rendered output also provides an effective front-end for many applications. But even where we provide a front-end or skin for regular end-users, we could let users access the notebook view to peek and poke under the hood.

Conventionally, a spreadsheet or REPL session is a file that we load into a suitable application. This application will interpret and render the file, and may edit the file based on user action. However, this is awkward in context of user-defined syntax, live coding, and composition. A viable alternative solution is to compile both spreadsheets and REPL sessions into objects with a shared, composable interface such as 'http' or 'gui', perhaps 'step'. By capturing the abstract source file, this interface may also support live coding.

Spreadsheets, REPL sessions, and notebooks are explicitly designed for interactive development, i.e. rendering a mixed view of editable source and reactive outputs. Unfortunately, not every useful syntax will share this property. However, we can at least implement an integrated live code editor with syntax highlighting. Even a minimal interface can be composed as a page or chapter into a larger notebook. Thus, in glas systems, I propose that every user-defined syntax compiler provides these features. That said, perhaps we can develop a more efficiently composable interface than 'http' and 'gui' in this role.

It should be possible to compile or run notebook applications in detached mode with a read-only view of the notebook, or drop the notebook view where the overhead is too much. This could involve compilation modes or overrides and dead code elimination.

## Composition

It is feasible to compose interactive development sessions or notebooks in various ways in accordance with the metaphor: we could continue a prior session, include pages or link chapters into a notebook, or merely import some definitions. Ideally, we'd also automatically build a table of contents and some useful indices. 

To support concise composition, we should extend our program values with a little metadata, e.g. `g:(ns:ProgramNamespace, app:(http, step, index))` to let the compilers easily identify which resources are available for composition. This would be used when including or linking conent to automatically compose things.

## Indices

For larger notebooks and REPL sessions, it is convenient to automatically index the content. Useful indices include both tables of contents and reverse lookups for keywords or topics. Efficient indexing benefits from composing a structured intermediate representation. Thus, we may introduce a separate, efficiently composable 'index' view within the application. This view might be parameterized by filters, level of detail, and language localization.

## Skin

With a little syntactic support, developers can override compiler provided 'http' and 'gui' definitions. However, manual overrides hinder consistent integration with a notebook view. An alternative is to describe the regular end-user's view then let a compiler automatically integrate this into the notebook view. The compiler might provide the end-user view as a landing page, provide a consistent mechanism to peek at the notebook view under the hood, and perhaps present the end-user view as a window or panel even in the notebook view.

## Extension to AR or VR

The notebook metaphor is useful for literate programming or a graphical, live coded REPL. However, I have also imagined interactive environments that users are immersed within, something feasible only with augmented reality (AR) or virtual reality (VR) devices. In my vision, users would integrate curated content from companies and communities, applying personalized extensions and overrides. Some user edits may propagate back to the community via DVCS or RPC. 

VR should be easier. AR especially has extra challenges for indexing, level-of-detail, and security. We must index on stable visual fingerprints and locations. AFAIK, it is basically impossible to integrate community content into AR without giving away information about your location and view. At best, we could develop private spaces - e.g. within one's home, you only add personalized content and widgets, though the widgets themselves may be portals to a community.

## Alternatives?

Instead of directly defining 'http', 'gui', etc. a compiler may define 

 integrating the notebook with the application's user interface, an application could present hooks for a notebook view then users could route `http` and `gui` to the compiler-provided integration based on some conventions 

 standard way, e.g. und

Can we separate primary compilation from introduction of the projectional editor? This seems feasible in theory. We would include 'hooks' in the compiled application to support any application-specific features of the projection, then the language module could provide a standard 'patch' for the projection.

OTOH, this complicates integration with imported dependencies. 

Might be best to revisit this after I have something working in practice.

