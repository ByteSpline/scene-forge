# CI Release Guide

This document describes `.github/workflows/release.yml`: the manual pipeline
that builds, tests, and packages SceneForge, and optionally publishes a
tagged GitHub Release. It is separate from `.github/workflows/ci.yml`, which
is the automatic pull-request gate (restore/build/test/format/accuracy
regression) and is unaffected by anything in this document.

## Why this workflow calls Phase 14's scripts

This workflow does not implement its own publish/package/installer logic.
It drives the packaging pipeline branch `14-packaging` already built and
verified in [docs/PACKAGING_REPORT.md](PACKAGING_REPORT.md):
`packaging/scripts/Publish-SceneForge.ps1`, `New-PortableZip.ps1`,
`Verify-PortableBuild.ps1`, and `packaging/installer/SceneForge.iss`, all
built around the `packaging/vendor/ffmpeg` + `packaging/vendor/vcredist`
vendoring convention.

An earlier version of this workflow was written without visibility into
that branch and built a second, independent pipeline
(`packaging/installer.iss`, `packaging/Stage-Ffmpeg.ps1`) that solved the
same problem differently — a Chocolatey-fetched ffmpeg instead of a vendored
one, no VC++ Redistributable handling, no reuse of the in-app
`NativeDependencyDiagnosticsService`/`StartupDiagnosticsWindow` that Phase
14 built specifically to catch a broken native-dependency package before a
user hits it. Once `14-packaging` was merged into this branch, that
duplication was reconciled by deleting this branch's own
`packaging/installer.iss` / `packaging/Stage-Ffmpeg.ps1` and rewriting this
workflow to call Phase 14's real scripts instead — Phase 14's pipeline is
the more complete of the two and is now the only one in this repository.
This workflow adds exactly three things Phase 14's local, manual workflow
did not need to automate for itself:

1. **`packaging/Get-VendorFfmpeg.ps1`** — downloads a pinned, verifiably
   LGPL-only FFmpeg build (BtbN's `*-lgpl-shared` releases, explicitly
   named as acceptable in `packaging/vendor/README.md` and
   `LICENSE_NOTICE.md`) into `packaging/vendor/ffmpeg`, since a CI runner
   has no packager to do that by hand.
2. **`packaging/Get-VendorVcRedist.ps1`** — downloads the genuine, current
   Microsoft Visual C++ Redistributable from Microsoft's own stable
   `aka.ms` link into `packaging/vendor/vcredist`, closing the one gap
   `docs/PACKAGING_REPORT.md` flagged as unautomated. Best-effort: the
   installer compiles and works without it (`SceneForge.iss` guards every
   reference with `#ifexist`), so a transient failure here does not fail
   the whole release.
