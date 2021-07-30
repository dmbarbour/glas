module Glas.TestGlasZero
    open Expecto
    open FParsec
    open Glas.Zero

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
                Expect.equal "foo" (pw "foo ; with comment\n") "word with comment"

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
                let zpd = Parser.parseData
                let pd s = doParse zpd s
                assert(toB 6 23UL = Bits.ofList [false; true; false; true; true; true])

                Expect.equal (toB 6 23UL) (pd "0b010111") "equal bit strings"
                Expect.equal (toB 1 1UL) (pd "0b1") "minimal bit string 1"
                Expect.equal (toB 1 0UL) (pd "0b0") "minimal bit string 0"
                Expect.equal (toB 8 23UL) (pd "0x17") "equal hex strings"
                Expect.equal (toB 5 23UL) (pd "23 ;  comment\n") "equal min-width numbers"
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
                Expect.equal "" (ps "\"\" ; <- it's empty!\r") "The empty string."

                // limiting strings to ASCII range, no C0 except SP, etc.
                failParse zps "\""
                failParse zps "\"\n\""
                failParse zps "\"\t\""
                failParse zps "\"→\""

            testCase "programs" <| fun () ->
                let zpp = Parser.parseProgBody
                let pp s = doParse zpp s

                // these shouldn't parse as words
                failParse zpp "dip" 
                failParse zpp "while"
                failParse zpp "with"
                failParse zpp "try"

                // A simple program.
                let p1 =  [Sym (toB 12 0xabcUL); Sym (Value.label "foo"); Str "test"; Call "swap"]
                Expect.equal p1 (pp "0xaBc 'foo \"test\" \n  swap  ") "test simple program"

                let p2 = [Dip []]
                Expect.equal p2 (pp "dip[]") "dip nop 1"
                Expect.equal p2 (pp "dip []") "dip nop 1"
                Expect.equal p2 (pp "dip [  ]") "dip nop 1"
                Expect.equal p2 (pp "dip \r\r\r\r[ \r\n\t ]") "dip nop 1"

                let p3 = [Dip[Call "foo"]]
                Expect.equal p3 (pp "dip[foo]") "dip foo 1"
                Expect.equal p3 (pp "dip [foo]") "dip foo 2"
                Expect.equal p3 (pp "dip [ foo ]") "dip foo 3"
                Expect.equal p3 (pp "dip \n[ \nfoo\r ]") "dip foo 4"
                Expect.equal p3 (pp "dip ;comment\n [foo]") "dip foo 1"

                let p4 = [Cond(Try=[Call "foo"], Then=[Call "bar"], Else=[Call "baz"]); Call "qux"]
                Expect.equal p4 (pp "try [foo] then [\nbar\n]\nelse [baz ;comment\n] qux ; more comments\n") "try then else"

                let p5 = [Env(With=[Call "x"], Do=[Call "y"])]
                Expect.equal p5 (pp "with [x] do [y]") "environment"

                let p6 = [Loop(While=[Call "a"], Do=[Call "b"])]
                Expect.equal p6 (pp "while[a]do[b]") "loop"


            testCase "imports" <| fun () -> 
                let zpf = Parser.parseFrom
                let pf s = doParse zpf s

                let p1 = From (Src="a", Imports=[
                            Import(Word="bar",As=Some "foo")
                            Import(Word="qux",As=None)
                            Import(Word="foo",As=Some "bar")
                            Import(Word="baz",As=None)
                            ]) 
                Expect.equal p1 (pf "from a import bar as foo, qux, foo as bar, baz") "from module import words"
                Expect.equal "abc" (doParse Parser.parseOpen "open abc") "parse open"


            testCase "toplevel" <| fun () ->
                let p1 = String.concat "\n" [ 
                    "open xyzzy ; inherit definitions"
                    "from foo import a, b as c, d"
                    "from bar import i as j, k"
                    ""
                    "prog baz [ ; defining baz"
                    "  a c j h"
                    "]"
                ]
                let fromFoo = From(Src="foo", Imports=[
                    Import(Word="a", As=None)
                    Import(Word="b", As=Some "c")
                    Import(Word="d", As=None)
                ])
                let fromBar = From(Src="bar", Imports=[
                    Import(Word="i",As=Some "j")
                    Import(Word="k",As=None)
                ])
                let defBaz = Prog(Name="baz", Body=[
                    Call "a"; Call "c"; Call "j"; Call "h"
                ])
                let p1act =
                    { Open = Some "xyzzy"
                      From = [fromFoo; fromBar]
                      Defs = [defBaz]
                    }
                Expect.equal p1act (doParse Parser.parseTopLevel p1) "parse a program"


        ]
