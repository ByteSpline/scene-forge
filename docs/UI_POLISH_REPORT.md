# UI Polish Report — Window Chrome, Palette, Typography, Spacing

Date: 2026-08-28
Branch: `17-ui-polish`

## Post-review fixes (2026-08-28, round 2)

Manual testing of the built `SceneForge.App.exe` on a 1366×768 laptop
surfaced a real regression plus two product-owner requests. All three are
fixed and re-verified by launching the actual Release build:

1. **Custom title bar was clipped off the top of the screen.** With
   `WindowStyle="None"` there is no OS caption for Windows to keep on-screen,
   so the 820 px default height on a 728 px work area, centred, put the
   window top at ≈ −46 — the entire 40 px custom caption (and its
   minimize/maximize/close buttons) sat above the screen edge, so the user
   saw "content with no title bar, no buttons, blending into the
   background". Fixed by `MainWindow` clamping its size to the monitor work
   area and centring within it before first paint
   (`WindowPlacementMath.CentreWithin`, unit-tested), switching
   `WindowStartupLocation` to `Manual`, setting
   `WindowChrome.GlassFrameThickness="1"` so DWM keeps drawing the window
   drop-shadow/frame, and a dedicated mid-tone `Brush.WindowBorder`.
2. **Primary buttons are now capsule / pill shaped** — `CornerRadius` =
   `Radius.Pill` (19, ≈ half the 38 px button height).
3. **Primary buttons use a lighter blue with white text** —
   `Brush.ButtonPrimaryBackground` `#2F6FE0` (was `#2563EB`), white label
   at 4.70:1 contrast (WCAG AA ≥ 4.5:1). A pre-existing bug where the
   app-wide implicit `TextBlock` style overrode the button's own foreground
   (making the label render dark, not white) was fixed in the button
   templates.

Verified on the running build: title bar with working minimize (→
minimized), maximize (→ fills the work area exactly, no taskbar cover / no
off-screen spill), and close (→ process exits) buttons; capsule buttons in
lighter blue with white text. See
`docs/screenshots/phase17/00_realapp_welcome_verification.png` (captured
from the live `SceneForge.App.exe` via `PrintWindow`).

## Release review (2026-08-29, round 3)

Strict release review of the branch against CLAUDE.md and this report — see
[PHASE_REPORT.md](PHASE_REPORT.md), "Phase 17 review", for the full write-up.
No blockers, no major issues. Actions taken:

- **Added `ListViewVirtualizationTests`** — an empirical STA-thread probe
  proving the retemplated `ListView` still virtualizes (3000 items → 9
  realized containers). Previously the rule 6/7 property was only reasoned
  about, not tested.
- **`MainWindow.xaml.cs` hardening** (2 lines): the `WM_GETMINMAXINFO` hook
  body is now wrapped in `try/catch` so a P/Invoke failure can never escape
  into the WPF message loop (on that path the window falls back to the OS
  default maximize bounds); `Marshal.StructureToPtr` now passes
  `fDeleteOld: false` (the correct value when overwriting an OS-owned
  buffer). Both re-verified on the running exe — maximize still fits the
  work area exactly.
- Report corrections: `Card` has a 1-px border and **no** drop shadow (the
  earlier "barely-there drop shadow" line was wrong); test counts updated
  to 77 App / 699 solution; the contrast table now lists the re-measured
  ratios.
- No relevant benchmark: this phase changes no algorithmic / media code;
  `SceneForge.Benchmarks` (module-info load timing only) still compiles.

## Scope

Purely visual / UX polish of the existing WPF shell and its eight workflow
screens (Welcome/Import → Completion) plus the pre-launch Startup
Diagnostics window. **No** business logic, ViewModel, navigation, or
pipeline behaviour was changed — every change is in XAML resource
dictionaries, one new view-layer ViewModel for the caption buttons, and the
shell window's code-behind (window-management interop only).

