# Phase 10 Report — WPF UI (MVVM, DI, Full Workflow)

Date: 2026-08-23

## Scope

Build the production Windows desktop UI on top of the media pipeline every
prior phase built (`SceneForge.Media`, which had no UI surface until now —
every phase 4-9 report noted this explicitly). The UI is native WPF on
.NET 8, MVVM throughout (CommunityToolkit.Mvvm source-generated
`ObservableObject`/`RelayCommand`), composed via `Microsoft.Extensions.DependencyInjection`,
and implements the required eight-screen workflow end to end: Welcome/Import
→ Analysis Settings → Analysis Progress → Scene Review → Timeline Summary →
Export Settings → Render Progress → Completion. No web view, no browser
control, no code-behind processing logic — every screen's `.xaml.cs` is a
single `InitializeComponent()` call and nothing else.

Also in scope, because the UI cannot function without it: a small,
test-first pipeline-glue component in `SceneForge.Media` itself
(`Planning.SceneRangeCalculator`) that turns `ITransitionDetector`'s output
into `ICleanClipExtractor`'s input — this bridge did not exist before this
phase because no caller needed it until now.

Explicitly **not** in scope: changing any existing `SceneForge.Media`
processing behavior (this phase only adds one new pure calculator; every
existing algorithm is untouched), a live/runtime dark-light theme toggle
(the OS theme is read once at startup — see Outstanding), and packaging/
installer work (still out of scope, as every prior phase's report has
noted for itself).

## Repository layout produced

```
src/SceneForge.App/
  App.xaml, App.xaml.cs                        - DI composition root, startup theme
  MainWindow.xaml(.cs)                          - shell: step indicator, back nav, ContentControl
  Navigation/
    WorkflowStep.cs, IWorkflowNavigator.cs, WorkflowNavigator.cs
  Session/
    WorkflowSession.cs                          - state shared across steps (+ ClipReviewOverride)
  Services/
    IDialogService.cs, DialogService.cs
    IThumbnailCacheService.cs, ThumbnailCacheService.cs
  ViewModels/
    MainWindowViewModel.cs
    WelcomeImportViewModel.cs, AnalysisSettingsViewModel.cs (+FrameRateOption)
    AnalysisProgressViewModel.cs
    SceneReviewViewModel.cs, ClipReviewItemViewModel.cs
    TimelineSummaryViewModel.cs (+TimelinePlacementRowViewModel)
    ExportSettingsViewModel.cs (+ResolutionOption)
    RenderProgressViewModel.cs
    CompletionViewModel.cs
  Views/
    WelcomeImportView, AnalysisSettingsView, AnalysisProgressView,
    SceneReviewView, TimelineSummaryView, ExportSettingsView,
    RenderProgressView, CompletionView (.xaml + trivial .xaml.cs each)
  Behaviors/
    DragDropImportBehavior.cs                   - attached DropCommand for drag/drop import
    LazyThumbnailLoader.cs                       - attached IsEnabled, loads on container Loaded
  Converters/
    TimeSpanToClockStringConverter, EditableTimeSpanConverter,
    ConfidenceToPercentStringConverter, EnumToSpacedStringConverter,
    NullToVisibilityConverter, WorkflowStepToDisplayConverter
  Themes/
    Colors.Light.xaml, Colors.Dark.xaml, Styles.xaml, DataTemplates.xaml

src/SceneForge.Media/Planning/
  SceneRangeCalculator.cs                        - (+SceneRangeCalculationResult, SceneBoundaryTransitions)

tests/SceneForge.App.Tests/
  Navigation/WorkflowNavigatorTests.cs (6)
  ViewModels/
    WelcomeImportViewModelTests.cs (7), AnalysisSettingsViewModelTests.cs (2),
    AnalysisProgressViewModelTests.cs (3), SceneReviewViewModelTests.cs (8),
    TimelineSummaryViewModelTests.cs (4), ExportSettingsViewModelTests.cs (5),
    RenderProgressViewModelTests.cs (3), CompletionViewModelTests.cs (4),
    MainWindowViewModelTests.cs (10, Theory-driven)
  TestSupport/
    MediaInfoBuilder, CleanClipBuilder, TransitionDetectionBuilder,
    FakeFfprobeService, FakeTransitionDetector, FakeCleanClipExtractor,
    FakeFFmpegRenderService, FakeDialogService, FakeThumbnailCacheService

tests/SceneForge.Media.Tests/Planning/
  SceneRangeCalculatorTests.cs (10)

docs/screenshots/
  01_welcome_import.png, 02_analysis_settings.png, 03_scene_review.png,
  04_timeline_summary.png, 05_export_settings.png, 06_completion.png
```

