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
    printfn "Env.Args=%A" (System.Environment.GetCommandLineArgs())
    0 // return an integer exit code