// Learn more about F# at http://fsharp.org

open System
open System.IO
open FSharp.Compiler.SourceCodeServices
open Test

[<EntryPoint>]
let main argv =

    let proj = Path.Combine(__SOURCE_DIRECTORY__, "..", "bla", "bla.fsproj")
    let checker = FSharpChecker.Create()

    match ProjectInfo.tryOfProject [] proj with
    | Ok info ->
        let args = List.toArray (ProjectInfo.toFscArgs info)
        let options = checker.GetProjectOptionsFromCommandLineArgs(proj, args)
        async {
            for f in info.files do
                let c = File.ReadAllText f
                let text = FSharp.Compiler.Text.SourceText.ofString c
                let! test = checker.ParseAndCheckFileInProject(f, 0, text, options, null)
                printfn "done %s" f
                ()
        } |> Async.RunSynchronously
    | Error err ->
        printfn "%A" err

    0 // return an integer exit code
