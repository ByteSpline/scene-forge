# Phase 12 Report — Accuracy/Benchmark Evaluation System, and a Strict Release Review

Date: 2026-08-24

## Scope

Turn the existing, narrow, in-test-project fixture harness
(`TransitionDetectorFixtureTests` + a private `SyntheticVideoFixtureBuilder`)
into reusable infrastructure: a wider synthetic fixture matrix (transition
types, non-transition distractors, and input-format edge cases), a
standalone `accuracy/SceneForge.Accuracy` console tool reporting per-group
and aggregate precision/recall/F1/boundary-error/false-positives-per-minute
plus throughput/peak-managed-memory/peak-working-set, a committed
regression baseline with documented hardware, and a CI job that fails a PR
only on a genuine correctness regression, never on speed/memory noise.
Explicitly **not** in scope: any optimization of `TransitionDetector`,
its classifiers, or signal extractors — this phase only builds measurement
infrastructure and records the current, honest baseline.

This report also documents a **strict release review pass** performed
after the initial implementation: re-reading the diff against CLAUDE.md,
running the full build/test/format suite for real, and specifically
auditing for web dependencies, unbounded memory/concurrency, UI-thread
work, unsafe process invocation, timing drift, silent fallback, missing
cancellation, unverifiable claims, and packaging omissions. Three
confirmed issues were found and fixed as part of this same phase (see
"Strict review findings" below); the initial implementation's own
functional deliverables (fixture matrix, metrics, baseline, docs, CI job)
were found sound and are unchanged.

## Repository layout produced

```
accuracy/SceneForge.Accuracy/            - new console project (Exe, net8.0)
  Program.cs                             - wires Ctrl+C -> CancellationTokenSource, delegates to CommandDispatcher
  AssemblyInfo.cs                        - InternalsVisibleTo("SceneForge.Accuracy.Tests")
  Cli/
    CommandDispatcher.cs, CommandLineOptions.cs, ExitCodeMapper.cs
    GenerateCommand.cs, EvaluateCommand.cs, GateCommand.cs, UpdateBaselineCommand.cs
  Fixtures/
    FixtureGroup.cs                      - 8 transition + 4 distractor + 3 format-robustness groups
    SyntheticFixtureCatalog.cs           - supersedes the old test-only SyntheticVideoFixtureBuilder
    ExpectedTransition.cs, SyntheticFixture.cs
    FixtureManifest(Entry).cs, FixtureManifestJson.cs
  Evaluation/
    MetricsCalculator.cs                 - precision/recall/F1/boundary-error/FP-per-minute, pure/testable
    ResourceUsageSampler.cs              - peak GC.GetTotalMemory + peak Process.WorkingSet64
    FixtureEvaluationRunner.cs           - ties the catalog + real TransitionDetector + metrics together
    EvaluationReport.cs, GroupMetrics.cs, GroupEvaluationInput.cs, TransitionMatchOutcome.cs
    HardwareDescriber.cs, HardwareDescription.cs
    RegressionBaseline(Json).cs, RegressionGate(Result).cs
  Reporting/
    ConsoleReportPrinter.cs, EvaluationReportJsonWriter.cs
  Json/AccuracyJsonOptions.cs
  Baselines/regression-baseline.json     - committed, real measured numbers (see below)

tests/SceneForge.Accuracy.Tests/         - new xunit project, no real ffmpeg needed
  MetricsCalculatorTests.cs, RegressionGateTests.cs, ResourceUsageSamplerTests.cs
  ExitCodeMapperTests.cs, CommandDispatcherTests.cs, HardwareDescriberTests.cs   (added during the review pass)

tests/fixtures/
  manifest.json                          - compact, committed ground truth (32 fixtures)
  README.md                              - what this is, how to regenerate it, what is/isn't committed
  generated/                             - gitignored; the actual .mp4 fixtures, rebuilt on demand

tests/SceneForge.Media.Tests/Detection/Fixtures/
  TransitionDetectorFixtureTests.cs      - refactored onto SyntheticFixtureCatalog (deleted SyntheticVideoFixtureBuilder.cs)

docs/ACCURACY_REPORT.md, docs/BENCHMARK_REPORT.md   - commands, real measured data, honest limitations
.github/workflows/ci.yml                 - new `accuracy-regression` job (installs real ffmpeg, runs `gate`)
```

