# Graphical User Interface for Glas

In a glas system, dedicated GUI API should systematically take advantage of transactions, the transaction loop, content-addressed storage, and automatic code distribution. I would also like to support HTTP-like features, e.g. support for concurrent users, multiple views, navigation, history, a back button. 

## Transactional GUI

The big idea is for human users to participate in transactions and transaction loops. Consider a few potential modes of participation:

* *read-only views* - Users observe a system without modifying it. This can be modeled by a transaction that we abort after rendering some output to the user. The transaction itself doesn't need to be read-only: it can be useful to 'view' outcomes of an action. Read-only views might further distinguish snapshots (like HTTP GET) and live views (via transaction loop).
* *approved action* - Starting with a read-only view for an effectful transaction, we could add a submit button to commit. To avoid surprises, the GUI might also present a summary of relevant changes for approval upon submit. This would from stability heuristics.
* *live interaction* - User behavior is continuously published through the GUI and implicitly committed when possible. The user receives continuous feedback from the application. Aborted transactions are still rendered, but they might be presented differently to clarify that they were aborted.

It is feasible to mix modes or choose between them procedurally.

Humans cannot directly participate in transaction loops. Instead, users will manipulate an agent to participate on their behalf with attention to transaction internals. This manipulation is stateful, usually focused on variables but it might generalize to live coding. Variables might be presented graphically as knobs, sliders, toggles, buttons, text boxes, and other conventional GUI elements.

## GUI Integration

Potential use cases:

* *main app GUI* - runtime renders any application that implements 'gui'
* *remote GUI* - published RPC objects may define 'gui' for remote access
* *debug GUI* - app components may define 'gui' for live coding or debugging
* *web-app GUI* - compile feature-restricted 'gui' to a web application

In general, rendering a GUI is stateful. It interacts with state on the user agent's end. For the main app GUI, user state would belong to the runtime. It can potentially be accessed through the reflection API, or perhaps via the database API if `sys.refl.gui.state` returns a database key.

The GUI transaction should not be used for background processing. It will often be running as a read-only view, and even *live interactive* GUI may run infrequently or with reduced priority if the application doesn't have user focus.

## Proposed Interface

A proposed application gui interface:

        gui : UserAgent -> ()

Here, UserAgent is an abstract, ephemeral RPC object that provides methods to render content. This allows for partial rendering even in case of a failed transaction. In addition to rendering, the UserAgent provides access user state and information about the display environment (window size, support for multi-media, etc.). A URL or query string would be modeled as user state. The application gui may discriminate on which interfaces UserAgent implements.

One motive for this design is to support fine-grained code distribution. In case of networked operations, fragments of 'gui' might evaluate user-side, and parts of 'UserAgent' might evaluate application side, using fine-grained calls to subprograms.

## Rendering Temporal Media

It is semantically awkward to directly ask a UserAgent to 'play' music or other temporal media in context of repeating or aborted transactions. However, we could ask the UserAgent to 'render' a sound graph. Similarly, we could render a video as a deck of cards or list of frames, or just a box with an initial image and a play button. 

Indirectly, the video could be associated with some user state or tooling that indicates whether the video is playing, and that state could potentially be manipulated through the UserAgent. 

## Forked GUI

An application might make some non-deterministic choices while rendering the GUI. We might present this to the user as multiple windows.

An interesting case is when we fork in the *approved action* modality. We might want to commit only the branch we selected. This might involve saving the fork path and providing means to replay it when the transaction is retried.

## Multi-User GUI

Although multi-user transactions are feasible (with some extra setup), they're a little restrictive - every user has veto power. A better way to model a multi-user GUI is to introduce an intermediate application that serves as a shared room and supports arbitrary collaboration models such as voting, approval tracking, or granting control. 

## User Agent API for OS Apps

## User Agent API for Web Apps

## User Agent API for Console Apps?

It is feasible to develop a user agent API that is suitable for running a GUI in console, leveraging ANSI escape codes and perhaps some terminal graphics extensions (like [kitty](https://sw.kovidgoyal.net/kitty/graphics-protocol/)). 

Not sure this is worth pursuing, though. Maybe as an adapter for console IO.

