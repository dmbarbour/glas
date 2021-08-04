namespace Glas

/// This module describes the FileSystem search algorithms and effects
/// for Glas modules. The main goal is to find a file with a suitable name.
/// This implementation does not attempt to recognize or report ambiguity.
/// 
/// Normally, GLAS_PATH environment variable is used to determine where
/// to search if a local search fails. A 'local' search is determined 
/// based on a file path for a file currently being processed.
module LoadModule =
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
    let findModuleInFolder (m:ModuleName) (dir:FolderPath) : FilePath list =
        if not (Directory.Exists(dir)) then [] else
        let subDir = Path.Combine(dir, m) 
        let folders =
            if not (Directory.Exists(subDir)) then Seq.empty else
            Directory.EnumerateFiles(subDir) |> Seq.filter (matchModuleName "public")
        let files = Directory.EnumerateFiles(dir) |> Seq.filter (matchModuleName m)
        Seq.append folders files |> Seq.toList

    /// Return first matching files for directories on GLAS_PATH.
    /// Does not continue searching GLAS_PATH after a match is found.
    let rec findModuleInPathList (m:ModuleName) (dirs: FolderPath list) : FilePath list =
        match dirs with 
        | d::dirs' -> 
            let dm = findModuleInFolder m d 
            if not (List.isEmpty dm) then dm else
            findModuleInPathList m dirs'
        | [] -> []

    let readGlasPath () : FolderPath list =
        let envPath = Environment.GetEnvironmentVariable("GLAS_PATH")
        if isNull envPath then [] else
        envPath.Split(';', StringSplitOptions.None) |> List.ofArray

    type Loader =
        // ModuleLoader assumes another effects handler is available for logging.
        // Also, any request other than 'load' is forwarded to this handler.
        val private NonLoadEff : IEffHandler 

        // A g0 compile function must be provided. This could be the 'compile' 
        // defined in GlasZero module, or based on the compiled language-g0 
        // module to support bootstrap. We assume g0 requires a string input.
        val private CompileG0 : IEffHandler -> string -> Value option

        // To resist cyclic dependencies, track which files we are actively
        // loading. Ideally, we'd also track dependencies precisely, but it
        // seems unnecessary for this bootstrap implementation - I'll just
        // log which cycles are noticed rather than attempt to detect all of
        // them.
        val mutable private Loading : FilePath list

        // When cycles are detected, record them. We cannot do much with them
        // at the moment, but perhaps later. 
        val mutable private Cycles : Set<FilePath list>

        // To avoid unnecessary rework, cache values for modules we've already
        // loaded. Ideally, we'd support a persistent cache, but that can be
        // deferred until after bootstrap.
        val mutable private Cache : Map<FilePath, Value option>

        new (g0,eff0) =
            { NonLoadEff = eff0
            ; CompileG0 = g0
            ; Loading = []
            ; Cache = Map.empty  
            ; Cycles = Set.empty
            }

        /// Obtain the 'compile' program associated with a file suffix.
        member ll.LoadCompileFunction fileSuffix : Program option =
            if String.IsNullOrEmpty fileSuffix then None else
            let langMod = "language-" + fileSuffix
            match ll.LoadModule langMod with
            | Some (Value.FullRec ["compile"] ([vCompile], _)) ->
                match vCompile with
                | Program.Program pCompile ->
                    match Program.static_arity pCompile with
                    | Some struct(1,1) ->
                        // success! everything else is different errors. 
                        Some pCompile
                    | Some struct(a,b) -> 
                        logError ll (sprintf "module %s.compile has bad arity %d--%d" langMod a b)
                        None
                    | None -> 
                        logError ll (sprintf "module %s.compile fails static arity check" langMod)
                        None
                | _ -> 
                    logError ll (sprintf "module %s.compile is not a Glas program" langMod)
                    None
            | Some _ ->
                logError ll (sprintf "module %s.compile does not exist" langMod)
                None
            | None ->
                logError ll (sprintf "module %s does not exist" langMod)
                None


        member private ll.Compile (langs : string list) (v0 : Value) : Value option = 
            match langs with
            | [] -> Some v0
            | ("g0"::langs') ->
                match v0 with
                | Value.String s ->
                    match ll.CompileG0 (ll :> IEffHandler) s with
                    | Some v' -> ll.Compile langs' v'
                    | None -> None
                | _ ->
                    logError ll (sprintf "input to g0 is not a string: %A" v0)
                    None
            | (lang::langs') ->
                match ll.LoadCompileFunction lang with
                | Some p ->
                    let e0 : Program.Interpreter.RTE = { DS = [v0]; ES = []; IO = (ll :> IEffHandler) }
                    match Program.Interpreter.interpret p e0 with
                    | Some e' -> ll.Compile langs' (e'.DS.[0])
                    | None -> None
                | None ->
                    // assuming the interpreter outputs suitable log messages
                    None

        member private ll.LoadFileBasic (fp : FilePath) : Value option =
            // attempt to read the file. Need to deal with permissions errors, etc..
            try 
                let langs = Path.GetFileName(fp).Split('.') |> Array.toList |> List.tail |> List.rev
                let bytes = File.ReadAllBytes(fp)
                let result = ll.Compile langs (Value.ofBinary bytes)
                if Option.isNone result then
                    logError ll (sprintf "compilation failed for file %s" fp)
                result
            with 
            | e -> 
                logError ll (sprintf "exception while loading file %s: %A" fp e)
                None

        // Wraps the basic load file activity with caching, cycle detection.
        member ll.LoadFile (fp : FilePath) : Value option =
            match Map.tryFind fp (ll.Cache) with
            | Some r -> // cache
                logInfo ll (sprintf "using cached result for file %s" fp)
                r
            | None when List.contains fp (ll.Loading) -> 
                let cycle = List.rev <| fp :: List.takeWhile ((<>) fp) ll.Loading
                ll.Cycles <- Set.add cycle ll.Cycles
                logError ll (sprintf "dependency cycle detected! %A" cycle)
                None
            | None ->
                let ld0 = ll.Loading
                ll.Loading <- fp :: ld0
                try 
                    let r = ll.LoadFileBasic fp
                    ll.Cache <- Map.add fp r ll.Cache
                    r
                finally
                    ll.Loading <- ld0

        /// Load a module
        member ll.LoadModule (m : ModuleName) : Value option =
            let localDir =  
                match ll.Loading with
                | [] -> Directory.GetCurrentDirectory()
                | (hd::_) -> Path.GetDirectoryName(hd)
            let searchPath = localDir :: readGlasPath()
            match findModuleInPathList m searchPath with
            | [] -> 
                logWarn ll (sprintf "module %s not found (searched %A)" m searchPath)
                None
            | [fp] ->
                logInfo ll (sprintf "loading module %s from file %s" m fp) 
                ll.LoadFile fp
            | ps ->
                logError ll (sprintf "module %s is ambiguous; found %A" m ps)
                None


        interface IEffHandler with
            // Handle 'load' effects. Forward everything else.
            // Might later add an association from files to log messages.
            member ll.Eff v =
                match v with
                | Value.Variant "load" vLoad ->
                    match vLoad with
                    | Value.AnyVariant (s,U) ->
                        ll.LoadModule s
                    | _ -> None
                | _ -> ll.NonLoadEff.Eff v 
        interface ITransactional with
            // Loader assumes external modules are constant. The cache is thus
            // valid across transaction boundaries. But we do pass transactions
            // to the NonLoadEff.
            member ll.Try () = 
                ll.NonLoadEff.Try ()
            member ll.Commit () = 
                ll.NonLoadEff.Commit ()
            member ll.Abort () = 
                ll.NonLoadEff.Abort ()

