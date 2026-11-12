module Program

open System.IO
open System.IO.Compression
open Nullean.Make.Fs
open Proc.Fs

/// The build's targets; `dotnet run --project build/scripts -- --help` lists them with these descriptions
type Target =
    /// clean known output locations
    | Clean
    /// Run build
    | Build
    /// Runs build, then creates and validates the packages and reports public API changes
    | Pack
    /// Runs build, then creates and validates the packages: exactly what CI runs
    | Ci
    /// Pack the root and portable 'any' tool packages into build/output
    | Generatepackages
    /// Validate the signed managed assembly in the 'any' package
    | Validatepackages
    /// Report public API changes against the previous release
    | Generateapichanges
    /// Publish the native-AOT linux-x64 build as a container image (--push sends it to ghcr.io)
    | Publishcontainers

/// build/output, relative to the repository root
let private output = Paths.RootRelative Paths.Output.FullName

let private isPerRidPackage (name: string) =
    Paths.AotRuntimeIdentifiers |> List.exists (fun rid -> name.Contains $".{rid}.")

/// `dotnet pack`'s RID-aware tool-packaging path (used once RuntimeIdentifiers is declared) copies
/// the *unsigned* obj/ build of this project's own assembly into the portable 'any' package, even
/// though the normal bin/ output is correctly strong-name signed — a long-standing obj-vs-bin mixup
/// in `dotnet pack` (see https://github.com/dotnet/sdk/issues/20197) that resurfaces here. Patched in
/// place after packing by swapping in the signed bin/ copies for every TFM the 'any' package ships.
let private fixAnyPackageSigning (anyPackagePath: string) =
    use archive = ZipFile.Open(anyPackagePath, ZipArchiveMode.Update)
    for tfm in Paths.ManagedTargetFrameworks do
        let entryName = $"tools/{tfm}/any/{Paths.ToolName}.dll"
        let signedDll = Path.Combine(Paths.ToolProject.FullName, "bin", "Release", tfm, $"{Paths.ToolName}.dll")
        match archive.GetEntry(entryName), File.Exists signedDll with
        | null, _ | _, false -> ()
        | entry, true ->
            entry.Delete()
            let newEntry = archive.CreateEntry(entryName)
            use entryStream = newEntry.Open()
            use fileStream = File.OpenRead(signedDll)
            fileStream.CopyTo(entryStream)

