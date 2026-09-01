# Detection Reporting Audit — "10,917 Transitions Detected"

Investigates a production report that, after the clean-clip-retention
buffer-tightening (`docs/CLEAN_CLIP_RETENTION_AUDIT.md`), the app displayed
"10,917 transitions detected" for a single video - not physically plausible,
and initially suspected to mean transition *detection* itself had regressed.
Per CLAUDE.md rule 9, every number below is real, measured evidence from the
real `TransitionDetector`/`CleanClipExtractor` pipeline, on real ffmpeg-
decoded video - never an estimate.

## Root cause

**Detection sensitivity/thresholds were not altered.** The buffer-tightening
work changed exactly two fields, both in `CleanClipScoringOptions`
(`src/SceneForge.Media/Extraction/CleanClipExtractionOptions.cs`):
`MinClipDuration` (3.0s → 2.0s) and `TransitionSafeDistance` (2.0s → 0.5s).
Both live in the *Extraction* layer and are consumed only after detection
has already produced its final result - they cannot change what
`TransitionDetector` detects. `git diff` against the pre-buffer-tightening
commit confirms zero changes to any file under `Detection/Classification/`,
`Detection/Fusion/`, `TransitionDetector.cs`, or `TransitionDetectionProfile.cs`.
Confirmed independently: the Phase 12 accuracy-regression gate
(`dotnet run --project accuracy/SceneForge.Accuracy -- gate --baseline accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json`)
**passes with metrics byte-identical to the committed baseline** - same
Recall/Precision/F1/BoundaryError/FalsePositivesPerMinute, per fixture
group, to the decimal.

