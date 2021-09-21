module Glas.TestGlasZero
    open Expecto
    open FParsec
    open Glas.Zero
    open Glas.Effects

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
        let acc' = Bits.cons (0UL <> (n &&& 1UL)) acc
        _toBits acc' (bitct - 1) (n >>> 1)

    let toB bitct n =
        _toBits (Bits.empty) bitct n


    let imp w = (w,w)
    let impAs w a = (w,a)


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
                assert(toB 6 23UL = Bits.ofList [false; true; false; true; true; true])

                Expect.equal (toB 6 23UL) (pd "0b010111") "equal bit strings"
                Expect.equal (toB 1 1UL) (pd "0b1") "minimal bit string 1"
                Expect.equal (toB 1 0UL) (pd "0b0") "minimal bit string 0"
                Expect.equal (toB 8 23UL) (pd "0x17") "equal hex strings"
                Expect.equal (toB 5 23UL) (pd "23 # comment\n") "equal min-width numbers"
                Expect.equal (Value.label "seq") (pd "'seq  ") "equal symbols"
                Expect.equal (Bits.empty) (pd "0") "zero is unit"
                Expect.equal (Bits.ofByte 0xABuy) (pd "0xaB") "hex cases"
                Expect.equal (toB 12 0xabcUL) (pd "0xAbC") "hex nibble alignment"

                failParse zpd ""
                failParse zpd "'"
                failParse zpd "0b"
                failParse zpd "0x"
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
                let p1 =  [Data (vb (toB 12 0xabcUL)); Data (Value.symbol "foo"); Data (Value.ofString "test"); Call "swap"]
                Expect.equal p1 (pp "0xaBc 'foo \"test\" \n  swap  ") "test simple program"

                let p2 = [Block []; Call "dip"]
                Expect.equal p2 (pp "[] dip") "dip nop 1"
                Expect.equal p2 (pp " [ ] dip ") "dip nop 2"
                Expect.equal p2 (pp "# dip nop\n [  ] dip") "dip nop 3"
                Expect.equal p2 (pp "\r\r\r [ \r\n\t ] \r\r\r\r dip \n\n\n") "dip nop 4"

                failParse zpp "[ dip "
                failParse zpp "] dip"
                failParse zpp "[]] dip "

                let p3 = [Block [Call "foo"]; Call "dip"]
                Expect.equal p3 (pp "[foo] dip") "dip foo 1"
                Expect.equal p3 (pp "[ foo ] dip ") "dip foo 2"
                Expect.equal p3 (pp "\n[ \nfoo\r ] \r dip ") "dip foo 3"
                Expect.equal p3 (pp "[foo] # comment\n dip") "dip foo 4"

                let p4 = [Block [Call "foo"]; Block [Call "bar"]; Block [Call "baz"]; Call "try-then-else"]
                Expect.equal p4 (pp "[foo] [\nbar\n] \n [baz #comment\n] try-then-else # more comments\n") "try then else"

            testCase "imports" <| fun () -> 
                let zpf = Parser.parseImportFrom
                let pf s = doParse zpf s

                let p1 = ImportFrom ("a", [impAs "bar" "foo"; imp "qux"; impAs "foo" "bar"; imp "baz" ])
                Expect.equal p1 (pf "from a import bar as foo, qux, foo as bar, baz") "from module import words"
                Expect.equal "abc" (doParse Parser.parseOpen "open abc") "parse open"


            testCase "toplevel" <| fun () ->
                let p1 = String.concat "\n" [ 
                    "open xyzzy # inherit definitions"
                    "from foo import a, b as c, d"
                    "from bar import i as j, k"
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
                let fromFoo = ImportFrom("foo", [imp "a"; impAs "b" "c"; imp "d"])
                let fromBar = ImportFrom("bar", [impAs "i" "j"; imp "k"])
                let defBaz = ProgDef("baz", [Call "a"; Call "c"; Call "j"; Call "h"])
                let defQux = MacroDef("qux", [Call "abra"; Call "cadabra"])
                let assertIJK = StaticAssert (11L, [Call "i"; Call "j"; Call "k" ])
                let p1act =
                    { Open = Some "xyzzy"
                      Ents = [fromFoo; fromBar; defBaz; defQux; assertIJK]
                      Export = [Call "foo"; Call "bar"]
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

    // Simple effects handler to test loading of modules.
    // Note that we aren't testing the log function, since
    // the logging behavior isn't very well specified.
    let testLoadEff (d:Value) : IEffHandler =
        let logging = false
        let logger = 
            if logging then consoleErrLogger () 
                       else noEffects 
        { new IEffHandler with
            member __.Eff msg =
                match msg with
                | Value.Variant "load" (Value.Bits k) ->
                    Value.record_lookup k d
                | _ -> logger.Eff msg
          interface ITransactional with
            member __.Try () = ()
            member __.Commit () = ()
            member __.Abort () = ()
        }

    let doCompile ll s = 
        match compile ll s with
        | Some r -> r
        | None ->
            failtest "expected to compile successfully"

    let doEval p e ds = 
        match ProgEval.interpret p e ds with
        | Some e' -> e'
        | None -> failtestf "eval unsuccessful for program %A" p

    let prims = String.concat "\n" [
        "# simplest macro - apply data as program"
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
        "prog pushl ['pushl apply]"
        "prog popl ['popl apply]"
        "prog pushr ['pushr apply]"
        "prog popr ['popr apply]"
        "prog join ['join apply]"
        "prog split ['split apply]"
        "prog len ['len apply]"
        "prog bjoin ['bjoin apply]"
        "prog bsplit ['bsplit apply]"
        "prog blen ['blen apply]"
        "prog bneg ['bneg apply]"
        "prog bmax ['bmax apply]"
        "prog bmin ['bmin apply]"
        "prog beq ['beq apply]"
        "prog add ['add apply]"
        "prog mul ['mul apply]"
        "prog sub ['sub apply]"
        "prog div ['div apply]"
        ""
        "# construction of composite operations"
        "macro dip [0 swap 'dip put]"
        "prog tag [[0 swap] dip put]"
        "macro while-do [['while tag] dip 'do put 'loop tag]"
        "macro try-then-else [[['try tag] dip 'then put] dip 'else put 'cond tag]"
        "macro with-do [['with tag] dip 'do put 'env tag]"
    ]

    let nmath = String.concat "\n" [
        "open prims"
        "# eralz - erase leading zeroes"
        "prog eralz [[0b0 get] [] while-do]"
        "# operations for bignum math "
        "prog modn [div [drop]dip eralz]"
        ]

    let util = String.concat "\n" [
        "open prims"
        "prog neq [[eq][fail][]try-then-else]"
        ]

    let gcd = String.concat "\n" [
        "# exercise every import type"
        "open util"
        "from nmath import modn as mod, eralz"
        "prog neq-zero [0 neq drop]"
        "# define gcd via Euclidean algorithm"
        "prog gcd [eralz [neq-zero] [copy [swap] dip mod] while-do drop eralz]"
        ]

    [<Tests>]
    let testCompile =
        testCase "gcd and its dependencies" <| fun () ->
            let getfn m w =
                match Value.record_lookup (Value.label w) m with
                | None -> failtestf "unable to find word %s" w
                | Some v when ProgVal.isValidProgramAST v -> v
                | Some _ -> failtestf "unable to parse program for word %s" w

            let ll0 = testLoadEff (Value.unit) 
            let mPrims = doCompile ll0 prims

            let ll1 = testLoadEff (Value.asRecord ["prims"] [mPrims])
            let mnmath = doCompile ll1 nmath
            let mutil = doCompile ll1 util

            // test eralz
            let pEralz = getfn mnmath "eralz"
            Expect.equal (ProgVal.static_arity pEralz) (Some struct(1,1)) "arity eralz"
            let eEralz = doEval pEralz noEffects [Value.u64 12345UL]
            Expect.equal eEralz [Value.nat 12345UL] "erase zero-bits prefix"

            // test modn
            let pModn = getfn mnmath "modn"
            Expect.equal (ProgVal.static_arity pModn) (Some struct(2,1)) "arity modn"
            let eModn1 = doEval pModn noEffects [Value.u64 1071UL; Value.u32 462u]
            Expect.equal eModn1 [Value.nat 462UL] "mod1"
            let eModn2 = doEval pModn noEffects [Value.u32 462u; Value.u64 1071UL]
            Expect.equal eModn2 [Value.nat 147UL] "mod2"

            // provide nmath and util to compilation of gcd module
            let ll2 = testLoadEff (Value.asRecord ["nmath"; "util"] [mnmath; mutil])
            let mgcd = doCompile ll2 gcd

            // test neq-zero
            let pNEQZ = getfn mgcd "neq-zero"
            Expect.equal (ProgVal.static_arity pNEQZ) (Some struct(1,1)) "arity neq-zero"
            let eNEQ1 = doEval pNEQZ noEffects [Value.u8 0uy] 
            Expect.equal eNEQ1 [Value.u8 0uy] "8-bit zero is not equal to 0-bit zero (unit)"
            Expect.isNone (ProgEval.interpret pNEQZ noEffects [Value.nat 0UL]) 
                            "neq-zero on unit"

            // test gcd 
            let pGCD = getfn mgcd "gcd"
            Expect.equal (ProgVal.static_arity pGCD) (Some struct(2,1)) "arity gcd"
            let eGCD = doEval pGCD noEffects [Value.u32 462u; Value.u64 1071UL]
            Expect.equal eGCD [Value.nat 21UL] "gcd computed "


