
This is currently a mess...

# Proc Model of Programs

The glas 'prog' model for programs prioritizes simplicity, compositionality, and locality at great cost to performance and scalability. I intend to develop another program model that is designed to optimize performance and scalability, especially in context of transaction machine applications. 

Ideally, 'proc' should serve as an effective intermediate language when compiling 'prog', in addition to being something that can be built directly by users, or perhaps even replace 'prog' and be targeted directly when compling language-g0 syntax.

## Thoughts and Issues

### Precise Data Plumbing

Use of a data stack is convenient because it avoids naming locations. But it is feasible to model each program as having named input and outputs (i.e. always return by ref), and intermediate locations. 

### Fine Grained Effects

Instead of a central 'eff' operator and relying too much on partial evaluation, it might be wiser to model an environment of objects that may be invoked with labeled methods. That is, a program would include the ability to 'call' a method on a referenced object. Argument to a call might be a static list of references to other objects (i.e. modeling data registers as objects).

### Incremental Effects

It would be very useful if we know which effects are write-only or read-only for a given computation. Something closer to channels might be a better option as a basis for effects.


### Reject Staging 

Staging at this layer is problematic because it hinders expression of 'eval' on a dynamic program with access to the same effects as a static program. We should only do staging in other layers, such as the language/syntax layer. 

This has some implications for partial evaluation. Partial eval must be based on fine-grained access to runtime parameters, not special static args and staging at this layer.

### Reject Partial Data

One idea that comes up repeatedly is partial data, e.g. the ability to send a list where the first few items are defined and the rest is left open for later definition. However, I'd prefer to avoid this for many reasons. What I'd prefer instead is something like built-in channels. 

### Partial Evaluation

With 'prog' I can already do a fair bit of partial eval based on abstract interpretation. But what can be done to improve this? Without explicit staging or partial data, my options are limited. Fine-grained variables may help. 

### Built-in Channels?

Promoting the channels API to something first-class might be a very good approach to 



### Fine-Grained Parallelism

Kahn Process Networks or Lafont Interaction Nets might make a better basis for parallel evaluation within a computation. Effects could feasibly be represented in terms of communication over channels. Though, it's a bit awkward to transfer access to effects between subprograms. 

OTOH, I could probably get most of these benefits by acceleration of KPN models within a 'prog'. Additionally, I could easily introduce annotations to evaluate pure subprograms in parallel. If combined with loops, it could be flexible.

### Partial Eval in General

It is possible even with 'prog' to perform some ad-hoc partial evaluation via abstract interpretation. It is feasible to make this more explicit in some 



Potential approach:

* Environment of static objects, but may have temp objects per call or loop.
* Each object has private state and fine-grained public methods.
* Effects via providing an initial environment of objects.
* Built-in staging. Methods statically computed based on name and args.
* Operations consist of calling a method with objects as parameters.
* All return data is handled via return parameters.

* 
* More flexible data plumbing - e.g. named variables or static objects.
* Static types for every variable or object.
* User-defined methods for every variable and object.
* Methods for 

I believe glas systems would benefit from another model that is the opposite, prioritizing performance and scalability.

Some thoughts:

* A hierarchical notion of static 'objects' in the environment. Each object has public methods that may be called, and private state that cannot be directly accessed or referenced. There are a few 'built-in' object types to represent numbers, values, etc.. Essentially, objects can replace the notions of typed data registers, stateful effects, and shared subroutines. Objects can be stack allocated and passed by reference into subprograms, but cannot be heap allocated.
* Each method call could include some static parameters, and in the method selector could be an ad-hoc static parameter that returns the actual method.
* 
* Some methods may also be constructors, returning component objects for use within a subprogram.
* Effects can be modeled as access to a provided object (or set of provided objects).
* The notion of concurrency is still oriented around non-deterministic choice. 


* support for fine-grained effects environments, ability to 'call' a subroutine from the environment, which may have its own state or share some state with other subroutines.
* support for static instancing of objects, i.e. the ability to create an 'object' instance with its own state and subroutines, based on some static parameters and references to any shared memory. 
* 

environment for reuse of code should be more flexible, e.g. include staging with both static and dynamic arguments to 'call' any subroutine.


Some design ideas:

* Need ability to efficiently 'fork' evaluation to model non-deterministic choice and concurrency. This implies multiple instances of a computation, so we cannot easily insist on static allocation of registers. Something closer to a data stack might be more appropriate.
* Reusable subprograms can be named and called for evaluation. 
* Recursion and tail-calls may be permitted in general.
* Fine-grained inputs, outputs, and state elements on the data stack.

* A program uses a static set of 'registers' for input, output, and memory. 
* It is easy to determine statically which registers are potentially read and written by the program. It is easy to track dynamically which registers are *actually* read and written when the program is evaluated.
* Every register is typed. Registers may at least have generic value types, common number types, and common buffer types for modeling queues or channels. Additionally, registers may have a location/partition to model distributed computation.
* Subprograms can efficiently, precisely, logically be 'forked' to model non-deterministic choice and potential computations. 

* effects and reusable subprograms may share some state registers between them.
* it is clear what needs to be saved for incremental computing






The 'proc' model expresses behavior as composition of transactional steps, with constraints on effects and shared state. This can be compiled to an equivalent 'prog' step function, but 'proc' should be easier to optimize for incremental computing, concurrency, and distribution.

In design. Some likely features:

* *annotations* - similar to 'prog' we can have a 'proc' header for annotations and the common variant for processes.
* *sequence* - similar to a prog sequence, but each operation may require multiple transactions. I may require that each operation is uniquely labeled for some extra stability during live coding. 
* *forks* - explicit partitioning of applications into multiple subtasks. This should appear within a 'stable' effects context. 
  * *static forks* - all the forks can be labeled at compile-time and bound to distinct processes.
  * *dynamic forks* - the set of forks is computed at runtime. The process is the same for every fork, but each may have its own runtime state. Some mechanism to support introducing or terminating forks.
* *channels* - explicit asynchronous, non-shared-state communication between forks, preferably without assuming external effects. Static channels and subchannels might be especially convenient for optimization.
  * *rendezvous* - this could be modeled in terms of a single element channel, with special recognition it might simplify optimization of the scheduler.
* *effects* - instead of a single abstract effects environment with a single state value, it could be useful to distinguish effects with shared state, effects with forkable state, write-only effects similar to channels where write order is flexible. 
* *conditionals* - likely need a couple layers such as stable conditionals (e.g. depending on configuration data) and instantaneous conditionals (decided as a process step).
* *overlay* - ability to compose applications in overlay style, wrapping components. We might treat the initial app as an overlay of the identity operation to improve compositionality.
