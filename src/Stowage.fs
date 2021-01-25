namespace Glas

/// Stowage supports content-addressed storage for immutable data via
/// secure hash to reference serialized representations. 
/// 
/// Stowage augments functional programs by reducing memory pressure, and
/// reducing the effective entry size for memoization. Secure hashes extend
/// stowage across distributed systems with provider independence, data
/// authentication, lazy downloads and incremental updates.
/// 
/// The cost of stowage is some extra complexity when comparing values for
/// logical equivalence (especially if stowage pretends to be transparent)
/// and complexity for garbage collection of stowed data. 
module Stowage =

    /// Stowage references identify an external binary resource.
    ///
    /// Ideally, they should include a secure hash of the binary
    /// for provider-independence.
    type StowageRef = ByteString

    /// Sizes of things are currently recorded as uint64.
    type Size = uint64

    /// IStowageProvider represents a stowage database.
    type IStowageProvider =

        /// Add binary to stowage database, return reference.
        abstract member Stow : ByteString -> StowageRef

        /// Access value in stowage database, return binary.
        /// Raise MissingRsc exception if missing.
        abstract member Load : StowageRef -> ByteString 

        /// The incref/decref methods can support reference counting GC
        /// but could be ignored if other GC (or no GC) is used. When a
        /// binary is initially stowed, we assume an initial reference.
        abstract member Decref : StowageRef -> unit
        abstract member Incref : StowageRef -> unit


    /// IStowageCodec supports storage of structured data into Stowage
    type IStowageCodec<'T> =
        /// Write will produce a lazy sequence of binary chunks.
        abstract member Write : 'T -> seq<ByteString>

        /// Read should parse a value from a ByteString and return remaining
        /// bytes. Stowage representations should not have any ambiguity.
        abstract member Read : IStowageProvider -> ByteString -> ('T * ByteString)

        /// Compaction can rewrite a value to use stowage. To leverage this fully,
        /// the value type should include stowage references.
        abstract member Compact : IStowageProvider -> 'T -> ('T * Size)

    /// If the provider cannot load a resource, raise this exception.
    exception MissingRscException of IStowageProvider * StowageRef

    /// Stowage value reference.

    /// Stowage optional data.


    /// fake stowage provider is useful as a placeholder
    let fakeStowageProvider : IStowageProvider =
        { new IStowageProvider with
            member __.Stow s = BS.sha512 s
            member db.Load h = raise (MissingRscException (db,h))
            member __.Decref _ = ()
            member __.Incref _ = ()
        }
    
    /// codec via intermediate representation (for codec combinators)
    let irepCodec (c : IStowageCodec<'IR>) (fromIR : 'IR -> 'T) (toIR : 'T -> 'IR) : IStowageCodec<'T> =
        { new IStowageCodec<'T> with
            member __.Write v = c.Write (toIR v)
            member __.Read db s = 
                let (ir, s') = c.Read db s
                (fromIR ir, s')
            member __.Compact db v = 
                let (ir, sz) = c.Compact db (toIR v)
                (fromIR ir, sz)
        }

    let boxedCodec (cV : IStowageCodec<'V>) : IStowageCodec<System.Object> =
        irepCodec cV (box<'V>) (unbox<'V>)



