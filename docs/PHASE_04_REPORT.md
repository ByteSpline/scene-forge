# Phase 04 Report — Media Foundation (FFmpeg/FFprobe Process Layer)

Date: 2026-08-22

## Scope

Build the media *foundation* only — immutable domain models, a hardened
process-invocation abstraction, an FFprobe-backed metadata service, and
input/output path validation — for `SceneForge.Media`. Explicitly **not** in
scope: scene detection, OpenCvSharp integration, or any `SceneForge.App`
wiring. All of that remains for later phases per
[docs/ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md) rule 15 (don't
advance while the current phase's criteria are unresolved).

This report was written under an explicit strict-reviewer mandate: verify the
diff and tests directly (not just describe intent), run build/tests/relevant
benchmarks, and check specifically for web dependencies, unbounded
memory/concurrency, UI-thread work, unsafe process invocation, timing drift,
silent fallback, missing cancellation, unverifiable claims, and packaging
omissions. The self-review section below documents what that pass found and
fixed, not just the original design.

## Branch state note

Before this phase, the working tree on `4-ffmpeg-media-probing` was missing
the Phase 3 scaffold entirely (no `.sln`, no `.csproj` files, only stale
`bin`/`obj` folders) — the scaffold commit only existed on
`3-scaffold-solution`, which is a direct ancestor-compatible fast-forward of
this branch. It was merged in first (`git merge 3-scaffold-solution`, a pure
fast-forward, zero conflicts) so this phase has something to build on.

## Repository layout produced

```
src/SceneForge.Media/
  AssemblyInfo.cs                    (InternalsVisibleTo SceneForge.Media.Tests)
  Domain/
    RationalFrameRate.cs             (immutable numerator/denominator frame rate)
    TimeRange.cs                     (immutable, validated [Start, End] range)
    VideoStreamInfo.cs
    AudioStreamInfo.cs
    MediaInfo.cs
  Processes/
    IProcessRunner.cs
    ProcessRunner.cs                 (hardened process invocation)
    ProcessExecutionRequest.cs / ProcessExecutionResult.cs / ProcessOutputLine.cs
    BoundedTextCollector.cs          (bounded stdout/stderr capture)
    ProcessLaunchException.cs / ProcessTimeoutException.cs
  Tooling/
    IFfmpegToolLocator.cs / FfmpegToolLocator.cs / FfmpegToolPaths.cs
    FfmpegToolsNotFoundException.cs / FfmpegToolsIncompatibleException.cs
  Probing/
    IFfprobeService.cs / FfprobeService.cs / FfprobeExecutionException.cs
    Json/ (FfprobeOutputDto, FfprobeFormatDto, FfprobeStreamDto, FfprobeSideDataDto)
  Validation/
    MediaPathValidator.cs
    OutputDirectoryValidator.cs
    MediaValidationException.cs / MediaValidationFailureReason.cs

tests/SceneForge.Media.Tests/
  Domain/            RationalFrameRateTests, TimeRangeTests, MediaInfoTests
  Processes/         ProcessRunnerTests, ProcessRunnerBuildStartInfoTests,
                      BoundedTextCollectorTests, ProcessExecutionRequestTests
  Tooling/           FfmpegToolLocatorTests, FfmpegToolLocatorIntegrationTests
  Probing/           FfprobeServiceTests, FfprobeServiceIntegrationTests
  Validation/        MediaPathValidatorTests, OutputDirectoryValidatorTests
  TestSupport/        FakeProcessRunner, FakeFfmpegToolLocator, RealFfmpegAvailability
  Fixtures/
    Json/            video_audio.json, audio_only.json (real ffprobe 9.0.1 output, trimmed)
    Media/           sample_video_audio.mp4, sample_audio_only.m4a
                     (~56 KB total; synthesized locally with `ffmpeg -f lavfi`,
                      no network, no third-party content)
```

No changes to `SceneForge.App`, `SceneForge.Core`, or `SceneForge.Infrastructure`.
`SceneForge.Media` still only references `SceneForge.Core` (unchanged
dependency graph from Phase 3).

## Design summary

### Domain models (`Domain/`)

`RationalFrameRate` and `TimeRange` are non-positional `readonly record struct`
types with hand-written validating constructors (negative/inconsistent inputs
throw `ArgumentOutOfRangeException`/`FormatException`); `VideoStreamInfo`,
`AudioStreamInfo`, and `MediaInfo` are `sealed record` reference types with
`required init` properties, so a fully-constructed instance can never be
partially initialized or mutated afterward. Collections are exposed as
`IReadOnlyList<T>` backed by `List<T>.AsReadOnly()`, never a raw array the
caller could still hold a mutable reference to.

### `IProcessRunner` / `ProcessRunner` (`Processes/`)

- Never touches a shell: `UseShellExecute = false` is hardcoded, arguments
  are always added to `ProcessStartInfo.ArgumentList` (never concatenated
  into the legacy `Arguments` string), so no argument can be reinterpreted as
  shell syntax regardless of its content.
- stdout/stderr are read concurrently via two `StreamReader.ReadLineAsync`
  loops (not `*DataReceived` events), captured into a `BoundedTextCollector`
  capped at `MaxCapturedBytesPerStream` (clamped to [1 KB, 64 MB] regardless
  of what a caller requests) and simultaneously pushed to an optional
  `IProgress<ProcessOutputLine>` for live progress/cancellation-friendly UIs
  in later phases.
- An external `CancellationToken` and an internal `Timeout` are linked into
  one token; whichever fires first kills the process via
  `Process.Kill(entireProcessTree: true)` — the *entire* tree, not just the
  immediate child — and the two cases are surfaced as distinct exception
  types (plain `OperationCanceledException` for caller cancellation,
  `ProcessTimeoutException : OperationCanceledException` for a timeout, so
  existing cancellation-handling code still catches both, but callers that
  care can branch on the more specific type).

### `IFfmpegToolLocator` / `FfmpegToolLocator` (`Tooling/`)

Resolves `tools/ffmpeg/ffprobe.exe` and `tools/ffmpeg/ffmpeg.exe` strictly
relative to `AppContext.BaseDirectory` (the running app's own directory) —
never the system `PATH`, and always as a full path passed to `ProcessRunner`
(a bare filename would trigger a `PATH` search; a full path never does).
Throws `FfmpegToolsNotFoundException` if either file is absent, and
`FfmpegToolsIncompatibleException` if `-version` fails to launch, times out,
exits non-zero, or its banner doesn't start with `"ffprobe version"` /
`"ffmpeg version"` — both are meant to be surfaced as a clear startup error,
not discovered later on a user's first probe.

### `IFfprobeService` / `FfprobeService` (`Probing/`)

Runs `ffprobe -v error -print_format json -show_format -show_streams <path>`,
parses the JSON into internal DTOs shaped from **real captured ffprobe 9.0.1
output** (see "Real-binary verification" below), and maps it into the
immutable domain model:

- Video/audio streams are matched by `codec_type`; other stream kinds
  (subtitle, data) are intentionally not modeled this phase and are skipped.
- `IsVariableFrameRate` is a documented heuristic: `r_frame_rate` and
  `avg_frame_rate` disagreeing by more than 0.01 fps. Per CLAUDE.md rule 10,
  this is never reported as a guarantee — see the code comment on
  `VideoStreamInfo.IsVariableFrameRate`.
- Rotation prefers the modern `side_data_list` "Display Matrix" entry over
  the legacy `tags.rotate`, normalized into `[0, 360)`. This path is
  documented as verified only against crafted fixtures, not a real
  device-rotated file — see "What I could not verify" below.
- `width`/`height` (video) and `sample_rate`/`channels` (audio) are
  **required**: a stream missing one throws `FfprobeExecutionException`
  rather than silently defaulting to `0` (this was a bug caught and fixed
  during self-review — see below).
- A file with zero video/audio streams, or no derivable duration from either
  `format.duration` or any per-stream `duration`/`duration_ts` × `time_base`,
  throws `MediaValidationException` with a specific
  `MediaValidationFailureReason` rather than returning a half-populated
  `MediaInfo`.

### Validation (`Validation/`)

`MediaPathValidator.ValidateInputFile` resolves and checks a local input path
(rejects null/whitespace, invalid characters, missing files, directories).
`OutputDirectoryValidator.EnsureWritable` creates the directory if needed and
*proves* writability by writing and deleting a probe file, rather than
trusting ACL metadata; `EnsureDoesNotOverwriteInput` case-insensitively
compares full paths so an output can never resolve to the same file as the
input (CLAUDE.md rules 11–12). Both are synchronous — these are local
filesystem checks in the microsecond range, not the kind of blocking
operation rule 5 is aimed at (process execution, which *is* fully
cancellable, above).

## Self-review findings (strict-reviewer pass)

Run against the checklist in the task: web dependencies, unbounded
memory/concurrency, UI-thread work, unsafe process invocation, timing drift,
silent fallback, missing cancellation, unverifiable claims, packaging
omissions.

| Area | Finding | Resolution |
|---|---|---|
| Silent fallback | `VideoStreamInfo.Width/Height` and `AudioStreamInfo.SampleRateHz/Channels` defaulted to `0` when ffprobe didn't report them, producing a `MediaInfo` that looked valid but had meaningless geometry/sample rate. | Fixed: these fields now throw `FfprobeExecutionException` when missing (`RequireField` helper in `FfprobeService`). Regression tests added: `ProbeAsync_VideoStreamMissingWidth_ThrowsInsteadOfDefaultingToZero`, `ProbeAsync_AudioStreamMissingSampleRate_ThrowsInsteadOfDefaultingToZero`. |
| Unverifiable claims | Per-stream duration parsing accepted a negative `seconds` value from a malformed `duration` field without question. | Fixed: `ParseSecondsOrNull` now rejects negative values (`seconds >= 0` guard). |
| Unverifiable claims | Rotation sign convention (ffprobe's `side_data_list` rotation, e.g. `-90` for some physical rotations) was implicitly trusted without a real rotated fixture to confirm against. | Documented in code (`ResolveRotationDegrees` comment) and below ("What I could not verify"); not presented as a verified guarantee. |
| Unsafe process invocation | Needed a way to *prove*, not just assert, that arguments are never shell-interpreted. | Added `ProcessRunnerBuildStartInfoTests` against `internal static ProcessRunner.BuildStartInfo` (via `InternalsVisibleTo`): asserts `UseShellExecute == false` and that arguments containing `&`, `|`, `"`, `;`, `$(...)` land in `ArgumentList` unchanged with the legacy `Arguments` string left empty. |
| Unbounded memory/concurrency | `BoundedTextCollector` and `MaxCapturedBytesPerStream` clamp capture, but confirm the clamp itself is enforced regardless of caller input. | Covered by `ProcessExecutionRequestTests` (floor 1 KB, ceiling 64 MB) and `BoundedTextCollectorTests` (truncation + no further growth after truncation). |
| Missing cancellation | Confirm cancellation actually kills the OS process tree, not just that `RunAsync` returns. | `ProcessRunnerTests` counts real `ping.exe`/`PING` processes before/after cancellation and timeout (both a direct child and a `cmd.exe`-spawned grandchild), rather than only asserting on elapsed wall-clock time. |
| Packaging omissions | `FfmpegToolLocator` resolves `tools/ffmpeg` correctly, but there is no build/publish step that actually populates that folder with real binaries in a packaged build. | Not fixed — out of scope for this phase (the task asked for the *resolution logic and startup error*, not packaging). Called out explicitly under "Outstanding for later phases" below so it isn't silently missing. |
| Web/network dependency | None found. | `SceneForge.Media` has no `HttpClient`, no socket, no DNS use anywhere in `src/`. The one test-only NuGet package added (`Xunit.SkippableFact`) is a build-time dependency, identical in kind to `xunit`/`BenchmarkDotNet` already present since Phase 3, and is never referenced by `src/`. |
| UI-thread work | None found. | `SceneForge.Media.csproj` has no `UseWPF`, no `Dispatcher`/`SynchronizationContext` reference anywhere in `src/`. |
| Timing drift | None found. | `ProcessRunner` uses `Stopwatch` (monotonic) for `Elapsed`, and `CancellationTokenSource(TimeSpan)` for timeouts — no `DateTime.Now` comparisons anywhere in the process/probing code. |

## Real-binary verification

FFmpeg/ffprobe are **not** committed to the repo and are never referenced via
`PATH` in product code (`.gitignore` now excludes `tools/`). However, this
dev machine happens to have ffmpeg 9.0.1 installed via `winget`
(`Gyan.FFmpeg.Shared`), which was used, read-only w.r.t. the product, to:

1. Generate the two small (~56 KB total) synthetic fixture media files under
   `Fixtures/Media/` with `ffmpeg -f lavfi -i testsrc=... -i sine=...` — no
   network, no copyrighted input.
2. Capture real `ffprobe -show_format -show_streams` JSON output against
   those files, which shaped the `Json/*Dto.cs` types and seeded
   `Fixtures/Json/*.json` (trimmed of fields the mapper doesn't use).
3. Temporarily copy the full ffmpeg `bin/` folder (240 MB, all required DLLs
   for the shared build) into
   `tests/SceneForge.Media.Tests/bin/{Debug,Release}/net8.0/tools/ffmpeg/`
   and run the `[SkippableFact]` integration tests end-to-end for real:

   ```
   dotnet test tests/SceneForge.Media.Tests/SceneForge.Media.Tests.csproj --no-build --filter "FullyQualifiedName~Integration"
   Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 822 ms (Debug)
   Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 799 ms (Debug, re-verified after the RequireField fix)
   ```

   This proves the full real pipeline — `ProcessRunner` → `FfmpegToolLocator`
   → real `ffprobe.exe` → JSON parsing → domain mapping — end to end,
   including a real invalid-file negative case (`moov atom not found`, exit
   code 1). The 240 MB copy was deleted afterward; it was never committed
   (`tools/` is gitignored) and a fresh clone or CI run will always see these
   four tests skip, which is exactly the intended "optional, skipped when
   binaries are absent" behavior.

### What I could not verify

Attempting to bake rotation metadata (`-metadata:s:v:0 rotate=90`, both on
direct encode and on `-c copy` remux) into an MP4 with this ffmpeg 9.0.1
build did not produce any `tags.rotate` or `side_data_list` on read-back —
the mp4 muxer in this build silently drops it. Rotation handling is
therefore verified only against hand-crafted JSON fixtures matching the
documented ffprobe schema (`ProbeAsync_RotationViaDisplayMatrix_...`,
`ProbeAsync_RotationViaLegacyTag_...`,
`ProbeAsync_DisplayMatrixRotation_TakesPrecedenceOverLegacyTag`), not a real
rotated file. This is called out in code and here rather than left implicit,
per CLAUDE.md rule 10 (never claim unverified behavior as absolute). True
variable-frame-rate video was similarly not synthesized (irregular
inter-frame timing isn't reliably producible with `lavfi` test sources); the
VFR *heuristic itself* (comparing `r_frame_rate`/`avg_frame_rate`) is
verified via JSON fixtures instead, which is the right level for that logic
regardless of real-file availability.

## Commands executed and results

All commands run from `C:\Users\Bwp COmputers\Desktop\scene-forge` with the
pinned .NET 8.0.424 SDK.

### Format

```
dotnet format SceneForge.sln --verify-no-changes
```
First run reported `ENDOFLINE` errors (new files created with LF, repo
requires CRLF per `.editorconfig`) — identical situation to Phase 3.
`dotnet format SceneForge.sln` (no verify) fixed it in place; a follow-up
`--verify-no-changes` run produced no output (zero violations).

### Build (Debug)

```
dotnet build SceneForge.sln
```
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
One real compile-time finding along the way: `CA1711` rejected the name
`ProcessOutputStream` (ends in "Stream", collides in spirit with
`System.IO.Stream`) — renamed to `ProcessOutputChannel` throughout.

### Test (Debug)

```
dotnet test SceneForge.sln --no-build
```
```
Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Core.Tests.dll (net8.0)
Passed! - Failed: 0, Passed: 92, Skipped: 4, Total: 96 - SceneForge.Media.Tests.dll (net8.0)
```
The 4 skipped are the real-binary integration tests (expected: no
`tools/ffmpeg` in this build output at the time of this run).

### Build + Test (Release, matching CI)

```
dotnet build SceneForge.sln --configuration Release
dotnet test SceneForge.sln --no-build --configuration Release
```
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1 - SceneForge.Core.Tests.dll (net8.0)
Passed! - Failed: 0, Passed: 92, Skipped: 4, Total: 96 - SceneForge.Media.Tests.dll (net8.0)
```

### Benchmark harness

```
cd benchmarks/SceneForge.Benchmarks
dotnet run --configuration Release --no-build -- --filter '*'
```
No new optimization exists in this phase to benchmark (this is foundational
plumbing, not an algorithmic change), so — consistent with Phase 3 — this
only re-confirms the harness itself still builds and runs to completion
against the unchanged `ModuleInfoBenchmarks` (`SceneForge.Media` isn't
referenced by the benchmark project). No performance claims are made.

## Test inventory

- **Domain** (20 tests): `RationalFrameRate` parsing/validation/equality,
  `TimeRange` validation/`Contains`/`Overlaps`, `MediaInfo` primary-stream
  selection.
- **Processes** (real-process, no ffmpeg needed — `dotnet.exe`, `ping.exe`,
  `cmd.exe` only): exit code/stdout/stderr capture, output progress
  callback, bounded-capture truncation, missing-executable error, external
  cancellation kills the process, timeout kills the process, cancellation
  kills an entire `cmd.exe → ping.exe` tree (not just the direct child), plus
  the `BuildStartInfo` hardening tests and `BoundedTextCollector`/
  `ProcessExecutionRequest` clamp unit tests.
- **Tooling**: missing/partial binaries, version-check failure/timeout/bad
  banner (mocked `IProcessRunner`), plus one real-binary
  `[SkippableFact]`.
- **Probing**: input validation ordering, argument construction, non-zero
  exit, malformed JSON, no-streams, no-duration, duration fallback via
  `duration_ts`×`time_base`, full field mapping against real captured
  ffprobe JSON, rotation (both sources + precedence), VFR heuristic
  (positive and negative), the two `RequireField` regression tests, and
  three real-binary `[SkippableFact]` tests (happy path, audio-only,
  invalid-file negative case).
- **Validation**: input path (missing/whitespace/directory/control
  character/relative-path resolution), output directory (writable, creates
  missing directories, leaves no probe file behind, rejects a file-as-directory),
  overwrite guard (same path, same path different casing, different paths).

## Compliance notes against CLAUDE.md

- Rule 1–2 (native WPF, no Electron/web/cloud/telemetry): satisfied —
  `SceneForge.Media` has no UI, network, or telemetry code.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): FFprobe half satisfied this phase;
  OpenCvSharp remains for the scene-detection phase, per the explicit
  "not scene detection" scope of this task.
- Rule 4 (clean architecture): unchanged dependency graph from Phase 3
  (`Media → Core` only); no UI concerns in `SceneForge.Media`.
- Rule 5 (cancellation/cooperative shutdown): every process-spawning
  operation (`ProcessRunner.RunAsync`, `FfmpegToolLocator.LocateAsync`,
  `FfprobeService.ProbeAsync`) takes and honors a `CancellationToken`,
  verified by killing a real OS process tree in tests, not just returning
  early.
- Rule 6 (bounded memory/concurrency): stdout/stderr capture is hard-capped
  (1 KB–64 MB, clamped regardless of caller input); no unbounded
  queues/caches/fan-out were introduced. Note: this phase probes one file
  per call — bounding *concurrent* probes across many files is an
  orchestration-layer concern for whichever later phase introduces batch
  processing, not this foundation.
- Rule 7 (no full-buffering): `ffprobe` itself streams the file; SceneForge
  only captures its bounded JSON stdout, never the media file's bytes.
- Rule 8 (test-first): every behavior above has a corresponding test;
  the two self-review fixes (`RequireField`, negative-duration guard) each
  have a regression test added in the same change.
- Rule 9 (benchmark before/after): not applicable — no optimization work
  this phase; the harness itself was re-run to confirm it's still wired
  (see above).
- Rule 10 (never claim absolute accuracy): `IsVariableFrameRate` and
  `RotationDegrees` are both documented as heuristic/best-effort in code and
  in this report, with the specific verification gap (no real rotated
  fixture) stated explicitly rather than implied to be fully verified.
- Rule 11–12 (preserve user files, output to new path only):
  `OutputDirectoryValidator.EnsureDoesNotOverwriteInput` exists and is
  tested; nothing in this phase writes any file except its own
  create-and-delete writability probe file in a caller-specified output
  directory. No input file is ever opened for writing anywhere in this
  phase's code.
- Rule 13 (format/build/tests before ending a task): all three run above,
  Debug and Release.
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (Decision 3 already
  anticipated FFprobe arriving in `SceneForge.Media`).
- Rule 15 (don't advance while criteria fail): this phase's own acceptance
  criteria (solution builds/tests clean in Debug and Release, formatting
  clean, no scene-detection code introduced) are met as of this report.

## Outstanding for later phases

- OpenCvSharp integration and actual scene-detection algorithms.
- A build/publish step that populates a shipped app's `tools/ffmpeg/`
  directory with real binaries (this phase only implements *resolving and
  validating* that directory, not packaging into it) — flagged explicitly
  above under "packaging omissions" rather than left silently unaddressed.
- `SceneForge.App` wiring (calling `FfmpegToolLocator` at startup to produce
  a user-visible clear error) — no App changes were made this phase.
- Bounding *concurrent* probes/encodes once a batch-processing orchestration
  layer exists (this phase is single-file-at-a-time by design).
- Real-file verification of rotation metadata once a fixture with genuine
  device-recorded (or otherwise reliably reproducible) rotation is
  available; current coverage is fixture-JSON-only for that specific path.
