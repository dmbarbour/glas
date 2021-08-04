
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
    // just some bullshit for testing a few things
    let fgc0 = System.Console.ForegroundColor
    for fgc in System.ConsoleColor.GetValues() do
        System.Console.ForegroundColor <- fgc
        printfn "Color is %A" fgc 
    System.Console.ForegroundColor <- fgc0

    printfn "Args=%A" argv
    printfn "Env.Args=%A" (System.Environment.GetCommandLineArgs())
    printfn "Console.IsOutputRedirected=%A" (System.Console.IsOutputRedirected)
    printfn "Console.IsErrorRedirected=%A" (System.Console.IsErrorRedirected)
    printfn "Console.IsInputRedirected=%A" (System.Console.IsInputRedirected)
    0 // return an integer exit code

