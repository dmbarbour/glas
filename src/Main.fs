
// The primary operation we require is data printing. This is 
// suitable for both REPL-style development and debugging, and
// for binary extraction.
//
//   print Value with PrintProgram
//   print Value        (implicitly `with std.print`)
//
// If std.print isn't available, we'll pretty-print to stderr
// then fail. The idea is to ensure all printer logic is in the
// module system. 
//
// Additionally, we might support automated testing with fork.
//
//   test
//   test TestProgram 
//   test TestProgram seed Seed 
//
// If a test program isn't specified, we infer from the current 
// directory. The fork inputs to tests are pseudo-randomized, 
// but we can specify a seed string to replay a specific test.
//
// We'll support running some console applications.
//
//   run AppProgram 
//
// Application programs are transactions, which will implicitly
// be repeated in a loop. This F# implementation of apps does
// not support incremental computing, so 'fork' should be avoided.
// Effects will support basic file, network, and console IO. 
//
//   help
//   help command
//
// Get help with any command.
//

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

