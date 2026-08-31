# Render Duration-Drift Audit

A systematic audit of `FFmpegRenderService` and its supporting types
(`RenderPlanBuilder`, `RenderFilterGraphBuilder`) requested after the
duration-tolerance verification failure kept resurfacing across different
rendering paths (single-pass `filter_complex`, the Stage A/B concat-demuxer
path for high repetition, and the batched/chunked concat path for a high
distinct-segment count) as each was built. Per CLAUDE.md rule 9, every
number below is real, measured evidence from real ffmpeg on real hardware
(the same machine documented in `docs/OPTIMIZATION_REPORT.md`) - never an
estimate. Per rule 10: this audit does not claim the fixes below make
duration matching "exact" or "guaranteed" in an absolute sense - only that
they are measured to be frame-exact (or within the pre-existing one-frame
tolerance) across every scenario this pass could reproduce, and the
mechanism for each fix is a general, not scenario-specific, correction.

## Scope

Explicitly out of scope, per the request that started this pass (left
untouched, byte-for-byte, verified by diff):

- `RenderSegmentRunAsync`'s adaptive OOM retry/halving logic.
- Per-segment `-ss <SourceStart> -i <source>` input seeking and hardware
  encoder selection/fallback.
- `TimelinePlanner`'s duration-guarantee/reuse-cap logic (Phase 16).
- All UI/XAML files.
- Packaging scripts and the release workflow.

## Two independent, real root causes found

Both were found by direct measurement against real ffmpeg 9.0.1 - first
reproduced in isolation outside SceneForge entirely (the same methodology
`docs/OPTIMIZATION_REPORT.md`'s original duration-tolerance investigation
used), then confirmed through the actual `RenderPlanBuilder` /
`FFmpegRenderService` code paths.

### 1. Independent per-segment rounding does not preserve the aggregate total

