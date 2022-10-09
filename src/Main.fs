
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
    "Built-in Commands:"
    ""
    "    glas --extract ValueRef"
    "        print referenced binary value to standard output"
    "        currently limited to produce binaries under 2GB"
    ""
    "    glas --run ValueRef -- Args"
    "        evaluate an application process, represented as a"
    "        transactional step function. Incomplete effects API!"
    ""
    "    glas --version"
    "        print a version string"
    ""
    "    glas --help"
    "        print this message"
    ""
    "    glas --print ValueRef"
    "        write value to standard output (ad-hoc, for debugging)"
    ""
    "User-Defined Commands (no '-' prefix):"
    ""
    "    glas opname Args"
    "        rewrites to"
    "    glas --run glas-cli-opname.main -- Args"
    ""
    "    We can define glas-cli-* modules "
    ""
    "A ValueRef is essentially a dotted path starting with a module ref. A"
    "module ref can be a module-name (global) or ./module-name (local)."
    ""
    ]

let ver = "glas pre-bootstrap 0.2 (dotnet)"

let EXIT_OK = 0
let EXIT_FAIL = -1

// parser for value ref.
module ValueRef =
    open FParsec
    type P<'T> = Parser<'T,unit>

    type Word = string

    type ModuleRef =
        | Local of Word
        | Global of Word
    
    let parseWord : P<string> =
        let wf = many1Satisfy2 isAsciiLower (fun c -> isAsciiLower c || isDigit c)
        stringsSepBy1 wf (pstring "-")

    let parseModuleRef : P<ModuleRef> =
        choice [
            pstring "./" >>. parseWord |>> Local
            parseWord |>> Global
        ]

    let parse : P<(ModuleRef * Word list)> = 
        parseModuleRef .>>. many (pchar '.' >>. parseWord) 

let getValue (ll:Loader) (vref : string): Value option =
    match FParsec.CharParsers.run (ValueRef.parse) vref with
    | FParsec.CharParsers.Success ((m,idx),_,_) ->
        let mv = 
            match m with
            | ValueRef.Local m' -> ll.LoadLocalModule m'
            | ValueRef.Global m' -> ll.LoadGlobalModule m'
        if Option.isNone mv then None else // error already printed
        let fn vOpt s =
            match vOpt with
            | None -> None
            | Some v -> Value.record_lookup (Value.label s) v
        let result = List.fold fn mv idx
        if Option.isNone result then
            logError ll (sprintf "value of module %A does not have path .%s" m (String.concat "." idx))
        result
    | FParsec.CharParsers.Failure (msg, _, _) ->
        logError ll (sprintf "reference %s fails to parse: %s" vref msg)
        None

// as getValue but checks for 'prog' header, valid AST, arity.
let getProgVal (ll:Loader) (vref : string) : Value option = 
    match getValue ll vref with
    | Some ((Value.Variant "prog" _) as p) when isValidProgramAST p ->
        match stackArity p with
        | Arity(1,1) -> Some p
        | ar ->
            logError ll (sprintf "value %s has wrong arity %A" vref ar)
            None
    | Some _ ->
        logError ll (sprintf "value %s is not a valid Glas program" vref)
        None
    | None -> None // error reported by getValue

let getLoader (logger:IEffHandler) =
    match tryBootStrapLoader logger with
    | Some ll -> ll
    | None -> 
        logWarn logger "failed to bootstrap language-g0; using built-in"
        nonBootStrapLoader logger

let extract (vref:string) : int =
    let ll = getLoader <| consoleErrLogger ()
    match getValue ll vref with
    | None -> EXIT_FAIL
    | Some (Value.Binary b) ->
        let stdout = System.Console.OpenStandardOutput()
        stdout.Write(b,0,b.Length)
        EXIT_OK
    | Some _ ->
        // This pre-bootstrap is limited to extracting 2GB. That's okay. The glas
        // executable should be small, a few megabytes at most. Larger files must
        // wait until after bootstrap. 
        logError ll (sprintf "value %s is not a binary (or is too big)" vref)
        EXIT_FAIL

let print (vref:string) : int = 
    let ll = getLoader <| consoleErrLogger ()
    match getValue ll vref with
    | Some v ->
        let s = Value.prettyPrint v
        System.Console.WriteLine(s)
        EXIT_OK
    | None ->
        EXIT_FAIL

// supports negative integers via one's complement of bits
let toInt bits =
    let fn acc b = (acc <<< 1) ||| (if b then 1 else 0)
    let bNat = (Bits.isEmpty bits) || (Bits.head bits)
    if bNat 
        then Bits.fold fn 0 bits
        else 1 + Bits.fold fn (-1) bits 

let run (vref:string) (args : string list) : int = 
    let ll = getLoader <| consoleErrLogger ()
    match getProgVal ll vref with
    | None -> EXIT_FAIL
    | Some p ->
        let eff = runtimeEffects ll 
        let pfn = eval p eff
        let rec loop st =
            match pfn [st] with
            | None ->
                // ideally, we'd track effects to know when to retry.
                // but this implementation is blind, so just wait and hope.
                System.Threading.Thread.Sleep(20)
                loop st
            | Some [st'] ->
                match st' with
                | Value.Variant "step" _ -> 
                    loop st'
                | Value.Variant "halt" (Value.Bits b) ->
                    toInt b // app controlled exit code
                | _ ->
                    logErrorV ll (sprintf "program %s reached unrecognized state" vref) st'
                    EXIT_FAIL
            | Some _ ->
                logError ll (sprintf "program %s halted on arity error" vref)
                EXIT_FAIL
        try
            let v0 = args |> List.map Value.ofString |> Value.ofList |> Value.variant "init"
            loop v0
        with 
        | e -> 
            logError ll (sprintf "program %s halted on exception %A" vref e)
            EXIT_FAIL

let rec main' (args : string list) : int =
    match args with
    | ["--extract"; b] ->
        extract b
    | ("--run" :: p :: "--" :: args') ->
        run p args'
    | ["--version"] -> 
        System.Console.WriteLine(ver)
        EXIT_OK
    | ["--help"] -> 
        System.Console.WriteLine(helpMsg)
        EXIT_OK
    | ["--print"; v] ->
        print v 
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

