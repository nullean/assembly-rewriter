# assembly-rewriter

A .NET tool that rewrites an assembly's top-level namespace (types, `ldstr` literals, backing fields and references
between the rewritten assemblies) with Mono.Cecil, so two versions of the same assembly can be referenced side by
side. It ships as a native-AOT dotnet tool, a container image (`ghcr.io/nullean/assembly-rewriter`) and a GitHub
Action (`action.yml`, which runs that image).

## Layout

| Path | What |
|---|---|
| `src/assembly-rewriter` | the tool (net8.0 and net10.0; native AOT per RID, plus a portable `any` fallback) |
| `build/scripts` | the F# build the `mise` tasks call |
| `action.yml` | the GitHub Action, running the `edge` container image |

## Commands

mise installs the SDK pinned in `global.json` (plus the .NET 10 runtime the build runs on). Run everything through it:

```bash
mise run build    # build the solution
mise run pack     # build, pack the root and `any` packages, validate them, report public API changes
mise run ci       # exactly what CI runs: build, then pack (there are no tests)
mise run check    # workflow security checks (zizmor)
mise run release  # show the release draft; `mise run release publish` releases
```

`mise tasks ls` lists them all. Package output goes to `build/output`. Versions live in `Directory.Packages.props`
(Central Package Management).

## Releasing

release-drafter keeps a draft release up to date from the labels of merged pull requests. **Publishing the draft**
releases: it creates the tag, and `ci.yml` builds the per-RID AOT packages, packs at that version, pushes the container
image and pushes all seven packages to nuget.org with trusted publishing. Every pull request carries exactly one
release label. The [releasing](.claude/skills/releasing/SKILL.md) and
[breaking-changes](.claude/skills/breaking-changes/SKILL.md) skills have the details.

## Rules

- Native AOT compiles only for the machine it runs on. `mise run pack` keeps the root package (a
  `DotnetToolSettings.xml` that maps each RID to its own package) and the portable `any` package; CI's `aot-pack`
  matrix builds the per-RID packages on matching runners.
- The `any` package must carry the strong-name signed assembly: `fixAnyPackageSigning` in `build/scripts/Targets.fs`
  patches it after `dotnet pack` copies the unsigned one.
- `create-major-tag.yml` moves the `v<major>` tag on every published release, so `uses: nullean/assembly-rewriter@v0`
  keeps working.
