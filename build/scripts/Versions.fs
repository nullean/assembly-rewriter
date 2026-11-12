module Versions

open System
open Proc.Fs

/// The version MinVer computes from the tags, which is the version the packages get
let current =
    lazy (shell {
        // dotnet/sdk#53783: on a cold tool-resolver cache (every fresh CI runner, every fresh container), restoring 2+
        // RID-specific tool packages in one manifest can misattribute one package's DotnetToolSettings.xml to another,
        // failing with "The command ... is not contained in the package ...". The cache is warm after the first attempt,
        // so one retry always succeeds.
        sh $"dotnet tool restore" |> Cmd.retry 1
        let! version = text $"dotnet minver --default-pre-release-identifiers canary.0"
        return version
    })

/// The informational version the package validator expects: the version, plus the commit it was built from
let informational =
    lazy (shell {
        let! sha = text $"git rev-parse HEAD"
        return $"{current.Value}+{sha}"
    })

/// Tags for the container image, from the same version: "edge" always (so `ghcr.io/nullean/assembly-rewriter:edge` is
/// always the latest main build), plus "latest" and the plain semver when this is an exact release tag rather than a
/// canary commit. MinVer's canary suffix always contains a hyphen, a clean tag never does.
let containerImageTags =
    lazy (
        let version = current.Value
        if version.Contains("-", StringComparison.Ordinal) then "edge" else $"edge;latest;{version}"
    )
