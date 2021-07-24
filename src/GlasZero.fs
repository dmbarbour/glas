namespace Glas

/// This file implements the g0 (Glas zero) bootstrap syntax. To simplify imports,
/// we cannot use languages other than g0 during bootstrap. 
/// 
/// The g0 syntax is designed for simplicity over usability or extensibility.
/// Thus, first goal after bootstrap is to build a better syntax. 
///
/// I'm using FParsec to provide decent error messages without too much effort.
module Zero =
    open FParsec

    /// The g0 syntax has a ton of reserved words.
    /// This includes all the basic ops.
    let reservedWords = 
        [ "dip"; "data"; "seq"
        ; "cond"; "try"; "then"; "else"
        ; "loop"; "while"; "do"
        ; "env"; "with"
        ; "prog"; "note"
        // namespace
        ; "from"; "open"; "import"; "as"
        // reserved for annotations
        ; "type"
        ; "memo"; "accel"; "stow"
        ; "assume"; "assert"; "test"
        ] 
        |> List.append (List.map Program.opStr Program.op_list) 
        |> Set.ofList

    // During parse, we'll often import a few modules.
    type LoadedModules = Map<string, Value>



    /// To detect cyclic dependencies, record which module we're loading.
    type Loading = string list

    /// We can generally treat a word as a string
    type Word = string


    /// A word is a string that re


