# Glas GUI?

Conventional GUI models hinder runtime composition, extension, and modification of applications. There is a huge disconnect between how data is computed and how it is presented and maintained in a retained mode GUI. 

I'm uncertain whether I can fix this with Glas. It's a huge design challenge. 

However, the [transaction-machine based application model](GlasApps.md) does offer an opportunity to revisit design of the GUI model. Transaction machines are easily extended, composed, and modified at runtime. An extension is essentially a fork, with a view of shared state.

Let's consider GUI from a few perspectives:

* **Interaction.** A GUI represents a negotiation between system and user. The system and user each have expectations. The interaction itself is stateful, which hinders abort or undo.
* **Projection.** A GUI is an editable, aesthetic projection of a system's state. Without action by a user, it is essentially a dashboard. Any number of projections may exist concurrently. 
* **Agent.** A GUI app represents the will of a user. It imposes a user's interests, intentions, interference upon system behavior.

It seems to me that we usually model GUI as interaction. However, this perspective puts users and machines on equal footing, which doesn't seem like a great idea before we have sapient machines. 





 do an awful job for composition, comprehension, and extension. We cannot wire an output field from one application as an input to another, easily determine how visible data is computed, or normally extend applications without invasive modifications. The GUI can be very pretty, but it's a walled garden.

Alternatives:
* GUI as editable projection of application state. This would support controlled sharing of state. A difficulty is managing projections.
* Immediate-mode GUI with frames. Logically, a transaction can continuously write named GUI frames and commit. Pointer inputs are read directly at the point of draw, but are stabilized via name within the frame. No intermediate channel or state.

The latter option is also a projection of sorts, but is more implicit to the transaction. The frames allow for smaller transactions to succeed and stabilize, so we can focus computation on the changing elements.


