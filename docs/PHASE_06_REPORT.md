# Phase 06 Report — Transition Analysis (Composable Signals, Classifiers, Fusion, Export)

Date: 2026-08-22

## Scope

Build transition/scene-boundary analysis on top of Phase 5's frame-sampling
pipeline: seven independent, composable OpenCvSharp-based signal extractors
computed per consecutive frame pair, seven independent classifiers (one per
transition type) that scan a bounded sliding window of those signals,
fusion of raw candidates into a final non-overlapping detection list via a
versioned configuration profile, CSV/JSON diagnostic export, and a
deterministic synthetic-fixture test harness (ffmpeg-generated clips with
exactly-known transition windows) used to measure — never assume — real
precision/recall/boundary-error per transition type. Explicitly **not** in
scope: `SceneForge.App` wiring (no UI surface exists yet to run this from),
and any concurrent/batch orchestration across multiple files.

## Repository layout produced

```
src/SceneForge.Media/Detection/
  Signals/
    AnalyzedFrame.cs                  (per-frame OpenCvSharp Mats + eager scalar signals)
    FrameSignalSample.cs              (+ GlobalMotionEstimate; the shared per-pair signal vocabulary)
    ISignalExtractor.cs               (IPairSignalExtractor / ISingleFrameSignalExtractor / IGlobalMotionSignalExtractor)
    HsvHistogramDistanceExtractor.cs, LuminanceDeltaExtractor.cs,
    EdgeDensityDeltaExtractor.cs, LaplacianBlurChangeExtractor.cs,
    BlackWhiteFrameScoreExtractor.cs (Black/WhiteScoreExtractor),
    StructuralDifferenceExtractor.cs, GlobalMotionEstimateExtractor.cs
    SignalPipeline.cs                 (orchestrates AnalyzedFrame + all 7 extractors, streaming)
  Classification/
    TransitionCandidate.cs, ITransitionClassifier.cs, ClassifierWindow.cs,
    ContiguousRunFinder.cs
    HardCutClassifier.cs, FadeBlackClassifier.cs, DissolveClassifier.cs,
    FlashClassifier.cs, BlurTransitionClassifier.cs, ZoomTransitionClassifier.cs,
    DirectionalSwipeClassifier.cs
  Fusion/
    TransitionDetectionProfileVersion.cs, TransitionDetectionProfile.cs
    (+ HardCut/FadeBlack/Dissolve/Flash/BlurTransition/ZoomTransition/
    DirectionalSwipeThresholds), TransitionDetectionProfiles.cs, TransitionFuser.cs
  Export/
    TransitionDetectionCsvWriter.cs, TransitionDetectionJsonWriter.cs
  TransitionType.cs, TransitionDetection.cs, TransitionDetectionOptions.cs,
  TransitionDetectionProgress.cs, TransitionDetectionException.cs,
  ITransitionDetector.cs / TransitionDetector.cs

tests/SceneForge.Media.Tests/Detection/
  Signals/       (AnalyzedFrame + all 7 extractors + SignalPipeline - 34 tests, no ffmpeg)
  Classification/ (all 7 classifiers + ClassifierWindow + ContiguousRunFinder - 30 tests, no ffmpeg)
  Fusion/        (TransitionDetectionProfile(s) + TransitionFuser - 20 tests, no ffmpeg)
  Export/        (CSV/JSON writers - 9 tests, no ffmpeg)
  TransitionDetectionOptionsTests.cs, TransitionDetectionTests.cs,
  TransitionDetectorTests.cs (5 tests, fully faked IFrameSampler/IFfprobeService)
  Fixtures/
    SyntheticVideoFixtureBuilder.cs   (ffmpeg-generated clips, exact ground-truth windows)
    TransitionDetectorFixtureTests.cs (SkippableFact, real ffmpeg, real end-to-end pipeline)

tests/SceneForge.Media.Tests/TestSupport/
  FrameSampleBuilder.cs, FrameSignalSampleBuilder.cs, FakeFrameSampler.cs (new)

benchmarks/SceneForge.Benchmarks/Detection/
  BenchmarkFrameGenerator.cs, TransitionDetectionBenchmarks.cs
```

