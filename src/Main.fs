
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
// Additionally, we might support automated testing. 
//
//   test
//   test TestProgram
//   fuzz TestProgram
//
// The 'fuzz' option allows external control of branching via stdin.
// However, it only runs the program once. The 'test' options use
// an internal search. 
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
open Glas.Effects
open Glas.LoadModule
open FParsec

let printMsg = String.concat "\n" [
    "print Value with Printer"
    "print Value"
    ""
    "A Printer is a Glas Program, arity 1--0, that uses 'write' effects to"
    "output streaming data, and 'log' effects for debugging. The Value may"
    "have any type, though print may fail in case of runtime type errors."
    "If a printer is not specified, we'll try 'std.print'."
    ""
    "If everything works out, we'll print binary data to stdout and halt when"
    "finished. If the program fails after printing some data, we'll use an error"
    "exit code."
    ""
    "Use shell commands to redirect stdout to file."
    ""
    "Value or Printer are specified as values. See 'help value'."
    ""
    ]

let valueMsg = String.concat "\n" [
    "module(.label|[index])*"
    ""
    "A value or program is specified by dotted path starting with module name."
    "Using `.label` indexes a record, and `[3]` indexes a list, starting at `[0]`."
    "The value for the requested module is loaded then accessed by index."
    ""
    "This command line doesn't provide a method to specify a program as an"
    "argument. Any value must first be represented in a file."
    ""
    "Values are used as part of other commands. See `help`."
    ]

let testMsg = String.concat "\n" [
    "fuzz TestProgram"
    "test TestProgram"
    "test"
    ""
    "A TestProgram is a Glas program, arity 0--0, using a random data effect"
    "for input to support sub-test selection, simulate race conditions, and"
    "generate random parameters. Outputs are pass/fail and log messages."
    ""
    "Use of 'fuzz' allows fork inputs to be provided via stdin. This can be"
    "useful for repeating tests or to externalize the heuristics. Each byte"
    "is read msb to lsb, with 0 bit as failure. For example, if first byte"
    "is 0b00010111 then fork will fail-fail-fail-pass-fail-pass-pass-pass,"
    "then read the next byte from stdin."
    ""
    "Use of 'test' leaves the random fork input to the command line utility."
    "The initial implementation will simply randomize the fork input, but it"
    "is feasible to search the input space with analysis or heuristics." 
    ""
    "If a test program is not specified for 'test', we'll implicitly search"
    "all files in the current directory whose names start with 'test' for an"
    "element labeled '.test'. This can test multiple files at once."
    ""
    "See `help value` for specifying the TestProgram."
    ""
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
    "    test"
    "    test TestProgram"
    "    fuzz TestProgram"
    "    run AppProgram"
    "    help (print|test|run|value)"
    ""
    "The performance and completeness of this implementation is not great. The"
    "intention is to bootstrap the command line then print a new executable."
    ""
    ]

let EXIT_OK = 0
let EXIT_FAIL = -1

let help (cmd : string list) : int =
    match cmd with
    | ("print"::_) ->
        System.Console.WriteLine(printMsg)
        EXIT_OK
    | ("test"::_) | ("fuzz" ::_) -> 
        System.Console.WriteLine(testMsg)
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
        pchar '[' >>. puint64 .>> pchar ']' |>> ListItem
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
    | Failure (msg, _, _) ->
        logError ll (sprintf "parse error in Value identifier:\n%s" msg)
        None
    | Success ((m,idx), _, _) ->
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
    | Some (Program.Program p) -> 
        match Program.static_arity p with
        | Some struct(i,o) when (ai = i) && (ao = o) ->
            Some p
        | Some struct(i,o) ->
            logError ll (sprintf "program %s has arity %d--%d; expecting %d--%d" vstr i o ai ao)
            None
        | None ->
            logError ll (sprintf "program %s does not have static arity" vstr)
            None
    | Some _ -> 
        logError ll (sprintf "value %s is not a valid program" vstr)
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
            logInfo logger (sprintf "print %s with %s - start" vstr pstr) 
            let e0 : Program.Interpreter.RTE = { DS = [v]; ES = []; IO = ioEff }
            match Program.Interpreter.interpret p e0 with
            | Some _ -> 
                logInfo logger (sprintf "print %s with %s - done" vstr pstr)
                EXIT_OK
            | None ->
                logInfo logger (sprintf "print %s with %s - aborted in failure" vstr pstr)
                log logger (Value.variant "value" v)
                EXIT_FAIL

let testCommon (lTests) : int =
    let logger = consoleErrLogger ()
    let loader = getLoader logger
    let loadTest tstr = 
        match getProgram loader (0,0) tstr with
        | None -> None
        | Some p -> 
            logInfo logger (sprintf "loading test %s" tstr)
            Some (tstr,p)
    let lpTests = List.collect (loadTest >> Option.toList) lTests 
    EXIT_FAIL

let testDir () : int =
    Testing.findTestModulesInFolder "./" 
        |> List.map (fun m -> m + ".test")
        |> testCommon

let test (p:string) : int =
    testCommon [p]

let fuzz (p:string) : int =
    EXIT_FAIL

let run (p:string) : int =
    let logger = consoleErrLogger ()
    let loader = getLoader logger
    EXIT_FAIL

[<EntryPoint>]
let main argv =
    match Array.toList argv with
    | [ "print"; v ] -> print v "std.print"
    | [ "print"; v ; "with"; p ] -> print v p
    | [ "test" ] -> testDir ()
    | [ "test"; p ] -> test p
    | [ "fuzz"; p ] -> fuzz p
    | [ "run"; p ] -> run p
    | ("help"::cmd) -> help cmd
    | args ->
        eprintfn "unrecognized command: %A; try 'help'" args
        -1

