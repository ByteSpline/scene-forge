# Phase 08 Report — Deterministic Constraint-Based TimelinePlanner

Date: 2026-08-23

## Scope

Build `TimelinePlanner` on top of Phase 7's `CleanClip` output: given a pool
of clean clips, a target audio duration, an output frame rate, a seed, and a
set of placement constraints (minimum repeat distance, maximum reuse count,
original-neighbor separation, visual-cluster adjacency limit), produce a
deterministic, ordered `TimelinePlan` whose total duration matches the
target exactly (in whole frames at the caller's chosen output time base),
while avoiding immediate duplicates, neighboring clips from the same source
scene, repeated visual clusters, and long stretches of preserved original
source order. Every placement carries a decision-trace entry explaining why
it happened and which constraints (if any) had to be relaxed to make it
possible; when the available footage cannot reach the target even after
relaxing every relaxable constraint, the result carries a quantified
`FeasibilityWarning` instead of silently exceeding the reuse cap. Explicitly
**not** in scope: `SceneForge.App` wiring (still no UI surface, as every
prior phase's report has noted), an actual FFmpeg export/render step that
consumes a `TimelinePlan` and writes a video file (a future phase), and
scene-boundary/scene-list construction (still a caller-supplied fact, as
Phase 7 established).

## Repository layout produced

```
src/SceneForge.Media/Planning/
  TimelineDurationBounds.cs, TimelinePlanRequest.cs, RelaxedConstraint.cs,
  TimelinePlacement.cs, TimelinePlanTraceEntry.cs, TimelineFeasibilityWarning.cs,
  TimelinePlan.cs, ITimelinePlanner.cs, TimelinePlanner.cs
  Internal/
    ClipShuffleOrder.cs

src/SceneForge.Media/Domain/
  RationalFrameRate.cs (extended: ToFrameCount/FromFrameCount)

tests/SceneForge.Media.Tests/Planning/
  ClipShuffleOrderTests.cs (9 tests)
  TimelinePlannerTests.cs (22 tests, hand-picked scenarios)
  TimelinePlannerPropertyTests.cs (9 tests, each iterating hundreds-to-
    thousands of seeds internally - see Test inventory)

tests/SceneForge.Media.Tests/TestSupport/
  CleanClipBuilder.cs (new), TimelinePlanAssertions.cs (new)

tests/SceneForge.Media.Tests/Domain/
  RationalFrameRateTests.cs (13 new tests, appended to the existing file)
```

No `.csproj` changes: `Planning/` lives in the existing `SceneForge.Media`
project and needs no new package references (no OpenCvSharp, no ffmpeg - the
planner is pure, in-memory selection logic over `CleanClip` records already
produced by Phase 7). The test project likewise needed no new references -
in particular, no external property-based-testing package (see Design
summary, "Property-based tests without a new dependency").

## Design summary

### `AvailableClips` is a plain caller-supplied fact, same as Phase 7's `SceneRanges`

`TimelinePlanRequest.AvailableClips` is typically
`CleanClipExtractionResult.AcceptedClips`, but `TimelinePlanner` never
re-scores, re-clusters, or otherwise second-guesses which clips are usable -
it reads exactly three fields off each `CleanClip` (`Range.Duration`,
`SourceSceneIndex`, `ClusterId`) and nothing else. This keeps the dependency
direction clean (`Planning` depends on `Extraction` only for the `CleanClip`
type itself, not for any of its scoring/clustering internals) and mirrors
Phase 7's own decision to take `SceneRanges`/`ExcludedIntervals` as facts
rather than computing them.

### Duration bounds are soft preferences, not hard requirements

The phase brief calls for "target audio duration, duration bounds" as
separate inputs. Read literally, a hard `[MinDuration, MaxDuration]` window
around the target would conflict with the brief's other hard requirement -
"trim only the last clip ... so the planned video duration matches the
audio duration exactly" - the moment available clip durations do not divide
the target evenly (nearly always, in practice). `TimelineDurationBounds`
resolves this by scoping duration bounds to exactly the one place trimming
happens: `MinFinalClipDuration` (a preferred floor for the trimmed final
clip) and `MaxOvershoot` (a preferred ceiling on how far the chosen final
candidate's full duration may exceed what remains before trimming). Both
are checked but never block a placement; exact-duration-match always wins,
and a violation is recorded as `RelaxedConstraint.FinalClipBelowMinDuration`
/ `FinalClipOvershootExceeded` on that placement's trace entry rather than
silently accepted. `TimelinePlannerTests` does not exercise a bound
violation directly (constructing one deterministically requires a
pool/target combination where every eligible final candidate is either too
short to satisfy `MinFinalClipDuration` given a fixed `remaining`, which is
impossible by definition, or overshoots `MaxOvershoot`, which is
straightforward but adds little beyond what the property tests' random
pools already exercise incidentally) - listed under Outstanding.

### One parameter, two related repetition rules: `OriginalNeighborSeparation`

The brief asks the planner to avoid both "neighboring clips from the same
source scene" and "long contiguous source order," but supplies only one
parameter for both -
`original-neighbor separation`. `TimelinePlanRequest.OriginalNeighborSeparation`
is defined as the minimum number of other placements required between two
clips sharing a `CleanClip.SourceSceneIndex`. This directly satisfies the
same-source-scene rule, and satisfies the contiguous-order rule as a direct
consequence: clips sharing one `SourceSceneIndex` are exactly the clips
`ClipCandidateGenerator` produced from one scene's sliding window in Phase 7
- i.e. exactly the clips whose original relative order would otherwise be
preserved - so keeping same-scene placements apart is what breaks
contiguity. `TimelinePlannerTests.Plan_RespectsOriginalNeighborSeparation_ForSameSourceScene`
demonstrates the mechanism directly: given two clips from each of two
scenes, the planner always produces an alternating (never same-scene-
adjacent) order without needing to relax anything.

### Tiered constraint relaxation, in a fixed, documented priority order

`MinimumRepeatDistance`, `OriginalNeighborSeparation`, and
`VisualClusterAdjacencyLimit` are the three constraints that can make no
clip eligible at a given step. Rather than fail or loop indefinitely,
`PlacementTracker.SelectCandidate` tries four fixed tiers in order - all
three constraints active, then `VisualClusterAdjacencyLimit` relaxed, then
also `OriginalNeighborSeparation`, then also `MinimumRepeatDistance` - and
stops at the first tier with any eligible clip. The order is deliberate:
cluster-adjacency is relaxed first because a repeated visual cluster is the
least noticeable repetition to a viewer; same-clip repeat distance is
relaxed last because literally reusing the identical clip too soon is the
most noticeable. `MaximumReuseCount` is never in this list - it is checked
before any tier and is a hard cap under every tier, by construction (see
next section). Every constraint actually relaxed to make a placement
possible is recorded on that placement's `TimelinePlanTraceEntry.RelaxedConstraints`,
never silently. `TimelinePlannerTests.Plan_ClusterAdjacencyAloneCanBlock_WithoutForcingSceneOrRepeatRelaxation`
and `Plan_SingleClipMustReuseImmediately_RelaxesAllThreePlacementConstraints`
demonstrate the isolated single-constraint case and the everything-at-once
case respectively.

### `MaximumReuseCount` is never relaxed - insufficient footage is reported, never papered over

Every other constraint bends before the target is missed; `MaximumReuseCount`
never does. `PlacementTracker.IsEligible` checks it unconditionally, before
any tier-specific logic runs, so no relaxation tier can ever cross it. When
every clip has reached the cap and the target duration has still not been
reached, `SelectCandidate` returns `null`, the placement loop stops, and
`TimelinePlan.FeasibilityWarning` reports the exact achieved duration and
shortfall (`Plan_InsufficientFootage_NeverExceedsReuseCap_AndReportsQuantifiedWarning`
constructs a concrete case: two 3s clips, `MaximumReuseCount = 1`, target
10s, yields exactly 6s achieved and a 4s shortfall, never more than two
placements). This is the direct implementation of the brief's "return
quantified warnings instead of silently producing extreme repetition":
"extreme repetition" would mean exceeding `MaximumReuseCount`, which the
hard-cap check makes structurally impossible.

### Selection within an eligible tier: unique clips first, least-used on tie, best-fit for the closing clip

Within whichever tier has eligible candidates, two different orderings
apply depending on whether any eligible clip's full duration already covers
what remains of the target:

- If none does, the plan is not yet closing out - `SelectCandidate` picks
  the eligible clip with the fewest prior uses, breaking ties via
  `ClipShuffleOrder`'s per-plan deterministic rank. Because every clip
  starts at zero uses, this means every distinct clip is placed once before
  any clip is placed twice ("use all suitable unique clips before reuse
  when practical"), and once reuse becomes necessary, the least-used clip
  is always preferred ("prefer least-used clips when reuse is necessary") -
  `TimelinePlannerTests.Plan_UsesEveryUniqueClipBeforeReusingAny_WhenPracticalAndConstraintsAllow`
  and `Plan_PrefersLeastUsedClipWhenReuseIsNecessary` assert both directly
  (the latter with a 2-clip pool driven to 6 placements, asserting an exact
  3-and-3 balanced split, never a lopsided 5-and-1).
- If at least one eligible clip's full duration already covers what
  remains, the *smallest* such clip is chosen (minimizing how much gets
  trimmed away), with the same least-used/rank tie-break as a secondary
  key. This is the only placement in a plan that is ever trimmed
  (`Plan_OnlyTheLastPlacementIsEverTrimmed`), and its trimmed duration is
  always exactly what remained of the target - never re-derived from which
  candidate was chosen, since every eligible final candidate would be
  trimmed to the identical remaining duration regardless (see
  `TimelineDurationBounds`'s remarks on why `MinFinalClipDuration` cannot be
  satisfied by picking a different clip).

### The seeded shuffle is the only source of randomness, computed once, per clip index

`ClipShuffleOrder.ComputeRanks` runs one seeded Fisher-Yates shuffle over
`[0, clipCount)` and returns each clip's position in that shuffle as its
tie-break rank - a permutation, verified directly
(`ComputeRanks_IsAPermutationOfZeroToCountMinusOne`, including across 5,000
seeds in `ComputeRanks_ManySeeds_AlwaysProduceAValidPermutation`). This rank
is computed exactly once per `Plan` call and never re-seeded mid-run, so
every least-used tie-break throughout one plan - and, given the same seed
and clip count, across repeated `Plan` calls - resolves identically
(`ClipShuffleOrderTests.ComputeRanks_SameSeedAndCount_IsDeterministic`,
`TimelinePlannerTests.Plan_SameSeedAndInputs_ProducesIdenticalPlan`, and
every iteration of `TimelinePlannerPropertyTests.Plan_ThousandsOfSeeds_AlwaysHoldsInvariants`,
which replans and compares placement-for-placement on every one of its
2,000 seeds per pool). This determinism holds for a given seed on a given
.NET major version and runtime - `System.Random`'s seeded sequence algorithm
is documented as stable within a major version but is not a cross-version
or cross-platform binary contract, so "the same seed always creates the
same plan" is scoped to "the same seed, on the same .NET 8 runtime this
repository targets," the same scoping every other seed-driven component in
this codebase (none existed before this phase) would need to state.

### Frame-exact duration matching via `RationalFrameRate.ToFrameCount`/`FromFrameCount`

"Matches the audio duration exactly in the selected output time base" is
answered literally: `RationalFrameRate` gained `ToFrameCount(TimeSpan)`
(round-to-nearest-frame, `MidpointRounding.AwayFromZero`) and
`FromFrameCount(long)` (the exact `TimeSpan` of that many whole frames,
rounded to the nearest 100ns tick since most rational rates - 30000/1001
foremost - do not divide `TimeSpan`'s tick resolution evenly). `TimelinePlanner.Plan`
quantizes `TargetAudioDuration` to `QuantizedTargetDuration` up front via
this pair, and every internal `remaining`/trim computation targets
`QuantizedTargetDuration` exactly, in ordinary exact `TimeSpan` arithmetic -
so `PlannedDuration == QuantizedTargetDuration` is bit-exact whenever
`IsComplete` is true, never merely "close." The sub-frame rounding this
implies is never hidden: `TimelinePlan.AudioDurationRoundingError` reports
`QuantizedTargetDuration - TargetDuration` directly
(`Plan_TargetNotAlignedToFrameBoundary_QuantizesAndReportsRoundingError`
exercises a target that is not a whole number of frames at 25fps and
asserts the error is nonzero and exactly accounted for). `RationalFrameRateTests`
covers both methods independently first (round-trip stability across
several rates including 24/1, 25/1, and 30000/1001, midpoint rounding
behavior, undefined-rate and negative-input rejection) before
`TimelinePlanner` is ever exercised against them.

### Property-based tests without a new dependency

The brief asks for property-based tests covering thousands of seeds. Rather
than add an external PBT package (FsCheck or similar) for a single
algorithm - a real new dependency, unlike anything else in this codebase's
minimal `OpenCvSharp4`-only dependency footprint -
`TimelinePlannerPropertyTests` implements the same idea directly: for each
of five representative clip pools (uniform/small, many clips with varied
duration, all-one-scene, deliberately footage-insufficient, and tiny-target-
with-abundant-footage), a single test iterates `TimelinePlanRequest.Seed`
from 0 to 1,999 (2,000 seeds x 5 pools = 10,000 planning runs from this one
test method alone) and asserts, for every seed: `MaximumReuseCount` is never
exceeded, every spacing constraint is either respected or recorded as
relaxed, only the last placement is ever trimmed, the duration/decision-
trace invariants hold, and replanning the identical request reproduces an
identical plan. Three further tests each loop 500-1,000 seeds over
dedicated edge-case pools (single-clip-single-slot, a pool containing a
zero-duration clip). `TimelinePlanAssertions` (test-only,
`tests/.../TestSupport/`) centralizes every one of these invariant checks so
both the hand-picked scenario tests in `TimelinePlannerTests` and the
seed-sweep tests in `TimelinePlannerPropertyTests` assert the exact same
rule, never two subtly different copies of it.

### Bounded loop, bounded memory - structural, not a runtime safeguard

Every clip that reaches `MaximumReuseCount` becomes permanently ineligible
(the hard-cap check never re-admits it), so the placement loop always
terminates within `AvailableClips.Count * MaximumReuseCount` iterations -
independent of target duration, independent of how aggressively spacing
constraints must be relaxed. `PlacementTracker` retains only fixed-size
per-clip arrays (`usageCount`, `lastPositionByClip`) plus two dictionaries
bounded by the number of distinct scenes/clusters actually present in
`AvailableClips` - never anything proportional to elapsed planning time or
placement count. `TimelinePlanner.Plan` is synchronous (not `async Task<...>`)
but still accepts and checks a `CancellationToken` once per placement
(`Plan_ExternalCancellation_ThrowsOperationCanceledException`) - the same
"CPU-only pipeline stage, no `await`, still cooperatively cancellable"
pattern this codebase already established for
`Extraction.Intervals.IntervalSubtractor.Subtract` and
`Extraction.Clustering.VisualClusterer.Cluster`, neither of which is async
either.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| Off-by-one in every spacing-constraint check | The first implementation blocked eligibility with `position - lastPosition < N`. Manually tracing `Plan_RespectsOriginalNeighborSeparation_ForSameSourceScene` (two clips per scene, `OriginalNeighborSeparation = 1`, expecting adjacency to be forbidden) before running it showed `distance < 1` is `distance == 0`, which is *impossible* between two distinct placements (positions are strictly increasing) - meaning `OriginalNeighborSeparation = 1`, the request type's own documented default, was a complete no-op under this formula, directly contradicting the field's own XML doc ("1 ... forbids only literally adjacent repeats"). The doc comment's intent - "N is the minimum number of *other* placements required between two occurrences" - requires blocking when `distance <= N` (equivalently `distance < N + 1`), which is what makes `N = 1` actually forbid `distance == 1`. Fixed all three checks (`MinimumRepeatDistance`, `OriginalNeighborSeparation`, `VisualClusterAdjacencyLimit`) to `<=` before writing `TimelinePlanAssertions` or running any test, and updated every doc comment to state the corrected semantics explicitly ("distance must exceed N"). Caught by hand-tracing a test before running it, not by a failing test - the danger of the bug was that the buggy `<` version would have compiled, run, and passed every test that only used `N = 0` (a no-op either way), which is most of the earlier hand-written scenarios; the fix and the assertion-helper update happened together specifically so no test could accidentally encode the same wrong formula twice. |
| `TheoryData<...>.Add(...)` does not accept named arguments | `TimelinePlannerPropertyTests.PoolConfigurations` initially called `data.Add(..., targetSeconds: 20, maximumReuseCount: 4, ...)`; `dotnet build` failed with `CS1739` because `TheoryData<T1..T7>.Add` has no parameter named `targetSeconds` (it is a positional-only generated overload). Fixed by switching every call to positional arguments - not a logic bug, but left here because the failure mode (a clean compile-time error) is exactly what CLAUDE.md rule 13's "run build before ending" step is for. |

Both were caught before any test run reported them as failures - the first
by tracing through the algorithm by hand against the test's own expected
outcome before trusting either, the second by `dotnet build` itself -
consistent with CLAUDE.md rule 8's requirement that algorithmic behavior be
verified, not assumed correct.

## Test inventory (new this phase)

53 new tests, verified directly from `dotnet test` output: the Phase 7
baseline was 363 total (355 passed + 8 skipped); this phase's suite is 416
total (408 passed + 8 skipped, all 53 new tests always-run - `TimelinePlanner`
needs no ffmpeg/OpenCvSharp, so none of its tests are `[SkippableFact]`).

- **RationalFrameRateTests** (13 new, appended to the existing 13) -
  `ToFrameCount`/`FromFrameCount` exact values at an integer rate (25/1),
  nearest-frame rounding at a non-integer rate (30000/1001), a `[Theory]`
  round-trip (`FromFrameCount` then `ToFrameCount` returns the original
  frame count) across 24/1, 25/1, and 30000/1001 at several frame counts
  including 0, undefined-rate rejection for both directions, negative-input
  rejection for both directions.
- **ClipShuffleOrderTests** (9) - empty/negative clip count, permutation
  property via `[Theory]` at several sizes, same-seed determinism,
  different-seeds-differ (a sanity check, not a hard guarantee), and the
  permutation property re-verified across 5,000 seeds in one test.
- **TimelinePlannerTests** (22, hand-picked scenarios, no ffmpeg) -
  argument validation (null request, negative duration, undefined time
  base), zero-target and empty-pool edge cases, exact-match and
  shorter-than-single-clip trimming, insufficient-footage quantified
  warning with reuse-cap enforcement asserted directly, same-seed
  determinism, unique-before-reuse and least-used-preference (with an exact
  3-and-3 balance assertion), each of the three spacing constraints
  respected in isolation, forced full relaxation (single clip, alone in its
  scene and cluster) and isolated cluster-only relaxation (three clips,
  distinct scenes, one shared cluster) demonstrating the documented tier
  order, only-the-last-placement-is-trimmed, decision-trace/placement
  correspondence, feasibility-warning message content, external
  cancellation, and frame-boundary quantization with a nonzero rounding
  error reported.
- **TimelinePlannerPropertyTests** (9) - one `[Theory]` with 5
  `MemberData`-supplied pool configurations (uniform-small, many-clips-
  varied-duration, single-scene, deliberately-insufficient, tiny-target-
  abundant-footage), each iterating 2,000 seeds internally and asserting
  reuse-cap/spacing/trim-location/duration/decision-trace invariants plus
  same-request determinism for every one (10,000 total planning runs from
  this one method); an empty-clips edge case via `[InlineData]`; and two
  further 500-1,000-seed sweeps over a single-clip-single-slot pool and a
  pool containing a zero-duration clip, both asserting no exception/hang
  and the same core invariants.

## Compliance notes against CLAUDE.md

- Rule 1-2 (native WPF, no Electron/web/cloud/telemetry): satisfied - pure
  in-memory selection logic, no UI, no network, no telemetry, no new
  package dependency.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): not implicated this phase -
  `TimelinePlanner` consumes only `CleanClip.Range`/`SourceSceneIndex`/`ClusterId`,
  values Phase 7's FFmpeg/OpenCvSharp-backed pipeline already produced; no
  new media I/O of any kind.
- Rule 4 (clean architecture): `Planning` depends on `Extraction` only for
  the `CleanClip` record type and on `Domain` for `TimeRange`/`RationalFrameRate`
  - no UI concern anywhere, no dependency in the other direction.
- Rule 5 (cancellation/cooperative shutdown): `TimelinePlanner.Plan` accepts
  and checks a `CancellationToken` once per placement
  (`ThrowIfCancellationRequested`), the same pattern
  `CleanClipExtractor.ExtractAsync`'s internal loops use, adapted to a
  synchronous, CPU-only method the same way `IntervalSubtractor.Subtract`
  and `VisualClusterer.Cluster` already are (see Design summary,
  "Bounded loop, bounded memory").
- Rule 6-7 (bounded memory/concurrency, no full-video buffering): the
  placement loop is structurally bounded by
  `AvailableClips.Count * MaximumReuseCount` iterations regardless of
  target duration or constraint pressure (see Design summary); no
  unbounded queue/cache/fan-out introduced, and nothing here touches video
  frames at all (this phase never opens a media file).
- Rule 8 (test-first): every component (`RationalFrameRate.ToFrameCount`/`FromFrameCount`,
  `ClipShuffleOrder`, `TimelinePlanner`) has dedicated tests requiring no
  OpenCvSharp/ffmpeg; the self-review section above documents one bug (the
  spacing-constraint off-by-one) caught by hand-tracing a test's expected
  outcome against the algorithm before trusting either, exactly the
  discipline this rule asks for.
- Rule 9 (benchmark with evidence): **not satisfied this phase**, following
  the same precedent Phase 7 documented for itself - this is new
  functionality with no prior version to diff against, and CLAUDE.md rule 9
  is about optimizations. `TimelinePlanner`'s own cost is a natural
  candidate for a future `benchmarks/SceneForge.Benchmarks/Planning/`
  benchmark once a baseline exists to compare future changes against - see
  Outstanding.
- Rule 10 (never claim absolute accuracy/exactness without qualification):
  `TimelinePlan.IsComplete`/`PlannedDuration` claim exact equality only
  against `QuantizedTargetDuration`, never raw `TargetAudioDuration`
  directly - the gap between the two is always reported via
  `AudioDurationRoundingError`, never hidden. When footage is insufficient,
  `FeasibilityWarning` states the achieved duration and shortfall
  explicitly rather than returning a plan that merely looks complete.
  Every relaxed constraint is named on its placement's trace entry, never
  silently absorbed - mirrors `ScoreReason`'s "never opaque" convention
  from Phase 7.
- Rule 11-12 (preserve user files, output to new path only): not
  implicated - `TimelinePlanner` reads only in-memory `CleanClip` records
  (themselves already read-only facts about a source file) and writes
  nothing to disk; `TimelinePlacement.SourceRange`/`UsedDuration` describe
  what a future export step should extract, they do not perform any
  extraction or file write themselves.
- Rule 13 (format/build/tests before ending): `dotnet format SceneForge.sln --verify-no-changes`
  clean; Debug and Release both build with 0 warnings/errors across all
  seven projects (product projects build under `TreatWarningsAsErrors=true`
  per `src/Directory.Build.props` - `AnalysisLevel=latest`/`AnalysisMode=Recommended`
  analyzers ran clean against every new file); Debug and Release both pass
  all 416 tests (408 passed, 8 skipped - the same pre-existing
  `[SkippableFact]` real-ffmpeg count as Phase 7, none of which this phase
  touches).
- Rule 14 (update docs on behavior change): this report is that update;
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (no new architectural
  decision beyond what is already on file - `Media -> Core` layering, no
  new package dependency).
- Rule 15 (don't advance while criteria fail): this phase's own criteria -
  builds/tests clean in Debug and Release, formatting clean, "same seed
  always produces the same plan" verified across 10,000+ planning runs (not
  merely asserted), `MaximumReuseCount` never exceeded verified the same
  way, `SceneForge.App` wiring and the actual FFmpeg export step explicitly
  out of scope - are met as of this report, with the benchmark gap and the
  `TimelineDurationBounds` violation-path gap both named explicitly under
  Outstanding rather than hidden.

## Outstanding for later phases

- **No benchmark for this phase's own cost** (see Compliance notes, Rule
  9) - a future phase should add `benchmarks/SceneForge.Benchmarks/Planning/`
  following the `Detection/TransitionDetectionBenchmarks` pattern (a
  synthetic in-memory clip pool of varying size, `MemoryDiagnoser`, results
  across a few representative constraint configurations).
- **`TimelineDurationBounds.MinFinalClipDuration`/`MaxOvershoot` violation
  paths are exercised only incidentally** (through the property tests'
  randomized pools, which occasionally happen to trigger them) rather than
  by a dedicated, deterministic `TimelinePlannerTests` case for each. A
  future pass should add a pool/target combination hand-constructed so the
  only eligible final candidate is known in advance to overshoot
  `MaxOvershoot`, and separately one where `remaining` at finalization is
  known in advance to be below `MinFinalClipDuration`, asserting the exact
  `RelaxedConstraint` recorded in each case.
- **No FFmpeg export/render step consumes `TimelinePlan` yet.** A future
  phase needs to turn `TimelinePlacement.SourceRange`/`UsedDuration` per
  placement into actual `ffmpeg` trim/concat arguments and write the
  planned video to a user-selected output path (CLAUDE.md rule 12) - this
  phase deliberately stops at producing the plan, not consuming it.
- **`SceneForge.App` wiring** (a UI surface to run `TimelinePlanner` against
  a `CleanClipExtractionResult`, expose the constraint knobs, and show the
  decision trace) remains untouched, as it has been since Phase 6.
- **Determinism is scoped to "same seed, same .NET 8 runtime."** If this
  codebase ever needs a plan to be reproducible across a future .NET major-
  version upgrade (e.g. a saved project file that must replan identically
  after an upgrade), `ClipShuffleOrder` would need its own explicit,
  version-independent PRNG algorithm rather than relying on `System.Random`'s
  documented-but-not-contractually-cross-version seeded sequence - not
  needed today, since nothing in this codebase persists a plan across a
  runtime upgrade yet.