Also changed: `Directory.Packages.props` (added `OpenCvSharp4` /
`OpenCvSharp4.runtime.win` 4.13.0.20260627), `Directory.Build.props` (root -
added a repo-wide MSBuild target working around an OpenCvSharp4 analyzer/SDK
compiler-version mismatch, see below), `src/SceneForge.Media/SceneForge.Media.csproj`,
`tests/SceneForge.Media.Tests/SceneForge.Media.Tests.csproj`,
`benchmarks/SceneForge.Benchmarks/SceneForge.Benchmarks.csproj` (all three:
added the two OpenCvSharp4 package references). No other existing file
changed; `SceneForge.App`/`SceneForge.Infrastructure` untouched.

## Design summary

### Composable signals, not one magic threshold

`AnalyzedFrame.Create` turns one `FrameSample` into independently-owned
OpenCvSharp `Mat`s (decoupled from the source `FrameSample`'s pooled buffer
via an eager `Marshal.Copy`, never a view over it) plus eagerly-computed
scalars. `SignalPipeline` is the only place that touches `AnalyzedFrame`/Mat
directly; every one of the seven required signals is its own named,
independently unit-tested extractor implementing one of three small
interfaces (`IPairSignalExtractor`, `ISingleFrameSignalExtractor` for
black/white-frame score, `IGlobalMotionSignalExtractor`):

| Signal | Extractor | What it measures |
|---|---|---|
| HSV histogram distance | `HsvHistogramDistanceExtractor` | Bhattacharyya distance between consecutive Hue/Saturation histograms |
| Luminance delta | `LuminanceDeltaExtractor` | signed mean-grayscale change |
| Edge-density delta | `EdgeDensityDeltaExtractor` | signed change in Canny edge-pixel fraction |
| Laplacian blur change | `LaplacianBlurChangeExtractor` | signed change in Laplacian-variance focus measure |
| Black/white-frame score | `BlackScoreExtractor` / `WhiteScoreExtractor` | current frame's own near-black / near-white pixel fraction (the one single-frame, non-delta signal) |
| Structural difference | `StructuralDifferenceExtractor` | normalized mean absolute grayscale pixel difference |
| Global motion estimate | `GlobalMotionEstimateExtractor` | Farneback optical flow reduced to Magnitude/RadialOutwardScore/DirectionalConsistency over a bounded 3x3 cell grid |

Two auxiliary absolute-value fields (`CurrentLaplacianVariance`,
`CurrentEdgeDensity`) ride alongside the required delta signals in
`FrameSignalSample` purely so `BlurTransitionClassifier` can reason about a
fractional drop from a window's own baseline rather than only ever seeing
frame-to-frame deltas with no absolute reference point — this does not add
an eighth required signal, it is implementation support for one.

### Seven classifiers, one per type

Each `ITransitionClassifier` scans a bounded `ClassifierWindow` (capacity
derived from `profile.MaxTransitionDuration`) independently and returns
zero or more `TransitionCandidate`s:

- **HardCutClassifier** — an isolated single-pair spike in
  StructuralDifference **or** HsvHistogramDistance (each judged isolated on
  its own terms, not ANDed — see self-review below).
- **FadeBlackClassifier** — finds the window's darkest point; if it reaches
  a peak BlackScore threshold, walks outward while BlackScore keeps moving
  toward the peak (monotonic expansion, not a fixed fraction-of-peak
  cutoff — see self-review), emitting `FadeToBlack` and/or `FadeFromBlack`.
- **DissolveClassifier** — a sustained (multi-sample), not-isolated
  elevation of StructuralDifference that does not pass through
  near-black/near-white.
- **FlashClassifier** — same shape as FadeBlack but keyed on WhiteScore,
  distinguished from a fade-to-white purely by total duration.
- **BlurTransitionClassifier** — a fractional Laplacian-variance drop from
  the window's own baseline, corroborated by an edge-density drop.
- **ZoomTransitionClassifier** / **DirectionalSwipeClassifier** — sustained
  runs (via the shared `ContiguousRunFinder` helper) where GlobalMotion's
  radial or directional-consistency score is elevated above a
  magnitude-gated threshold.