Requested `frontend-design` skill: not available in this environment. The
work follows standard WPF/XAML design-token conventions instead (named
`FontFamily` / `Double` / `Thickness` / `CornerRadius` resources for the
type scale, spacing scale, and radii; semantic `SolidColorBrush` keys for
colour, unchanged in shape from Phase 10).

## 1. Window chrome — custom title bar

`MainWindow` moved from the default OS caption to `WindowStyle="None"` +
`System.Windows.Shell.WindowChrome`, so the caption matches the app's own
light/dark palette instead of staying OS-white in dark mode.

| Element | Behaviour |
|---|---|
| Frame / shadow | `WindowChrome.GlassFrameThickness="1"` so DWM keeps drawing the standard window drop-shadow and frame line; plus an explicit 1 px `Brush.WindowBorder` edge, so the window is always a clearly-bounded surface, not "flat content blending into the background" |
| Startup size / position | `WindowStartupLocation="Manual"`; `MainWindow` clamps its size to the monitor **work area** and centres within it before first paint (`WindowPlacementMath.CentreWithin`) — the custom caption can never be pushed off the top of a short display |
| Drag / move | `WindowChrome.CaptionHeight="40"` — the title-bar row is the drag region |
| Double-click caption | Maximize / restore (WindowChrome default) |
| Resize | `ResizeMode="CanResize"`, `WindowChrome.ResizeBorderThickness="6"` — all eight edges/corners; Aero Snap works |
| Minimize / Maximize-Restore / Close | Custom buttons, bound to `WindowChromeViewModel` **RelayCommands** (not code-behind `Click` handlers) |
| Maximize spill | `WM_GETMINMAXINFO` hook (`MainWindow.xaml.cs`) clamps the maximized frame to the monitor work area so a borderless window doesn't cover the taskbar or bleed off-screen; the root border padding is toggled to 8 px while maximized |
| Accessibility | Each caption button has a distinct `AutomationProperties.Name` ("Minimize window", "Maximize window" / "Restore window", "Close window"); caption-button glyphs are `Path` geometry, not an icon font, so they render on any machine regardless of which Segoe icon font is installed |

`WindowChromeViewModel` implements the caption commands against a small
`IChromeWindow` seam (`WindowState` + `Close()`), which `MainWindow`
implements directly. That seam is what lets the buttons be unit-tested
without an STA message loop (see `WindowChromeViewModelTests`).

The Startup Diagnostics window keeps its native caption (it is a short-lived
modal shown before the shell exists) but inherits the new palette, type
scale, and control styles automatically.

## 2. Colour palette

Kept the exact same semantic brush-key set and the Phase 10 dark/light
mechanism (`App.OnStartup` inserts `Colors.Light.xaml` or `Colors.Dark.xaml`
at merged-dictionary index 0; every colour is a `DynamicResource`). Only the
values changed, plus a few additive keys.

Softened the accent family — the old primary read as harsh next to large
button fills:

| Key | Light before | Light after | Dark before | Dark after |
|---|---|---|---|---|
| `Brush.Accent` | `#0057B8` | `#2563EB` | `#5AA6FF` | `#5C9CFF` |
| `Brush.AccentHover` | `#00449A` | `#1D4ED8` | `#7FBBFF` | `#7DB2FF` |
| `Brush.AccentText` | `#FFFFFF` | `#FFFFFF` | `#0B1522` | `#08182E` |

New keys (defined in **both** palettes): `Brush.SurfaceAlt`,
`Brush.AccentPressed`, `Brush.AccentSubtle`, `Brush.InputBorderFocused`,
`Brush.TextDisabled`, `Brush.TitleBarBackground`, `Brush.TitleBarForeground`,
`Brush.TitleBarButtonHover`, `Brush.CloseButtonHover`,
`Brush.CloseButtonHoverText`.

### Contrast (WCAG AA)

Checked with the standard relative-luminance formula:

Values below are the measured ratios (re-verified during the Phase 17
release review with a relative-luminance script over the actual palette
hex values):

