# Phase 05 Report — Offline Frame-Sampling Pipeline

Date: 2026-08-22

## Scope

Build an offline frame-sampling pipeline in `SceneForge.Media`: decode a
source file through ffmpeg into a downscaled raw BGR24/Gray8 stream on
stdout (never thousands of image files, never the source buffered whole),
read one frame at a time into pooled buffers, attach the frame's *exact*
source timestamp, emit `FrameSample` objects through a bounded
`System.Threading.Channels` producer/consumer with backpressure,
cancellation, and progress, and provide Fast/Balanced/Accurate analysis
profiles. Explicitly **not** in scope: OpenCvSharp, any scene-detection
signal computation, or `SceneForge.App` wiring — those remain for later
phases per [docs/ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md) and
CLAUDE.md rule 15.

## Repository layout produced

```
src/SceneForge.Media/Sampling/
  AnalysisProfile.cs                 (Fast / Balanced / Accurate)
  FrameSamplePixelFormat.cs          (Bgr24 / Gray8 + ffmpeg name mapping)
  FrameSamplingOptions.cs            (clamped, documented, per-run config)
  FrameSamplingProfiles.cs           (the three profiles' documented defaults)
  FrameDimensions.cs                 (aspect-preserving scale-to-width calc)
  FrameSample.cs                     (IDisposable, ArrayPool-backed frame)
  FrameSamplingProgress.cs / FrameSamplingException.cs
  ShowInfoTimestampParser.cs         (parses ffmpeg's `showinfo` stderr log)
  RawFrameStreamReader.cs            (one fixed-size pooled frame at a time)
  IFrameSamplingProcess(Launcher).cs (internal seam over a running ffmpeg)
  FfmpegFrameSamplingProcessLauncher.cs (real, hardened process launch)
  IFrameSampler.cs / FrameSampler.cs (the public orchestrator)

tests/SceneForge.Media.Tests/Sampling/
  ShowInfoTimestampParserTests, RawFrameStreamReaderTests, FrameSampleTests,
  FrameDimensionsTests, FrameSamplingOptionsTests, FrameSamplingProfilesTests,
  FrameSamplerTests            (fully faked - no real ffmpeg)
  FrameSamplerMemoryTests      (synthetic, proves flat retained memory)
  FrameSamplerIntegrationTests (SkippableFact, real ffmpeg)

tests/SceneForge.Media.Tests/TestSupport/
  FakeFfprobeService, FakeFrameSamplingProcess(Launcher),
  SyntheticFrameSourceStream, SyntheticShowInfoTextReader

benchmarks/SceneForge.Benchmarks/Sampling/
  BenchmarkFrameSource / BenchmarkShowInfoReader / BenchmarkFrameSamplingProcess(Launcher)
  BenchmarkFfprobeService / BenchmarkFfmpegToolLocator
  FrameSamplingBenchmarks.cs
```

Also changed: `src/SceneForge.Media/AssemblyInfo.cs` (added
`InternalsVisibleTo("SceneForge.Benchmarks")`),
`benchmarks/SceneForge.Benchmarks/SceneForge.Benchmarks.csproj` (added a
`ProjectReference` to `SceneForge.Media`), and
`benchmarks/SceneForge.Benchmarks/Program.cs` (switched from running only
`ModuleInfoBenchmarks` to `BenchmarkSwitcher.FromAssembly(...)` so the new
`FrameSamplingBenchmarks` class is discoverable and filterable). No other
existing file changed; `SceneForge.App`/`SceneForge.Infrastructure` untouched.

## Design summary

### Why the process layer needed a new seam

