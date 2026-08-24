# Benchmark Report — Transition Detection Throughput & Memory

Companion to [`docs/ACCURACY_REPORT.md`](ACCURACY_REPORT.md): where that
document covers correctness (precision/recall/F1/boundary error), this one
covers resource usage and speed, measured by the same
`accuracy/SceneForge.Accuracy evaluate`/`gate`/`update-baseline` commands
over the same 32-fixture synthetic matrix (see
[`tests/fixtures/README.md`](../tests/fixtures/README.md)). Per CLAUDE.md
rule 9, every number below is measured evidence from a real run, not an
estimate — and per this task's explicit scope, **no optimization work was
done**; this is only the current, honest baseline.

## What is measured

- **Throughput** — total source-seconds of fixture video analyzed, divided
  by total wall-clock seconds for the whole matrix run (`ResourceUsageSampler`
  + a `Stopwatch` around the full `evaluate` run).
- **Peak managed memory** — the highest `GC.GetTotalMemory` observed by
  `ResourceUsageSampler` sampling every 50ms for the run's duration (a
  point-in-time before/after read would miss a transient peak inside a
  single fixture's analysis).
- **Peak working set** — the highest `Process.WorkingSet64` observed the
  same way; the OS-level view of the process's resident memory, not just
  the managed heap.

## Documented hardware

Captured 2026-08-24, commit `a0f8902`, `AnalysisProfile.Accurate`, from
[`accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json`](../accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json):

| | |
|---|---|
| CPU | AMD Ryzen 5 3500U with Radeon Vega Mobile Gfx |
| Logical processors | 8 (4 cores) |
| RAM | 13.9 GB |
| OS | Microsoft Windows 10.0.19045 |
| .NET SDK | .NET 8.0.30 (pinned via `global.json` to 8.0.424) |

## Current measured baseline

| Metric | Value |
|---|---:|
| Throughput | 1.43 source-seconds analyzed / wall-clock second |
| Total wall clock (32 fixtures, ~90s combined source) | 67.2 s |
| Peak managed memory | 9.3 MB |
| Peak working set | 67.3 MB |

Reproduce with:

```
dotnet run --project accuracy/SceneForge.Accuracy -- evaluate --report evaluation-report.json
```

Re-capture the committed baseline (after an intentional change, on
whichever machine should become the new documented reference) with:

```
dotnet run --project accuracy/SceneForge.Accuracy -- update-baseline --output accuracy/SceneForge.Accuracy/Baselines/regression-baseline.json
```

## Why CI never gates on these numbers

`RegressionGate` (see its own remarks in
`accuracy/SceneForge.Accuracy/Evaluation/RegressionGate.cs`) compares
correctness metrics against the baseline with a small fixed epsilon and
fails the build on any regression beyond it, because those metrics are
fully deterministic — same code, same deterministically-generated
fixtures, same result, every time.

Throughput/memory/working-set are not like that. On a shared CI runner
(and even on one dev machine across runs) they move with CPU contention,
thermal throttling, background processes, and JIT/GC timing noise that has
nothing to do with whether the code is actually faster or slower. Gating
CI on them would mean real PRs failing for reasons unrelated to their own
changes — exactly the "noisy speed variance" this task's instructions
called out to avoid. `gate`/CI still **report** every resource/throughput
number and its percent delta from baseline (see the "Performance notes"
section of the tool's console output and the uploaded JSON report), so a
real, sustained regression is still visible to a human reviewer — it just
never blocks a merge on its own.

## Limitations

- One run, one machine — no variance across repeated runs was measured, so
  the "informational" performance deltas `gate` reports on a later run
  (typically low single-digit percent on unrelated changes, per this run's
  own performance-note output) are this baseline's honest, unfiltered
  run-to-run noise floor.
- These numbers describe analyzing 32 very small (160x90-640x360,
  ~3 second) synthetic fixtures at `AnalysisProfile.Accurate` — they are
  not a measurement of throughput/memory on real, longer, full-resolution
  video, which is a materially different workload.
- `ResourceUsageSampler`'s 50ms sampling interval can still miss a peak
  narrower than that window; the reported peaks are a lower bound on the
  true peak, not an exact one.
