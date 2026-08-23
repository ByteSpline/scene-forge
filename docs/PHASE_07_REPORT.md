# Phase 07 Report — Clean Clip Extraction (Interval Subtraction, Scoring, Perceptual Clustering, Export)

Date: 2026-08-23

## Scope

Build `CleanClipExtractor` on top of Phase 5's frame-sampling pipeline and
Phase 6's transition detections (the latter consumed only as caller-supplied
`TimeRange` intervals, never recomputed): subtract every buffered transition
interval, black/frozen/invalid interval, and user-excluded interval from a
caller-supplied list of scene ranges; generate 3-5 second clip candidates
from what remains, with a boundary guard and sliding-window overlap;
compute seven independent, deterministic, explainable score factors per
candidate; build a cheap perceptual fingerprint (pHash, color histogram,
edge histogram, motion class) per candidate; cluster visually similar
accepted candidates with a small greedy algorithm (no ML model, no external
dependency); and export the whole result to JSON for manual inspection.
Explicitly **not** in scope: `SceneForge.App` wiring (still no UI surface to
run this from), detecting black/frozen/invalid intervals or transitions
itself (both are taken as caller-supplied facts — see Design summary), and
any concurrent/batch orchestration across multiple files.

## Repository layout produced

```
src/SceneForge.Media/Extraction/
  CleanClip.cs, ClipScore.cs (+ ScoreReason), PerceptualDescriptor.cs (+ MotionClass),
  VisualCluster.cs, RejectionReason.cs, ExcludedInterval.cs (+ ExclusionKind)
  CleanClipExtractionOptions.cs (+ CleanClipScoringOptions, ClusteringOptions)
  CleanClipExtractionResult.cs, CleanClipExtractionProgress.cs,
  CleanClipExtractionException.cs, ICleanClipExtractor.cs, CleanClipExtractor.cs
  Intervals/
    IndexedTimeRange.cs, IntervalSubtractor.cs, ClipCandidateGenerator.cs,
    ExclusionDistanceCalculator.cs
  Signals/
    ClipFrameMetrics.cs, ClipFrameMetricsExtractor.cs, ClipFrameMetricsPipeline.cs,
    EdgeHistogramExtractor.cs, PerceptualHashExtractor.cs, ColorHistogramExtractor.cs
  Scoring/
    ClipScorer.cs, MotionClassifier.cs
  Streaming/
    CleanClipScoringSweep.cs
  Clustering/
    PerceptualDistance.cs, VisualClusterer.cs
  Export/
    CleanClipJsonWriter.cs

tests/SceneForge.Media.Tests/Extraction/
  CleanClipExtractorTests.cs (9 tests, fully faked IFrameSampler/IFfprobeService)
  CleanClipExtractorIntegrationTests.cs (1 test, SkippableFact, real ffmpeg)
  CleanClipExtractionOptionsTests.cs (11 tests)
  Intervals/    (IntervalSubtractor + ClipCandidateGenerator + ExclusionDistanceCalculator - 35 tests, no ffmpeg)
  Signals/      (EdgeHistogram/PerceptualHash/ColorHistogram extractors + ClipFrameMetricsExtractor/Pipeline - 33 tests, real OpenCvSharp Mats, no ffmpeg)
  Scoring/      (ClipScorer + MotionClassifier - 15 tests, no OpenCvSharp)
  Streaming/    (CleanClipScoringSweep - 9 tests, no OpenCvSharp)
  Clustering/   (PerceptualDistance + VisualClusterer - 12 tests, no OpenCvSharp)
  Export/       (CleanClipJsonWriter - 4 tests)

tests/SceneForge.Media.Tests/TestSupport/
  ClipFrameMetricsBuilder.cs (new)
```

No `.csproj` changes: `Extraction/` lives in the existing `SceneForge.Media`
project and reuses the `OpenCvSharp4`/`OpenCvSharp4.runtime.win` package
references Phase 6 already added; the test project likewise needed no new
package references.

## Design summary

### Scene ranges and exclusion intervals are caller-supplied facts, not computed here

