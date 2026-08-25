# Optimization Report — Low-Spec Pipeline Optimization (Phase 13)

This document records what was changed, why, and what was actually measured
for the low-spec optimization phase (branch `13-low-spec-optimization`).
Per CLAUDE.md rule 9, every number below is real, measured evidence from a
real run on real hardware — never an estimate. Per rule 10, this report is
explicit about what was **not** completed: a full before/after comparison
across all three `AnalysisProfile`s on a large synthetic source was cut
short by a deliberate decision to stop a long-running automated comparison
in favor of manual verification (see "What was not completed" below). That
gap is disclosed here, not hidden.

## Documented hardware

Same machine as `docs/BENCHMARK_REPORT.md`/`docs/ACCURACY_REPORT.md`:

| | |
|---|---|
| CPU | AMD Ryzen 5 3500U with Radeon Vega Mobile Gfx |
| Logical processors | 8 (4 cores) |
| RAM | 13.9 GB |
| OS | Microsoft Windows 10.0.19045 |
| .NET SDK | .NET 8.0.30 (pinned via `global.json` to 8.0.424) |
| Commit measured against | `0d8012bab6a33f7a55066272f170f40c9a4bf5ae` (pre-optimization baseline) |

## What changed

Six evidence-backed, value-preserving optimizations plus one new
cross-cutting service. Every item was chosen specifically because it does
**not** change any computed signal, detection, or score value — each is
purely a reduction in redundant process launches, redundant computation, or
redundant allocation — so accuracy should be unaffected by construction, not
just by hope (see "Accuracy reasoning per change" below).

