# Phase 03 Report — Solution Scaffold

Date: 2026-08-22

## Scope

Scaffold `SceneForge.sln` and its constituent .NET 8 projects, enforce the
clean-architecture dependency direction required by
[CLAUDE.md](../CLAUDE.md) and
[docs/ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md), wire up shared
build configuration (nullable reference types, implicit usings, deterministic
builds, warnings-as-errors for product code, central package management,
analyzers, `.editorconfig`), add a pull-request CI workflow, and prove
application startup with a minimal WPF shell. No product features were
implemented.

## Repository layout produced

```
SceneForge.sln
global.json
Directory.Build.props
Directory.Packages.props
.editorconfig
.gitignore
.github/workflows/ci.yml
src/
  SceneForge.App/            (WPF, net8.0-windows)
  SceneForge.Core/           (class library, net8.0)
  SceneForge.Media/          (class library, net8.0)
  SceneForge.Infrastructure/ (class library, net8.0)
  Directory.Build.props      (turns on TreatWarningsAsErrors for product code)
tests/
  SceneForge.Core.Tests/     (xUnit, net8.0)
  SceneForge.Media.Tests/    (xUnit, net8.0)
  .editorconfig              (relaxes CA1707 for xUnit naming)
benchmarks/
  SceneForge.Benchmarks/     (BenchmarkDotNet console, net8.0)
```

## Dependency direction