## Strict review findings

Performed against CLAUDE.md, `docs/ARCHITECTURE_DECISIONS.md`, and the
original task instructions, by re-reading the actual diff and re-running
verification rather than trusting the first pass's own summary.

### Blockers

None. Nothing found that would justify holding the phase.

### Major (confirmed, fixed in this pass)

1. **No command actually accepted cancellation — `CommandDispatcher`
   hardcoded `CancellationToken.None` for every command.** Every
   downstream method (`FixtureEvaluationRunner`, `SyntheticFixtureCatalog`,
   `ProcessRunner`) correctly threaded a `CancellationToken` parameter, but
   the token itself was always the inert `None`, with no `Console.CancelKeyPress`
   wiring anywhere. A real run takes 60–90 seconds of real ffmpeg encodes
   plus real detector analysis (measured below); pressing Ctrl+C mid-run
   would hit .NET's default abrupt-termination behavior rather than
   `ProcessRunner`'s own cooperative kill-the-process-tree path, risking
   orphaned `ffmpeg.exe` processes. Violates CLAUDE.md rule 5 ("every
   long-running or potentially blocking operation must support async
   cancellation and cooperative shutdown").
   **Fix:** `Program.cs` now creates a `CancellationTokenSource`, wires
   `Console.CancelKeyPress` to cancel it, and passes the real token through
   `CommandDispatcher.RunAsync(args, cancellationToken)` into every command.
2. **`HardwareDescriber` bypassed the project's own hardened `ProcessRunner`
   and used a raw, unbounded `Process.Start` with a dead-code timeout.**
   `TryGetCpuName` called `process.StandardOutput.ReadToEnd()` (which
   blocks until the process closes its stdout handle, with **no** timeout)
   *before* `process.WaitForExit(5000)` — the 5-second bound was
   unreachable code; the actual behavior was an unbounded, uncancellable
   blocking wait on a WMI/PowerShell subprocess, exercised on every
   `update-baseline` run. Also bypassed `ProcessRunner`'s bounded-output-capture
   and process-tree-teardown guarantees.
   **Fix:** rewritten onto `IProcessRunner`/`ProcessRunner` (same
   never-through-a-shell, discrete-`ArgumentList`, bounded-timeout,
   cancellable posture as every other process invocation in this
   codebase), with a real 5-second timeout via `ProcessExecutionRequest.Timeout`
   and a genuine `CancellationToken` parameter. A lookup-specific timeout
   or launch failure still degrades to `"Unknown"` (documented, visible in
   the output baseline — not silent), but a genuine external cancellation
   now propagates instead of being swallowed into that same fallback.
3. **`CommandLineOptions.Parse` ran outside `CommandDispatcher`'s
   `try`/`catch`.** Any malformed invocation (a dangling `--flag` with no
   trailing value — e.g. `generate --output`) threw an unhandled
   `ArgumentException` straight out of `RunAsync`/`Main`, producing a raw
   .NET stack-trace crash dump instead of the tool's intended clean
   usage/error message and exit code.
   **Fix:** parsing moved inside the `try` block; a new `ExitCodeMapper`
   maps `OperationCanceledException` → exit 130 with a clean "Cancelled."
   message, `ArgumentException` → exit 1 with the command name and message,
   and any other exception → exit 1 with its message — all without ever
   letting an exception escape `RunAsync` uncaught.

### Minor (reported, not fixed — below the "confirmed blocker/major" bar)

- `ResourceUsageSamplerTests.Sampler_CapturesAPeakReachedDuringTheSpan_...`
  reassigns `block = []` and then calls `GC.KeepAlive(block)` — the
  `KeepAlive` now targets the *empty* array, not the original 64 MB one.
  Harmless (the peak assertion already ran against the correct value
  beforehand) but confusing to a future reader; left as-is per this pass's
  "fix only confirmed blockers/major" scope.
- The task's original instruction said to store "generator scripts" in
  `tests/fixtures/`; the actual generator (`SyntheticFixtureCatalog.cs`)
  lives in `accuracy/SceneForge.Accuracy/Fixtures/` instead, with only the
  compact `manifest.json` and a `README.md` under `tests/fixtures/`. This
  was a deliberate, disclosed design decision from the original planning
  pass (code belongs with the buildable project that compiles it; `tests/fixtures/`
  holds data/docs), not an oversight — flagged here for traceability
  against the literal original wording.
- `accuracy/SceneForge.Accuracy.csproj` and `powershell`/`git` invocations
  rely on PATH resolution (unlike `FfmpegToolLocator`, which deliberately
  never does, per its own comment about packaged builds). Acceptable for a
  dev-only tool that is never part of the shipped WPF app, but worth
  naming explicitly rather than leaving implicit.

### Explicitly checked and found clean

- **Web dependencies:** none. No HTTP/cloud package added anywhere in the
  new projects; `Directory.Packages.props` additions are limited to
  `OpenCvSharp4`/`OpenCvSharp4.runtime.win` (mirrors `benchmarks/SceneForge.Benchmarks.csproj`'s
  existing pattern).
- **Unbounded memory/concurrency:** fixture evaluation is a single
  sequential `foreach` over a fixed 32-fixture matrix (no `Parallel.ForEach`,
  no unbounded fan-out); `ResourceUsageSampler` is one bounded
  `PeriodicTimer` loop; `ProcessRunner`'s own output capture is bounded
  (`MaxCapturedBytesPerStream`, pre-existing).
- **UI-thread work:** none — this is a console tool with no UI thread.
- **Unsafe process invocation:** `ProcessRunner` is used everywhere except
  the one confirmed-and-now-fixed `HardwareDescriber` gap above; no
  process is ever launched through a shell, and every argument is a
  discrete list entry, never an interpolated command string.
- **Timing drift:** `SyntheticFixtureCatalog`'s new fixtures (BlackHold,
  FrozenFrame, StaticShot, RapidMotion, VariableFrameRate, MixedResolution,
  Rotated) reuse the existing, already-reviewed fixed-offset/fixed-duration
  construction; the one genuinely best-effort case
  (`BuildVariableFrameRateHardCutAsync`) is disclosed as such in
  `docs/ACCURACY_REPORT.md`'s limitations, not silently assumed exact.
