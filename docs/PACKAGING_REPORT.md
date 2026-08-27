# Packaging Report

Date: 2026-08-25

## Scope

Produce a self-contained, single-file win-x64 Release publish of
SceneForge with FFmpeg/FFprobe and OpenCV's native library bundled under a
`tools\` folder next to the executable (never `PATH`), a first-run
diagnostics gate that verifies those native components before the app
lets a user start an analysis, an Inno Setup installer, and a portable
ZIP - plus the licensing notes and verification evidence a real release
needs before anything leaves this repository. See
[docs/ARCHITECTURE_DECISIONS.md](ARCHITECTURE_DECISIONS.md), Decision 9,
for the one-paragraph summary; this document is the detailed record.

## What changed

```
src/SceneForge.App/
  SceneForge.App.csproj                          - app icon, Product/Company/Copyright/Version metadata,
                                                    RelocateOpenCvNativeAssetForPackaging MSBuild target
  Properties/PublishProfiles/win-x64-Release.pubxml - the only place SelfContained/RuntimeIdentifier/
                                                    PublishSingleFile/PublishTrimmed=false live (see below
                                                    for why this is a separate profile, not a project default)
  Resources/app.ico                              - generated multi-resolution icon (16/32/48/64/128/256px)
  App.xaml.cs                                    - wires the startup diagnostics gate before MainWindow
  ViewModels/StartupDiagnosticsViewModel.cs       - runs INativeDependencyDiagnosticsService on construction
  Views/StartupDiagnosticsWindow.xaml(.cs)        - the gate's UI (pass/fail per component, Retry/Exit/Continue)

src/SceneForge.Media/Tooling/
  INativeDependencyDiagnosticsService.cs, NativeDependencyDiagnosticsService.cs,
  NativeComponentCheckResult.cs, NativeDependencyDiagnosticsReport.cs   - the three checks (ffmpeg, VC++ runtime, OpenCV)
  IOpenCvNativeProbe.cs, OpenCvNativeProbe.cs                           - real OpenCV native call, wrapped for testability
  INativeLibraryProbe.cs, SystemNativeLibraryProbe.cs                  - VC++ runtime DLL-loadable probe
  OpenCvNativeLibraryResolver.cs                                       - ModuleInitializer that redirects OpenCvSharp4's
                                                                          DllImport resolution to tools\opencv\ when packaged

