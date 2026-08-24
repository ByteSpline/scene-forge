# Accuracy Report — Transition Detection

This document explains what the accuracy/benchmark evaluation tool measures,
how to reproduce those measurements, and reports the current, real,
measured baseline. Per CLAUDE.md rule 10, nothing here is a claim of
absolute or complete accuracy — every number below is one measurement, on
one small synthetic fixture matrix, on one machine, on one date, and should
be read that way.

## What is measured

`accuracy/SceneForge.Accuracy` runs `SceneForge.Media.Detection.TransitionDetector`
over the synthetic fixture matrix described in
[`tests/fixtures/README.md`](../tests/fixtures/README.md) (32 fixtures: 8
transition types x 2 content variants, 4 distractor types x 2 variants, and
3 format-robustness groups), and reports, per `FixtureGroup` and in
aggregate:

- **Recall** — of the transitions actually present, how many were found.
- **Precision** — of the transitions reported, how many were real.
- **F1** — harmonic mean of Recall and Precision.
- **Mean boundary error (ms)** — for each true positive, how far the
  detector's single recommended `BoundaryTimestamp` fell from the
  ground-truth window's midpoint.
- **False positives per minute** — unmatched detections normalized by the
  group's total source duration, so it's comparable across groups with
  different fixture counts/durations.

`Recall`/`Precision`/`F1`/boundary error are `NaN` (never `0`) wherever they
are genuinely not applicable — a distractor group (BlackHold, FrozenFrame,
StaticShot, RapidMotion) has no expected transitions, so `Recall` is always
`NaN` for it; see `MetricsCalculator`'s own remarks for the exact rules.

## Commands

```
# Build the fixture matrix + write the compact ground-truth manifest.
dotnet run --project accuracy/SceneForge.Accuracy -- generate --output tests/fixtures/generated --manifest tests/fixtures/manifest.json

# Rebuild the matrix fresh and print/optionally save a full report.
dotnet run --project accuracy/SceneForge.Accuracy -- evaluate --report evaluation-report.json

# Same as evaluate, then compare against the committed baseline and exit
# non-zero only on a correctness regression (what CI runs).
dotnet run --project accuracy/SceneForge.Accuracy -- gate --baseline accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json

# Re-capture the committed baseline after an intentional accuracy change.
dotnet run --project accuracy/SceneForge.Accuracy -- update-baseline --output accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json
```

All four require real `ffmpeg.exe`/`ffprobe.exe` copied into
`accuracy/SceneForge.Accuracy/bin/<Debug|Release>/net8.0/tools/ffmpeg/`
(never committed — see `tests/fixtures/README.md`), or point elsewhere with
`--ffmpeg-base-dir <dir>`.

## Current measured baseline

Captured 2026-08-24 on the hardware documented in
[`docs/BENCHMARK_REPORT.md`](BENCHMARK_REPORT.md), commit `a0f8902`,
`AnalysisProfile.Accurate`, from
[`accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json`](../accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json):

| Group | TP | FN | FP | Recall | Precision | F1 | Boundary err (ms) | FP/min |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| HardCut | 1 | 1 | 1 | 50% | 50% | 50% | 0 | 10.0 |
| FadeToBlack | 2 | 0 | 0 | 100% | 100% | 100% | 250 | 0.0 |
| FadeFromBlack | 1 | 1 | 1 | 50% | 50% | 50% | 750 | 10.0 |
| Dissolve | 1 | 1 | 2 | 50% | 33% | 40% | 125 | 20.0 |
| Flash | 2 | 0 | 0 | 100% | 100% | 100% | 130 | 0.0 |
| BlurTransition | 1 | 1 | 2 | 50% | 33% | 40% | 0 | 20.0 |
| ZoomTransition | 0 | 2 | 2 | 0% | 0% | n/a | n/a | 20.0 |
| DirectionalSwipe | 0 | 2 | 2 | 0% | 0% | n/a | n/a | 20.0 |
| BlackHold (distractor) | 0 | 0 | 0 | n/a | n/a | n/a | n/a | 0.0 |
| FrozenFrame (distractor) | 0 | 0 | 0 | n/a | n/a | n/a | n/a | 0.0 |
| StaticShot (distractor) | 0 | 0 | 0 | n/a | n/a | n/a | n/a | 0.0 |
| RapidMotion (distractor) | 0 | 0 | 1 | n/a | 0% | n/a | n/a | 10.0 |
| VariableFrameRate | 1 | 1 | 1 | 50% | 50% | 50% | 0 | 10.0 |
| MixedResolution | 1 | 2 | 3 | 33% | 25% | 29% | 0 | 20.0 |
| Rotated | 1 | 2 | 2 | 33% | 33% | 33% | 0 | 13.3 |
| **AGGREGATE** | 11 | 13 | 17 | 46% | 39% | 42% | 149 | 10.6 |

Read alongside `docs/PHASE_06_REPORT.md`'s own honest accounting of this
same detector, this baseline is a **wider, harder** matrix than the one
that phase originally validated against — it adds three distractor groups
specifically designed to provoke false positives, and three
format-robustness groups the detector had never been measured against
before. The lower aggregate numbers here versus Phase 6's narrower matrix
are not a regression; they are this task's whole point — measuring, not
assuming, how the *unmodified* detector behaves on a wider matrix. Per this
task's explicit scope, no tuning or optimization was done in response to
these numbers.

## Limitations

- **Small sample size per group.** 1–3 expected transitions per group means
  a single miss swings Recall by 33–100 percentage points. Boundary error
  and FP/minute are similarly coarse. None of these numbers should be read
  as a precise population estimate — they are what this exact 32-fixture
  matrix showed on this exact run.
- **ZoomTransition and DirectionalSwipe recall was already a known,
  documented limitation** before this task (see
  `TransitionDetectorFixtureTests`'s own remarks and `docs/PHASE_06_REPORT.md`):
  at the ~160x90 analysis resolution these fixtures run at, genuine
  zoom/swipe optical-flow magnitude sits close to ordinary non-transition
  motion. This baseline's 0% recall for both is consistent with that
  existing, already-documented caveat, not a new finding.
- **MixedResolution/Rotated/VariableFrameRate are new fixture groups with
  no prior track record.** Their lower recall here may reflect real,
  previously-unmeasured pipeline behavior under these input-format quirks,
  or may simply reflect the small-sample-size caveat above — this baseline
  does not distinguish between the two, and no root-causing was done as
  part of this task.
- **Distractor false positives are low but not zero** (RapidMotion: 1 FP
  across 2 fixtures). This is a real, measured signal that continuous
  ordinary camera motion can still occasionally be misclassified — exactly
  the risk `BuildRapidMotionAsync`'s own code comments describe.
- **Single machine, single run.** No variance/confidence interval is
  reported — `RegressionGate` treats these correctness numbers as exact
  because the pipeline is fully deterministic (see its own remarks), but a
  different machine could still reveal a discrepancy if, e.g., libx264's
  encoded output differs subtly across ffmpeg builds/versions.