- **Packaging omissions:** `grep -r "SceneForge.Accuracy" src/` finds no
  reference from `SceneForge.App` or any other shipped project — the
  accuracy tool cannot end up in a `dotnet publish` of the product.
- **Silent fallback:** the one intentional fallback (`HardwareDescriber`'s
  CPU name → `"Unknown"`) is recorded directly in the baseline JSON and
  printed in the console/report output, never hidden.
- **Unverifiable claims:** the new `accuracy-regression` CI job (installs
  ffmpeg via Chocolatey, runs `gate`) has **not** been executed in real
  GitHub Actions — this sandbox cannot run GitHub-hosted CI. The exact
  command form (`dotnet run --project accuracy/SceneForge.Accuracy --configuration Release --no-build -- gate ...`)
  was verified locally against real ffmpeg (see below); the Chocolatey
  install step and the assumption that its `ffmpeg` package includes
  `ffprobe.exe` remain unverified until the workflow actually runs on a
  hosted runner. This is stated as a known gap, not claimed as proven.

## Verification (commands actually run, this pass)

```
dotnet build SceneForge.sln --configuration Release
  -> Build succeeded. 0 Warning(s). 0 Error(s).

dotnet test SceneForge.sln --no-build --configuration Release
  -> SceneForge.Core.Tests:          1 passed
  -> SceneForge.Accuracy.Tests:     29 passed   (15 pre-existing + 14 added this pass:
                                                  5 HardwareDescriberTests, 4 ExitCodeMapperTests,
                                                  5 CommandDispatcherTests)
  -> SceneForge.Infrastructure.Tests: 45 passed
  -> SceneForge.App.Tests:            58 passed
  -> SceneForge.Media.Tests:         474 passed, 10 skipped (real-ffmpeg-gated; ffmpeg not
                                                  present in this sandbox's default state,
                                                  matching documented CI behavior)
  -> 607 passed, 0 failed, 10 skipped, 0 errors, across the whole solution.

dotnet format SceneForge.sln --verify-no-changes
  -> No formatting violations.

# Real end-to-end run, real ffmpeg (winget-installed Gyan build) temporarily
# copied to accuracy/SceneForge.Accuracy/bin/<Debug|Release>/net8.0/tools/ffmpeg/,
# exactly the CI job's own staging step:
dotnet run --project accuracy/SceneForge.Accuracy --configuration Release --no-build -- \
  gate --baseline accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json
  -> 32 fixtures built and analyzed; Regression gate: PASSED (no correctness
     regression vs. baseline); exit code 0. Re-run twice after the fixes above
     (once in Debug, once in the exact Release/--no-build CI form) with
     identical correctness metrics both times (11 TP / 13 FN / 17 FP
     aggregate) and only informational performance deltas (throughput
     -1.5% to -3.0%, peak managed memory +12% to +21%, peak working set
     +6% to +7% across runs) - exactly the "reported but never gates"
     behavior RegressionGate is designed to produce.
```

