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

        // To avoid unnecessary rework, cache values for modules we've already
        // loaded. Ideally, we'd support a persistent cache, but that can be
        // deferred until after bootstrap.
        val mutable private Cache : Map<FilePath, Value option>

        // We'll also cache valid language module compile functions to reduce
        // rework a little. Here 'valid' just means it parses and passes the
        // 1--1 static arity check.
        val mutable private CompilerCache : Map<FilePath, Program option>

        new (g0,eff0) =
            { NonLoadEff = eff0
            ; CompileG0 = g0
            ; Loading = []
            ; Cache = Map.empty  
            ; CompilerCache = Map.empty
            }

        member ll.CompileCompiler fp langMod = 
            match ll.LoadFile fp with
            | Some (Value.FullRec ["compile"] ([vCompile], _)) ->
                match vCompile with
                | Program.Program pCompile ->
                    match Program.static_arity pCompile with
                    | Some struct(1,1) ->
                        Some pCompile // success!
                    | Some struct(a,b) -> 
                        logError ll (sprintf "%s compile has incorrect static arity %d--%d (expecting 1--1)" langMod a b)
                        None
                    | None -> 
                        logError ll (sprintf "%s compile fails static arity check" langMod)
                        None
                | _ -> 
                    logError ll (sprintf "%s compile is not a Glas program" langMod)
                    None
            | Some _ ->
                logError ll (sprintf "module %s does not define 'compile'" langMod)
                None
            | None ->
                logError ll (sprintf "module %s could not be loaded" langMod)
                None

        member private ll.GetCompiler (fileSuffix : string) : Program option =
            if String.IsNullOrEmpty(fileSuffix) then None else
            let langMod = "language-" + fileSuffix
            match ll.FindModule langMod with
            | None -> None
            | Some fp ->
                match Map.tryFind fp ll.CompilerCache with
                | Some result -> result
                | None -> 
                    let result = ll.CompileCompiler fp langMod
                    ll.CompilerCache <- Map.add fp result ll.CompilerCache
                    result

        member private ll.Compile fileSuffix (v0 : Value) : Value option = 
            if "g0" = fileSuffix then
                match v0 with
                | Value.String s -> 
                    ll.CompileG0 (ll :> IEffHandler) s
                | _ ->
                    logError ll "input to g0 compiler is not a string"
                    None
            else
                match ll.GetCompiler fileSuffix with
                | Some p ->
                    let e0 : Program.Interpreter.RTE = { DS = [v0]; ES = []; IO = (ll :> IEffHandler) }
                    match Program.Interpreter.interpret p e0 with
                    | Some e' -> Some (e'.DS.[0])
                    | None -> None // interpreter may output error messages.
                | None -> None // GetCompiler emits reason to log

        member private ll.LoadFileBasic (fp : FilePath) : Value option =
            let appLang fileSuffix vOpt =
                match vOpt with
                | Some v -> ll.Compile fileSuffix v
                | None -> None
            let langs = Path.GetFileName(fp).Split('.') |> Array.toList |> List.tail
            let v0 = 
                try fp |> File.ReadAllBytes |> Value.ofBinary |> Some
                with 
                | e -> 
                    logError ll (sprintf "exception while loading file %s:  %A" fp e)
                    None
            List.foldBack appLang langs v0

        /// Load a specified file as a module.
        member ll.LoadFile (fp : FilePath) : Value option =
            match Map.tryFind fp (ll.Cache) with
            | Some r -> // use cached value 
                logInfo ll (sprintf "using cached result for file %s" fp)
                r
            | None when List.contains fp (ll.Loading) -> 
                let cycle = List.rev <| fp :: List.takeWhile ((<>) fp) ll.Loading
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

        /// Find a module.
        member ll.FindModule m : FilePath option = 
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
                Some fp
            | ps ->
                logError ll (sprintf "module %s is ambiguous; found %A" m ps)
                None

        /// Load a module
        member ll.LoadModule (m : ModuleName) : Value option =
            match ll.FindModule m with
            | None -> None
            | Some fp -> ll.LoadFile fp

        interface IEffHandler with
            // Handle 'load' effects. Forward everything else.
            member ll.Eff v =
                match v with
                | Value.Variant "load" vLoad ->
                    match vLoad with
                    | Value.AnyVariant (s,U) ->
                        ll.LoadModule s
                    | _ -> None
                | _ -> ll.NonLoadEff.Eff v 
        interface ITransactional with
            // Loader assumes external modules are constant during its lifespan.
            // The cache is thus valid across transaction boundaries. But we do
            // pass transactions onwards to the logger or other effects.
            member ll.Try () = 
                ll.NonLoadEff.Try ()
            member ll.Commit () = 
                ll.NonLoadEff.Commit ()
            member ll.Abort () = 
                ll.NonLoadEff.Abort ()


    /// Loader without bootstrapping. Simply use the built-in g0.
    let nonBootStrapLoader (nle : IEffHandler) : Loader =
        Loader(Zero.Compile.compile, nle)


    let private _findG0 ll =
        // only bootstrap from GLAS_PATH.
        match findModuleInPathList "language-g0" (readGlasPath()) with
        | [fp] -> Some fp
        | [] -> 
            logError ll "bootstrap failed: language-g0 not found on GLAS_PATH"
            None
        | ambList ->
            logError ll (sprintf "bootstrap failed: language-g0 ambiguous: %A" ambList)
            None

    let private _compileG0 (p : Program) (ll : IEffHandler) (s : string) : Value option =
        let e0 : Program.Interpreter.RTE = { DS = [Value.ofString s]; ES = []; IO = ll } 
        match Program.Interpreter.interpret p e0 with
        | None -> None
        | Some e' -> 
            match e'.DS with
            | [r] -> Some r
            | _ -> None

    /// Attempt to bootstrap the g0 language, then use the language-g0
    /// module for the loader.
    let tryBootStrapLoader (nle : IEffHandler) : Loader option = 
        match _findG0 nle with
        | None -> None
        | Some fp ->
            //logInfo nle (sprintf "bootstrap: language-g0 found at %s" fp)
            let ll0 = nonBootStrapLoader nle
            match ll0.CompileCompiler fp "language-g0" with
            | None -> None
            | Some p0 ->
                logInfo nle "bootstrap: language-g0 compiled using built-in g0"
                let ll1 = Loader(_compileG0 p0, nle)
                match ll1.CompileCompiler fp "language-g0" with
                | None -> None 
                | Some p1 -> 
                    logInfo nle "bootstrap: language-g0 compiled using language-g0"
                    let ll2 = Loader(_compileG0 p1, nle)
                    match ll2.CompileCompiler fp "language-g0" with
                    | None -> None
                    | Some p2 when (p1 <> p2) ->
                        logError nle "bootstrap failed: language-g0 does not exactly rebuild"
                        None
                    | Some _ ->
                        logInfo nle "bootstrap success! language-g0 is verified fixpoint"
                        Some ll2 