/// Keeps the root and 'any' packages from the staging folder, patching the 'any' package's signing
let private keepRootAndAnyPackages (staging: string) =
    DirectoryInfo(staging).GetFiles("*.nupkg")
    |> Seq.filter (fun f -> not (isPerRidPackage f.Name))
    |> Seq.iter (fun f ->
        let destination = Path.Combine(Paths.Output.FullName, f.Name)
        printfn "keeping %s" f.Name
        f.CopyTo(destination, true) |> ignore
        if f.Name.Contains $"{Paths.ToolName}.any." then
            fixAnyPackageSigning destination)

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("dotnet run --project build/scripts --", None)
    let push =
        app.Flag("--push", desc = "publishcontainers only: push the built image to ghcr.io instead of building it into the local Docker daemon")

    // `-s` skips what a target `requires`, never what it `composes`: build always cleans first
    app.Bind <| function
        | Clean -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              // every project but this build itself: `dotnet clean` on the solution would delete the assemblies the
              // running build still loads lazily and fail the next target
              for project in Directory.EnumerateFiles("src", "*.?sproj", SearchOption.AllDirectories) do
                  sh $"dotnet clean {project} -c Release -v q" }
        | Build -> command {
              composes [Clean] }
                   |> executes { sh $"dotnet build -c Release" }
        | Pack -> command { requires [Build]; composes [Generatepackages; Validatepackages; Generateapichanges] }
        // no tests yet, so CI runs exactly what pack does
        | Ci -> command { requires [Build]; composes [Generatepackages; Validatepackages; Generateapichanges] }
        // A plain `dotnet pack` emits the root package (whose DotnetToolSettings.xml v2 maps each RID to
        // its own package) AND a package per RID — but native AOT can only compile for the machine it
        // runs on, so those per-RID outputs from a single machine are self-contained MANAGED builds,
        // silently missing the AOT compilation. We therefore keep only the root and the portable 'any'
        // fallback here, and take the real per-RID packages from the CI matrix, where each is compiled
        // on a matching runner (see aot-pack in .github/workflows/ci.yml).
        | Generatepackages -> target [] {
              if not Paths.Output.Exists then Paths.Output.Create()
              let staging = Paths.RootRelative <| Path.Combine(Paths.Output.FullName, "..", "rewriter-staging")
              if Directory.Exists staging then Directory.Delete(staging, true)
              sh $"dotnet pack src/{Paths.ToolName}/{Paths.ToolName}.csproj -c Release -o {staging}"
              keepRootAndAnyPackages staging
              Directory.Delete(staging, true) }
        // Only the 'any' package carries a signed managed assembly to check: the root package is
        // just a DotnetToolSettings.xml pointer with no dll of its own, and the per-RID AOT packages
        // hold a native binary with no managed identity either.
        | Validatepackages -> target [] {
              let package =
                  Paths.Output.GetFiles("*.nupkg")
                  |> Seq.filter (fun f -> f.Name.Contains $"{Paths.ToolName}.any.")
                  |> Seq.sortByDescending (fun f -> f.CreationTimeUtc)
                  |> Seq.head
              let package = Paths.RootRelative package.FullName
              let version = Versions.informational.Value
              sh $"dotnet nupkg-validator validate {package} -v {version} -a {Paths.ToolName} -k 96c599bbe3e70f5d" }
        // The plain "assembly-rewriter" package id is just a DotnetToolSettings.xml v2 shim pointing at per-RID
        // sub-packages (see generatepackages) - it ships no managed assembly of its own, so NuGetAssemblyProvider would
        // find 0 assemblies there. The portable, signed managed build lives in "assembly-rewriter.any" instead.
        // `-a`: don't fail a first-ever diff when "assembly-rewriter.any" has no previous version on nuget.org.
        | Generateapichanges -> target [] {
              let version = Versions.current.Value
              // quoted: an unquoted | would pipe
              sh $"dotnet assembly-differ diff 'previous-nuget|{Paths.ToolName}.any|{version}|net10.0' 'directory|src/{Paths.ToolName}/bin/Release/net10.0' -a --target {Paths.ToolName} -f github-comment --output {output}" }
        // Publishes the CLI's native-AOT build as a container image via the .NET SDK's own container
        // support (`dotnet publish -t:PublishContainer`), the same mechanism used for curb — see
        // https://github.com/nullean/curb/pull/72. linux-x64 only for now; a second RID becomes a second
        // manifest-list platform later with no change to action.yml.
        //
        // Base image is the chiseled/distroless runtime-deps image: no shell, minimal surface, and correct
        // for an AOT binary specifically because there is no managed runtime to host — a plain `runtime`
        // image would carry a CLR this binary never uses.
        //
        // --push is an explicit flag, not inferred from a CI/event-name environment variable: the aot-pack
        // job's linux-x64 leg calls this on every trigger (PR, push, release) purely to prove the container
        // build itself still works, with no ghcr.io credentials configured there. Only the build job, which
        // does log in, passes --push.
        | Publishcontainers -> target [] {
              let project = Paths.RootRelative Paths.ToolProject.FullName
              let baseImage = "mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled"
              // the quotes are part of the value: MSBuild would otherwise split the tags on `;`
              let tags = $"ContainerImageTags=\"{Versions.containerImageTags.Value}\""
              let publish =
                  [ "publish"; project; "-c"; "Release"; "-f"; "net10.0"; "-r"; "linux-x64"; "/t:PublishContainer"
                    "-p"; "DebugType=none"; "-p"; $"ContainerBaseImage={baseImage}"
                    "-p"; $"ContainerRepository={Paths.Repository}"; "-p"; tags; "-p"; "ContainerUser=1001:1001" ]
              if push.Value then
                  sh $"dotnet {publish} -p ContainerRegistry=ghcr.io"
              else
                  sh $"dotnet {publish}" }

    app.RunAsync(argv).GetAwaiter().GetResult()
