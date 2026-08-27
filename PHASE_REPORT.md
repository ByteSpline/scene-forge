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