3. **`packaging/Set-PackageVersion.ps1`** — stamps a version into both
   `SceneForge.App.csproj`'s `<Version>` and `SceneForge.iss`'s
   `MyAppVersion` from one input, since a human packager doing this by
   hand (per the csproj's own "bump this for every subsequent packaged
   build" comment) is not available in an automated pipeline.

Everything else — publishing, zipping, verifying, and compiling the
installer — is Phase 14's own, unmodified scripts.

## Trigger: manual only

`release.yml` defines exactly one trigger — `workflow_dispatch`. It does
**not**, and must not, run on `push` or `pull_request`. Publishing a build
artifact or a Release is a deliberate, human-initiated action, never an
automatic side effect of merging code. If a future change ever adds an
automatic trigger to this workflow, that change violates the intent of this
document and should be reverted.

### Inputs

| Input | Required | Values | Purpose |
|---|---|---|---|
| `ref` | yes | branch, tag, or commit SHA (default `main`) | What to check out and build. |
| `build_type` | yes | `artifact-only` (default) or `versioned-release` | Whether to only produce workflow artifacts, or also tag and publish a GitHub Release. |
| `version` | only for `versioned-release` | semantic version, e.g. `1.4.0` or `1.4.0-rc.1` | Used as the release tag (`vX.Y.Z`) and stamped into the csproj/installer (see below). Ignored for `artifact-only` runs. |

The `Validate inputs` step fails fast if `build_type` is
`versioned-release` and `version` is missing or not a valid semantic
version (`packaging/ReleaseVersioning.psm1`'s `Test-ReleaseVersion`,
regression-tested by `packaging/Tests/Test-ReleaseVersioning.ps1`).

For `artifact-only` runs, the pipeline stamps an internal version tag of
`0.0.0-artifact.<7-char-commit-sha>` into the csproj/installer — every
artifact this workflow produces is traceable to the commit it came from,
not just `versioned-release` ones, and repeated artifact-only runs never
collide.

## What the pipeline does

Runs on `windows-latest`, matching the "native Windows WPF on .NET 8"
architecture (CLAUDE.md rule 1). Two jobs:

### Job 1 — `build` (always runs)

1. **Checkout** the requested `ref`.
2. **Validate inputs** — see above.
3. **Resolve build metadata** — reads back the actual checked-out commit SHA
   (not `github.sha`, which reflects the ref that triggered the dispatch,
   which is not necessarily the `ref` input) and computes the version tag.
4. **Setup .NET 8**, **Restore**, **Build** (`Release` configuration),
   **Test** — the same commands as `ci.yml`, run against the code exactly
   as committed, before any packaging-only file edits happen. A failing
   test fails the whole run; no later step executes (CLAUDE.md rule 15).
   Test results (`.trx`) are uploaded as a workflow artifact regardless of
   outcome.
5. **Stamp package version** — `packaging/Set-PackageVersion.ps1 -Version
   <version tag>`, edits `SceneForge.App.csproj` and
   `packaging/installer/SceneForge.iss` in the ephemeral checkout (never
   committed back).
6. **Fetch vendor ffmpeg** — `packaging/Get-VendorFfmpeg.ps1` downloads the
   pinned LGPL-shared BtbN build into `packaging/vendor/ffmpeg`.
7. **Fetch vendor VC++ Redistributable (best-effort)** —
   `packaging/Get-VendorVcRedist.ps1`; a failure here only logs a warning.
8. **Restore win-x64 publish assets** — `dotnet restore
   src/SceneForge.App/SceneForge.App.csproj -r win-x64`. Reproduced and
   confirmed necessary during this reconciliation: the earlier
   solution-level `Restore` step (needed for `Build`/`Test`) leaves
   `src/SceneForge.App/obj/project.assets.json` without a win-x64 target,
   and `Publish-SceneForge.ps1`'s later `dotnet publish
   -p:PublishProfile=win-x64-Release` then fails with NETSDK1047 ("Assets
   file ... doesn't have a target for 'net8.0-windows/win-x64'") because
   MSBuild's incremental-restore check treats that file as already
   up to date. This explicit, RID-scoped restore must run immediately
   before publish, after every step that edits the csproj (i.e., after
   "Stamp package version") — editing the csproj again after this restore
   reproduces the same failure, which is why "Stamp package version" runs
   before this step, not after.
9. **Publish self-contained build** — `packaging/scripts/Publish-SceneForge.ps1`
   (unmodified from Phase 14): `dotnet publish` via the
   `win-x64-Release` publish profile, stages ffmpeg from
   `packaging/vendor/ffmpeg` into `tools\ffmpeg\`, and verifies
   `SceneForge.App.exe` and `tools\opencv\OpenCvSharpExtern.dll` both
   exist — failing loudly if not, so a broken publish can never silently
   ship.
10. **Build portable ZIP** — `packaging/scripts/New-PortableZip.ps1`
    (unmodified), names the archive from the csproj `<Version>` just
    stamped in step 5.
11. **Verify portable build** — `packaging/scripts/Verify-PortableBuild.ps1`
    (unmodified): copies the publish output into an isolated temp folder
    outside the repo, launches it with `PATH` cleared, and uses UI
    Automation to confirm the startup diagnostics gate actually reports
    success — the same check that caught a real `ShutdownMode` bug during
    Phase 14 (see `docs/PACKAGING_REPORT.md`). Runs under `shell: powershell`
    (Windows PowerShell, not `pwsh`) — confirmed to actually work end to
    end during this reconciliation (real `PASS` output reproduced on a
    real publish output, not just assumed) — see "Known risks and
    follow-ups" for the one remaining caveat (this was verified under
    Windows PowerShell specifically, not under `pwsh`).
12. **Build installer** — installs Inno Setup via Chocolatey (pinned
    version) and compiles `packaging/installer/SceneForge.iss`
    (unmodified other than the version stamped in step 5), producing
    `packaging/output/SceneForge-<version>-win-x64-setup.exe`.
13. **Upload installer artifact** / **Upload portable artifact** — both
    uploaded as workflow artifacts (`sceneforge-installer-<version>` and
    `sceneforge-portable-<version>`), downloadable from the workflow run
    page regardless of `build_type`.

### Job 2 — `release` (only when `build_type` is `versioned-release`)

Runs on `ubuntu-latest` (no Windows-specific work here — just downloading
artifacts and calling the GitHub CLI). Depends on `build` succeeding.

1. Downloads the installer and portable-zip artifacts produced by `build`.
2. Runs `gh release create "v<version>" --target <ref> ... dist/*`, which:
   - Creates the tag `v<version>` pointing at the resolved `ref` (fails if
     that tag already exists — releases are never silently overwritten).
   - Publishes a GitHub Release titled `SceneForge v<version>`.
   - Attaches both the installer `.exe` and the portable `.zip` as release
     assets.

`artifact-only` runs never execute this job — no tag, no Release is created,
only workflow artifacts are produced.

## Required repository permissions

- **Workflow-level `permissions: contents: write`** is declared directly in
  `release.yml` (and again on the `release` job). This is the only
  permission the pipeline needs: it reads code via checkout and writes a
  tag + Release via `gh release create`. No other GITHUB_TOKEN scope
  (issues, pull-requests, packages, etc.) is requested or required.
- For the workflow-level `permissions:` block to actually grant write
  access, the repository (or organization) must allow it:
  **Settings → Actions → General → Workflow permissions** must be set to
  *"Read and write permissions"* (or the org-level default must allow
  workflows to request `contents: write`). If it is locked to *"Read
  repository contents permission"* and workflow-level overrides are
  disabled at the org level, `versioned-release` runs will fail at the
  `Create release` step with a permissions error — `artifact-only` runs are
  unaffected, since they never call `gh release create`.
- No repository secrets need to be added. `gh release create` uses the
  automatically-provided `secrets.GITHUB_TOKEN`. Fetching ffmpeg and the
  VC++ Redistributable uses public, unauthenticated URLs (a GitHub release
  download and Microsoft's own `aka.ms` link) — no token needed for those.
- Anyone dispatching this workflow needs at least **write** access to the
  repository (required by GitHub to run `workflow_dispatch` at all).

## Known risks and follow-ups

- **`Verify-PortableBuild.ps1` under `pwsh` (PowerShell 7) is still
  unverified — under Windows PowerShell it now is.** During this
  reconciliation, the full chain (stamp version → fetch real vendor
  ffmpeg → RID-scoped restore → publish → zip → verify) was actually run
  end to end on a real Windows machine, and `Verify-PortableBuild.ps1`
  produced a genuine `PASS: all native-component diagnostics reported
  success` under `shell: powershell` (Windows PowerShell 5.1) — this was
  not just assumed. What remains unverified is specifically whether
  `UIAutomationClient`/`UIAutomationTypes` resolve the same way under
  `pwsh` (PowerShell 7 / Core CLR), which this workflow deliberately does
  not use for this one step (no `pwsh` was available on the machine this
  reconciliation was done on, so that specific combination could not be
  tested). If a future change moves this step to `pwsh` for consistency
  with the rest of the file, re-verify it actually still finds the
  diagnostics window first, rather than assuming parity with Windows
  PowerShell.
- **The FFmpeg pin is a dated release tag, not a version number.**
  `packaging/Get-VendorFfmpeg.ps1` pins a specific BtbN release *tag*
  (`autobuild-YYYY-MM-DD-HH-MM`) rather than the mutable `latest` alias, so
  it will not silently start fetching a different build; it does need a
  deliberate bump periodically (BtbN does not keep old autobuild assets
  forever). The VC++ Redistributable fetch is intentionally *not* pinned
  (see that script's own header for why) and best-effort, matching
  `packaging/vendor/README.md`'s original design.
- **The csproj/`.iss` version stamp is applied to an ephemeral checkout
  only.** `packaging/Set-PackageVersion.ps1` never commits its edits back —
  by design, this workflow does not push code. A `versioned-release` run
  does *not* bump the version checked into `main`; if the project's
  ongoing `<Version>` should track releases, that is a separate, deliberate
  commit a maintainer makes, not something this workflow does on their
  behalf.
- **This workflow has still not been exercised end-to-end on GitHub
  Actions itself, though its packaging logic has been.** No authenticated
  `gh` credentials were available in the environment this reconciliation
  was performed in, so the actual `.yml` was never dispatched. What *was*
  run for real, directly, on a Windows machine: `Set-PackageVersion.ps1`
  → `Get-VendorFfmpeg.ps1` → `Get-VendorVcRedist.ps1` → the RID-scoped
  restore → `Publish-SceneForge.ps1` → `New-PortableZip.ps1` →
  `Verify-PortableBuild.ps1`, in that exact order, producing a genuine
  `PASS`. **Not** run: `choco install innosetup` + `ISCC.exe` (Inno Setup
  was not installed on that machine, and installing new software onto a
  real, non-ephemeral machine for this check was judged out of scope — see
  "Manual verification steps" below for what to check once the actual
  workflow runs on GitHub Actions' own ephemeral runner).

## Manual verification steps

This workflow was **not run in this session** — the environment has no
authenticated `gh`/GitHub credentials (`gh auth status` reports not logged
in), so it cannot be dispatched here. Verify it manually as follows:

### 1. Dispatch an `artifact-only` run

Via the GitHub UI: **Actions → Release → Run workflow**, leave `ref` as
`main` (or pick a branch), leave `build_type` as `artifact-only`, leave
`version` blank, click **Run workflow**.

Via `gh` CLI (from a machine with an authenticated, write-access account):

```sh
gh workflow run release.yml -f ref=main -f build_type=artifact-only
```

**Check:**
- The `build` job goes green through every step, in particular
  `Verify portable build` actually reports the diagnostics gate passed
  (check the step log for "PASS: all native-component diagnostics
  reported success"), not just that the process didn't crash.
- The `release` job does **not** run (skipped, since `build_type` is
  `artifact-only`).
- The run's artifact list contains `sceneforge-installer-0.0.0-artifact.<sha>`
  and `sceneforge-portable-0.0.0-artifact.<sha>`, each containing exactly
  one file, and the file *inside* is named
  `SceneForge-0.0.0-artifact.<sha>-win-x64-{setup.exe,portable.zip}`.
- Download and run the installer on a clean Windows machine/VM — genuinely
  clean, i.e. **without the Visual C++ Redistributable pre-installed**. It
  should install without any network access, create a Start Menu entry,
  and launch SceneForge successfully (all three startup diagnostics pass:
  ffmpeg, VC++ runtime, OpenCV).
- Unzip the portable artifact and launch `SceneForge.App.exe` directly from
  the extracted folder — it should run without a separate install step.
- Uninstall via **Settings → Apps** (or the Start Menu uninstall shortcut)
  and confirm it removes cleanly, leaving `%LOCALAPPDATA%\SceneForge`
  (the user's own projects/logs) untouched.

### 2. Dispatch a `versioned-release` run

```sh
gh workflow run release.yml -f ref=main -f build_type=versioned-release -f version=0.1.0
```

**Check:**
- Same `build` job checks as above, with `sceneforge-installer-0.1.0` /
  `sceneforge-portable-0.1.0` artifacts and filenames containing `0.1.0`.
- The `release` job runs and succeeds.
- A new tag `v0.1.0` exists on the repository, pointing at the commit
  resolved from `ref`.
- A GitHub Release titled `SceneForge v0.1.0` exists, marked as published
  (not draft), with both `SceneForge-0.1.0-win-x64-setup.exe` and
  `SceneForge-0.1.0-win-x64-portable.zip` attached as downloadable assets.
- Re-running the same workflow with the same `version` a second time fails
  at `gh release create` (tag already exists) rather than silently
  overwriting the existing Release — this is intentional; publish a new
  version to replace a broken release.

### 3. Negative-path checks

- Dispatch with `build_type=versioned-release` and `version` left blank —
  the `Validate inputs` step should fail immediately with a clear error,
  before any build, packaging, or Chocolatey install happens.
- Dispatch with `build_type=versioned-release` and `version=not-a-version`
  — same fast failure, with the specific semver-mismatch message.
- Confirm in the repository's `.github/workflows/` directory that no
  workflow file (this one or any other) declares `on: push` or
  `on: pull_request` for release/packaging purposes — only `ci.yml`'s
  pull-request gate does, and that job never publishes anything.

## Files this pipeline is built from

- `.github/workflows/release.yml` — the workflow itself.
- `packaging/scripts/Publish-SceneForge.ps1`, `New-PortableZip.ps1`,
  `Verify-PortableBuild.ps1`, `packaging/installer/SceneForge.iss` — Phase
  14's packaging pipeline (see `docs/PACKAGING_REPORT.md`), called
  unmodified except for the version stamp described below.
- `packaging/Get-VendorFfmpeg.ps1` (new) — fetches a pinned, LGPL-only
  ffmpeg build into `packaging/vendor/ffmpeg` for this workflow's use.
- `packaging/Get-VendorVcRedist.ps1` (new) — fetches the genuine Microsoft
  VC++ Redistributable into `packaging/vendor/vcredist`, best-effort.
- `packaging/Set-PackageVersion.ps1` (new) — stamps one version into both
  the csproj and the `.iss`, regression-tested by
  `packaging/Tests/Test-SetPackageVersion.ps1`.
- `packaging/ReleaseVersioning.psm1` (new) — the semantic-version
  validation rule for the `version` workflow input, regression-tested by
  `packaging/Tests/Test-ReleaseVersioning.ps1`.
- `packaging/output/` is git-ignored — it is always build output, never a
  committed artifact (see `.gitignore`).

**Removed during reconciliation**: `packaging/installer.iss` and
`packaging/Stage-Ffmpeg.ps1` — this workflow's original, independent
packaging pipeline, superseded entirely by Phase 14's more complete one
(see "Why this workflow calls Phase 14's scripts" above).
