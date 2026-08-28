# Phase 16 Report — Never-Short-Output Guarantee: Reuse-Count Relaxation in TimelinePlanner

Date: 2026-08-27

## Scope

Product requirement, confirmed by the product owner: the planned output must
always match the target audio duration exactly, no matter how little clean
footage is available — this takes priority over transition-safety
strictness, clip reuse limits, and shuffle constraints. If there is not
enough clean footage to reach the target under the current constraints, the
system must relax those constraints, in order: (1) `MaximumReuseCount`, (2)
the spacing constraints Phase 8 already made relaxable, (3) the pre/post
safety buffer around detected transitions (Phase 7), (4) as a last resort,
repeating the whole clean-clip sequence with continued shuffling. The output
must never be silently short, and a hard failure with only a warning is not
acceptable when reasonable relaxation could close the gap; a
`FeasibilityWarning` may still be shown for transparency.

This phase extends `TimelinePlanner` (Phase 8) so `MaximumReuseCount`
becomes relaxable — the one constraint Phase 8 deliberately never relaxed —
and investigates `CleanClipExtractor`'s (Phase 7) safety-buffer defaults per
the brief's tier 3. Explicitly **not** in scope, and explained under Design
summary/Outstanding why: an automatic runtime pipeline that re-runs
`CleanClipExtractor` with a smaller buffer when `TimelinePlanner` alone
cannot close the gap (no such orchestrator exists yet, and — as this
report's analysis shows — none is needed to satisfy the hard requirement).

## Post-review update (2026-08-27)

A strict release review (`PHASE_REPORT.md`, "Phase 16 review") found and
fixed one major issue this report's original version did not catch:
`TimelineSummaryViewModel.BuildPlan` called the new, potentially-long-running
`ITimelinePlanner.Plan` synchronously on the UI thread with
`CancellationToken.None`, a CLAUDE.md rule 5 violation this phase's own
reuse-relaxation change introduced without anyone noticing, since Phase 8
built `Plan` as synchronous specifically because it used to always be fast.
Measured directly: a plausible large-project scenario (500 clips, a 4-hour
target) already took ~1s; a deliberately extreme case (a single clip against
a ~22-day target) took over 18s — an 18-second frozen, uncancelable window.
Fixed by moving the call behind `Task.Run` with a real, live
`CancellationToken`, following `AnalysisProgressViewModel`'s existing async
pattern; see `PHASE_REPORT.md`'s Phase 16 review for the full writeup,
including why `MaxReuseRelaxationHeadroom` was deliberately left at its
original 2,000,000 rather than tightened further (narrowing it would trade
away this phase's own "never short, no matter what" guarantee for
comparatively little remaining benefit once the UI-thread fix was in place).
Every number and test count elsewhere in this report below reflects the
state **before** that review; the corrected, final counts are:
`SceneForge.App.Tests` 64 passed (was 61 — 3 new regression tests from the
review), `SceneForge.Media.Tests` unchanged at 514 (the review's diff inside
`TimelinePlanner.cs` was comment-only, `MaxReuseRelaxationHeadroom`'s value
unchanged), full solution 663/663 in both Debug and Release.

## Post-acceptance fix (2026-08-27): removed ffmpeg option broke every many-clip render

Manual end-to-end testing of a real edit that produced many short clips —
exactly what this phase's never-short-output guarantee makes more common,
since reuse-cap escalation can now repeat a small clip pool dozens of times
to fill a long target — failed at the render step with:

```
ffmpeg render with encoder 'libx264' failed (exit code -1414549496):
Unrecognized option 'filter_complex_script'.
Error splitting the argument list: Option not found
```

**Root cause.** `FFmpegRenderService` builds one `filter_complex` graph for
the whole edit and, when that graph would get long enough to risk the
Win32 command-line limit (`InlineFilterGraphCharacterThreshold`, 6,000
chars — roughly 25+ segments), writes it to a temp file and passes the file
to ffmpeg instead of inlining it. That fallback (designed in Phase 9) had
never run against real ffmpeg before — every test that reached it used a
fake process runner that only string-matched the argument name — and it
passed the graph with `-filter_complex_script <file>`. That option was
**deprecated in ffmpeg 7.0 (2024-04) and removed in 8.0**; SceneForge's
own bundled/expected ffmpeg is 9.x, which rejects the entire invocation
before doing any work. The leading dash was never missing — ffmpeg always
prints an unknown option without its dash — this was a removed option, not
a formatting bug.

**Fix.** Pass the file with ffmpeg's generic "read this option's value
from a file" form, `-/filter_complex <file>` (available since 7.0, the
documented replacement). Verified directly against ffmpeg 9.0.1: the old
form reproduces the exact error above, the new form renders identically to
an inline `-filter_complex`. One line of production change in
`FFmpegRenderService.BuildFilterArguments`, plus the option strings lifted
to named `internal const`s so tests can reference them.

**Test gap closed.**

- `FFmpegRenderServiceTests.RenderAsync_ManySegments_UsesFilterComplexScriptFile_AndDeletesItAfterward`
  updated: now asserts the removed `-filter_complex_script` is *never*
  emitted and that `-/filter_complex <file>` is used instead.
- New real-ffmpeg integration test
  `FFmpegRenderServiceIntegrationTests.RenderAsync_ManyClips_CrossesFilterScriptThreshold_RealFfmpegAcceptsFileForm`:
  builds a 48-clip plan (each an exact 3-frame slice so quantization is a
  no-op and the test isolates the filter-script path), asserts white-box
  that the resulting graph really does exceed
  `InlineFilterGraphCharacterThreshold` (~10,300 chars), then requires a
  real ffmpeg encode of that plan to succeed and pass duration/stream
  verification (actual output duration matched the plan exactly, delta 0).
  Skipped in CI like every other real-binary test; runs locally where
  `tools/ffmpeg` is staged.

`SceneForge.Media.Tests` is now 515 (was 514); full solution 664. Format,
Release build of `SceneForge.Media`, and the full Release test run are all
green. (The `SceneForge.App` Release build could not be re-linked during
this fix because the app was running for manual testing and held its output
DLLs locked — an environmental lock, not a compile error; nothing in this
fix touches `SceneForge.App`.)

## Post-acceptance fix (2026-08-27): extreme source:target ratios made the filter graph infeasible for ffmpeg

Continued manual end-to-end testing of the same class of edit — the one
this phase's never-short-output guarantee makes common — hit a second,
deeper failure. With **very** limited clean footage (19 clips, ~67s total)
against a **22-minute** audio target, the render step failed with `Cannot
allocate memory` and, on runs that did not OOM outright, an estimated
render time around **17 hours**.