| Pair | Ratio | Requirement |
|---|---|---|
| Both: white label on `ButtonPrimaryBackground` `#2F6FE0` | 4.70:1 | ≥ 4.5:1 (normal text) ✔ |
| Both: white on `ButtonPrimaryHover` `#2461D6` / `Pressed` `#1B52BE` | 5.59 / 7.01 | ✔ |
| Light: `Accent` `#2563EB` with white (focus/links/progress-on-fill) | 5.17:1 | ✔ |
| Light: `TextPrimary` on `PanelBackground` | 16.7:1 | ✔ |
| Light: `TextSecondary` `#59616C` on `WindowBackground` / `SurfaceAlt` | 5.80 / 5.48 | ✔ |
| Light: `Success` / `Warning` / `Error` on `PanelBackground` | 5.33 / 5.93 / 6.54 | ✔ |
| Dark: `AccentText` `#08182E` on `Accent` `#5C9CFF` (checkbox check) | 6.48:1 | ✔ |
| Dark: `TextPrimary` on `PanelBackground` | 12.7:1 | ✔ |
| Dark: `TextSecondary` `#AAB0BA` on `PanelBackground` | 6.84:1 | ✔ |
| Dark: `Success` / `Warning` / `Error` on `PanelBackground` | 7.53 / 7.66 / 5.24 | ✔ |

Disabled-state text (`TextDisabled`) is below AA by design and is exempt
from WCAG 1.4.3. The outer `Brush.WindowBorder` edge is ~1.8:1 (light) /
~2.5:1 (dark) against the window background — the window-boundary cue is
carried by the DWM `GlassFrameThickness` system frame and drop-shadow
(user-agent-drawn, WCAG-exempt), with `WindowBorder` as a supplementary
inner line.

(`#2F6FE0` is the lightest blue that still clears AA with white text; azure /
sky-blue shades all fall below 4.5:1 against white.)

A `SceneForge.App.Tests` guard (`ThemePaletteConsistencyTests`) fails the
build if the two palette files ever stop defining an identical key set, or
if `Styles.xaml` references a `Brush.*` key that neither palette defines.

## 3. Typography

- `FontFamily x:Key="Font.Primary"` = `Segoe UI Variable Text, Segoe UI Variable, Segoe UI, Segoe UI Symbol`
- `FontFamily x:Key="Font.Display"` = `Segoe UI Variable Display, Segoe UI Variable, Segoe UI` (used by `Heading1`)
- Both degrade cleanly to plain **Segoe UI** on machines without the
  variable faces.

Type scale (device-independent px), as named `Double` resources:

| Token | px | Used by |
|---|---|---|
| `FontSize.Caption` | 12 | `Caption`, metric labels |
| `FontSize.Body` | 14 | default `TextBlock`, all controls (was 13) |
| `FontSize.BodyLarge` | 15 | `Heading3`, `Subtitle` |
| `FontSize.Subtitle` | 16 | `Heading2` |
| `FontSize.Metric` | 22 | `MetricValue` (progress-screen readouts, replacing ad-hoc `FontSize="18/20"`) |
| `FontSize.Display` | 28 | `Heading1` (was 24) |

New shared text styles: `Heading3`, `Subtitle` (secondary lead-in
paragraph), `MetricValue`.

## 4. Shared styles / general polish

All applied in `Themes/Styles.xaml` — no per-screen hard-coded colours,
sizes, radii, or spacing.

- **Tokens**: `Radius.Small` (4) / `Radius.Control` (6) / `Radius.Card`
  (10) / `Radius.Pill` (19); `Space.CardPadding` (20), `Space.SectionGap`
  (`0,0,0,16`), `Space.ActionBarTop` (`0,24,0,0`).
- **Button (primary)**: capsule / pill shape (`Radius.Pill`), lighter-blue
  fill (`ButtonPrimaryBackground` `#2F6FE0`), white label, `22,9` padding,
  38-px min height, `SemiBold`, explicit hover **and** pressed states
  (`ButtonPrimaryHover` / `ButtonPrimaryPressed`), disabled uses `Disabled`
  + `TextDisabled`. The button template re-asserts its own foreground onto
  the content text so the app-wide implicit `TextBlock` style can't override
  the white label.
