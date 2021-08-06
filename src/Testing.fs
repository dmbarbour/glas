namespace Glas

/// This module defines functions to support automated testing of Glas
/// programs.
module Testing =
    open System.IO
    open LoadModule

    /// finds test-files only, not subdirs.
    let findTestsInFolder (dir:FolderPath) : FilePath list =
        if not (Directory.Exists(dir)) then [] else
        Directory.EnumerateFiles(dir) 
            |> Seq.filter (fun fp -> Path.GetFileName(fp).StartsWith("test")) 
            |> Seq.toList

    let private _hashAlg () = 
        System.Security.Cryptography.SHA512.Create() :> System.Security.Cryptography.HashAlgorithm

    let private _randomBytes amt = 
        let arr = Array.zeroCreate amt
        use rng = System.Security.Cryptography.RNGCryptoServiceProvider.Create()
        rng.GetBytes(arr)
        arr

    // random number generation...
    //
    // I don't trust System.Random for this role. It doesn't have enough state
    // or a large enough seed. Also, performance won't be a bottleneck here.
    // So I'll just use a crypto-random CPRNG.
    //
    // I'd use RNGCryptoServiceProvider, but it doesn't accept a seed. So I need
    // to provide my own implementation, here based on secure hashes. This is a 
    // secure RNG, which is not very useful here but also doesn't hurt anything.
    type RNG =
        val private HashAlg : System.Security.Cryptography.HashAlgorithm
        val mutable private Hash : byte[]
        val mutable private Next : int
        new() =
            { HashAlg = _hashAlg ()
            ; Hash = _randomBytes 64
            ; Next = 32
            }
        new(seed:string) =
            { HashAlg = _hashAlg ()
            ; Hash = System.Text.Encoding.UTF8.GetBytes(seed) 
            ; Next = 0 // hash on first use
            }
        member rng.GetByte() : byte =
            if (0 = rng.Next) then
                rng.Hash <- rng.HashAlg.ComputeHash rng.Hash
                rng.Next <- rng.Hash.Length / 2     
            rng.Next <- rng.Next - 1
            rng.Hash.[rng.Next]

    let private _random_seed_chars = 
        ['b';'c';'d';'f'
        ;'g';'h';'j';'k'
        ;'l';'m';'n';'p'
        ;'q';'r';'s';'t'
        ] |> List.toArray |> Array.map byte

    /// Automatic seed selection.
    let randomSeed (rng:RNG) (len:int) : string =
        [| for _ in 1 .. len do 
                yield _random_seed_chars.[0x0F &&& int (rng.GetByte())]
        |] |> System.Text.Encoding.ASCII.GetString
        

