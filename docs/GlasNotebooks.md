# Interactive Development 

Extending a read-eval-print loop (REPL) to support graphical inputs and outputs, live coding, and fancy comments essentially results in a [notebook interface](https://en.wikipedia.org/wiki/Notebook_interface). In my vision for glas systems, this sort of interactive development experience should be the default. Even for applications that present a conventional front-end, we might provide access to the notebook view.

In the notebook metaphor, a module might represent a page or chapter or widget. Pages could be inlined or hyperlinked into a view. Ideally, we can automatically construct a table of contents, or mark some content for latent access as an appendix. Not every syntax is suitable for presenting source alongside runtime output. However, even a ".txt" file benefits from a good editable projection, and a larger notebook page could let users immediately observe the outcome of changing the text.

## Implementation Thoughts

### Source Setters

How shall we route proposed updates back to original source files? In context of user-defined syntax and ".zip" files, this must be abstracted and aligned with 'ns.read'. But it is feasible for a compiler to systematically support '@src.set(FilePath, Data)' or similar. The intermediate '@src.set' would rewrite SrcRef then pass each request onwards. A rooted '@src.set' will instead engage the filesystem or network.

### Auxilliary Output

Tables of contents, table of illustrations or interactions, navigation support (e.g. 'prev' and 'next' buttons), etc.. 

### Cooperative Work

Use of '@src.set' may be a little simplistic in context of communities, concurrent users, and DVCS. We could extend this API with functions to support cooperative work, e.g. tracking attention, proposed edits, comments, curation, pull requests. But what should this API look like? '@src.op(MethodName, SrcRef, Args)' or similar?

### Avoiding Bloat

We'll need to rely heavily on shared libraries for the editable projections. It's probably also a good idea to bottleneck the source setter/getter/etc. so we can easily modify source references and further delegate without a lot of extra code in the namespace.

Ideally, the notebook is also designed such that unused features are subject to lazy loading and dead-code elimination.

### Extension to AR or VR

We can move from 2D into 3D with augmented reality (AR) or virtual reality (VR) devices. Use of AR would require some extra index binding views to visual fingerprints. What exactly would this entail? We'll probably need to extend '.gui' to 3D, or use some form of VRML via 'http'?