- **SecondaryButton**: outlined pill; hover tints background + border,
  pressed uses `AccentSubtle`.
- **TextBox / ComboBox / CheckBox**: full retemplate to a flat, rounded,
  palette-driven look with a visible focus/hover border
  (`InputBorderFocused`); the ComboBox drop-down is a rounded card with a
  soft shadow; the CheckBox is a rounded box with an accent fill + drawn
  check.
- **ListView**: rounded 10-px container; rows are borderless with a rounded
  hover/selection fill instead of divider lines. Virtualization contract
  from Phase 10 preserved — the retemplated `ScrollViewer` still binds
  `CanContentScroll` / scrollbar visibility from the templated parent, and
  the items panel (`VirtualizingStackPanel`) is unchanged, so Scene
  Review's recycling virtualization still works (CLAUDE.md rule 6/7). This
  is now covered by `ListViewVirtualizationTests`: 3000 items in a bounded
  host realize only 9 row containers.
- **ProgressBar**: palette + proportions only (default template kept — its
  indeterminate animation is non-trivial to re-implement).
- **Card**: 10-px radius, 20-px padding, 1-px border (no shadow — a flat
  bordered panel); `CardSection` variant adds the standard 16-px bottom gap
  so stacked cards don't each repeat an inline margin.
- **Focus visual**: same accessible dashed outline as Phase 10, now with
  rounded corners to match the new control radii.
- **Per-screen sweep**: every screen's `Heading1` → content gap, card
  spacing, and primary-action-button placement normalised to the tokens
  above; the progress screens' inline font sizes replaced with
  `MetricValue`; Scene Review's start/end boundary fields widened so the
  timecodes are no longer clipped.

## 5. Screenshots

`docs/screenshots/phase17/`:

| File | Source |
|---|---|
| `00_realapp_welcome_verification.png` | The **live `SceneForge.App.exe`** (Release), captured via `PrintWindow` after passing Startup Diagnostics — shows the working title bar and capsule buttons on the real running app |
| `01_welcome_import_{light,dark}.png` | Welcome / Import |
| `03_scene_review_{light,dark}.png` | Scene Review |
| `06_completion_{light,dark}.png` | Completion |

The `01/03/06` set is rendered by composing `MainWindow` + the real Views
against the real merged resource dictionaries with representative fake
session data (the same DI-with-fakes pattern `MainWindowViewModelTests`
uses), then `RenderTargetBitmap` → PNG. No pipeline or ffmpeg involvement.

## 6. Tests

New in `SceneForge.App.Tests` (77 total, was 64; full solution 699, all
passing):

- **`WindowChromeViewModelTests`** (6) — the caption buttons are present and
  wired to real commands: `MinimizeCommand` sets `WindowState.Minimized`;
  `ToggleMaximizeCommand` flips Normal↔Maximized; `CloseCommand` closes the
  window; `IsMaximized` / `MaximizeRestoreLabel` track the window state and
  raise `PropertyChanged`.
- **`WindowPlacementMathTests`** (3) — a window larger than the work area is
  shrunk to fit and centred with its top on-screen (the regression this
  round fixed); a window that fits keeps its size and is centred; an
  offset work area (side taskbar / second monitor) is respected.
- **`ListViewVirtualizationTests`** (1) — added in the Phase 17 release
  review. Instantiates a `ListView` with the real merged `Styles.xaml`,
  3000 items in a bounded-height host, forces layout on an STA thread, and
  asserts the items panel is a `VirtualizingStackPanel`, `IsVirtualizing`
  and `CanContentScroll` are true, and only a screenful of containers
  (< 100; 9 in practice) were realized — the load-bearing rule 6/7 property
  that the ListView retemplate had to preserve.
