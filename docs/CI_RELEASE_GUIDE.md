# CI Release Guide

This document describes `.github/workflows/release.yml`: the manual pipeline
that builds, tests, and packages SceneForge, and optionally publishes a
tagged GitHub Release. It is separate from `.github/workflows/ci.yml`, which
is the automatic pull-request gate (restore/build/test/format/accuracy
regression) and is unaffected by anything in this document.

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
| `version` | only for `versioned-release` | semantic version, e.g. `1.4.0` or `1.4.0-rc.1` | Used as the release tag (`vX.Y.Z`), the installer/portable filenames, and the assembly `Version` MSBuild property. Ignored for `artifact-only` runs. |

The first step of the `build` job (`Validate inputs`) fails fast if
`build_type` is `versioned-release` and `version` is missing or not a valid
semantic version (`^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z-.]*)?$`).

For `artifact-only` runs, the pipeline uses an internal version tag of
`0.0.0-artifact.<7-char-commit-sha>` for filenames and MSBuild `Version` —
this keeps repeated artifact-only runs from colliding and makes it obvious
which commit an artifact came from, without implying it is a real release.

## What the pipeline does

Runs on `windows-latest`, matching the "native Windows WPF on .NET 8"
architecture (CLAUDE.md rule 1). Two jobs:

### Job 1 — `build` (always runs)

1. **Validate inputs** — see above.
2. **Checkout** the requested `ref`.
3. **Resolve build metadata** — reads back the actual checked-out commit SHA
   (not `github.sha`, which reflects the ref that triggered the dispatch,
   which is not necessarily the `ref` input) and computes the version tag.
4. **Setup .NET 8**, **Restore**, **Build** (`Release` configuration) — same
   commands as `ci.yml`.
5. **Test** — `dotnet test SceneForge.sln --no-build --configuration Release`
   for every test project in the solution. A failing test fails the whole
   run; no later step executes (CLAUDE.md rule 15). Test results (`.trx`)
   are uploaded as a workflow artifact regardless of outcome.
6. **Install ffmpeg** via Chocolatey — needed only to obtain real ffmpeg/
   ffprobe binaries to bundle into the app; not a runtime dependency of the
   built product.
7. **Publish self-contained build** —
   `dotnet publish src/SceneForge.App/SceneForge.App.csproj -c Release -r win-x64 --self-contained true`
   into `publish\SceneForge.App`. For `versioned-release`, `-p:Version=<version>`
   is passed so the built assembly carries the real version.
8. **Stage ffmpeg into publish output** — copies the *real* ffmpeg/ffprobe
   binaries (not the Chocolatey PATH shims) into
   `publish\SceneForge.App\tools\ffmpeg`, using
   [`packaging/Stage-Ffmpeg.ps1`](../packaging/Stage-Ffmpeg.ps1). This exists
   because `FfmpegToolLocator` resolves ffmpeg/ffprobe strictly relative to
   the running app and deliberately never searches `PATH` (see
   `src/SceneForge.Media/Tooling/FfmpegToolLocator.cs`), and because
   `tools/ffmpeg/` is deliberately excluded from source control (`.gitignore`).
   The script mirrors the binary-resolution technique already proven in
   `ci.yml`'s `accuracy-regression` job (a Chocolatey ffmpeg install can be
   either a self-contained static build or a shared build needing sibling
   DLLs — the script probes and handles both). OpenCvSharp's native
   binaries need no equivalent step: they are ordinary NuGet runtime assets
   and `dotnet publish` places them automatically.
9. **Build installer** — installs Inno Setup via Chocolatey and compiles
   [`packaging/installer.iss`](../packaging/installer.iss) against the
   publish output, producing
   `packaging/output/SceneForge-<version>-win-x64-setup.exe`. The installer
   only copies files already present in the publish output (including the
   vendored ffmpeg and OpenCvSharp binaries); it downloads nothing at
   install time, and the installed app makes no network calls, matching
   CLAUDE.md's offline/no-telemetry rules.
10. **Zip portable build** — `Compress-Archive`s the same publish output into
    `packaging/output/SceneForge-<version>-win-x64-portable.zip`, a
    run-in-place alternative to installing.
11. **Upload installer artifact** / **Upload portable artifact** — both
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
  automatically-provided `secrets.GITHUB_TOKEN`.
- Anyone dispatching this workflow needs at least **write** access to the
  repository (required by GitHub to run `workflow_dispatch` at all).

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
- The `build` job goes green: Restore → Build → Test → Install ffmpeg →
  Publish → Stage ffmpeg → Build installer → Zip → both uploads all
  succeed.
- The `release` job does **not** run (skipped, since `build_type` is
  `artifact-only`).
- The run's artifact list contains `sceneforge-installer-0.0.0-artifact.<sha>`
  and `sceneforge-portable-0.0.0-artifact.<sha>`, each containing exactly
  one file.
- Download and run the installer on a clean Windows machine/VM: it should
  install without any network access, create a Start Menu entry, and launch
  SceneForge successfully (ffmpeg/ffprobe resolve without error — this
  exercises `FfmpegToolLocator` against the real bundled binaries).
- Unzip the portable artifact and launch `SceneForge.App.exe` directly from
  the extracted folder — it should run without a separate install step.
- Uninstall via **Settings → Apps** (or the Start Menu uninstall shortcut)
  and confirm it removes cleanly.

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
  before any checkout, build, or Chocolatey install happens.
- Dispatch with `build_type=versioned-release` and `version=not-a-version`
  — same fast failure, with the specific semver-mismatch message.
- Confirm in the repository's `.github/workflows/` directory that no
  workflow file (this one or any other) declares `on: push` or
  `on: pull_request` for release/packaging purposes — only `ci.yml`'s
  pull-request gate does, and that job never publishes anything.

## Files added for this pipeline

- `.github/workflows/release.yml` — the workflow itself.
- `packaging/installer.iss` — Inno Setup 6 script; takes `PublishDir`,
  `OutputDir`, and `AppVersion` as command-line defines so it never hardcodes
  a path back into the repo. Installs to `Program Files\SceneForge`
  (requires admin, standard for a per-machine install), creates Start Menu
  and optional desktop shortcuts, and registers a normal Windows uninstaller.
- `packaging/Stage-Ffmpeg.ps1` — vendors real ffmpeg/ffprobe binaries (not
  Chocolatey's PATH shims) into a publish output, reusing the resolution
  technique already proven in `ci.yml`'s `accuracy-regression` job.
- `packaging/output/` is git-ignored — it is always build output, never a
  committed artifact (see `.gitignore`).