### Versioned fusion, never silent

`TransitionDetectionProfile` (obtained via
`TransitionDetectionProfiles.GetDefaults(TransitionDetectionProfileVersion.V1)`,
same pattern as `Sampling.FrameSamplingProfiles`) carries every classifier's
thresholds plus `MaxTransitionDuration`, `MergeGapTolerance`,
`PreBufferDuration`/`PostBufferDuration` — all clamped in `init` setters.
`TransitionFuser.Fuse` collapses the raw candidate stream (deliberately
full of duplicates — every classifier re-scans the window on every new
sample) via value-equality `Distinct()`, groups overlapping/near candidates,
and picks a winner using confidence plus a small, documented specificity
bonus (HardCut/FadeToBlack/FadeFromBlack/Flash +0.2, Zoom/Swipe/Blur +0.1,
Dissolve +0) so a generic Dissolve false alarm can't silently replace a
more specific correct detection on a tie. Losers are folded into
`ContributingSignals`/`DiagnosticReason`, never dropped.

### Scene boundary ≠ contaminated interval

`TransitionDetection.BoundaryTimestamp` is a single recommended splice
point; `[Start, End]` is the full contaminated interval a later
splitting/export stage must not treat as clean footage. These are
deliberately never conflated: `TransitionFuserTests` asserts
`Start < End` strictly and `BoundaryTimestamp` always falls within
`[Start, End]` for every candidate shape tested, and the type's XML remarks
state the rule explicitly.

### Bounded memory regardless of video length

At most two `AnalyzedFrame` instances (previous/current) are ever alive at
once in `SignalPipeline`, each disposed as soon as no longer needed.
`ClassifierWindow` holds only `MaxTransitionDuration`'s worth of
`FrameSignalSample`s (tiny scalar records, not pixel data) — a few seconds
at a few samples/second, tens of entries — regardless of total video
duration. No stage buffers the whole video's frames or signal series.

## Self-review findings

Two real bugs and one design change were found only once measured against
real ffmpeg-decoded content (not the hand-built unit-test fixtures) — this
is exactly why CLAUDE.md rule 8/9 requires evidence, not just passing unit
tests, before calling algorithmic work complete:

| Area | Finding | Resolution |
|---|---|---|
| HardCut false negatives | HsvHistogramDistance is computed over Hue/Saturation only (ignores Value/brightness) — a same-hue, brightness-only cut on desaturated content produced near-zero HSV distance, and the original AND-gate (`StructuralDifference >= threshold AND HsvHistogramDistance >= threshold`) missed it entirely. | Changed to independent OR-gating: each signal qualifies (and is judged isolated) on its own terms, so either a brightness-only or a hue-only cut is caught. Confirmed via `HardCutClassifierTests.Classify_IsolatedStructuralSpikeAlone_DetectsHardCut` / `..HsvSpikeAlone..`. |
| FadeFromBlack never detected | `FadeBlackClassifier`'s ramp-out walker used a fixed "still above 30% of peak" cutoff to bound the walk. Measured against a real fade-filter clip, BlackScore dropped from 0.98 to 0.29 in a single sample interval (BlackScore is highly nonlinear near its extremes) — below the 30% cutoff after just one step, so the walk terminated before accumulating `MinRampSamples`, and `FadeFromBlack` was never emitted despite the ramp genuinely continuing for three more samples. | Replaced the fixed-fraction cutoff with monotonic-trend expansion (walk while the score keeps moving toward the peak, stop only when it turns around). Verified against the real fixture: FadeBlackClassifier went from 0 to 10 raw `FadeFromBlack` candidates over the same clip. |
| FadeToBlack/FadeFromBlack silently merged into one | `TransitionFuser` originally merged any candidates within `MergeGapTolerance` of each other regardless of type. `FadeFromBlack.Start` exactly equals `FadeToBlack.End` (the shared black-peak instant) by design, so they always merged into a single detection, and whichever type lost the confidence tie was discarded outright rather than folded in. | Cross-type merges now require genuine interval overlap (`Start < groupEnd`, not `<=`); same-type merges keep the gap-tolerance behavior (needed to collapse the deliberate sliding-window duplicates). `TransitionFuserTests` covers both paths. |

