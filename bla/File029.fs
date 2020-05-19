namespace Some
module File029 =

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

    let value = 29
