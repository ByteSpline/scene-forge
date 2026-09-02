# CPU Saturation Audit

## Trigger

A real hang was reported: Task Manager showed SceneForge at **91.6% CPU**
(system-wide CPU at 100%) while using only **165 MB RAM**. Low memory ruled
out a buffering/leak problem (CLAUDE.md rule 7's bounded-streaming design was
working as intended); the machine becoming unresponsive pointed at CPU
scheduling, not memory pressure. This directly violated the existing Phase 13
intent to "leave one logical CPU available."

## Investigation

`IAdaptiveResourceGovernor.MaxWorkers` (`src/SceneForge.Core/Resources/AdaptiveResourceGovernor.cs`)
computed `Math.Max(1, Environment.ProcessorCount - 1)` correctly, and it *was*
wired into every place C# code fans work out across concurrent tasks
(`ThumbnailCacheService`'s `SemaphoreSlim`). No `Parallel.ForEach` or
unbounded `Task.WhenAll` fan-out existed anywhere in `src/`.

The gap was elsewhere: **`MaxWorkers` was never passed to ffmpeg's own
`-threads` argument, anywhere.** A grep across every ffmpeg argument builder
in the codebase (`FFmpegRenderService`, `RenderDurationCorrector`,
`HardwareEncoderProbe`, `FrameSampler`, `ThumbnailCacheService`) found zero
occurrences of `-threads`. ffmpeg defaults `-threads` to `0` ("auto"), which
means **every single ffmpeg process decides for itself to use every logical
CPU on the machine**, completely independent of how many *other* processes
the app is or isn't running concurrently. A governor that only bounds
process/task *count* cannot bound a process that is itself internally
multi-threaded.

The same gap existed on the OpenCV side: Phase 6 transition detection
(`SignalPipeline` → `AnalyzedFrame`, `GlobalMotionEstimateExtractor`'s
Farneback optical flow, `Canny`, histogram math) calls OpenCvSharp's `Cv2.*`
functions directly. OpenCV's `parallel_for_` backend also defaults to using
every logical CPU (`cv::getNumThreads()` defaults to hardware concurrency)
unless `Cv2.SetNumThreads` is called. Nothing in the codebase ever called it.

So the actual bypass was: **ffmpeg's own codec/filter threading and OpenCV's
own internal thread pool, not C# task fan-out.**

### Real measurement (before)

Reproduced with the real pipeline — `accuracy/SceneForge.Accuracy`'s
`profile-pipeline` command, which runs the actual
`FrameSampler → TransitionDetector → CleanClipExtractor → FFmpegRenderService`
chain (the same classes the WPF app uses) against a real ~25-minute
1920×1080 synthetic source, through the real vendored ffmpeg binary — while
sampling system-wide and per-process CPU every 5 seconds via
`Get-Counter`/`Get-Process` on this 8-logical-processor machine:

| | Before |
|---|---|
| Peak system-wide CPU | **100%** |
| Peak CPU attributable to SceneForge (dotnet host + ffmpeg.exe, summed) | **85.3%** (~6.8 of 8 cores) |
| Average CPU attributable to SceneForge over the run | 42.8% |

Multiple consecutive 5-second samples during the render stage sat at
96-100% system CPU. This matches the reported hang.

## Fix

Product requirement (tightened from "leave one core free"): **SceneForge
must never use more than ~35% of total system CPU, at any pipeline stage, on
any machine.** This is now enforced as follows.

### 1. `AdaptiveResourceGovernor.MaxWorkers` — hard percentage cap

```csharp
internal const double CpuBudgetFraction = 0.35;
public int MaxWorkers => Math.Max(1, (int)Math.Floor(_processorCount * CpuBudgetFraction));
```

Flooring (never rounding) guarantees `MaxWorkers / processorCount <= 0.35`
for every machine with 3+ logical CPUs — proved by a new test,
`MaxWorkers_NeverExceedsThirtyFivePercentCpuBudget`, a `[Theory]` across 11
core counts (3 to 128). On 1-2 logical CPUs the pre-existing "never below 1"
floor still applies (documented, unavoidable exception — a machine cannot do
less than one unit of work). On this 8-core machine: `floor(8 × 0.35) = 2`
workers (25%).

### 2. Every ffmpeg invocation now passes `-threads`

| Call site | Threads passed | Why |
|---|---|---|
| `FFmpegRenderService` (single-pass, Stage A batch/dedup encode, Stage B concat, duration-correction remux) | `MaxWorkers` (full budget) | Nothing else CPU-heavy runs concurrently with render |
| `HardwareEncoderProbe` (capability smoke tests) | `MaxWorkers` | Brief, sequential, same reasoning |
| `FrameSampler` (Phase 5/6 decode) | fixed `1` | See split below |
| `ThumbnailCacheService` (single-frame extraction) | fixed `1` | Already concurrency-capped at `MaxWorkers` *processes* by its `SemaphoreSlim` — `MaxWorkers` processes × 1 thread = `MaxWorkers` total, not `MaxWorkers²` |

### 3. The concurrent decode+analyze split (`FrameSampler`)

`FrameSampler` streams decoded frames through a bounded channel to a
consumer (`TransitionDetector`'s `SignalPipeline`,
`CleanClipExtractor`'s `ClipFrameMetricsPipeline`) **while ffmpeg is still
decoding further frames** — producer and consumer run concurrently by
design (that's the whole point of the streaming architecture). Handing the
*full* budget to both ffmpeg's decode and OpenCV's analysis independently
would let their combined usage run up to 2x over the cap.

`FrameSampler` now owns both halves of that split, since it's the one
composition point both `TransitionDetector` and `CleanClipExtractor` depend
on:

```csharp
var ffmpegThreads = Math.Min(FfmpegDecodeThreadShare, resourceGovernor.MaxWorkers); // fixed 1
var openCvThreads = Math.Max(1, resourceGovernor.MaxWorkers - ffmpegThreads);
Cv2.SetNumThreads(openCvThreads); // process-wide, applies to every downstream Cv2.* call
```

ffmpeg's decode share is fixed and small because it always targets an
already-downscaled analysis-resolution stream (`FrameDimensions.ForTargetWidth`)
— the expensive part of Phase 6 is the per-frame OpenCV math (Farneback
optical flow, Canny, histograms), which gets the rest of the budget.

`Cv2.SetNumThreads` is a process-wide setting; calling it from
`FrameSampler`'s constructor means every host (the WPF app via DI, the
accuracy CLI, benchmarks, tests) gets it for free without needing to
remember to configure OpenCV separately.

### Real measurement (after)

Same real pipeline, same real ~25-minute synthetic source, same real ffmpeg
binary, same 8-core machine, sampled the same way — this run completed the
full Detect → Extract → Render pipeline (389 s wall clock, 181 detections,
118/289 clips accepted, render verified valid):

| | Before | After |
|---|---|---|
| Peak CPU attributable to SceneForge | **85.3%** | **33.6%** |
| Average CPU attributable to SceneForge | 42.8% | 20.2% |

33.6% is under the 35% hard cap in every one of the 63 samples taken across
the full pipeline. (System-wide CPU still touched 100% a few times in the
"after" run — this is a shared dev machine also running the IDE, this
Claude Code session, and background dotnet processes; at the exact sample
where system CPU hit 100%, SceneForge's own contribution — ffmpeg + the
dotnet host — was only ~18%, confirming the spike was other machine
activity, not SceneForge.)

## Trade-off

This is a real, accepted slowdown: capping ffmpeg/OpenCV to ~2 of 8 cores
instead of letting them use 7-8 makes encode/decode/analysis
proportionally slower. The product requirement explicitly accepts this in
exchange for guaranteed system responsiveness — do not widen the cap for
performance reasons without a corresponding product decision.

## Testing

- `AdaptiveResourceGovernorTests`: exact-value tests for 1, 2, and 8 logical
  CPUs, plus the `[Theory]` CPU-budget invariant across 11 core counts.
- `FrameSamplerTests.SampleAsync_BuildsExpectedFfmpegArguments`: asserts
  `-threads 1` is present on the decode invocation.
- `FFmpegRenderServiceTests`, `HardwareEncoderProbeTests`,
  `RenderDurationCorrectorTests`: existing argument-shape assertions
  continue to pass unchanged (index-based, not exact-list-equality) with
  `-threads` now present.
- Full suite: 748 tests passed (0 failed) across all six test projects,
  including 18 tests that spawn the real ffmpeg binary end-to-end and
  confirm it accepts the new `-threads` arguments without error.
- `dotnet format --verify-no-changes`: clean.
- Real pipeline run (see above): confirms the measured effect, not just
  unit-level argument presence.

## Files changed

- `src/SceneForge.Core/Resources/AdaptiveResourceGovernor.cs`,
  `IAdaptiveResourceGovernor.cs` — new formula and contract.
- `src/SceneForge.Media/Sampling/FrameSampler.cs` — governor injection,
  `-threads`, `Cv2.SetNumThreads` split.
- `src/SceneForge.Media/Rendering/FFmpegRenderService.cs`,
  `HardwareEncoderProbe.cs`, `Internal/RenderDurationCorrector.cs` —
  `-threads` on every ffmpeg invocation.
- `src/SceneForge.App/Services/ThumbnailCacheService.cs` — fixed
  `-threads 1` per concurrent process.
- Corresponding test/benchmark/accuracy-tool call sites updated for the new
  constructor parameters.
