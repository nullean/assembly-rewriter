# assembly-rewriter

![nullean](https://raw.githubusercontent.com/nullean/nullean.github.io/main/assets/nullean-lockup-544x192.png)

A .NET tool that renames the top-level namespace of .NET assemblies, and the references between them, so two versions
of the same assembly can be referenced side by side.

[![NuGet](https://img.shields.io/nuget/v/assembly-rewriter?style=flat-square)](https://www.nuget.org/packages/assembly-rewriter)

## Install

```sh
dotnet tool install --global assembly-rewriter
```

On Linux (x64, arm64), Windows (x64, arm64) and macOS (arm64) this installs a native-AOT executable from the
`assembly-rewriter.<rid>` package; everywhere else it falls back to the framework-dependent `assembly-rewriter.any`.
Install the `assembly-rewriter` package itself, not those.

## Quick start

Rewrite NEST and the Elasticsearch.Net it depends on in one call, so `Nest620.dll` declares `Nest620.*` types and
references `Elasticsearch.Net620`:

```sh
assembly-rewriter rewrite -i lib/Nest.dll -o out/Nest620.dll -i lib/Elasticsearch.Net.dll -o out/Elasticsearch.Net620.dll
```

The tool assumes the dll's name is its top-level namespace and takes the new one from the output file name. It
rewrites type namespaces, the references between the assemblies passed with `-i` (dependencies first), `ldstr` literals
that start with the namespace, and compiler-generated backing fields.

| Option | What |
|---|---|
| `-i`, `--in` | an assembly to rewrite (repeatable) |
| `-o`, `--out` | where its rewritten copy goes (repeatable); pairs up with `-i` by position |
| `-r`, `--resolvedir` | an extra directory to resolve dependencies that are not rewritten from (repeatable) |
| `-k`, `--keyfile` | strong-name sign the rewritten assemblies, or the merged one with `-m` |
| `-m`, `--merge` | merge every rewritten assembly into the first `-o` path |
| `-v`, `--verbose` | verbose output |

The package also carries an agent skill (`skills/assembly-rewriter/SKILL.md`). The tool runs as a GitHub Action
(`uses: nullean/assembly-rewriter@main`) and as a container (`ghcr.io/nullean/assembly-rewriter`) too, both without a
.NET SDK.

## Learn more

- Source, GitHub Action and container usage: https://github.com/nullean/assembly-rewriter
- Releases: https://github.com/nullean/assembly-rewriter/releases
