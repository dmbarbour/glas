
// The primary operation we require is data printing. This is 
// suitable for both REPL-style development and debugging, and
// sufficient for binary extraction and compilation.
//
//   print Value with PrintProgram
//   print Value        (implicitly `with std.print`)
//
// If std.print isn't available, we'll pretty-print to stderr
// then fail. The idea is to ensure all printer logic is in the
// module system. 
//
// If we want to evaluate before printing, that can be done by
// print Program with EvaluatingPrinter. 
//
// Lightweight checks:
// 
//   arity Program
//
// Additionally, we might support basic automated testing. 
//
//   test TestProgram
//
// I'm uncertain about how to provide fork inputs to the test. I
// could try to use stdin, or an extra command-line parameter.
//
// This CLI will also support running simple console applications.
//
//   run AppProgram 
//
// In this case, we're using the transaction-machine model. The 
// application is a transaction that can interact with memory,
// filesystem, and network. We'll replay this transaction until
// the application writes an exit code or is interrupted.
//
//   help
//   help command
//
// Get help with any command.
//

open Glas
open FParsec
open Glas.Effects
open Glas.LoadModule
open Glas.ProgVal
open Glas.ProgEval

let printMsg = String.concat "\n" [
    "print Value with Printer"
    "print Value"
    ""
    "A Printer is a Glas Program, arity 1--0, that uses 'write' effects to"
    "output streaming data, and 'log' effects for debugging. A Printer can"
    "fail after partial output if applied to a value of unexpected type."
    ""
    "If a printer is not specified, we'll try 'std.print', which should be"
    "designed for text terminal based debugging of arbitrary values. This"
    "could be used as a REPL, albeit inefficient due to lack of caching."
    ""
    "By default, writes go to stdout and log messages to stderr. The client"
    "can redirect these streams normally."
    ""
    "Value or Printer are specified as values. See 'help value'."
    ""
    ]

let arityMsg = String.concat "\n" [
    "arity Program"
    ""
    "Print a description of the program's arity, e.g. `1--0`."
    "This also checks if the input is a valid Glas program."
    ]


let valueMsg = String.concat "\n" [
    "module(.label)*"
    ""
    "A value or program is specified by dotted path starting with module name."
    "Using `.label` indexes a record value. This is adequate for most use-cases."
    "There is no support for specifying values via command line."
    ""
    "Values are parameters in commands. See `help`."
    ]

let runMsg = String.concat "\n" [
    "run AppProgram"
    ""
    "An AppProgram is a Glas program, arity 0--0, that uses a wide variety"
    "of effects to interact with console, filesystem, and network. The API"
    "for this is described in [1]."
    ""
    "[1] https://github.com/dmbarbour/glas/blob/master/docs/GlasApps.md"
    ""
    "See `help value` for specifying the AppProgram."
    ]

let helpMsg = String.concat "\n" [
    "This is a pre-bootstrap implementation of the Glas command line utitlity."
    ""
    "Primary methods:"
    "    print Value"
    "    print Value with Printer"
    "    arity Program"
    "    run AppProgram"
    "    help (print|arity|run|value)"
    ""
    "The performance and completeness of this implementation is not great. The"
    "intention is to bootstrap the command line then print a new executable."
    ]

let EXIT_OK = 0
let EXIT_FAIL = -1

let help (cmd : string list) : int =
    match cmd with
    | ("print"::_) ->
        System.Console.WriteLine(printMsg)
        EXIT_OK
    | ("arity"::_) ->
        System.Console.WriteLine(arityMsg)
        EXIT_OK
    | ("run"::_) ->
        System.Console.WriteLine(runMsg)
        EXIT_OK
    | ["value"] ->
        System.Console.WriteLine(valueMsg)
        EXIT_OK
    | [] ->
        System.Console.WriteLine(helpMsg)
        EXIT_OK
    | _ ->
        eprintfn "Unrecognized 'help' subject %A; try 'help' by itself." cmd
        EXIT_FAIL

type ValueIndex =
    | RecordLabel of string
    | ListItem of uint64

