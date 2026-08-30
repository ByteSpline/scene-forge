# Phase Review Report

This file accumulates the strict release reviews performed on each phase,
most recent last.

## Phase 14 review — 2026-08-25

Date: 2026-08-25

### Scope

Strict release review of branch `14-packaging`, commit `084e0b9` "Step 14:
Package self-contained portable ZIP and Inno Setup installer with bundled
FFmpeg/OpenCV") against the non-negotiable rules in
[CLAUDE.md](CLAUDE.md), [docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md)
Decision 9, and the phase's own [docs/PACKAGING_REPORT.md](docs/PACKAGING_REPORT.md).
The prior phase's self-report was treated as a claim to verify, not a fact
to trust: the actual diff, tests, build, and packaging pipeline were
inspected and re-run independently rather than relying on the report's
narrative alone.

### Commands executed and results

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

### Review outcome

#### Blockers

None.

#### Major issues (found, fixed in this review)

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

#### Minor issues

- None beyond what `docs/PACKAGING_REPORT.md` itself already discloses
  under "Known limitations / what a real release still needs" (no
  clean-VM pass, `packaging/vendor/ffmpeg/` intentionally unpopulated, the
  per-machine/admin installer path not run end-to-end, no automated
  `licenses\` folder population, no code signing). These are honestly
  disclosed gaps for a *future public release*, not defects in this
  phase's own acceptance criteria, and are correctly still open items
  rather than being silently marked done.

#### Verified compliant with CLAUDE.md (evidence-based, not assumed)

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

### Conclusion

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

## Phase 15 review — 2026-08-26

Date: 2026-08-26

### Scope

Strict release review of the then-current branch (`15-github-actions-release`,
at commit `6ad7044` "Step 15: Add manual GitHub Actions workflow for build
and release") against the non-negotiable rules in
[CLAUDE.md](CLAUDE.md), [docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md),
and the phase's own task: add a manual `workflow_dispatch`-only GitHub
Actions workflow that restores, builds, tests, publishes a self-contained
build, builds the installer, zips the portable build, uploads both as
workflow artifacts, and — in `versioned-release` mode — creates a tagged
GitHub Release, documented in [docs/CI_RELEASE_GUIDE.md](docs/CI_RELEASE_GUIDE.md).

The prior implementation's own summary was treated as a claim to verify,
not a fact to trust: the actual diff, `.github/workflows/release.yml`,
`packaging/installer.iss`, and `packaging/Stage-Ffmpeg.ps1` were re-read
line by line against the specific risk checklist below, and the build/test
suite was re-run independently rather than relying on the earlier
narrative alone.

### Commands executed and results

```text
dotnet build SceneForge.sln --configuration Release
  -> Build succeeded, 0 Warning(s), 0 Error(s)

dotnet test SceneForge.sln --configuration Release --logger "console;verbosity=minimal"
  -> 641/641 passed, 0 failed, 0 skipped
     (8 Core, 31 Accuracy, 46 Infrastructure, 58 App, 498 Media)

dotnet format SceneForge.sln --verify-no-changes
  -> exit 0, no diff (no C# was touched by this phase's diff anyway)

python -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"
  -> YAML OK (re-run after every edit made during this review)

powershell -File packaging\Tests\Test-ReleaseVersioning.ps1
  -> "Test-ReleaseVersioning: all 15 cases passed." (exit 0) - new
     regression test added during this review, see "Fixes applied" below

curl https://community.chocolatey.org/api/v2/... (ffmpeg, innosetup)
  -> confirmed ffmpeg 9.0.1 and innosetup 6.6.1 both exist in the
     Chocolatey community feed right now, before pinning the workflow to
     those exact versions
```

**Not run, and why:** `ISCC.exe packaging\installer.iss` was not actually
compiled — this development machine has neither Chocolatey nor Inno Setup
installed, and installing either onto it was judged out of scope for a
review (that's persistent software on someone's real machine, not an
ephemeral CI runner). The `.iss` syntax was reviewed manually instead
(section structure, `{#Define}` usage, the `AppId={{GUID}}` escaping
idiom) and believed correct, but this is **not the same as a verified
compile** — the manual verification steps in
[docs/CI_RELEASE_GUIDE.md](docs/CI_RELEASE_GUIDE.md) call this out and
the actual GitHub Actions dry run is the real verification, not yet
performed (no authenticated `gh` credentials in this environment either).
BenchmarkDotNet suites were not re-run: this phase's diff contains no
algorithmic change (CLAUDE.md rule 9 applies to optimizations, and there
isn't one here) — only confirmed the benchmark project still builds
cleanly as part of the solution build above.

### Review outcome

#### Blockers

None. The workflow is syntactically valid, every step it depends on
(`packaging/Stage-Ffmpeg.ps1`, `packaging/installer.iss`,
`packaging/ReleaseVersioning.psm1`) exists and is internally consistent,
and the solution builds/tests/formats cleanly.

#### Major issues (found, fixed in this review)

1. **Unsafe interpolation of workflow inputs into shell scripts.** Every
   `run:` step originally spliced `${{ inputs.* }}` / `${{ steps.*.outputs.* }}`
   directly into its script body (e.g. `$version = "${{ inputs.version }}"`
   in PowerShell, and `gh release create "v${{ inputs.version }}" --target
   "${{ inputs.ref }}"` in bash). GitHub performs this substitution on the
   raw script text before the shell parses it, so a `ref` or `version`
   value containing quotes, backticks, or shell metacharacters could break
   out of the intended string literal — a real (if lower-severity, since
   `workflow_dispatch` already requires repo write access) code-injection
   pattern, and independently a correctness bug for any legitimate value
   containing those characters. **Fixed**: every such value now flows
   through a step-level `env:` mapping and is read back as `$env:NAME`
   (PowerShell) or `$NAME` (bash) — passed as a real environment variable,
   never spliced into script source. See "Known risks and follow-ups" in
   docs/CI_RELEASE_GUIDE.md.
2. **No verification that the OpenCvSharp native asset actually published.**
   The "Publish self-contained build" step verified `SceneForge.App.exe`
   existed but nothing else — a native-asset resolution failure for
   OpenCvSharp (silent per CLAUDE.md's "no silent fallback" concern) would
   have produced a "successful" build that ships a portable zip/installer
   which crashes at first OpenCV use, undetected by CI. **Fixed**: the same
   step now also asserts `OpenCvSharpExtern.dll` exists somewhere under the
   publish output, failing the job loudly if not.
3. **Unpinned Chocolatey package versions in a release pipeline.**
   `choco install ffmpeg -y` and `choco install innosetup -y` both floated
   to "whatever is current today" — acceptable for `ci.yml`'s PR gate, but
   a *release* pipeline needs to be able to say exactly what ffmpeg build
   is inside a given tagged release. **Fixed**: pinned to `ffmpeg 9.0.1`
   and `innosetup 6.6.1` via workflow-level `env:` vars, each confirmed
   present in the Chocolatey community feed via a live API query before
   pinning (not guessed). Documented that the pin must be bumped
   deliberately, not left to float.

#### Major issues (found, deliberately NOT fixed — flagged for a human decision)

4. **A parallel, independently-built packaging pipeline already exists on
   branch `14-packaging`** (commits `084e0b9` / `fdddf60`, not merged into
   this branch or `main`). That branch solved the same problem — publish,
   vendor ffmpeg, build an installer, zip a portable build — with a
   different and more thorough design: `packaging/installer/SceneForge.iss`,
   `packaging/scripts/Publish-SceneForge.ps1`, `packaging/scripts/New-PortableZip.ps1`,
   `packaging/scripts/Verify-PortableBuild.ps1`, a `packaging/vendor/ffmpeg/`
   + `packaging/vendor/vcredist/` vendoring convention, and an in-app
   `StartupDiagnosticsWindow` / `INativeDependencyDiagnosticsService` that
   surfaces missing native dependencies to the user at runtime. This
   phase's `packaging/installer.iss` and `packaging/Stage-Ffmpeg.ps1` were
   built without visibility into that branch and now duplicate it with a
   different, Chocolatey-based ffmpeg-sourcing strategy instead of the
   vendored-file convention Step 14 established. **Not fixed here**:
   reconciling two independently-developed branches (deciding which
   packaging pipeline the release workflow should actually drive) is a
   structural decision for whoever owns both branches, not something to
   resolve unilaterally while reviewing one workflow file. Must be
   resolved before either branch merges to `main`, or `main` will carry
   two divergent, undermaintained installer definitions.
5. **No Visual C++ Redistributable handling.** OpenCvSharp4's native
   binary is typically linked against the standard MSVC++ runtime, which
   self-contained .NET deployment's own renamed `*_cor3.dll` copies do not
   satisfy. Neither this pipeline nor `packaging/installer.iss` installs
   or bundles it, so a genuinely clean target machine could install
   SceneForge successfully and then crash on first OpenCV use — a failure
   mode this pipeline cannot detect on a build machine that already has
   the redistributable present. Branch `14-packaging`'s
   `packaging/vendor/vcredist/` is very likely the intended fix, which is
   one more reason item 4 needs resolving. Flagged, not fixed, and called
   out explicitly in the manual verification steps (test on a machine
   without the redistributable pre-installed).

#### Minor issues

- `packaging/installer.iss` was reviewed for correctness but not actually
  compiled in this session (see "Not run, and why" above) — treat the
  `Build installer` step of the actual GitHub Actions run as the first
  real compile.
- This workflow itself was still not dispatched in this session (no
  authenticated `gh` in this environment) — `docs/CI_RELEASE_GUIDE.md`'s
  manual verification section remains the operative verification path
  until someone with repo write access runs it.

#### Checklist: specific risk categories

| Category | Finding |
|---|---|
| Web dependencies | `choco install ffmpeg`/`innosetup` fetch over the network at **build time only**, mirroring `ci.yml`'s existing precedent; the shipped app makes no network calls (CLAUDE.md rule 2). Not a violation. |
| Unbounded memory/concurrency | None introduced — the workflow is a sequential CI pipeline on one runner; `Stage-Ffmpeg.ps1`'s `Get-ChildItem -Recurse` is bounded to the Chocolatey lib tree. |
| UI-thread work | N/A — no application/UI code in this phase's diff. |
| Unsafe process invocation | Found and fixed — see Major issue 1. |
| Timing drift | Checked deliberately: `Resolve build metadata` reads `git rev-parse HEAD` post-checkout rather than trusting `github.sha` (which reflects the ref that *triggered* dispatch, not necessarily the `ref` input) — correct as originally written. |
| Silent fallback | Found and fixed — see Major issue 2. |
| Missing cancellation | Both jobs carry `timeout-minutes`; a `workflow_dispatch` run is always manually cancellable from the Actions UI regardless. Adequate for a CI/CD pipeline (CLAUDE.md rule 5 targets the *application's* long-running operations). |
| Unverifiable claims | `docs/CI_RELEASE_GUIDE.md` explicitly discloses what was and wasn't run/verified in this session (workflow not dispatched, installer not compiled locally) rather than asserting success; the two Chocolatey pins were confirmed against the live package feed, not assumed. |
| Packaging omissions | Major issues 2, 3, 4, 5 above. |

### Fixes applied in this review

- `.github/workflows/release.yml`: reordered `Checkout` before
  `Validate inputs` (required for the fix below); replaced every
  `${{ inputs.* }}` / `${{ steps.*.outputs.* }}` interpolation inside a
  `run:` body with a step-level `env:` mapping; pinned
  `FFMPEG_CHOCO_VERSION=9.0.1` and `INNOSETUP_CHOCO_VERSION=6.6.1`; added
  an explicit `OpenCvSharpExtern.dll` existence check alongside the
  existing `SceneForge.App.exe` check in "Publish self-contained build".
- `packaging/ReleaseVersioning.psm1` (new): the semantic-version rule for
  the `version` input, factored out of the workflow so it exists in one
  place instead of being duplicated inline.
- `packaging/Tests/Test-ReleaseVersioning.ps1` (new): regression test for
  the module above — 15 cases (valid/invalid semver shapes plus two
  injection-shaped adversarial strings tied directly to Major issue 1) —
  run and passing (see "Commands executed and results").
- `docs/CI_RELEASE_GUIDE.md`: documented all of the above, added a "Known
  risks and follow-ups" section covering the two unresolved major issues
  (4 and 5) so they are not silently lost, and strengthened the manual
  clean-machine verification step to explicitly test without the VC++
  Redistributable pre-installed.

### Conclusion

No blockers. Three confirmed major issues were fixed with evidence (build/
test/format clean, new regression test passing, YAML re-validated after
every edit, Chocolatey pins confirmed against the live feed). Two further
major issues were identified and deliberately left unresolved because
fixing them correctly requires a structural decision this review is not
positioned to make unilaterally: reconciling this branch's packaging
pipeline with the independently-built one on `14-packaging`, and the
Visual C++ Redistributable gap that pipeline's `vendor/vcredist/`
convention most likely already answers. Both are documented in
`docs/CI_RELEASE_GUIDE.md` and must be resolved before this branch and
`14-packaging` both merge to `main`. The workflow itself has still not
been exercised end-to-end on GitHub Actions (no credentials in this
environment) — that remains the outstanding verification step.

## Reconciliation review — 2026-08-27

Date: 2026-08-27

### Scope

The two major issues left open at the end of the Phase 15 review above —
duplicate packaging pipelines on `15-github-actions-release` and
`14-packaging`, and a missing Visual C++ Redistributable story — were
resolved by merging `14-packaging` into `15-github-actions-release` and
rewriting `.github/workflows/release.yml` to drive Phase 14's real
packaging scripts instead of this branch's own. This review verifies that
reconciliation: the merge itself, the rewritten workflow's logic against
Phase 14's actual script interfaces (not assumed from reading them), and
a real (not simulated) end-to-end run of the resulting packaging chain on
this machine.

`14-packaging` was confirmed **not yet merged into `main`**
(`git merge-base --is-ancestor origin/14-packaging origin/main` → false)
at the start of this review, so the instruction to "pull main into this
branch" was performed as merging branch `14-packaging` directly instead —
the practical equivalent, since that branch's content does not exist on
`main` yet either.

### What changed

- **Merged `14-packaging` (commit `fdddf60`) into `15-github-actions-release`**
  (merge commit, `.gitignore` auto-merged cleanly, no other conflicts in
  tracked source). This brought in Phase 14's full packaging pipeline:
  `packaging/scripts/{Publish-SceneForge,New-PortableZip,Verify-PortableBuild}.ps1`,
  `packaging/installer/SceneForge.iss`, the `packaging/vendor/{ffmpeg,vcredist}`
  convention, `docs/PACKAGING_REPORT.md`, and the in-app
  `NativeDependencyDiagnosticsService`/`StartupDiagnosticsWindow` gate and
  its tests.
- **Deleted** this branch's own, now-superseded `packaging/installer.iss`
  and `packaging/Stage-Ffmpeg.ps1`.
- **Rewrote `.github/workflows/release.yml`** to call Phase 14's scripts
  instead of the deleted ones. Net new automation this workflow adds on
  top of Phase 14's local, manual pipeline (all new, all regression-tested
  or run for real — see "Commands executed and results"):
  - `packaging/Get-VendorFfmpeg.ps1` — downloads a pinned, verifiably
    LGPL-only FFmpeg build (BtbN `autobuild-2026-08-26-13-06` /
    `ffmpeg-n9.0.1-8-g16dfae5c88-win64-lgpl-shared-9.0.zip`, explicitly an
    acceptable source per `packaging/vendor/README.md` and
    `LICENSE_NOTICE.md`) into `packaging/vendor/ffmpeg`.
  - `packaging/Get-VendorVcRedist.ps1` — downloads the genuine Microsoft
    VC++ Redistributable from `aka.ms/vs/17/release/vc_redist.x64.exe`
    into `packaging/vendor/vcredist`, best-effort (the installer works
    without it by Phase 14's own design).
  - `packaging/Set-PackageVersion.ps1` (+ regression test
    `packaging/Tests/Test-SetPackageVersion.ps1`) — stamps one version
    into both `SceneForge.App.csproj`'s `<Version>`/`<AssemblyVersion>`/`<FileVersion>`
    and `SceneForge.iss`'s `MyAppVersion`, so the two Phase-14-documented
    independent sources of version truth can never drift apart in an
    automated run.
  - An explicit `dotnet restore src/SceneForge.App/SceneForge.App.csproj -r win-x64`
    step immediately before `Publish-SceneForge.ps1` — see the confirmed
    defect below.
- **Updated `docs/CI_RELEASE_GUIDE.md`** to document the unified pipeline,
  why the reconciliation happened, and the risks/limitations discovered
  during it.

### Commands executed and results

```text
dotnet build SceneForge.sln --configuration Release
  -> Build succeeded, 0 Warning(s), 0 Error(s) (run twice: immediately
     after the merge, and again after all reconciliation edits)

dotnet test SceneForge.sln --no-build --configuration Release --logger "console;verbosity=minimal"
  -> 650/650 passed (8 Core, 31 Accuracy, 46 Infrastructure, 61 App,
     504 Media) - matches Phase 14's own reported count exactly

python -c "import yaml; yaml.safe_load(open('.github/workflows/release.yml'))"
  -> YAML OK, re-run after every edit; step order independently confirmed
     via a small python check that prints jobs.build.steps in order

powershell -File packaging\Tests\Test-ReleaseVersioning.ps1
  -> all 15 cases passed (unaffected by the merge)

powershell -File packaging\Tests\Test-SetPackageVersion.ps1
  -> all cases passed (3 valid-version cases + 1 invalid-core case that
     must throw)

# Real, direct execution of the packaging chain on this machine (not the
# GitHub Actions workflow itself - no gh credentials available - but every
# script the workflow calls, in the same order, with real inputs):
.\packaging\Get-VendorFfmpeg.ps1
  -> "Vendored 9 file(s)" into packaging/vendor/ffmpeg (ffmpeg.exe,
     ffprobe.exe, 7 matching DLLs) - confirmed real, not a stub

.\packaging\Get-VendorVcRedist.ps1
  -> "Vendored VC_redist.x64.exe (24.4 MB)" into packaging/vendor/vcredist

.\packaging\Set-PackageVersion.ps1 -Version "0.0.0-recon-e2e"
dotnet restore src/SceneForge.App/SceneForge.App.csproj -r win-x64
.\packaging\scripts\Publish-SceneForge.ps1
  -> Publish complete (65.4 MB self-contained exe), OpenCV native library
     staged, ffmpeg staged (10 file(s))

.\packaging\scripts\New-PortableZip.ps1
  -> packaging/output/SceneForge-0.0.0-recon-e2e-win-x64-portable.zip
     (154.5 MB), no ffmpeg-missing warning (real ffmpeg was staged)

.\packaging\scripts\Verify-PortableBuild.ps1  (shell: powershell, per release.yml)
  -> "PASS: all native-component diagnostics reported success in the
     isolated folder" - a genuine pass, in an isolated %TEMP% folder with
     PATH cleared, not simulated
```

**Not run**: `choco install innosetup` + `ISCC.exe packaging\installer\SceneForge.iss`
— Inno Setup is not installed on this machine, and installing new,
persistent software onto it for this check was judged out of scope (this
is a real development machine, not an ephemeral CI runner). The `.iss`
file itself was not modified by this reconciliation beyond what
`Set-PackageVersion.ps1` already regression-tests (the `MyAppVersion`
stamp), so Phase 14's own prior verification of that exact file (a real
`ISCC.exe` compile, `docs/PACKAGING_REPORT.md`) is the standing evidence
for the compile step itself; the actual GitHub Actions run remains the
first true end-to-end proof including that step. The workflow `.yml`
itself was still not dispatched — no authenticated `gh` in this
environment.

### Review outcome

#### Blockers found and fixed

1. **Confirmed, reproducible NETSDK1047 failure in the reconciled step
   order** — not a hypothetical, actually reproduced twice. The
   solution-level `Restore` step this workflow already had (needed for
   `Build`/`Test`) writes `src/SceneForge.App/obj/project.assets.json`
   without a win-x64 target, since a plain solution restore has no RID
   context. `Publish-SceneForge.ps1`'s `dotnet publish
   -p:PublishProfile=win-x64-Release` then fails outright
   (`error NETSDK1047: Assets file ... doesn't have a target for
   'net8.0-windows/win-x64'`) because MSBuild's incremental-restore check
   treats the existing assets file as already satisfied and does not
   regenerate it for the RID the PublishProfile needs. Reproduced
   identically twice (once with a version-stamp edit in between, once
   without) before isolating the cause. **Fixed**: an explicit
   `dotnet restore src/SceneForge.App/SceneForge.App.csproj -r win-x64`
   step now runs immediately before `Publish-SceneForge.ps1`. Also
   discovered and confirmed by direct testing: this restore must run
   *after* any step that edits the csproj (`Set-PackageVersion.ps1`) —
   editing the csproj again after the RID restore reproduces the same
   failure — which is why the workflow's step order is Stamp → fetch
   vendor inputs → RID-restore → Publish, not the other way around. This
   is exactly the class of defect Phase 14's own `docs/PACKAGING_REPORT.md`
   says only running the real pipeline (not reading it) would catch, and
   that is exactly how it was found here.
2. **`Invoke-WebRequest` timeout in both new fetch scripts.** Windows
   PowerShell's default progress-bar rendering makes `Invoke-WebRequest`
   extremely slow for large files - the first real run of
   `Get-VendorFfmpeg.ps1` (a 67 MB download) timed out at 3 minutes.
   **Fixed**: `$ProgressPreference = 'SilentlyContinue'` added to both
   `Get-VendorFfmpeg.ps1` and `Get-VendorVcRedist.ps1`; the retried
   download then completed in seconds. This would very likely have caused
   real, confusing timeouts in the actual GitHub Actions run had it not
   been caught here.

#### Minor issues found and fixed

3. **Inaccurate file count in `Get-VendorFfmpeg.ps1`'s own success
   message.** Counted every file already present in the destination
   directory (including the pre-existing `.gitkeep`), so a fresh vendor
   directory reported "10 file(s)" when only 9 were actually vendored.
   **Fixed**: count now filters to `.exe`/`.dll` extensions only,
   confirmed to report "9 file(s)" correctly on re-run.
4. **Duplicate `packaging/output/` `.gitignore` entries.** The merge
   combined this branch's own `packaging/output/` ignore rule (added
   during the Phase 15 review, under a "Locally built installer/portable
   packages" comment referencing the now-deleted `packaging/installer.iss`)
   with Phase 14's separate, more complete vendor/output ignore block.
   **Fixed**: removed the stale, now-inaccurate duplicate block; Phase
   14's block (which already covers `packaging/output/`) is the only one
   left.

#### Verified, not just re-read

- Every packaging script name, parameter, and output path this workflow
  now depends on (`Publish-SceneForge.ps1`'s fixed output at
  `src\SceneForge.App\bin\publish\win-x64`, its `-VendorFfmpegDir`/
  `-SkipFfmpegStaging` parameters, `New-PortableZip.ps1`'s `-PublishDir`
  parameter and csproj-`<Version>`-derived naming, `Verify-PortableBuild.ps1`'s
  `-SourceDir`/`-ZipPath` parameters, `SceneForge.iss`'s `MyAppVersion`/
  `SourceDir`/`OutputDir`/`VcRedistPath` `#define`s) was confirmed by
  reading the actual script source, then confirmed again by running the
  real chain end to end and observing the documented paths/behavior
  actually occur.
- The FFmpeg build fetched by `Get-VendorFfmpeg.ps1` was confirmed to
  contain `bin/ffmpeg.exe`, `bin/ffprobe.exe`, and the matching
  `avcodec-63`/`avformat-63`/`avutil-61`/`swresample-7`/`swscale-10`/
  `avdevice-63`/`avfilter-12` DLLs, plus a `LICENSE.txt`, by actually
  downloading and inspecting the archive before writing the script -
  not assumed from the filename alone.
- `https://aka.ms/vs/17/release/vc_redist.x64.exe` was confirmed to
  resolve (via a redirect to `download.visualstudio.microsoft.com`) to a
  real, current `VC_redist.x64.exe` before being wired into a script.

### Conclusion

The packaging-pipeline duplication flagged in the Phase 15 review above is
resolved: `15-github-actions-release` now contains exactly one packaging
pipeline (Phase 14's), and the release workflow drives it. The Visual C++
Redistributable gap flagged in that same review is substantially closed:
`packaging/Get-VendorVcRedist.ps1` makes the installer's already-built,
already-tested VC++ bootstrap step actually fire in an automated run,
best-effort. One genuine blocker (NETSDK1047 from restore/publish
sequencing) and one real operational defect (slow/timing-out downloads)
were found by actually running the reconciled pipeline end to end on this
machine, not merely by reading the YAML - both are fixed and the fix was
itself re-verified by re-running. The only step not exercised for real
anywhere in this repository's history yet is the actual `ISCC.exe` compile
of the now-version-stamped `SceneForge.iss` from inside this exact
workflow, and the workflow's own dispatch on GitHub Actions - both remain
the outstanding verification step, unchanged from the Phase 15 review's
own conclusion, now with a substantially larger fraction of the pipeline
independently proven correct.

## Phase 16 review — 2026-08-27

Date: 2026-08-27

### Scope

Strict release review of branch `16-acceptance-testing`, commit `abe5915`
"Step 16: Guarantee output duration always matches target audio via
provable reuse-cap escalation", against [CLAUDE.md](CLAUDE.md) (rules 5-10
foremost), [docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md)
Decisions 5 and 7, and the phase's own task: guarantee
`TimelinePlanner`-planned output always matches the target audio duration
exactly by relaxing `MaximumReuseCount` (then spacing constraints) before
ever reporting a shortfall, documented in
[docs/PHASE_16_REPORT.md](docs/PHASE_16_REPORT.md). The prior phase's own
report was treated as a claim to verify, not a fact to trust: the actual
diff (`git show abe5915`), the new/changed tests, and the report's own
quantified claims were independently inspected and re-run rather than
relying on the report's narrative alone, and the running application's
actual call path (`TimelineSummaryViewModel` → `ITimelinePlanner.Plan`) was
traced end to end rather than reviewing `TimelinePlanner.cs` in isolation.

### Commands executed and results

```text
git show --stat HEAD / git log --oneline -5           -> commit contents confirmed,
                                                           branch 1 commit ahead of main

dotnet build SceneForge.sln -c Release (before any review fix)
  -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test SceneForge.sln -c Release --no-build
  -> 660/660 passed (8 Core, 31 Accuracy, 61 App, 46 Infrastructure, 514 Media)

# Independent empirical verification of the phase report's own headline
# numeric claim ("800s achieved / 520s shortfall under the old cap=1
# behavior"), via a temporary scratch test (written, run, then deleted -
# never committed) reproducing the exact 200-clip pool at MaximumReuseCount=1
# with a target equal to the pool's own total duration:
  -> 200/200 clips placed, 800s achieved exactly, IsComplete=true,
     FeasibilityWarning=null - confirms the claim was sound, not asserted
     on faith.

# Worst-case latency measurement of the new reuse-relaxation path (also via
# temporary scratch tests, deleted after use), since TimelineSummaryViewModel.BuildPlan
# called ITimelinePlanner.Plan synchronously:
  500 clips / 4-hour target  -> 1033 ms,  4,800 placements
  200 clips / 2-hour target  ->  170 ms,  2,400 placements
   50 clips / 1-hour target  ->   65 ms,  1,200 placements
   20 clips / 22-min target  ->    5 ms,    440 placements
 1000 clips / 24-hour target -> 6,099 ms, 86,400 placements
    1 clip / ~22-day target  (forces ~MaxReuseRelaxationHeadroom) -> 18,038 ms, 1,900,000 placements, IsComplete=true

# After the fix (TimelineSummaryViewModel.BuildPlan now runs Plan via
# Task.Run with a real CancellationToken):
dotnet build tests/SceneForge.App.Tests/SceneForge.App.Tests.csproj -c Release
  -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test tests/SceneForge.App.Tests/SceneForge.App.Tests.csproj -c Release --filter "FullyQualifiedName~TimelineSummary"
  -> 7/7 passed (2 pre-existing rewritten for the new async shape, 3 new
     regression tests added by this review)
dotnet test tests/SceneForge.App.Tests/SceneForge.App.Tests.csproj -c Release
  -> 64/64 passed

dotnet build src/SceneForge.App/SceneForge.App.csproj -c Release
  -> Build succeeded, 0 Warning(s), 0 Error(s) (confirms the XAML busy-indicator
     addition compiles)

dotnet build SceneForge.sln -c Release / -c Debug   -> both 0 Warning(s), 0 Error(s)
dotnet test SceneForge.sln -c Release --no-build
  -> 663/663 passed (8 Core, 31 Accuracy, 64 App, 46 Infrastructure, 514 Media)
dotnet test SceneForge.sln -c Debug --no-build
  -> 663/663 passed (no flake this run - Phase 16's own report already
     documented the pre-existing, unrelated TransitionDetectorTests
     GC-timing flake pattern from Phases 6/7; not observed in this review's
     run)

dotnet format SceneForge.sln
dotnet format SceneForge.sln --verify-no-changes
  -> exit 0, no diff
```

**Relevant benchmarks:** none exist for `TimelinePlanner` (Phase 8 already
listed this as outstanding for itself; Phase 16's own report correctly
notes CLAUDE.md rule 9 targets *optimizations*, and this phase is a
correctness change, not one). The worst-case-latency measurements above are
this review's substitute evidence for the one performance-adjacent claim
actually at stake — whether the new code path could block the UI thread —
which is exactly what a `BenchmarkDotNet` microbenchmark would not have
caught on its own (the defect was architectural: *where* the call ran, not
how fast the algorithm itself is).

### Review outcome

#### Blockers

None. Every acceptance-criteria claim in `docs/PHASE_16_REPORT.md` — the
duration-guarantee property tests, the realistic 24-min/22-min scenario, the
"never exceeds" reuse-cap semantics, the rewritten Phase 8 tests — was
independently re-run and confirmed correct. The one real defect found (below)
was a UI-thread-blocking risk, not a correctness or data-safety defect.

#### Major issues (found, fixed in this review)

1. **`TimelineSummaryViewModel.BuildPlan` called the new,
   potentially-long-running `ITimelinePlanner.Plan` synchronously on the UI
   thread, with `CancellationToken.None` — a CLAUDE.md rule 5 violation
   Phase 16's own diff introduced without noticing.** `TimelinePlanner.Plan`
   was deliberately kept synchronous in Phase 8 specifically *because* it
   was always fast (`MaximumReuseCount` was a small, never-relaxed hard cap
   — see `ITimelinePlanner.cs`'s own doc comment, which still cited that
   now-outdated reasoning). Phase 16 removed that guarantee: `Plan` can now
   legitimately run for hundreds of milliseconds to multiple seconds
   whenever footage is insufficient at the requested cap, and
   `TimelineSummaryViewModel.BuildPlan` — invoked directly from the
   constructor and from the `Reshuffle` command, both on the UI thread —
   never changed to account for that. Measured directly (see "Commands
   executed" above): a plausible large-project scenario (500 clips, a
   4-hour target) already took just over one second; the genuinely extreme
   end (a single clip against a ~22-day target, deliberately forcing
   `MaxReuseRelaxationHeadroom`'s upper bound) took over 18 seconds — an
   18-second frozen, unresponsive window with no way to cancel, the exact
   failure mode CLAUDE.md rule 5 exists to prevent. This was not
   hypothetical: it reproduces on every call whose footage is insufficient
   at the requested cap, which — as of this same phase — is now the
   documented, intended behavior for underprovisioned footage rather than
   a rare edge case.

   **Fixed**: `TimelineSummaryViewModel` now follows the exact
   `IsRunning`/`CanExecute`/`CancellationTokenSource` shape
   `AnalysisProgressViewModel` already established for this same concern
   (adapted for a CPU-bound `Task.Run` offload instead of already-async
   I/O): a new `IsBuilding` observable property gates `ReshuffleCommand` and
   `ContinueCommand` via `[NotifyCanExecuteChangedFor]`, `BuildPlan` is now
   `[RelayCommand] private async Task`, and `Plan` runs inside
   `Task.Run(() => _timelinePlanner.Plan(request, cancellationToken),
   cancellationToken)` with a real, live `CancellationTokenSource` owned by
   the ViewModel (previously always `CancellationToken.None` in practice).
   Both awaits use `ConfigureAwait(true)` so the continuation that mutates
   `Placements` (an `ObservableCollection` bound to a WPF `ListView`)
   correctly resumes on the UI thread rather than a thread-pool thread — a
   cross-thread-collection-mutation crash this review specifically checked
   for and confirmed avoided, since nothing in a headless xUnit run would
   have caught a `ConfigureAwait(false)` mistake here (no real WPF
   `Dispatcher` exists in the test host). `TimelineSummaryView.xaml` gained
   a small `IsBuilding`-bound "Building timeline..." indeterminate
   `ProgressBar`, matching `AnalysisProgressView.xaml`'s existing pattern,
   so the now-disabled buttons do not look like an unexplained freeze.
   `ITimelinePlanner.cs`'s doc comment claiming an async signature "would be
   misleading" is now only true at the interface/algorithm level — the
   caller-side fix is the correct layer to have applied this at, since
   changing the interface itself would have disrupted every other
   `TimelinePlanner` caller/test for no benefit `Task.Run` at the one
   long-running call site does not already provide.

   New regression coverage (`tests/SceneForge.App.Tests/TestSupport/FakeTimelinePlanner.cs`,
   new; `TimelineSummaryViewModelTests.cs`, 3 new tests): a deterministic
   `Gate`-based test (mirroring `FakeTransitionDetector`'s existing pattern
   — no wall-clock timing assumptions, cannot flake) proves `IsBuilding` and
   `CanExecute` on `ReshuffleCommand`/`ContinueCommand` are correctly false
   mid-flight and true again after completion; a second test proves a real,
   live, cancelable `CancellationToken` is actually threaded through (not
   `CancellationToken.None`); a third proves `BuildPlan`'s
   `catch (OperationCanceledException)` path clears `IsBuilding` and leaves
   the ViewModel usable rather than getting stuck. The four pre-existing
   tests were updated to `await` command completion via
   `IAsyncRelayCommand.ExecutionTask` (CommunityToolkit.Mvvm 8.4.0, already
   the pinned package version) instead of asserting state synchronously
   right after construction, which the new genuinely-asynchronous `Task.Run`
   offload made stale (a real `Task.Run` always yields, unlike
   `AnalysisProgressViewModelTests`' fully-synchronous I/O fakes).

   `MaxReuseRelaxationHeadroom` (`TimelinePlanner.cs`) was considered for
   reduction as additional defense-in-depth once the UI-thread fix was in
   place, and briefly changed to 200,000 during this review — but reverted
   back to Phase 16's original 2,000,000 after measurement showed the
   smaller bound would make the plan **genuinely incomplete** (a real
   `IsComplete = false` shortfall) for inputs the larger bound still
   satisfies exactly. Given this phase's own explicit, non-negotiable
   product requirement ("never produce a short output ... no matter what"),
   and given the actual UI-freeze risk is already eliminated by the
   `Task.Run` offload (an 18-second *background* computation is a far
   smaller concern than an 18-second *frozen window*), narrowing the
   completion guarantee's envelope for comparatively little remaining
   safety benefit was judged the wrong trade — documented in
   `TimelinePlanner.cs`'s own comment on the constant so a future reader
   does not have to rediscover this reasoning.

#### Minor issues

- `TimelineSummaryView.xaml`'s "planned duration" card is still visible
  (with stale/zero values) underneath the new "Building timeline..."
  indicator while a build is in flight, since its own visibility is gated
  on `ErrorMessage == null` rather than also excluding `IsBuilding`. Not
  incorrect (values are simply overwritten the moment the build completes)
  and not addressed in this review — a small XAML polish item, not a defect
  meeting the bar for a "major issue" fix here.
- `TimelineSummaryViewModel.Dispose()` disposes its
  `CancellationTokenSource` without cancelling it first (`Cancel()` is never
  called), identical to `AnalysisProgressViewModel.Dispose()`'s existing
  shape — meaning a ViewModel torn down mid-build does not actually stop
  the in-flight background computation early. This matches an existing,
  pre-Phase-16 pattern in this exact codebase rather than being a new gap,
  and no navigation-level code anywhere in this repository currently calls
  either ViewModel's `Dispose()` at all (confirmed by search — only
  `App.xaml.cs`'s `_serviceProvider?.Dispose()` on full app shutdown), so
  this is unlikely to matter in practice today. Left as-is to avoid
  unrelated scope creep into `AnalysisProgressViewModel`.

#### Checklist: specific risk categories

| Category | Finding |
|---|---|
| Web dependencies | None — no new package reference, no HTTP/socket call anywhere in the diff (confirmed by reading every changed file). |
| Unbounded memory/concurrency | `ComputeGuaranteedSufficientReuseCap`'s relaxed cap is bounded by `MaxReuseRelaxationHeadroom` (2,000,000, unchanged after this review's reversion — see Major issue 1); each `TimelineSummaryViewModel.BuildPlan` call owns exactly one `CancellationTokenSource`, disposed and replaced each call, no unbounded fan-out. Not a violation. |
| UI-thread work | **Found and fixed — Major issue 1.** |
| Unsafe process invocation | N/A — no process/FFmpeg/OpenCvSharp code touched by this phase's diff at all. |
| Timing drift | Checked deliberately: the one floating-point operation this phase introduces (`quantizedTarget / shortestPositiveDuration` in `ComputeGuaranteedSufficientReuseCap`) is proven bounded well within `long`/`double` range by `TimeSpan`'s own structural limits (max ~3.16×10^18 ticks), used only to derive an `int` *cap*, never to compute or compare an actual planned duration — every duration value in the returned `TimelinePlan` remains exact `TimeSpan`/tick arithmetic, unchanged from Phase 8. No drift risk. |
| Silent fallback | None found — every placement that exceeds the originally-requested `MaximumReuseCount` is tagged `RelaxedConstraint.MaximumReuseCount` on its trace entry (verified by reading `PlanWithReuseCap` and by the existing `Plan_InsufficientFootage_RelaxesMaximumReuseCount_AndStillReachesTargetExactly`/property-test coverage), and `TimelineFeasibilityWarningKind.SignificantRepetition` surfaces the relaxation to the caller even when `IsComplete` is true, per the product requirement's own transparency clause. |
| Missing cancellation | **Found and fixed — Major issue 1** (the UI-thread caller never passed a live token). `TimelinePlanner.Plan` itself already checked a passed-in token once per placement since Phase 8, unchanged and re-confirmed correct in this review. |
| Unverifiable claims | Phase 16's report's central "800s / 520s shortfall" numeric claim was independently reproduced (see "Commands executed"), not merely trusted. This review's own worst-case latency numbers are likewise measured, not estimated. |
| Packaging omissions | N/A — this phase's diff does not touch packaging. |

### Fixes applied in this review

- `src/SceneForge.App/ViewModels/TimelineSummaryViewModel.cs`: `BuildPlan`
  converted to an async, cancelable, `Task.Run`-offloaded command with a new
  `IsBuilding` state gating `Reshuffle`/`Continue`; implements `IDisposable`
  for its `CancellationTokenSource`, matching `AnalysisProgressViewModel`'s
  established shape.
- `src/SceneForge.App/Views/TimelineSummaryView.xaml`: added an
  `IsBuilding`-bound indeterminate `ProgressBar` and status text.
- `tests/SceneForge.App.Tests/TestSupport/FakeTimelinePlanner.cs` (new): a
  gate-/throw-/token-capturing test double for `ITimelinePlanner`, mirroring
  `FakeTransitionDetector`'s existing convention.
- `tests/SceneForge.App.Tests/ViewModels/TimelineSummaryViewModelTests.cs`:
  4 pre-existing tests updated to await the new async shape; 3 new
  regression tests added (mid-flight gating, live-token verification,
  cancellation recovery) — see Major issue 1 above.
- `src/SceneForge.Media/Planning/TimelinePlanner.cs`: `MaxReuseRelaxationHeadroom`'s
  comment expanded to document the 200,000-vs-2,000,000 trade-off this
  review considered and explicitly rejected, so the reasoning is not lost.
  No behavior change from Phase 16's original value.

### Conclusion

No blockers. One confirmed major issue — the UI thread could freeze for
several seconds to (in an extreme, now-measured case) over eighteen seconds,
with no cancellation actually wired up, as a direct and unnoticed
consequence of Phase 16's own reuse-relaxation change — was found by tracing
the real call path from the UI down into `TimelinePlanner.Plan` rather than
reviewing the algorithm in isolation, fixed following this codebase's own
established async-offload pattern, and verified both by rerunning the full
build/test suite (663/663 in both Debug and Release) and by re-measuring the
same worst-case scenario to confirm the UI thread is no longer the one
paying that cost. `docs/PHASE_16_REPORT.md` is updated alongside this review
to reflect the fix and the corrected test counts.

## Phase 17 review — 2026-08-29

Date: 2026-08-29

### Scope

Strict release review of branch `17-ui-polish` (working tree; no commits
yet, `HEAD` at `30a9b56`) against [CLAUDE.md](CLAUDE.md),
[docs/ARCHITECTURE_DECISIONS.md](docs/ARCHITECTURE_DECISIONS.md) Decisions
1-2 and 5, and the phase's own [docs/UI_POLISH_REPORT.md](docs/UI_POLISH_REPORT.md)
plus the packaging addendum in [docs/PACKAGING_REPORT.md](docs/PACKAGING_REPORT.md).
Phase scope: purely visual/UX polish of the WPF shell + 8 screens (custom
`WindowChrome` title bar, softer accent palette, type scale, spacing
tokens, retemplated controls), a follow-up round fixing a real
title-bar-clipped-off-screen regression plus capsule / lighter-blue
buttons, and a packaging-contents audit. The reports were treated as claims
to verify: the actual diff, tests, build, a real run of the built exe, and
the packaging pipeline were inspected and re-run independently.

### Commands executed and results

```text
git diff --stat                                     -> 18 files, +1049/-156; 8 new untracked
                                                       (all under src/SceneForge.App, tests/, packaging/, docs/)
dotnet build SceneForge.sln -c Release               -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet build SceneForge.sln -c Debug                 -> Build succeeded, 0 Warning(s), 0 Error(s)
dotnet test  SceneForge.sln -c Release --no-build     -> 699/699 passed, 0 skipped
                                                       (Core 8, Accuracy 31, Infrastructure 46, App 77, Media 537)
dotnet format SceneForge.sln --verify-no-changes      -> clean, no diff
dotnet build benchmarks\SceneForge.Benchmarks         -> Build succeeded (no benchmark relevant - see below)
packaging\scripts\Publish-SceneForge.ps1             -> "Removed 4 .pdb symbol file(s)", "ffmpeg staged: 9 file(s)",
                                                       65.4 MB single-file exe
packaging\scripts\New-PortableZip.ps1                -> SceneForge-1.0.0-win-x64-portable.zip, 154.4 MB, 17 entries
  (extracted + scanned)                              -> CLEAN: no .cs/.csproj/.sln/.pdb/dotfiles
packaging\scripts\Verify-PortableBuild.ps1 -ZipPath  -> "PASS: all native-component diagnostics reported success
                                                       in the isolated folder" (fresh %TEMP%, PATH cleared)
packaging\Tests\Test-PortableZipContents.ps1          -> all cases passed
packaging\Tests\Test-SetPackageVersion.ps1            -> all cases passed
packaging\Tests\Test-ReleaseVersioning.ps1            -> all 15 cases passed
Ran the built SceneForge.App.exe, driven via UI Automation:
  restored window rect (43,0)-(1323,728), top >= 0                          -> title bar on-screen (not clipped)
  Maximize button  -> window (0,0)-(1366,728) == work area exactly          -> no taskbar cover, no off-screen spill
  Restore button   -> present when maximized, restores                     -> ok
  Minimize button  -> WindowVisualState = Minimized                        -> ok
  Close button     -> process exits                                        -> ok
STA layout probe: ListView (app style) + 3000 items in a 300px host        -> VirtualizingStackPanel, IsVirtualizing=True,
                                                                              Recycling, Pixel, CanContentScroll=True,
                                                                              9 realized containers  (virtualization intact)
Relative-luminance contrast script over the actual palette hex values      -> every text pairing >= WCAG AA 4.5:1
                                                                              (light + dark); numbers folded into the report
```

### Findings

**Blockers:** none.

**Major issues:** none. The follow-up round's own real bug (custom caption
clipped above the screen top on a display shorter than the 820 px default
window, because `WindowStyle="None"` leaves Windows no OS caption to keep
on-screen) was already found and fixed by the phase before this review,
with a unit test (`WindowPlacementMathTests`) and a real-run verification;
this review re-confirmed both.

**Minor issues:**

| # | Issue | Disposition |
|---|---|---|
| 1 | **`ListView` was fully retemplated** (rounded container) but the CLAUDE.md rule 6/7 / Phase 10 "load-bearing" virtualization property was only argued in prose, not tested. | **Verified intact** empirically (STA layout probe: 3000 items -> 9 realized containers, panel is `VirtualizingStackPanel`, `CanContentScroll`/`IsVirtualizing` true) and **`ListViewVirtualizationTests` added** so a future retemplate that breaks it fails `dotnet test`. |
| 2 | `MainWindow.WindowProc` (a `WM_GETMINMAXINFO` hook) could let a P/Invoke exception escape into the WPF message loop. | **Fixed** - hook body wrapped in `try/catch`; on the failure path it leaves the message unhandled so the OS default maximize applies (cosmetic, not a crash). |
| 3 | `Marshal.StructureToPtr(mmi, lParam, fDeleteOld: true)` - `true` is the wrong value when overwriting an OS-owned `MINMAXINFO` buffer (harmless for this all-blittable struct, but incorrect). | **Fixed** to `false`. Maximize re-verified on the running exe (exact work-area fit). |
| 4 | `docs/UI_POLISH_REPORT.md` claimed `Card` has a "barely-there drop shadow" - the `Card` style has a 1-px border and no `Effect`. Test counts were stale (76/698). One contrast-table row still referenced the pre-capsule `AccentText on Accent` pairing. | **Report corrected** - no shadow, 77/699, re-measured contrast table. |
| 5 | `WindowStyle="None"` removes the Alt+Space system menu; `FitIntoWorkArea` first-centres on the *primary* monitor's work area regardless of which monitor the window later opens on. | **Documented** as known limitations in `UI_POLISH_REPORT.md` section 9. Caption buttons are keyboard-reachable (Tab); Alt+F4 still closes. Not worth the added surface to fix for a first-launch position nicety. |
| 6 | Outer `Brush.WindowBorder` edge is ~1.8:1 (light) / ~2.5:1 (dark) vs the window background - below the 3:1 non-text-contrast guideline if read as a UI-component boundary. | **Left as-is, documented.** The window-boundary cue is carried by the DWM `GlassFrameThickness="1"` system frame + drop-shadow (user-agent-drawn, WCAG-exempt); `WindowBorder` is a supplementary inner line. The real-run screenshot shows the window clearly bounded. |
| 7 | `#pragma warning disable SA1307, SA1310` in `MainWindow.xaml.cs` references StyleCop rules not enabled in this repo. | **Left** - harmless no-op; documents the intent (lowercase native-struct field names). Build is 0-warning with or without it. |
| 8 | Pre-existing: `Brush.Warning` is defined in both palettes but referenced nowhere in the app. Pre-existing: portable ZIP / installer ship no third-party license text. | Not introduced by Phase 17. The license-file gap is already tracked (Phase 14 review "known gaps toward a public release"; `LICENSE_NOTICE.md` states legal review is required before any public distribution). |

### Risk-category checklist

| Category | Result |
|---|---|
| Web dependencies | None. No `HttpClient`, no URL fetched at runtime; the only new native call is `user32.dll` `GetMonitorInfo`/`MonitorFromWindow`. `AppSupportURL` in the `.iss` is Add/Remove-Programs metadata only. |
| Unbounded memory / concurrency | None. No new queues/caches/threads/`Task.Run`. ListView virtualization confirmed intact (finding 1). The one `DropShadowEffect` is on the transient ComboBox popup only. |
| UI-thread work | All new code (`FitIntoWorkArea`, `OnWindowStateChanged`, the `WM_GETMINMAXINFO` hook, `WindowChromeViewModel` commands) is trivial synchronous arithmetic / property sets on the UI thread. No blocking I/O, no `.Result`/`.Wait()`. |
| Unsafe process invocation | None added. Packaging scripts invoke `dotnet publish` at dev time only. |
| Timing drift | N/A - no duration / frame-rate / timeline math touched. `WindowPlacementMath` is pure geometry, no time. |
| Silent fallback | The `WM_GETMINMAXINFO` hook degrades gracefully (OS default maximize) if `MonitorFromWindow`/`GetMonitorInfo` fail or the body throws - acceptable for window chrome, and now explicit (finding 2). Font fallback (`Segoe UI Variable Text` -> `Segoe UI`) is normal. |
| Missing cancellation | N/A - no async / long-running operations added. |
| Unverifiable claims | The report's contrast numbers were re-derived from the palette hex values (script); the "Card drop shadow" and test-count claims were wrong and corrected (finding 4); the real-run window-control behaviour was reproduced, not trusted. |
| Packaging omissions | Audited: the fresh portable ZIP is 17 runtime-only files (`SceneForge.App.exe` + 5 WPF native `*_cor3.dll` + `tools\ffmpeg\` x9 + `tools\opencv\` x2). The prior round's `.gitkeep` leak and 4 `.pdb` files are gone (ffmpeg staging now skips dotfiles; publish strips `*.pdb`). `New-PortableZip.ps1` gained a hard gate + `SceneForge.iss` an `Excludes:` clause; `Verify-PortableBuild.ps1` still PASSes. Debug-symbol decision (strip; deterministic build makes exact-commit symbols regenerable) is documented in `PACKAGING_REPORT.md`. |

### Fixes applied in this review

- `src/SceneForge.App/MainWindow.xaml.cs`: `WM_GETMINMAXINFO` hook wrapped
  in `try/catch` (never let an exception into the message loop);
  `Marshal.StructureToPtr` -> `fDeleteOld: false`.
- `tests/SceneForge.App.Tests/Themes/ListViewVirtualizationTests.cs` (new):
  STA-thread probe asserting the retemplated `ListView` still virtualizes
  (3000 items -> < 100 realized containers; `VirtualizingStackPanel`;
  `IsVirtualizing` / `CanContentScroll` true).
- `docs/UI_POLISH_REPORT.md`: removed the false "Card drop shadow" line,
  corrected test counts (77 App / 699 solution), replaced the contrast
  table with re-measured ratios, added two new known limitations
  (Alt+Space, primary-monitor first-centre) and a "Release review" section.

### Conclusion

No blockers, no major issues. The one genuine risk a strict review of a
control-retemplating UI phase must chase - that rounding the `ListView`
container silently defeated the virtualization CLAUDE.md rule 6/7 and the
Phase 10 report both call load-bearing - was run down empirically (it
holds: 9 of 3000 containers realized) and is now locked in by a regression
test. Two small stability/correctness hardenings were applied to the
`WM_GETMINMAXINFO` P/Invoke hook and re-verified on the running exe. The
portable ZIP was rebuilt from scratch and re-audited (17 runtime-only
files, `Verify-PortableBuild.ps1` PASS). Full suite 699/699 in Release;
build and format clean. `docs/UI_POLISH_REPORT.md` is updated alongside
this review.
