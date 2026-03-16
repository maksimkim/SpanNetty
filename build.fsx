#r "paket:
nuget Fake.Core.Target
nuget Fake.Core.ReleaseNotes
nuget Fake.Core.Process
nuget Fake.Core.Trace
nuget Fake.DotNet.Cli
nuget Fake.IO.FileSystem
nuget Fake.Testing.Common //"

open System
open System.IO

open Fake.Core
open Fake.Core.TargetOperators
open Fake.DotNet
open Fake.IO
open Fake.IO.FileSystemOperators
open Fake.IO.Globbing.Operators
open Fake.Testing.Common

Target.initEnvironment ()

// Variables
let configuration = Environment.environVarOrDefault "configuration" "Debug"
let solution = Path.GetFullPath(string "./DotNetty.sln")

// Directories
let toolsDir = __SOURCE_DIRECTORY__ @@ "tools"
let output = __SOURCE_DIRECTORY__  @@ "Artifacts"
let outputTests = __SOURCE_DIRECTORY__ @@ "TestResults"
let outputPerfTests = __SOURCE_DIRECTORY__ @@ "PerfResults"

let buildNumber = Environment.environVarOrDefault "BUILD_NUMBER" "0"
let hasTeamCity = (not (buildNumber = "0")) // check if we have the TeamCity environment variable for build # set
let preReleaseVersionSuffix = "beta" + (if (not (buildNumber = "0")) then (buildNumber) else DateTime.UtcNow.Ticks.ToString())

let releaseNotes =
    File.ReadAllLines (__SOURCE_DIRECTORY__ @@ "RELEASE_NOTES.md")
    |> Array.toList
    |> ReleaseNotes.parse

let versionFromReleaseNotes =
    match releaseNotes.SemVer.PreRelease with
    | Some r -> r.Origin
    | None -> ""

let versionSuffix = 
    match (Environment.environVarOrDefault "nugetprerelease" "") with
    | "main" -> preReleaseVersionSuffix
    | "" -> versionFromReleaseNotes
    | str -> str
    

// Incremental builds
let runIncrementally = Environment.hasEnvironVar "incremental"
let incrementalistReport = output @@ "incrementalist.txt"

// Configuration values for tests
let testNetFrameworkVersion = "net471"

Target.create "Clean" (fun _ ->
    Target.activateFinal "KillCreatedProcesses"

    Shell.cleanDir output
    Shell.cleanDir outputTests
    Shell.cleanDir outputPerfTests

    !! "./**/TestResults" |> Shell.cleanDirs
    !! "./**/bin" |> Shell.cleanDirs
    !! "./**/obj" |> Shell.cleanDirs
)


//--------------------------------------------------------------------------------
// Incrementalist targets
//--------------------------------------------------------------------------------
// Pulls the set of all affected projects detected by Incrementalist from the cached file
let getAffectedProjectsTopology =
    lazy(
        Trace.log (sprintf "Checking inside %s for changes" incrementalistReport)

        let incrementalistFoundChanges = File.Exists incrementalistReport

        Trace.log (sprintf "Found changes via Incrementalist? %b - searched inside %s" incrementalistFoundChanges incrementalistReport)
        if not incrementalistFoundChanges then None
        else
            let sortedItems = (File.ReadAllLines incrementalistReport) |> Seq.map (fun x -> (x.Split ','))
                              |> Seq.map (fun items -> (items.[0], items))
            let d = dict sortedItems
            Some(d)
    )

let getAffectedProjects =
    lazy(
        let finalProjects = getAffectedProjectsTopology.Value
        match finalProjects with
        | None -> None
        | Some p -> Some (p.Values |> Seq.concat)
    )

