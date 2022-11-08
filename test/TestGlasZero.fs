module Glas.TestGlasZero
    open Expecto
    open FParsec
    open RandVal
    open Value
    open Glas.Zero
    open Glas.Effects

    let ofBitList l = List.foldBack consStemBit l unit

    let doParse p s =
        match run (p .>> eof) s with
        | Success (r, _, _) -> r
        | Failure (msg, _, _) ->
            failtestf "unexpected parse failure for %A: %s" s msg
    
    let failParse p s =
        match run (p .>> eof) s with
        | Success (r, _, _) ->
            failtestf "unexpected parse success input = %A result = %A" s r
        | Failure (msg, _, _) ->
            //printfn "parse failed as expected: \n%A" msg 
            ()

    let rec _toBits acc bitct n =
        if (bitct < 1) then acc else
        let acc' = consStemBit (0UL <> (n &&& 1UL)) acc
        _toBits acc' (bitct - 1) (n >>> 1)

    let toB bitct n =
        _toBits (unit) bitct n


    let inline impAs w a = struct(w,a)
    let inline imp w = impAs w w
    let inline callW (s:string) : Action = 
        Call struct(s,[])

    [<Tests>]
    let tests = 
        testList "g0 parser tests" [
            // starting with embedded data
            testCase "words" <| fun () ->
                let zpw = Parser.parseWord
                let pw s = doParse zpw s
                Expect.equal "foo" (pw "foo") "equal basic word"
                Expect.equal "foo-bar" (pw "foo-bar") "equal hyphenated word"
                Expect.equal "foo" (pw "foo \n\r") "ignoring spaces after word"
                Expect.equal "foo32" (pw "foo32") "words with numbers"
                Expect.equal "foo3-bar2" (pw "foo3-bar2") "words with numbers and hyphens"
                Expect.equal "foo" (pw "foo # with comment\n") "word with comment"

                failParse zpw ""
                failParse zpw "0"
                failParse zpw "5foo"
                failParse zpw "foo-"
                failParse zpw "-foo"
                failParse zpw "foo bar"
                failParse zpw " foo"

                // might be permitted later, but not for now
                failParse zpw "camelCase"
                failParse zpw "Uppercase"
                failParse zpw "under_scores"

            testCase "bitstring data" <| fun () ->
                let zpd = Parser.parseBitString
                let pd s = doParse zpd s
                assert(toB 6 23UL = ofBitList [false; true; false; true; true; true])

                Expect.equal (toB 6 23UL) (pd "0b010111") "equal bit strings"
                Expect.equal (toB 1 1UL) (pd "0b1") "minimal bit string 1"
                Expect.equal (toB 1 0UL) (pd "0b0") "minimal bit string 0"
                Expect.equal (toB 8 23UL) (pd "0x17") "equal hex strings"
                Expect.equal (toB 5 23UL) (pd "23 # comment\n") "equal min-width numbers"
                Expect.equal (Value.label "seq") (pd "'seq  ") "equal symbols"
                Expect.equal (unit) (pd "0") "zero is unit"
                Expect.equal (Value.ofByte 0xABuy) (pd "0xaB") "hex cases"
                Expect.equal (toB 12 0xabcUL) (pd "0xAbC") "hex nibble alignment"
                Expect.equal (unit) (pd "0b") "0b is now accepted."
                Expect.equal (unit) (pd "0x") "0x is now accepted."

                failParse zpd ""
                failParse zpd "'"
                failParse zpd "0332"
                failParse zpd "0xH"
                failParse zpd "'3foo"


            testCase "string data" <| fun () ->
                let zps = Parser.parseString
                let ps s = doParse zps s

                Expect.equal "Hello, world!" (ps "\"Hello, world!\"") "why hello there" 
                Expect.equal "" (ps "\"\" # <- it's empty!\r") "The empty string."

                // limiting strings to ASCII range, no C0 except SP, etc.
                failParse zps "\""
                failParse zps "\"\n\""
                failParse zps "\"\t\""

                // I might later support UTF-8 in general, but not now.
                failParse zps "\"→\""

            testCase "programs" <| fun () ->
                let zpp = Parser.ws >>. many Parser.parseAction
                let pp s = doParse zpp s
                let vb = Value.ofBits

                // A simple program.
                let p1 =  [Const (vb (toB 12 0xabcUL)); Const (Value.symbol "foo"); Const (Value.ofString "test"); callW "swap"]
                Expect.equal p1 (pp "0xaBc 'foo \"test\" \n  swap  ") "test simple program"

                let p2 = [Block []; callW "dip"]
                Expect.equal p2 (pp "[] dip") "dip nop 1"
                Expect.equal p2 (pp " [ ] dip ") "dip nop 2"
                Expect.equal p2 (pp "# dip nop\n [  ] dip") "dip nop 3"
                Expect.equal p2 (pp "\r\r\r [ \r\n\t ] \r\r\r\r dip \n\n\n") "dip nop 4"

                failParse zpp "[ dip "
                failParse zpp "] dip"
                failParse zpp "[]] dip "

                let p3 = [Block [callW "foo"]; callW "dip"]
                Expect.equal p3 (pp "[foo] dip") "dip foo 1"
                Expect.equal p3 (pp "[ foo ] dip ") "dip foo 2"
                Expect.equal p3 (pp "\n[ \nfoo\r ] \r dip ") "dip foo 3"
                Expect.equal p3 (pp "[foo] # comment\n dip") "dip foo 4"

                let p4 = [Block [callW "foo"]; Block [callW "bar"]; Block [callW "baz"]; callW "try-then-else"]
                Expect.equal p4 (pp "[foo] [\nbar\n] \n [baz #comment\n] try-then-else # more comments\n") "try then else"

            testCase "imports" <| fun () -> 
                Expect.equal (Global "abc") (doParse Parser.parseOpen "open abc") "parse open"

                let zpf = Parser.parseEnt 
                let pf s = doParse zpf s

                let p1 = FromModule (Local "a", [impAs "bar" "foo"; imp "qux"; impAs "foo" "bar"; imp "baz" ])
                Expect.equal p1 (pf "from ./a import bar as foo, qux, foo as bar, baz") "from module import words"

                let p2 = FromData ([callW "a"; callW "b"], [impAs "x" "y"])
                Expect.equal p2 (pf "from [a b] import x as y") "from data import words"


            testCase "toplevel" <| fun () ->
                let p1 = String.concat "\n" [ 
                    "open xyzzy # inherit definitions"
                    "from foo import a, b as c, d"
                    "from ./bar import i as j, k"
                    ""
                    "prog baz [ # defining baz"
                    "  a c j h"
                    "]"
                    ""
                    "macro qux [ abra cadabra ]"
                    ""
                    "assert [ i j k ] "
                    ""
                    "export [foo bar]"
                ]
                let fromFoo = FromModule(Global "foo", [imp "a"; impAs "b" "c"; imp "d"])
                let fromBar = FromModule(Local "bar", [impAs "i" "j"; imp "k"])
                let defBaz = ProgDef("baz", [callW "a"; callW "c"; callW "j"; callW "h"])
                let defQux = MacroDef("qux", [callW "abra"; callW "cadabra"])
                let assertIJK = StaticAssert (11L, [callW "i"; callW "j"; callW "k" ])
                let p1act =
                    { Open = Some (Global "xyzzy")
                      Ents = [fromFoo; fromBar; defBaz; defQux; assertIJK]
                      Export = Some (ExportFn [callW "foo"; callW "bar"])
                    }
                Expect.equal p1act (doParse Parser.parseTopLevel p1) "parse a program"
        ]

    let pp = doParse Parser.parseTopLevel

    [<Tests>]
    let testValidation = 
        // the only structural validation we really do is checking for shadowed words.
        // Shadowing includes accidental recursive definitions, using words before definition.
        // Other validation - assert, etc. - 
        testList "validation" [
            testCase "dup words" <| fun () ->
                let p1 = pp <| String.concat "\n" [
                    "open foo"
                    "from a import b, c"
                    "from d import e as b"
                    "prog c [b e e]"
                    ] 
                Expect.equal (wordsShadowed p1) (Set.ofList ["b";"c"]) "dup import and prog"

            testCase "used before defined" <| fun () ->
                let p1 = pp <| String.concat "\n" [
                    "prog a [ b c ] "
                    "prog b [ a c ] "
                    "prog c [ a b ] "
                    ]
                Expect.equal (wordsShadowed p1) (Set.ofList ["b";"c"]) "using words in mutual cycle"

                let p2 = pp "prog a [a b c]"
                Expect.equal (wordsShadowed p2) (Set.ofList ["a"]) "recursive def"

            testCase "looks okay" <| fun () ->
                let p1 = pp ""
                Expect.equal (wordsShadowed p1) Set.empty "empty program is okay"

                let p2 = pp "open xyzzy"
                Expect.equal (wordsShadowed p2) Set.empty "single open is okay"

                let p3 = pp "from xyzzy import a, b, c, d, e as f, f as e,\n  h, i as j"
                Expect.equal (wordsShadowed p3) Set.empty "import lists are okay"

                let p4 = pp "prog foo [a b c]\nprog bar [d e f]" 
                Expect.equal (wordsShadowed p4) Set.empty "progs are okay"
        ]

    let inline toVOpt opt =
        match opt with
        | Some v -> ValueSome v
        | None -> ValueNone

    // Simple effects handler to test loading of modules.
    // Note that we aren't testing the log function, since
    // the logging behavior isn't very well specified.
    let testLoadEff (src:string) (ns:Map<ModuleRef,Value>) : IEffHandler =
        let logging = not (System.String.IsNullOrEmpty(src))
        { new IEffHandler with
            member __.Eff request =
                if logging then
                    printfn "%s: %s" src (Value.prettyPrint request)
                match request with
                | Value.Variant "load" (Value.Variant "global" (Value.String m)) ->
                    toVOpt <| Map.tryFind (Global m) ns
                | Value.Variant "load" (Value.Variant "local" (Value.String m)) ->
                    toVOpt <| Map.tryFind (Local m) ns
                | Value.Variant "log" msg ->
                    ValueSome (Value.unit)
                | _ ->
                    ValueNone
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
        }

    let doCompile ll s = 
        match compile ll s with
        | ValueSome r ->
            //printfn "COMPILED TO %s" (Value.prettyPrint r) 
            r
        | ValueNone ->
            failtestf "program does not compile"

    let doEval p e ds = 
        match ProgEval.eval p e ds with
        | Some e' -> e'
        | None -> failtestf "eval unsuccessful for program %A" p

    let prims = String.concat "\n" [
        "# the simplest macro - apply static data as program"
        "macro apply []"
        ""
        "# primitive symbolic operators"
        "prog copy ['copy apply]"
        "prog drop ['drop apply]"
        "prog swap ['swap apply]"
        "prog eq ['eq apply]"
        "prog fail ['fail apply]"
        "prog eff ['eff apply]"
        "prog get ['get apply]"
        "prog put ['put apply]"
        "prog del ['del apply]"
        ""
        "# construction of composite operations"
        "macro dip [0 'dip put]"
        "macro while-do [0 'do put 'while put 0 'loop put]"
        "macro try-then-else [0 'else put 'then put 'try put 0 'cond put]"
        "macro with-do [0 'do put 'with put 0 'env put]"
    ]

    let listOps = String.concat "\n" [
        "# list ops were removed from Glas primitive ops."
        "# let's rebuild them here."
        ""
        "# testing multiple import styles"
        "open prims"
        "from prims import copy as cp"
        ""
        "prog pushl [0 0b0 put 0b1 put]"
        "prog popl  [cp 0b1 get swap 0b0 get]"
        ""
        "prog log-val [cp 0 'log put eff 0 eq]"
        "assert [\"hello\" popl 0x68 eq \"ello\" eq]"
        "assert [\"ello\" 0x68 pushl \"hello\" eq]"
        ""
        "prog list-rev-append ["
        "  [[popl] dip]  # (V:L1) L2 -- L1 V L2"
        "  [swap pushl]  # L1 V L2 -- L1 (V:L2)"
        "  while-do      # L1 L2 -- () (Rev(L1)+L2)"
        "  swap 0 eq     # remove L1 unit value"
        "]"
        "assert [\"cba\" \"def\" list-rev-append \"abcdef\" eq]"
        "prog list-rev [0 list-rev-append]"
        ""
        "assert [\"hello\" log-val list-rev log-val \"olleh\" eq]"
        ""
        "prog join [[list-rev] dip list-rev-append]"
        ""
    ]
    
    // note: cannot use math as base test since removed arithmetic.
    // might try to implement list append, instead.

    [<Tests>]
    let testCompile =
        testCase "compile and test a simple g0 program" <| fun () ->
            let getfn m w =
                match Value.record_lookup (Value.label w) m with
                | ValueNone -> failtestf "unable to find word %s" w
                | ValueSome v when ProgVal.isValidProgramAST v -> v
                | ValueSome _ -> failtestf "unable to parse program for word %s" w

            let ll0 = testLoadEff "" (Map.empty) 
            let mPrims = doCompile ll0 prims

            let ll1 = testLoadEff "" (Map.ofList [(Global "prims", mPrims)])
            let mLists = doCompile ll1 listOps

            let s0 = "abcdefghijklm"
            let s1 = "nopqrstuvwxyz"

            let pJoin = getfn mLists "join"
            match ProgEval.eval pJoin noEffects [Value.ofString s1; Value.ofString s0] with
            | Some [Value.String s] -> 
                Expect.equal s (s0 + s1) "join succeeded" 
            | _ ->
                failtest "join did not return expected value" 

