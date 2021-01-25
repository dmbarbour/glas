namespace Glas

module Monoid =

    type IMonoid<'a> =
        abstract member Zero: 'a
        abstract member Plus: 'a -> 'a -> 'a

    type MonoidProvider<'T when 'T :> IMonoid<'T> and 'T: (new: unit -> 'T)> private () =
        static let zero = (new 'T() :> IMonoid<'T>).Zero
        static member Instance = (zero :> IMonoid<'T>)