New package references: `CommunityToolkit.Mvvm` and
`Microsoft.Extensions.DependencyInjection` (`Directory.Packages.props`,
centrally versioned like every other dependency in this repo). No other
project's package set changed.

## Design summary

### `WorkflowSession` is the one piece of App-layer state that outlives a screen

Every step's ViewModel is resolved **transient** from DI on each
`IWorkflowNavigator.StepChanged` (`MainWindowViewModel.OnStepChanged`), so a
ViewModel instance itself never survives navigating away and back. Anything
a later step needs from an earlier one — imported file paths/`MediaInfo`,
analysis results, the user's Scene Review edits, the built plans — lives on
`Session.WorkflowSession`, a single DI singleton. This is why Scene Review's
edits are never lost by navigating to Timeline Summary and back:
`SceneReviewViewModel` persists every `IsIncluded`/boundary change into
`WorkflowSession.ClipOverrides` immediately (`OnClipOverrideChanged`), and
re-reads that dictionary when reconstructed.

### `SceneRangeCalculator` is the literal Detection→Extraction bridge, and it needed its own tests before the UI could use it

`ITransitionDetector` (Phase 6) produces `TransitionDetection` intervals;
`ICleanClipExtractor` (Phase 7) consumes `SceneRanges`/`ExcludedIntervals`.
Nothing in `SceneForge.Media` ever converted one into the other — every
prior phase's tests supplied `SceneRanges` as a caller-given fact. The App
layer is the first real caller that has only detections and needs scene
ranges, so this phase adds `Planning.SceneRangeCalculator.Calculate`: pure,
synchronous, clamps every detection's `[Start, End]` to `[0, totalDuration]`,
turns it into an `ExcludedInterval` (`Kind = Transition`, `Reason` = the
detected type's name), and the complement of the (sorted, cursor-merged)
exclusion set becomes `SceneRanges` — never emitting a degenerate zero/
negative-length range. It also returns `SceneBoundaryTransitions` (the
`Leading`/`Trailing` detection immediately bounding each scene range, or
`null` at either end) purely for Scene Review's "entering/exiting
transition" display — `CleanClipExtractor` itself never sees this, only
`ExcludedIntervals`. Following CLAUDE.md rule 8, this was written test-first:
10 cases (no detections, one mid-clip detection, a detection at the very
start/end, one spanning the whole duration, out-of-order input, overlapping
detections, clamping past the source duration, and the two guard-clause
error cases) were run and confirmed green before any ViewModel used it — see
Self-review findings for the one real bug this caught later, downstream.

### Frame rate is fixed once in Analysis Settings, never re-editable in Export Settings

`RenderOutputSpec.FrameRate` must equal the `RationalFrameRate` originally
passed as `TimelinePlanRequest.OutputTimeBase` (Phase 9's own documented
caller invariant — `TimelinePlan` does not carry it back out, so nothing in
`SceneForge.Media` can verify agreement itself). Rather than re-exposing a
frame-rate picker on Export Settings and risking the two silently
disagreeing, `WorkflowSession.OutputFrameRate` is set exactly once
(`AnalysisSettingsViewModel.StartAnalysisCommand`) and Export Settings only
displays it read-only (`ExportSettingsViewModel.FrameRateSummary`). The same
reasoning is why Welcome/Import collects **both** the source video and the
background audio track up front (not deferred to Export Settings): planning
needs the audio's real, probed duration as `TimelinePlanRequest.TargetAudioDuration`
before Timeline Summary can build a plan, and Timeline Summary is required
to come before Export Settings in the fixed workflow order.

### Scene Review virtualization is real, not cosmetic — and it drove one layout decision in `MainWindow.xaml`

`VirtualizingPanel.VirtualizationMode="Recycling"` (set on the app-wide
`ListView` style, `Themes/Styles.xaml`) only keeps container/thumbnail
instantiation bounded to visible rows if the `ListView`'s own immediate
`ScrollViewer` has bounded height. A virtualizing panel handed effectively
infinite height by an ancestor `ScrollViewer` measures (and therefore
realizes) every item up front, silently defeating virtualization. Because of
this, `MainWindow.xaml`'s shell deliberately does **not** wrap its
`ContentControl` in an outer `ScrollViewer` — every other screen wraps its
own content in one where it might overflow, but Scene Review's `Grid` gives
its `ListView` a `*`-sized row instead, so the `ListView` (and only it) gets
a real, bounded viewport. `ClipReviewItemViewModel` is deliberately
lightweight to construct (only the already-computed `CleanClip`/`ClipScore`
facts plus a handful of bindable fields) and never loads its thumbnail
itself; `Behaviors.LazyThumbnailLoader` triggers the load from the row's
`Image.Loaded` event, which the virtualizing panel only raises once a row is
actually realized (and can re-raise on recycling), so a source with
thousands of candidate clips never has thousands of live decoded bitmaps at
once (CLAUDE.md rule 6/7).

### Thumbnails are ffmpeg single-frame extractions, bounded and disk-cached — never a decoded frame kept in .NET memory per row

`Services.ThumbnailCacheService` spawns `ffmpeg -ss <t> -i <source> -frames:v 1 -vf scale=160:-2 -q:v 4 <cache>.jpg`
(input-side seeking, deliberately not frame-accurate — thumbnails don't need
it, and it is far cheaper) through the same `IProcessRunner`/`IFfmpegToolLocator`
abstractions every other ffmpeg-touching component in this repo already
uses. Concurrency is capped by a fixed `SemaphoreSlim(4)` — never one ffmpeg
process per row spawned at once — and the disk cache
(`%LOCALAPPDATA%\SceneForge\ThumbnailCache`) is swept back to a target size
whenever it exceeds a hard file-count cap, so neither concurrent process
count nor disk usage is unbounded (CLAUDE.md rule 6/7). A cache key folds in
the source file's own path, size, and last-write-time alongside the
requested timestamp, so a stale entry is never served for a source file that
has since changed on disk. Generation failures return `null`, never throw
into the UI — a missing thumbnail degrades to a "No preview" placeholder,
it never blocks the review workflow.

### Dark/light-safe colors and accessible focus, applied through one indirection

Every color anywhere in the app resolves through a shared semantic
`DynamicResource` key (`Brush.TextPrimary`, `Brush.PanelBackground`, ...)
defined twice — `Themes/Colors.Light.xaml` and `Themes/Colors.Dark.xaml` —
never as a literal `Color`/`Brush` in a View or in `Styles.xaml`.
`App.OnStartup` reads `HKCU\...\Themes\Personalize\AppsUseLightTheme` once
and inserts the matching dictionary before the window is shown, so the
whole application (not just individual controls) renders in the palette
that already matches the user's OS setting. `AccessibleFocusVisual`
(`Styles.xaml`) replaces WPF's default (sometimes near-invisible on a dark
background) focus adorner with a visible dashed outline, applied to every
interactive control via `BaseControlStyle`, and every interactive element
has an explicit `AutomationProperties.Name` distinct from its visible label
where the label alone would be ambiguous to a screen reader (e.g. "Browse
for a source video file" vs. the two "Browse..." buttons' shared visible
text).