tests/SceneForge.Media.Tests/Tooling/NativeDependencyDiagnosticsServiceTests.cs   - 6 tests, all fakes
tests/SceneForge.App.Tests/ViewModels/StartupDiagnosticsViewModelTests.cs        - 3 tests
tests/*/TestSupport/Fake{OpenCvNativeProbe,NativeLibraryProbe,NativeDependencyDiagnosticsService}.cs

packaging/
  scripts/Publish-SceneForge.ps1     - dotnet publish + stage ffmpeg vendor binaries into tools\ffmpeg\
  scripts/New-PortableZip.ps1        - zip an existing publish output
  scripts/Verify-PortableBuild.ps1   - copy/extract to an isolated temp folder, launch with PATH cleared,
                                        read the diagnostics result via UI Automation
  installer/SceneForge.iss           - Inno Setup script (compiled and run end-to-end - see below)
  vendor/README.md                   - where a packager places ffmpeg / VC_redist.x64.exe (never committed)

LICENSE_NOTICE.md          - concrete redistribution notes for FFmpeg, OpenCV/OpenCvSharp, VC++ runtime, .NET
docs/ARCHITECTURE_DECISIONS.md   - Decision 9
.gitignore                 - packaging/vendor/*, packaging/output/
```

## Design decisions and why

**Trimming stays off.** `PublishTrimmed=false` in the pubxml, with a
comment pointing at CLAUDE.md rule 9 (benchmark every optimization with
evidence). WPF's binding engine, CommunityToolkit.Mvvm/Microsoft.Extensions.DependencyInjection's
reflection-based wiring, and OpenCvSharp's P/Invoke marshaling are not
known to be trim-safe, and no benchmark exists proving otherwise - so it
stays off rather than being enabled speculatively. Revisiting this needs
its own before/after benchmark pass, not a default flip.

**Publish-only settings live in a separate `.pubxml`, not the `.csproj`.**
`SelfContained`/`RuntimeIdentifier`/`PublishSingleFile` change `bin\`
layout and pull in the whole .NET runtime; putting them directly in the
project file would affect every `dotnet build`/`dotnet test`/F5 run, not
just packaging. Icon/version/product metadata *is* in the `.csproj`
directly, since embedding real metadata into every build (including
Debug) is harmless and arguably an improvement (Explorer's Properties tab
was showing nothing meaningful before this).

**OpenCV relocation is an MSBuild target, ffmpeg staging is a PowerShell
script.** MSBuild already knows exactly where the OpenCvSharp4.runtime.win
NuGet package's native asset lands after a real publish (verified by
actually running `dotnet publish` - see below); ffmpeg has no NuGet
package at all here; it is an external vendor binary a packager supplies,
which only `packaging/scripts/Publish-SceneForge.ps1` can meaningfully
stage.

**`OpenCvNativeLibraryResolver` uses `NativeLibrary.SetDllImportResolver`,
not `AddDllDirectory`/`SetDllDirectory`.** It is registered against the
`OpenCvSharp4` assembly specifically (not `SceneForge.Media`'s own
assembly) because that is where OpenCvSharp4's `[DllImport("OpenCvSharpExtern")]`
declarations actually live - the resolver would silently never fire if
registered against the wrong assembly. A `[ModuleInitializer]` in
`SceneForge.Media` guarantees it is registered before any OpenCvSharp call
in this codebase can run (every such call lives inside `SceneForge.Media`
itself - see that class's remarks), and it falls back to default
resolution when `tools\opencv\` does not exist, so ordinary `dotnet
build`/`dotnet test` runs (where the DLL is copied to
`runtimes\win-x64\native\` by OpenCvSharp4.runtime.win's own build target)
are completely unaffected. Confirmed unaffected by running the full
`SceneForge.Media.Tests` suite (504 tests) after adding it - see
"Verification" below.

**The startup diagnostics gate runs on every launch, not literally only
the first.** A one-time flag would go stale the moment antivirus
quarantines a `tools\` file, a Windows update breaks the VC++ runtime, or
someone copies the app folder without the `tools\` subfolders - all three
checks are fast, local, and side-effect-free, so there is no real cost to
always verifying instead of trusting a "first run" marker. It blocks the
*entire* app, not just the analysis screens: everything past Welcome/Import
already depends transitively on ffmpeg (import itself needs ffprobe) and
the workflow has no meaningful "browse-only" mode, so gating the whole app
is simpler than threading a partial-capability state through every
downstream screen and is easy for a user to reason about ("something's
wrong, fix it, retry").

## A real bug this caught: WPF's default ShutdownMode

Implementing the gate as `diagnosticsWindow.ShowDialog()` followed by
`mainWindow.Show()` looked correct and passed every unit test, but running
the actual packaged exe showed the app silently exiting the instant a
*passing* diagnostics check closed its window. WPF's default
`ShutdownMode` (`OnLastWindowClose`) tears the whole `Application` down
the moment zero windows are open - which is exactly the state between the
diagnostics window closing and `MainWindow.Show()` running a few lines
later, since neither window exists yet while the diagnostics window is the
only one open. The fix (now in `App.xaml.cs`) sets
`ShutdownMode = ShutdownMode.OnExplicitShutdown` before showing the
diagnostics window and restores `OnLastWindowClose` right after
`MainWindow.Show()`, so closing MainWindow later still exits the app
normally. This is exactly the kind of defect that only running the real
built artifact - not unit tests, not code review - would surface, which is
why the verification steps below run the actual `.exe`, not just `dotnet
test`.

## Verification performed

All of the following were run for real in this environment (not just
described) against a real `dotnet publish -p:PublishProfile=win-x64-Release`
output, using a genuine local FFmpeg build (Gyan "full_build-shared"
9.0.1) already present on the development machine for convenience.
**That build is GPL v3** (confirmed by reading its own bundled `LICENSE`
file) and was used strictly for this local, non-distributed verification -
see `LICENSE_NOTICE.md` for why it must never be the build staged into
`packaging/vendor/ffmpeg/` for an actual release, and note that
`packaging/vendor/ffmpeg/` has been left empty (only `.gitkeep`) after
this verification, specifically so nothing GPL-licensed lingers where a
future packaging run might pick it up by accident.

1. **Full solution build, format, and test gate** (CLAUDE.md rule 13):
   `dotnet format SceneForge.sln --verify-no-changes`,
   `dotnet build SceneForge.sln`, `dotnet test SceneForge.sln` - all clean.
   650 tests passed (8 Core, 31 Accuracy, 61 App, 46 Infrastructure, 504
   Media), including the 6 new `NativeDependencyDiagnosticsServiceTests`
   and 3 new `StartupDiagnosticsViewModelTests`.

2. **Real publish**: `dotnet publish src/SceneForge.App/SceneForge.App.csproj
   -c Release -p:PublishProfile=win-x64-Release` succeeded. Output: a
   single 66 MB `SceneForge.App.exe` (self-contained, includes the .NET 8
   + WPF runtime) plus a handful of WPF native interop DLLs at the root
   (`D3DCompiler_47_cor3.dll`, `PenImc_cor3.dll`, `PresentationNative_cor3.dll`,
   `wpfgfx_cor3.dll`, `vcruntime140_cor3.dll` - all standard self-contained
   .NET/WPF publish output, not OpenCV-related) and
   `tools\opencv\OpenCvSharpExtern.dll` (+ the unused
   `opencv_videoio_ffmpeg4130_64.dll`, moved alongside it for tidiness -
   see `SceneForge.App.csproj`'s target comment for why it is unused here).
   The `RelocateOpenCvNativeAssetForPackaging` MSBuild target's own
   `<Error>` safety check (fails the build if the relocation did not
   happen) did not fire, confirming the relocation actually ran.

3. **ffmpeg staged manually** (`ffmpeg.exe`, `ffprobe.exe`, and the shared
   build's `avcodec-63.dll`/`avformat-63.dll`/`avutil-61.dll`/
   `avdevice-63.dll`/`avfilter-12.dll`/`swresample-7.dll`/`swscale-10.dll`)
   into `tools\ffmpeg\` under the publish output, reproducing what
   `Publish-SceneForge.ps1` automates from `packaging/vendor/ffmpeg/`.

4. **Launched the actual published `.exe`** (not `dotnet run`) and
   confirmed, via `PrintWindow` captures of only the app's own window
   handle (see "A note on how this was captured" below) and UI Automation:
   - The startup diagnostics window appears and all three checks report
     **Passed**: FFmpeg/FFprobe (launched from `tools\ffmpeg\`), Visual
     C++ Runtime (`vcruntime140.dll`/`vcruntime140_1.dll`/`msvcp140.dll`
     all loaded), OpenCV native library (`Cv2.GetBuildInformation()`
     returned a real "General configuration for OpenCV 4.13.0" banner,
     proving the relocated `tools\opencv\OpenCvSharpExtern.dll` actually
     loaded and ran through `OpenCvNativeLibraryResolver`).
   - Clicking **Continue** (via UI Automation's `InvokePattern`, not a
     simulated click) correctly opens MainWindow at "Step 1 of 8 - Welcome
     & Import", with the app icon visible in the title bar.
   - The window title bar, taskbar entry, and Explorer file icon all show
     the generated `app.ico`.

5. **No PATH dependency, proven, not assumed**: relaunched the published
   exe with the child process's `PATH` environment variable entirely
   absent (only `SystemRoot`/`WINDIR`/`TEMP`/`TMP`/`USERPROFILE`/
   `LOCALAPPDATA` kept). All three diagnostics still passed and Continue
   was still enabled - ffmpeg/ffprobe/OpenCV resolution genuinely never
   touches PATH, exactly as `FfmpegToolLocator` and
   `OpenCvNativeLibraryResolver`'s own code comments claim.

6. **Portable build verification, in an isolated folder outside the
   repo**: `packaging/scripts/Verify-PortableBuild.ps1` copies the publish
   output (or extracts a ZIP) into a fresh `%TEMP%\SceneForgePortableVerify-*`
   folder, launches it there with PATH cleared, and polls the diagnostics
   window via UI Automation until Continue becomes enabled or a timeout
   passes. Ran successfully against both the raw publish output and the
   actual portable ZIP (`Expand-Archive` code path) - both reported PASS.
   **Honest limitation**: this still runs on the same Windows install as
   development (same registry/WinSxS/VC++ runtime state), so it proves
   "nothing reads from the repo checkout or PATH" but not "works on a
   machine that has literally never had the .NET SDK or VC++ runtime
   installed." A real clean-VM pass (fresh Windows 10/11 install, no dev
   tools) is still recommended before public distribution and is not
   something this environment could perform.

7. **Portable ZIP**: `New-PortableZip.ps1` produced
   `packaging/output/SceneForge-1.0.0-win-x64-portable.zip` (188.8 MB),
   named from `SceneForge.App.csproj`'s own `<Version>` so the ZIP name
   and installer name can never drift apart.

8. **Inno Setup installer - compiled and run for real**, not just
   authored. Inno Setup 6.7.3 was already present on this machine (user
   confirmed installing it was fine); `ISCC.exe` compiled
   `packaging/installer/SceneForge.iss` successfully on the first attempt,
   producing `packaging/output/SceneForge-1.0.0-win-x64-setup.exe`
   (149 MB). The installer was then actually run, silently, in per-user
   mode (`/CURRENTUSER /VERYSILENT`, no admin elevation needed - see
   "Per-user vs per-machine" below) and verified end to end:
   - Installed to `%LOCALAPPDATA%\Programs\SceneForge\` with `tools\ffmpeg\`
     and `tools\opencv\` present.
   - Created a Start Menu group with `SceneForge.lnk` and
     `Uninstall SceneForge.lnk`.
   - Wrote a correct `HKCU\...\Uninstall\{...}_is1` registry entry:
     `DisplayName=SceneForge version 1.0.0`, `DisplayVersion=1.0.0`,
     `Publisher=SceneForge contributors`, `DisplayIcon` pointing at the
     installed exe, `UninstallString` pointing at `unins000.exe`.
   - Launched correctly from the installed location (diagnostics passed
     there too).
   - Running `unins000.exe /VERYSILENT` removed the install directory and
     Start Menu group, removed the registry uninstall key, and - the part
     that actually matters for CLAUDE.md rule 11 - **left
     `%LOCALAPPDATA%\SceneForge` (the user's projects, logs, thumbnail
     cache) completely untouched**, confirmed by checking the path still
     existed after uninstall.

## Per-user vs per-machine install

The installer defaults to a **per-user** install
(`PrivilegesRequired=lowest`, `PrivilegesRequiredOverridesAllowed=commandline dialog`)
rather than requiring admin/Program Files. This was a deliberate choice
made while writing this script, not a pre-existing requirement:

- It lets a non-admin user install SceneForge with no UAC prompt at all.
- `{autopf}`/`{autoprograms}`/`{autodesktop}` (used throughout the `.iss`)
  already resolve correctly for either mode, so both paths are exercised
  by the same script.
- The bundled VC++ redistributable install step
  (`VCRedistNeedsInstall` in `[Code]`) explicitly checks
  `IsAdminInstallMode()` and skips itself for a per-user install (it
  cannot install a machine-wide component without elevation) - a per-user
  install that is missing the VC++ runtime instead surfaces that clearly
  through the app's own startup diagnostics on first launch, with concrete
  remediation text, rather than the installer failing or silently
  prompting mid-install.
- Passing `/ALLUSERS` (or choosing "for all users" in the interactive
  wizard) switches to a real per-machine, Program Files install and does
  attempt the VC++ redistributable install, still gated on it not already
  being present.

This was verified only in the per-user path (no admin session was
available in this environment). The per-machine path compiles from the
same script and uses the same elevation-aware Inno constants, but was not
run end-to-end here - a packager should do that pass at least once on a
real machine before relying on it.

## A note on how this was captured

Early in verification, a full-desktop screenshot was taken to visually
confirm the diagnostics window - which was a mistake: it captured
whatever else was on screen at the time (unrelated browser tabs, personal
files), not just the app. That screenshot was deleted immediately and not
examined further. Every subsequent visual check in this report used
`PrintWindow` against the specific app window's own handle (which renders
only that window's content, regardless of what else is on screen or
whether it is occluded) instead of a screen-region capture - the safer
technique, used throughout the rest of this verification pass.

## Licensing

See `LICENSE_NOTICE.md` for the full, concrete breakdown. Summary:

| Component | License | Redistribution note |
|---|---|---|
| FFmpeg/FFprobe | LGPL v2.1+ (LGPL-only build) or GPL (if GPL components enabled) | **Must ship an LGPL-only build.** The GPL build used for this report's verification must never be staged for a real release. |
| OpenCV (native) | Apache License 2.0 | Confirmed from the restored `OpenCvSharp4.runtime.win` 4.13.0.20260627 package's own `.nuspec`. Permissive; include license text. |
| OpenCvSharp (managed wrapper) | Apache License 2.0 | Confirmed from the restored `OpenCvSharp4` 4.13.0.20260627 package's own `.nuspec`. |
| Visual C++ Redistributable | Microsoft's own terms | Only bundle the genuine official installer; never copy raw System32 DLLs. |
| .NET runtime (self-contained) | MIT | No action required. |

## Known limitations / what a real release still needs

1. **No real clean-VM pass.** Verification here proves independence from
   the repo checkout, `PATH`, and the dev SDK, but ran on a machine that
   already has a .NET SDK, Visual Studio tooling, and a VC++ runtime
   installed. A genuinely clean Windows VM pass is recommended before
   public distribution.
2. **`packaging/vendor/ffmpeg/` must be populated with a real LGPL build**
   before a real release - it is intentionally empty in this repository.
3. **Per-machine (admin) installer path** was authored but not run
   end-to-end (no admin session available here) - only the per-user path
   was verified live.
4. **`licenses\` folder is not yet auto-populated** by the packaging
   scripts - `LICENSE_NOTICE.md`'s packaging checklist calls for copying
   the actual FFmpeg/OpenCV license text files into the shipped output
   before a real release; this report's scripts do not yet automate that
   copy step.
5. **No code-signing.** The produced `.exe`/installer are unsigned;
   Windows SmartScreen will warn on first run of a downloaded copy. Out of
   scope for this phase (no certificate available in this environment).