The existing `IProcessRunner`/`ProcessRunner` (Phase 4) buffers stdout as
line-oriented **text** via `StreamReader.ReadLineAsync` — correct for
ffprobe's JSON, unusable for ffmpeg's **binary** rawvideo stream. Rather
than bend that abstraction, `Sampling/` adds its own hardened process seam:
`IFrameSamplingProcess`/`IFrameSamplingProcessLauncher`, implemented for
real by `FfmpegFrameSamplingProcessLauncher` (same hardening as
`ProcessRunner`: `UseShellExecute=false`, arguments only via
`ArgumentList`, entire-tree kill), but exposing stdout as a raw `Stream`
and stderr as a `TextReader`. Both interfaces are `internal`
(`InternalsVisibleTo` grants `SceneForge.Media.Tests` and, for
benchmarking, `SceneForge.Benchmarks`) — the public surface is just
`IFrameSampler`/`FrameSampler`/`FrameSample`/`FrameSamplingOptions`. This
seam is what makes the entire producer/consumer pipeline unit-testable
against synthetic in-memory streams (`SyntheticFrameSourceStream`,
`SyntheticShowInfoTextReader`) without spawning ffmpeg for every test.

### Exact source timestamps via `showinfo`, not index math

The ffmpeg filter graph is `fps=<N>,showinfo,scale=<W>:<H>`. The `fps`
filter selects (drops/duplicates) the nearest real source frame for each
output tick; `showinfo` logs that frame's genuine `pts_time` to stderr
*before* scaling touches it. `FrameSampler.PumpStderrTimestampsAsync` reads
those lines concurrently with the raw-frame loop reading stdout (both must
be drained concurrently or ffmpeg can deadlock on a full OS pipe buffer)
and correlates them one-for-one via a small internal
`Channel<TimeSpan>`. `ShowInfoTimestampParser` extracts `pts_time` with a
`GeneratedRegex`. The result: `FrameSample.Timestamp` is ffmpeg's own
reported timestamp for the frame that was actually selected, not a
synthetic `frameIndex / fps` estimate — confirmed against a real file
below.

### Pooling and bounded backpressure

`RawFrameStreamReader.TryReadFrameAsync` rents exactly one
`ArrayPool<byte>.Shared` buffer per frame and fills it from the process's
raw stdout stream; a short final read (mid-frame EOF) throws
`FrameSamplingException` rather than silently returning a truncated frame.
`FrameSample` owns that buffer and returns it to the pool in `Dispose()`
(idempotent via `Interlocked.Exchange`). `FrameSampler.SampleAsync` writes
each `FrameSample` into a `Channel<FrameSample>` bounded by
`FrameSamplingOptions.ChannelCapacity` (`FullMode.Wait`) — a slow consumer
therefore blocks the producer's `WriteAsync`, which blocks it from reading
the next frame off ffmpeg's stdout pipe, which (OS pipe buffers being
finite) eventually blocks ffmpeg itself. Real backpressure, not just an
in-process queue cap.

### Teardown correctness

`SampleAsync` is a `async IAsyncEnumerable<FrameSample>` iterator. Its
`finally` block runs whether the caller exhausts it, cancels the external
token, or simply stops enumerating early (`break`/`DisposeAsync` on the
enumerator) — C# guarantees this for `try`/`finally` around `yield return`.
On any exit it: cancels a linked `producerCts` (unblocking a producer
possibly parked in `channel.Writer.WriteAsync`), calls
`process.Kill()` (safe/idempotent if already exited), awaits the producer
task quietly, drains and `Dispose()`s any leftover buffered
`FrameSample`s so their pooled buffers are returned even if the consumer
never got to them, then disposes the process. `FrameSamplerTests` proves
both the external-cancellation and early-`break` paths actually kill and
dispose the process.

### Profiles

| Profile  | Analysis width | Sample rate | Expensive signals |
|----------|----------------|-------------|--------------------|
| Fast     | 320 px         | 2 fps       | off |
| Balanced | 384 px         | 4 fps       | off |
| Accurate | 480 px         | 8 fps       | on (flag only — no signal computed this phase) |

