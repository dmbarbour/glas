namespace Glas

/// This module describes the FileSystem search algorithms and effects
/// for Glas modules. The main goal is to find a file with a suitable name.
/// This implementation does not attempt to recognize or report ambiguity.
/// 
/// Normally, GLAS_PATH environment variable is used to determine where
/// to search if a local search fails. A 'local' search is determined 
/// based on a file path for a file currently being processed.
module FindModule =
    open System
    open System.IO
    open Glas.Effects

    // type aliases for documentation
    type FolderPath = string
    type FilePath = string
    type ModuleName = string

    let private matchModuleName (m:ModuleName) (fullPath:FilePath) : bool =
        Path.GetFileName(fullPath).Split('.').[0] = m

    /// Return files that match module name within a folder.
    /// This includes a public module within a subfolder.
    let findModuleInFolder (dir:FolderPath) (m:ModuleName) : FilePath list =
        if not (Directory.Exists(dir)) then [] else
        let subDir = Path.Combine(dir, m) 
        let folders =
            if not (Directory.Exists(subDir)) then Seq.empty else
            Directory.EnumerateFiles(subDir) |> Seq.filter (matchModuleName "public")
        let files = Directory.EnumerateFiles(dir) |> Seq.filter (matchModuleName m)
        Seq.append folders files |> Seq.toList

    /// Return first matching files for directories on GLAS_PATH.
    /// Does not continue searching GLAS_PATH after a match is found.
    let findModuleInGlasPath (m:ModuleName) : FilePath list =
        let envPath = Environment.GetEnvironmentVariable("GLAS_PATH")
        if isNull envPath then [] else // null results are possible from System.
        let rec search ps =
            match ps with
            | p::ps' -> 
                let pm = findModuleInFolder p m 
                if not (List.isEmpty pm) then pm else
                search ps'
            | [] -> []
        let paths = envPath.Split(';', StringSplitOptions.None) |> List.ofArray
        search paths

    type ModuleLoader =
        // ModuleLoader assumes another effects handler is available for logging.
        // Also, any request other than 'load' is forwarded to this handler.
        val private Eff : IEffHandler 

        // A g0 compile function must be provided. This could be based on the
        // built-in g0 compileFile, or based on a bootstrap cycle. 
        val private CompileG0 : IEffHandler -> Value -> Value option

        // To resist cyclic dependencies, we'll track which files we're loading.
        // We'll also log which files we're loading to simplify things.
        val mutable private Loading : FilePath list

        // To avoid rework, we'll cache files we've previously loaded. This
        // includes caching failed efforts.
        val mutable private Cache : Map<FilePath, Value option>