Full per-group numbers, hardware, and limitations are in
[`docs/ACCURACY_REPORT.md`](ACCURACY_REPORT.md) and
[`docs/BENCHMARK_REPORT.md`](BENCHMARK_REPORT.md) (unchanged by this
review pass — the underlying detector and fixture ground truth were not
modified, so those numbers still stand).

## CLAUDE.md rule check

- Rule 1 (native WPF only): unaffected — the accuracy tool is a separate
  console dev tool, not referenced by `SceneForge.App`, not part of the
  shipped product (confirmed via `grep`, above).
- Rule 2 (no web/cloud/telemetry/runtime-network requirement): confirmed
  clean — see "Explicitly checked and found clean" above. CI's Chocolatey
  install is CI infrastructure (like `dotnet restore`'s own NuGet fetch),
  not a runtime dependency of any shipped code.
- Rule 3 (FFmpeg/FFprobe/OpenCvSharp media stack): the tool exclusively
  reuses the existing `SceneForge.Media` pipeline; no alternate media
  stack introduced.
- Rule 5 (async cancellation/cooperative shutdown): the three Major
  findings above were exactly rule-5 gaps; all three are now fixed and
  regression-tested.
- Rule 8 (test-first for new algorithmic behavior): `MetricsCalculator`
  and `RegressionGate` were built test-first in the original pass;
  `ExitCodeMapper`'s exception-to-exit-code policy and the cancellation/
  timeout distinction in `HardwareDescriber` were added test-first in this
  review pass.
- Rule 9 (benchmark evidence): the committed baseline and both reports
  carry real, measured numbers from this machine, re-confirmed identical
  (aggregate correctness) across three separate runs during this review.
- Rule 10 (never claim absolute accuracy): unchanged from the original
  pass — both docs already carry explicit, honest limitations sections.
- Rule 13 (format/build/test before ending): all three re-run in this
  pass, commands and results recorded above verbatim.

## What a future phase would still need to verify

- The `accuracy-regression` CI job's actual execution on `windows-latest`
  (Chocolatey ffmpeg availability/composition, `actions/upload-artifact@v4`
  step) — genuinely unverifiable outside GitHub Actions itself.
- Whether `Console.CancelKeyPress`-triggered cancellation actually kills an
  in-flight `ffmpeg.exe` child in an interactive terminal session — verified
  by code inspection (it reuses `ProcessRunner`'s already-tested
  cancellation/kill-tree path) and by the new `CommandDispatcherTests`
  proving no exception escapes `RunAsync` uncaught, but not by an
  interactive Ctrl+C reproduction, which this non-interactive review
  environment cannot perform.
