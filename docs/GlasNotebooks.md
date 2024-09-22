# Interactive Development 

Extending a read-eval-print loop (REPL) session to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). If a live coding notebook is purely functional, no effects, then it's essentially a spreadsheet. But if the notebook or spreadsheet defines a transactional 'step' method that we run in a background loop, it can also serve as a [glas application](GlasApps.md).

I want this REPL-like development experience to be the default in glas systems. I believe notebooks also provide a good basis for applications in general. Especially if we can easily specify a skin, a primary view and interaction surface for end-users.

Conventionally, a spreadsheet or REPL session is a file that we load into a suitable application. This application will interpret and render the file, and may edit the file based on user action. Unfortunately, this is awkward in context of user-defined syntax, live coding, and composition. 

A viable solution is to compile spreadsheets and REPL sessions into a more generic and composable object type. This object would provide 'http' or 'gui' interfaces for rendering and user input. With access to the filesystem, the object could update the spreadsheet or session file based on user action. A REPL session isn't necessarily a pure function - it might start some background threads, which we might represent with a 'step' method. Essentially, a spreadsheet or REPL session can be compiled into an applications, then we compose applications based on their interfaces.

Although not all syntaxes are suitable for an interactive development view that mixes sources and live outputs, most can at least support projectional editing. In theory, *every* user-defined language in glas systems could automatically define an 'http' and 'gui' view oriented around live coding of the source code. And these projectional editors could optionally be covered with a skin or composed into a larger notebook.

Ideally, we can separate applications from their development environment. Our compiler should capture source code at compile-time. With this, we can render a read-only view of a notebook even detached from original filesystem. Also, our compiler should avoid entangling things, such that the notebook view and integrated development environment is easily erased via dead-code elimination. This would let us use notebooks as conventional modules, extracting only a few definitions we need.

## Composition

It is feasible to compose interactive development sessions or notebooks in various ways in accordance with the metaphor: we could continue a prior session, include pages or link chapters into a notebook, or merely import some definitions. Ideally, we'd also automatically build a table of contents and some useful indices. 

To support concise composition, we should extend our program values with a little metadata, e.g. `g:(ns:ProgramNamespace, app:(http, step, toc))` to let the compilers easily identify which resources are available for composition. The 'app' metadata wouldn't be necessary for 'import' (which pushes most integration effort to the user) but may be needed for 'include'.

## Indices

It is feasible to automatically index a large notebook or REPL session, whether it's presented as multiple 'web pages' or a single page with anchors. But extracting and composing indices from HTTP responses is awkward and expensive, e.g. numbering and indentation is contextual. So, it might be useful to introduce conventions for intermediate methods like 'toc' as a composable value for building a table of contents.

A reverse lookup index, e.g. find all mentions of 'foo', is also feasible with adequate compiler support. Though, working with stateful outputs may require careful attention, and we'll need an efficient way to compose indices. We could perhaps define 'rlu'

## Skin

It is possible for developers to manually override http and gui. However, unless we intend to fully replace the notebook view, it will be difficult to ensure consistent access to the underlying notebook view across applications.

An alternative is let application developers describe a skin for an application with a little built-in syntax, then the compiler integrates the skin into the definitions of http and gui, where it serves as the initial landing page or interaction surface. For normal end users, the skin might be their entire application. However, a compiler could provide consistent mechanisms for programmers to peek and poke under the hood.

Of course, if we aren't careful we might still introduce inconsistencies between user-defined languages. This could be mitigated by most languages sharing code for the final integration step.