type ValueID = 
    { ModuleName : string
    ; Index : ValueIndex list
    }

let parseLabel : Parser<string,unit> = 
    many1Satisfy (fun c -> isAsciiLetter c || isDigit c || (c = '-'))

let parseValueIndex : Parser<ValueIndex, unit> =
    choice [
        pchar '.' >>. parseLabel |>> RecordLabel
        //pchar '[' >>. puint64 .>> pchar ']' |>> ListItem
    ] 

let parseValue =
    parseLabel .>>. many parseValueIndex

let rec indexValue v idx =
    match idx with
    | ((RecordLabel s)::idx') ->
        match Value.record_lookup (Value.label s) v with
        | None -> None
        | Some v' -> indexValue v' idx'
    | ((ListItem u)::idx') ->
        match v with
        | Value.FTList l when ((FTList.length l) > u) ->
            let v' = FTList.item u l
            indexValue v' idx'
        | _ -> None
    | [] -> Some v

let getValue (ll:Loader) (vstr : string): Value option =
    match run parseValue vstr with
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
                logError ll (sprintf "%s: module does not contain requested element" vstr)
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
            logError ll (sprintf "value %s does not have AST of a Glas program" vstr)
            None
    | None ->
        // reason for failure is already logged. 
        None

let getLoader (logger:IEffHandler) =
    match tryBootStrapLoader logger with
    | Some ll -> ll
    | None -> nonBootStrapLoader logger


let print (vstr:string) (pstr:string) : int =
    let logger = consoleErrLogger ()
    logInfo logger (sprintf "print %s with %s" vstr pstr) 
    let loader = getLoader logger
    let printer = Printing.printStdout ()
    let ioEff = composeEff printer logger
    match getValue loader vstr with
    | None ->
        logError logger (sprintf "value %s not loaded; aborting" vstr)
        EXIT_FAIL
    | Some v ->
        match getProgram loader (1,0) pstr with
        | None ->
            logError logger (sprintf "printer %s not loaded; logging value then aborting" pstr)
            log logger (Value.variant "value" v)
            EXIT_FAIL
        | Some p ->
            match eval p ioEff [v] with
            | Some [] -> 
                //logInfo logger (sprintf "print %s with %s - done" vstr pstr)
                EXIT_OK
            | None ->
                logInfo logger (sprintf "print %s with %s - aborted in failure" vstr pstr)
                log logger (Value.variant "value" v)
                EXIT_FAIL
            | Some _ ->
                // should be impossible
                logError logger (sprintf "program %s stymied the arity checker" vstr)
                EXIT_FAIL

let arity (pstr:string) : int =
    let logger = consoleErrLogger ()
    let loader = getLoader logger
    match getValue loader pstr with
    | Some p when isValidProgramAST p -> 
        match stackArity (Arity(1,1)) p with
        | Arity(a,b) ->
            printfn "%d--%d" a b
            EXIT_OK
        | ArityDyn ->
            printfn "dynamic"
            EXIT_FAIL
        | ArityFail ->
            printfn "failure"
            EXIT_FAIL
    | Some _ -> 
        printfn "non-program"
        EXIT_FAIL
    | None ->
        // reason for failure is already logged. 
        EXIT_FAIL

let run (p:string) : int =
    let logger = consoleErrLogger ()
    logInfo logger (sprintf "run %s" p)
    logError logger "run not yet implemented"
    //let loader = getLoader logger
    EXIT_FAIL

[<EntryPoint>]
let main argv =
    //use stdin = System.Console.OpenStandardInput()
    //stdin.ReadTimeout <- 1000
    //printfn "Console Timeouts: %A" (System.Console.OpenStandardInput().CanTimeout)

    match Array.toList argv with
    | [ "print"; v ] -> print v "std.print"
    | [ "print"; v ; "with"; p ] -> print v p
    | [ "arity"; p ] -> arity p
    | [ "run"; p ] -> run p
    | ("help"::cmd) -> help cmd
    | args ->
        eprintfn "unrecognized command: %A; try 'help'" args
        -1

