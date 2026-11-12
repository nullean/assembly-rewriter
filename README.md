<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-lockup-544x192.png">
    <img src="https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-lockup-light-544x192.png" width="272" alt="nullean">
  </picture>
</p>

<h1 align="center">assembly-rewriter</h1>

<p align="center">Rename the top-level namespace of .NET assemblies, and the references between them, so two versions of the same assembly can be referenced side by side.</p>

<p align="center">
  <a href="https://www.nuget.org/packages/assembly-rewriter"><img src="https://img.shields.io/nuget/v/assembly-rewriter?style=flat-square" alt="NuGet"></a>
  <a href="https://github.com/nullean/assembly-rewriter/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/nullean/assembly-rewriter/ci.yml?branch=main&style=flat-square&label=ci" alt="CI"></a>
</p>

Rewriting NEST 6.2.0 and the Elasticsearch.Net 6.2.0 it depends on, both taken from nuget.org. The per-reference lines are
left out and the paths shortened:

```console
$ assembly-rewriter rewrite -i in/Nest.dll -o out/Nest620.dll -i in/Elasticsearch.Net.dll -o out/Elasticsearch.Net620.dll
[2026-11-11T22:21:41.37+01:00][Nest              ][AssemblyDefinition     ] rewriting Nest from …/in/Nest.dll
[2026-11-11T22:21:41.39+01:00][Nest              ][AssemblyNameReference  ] Elasticsearch.Net will be rewritten first
[2026-11-11T22:21:41.39+01:00][Elasticsearch.Net ][AssemblyDefinition     ] rewriting Elasticsearch.Net from …/in/Elasticsearch.Net.dll
[2026-11-11T22:21:41.53+01:00][Elasticsearch.Net ][AssemblyTitleAttribute ] Elasticsearch.Net to Elasticsearch.Net620
[2026-11-11T22:21:41.60+01:00][Elasticsearch.Net ][AssemblyDefinition     ] finished rewriting Elasticsearch.Net into …/out/Elasticsearch.Net620.dll
[2026-11-11T22:21:43.20+01:00][Nest              ][AssemblyTitleAttribute ] Nest to Nest620
[2026-11-11T22:21:43.20+01:00][Nest              ][AssemblyDefinition     ] finished rewriting Nest into …/out/Nest620.dll
```

`Nest620.dll` now declares `Nest620.*` types and references `Elasticsearch.Net620`, so it can sit next to any other
version of `Nest.dll` in one project.

## Install

```sh
dotnet tool install --global assembly-rewriter
```

On Linux (x64, arm64), Windows (x64, arm64) and macOS (arm64) this installs a self-contained native-AOT executable: no
shared .NET runtime, no JIT warm-up. Everywhere else it falls back to a framework-dependent build, which needs the .NET
runtime installed.

## Quick start

```sh
assembly-rewriter rewrite -i lib/Nest.dll -o out/Nest620.dll
```

The tool assumes the dll's name is its top-level namespace, and takes the new namespace from the output file name. It
rewrites, with [Mono.Cecil](https://github.com/jbevain/cecil):

1. the top-level namespace of every type in the assembly;
2. the assemblies in dependency order, dependencies first;
3. IL `ldstr` string literals that start with the namespace;
4. compiler-generated backing fields.

Both versions of NEST depend on `Elasticsearch.Net.dll`, so rewrite that dependency in the same call. The references
to it inside NEST are then updated to the rewritten assembly:

```sh
assembly-rewriter rewrite -i lib/Nest.dll -o out/Nest620.dll -i lib/Elasticsearch.Net.dll -o out/Elasticsearch.Net620.dll
```

Add any other direct dependency that would clash (say `-i lib/Newtonsoft.Json.dll -o out/Newtonsoft.Json620.dll`) the
same way. Only references between the assemblies passed with `-i` are rewritten.

| Option | What |
|---|---|
| `-i`, `--in` | an assembly to rewrite (repeatable) |
| `-o`, `--out` | where its rewritten copy goes (repeatable); `-i` and `-o` pair up by position and their counts must match |
| `-r`, `--resolvedir` | an extra directory to resolve dependencies that are not rewritten from (repeatable) |
| `-k`, `--keyfile` | strong-name sign the rewritten assemblies, or the merged one with `-m`, with this key file |
| `-m`, `--merge` | merge every rewritten assembly into the first `-o` path |
| `-v`, `--verbose` | verbose output |

This was written to benchmark different versions of the
[Elasticsearch .NET clients](https://github.com/elastic/elasticsearch-net) against each other. Your mileage may vary
with other assemblies: type names held in strings that do not start with the namespace are not rewritten.

Check that everything expected was rewritten with the
[IL Disassembler](https://learn.microsoft.com/dotnet/framework/tools/ildasm-exe-il-disassembler):

```powershell
ildasm <rewritten>.dll /OUT=<rewritten>.il /NOBAR
Select-String -Path <rewritten>.il -Pattern '<original namespace>\.' -AllMatches | ft LineNumber,Line
```

### GitHub Action

```yaml
- uses: nullean/assembly-rewriter@main
  with:
    args: -i Nest.dll -o Nest620.dll
```

The action runs `assembly-rewriter rewrite <args>` in the prebuilt, distroless
`ghcr.io/nullean/assembly-rewriter:edge` container, so the workflow needs no .NET SDK. `args` is the rest of the command
line, since every path is an `-i`/`-o` pair. The container's working directory is the workflow's checkout. Container
actions run on Linux runners only.

### Container

The same image works anywhere `docker run` does, such as GitLab CI or a machine without the .NET SDK:

```sh
docker run --rm -v "$(pwd)":/workspace ghcr.io/nullean/assembly-rewriter:edge rewrite -i /workspace/Nest.dll -o /workspace/Nest620.dll
```

It is native-AOT on a chiseled `runtime-deps` base, with no shell, running as a non-root user. `edge` tracks the latest
build of `main`; `latest` and the version tags follow releases.

## Packages

The tool ships as one package per platform. Install the root package; the .NET SDK picks the right one.

| Package | What it is | |
|---|---|---|
| [`assembly-rewriter`](src/assembly-rewriter/README.md) | the tool; points the SDK at the package for your platform | [![NuGet](https://img.shields.io/nuget/v/assembly-rewriter?style=flat-square)](https://www.nuget.org/packages/assembly-rewriter) |
| `assembly-rewriter.<rid>` | the native-AOT build for `linux-x64`, `linux-arm64`, `win-x64`, `win-arm64` and `osx-arm64` | [![NuGet](https://img.shields.io/nuget/v/assembly-rewriter.linux-x64?style=flat-square)](https://www.nuget.org/packages/assembly-rewriter.linux-x64) |
| `assembly-rewriter.any` | the framework-dependent fallback (net10.0, net11.0) for every other platform | [![NuGet](https://img.shields.io/nuget/v/assembly-rewriter.any?style=flat-square)](https://www.nuget.org/packages/assembly-rewriter.any) |

The packages also carry an agent skill (`skills/assembly-rewriter/SKILL.md`).

## Learn more

- [Releases](https://github.com/nullean/assembly-rewriter/releases)
- [Contributing](https://github.com/nullean/.github/blob/main/CONTRIBUTING.md)

MIT licensed. Copyright Nullean and contributors.
