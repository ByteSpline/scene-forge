# Phase 09 Report — RenderPlan and FFmpegRenderService

Date: 2026-08-23

## Scope

Build the export/render step that finally consumes a `TimelinePlan`
(Phase 8) and writes an actual video file: `RenderPlanBuilder` translates a
`TimelinePlan` plus the original source file's own probed `MediaInfo` and a
caller-chosen output/audio spec into a `RenderPlan` — an ordered list of
trims expressed in the **original source file's own timestamps**, never an
analysis-proxy timestamp (Phases 4-8 always measured everything against the
original file's timeline even when scoring read frames from a downscaled
analysis stream; this phase's job is to keep that invariant true all the way
through to the rendered pixels). `FFmpegRenderService` then renders that
plan in a single ffmpeg pass wherever possible: one filter graph trims,
rotates, fits (letterbox/fill/stretch), and normalizes every segment to a
shared resolution/pixel-format/sample-aspect-ratio/frame-rate/time-base
before concatenating, discards the source file's own audio structurally
(never referencing `0:a` in any `-map`), maps only the one supplied audio
track, selects a video encoder by actually running a short real encode
against each of NVENC/Quick Sync/AMF/libx264 in turn rather than inferring
capability from a GPU name, falls back to libx264 if a selected hardware
encoder's real render attempt still fails, streams machine-readable
progress/ETA from ffmpeg's own `-progress` output, and verifies the
rendered file afterward via ffprobe and direct frame-decode checks before
returning. Explicitly **not** in scope: `SceneForge.App` UI wiring (no UI
surface exists yet, as every prior phase's report has noted), multi-source-
file timelines (every render is still built from one source file, the same
single-file shape `CleanClipExtractor`/`TimelinePlanner` have used since
Phase 6), and encoder-quality benchmarking/tuning (see Compliance notes,
Rule 9).

## Repository layout produced

```
src/SceneForge.Media/Rendering/
  AspectFitMode.cs, SampleAspectRatio.cs, RenderOutputSpec.cs,
  RenderAudioTrackSpec.cs, RenderSegment.cs, RenderPlan.cs,
  RenderPlanRequest.cs, RenderPlanException.cs, IRenderPlanBuilder.cs,
  RenderPlanBuilder.cs
  VideoEncoderKind.cs, VideoEncoderSelection.cs, IHardwareEncoderProbe.cs,
  HardwareEncoderProbe.cs, RenderExecutionException.cs
  RenderProgress.cs, RenderResult.cs, RenderVerificationResult.cs,
  RenderVerificationException.cs, IFFmpegRenderService.cs,
  FFmpegRenderService.cs
  Internal/
    RenderFilterGraphBuilder.cs, RenderProgressParser.cs,
    RenderOutputVerifier.cs, SynchronousProgress.cs

tests/SceneForge.Media.Tests/Rendering/
  RenderPlanBuilderTests.cs (15), RenderFilterGraphBuilderTests.cs (6
    methods / 9 cases), RenderProgressParserTests.cs (6),
  HardwareEncoderProbeTests.cs (6), RenderOutputVerifierTests.cs (7),
  FFmpegRenderServiceTests.cs (11), FFmpegRenderServiceIntegrationTests.cs
    (2, real-binary, SkippableFact)

tests/SceneForge.Media.Tests/TestSupport/
  MediaInfoBuilder.cs (new), TimelinePlanBuilder.cs (new),
  RecordingProgress.cs (new)
```

No `.csproj` changes: `Rendering/` lives in the existing `SceneForge.Media`
project, reuses `Processes`/`Tooling`/`Probing`/`Validation` exactly as they
already existed, and needs no new package reference (no new encoder SDK, no
new JSON/CLI dependency — ffmpeg's own `-progress` output is parsed as plain
`key=value` text). The test project likewise needed no new package
reference.

## Design summary

### `RenderPlanBuilder` is pure and synchronous, the same shape `TimelinePlanner` already established

`RenderPlanRequest.SourceMediaInfo` is a caller-supplied fact (the same
`MediaInfo` an earlier pipeline stage already probed via `IFfprobeService`),
not something `RenderPlanBuilder` probes itself. This keeps `Build` free of
I/O beyond the cheap `File.Exists`/`Path.GetFullPath` checks
`MediaPathValidator` already performs elsewhere, mirroring
`TimelinePlanner`'s "facts in, no process spawned" shape from
docs/PHASE_08_REPORT.md. `TimelinePlacement.SourceRange`/`UsedDuration` are
carried into `RenderSegment` completely unmodified — `RenderPlanBuilder`
never recomputes or re-derives a trim point, since Phase 8's
`TimelinePlacement.SourceRange` is already stated to be "the source clip's
own Range, unmodified," itself always measured against the original file
(Phase 7). This is the literal mechanism behind "render from the original
media, never from analysis proxies": the render step simply never has
access to an analysis-proxy timestamp to begin with, because `TimelinePlan`
never carried one.

### Boundary validation catches a mismatched `MediaInfo` before any process spawns

`RenderPlanBuilder.Build` checks every placement's `SourceRange.Start +
UsedDuration` against `SourceMediaInfo.PrimaryVideoStream.Duration`
(allowing 500ms of slack for ffprobe's own container-duration
rounding/overhead — `Build_SegmentWithinSmallProbeSlack_Succeeds` and
`Build_SegmentExceedsProbedSourceDuration_Throws` cover both sides of that
boundary directly) so a caller who accidentally passes a `TimelinePlan` and
a `MediaInfo` that do not actually correspond to the same file fails loudly
with a `RenderPlanException` naming the exact placement and durations
involved, rather than producing a plan `FFmpegRenderService` would later
fail on with a much less specific ffmpeg stderr message. `RenderAudioTrackSpec.TrimStart`/`TrimDuration`
are validated the same way (non-negative / strictly positive respectively)
— see Self-review findings below for how this check was added.

### One filter graph, one encode pass: `RenderFilterGraphBuilder`

Every segment becomes `[0:v]trim=start=S:duration=D,setpts=PTS-STARTPTS`
plus, in order: a rotation filter selected from
`RenderPlan.SourceRotationDegrees` (`transpose=1`/`hflip,vflip`/`transpose=2`
for 90/180/270, nothing for 0 — `RenderFilterGraphBuilderTests.Build_AppliesRotationFilterMatchingSourceRotationDegrees`
covers all four), a fit filter selected from `RenderOutputSpec.FitMode`
(`scale=...:force_original_aspect_ratio=decrease,pad=...` for Letterbox,
`scale=...:force_original_aspect_ratio=increase,crop=...` for Fill, plain
`scale=...` for Stretch — `Build_AppliesFitModeSpecificFilter` covers all
three), then the shared `fps=.../format=.../setsar=...` normalization every
segment ends with identically (`Build_EverySegmentEndsWithSharedFpsFormatAndSar`)
so ffmpeg's `concat` filter — which requires every input to already agree on
format/dimensions/timebase — never has to reconcile a mismatch itself. The
audio track (input 1) gets its own `atrim`/`asetpts`/`aformat` chain trimmed
to `RenderAudioTrackSpec.TrimStart`/`TrimDuration`. Because every filter
graph is built from exactly two `-i` inputs (source video once, audio file
once) regardless of segment count — never one `-i`/`-ss` pair per segment —
this is a single ffmpeg process, a single decode pass over the source file,
and a single encode pass over the concatenated result, the phase brief's
"prefer a single final encoding pass" satisfied structurally rather than as
a special-cased optimization.

**Post-Phase-16 update:** this single-graph design scales the in-memory
filtergraph with *total* segment count (~7 nodes + one `split` output + one
`concat` input per segment), which Phase 16's never-short-output guarantee -
combined with any long audio target - pushes into the many hundreds
regardless of repetition (heavy reuse of a tiny clip pool, or a
footage-rich source cut into hundreds of distinct trims). Both overflow a
graph real ffmpeg 9.x fails to allocate. For plans past
`FFmpegRenderService.MaxSegmentsPerFilterGraph` (60), `FFmpegRenderService`
pre-renders the timeline in pieces and assembles them with ffmpeg's concat
*demuxer* (video stream-copied, audio via `BuildAudioOnlyGraph`): either one
piece per *distinct* segment (`DistinctDedup`, when a small set repeats) or
one piece per bounded batch of <= 60 placements
(`RenderFilterGraphBuilder.BuildVideoConcat`, the general `Batched` path).
No single ffmpeg invocation ever carries more than 60 segments' worth of
filtergraph. The single-graph path here stays the default for plans at or
below 60 segments. See docs/PHASE_16_REPORT.md, the three "Post-acceptance
fix" sections.

### Source audio removed structurally, not by an extra "mute" step

`[0:a]` (the source file's own audio stream) never appears anywhere in the
filter graph or in any `-map` argument. With `-filter_complex` in use,
ffmpeg does not auto-map any stream that was not explicitly referenced, so
omitting `0:a` is sufficient on its own to guarantee it never reaches the
output — `RenderFilterGraphBuilderTests.Build_NeverReferencesSourceAudioStream`
and `FFmpegRenderServiceTests.RenderAsync_NeverMapsSourceAudioStream` assert
this directly at both the filter-graph and full-argument-list level, and
`FFmpegRenderServiceIntegrationTests.RenderAsync_RealFfmpegAgainstRealFiles_NeverContainsSourceAudioStreamMetadata`
confirms it against a real ffmpeg render (exactly one audio stream in the
output, carrying the supplied track's codec, never the source's).

### Bounded intermediate strategy: a temporary filter script file past a documented character threshold

`FFmpegRenderService` measures the built filter-graph string's length
before deciding how to pass it to ffmpeg: at or under
`InlineFilterGraphCharacterThreshold` (6,000 characters — a documented,
conservative safety margin under Win32 `CreateProcess`'s ~32,767-character
total command-line limit, chosen because `ProcessRunner` never goes through
a shell so the binding constraint is that Win32 limit, not `cmd.exe`'s much
smaller 8,191-character one) it is passed inline via `-filter_complex`;
above it, it is written to a temporary file under
`%TEMP%\SceneForge\render-filters\<guid>.filter` and passed via
`-/filter_complex <file>` instead. (Originally `-filter_complex_script
<file>`; that option was removed in ffmpeg 8.0 and every current build
rejects it — see the "Post-acceptance fix" section of
docs/PHASE_16_REPORT.md. `-/filter_complex` is ffmpeg's generic
"read this option's value from a file" form, available since 7.0, and is
exactly equivalent to spelling the whole graph out inline.) Exactly one
file is ever written per render attempt, and it is always deleted in a
`finally` block regardless of whether the render succeeded, failed, or was
cancelled — a bounded, single-file, always-cleaned-up strategy, never an
accumulating cache (CLAUDE.md rule 6/7).
`FFmpegRenderServiceTests.RenderAsync_ManySegments_UsesFilterComplexScriptFile_AndDeletesItAfterward`
builds a 400-segment plan (its filter graph is tens of thousands of
characters, far past the threshold), asserts `-/filter_complex <file>` was
used (and `-filter_complex_script` never emitted) and the file existed
while ffmpeg was "running" (inside the fake process handler), and asserts
it is gone immediately afterward;
`RenderAsync_FewSegments_UsesInlineFilterComplex` asserts the inline path
for a small plan; and the real-binary
`FFmpegRenderServiceIntegrationTests.RenderAsync_ManyClips_CrossesFilterScriptThreshold_RealFfmpegAcceptsFileForm`
(added in Phase 16) drives an actual ffmpeg encode through this branch.

The concat-demuxer strategies added post-Phase-16 (see above) use the same
bounded, always-cleaned-up discipline for their intermediates: one temp
directory `%TEMP%\SceneForge\render-concat\<guid>\` holds the pre-rendered
pieces (per distinct segment, or per bounded batch), any per-batch filter
scripts, and the demuxer list file, all deleted in a `finally` block on
every exit path. Per-batch filter graphs past the inline char threshold are
written *inside* that directory (not the shared `render-filters` one) so
the single cleanup covers them. The `DistinctDedup` pre-render volume is
bounded by the *distinct* clean-footage duration; the `Batched` one is the
output written in bounded chunks (decoded working set: one batch at a
time). Both are disk-space-checked up front via `IAdaptiveResourceGovernor`
(CLAUDE.md rule 7 — see docs/PHASE_16_REPORT.md's third post-acceptance fix
for the full rule-7 reasoning).

### Encoder selection is capability-tested, never GPU-name-inferred

`HardwareEncoderProbe.SelectEncoderAsync` tries `h264_nvenc`, then
`h264_qsv`, then `h264_amf`, then `libx264`, in that fixed order, by
actually launching ffmpeg against a short synthetic `lavfi` `color`
source with each `-c:v` candidate and checking the process exits cleanly —
never by reading a GPU vendor string or driver registry key. A launch
failure (`ProcessLaunchException`) or timeout is treated exactly like a
nonzero exit code: the candidate is skipped and the next one tried
(`HardwareEncoderProbeTests.SelectEncoderAsync_CandidateThrowsProcessLaunchException_TreatedAsFailureAndNextCandidateTried`).
If every candidate fails its smoke test, `SelectEncoderAsync` throws
`RenderExecutionException` naming every candidate tried, rather than
silently falling through to an encoder that was never actually proven to
work.

**Post-acceptance update (2026-08-28), see docs/PHASE_16_REPORT.md.** Two
gaps were found and fixed: (1) the smoke-test clip was `64x64` (below
NVENC's minimum dimensions — a working NVENC would false-negative) and
passed no rate-control args, so a candidate that rejects the *real*
preset/CRF settings was not caught; it is now a representative `320x240`
clip encoded with the exact `EncoderQualityDefaults` a render uses. (2)
`libx264` was the only software candidate, but SceneForge's vendored ffmpeg
is built `--disable-libx264`; `libopenh264` is now probed after it, and
`SelectSoftwareEncoderAsync` exposes the software-only result so
`RunWithFallbackAsync` no longer hardcodes `libx264`. The selected encoder
is written to `System.Diagnostics.Trace`.

### Hardware output is validated and falls back safely, at two separate layers

The phase brief's "hardware output must be validated and fall back safely"
is satisfied at two distinct points, deliberately not conflated into one:
(1) `HardwareEncoderProbe`'s smoke test proves an encoder can launch and
produce *some* output at all before it is ever selected: cheap, but not
proof a full-length, full-resolution render will succeed. (2)
`FFmpegRenderService.RunWithFallbackAsync` still retries the entire render,
once, with the probe-resolved software encoder (`libx264`, else
`libopenh264` — never hardcoded, see the post-acceptance update above) if
the selected hardware encoder's real render attempt exits nonzero — a real
full-length render can still fail for reasons a short smoke test cannot
catch (unsupported resolution/profile, driver contention, VRAM exhaustion
under load). If the encoder that failed was already a software one (the
fallback itself, or a caller-forced software selection), no retry is
attempted — a second identical attempt
against the same failure would never succeed — and `RenderExecutionException`
is thrown immediately
(`RenderAsync_SoftwareEncoderFails_ThrowsImmediatelyWithoutRetry` asserts
exactly one render attempt was made). `RenderResult.FellBackToSoftwareEncoder`
reports which of these two paths actually happened, never silently.

### Progress is ffmpeg's own machine-readable `-progress` stream, never scraped from banner/stats text

`-progress pipe:1 -nostats -loglevel error` puts ffmpeg into its documented
machine-readable mode: one `key=value` line per field, each block
terminated by a `progress=continue`/`progress=end` line.
`Internal.RenderProgressParser` is a small, stateful accumulator over this —
`frame`, `fps`, `out_time_us` (preferred, exact microseconds) or `out_time`
(string fallback), and `speed` (its trailing `x` stripped) — that emits one
`RenderProgress` per completed block and clears its accumulator on every
`progress` key regardless of value, so a later block never inherits a
stale field from an earlier one
(`RenderProgressParserTests.Accept_SecondBlock_DoesNotCarryStateFromFirstBlock`).
`FFmpegRenderService` computes `EstimatedTimeRemaining` itself, outside the
parser (which does not know the plan's total duration), as
`(PlannedVideoDuration - OutTime) / Speed` — left `null` whenever `Speed` is
not yet a positive number, since a divide-by-zero or negative ETA would be
a fabricated number, not a real estimate (CLAUDE.md rule 10).

### Post-render verification re-probes the file itself, never trusts ffmpeg's exit code alone

`Internal.RenderOutputVerifier.VerifyAsync` re-probes the *rendered file*
with `IFfprobeService` (a real, independent check — ffmpeg exiting 0 proves
the encoder finished, not that the container it wrote is well-formed) and
checks, itemized rather than as one opaque boolean: exactly one decodable
video stream present, exactly one audio stream present (never zero — the
source's own audio was structurally excluded — and never more than one),
duration within one frame of `RenderPlan.PlannedVideoDuration` at
`RenderOutputSpec.FrameRate` (the phase brief's literal "duration tolerance
of one frame," computed via the same `RationalFrameRate.FromFrameCount(1)`
Phase 8 already built), and that the first, middle, and last frame are each
independently decodable (`ffmpeg -ss <t> -i <file> -frames:v 1 -f null -`
exiting 0). `RenderVerificationResult.Failures` lists exactly which checks
failed in plain English; `FFmpegRenderService.RenderAsync` throws
`RenderVerificationException` carrying the full result when any check
fails, so a broken output file is always a loud, itemized failure, never a
`RenderResult` that merely looks successful (mirrors `TimelinePlan.FeasibilityWarning`'s
"quantified, never silent" convention from Phase 8, applied to a hard
failure rather than a soft one — see that phase's report for why the two
cases are treated differently).

### Output path safety reuses Phase 4's `Validation` module unchanged

`FFmpegRenderService.RenderAsync` calls `OutputDirectoryValidator.EnsureWritable`
on the output directory and `EnsureDoesNotOverwriteInput` against **both**
`RenderPlan.SourceFilePath` and `RenderPlan.Audio.FilePath` before spawning
anything — CLAUDE.md rules 11/12 (never overwrite a source input, always a
user-selected new path) apply to every input file a render touches, not
only the primary video source.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| `System.Progress<T>` used as an internal cross-layer relay | The first implementation bridged `ProcessRunner`'s per-line `OutputProgress` callback to `RenderProgressParser`/the caller's `IProgress<RenderProgress>` via `new Progress<ProcessOutputLine>(...)`. `FFmpegRenderServiceTests.RenderAsync_ReportsProgressParsedFromFfmpegStdout` — a fake process runner that calls `request.OutputProgress?.Report(...)` synchronously several times before returning an already-completed `Task` — failed with an empty collection even though the handler undeniably called `Report`. Root cause: `System.Progress<T>.Report` posts through the `ThreadPool` (or a captured `SynchronizationContext`) rather than invoking its callback inline, so there is no guarantee a queued callback has run by the time an awaited caller returns — harmless in the real pipeline (real ffmpeg I/O takes enough wall-clock time for the ThreadPool to catch up) but a real, undocumented source of lag/reordering between when ffmpeg actually emitted a progress line and when a caller's `IProgress<RenderProgress>` saw it, and a guaranteed failure against a synchronously-completing fake. Fixed by introducing `Internal.SynchronousProgress<T>`, a plain synchronous `IProgress<T>` adapter, as the internal relay, and adding `TestSupport.RecordingProgress<T>` (a synchronous test double, distinct from `System.Progress<T>`) so tests exercising a synchronous fake process runner get deterministic, immediate delivery instead of a timing-dependent one. Caught by a failing test, exactly the discipline CLAUDE.md rule 8 asks for — not caught by inspection beforehand. |
| `RenderAudioTrackSpec.TrimStart`/`TrimDuration` were unvalidated | Neither field clamps or validates its value at the type level (unlike, e.g., `TimelineDurationBounds`'s clamped init setters from Phase 8), so a caller passing a negative `TrimStart` or a zero/negative `TrimDuration` would silently reach `RenderFilterGraphBuilder` and produce a nonsensical `atrim` filter argument, discovered only via an opaque ffmpeg failure. Caught during a design self-review of `RenderPlanBuilder` (the natural place to validate a caller-supplied fact before any process spawns, consistent with the boundary-validation convention already used for segment-vs-source-duration above) rather than by a failing test. Fixed by adding two explicit checks in `RenderPlanBuilder.Build` (`Build_NegativeAudioTrimStart_Throws`, `Build_ZeroAudioTrimDuration_Throws`) before any test run reported it as a gap. |
| Newly authored files used LF line endings | Every new file in this phase was authored with `\n` line endings; `dotnet format SceneForge.sln --verify-no-changes` failed with `ENDOFLINE` errors across every one of them (the repository's `.editorconfig` pins `end_of_line = crlf`). Not a logic bug, but left here for the same reason Phase 8's `TheoryData` compile error is on record: it is exactly the class of issue CLAUDE.md rule 13's "run formatting before ending" step exists to catch, and it was caught by that step, not missed by it. Fixed by running `dotnet format SceneForge.sln` (apply mode) once, then re-verifying clean; `git status` after confirmed only the intentionally new files were touched, no incidental reformatting of any pre-existing file. |

All three were caught before this report was written — the first two by a
failing test and a deliberate boundary-validation review respectively, the
third by the same formatting gate CLAUDE.md rule 13 requires — never
assumed correct without running something that could have disproved it.

## Real-binary verification plan

Like every ffmpeg-touching component before it, this phase's real-process
behavior is verified by `[SkippableFact]` integration tests that spawn a
real `ffmpeg.exe`/`ffprobe.exe` and skip themselves (never fail) when
`tools/ffmpeg` is absent — this workstation has no real binaries installed,
so both new integration tests report `[SKIP]` in every run recorded below,
exactly as every other `*IntegrationTests` class in this project already
does until someone copies real binaries into place locally.
`FFmpegRenderServiceIntegrationTests` covers, against the existing
`Fixtures/Media/sample_video_audio.mp4`/`sample_audio_only.m4a` fixtures
(reading each fixture's own real probed duration first and deriving segment
lengths from `Math.Min` against both, so the test stays correct however
long either fixture actually is, rather than hard-coding an assumed
length):

- `RenderAsync_RealFfmpegAgainstRealFiles_ProducesVerifiedOutput` — builds a
  two-placement `RenderPlan` via `RenderPlanBuilder` against real probed
  `MediaInfo`, renders through the real `FFmpegRenderService` (real encoder
  capability probing included — whichever of NVENC/Quick Sync/AMF/libx264
  this machine actually supports), and asserts the output file exists,
  `RenderResult.Verification.IsValid` is true, and the output has exactly
  one audio stream.
- `RenderAsync_RealFfmpegAgainstRealFiles_NeverContainsSourceAudioStreamMetadata` —
  a single-placement render, re-probes the output directly and asserts
  exactly one audio stream whose codec is `"aac"` (the supplied track's
  codec, per `RenderAudioTrackSpec.Codec`'s default), never anything
  reflecting the source file's own audio stream.

Both are ready to run as-is the moment `ffmpeg.exe`/`ffprobe.exe` are placed
under `tests/SceneForge.Media.Tests/bin/<config>/net8.0/tools/ffmpeg/` —
no code change required, the same story Phases 4-8 already left for every
other real-binary integration test in this project.

## Test inventory (new this phase)

58 new tests, verified directly from `dotnet test` output in both Debug and
Release: the Phase 8 baseline was 416 total (408 passed + 8 skipped); this
phase's suite is 474 total (464 passed + 10 skipped — the 8 pre-existing
real-binary skips plus this phase's 2 new ones, all real-binary tests still
skipped on this workstation, none of which this phase's non-integration
tests depend on).

- **RenderPlanBuilderTests** (15) — null request, empty placements, no
  video stream in `SourceMediaInfo`, missing source file, missing audio
  file, undefined output frame rate, a segment exceeding the probed source
  duration (and, separately, one just inside the 500ms slack), negative
  audio trim start, zero audio trim duration, segments correctly reordered
  to `Position` order regardless of input order, source rotation degrees
  carried forward from `MediaInfo`, `PlannedVideoDuration` matching the sum
  of placement durations, the audio file path resolved to a full path, and
  a trimmed final placement's `IsTrimmed`/shortened duration preserved.
- **RenderFilterGraphBuilderTests** (6 methods, 9 cases via `[Theory]`) —
  one segment producing the expected `trim`/`atrim`/output-label fragments,
  the source audio stream never referenced, multiple segments concatenated
  in `Position` order, all three `AspectFitMode` values producing their
  distinct filter fragment, all four rotation-degree cases (0/90/180/270)
  producing the matching filter (or none, for 0), and every segment ending
  with the identical shared `fps`/`format`/`setsar` normalization.
- **RenderProgressParserTests** (6) — intermediate key lines return null
  until a block completes, a `progress=continue` line completes exactly one
  update with every field parsed correctly (including `out_time_us`-to-
  `TimeSpan` and `speed`'s trailing-`x` stripped), `progress=end` marks
  `IsFinished`, a second block never inherits a field left over from the
  first, a malformed line without `=` is ignored rather than throwing, and
  a missing `out_time_us`/`out_time` defaults to `TimeSpan.Zero` rather than
  throwing.
- **HardwareEncoderProbeTests** (6) — NVENC succeeding first is returned
  first (and is the only process spawned), NVENC failing falls through to
  Quick Sync, all three hardware candidates failing falls through to
  libx264, every candidate (including libx264) failing throws
  `RenderExecutionException`, a candidate throwing `ProcessLaunchException`
  is treated as a failure and the next candidate is still tried, and a
  pre-cancelled token throws `OperationCanceledException` before any
  candidate is attempted.
- **RenderOutputVerifierTests** (7) — a fully matching output passes every
  check, a duration beyond one frame's tolerance fails only that check (and
  one just inside tolerance passes it), zero audio streams and two audio
  streams both fail the audio-stream-count check, zero video streams fails
  the video-stream check, and a middle-frame decode failure is reported
  only against `MiddleFrameDecodable` (first/last remain independently
  true).
- **FFmpegRenderServiceTests** (11) — null plan and same-path-as-source
  output both throw before any process spawns, a hardware encoder
  succeeding on the first attempt returns a result with
  `FellBackToSoftwareEncoder = false` and exactly one render process
  spawned, a hardware encoder failing once falls back to libx264 and
  succeeds (two render attempts, `FellBackToSoftwareEncoder = true`), both
  hardware and the libx264 fallback failing throws
  `RenderExecutionException`, a software-encoder-only failure throws
  immediately with exactly one attempt (no pointless retry), a failing
  verification throws `RenderVerificationException` carrying the itemized
  result, ffmpeg stdout progress lines are parsed and forwarded to the
  caller's `IProgress<RenderProgress>`, a 400-segment plan is routed through
  `-filter_complex_script` (and the temp file is confirmed deleted
  afterward) while a small plan stays inline via `-filter_complex`, and the
  source audio stream (`"0:a"`) never appears in the full ffmpeg argument
  list.
- **FFmpegRenderServiceIntegrationTests** (2, `[SkippableFact]`, real
  ffmpeg/ffprobe) — see Real-binary verification plan above.

## Compliance notes against CLAUDE.md

- Rule 1-2 (native WPF, no Electron/web/cloud/telemetry): satisfied — pure
  local process orchestration over the existing `Processes`/`Tooling`
  abstractions, no UI, no network, no telemetry, no new package dependency.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp basis): this phase is built entirely
  on FFmpeg (filter graph, encode, `-progress`) and FFprobe (post-render
  verification) via the same `IProcessRunner`/`IFfmpegToolLocator`/`IFfprobeService`
  abstractions every prior media-touching phase already used; OpenCvSharp
  is not implicated (no new frame-level pixel processing is introduced —
  ffmpeg's own `scale`/`pad`/`crop`/`transpose` filters perform all
  geometric normalization).
- Rule 4 (clean architecture): `Rendering` depends on `Domain` (`MediaInfo`,
  `RationalFrameRate`), `Planning` (`TimelinePlan`/`TimelinePlacement`),
  `Processes`, `Tooling`, `Probing`, and `Validation` — never the reverse,
  and no UI concern anywhere in `SceneForge.Media`.
- Rule 5 (cancellation/cooperative shutdown): `RenderPlanBuilder.Build` is
  synchronous, pure, and needs no cancellation (same reasoning
  `TimelinePlanner.Plan` documented for itself). `FFmpegRenderService.RenderAsync`
  accepts and forwards a `CancellationToken` to every awaited call
  (`IFfmpegToolLocator.LocateAsync`, `IHardwareEncoderProbe.SelectEncoderAsync`,
  `IProcessRunner.RunAsync`, `RenderOutputVerifier.VerifyAsync`); actual
  process termination on cancellation is `ProcessRunner`'s existing,
  already-tested kill-the-whole-tree behavior (Phase 4), not re-implemented
  here.
- Rule 6-7 (bounded memory/concurrency, no full-video buffering): ffmpeg
  itself streams frames through the filter graph and encoder — this phase
  never reads a decoded frame into .NET memory at all, unlike `FrameSampler`
  (Phase 5), which by design does for analysis. The one piece of local
  state this phase writes to disk — the filter-complex script file — is
  capped at exactly one file per render attempt, always deleted in a
  `finally` block (see Design summary, "Bounded intermediate strategy").
  `BoundedTextCollector` (Phase 4) already bounds ffmpeg's captured
  stdout/stderr; this phase adds no new unbounded buffer.
- Rule 8 (test-first): every new component (`RenderPlanBuilder`,
  `RenderFilterGraphBuilder`, `RenderProgressParser`, `HardwareEncoderProbe`,
  `RenderOutputVerifier`, `FFmpegRenderService`) has dedicated tests
  requiring no real ffmpeg/ffprobe binary, run automatically on every
  `dotnet test`; the self-review section above documents one bug (the
  `System.Progress<T>` relay) caught directly by a failing test before this
  report was written, exactly the discipline this rule asks for.
- Rule 9 (benchmark with evidence): **not satisfied this phase**, following
  the same precedent Phases 6-8 documented for themselves — this is new
  functionality with no prior version to diff against, and CLAUDE.md rule 9
  is about optimizations. The per-encoder quality arguments
  (`EncoderQualityArguments`) are conservative, documented heuristic
  defaults, explicitly not tuned or benchmarked against any measured
  quality/size target — see Outstanding.
- Rule 10 (never claim absolute/opaque correctness): `RenderVerificationResult`
  reports every check it performed individually (`Failures` lists exactly
  which ones failed, never a bare pass/fail); duration correctness is
  always stated relative to an explicit, named tolerance
  (`DurationTolerance` = exactly one frame at the output frame rate, never
  a looser or unstated one); `VideoStreamInfo.RotationDegrees`'s own
  documented caveat from Phase 4 (ffprobe's reported convention, not an
  independently verified physical rotation) is inherited unchanged by the
  rotation-filter selection this phase adds on top of it, not silently
  upgraded to a stronger claim.
- Rule 11-12 (preserve user files, output to new path only): `RenderAsync`
  validates the output directory is writable and that the chosen output
  path does not collide with **either** input file (source video and
  supplied audio) before spawning any process — see Design summary,
  "Output path safety."
- Rule 13 (format/build/tests before ending): `dotnet format SceneForge.sln --verify-no-changes`
  clean (after the one round of `ENDOFLINE` fixes recorded in Self-review
  findings); Debug and Release both build with 0 warnings/errors across all
  eight projects; Debug and Release both pass all 474 tests (464 passed, 10
  skipped — the pre-existing real-binary skip set plus this phase's 2 new
  ones, all skipped identically in both configurations on this
  workstation).
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (no new architectural
  decision beyond what is already on file — `Media` depends only downward,
  no new package dependency, no new cross-cutting concern).
- Rule 15 (don't advance while criteria fail): this phase's own criteria —
  builds/tests clean in Debug and Release, formatting clean, source-audio
  removal and source-timestamp fidelity verified directly by tests (not
  merely asserted in prose), hardware-encoder fallback exercised on both
  the success and total-failure paths, `SceneForge.App` wiring and multi-
  source-file timelines explicitly out of scope — are met as of this
  report, with the encoder-quality-tuning gap and the App-wiring gap both
  named explicitly under Outstanding rather than hidden.

## Outstanding for later phases

- **No benchmark for this phase's own cost or encoder-quality tuning** (see
  Compliance notes, Rule 9) — a future phase should add
  `benchmarks/SceneForge.Benchmarks/Rendering/` (filter-graph construction
  cost as segment count grows, and — separately, requiring real hardware —
  measured output size/quality/speed per encoder at the current heuristic
  `EncoderQualityArguments` settings, as a baseline future tuning changes
  can be diffed against).
- **`EncoderQualityArguments`' per-encoder preset/CRF/quality values are
  untuned heuristic defaults**, not calibrated against any measured
  quality/size/speed target — a future pass should benchmark them (see
  above) and document the tradeoff explicitly, per CLAUDE.md rule 10's
  "never claim exactness" applied to encoder output quality specifically.
- **No real hardware encoder has actually been exercised** on this
  workstation (no NVENC/Quick Sync/AMF GPU available in this environment) —
  `HardwareEncoderProbeTests` and `FFmpegRenderServiceTests` verify the
  *selection and fallback logic* exhaustively against fakes, but the
  capability-testing smoke test's real behavior against a real GPU encoder,
  and a real hardware-encoder mid-render failure triggering the libx264
  fallback in practice, remain unverified until this code runs on hardware
  that actually has one. `FFmpegRenderServiceIntegrationTests` is ready to
  exercise whichever encoder a given machine's real ffmpeg build actually
  supports the moment real binaries are present (see Real-binary
  verification plan) — but on any given CI/dev machine that will most
  likely still resolve to libx264 unless that machine specifically has a
  supported GPU.
- **Multi-source-file timelines remain out of scope.** `RenderPlanRequest.SourceFilePath`/
  `SourceMediaInfo` are singular, matching `CleanClipExtractor`/`TimelinePlanner`'s
  existing single-source-file shape throughout this codebase (Phases 6-8) —
  a future phase combining footage from multiple source files into one
  timeline would need `TimelinePlacement` (or a wrapping type) to carry a
  per-placement source file reference, which does not exist today.
- **`SceneForge.App` wiring** (a UI surface to select an output path/audio
  track/output spec, run `FFmpegRenderService.RenderAsync` against a built
  `RenderPlan`, and show progress/ETA/decision trace end to end) remains
  untouched, as it has been since Phase 6.
- **Audio loudness/normalization is out of scope.** `RenderAudioTrackSpec`
  trims and reformats the supplied audio track's sample rate/channel count
  but never adjusts its loudness/level — a future phase wanting consistent
  perceived volume across different supplied audio tracks would need an
  explicit, documented normalization step (e.g. an `loudnorm` filter pass),
  not silently added to this one.