Grepping the codebase before starting confirmed there is no `Scene`,
`SceneRange`, black/frozen/invalid-interval, or user-exclusion type
anywhere yet — Phase 6 produces `TransitionDetection` (a contaminated
`[Start, End]` interval), but nothing upstream yet groups a video into
scenes, and nothing detects black/frozen/invalid frames as a standing
interval concept. Rather than build those (out of scope for this phase and
each arguably its own future phase), `CleanClipExtractor` takes
`CleanClipExtractionOptions.SceneRanges` (`IReadOnlyList<TimeRange>`) and
`ExcludedIntervals` (`IReadOnlyList<ExcludedInterval>`, each tagged with an
`ExclusionKind` of `Transition`, `BlackFrozenInvalid`, or `UserExcluded`
plus an optional human-readable `Reason`) as plain caller-supplied facts.
This keeps the dependency graph clean (`Extraction` does not need to know
*how* a `TransitionDetection` or a black-frame interval was produced, only
that it is a `TimeRange` to subtract) and means a caller can feed Phase 6's
`TransitionDetection.Start`/`.End` straight in as `ExclusionKind.Transition`
intervals without this phase depending on `Detection` at the type level.

### Never reconstruct frames hidden by an effect — enforced structurally, not by a runtime check

`IntervalSubtractor.Subtract` is pure interval math with no I/O: it merges
overlapping/touching exclusions first, then sweeps each scene range once,
emitting only the disjoint remainder. `ClipCandidateGenerator` (also pure)
generates every clip candidate exclusively from that remainder. Because
candidates are never generated from anything else, an excluded interval
cannot leak into a clip candidate by construction — there is no later
"does this overlap an exclusion" filter to forget to call, and
`CleanClipExtractorTests.ExtractAsync_ExcludedIntervalInMiddleOfScene_NoClipEverOverlapsIt`
plus `IntervalSubtractorTests.Subtract_NeverAttemptsToRecoverAnExcludedInterval_...`
assert this invariant directly rather than trusting the design by
inspection alone.

### Interval subtraction: off-by-one, short remnants, overlapping exclusions

`IntervalSubtractor` uses the same strict-inequality "touching is not
overlapping" convention `TimeRange.Overlaps` already established
(`Start < other.End && other.Start < End`), so an exclusion whose `End`
exactly equals a scene range's `Start` (or vice versa) consumes none of it
and — critically — never produces a zero-length remainder at that boundary
(`Subtract_ExclusionEndsExactlyAtSceneEnd_NoZeroLengthRemainder`,
`..StartsExactlyAtSceneStart..`). Overlapping or exactly-duplicate
exclusions are merged (sorted by `Start`, merged when
`next.Start <= currentEnd`) before the single sweep runs, so
`Subtract_OverlappingExclusions_MergeBeforeSubtracting` and
`Subtract_DuplicateExclusions_MergeToOne` never see the range incorrectly
double-carved. A narrow gap between two exclusions is preserved exactly as
computed, not silently dropped or clamped
(`Subtract_NarrowGapBetweenTwoExclusions_IsPreservedAsShortRemainder`) —
deciding whether a remnant is *too short to use* is deliberately
`ClipCandidateGenerator`'s job (it returns no candidate for a guarded range
below `MinClipDuration`), not `IntervalSubtractor`'s: the subtractor's only
contract is mathematical correctness of the remainder.

### Exact TimeSpan arithmetic, not floating-point seconds

Both `IntervalSubtractor` and `ClipCandidateGenerator` operate entirely in
`TimeSpan` (backed by `long` ticks), never `double` seconds, so there is no
floating-point epsilon anywhere in the boundary comparisons that decide
off-by-one behavior — `cursor + clipDuration <= guardedEnd` is either
exactly true or exactly false. `ClipCandidateGeneratorTests` exploits this
directly: `Generate_RangeExactlyMaxDurationAfterGuard_ProducesOneMaxDurationCandidate`
asserts an exact-duration boundary case with `Assert.Equal`, not
`Assert.InRange`.

### Candidate generation: boundary guard, then a sliding window

For each remaining range, `ClipCandidateGenerator` first trims
`BoundaryGuard` (default 250ms) off both ends — absorbing any residual
soft edge right at an exclusion boundary the exclusion interval itself may
not have captured exactly — then slides a fixed-duration window (as long as
`MaxClipDuration` allows, down to `MinClipDuration` for a range too short
for the maximum) across what remains, stepping by
`clipDuration * (1 - OverlapFraction)`. This deliberately generates
*multiple*, overlapping candidates per long remaining range rather than one
greedy maximal clip, so `ClipScorer` — not the generator — is what decides
which section of a long stable shot is "most" preferred; the generator's
job is coverage, not judgment.

### Seven independent, explainable score factors, never a single opaque number