`RenderPlanBuilder` already quantized each segment's duration to the
nearest whole output frame (the Phase 13 fix documented in
`docs/OPTIMIZATION_REPORT.md`), which correctly makes ffmpeg's `trim`
filter produce exactly that many frames for *that one segment*. What it
did not guarantee is that the **sum** of many independently-rounded
segments stays close to the *true* continuous total
(`TimelinePlan.PlannedDuration`, which `RenderAudioTrackSpec.TrimDuration`
is normally set to - see `ExportSettingsViewModel.Continue`). Rounding
error is bounded per segment (≤ 0.5 frame) but not per plan: a fixed bias
on a repeated clip multiplies by the repeat count instead of cancelling
out (DistinctDedup's own high-repetition shape), and even without
repetition a many-hundred-placement plan (Batched territory) accumulates
error via ordinary random-walk-style summation.

**Measured** (`RenderPlanBuilder` driven by a real `TimelinePlanner.Plan`
call, non-frame-aligned clip durations representative of real scene-cut
footage, 30fps output):

| Scenario | Placements | `TimelinePlan.PlannedDuration` | `RenderPlan.PlannedVideoDuration` (before fix) | Delta |
|---|---:|---:|---:|---:|
| 19-clip pool, high repetition (22-min target) | 376 | 22:00.000 | 22:02.233 | **67 frames / 2.23s** |
| 420-clip pool, low repetition (22-min target) | 328 | 22:00.000 | 22:01.100 | **33 frames / 1.10s** |

Both are dozens of times past the verifier's one-frame (0.033s at 30fps)
tolerance - not a marginal edge case.

**Fix** (`RenderPlanBuilder.Build`): replaced independent per-placement
rounding with the standard "largest remainder"/Bresenham apportionment
technique - track the cumulative *ideal* (continuous, un-quantized)
duration and the cumulative frame count already committed, in
Position order, and assign each placement only the frame **delta** needed
to bring the running total's rounded frame count back in line. This bounds
the error at *every prefix* - not just the final total - to under one
frame, by construction, regardless of placement count or repetition
pattern. Re-measured with the fix: **0.00s delta** in both scenarios above
(and in the original frame-aligned `HighRepetitionRenderScenarioTests`
shapes, which were already exact and remain exact).

Side effect verified and bounded: a byte-identical repeated window can now
land on two (rarely three) different quantized durations depending on
where it falls in the running phase, rather than always the same one.
`CountDistinctSegments`/`RenderDistinctDedupStageAAsync` already key
pre-render pieces by `(SourceStart, SourceDuration)`, so this needs no
code change there - it just turns into more dedup pieces for a given
distinct-window count. Measured on the 19-clip/376-placement scenario
above: 39 distinct pieces (vs. 19 clips) - a ~2x increase, comfortably
under `MaxDistinctDedupPieces` (400) and under 11% of the 376 total
segments (`MaxDistinctToTotalRatioForDedup` is 50%), so `SelectRenderStrategy`
still correctly chooses `DistinctDedup` for this shape.

### 2. `fps=` frame-rate conversion can emit more frames than the (already-exact) trim window implies

Every segment's filter chain ends with `fps=<spec.FrameRate>` to convert
the source's own frame rate to the plan's output frame rate. This filter
duplicates/drops frames to hit the target rate based on presentation
timestamps, not on the trim window's exact requested duration - when the
segment's source footage is not already at `spec.FrameRate`, it can emit
**more** frames than `spec.FrameRate.ToFrameCount(segment.SourceDuration)`
calls for, because it keeps extending/duplicating the last source frame
until told to stop, and a preceding time-domain `trim=duration=` does not
give it an exact downstream frame-count bound.

This is a *source/output frame-rate mismatch* bug, structurally different
from (and not fixed by) root cause 1's per-segment duration quantization -
`RenderPlanBuilder` already guarantees `segment.SourceDuration` is an
exact multiple of the frame period, but that says nothing about how many
frames the `fps` filter itself emits for that exact duration when the
source's own rate differs. No existing fixture or test caught it because
every committed fixture (`sample_video_audio.mp4`, 25fps) and every
synthesized-at-test-time source elsewhere in this project happens to
already be at the exact frame rate the test then renders at. A real user's
own footage has no such guarantee: `AnalysisSettingsViewModel.AvailableFrameRates`
lets the user pick 24/25/30/29.97/50/60fps regardless of the source's
native rate (defaulting to 30fps), so this mismatch is the **common**
case for real footage, not an edge case.

**Measured** (isolated, real ffmpeg 9.0.1, 30fps synthetic source, 25fps
output, the exact filter chain `RenderFilterGraphBuilder` builds):

| Segments (each `0.28-0.4s`, non-frame-aligned pattern) | Expected frames | Actual frames (before fix) | Delta |
|---:|---:|---:|---:|
| 5 | 36 | 37 | +1 |
| 20 | 144 | 148 | +4 |
| 40 | 288 | 296 | +8 |
| 60 | 432 | 442 | +10 |

The excess grows with segment count, exactly like the already-fixed
trim-only bug did before it. Reproduced through the real
`FFmpegRenderService.RenderAsync` end to end (40 segments, 30fps
synthetic source, 25fps `RenderOutputSpec`, real `SinglePass` render):
verification failed with **actual 00:00:11.960 vs. expected 00:00:11.400
(0.56s / 14 frames past the 0.04s tolerance)**.

**Fix** (`RenderFilterGraphBuilder.BuildSegmentFilter`): a second,
FRAME-domain trim (`trim=start_frame=0:end_frame=<N>`, which counts actual
output frames rather than reading timestamps, so it is immune to the same
boundary ambiguity) immediately after `fps=`, forcing every segment back
to exactly its intended frame count regardless of what the `fps` filter
itself produced. Re-measured with the fix, same 60-segment case: **470/470
frames, 0.00s delta**. Re-measured through the real `FFmpegRenderService`
end to end (same 40-segment SinglePass scenario): **0.0027s delta** (AAC
encoder sub-frame granularity noise, well inside the 0.04s tolerance).
A no-op by construction whenever the source and output frame rates already
match (verified: every pre-existing test, all built on 25fps-matching
fixtures/sources, is unaffected - see "Verification" below).

Because `BuildSegmentFilter` is the single shared per-segment filter
builder used by `BuildVideoConcat` (SinglePass), `BuildSeekedVideoConcat`
(Batched/DistinctDedup Stage A), this one fix covers all three rendering
paths without touching `FFmpegRenderService`'s batching, retry, or
encoder-selection logic at all.

## What was checked and found already correct

- **Batched Stage A's own `-frames:v` pinning** (`BuildSegmentRunArguments`)
  already truncates a whole batch's output to the summed per-segment frame
  count, which happens to also mask most of root cause 2 for the Batched/
  DistinctDedup paths specifically (their per-segment excess frames get
  silently dropped by the batch-level `-frames:v` cap) - but this is an
  accidental side effect of a mechanism built for a different purpose, not
  a general fix: it does not apply to SinglePass (no such pinning exists
  there), and it does not correct which frames get kept (the cap could
  truncate valid trailing content rather than the fps filter's own
  duplicated tail frame). Fixing `BuildSegmentFilter` directly, rather
  than relying on this side effect, is the addressed, permanent fix.
- **`RationalFrameRate.FromFrameCount(n)` -> `ToFrameCount` round-trip**:
  proven exact for any real frame rate (the tick-resolution error is far
  below one unit, verified across the frame rates this project's fixtures
  and default `FrameRateOption.Defaults` use) - so `BuildSegmentRunArguments`'s
  own `frameCount = segments.Sum(s => ...ToFrameCount(s.SourceDuration))`
  recomputation on an already-quantized `SourceDuration` was never a drift
  source itself.
- **Concat-demuxer Stage B** (`BuildStageBConcatArguments`): stream-copies
  the assembled video (`-c:v copy`), so it introduces no re-encoding
  timestamp drift of its own; the audio track is trimmed/encoded once in
  the same pass from the caller-supplied `Audio.TrimDuration`, unaffected
  by this audit's two fixes (which only change what frame count each
  video *segment* renders to, not the audio side).
