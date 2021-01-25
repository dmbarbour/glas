namespace Glas

module Measured = 

    /// A useful summary of a larger value.
    type IMeasured<'M> =
        abstract member Measure: 'M