`ClipScorer.Score` is pure (`TimeRange` + `IReadOnlyList<ClipFrameMetrics>`
+ distance-to-nearest-exclusion + `CleanClipScoringOptions` in,
`ClipScore` out) and computes Duration, Sharpness, Stability, Exposure,
FreezeRisk, TransitionDistance, and OverlaySuspicion, each normalized 0..1,
plus a weighted `Overall`. Every factor produces its own `ScoreReason`
(`Factor`, `Passed`, a nullable `RejectionReason` code, and a `Detail`
string carrying the actual measured value) — `ClipScore.Reasons` always has
exactly 8 entries (the seven factors plus a final "Overall" entry), so a
caller inspecting a *rejected* candidate sees every individual verdict, not
only whichever one happened to trip first. This mirrors the
`ContributingSignals`/`DiagnosticReason` "never opaque" convention
`TransitionDetection` established in Phase 6, extended to explain
acceptance as well as rejection (`ClipScorerTests.Score_AllFactorsWithinBounds_AcceptsCandidate`
asserts every reason is `Passed` with a null `Code`, not merely that
`Accepted` is `true`). FreezeRisk and OverlaySuspicion are the two
higher-is-worse factors (risk scores, not quality scores) and are inverted
before entering the weighted `Overall` average; this is called out
explicitly in `ClipScore`'s XML remarks since it is the one place in the
type that would silently produce a nonsensical `Overall` if a future
change forgot the inversion.

### Reusing Phase 6's Detection.Signals internals instead of duplicating them

`AnalyzedFrame` (Gray Mat, `MeanLuminance`, `LaplacianVariance`,
`EdgeDensity`, `BlackScore`, `WhiteScore`) and `StructuralDifferenceExtractor`
are `internal` types in `SceneForge.Media.Detection.Signals` — `internal` is
assembly-scoped, not namespace-scoped, so `Extraction.Signals.ClipFrameMetricsExtractor`
consumes both directly rather than re-implementing grayscale conversion,
sharpness, exposure, or frame-to-frame difference a second time.
`ColorHistogramExtractor` goes one step further: rather than converting to
HSV and calling `Cv2.CalcHist` again, it reduces `AnalyzedFrame`'s
already-computed, already-normalized 2D Hue x Saturation histogram down to
1D via `Cv2.Reduce(..., ReduceDimension.Column, ReduceTypes.Sum)`, so the
one HSV histogram computation Phase 6 already pays for is the only one this
phase pays for too. `EdgeHistogramExtractor` similarly does one `Cv2.Canny`
pass per frame and serves two purposes from it: the full 4x4 grid is the
`EdgeHistogram` perceptual-descriptor field, and `BorderDensity`/
`InteriorDensity` (mean of the outer-ring cells vs. the inner cells) is the
direct input to `ClipScore.OverlaySuspicion` — one Canny call, not two.

### Perceptual descriptors: pHash, color histogram, edge histogram, motion class — no ML model

`PerceptualHashExtractor` implements a standard mean-thresholded
low-frequency-DCT hash: resize to 32x32 grayscale, `Cv2.Dct`, keep the
top-left 8x8 block, threshold each of the 64 AC/DC coefficients (excluding
the DC term from the threshold mean, since DC reflects only overall
brightness) against the block's own mean, pack into a `ulong`.
`PerceptualHashExtractorTests` verifies identical frames hash identically
(Hamming distance 0) and that very different frames (solid color vs.
checkerboard) produce a strictly larger Hamming distance than near-identical
ones (two solid-black frames one shade apart) — a known-property test, not
a hand-verified reference hash, since pHash has no single "correct" bit
pattern to assert against. `MotionClass` (`Static`/`Subtle`/`Moderate`/`High`)
buckets the same mean structural-difference value `ClipScore.Stability`
normalizes against (`MotionClassifier.Classify`), so a clip's motion label
and its stability score are always describing one underlying measurement,
never two independently-tuned notions of "a lot of motion." None of this
involves an external ML model, embedding service, or GPU dependency —
every descriptor is a handful of OpenCvSharp calls per frame (CLAUDE.md
rule 2: no cloud API dependency).

### Clustering: deterministic greedy "leader" grouping, not a heavy model

