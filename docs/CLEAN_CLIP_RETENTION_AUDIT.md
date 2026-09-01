# Clean-Clip Retention Audit

Investigates a real production report: a 320-scene source video yielded
only 18-19 usable clips after `CleanClipExtractor`. Per CLAUDE.md rule 9,
every number below is real, measured evidence - from the real
`ClipCandidateGenerator`/`ClipScorer`/`CleanClipExtractor` pipeline, on
real ffmpeg-decoded frames where noted - never an estimate. Per rule 10:
the fix below does not remove or weaken the "never include a contaminated
frame" guarantee - that guarantee is enforced by `IntervalSubtractor`'s
interval math, independent of and untouched by either changed value.

## Root cause

`CleanClipExtractor`'s pipeline is `IntervalSubtractor` (hard-cuts
excluded/transition intervals out of the scene ranges - candidates are
generated only from what remains, so a contaminated frame can never reach
a candidate by construction) → `ClipCandidateGenerator` (trims a
`BoundaryGuard` off both ends of what's left, then slides 2-5s windows) →
`ClipScorer` (a 7-factor accept/reject gate, one factor of which is
`TransitionDistance`).

The defect: `ClipScorer`'s `TransitionDistance` factor required a
candidate be at least `MinAcceptableFactorScore (0.3) x TransitionSafeDistance
(2.0s) = 600ms` from the nearest excluded interval just to avoid automatic
rejection on that one factor alone - **regardless of footage quality**.
But `ClipCandidateGenerator`'s own `BoundaryGuard` only ever pushed a
candidate 250ms away from that same boundary - **less than half** what the
scoring gate demanded. Every remaining range short enough to produce
exactly one full-width candidate (any scene not much longer than
`MaxClipDuration`, 5s) places that candidate touching both guarded edges,
so it was **always** rejected on `TransitionDistance` alone, no matter how
sharp, stable, or well-exposed the footage was.

Verified directly against the real `ClipCandidateGenerator`/
`ExclusionDistanceCalculator`/`ClipScorer` pipeline, feeding it synthetic
*ideal-quality* frames (maximum sharpness/stability/exposure) at varying
gap lengths between two exclusions, with the old defaults:

| Gap between transitions | Any clip accepted, with perfect footage? |
|---|---|
| 0.5s – 8.25s | **No — rejected every time**, purely on `TransitionDistance` |
| 8.5s+ | First becomes possible |

**No scene shorter than 8.5 seconds could ever produce a clip, regardless
of footage quality.**

## Why 2.0s was not evidence-based, and 0.5s is

`CleanClipScoringOptions`'s own comment already disclosed these were
"deliberately heuristic starting points, not calibrated against measured
real-footage data" (`docs/PHASE_07_REPORT.md`). This codebase has real
measured evidence for the actual contamination width sitting right next
to it: `accuracy/SceneForge.Accuracy/Fixtures/SyntheticFixtureCatalog.cs`
(the ground-truth generator behind `docs/ACCURACY_REPORT.md`'s own
accuracy baseline) builds every fade/dissolve/blur transition with
`TransitionDuration = 0.5s` - the actual designed width of a contaminated
zone. `TransitionDetectionProfile.PreBufferDuration`/`PostBufferDuration`
(100ms each) are already folded into every excluded interval before
`CleanClipExtractor` ever sees it. So a candidate sitting at the
`BoundaryGuard` edge (250ms) is already ~350ms past the real transition
edge - comfortably beyond the detector's own mean boundary error (149ms
aggregate, `docs/ACCURACY_REPORT.md`). There is no evidence anywhere in
this codebase for needing another 600ms-2000ms of clearance on top of
that.

## Quantified impact, on a realistic 320-scene distribution

A synthetic 320-scene length distribution (median 2.57s, p75 4.17s - a
plausible fast-cut source) run through the real scoring pipeline (ideal
footage, isolating the geometry/scoring-threshold effect):

| Configuration | Accepted clips | Footage used | % of clean footage |
|---|---:|---:|---:|
| **Old defaults** (MinClipDuration=3s, TransitionSafeDistance=2s) | **17** | 85.0s | **7.9%** |
| `TransitionSafeDistance` → 0.5s only | 119 | 521.0s | 48.4% |
| + `MinClipDuration` → 2.0s (**the fix shipped**) | 174 | 658.4s | 61.2% |

The "old defaults" row (17 clips) independently reproduces the reported
real-world symptom (18-19 clips) almost exactly, on an unrelated synthetic
distribution - strong confirmation this was the actual mechanism, not a
coincidental side issue. Total clean footage available in this scenario
was 1075s (~18 min); the old defaults used under 8% of it.

## Fix

`src/SceneForge.Media/Extraction/CleanClipExtractionOptions.cs`,
`CleanClipScoringOptions` defaults:

| Field | Old | New |
|---|---:|---:|
| `TransitionSafeDistance` | 2.0s | **0.5s** |
| `MinClipDuration` | 3.0s | **2.0s** |
| `BoundaryGuard` | 250ms | unchanged (already proportionate to the ~149ms mean measured detector boundary error) |
| everything else | unchanged | unchanged |

