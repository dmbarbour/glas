# REPL in Glas

A read-eval-print loop (REPL) is a simple and effective tool for interactive development and debugging. For glas systems we should consider how to support REPL in context of user-defined syntax, module localizations, and live coding. 

A viable interface:

        # in language module 'lang' namespace for suitable languages
        repl : SourceBinary -> (overlay: List of (near:Position, show:Resource)
                               ,context: Namespace defining 'http' or 'gui'
                               )
        type Resource = Text compatible with HTTP Path and Query
        type Position = Integer byte offset into source 

This 'repl' interface essentially expresses an *overlay* of a glas module, hinting at what to show and where. Resources are presented as HTTP references into a context. This can support interactive views, graphics, structured data, progressive disclosure, and content negotiation. It's left to each language module to decide how much data is encoded in the resource and how much in the context.

Separately, we'll need a REPL display application that can display source together with the overlay. I assume we'll have some other conventions to support syntax highlighting and such. Regarding exactly how the overlay is displayed, there are many options. As a minimum viable product, we might merely print a list of positions together with a plain text representation for each resource. Better options might involve a side panel or opening up some vertical space between lines of code. It is feasible to serve the module system as a wiki of REPL.

Aside from listed resources, the REPL display application might integrate other resources such as "/favicon.ico" or "/", subject to ad-hoc conventions and de-facto standardization. 

Intriguingly, context easily generalizes to a full [application](GlasApps.md), e.g. by defining start, step, switch, settings, and assuming system APIs. This allows for flexible user interaction, background processing, and network access. REPL display might present users with a pause toggle and a restart button where appropriate, defaulting to a paused state.

In context of live coding, performance will depend heavily on memoization. Careful placement of annotations can reduce rework between 'repl' calls and even share work with 'compile' and other language processing.
