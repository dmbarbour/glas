open System

// What commands should be supported?
//
// Primary Requirements
// - compilation with logging
// - pretty-printing of computed module values
// - decompilation support for user-defined printing
// - extracting computed binaries
// - optional input via stdin or args 
//    special sources
//     -as foo.xyzzy  
//          default from filename, or `language-std` for stdin.
//     -m modulename
//          always current directory
//     -s string
//     -stdin
//     -stdin=foo.xyzzy
//     -
//     
//
// Utility 
// - lightweight application model (console, filesystem, network)
//
// Cache Management
// - summary of cache and stowage
// - clear cache and stowage 
// 
// 



[<EntryPoint>]
let main argv =
    // todo: parse command
    0 // return an integer exit code