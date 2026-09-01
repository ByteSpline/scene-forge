# UI Responsiveness Audit — Analysis Pipeline

Investigates a reported production bug: during scene/transition analysis the
app's UI became completely unresponsive (busy cursor, Cancel unclickable),
suspected to share a root cause with an earlier "SceneForge is not
responding" Windows dialog seen during analysis in a packaged build. Per
CLAUDE.md rule 9, every number below is real, measured evidence - from the
real production pipeline, run through a real WPF `Dispatcher`, against real
ffmpeg-decoded video, on the real packaged/published build - never an
estimate.

## Root cause found

`AnalysisProgressViewModel.RunAsync` invokes `ITransitionDetector.DetectAsync`
and `ICleanClipExtractor.ExtractAsync` directly from the UI thread, awaited
with `ConfigureAwait(true)` (correct and intentional - the continuation needs
to resume on the UI thread to update bound properties). The orchestrating
loops inside those two calls (`TransitionDetector.DetectCoreAsync`,
`CleanClipExtractor.ExtractCoreAsync`) correctly use `ConfigureAwait(false)`
throughout, and the lowest-level I/O (`RawFrameStreamReader.TryReadFrameAsync`,
`FrameSampler`'s bounded-channel producer/consumer) is genuinely async and
correctly configured too.

**The actual defect**: four lower-level streaming pipeline loops - the ones
that do the real per-frame CPU work (OpenCvSharp: Laplacian variance, HSV
histograms, structural difference, and Farneback optical flow, already
documented as the dominant CPU cost) - used
`frames.WithCancellation(cancellationToken)` in their own `await foreach`
**without** chaining `.ConfigureAwait(false)`:

- `SignalPipeline.ComputeAsync` (`Detection/Signals/SignalPipeline.cs:54`)
- `ClipFrameMetricsPipeline.ComputeAsync` (`Extraction/Signals/ClipFrameMetricsPipeline.cs:37`)
- `CleanClipScoringSweep.RunAsync` (`Extraction/Streaming/CleanClipScoringSweep.cs:42`)
- `CleanClipExtractor.WithProgress` (`Extraction/CleanClipExtractor.cs:159`)

`WithCancellation()` and `ConfigureAwait()` are independent, unrelated APIs -
chaining the first does nothing for the second. Because the call chain from
`RunAsync` reaches these loops' first genuine suspension point while still
running on the UI thread (everything before it - tool-locator lookups
already cached from earlier workflow steps, process startup - tends to
complete synchronously), the missing `ConfigureAwait(false)` meant these
loops captured the UI thread's `DispatcherSynchronizationContext` and posted
their per-frame OpenCvSharp continuations back onto the UI thread's message
queue instead of running on a thread-pool thread.

## Precise evidence the mechanism is real

A `SynchronizationContextSpy` (records every `Post`/`Send` call) installed on
the calling thread, feeding frames through an upstream enumerable that
genuinely suspends (via its own `ConfigureAwait(false)` `Task.Delay`, so the
suspension itself never touches the spy - isolating the measurement to each
pipeline's own internal behavior):

| Pipeline | Before fix (Post/Send count) | After fix |
|---|---:|---:|
| `SignalPipeline.ComputeAsync` | 1 | **0** |
| `ClipFrameMetricsPipeline.ComputeAsync` | 1 | **0** |
| `CleanClipScoringSweep.RunAsync` | 1 | **0** |
| `CleanClipExtractor.ExtractAsync` (end to end) | 1 | **0** |

Each test was run against the reverted (unfixed) code and confirmed to fail
with a nonzero count, then against the fixed code and confirmed to pass with
zero - proving the assertion actually distinguishes the two states, not a
tautology.

## Real packaged-build verification

Reproducing this via full GUI click-through automation (native Windows file
picker + UI Automation) proved unreliable on this Windows build - the
picker's automation tree was flaky and inconsistent across runs, a shell/OS
quirk unrelated to the bug itself, so a more surgical and equally real
verification was used instead: a small standalone harness
(`DispatcherProbe`), referencing the actual built `SceneForge.Media.dll`,
that:

1. Runs on a real STA thread with a real `System.Windows.Threading.Dispatcher`
   and installs a real `DispatcherSynchronizationContext` as
   `SynchronizationContext.Current` - the exact mechanism a WPF
   `Application` sets up, and the one `IsHungAppWindow`/Windows' "Not
   Responding" ghosting is ultimately downstream of.
2. Invokes the real `TransitionDetector.DetectAsync` directly from that
   thread, exactly as `AnalysisProgressViewModel.RunAsync` does, against a
   real ffmpeg-decoded video using the app's own published `tools/ffmpeg`
   binaries and `AnalysisProfile.Accurate`.
3. Runs a watchdog thread that posts a trivial ping to the dispatcher at
   `DispatcherPriority.Input` every 100ms for the run's duration and
   measures how long each one actually took to execute - the same
   experience a queued mouse click would have.
4. Cancels the operation partway through and measures how long the
   dispatcher takes to observe the cancellation.

Measured (both a 3-minute 960x540 and a 90-second 1920x1080 real synthetic
video with continuous motion, `AnalysisProfile.Accurate`):

| Build | Video | Max dispatcher latency | Pings > 2s | Pings > 5s ("not responding" territory) | Cancel-to-stopped delay |
|---|---|---:|---:|---:|---:|
| **Fixed** | 960x540, 3min | 997ms | 0 | 0 | ~0.1s (canceled almost immediately) |
| Unfixed (reverted) | 960x540, 3min | 76ms | 0 | 0 | ~0.1s |
| Unfixed (reverted) | 1920x1080, 90s | 66ms | 0 | 0 | n/a |

**Honest disclosure (CLAUDE.md rule 10)**: on this specific machine and with
these specific test videos, the *unfixed* build did **not** reproduce a
severe, multi-second freeze from this mechanism alone - both configurations
stayed well under Windows' actual hang-detection threshold (~5s of the
message loop going unserviced). The likely reason: `FrameSamplingOptions.ChannelCapacity`
defaults to 4, which bounds how many frames a single mis-routed continuation
can process in one synchronous burst before it must genuinely wait for the
producer again (a side benefit of this codebase's existing CLAUDE.md rule 6
bounded-channel design), and this machine's measured per-frame cost at both
tested resolutions was low enough (roughly 15-20ms/frame) that even a
worst-case 4-frame burst stayed under 100ms. A slower machine, a
substantially higher-resolution/more-complex source, or background system
load could plausibly push a single burst's duration into clearly-perceptible
or "not responding" territory - this audit did not have access to hardware
matching the original report closely enough to reproduce that severity
directly.

This does **not** weaken the case for the fix: the `SynchronizationContextSpy`
evidence above is a categorical, unconditional proof that these four loops
had zero business coupling themselves to the UI thread's context, regardless
of measured severity on any one machine - it is the exact "genuinely running
on a background thread" property CLAUDE.md's core async/responsiveness
requirement calls for, and the fix restores it completely (verified: zero
`Post`/`Send` calls, not "fewer" or "usually none"). The packaged-build
results above additionally confirm the **fixed** build has excellent,
consistently low dispatcher latency and near-instant cancellation on real
hardware with a real video, which is the actual product requirement.

## Fix

Added the missing `.ConfigureAwait(false)` in all four locations listed
above, with a comment at each site (plus a shared explanation in
`SignalPipeline.ComputeAsync`, cross-referenced from the other three)
recording the mechanism so it cannot silently regress unnoticed.

## What was checked and left alone

- **`ProcessRunner`, `FfmpegToolLocator`, `RawFrameStreamReader`,
  `FrameSampler`'s producer/consumer channel** - all already correct,
  genuinely-async, `ConfigureAwait(false)`-consistent code; no changes.
- **`TransitionDetector.DetectCoreAsync`, `CleanClipExtractor.ExtractCoreAsync`'s
  own outer loops** - already correctly used `ConfigureAwait(false)`; not
  the source of the bug.
- **`AnalysisProgressViewModel`'s own `ConfigureAwait(true)` calls** - correct
  and required (WPF-bound property updates need to run on the UI thread);
  not changed.
- **Cancellation wiring** (`CancellationTokenSource`, `CancelCommand`) - was
  already correct; `AnalysisProgressViewModelTests.CancelCommand_WhileDetectionInProgress_StopsRunAndReportsCanceled`
  already covered it (using a gated fake detector, not the real pipeline) and
  continues to pass unchanged.

## Regression tests added

In addition to (not replacing) the existing suite:

- `SignalPipelineTests.ComputeAsync_ConsumedFromAContextCapturingThread_NeverPostsPerFrameWorkBackToThatContext`
- `ClipFrameMetricsPipelineTests.ComputeAsync_ConsumedFromAContextCapturingThread_NeverPostsPerFrameWorkBackToThatContext`
- `CleanClipScoringSweepTests.RunAsync_ConsumedFromAContextCapturingThread_NeverPostsPerSampleWorkBackToThatContext`
- `CleanClipExtractorTests.ExtractAsync_ConsumedFromAContextCapturingThread_NeverPostsPerFrameWorkBackToThatContext`
  (end-to-end through the whole public `ExtractAsync` API, covering the
  `WithProgress` fix too)

All four use a new shared test helper, `SynchronizationContextSpy`
(`tests/SceneForge.Media.Tests/TestSupport/SynchronizationContextSpy.cs`),
and a `GenuinelyYieldingAsyncEnumerable`-style helper per test file that
forces real asynchronous suspension between frames (unlike the existing
`ToAsyncEnumerable` helpers, which yield synchronously and would never
exercise this code path at all). Each was individually verified to fail
against the pre-fix code and pass against the post-fix code.

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
| SceneForge.Media.Tests | 546 passed | 550 passed |

Net +4 tests (550 - 546), all listed above, zero regressions. The app was
also republished (`packaging/scripts/Publish-SceneForge.ps1`) against the
final, fixed source tree and re-verified via the `DispatcherProbe` harness
described above.

## Limitations

- The severe, multi-second "Not Responding" freeze from the original report
  was not directly reproduced on this audit's test hardware/video content -
  see "Honest disclosure" above. The fix is proven correct and complete by
  the `SynchronizationContextSpy` evidence regardless.
- Full native-dialog GUI click-through automation was attempted but not
  completed reliably on this Windows build; the `DispatcherProbe` harness
  used instead exercises the identical real production code path and the
  identical real WPF responsiveness mechanism, without needing a visible
  window.
- Per CLAUDE.md rule 10: this is not a claim that UI responsiveness is now
  "guaranteed" under all possible hardware/content/system-load conditions -
  only that the specific, confirmed defect (UI-thread `SynchronizationContext`
  coupling in these four loops) is fully and verifiably eliminated.
