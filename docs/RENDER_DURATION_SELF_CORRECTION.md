# Render Duration Self-Correction and Calm Failure Messaging

Product requirement: a valid render must never end in a red "Render failed"
screen over a duration-tolerance mismatch - time is not a concern, reliability
is. This implements automatic internal retry/correction for duration-only
verification misses, and reworks the render screen's remaining (genuinely
unrecoverable) failure messages to be calm and plain-language instead of raw
exception text, per CLAUDE.md rule 10 (never claim exactness without stating
what it was measured against) and rule 15 (do not advance past a failing
guarantee - here, "the render finishes").

## Scope

Only a **duration-only** verification miss self-corrects. `RenderOutputVerifier`
checks six independent things (video stream present, exactly one audio
stream, duration within one frame, first/middle/last frame decodable -
`RenderVerificationResult`); a miss on ANY of the other five means the file
itself has a real content-integrity problem (missing/corrupted stream,
undecodable frames) that padding/trimming cannot fix and must not be forced
through as if it succeeded. `FFmpegRenderService.IsDurationOnlyFailure` draws
this line explicitly:

```csharp
internal static bool IsDurationOnlyFailure(RenderVerificationResult result) =>
    !result.DurationWithinTolerance
    && result.HasExpectedVideoStream
    && result.HasExactlyOneAudioStream
    && result.FirstFrameDecodable
    && result.MiddleFrameDecodable
    && result.LastFrameDecodable;
```

A non-duration-only failure still throws `RenderVerificationException`
immediately, unchanged from before this work.

## The three-tier self-correction loop

`FFmpegRenderService.CorrectDurationOnlyFailureAsync` runs after the normal
render (including the pre-existing HW/SW execution fallback and memory-driven
batch halving, both untouched) produces a file that fails verification on
duration alone. Each tier re-verifies before deciding whether to continue;
the loop stops the moment verification passes, or the moment a re-verification
surfaces a DIFFERENT (non-duration-only) problem a duration fix cannot
address. Bounded to exactly these three named tiers (CLAUDE.md rule 6 - no
unbounded loop):

1. **Same-encoder retry** - re-run the exact same render once more. Cheap
   insurance against transient/non-deterministic encoder timing jitter;
   worth trying because time is explicitly not a constraint here.
2. **Forced software-encoder retry** - re-run the whole render forced onto
   a software encoder (bypassing hardware-encoder selection). Deterministic,
   and addresses hardware-encoder frame-timing drift as the likelier root
   cause of a real-world duration miss.
3. **Frame-exact remux** (`RenderDurationCorrector`) - re-processes the
   already-assembled output file directly: `tpad=stop_mode=clone:stop_duration=<target>`
   pads (clones the trailing frame) generously past the target if the file
   is short - a no-op if it is not - followed by
   `trim=start_frame=0:end_frame=<exact frame count>` to cut to EXACTLY the
   planned frame count, and the audio-side equivalent
   (`apad=whole_dur=<target>,atrim=duration=<target>`). This is
   **guaranteed effective by construction**, not by hoping a re-encode
   lines up differently - the same principle
   `RenderFilterGraphBuilder`'s own per-segment frame-domain trim already
   uses (see its own remarks), applied once more to the finished file. The
   remuxed copy replaces the output via a single `File.Move(..., overwrite:
   true)` (not delete-then-move): if that replace fails, the already-valid
   original render stays in place rather than both copies being lost - see
   the Phase 18 review in `PHASE_REPORT.md`.

Every tier taken is recorded on `RenderResult.DurationCorrections`
(`RenderDurationCorrectionEvent`, one entry per tier attempted) and written
to `System.Diagnostics.Trace` - visible and debuggable, but never surfaced to
the UI as an error. A non-empty `DurationCorrections` list means the render
still succeeded.

## Design tradeoffs (resolved with the product owner before implementing)

Two genuinely open questions were put to the product owner before writing
any of this:

1. **How many full re-render attempts before the corrective remux?**
   Chose same-encoder retry + forced-software retry (3 total render
   attempts) over skipping straight to the software retry, since the extra
   attempt is cheap given "time is not a concern" and guards against
   transient timing jitter a purely-deterministic tool wouldn't otherwise
   explain.
2. **Should the last-resort correction force an exact match via frame
   trim/pad, or surface a rare calm message instead?** Chose to force the
   exact match (logged internally, never shown to the user) - it directly
   satisfies "the render must always end in a successful video, no matter
   what," and is deterministic by construction rather than a gamble.

## Real-ffmpeg verification

A genuine render of correctly-planned segments always reaches the same real
duration regardless of encoder identity or how many identical retries run
it - so `FFmpegRenderServiceIntegrationTests.RenderAsync_RealFfmpeg_PlannedDurationDeliberatelyWrong_SelfCorrectsToTheExactPlannedDurationWithoutThrowing`
proves the whole loop against real ffmpeg by giving `RenderPlan` a
deliberately WRONG `PlannedVideoDuration` (0.5s off from what the real
segments actually produce) - a reproducible way to force a genuine,
unfakeable duration-only miss that tiers 1 and 2 cannot fix (they re-render
the same real content), driving the loop to tier 3. Measured:

```
Honest segment duration=00:00:00.6800000, deliberately-wrong PlannedVideoDuration=00:00:01.1800000
DurationCorrections: SameEncoderRetry, ForcedSoftwareEncoderRetry, FrameExactRemux
Final verification: valid=True, actual=00:00:01.2000000, expected=00:00:01.1800000, delta=00:00:00.0200000
```