**Root cause — confirmed empirically, not estimated.**
`HighRepetitionRenderScenarioTests` plans that exact scenario with the real
`TimelinePlanner` at production defaults (`MaximumReuseCount = 1`, 30 fps,
seed 1) and measures:

| Quantity | Value |
|---|---|
| Placements produced | **378** (each of the 19 clips reused ~20×) |
| Distinct source ranges | **20** (19 clips + 1 trimmed final placement) |
| `filter_complex` graph length | **~80,400 characters** |
| Approx. libavfilter nodes | **~2,650**, fed by a **378-way `split`** off one decoded input and a **378-way `concat`** |

Phase 16's reuse-cap escalation is mathematically correct — the plan
*does* match the audio exactly. The defect is entirely in the renderer:
Phase 9's `RenderFilterGraphBuilder` emits one
`trim→setpts→scale→pad→fps→format→setsar` chain **per placement**, plus one
implicit `split` output and one `concat` input per placement, so the
in-memory filtergraph scales with **total segment count**, not **distinct
clip count**. `concat` drains its 378 inputs strictly in order while every
input derives from a single `split`, so the split's not-yet-read output
FIFOs back into buffering the whole decoded source hundreds of times over —
allocation failure, or pathological scheduling of a ~2,650-node graph. It
is also, at that scale, a latent CLAUDE.md rule 7 violation (near-whole-
source buffering) that the never-short guarantee made reachable in
practice. The `-/filter_complex` fix above is unrelated and does not help
here — the problem is the graph ffmpeg builds internally, not the command
line.

**Fix — two-stage render via ffmpeg's concat *demuxer* for high-repetition
plans.** `FFmpegRenderService.RenderAsync` now chooses a strategy per plan
(`ShouldUseConcatDemuxerStrategy`):

- **Below `ConcatDemuxerSegmentThreshold` (60 segments), or a large plan
  with little repetition** — the existing single-pass filter graph,
  unchanged and still the default. (A large *low-repetition* plan stays
  here deliberately: pre-rendering it would just buffer the whole output to
  disk. That case is called out under Outstanding.)
