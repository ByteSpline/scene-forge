# Phase Review Report

Date: 2026-08-25

## Scope

Strict release review of the current branch (`14-packaging`, commit
`084e0b9` "Step 14: Package self-contained portable ZIP and Inno Setup
installer with bundled FFmpeg/OpenCV") against the non-negotiable rules in
[CLAUDE.md](CLAUDE.md), [docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md)
Decision 9, and the phase's own [docs/PACKAGING_REPORT.md](docs/PACKAGING_REPORT.md).
The prior phase's self-report was treated as a claim to verify, not a fact
to trust: the actual diff, tests, build, and packaging pipeline were
inspected and re-run independently rather than relying on the report's
narrative alone.

## Commands executed and results

```text
git log --oneline -20 / git show --stat HEAD          -> commit contents confirmed
dotnet build SceneForge.sln -c Release                 -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test SceneForge.sln -c Release --no-build        -> 650/650 passed
                                                            (8 Core, 31 Accuracy, 61 App,
                                                             46 Infrastructure, 504 Media)
dotnet format SceneForge.sln --verify-no-changes        -> clean, no diff
& ISCC.exe packaging\installer\SceneForge.iss           -> "Successful compile (205.859 sec)",
                                                            produced SceneForge-1.0.0-win-x64-setup.exe
packaging\scripts\Verify-PortableBuild.ps1               -> "PASS: all native-component diagnostics
                                                            reported success in the isolated folder"
                                                            (fresh %TEMP% folder, PATH cleared from
                                                            the child process environment)
```

All of the above were run for real in this review, against the existing
`src/SceneForge.App/bin/publish/win-x64` publish output (68 MB self-contained
single-file exe, real `tools\ffmpeg\` and `tools\opencv\` payloads already
staged) and the existing `packaging/output/` artifacts (`SceneForge-1.0.0-win-x64-portable.zip`,
188.7 MB; `SceneForge-1.0.0-win-x64-setup.exe`, 148.6 MB) - sizes match
`docs/PACKAGING_REPORT.md`'s claims. `packaging/vendor/ffmpeg/` and
`packaging/vendor/vcredist/` were confirmed empty (only `.gitkeep`), as the
report claims. The verification temp folder created by
`Verify-PortableBuild.ps1` was deleted after inspection; no installer was
actually run/installed on this machine during this review (per-user install
already covered live in the prior phase's own report; re-running it here
would modify this machine's real Start Menu/registry state for no
additional evidence beyond what was already independently confirmed).

## Review outcome

### Blockers

None.

### Major issues

1. **Resolved in this review.** `packaging/installer/SceneForge.iss`'s own
   header comment claimed *"Not compiled/tested in the environment this
   was authored in (no Inno Setup Compiler available there)"*, while
   `docs/PACKAGING_REPORT.md` - committed in the same commit - describes
   exhaustive compile/install/uninstall verification of that exact script.
   Independently recompiling the script in this review (`ISCC.exe
   packaging\installer\SceneForge.iss`) succeeded cleanly, confirming the
   report's claim and the comment's claim were in direct contradiction.
   A stale, false claim left inside a shipped script is exactly the kind
   of unverifiable/contradictory claim a release review must catch: a
   future packager reading only the script (not the report) would
   wrongly conclude the installer had never been tested. **Fix applied**:
   the comment now states what was actually verified and points at
   `docs/PACKAGING_REPORT.md` for details, matching the evidence gathered
   both by the original phase and independently in this review. No code
   or test changes were needed - the packaging mechanism itself was
   already correct; only the documentation was wrong.

### Minor issues

- None beyond what `docs/PACKAGING_REPORT.md` itself already discloses
  under "Known limitations / what a real release still needs" (no
  clean-VM pass, `packaging/vendor/ffmpeg/` intentionally unpopulated, the
  per-machine/admin installer path not run end-to-end, no automated
  `licenses\` folder population, no code signing). These are honestly
  disclosed gaps for a *future public release*, not defects in this
  phase's own acceptance criteria, and are correctly still open items
  rather than being silently marked done.

### Verified compliant with CLAUDE.md (evidence-based, not assumed)

- **Rule 2 (no web/telemetry/network requirement)**: `NativeDependencyDiagnosticsService`
  and its three checks (`FfmpegToolLocator`, `SystemNativeLibraryProbe`,
  `OpenCvNativeProbe`) are 100% local/offline - no HTTP client, no socket,
  anywhere in this diff. `SceneForge.iss` never downloads anything; vendor
  binaries are placed locally ahead of time (`packaging/vendor/README.md`).
- **Rule 3 (FFmpeg/FFprobe + OpenCvSharp as the media stack)**: unchanged;
  this phase only changes how those binaries are located/bundled, not
  what does the media work.
- **Rule 5 (async cancellation / cooperative shutdown)**: `ProcessRunner`
  (unchanged by this phase, re-verified by reading it) never uses a shell,
  always passes arguments via `ArgumentList`, sets `CreateNoWindow=true`,
  honors both an external `CancellationToken` and an internal timeout, and
  kills the full process tree on either. `FfmpegToolLocator`'s `-version`
  probe is timeout-bounded (10s/binary) and threads a real
  `CancellationToken` through; `StartupDiagnosticsViewModel` deliberately
  passes `CancellationToken.None` with a documented rationale (checks are
  fast/bounded, not the kind of long-running operation the rule targets),
  and the user retains an always-enabled Exit button as an escape hatch
  even mid-check.
- **Rule 6/7 (bounded memory/concurrency, no full-buffering)**: nothing in
  this diff introduces an unbounded queue, cache, or fan-out; the
  diagnostics gate runs three fixed, sequential checks once per launch (or
  on explicit Retry, itself disabled while a run is in flight).
- **Rule 11/12 (never mutate/overwrite user files; outputs to a
  user-selected new path)**: `SceneForge.iss` only installs under `{app}`
  and only removes what it installed on uninstall - confirmed by reading
  the script (no `[UninstallDelete]` or other entry touches
  `%LOCALAPPDATA%\SceneForge`, which is where `ProjectLayout.DefaultAppDataRoot`
  keeps a user's projects/logs/thumbnail cache). Unrelated to and
  unaffected by this phase's render-output path handling.
- **UI-thread safety**: `NativeDependencyDiagnosticsService.RunAsync`'s
  first `await` uses `ConfigureAwait(false)`, so the two synchronous native
  probes (`CheckVcRuntime`, `CheckOpenCv`) run on a thread-pool thread, not
  the UI thread; `StartupDiagnosticsViewModel` resumes on the UI thread
  (`ConfigureAwait(true)`) only to update the bound `ObservableCollection`.
  `App.OnStartup` itself does no blocking work before showing the
  diagnostics window.
- **Process invocation safety**: every process launch in this phase
  (ffmpeg/ffprobe `-version` checks) goes through the existing hardened
  `ProcessRunner` - no shell, no string-concatenated command line, fixed
  known paths under `tools\ffmpeg\` (never user input).
- **Real bug caught and fixed during the phase itself, verified present
  in the current code**: `App.xaml.cs` sets `ShutdownMode =
  ShutdownMode.OnExplicitShutdown` before showing the diagnostics window
  and restores `OnLastWindowClose` after `MainWindow.Show()` - confirmed
  necessary and present; without it, WPF's default `ShutdownMode` would
  tear the app down the instant the diagnostics window closes (zero
  windows briefly open before `MainWindow` exists).
- **Test coverage**: 6 new `NativeDependencyDiagnosticsServiceTests` and 3
  new `StartupDiagnosticsViewModelTests` were read in full - they exercise
  real failure modes (ffmpeg missing/incompatible, individual VC++ DLLs
  missing, OpenCV probe throwing, Retry replacing prior results), not
  trivial pass-through assertions.

## Conclusion

The packaging phase is release-gate-clean for this stage: build, format,
and the full test suite (650/650) pass; the publish -> ffmpeg staging ->
portable ZIP -> Inno Setup installer pipeline was independently re-run in
this review (not just re-read) and produced correct, correctly-sized
artifacts; the portable build's "no PATH dependency" claim was
independently reproduced with `Verify-PortableBuild.ps1` in an isolated,
PATH-cleared folder. The one confirmed defect - a stale, contradictory
comment in `SceneForge.iss` that falsely claimed the installer was never
compiled/tested - has been fixed in this review. Known, honestly-disclosed
gaps toward a *public* release (clean-VM pass, license-file automation,
per-machine installer path, code signing) remain open and are correctly
not represented as done anywhere in the repository.