All values live in one place (`FrameSamplingProfiles`), are documented
there and on `FrameSamplingOptions`, and every field remains overridable —
`FrameSamplingOptions.ForProfile(profile) with { ... }`. `IncludeExpensiveSignals`
is carried through for a later analysis-stage phase to consume; this phase
does not compute any per-frame signal itself. `AnalysisWidthPixels` (16–4096),
`SampleFramesPerSecond` (0.1–60), and `ChannelCapacity` (1–64) are all
clamped in their `init` setters (same pattern as
`ProcessExecutionRequest.MaxCapturedBytesPerStream` from Phase 4), so a
misconfigured caller can never turn this into unbounded work.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| Compile correctness | `TextReader.ReadLineAsync(CancellationToken)` returns `ValueTask<string?>`, not `Task<string?>` — the first synthetic reader override didn't compile. | Fixed the override signature; `FrameSampler`'s `await stderr.ReadLineAsync(token)` call site needed no change (both awaitable). |
| Test correctness | `Assert.ThrowsAsync<OperationCanceledException>` requires an *exact* type match; a cancelled `ChannelReader.ReadAllAsync` throws `TaskCanceledException` (a subclass). | Switched to `Assert.ThrowsAnyAsync<OperationCanceledException>`, which is what the code actually needs to guarantee (any cancellation-shaped exception), not one specific subclass. |
| Deadlock risk | An earlier draft awaited the stderr-pump task directly after the frame loop ended, with no way to unstick it if trailing stderr output couldn't be written to a timestamp channel nobody was reading anymore. | Gave the stderr pump its own linked `CancellationTokenSource`, cancelled in a `finally` the instant the frame loop stops for any reason, so it can never block teardown. |
| Unbounded fan-out | Confirm a slow/early-stopping consumer can't leave the producer spinning or the pooled buffers un-returned. | `SampleAsync_SlowConsumer_ProducerIsBoundedByChannelCapacity` proves the producer is blocked (not "eventually catches up") when nobody's reading; `SampleAsync_ConsumerStopsEnumeratingEarly_StillKillsAndDisposesProcess` proves teardown runs on early `break`. |
| Silent fallback | `RawFrameStreamReader` could have silently returned a short/zero-padded final frame on unexpected EOF. | Throws `FrameSamplingException` naming exactly how many of the expected bytes were read, instead of fabricating a plausible-looking frame. |
| Fail-fast, no wasted process | A file with no video stream would otherwise still launch ffmpeg before failing. | `SampleAsync` throws `FrameSamplingException` right after probing, before ever calling the process launcher — asserted via a launcher that throws if invoked (`SampleAsync_MediaInfoHasNoVideoStream_ThrowsWithoutLaunchingFfmpeg`). |

## Real-binary verification

Same approach as Phase 4: this dev machine has ffmpeg 9.0.1
(`Gyan.FFmpeg.Shared`, via `winget`) available outside the repo. Its `bin/`
folder was temporarily copied into
`tests/SceneForge.Media.Tests/bin/Debug/net8.0/tools/ffmpeg/` (never
committed — `tools/` is gitignored) to run the `[SkippableFact]`
integration tests against the real binary and the existing Phase 4 fixture
(`sample_video_audio.mp4`: h264, 320×240, 25 fps, ~2 s), then deleted:

```
dotnet test tests/SceneForge.Media.Tests --filter "FullyQualifiedName~Integration"
Test Run Successful. Total tests: 6, Passed: 6 (3.3-4.4s)
```

The frame-sampling test specifically (5 fps, `AnalysisWidthPixels=64`)
logged:

```
Emitted 10 frames; timestamps: 0, 0.2, 0.4, 0.6, 0.8, 1, 1.2, 1.4, 1.6, 1.8
```