- **Past the threshold *and* distinct segments ≤ 50% of total** (i.e. every
  distinct segment reused ≥ ~2× on average) — a new two-stage path:
  - **Stage A** encodes each *distinct* `(SourceStart, SourceDuration)`
    window exactly once, applying the identical per-segment normalization
    (`RenderFilterGraphBuilder.BuildSingleSegmentVideoGraph`), pinned to the
    plan's frame-quantized length with `-frames:v` so the concatenated
    total stays frame-exact against `PlannedVideoDuration`. For the 19-clip
    scenario that is **20 short encodes (~67s of video), not 378**.
  - **Stage B** writes a concat-demuxer list file (one `file '…'` line per
    placement, in timeline order, with the demuxer's `'\''` quoting) and
    runs one `ffmpeg -f concat -safe 0 -i list -i audio -c:v copy …` pass:
    the assembled video is **stream-copied** (no re-encode of the 22-minute
    timeline) while the supplied audio is trimmed/encoded in the same pass
    (`RenderFilterGraphBuilder.BuildAudioOnlyGraph`, `-map 0:v:0 -map
    [aout]` — the source's own audio is still never referenced).

  Every intermediate lives under one temp directory
  (`%TEMP%\SceneForge\render-concat\<guid>\`) deleted in a `finally` block,
  success or failure — the same bounded, always-cleaned-up discipline as
  the filter-script file. Disk space for the pre-renders is checked up
  front via the existing `IAdaptiveResourceGovernor`, sized by the
  *distinct* footage volume (bounded by extraction), never the output
  duration. Hardware-encoder fallback wraps Stage A: if an NVENC/QSV/AMF
  segment encode fails, the whole attempt is retried with libx264 (all
  distinct segments re-encoded); Stage B is always `-c:v copy` so it can
  never be an encoder-specific failure. Progress reporting sweeps Stage A
  across the first 95% of the bar (it is nearly all the wall-clock cost for
  a high-repetition plan) and maps Stage B's real `-progress` stream into
  the last 5%.

Total video-encode work is *strictly less* than the single-pass path (each
distinct segment encoded once instead of every repetition re-encoded), and
the strategy scales to any segment count.

**Verified end to end against real ffmpeg 9.0.1.**
`FFmpegRenderServiceIntegrationTests.RenderAsync_HighRepetitionPlan_RealFfmpegPreRendersDistinctSegmentsAndConcats`
drives the path with 6 distinct 0.2s windows repeated to **150 placements**
(30s output): pre-renders 6 segments, concat-assembles the rest, and the
verified output duration matches the plan **exactly (delta 0)** with one
audio stream — in **~5 seconds** where the old path projected ~17 hours.

**Tests added (all green, `tools/ffmpeg` staged locally so none skip):**

- `HighRepetitionRenderScenarioTests` (2) — the empirical root-cause
  measurement above: placement count in the several-hundred range, and the
  resulting single-pass graph is `>40,000` chars / `>2,000` nodes while the
  distinct-segment count never exceeds `clipCount + 1`.
- `FFmpegRenderServiceTests` (6) — the concat path pre-renders each distinct
  segment once then concats (Stage A/B invocation counts and argument
  shape); the concat list has one line per placement in timeline order with
  the expected reuse period; the working directory is deleted afterward; a
  large low-repetition plan stays on the single-pass graph; a hardware
  Stage A failure falls back to libx264 for all distinct segments while
  Stage B stays a copy; progress is reported monotonically across both
  stages.
- `FFmpegRenderServiceIntegrationTests` (1, real ffmpeg) — the end-to-end
  verification above.

`SceneForge.Media.Tests` is now **524** (was 515). Full Debug solution
**673/673**, 0 skipped. Format clean. Release: `SceneForge.Core`,
`SceneForge.Media`, `SceneForge.Infrastructure`, `SceneForge.Accuracy` and
their test projects all build and pass (609). The `SceneForge.App` Release
relink is still blocked by the running app holding its output DLLs (the
same environmental lock noted above); nothing in this fix touches
`SceneForge.App`, and `SceneForge.App.Tests` passes in Debug (64).

**Outstanding — resolved by the next fix below.** As first written, this
fix left a large *low-repetition* plan (e.g. a 22-minute output cut from
hundreds of distinct 4s clips) on the single-pass filter-graph path, where
it hits the same filtergraph limits. That gap is closed by the batched
strategy in "Post-acceptance fix (2026-08-27): large distinct-segment
counts" below — the two piece-production strategies (distinct-dedup and
batched) now share one concat-demuxer assembly and cover every
total/distinct segment mix.

## Post-acceptance fix (2026-08-27): large distinct-segment counts — the general batched strategy

A third real-world render failure, a different shape again: a **new source
video with plenty of clean footage**. `Cannot allocate memory`, but this
time with **no repetition** — the 22-minute target is filled entirely by
distinct trims, so the distinct-dedup fix above (keyed on repetition) does
not engage and the render falls through to the single-pass graph.

**Root cause — confirmed empirically.**
`HighRepetitionRenderScenarioTests.PlentifulFootageLowRepetition_RoutesToBatchedStrategy_NotTheInfeasibleSinglePassGraph`
plans a 420-clip / ~1680s pool (3–5s clips, the `CleanClipExtractor`
default range) against the 22-minute target at production defaults:

| Quantity | Value |
|---|---|
| Placements produced | **329** |
| Distinct source ranges | **329** (max reuse of any clip: **1** — zero repetition) |
| `filter_complex` graph length | **~69,700 characters** |
| Approx. libavfilter nodes | **~2,300** |

Same defect, different trigger: the single-pass graph's node count scales
with **total** segment count, and segment count scales with output duration
÷ average clip length (~15 segments/minute at 4s clips). Repetition was
never the real variable — any target longer than ~10–13 minutes produces
enough segments to overflow the graph, and a footage-rich source (no
repetition) is the *more common* trigger.

**Fix — one scalable pre-render pipeline, two ways to produce the pieces.**
`FFmpegRenderService.SelectRenderStrategy` now returns one of three:

- **`SinglePass`** — `plan.Segments.Count <= MaxSegmentsPerFilterGraph`
  (60). The existing single filter_complex pass, unchanged, still the
  default for normal-length edits.
- **`DistinctDedup`** — past 60 segments *and* distinct ≤ 50% of total
  *and* distinct ≤ `MaxDistinctDedupPieces` (400). Render each distinct
  window once (the fix above). Optimal when a small set repeats.
- **`Batched`** — everything else past 60 segments. Partition the
  Position-ordered placement sequence into consecutive batches of ≤ 60,
  render each batch with a **bounded** video-only filter_complex
  (`RenderFilterGraphBuilder.BuildVideoConcat` — the same per-segment
  chain, capped at 60 trims + one `concat` node), `-frames:v`-pinned to the
  batch's summed frame-quantized length. The 329-segment plan → **6
  batches**. The general guarantee: correct for an all-distinct plan, a
  heavily-repeated one, or any mix.

Both non-single-pass strategies feed the **same Stage B**: list the pieces
in playback order (per placement for dedup, per batch for batched) for the
concat demuxer, stream-copy the assembled video, mux the trimmed audio.
Everything lives under one `%TEMP%\SceneForge\render-concat\<guid>\`
directory (piece files, per-batch filter scripts when a batch graph still
exceeds the inline char threshold, the list file), deleted in a `finally`
block on every exit path. Hardware→libx264 fallback wraps the whole
pre-render stage; Stage B is always `-c:v copy`.

**Why this is a general fix, not a third point patch.** The bound is now
structural: **no ffmpeg invocation ever carries a filter graph larger than
`MaxSegmentsPerFilterGraph` segments**, regardless of how many total or
distinct segments the plan has, and the concat demuxer that joins the
pieces has no practical count limit. The plan always renders — satisfying
CLAUDE.md rule 15 and the non-negotiable audio-duration guarantee — for any
segment count and any repetition mix real footage produces.

**On CLAUDE.md rule 7 (no full-video buffering).** The batched strategy's
encoded intermediates sum to ≈ the output size on disk transiently (one
temp dir, disk-space-checked up front, deleted in `finally`). That is the
output written in bounded chunks, not a decoded-video buffer: the
decoded-frame working set is one batch (≤ 60 segments) at a time, processed
and discarded before the next — a bounded window, which is what rule 7
asks for. Any multi-pass assembly writes its pieces to disk before the
final join; the alternative (a single pass) is the infeasible graph this
fix exists to avoid.

**Verified end to end against real ffmpeg 9.0.1.**
`FFmpegRenderServiceIntegrationTests.RenderAsync_LargeAllDistinctPlan_RealFfmpegRendersInBoundedBatchesAndConcats`
drives 70 distinct windows (no repetition) → 2 batches → a verified 28s
output whose duration matches the plan **exactly (delta 0)**, in **~4
seconds**. The distinct-dedup integration test still passes unchanged
(delta 0).

**Tests (all green; `tools/ffmpeg` staged locally so none skip):**

- `HighRepetitionRenderScenarioTests` — +1 (the 329-segment low-repetition
  measurement; asserts it routes to `Batched`).
- `FFmpegRenderServiceTests` — the large-plan test now asserts the `Batched`
  path (`ceil(305/60) = 6` bounded batches, every segment accounted for
  once, one concat pass); +1 for hardware-batch-failure → libx264 fallback
  across all batches. The renamed
  `RenderAsync_SinglePassGraphPastCharThreshold_UsesFilterComplexScriptFile_AndDeletesItAfterward`
  now exercises the single-pass script-file branch at exactly
  `MaxSegmentsPerFilterGraph`.
- `FFmpegRenderServiceIntegrationTests` — +1 (the real-ffmpeg batched
  verification above).
- `RenderFilterGraphBuilder` gains `BuildVideoConcat` (video-only, any
  segment subset); `Build` now delegates to it and no longer wraps a
  single-segment plan in a pointless `concat=n=1`.

`SceneForge.Media.Tests` is now **527** (was 524). Full Debug solution
**676/676**, 0 skipped. Format clean. Release: `SceneForge.Core`,
`SceneForge.Media`, `SceneForge.Infrastructure`, `SceneForge.Accuracy` +
their test projects build and pass (`SceneForge.Media.Tests` 527/527 in
Release too). The `SceneForge.App` Release relink remains blocked by the
running app holding its DLLs (unchanged environmental lock); nothing in
this fix touches `SceneForge.App`, and `SceneForge.App.Tests` passes in
Debug (64).

## Post-acceptance perf fix (2026-08-28): the batched render was 30-50 min because every batch re-decoded the whole source

Manual end-to-end testing confirmed the batched strategy above finally
renders the motivating scenario (231 clips / 22-minute audio) with no
memory crash — but it took **30-50 minutes**, too slow to use.

**Root cause — confirmed by measurement, not estimate.** Stage A rendered
each `<= InitialBatchSegmentCount` batch with a single shared `-i <source>`
input fanned out through the filtergraph's implicit `split` to one
`trim=start=<absolute>` per segment. `trim` operates *after* decode and the
command carried no input-level `-ss`, so ffmpeg decoded the source **from
frame 0** every time. Because a Position-ordered batch's segments are
scattered across the whole source (the planner shuffles), the latest trim
window in each batch sits near the source end, so each batch decoded
*almost the entire source*. For ~4 batches against a ~24-minute source that
is ~4x a full-source decode before any encoding. Direct measurement on an
8-minute 1080p source: one 2-second segment near the end took **32 s** with
the shared-input decode vs **1 s** with an input seek — a **32x** gap that
grows with source length and multiplies by batch count. Hardware-encoder
detection was *not* the problem (verified: `h264_amf` was probed and used
correctly on the test machine); the encode was already fast, the decode was
the cost.

**Fix — per-segment input seeking, batch sizing and retry logic untouched.**
`BuildSegmentRunArguments` now emits one `-ss <SourceStart> -i <source>`
input **per segment in the run** instead of one shared input, and
`RenderFilterGraphBuilder.BuildSeekedVideoConcat` builds the batch graph so
segment *k* reads from input *k* and trims from `0` (the seek already
positioned it). ffmpeg now decodes ~one GOP into each segment. Everything
that governs *when* and *how big* a batch is stays exactly as it was:
`SelectRenderStrategy`, `InitialBatchSegmentCount` (60), the
`DistinctDedup`/`Batched` split, and the recursive **halve-on-OOM** retry
in `RenderSegmentRunAsync` (which now splits an N-seeked-input run into two
smaller seeked-input runs, same as before). `-frames:v` still pins each
batch's concatenated frame count to the sum of its segments'
frame-quantized lengths, so the frame-exact duration guarantee is
unchanged — the rendered frames are identical, only the path the decoder
takes to reach them differs. At the 60-segment batch cap the extra `-ss
<n> -i <path>` tokens add a few KB to the command line, well within the
Win32 limit the `InlineFilterGraphCharacterThreshold` note already covers.

**Also fixed: the software fallback was dead on the shipped ffmpeg.**
SceneForge's vendored ffmpeg is built `--disable-libx264`, so
`RunWithFallbackAsync`'s hardcoded `libx264` retry — and
`HardwareEncoderProbe`'s hardcoded `libx264` last-resort candidate — could
never actually run: a hardware-render failure would have thrown instead of
falling back. `HardwareEncoderProbe` now also probes `libopenh264` (after
`libx264`), exposes `SelectSoftwareEncoderAsync` for the retry to resolve
the real software encoder name rather than assume one, and its smoke test
uses a representative `320x240` clip with the real per-encoder quality args
(a `64x64` bare probe is below NVENC's minimum dimensions and would
false-negative a working NVENC). `EncoderQualityDefaults` (new) is the one
place those args live, shared by the probe and the render. The selected
encoder — and any fallback — is now written to `System.Diagnostics.Trace`
(`[SceneForge.Render] encoder probe selected '<name>' (<hardware|software>)`).

**Measured before/after (synthetic 8-minute 1080p30 source, AMD AMF
hardware encode — `h264_amf` probed and selected, no fallback —
`tools/ffmpeg` staged; each run alone on the box):**

| Scenario | Before (shared `-i`, no seek) | After (`-ss` per segment) |
|---|---|---|
| ffmpeg-level Stage A — 120 scattered 1 s segments / 3 batches of 40 | **178 s** | **85 s** (2.1x) |
| ffmpeg-level Stage A — 180 scattered 2.5 s segments / 3 batches of 60 | **521 s** (8.7 min) | ~130 s (part of the 150 s below) |
| end-to-end `FFmpegRenderService.RenderAsync` — same 180-segment / 7:30-output plan, incl. probe + Stage B concat + verification | **> 521 s** (Stage A alone already 8.7 min) | **150 s** (2.5 min) |
| isolation — one 2 s segment near the source end | **32 s** | **1 s** (32x) |

End-to-end 180-segment run: **≥ 3.5x faster**, output verified with
`DurationDelta = 00:00:00` / `DurationWithinTolerance = true`, per-piece
bitrate identical to the shared-input form. The relative win grows with
source length (decode cost scales with it; the seek cost does not) and with
batch count, so the motivating 22-minute / ~24-minute-source case — 4-5
batches, ~3x this source's decode cost — sees a larger multiple than the
8-minute test does.

Bounded parallelism across independent Stage A batches
(`IAdaptiveResourceGovernor.MaxWorkers`) was investigated as a further
speed-up but **deliberately deferred**: it requires restructuring the
shared piece-index / `producedFiles` / `SplitEvents` accumulation that the
recursive halving writes into, which is exactly the retry machinery this
change was required to leave untouched. It is a clean follow-up once that
accumulation is made per-batch.

**Tests (all green; `tools/ffmpeg` staged locally so none skip):**

- `FFmpegRenderServiceTests` — +6:
  `RenderAsync_BatchedPlan_SeeksToEverySegmentSourceStart_NotASingleFullDecode`
  (every batch carries one `-ss <start> -i <source>` per segment, in
  Position order, each graph trim starting at 0),
  `RenderAsync_DistinctDedupPlan_SeeksToEachDistinctWindowStart`,
  `RenderAsync_BatchedPlan_StillPinsConcatenatedFrameCount_AfterSeekChange`
  (Phase 16 `-frames:v` guarantee), `RenderAsync_LogsWhichEncoderTheProbeSelected`
  (`[Theory]`, hardware + software), `RenderAsync_HardwareRenderFails_LogsTheSoftwareEncoderItRetriesWith`.
- `HardwareEncoderProbeTests` — +4: representative smoke-test resolution +
  real quality args; `SelectSoftwareEncoderAsync` skips hardware candidates
  and returns the first working software encoder; throws when none work; is
  cached separately from `SelectEncoderAsync`.
- `FFmpegRenderServiceIntegrationTests` — the two real-ffmpeg batched/dedup
  tests (`RenderAsync_LargeAllDistinctPlan_...`, `RenderAsync_HighRepetitionPlan_...`)
  pass unchanged with `DurationDelta = 0`, exercising the new seeked
  command against real ffmpeg.

`SceneForge.Media.Tests` is now **537** (was 527). Full Debug **686/686**,
Release **686/686**, 0 skipped. Format clean.

## Repository layout produced

```
src/SceneForge.Media/Planning/
  RelaxedConstraint.cs (extended: + MaximumReuseCount)
  TimelinePlanRequest.cs (doc comment only - MaximumReuseCount is now a
    preference, not a hard cap)
  TimelineFeasibilityWarning.cs (extended: + TimelineFeasibilityWarningKind,
    + RequestedMaximumReuseCount, + EffectiveMaximumReuseCount)
  TimelinePlan.cs (doc comment only)
  TimelinePlanner.cs (extended: two-attempt Plan(), ComputeGuaranteedSufficientReuseCap,
    PlacementTracker.RotatingTieBreakKey)

tests/SceneForge.Media.Tests/Planning/
  TimelinePlannerTests.cs (2 tests rewritten, 4 tests added - net +2)
  TimelinePlannerPropertyTests.cs (assertion-helper rename only)
  TimelinePlannerDurationGuaranteeTests.cs (new - 8 tests)

tests/SceneForge.Media.Tests/TestSupport/
  TimelinePlanAssertions.cs (AssertNeverExceedsMaximumReuseCount renamed to
    AssertMaximumReuseCountRespectedOrRelaxed and its semantics changed to
    match the other three "respected or relaxed" assertions;
    AssertDurationInvariants extended for the new SignificantRepetition case)
```

No `.csproj` changes: everything lives in the existing `SceneForge.Media`
project and its existing test project, no new package references (confirmed
no property-based-testing framework is a dependency anywhere in this repo —
same "hand-rolled thousands-of-seeds" convention Phase 8 established is
reused here, not replaced).

## Design summary

### `MaximumReuseCount` moves from "hard cap, never relaxed" to "preference, relaxed last resort, first among relaxable knobs"

Phase 8 built `MaximumReuseCount` as the one constraint `PlacementTracker`
checked unconditionally before any spacing tier, and explicitly never
relaxed — insufficient footage was reported as a shortfall instead
(`docs/PHASE_08_REPORT.md`, "`MaximumReuseCount` is never relaxed"). The
product owner's new requirement inverts this: the reuse cap must be the
*first* thing relaxed when the target cannot otherwise be reached, ahead of
the spacing constraints Phase 8 already made relaxable.

`TimelinePlanner.Plan` now makes up to two attempts. Attempt 1 runs exactly
as Phase 8 did — full spacing-tier relaxation, `MaximumReuseCount` enforced
as requested. Only if attempt 1 stops short does attempt 2 run:
`ComputeGuaranteedSufficientReuseCap` computes the smallest reuse cap that
provably lets the *shortest* available clip alone cover the whole
remaining target (worst-case single-clip repetition, spacing fully relaxed
— see the proof below), and the entire placement process re-runs from
scratch at that cap. Because attempt 2 reuses the *unmodified* Phase 8
per-placement tier system (`PlacementTracker.IsEligible`/`SelectCandidate`
are unchanged in structure — only the cap value fed to them moved from
`request.MaximumReuseCount` directly to a computed `effectiveMaximumReuseCount`
parameter), the priority order the product owner asked for falls out
naturally: for any single placement, spacing tiers 0–3 are tried at
whichever cap is currently in effect, so within one placement the algorithm
still explores "can I avoid relaxing spacing here" first — but at the
*plan* level, the cap itself is only ever raised as the outer, last-resort
move, exactly matching "first relax `MaximumReuseCount` ... if still short,
relax spacing."

### Why a single, directly-computed retry — not an escalating loop

An earlier design considered incrementally raising the cap by 1 inside
`PlacementTracker.SelectCandidate` itself each time every clip became
cap-blocked, retrying spacing tiers after each increment. This works but
adds looping complexity inside the already-intricate tier-selection method
that caused Phase 8's own off-by-one self-review finding
(`docs/PHASE_08_REPORT.md`, Self-review findings). Instead,
`ComputeGuaranteedSufficientReuseCap` computes a *sufficient* cap directly,
in closed form, before attempt 2 ever starts:

```
neededUses = ceil(quantizedTarget / shortestPositiveClipDuration) + 1
```

**Proof this is sufficient:** every placement `PlanWithReuseCap` makes
contributes at least `shortestPositiveClipDuration` toward `remaining`
(the true minimum across the whole pool — no clip is shorter), except
possibly the final trimmed placement, which only ever *helps* by consuming
less than a full clip's worth of remaining budget. So across *all* clips
combined, no more than `ceil(quantizedTarget / shortestPositiveClipDuration) + 1`
total placements are ever needed to reach the target, regardless of which
specific clips the algorithm happens to choose along the way. Since tier 3
(every spacing constraint relaxed) is always reachable and its only
remaining condition is the reuse cap itself, the true worst case — every one
of those placements forced onto a single clip because every other clip is
for some reason ineligible — still fits within `neededUses` uses of that one
clip. Setting `effectiveMaximumReuseCount = neededUses` therefore guarantees
`SelectCandidate` never returns `null` before `remaining` reaches zero,
proven directly rather than merely tested — though
`TimelinePlannerDurationGuaranteeTests` still verifies it empirically across
hundreds of seeds and several pool/ratio shapes, per CLAUDE.md rule 8.

`MaxReuseRelaxationHeadroom` (2,000,000) additionally bounds this
computation — a finite safety ceiling for a pathological synthetic input
(e.g. a sub-millisecond test clip against a very long target), not a limit
expected to bind against realistic footage (`CleanClipExtractor`'s own
minimum clip duration is 1s, default 3s; even the brief's extreme "1-minute
source vs. 20-minute target" example needs at most a few hundred uses of a
single clip — see `TimelinePlannerDurationGuaranteeTests.RatioScenarios`,
`ExtremeRatio_SingleClipPool`).

### Tiers 3 and 4 from the brief are structurally unnecessary given tier 1's guarantee — and that is stated, not hidden

The brief describes four ordered relaxation tiers: reuse count, spacing,
transition-buffer tightening, and — as an absolute last resort —
re-looping the whole clean-clip sequence. This phase implements tier 1
(reuse count) as a closed-form, provably-sufficient cap, and tier 2
(spacing) was already fully relaxable per placement since Phase 8.
Combined, tiers 1+2 already **guarantee** `TimelinePlan.IsComplete` whenever
the available pool contains at least one clip with positive duration — the
proof above does not depend on tier 3 or tier 4 at all. The only state
`ComputeGuaranteedSufficientReuseCap` cannot rescue is a pool with *no*
usable positive-duration content whatsoever (empty, or every clip collapsed
to zero duration) — and no amount of buffer-tightening or re-looping could
manufacture duration from that either, since both tiers still only ever
place clips *from the same pool*.

Given that, wiring tier 3 (an automatic pipeline that re-runs
`CleanClipExtractor` with a smaller `BoundaryGuard`/transition buffer and
re-plans) as a live runtime escalation step was judged not worth building
this phase: it would require a new cross-component orchestrator (there is
none — `CleanClipExtractor` and `TimelinePlanner` are separate DI-registered
services invoked at separate workflow steps, `TimelineSummaryViewModel`
only ever calls `ITimelinePlanner.Plan` against clips Scene Review already
finalized; see `src/SceneForge.App/ViewModels/TimelineSummaryViewModel.cs`),
would re-run the FFmpeg/OpenCvSharp extraction pipeline (real I/O cost, not
free like re-planning), and — per the proof above — is not load-bearing for
the hard "never short" requirement. This is named here explicitly as a
scope decision, not a gap papered over (CLAUDE.md rule 10): if a future
phase wants automatic buffer-tightening for its own sake (e.g. to reduce
*how much* repetition tier 1 needs, not to make completion possible at all),
`CleanClipScoringOptions.BoundaryGuard` and
`TransitionDetectionProfile.PreBufferDuration`/`PostBufferDuration` are
already caller-overridable `init` properties (see Buffer investigation
below) — no plumbing changes would be needed to try it.

Tier 4 (re-looping the sequence "with continued shuffling, not identical
repeated order") is, in effect, exactly what unbounded reuse relaxation
already produces once the least-used-first policy exhausts every unique
clip once and starts a second "lap" — see the next section for how "not
identical" is satisfied.

### `RotatingTieBreakKey`: successive laps through the pool don't replay the same order

Phase 8's tie-break among equally-least-used clips was the seeded shuffle
rank, fixed once per `Plan` call
(`Internal/ClipShuffleOrder.ComputeRanks`). Left unchanged, that would mean
every "lap" through the pool (the point where every clip's usage count ties
again) resolves ties in the *identical* relative order every time —
technically never "the same clip twice in a row" (spacing constraints still
apply), but a fixed round-robin cycle rather than genuine continued
shuffling. `PlacementTracker.RotatingTieBreakKey(clipIndex)` replaces the
raw `_rank[clipIndex]` in both selection orderings with
`(_rank[clipIndex] + _usageCount[clipIndex]) % _clips.Count` — a pure
function of the one seeded shuffle already computed (no new randomness, so
determinism is unaffected) that is a no-op on every clip's first use
(`usageCount == 0` ⇒ key equals the raw rank exactly, so which unique clip
is placed first is unchanged from Phase 8) but rotates the tie-break order
on every subsequent lap.
`Plan_HeavyRepetition_RotatesTieBreakOrder_SoConsecutiveFullPassesOverThePoolDiffer`
verifies this directly: 6 clips, a target forcing exactly 6 full passes,
asserts at least one pass's clip-index order differs from the first pass's.

### Buffer investigation (Phase 7, tier 3 of the brief) — re-examined, deliberately left unchanged

Two buffer-like knobs sit between a detected transition and a usable clip:
`TransitionDetectionProfile.PreBufferDuration`/`PostBufferDuration` (Phase
6, `Detection/Fusion` — 100ms each, subtracted/added to every detection's
`Start`/`End` before it becomes an `ExcludedInterval`) and
`CleanClipScoringOptions.BoundaryGuard` (Phase 7, `Extraction` — 250ms,
trimmed off both ends of every remaining range *after* exclusion
subtraction, before any candidate is generated at all). `BoundaryGuard`'s
own doc comment describes it as absorbing only "residual jitter/soft edges
... that the exclusion interval itself may not have captured exactly" — a
supplementary margin on top of the 100ms buffer already applied upstream,
yet it is itself 2.5× larger than the thing it supplements. That
internal-consistency observation is a real, named finding of this
investigation.

It was **not** acted on by lowering the shipped default, for two reasons
consistent with this codebase's existing discipline (CLAUDE.md rules 9–10;
`docs/PHASE_07_REPORT.md` made the identical call about every other scoring
threshold): first, no real-footage measurement exists either way — Phase
7's own report states every `CleanClipScoringOptions` default is "a
deliberately heuristic, unmeasured-against-real-footage constant," and
picking a different unmeasured number trades away contamination-safety
margin for every caller without evidence it is actually safe to do so;
second, and decisively, it is not necessary — the "Tiers 3 and 4 are
structurally unnecessary" proof above means the hard "never produce a short
output" requirement is already fully satisfied by tier 1 alone, so loosening
this default would only reduce *how much* repetition tier 1 ends up needing,
not whether the target is reached at all, and doing that without measured
evidence is not a trade this phase makes unilaterally. `BoundaryGuard`
remains a fully caller-overridable `init` property today, so a future phase
with real-footage measurement in hand (or an actual orchestrator wanting to
trade buffer size for less repetition) can act on this finding without any
further plumbing.

### `TimelineFeasibilityWarning` gains a `Kind` to distinguish "informational" from "actually short"

Before this phase, `FeasibilityWarning` was non-null if and only if
`IsComplete` was false — one meaning, one shape. Reuse relaxation makes that
insufficient: a plan can now be `IsComplete == true` while still having
needed significant repetition worth surfacing to the user (the brief's own
example: *"significant repetition was needed to match audio length"*), and
that is a fundamentally different situation from a genuine shortfall — a UI
should never treat them the same way (one is FYI, the other is a real
problem). `TimelineFeasibilityWarningKind` (`Shortfall` /
`SignificantRepetition`) makes this explicit rather than leaving a caller to
infer it from `IsComplete` plus string-matching the message.
`RequestedMaximumReuseCount`/`EffectiveMaximumReuseCount` were added
alongside it so a caller can quantify "how much" relaxation happened without
parsing the message string. `TimelineSummaryViewModel` needed **no code
change** to pick this up — it already binds
`plan.FeasibilityWarning?.Message` directly, so the new message text (and
the new, non-blocking case where it fires alongside a complete plan) flows
through automatically; `TimelinePlanAssertions.AssertDurationInvariants`
was extended to assert the `Kind`/`Shortfall` pairing is always internally
consistent for both existing and new callers.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| `Plan_InsufficientFootage_NeverExceedsReuseCap_AndReportsQuantifiedWarning` and `Plan_FeasibilityWarningMessage_ContainsQuantifiedNumbers` (Phase 8) silently changed meaning | Both existing tests exercised exactly the "insufficient footage" scenario this phase was written to fix — after the change, both now complete instead of falling short, so their original assertions (`Assert.False(plan.IsComplete)`, exact shortfall numbers) became assertions about behavior that no longer exists, not compile errors, so `dotnet build` could not have caught this. Found by hand-tracing each scenario against the new algorithm before running the tests (the same discipline Phase 8's own self-review section used), confirmed by actually running them. Resolved by rewriting both to assert the new, correct behavior (`Plan_InsufficientFootage_RelaxesMaximumReuseCount_AndStillReachesTargetExactly`, `Plan_SignificantRepetitionWarningMessage_ContainsQuantifiedNumbers`) and adding two new tests specifically to keep the genuine-`Shortfall` code path covered (`Plan_ZeroDurationOnlyPool_CannotBeRelaxedIntoReachingTarget_ReportsShortfall`, `Plan_ShortfallWarningMessage_ContainsQuantifiedNumbers`), since a zero-duration-only pool is now the *only* scenario that still legitimately produces one. |
| Hand-designed "zero-duration pool never places anything" test was wrong | The first draft of `Plan_ZeroDurationOnlyPool_CannotBeRelaxedIntoReachingTarget_ReportsShortfall` asserted `Assert.Empty(plan.Placements)`. Running it failed: zero-duration clips *are* placed (they just never advance `remaining`, matching the pre-existing Phase 8 behavior `Plan_EdgeCase_ZeroDurationClipsAreUsedButNeverProgressTheBudgetIncorrectly` already covers) — attempt 1 places every clip up to the requested cap before `SelectCandidate` finally returns `null`, so the plan has placements whose durations sum to zero, not zero placements. Fixed by asserting `PlannedDuration == TimeSpan.Zero` (the actually-invariant fact) instead of an incidental placement count. |

Both were caught by running the tests against hand-traced expectations
before trusting either, consistent with CLAUDE.md rule 8.

## Test inventory (new/changed this phase)

`SceneForge.Media.Tests` baseline before this phase: 504 passed, 0 skipped
(confirmed by a full-suite run at the start of this session, before any
Planning changes). This phase's suite: 514 passed, 0 skipped — net +10.

- **`RelaxedConstraint.cs`/`TimelineFeasibilityWarning.cs`/`TimelinePlan.cs`/`TimelinePlanRequest.cs`**
  — doc-comment and type-shape changes only, covered indirectly by every
  test below that inspects `RelaxedConstraint.MaximumReuseCount` or
  `TimelineFeasibilityWarningKind`.
- **`TimelinePlannerTests.cs`** (net +2: 2 rewritten, 2 net added on top) —
  `Plan_InsufficientFootage_RelaxesMaximumReuseCount_AndStillReachesTargetExactly`
  (the exact Phase 8 scenario that used to fall 4s short now completes
  exactly, tagged `RelaxedConstraint.MaximumReuseCount`, reports
  `SignificantRepetition`), `Plan_ZeroDurationOnlyPool_CannotBeRelaxedIntoReachingTarget_ReportsShortfall`
  (the one case relaxation cannot fix), `Plan_ShortfallWarningMessage_ContainsQuantifiedNumbers`
  and `Plan_SignificantRepetitionWarningMessage_ContainsQuantifiedNumbers`
  (message-content coverage for both `TimelineFeasibilityWarningKind`
  values). Every pre-existing test in this file not touching the
  insufficient-footage path was re-verified to already have enough capacity
  under its originally-requested cap (so attempt 2 never triggers) and
  passes unchanged.
- **`TimelinePlannerPropertyTests.cs`** — no behavioral changes; the one
  pool configuration that previously demonstrated the shortfall path
  (`"InsufficientFootage"`, 3 clips × 2s at `MaximumReuseCount = 1` against a
  100s target) now completes via relaxation across all 2,000 swept seeds
  instead, exercised by the renamed, generalized
  `TimelinePlanAssertions.AssertMaximumReuseCountRespectedOrRelaxed`.
- **`TimelinePlannerDurationGuaranteeTests.cs`** (new, 8 tests) —
  `Plan_AcrossWideSourceToTargetRatios_AlwaysReachesTargetExactly` (`[Theory]`,
  6 pool/target ratio configurations × 200 seeds each = 1,200 planning runs
  from one test method, including the brief's own extreme "1-minute source
  vs. 20-minute target" example and a single-clip worst case against the
  same 20-minute target), `Plan_RealisticHeavyTransitionSource_TwentyFourMinuteSourceAgainstTwentyTwoMinuteTarget_ReachesTargetExactly`
  (200 synthetic clips modeling ~13.3 minutes of clean footage surviving a
  24-minute, heavy-transition source, against a 22-minute target, at
  `TimelineSummaryViewModel`'s actual production defaults —
  `MaximumReuseCount = 1` — confirming the scenario now succeeds instead of
  the ~520s shortfall it would have reported before this phase),
  `Plan_HeavyRepetition_RotatesTieBreakOrder_SoConsecutiveFullPassesOverThePoolDiffer`.
- **`TimelinePlanAssertions.cs`** — `AssertNeverExceedsMaximumReuseCount`
  renamed to `AssertMaximumReuseCountRespectedOrRelaxed` and changed from a
  hard `<=` assertion to the same "respected, or the exceedance is tagged on
  its trace entry" pattern the other three placement-constraint assertions
  already used; `AssertDurationInvariants` extended to accept a non-null,
  `SignificantRepetition`-kind warning on a complete plan while still
  requiring a `Shortfall`-kind warning with a matching numeric shortfall on
  an incomplete one.

## Commands executed and results

All commands run from `C:\Users\Bwp COmputers\Desktop\scene-forge` with the
pinned .NET 8.0.424 SDK.

### Format

```
dotnet format SceneForge.sln
dotnet format SceneForge.sln --verify-no-changes
```
Second run produced no output (exit 0) — clean.

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
for both configurations, across all eleven projects (App, Core, Media,
Infrastructure, Accuracy, Benchmarks, and each project's `.Tests` project).

### Test (Debug and Release)

```
dotnet test SceneForge.sln --no-build --configuration Release
dotnet test SceneForge.sln --no-build --configuration Debug
```
Release: 660/660 passed, 0 skipped, across all five test projects
(`SceneForge.Core.Tests` 8, `SceneForge.Accuracy.Tests` 31,
`SceneForge.App.Tests` 61, `SceneForge.Infrastructure.Tests` 46,
`SceneForge.Media.Tests` 514).

Debug: one transient failure on the first full-suite run —
`TransitionDetectorTests.DetectAsync_ReportsProgressForEachAnalyzedFramePair`
(`Detection` module, untouched by this phase) — reported 2 progress
callbacks instead of an expected 3. Re-run in isolation: passed. Re-run the
full `SceneForge.Media.Tests` suite again: 514/514 passed. This is the same
"GC-timing-sensitive test flakes once under full-suite pressure, passes on
immediate re-run" pattern Phases 6 and 7 already documented for a different
test (`docs/PHASE_07_REPORT.md`, Test section) — not a regression introduced
by this phase, and not a test this phase's changes touch.

**Superseded by the post-review fix** (see "Post-review update" above):
`TimelineSummaryViewModel` *did* need a code change after all — the
strict release review found it called `Plan` synchronously on the UI thread
with no real cancellation, fixed it, and added 3 regression tests. The
corrected, current counts are `SceneForge.App.Tests` 64 and full solution
663/663 (both Debug and Release) — see `PHASE_REPORT.md`'s Phase 16 review
for the commands and evidence.

## Compliance notes against CLAUDE.md

- Rule 1–2 (native WPF, no Electron/web/cloud/telemetry): satisfied — pure
  in-memory selection-logic changes plus doc-comment updates; no network or
  telemetry surface touched. **Correction from the post-review fix**: unlike
  this report originally claimed, `TimelineSummaryViewModel` *did* need a
  code change (moving `Plan` off the UI thread) — see "Post-review update"
  above; that change stayed within the existing WPF/MVVM UI layer and
  introduced no new UI technology or pattern.
- Rule 3 (FFmpeg/FFprobe + OpenCvSharp): not implicated — no media I/O in
  this phase's changes; the buffer investigation reviewed but did not modify
  `CleanClipExtractor`'s FFmpeg/OpenCvSharp-backed pipeline.
- Rule 4 (clean architecture): unchanged dependency graph; `Planning` still
  depends on `Extraction` only for the `CleanClip` type, `Detection.Fusion`
  untouched.
- Rule 5 (cancellation): `Plan`'s two-attempt structure still checks the
  same `CancellationToken` once per placement in `PlanWithReuseCap` (the
  extracted former loop body) — both attempt 1 and a triggered attempt 2
  honor it independently; `Plan_ExternalCancellation_ThrowsOperationCanceledException`
  (Phase 8, unchanged) still passes.
- Rule 6–7 (bounded memory/concurrency, no full-video buffering): each
  attempt's loop remains bounded by `AvailableClips.Count * effectiveMaximumReuseCount`
  iterations exactly as Phase 8 documented, just with `effectiveMaximumReuseCount`
  now sometimes larger than the requested cap; that larger value is itself
  bounded by `MaxReuseRelaxationHeadroom` (a finite, named constant — see
  Design summary), so no unbounded fan-out is introduced. At most one extra
  full re-plan (attempt 2) ever runs — no retry loop, no unbounded escalation.
- Rule 8 (test-first): the self-review section above documents finding both
  a stale-test issue and a wrong test assumption by hand-tracing against the
  new algorithm and then running the tests, before trusting either, matching
  Phase 8's own established discipline; the `ComputeGuaranteedSufficientReuseCap`
  sufficiency claim is backed by both a closed-form proof (Design summary)
  and empirical coverage across hundreds of seeds and multiple ratio shapes
  (`TimelinePlannerDurationGuaranteeTests`).
- Rule 9 (benchmark every optimization with evidence): this phase is a
  correctness/behavior change (closing a duration shortfall), not a
  performance optimization, so no before/after benchmark applies in the
  sense rule 9 means — consistent with how Phase 8 itself was scoped (no
  benchmark, new functionality, not an optimization). The "before" and
  "after" *behavior* on the motivating realistic scenario is itself the
  evidence this rule's spirit asks for, and is measured directly:
  `Plan_RealisticHeavyTransitionSource_TwentyFourMinuteSourceAgainstTwentyTwoMinuteTarget_ReachesTargetExactly`
  proves the after-state (reaches 22:00 exactly), and the before-state (a
  ~520s/8.7-minute shortfall under the old hard-cap behavior) is derived
  directly from the same fixture's numbers in that test's own comment,
  reproducible by inspection of the removed hard-cap logic in this diff's
  `git show`.
- Rule 10 (never claim absolute accuracy/exactness without qualification):
  `IsComplete`/`PlannedDuration` still claim exactness only against
  `QuantizedTargetDuration`, never raw `TargetAudioDuration` directly (Phase
  8's frame-quantization scoping is unchanged). The one remaining way
  `IsComplete` can be `false` — a pool with no positive-duration content at
  all — is stated explicitly in `TimelinePlan.IsComplete`'s doc comment and
  `TimelineFeasibilityWarningKind.Shortfall`'s doc comment, never implied to
  be impossible. The buffer-default investigation states its own finding
  (250ms is disproportionate to the 100ms margin it supplements) and its own
  reason for not acting on it (no measurement, not load-bearing) rather than
  silently leaving the question unaddressed.
- Rule 11–12 (preserve user files, output to new path only): not implicated
  — `TimelinePlanner` still reads only in-memory `CleanClip` records and
  writes nothing to disk.
- Rule 13 (format/build/tests before ending): all run above — format clean,
  Debug and Release both build with 0 warnings/errors across all eleven
  projects, Debug and Release both pass every test (Debug's one transient
  unrelated flake reproduced the documented pre-existing pattern and passed
  on re-run, confirmed twice).
- Rule 14 (update docs on behavior change): this report is that update. No
  change to `docs/ARCHITECTURE_DECISIONS.md` was needed (no new
  architectural decision beyond what is already on file — no new
  dependency, no new component, same `Media` layering).
- Rule 15 (don't advance while criteria fail): this phase's own criteria —
  builds/tests clean in Debug and Release, formatting clean, the "never
  produce a short output" guarantee proven both analytically and empirically
  across the required extreme-ratio and realistic-scenario cases, tier 3/4
  scope decisions stated explicitly rather than silently skipped — are met
  as of this report.

## Outstanding for later phases

- **No live orchestrator ties `CleanClipExtractor` and `TimelinePlanner`
  together with an automatic retry loop.** As established under Design
  summary, this is not needed for the hard "never short" requirement (tiers
  1+2 alone guarantee it), but a future phase could still build one if
  reducing *how much* repetition is needed (rather than whether the target
  is reached at all) becomes its own goal — e.g. trying a smaller
  `BoundaryGuard` before falling back to heavy reuse, trading a small,
  measured contamination-risk increase for less visible repetition. Doing
  that responsibly needs real-footage measurement first (see the buffer
  investigation's stated reasoning), which this phase's time budget did not
  extend to gathering.
- **`CleanClipScoringOptions.BoundaryGuard`'s 250ms default is still an
  unmeasured heuristic**, same as every other threshold in that type per
  Phase 7's own report — this phase's investigation narrowed the open
  question (it is disproportionate to the 100ms transition buffer it
  supplements, specifically) but did not resolve it with measurement. A
  future fixture-matrix pass (the same kind Phase 7 already listed as
  outstanding for its scoring thresholds generally) should cover this
  specific value too.
- **No benchmark for `ComputeGuaranteedSufficientReuseCap`'s or attempt 2's
  own cost** — both are O(pool size) and O(placements), the same complexity
  class Phase 8 already established for the unchanged parts of `Plan`, so no
  new performance characteristic was introduced, but no
  `benchmarks/SceneForge.Benchmarks/Planning/` benchmark exists yet for this
  component at all (Phase 8 already listed this as outstanding for itself).
- **`SceneForge.App` surfaces `FeasibilityWarning.Message` as a single
  string** (`TimelineSummaryViewModel.FeasibilityWarning`) without
  distinguishing `TimelineFeasibilityWarningKind` in the UI — functionally
  correct (the message text itself already reads very differently for the
  two kinds) but a future UI pass could style `SignificantRepetition`
  (informational) differently from `Shortfall` (a real problem) now that the
  `Kind` is available to bind against.
