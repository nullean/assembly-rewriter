# assembly-rewriter

Rewrites .NET assemblies with [Mono.Cecil](https://www.mono-project.com/docs/tools+libraries/libraries/Mono.Cecil/), to allow two different versions of the same assembly to be referenced within an application.
 
It assumes that the assembly DLL name is the top level namespace and rewrites

1. the top level namespace for all types within the assembly
2. assemblies in the order of dependencies first
3. IL `ldstr` op codes if they start with the namespace
4. compiler generated backing fields

This small program was written to allow different versions [Elasticsearch .NET clients](https://github.com/elastic/elasticsearch-net) to be rewritten for benchmark comparisons. 
Your mileage may vary rewriting other assemblies :)

## Installation


Distributed as a .NET tool so install using the following

```
dotnet tool install assembly-rewriter
```

## Run 

```bat
dotnet assembly-rewriter
```

You can omit `dotnet` if you install this as a global tool

## GitHub Action

```yaml
- uses: nullean/assembly-rewriter@main
  with:
    args: -i Nest.dll -o Nest620.dll
```

Runs `assembly-rewriter` from a pre-built, distroless container (`ghcr.io/nullean/assembly-rewriter`) —
no .NET SDK install needed in the workflow. `args` is the full command line, since every input and
output path is passed as an `-i`/`-o` pair (see below). Mount your working directory's DLLs where the
container can see them; a container action's default working directory already maps to the workflow's
checkout. Linux runners only (`ubuntu-latest` and similar) — container actions can't run on Windows or
macOS runners.

## Container image

`ghcr.io/nullean/assembly-rewriter` also works as a general-purpose container, outside GitHub Actions —
GitLab CI, a local machine without the .NET SDK, anywhere `docker run` works:

```sh
docker run --rm -v "$(pwd)":/workspace ghcr.io/nullean/assembly-rewriter:edge -i /workspace/Nest.dll -o /workspace/Nest620.dll
```

Distroless: native-AOT, chiseled `runtime-deps` base, no shell, runs as a non-root user. Tags follow
`assembly-rewriter`'s own releases — `edge` tracks the latest commit on `master`, `latest` and a semver
tag follow tagged releases.

## Examples

Rewrite [NEST, the Elasticsearch .NET high level client](https://github.com/elastic/elasticsearch-net), version 6.2.0

```c#
assembly-rewriter -i C:/Nest.dll -o C:/Nest620.dll
```

Now, `Nest620.dll` and another version of `Nest.dll` can be referenced in the same project. 

There's _a small issue here_ however; both versions of NEST rely on `Elasticsearch.Net.dll`, so we should also rewrite
this dependency at the same time, and update the references to Elasticsearch.Net within NEST to reference the new rewritten assembly

```c#
assembly-rewriter -i C:/Nest.dll -o C:/Nest620.dll -i C:/Elasticsearch.Net.dll -o C:/Elasticsearch.Net620.dll
```

Great! Now we can reference both in the same project.

If there are other direct dependencies that may version clash, these can be passed as well

```c#
assembly-rewriter -i C:/Nest.dll -o C:/Nest620.dll -i C:/Elasticsearch.Net.dll -o C:/Elasticsearch.Net620.dll -i C:/Newtonsoft.Json.dll -o C:/Newtonsoft.Json620.dll
```

## Rewrite validation

You can check to see if everything expected has been rewritten using [IL Disassembler](https://docs.microsoft.com/en-us/dotnet/framework/tools/ildasm-exe-il-disassembler)

```powershell
ildasm <rewritten>.dll /OUT=<rewritten>.il /NOBAR
Select-String -Path <rewritten>.il -Pattern '<original namespace>\.' -AllMatches | ft LineNumber,Line
```

## License

[Apache 2.0](License.txt)