Ten frames at exactly 0.2 s spacing over the ~2 s clip, and — critically —
those are ffmpeg's own reported `pts_time` values recovered from a real
decode, not a computed `index / fps` sequence the code could have
produced even if the ffmpeg pipeline were wired wrong. This is the
strongest evidence available that the full real command line (`fps=5,
showinfo, scale=64:36` → `-pix_fmt bgr24 -f rawvideo pipe:1`) is correct
end-to-end. The invalid-file negative case
(`SampleAsync_RealFfmpegAgainstInvalidFile_ThrowsFfprobeExecutionException`)
also passed, confirming probing still fails before any ffmpeg decode
process is spawned.

### What I could not verify

Real device-recorded rotation/VFR content still isn't available (same gap
noted in Phase 4), but it's irrelevant to this phase — `FrameSampler` reads
`VideoStreamInfo.Width/Height` only, and rotation/VFR handling downstream
of sampling is a later phase's concern. `IncludeExpensiveSignals` is
carried as a flag only; no expensive per-frame signal exists yet to verify
against real content.

## Memory: flat regardless of simulated duration

`FrameSamplerMemoryTests.SampleAsync_RetainedMemoryStaysApproximatelyFlatAsSimulatedDurationIncreases`
runs the *real* `FrameSampler` pipeline (bounded channel, `ArrayPool`
pooling, everything except the actual OS process) against a synthetic
in-memory frame source sized to represent two very different "video
durations" — 2,000 and 20,000 frames at 384×216 Gray8 (~83 KB/frame,
`ChannelCapacity=4`) — disposing each `FrameSample` immediately after
"consuming" it, and measures `GC.GetTotalMemory(forceFullCollection: true)`
before and after each full run:

```
2,000 frames  -> retained delta   1,336,552 bytes
20,000 frames -> retained delta     396,024 bytes
Raw frame data that passed through the long run: 1,658,880,000 bytes
```

A 10x increase in simulated duration did **not** produce a 10x (or any)
increase in retained managed memory — if anything the second run measured
lower, within GC noise. Both deltas are under 1.3 MB, roughly 1,250x
smaller than the ~1.66 GB of raw frame bytes that actually flowed through
the pipeline for the long run. The test asserts both an absolute ceiling
(< 10 MB retained) and a flatness bound (< 5 MB difference between the two
runs) so it fails loudly if pooling regresses, not just if it disappears
entirely.

## Benchmark: throughput and allocations per profile

`FrameSamplingBenchmarks.SampleFrames` (BenchmarkDotNet, `MemoryDiagnoser`,
Release) runs the real pipeline against a synthetic 1920×1080 source,
300 frames, once per profile at that profile's own analysis
width/pixel-format (no real ffmpeg — this isolates the pipeline's own
overhead from ffmpeg's decode cost, which is not this phase's code):

```
| Method       | Profile  | Mean     | Error     | StdDev    | Gen0     | Allocated |
|------------- |--------- |---------:|----------:|----------:|---------:|----------:|
| SampleFrames | Fast     | 2.767 ms | 0.0549 ms | 0.0714 ms | 148.4375 | 301.12 KB |
| SampleFrames | Balanced | 3.679 ms | 0.0736 ms | 0.1600 ms | 148.4375 | 303.81 KB |
| SampleFrames | Accurate | 5.853 ms | 0.1161 ms | 0.2847 ms | 140.6250 | 307.12 KB |
```

Run on: AMD Ryzen 5 3500U, .NET 8.0.30, Windows 10 22H2, `--configuration
Release`. Two things stand out:

- **Allocations barely move with profile size** (301–307 KB for 300
  frames, ~1 KB/frame) even though the actual per-frame buffer size grows
  ~2.25x from Fast (320×180×3 ≈ 169 KB/frame) to Accurate (480×270×3 ≈ 380
  KB/frame) — confirming, from a different angle than the memory test
  above, that frame buffers are pooled and reused rather than allocated
  fresh per frame.