### Cancellation is real, not a UI-only flag

`AnalysisProgressViewModel` and `RenderProgressViewModel` each own a
`CancellationTokenSource` created fresh per run and pass its token through
every awaited call into `SceneForge.Media` (`ITransitionDetector.DetectAsync`,
`ICleanClipExtractor.ExtractAsync`, `IFFmpegRenderService.RenderAsync`) —
the same tokens those methods already thread down into `IProcessRunner`'s
real process-tree-kill behavior (Phase 4). Both ViewModels implement
`IDisposable` to dispose that source (a static-analysis-driven fix — see
Self-review findings) and both are covered by a dedicated cancellation test
using a gated fake service (`TaskCompletionSource`-based) so the test can
call `Cancel` while the operation is still genuinely in flight, rather than
only asserting on a synchronously-completing fake.

## Manual end-to-end verification (real ffmpeg, real UI, real render)

Beyond the automated test suite, the running application was driven
end-to-end through Windows UI Automation against the real, built
`SceneForge.App.exe`, with real `ffmpeg`/`ffprobe` binaries (this
workstation has them installed, unlike the environment every phase 4-9
report described) placed at the path `FfmpegToolLocator` expects. A
synthetic 24-second test video (two `lavfi` segments with a hard cut at the
midpoint, via `ffmpeg -f lavfi testsrc2`/`color`) plus a matching sine-wave
audio track were imported through the real file-picker dialogs
(`Microsoft.Win32.OpenFileDialog`, driven via `SendKeys` once focused) and
carried through every one of the eight screens for real:

1. **Welcome/Import** — both files probed via the real `FfprobeService`,
   summaries updated (`sample: 0:24`).
2. **Analysis Settings** — profile/frame-rate/seed selection.
3. **Analysis Progress** — real `TransitionDetector`/`CleanClipExtractor`
   runs found 3 candidate clips around the synthetic hard cut (Zoom/Blur
   classifier hits on the synthetic content — expected, since `testsrc2`'s
   own internal motion is not a clean fixture the way Phase 6's dedicated
   synthetic matrix is).
4. **Scene Review** — all 3 clips rendered with real generated thumbnails
   (see Self-review findings for the bug this step caught), entering/
   exiting transition type and confidence shown per row, one clip manually
   included via its checkbox.
5. **Timeline Summary** — real `TimelinePlanner` output, including a
   correctly-surfaced `FeasibilityWarning` ("Requested 24.00s but only
   5.00s is achievable from 1 clip(s)...") since only one clip was included.
6. **Export Settings** — resolution/fit-mode selection, real `SaveFileDialog`.
7. **Render Progress** — real `FFmpegRenderService.RenderAsync`, which
   probed NVENC/Quick Sync/AMF (none available on this workstation) and
   correctly fell back to `libx264`.
8. **Completion** — real `RenderResult`: `libx264 (software)`, elapsed
   `0:04.8`, and `RenderVerificationResult.IsValid = true` ("Every check
   passed: expected video/audio streams present, duration within tolerance,
   first/middle/last frames decodable").

The resulting file was independently re-probed with `ffprobe` outside the
app and confirmed to be a real, playable 5.000-second H.264/AAC MP4 —
exactly `TimelinePlan.PlannedDuration` for the one included clip. Screenshots
of steps 1, 2, 4 (with thumbnails), 5, 6, and 8 are in `docs/screenshots/`.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| `InvariantGlobalization=true` (repo-wide default, `Directory.Build.props`) is incompatible with WPF | The built app launched (process alive, `Responding: True`, an `HWND` created with the correct `CenterScreen`-computed position/size) but the window never became visible — no crash dialog, no console output, nothing actionable from the outside. Added temporary file-logging around `App.OnStartup` and the `DispatcherUnhandledException` handler and found the real cause: `System.InvalidOperationException: Cannot find non-neutral culture related to 'en-us'` thrown from `System.Windows.Data.BindingExpression` → `XmlLanguage.GetSpecificCulture()` on the very first data-bound layout pass, caught by the app's own unhandled-exception handler (which itself needs WPF's binding/culture machinery to show its `MessageBox`, so the error was invisible even to that safety net). WPF's binding engine needs real per-culture data that invariant globalization mode strips out. | Overrode `InvariantGlobalization` back to `false` for `SceneForge.App.csproj` and `SceneForge.App.Tests.csproj` specifically (both reference WPF binding/markup types), documented inline in each `.csproj` — every other project in the solution keeps the repo-wide `true` default unchanged. Confirmed by relaunching: window now shows immediately and every screenshot in this report was captured from the real running window. |
| Thumbnail temp file could never succeed | `ThumbnailCacheService` wrote its in-progress ffmpeg output to `<hash>.jpg.tmp-<guid>` before an atomic rename to `<hash>.jpg`. Every real generation attempt failed with ffmpeg's `Unable to choose an output format for '...jpg.tmp-...'` — ffmpeg infers its output muxer from the path's own extension, and `.tmp-<guid>` (with no recognized suffix) is not one. This was invisible from the UI alone: `GetThumbnailAsync` swallows generation failures by design (a missing thumbnail is meant to degrade gracefully to a placeholder, never block the review workflow — see Design summary), so Scene Review simply showed "No preview" for every row with no error surfaced anywhere. Caught during manual end-to-end verification (Scene Review screenshots kept showing empty thumbnails against a real synthetic video that definitely has visible content), not by the automated test suite — `FakeThumbnailCacheService` always returns `null` by design and so could not have caught this; the real fix required adding temporary diagnostic logging around the real `ThumbnailCacheService` and inspecting ffmpeg's own `stderr`. | Changed the temp-file naming to preserve the `.jpg` extension at the very end (`<hash>.tmp-<guid>.jpg` instead of `<hash>.jpg.tmp-<guid>`). Re-verified against the real running app: the thumbnail cache directory now contains real generated JPEGs, and Scene Review's rows show the actual synthetic video content (the `testsrc2` frame for the first clip, solid blue for the other two) instead of placeholders — see `docs/screenshots/03_scene_review.png`. |
| `AnalysisProgressViewModel`/`RenderProgressViewModel` own a `CancellationTokenSource` but were not `IDisposable` | `CA1001` (own disposable field(s) but type is not disposable) failed the build the first time `TreatWarningsAsErrors` ran against these two new ViewModels. | Both now implement `IDisposable`, disposing the token source. DI-resolved transient instances are tracked and disposed by the root `ServiceProvider` at app shutdown, consistent with how this repo's DI container is used elsewhere in this phase. |
| `TimeSpan.ToString(format)` without an explicit culture (`CA1305`) | Several `FormatClock`/`Describe` helpers called the culture-sensitive `TimeSpan.ToString(string)` overload directly - locale-dependent behavior the analyzer flags once `InvariantGlobalization` is `false` (see above) and real culture data is actually in play. | Every such call now passes `CultureInfo.InvariantCulture` explicitly, matching the convention `SceneForge.Media`'s own formatting code (e.g. `RationalFrameRate.ToString`) already uses. |
| A parameter named `step` on the `IWorkflowNavigator.NavigateTo` interface member (`CA1716`) | `step` collides with the reserved-in-some-languages identifier `Step`; the implementation's matching parameter name then separately tripped `CA1725` (implementation parameter name must match the interface's). | Renamed to `targetStep` on both the interface and `WorkflowNavigator`'s implementation. |
| Newly authored files used LF line endings | Same class of issue Phase 9's report already recorded for itself — `.editorconfig` pins `end_of_line = crlf`, and every file in this phase was authored with `\n`. | `dotnet format SceneForge.sln` (apply mode), then `--verify-no-changes` confirmed clean; `git status` afterward showed only this phase's own new/changed files touched, no incidental reformatting elsewhere. |