**The actual defect: `AnalysisProgressViewModel` displayed the wrong
metric, and never corrected it.** `TransitionDetectionProgress.RawCandidatesSoFar`
is the *raw, pre-fusion* candidate count - every one of the 7 classifiers
re-scans its own sliding window on *every sampled frame*, and
`TransitionFuser` is the step (run once, only after the whole stream
finishes) that collapses this into the real, deduplicated
`TransitionDetection` list (see `TransitionFuser`'s own remarks: "the raw
candidate stream... by design, can contain many exact or overlapping
duplicates"). `AnalysisProgressViewModel.RunAsync` bound its "Transitions
found" UI label directly to this raw count
(`TransitionsFound = p.RawCandidatesSoFar;`) and - unlike `ClipsAccepted`/
`ClipsRejected`, which *are* corrected to their real final values once
extraction completes - never corrected `TransitionsFound` to the real,
fused count once `DetectAsync` returned. Whatever inflated number was
showing at the last progress tick is what stayed on screen.

Measured directly (real ffmpeg, real `TransitionDetector`, a real 3-minute
960x540 video):

| Metric | Value |
|---|---:|
| `RawCandidatesSoFar` (what the UI showed) | **4,004** |
| Real, fused `TransitionDetector.DetectAsync` result (what actually drives exclusion) | **109** |

A 3-minute video producing a raw count in the thousands, growing at
roughly 1,300/minute, fully explains a report of "10,917" on a longer or
busier real video - with zero need for any detection defect.

## Why Phase 6/12 didn't catch it, and why they couldn't have

Neither test suite exercises `AnalysisProgressViewModel` or
`TransitionDetectionProgress.RawCandidatesSoFar` at all:

- `TransitionDetectorFixtureTests` (Phase 6, `SceneForge.Media.Tests`) and
  the Phase 12 accuracy console tool both test only
  `TransitionDetector.DetectAsync`'s **return value** - a different
  project (`SceneForge.Media`) with no dependency on the `SceneForge.App`
  ViewModel layer at all. The bug lived entirely in how the app layer
  *displayed* a progress-reporting field, which is architecturally outside
  what either suite could ever reach.
- Separately, `TransitionDetectorFixtureTests`'s own assertion
  (`Assert.True(failures.Count == 0, ...)`) checked only `recall > 0` per
  type - **zero assertion on false positives**. It printed
  `falsePositives.Count` but never gated on it, so even a genuine
  false-positive explosion in the *fused* count would have passed silently.
  (The separate Phase 12 `RegressionGate` *does* check
  `FalsePositivesPerMinute` against the committed baseline - but only
  across the fixture matrix's ~3-second clips, which cannot reveal how a
  false-positive rate scales with sustained, minutes-long content.)

## A real, pre-existing (not introduced by this work) classifier weakness, investigated but not fixed here

While isolating the true cause, real-ffmpeg testing surfaced a genuine,
independent weakness: `ZoomTransitionClassifier`/`DirectionalSwipeClassifier`
rely purely on optical-flow motion signature (magnitude + radial/directional
consistency), with no corroborating "did the content actually change to a
different scene" signal. Measured:

| Test content | Fused false Zoom/Swipe detections (3 min) |
|---|---:|
| Zero-motion solid-color segments + 5 real hard cuts | **0** (5/5 correct HardCuts, nothing else) |
| `testsrc2`-based segments (static camera) + 5 real hard cuts | **57** |
| `testsrc2` with continuous panning crop | **109** |
| Genuine camera pan across a static, richly-textured (non-`testsrc2`) image | **1** (in 90s) |

This isolates the effect to `ffmpeg`'s `testsrc2` lavfi source's own
internal calibration-pattern animation (present even with a static "camera")
being mistaken for motion - a synthetic-test-methodology artifact, not
necessarily representative of real footage. It matches Phase 6's
already-documented "0% recall, known weak spot" finding for these two
classifiers (`TransitionDetectorFixtureTests.RecallNotYetRequiredAboveZero`).
**Not fixed in this pass**: the evidence does not show this as the
dominant cause of "10,917" (the raw/fused display bug fully explains it on
its own), and classifier-threshold changes are a materially different,
higher-risk kind of change than what was reported broken - left as a
disclosed, measured, future-phase candidate rather than rushed in without
its own dedicated test-first investigation against real (non-synthetic)
footage.

## Fix

`AnalysisProgressViewModel.RunAsync`
(`src/SceneForge.App/ViewModels/AnalysisProgressViewModel.cs`): added
`TransitionsFound = detections.Count;` immediately after `DetectAsync`
returns - the same "correct the live estimate to the real final value"
pattern the method already used for `ClipsAccepted`/`ClipsRejected` after
`ExtractAsync`. The in-progress value (`RawCandidatesSoFar`, shown while
detection is still running) is left as a live activity indicator, now
documented as such in a code comment; only the value the user actually
reads once analysis completes is corrected.

## Regression tests added

In addition to (not replacing) the existing suite:

- `AnalysisProgressViewModelTests.Construction_RawCandidateProgressFarExceedsFinalResult_TransitionsFoundEndsUpAtTheRealFusedCount` -
  simulates the exact measured gap (`RawCandidatesSoFar=4004`,
  `Result.Count=5`) via an extended `FakeTransitionDetector`
  (`RawCandidatesSoFarOverride`) and asserts `TransitionsFound` settles on
  the real count, not the inflated one. Uses a new
  `ImmediateSynchronizationContext` test helper to make `Progress<T>`'s
  own asynchronous `Post` marshaling deterministic in a plain xUnit test
  (matching a real single-threaded UI dispatcher's ordering) rather than
  racing against the default `ThreadPool` fallback.
- `Construction_HappyPath_RunsPipelineSynchronouslyAndNavigatesToSceneReview`
  gained an assertion that `TransitionsFound` matches the real detection
  count in the already-existing happy-path scenario.
- `TransitionDetectorFixtureTests` (Phase 6): added a sanity ceiling
  (`MaxAcceptableTotalFalsePositives = 30`, current baseline measures 10 -
  generous headroom against run-to-run synthetic-fixture noise, tight
  enough to fail hard on a real explosion) as a fast, always-runs-with-
  `dotnet test` backstop, since the Phase 12 gate's own (better, per-group)
  FP check is not part of the normal test suite.

**Verified both new/strengthened assertions actually catch their
respective bugs**: reverted the `AnalysisProgressViewModel` fix and
confirmed `Construction_RawCandidateProgressFarExceedsFinalResult_...`
fails (`Expected: 5, Actual: 4004`); temporarily lowered
`MaxAcceptableTotalFalsePositives` to 5 and confirmed
`TransitionDetectorFixtureTests` fails (`Total false positives (10)
exceeded the sanity ceiling (5)`); restored both.

## Verification: both requirements true at once, on real footage

A real, combined `TransitionDetector` → `SceneRangeCalculator` →
`CleanClipExtractor` run (the same pipeline `AnalysisProgressViewModel`
drives), against a real 3-minute video (6 shots, 5 real hard cuts):

```
===== STEP 1: TRANSITION DETECTION =====
Fused transition count: 62 (DirectionalSwipe: 47, ZoomTransition: 10, HardCut: 5)

===== STEP 2: CLEAN-CLIP EXTRACTION =====
[OLD defaults (pre-buffer-tightening: 3.0s/2.0s)] Accepted clips: 4, footage: 00:00:20 (11.1%)
[NEW defaults (current: 2.0s/0.5s)]               Accepted clips: 19, footage: 00:01:29.9 (49.9%)

===== SUMMARY =====
Transitions found (real, fused - what the UI now shows): 62 - SANE (dozens to low hundreds range)
Clip retention: OLD=4 clips vs NEW=19 clips - IMPROVED
```

Both hold simultaneously: a sane, realistic transition count (what the
corrected UI metric now shows) **and** the clean-clip-retention improvement
(4.75x more accepted clips on the identical source) are both true at once,
as required.

## Full-suite verification

```
dotnet format SceneForge.sln --verify-no-changes
  -> No formatting violations.
dotnet build SceneForge.sln --configuration Debug
  -> Build succeeded. 0 Warning(s). 0 Error(s).
```

| Project | Before this pass | After |
|---|---:|---:|
| SceneForge.Core.Tests | 8 passed | 8 passed |
| SceneForge.Accuracy.Tests | 31 passed | 31 passed |
| SceneForge.App.Tests | 77 passed | 78 passed |
| SceneForge.Infrastructure.Tests | 46 passed | 46 passed |
| SceneForge.Media.Tests | 550 passed | 550 passed |

Net +1 test (`SceneForge.App.Tests`), zero regressions.
`TransitionDetectorFixtureTests` (Phase 6) and the Phase 12 accuracy gate
were both re-run against the final code and continue to pass with the
same, unchanged, byte-identical-to-baseline metrics.

## Limitations

- The Zoom/Swipe/testsrc2-artifact finding above is disclosed as a real,
  measured, pre-existing limitation, not fixed in this pass - see "A real,
  pre-existing... classifier weakness" above for why, and for the exact
  numbers a future pass would start from.
- The "sane count" verification used one real synthetic multi-shot video,
  not the reporter's original footage; the mechanism (raw vs. fused count)
  is unconditional and doesn't depend on video content, but the *specific*
  fused-count magnitude on genuinely different real content was not
  independently re-measured.
- Per CLAUDE.md rule 10: this does not claim transition detection is
  "accurate" in any absolute sense - only that the specific, confirmed
  defect (a UI layer displaying the wrong, pre-fusion metric) is fixed,
  and that detection itself is proven, by the passing Phase 12 gate, to be
  unchanged from its already-measured, already-baselined behavior.