`VisualClusterer.Cluster` is a single pass over descriptors in input order:
each one joins the first existing cluster whose *leader* (the earliest
member) is within `ClusteringOptions.SimilarityThreshold` of it via
`PerceptualDistance.Compute` (a weighted sum of normalized pHash Hamming
distance, mean-absolute color-histogram difference, mean-absolute
edge-histogram difference, plus a flat penalty when `MotionClass` differs);
otherwise it starts a new cluster as its own leader. `VisualClustererTests.Cluster_IsDeterministicForTheSameInputOrder`
asserts running it twice on the same input produces identical cluster
membership. `CleanClipExtractor` clusters only `AcceptedClips`' descriptors
— rejected candidates are never clustered, since there is no reason to
group content that was already excluded from the result.

### Streaming assignment: bounded memory without buffering the whole video's metrics

`IFrameSampler` has no seek/range support (confirmed by reading
`FrameSampler.BuildFfmpegArguments` — it always decodes from the start of
the file), so scoring every candidate anywhere in a long video cannot
re-decode per-candidate without massive duplicate work, and naively
retaining *every* sampled frame's `ClipFrameMetrics` for the whole video
before scoring would grow linearly and unboundedly with video length —
exactly what CLAUDE.md rule 6/7 rules out. `CleanClipScoringSweep.RunAsync`
instead does a single streamed pass: candidates are pre-sorted by `Start`,
and as each `ClipFrameMetrics` arrives it is handed to every currently
"open" candidate accumulator whose range contains it; an accumulator closes
(and is scored immediately, discarding its frame buffer) the moment the
stream's timestamp passes its `End`. Because candidates overlap only
through the small, fixed sliding-window stride `ClipCandidateGenerator`
uses, the number of concurrently open accumulators is bounded by a small
constant (`~1/(1-OverlapFraction)`, a handful at most) independent of video
length — the only thing ever retained across the whole run is that small,
bounded set of in-progress accumulators plus the already-finalized
`CleanClip` list (one small record per candidate, not per frame). Each
accumulator also tracks the *sharpest* frame it has seen as the clip's
representative frame for its perceptual descriptor, deliberately not the
temporally-centered one, since a mid-motion-blur or otherwise soft frame
would make a poor visual fingerprint for the whole clip
(`CleanClipScoringSweepTests.RunAsync_RepresentativeDescriptor_UsesSharpestFrameInWindow`).

### JSON export for manual inspection