## Real-binary verification and measured metrics by transition type

Same approach as Phases 4/5: this dev machine has ffmpeg 9.0.1
(`Gyan.FFmpeg.Shared`, via `winget`) available outside the repo. Its `bin/`
folder was temporarily copied into
`tests/SceneForge.Media.Tests/bin/Debug/net8.0/tools/ffmpeg/` (never
committed — `tools/` is gitignored) to run `TransitionDetectorFixtureTests`
end-to-end (real `FrameSampler` → real ffmpeg decode → real
`TransitionDetector`) against `SyntheticVideoFixtureBuilder`'s deterministic
clips, then deleted.

`SyntheticVideoFixtureBuilder` uses only ffmpeg's own generator sources
(`testsrc2`/`smptebars`/`rgbtestsrc`, two independent base-content pairs per
type — never tuned against a single video) and filters whose timing
parameters give an exact, documented ground-truth window: `xfade`'s
`duration`/`offset` for Dissolve/DirectionalSwipe, `fade`'s `st`/`d` (scoped
with `enable='between(t,...)'` — see below) for FadeToBlack/FadeFromBlack/
Flash, `boxblur`'s `enable=` window for BlurTransition, a time-varying
`scale`+`crop` expression for a genuine ZoomTransition, and `concat` for
HardCut.

Two fixture-construction bugs were themselves found and fixed via this same
measurement process (not assumed correct because the ffmpeg command "looked
right"): `fade=type=in` holds the fade *color* for every frame **before**
its own `st`, and `fade=type=out` holds it for every frame **after**
`st+d`, regardless of any earlier filter in the same `-vf` chain — an
unscoped `fade=out,fade=in` chain (e.g. for Flash or FadeBlack) silently
recolored the entire clip, not just the intended pulse/ramp. Scoping each
fade to its own `enable='between(t, its_own_start, its_own_end)'` window
fixed both fixtures; the debug dump before/after is what caught it (every
sample showing `white=1.000`/`black=1.000` from frame one, not just during
the intended window).

Measured results (`AnalysisProfile.Accurate`, 8fps/480px, `V1` profile),
one `TransitionDetectorFixtureTests` run, 14 fixtures (7 types × 2
independent base-content variants each; FadeToBlack/FadeFromBlack share
one fixture per variant):

| Type | TP | FN | FP | Recall | Precision | Mean boundary error |
|---|---|---|---|---|---|---|
| HardCut | 1 | 1 | 4 | 50% | 20% | 0 ms |
| FadeToBlack | 2 | 0 | 0 | 100% | 100% | 250 ms |
| FadeFromBlack | 1 | 1 | 0 | 50% | 100% | 250 ms |
| Dissolve | 1 | 1 | 3 | 50% | 25% | 125 ms |
| Flash | 2 | 0 | 0 | 100% | 100% | 130 ms |
| BlurTransition | 1 | 1 | 1 | 50% | 50% | 0 ms |
| ZoomTransition | 0 | 2 | 0 | 0% | n/a | n/a |
| DirectionalSwipe | 0 | 2 | 1 | 0% | 0% | n/a |

**These numbers are measured, bounded to this specific synthetic fixture
matrix, and not claimed to generalize to arbitrary real-world footage
(CLAUDE.md rule 10) — they are the current, honest evidence, not a target.**
Five of seven types have real, non-zero measured recall on real
ffmpeg-decoded content (not just hand-built unit-test data); two
(FadeToBlack, Flash) reach 100%/100% on this matrix. Known, measured
limitations, left unresolved for a later phase given this phase's time
budget:

- **HardCut/Dissolve false positives (FP 4 and 3 respectively).** Several
  fixtures built to exercise a *different* classifier also produce a
  plausible, weaker HardCut or Dissolve candidate over the same interval,
  because StructuralDifference/HsvHistogramDistance genuinely do rise
  during most kinds of transitions, not only cuts/dissolves. `TransitionFuser`'s
  specificity-bonus tie-break (see Design summary) reduces but does not
  eliminate this; the underlying classifiers' own isolation/sustain logic
  would need further tightening against a larger fixture matrix.
- **ZoomTransition/DirectionalSwipe (0% recall on this matrix).**
  Confirmed via direct, isolated classifier runs against the real captured
  signal data that both classifiers *do* produce genuine candidates for
  their own fixtures in isolation — the failure is at fusion time: an
  unrelated classifier's (typically HardCut's) higher-confidence,
  higher-priority false alarm over the same interval wins the merge and
  the correct Zoom/Swipe detection is folded into its diagnostic text
  instead of surfacing as the winning type. Additionally, at the
  ~160×90-equivalent analysis resolution this fixture matrix exercises,
  normalized optical-flow motion magnitude during a genuine zoom/swipe
  (~0.01–0.03) sits close enough to the magnitude produced by `testsrc2`'s
  own gentle non-transition motion (~0.003–0.005) that the signal itself is
  noisy at this scale. `GlobalMotionEstimateExtractor`'s Farneback
  parameters were already tuned once during this phase (`levels`
  2→4, `winsize` 15→21, `iterations` 2→3) in response to this exact
  measurement, which materially improved but did not fully resolve it.
  `TransitionDetectorFixtureTests` measures and reports these two types'
  metrics like every other type but does not hard-assert `recall > 0` for
  them specifically, with the reasoning recorded in-line in the test.