`MinClipDuration` was also lowered - separately from the `TransitionDistance`
fix - because even with `TransitionSafeDistance` corrected, the geometric
floor (`MinClipDuration + 2 x BoundaryGuard`) still discarded any scene
shorter than 3.5s outright (the exact "a 2-second clean segment between
two transitions is entirely discarded" case reported). 2.0s keeps the
still-real product intent ("prefer a few-second clip") while no longer
silently deleting genuinely usable short clean segments; both new values
remain within the existing clamp ranges (`MinAllowedClipDuration=1s`,
`MaxAllowedTransitionSafeDistance=30s`), so no clamp bound needed to
change.

Both changes are **defaults only** - `CleanClipScoringOptions` remains
fully overridable via `with { ... }` for any caller that wants different
behavior, exactly as before.

## What was checked and left alone

- **The correctness guarantee.** `IntervalSubtractor.Subtract` is pure
  interval math: a candidate is only ever generated from what remains
  *after* every excluded interval has been cut out. Neither
  `TransitionSafeDistance` nor `MinClipDuration` participates in that
  subtraction - they only affect which of the already-clean remainders get
  *scored as acceptable*. Lowering them cannot let a contaminated frame
  into an accepted clip; it only reduces a redundant extra margin on top
  of the hard exclusion.
- **`BoundaryGuard`** (250ms) - left unchanged; already proportionate to
  measured detector boundary error, and not implicated by the geometric
  analysis above (the mismatch was between the guard and the *scoring*
  threshold, not the guard itself).
- **Every other `CleanClipScoringOptions` field** (Sharpness/Stability/
  Exposure/FreezeRisk/OverlaySuspicion references and weights,
  `AcceptanceThreshold`, `OverlapFraction`, `MaxClipDuration`) - unchanged;
  no evidence gathered this pass implicates them, and they were out of
  scope for the reported symptom.

## Regression tests added

In addition to (not replacing) the existing suite:

- `ClipScorerTests.Score_ExactlyBoundaryGuardDistanceFromExclusion_PassesTransitionDistanceUnderCurrentDefaults`
  and `..._WouldHaveFailedUnderThePreviousDefaults` (fast, no real ffmpeg) -
  pin the exact numeric mismatch this audit found and its fix, both
  directions.
- `CleanClipRetentionIntegrationTests.ExtractAsync_RealFfmpegMultiTransitionSource_NewDefaultsRetainSignificantlyMoreCleanFootage_AndNeverIncludeContaminatedFrames`
  (real ffmpeg): builds a real synthetic source with five real 0.5s
  fade-to-black-and-back transition zones (matching
  `SyntheticFixtureCatalog.TransitionDuration`) separating clean scenes of
  deliberately mixed length (2.6s/9.0s/2.7s/1.4s/6.0s), runs the real
  `CleanClipExtractor.ExtractAsync` twice against the same real source (old
  defaults vs. new), and asserts:
  - New defaults retain strictly more clips and at least 50% more footage
    than old (measured: **5 clips / 19.0s vs. 1 clip / 5.0s** - a 3.8x
    footage, 5x clip-count improvement on this specific real source).
  - The two motivating short scenes (2.6s, 2.7s) produce an accepted clip
    under the new defaults and **none** under the old ones.
  - The deliberately-too-short-even-for-the-new-floor scene (1.4s)
    produces nothing under **either** configuration - confirming this fix
    relaxes a disproportionate margin, not the floor itself.
  - Not one accepted or rejected clip, under either configuration, ever
    overlaps a real transition zone - the correctness guarantee, checked
    against real ffmpeg-decoded output.

Existing tests were audited for dependence on the old default values
before changing them: `ClipScorerTests`/`CleanClipScoringSweepTests` use
`CleanClipScoringOptions.Default` but none of their assertions depend on
the specific old numbers (verified line-by-line: the one candidate
duration used, `[10,14]` = 4s, stays within both the old `[3,5]` and new
`[2,5]` bounds; the one too-short-duration case, 0.5s, stays below both
old 3s and new 2s minimums). `ClipCandidateGeneratorTests`,
`CleanClipExtractorTests`, and `CleanClipExtractorIntegrationTests` all
construct `CleanClipScoringOptions` explicitly rather than using
`.Default`, so they were unaffected by construction.

## Verification

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
| SceneForge.App.Tests | 77 passed | 77 passed |
| SceneForge.Infrastructure.Tests | 46 passed | 46 passed |
| SceneForge.Media.Tests | 543 passed | 546 passed |

Net +3 tests (546 - 543), all listed above, zero regressions.

## Limitations

- The 320-scene distribution used to quantify impact is a synthetic model
  (log-normal-ish scene lengths, mean ~3.5s), not the reporter's actual
  source video - it independently reproduces the reported symptom's order
  of magnitude (17 clips vs. the reported 18-19) but is not a replay of
  the exact real file.
- The real-ffmpeg regression test uses one specific mix of five scene
  lengths and four transitions on one synthetic source; it demonstrates
  the mechanism and a real, substantial improvement, not a claim that
  every real video will see this exact 3.8x/5x ratio.
- Per CLAUDE.md rule 10: this is not a claim that clip acceptance is now
  "correct" in any absolute sense - `Sharpness`/`Stability`/`Exposure`/
  `FreezeRisk`/`OverlaySuspicion` thresholds remain the same
  un-recalibrated heuristic starting points `docs/PHASE_07_REPORT.md`
  always disclosed them as; this pass only fixed the specific,
  measured `TransitionDistance`/`BoundaryGuard` inconsistency and the
  `MinClipDuration` floor.
