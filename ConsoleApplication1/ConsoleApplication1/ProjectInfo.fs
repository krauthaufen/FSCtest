namespace Test


open System.IO


[<RequireQualifiedAccess>]
type Target =   
    | Exe
    | Library
    | WinExe
    | Module

[<RequireQualifiedAccess>]
type DebugType =
    | Off
    | Full
    | PdbOnly
    | Portable


type ProjectInfo =
    {
        project     : string
        isNewStyle  : bool
        references  : list<string>
        files       : list<string>
        defines     : list<string>
        target      : Target
        output      : Option<string>
        additional  : list<string>
        debug       : DebugType
    }

module ProjectInfo = 

    let ofFscArgs (isNewStyle : bool) (path : string) (args : list<string>) =
        let mutable parsed = Set.empty
        let path = Path.GetFullPath path
        let dir = Path.GetDirectoryName path

        let full (str : string) =
            if Path.IsPathRooted str then str
            else Path.Combine(dir, str)


        let removeArg (a : string) = parsed <- Set.add a parsed

        let references = 
            args |> List.choose (fun a ->
                if a.StartsWith "-r:" then removeArg a; Some (full (a.Substring 3))
                elif a.StartsWith "--reference:" then removeArg a; Some (full (a.Substring 12))
                else None
            )

        let files =
            args |> List.choose (fun a -> 
                if not (a.StartsWith "-") then
                    let isAssemblyInfo = (Path.GetFileName(a).ToLower().EndsWith "assemblyinfo.fs")
                    removeArg a
                    if not isAssemblyInfo then
                        Some (full a)
                    else
                        None
                else
                    None
            ) 

        let output =
            args |> List.tryPick (fun a ->
                if a.StartsWith "-o:" then removeArg a; Some (full (a.Substring 3))
                elif a.StartsWith "--out:" then removeArg a; Some (full (a.Substring 6))
                else None
            )

        let target =
            args |> List.tryPick (fun a ->
                if a.StartsWith "--target:" then
                    removeArg a
                    let target = a.Substring(9).Trim().ToLower()
                    match target with
                    | "exe" -> Some Target.Exe
                    | "library" -> Some Target.Library
                    | "winexe" -> Some Target.WinExe
                    | "module" -> Some Target.Module
                    | _ -> None
                else
                    None
            )

        let defines =
            args |> List.choose (fun a ->
                if a.StartsWith "-d:" then removeArg a; Some (a.Substring 3)
                elif a.StartsWith "--define:" then removeArg a; Some (a.Substring 9)
                else None
            )

        let hasDebug =
            args |> List.tryPick (fun a ->
                let rest = 
                    if a.StartsWith "-g" then Some (a.Substring(2).Replace(" ", ""))
                    elif a.StartsWith "--debug" then Some (a.Substring(7).Replace(" ", ""))
                    else None
                        
                match rest with
                | Some "" | Some "+" -> removeArg a; Some true
                | Some "-" -> removeArg a; Some false
                | _ -> None
            )

        let debugType =
            args |> List.tryPick (fun a ->
                let rest = 
                    if a.StartsWith "-g" then Some (a.Substring(2).Replace(" ", ""))
                    elif a.StartsWith "--debug" then Some (a.Substring(7).Replace(" ", ""))
                    else None
                        
                match rest with
                | Some ":full" -> removeArg a; Some DebugType.Full
                | Some ":pdbonly" -> removeArg a; Some DebugType.PdbOnly
                | Some ":portable" -> removeArg a; Some DebugType.Portable
                | _ -> None
            )

        let additional =
            args |> List.filter (fun a -> not (Set.contains a parsed))

        let debug =
            match hasDebug with
            | Some true -> defaultArg debugType DebugType.Full
            | Some false -> DebugType.Off
            | None -> defaultArg debugType DebugType.Full 

        {
            isNewStyle  = isNewStyle
            project     = path
            //fscArgs     = args
            references  = references
            files       = files
            target      = defaultArg target Target.Library
            defines     = defines
            additional  = additional
            output      = output
            debug       = debug
        }

    let toFscArgs (info : ProjectInfo) =
        [
            match info.output with
            | Some o -> yield sprintf "-o:%s" o
            | None -> ()

            match info.debug with
            | DebugType.Off ->
                ()
            | DebugType.Full -> 
                yield "-g"
                yield "--debug:full"
            | DebugType.Portable -> 
                yield "-g"
                yield "--debug:portable"
            | DebugType.PdbOnly -> 
                yield "-g"
                yield "--debug:pdbonly"
                
            match info.target with
            | Target.Exe -> yield "--target:exe"
            | Target.Library -> yield "--target:library"
            | Target.WinExe -> yield "--target:winexe"
            | Target.Module -> yield "--target:module"

            for d in info.defines do
                yield sprintf "-d:%s" d

            for a in info.additional do
                yield a

            for r in info.references do
                yield sprintf "-r:%s" r

            for f in info.files do
                yield f

        ]

    let normalize (info : ProjectInfo) =
        let path = Path.GetFullPath info.project
        let dir = Path.GetDirectoryName path

        let full (str : string) =
            if Path.IsPathRooted str then str
            else Path.Combine(dir, str)

        { info with
            project = path
            files = info.files |> List.map full
            references = info.references |> List.map full
            output = info.output |> Option.map full
        }
    open Dotnet.ProjInfo
    open Dotnet.ProjInfo.Inspect
    open Dotnet.ProjInfo.Workspace

    module internal Utils =
        let runProcess (log: string -> unit) (workingDir: string) (exePath: string) (args: string) =
            let psi = System.Diagnostics.ProcessStartInfo()
            psi.FileName <- exePath
            psi.WorkingDirectory <- workingDir
            psi.RedirectStandardOutput <- true
            psi.RedirectStandardError <- true
            psi.Arguments <- args
            psi.CreateNoWindow <- true
            psi.UseShellExecute <- false

            use p = new System.Diagnostics.Process()
            p.StartInfo <- psi

            p.OutputDataReceived.Add(fun ea -> log (ea.Data))

            p.ErrorDataReceived.Add(fun ea -> log (ea.Data))

            // printfn "running: %s %s" psi.FileName psi.Arguments

            p.Start() |> ignore
            p.BeginOutputReadLine()
            p.BeginErrorReadLine()
            p.WaitForExit()

            let exitCode = p.ExitCode

            exitCode, (workingDir, exePath, args)

    let private projInfo additionalMSBuildProps (file : string) =

        let projDir = Path.GetDirectoryName file
        let runCmd exePath args = Utils.runProcess ignore projDir exePath (args |> String.concat " ")
    
        let additionalMSBuildProps = ("GenerateDomainTypes", "false") :: additionalMSBuildProps

        let netcore =
            match ProjectRecognizer.kindOfProjectSdk file with
            | Some ProjectRecognizer.ProjectSdkKind.DotNetSdk -> true
            | _ -> false
    
        let projectAssetsJsonPath = Path.Combine(projDir, "obj", "project.assets.json")
        if netcore && not(File.Exists(projectAssetsJsonPath)) then
            let (s, a) = runCmd "dotnet" ["restore"; sprintf "\"%s\"" file]
            if s <> 0 then 
                failwithf "Cannot find restored info for project %s" file
    
        let getFscArgs = 
            if netcore then
                Dotnet.ProjInfo.Inspect.getFscArgs
            else
                let asFscArgs props =
                    let fsc = Microsoft.FSharp.Build.Fsc()
                    Dotnet.ProjInfo.FakeMsbuildTasks.getResponseFileFromTask props fsc
                Dotnet.ProjInfo.Inspect.getFscArgsOldSdk (asFscArgs >> Ok)

        let results =
            let msbuildExec =
                let msbuildPath =
                    if netcore then Dotnet.ProjInfo.Inspect.MSBuildExePath.DotnetMsbuild "dotnet"
                    else 
                        let all = 
                            BlackFox.VsWhere.VsInstances.getWithPackage "Microsoft.Component.MSBuild" true

                        let probes =
                            [
                                @"MSBuild\Current\Bin\MSBuild.exe"
                                @"MSBuild\15.0\Bin\MSBuild.exe"
                            ]

                        let msbuild =
                            all |> List.tryPick (fun i ->
                                probes |> List.tryPick (fun p ->
                                    let path = Path.Combine(i.InstallationPath, p)
                                    if File.Exists path then Some path
                                    else None
                                )
                            )

                        match msbuild with
                        | Some msbuild -> Dotnet.ProjInfo.Inspect.MSBuildExePath.Path msbuild
                        | None -> failwith "no msbuild"
                Dotnet.ProjInfo.Inspect.msbuild msbuildPath runCmd

            let additionalArgs = additionalMSBuildProps |> List.map (Dotnet.ProjInfo.Inspect.MSBuild.MSbuildCli.Property)

            let log = ignore

            let projs = Dotnet.ProjInfo.Inspect.getResolvedP2PRefs

            file
            |> Inspect.getProjectInfos log msbuildExec [projs; getFscArgs] additionalArgs

        netcore, results


    let tryOfProject (additionalMSBuildProps : list<string * string>) (file : string) =
        let (netcore, info) = projInfo additionalMSBuildProps file

        match info with
        | Ok info ->
            let mutable errors = []
            let fscArgs = 
                info |> List.tryPick (fun res ->
                    match res with
                    | Ok res ->
                        match res with
                        | GetResult.FscArgs args -> Some args
                        | _ -> None
                    | Error err ->
                        errors <- err :: errors
                        None
                )

            match fscArgs with
            | Some args -> 
                Ok (ofFscArgs netcore file args)
            | None -> 
                let errors = 
                    errors |> List.map (fun e ->
                        match e with
                        | MSBuildFailed (code, err) ->
                            sprintf "msbuild error %d: %A" code err
                        | MSBuildSkippedTarget ->
                            sprintf "msbuild skipped target"
                        | UnexpectedMSBuildResult res ->
                            sprintf "msbuild error: %s" res
                    )
                Error errors
        | Error e ->
            match e with
            | MSBuildFailed (code, err) ->
                Error [sprintf "msbuild error %d: %A" code err]
            | MSBuildSkippedTarget ->
                Error [sprintf "msbuild skipped target"]
            | UnexpectedMSBuildResult res ->
                Error [sprintf "msbuild error: %s" res]