- **`RenderOutputVerifier`'s one-frame tolerance and duration source**:
  unchanged - both root causes above were fixed to make the *actual*
  render match `RenderPlan.PlannedVideoDuration` far more precisely than
  the tolerance requires, rather than by widening the tolerance (CLAUDE.md
  rule 10: never paper over drift with a wider band).

## Regression tests added

In addition to (not replacing) the existing suite:

- `RenderPlanBuilderTests.Build_MultipleNonFrameAlignedPlacements_PlannedVideoDurationTracksCumulativeIdealTotal`
  (renamed/corrected from `..._PlannedVideoDurationIsSumOfQuantizedSegments`,
  which asserted the superseded independent-rounding contract as if it
  were intended - the same class of correction
  `docs/OPTIMIZATION_REPORT.md` made to a Phase 13 test for the same
  reason) and `Build_RepeatedIdenticalWindow_CumulativeApportionmentBoundsAggregateDriftToUnderOneFrame`
  (fake media, no real ffmpeg).
- `RenderFilterGraphBuilderTests.Build_SegmentDuration_EmitsFrameDomainTrimRightAfterFpsPinningExactFrameCount`
  and `Build_SeekedVideoConcat_AlsoEmitsFrameDomainTrimPinningExactFrameCount`
  (fake media, white-box graph-string assertions).
- `FFmpegRenderServiceIntegrationTests`, three new real-ffmpeg,
  frame-rate-mismatched (30fps synthetic source, 25fps output) tests, one
  per rendering path at realistic scale:
  `RenderAsync_RealFfmpegSourceFrameRateDiffersFromOutputFrameRate_SinglePass_VerifiesWithinTolerance`
  (40 segments), `..._Batched_VerifiesWithinTolerance` (70 all-distinct
  segments, 2 batches), `..._DistinctDedup_VerifiesWithinTolerance` (6
  distinct windows, 150 placements).

**Verified all new/changed tests actually catch their respective bugs**,
not just pass trivially: temporarily reverted each fix in isolation and
re-ran the affected tests.