All six were caught before this report was written — the first two only
because the running application was actually launched, driven through every
screen, and its screenshots inspected (not merely because the code compiled
and the automated ViewModel tests passed), the middle three by the
compiler's own analyzer gate, and the last by the formatting gate CLAUDE.md
rule 13 requires. This is the same discipline every prior phase's
self-review section has documented for itself, applied here to a class of
bug (a real window that silently never becomes visible) that unit tests
structurally cannot catch — only running the actual GUI can.

## Test inventory (new this phase)

**`SceneForge.Media.Tests`** — 10 new tests (`Planning/SceneRangeCalculatorTests.cs`):
no detections, one mid-clip detection with matching boundary transitions on
both resulting scenes, a detection at the very start (no leading scene), a
detection at the very end (no trailing scene), a detection spanning the
entire duration (zero scene ranges), out-of-order input sorted before
processing, overlapping detections merged without the cursor moving
backward, a detection exceeding the total duration clamped rather than
throwing, and the two argument-validation error cases (negative duration,
null detections).

**`SceneForge.App.Tests`** (new project) — 52 tests across 9 classes:

- **WorkflowNavigatorTests** (6) — initial state, forward navigation raises
  `StepChanged` and updates `CanGoBack`, same-step navigation is a no-op,
  multi-step back navigation returns steps in the correct reverse order,
  `GoBack` with empty history is a no-op, `Reset` clears history and returns
  to `WelcomeImport`.
