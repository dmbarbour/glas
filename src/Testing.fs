namespace Glas

/// This module defines functions to support automated testing of Glas
/// programs.
module Testing =
    open System.IO
    open LoadModule
    open Effects

    /// finds test-files only, not subdirs.
    let findTestModulesInFolder (dir:FolderPath) : ModuleName list =
        if not (Directory.Exists(dir)) then [] else
        Directory.EnumerateFiles(dir) 
            |> Seq.map (fun fp -> Path.GetFileName(fp).Split('.').[0])
            |> Seq.filter (fun m -> m.StartsWith("test")) 
            |> Seq.toList

    // stream 