Target.create "ComputeIncrementalChanges" (fun _ ->
    if runIncrementally then
        let targetBranch = match Environment.environVarOrDefault "targetBranch" "" with
                            | "" -> "main"
                            | b -> b
        let incrementalistPath =
                let incrementalistDir = toolsDir @@ "incrementalist"
                let globalTool = ProcessUtils.tryFindFileOnPath "incrementalist.exe"
                match globalTool with
                    | Some t -> t
                    | None -> if Environment.isWindows then
                                System.IO.Directory.GetFiles(incrementalistDir, "incrementalist.exe", SearchOption.AllDirectories)
                                |> Seq.head
                              elif Environment.isMacOS then incrementalistDir @@ "incrementalist"
                              else incrementalistDir @@ "incrementalist"
    
   
        let args = sprintf "-b %s -s %s -f %s --verbose" targetBranch solution incrementalistReport

        let result =
            CreateProcess.fromRawCommandLine incrementalistPath args
            |> CreateProcess.withWorkingDirectory __SOURCE_DIRECTORY__
            |> CreateProcess.withTimeout (TimeSpan.FromMinutes 5.0)
            |> Proc.run
        
        if result.ExitCode <> 0 then failwithf "Incrementalist failed. %s" args
    else
        Trace.log "Skipping Incrementalist - not enabled for this build"
)

let filterProjects selectedProject =
    if runIncrementally then
        let affectedProjects = getAffectedProjects.Value

        match affectedProjects with
        | None -> None
        | Some x when x |> Seq.exists (fun n -> n.Contains (Path.GetFileName(string selectedProject))) -> Some selectedProject
        | _ -> None
    else
        Trace.log "Not running incrementally"
        Some selectedProject

//--------------------------------------------------------------------------------
// Build targets
//--------------------------------------------------------------------------------
let skipBuild =
    lazy(
        match getAffectedProjects.Value with
        | None when runIncrementally -> true
        | _ -> false
    )

let headProjects =
    lazy(
        match getAffectedProjectsTopology.Value with
        | None when runIncrementally -> [||]
        | None -> [|solution|]
        | Some p -> p.Keys |> Seq.toArray
    )

Target.create "Build" (fun _ ->
    if not skipBuild.Value then
        let additionalArgs = if versionSuffix.Length > 0 then [sprintf "/p:VersionSuffix=%s" versionSuffix] else []
        let buildProject proj =
            DotNet.build
                (fun p ->
                    { p with
                        Configuration = DotNet.BuildConfiguration.Custom configuration
                        Common =
                            { p.Common with
                                CustomParams =
                                    match additionalArgs with
                                    | [] -> None
                                    | args -> Some (String.concat " " args) } })
                proj

        match getAffectedProjects.Value with
        | Some p -> p |> Seq.iter buildProject
        | None -> buildProject solution // build the entire solution if incrementalist is disabled
)

//--------------------------------------------------------------------------------
// Tests targets
//--------------------------------------------------------------------------------
type Runtime =
    | Net
    | NetFramework

module internal ResultHandling =
    let (|OK|Failure|) = function
        | 0 -> OK
        | x -> Failure x

    let buildErrorMessage = function
        | OK -> None
        | Failure errorCode ->
            Some (sprintf "xUnit2 reported an error (Error Code %d)" errorCode)

    let failBuildWithMessage = function
        | DontFailBuild -> Trace.traceError
        | _ -> (fun m -> raise(FailedTestsException m))

    let failBuildIfXUnitReportedError errorLevel =
        buildErrorMessage
        >> Option.iter (failBuildWithMessage errorLevel)

