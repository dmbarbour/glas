namespace Glas

// Value type for Glas. 
// TODO: 
//   - support for content-addressed storage
//   - lists as finger-trees and ropes
type V
    = N of bigint
    | L of list<V>
    | D of Map<V,V>


