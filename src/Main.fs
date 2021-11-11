
// The Glas command line utility finally has a proper design!
//
//   glas verb parameters
//      rewrites to
//   glas --run glas-cli-verb.run -- parameters
//
// This supports user-definable verbs as the default mode of interaction.
// Meanwhile, we can introduce new built-in operations (with --op).
//

open Glas
open FParsec
open Glas.Effects
open Glas.LoadModule
open Glas.ProgVal
open Glas.ProgEval


let helpMsg = String.concat "\n" [
    "Pre-bootstrap implementation of Glas command line interface."
    ""
    "Available Operations:"
    ""
    "    --run Program -- Parameters"
    "        run a Glas program from the module system"
    "        parameters after '--' are passed to Program"
    "    --arity Program"
    "        print arity for a referenced program"
    "    --print Value"
    "        pretty-print a value using a built-in printer"
    "    --version"
    "        print a version string"
    "    --help"
    "        print this message"
    ""
    "A simple syntactic sugar enables user-defined verbs:"
    ""
    "    glas foo Parameters "
    "        (is equivalent to) "
    "    glas --run glas-cli-foo.run -- Parameters "
    ""
    "The expectation is that user-defined verbs should be the"
    "normal mode of use."
    ""
    "Use GLAS_PATH to specify where modules are stored."
    ]

let ver = "glas 0.1.0 (dotnet)"

let EXIT_OK = 0
let EXIT_FAIL = -1


let parseLabel : Parser<string,unit> = 
    many1Satisfy (fun c -> isAsciiLetter c || isDigit c || (c = '-'))

let parseValRef : Parser<(string * string list), unit> =
    parseLabel .>>. many (pchar '.' >>. parseLabel)

let indexValue v0 idx =
    let fn vOpt s =
        match vOpt with
        | None -> None
        | Some v -> Value.record_lookup (Value.label s) v
    List.fold fn (Some v0) idx

let getValue (ll:Loader) (vstr : string): Value option =
    match run parseValRef vstr with
    | FParsec.CharParsers.Failure (msg, _, _) ->
        logError ll (sprintf "parse error in Value identifier:\n%s" msg)
        None
    | FParsec.CharParsers.Success ((m,idx), _, _) ->
        match ll.LoadModule m with
        | None ->
            logError ll (sprintf "module %s failed to load" m)
            None
        | Some v0 -> 
            let result = indexValue v0 idx
            if Option.isNone result then
                logError ll (sprintf "value of module %s does not have path .%s" m (String.concat "." idx))
            result

let getProgram (ll : Loader) (ai,ao) (vstr : string) : Program option =
    match getValue ll vstr with
    | Some p -> 
        match static_arity p with
        | Some struct(i,o) when ((i - o) = (ai - ao)) && (ai >= i) ->
            Some p
        | Some struct(i,o) ->
            logError ll (sprintf "program %s has arity %d--%d; expecting %d--%d" vstr i o ai ao)
            None
        | None when isValidProgramAST p ->
            logError ll (sprintf "program %s does not have static arity" vstr)
            None
        | None ->
            logError ll (sprintf "value %s does not represent a Glas program" vstr)
            None
    | None ->
        // reason for failure is already logged. 
        None

let getLoader (logger:IEffHandler) =
    match tryBootStrapLoader logger with
    | Some ll -> ll
    | None -> nonBootStrapLoader logger


let print (vstr:string) : int =
    let logger = consoleErrLogger ()
    let loader = getLoader logger
    match getValue loader vstr with
    | None ->
        logError logger (sprintf "value %s not loaded; aborting" vstr)
        EXIT_FAIL
    | Some v ->
        printf "%s\n" (Value.prettyPrint v) 
        EXIT_OK

let arity (pstr:string) : int =
    let logger = consoleErrLogger ()
    // logInfo logger (sprintf "--arity %s" pstr)
    let loader = getLoader logger
    match getValue loader pstr with
    | Some p when isValidProgramAST p -> 
        match stackArity p with
        | Arity(a,b) ->
            printfn "%d--%d" a b
            EXIT_OK
        | ArityFail i ->
            printfn "%d--FAIL" i
            EXIT_FAIL
        | ArityDyn ->
            printfn "dynamic"
            EXIT_FAIL
    | Some _ -> 
        printfn "not a program"
        EXIT_FAIL
    | None ->
        // reason for failure is already logged. 
        EXIT_FAIL

let mkFullEff (ll:Loader) = 
    (ll :> IEffHandler)

let run (pstr:string) (args:string list): int =
    let logger = consoleErrLogger ()
    // logInfo logger (sprintf "--run %s -- %s" pstr (String.concat " " args))
    let loader = getLoader logger
    match getProgram loader (1,1) pstr with
    | Some p ->
        let argVals =
            args |> List.map (Value.ofString) 
                 |> FTList.ofList |> Value.ofFTList 
                 |> Value.variant "cmd"
        let io = mkFullEff loader 
        match eval p io [argVals] with
        | Some [Value.Bits b] when (32 >= Bits.length b) ->
            (int (Bits.toU32 b)) // user-provided exit code
        | Some vs ->
            let vsStr = vs |> List.map (Value.prettyPrint) |> String.concat ";;;" // should be singleton
            logError logger (sprintf "evaluation of %s exited with unrecognized value %s" pstr vsStr)
            EXIT_FAIL
        | None ->
            logError logger (sprintf "evaluation of %s halted in failure" pstr) 
            EXIT_FAIL
    | None -> 
        // error message already logged
        EXIT_FAIL
        


let rec main' (args : string list) : int =
    match args with
    | (verb::args) when not (verb.StartsWith("-")) ->
        main' ("--run" :: ("glas-cli-" + verb + ".run") :: "--" :: args)
    | ( "--run" :: p :: "--" :: args) ->
        // todo: process extra run options, if we add them. 
        run p args
    | [ "--print"; v ] -> 
        print v
    | [ "--arity"; p ] -> 
        arity p
    | ( "--version" :: _) -> 
        System.Console.WriteLine(ver)
        EXIT_OK
    | ("--help"::_) -> 
        System.Console.WriteLine(helpMsg)
        EXIT_OK
    | args -> 
        eprintfn "unrecognized command: %A; try '--help'" args
        EXIT_FAIL

[<EntryPoint>]
let main args = 
    main' (Array.toList args)