- **`ThemePaletteConsistencyTests`** (3) — light and dark palettes define an
  identical brush-key set; every `Brush.*` key referenced by `Styles.xaml`
  is defined; palettes are non-empty.

`FakeChromeWindow` added under `TestSupport`.

## 7. Verification (CLAUDE.md rule 13)

- `dotnet format SceneForge.sln --verify-no-changes` — clean.
- `dotnet build SceneForge.sln -c Release` / `-c Debug` — 0 warnings, 0 errors.
- `dotnet test SceneForge.sln -c Release` — 699 passed, 0 failed, 0 skipped
  (Core 8, Accuracy 31, Infrastructure 46, App 77, Media 537).
- **Ran the built `SceneForge.App.exe`** and drove it via UI Automation:
  Minimize button → window minimized; Maximize button → window fills the
  1366×728 work area exactly (no taskbar cover, no off-screen spill) and the
  Restore button appears; Close button → process exits. Title bar, caption
  buttons, and capsule/white-text primary buttons confirmed visible in a
  `PrintWindow` capture of the live app.

## 8. CLAUDE.md compliance

- **Rules 1–2** (native WPF, no web/cloud/network): unchanged — still pure
  WPF/.NET 8. `WindowChrome` and the `user32.dll` `WM_GETMINMAXINFO` hook
  are local windowing APIs. The always-visible "runs entirely on this
  computer" footer is retained.
- **Rule 3** (media stack): untouched — no media code changed.
- **Rule 4** (clean architecture): the new `WindowChromeViewModel` is
  view-layer only (window state + close), depends on nothing in
  `SceneForge.Media`, and is not referenced by core/domain/pipeline code.
  The `WM_GETMINMAXINFO` interop is isolated in `MainWindow.xaml.cs` and is
  window-management, not processing logic.
- **Rules 5–7** (cancellation, bounded memory/concurrency, no full-video
  buffering): unaffected. No async / long-running operations were added
  (caption commands are synchronous property sets). Scene Review's
  virtualization contract was explicitly preserved when the `ListView` was
  retemplated and is now asserted by `ListViewVirtualizationTests`.
- **Rule 8** (test-first for algorithmic behaviour): N/A — no algorithmic
  behaviour changed; the new UI logic (caption commands, window-placement
  math, palette-key parity, list virtualization) is covered by tests.
- **Rules 9–10** (benchmarks, accuracy claims): N/A — visual-only change,
  no optimization to benchmark.
- **Rules 11–12** (preserve user files, output to new path): unaffected —
  no file I/O changed.
- **Rule 13**: see §7.
- **Rule 14**: this report; behaviour docs elsewhere needed no change (no
  workflow, risk, or processing behaviour changed). The Phase 10 report's
  "no live OS theme switching" limitation still stands.

## 9. Known limitations

- **Custom-chrome maximize** is handled via a `WM_GETMINMAXINFO` monitor
  clamp; multi-monitor mixed-DPI setups were not exhaustively exercised in
  this environment. The restored (non-maximized) and single-monitor
  maximized cases were verified visually.
- **First-launch window position** uses the *primary* monitor work area
  (`SystemParameters.WorkArea`); if the app is later reopened onto a
  secondary monitor it still first-centres on the primary. The user can
  move/resize it normally afterwards.
- **No Alt+Space system menu** — `WindowStyle="None"` removes the standard
  window menu. The three caption buttons are keyboard-reachable (Tab) and
  Alt+F4 still closes; a right-click system-menu was not added.
- **Live OS light/dark switch while running** — still out of scope (Phase
  10 limitation); the palette is read once at startup.
- **Windows High Contrast mode** — still no dedicated palette.
- **ProgressBar** keeps the stock template (rounded-corner + custom
  indeterminate animation deferred).
- **Views still have no automated UI-automation coverage** — the shell +
  views were verified by rendering them to PNG with fake data and by
  inspection; the ViewModels (including the new caption ViewModel) are
  covered by `dotnet test`.
