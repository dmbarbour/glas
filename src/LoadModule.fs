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

    let private findModuleAsFile (m:ModuleName) (dir:FolderPath) : FilePath list =
        let matching_name (fp : FilePath) : bool = 
            Path.GetFileName(fp).Split('.').[0] = m
        Directory.EnumerateFiles(dir) |> Seq.filter matching_name |> List.ofSeq

    let private findModuleAsFolder (m:ModuleName) (dir:FolderPath) : FilePath list =
        let subdir = Path.Combine(dir,m)
        if not (Directory.Exists(subdir)) then [] else
        match findModuleAsFile "public" subdir with
        | [] -> [Path.Combine(subdir, "public.g0")] // default missing file
        | files -> files // should be list of one file

    let private findLocalModule (m:ModuleName) (localDir:FolderPath): FilePath list =
        List.append (findModuleAsFile m localDir) (findModuleAsFolder m localDir)

    let private findGlobalModule (m:ModuleName) : FilePath list =
        let envPath = Environment.GetEnvironmentVariable("GLAS_PATH")
        if isNull envPath then [] else
        let searchPaths = 
                envPath.Split(Path.PathSeparator, StringSplitOptions.None)
                |> Array.map (fun s -> s.Trim()) // remove surrounding whitespace
                |> List.ofArray
        let rec loop ps =
            match ps with
            | (p::ps) ->
                match findModuleAsFolder m p with
                | [] -> loop ps
                | findings -> findings
            | [] -> []
        loop searchPaths

    // wrap a compiler function for arity 1--1
    let private _compilerFn (p:Program) (ll:IEffHandler) =
        let preLinkedEval = eval p ll 
        fun v ->
            match preLinkedEval [v] with
            | Some [r] -> ValueSome r 
            | _ -> ValueNone
    
    // factored out some error handling
    let private _expectCompiler (ll:IEffHandler) (src:string) (vOpt:Value voption) =
        match vOpt with
        | ValueSome (Value.FullRec ["compile"] ([pCompile], _)) ->
            match stackArity pCompile with
            | Arity (a,b) when ((a = b) && (1 >= a)) -> 
                ValueSome pCompile
            | ar ->
                logError ll (sprintf "%s.compile has incorrect arity %A" src ar)
                ValueNone
        | ValueSome _ ->
            logError ll (sprintf "%s does not define 'compile'" src)
            ValueNone
        | ValueNone -> ValueNone

    type Loader =
        // Effects other than 'load'. Logging is assumed.
        val private NonLoadEff : IEffHandler 

        // To resist cyclic dependencies, track which files we are loading.
        val mutable private Loading : FilePath list

        // Cache results per file.
        val mutable private Cache : Map<FilePath, Value voption>

        // Cached compiler functions. The "g0" compiler is added at construction.
        val mutable private Compilers : Map<string, ((Value -> Value voption) voption)>

        new (linkG0,eff0) as ll =
            { NonLoadEff = eff0
            ; Loading = []
            ; Cache = Map.empty  
            ; Compilers = Map.empty
            } then // link the g0 compiler 
            let g0c = linkG0 (ll :> IEffHandler)
            ll.Compilers <- Map.add "g0" (ValueSome g0c) (ll.Compilers)

        member private ll.GetCompiler (fileExt : string) : (Value -> Value voption) voption =
            if String.IsNullOrEmpty(fileExt) then ValueNone else
            match Map.tryFind fileExt ll.Compilers with
            | Some result -> result // cached result
            | None ->
                let m = "language-" + fileExt
                let result = 
                    match _expectCompiler ll m (ll.LoadGlobalModule m) with
                    | ValueSome pCompile -> ValueSome (_compilerFn pCompile ll)
                    | ValueNone -> ValueNone
                ll.Compilers <- Map.add fileExt result ll.Compilers
                result

        member private ll.Compile fileExt (v0 : Value) : Value voption = 
            match ll.GetCompiler fileExt with
            | ValueSome p -> p v0
            | ValueNone -> ValueNone

        member private ll.LoadFileBasic (fp : FilePath) : Value voption =
            let appLang fileSuffix vOpt =
                match vOpt with
                | ValueSome v -> ll.Compile fileSuffix v
                | ValueNone -> ValueNone
            let langs = Path.GetFileName(fp).Split('.') |> Array.toList |> List.tail
            let v0 = 
                try fp |> File.ReadAllBytes |> Value.ofBinary |> ValueSome
                with 
                | e -> 
                    logError ll (sprintf "error loading file %s:  %A" fp e)
                    ValueNone
            // extensions apply from outer to inner
            List.foldBack appLang langs v0

        /// Load a specified file as a module.
        member ll.LoadFile (fp : FilePath) : Value voption =
            match Map.tryFind fp (ll.Cache) with
            | Some r -> // use cached value 
                logInfo ll (sprintf "using cached result for file %s" fp)
                r
            | None when List.contains fp (ll.Loading) -> 
                // report cyclic dependency, leave to programmers to solve.
                let cycle = List.rev <| fp :: List.takeWhile ((<>) fp) ll.Loading
                logError ll (sprintf "dependency cycle detected! %s" (String.concat ", " cycle))
                ValueNone
            | None -> 
                logInfo ll (sprintf "loading file %s" fp)
                let ld0 = ll.Loading
                ll.Loading <- fp :: ld0
                try 
                    let r = ll.LoadFileBasic fp
                    ll.Cache <- Map.add fp r ll.Cache
                    r
                finally
                    ll.Loading <- ld0

        member ll.LoadLocalModule (m : ModuleName) : Value voption =
            let localDir = 
                match ll.Loading with
                | (fp::_) -> Path.GetDirectoryName(fp)
                | [] -> Directory.GetCurrentDirectory()
            match findLocalModule m localDir with
            | [fp] -> ll.LoadFile fp
            | [] ->
                logWarn ll (sprintf "local module %s not found in %s" m localDir)
                ValueNone
            | ps ->
                logWarn ll (sprintf "local module %s ambiguous in %s" m localDir)
                ValueNone

        member ll.LoadGlobalModule (m : ModuleName) : Value voption =
            match findGlobalModule m with
            | [fp] -> ll.LoadFile fp
            | [] ->
                logWarn ll (sprintf "global module %s not found" m)
                ValueNone
            | fps ->
                // most likely cause is more than one 'public' file.
                logWarn ll (sprintf "global module %s ambiguous [%s]" m (String.concat "; " fps))
                ValueNone

        interface IEffHandler with
            // Handle 'load' effects. Forward everything else.
            member ll.Eff v =
                match v with
                | Value.Variant "load" vLoad ->
                    match vLoad with
                    | Value.Variant "local" (Value.String m) ->
                        ll.LoadLocalModule m 
                    | Value.Variant "global" (Value.String m) ->
                        ll.LoadGlobalModule m
                    | _ -> 
                        logWarn ll (sprintf "unrecognized ModuleRef %s" (Value.prettyPrint vLoad)) 
                        ValueNone
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
            ValueNone

    /// Loader without bootstrapping. Simply use the built-in g0.
    let nonBootStrapLoader (nle : IEffHandler) : Loader =
        Loader(_builtInG0, nle)

    /// Attempt to bootstrap the g0 language, then use the language-g0
    /// module for the loader.
    let tryBootStrapLoader (nle : IEffHandler) : Loader voption = 
        let ll0 = nonBootStrapLoader nle
        let src0 = "(as compiled by built-in g0) language-g0"
        match _expectCompiler ll0 src0 (ll0.LoadGlobalModule "language-g0") with
        | ValueNone -> ValueNone
        | ValueSome p0 ->
            // logInfo nle "bootstrap: language-g0 compiled using built-in g0"
            let ll1 = Loader(_compilerFn p0, nle)
            let src1 = "(as compiled by language-g0) language-g0"
            match _expectCompiler ll1 src1 (ll1.LoadGlobalModule "language-g0") with
            | ValueNone -> ValueNone 
            | ValueSome p1 -> 
                // logInfo nle "bootstrap: language-g0 compiled using language-g0"
                let ll2 = Loader(_compilerFn p1, nle)
                match ll2.LoadGlobalModule "language-g0" with
                | ValueSome (Value.FullRec ["compile"] ([p2],_)) when (Value.eq p2 p1) ->
                    // logInfo nle "language-g0 bootstrap successful!"
                    ValueSome ll2 
                | _ -> 
                    logError nle "language-g0.compile fails to exactly rebuild itself"
                    ValueNone
