# Graphical User Interface for Glas

It is feasible to support a conventional GUI interface, but that isn't a good fit for my vision of glas systems. My big idea is *users participate in transactions*. 

## Transactional User Interaction

It isn't easy for human users to participate in transactions! The biggest problems are that humans are slow to respond, slow transactions are disrupted by background events, disrupted transactions are repeated, and humans also don't like repeating themselves. 

To solve this, we introduce a user agent to handle fast response and repetition. But the user must be provided tools to see what the user agent sees and control how the user agent responds on their behalf. 

This involves *reflection* on the user agent, together with manipulation of user variables. Reflection allows users to observe aborted transactions. This provides a basis for read-only views or to withhold approval until the user has time to understand the information on display. User variables might be rendered as knobs, sliders, toggles, and text boxes.

Some possible modes for user participation:

* *read-only* - The user agent continuously or infrequently renders the GUI then aborts.
* *live action* - The user agent continuously renders the GUI and commits when possible.
* *approved action* - The transaction is aborted unless the user explicitly submits. The GUI system tracks user observations and presents a summary of relevant changes for approval in proximity to a submit button. 

The *approved action* mode gives users the most stake in each transaction. Approving a summary of relevant changes even simulates a read-write conflict analysis. However, it's slow and deliberate, not suitable for every context. The *live action* mode is close to [immediate mode GUI](https://en.wikipedia.org/wiki/Immediate_mode_GUI), while the *read-only* mode is suitable for maintaining user awareness.

In context of *live action* mode, we may need to buffer or latch user inputs. For example, pushing a button sets a 'button-pushed' variable to 'true' until it is read and reset. The button would continue to render in a depressed state while 'button-pushed' remains true.

## Proposed API

The proposed API:

        gui : UserAgent -> ()

The 'gui' method calls back to the user both to render data and to ask for information including navigation variables, window sizes, feature support, preferences, etc. relevant to constructing a view. The application may write user variables, too, though this might be presented to the user as a recommendation. 

Under premise of user participation in transactions, we render aborted transactions. Further, we leverage aborted transactions as a basis for 'read-only' GUI views. Exactly what is rendered is left to the user agent, i.e. access to failed hierarchical transactions might only be visible in a debug view. We can render what-if scenarios by performing some other operations before rendering the GUI in an aborted transaction. 

*Aside:* The proposed API also aims to localize input validation and avoid construction or parsing of large intermediate values. The user agent can directly operate on its internal abstract scene graph.

### Integration

We can potentially find the 'gui' in many locations: the application toplevel, hierarchical application components, or RPC objects published to the registry.

When defined at the application toplevel, the GUI might be directly rendered by the runtime, i.e. the runtime provides a built-in user agent. The application can potentially manipulate user agent state and configuration options through a system API. 

When defined in application components, the GUI would only be accessible in context of live coding or debug views. However, these interface would also be convenient for hierarchical composition of the toplevel application GUI.

When defined on RPC objects, we can easily browse through the available objects and render them. Or we could compose those objects into another application's GUI, similar to normal application components.

### GUI to Web Application Adapter

We can UserAgent interface that supports only a subset of features easily implemented on the web application stack. We can leverage XMLHttpRequest to model transactions between a browser and an application. And it is difficult, but not impossible, to compile some part of the 'gui' method into JavaScript that runs on the browser. Further, we could specialize the JavaScript based on stable navigation variables, including URL.

I think this would be a very effective option for integrating GUI. But actually developing the compiler won't be trivial. 

## Navigation and History

Some applications may read 'navigation' variables similar to URLs or query strings. These are just normal user variables, except that by annotating them as navigation variables we can effectively inform the user agent to track them for purpose of history, backtracking, or opening multiple tabs or windows.

## Rendering Temporal Media

An application may ask a user agent to 'render' a video for user consumption. 

Returning to the big idea of user participation in transactions, the user should have the tools to comprehend this video before committing to anything. Such tools certainly include the option to 'play' the video. But users could also speed it up, slow it down, run in reverse, run a short segment in a loop, jump around, search based on images or contained dialog, and so on.

The application could modify user variables to begin playing the application on behalf of the user. However, like any other manipulation of user variables, this might be presented to the user as a recommendation.

Intriguingly, temporal media could be downloaded and buffered as needed via the content addressed storage layer. 

## Mitigating Glitches

If users observe all transactions in which a user agent participates, they will certainly observe many transactions that are ultimately aborted due to concurrent read-write conflicts. A subset of these may exhibit 'glitches' where rendered values are inconsistent.

The GUI system can easily skip rendering of transactions that might be inconsistent, but there is a potential cost to frame-rate depending on level of concurrent interference. To mitigate glitches without reducing frame rates may require support from applications or the glas runtime system. Some possibilities:

* redesign apps to reduce conflict: use more queues!
* systematic support for 'snapshots' of system state
* precise conflict analysis to distinguish 'glitches'

In practice, I expect most glitches will be subtle and short-lived. Users often won't even notice. Where users do notice, it will often be easy to solve the problem at the app layer. Snapshots would further reduce the barrier.

An adjacent issue is that asynchronous interactions may appear glitchy due to time and transactions between user action and feedback. One viable solution is to manage user expectations in the GUI, e.g. report the status of prior requests, make it clear when a user action is still being processed. Alternatively, if it doesn't introduce too much contention, we could make user actions more direct and synchronous. 

## Multi-Frame GUI

For an isolated transaction, repetition is equivalent to replication. A fair non-deterministic choice can be replicated to take both routes. We can leverage this to render multiple independent frames or windows from a single 'gui' request. 

However, it would be best to make this choice visible to the user agent, such that the GUI system can stabilize and render specific choices based on user attention. This implies we can influence non-deterministic choice through the transaction layer, somewhat similar to an algebraic effect.

Ideally, for *approved action* mode, the user can explore possible outcomes based on feedback, and control which choice is committed in the end. That is, the user also participates in non-deterministic transactions.

## Multi-User Transactions

The API directly supports multi-user systems where each user holds independent GUI connections. A multi-user aware application can coordinate multiple users. This should be sufficient for most use cases. No need to share a *transaction* between users.

But if ever we invent a scenario where a multi-user transaction makes sense, how would we implement it? 

Well, if multiple users shared a room, we could implement a 'multi-user agent' to allow multiple users to share a transaction. It doesn't need to be a physical room: users could share a virtual room. Each user could have independent GUI connections to the shared virtual room, allowing transactional observation and operation of the shared multi-user agent, which ultimately interacts with the remote application.

A virtual room could also support coordination and handoff protocols to share a single-user GUI connection between users. This would roughly correspond to desktop sharing.
