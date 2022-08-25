
// The Glas command line utility API has a proper design now.
//
//   glas --run ProgramRef -- Args
//   glas --help
//   glas --version
//   glas --extract BinaryRef
//
// Additionally, we support user-defined verbs:
//
//   glas verb parameters
//      rewrites to
//   glas --run glas-cli-verb.run -- parameters
//
// However, this is a pre-bootstrap implementation that does not 
// support --run. This considerably simplifies the scope.
//

open Glas
open FParsec
open Glas.Effects
open Glas.LoadModule
open Glas.ProgVal
open Glas.ProgEval

let helpMsg = String.concat "\n" [
    "A pre-bootstrap implementation of Glas command line interface."
    ""
    "Methods:"
    ""
    "    glas --extract ValueRef"
    "        print referenced binary value to standard output"
    "        currently limited to produce binaries under 2GB"
    ""
    "    glas --version"
    "        print a version string"
    ""
    "    glas --help"
    "        print this message"
    ""
    "A ValueRef must be a dotted path, `ModuleName(.Label)*`. The module's"
    "transitive dependencies are compiled, including language modules. This"
    "includes bootstrap of module language-g0 if it is defined."
    ""
    "Normally, a `--run` method will support user-defined apps with limited"
    "effects, including filesystem and network access. However, this feature"
    "is deferred for a post-bootstrap implementation."
    ]

let ver = "glas pre-bootstrap 0.1 (dotnet)"

let EXIT_OK = 0
let EXIT_FAIL = -1

let getValue (ll:Loader) (vstr : string): Value option =
    // no complicated parsing of value identifiers, just split on '.'
    match List.ofArray (vstr.Split('.')) with
    | (m::idx) ->
        match ll.LoadModule m with
        | None ->
            logError ll (sprintf "module %s failed to load" m)
            None
        | Some v0 -> 
            // we have the module value, the hard part is done!
            // just need to index via dotted path.
            let fn vOpt s =
                match vOpt with
                | None -> None
                | Some v -> Value.record_lookup (Value.label s) v
            let result = List.fold fn (Some v0) idx
            if Option.isNone result then
                logError ll (sprintf "value of module %s does not have path .%s" m (String.concat "." idx))
            result
    | [] -> 
        logError ll "failed to parse value reference"
        None

let getLoader (logger:IEffHandler) =
    match tryBootStrapLoader logger with
    | Some ll -> ll
    | None -> 
        logWarn logger "failed to bootstrap language-g0; using built-in"
        nonBootStrapLoader logger

let extract (vstr:string) : int =
    let logger = consoleErrLogger ()
    let loader = getLoader logger
    match getValue loader vstr with
    | None -> EXIT_FAIL
    | Some (Value.Binary b) ->
        let stdout = System.Console.OpenStandardOutput()
        stdout.Write(b,0,b.Length)
        EXIT_OK
    | Some _ ->
        // This pre-bootstrap is limited to extracting 2GB. That's okay. The glas
        // executable should be small, a few megabytes at most. Larger files must
        // wait until after bootstrap. 
        logError logger (sprintf "value %s is not a binary (or is too big)" vstr)
        EXIT_FAIL

let rec main' (args : string list) : int =
    match args with
    | ["--extract"; vstr] ->
        extract vstr
    | ["--version"] -> 
        System.Console.WriteLine(ver)
        EXIT_OK
    | ["--help"] -> 
        System.Console.WriteLine(helpMsg)
        EXIT_OK
    | ( "--run" :: p :: "--" :: args') ->
        eprintfn "Command recognized: %s" (String.concat " " args)
        eprintfn "But --run is not currently supported."
        EXIT_FAIL
    | (verb::args') when not (verb.StartsWith("-")) ->
        // trivial rewrite supports user-defined behavior
        let p = "glas-cli-" + verb + ".main"
        main' ("--run" :: p :: "--" :: args')
    | args -> 
        eprintfn "unrecognized command: %s" (String.concat " " args)
        eprintfn "try 'glas --help'"
        EXIT_FAIL

[<EntryPoint>]
let main args = 
    try 
        main' (Array.toList args)
    with
    | e -> 
        eprintfn "Unhandled exception: %A" e
        EXIT_FAIL

