---
name: assembly-rewriter
description: >-
  Use when a .NET project must reference two versions of the same assembly side by side (for example to benchmark or
  migrate between client versions). assembly-rewriter renames an assembly's top-level namespace, and the references
  between the assemblies rewritten with it, so the copies no longer clash.
---

# assembly-rewriter

A dotnet tool (`dotnet tool install -g assembly-rewriter`), also a container image (`ghcr.io/nullean/assembly-rewriter`)
and a GitHub Action (`uses: nullean/assembly-rewriter@v0` with `args:`). It assumes the file name is the top-level
namespace: rewriting `Nest.dll` to `Nest620.dll` turns every `Nest.*` type, `ldstr` literal and backing field into
`Nest620.*`. It is not a general IL weaver.

## The calls you need

```bash
# one assembly
assembly-rewriter -i lib/Nest.dll -o out/Nest620.dll

# an assembly and the dependency it shares with the other version: rewrite both in one call,
# so Nest620 references Elasticsearch.Net620 instead of Elasticsearch.Net
assembly-rewriter -i lib/Nest.dll -o out/Nest620.dll -i lib/Elasticsearch.Net.dll -o out/Elasticsearch.Net620.dll

# dependencies that are not rewritten but must resolve, a strong-name key, or one merged assembly
assembly-rewriter -i a/Foo.dll -o out/Foo2.dll -r packages/deps -k key.snk
assembly-rewriter -i a/Foo.dll -o out/Foo2.dll -i a/Foo.Core.dll -o out/Foo2.Core.dll -m -k key.snk
```

## Gotchas

- `-i` and `-o` pair up by position, and their counts must match; the tool exits 1 otherwise.
- The new namespace comes from the output file name, so name the output after the namespace you want.
- Rewrite every assembly that references a renamed one in the same call. Only references between the assemblies
  passed with `-i` are updated.
- `-m` merges all outputs into the first `-o` path (ILRepack, internalized). With `-m`, `-k` signs the merged
  assembly; without it, `-k` signs each rewritten one.
- Reflection on type names held in strings that do not start with the namespace is not rewritten.

Docs: https://github.com/nullean/assembly-rewriter
