module CommandLine

open Argu
open Microsoft.FSharp.Reflection

type Arguments =
    | [<CliPrefix(CliPrefix.None);SubCommand>] Clean
    | [<CliPrefix(CliPrefix.None);SubCommand>] Build
    | [<CliPrefix(CliPrefix.None);SubCommand>] Pack
    | [<CliPrefix(CliPrefix.None);SubCommand>] Ci
    
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GeneratePackages
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] ValidatePackages 
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] GenerateApiChanges 
    | [<CliPrefix(CliPrefix.None);Hidden;SubCommand>] PublishContainers
    
    | [<Inherit;AltCommandLine("-s")>] SingleTarget of bool
    | [<Inherit>] Push
with
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Clean _ -> "clean known output locations"
            | Build _ -> "Run build"
            | Pack _ -> "Runs build, then creates and validates the packages and reports public API changes"
            | Ci _ -> "Runs build, then creates and validates the packages: exactly what CI runs"
            
            | SingleTarget _ -> "Runs the provided sub command without running their dependencies"
            | Push -> "publishcontainers only: push the built image to ghcr.io instead of building it into the local Docker daemon"
            
            | GeneratePackages
            | ValidatePackages
            | GenerateApiChanges
            | PublishContainers
                -> "Undocumented, dependent target"
    member this.Name =
        match FSharpValue.GetUnionFields(this, typeof<Arguments>) with
        | case, _ -> case.Name.ToLowerInvariant()