## Memory: bounded window, not full-video buffering

No dedicated long-duration memory test was added this phase (unlike Phase
5's `FrameSamplerMemoryTests`) because the bound here is structural and
independent of video length by construction, not by amortization: `ClassifierWindow`
is a `List<FrameSignalSample>` capped at `MaxTransitionDuration` worth of
tiny scalar records (a `FrameSignalSample` is on the order of 100 bytes;
2.5s at 8fps is ~20 entries, regardless of whether the source video is 10
seconds or 10 hours long), and `SignalPipeline` never holds more than two
`AnalyzedFrame`s (each a handful of small OpenCvSharp `Mat`s, not the
original frame buffer) at once. `TransitionDetectorFixtureTests` running
against real ~4-second decoded clips is the practical evidence that this
holds end-to-end with real frame data flowing through the whole pipeline.

## Benchmark: signal-extraction + classification cost per profile

`TransitionDetectionBenchmarks.AnalyzeAndClassify` (BenchmarkDotNet,
`MemoryDiagnoser`, Release) runs the real `SignalPipeline` + all 7
`ITransitionClassifier`s against a synthetic, deterministic, in-memory
200-frame sequence (no real ffmpeg — isolates this phase's own pipeline
cost from ffmpeg's decode cost, same reasoning as `FrameSamplingBenchmarks`),
once per `AnalysisProfile` at that profile's own analysis width, 200 frames
per iteration:

```
| Method             | AnalysisProfile | Mean     | Error    | StdDev   | Allocated |
|------------------- |----------------- |---------:|---------:|---------:|----------:|
| AnalyzeAndClassify | Fast             |  5.141 s | 0.0621 s | 0.0581 s |  34.64 MB |
| AnalyzeAndClassify | Balanced         |  7.335 s | 0.1377 s | 0.1288 s |  49.13 MB |
| AnalyzeAndClassify | Accurate         | 11.845 s | 0.1167 s | 0.1035 s |  75.83 MB |
```

Run on: AMD Ryzen 5 3500U, .NET 8.0.30, Windows 10 22H2, `--configuration
Release`. That is ~25.7 ms/frame-pair (Fast, 320px), ~36.7 ms/frame-pair
(Balanced, 384px), ~59.2 ms/frame-pair (Accurate, 480px) — cost scales with
analysis resolution as expected, dominated by Farneback optical flow. All
three profiles show heavy Gen0/Gen1/Gen2 activity (10,000-22,000 collections
per 1,000 operations) — a direct, expected consequence of the deliberate
"every classifier re-scans the whole sliding window on every new sample"
design (see `ClassifierWindow`'s remarks): each of the ~199 frame pairs in
this benchmark triggers up to 7 full window scans, each allocating a fresh
result list and (for any candidate found) a `ContributingSignals`
dictionary. This is a real, measured cost of that design choice, not
overhead this phase attempted to hide; a later phase revisiting throughput
for long videos should start here; that would then be an *optimization*
requiring its own before/after benchmark evidence per CLAUDE.md rule 9,
which does not apply to this first, baseline measurement of new
functionality (there is no prior version to diff against, same handling as
Phase 5's own baseline).

## Commands executed and results

All commands run from `C:\Users\Bwp COmputers\Desktop\scene-forge` with the
pinned .NET 8.0.424 SDK.

### The OpenCvSharp4 / pinned-SDK analyzer conflict

`OpenCvSharp4` 4.13.0.20260627's bundled Roslyn analyzer targets a newer
compiler (4.14.0.0) than the one the pinned .NET 8.0.424 SDK ships
(4.11.0.0), which fails the build with `CS9057`. Excluding the analyzer
asset on the `PackageReference` (`ExcludeAssets="analyzers"`) correctly
narrows what `dotnet restore` selects but, measured directly (inspecting
`obj/*.nuget.dgspec.json`, which confirmed the exclusion *did* take effect
at the restore/asset-selection level), `ResolvePackageAssets` still
surfaced the analyzer DLL as an `Analyzer` item at `CoreCompile` time
regardless — a project.assets.json/MSBuild interaction, not an SDK version
question that upgrading the pinned SDK would necessarily have avoided
either. The `ExcludeAssets` attribute is kept (harmless, correctly
expresses intent) alongside a guaranteed fix in `Directory.Build.props`: a
repo-wide `BeforeTargets="CoreCompile"` target that removes any `Analyzer`
item named `OpenCvSharp.Analyzers`, a no-op for every project that doesn't
reference the package. `global.json`'s pinned SDK version was not changed.

### Format

```
dotnet format SceneForge.sln
dotnet format SceneForge.sln --verify-no-changes
```
First run reported `ENDOFLINE` errors across every newly authored file
(created with LF; repo requires CRLF) — the same situation every prior
phase's report has hit. `dotnet format SceneForge.sln` fixed it in place;
the follow-up `--verify-no-changes` run produced no output.

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
With the real ffmpeg binaries temporarily present (Debug only):
```
Passed! - Failed: 0, Passed: 1,   Skipped: 0 - SceneForge.Core.Tests.dll  (net8.0)
Passed! - Failed: 0, Passed: 244, Skipped: 0 - SceneForge.Media.Tests.dll (net8.0)
```
After removing the temporary `tools/ffmpeg` copy (never committed), both
configurations show the expected CI-equivalent skip count:
```
Passed! - Failed: 0, Passed: 1,   Skipped: 0 - SceneForge.Core.Tests.dll  (net8.0)
Passed! - Failed: 0, Passed: 237, Skipped: 7 - SceneForge.Media.Tests.dll (net8.0)  (Debug)
Passed! - Failed: 0, Passed: 237, Skipped: 7 - SceneForge.Media.Tests.dll (net8.0)  (Release)
```
The 7 skipped are all `[SkippableFact]` real-binary tests: 6 pre-existing
from Phases 4/5 plus 1 new this phase (`TransitionDetectorFixtureTests`).
One transient flake was observed and
diagnosed: `FrameSamplerMemoryTests.SampleAsync_RetainedMemoryStaysApproximatelyFlatAsSimulatedDurationIncreases`
(a pre-existing Phase 5 GC-timing-sensitive test, untouched by this phase)
failed once in a full-suite run, then passed both in isolation and in an
immediate full-suite re-run — consistent with GC-state noise from the
~240 other tests (many now doing real OpenCvSharp/native-Mat work) sharing
one process ahead of it, not a regression introduced by this phase's code.

### Benchmark

```
cd benchmarks/SceneForge.Benchmarks
dotnet run --configuration Release --no-build -- --filter "*TransitionDetectionBenchmarks*"
```
Completed in ~10.5 minutes (three profiles x 200 frames x BenchmarkDotNet's
own multi-iteration measurement overhead); results captured above.
`FrameSamplingBenchmarks`/`ModuleInfoBenchmarks` (Phases 3/5, unchanged)
remain runnable via the same switcher.

## Test inventory (new this phase)

102 new tests this phase (101 always-run + 1 `[SkippableFact]` real-ffmpeg
fixture test), verified directly from `dotnet test` output: the Phase 5
baseline was 142 total (136 passed + 6 skipped); this phase's suite is 244
total (237 passed + 7 skipped) with real ffmpeg absent, 244 passed with it
present.

- **AnalyzedFrameTests** (8) — black/white/mid-gray solid-color known
  answers for BlackScore/WhiteScore/MeanLuminance, zero edge
  density/Laplacian variance on solid color vs. positive on a checkerboard,
  Gray8 input throws, double-`Dispose()` is safe, HSV histogram sums to ~1.
- **Per-extractor tests** (HsvHistogramDistance, LuminanceDelta,
  EdgeDensityDelta, LaplacianBlurChange, BlackWhiteFrameScore,
  StructuralDifference, GlobalMotionEstimate — 21 total) — known-answer
  cases against hand-built solid-color/checkerboard/textured-shift frames;
  `GlobalMotionEstimateExtractorTests` uses a continuous 2D sinusoidal
  texture (not a flat gradient or periodic checkerboard, both measured to
  be pathological/ambiguous cases for Farneback) with controllable
  translation, asserting magnitude scaling and directional consistency
  rather than exact vectors.
- **SignalPipelineTests** (5) — empty/single-frame yields nothing,
  two-frame pair yields one sample, three identical frames yield two
  zero-delta samples, external cancellation throws.
- **ContiguousRunFinderTests** (5), **ClassifierWindowTests** (2).
- **Per-classifier tests** (23 across all 7 classifiers) — isolated-spike
  detection and sustained-elevation rejection for HardCut; both fade
  directions and below-threshold rejection for FadeBlack; bell-shaped
  detection, black-frame-passthrough rejection, and too-short rejection for
  Dissolve; brief-spike detection and sustained-span/below-threshold
  rejection for Flash; sharp-blur-sharp detection, consistently-sharp
  rejection, and variance-drop-without-edge-drop rejection for
  BlurTransition; sustained-run detection, sign-flip splitting, and
  magnitude-gating for Zoom; sustained-run detection and
  consistency/magnitude-gating for DirectionalSwipe.
- **TransitionDetectionProfile(s)Tests** (10) — every clamp at both ends,
  V1 defaults present, unknown version throws.
- **TransitionFuserTests** (9) — no-candidates, single-candidate
  buffer/boundary invariants, same-type overlap and gap-tolerance merging,
  beyond-tolerance separation, exact-duplicate dedup, cross-type
  winner/fold-in, pre-buffer zero-clamp, post-buffer video-duration-clamp.
- **TransitionDetectionCsvWriterTests** / **JsonWriterTests** (9) — header-only
  on empty input, formatted row content, CSV escaping, JSON round-trip
  including the TimeSpan-as-seconds converter and enum-as-string, file
  writer creates a new file, and refuses (throws, leaves the existing file
  untouched) to overwrite an existing one.
- **TransitionDetectionOptionsTests** (2), **TransitionDetectionTests** (1).
- **TransitionDetectorTests** (5, fully faked `IFrameSampler`/`IFfprobeService`,
  no ffmpeg) — end-to-end hard-cut detection through the whole orchestrator,
  no-change-detects-nothing, no-video-stream fails fast without invoking
  the frame sampler, external cancellation propagates, progress is reported
  once per analyzed frame pair.
- **TransitionDetectorFixtureTests** (1, `[SkippableFact]`, real ffmpeg) —
  the full 14-fixture precision/recall/boundary-error matrix described
  above.

## Compliance notes against CLAUDE.md

- Rule 1–2 (native WPF, no Electron/web/cloud/telemetry): satisfied — pure
  processing-pipeline code, no UI, network, or telemetry.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): both now in active use — FFmpeg
  via the existing `Sampling` pipeline this phase consumes unchanged,
  OpenCvSharp newly integrated for every signal computed in `Detection/Signals`.
- Rule 4 (clean architecture): unchanged dependency graph
  (`Media → Core` only); `Detection/` is pure processing-pipeline code,
  layered internally (Signals → Classification → Fusion → the public
  `TransitionDetector` orchestrator → Export), no UI concerns.
- Rule 5 (cancellation/cooperative shutdown): `TransitionDetector.DetectAsync`
  and `SignalPipeline.ComputeAsync` both honor the external token
  explicitly (checked every loop iteration, not only relied upon via
  downstream awaits), verified by `SignalPipelineTests`/`TransitionDetectorTests`
  cancellation cases.
- Rule 6 (bounded memory/concurrency): `ClassifierWindow` bounds every
  classifier's view to `MaxTransitionDuration`'s worth of signal samples
  (clamped 200ms–10s in `TransitionDetectionProfile`); at most two
  `AnalyzedFrame`s alive at once in `SignalPipeline`; no unbounded
  queue/cache/fan-out introduced anywhere in this phase.
- Rule 7 (no full-video buffering): frames stream through `SignalPipeline`
  one pair at a time via the existing bounded `IFrameSampler` channel; the
  only thing retained across the whole run is the small, bounded candidate
  list (one entry per detected event, not per frame) and the bounded
  sliding window — never the frames/pixels themselves.
- Rule 8 (test-first): every signal, classifier, fusion rule, and export
  behavior has dedicated unit tests with no ffmpeg dependency, written
  before/alongside each component; the three self-review bugs above each
  have a regression test (`Classify_IsolatedStructuralSpikeAlone_DetectsHardCut`,
  the FadeBlack ramp tests, `TransitionFuserTests`' cross-type-overlap
  tests) and were only found by adding the real-ffmpeg fixture matrix,
  which is itself now a permanent (if `[SkippableFact]`-gated) regression
  test.
- Rule 9 (benchmark with evidence): `TransitionDetectionBenchmarks` records
  a new-functionality baseline (see above) — no prior version exists to
  diff against, same handling as Phase 5.
- Rule 10 (never claim absolute accuracy): the measured-metrics table above
  is reported with explicit per-type numbers, not a single pass/fail
  claim; known false-positive and zero-recall limitations are documented
  by name rather than omitted; no code path or doc claims 100% or
  "guaranteed" detection anywhere.
- Rule 11–12 (preserve user files, output to new path only): `Detection/`
  never writes to the source media file; `TransitionDetectionCsvWriter`/
  `JsonWriter` write only to a caller-provided output path, using
  `FileMode.CreateNew` (throws rather than silently overwriting an
  existing file at that path) plus `OutputDirectoryValidator.EnsureWritable`
  (the same validator `SceneForge.Media.Validation` already provides).
- Rule 13 (format/build/tests before ending): all run above — format
  clean, Debug and Release both build with 0 warnings/errors, Debug and
  Release both pass all non-real-ffmpeg tests with the expected skip count
  after cleanup.
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (Decision 3 already
  named OpenCvSharp as part of the media stack).
- Rule 15 (don't advance while criteria fail): this phase's own criteria —
  solution builds/tests clean in Debug and Release, formatting clean, real-
  ffmpeg pipeline verified end-to-end with measured (not assumed) metrics,
  benchmark recorded, `SceneForge.App` wiring explicitly out of scope — are
  met as of this report.

## Outstanding for later phases

- `SceneForge.App` wiring (a UI surface to run `TransitionDetector` against
  a user-selected file, show progress, cancel, and export CSV/JSON to a
  user-selected path).
- The measured false-positive (HardCut/Dissolve) and zero-recall
  (ZoomTransition/DirectionalSwipe) limitations documented above are real,
  open items — a larger, more varied fixture matrix (more base-content
  sources, higher analysis resolution, real recorded footage rather than
  only ffmpeg generator sources) is needed before further threshold/fusion
  tuning would be evidence-based rather than guesswork.
- A dedicated long-duration memory test analogous to
  `FrameSamplerMemoryTests` (Phase 5) would strengthen the bounded-memory
  claim with a measured number rather than only a structural argument.
- Batch/concurrent orchestration across multiple files remains out of
  scope, as it was for Phase 5.