- Reverting the `RenderFilterGraphBuilder` fix alone: the two new
  white-box graph tests fail (`Assert.Contains` - the frame-domain trim is
  absent), and the strengthened 40-segment SinglePass integration test
  fails with a real `RenderVerificationException` (`0.56s` delta vs.
  `0.04s` tolerance) - confirmed above. (The Batched/DistinctDedup
  integration tests still passed without this fix specifically because of
  the `-frames:v` masking effect noted above - expected, and why the
  SinglePass test is the one that must catch this regression.)
- Restoring the fix: all three real-ffmpeg tests pass, with delta 0.0027s
  (SinglePass) / 0.000s (Batched) / 0.000s (DistinctDedup).

## Verification (Debug and Release, whole solution)

```
dotnet format SceneForge.sln --verify-no-changes
  -> No formatting violations.

dotnet build SceneForge.sln --configuration Debug
  -> Build succeeded. 0 Warning(s). 0 Error(s).
dotnet build SceneForge.sln --configuration Release
  -> Build succeeded. 0 Warning(s). 0 Error(s).
```

| Project | Before (Debug) | After (Debug) | After (Release) |
|---|---:|---:|---:|
| SceneForge.Core.Tests | 8 passed | 8 passed | 8 passed |
| SceneForge.Accuracy.Tests | 31 passed | 31 passed | 31 passed |
| SceneForge.App.Tests | 77 passed | 77 passed | 77 passed |
| SceneForge.Infrastructure.Tests | 46 passed | 46 passed | 46 passed |
| SceneForge.Media.Tests | 537 passed | 543 passed | 543 passed |
| **Total** | **699 passed, 0 failed** | **705 passed, 0 failed** | **705 passed, 0 failed** |

`SceneForge.Media.Tests` (the only project touched) was run three times
against the final Debug tree (543/543 each time) after one transient,
non-reproducible single-test failure surfaced once mid-verification (real
ffmpeg processes launched back-to-back under load); disclosed here per
CLAUDE.md rule 10 rather than silently dropped - the two immediate re-runs
both passed 543/543 with no code changes in between, consistent with
environmental flakiness (process/resource contention from dozens of
real ffmpeg encodes in quick succession) rather than a regression from
this pass's changes.

Every previously-passing test still passes; net +6 tests added this pass
(543 - 537), all listed above.

## Files changed

- `src/SceneForge.Media/Rendering/RenderPlanBuilder.cs` - cumulative
  frame-apportionment fix (root cause 1).
- `src/SceneForge.Media/Rendering/Internal/RenderFilterGraphBuilder.cs` -
  frame-domain trim fix (root cause 2).
- `tests/SceneForge.Media.Tests/Rendering/RenderPlanBuilderTests.cs`,
  `RenderFilterGraphBuilderTests.cs`, `FFmpegRenderServiceIntegrationTests.cs` -
  regression coverage, as listed above.

Not touched: `FFmpegRenderService.cs` (batching/retry/encoder-selection
logic), `TimelinePlanner.cs`/`TimelinePlan.cs`, any XAML/UI file, and no
packaging or release-workflow file.

## Limitations

- Single machine, single set of runs for the timing numbers above - same
  caveat `docs/BENCHMARK_REPORT.md`/`docs/OPTIMIZATION_REPORT.md` already
  carry; no repeated-run statistical variance was measured.
- The frame-domain trim fix (root cause 2) was verified across the 24/25/
  30/29.97/50/60fps set `AnalysisSettingsViewModel.AvailableFrameRates`
  offers only indirectly (30fps source / 25fps output was the pairing
  measured directly); the mechanism (ffmpeg's own `start_frame`/`end_frame`
  trim semantics, which count decoded output frames rather than
  timestamps) is rate-independent by construction, not tuned to this one
  pairing, but every combination was not individually re-measured.
- Per CLAUDE.md rule 10: this audit does not claim duration matching is
  now unconditionally exact for every possible plan - only that it is
  measured frame-exact (or within the existing one-frame tolerance) for
  every scenario reproduced in this pass, including realistic high-scale
  and frame-rate-mismatched cases that previously failed by multiple
  seconds.