Target.create "RunTests" (fun _ ->
    if not skipBuild.Value then
        let projects = 
            let rawProjects = match (Environment.isWindows) with 
                                | true -> !! "./test/*.Tests/*.Tests.csproj"
                                          -- "./test/*.Tests/DotNetty.Transport.Tests.csproj"
                                          -- "./test/*.Tests/DotNetty.Suite.Tests.csproj"
                                | _ -> !! "./test/*.Tests/*.Tests.csproj" // if you need to filter specs for Linux vs. Windows, do it here
                                       -- "./test/*.Tests/DotNetty.Transport.Tests.csproj"
                                       -- "./test/*.Tests/DotNetty.Suite.Tests.csproj"
                                       -- "./test/*.Tests/DotNetty.End2End.Tests.csproj"
            rawProjects |> Seq.choose filterProjects

        let testNetVersions = [ "net8.0" ]
    
        let runSingleProject project testNetVersion =
            let arguments =
                match (hasTeamCity) with
                | true -> (sprintf "test -c %s --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s -- RunConfiguration.TargetPlatform=x64 --results-directory \"%s\" -- -parallel none -teamcity" configuration testNetVersion outputTests)
                | false -> (sprintf "test -c %s --no-build --filter \"FullyQualifiedName=DotNetty.Handlers.Proxy.Tests.ProxyHandlerTest.Test\" --logger:trx --logger:\"console;verbosity=normal\" --framework %s -- RunConfiguration.TargetPlatform=x64 --results-directory \"%s\" -- -parallel none" configuration testNetVersion outputTests)

            let result =
                CreateProcess.fromRawCommandLine "dotnet" arguments
                |> CreateProcess.withWorkingDirectory (System.IO.Directory.GetParent(project).FullName)
                |> CreateProcess.withTimeout (TimeSpan.FromMinutes 30.0)
                |> Proc.run
        
            ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.Error result.ExitCode

        Directory.ensure outputTests

        for project in projects do
            for testNetVersion in testNetVersions do
                runSingleProject project testNetVersion        
)

Target.create "RunTestsNetFx471" (fun _ ->    
    let projects = 
        let rawProjects = match (Environment.isWindows) with 
                            | true -> !! "./test/*.Tests/*.Tests.csproj"
                                      -- "./test/*.Tests/DotNetty.Suite.Tests.csproj"
                                      -- "./test/*.Tests/DotNetty.Buffers.ReaderWriter.Tests"
                            | _ -> !! "./test/*.Tests/*.Tests.csproj" // if you need to filter specs for Linux vs. Windows, do it here
                                   -- "./test/*.Tests/DotNetty.Suite.Tests.csproj"
                                   -- "./test/*.Tests/DotNetty.Buffers.ReaderWriter.Tests"
        rawProjects |> Seq.choose filterProjects
    
    let runSingleProject project =
        let arguments =
            match (hasTeamCity) with
            | true -> (sprintf "test -c %s --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s -- RunConfiguration.TargetPlatform=x64 --results-directory \"%s\" -- -parallel none -teamcity" configuration testNetFrameworkVersion outputTests)
            | false -> (sprintf "test -c %s --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s -- RunConfiguration.TargetPlatform=x64 --results-directory \"%s\" -- -parallel none" configuration testNetFrameworkVersion outputTests)

        let result =
            CreateProcess.fromRawCommandLine "dotnet" arguments
            |> CreateProcess.withWorkingDirectory (System.IO.Directory.GetParent(project).FullName)
            |> CreateProcess.withTimeout (TimeSpan.FromMinutes 30.0)
            |> Proc.run
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.Error result.ExitCode

    Directory.ensure outputTests
    projects |> Seq.iter (runSingleProject)
)

Target.createFinal "KillCreatedProcesses" (fun _ ->
    Trace.log "Shutting down dotnet build-server"
    let result =
        CreateProcess.fromRawCommandLine "dotnet" "build-server shutdown"
        |> CreateProcess.withWorkingDirectory __SOURCE_DIRECTORY__
        |> CreateProcess.withTimeout (TimeSpan.FromMinutes 2.0)
        |> Proc.run
    if result.ExitCode <> 0 then failwithf "dotnet build-server shutdown failed"
)

//--------------------------------------------------------------------------------
// Help
//--------------------------------------------------------------------------------

Target.create "Help" (fun _ ->
    List.iter printfn [
      "usage:"
      "/build [target]"
      ""
      " Targets for building:"
      " * Build      Builds"
      " * RunTests   Runs tests"
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help"
      ""])

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target.create "BuildDebug" ignore
Target.create "All" ignore
Target.create "Nuget" ignore
Target.create "RunTestsFull" ignore

// build dependencies
"Clean" ==> "Build"
"Build" ==> "BuildDebug"
"ComputeIncrementalChanges" ==> "Build" // compute incremental changes

// tests dependencies
"Build" ==> "RunTests"
"Build" ==> "RunTestsNetFx471"

// all
"BuildDebug" ==> "All"
"RunTests" ==> "All"
"RunTestsNetFx471" ==> "All"

Target.runOrDefaultWithArguments "Help"
