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
    open ProgVal
    open ProgEval

    // type aliases for documentation
    type FolderPath = string
    type FilePath = string
    type ModuleName = string

    let private matchModuleName (m:ModuleName) (fullPath:FilePath) : bool =
        Path.GetFileName(fullPath).Split('.').[0] = m

    let private findModuleAsFile (m:ModuleName) (dir:FolderPath) : FilePath list =
        Directory.EnumerateFiles(dir) |> Seq.filter (matchModuleName m) |> List.ofSeq

    let private findModuleAsFolder (m:ModuleName) (dir:FolderPath) : FilePath list =
        let subdir = Path.Combine(dir,m)
        if not (Directory.Exists(subdir)) then [] else
        match findModuleAsFile "public" subdir with
        | [] -> [Path.Combine(subdir, "public.g0")] // missing file
        | files -> files

    let private findModule (m:ModuleName) (dir:FolderPath) : FilePath list =
        List.append (findModuleAsFolder m dir) (findModuleAsFile m dir)

    let rec private moduleSearch (m:ModuleName) (searchPath:FolderPath list) : FilePath list =
        match searchPath with
        | [] -> []
        | (dir::searchPath') ->
            match findModule m dir with
            | [] -> moduleSearch m searchPath'
            | files -> files 

    let private readGlasPath () = 
        let envPath = Environment.GetEnvironmentVariable("GLAS_PATH")
        if isNull envPath then [] else
        envPath.Split(';', StringSplitOptions.None) 
            |> Array.map (fun s -> s.Trim())
            |> List.ofArray
   

    // wrap a compiler function for arity 1--1
    let private _compilerFn (p:Program) (ll:IEffHandler) =
        let preLinkedEval = eval p ll 
        fun v ->
            match preLinkedEval [v] with
            | Some (r::_) -> Some r 
            | _ -> None
    
    // factored out some error handling
    let private _expectCompiler (ll:IEffHandler) (src:FilePath) (vOpt:Value option) =
        match vOpt with
        | Some (Value.FullRec ["compile"] ([pCompile], _)) ->
            match stackArity pCompile with
            | ProgVal.Arity (a,b) when (a = b) && (1 >= a) -> 
                Some pCompile
            | ar ->
                logError ll (sprintf "%s compile has incorrect arity %A" src ar)
                None
        | Some _ ->
            logError ll (sprintf "%s does not define 'compile'" src)
            None
        | None -> 
            logError ll (sprintf "%s could not be loaded" src)
            None

    type Loader =
        // Effects other than 'load'. Logging is assumed.
        val private NonLoadEff : IEffHandler 

        // Compiler for G0 must be provided upon construction.
        val mutable private CompileG0 : Value -> Value option

        // To resist cyclic dependencies, track which files we are loading.
        val mutable private Loading : FilePath list

        // Cache results per file.
        val mutable private Cache : Map<FilePath, Value option>

        // Cache compiler functions.
        val mutable private CompilerCache : Map<FilePath, ((Value -> Value option) option)>

        new (linkG0,eff0) as ll =
            { NonLoadEff = eff0
            ; CompileG0 = fun _ -> invalidOp "todo: link the g0 compiler"
            ; Loading = []
            ; Cache = Map.empty  
            ; CompilerCache = Map.empty
            } then // link the g0 compiler 
            ll.CompileG0 <- linkG0 (ll :> IEffHandler)

        member private ll.GetCompiler (fileSuffix : string) : (Value -> Value option) option =
            if String.IsNullOrEmpty(fileSuffix) then None else
            if "g0" = fileSuffix then Some (ll.CompileG0) else 
            let langMod = "language-" + fileSuffix
            match ll.FindModule langMod with
            | None -> None
            | Some fp ->
                match Map.tryFind fp ll.CompilerCache with
                | Some result -> result
                | None -> 
                    let result = 
                        match _expectCompiler ll langMod (ll.LoadFile fp) with
                        | Some pCompile -> Some (_compilerFn pCompile ll)
                        | None -> None
                    ll.CompilerCache <- Map.add fp result ll.CompilerCache
                    result

        member private ll.Compile fileSuffix (v0 : Value) : Value option = 
            match ll.GetCompiler fileSuffix with
            | Some p -> p v0
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
                    logError ll (sprintf "error loading file %s:  %A" fp e)
                    None
            List.foldBack appLang langs v0

        /// Load a specified file as a module.
        member ll.LoadFile (fp : FilePath) : Value option =
            match Map.tryFind fp (ll.Cache) with
            | Some r -> // use cached value 
                logInfo ll (sprintf "using cached result for file %s" fp)
                r
            | None when List.contains fp (ll.Loading) -> 
                // report error, leave to programmers to solve.
                let cycle = List.rev <| fp :: List.takeWhile ((<>) fp) ll.Loading
                logError ll (sprintf "dependency cycle detected! %s" (String.concat ", " cycle))
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
            match moduleSearch m (localDir :: readGlasPath()) with
            | [] -> 
                logWarn ll (sprintf "module %s not found" m)
                None
            | [fp] ->
                logInfo ll (sprintf "loading module %s from file %s" m fp) 
                Some fp
            | ps ->
                logError ll (sprintf "module %s is ambiguous; found %s" m (String.concat ", " ps))
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
                    | Value.String m ->
                        ll.LoadModule m
                    | _ -> 
                        logWarn ll (sprintf "unrecognized module %s (expect string)" (Value.prettyPrint vLoad)) 
                        None
                | Value.Variant "log" vMsg ->
                    // add filepath to log messages
                    let vMsg' = 
                        match ll.Loading with
                        | (f::_) -> Value.record_insert (Value.label "file") (Value.ofString f) vMsg
                        | _ -> vMsg
                    ll.NonLoadEff.Eff (Value.variant "log" vMsg')
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

    let private _builtInG0 ll v = 
        match v with
        | Value.String s -> Zero.compile ll s
        | _ -> 
            logError ll "built-in g0 requires string input"
            None

    /// Loader without bootstrapping. Simply use the built-in g0.
    let nonBootStrapLoader (nle : IEffHandler) : Loader =
        Loader(_builtInG0, nle)

    let private _findG0 ll =
        match moduleSearch "language-g0" (readGlasPath()) with
        | [fp] -> Some fp
        | [] -> 
            None
        | ambList ->
            logError ll (sprintf "bootstrap failed: language-g0 ambiguous: %s" (String.concat ", " ambList))
            None

    /// Attempt to bootstrap the g0 language, then use the language-g0
    /// module for the loader.
    let tryBootStrapLoader (nle : IEffHandler) : Loader option = 
        match _findG0 nle with
        | None -> None 
        | Some fp ->
            let ll0 = nonBootStrapLoader nle
            match _expectCompiler ll0 "language-g0 via built-in g0" (ll0.LoadFile fp) with
            | None -> None
            | Some p0 ->
                // logInfo nle "bootstrap: language-g0 compiled using built-in g0"
                let ll1 = Loader(_compilerFn p0, nle)
                match _expectCompiler ll1 "language-g0 via language-g0" (ll1.LoadFile fp) with
                | None -> None 
                | Some p1 -> 
                    // logInfo nle "bootstrap: language-g0 compiled using language-g0"
                    let ll2 = Loader(_compilerFn p1, nle)
                    match ll2.LoadFile fp with
                    | None -> None
                    | Some p2 when (p1 <> p2) ->
                        logError nle "language-g0 compile fails to exactly rebuild itself"
                        None
                    | Some _ ->
                        // logInfo nle "language-g0 bootstrap successful!"
                        Some ll2 
