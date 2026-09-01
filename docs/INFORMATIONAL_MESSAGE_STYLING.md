# Informational (non-error) message styling

Some user-visible messages describe context, not a failure: the operation
succeeded, and the text only explains *why* something happened. Styling
those in red (`ErrorText` / `Brush.Error`) misreads them as problems. This
change re-styles the non-error messages calmly while leaving genuine
errors red.

## Rule

- **Genuine error / failure** → `ErrorText` (red `Brush.Error`, SemiBold).
  Unchanged: validation failures, `ErrorMessage` bindings, output
  verification failures, startup-diagnostic remediation guidance,
  timeline **shortfall** (target duration not reached).
- **Non-error informational notice** → `NoticeText` (amber `Brush.Warning`),
  the same calm style the render screen already uses for
  recoverable/plain-language notices.

No processing logic changed - only the visual style of the affected
`TextBlock`s.

## Changes

### Timeline Summary - "significant repetition" message

`TimelineFeasibilityWarning` has two `Kind`s (see
[PHASE_16_REPORT.md](PHASE_16_REPORT.md)):

- `SignificantRepetition` - the target duration **was** matched exactly;
  the message only explains that clips had to repeat more than requested
  to fill the audio length. `TimelinePlan.IsComplete` is `true` and the
  green "Exactly matches the requested audio duration." line is shown
  directly below it.
- `Shortfall` - the target duration was **not** reached. A real problem.

Previously both rendered with `ErrorText`. Now `TimelineSummaryViewModel`
exposes `FeasibilityWarningIsError` (`true` only for `Shortfall`), and
`TimelineSummaryView.xaml` styles the message as `NoticeText` by default,
switching to red `Brush.Error` only when `FeasibilityWarningIsError` is
`true`. The green duration-match confirmation is unchanged.

### Completion - "fell back to the software encoder" message

Shown on the **Render complete** screen after a successful render whose
hardware encoder failed mid-run and was replaced by the software encoder.
The render succeeded and the output is valid, so this is informational
context, not an error. Switched from `ErrorText` to `NoticeText` in
`CompletionView.xaml`. Genuine output-verification failures on the same
screen keep `ErrorText`.

## Tests

`TimelineSummaryViewModelTests`:

- `BuildPlan_SignificantRepetitionWarning_ExposesMessageButNotAsError` -
  message is exposed, `FeasibilityWarningIsError` stays `false`.
- `BuildPlan_ShortfallWarning_IsExposedAsError` - `FeasibilityWarningIsError`
  is `true`.
- `Constructor_ReviewedClipsCoveringTargetDuration_BuildsCompletePlan` also
  asserts `FeasibilityWarningIsError` is `false` for a clean plan.

Full suite: 735 passing (Core 8, Accuracy 31, Infrastructure 46, App 83,
Media 567).

## Screenshots

`docs/screenshots/step18/`:

- `04_timeline_summary_repetition_light.png` / `_dark.png` - repetition
  message in amber, green confirmation below it.
- `04_timeline_summary_shortfall_light.png` - genuine shortfall still red.
- `06_completion_software_fallback_light.png` - encoder-fallback notice in
  amber.
