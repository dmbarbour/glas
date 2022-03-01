
// The Glas command line utility finally has a proper design!
//
//   glas verb parameters
//      rewrites to
//   glas --run glas-cli-verb.run -- parameters
//
// This supports user-definable verbs as the default mode of interaction.
// We can introduce ad-hoc built-in operations such as --arity, but in
// theory they shouldn't be necessary.
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

let ver = "glas 0.2.0 (dotnet)"

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
    match FParsec.CharParsers.run parseValRef vstr with
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

let inline (|ProgOfArity|) p =
    (p, stackArity p)

let run (pstr:string) (args:string list): int =
    let logger = consoleErrLogger ()
    // logInfo logger (sprintf "--run %s -- %s" pstr (String.concat " " args))
    let loader = getLoader logger
    match getValue loader pstr with
    | None -> EXIT_FAIL // error already logged
    | Some (ProgOfArity (_, ar)) when (Arity (1,1) <> ar) ->
        logError logger (sprintf "program %s has unexpected arity %A" pstr ar)
        EXIT_FAIL
    | Some p ->
        let io = deferTry <| composeEff (writeEff ()) loader    // minimal bootstrap effects
        let appFn = eval p io                                   // compile application body
        let rec appLoop st =
            io.Try () // defer effects via transaction
            match appFn [st] with
            | Some [st'] -> 
                io.Commit () // successful return
                match st' with
                | Value.Variant "step" _ ->
                    appLoop st'
                | Value.Variant "halt" exitCode ->
                    match exitCode with
                    | Value.Bits b when (32 >= Bits.length b) ->
                        int (Bits.toU32 b) // cast to integer
                    | _ ->
                        logError logger (sprintf "evaluation of %s halted with unrecognized value %s" pstr (Value.prettyPrint exitCode))
                        EXIT_FAIL
                | _ ->
                    logError logger (sprintf "unrecognized output from %s: %s" pstr (Value.prettyPrint st'))
                    EXIT_FAIL
            | Some _ ->
                // this should never happen because we verify arity ahead of time.
                failwith "dynamic arity failure despite static check"
            | None ->
                io.Abort()
                // Failed evaluation doesn't halt the application. Instead, this models 
                // waiting on external changes, such as user input. In theory, busy-wait
                // can be optimized to waiting on relevant changes. But slowing the loop
                // is adequate for simple single-threaded apps.
                System.Threading.Thread.Sleep(25) 
                appLoop st

        let st0 = 
            args |> List.map (Value.ofString) 
                    |> FTList.ofList 
                    |> Value.ofFTList 
                    |> Value.variant "init"
        appLoop st0

let rec main' (args : string list) : int =
    match args with
    | (verb::args) when not (verb.StartsWith("-")) ->
        // trivial rewrite supports user-defined behavior
        let p = "glas-cli-" + verb + ".run"
        main' ("--run" :: p :: "--" :: args)
    | ( "--run" :: p :: "--" :: args) ->
        try 
            run p args
        with
        | e -> // handle uncaught exceptions
            let msg = sprintf "halted %s with exception %s" p (e.ToString())
            System.Console.Error.WriteLine(msg)
            EXIT_FAIL
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

