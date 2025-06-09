# Graphical User Interface for Glas

It is feasible to support a conventional GUI interface, but that isn't a good fit for my vision of glas systems. My big idea is *users participate in transactions*. 

## Transactional User Interaction

It isn't easy for human users to participate in transactions! The biggest problems are that humans are slow to respond, slow transactions are disrupted by background events, disrupted transactions are repeated, and humans also don't like repeating themselves. 

To solve this, we introduce a user agent to handle fast response and repetition. But the user must be provided tools to see what the user agent sees, such as data and queries, and control how the user agent responds to queries on the user's behalf. 

This involves *reflection* on the user agent, together with manipulation of user variables. Reflection allows users to observe aborted transactions. This provides a basis for read-only views or to withhold approval until the user has time to understand the information on display. User variables might be rendered as knobs, sliders, toggles, and text boxes.

Reasonable modes for user participation:

* *read-only view* - never commits - The user agent continuously or infrequently renders the GUI then aborts. Not *necessarily* read-only in context of reflection APIs, but should be safe and cacheable like HTTP GET.
* *live action* - always commits - The user agent continuously renders the GUI and commits when possible.
* *approved action* - controlled commit - The transaction is aborted unless the user explicitly submits or commits, in some way that is clearly under control of the user. The GUI system tracks user observations and may present a summary of relevant changes for approval in proximity to a submit button. To account for continuous background updates, a user agent may track tolerances for 'irrelevant' changes based on user policy, user action, and app recommendations.

The *approved action* mode gives users the most stake in each transaction. Approving a summary of relevant changes even simulates a read-write conflict analysis. However, it's slow and deliberate, not suitable for every context. The *live action* mode is close to [immediate mode GUI](https://en.wikipedia.org/wiki/Immediate_mode_GUI) and can be used for almost any conventional GUI design, while the *read-only view* is suitable for maintaining user awareness. 

In context of *live action* mode, we may need to buffer or latch user inputs. For example, pushing a button sets a 'button-pushed' variable to 'true' until it is read and reset. The button would continue to render in a depressed state while 'button-pushed' remains true.

### Mitigating Glitches

If users observe all transactions in which a user agent participates, they will certainly observe some transactions that are ultimately aborted due to concurrent read-write conflicts. A subset of these may exhibit 'glitches' where rendered values are inconsistent (e.g. due to reading cached values from multiple remote systems). 

A transactional GUI system can easily skip rendering of transactions that might be inconsistent, but there is a small cost to latency (to wait for consistency checks) and a small to large cost to frame-rate (because skipping bad 'frames' due to inconistency) depending on level of concurrent interference. This can be mitigated through app design (buffers and queues, history for views) or runtime support (rendering older snapshots for read-only views, precise conflict analysis).

Alternatively, we can modify applications to reduce severity of known glitches. This would be closer to convention with non-transactional GUI today.

An adjacent issue is that *asynchronous* interactions - where feedback is not logically 'instantaneous' within a transaction - may appear to be glitchy if presented as synchronous to the user. In this case, I think the problem is more about managing user expectations (e.g. report actual status of pending requests) or meeting them (e.g. use RPC to complete actions synchronously in GUI transaction).

## Integration

        gui : FrameRef? -> [user, system] unit

An application's 'gui' method is repeatedly called in separate transactions. On each call, it queries the user agent and renders some outputs. 

In general, the queries and rendered outputs may be stable, subject to incremental computing. However, some 'frames' may be less stable than others. To support these cases (TBD)

In some cases, we may 'fork' the GUI with non-deterministic choice, which a user agent might render in terms of multiple windows. We render without commit; the final 'commit' decision is left to the user through the user agent.

A user agent can help users systematically explore different outcomes. This involves heuristically maintaining history, checkpoints and bookmarks based on which values are observed. An application can help, perhaps suggesting alternatives to a query or using naming conventions to guide heuristics (e.g. distinguishing navigation and control).

*Note:* It is feasible to introduce a notion of user-local state or session-local state. However, it is not clear to me how such state would be integrated between sessions, other than as queries. A few exceptions include passing files and such over the GUI, e.g. drag and drop, which may require special attention.

## Navigation Variables

UserAgents might broadly distinguish a few 'roles' for variables. Navigation variables would serve a role similar to HTTP URLs, with the user agent maintaining a history and providing a 'back' button. Writing to navigation variables would essentially represent navigating to a new location upon commit, albeit limited to the same 'gui' interface.

Other potential roles would be inventory or equipment, influencing how the user interacts with the world. In any case, I think we could and should develop a more coherent metaphor than clipboards and cookies. 

## Rendering Temporal Media and Large Objects

An application may ask a user agent to 'render' a video for user consumption. As a participant in the transaction, a user should have the tools to comprehend this video before committing to anything. 

One of the best ways to understand a video is to play it. Of course, other very useful tools would include the ability to search it (find people or particular objects), read dialogues, present video frames side by side, apply filters, slow motion, fast forward, reverse, etc.. Ideally, the GUI system provides a whole gamut of tools that can be applied to any video.

The same idea should apply to any large 'object' presented to the user within a transaction. For example, if the user agent is asked to render an entire 'database' as a value the user should have suitable tools to browse, query, and graph database values to obtain some comprehension of them. Rendering of very large objects is feasible between content-addressed references and content distribution networks.

Ideally, user agents are extensible such that, if they lack the necessary tools, users can easily download the tools they need. We could develop some conventions for recommending certain tools to understand a large object. Further, an application can also support users in understanding large objects.

## Non-deterministic Choice and GUI

For isolated transactions, repetition is equivalent to replication. Fair non-deterministic choice can be replicated to evaluate both forks in parallel. Assuming the transactions do not have a read-write conflict, they can both commit. This optimization is leveraged for task-based concurrency of transaction loops.

This will impact GUI. If an application makes a non-deterministic choice, it will potentially affect what is rendered to the user. Assuming the user agent is aware of the choice, this could be rendered using multiple frames (tabs, windows, etc.) or more adventurously rendered as an overlay or branching conversation. 

Ideally, the user should have some control over the non-deterministic choice. This allows a *read-only view* to focus on frames that receive user attention, and *approved action* to efficiently approve a specific branch instead of waiting for it to cycle around. 

This can be understood as a form of participation: users can ignore and abort forks that aren't of interest to them, or explore the options in a controlled manner instead of randomly. Control over non-deterministic choice must be integrated with both the runtime and distributed transactions. Fortunately, this is a feature we'll also want for many other reasons: scheduling transactions, debugging, loop fusion, etc.. 

## Multi-User Transactions

The API directly supports multi-user systems where each user is performing independent transactions. That should be sufficient for most use cases. However, what if we want a 'multi-user transaction' in the sense of multiple users participating in one transaction?

To support a multi-user transaction, we could model a 'multi-user agent'. If the users do not share a physical room, the multi-user agent could be placed into a virtual room created for the task. If the application is not multi-user aware, we could use a normal user agent and the virtual room could instead implement handoff protocols. 
