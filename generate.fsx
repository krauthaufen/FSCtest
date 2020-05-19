open System.IO
open System.Collections.Generic

let createProject (cnt : int) (file : string) =
    let dir = Path.GetDirectoryName file
    if Directory.Exists dir then Directory.Delete(dir, true)
    Directory.CreateDirectory dir |> ignore

    let files = List<string>()

    for i in 1 .. cnt do
        let name = sprintf "File%03d.fs" i

        files.Add name

        File.WriteAllLines(Path.Combine(dir, name), [|
            sprintf "namespace Some"
            sprintf "open System"
            sprintf "module File%03d =" i

            sprintf "    let list ="
            sprintf "        let rand = Random()"
            sprintf "        ["
            for i in 1 .. 200 do
                sprintf "            if rand.NextDouble() > 0.5 then \"%05d\"" i
                
            sprintf "        ]"
            sprintf "    let value = %d" i
        |])

    File.WriteAllLines(Path.Combine(dir, "Program.fs"), [
        "open Some"
        for i in 1 .. cnt do
            sprintf "printfn \"%d: %%d\" File%03d.value" i i
    ])
    files.Add "Program.fs"

    File.WriteAllLines(file, [
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>"
        "<Project Sdk=\"Microsoft.NET.Sdk\">"
        "  <PropertyGroup>"
        "    <OutputType>Exe</OutputType>"
        "    <TargetFramework>netcoreapp3.1</TargetFramework>"
        "  </PropertyGroup>"
        "  <ItemGroup>"

        for f in files do sprintf "    <Compile Include=\"%s\" />" f

        "  </ItemGroup>"
        "</Project>"
    ])

createProject 2 (Path.Combine(__SOURCE_DIRECTORY__, "bla", "bla.fsproj"))






