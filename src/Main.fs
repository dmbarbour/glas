
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
    "        Print referenced binary value to standard output."
    "        Currently limited to produce binaries below 2GB."
    ""
    "    glas --run ValueRef -- Args"
    "        Evaluate an application process, which represents"
    "        a transactional step function. Incomplete effects!"
    "        Currently limited to 'log' and 'load' effects."
    ""
    "    glas --version"
    "        Print a version string."
    ""
    "    glas --help"
    "        Print this message."
    ""
    "    glas --print ValueRef"
    "        Write arbitrary value to standard output using an"
    "        ad-hoc pretty printer. Intended for debugging."
    ""
    "    glas --check ValueRef"
    "        Check that module compiles and value is defined."
    "        Combines nicely with assertions for debugging."
    ""
    "User-Defined Commands (no '-' prefix):"
    ""
    "    glas opname Args"
    "        rewrites to"
    "    glas --run glas-cli-opname.main -- Args"
    ""
    "    Users may freely define glas-cli-* modules "
    ""
    "A ValueRef is essentially a dotted path starting with a module ref. A"
    "module ref can be a module-name (global) or ./module-name (local)."
    ""
    ]

let ver = "glas pre-bootstrap 0.2 (dotnet)"

let EXIT_OKAY = 0
let EXIT_FAIL = -1

// a short script to ensure GLAS_HOME is defined and exists.
let GLAS_HOME = "GLAS_HOME"
let select_GLAS_HOME () : string =
    let home = System.Environment.GetEnvironmentVariable(GLAS_HOME)
    if not (isNull home) then home else
    // default GLAS_HOME
    let appDir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData)
    System.IO.Path.Combine(appDir, "glas")
let prepare_GLAS_HOME () : unit =
    let home = System.IO.Path.GetFullPath(select_GLAS_HOME ())
    // printfn "GLAS_HOME=%A" home
    ignore <| System.IO.Directory.CreateDirectory(home)
    System.Environment.SetEnvironmentVariable(GLAS_HOME, home)

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
        (parseModuleRef .>>. many (pchar '.' >>. parseWord)) .>> eof

let mRefStr m =
    match m with
    | ValueRef.Local p -> "./" + p
    | ValueRef.Global p -> p


let getValue (ll:Loader) (vref : string): Value voption =
    match FParsec.CharParsers.run (ValueRef.parse) vref with
    | FParsec.CharParsers.Success ((m,idx),_,_) ->
        let mv = 
            match m with
            | ValueRef.Local m' -> ll.LoadLocalModule m'
            | ValueRef.Global m' -> ll.LoadGlobalModule m'
        if ValueOption.isNone mv then ValueNone else // error already printed
        let fn vOpt s =
            match vOpt with
            | ValueNone -> ValueNone
            | ValueSome v -> Value.record_lookup (Value.label s) v 
        let result = List.fold fn mv idx
        if ValueOption.isNone result then
            logError ll (sprintf "value of module %s does not have path .%s" (mRefStr m) (String.concat "." idx))
        result
    | FParsec.CharParsers.Failure (msg, _, _) ->
        logError ll (sprintf "reference %s fails to parse: %s" vref msg)
        ValueNone

let inline sepListOn sep l =
    match List.tryFindIndex ((=) sep) l with
    | None -> (l, [])
    | Some ix -> (List.take ix l, List.skip (1 + ix) l)

// get app value will also apply application macros, consuming args.
let getAppValue (ll:Loader) (vref : string) (args0 : string list) : (Value * string list) voption = 
    match getValue ll vref with
    | ValueSome v0 -> 
        // might need to expand application macros to interpret some args
        let rec macroLoop v args =
            match v with
            | (Value.Variant "macro" ((Value.Variant "prog" _) as p)) ->
                let (staticArgs,dynamicArgs) = sepListOn "--" args
                let sa = staticArgs |> List.map Value.ofString |> Value.ofList
                match eval p ll [sa] with
                | Some [p'] -> 
                    macroLoop p' dynamicArgs
                | _ ->
                    logError ll (sprintf "application macro expansion failure in %s" vref)
                    ValueNone
            | Value.Variant "prog" _ ->
                match stackArity v with
                | Arity(1,1) -> 
                    ValueSome(v, args)
                | _ ->
                    logError ll (sprintf "%s has invalid arity" vref)
                    ValueNone
            | _ -> 
                logError ll (sprintf "%s not recognized as an application" vref)
                ValueNone
        macroLoop v0 args0
    | ValueNone -> ValueNone // error reported by getValue

let getLoader (logger:IEffHandler) =
    match tryBootStrapLoader logger with
    | ValueSome ll -> ll
    | ValueNone -> 
        logWarn logger "failed to bootstrap language-g0; using built-in"
        nonBootStrapLoader logger

let extract (vref:string) : int =
    let ll = getLoader <| consoleErrLogger ()
    match getValue ll vref with
    | ValueNone -> EXIT_FAIL
    | ValueSome (Value.BinaryArray b) ->
        let stdout = System.Console.OpenStandardOutput()
        stdout.Write(b,0,b.Length)
        EXIT_OKAY
    | ValueSome _ ->
        // This pre-bootstrap is limited to extracting 2GB. That's okay. The glas
        // executable should be small, a few megabytes at most. Larger files must
        // wait until after bootstrap. 
        logError ll (sprintf "value %s is not a binary (or is too big)" vref)
        EXIT_FAIL

let print (vref:string) : int = 
    let ll = getLoader <| consoleErrLogger ()
    match getValue ll vref with
    | ValueSome v ->
        let s = Value.prettyPrint v
        System.Console.WriteLine(s)
        EXIT_OKAY
    | ValueNone ->
        EXIT_FAIL

let check (vref:string) : int =
    let ll = getLoader <| consoleErrLogger()
    match getValue ll vref with
    | ValueSome _ -> EXIT_OKAY
    | ValueNone -> EXIT_FAIL

let run (vref:string) (args : string list) : int = 
    let ll = getLoader <| consoleErrLogger ()
    match getAppValue ll vref args with
    | ValueNone -> EXIT_FAIL
    | ValueSome(p0, args') ->
        let eff = runtimeEffects ll 
        let p = p0 // ProgVal.Optimizers.tryOptimize p0
        let pfn = eval p eff
        let rec loop st =
            let tx = withTX eff
            match pfn [st] with
            | None ->
                tx.Abort()
                // ideally, we'd track effects to know when to retry.
                // but this implementation is blind, so just wait and hope.
                System.Threading.Thread.Sleep(20)
                loop st
            | Some [st'] ->
                tx.Commit()
                match st' with
                | Value.Variant "step" _ -> 
                    loop st'
                | Value.Variant "halt" (Value.Int32 exit_code) -> 
                    exit_code
                | _ ->
                    logErrorV ll (sprintf "program %s reached unrecognized state" vref) st'
                    EXIT_FAIL
            | _ ->
                tx.Abort()
                logError ll (sprintf "program %s halted on arity error" vref)
                EXIT_FAIL
        try
            let v0 = args' |> List.map Value.ofString |> Value.ofList |> Value.variant "init"
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
    | ["--run"; p] -> // no args to app
        run p []
    | ["--version"] -> 
        System.Console.WriteLine(ver)
        EXIT_OKAY
    | ["--help"] -> 
        System.Console.WriteLine(helpMsg)
        EXIT_OKAY
    | ["--print"; v] ->
        print v 
    | ["--check"; v] ->
        check v
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
        prepare_GLAS_HOME ()
        main' (Array.toList args)
    with
    | e -> 
        eprintfn "Unhandled exception: %A" e
        EXIT_FAIL

