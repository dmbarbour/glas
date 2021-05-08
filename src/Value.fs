namespace Glas

// Glas values can directly be encoded as a node with two optional children:
//
//   type Node = { L: Node option; R: Node option }
//
// However, Glas widely uses non-branching path segments to encode symbols or
// numbers. If an allocation is required per bit, this is much too inefficient.
// So, I'll favor a radix tree structure that compacts a non-branching stem:
//
//   type Node = { Stem: Bits; Term: (Node * Node) option }
//
// This representation is adequate for bootstrapping, and is essentially what
// I've chosen to use in this project. However, this representation lacks the
// support for stowage, finger-tree representation of lists, compilation of
// static record types to structs, accelerated representations, etc..
//
// My current intention is to defer these more advanced representations to the
// bootstrapped Glas compilers. However, if it becomes necessary, I can consider
// introducing a value-object layer with more flexible representations.

[<Struct>]
type Branch<'V> = { L: 'V; R: 'V }

[<Struct>]
type Value = { Stem : Bits; Term: Branch<Value> option }

module Value =
    let x = 42