`RenderAsync` returned successfully - no `RenderVerificationException` -
and the corrected file's own re-probed real ffprobe duration landed within
one frame (0.04s at 25fps; measured delta 0.02s) of the deliberately wrong
target, proving the corrective mechanism genuinely reshapes real encoded
output to hit an arbitrary target duration, not merely a fake-mocked one.

## Calm failure messaging (RenderProgressViewModel)

Everything that still reaches the UI after the above is, by construction, a
genuine content-integrity or environment problem (corrupted/missing stream,
disk full, ffmpeg missing, permission denied, etc.) - not a duration
hiccup. Two changes:

- **Fixed a real gap**: `FfmpegToolsNotFoundException` and
  `FfmpegToolsIncompatibleException` were not in
  `IsRecognizedRenderFailure`'s type list, so a missing/incompatible ffmpeg
  install propagated as an *unhandled* crash - strictly worse than the red
  screen this whole change is about avoiding. Both are now recognized.
- **`BuildFriendlyErrorMessage`** maps every recognized exception type to
  calm, plain-language text explaining what's wrong and what the user can
  do (e.g. "There isn't enough free disk space to finish this render. Free
  up some space on your drive and try again.") instead of `ex.Message`
  (which can carry exit codes and ffmpeg stderr excerpts). `StatusText` on
  failure changed from "Render failed." to "We couldn't finish your
  render." The error text's XAML style (`RenderProgressView.xaml`) switched
  from `ErrorText` (red `Brush.Error`, used elsewhere for genuine crashes)
  to a new `NoticeText` style (amber `Brush.Warning`, already used
  elsewhere in the app for non-fatal notices) - scoped to this one screen,
  not a repo-wide error-styling change.

## Tests added

In addition to (not replacing) the existing suite:

- `FFmpegRenderServiceTests`: an `IsDurationOnlyFailure` theory covering
  every flag-combination boundary, plus five `RenderAsync` scenarios against
  fakes - tier-1-recovers, tier-1-fails-tier-2-recovers,
  all-three-tiers-then-succeeds, all-three-tiers-exhausted-then-throws (proving
  the loop is bounded, not infinite), and a non-duration-only failure that
  confirms zero correction tiers ever run.
- `RenderDurationCorrectorTests` (new file): argument-level coverage of the
  ffmpeg invocation the frame-exact remux builds (fps/tpad/trim/apad/atrim,
  encoder/codec/bitrate arguments), the real-file swap and temp-file
  cleanup, the ffmpeg-failure path (throws `RenderExecutionException`,
  leaves the original output untouched), and - added in the Phase 18
  review - a failed *swap* leaving the already-valid render in place rather
  than losing both copies.
- `FFmpegRenderServiceIntegrationTests.RenderAsync_RealFfmpeg_PlannedDurationDeliberatelyWrong_SelfCorrectsToTheExactPlannedDurationWithoutThrowing`
  (new, real ffmpeg) - see "Real-ffmpeg verification" above.
- `RenderProgressViewModelTests`: the existing recognized-failure test now
  asserts the message is calm (no exit codes/stderr) rather than the raw
  exception text; new tests cover both tool-locator exceptions being
  recognized (regression coverage for the unhandled-crash gap) and the
  disk-space message specifically mentioning disk space without raw byte
  counts.

## Full-suite verification

```
dotnet format SceneForge.sln --verify-no-changes  -> No formatting violations.
dotnet build SceneForge.sln --configuration Debug -> Build succeeded. 0 Warning(s). 0 Error(s).
```

| Project | Before this pass | After |
|---|---:|---:|
| SceneForge.Core.Tests | 8 passed | 8 passed |
| SceneForge.Accuracy.Tests | 31 passed | 31 passed |
| SceneForge.App.Tests | 78 passed | 81 passed |
| SceneForge.Infrastructure.Tests | 46 passed | 46 passed |
| SceneForge.Media.Tests | 550 passed | 567 passed |

Net +20 tests (3 in `SceneForge.App.Tests`, 17 in `SceneForge.Media.Tests`
across the new `RenderDurationCorrectorTests` file and the duration-correction/
real-ffmpeg additions to the existing render test files), zero regressions
in any pre-existing test.

The Phase 18 strict release review (`PHASE_REPORT.md`) then added one more
`RenderDurationCorrectorTests` case for the failed-swap safety fix and
re-ran the full suite in Debug **and** Release (737/737).

## What was deliberately NOT touched

Per the task's own constraints: `TimelinePlanner`'s duration-guarantee logic
(Phase 16), the render plan's frame-quantization work (Task 1's
`RenderPlanBuilder`/`RenderFilterGraphBuilder` fixes), the adaptive
memory-driven batch-halving retry, and input-seeking/hardware-encoder
*selection* logic are all unchanged - this adds a new, independent
correction layer strictly after verification, rather than modifying any of
those existing guarantees.

## Limitations

- Tier 3's corrective remux re-decodes and re-encodes the entire assembled
  output once more - a second lossy generation pass. Expected to be rare in
  practice (tiers 1-2 resolve genuine transient/hardware-timing causes; the
  real-ffmpeg test above only reaches tier 3 because it uses a deliberately,
  artificially wrong target that no real render would ever naturally
  produce), but is a real, disclosed quality tradeoff versus never applying
  it.
- Per CLAUDE.md rule 10: this does not claim rendered duration is "exact" in
  an absolute sense - `RenderVerificationResult`'s one-frame tolerance and
  itemized `DurationDelta`/`DurationTolerance` fields are unchanged and
  still always reported on `RenderResult.Verification`, corrected or not.