- **Mean time scales with pixel count** (2.77 ms → 5.85 ms), which is
  expected: more bytes touched per frame (fill/copy) as the analysis
  width grows; this is not a regression to chase, just the honest cost of
  a larger analysis frame, consistent with why Fast exists as a cheaper
  option.

There is no prior version of this code to compare against (this is new
functionality, not an optimization of existing code), so per CLAUDE.md
rule 9 this is recorded as the baseline measurement — the same handling
Phase 3's report used for its own benchmark-harness-only run.

## Commands executed and results

All commands run from `C:\Users\Bwp COmputers\Desktop\scene-forge` with the
pinned .NET 8.0.424 SDK.

### Format

```
dotnet format SceneForge.sln --verify-no-changes
```
First run reported `ENDOFLINE` errors across every newly authored file
(created with LF; repo requires CRLF) — the same situation Phase 3 and
Phase 4 hit. `dotnet format SceneForge.sln` fixed it in place; the
follow-up `--verify-no-changes` run produced no output.

### Build (Debug and Release)

```
dotnet build SceneForge.sln --configuration Debug
dotnet build SceneForge.sln --configuration Release
```
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```
for both configurations, across all seven projects (App, Core, Media,
Infrastructure, Core.Tests, Media.Tests, Benchmarks).

### Test (Debug and Release)

```
dotnet test SceneForge.sln --no-build --configuration Debug
dotnet test SceneForge.sln --no-build --configuration Release
```
```
Passed! - Failed: 0, Passed: 1,   Skipped: 0 - SceneForge.Core.Tests.dll  (net8.0)
Passed! - Failed: 0, Passed: 136, Skipped: 6 - SceneForge.Media.Tests.dll (net8.0)
```
identical in both configurations. The 6 skipped are the real-binary
`[SkippableFact]`s (4 from Phase 4, 2 new this phase) — expected, since
`tools/ffmpeg` is absent from a normal build output. With the temporary
real-ffmpeg copy in place (see "Real-binary verification" above), the same
filter set runs **142/142 passed, 0 skipped**.

### Benchmark

```
cd benchmarks/SceneForge.Benchmarks
dotnet run --configuration Release --no-build -- --filter "*FrameSamplingBenchmarks*"
```
Completed in ~2 minutes; results captured above. `ModuleInfoBenchmarks`
(Phase 3, unchanged) remains runnable via the same switcher.

## Test inventory (new this phase)

- **ShowInfoTimestampParserTests** — valid `pts_time` extraction (several
  real-shaped lines), lines without `pts_time` (empty, ffmpeg progress
  line, missing field), negative `pts_time` rejected.
- **RawFrameStreamReaderTests** — full frame read, clean EOF returns null,
  mid-frame truncation throws, sequential multi-frame reads, non-positive
  frame length throws.
- **FrameSampleTests** — `Dispose()` returns the buffer to its pool,
  double-`Dispose()` returns it only once, `Span` exposes only the valid
  byte range not the pool's over-sized buffer.
- **FrameDimensionsTests** — aspect ratio preserved, height forced even,
  byte length correct for both pixel formats, non-positive source
  dimensions throw.
- **FrameSamplingOptionsTests** — every clamp (width, fps, channel
  capacity) at both ends, defaults match the Balanced profile,
  `ForProfile` delegates correctly.
- **FrameSamplingProfilesTests** — exact Fast/Balanced/Accurate values,
  unknown profile throws.
- **FrameSamplerTests** (fully faked, no real ffmpeg) — happy path
  (sequential index, exact timestamps, correct dimensions), no-video-stream
  fails fast without launching ffmpeg, non-zero ffmpeg exit throws,
  frame-without-matching-timestamp throws, built ffmpeg arguments match
  options exactly, external cancellation kills the process, early
  `break` still kills/disposes the process, slow consumer proves real
  channel backpressure, progress is reported once per emitted frame with
  correct running count/timestamp.
- **FrameSamplerMemoryTests** — retained memory flat across a 10x
  simulated-duration increase (see above).
- **FrameSamplerIntegrationTests** — `[SkippableFact]`, real ffmpeg against
  the real fixture: correct frame count/dimensions/strictly-increasing
  timestamps, and the invalid-file negative case.

## Compliance notes against CLAUDE.md

- Rule 1–2 (native WPF, no Electron/web/cloud/telemetry): satisfied —
  `SceneForge.Media.Sampling` has no UI, network, or telemetry code.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): FFmpeg decode half satisfied this
  phase (ffprobe for dimensions, ffmpeg for the raw stream); OpenCvSharp
  remains for the scene-detection phase, explicitly out of scope here.
- Rule 4 (clean architecture): unchanged dependency graph (`Media → Core`
  only); `Sampling/` is pure processing-pipeline code, no UI concerns; the
  new internal process seam is analogous to (and consistent with) the
  Phase 4 `Processes/` seam, not a replacement of it.
- Rule 5 (cancellation/cooperative shutdown): `SampleAsync` honors the
  external token and unblocks on early enumerator disposal too (not just
  explicit cancellation), verified by killing the (fake) process in both
  cases in tests.
- Rule 6 (bounded memory/concurrency): `ChannelCapacity` (1–64, clamped),
  `AnalysisWidthPixels` (16–4096, clamped), and `SampleFramesPerSecond`
  (0.1–60, clamped) all bound the pipeline; the bounded-channel
  backpressure test proves the producer cannot race ahead of a stalled
  consumer.
- Rule 7 (no full-video buffering): ffmpeg decodes and streams; this code
  never holds more than `ChannelCapacity` frames plus the pool's retained
  buffers at once, proven flat via the memory test above regardless of
  video duration.
- Rule 8 (test-first): every behavior above has a corresponding test; the
  self-review fixes (deadlock-safe stderr teardown, fail-fast on no video
  stream, truncated-frame error instead of silent fallback) each have a
  regression test in the same change.
- Rule 9 (benchmark with evidence): recorded above as the baseline
  measurement for new functionality (no prior version exists to diff
  against), with real numbers (mean time, allocated bytes) per profile.
- Rule 10 (never claim absolute accuracy): not applicable — this phase
  performs no scene/transition detection; profile documentation describes
  sampling *rate/width* trade-offs only, never an accuracy guarantee.
- Rule 11–12 (preserve user files, output to new path only): this phase
  never writes to disk at all — `FrameSample`s are in-memory objects
  handed to the caller; the source file is opened by ffmpeg for reading
  only, matching the same guarantee `FfprobeService` already provides.
- Rule 13 (format/build/tests before ending): all run above, Debug and
  Release, plus the real-binary pass.
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (Decision 5 already
  anticipated bounded, cancellable, streaming processing).
- Rule 15 (don't advance while criteria fail): this phase's own criteria
  (solution builds/tests clean in Debug and Release, formatting clean,
  real-ffmpeg pipeline verified end-to-end, memory proven flat, benchmark
  recorded, no OpenCvSharp/scene-detection/App code introduced) are met as
  of this report.

## Outstanding for later phases

- OpenCvSharp integration and the actual scene/transition-detection
  algorithms that will consume `FrameSample` streams (including whatever
  `IncludeExpensiveSignals` ends up triggering — no signal is computed by
  this phase).
- `SceneForge.App` wiring (a UI surface to pick a profile, run sampling,
  show progress via `IProgress<FrameSamplingProgress>`, cancel).
- Bounding *concurrent* sampling runs across multiple files, once a
  batch-processing orchestration layer exists (this phase is
  single-file-at-a-time by design, same as Phase 4's probing).
- Real device-recorded rotation/VFR fixtures remain unavailable (Phase 4's
  gap); irrelevant to sampling itself but will matter once a later phase
  needs to reason about a rotated/VFR source's frame geometry beyond raw
  width/height.