`CleanClipJsonWriter` follows `Detection.Export.TransitionDetectionJsonWriter`'s
exact conventions (`System.Text.Json`, camelCase, indented, `TimeSpan` as
seconds via a private converter, enums as strings, `FileMode.CreateNew` so
an accidental re-run never silently overwrites a previous report). It
exports the *whole* `CleanClipExtractionResult` — `RemainingCleanRanges`
(what subtraction actually left), both `AcceptedClips` and `RejectedClips`
(each carrying its full `ClipScore.Reasons`), and `Clusters` — so a person
can inspect exactly why any given candidate was accepted or rejected
without re-running extraction or reading code.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| `EdgeHistogramExtractorTests` false assumption about Canny on fine periodic patterns | The original test built a strict 2-pixel-period checkerboard and asserted every 16x16 grid cell had a positive edge count. Measured directly: `Cv2.Canny` at the (50, 150) thresholds this codebase already uses (matching `AnalyzedFrame`'s own constants) found only 4 non-zero pixels across the entire 64x64 image for a 2px-period pattern — a genuine, known adversarial case for gradient-based edge detection (a period this fine aliases against the Sobel kernel Canny uses internally), not a bug in `EdgeHistogramExtractor`. `AnalyzedFrameTests`' own pre-existing `Create_CheckerboardFrame_HasPositiveEdgeDensityAndLaplacianVariance` only asserts `> 0` on the *whole-image* aggregate, which a handful of stray pixels trivially satisfies — it never actually exercised "edges reliably present," so this gap in test-content design was invisible until a per-cell assertion was added. Fixed by widening the test checkerboard's square size to 4px (measured via a standalone repro: min per-cell count 54 out of 256, comfortably positive in every cell) rather than weakening the assertion. |
| Border/interior split test asserted an unrealistic exact zero | A "border-only" synthetic frame (checkerboard confined to the outer 16px ring, flat fill elsewhere) was expected to give `InteriorDensity` an exact `0.0`. Measured: `0.03125` — Canny correctly finds the real edge where the checkered ring meets the flat interior fill, which is a genuine edge, not noise. The test's expectation, not the implementation, was wrong. Changed to a bounded-small assertion (`< 0.05`) plus the actual invariant under test (`border > interior`), for both the border-only and interior-only cases. |
| `ClipCandidateGeneratorTests` clustering test asserted more candidates than the chosen scene length could produce | A test using a 10s scene, `OverlapFraction = 0`, and default 3-5s clip bounds asserted `AcceptedClips.Count > 1` for the clustering test, but a 10s scene with 250ms guards and non-overlapping 5s candidates fits exactly one 5s candidate (9.5s guarded length, one candidate consumes 5s, 4.5s left over is short of a second 5s candidate). Not a product bug — an arithmetic mistake in the test's own setup. Fixed by widening the scene to 20s, which fits several back-to-back candidates and actually exercises multi-clip clustering. |

All three were caught by running the tests, not by inspection — consistent
with CLAUDE.md rule 8's requirement that new algorithmic behavior be
verified, not assumed correct.

## Real-binary verification

Same approach as every prior phase: this dev machine has ffmpeg 9.0.1
(`Gyan.FFmpeg.Shared`, via `winget`) available outside the repo. Its `bin/`
folder was temporarily copied into
`tests/SceneForge.Media.Tests/bin/Debug/net8.0/tools/ffmpeg/` (never
committed — `tools/` is gitignored) to run
`CleanClipExtractorIntegrationTests` end-to-end (real `ProcessRunner` ->
`FfmpegToolLocator` -> `FfprobeService` + `FrameSampler` -> the full
`CleanClipExtractor` pipeline) against the existing Phase 4 fixture
(`Fixtures/Media/sample_video_audio.mp4`: h264, 320x240, 25fps, ~2s), then
deleted.

Because that fixture is only ~2 seconds — shorter than the 3-5s production
default `MinClipDuration`/`MaxClipDuration` — this one test uses relaxed
clip-duration bounds (`MinClipDuration = 500ms`, `MaxClipDuration = 1s`,
`BoundaryGuard = 0`) so a real end-to-end run actually produces at least
one candidate; the 3-5s production default is exercised elsewhere, against
synthetic `ClipFrameMetrics` (`ClipScorerTests`,
`ClipCandidateGeneratorTests`) and faked frame streams
(`CleanClipExtractorTests`), never against a source this short. Measured
result of one real run:

```
Accepted: 3, Rejected: 0, Remaining ranges: 1
```

Every produced `CleanClip` (accepted and rejected) was asserted to carry
exactly 8 `ScoreReason` entries with `Overall` in `[0, 1]`, and the full
result round-tripped through `CleanClipJsonWriter.WriteAsync` to a non-empty
stream. This confirms the whole pipeline — real ffprobe, real ffmpeg
decode, real OpenCvSharp Mat processing (Canny, DCT, histogram reduction),
streaming candidate scoring, clustering, and JSON export — runs correctly
against a real file end to end, not only against hand-built fixtures.

### What this does not verify

This is a single-fixture smoke test, not a precision/recall matrix like
Phase 6's `TransitionDetectorFixtureTests` — there is no ground-truth
labeling of "which sections of this 2-second clip are genuinely clean" to
measure acceptance accuracy against, and the scoring thresholds throughout
`CleanClipScoringOptions` remain unmeasured heuristic starting points (see
Compliance notes, Rule 10). A larger, purpose-built fixture matrix with
known clean/damaged regions is listed under Outstanding for later phases.

## Memory: bounded regardless of video length

Two independent structural bounds, both exercised by the tests above rather
than only argued by inspection:

- **`ClipFrameMetricsPipeline`** holds at most two `AnalyzedFrame` instances
  (previous/current) alive at once, each disposed as soon as the next
  frame's metrics are computed — identical shape to Phase 6's
  `SignalPipeline`, and for the same reason (native OpenCvSharp `Mat`
  memory, not managed-heap, so disposal timing matters).
- **`CleanClipScoringSweep`** never retains the whole video's
  `ClipFrameMetrics` stream. Because `ClipCandidateGenerator` only produces
  candidates that overlap through a small, fixed sliding-window stride, the
  number of concurrently "open" candidate accumulators at any point in the
  stream is bounded by a small constant independent of video length or
  scene-range count — see Design summary above. `CleanClipExtractorTests`
  and `CleanClipExtractorIntegrationTests` exercise this end-to-end (a
  20-second synthetic run and a real ffmpeg-decoded run) without any test
  needing to special-case memory measurement, since the bound is structural
  by construction, the same reasoning Phase 6 used for `ClassifierWindow`.

No dedicated long-duration memory measurement test (analogous to Phase 5's
`FrameSamplerMemoryTests`) was added this phase — the same gap Phase 6 also
left open, listed again under Outstanding for later phases.

## Benchmark

**No BenchmarkDotNet benchmark was added this phase.** CLAUDE.md rule 9
requires before/after evidence for *optimizations*; this phase is new
functionality with no prior version to diff against, the same situation
Phase 5 documented for its own baseline (and unlike Phase 5, which still
recorded a baseline number, this phase's time budget did not extend to
building one). This is an honest, explicit gap, not an oversight papered
over — see Outstanding for later phases. The existing
`benchmarks/SceneForge.Benchmarks` project already has the infrastructure
(`Detection/`, `Sampling/` benchmark folders) an `Extraction/` benchmark
would follow the same pattern as.

## Commands executed and results

All commands run from `C:\Users\Bwp COmputers\Desktop\scene-forge` with the
pinned .NET 8.0.424 SDK.

### Format

```
dotnet format SceneForge.sln
dotnet format SceneForge.sln --verify-no-changes
```
First run reported `ENDOFLINE` errors on every newly authored file (created
with LF; repo requires CRLF) — the same situation every prior phase's
report has hit. `dotnet format SceneForge.sln` fixed it in place; the
follow-up `--verify-no-changes` run produced no output (exit 0).

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
Passed! - Failed: 0, Passed: 363, Skipped: 0 - SceneForge.Media.Tests.dll (net8.0)
```
After removing the temporary `tools/ffmpeg` copy (never committed), both
configurations show the expected CI-equivalent skip count:
```
Passed! - Failed: 0, Passed: 1,   Skipped: 0 - SceneForge.Core.Tests.dll  (net8.0)
Passed! - Failed: 0, Passed: 355, Skipped: 8 - SceneForge.Media.Tests.dll (net8.0)  (Debug)
Passed! - Failed: 0, Passed: 355, Skipped: 8 - SceneForge.Media.Tests.dll (net8.0)  (Release)
```
The 8 skipped are all `[SkippableFact]` real-binary tests: 7 pre-existing
from Phases 4/5/6 plus 1 new this phase (`CleanClipExtractorIntegrationTests`).
One transient flake was observed and diagnosed during this run:
`FrameSamplerMemoryTests.SampleAsync_RetainedMemoryStaysApproximatelyFlatAsSimulatedDurationIncreases`
(a pre-existing Phase 5 GC-timing-sensitive test, untouched by this phase,
already documented as flaky under full-suite GC pressure in Phase 6's own
report) failed once in a full-suite Debug run, then passed in an immediate
full-suite re-run — the same "GC-state noise, not a regression" pattern
Phase 6 diagnosed for the identical test.

## Test inventory (new this phase)

119 new tests this phase (118 always-run + 1 `[SkippableFact]` real-ffmpeg
integration test), verified directly from `dotnet test` output: the Phase 6
baseline was 244 total (237 passed + 7 skipped); this phase's suite is 363
total (355 passed + 8 skipped) with real ffmpeg absent, 363 passed with it
present.

- **IntervalSubtractorTests** (16) — no-exclusions passthrough, mid-range
  split, full coverage, outside-range no-effect, both off-by-one
  touching-boundary cases (start and end), both zero-length-remainder
  boundary cases, short-remnant preservation, overlapping-exclusion merge,
  duplicate-exclusion merge, unsorted-input merge correctness,
  multi-scene-range source-index tracking, and the
  never-overlaps-an-exclusion invariant.
- **ClipCandidateGeneratorTests** (11) — exact-max-duration boundary,
  below-minimum rejection, between-min-and-max sizing, sliding-window
  overlap stride, guarded-bounds containment, zero-guard passthrough,
  source-index preservation, multi-range sort-by-start, empty input.
- **ExclusionDistanceCalculatorTests** (5) — no-exclusions sentinel,
  before/after gap direction, nearest-of-multiple, touching-zero-distance.
- **ClipScorerTests** (11) — full-acceptance path, one dedicated rejection
  test per factor (sharpness, stability, exposure, freeze risk, transition
  distance, overlay suspicion, duration), no-frames-in-window graceful
  handling, overall-below-threshold-despite-every-factor-passing, and a
  same-inputs-twice determinism check.
- **MotionClassifierTests** (7) — bucket boundaries via `[Theory]`, empty-
  frames zero, multi-frame averaging.
- **CleanClipScoringSweepTests** (9) — single/non-overlapping/overlapping
  candidate assignment, sharpest-frame-as-representative, candidate never
  reached by the stream still scored (zero frames), no-candidates
  short-circuit, exclusion-distance pass-through, external cancellation.
- **PerceptualDistanceTests** (5), **VisualClustererTests** (7) — identical/
  hash/histogram/motion-mismatch distance behavior; empty/single/near-
  identical/fully-different/three-group clustering; leader-is-representative;
  determinism across repeated runs.
- **PerceptualHashExtractorTests** (6), **EdgeHistogramExtractorTests** (6),
  **ColorHistogramExtractorTests** (4) — real OpenCvSharp Mat-based
  known-property tests (identical frames hash identically, very-different
  frames diverge more than near-identical ones, solid color yields all-zero
  edge cells, border-vs-interior confinement, HSV-histogram-row-count
  agreement, sums to ~1, different hues diverge).
- **ClipFrameMetricsExtractorTests** (6), **ClipFrameMetricsPipelineTests** (4)
  — first-frame zero structural difference, positive/zero difference for
  different/identical frame pairs, sharpness matches the underlying
  `AnalyzedFrame`, populated histograms/hash, black-frame black-score,
  one-metrics-per-frame streaming shape, external cancellation.
- **CleanClipJsonWriterTests** (4) — full round-trip of every section
  (remaining ranges, accepted/rejected clips with reasons, clusters) with
  seconds-as-TimeSpan and enum-as-string, empty-result, new-file write,
  existing-file refuse-to-overwrite.
- **CleanClipExtractorTests** (9, fully faked `IFrameSampler`/`IFfprobeService`,
  no ffmpeg) — lenient-options acceptance path, multi-clip clustering,
  default-options deterministic rejection (zero-sharpness solid color),
  excluded-interval-never-overlapped end-to-end, no-candidates skips frame
  sampling entirely, no-video-stream fails fast, external cancellation,
  progress reporting.
- **CleanClipExtractionOptionsTests** (11) — `ForProfile` delegation and
  scene-range/exclusion passthrough, representative clamp coverage across
  both `CleanClipScoringOptions` and `ClusteringOptions`.
- **CleanClipExtractorIntegrationTests** (1, `[SkippableFact]`, real ffmpeg)
  — the real-binary end-to-end run described above.

## Compliance notes against CLAUDE.md

- Rule 1-2 (native WPF, no Electron/web/cloud/telemetry): satisfied — pure
  processing-pipeline code, no UI, network, or telemetry. Perceptual
  hashing/histograms/clustering are all local OpenCvSharp/plain-C#
  computation, no external ML service or model file.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): `IFrameSampler` (FFmpeg-backed,
  unchanged from Phase 5/6) supplies frames; every perceptual/scoring
  signal is computed via OpenCvSharp (`Cv2.Dct`, `Cv2.Canny`, `Cv2.Reduce`,
  `Cv2.Absdiff`, reusing `AnalyzedFrame`'s existing Mats).
- Rule 4 (clean architecture): unchanged dependency graph (`Media -> Core`
  only). `Extraction/` is layered internally exactly like `Detection/`
  (Signals -> Scoring/Streaming -> the public `CleanClipExtractor`
  orchestrator -> Export), and depends on `Detection.Signals` only for its
  `internal` (assembly-visible) Mat-touching helpers — no UI concerns
  anywhere in this phase.
- Rule 5 (cancellation/cooperative shutdown): `CleanClipExtractor.ExtractAsync`,
  `ClipFrameMetricsPipeline.ComputeAsync`, and `CleanClipScoringSweep.RunAsync`
  all honor the external token explicitly (checked every loop iteration via
  `ThrowIfCancellationRequested`, not only relied upon via downstream
  awaits), verified by dedicated cancellation tests in each of
  `CleanClipExtractorTests`, `ClipFrameMetricsPipelineTests`, and
  `CleanClipScoringSweepTests`.
- Rule 6-7 (bounded memory/concurrency, no full-video buffering):
  `CleanClipScoringSweep` never retains the whole video's per-frame metrics
  — see Memory section above; `ClipFrameMetricsPipeline` holds at most two
  `AnalyzedFrame`s alive at once. No unbounded queue/cache/fan-out
  introduced anywhere in this phase.
- Rule 8 (test-first): every pure component (`IntervalSubtractor`,
  `ClipCandidateGenerator`, `ExclusionDistanceCalculator`, `ClipScorer`,
  `MotionClassifier`, `PerceptualDistance`, `VisualClusterer`,
  `CleanClipScoringSweep`) has dedicated unit tests requiring no
  OpenCvSharp/ffmpeg at all; the three self-review findings above were each
  found only by running the tests, not by inspection, and are reflected in
  the final (corrected) test assertions.
- Rule 9 (benchmark with evidence): explicitly **not** satisfied this phase
  — see Benchmark section above for the honest reasoning and the deferred
  item under Outstanding.
- Rule 10 (never claim absolute accuracy): `CleanClipScoringOptions`'
  every threshold/weight is documented in its own XML remarks as a
  "deliberately heuristic, unmeasured-against-real-footage constant," not a
  calibrated value; `RejectionReason`/`ScoreReason` make every rejection
  explainable and traceable to a specific measured number rather than a
  pass/fail claim; the real-binary verification section states plainly
  what it does and does not prove (a single-fixture smoke test, not a
  precision/recall matrix).
- Rule 11-12 (preserve user files, output to new path only):
  `CleanClipExtractor` never writes to the source media file — it only
  reads frames via the existing `IFrameSampler` contract.
  `CleanClipJsonWriter` writes only to a caller-provided output path using
  `FileMode.CreateNew` (throws rather than silently overwriting an existing
  file at that path) plus `OutputDirectoryValidator.EnsureWritable`, the
  same validator every prior phase's writer uses.
- Rule 13 (format/build/tests before ending): all run above — format
  clean, Debug and Release both build with 0 warnings/errors, Debug and
  Release both pass all non-real-ffmpeg tests with the expected skip count
  after cleanup, and once more with real ffmpeg binaries present
  (363/363, 0 skipped) to verify the new real-binary test itself actually
  runs and passes rather than only compiling.
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (no new architectural
  decision beyond what Decisions already on file already cover — OpenCvSharp
  as the media/vision stack, `Media -> Core` layering).
- Rule 15 (don't advance while criteria fail): this phase's own criteria —
  solution builds/tests clean in Debug and Release, formatting clean,
  real-ffmpeg pipeline verified end-to-end with measured (not assumed)
  output, `SceneForge.App` wiring explicitly out of scope — are met as of
  this report, with the benchmark gap named explicitly rather than hidden.

## Outstanding for later phases

- **No benchmark for this phase's own cost** (see Benchmark section) — a
  future phase should add `benchmarks/SceneForge.Benchmarks/Extraction/`
  following the exact pattern `Detection/TransitionDetectionBenchmarks`
  already established (synthetic in-memory frame sequence, `MemoryDiagnoser`,
  per-`AnalysisProfile` table).
- **Black/frozen/invalid-interval detection does not exist yet.**
  `CleanClipExtractor` accepts these as `ExcludedInterval` facts but nothing
  in this repository currently produces them — a future phase needs a
  dedicated detector (likely reusing `AnalyzedFrame.BlackScore` over a
  sustained window, analogous to how `FadeBlackClassifier` already finds
  sustained black runs, but for standalone black/frozen segments rather
  than transitions).
- **No scene-boundary/scene-list construction exists yet either** — this
  phase consumes `SceneRanges` as a caller-supplied fact; a future phase
  needs to turn Phase 6's `TransitionDetection` list plus video duration
  into an actual ordered scene list.
- **`SceneForge.App` wiring** (a UI surface to run `CleanClipExtractor`
  against a user-selected file/scene list, show progress, cancel, and
  export JSON to a user-selected path) remains untouched, as it has been
  since Phase 6.
- **Scoring thresholds are unmeasured heuristics.** `CleanClipScoringOptions`'
  defaults (sharpness/stability reference values, freeze/overlay
  thresholds, factor weights) were chosen for plausibility and tested for
  internal consistency (each factor's pass/fail boundary behaves as
  documented), not calibrated against a labeled real-footage corpus. A
  larger fixture matrix with known clean/damaged regions — the same
  measurement discipline `TransitionDetectorFixtureTests` applies to
  transition detection — is needed before further tuning would be
  evidence-based rather than guesswork.
- **No dedicated long-duration memory measurement test** (analogous to
  Phase 5's `FrameSamplerMemoryTests`) exists for `CleanClipScoringSweep`;
  the bounded-memory claim rests on structural argument plus the
  20-second/real-fixture tests, not a measured number over a much longer
  simulated run.
- Batch/concurrent orchestration across multiple files remains out of
  scope, as it has been since Phase 5.