- **WelcomeImportViewModelTests** (7) — a valid video import updates the
  session and summary, a file with no video stream sets `ErrorMessage`
  without touching the session, `ContinueCommand.CanExecute` only becomes
  true once both video and audio are imported, `Continue` navigates to
  Analysis Settings, a cancelled file dialog leaves state untouched, and
  constructing against a session that already has an imported file prefills
  the summary.
- **AnalysisSettingsViewModelTests** (2) — default selections match session
  defaults, `StartAnalysisCommand` commits every selection into the session
  and navigates.
- **AnalysisProgressViewModelTests** (3) — the full pipeline (probe →
  detect → `SceneRangeCalculator` → extract) runs to completion and
  navigates to Scene Review, a recognized failure from the detector sets
  `ErrorMessage` and does not navigate, and — using a gated fake detector so
  the operation is genuinely still in flight — `Cancel` stops the run and
  reports "Analysis canceled." without navigating.
- **SceneReviewViewModelTests** (8) — missing extraction result sets an
  error and an empty list, accepted/rejected clips combine into one list
  defaulting to their automatic verdict, toggling `IsIncluded` persists into
  `Session.ClipOverrides` and updates the count, a valid boundary edit
  persists the adjusted range, an edit that makes the boundary invalid falls
  back to the clip's original range in the session (never a corrupt saved
  range), `Continue`'s `CanExecute` requires at least one included+valid
  clip, `Continue` builds `ReviewedClips` from only included+valid rows
  using their adjusted ranges and navigates, and revisiting the screen
  restores prior edits from the session.
- **TimelineSummaryViewModelTests** (4) — no reviewed clips sets an error
  and disables `Continue`, a real `TimelinePlanner` run against clips
  covering the target duration produces a complete plan with no feasibility
  warning, `Reshuffle` increments the seed and rebuilds the plan into the
  session, `Continue` navigates to Export Settings.
- **ExportSettingsViewModelTests** (5) — default resolution/fit-mode
  selection, browsing sets the output path from the dialog, `Continue`'s
  `CanExecute` requires an output path, a real `RenderPlanBuilder` run
  builds a plan into the session and navigates, and a source video shorter
  than what the plan's placements require surfaces `RenderPlanBuilder`'s
  own `RenderPlanException` as `ErrorMessage` without navigating.
- **RenderProgressViewModelTests** (3) — a real render (via a fake
  `IFFmpegRenderService`) completes and navigates to Completion, a
  recognized render failure sets `ErrorMessage` without navigating, and —
  same gated-fake pattern as analysis — `Cancel` stops an in-flight render
  and reports "Render canceled."
- **CompletionViewModelTests** (4) — a present `RenderResult` is reflected
  in every displayed field, a missing one falls back to the session's output
  path with an "Unknown" encoder, `OpenOutputFolderCommand` reveals the
  output path via `IDialogService`, `StartOverCommand` resets both the
  session and the navigator.
- **MainWindowViewModelTests** (10, `Theory`-driven across all 8 steps plus
  2 back-navigation-allowed cases) — every `WorkflowStep` resolves the
  correct concrete ViewModel type from DI, and `IsBackAllowed` is false on
  Render Progress/Completion even with navigation history, true on an
  ordinary step with history.

Verified via `dotnet test SceneForge.sln` in both Debug and Release: the
Phase 9 baseline was 474 total (464 passed + 10 skipped, `SceneForge.Media.Tests`
only). This phase's full-solution total is **537** (`SceneForge.Core.Tests`
1, `SceneForge.App.Tests` 52, `SceneForge.Media.Tests` 484 — 474 passed + 10
pre-existing real-binary skips, unrelated to this phase — plus the new
`SceneRangeCalculatorTests`), all passing in both configurations, run
repeatedly for stability (one isolated, non-reproducing flake was observed
in an unrelated pre-existing `SceneForge.Media.Tests` case on one run out of
several; two immediate reruns were clean, and nothing in this phase touches
that test or the component it covers).

## Compliance notes against CLAUDE.md

