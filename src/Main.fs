open System

// What commands should be supported?
//
// Primary Requirements
// - log and load
// - binary extraction
// - test apps
// - user apps (console)
// - bootstrap (consider a -no-bootstrap flag to NOT bootstrap language-g0)
// 
// Support for caching computations in filesystem would be useful,
// but is not essential at this time.

[<EntryPoint>]
let main argv =
    printfn "Args=%A" argv
    0 // return an integer exit code