namespace Glas

/// This module describes the FileSystem search algorithm for a named
/// Glas module. The main goal is to find a file with a suitable name.
/// This implementation does not attempt to recognize or report ambiguity.
/// 
/// Normally, GLAS_PATH environment variable is used to determine where
/// to search if a local search fails. A 'local' search is determined 
/// based on a file path for a file currently being processed.
module FindModule =
    open System
    open System.IO

    // type aliases for documentation
    type FolderPath = string
    type FilePath = string
    type ModuleName = string

    let private matchModuleName (m:ModuleName) (fullPath:FilePath) : bool =
        m = Path.GetFileNameWithoutExtension(fullPath)

    /// Return a files that matches module name within a folder.
    /// This includes a public module within a subfolder.
    let findModuleInFolder (dir:FolderPath) (m:ModuleName) : FilePath option =
        if not (Directory.Exists(dir)) then None else
        let subDir = Path.Combine(dir, m) 
        let folders =
            if not (Directory.Exists(subDir)) then Seq.empty else
            Directory.EnumerateFiles(subDir) |> Seq.filter (matchModuleName "public")
        let files = Directory.EnumerateFiles(dir) |> Seq.filter (matchModuleName m)
        let lFiles = Seq.append folders files |> Seq.toList
        match lFiles with
        | [f] -> Some f 
        | [] -> None
        | (f::_) ->
            eprintfn "WARNING: ambiguous module (using head) %A" lFiles
            Some f // just go with the first result

    /// Return first file that matches a module name on GLAS_PATH.
    let findModuleInGlasPath (m:ModuleName) : FilePath option =
        let envPath = Environment.GetEnvironmentVariable("GLAS_PATH")
        if isNull envPath then None else // null results are possible from System.
        let rec search ps =
            match ps with
            | p::ps' -> 
                let pm = findModuleInFolder p m 
                if Option.isSome pm then pm else
                search ps'
            | [] -> None 
        let paths = envPath.Split(';', StringSplitOptions.None) |> List.ofArray
        search paths

    /// Find a module relative to an initial filepath. This first searches the local
    /// directory of the file, then searches module paths.
    let findModuleRelFile (f0:FilePath) (m:ModuleName) : FilePath option =
        let dir = Path.GetDirectoryName(f0)
        let localModule = findModuleInFolder dir m
        if Option.isSome localModule then localModule else
        findModuleInGlasPath m