- **Rule 1-2** (native WPF, no Electron/web/cloud/telemetry/runtime
  network): satisfied — pure WPF/.NET 8, `Microsoft.Win32` common dialogs,
  local `ffmpeg`/`ffprobe` processes only. The shell's footer
  (`MainWindow.xaml`) states "SceneForge runs entirely on this computer. No
  internet connection is used or required." on every screen, always
  visible, not just at startup.
- **Rule 3** (FFmpeg/FFprobe/OpenCvSharp basis): every media operation the
  UI triggers goes through the existing `SceneForge.Media` interfaces
  (`IFfprobeService`, `ITransitionDetector`, `ICleanClipExtractor`,
  `ITimelinePlanner`, `IRenderPlanBuilder`, `IFFmpegRenderService`), plus one
  new direct ffmpeg use in this phase — `ThumbnailCacheService`'s
  single-frame extraction — via the same `IProcessRunner`/`IFfmpegToolLocator`
  abstractions. No new OpenCvSharp/pixel-level processing was added in this
  phase (thumbnails are ffmpeg's own scale filter, not client-side resizing).
- **Rule 4** (clean architecture): `SceneForge.App` depends on
  `SceneForge.Media` (and, transitively, `Core`/`Infrastructure`), never the
  reverse; `SceneForge.Media.Planning.SceneRangeCalculator` has no knowledge
  of the App layer. Every View's code-behind is exactly
  `public ViewName() => InitializeComponent();` — all logic lives in
  ViewModels/Services, verified by inspection of every `.xaml.cs` file in
  `Views/`.
- **Rule 5** (async cancellation/cooperative shutdown): `AnalysisProgressViewModel`/
  `RenderProgressViewModel` both create and thread a `CancellationTokenSource`
  through every awaited Media call and expose a `Cancel` command bound to
  each screen's Cancel button; both are covered by a dedicated
  gated-fake-based test proving cancellation actually interrupts an
  in-flight operation, not merely that a token is passed somewhere.
- **Rule 6-7** (bounded memory/concurrency, no full-video buffering):
  `ThumbnailCacheService` bounds concurrent ffmpeg thumbnail processes to 4
  (`SemaphoreSlim`) and sweeps its disk cache back to a target size past a
  hard file-count cap; `SceneReviewView`'s `ListView` uses
  `VirtualizingPanel.VirtualizationMode="Recycling"` inside a genuinely
  bounded-height container (see Design summary) so a large candidate-clip
  list never realizes more than the visible rows' worth of containers/
  thumbnails at once. No screen ever loads a decoded video frame or a
  full-resolution bitmap outside of a single small (160px-wide) thumbnail
  per visible row.
- **Rule 8** (test-first for algorithmic behavior):
  `Planning.SceneRangeCalculator` — the one new piece of actual
  computational logic this phase adds to `SceneForge.Media` — was written
  test-first (10 cases run and confirmed green before any ViewModel used
  it). ViewModel logic (navigation, session persistence, command
  enablement) is covered by 52 tests in the new `SceneForge.App.Tests`
  project, written and run before this report; the self-review section
  documents one class of defect (the invisible-window bug) that only
  running the real app caught, which is exactly why this phase's process
  included launching and driving the actual built executable, not only
  running the automated suite.
- **Rule 9** (benchmark every optimization): not applicable this phase —
  this is new UI functionality with no prior version to diff against
  (the same precedent every prior new-functionality phase has recorded for
  itself). No performance-sensitive optimization was made that needed
  before/after evidence; the one bounded-concurrency choice
  (`SemaphoreSlim(4)` for thumbnails) is a safety bound, not a tuned
  optimization.
- **Rule 10** (never claim absolute/opaque correctness): Scene Review shows
  `ClipScore`'s per-factor pass/fail (`ScoreReasons`/tooltip) and the exact
  transition confidence percentage for each boundary, never a bare accept/
  reject bit; Timeline Summary surfaces `TimelineFeasibilityWarning.Message`
  verbatim (a quantified shortfall, never hidden) and the exact planned-vs-
  target duration; Completion shows every itemized
  `RenderVerificationResult` check, listing exactly which failed when
  `IsValid` is false, and explicitly flags when a render fell back to the
  software encoder.
- **Rule 11-12** (preserve user files, output to new path only):
  `ExportSettingsViewModel` only ever writes to `IDialogService.ShowSaveVideoFileDialog`'s
  user-chosen path; `RenderPlanBuilder`/`FFmpegRenderService` (Phase 8/9,
  unchanged) independently re-validate that path never collides with either
  input file before any process spawns. The UI never offers to overwrite an
  imported source file.
- **Rule 13** (format/build/tests before ending): `dotnet format SceneForge.sln --verify-no-changes`
  clean (after the one round of `ENDOFLINE` fixes recorded in Self-review
  findings, the same class of issue Phase 9 recorded for itself); Debug and
  Release both build with 0 warnings/errors across all nine projects; Debug
  and Release both pass all tests (see Test inventory) — verified by
  actually launching, driving, and screenshotting the real running
  application, not only by the automated suite.
- **Rule 14** (update docs on behavior change): this report is that update.
  `docs/ARCHITECTURE_DECISIONS.md` needed no change (no new architectural
  decision beyond what is already on file — the UI is exactly the native
  WPF/clean-architecture shape Decisions 1-2 already committed to).
- **Rule 15** (don't advance while criteria fail): this phase's own
  criteria — the full required eight-screen workflow implemented and driven
  end to end against a real render, virtualization/thumbnail-bounding
  verified by inspection and by a real bug found and fixed through manual
  testing, ViewModel tests passing, formatting/build/tests clean in both
  configurations — are met as of this report, with the live-theme-switching
  and thumbnail-cache-size-tuning gaps both named explicitly under
  Outstanding rather than hidden.

## Known limitations / Outstanding for later phases

- **No live OS dark/light theme switching while running.** The theme is
  read once at startup (`App.OnStartup` → `ApplySystemColorTheme`) and never
  re-evaluated if the user changes their Windows theme while the app is
  open. Both palettes are fully implemented and correct (see
  `Themes/Colors.Light.xaml`/`Colors.Dark.xaml`) — only the runtime
  switching trigger is out of scope for this phase. A future pass could
  listen for `SystemParameters.StaticPropertyChanged`/`SystemEvents.UserPreferenceChanged`
  and swap the merged dictionary at index 0.
- **No high-contrast-mode-specific styling.** `SystemParameters.HighContrast`
  is not currently read; a user running Windows High Contrast mode gets this
  phase's Light/Dark palette rather than a dedicated high-contrast one.
- **Thumbnail cache size bound (4,000 files, swept to 3,000) is a heuristic
  default**, not measured/tuned against a real large-library usage pattern —
  consistent with CLAUDE.md rule 10's "never claim calibrated," the same
  caveat this codebase already attaches to `CleanClipScoringOptions`/
  `TransitionDetectionProfile`'s own heuristic defaults (Phases 6-7).
- **Advanced `TimelinePlanRequest`/`CleanClipExtractionOptions` knobs are
  not exposed in the UI** — `MinimumRepeatDistance`, `MaximumReuseCount`,
  `OriginalNeighborSeparation`, `VisualClusterAdjacencyLimit`,
  `TimelineDurationBounds`, and every `CleanClipScoringOptions`/
  `ClusteringOptions` weight all use their library defaults. Analysis
  Settings exposes only `AnalysisProfile`, output frame rate, and the
  shuffle seed — the knobs a first-time user actually needs. A future phase
  could add an "Advanced" section for the rest.
- **Views are not covered by automated UI tests** (e.g. no
  `WindowsAppDriver`/UI Automation test suite checked into the repo) — this
  phase's Views were instead verified by actually launching the built
  application and driving it through Windows UI Automation interactively
  (see "Manual end-to-end verification" above, including the real bug that
  process caught), but that verification is not repeatable via `dotnet
  test`. A future phase wanting regression coverage for the Views
  themselves (as opposed to the ViewModels, which are covered) would need a
  dedicated UI-automation test project.
- **No packaging/installer** — still out of scope, as every prior phase's
  report has noted for itself; the app runs today only as a built `.exe`
  with `ffmpeg.exe`/`ffprobe.exe` placed under `tools\ffmpeg\` relative to
  it (`FfmpegToolLocator`'s existing, unchanged resolution rule).
