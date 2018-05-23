# AssemblyRewriter

Rewrites assemblies with [Mono.Cecil](https://www.mono-project.com/docs/tools+libraries/libraries/Mono.Cecil/), to allow two different versions of the same assembly to be referenced within an application.
 
It assumes that the assembly DLL name is the top level namespace and rewrites

1. the top level namespace for all types within the assembly
2. assemblies in the order of dependencies first
3. IL `ldstr` op codes if they start with the namespace
4. compiler generated backing fields

This small program was written to allow different versions [Elasticsearch .NET clients](https://github.com/elastic/elasticsearch-net) to be rewritten for benchmark comparisons. Your mileage may vary rewriting other assemblies :)

## Examples

Rewrite [NEST, the Elasticsearch .NET high level client](https://github.com/elastic/elasticsearch-net), version 6.2.0

```c#
dotnet run -- -i C:/Nest.dll -o C:/Nest620.dll
```

Now, `Nest620.dll` and another version of `Nest.dll` can be referenced in the same project. 

There's _a small issue here_ however; both versions of NEST rely on `Elasticsearch.Net.dll`, so we should also rewrite
this dependency at the same time, and update the references to Elasticsearch.Net within NEST to reference the new rewritten assembly

```c#
dotnet run -- -i C:/Nest.dll -o C:/Nest620.dll -i C:/Elasticsearch.Net.dll -o C:/Elasticsearch.Net620.dll
```

Great! Now we can reference both in the same project.

If there are other direct dependencies that may version clash, these can be passed as well

```c#
dotnet run -- -i C:/Nest.dll -o C:/Nest620.dll -i C:/Elasticsearch.Net.dll -o C:/Elasticsearch.Net620.dll -i C:/Newtonsoft.Json.dll -o C:/Newtonsoft.Json620.dll
```

## Rewrite validation

You can check to see if everything expected has been rewritten using [IL Disassembler](https://docs.microsoft.com/en-us/dotnet/framework/tools/ildasm-exe-il-disassembler)

```powershell
ildasm <rewritten>.dll /OUT=<rewritten>.il /NOBAR
Select-String -Path <rewritten>.il -Pattern '<original namespace>\.' -AllMatches | ft LineNumber,Line
```

## License

[Apache 2.0](License.txt)