1. **Redundant-probe elimination.** `ITransitionDetector.DetectAsync` and
   `ICleanClipExtractor.ExtractAsync` gained `MediaInfo`-accepting overloads
   (mirroring `IFrameSampler`'s existing dual-overload pattern).
   `AnalysisProgressViewModel` now passes its already-probed `MediaInfo`
   through to both stages instead of letting each stage re-probe via
   `ffprobe`. Cuts 2 of 3 `ffprobe` process launches per analysis run.
   Verified via `TransitionDetectorTests.DetectAsync_GivenMediaInfo_NeverProbesInternally`
   and the equivalent `CleanClipExtractorTests` test, both asserting
   `FakeFfprobeService.ProbeCallCount == 0`.

2. **Hardware encoder probe caching.** `HardwareEncoderProbe.SelectEncoderAsync`
   now caches the winning `VideoEncoderSelection` after the first successful
   probe (a plain `lock`-guarded field, not a `SemaphoreSlim`, to avoid a
   new `IDisposable` surface — see the class's own remarks for the
   documented, accepted tradeoff on the theoretical concurrent-first-call
   race). A failed probe is deliberately never cached. Cuts 1-4 ffmpeg
   smoke-test process launches on every render after the first, since
   `FFmpegRenderService` is a DI singleton and owns one `HardwareEncoderProbe`
   for the app's whole lifetime. Verified via
   `HardwareEncoderProbeTests.SelectEncoderAsync_CalledTwiceOnSameInstance_OnlyProbesOnce`
   and `..._FirstAttemptThrows_SecondAttemptRetriesRatherThanCachingFailure`.

3. **Duplicate Canny pass eliminated.** `AnalyzedFrame.Create` already ran
   Canny edge detection once for whole-frame `EdgeDensity`; `Extraction`'s
   `EdgeHistogramExtractor.Extract(Mat gray)` was recomputing Canny a
   second time on the same pixels for its 4x4 grid histogram.
   `AnalyzedFrame` now retains that edges Mat (`AnalyzedFrame.Edges`), and
   `EdgeHistogramExtractor.ExtractFromEdges(Mat edges)` reuses it directly.
   Verified bit-identical via
   `EdgeHistogramExtractorTests.ExtractFromEdges_GivenTheSameCannyOutputExtractWouldComputeInternally_ProducesIdenticalHistogram`.

4. **Classifier allocation cuts.** All 7 `ITransitionClassifier`s
   unconditionally allocated `new List<TransitionCandidate>()` on every
   `Classify()` call regardless of whether anything was found — the
   overwhelming majority of calls, since real transitions are rare relative
   to total frame pairs. Each now allocates lazily
   (`List<TransitionCandidate>? results = null; ... (results ??= []).Add(...); ... return results ?? [];`),
   and early-exit paths that were already guaranteed empty return `[]`
   directly. This is exactly the allocation pattern `docs/PHASE_06_REPORT.md`
   measured and flagged (10,000-22,000 Gen0-2 collections per 1,000 ops)
   as a future-phase target. Zero behavior change — the existing classifier
   test suite (30 tests across all 7 classifiers) passes unchanged, which is
   the regression guard; no new tests were needed since this is allocation-only.

5. **BGR scratch-Mat pooling.** `AnalyzedFrame.Create` allocated a fresh
   native `Mat` for its BGR working buffer on every single frame.
   `AnalyzedFrame` gained a `Create(FrameSample, Mat scratchBgr)` overload
   that writes into a caller-owned, caller-reused Mat instead; `SignalPipeline`
   and `ClipFrameMetricsPipeline` each own one such scratch Mat for their
   whole streamed run (disposed in their existing `finally` block). Safe
   because `Gray`/`HsvHistogram`/`Edges` remain independently-allocated
   destination Mats, never views over the scratch buffer. Verified via
   `AnalyzedFrameTests.Create_ScratchBgrMatReusedAcrossTwoDifferentFrames_EachResultStaysIndependentlyCorrect`.

6. **`IAdaptiveResourceGovernor`** (new, `SceneForge.Core.Resources` — placed
   in `Core`, not `Infrastructure`, specifically so `SceneForge.Media` can
   depend on it without a reverse reference from `Media` to `Infrastructure`,
   which already depends on `Media` and would create a cycle):
   - `MaxWorkers => Math.Max(1, Environment.ProcessorCount - 1)` — leaves
     one logical CPU free. Wired into `ThumbnailCacheService`'s
     `SemaphoreSlim` (previously a hardcoded `4`).
   - `EnsureSufficientDiskSpace(path, requiredBytes)` — throws
     `InsufficientDiskSpaceException` (derives from `IOException`, so every
     existing "recognized failure" catch site in `AnalysisProgressViewModel`/
     `RenderProgressViewModel` picks it up automatically) when free space is
     below the requested floor. Wired into `FFmpegRenderService.RenderAsync`
     (before starting a render, using a disclosed conservative byte-per-second
     estimate, not a size prediction) and `AutosaveService` (before every
     project-document write).
   - 8 new unit tests in `SceneForge.Core.Tests` covering the worker-count
     formula (including the 1-CPU edge case) and the disk-space guard's
     threshold behavior via an injectable `IDriveInfoProvider` seam.
   - **Bounded-queue audit** (documentation deliverable, not new code):
     `grep -rn "Channel\.Create\|SemaphoreSlim" src/` finds exactly 2
     `Channel.CreateBounded` calls (`FrameSampler`'s frame channel and
     stderr-timestamp channel, both bounded by caller-supplied capacity) and
     1 `SemaphoreSlim` (`ThumbnailCacheService`, now `governor.MaxWorkers`-sized
     instead of a literal `4`). No `Channel.CreateUnbounded`,
     `ConcurrentQueue`, or `BlockingCollection` anywhere in `src/`. Every
     concurrency primitive in the codebase remains explicitly bounded after
     this phase's changes.

### Explicitly deferred (measured concern, not attempted)

- **Detect/Extract dual-decode merge.** `TransitionDetector.DetectAsync` and
  `CleanClipExtractor.ExtractAsync` each independently probe and fully
  decode the source file — two full ffmpeg decode passes per analysis run,
  confirmed via code inspection this phase. This is very likely the single
  largest remaining throughput lever, but merging them requires a shared,
  streamed per-frame descriptor cache spanning two pipelines with different
  disposal/streaming guarantees — real architectural risk. **User-confirmed
  decision this phase: do not attempt it; document it as a flagged future
  opportunity**, the same pattern `docs/PHASE_06_REPORT.md` used for the
  classifier-rescan GC issue (which this phase then did pick up as item 4
  above).
- **Farneback optical-flow tuning.** `docs/PHASE_06_REPORT.md` measured
  optical flow (`GlobalMotionEstimateExtractor`) as the dominant CPU cost in
  the whole signal-extraction pipeline. Its parameters were not tuned this
  phase: doing so would change `GlobalMotion` values and risk further
  degrading Zoom/Swipe classifier accuracy, which `docs/ACCURACY_REPORT.md`
  already documents as a weak spot (0% recall on the fixture matrix).

## Accuracy reasoning per change

None of the six implemented changes alter any computed signal, score, or
detection value:

- Changes 1, 2, and 6 touch only process-launch counts, caching of an
  encoder *selection* (not a pixel value), and resource-governance plumbing
  — none are in the signal/detection computation path at all.
- Change 3 is proven bit-identical by test (same Canny output, reused vs.
  recomputed).
- Change 4 is allocation-only; the classification *logic* (which indices
  qualify, which candidates are built) is untouched, and the full existing
  classifier test suite passes unchanged.
- Change 5 reuses a native buffer whose contents are fully overwritten
  (`Marshal.Copy`) before every use, with all retained outputs
  independently allocated — proven non-corrupting by test.

This is a testable prediction, not yet an empirically re-confirmed one this
session (see "What was not completed" below) — it should be verified by a
fresh `accuracy ... evaluate --profile <X>` run before/after on the final
diff.

## Verification actually performed, both trees

Every command below was run twice: once against the pre-optimization
commit (`0d8012b...`, working tree unmodified) and once against the
optimized working tree.

```
dotnet build SceneForge.sln --configuration Release
dotnet format SceneForge.sln --verify-no-changes
dotnet test SceneForge.sln --no-build --configuration Release
```

**Pre-optimization tree:** build succeeded (0 warnings, 0 errors); 624/625
tests passed — the one failure,
`FFmpegRenderServiceIntegrationTests.RenderAsync_RealFfmpegAgainstRealFiles_ProducesVerifiedOutput`,
is a real-ffmpeg duration-tolerance check (`0.72s` actual vs. `0.6667s`
expected, ±0.04s tolerance).

**Post-optimization tree:** build succeeded (0 warnings, 0 errors);
`dotnet format --verify-no-changes` clean; 634/635 tests passed —
**the exact same single failure**, same test, same numbers
(`0.72s` vs `0.6667s` expected, ±0.04s tolerance), confirming it is
pre-existing and unrelated to any change in this phase, not a regression
introduced by it. (Total test count differs, 624 vs. 634, only because this
phase added 10 new tests net of the ones already accounted for above.)

**Disclosed as a known, unrelated gap**, consistent with how
`docs/PHASE_12_REPORT.md` treats similar real-ffmpeg-only findings: this
looks like ffmpeg-version-specific encode timing drift (the render
completing ~0.053s longer than planned, just outside a 0.04s tolerance
band) rather than anything touched by this phase's changes — none of the
six optimizations above modify `FFmpegRenderService`'s render-duration
logic, `RenderFilterGraphBuilder`, or `RenderOutputVerifier`'s tolerance
calculation. Not investigated or fixed this phase per explicit scope
decision; left for a future phase to root-cause.

## What was measured (pre-optimization baseline only)

### Accuracy — 32-fixture matrix, all three profiles

Reproduced with `dotnet run --project accuracy/SceneForge.Accuracy --
evaluate --profile <Fast|Balanced|Accurate> --report <path>`. Only
`Accurate` has a long-standing committed baseline
(`accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json`,
documented in `docs/ACCURACY_REPORT.md`); `Fast`/`Balanced` numbers below
are captured here for the first time as this phase's own reference point.

| Profile | TP | FN | FP | Recall | Precision | F1 | Boundary err (ms) | FP/min | Throughput (src-s/wall-s) | Wall clock | Peak managed mem | Peak working set |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Fast | 9 | 15 | 17 | 38% | 35% | 36% | 222.2 | 10.62 | 3.80 | 25.23 s | 5.5 MB | 62.1 MB |
| Balanced | 10 | 14 | 17 | 42% | 37% | 39% | 76.0 | 10.62 | 2.86 | 33.58 s | 6.4 MB | 63.6 MB |
| Accurate | 11 | 13 | 17 | 46% | 39% | 42% | 148.6 | 10.62 | 1.40 | 68.50 s | 9.5 MB | 70.9 MB |

The `Accurate` row matches the already-committed baseline exactly (as
expected — the working tree was unmodified for this capture).

### BenchmarkDotNet microbenchmarks — Detection + Sampling, all three profiles

`TransitionDetectionBenchmarks.AnalyzeAndClassify` (real `SignalPipeline` +
all 7 classifiers, synthetic in-memory 200-frame sequence, `MemoryDiagnoser`,
Release):

| Method | AnalysisProfile | Mean | Error | StdDev | Gen0 | Gen1 | Gen2 | Allocated |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| AnalyzeAndClassify | Fast | 4.619 s | 0.0904 s | 0.1296 s | 10000.0000 | 10000.0000 | 10000.0000 | 34.64 MB |
| AnalyzeAndClassify | Balanced | 6.373 s | 0.1238 s | 0.2795 s | 15000.0000 | 15000.0000 | 15000.0000 | 49.13 MB |
| AnalyzeAndClassify | Accurate | 10.103 s | 0.0664 s | 0.0518 s | 22000.0000 | 22000.0000 | 22000.0000 | 75.83 MB |

`FrameSamplingBenchmarks.SampleFrames` (synthetic 1920x1080 source, 300
frames, no real ffmpeg):

| Method | Profile | Mean | Error | StdDev | Gen0 | Allocated |
|---|---|---:|---:|---:|---:|---:|
| SampleFrames | Fast | 2.385 ms | 0.0256 ms | 0.0227 ms | 148.4375 | 301.18 KB |
| SampleFrames | Balanced | 2.990 ms | 0.0564 ms | 0.0627 ms | 148.4375 | 304.04 KB |
| SampleFrames | Accurate | 4.532 ms | 0.0627 ms | 0.0556 ms | 148.4375 | 307.26 KB |

Allocated-byte figures match `docs/PHASE_05_REPORT.md`/`docs/PHASE_06_REPORT.md`'s
own numbers exactly (deterministic given identical code and synthetic
input); wall-clock figures differ slightly from those older reports simply
from normal machine-load variance run to run, not from any code change,
since this capture is against the unmodified pre-optimization commit.

### Full end-to-end pipeline profiling — Fast profile only

A new `profile-pipeline` command was added to `accuracy/SceneForge.Accuracy`
specifically for this phase (`accuracy/SceneForge.Accuracy/Profiling/`,
`Cli/ProfilePipelineCommand.cs`): it builds/reuses one cached, disclosed-
synthetic 1920x1080 source (`SyntheticProfilingSourceBuilder` — alternating
static/panning-motion segments joined by hard cuts, a fade-to-black, a
fade-from-black, and a dissolve, encoded via ffmpeg's own `lavfi` sources
and concatenated losslessly; **1497.0 s (~24.95 min) actual duration**, not
exactly 30 minutes — the segment durations chosen (4x300s + a 300s dissolve
pair) sum to 1497s by construction), then runs the real
Detect → `SceneRangeCalculator` → Extract → `TimelinePlanner` → Render
chain against it, capturing per-stage wall clock, throughput, this
process's own CPU time, peak managed memory/working set, and free-disk-space
deltas. Reproduce with:

```
dotnet run --project accuracy/SceneForge.Accuracy -- profile-pipeline --profile All --report <path>
```

Only the `Fast` profile run completed before this comparison was
deliberately stopped (see "What was not completed" below):

| Metric | Value |
|---|---:|
| Input duration | 1497.0 s |
| Detect stage | 121.41 s (181 transitions found) |
| Extract stage | 53.82 s (392 candidates scored: 87 accepted, 305 rejected) |
| Render stage | 71.18 s (1 render, encoder libx264, no hardware fallback, output verified valid) |
| Total wall clock | 246.42 s |
| Throughput | 6.07 source-seconds analyzed per wall-clock second |
| Process CPU time | 199.52 s (this process only — see `PipelineProfileReport`'s own remarks: excludes the separate ffmpeg child processes' own CPU usage, which is still fully reflected in wall-clock time) |
| Peak managed memory | 7.3 MB |
| Peak working set | 61.7 MB |
| Free disk before/after | 26,041 MB / 25,965 MB |

A real, notable finding surfaced while building this harness: the first
two attempts to generate the synthetic source failed with a generic
"Cancelled." message (`accuracy/SceneForge.Accuracy`'s `ExitCodeMapper`
deliberately treats `ProcessTimeoutException` identically to a real Ctrl+C,
per an already-reviewed Phase 12 decision — see
`ExitCodeMapperTests.Map_ProcessTimeoutStyleCancellation_AlsoTreatedAsCancelled`'s
own remarks). Direct, isolated measurement traced this to the initial
source design using ffmpeg's `mandelbrot` lavfi filter for the "motion"
segment, which measured at **~0.39x realtime** at 1920x1080 (51.5s of
wall-clock to encode a 20s sample) — enough to exceed the harness's
5-minute per-segment timeout on the full 300s segment. Fixed by replacing
`mandelbrot` with a cheap panning-crop over a larger static canvas (same
motion-exercising effect for the optical-flow signal, measured at ~3.9x
realtime) and padding the per-segment timeout to 15 minutes as a safety
margin; see `SyntheticProfilingSourceBuilder`'s own remarks for the full
account.

## What was not completed

- **`Balanced` and `Accurate` full-pipeline profiling** (the `profile-pipeline`
  command above) were not run — the automated before/after comparison was
  stopped part-way through the `Balanced` profile run at the user's explicit
  direction, in favor of manual end-to-end testing through the actual app
  UI instead.
- **No "after" (post-optimization) measurements were captured for any of
  the three evidence categories above** (accuracy evaluate, BenchmarkDotNet,
  full-pipeline profiling). Only the build/format/test verification above
  was re-run on the optimized tree. The "Accuracy reasoning per change"
  section above is a well-supported but empirically unconfirmed prediction
  for this specific diff, not a measured result.
- Per CLAUDE.md rule 9 ("benchmark every optimization before and after"),
  this is a real, disclosed shortfall for this phase, not a claim of
  completeness. **Recommended next step for whoever picks this up:** re-run
  `accuracy ... evaluate --profile <X>` (fast, ~25-70s per profile — safe to
  run interactively) for all three profiles against the optimized tree and
  diff against the `Fast`/`Balanced`/`Accurate` tables above; re-run the
  BenchmarkDotNet suite (~17 minutes, safe as a single foreground or
  background run) and diff against the tables above; and, time permitting,
  resume `profile-pipeline --profile Balanced` (source is already cached at
  the default `%TEMP%\sceneforge-profiling\profiling-source.mp4` location,
  so this does not require regenerating it) followed by `--profile Accurate`.

## Commands reference

```
# Build/format/test (run against both trees for this report)
dotnet build SceneForge.sln --configuration Release
dotnet format SceneForge.sln --verify-no-changes
dotnet test SceneForge.sln --no-build --configuration Release

# Accuracy, per profile
dotnet run --project accuracy/SceneForge.Accuracy -- evaluate --profile Fast --report evaluate-fast.json
dotnet run --project accuracy/SceneForge.Accuracy -- evaluate --profile Balanced --report evaluate-balanced.json
dotnet run --project accuracy/SceneForge.Accuracy -- evaluate --profile Accurate --report evaluate-accurate.json

# Microbenchmarks
dotnet run --project benchmarks/SceneForge.Benchmarks --configuration Release -- --filter "*TransitionDetectionBenchmarks*" "*FrameSamplingBenchmarks*"

# Full end-to-end pipeline profiling (builds/reuses the cached synthetic source)
dotnet run --project accuracy/SceneForge.Accuracy -- profile-pipeline --profile All --report profile-pipeline.json
```

All `accuracy`/real-ffmpeg-gated commands require `ffmpeg.exe`/`ffprobe.exe`
staged under `accuracy/SceneForge.Accuracy/bin/<Debug|Release>/net8.0/tools/ffmpeg/`
(and, for the real-binary integration tests, the equivalent path under
`tests/SceneForge.Media.Tests/bin/<Debug|Release>/net8.0/tools/ffmpeg/`) —
never committed, per `tests/fixtures/README.md`'s existing convention.

## Limitations

- Single machine, single set of runs — no repeated-run variance was
  measured for any number in this report, same caveat
  `docs/BENCHMARK_REPORT.md` already carries.
- The synthetic profiling source is disclosed-synthetic ffmpeg-generated
  content (per `SyntheticProfilingSourceBuilder`'s own remarks), not real
  footage, and carries no accuracy ground truth — it measures resource
  cost only. Accuracy claims in this report rest entirely on the existing
  32-fixture matrix, which remains small-sample and synthetic itself (see
  `docs/ACCURACY_REPORT.md`'s own limitations).
- Per CLAUDE.md rule 10: nothing in this report should be read as a claim
  of complete or absolute optimization coverage, or of proven zero
  accuracy impact for this specific diff — see "What was not completed"
  above.
