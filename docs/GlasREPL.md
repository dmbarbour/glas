# REPL-like Experience in Glas

A read-eval-print loop (REPL) is a simple and effective tool for interactive development and debugging. I want early access to REPL-like experience for development in glas systems. However, interaction with live coding, user-defined syntax, and staging requires attention. 

Regarding user-defined syntax, I assume we can develop or modify languages to support REPL sessions. REPLs shouldn't be limited to text-based languages, i.e. we might have REPLs for graphical languages. Symmetrically, we might wish to 'print' graphics, graphs, and tables, or even interactive widgets to influence application state. 

Anyhow, we can assume a REPL session compiles to an application that provides suitable interfaces for integration with REPL tools. For example, the application could define 'repl.http' or 'repl.gui'. The dedicated 'repl.\*' component is convenient in context of dead-code elimination and lazy evaluation, allowing a REPL session to be efficiently imported as a regular module. An 'http' interface can supports graphical output, interaction, and also content negotiation in case we're restricted to plain text.

The REPL environment should poll 'repl.http' or 'repl.gui' to maintain a view. By doing so, output would be responsive to changes in code or state. There is a relevant challenge in updating views without breaking user focus or ongoing edits. But this isn't a new problem. We could use the React framework, CSS, etc. and let a browser manage polling via XMLHttpRequest.

The REPL session compiler will directly capture source code, i.e. `text:FileBinary`, to support its views and future edits. This allows the application to render a read-only view of the session even when detached from the original compilation environment. To improve stability over edits or support progressive disclosure, the application may preprocess the binary up such that rendering the full source requires multiple 'repl.http' requests. Capturing the source introduces an opportunity to 'compose' REPL sessions in carefully designed ways, such as one session logically extending or including another.

To support editing, the application also captures a minimal reference to its build environment: the abstract file location used when loading the source file. The REPL environment can also provide an extended effects API, perhaps `sys.repl.file.*`, to let the application write patches, track failed or pending patches, and detect read-only files (or detached state) so we can distinctly render read-only code.

*Aside:* Lifting REPL to a graphical environment and extension with live coding results in a qualitatively distinct look and feel compared to conventional console-based REPLs. It's close to a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface).