Enforced purely through `ProjectReference` edges (no project can compile
against one it doesn't reference):

- `SceneForge.App` → `Core`, `Media`, `Infrastructure`
- `SceneForge.Media` → `Core`
- `SceneForge.Infrastructure` → `Core`
- `SceneForge.Core` → *(no product project references)*
- `SceneForge.Core.Tests` → `Core`
- `SceneForge.Media.Tests` → `Media`
- `SceneForge.Benchmarks` → `Core`

## Build configuration decisions

- **`global.json`** pins the SDK to `8.0.424` with `rollForward: latestFeature`
  so `dotnet` commands resolve deterministically to a .NET 8 SDK even though
  .NET 10 SDK (`10.0.400`) is also installed on this machine.
- **`Directory.Build.props`** (root) sets `Nullable`, `ImplicitUsings`,
  `Deterministic`, `EnableNETAnalyzers`, `AnalysisLevel=latest`,
  `AnalysisMode=Recommended`, and `EnforceCodeStyleInBuild` for every project,
  and stamps `ContinuousIntegrationBuild=true` when the `CI` environment
  variable is set. `TreatWarningsAsErrors` defaults to `false` here.
- **`src/Directory.Build.props`** imports the root file and flips
  `TreatWarningsAsErrors` to `true`, scoping "warnings are errors" to the four
  product projects (App, Core, Media, Infrastructure) per CLAUDE.md rule 13,
  without forcing it onto tests/benchmarks.
- **`Directory.Packages.props`** enables central package management
  (`ManagePackageVersionsCentrally`) with pinned versions for
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`,
  `coverlet.collector`, and `BenchmarkDotNet`; individual `.csproj` files
  reference packages with no `Version` attribute.
- **`.editorconfig`** sets formatting conventions (4-space indent, CRLF,
  file-scoped namespaces, brace usage) and elevates a few unnecessary-code
  analyzers (`IDE0051`, `IDE0052`) plus nullable-flow diagnostics to
  warnings. `tests/.editorconfig` relaxes `CA1707` (no-underscores) because
  underscore-separated test names are idiomatic xUnit style.
- Each project's `.csproj` only carries what varies per project
  (`TargetFramework`, `OutputType`, `UseWPF`, package/project references);
  everything shared lives in the `Directory.Build.props` chain.

## Minimal WPF shell

`SceneForge.App` contains only `App.xaml`/`App.xaml.cs` and
`MainWindow.xaml`/`MainWindow.xaml.cs` with a single centered "SceneForge"
`TextBlock`, sufficient to prove the executable starts. No other UI, no
media pipeline wiring, no infrastructure wiring beyond the project
references themselves.

## CI workflow

`.github/workflows/ci.yml` runs on `pull_request` targeting `main`, on
`windows-latest` (required for the WPF target), and performs
`restore` → `build --configuration Release` → `test --configuration Release`
→ `dotnet format --verify-no-changes`. It does not publish or produce any
distributable artifact.

## Commands executed and results

All commands were run from `C:\Users\Bwp COmputers\Desktop\scene-forge`
using the pinned .NET 8.0.424 SDK (`dotnet --version` → `8.0.424`).

```powershell
dotnet new sln -n SceneForge -o .
dotnet new classlib -n SceneForge.Core -o src/SceneForge.Core -f net8.0
dotnet new classlib -n SceneForge.Media -o src/SceneForge.Media -f net8.0
dotnet new classlib -n SceneForge.Infrastructure -o src/SceneForge.Infrastructure -f net8.0
dotnet new wpf -n SceneForge.App -o src/SceneForge.App -f net8.0        # resolved to net8.0-windows
dotnet new xunit -n SceneForge.Core.Tests -o tests/SceneForge.Core.Tests -f net8.0
dotnet new xunit -n SceneForge.Media.Tests -o tests/SceneForge.Media.Tests -f net8.0
dotnet new console -n SceneForge.Benchmarks -o benchmarks/SceneForge.Benchmarks -f net8.0

dotnet sln add <src projects>  --solution-folder src
dotnet sln add <test projects> --solution-folder tests
dotnet sln add <benchmark project> --solution-folder benchmarks

dotnet add <project> reference <project>   # wired per "Dependency direction" above

dotnet restore SceneForge.sln
dotnet build SceneForge.sln
dotnet test SceneForge.sln --no-build
dotnet format SceneForge.sln --verify-no-changes
dotnet build SceneForge.sln --configuration Release
dotnet test SceneForge.sln --no-build --configuration Release
```

### Restore

```text
Restored SceneForge.Core.csproj, SceneForge.Media.csproj,
SceneForge.Infrastructure.csproj, SceneForge.App.csproj,
SceneForge.Core.Tests.csproj, SceneForge.Media.Tests.csproj,
SceneForge.Benchmarks.csproj.
Restore succeeded.
```

### Build (Debug)

First attempt failed with:

```text
CSC : error EnableGenerateDocumentationFile: Set MSBuild property
'GenerateDocumentationFile' to 'true' in project file to enable IDE0005
(Remove unnecessary usings/imports) on build
```

Fixed by removing the `IDE0005` severity override from `.editorconfig`
(doc-file generation was intentionally left off; forcing it on would have
required XML doc comments on every public member, which conflicts with the
project's no-unnecessary-comments convention). After that:

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Two more warning classes surfaced before the fix above was finalized and were
resolved directly:

- `CA1050` (declare types in namespaces) — the benchmark class was moved out
  of the top-level-statements file into its own file
  (`ModuleInfoBenchmarks.cs`) with a file-scoped namespace.
- `CA1822` (member can be static) — the benchmark method now reads a private
  instance field (`_moduleName`) instead of the static `ModuleInfo.Name`
  directly, which is both a legitimate instance access and a slightly more
  realistic benchmark shape.
- `CA1707` (remove underscores from member names) — suppressed for
  `tests/**` via a nested `tests/.editorconfig`, since underscore-separated
  test names are the idiomatic xUnit convention.

### Test (Debug)

```text
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Core.Tests.dll (net8.0)
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Media.Tests.dll (net8.0)
```

### Format verification

First run reported `ENDOFLINE` errors across every newly authored file (the
files were created with LF line endings; `.editorconfig` requires CRLF).
Running `dotnet format SceneForge.sln` (no `--verify-no-changes`) rewrote the
line endings in place; a follow-up `--verify-no-changes` run then produced no
output, i.e. zero violations.

### Build + Test (Release, matching CI)

```text
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Core.Tests.dll (net8.0)
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Media.Tests.dll (net8.0)
```

### Startup proof

`src/SceneForge.App/bin/Debug/net8.0-windows/SceneForge.App.exe` was launched
directly and observed in `tasklist` (PID present, ~92 MB working set) before
being terminated, confirming the WPF shell starts and stays resident.

### Benchmark harness proof

`dotnet run --configuration Release --no-build -- --filter '*'` inside
`benchmarks/SceneForge.Benchmarks` was executed under a bounded timeout. The
process launched, BenchmarkDotNet discovered `ModuleInfoBenchmarks`, and
pilot/workload iterations executed and produced timing output before the
timeout ended the run. This confirms the harness is wired correctly
end-to-end; no performance conclusions are drawn from this scaffold-only
benchmark, and no optimization is being claimed or measured at this phase.

## `dotnet sln list` (final state)

```text
benchmarks\SceneForge.Benchmarks\SceneForge.Benchmarks.csproj
src\SceneForge.App\SceneForge.App.csproj
src\SceneForge.Core\SceneForge.Core.csproj
src\SceneForge.Infrastructure\SceneForge.Infrastructure.csproj
src\SceneForge.Media\SceneForge.Media.csproj
tests\SceneForge.Core.Tests\SceneForge.Core.Tests.csproj
tests\SceneForge.Media.Tests\SceneForge.Media.Tests.csproj
```

## Compliance notes against CLAUDE.md

- Rule 1–2 (native WPF, no Electron/web/cloud): satisfied — the only
  executable project is a WPF app with no network or browser dependency.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp media stack): not yet applicable —
  no media processing code exists at this phase, so no media package
  references were added. This will be introduced, with tests first, when the
  media pipeline itself is scaffolded.
- Rule 4 (clean architecture): satisfied via the dependency-direction
  `ProjectReference` graph above.
- Rule 8 (test-first): satisfied for what exists — `ModuleInfo` marker types
  in `Core`/`Media` each have a corresponding passing test; no algorithmic
  behavior exists yet to require tests-before-implementation beyond this.
- Rule 9 (benchmark before/after): not applicable — no optimization work was
  performed in this phase; the benchmark harness itself was proven to run
  (see above), not used to measure a change.
- Rule 13 (formatting, build, tests before ending a task): all three were
  run in this phase; results are captured above.
- Rule 15 (no advancing phases while criteria fail): this phase's
  acceptance criteria (solution builds with zero warnings/errors, all tests
  pass, formatting is clean, CI workflow present, WPF shell proven to start)
  are all met as of this report.

## Outstanding for later phases

- FFmpeg/FFprobe and OpenCvSharp integration into `SceneForge.Media`.
- Actual application UI/UX beyond the startup shell.
- Cancellation-aware, bounded-concurrency processing pipeline in
  `SceneForge.Core`/`SceneForge.Infrastructure`.
- Real algorithmic benchmarks with recorded before/after evidence once an
  optimization exists to measure.
