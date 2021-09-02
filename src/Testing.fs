namespace Glas

/// This module defines functions to support automated testing of Glas
/// programs.
module Testing =
    open System.IO
    open LoadModule
    open Effects


    /// finds test-files only, not subdirs.
    let findTestModulesInFolder (dir:FolderPath) : ModuleName list =
        if not (Directory.Exists(dir)) then [] else
        Directory.EnumerateFiles(dir) 
            |> Seq.map (fun fp -> Path.GetFileName(fp).Split('.').[0])
            |> Seq.filter (fun m -> m.StartsWith("test")) 
            |> Seq.toList

    // default fork source is secure-random bytes
    let private getRandomBytes () =
        let rng = System.Security.Cryptography.RNGCryptoServiceProvider.Create()
        fun amt -> 
            let arr = Array.zeroCreate amt
            rng.GetBytes(arr)
            arr

    /// the Fork bitstream is random by default, but is configurable.
    type ForkEff =
        val private ByteSource : int -> byte[]
        val mutable private ReadBuffer : Bits
        val mutable private TXStack : Bits list
        // note: bits within tx stack are reverse ordered

        new() = 
            {   ByteSource = getRandomBytes ()
                ReadBuffer = Bits.empty
                TXStack = List.empty
            }
        new(src) =
            {   ByteSource = src
                ReadBuffer = Bits.empty
                TXStack = List.empty
            }

        // prepare enough bits in buffer (or raise exception)
        member self.PrepareBits amt =
            let avail = Bits.length (self.ReadBuffer)
            if (avail >= amt) then () else
            let byteCt = (7 + amt - avail) / 8
            assert (byteCt > 0)
            let bytes = self.ByteSource byteCt
            if (bytes.Length < byteCt) then
                failwith "insufficient fork input"
            let newBits = Array.foldBack (Bits.consByte) bytes (Bits.empty)
            self.ReadBuffer <- Bits.append (self.ReadBuffer) newBits

        member self.ReadBits amt =
            self.PrepareBits amt
            let (rd,rem) = Bits.splitAt amt (self.ReadBuffer)
            self.ReadBuffer <- rem
            match self.TXStack with
            | (revRdPrior::txs) ->
                self.TXStack <- (Bits.append (Bits.rev rd) revRdPrior)::txs
            | [] -> ()
            rd

        interface ITransactional with
            member self.Try () =
                self.TXStack <- (Bits.empty)::(self.TXStack)
            member self.Commit () =
                match self.TXStack with
                | (tx0::tx1::txs) ->
                    // record bits read into parent transaction
                    self.TXStack <- (Bits.append tx0 tx1)::txs
                | [_]  -> 
                    // reads are fully committed, so forget about them.
                    self.TXStack <- []
                | [] -> 
                    invalidOp "commit outside of transaction"
            member self.Abort () =
                match self.TXStack with
                | (tx0::txs) ->
                    // on abort, putback any bits we've read so we can read them again.
                    self.ReadBuffer <- Bits.append (Bits.rev tx0) (self.ReadBuffer)
                    self.TXStack <- txs
                | [] ->
                    invalidOp "abort outside of transaction"

        interface IEffHandler with
            member self.Eff v =
                match v with
                | Value.Variant "fork" (Value.I amt) ->
                    let maxForkBits = 8192I // arbitrary limit
                    if (amt > maxForkBits) then
                        failwithf "fork: asking for too many bits at once (%A)" amt
                    let b = self.ReadBits (int amt)
                    Some (Value.ofBits b)
                | _ -> None


