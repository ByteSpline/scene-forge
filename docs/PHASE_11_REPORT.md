# Phase 11 Report — Local Project Persistence, Autosave, and Crash Recovery

Date: 2026-08-23

## Scope

Give SceneForge a durable, local, versioned project file so a session
survives an application restart, a crash, or a deliberate cancellation
without losing already-completed work. Concretely: a new
`SceneForge.Infrastructure.Persistence` engine (`SceneForgeProjectDocument`,
`IProjectStore`, `IAutosaveService`, `IProjectRecoveryService`,
`IStaleSourceDetector`, `ITempFileRegistry`) plus a small
`SceneForge.Infrastructure.Logging` rolling file logger, and an App-layer
seam (`IProjectPersistenceCoordinator`, `StartupRecoveryRunner`) that wires
every one of the eight workflow screens into it: autosave after each
completed stage, atomic (temp-file-then-replace) writes, a crash-safe
in-progress marker, stale-source (size/mtime) detection, a startup recovery
prompt, and app-owned temp-file registration/cleanup. Explicitly **not** in
scope: SQLite (the persisted schema — one root document plus a handful of
small nested records — never needed relational queries, so JSON stayed the
right choice; see Design summary), a deep mid-pipeline resume that
reconstructs `CleanClipExtractionResult`/`TimelinePlan`/`RenderPlan` from the
persisted schema (deliberately not persisted at that fidelity — see Design
summary), and any change to `SceneForge.Media`'s own processing algorithms
(this phase adds zero new lines to that project).

## Repository layout produced

```
src/SceneForge.Infrastructure/
  Persistence/
    ProjectStage.cs                          - Created/Imported/Analyzed/Reviewed/
                                                TimelinePlanned/RenderConfigured/Completed
    SourceFingerprint.cs                      - path + size + last-write-time
    ManualOverrideRecord.cs, ClipMetadataRecord.cs, RenderSettingsRecord.cs
    SceneForgeProjectDocument.cs              - the versioned JSON root (SchemaVersion = 1)
    ProjectPersistenceException.cs, ProjectCorruptedException.cs,
      ProjectSchemaVersionException.cs
    ProjectLayout.cs                          - every on-disk path, from one root directory
    AtomicFileWriter.cs                       - temp file -> flush -> File.Replace/Move
    ITempFileRegistry.cs, TempFileRegistry.cs - app-owned-root-scoped register/cleanup/sweep
    IProjectStore.cs, ProjectStore.cs         - versioned JSON read/write
    SourceFreshnessResult.cs, IStaleSourceDetector.cs, StaleSourceDetector.cs
    IAutosaveService.cs, AutosaveService.cs   - BeginStageAsync/CompleteStageAsync
    RecoverableProject.cs, IProjectRecoveryService.cs, ProjectRecoveryService.cs
  Logging/
    LogLevel.cs, IAppLogger.cs, RollingFileLogger.cs

src/SceneForge.App/
  Persistence/
    IProjectPersistenceCoordinator.cs, ProjectPersistenceCoordinator.cs
    StartupRecoveryRunner.cs
  Session/WorkflowSession.cs                 - + ProjectId (new Guid per project, reset with the rest)
  ViewModels/                                 - WelcomeImport/AnalysisProgress/SceneReview/
                                                TimelineSummary/ExportSettings/RenderProgress
                                                each now take IProjectPersistenceCoordinator and
                                                call BeginStageAsync/CheckpointAsync at the point
                                                a stage actually starts/completes
  App.xaml.cs                                - registers the persistence/logging services,
                                                runs StartupRecoveryRunner before the shell shows

tests/SceneForge.Infrastructure.Tests/        - new project, 43 tests
  Persistence/AtomicFileWriterTests.cs (6), ProjectStoreTests.cs (7),
    TempFileRegistryTests.cs (7), StaleSourceDetectorTests.cs (6),
    AutosaveServiceTests.cs (4), ProjectRecoveryServiceTests.cs (7)
  Logging/RollingFileLoggerTests.cs (6)
  TestSupport/TempDirectoryFixture.cs, SampleDocumentBuilder.cs

tests/SceneForge.App.Tests/
  TestSupport/FakeProjectPersistenceCoordinator.cs (new)
  ViewModels/*ViewModelTests.cs                - existing tests updated for the new constructor
                                                parameter; happy-path tests now also assert the
                                                expected BeginStage/Checkpoint calls actually
                                                happened (and, for cancellation, that they did not)
```

New project reference: `SceneForge.Infrastructure` now references
`SceneForge.Media` (previously it referenced only `SceneForge.Core` and had
no code beyond `ModuleInfo`) — see Design summary for why. No other
project's package/reference set changed; no new NuGet package was added
anywhere (`System.Text.Json` is already part of the .NET 8 SDK).

## Design summary

### JSON, not SQLite — the schema is a single small document, never queried

The phase brief allowed SQLite "unless evidence requires JSON" (worded the
other way around here, but the same question): does anything about this
data need relational storage — indexed lookups across many projects,
partial updates to one row, concurrent multi-writer access? No. A
SceneForge project is one document, read and written as a whole by one
process at a time, and the very first thing every autosave does is replace
the *entire* file (see "Atomic writes" below) — there is no query pattern
here JSON-plus-atomic-replace does not already satisfy, and no evidence was
produced during this phase that changed that. SQLite would have added a new
package dependency, a migration story, and a locking model for exactly zero
benefit over the versioned-JSON-plus-`SchemaVersion` approach every other
diagnostic export in this codebase already uses (`TransitionDetectionJsonWriter`,
`CleanClipJsonWriter` — Phases 6/7). `SceneForgeProjectDocument.SchemaVersion`
(currently `1`) is the sanctioned seam for a future migration if the shape
ever needs to change after real projects exist on disk.

### Reusing `SceneForge.Media`'s own domain types, not shadow DTOs

`SceneForgeProjectDocument` embeds `TransitionDetection`, `ClipScore`,
`RationalFrameRate`, `AnalysisProfile`, `AspectFitMode`, and
`TransitionDetectionProfileVersion` directly — the exact types
`SceneForge.Media` already produces — rather than a parallel shadow-DTO
layer that would need to be kept in sync by hand every time one of those
types changed. This is why `SceneForge.Infrastructure` now references
`SceneForge.Media` (previously the reverse of "true" — `Infrastructure` had
no code at all beyond a placeholder `ModuleInfo`, waiting for exactly this
phase). The dependency graph stays a strict DAG:
`Infrastructure -> Media -> Core`, and `App` depends on all three exactly as
before — `Media` gained zero new knowledge of `Infrastructure` or `App`.
The one thing deliberately **not** reused verbatim is `CleanClip` itself:
`ClipMetadataRecord` is a lean projection (index, accepted/rejected,
range, source scene index, `ClipScore`, cluster id) that drops
`CleanClip.Descriptor` (`PerceptualDescriptor`'s 64-bit hash plus two float
histograms) — that data only ever serves `VisualClusterer` inside a single
extraction run, is cheap to recompute from the source video, and persisting
it on every clip in every project would grow every project file for no
resume-time benefit.

### Atomic writes: temp file, flush, then `File.Replace`/`File.Move` — never a partial file on disk

`AtomicFileWriter.WriteAsync` is the single write path every persisted file
in this phase goes through (`ProjectStore.SaveAsync`, and — indirectly — the
in-progress marker files `AutosaveService` writes). It writes to
`<target>.tmp-<guid>` beside the real target, flushes it, then
`File.Replace` (target already exists) or `File.Move` (first write) into
place; a `finally` block deletes the leftover temp file whichever way the
write ended, and if a caller-supplied `ITempFileRegistry` was given, the
temp path is registered before the write starts and unregistered
afterward — so even if the process dies mid-write, the temp file is either
cleaned up immediately (normal path) or found and swept on the next startup
(`ITempFileRegistry.SweepOrphansAsync`, see below). `AtomicFileWriterTests`
covers both the new-file and replace-existing-file cases, that a
`writeBody` exception never creates or corrupts the target, and that the
temp file is registered/unregistered around the write.

### `IAutosaveService`'s two-call shape is what makes cancellation retain the last valid checkpoint, without a special case

`BeginStageAsync(projectId, stage)` writes an in-progress marker file;
`CompleteStageAsync(document)` is the *only* thing that ever writes a new
project checkpoint, and it clears that marker afterward. Every ViewModel
that runs a real pipeline stage (`AnalysisProgressViewModel`,
`RenderProgressViewModel`) calls `BeginStageAsync` right before starting
work and `CheckpointAsync` (the App-layer wrapper, see below) only after
that work has genuinely succeeded. If cancellation (or a crash) happens in
between, the marker is left in place but no new checkpoint was ever
written — the project file on disk is still exactly what the *previous*
completed stage left it as. "Cancellation must retain the last valid
checkpoint" (the phase brief's literal wording) falls out of this shape
directly; no code anywhere had to special-case "what if we were cancelled
right here." `AutosaveServiceTests.CancellationBetweenBeginAndComplete_PreviousCheckpointIsRetained`
and `AnalysisProgressViewModelTests`'s cancellation test both assert this
directly (the latter via `FakeProjectPersistenceCoordinator`'s recorded
`BegunStages`/`CheckpointedStages` lists — `Analyzed` is begun but never
checkpointed when the run is cancelled mid-flight).

### Stale-source detection is a fingerprint comparison, never a content hash — and says so

`SourceFingerprint` is exactly what the phase brief asked for: path, size,
last-write-time — not a checksum of a potentially multi-gigabyte video file
(CLAUDE.md rule 6/7's "no full-video buffering" extends naturally to "no
full-video hashing" for a cheap staleness check). `IStaleSourceDetector.CheckFreshness`
returns one of three named, explainable states (`Fresh`/`Missing`/`Changed`)
with a human-readable `Message`, never a bare boolean (CLAUDE.md rule 10) —
a caller can tell a user exactly why a source is considered stale rather
than just that it is.

### The in-progress marker plus the checkpoint file together are the whole recovery story

`ProjectRecoveryService.ScanForInterruptedProjectsAsync` looks for exactly
one signal: a project directory whose in-progress marker file still exists
(i.e. some `BeginStageAsync` was never followed by a matching
`CompleteStageAsync`/marker deletion — the process did not shut down
cleanly while working on it). It reports the project's `LastCheckpoint`
(the most recent stage that *did* complete — `null` only if the process
died before the very first checkpoint) alongside `InterruptedStage`/
`InterruptedAtUtc` read from the marker itself. A checkpoint file that
fails to load (corrupted — see Self-review findings and
`ProjectRecoveryServiceTests.ScanForInterruptedProjectsAsync_CorruptedCheckpointFile_StillReportsInterruptedStage`)
still surfaces the project with `LastCheckpoint = null` rather than being
silently dropped from the scan — an unreadable checkpoint is itself
evidence worth surfacing, not a reason to pretend nothing happened.

### A resumed project always re-enters at Welcome/Import, never mid-pipeline

`StartupRecoveryRunner` (App layer) offers to resume a found interrupted
project via `IDialogService.ShowConfirmation`, and if accepted, restores
`WorkflowSession`'s scalar state (analysis profile, output frame rate,
shuffle seed, fit mode/resolution/output path) plus the source paths — but
lands the user back on Welcome/Import, not at whatever screen the
interrupted stage implies. This was a deliberate scope decision, not an
oversight: `SceneForgeProjectDocument` does not (and, per the phase brief's
own field list, was never asked to) persist `CleanClipExtractionResult`
with full `PerceptualDescriptor`s, a built `TimelinePlan`, or a `RenderPlan` —
only detections, lean clip metadata, manual overrides, the seed, and render
settings. Fabricating a deeper resume (jumping straight to Scene Review,
say) would mean either silently re-deriving those heavier artifacts from
the lean persisted data (not actually possible for `PerceptualDescriptor`,
which the schema intentionally drops) or claiming a fidelity the persisted
schema does not back up. Landing on Welcome/Import and letting every later
stage recompute its own output — the same deterministic pipeline a fresh
run already goes through — is the honest choice. What recovery *does* save
the user: before landing, `StartupRecoveryRunner` re-runs
`IStaleSourceDetector.CheckFreshness` against each recorded source, and for
a source that is still fresh, re-probes it immediately via the same
`IFfprobeService` every screen already uses — so a user resuming an
unmodified source lands on Welcome/Import with both files already probed
and `ContinueCommand` immediately enabled, rather than needing to re-browse
anything. A changed or missing source instead surfaces a named
`IDialogService.ShowError` explaining exactly why (never silently trusting
stale probed data).

### `ITempFileRegistry` never deletes outside its own root — enforced, not just documented

Every path passed to `Register` is checked against `RootDirectory` (with a
trailing-separator-normalized prefix comparison, so `...\Temp2` can never
be mistaken for being inside `...\Temp`) and throws if it is not underneath
it; every path considered for deletion (`CleanupAsync`, `SweepOrphansAsync`)
is silently skipped rather than deleted if it somehow is not (defense in
depth — `Register`'s own guard should make this unreachable in practice,
but cleanup code is exactly the code CLAUDE.md rule 11 wants held to the
highest bar). The registry persists its own manifest (`registry.json`,
inside the same root) so a registered-but-not-yet-cleaned-up path survives
a process restart; `SweepOrphansAsync` (run once at startup, see
`StartupRecoveryRunner`) additionally deletes any file directly under the
root that is not in that manifest at all — the case where a process died
before it ever got to register or clean up something it wrote.
`TempFileRegistryTests` covers the outside-root guard, cleanup, orphan
sweeping (including that a still-registered file is left alone), and that
the manifest survives a fresh `TempFileRegistry` instance pointed at the
same root.

### Log rotation is a size-triggered rotate-and-trim, bounded on file count

`RollingFileLogger` writes to one active `sceneforge.log`; once it crosses
`maxFileSizeBytes` (5 MB by default), the active file is renamed to a
timestamped-plus-guid name (the guid suffix avoids a same-millisecond
collision under rapid rotation — see Self-review findings) and a fresh
`sceneforge.log` starts, and only the `maxRetainedFiles` (10 by default)
most-recently-modified rotated files are kept — anything older is deleted
on the next rotation. This is CLAUDE.md rule 6's "no unbounded cache"
applied to log files specifically: log volume can never grow without
bound, regardless of how long the application runs.

## Self-review findings

| Area | Finding | Resolution |
|---|---|---|
| `RationalFrameRate` silently deserialized to `Undefined` (0/0) through plain `System.Text.Json` reflection | `RationalFrameRate` is a `readonly record struct` whose `Numerator`/`Denominator` are get-only, assigned only by its constructor. `System.Text.Json`'s reflection-based converter for a **struct** never invokes a parameterized constructor the way it does for classes/records — it always starts from `default(T)` and assigns settable properties, and here there are none to assign, so every round trip through the plain default options produced `RationalFrameRate.Undefined` with no exception at all. This is a real, previously-latent bug: `RationalFrameRate` had only ever been JSON-*written* before this phase (`RenderPlanBuilder`'s diagnostic exports are one-way), so nothing had ever round-tripped it through a reader until `ProjectStoreTests.SaveAsync_ThenLoadAsync_RoundTripsEveryField` did. Caught by that test failing (`Expected: 30/1, Actual: undefined`), exactly the discipline CLAUDE.md rule 8 asks for. | Added `ProjectStore.RationalFrameRateConverter`, a thin `JsonConverter<RationalFrameRate>` that serializes via `.ToString()` and deserializes via the type's own `.Parse()` (both already existed, already exercised by other tests) — a JSON string like `"30/1"` rather than an object STJ would need constructor-matching for. Verified: the round-trip test (and the rest of `ProjectStoreTests`) passes. |
| Log rotation could collide on rapid successive rotations | The first implementation named a rotated file `sceneforge-yyyyMMdd-HHmmssfff.log` (millisecond precision). `RollingFileLoggerTests.Log_RotationBeyondRetentionCap_DeletesOldestRotatedFiles` (a tight loop forcing many rotations with a tiny `maxFileSizeBytes`) could rotate twice within the same millisecond on fast hardware, and `File.Move` throws if the destination already exists — a real, if rare, source of an unhandled `IOException` during logging in production. | Appended a `Guid.NewGuid():N}` suffix to the rotated file name, guaranteeing uniqueness regardless of clock resolution or rotation speed. |
| `dotnet format` line-ending fixes | Same class of issue every prior phase's report has recorded for itself — every new file in this phase was authored with `\n`; `.editorconfig` pins `end_of_line = crlf`. | `dotnet format SceneForge.sln` (apply mode), then `--verify-no-changes` confirmed clean; `git status` afterward showed only this phase's own new/changed files touched. |

All three were caught before this report was written — the first by a
failing round-trip test (not by inspection; `RationalFrameRate`'s shape
gave no visible reason to suspect its JSON behavior was broken), the second
by a stress-style rotation test, and the third by the formatting gate
CLAUDE.md rule 13 requires.

## Test inventory (new this phase)

**`SceneForge.Infrastructure.Tests`** (new project) — 43 tests:

- **AtomicFileWriterTests** (6) — new file created with written content,
  existing file replaced atomically, no leftover temp file after success,
  a `writeBody` exception leaves the target file never-created (and, for an
  already-existing target, unmodified) with the temp file cleaned up, and
  the temp path is registered during the write and unregistered after.
- **ProjectStoreTests** (7) — a fully-populated document (detections, clip
  metadata, manual overrides, render settings) round-trips every field
  exactly, a minimal document round-trips with every optional field `null`,
  saving twice replaces the first checkpoint, a missing file throws
  `FileNotFoundException`, an empty file / malformed JSON / valid-JSON-
  missing-required-fields all throw `ProjectCorruptedException`, and an
  unsupported `SchemaVersion` throws `ProjectSchemaVersionException` naming
  both the found and expected version.
- **TempFileRegistryTests** (7) — registering a path outside the root
  throws, cleanup deletes registered files and clears the registry (and
  tolerates a file already gone), unregister removes an entry, sweeping
  orphans deletes an unregistered file while leaving a registered one
  alone, the manifest survives a fresh instance pointed at the same root,
  and sweeping never touches a sibling directory outside the root.
- **StaleSourceDetectorTests** (6) — capturing a missing file throws,
  capturing an existing file matches its real `FileInfo`, and freshness
  checks correctly report Fresh/Missing/size-Changed/timestamp-Changed.
- **AutosaveServiceTests** (4) — `BeginStageAsync` writes the marker,
  `CompleteStageAsync` writes the checkpoint and clears the marker,
  `LastModifiedUtc` is stamped to "now," and — the cancellation-retention
  case — beginning a second stage without completing it leaves the first
  stage's checkpoint on disk untouched with the marker still present.
- **ProjectRecoveryServiceTests** (7) — no projects root returns empty, a
  cleanly-completed project (marker cleared) is not reported, an
  interrupted project with a prior checkpoint returns both stages
  correctly, a project interrupted before its first-ever checkpoint returns
  `LastCheckpoint = null`, a corrupted checkpoint file still reports the
  interrupted stage (with `LastCheckpoint = null`), and `DiscardAsync`
  removes only the marker, never the checkpoint file.
- **RollingFileLoggerTests** (6) — a written line contains the level and
  message, an exception's details are included, exceeding the size
  threshold rotates to a timestamped file (leaving a fresh active file),
  rotation beyond the retention cap deletes the oldest rotated files down
  to the cap, and non-positive `maxFileSizeBytes`/`maxRetainedFiles` both
  throw.

**`SceneForge.App.Tests`** — the existing 52 tests were updated (every
constructor call for `WelcomeImportViewModel`, `AnalysisProgressViewModel`,
`SceneReviewViewModel`, `TimelineSummaryViewModel`, `ExportSettingsViewModel`,
and `RenderProgressViewModel` now passes a `FakeProjectPersistenceCoordinator`)
and six of them gained new assertions against that fake's recorded
`BegunStages`/`CheckpointedStages`/`FinalizeCallCount`, directly verifying
this phase's new integration points rather than only the pre-existing
behavior: video import checkpoints `Imported`, analysis begins and
checkpoints `Analyzed` on success (and begins-but-never-checkpoints on
cancellation), Scene Review's Continue checkpoints `Reviewed`, Timeline
Summary's Continue checkpoints `TimelinePlanned`, Export Settings' Continue
checkpoints `RenderConfigured`, and a successful render begins and
checkpoints `Completed` and calls `FinalizeAsync` exactly once. Total test
count is unchanged (52) — no new `[Fact]` methods were added, only
assertions within existing ones — since every scenario needing its own new
test case already existed as its own method.

Verified via `dotnet test SceneForge.sln` in both Debug and Release: the
Phase 10 baseline was 537 total. This phase's full-solution total is **580**
(`SceneForge.Core.Tests` 1, `SceneForge.Infrastructure.Tests` 43 new,
`SceneForge.App.Tests` 52, `SceneForge.Media.Tests` 484 — 474 passed + 10
pre-existing real-binary skips, unrelated to this phase), all passing in
Release. One isolated, non-reproducing flake was observed in the Debug run
only, in an unrelated pre-existing `SceneForge.Media.Tests` case
(`TransitionDetectorTests.DetectAsync_ReportsProgressForEachAnalyzedFramePair`)
— re-run individually it passed immediately, and it is the same class of
timing-sensitive pre-existing flake Phase 10's own report already recorded
for a different case in this same test project; nothing in this phase
touches `SceneForge.Media` at all (zero files changed in that project this
phase), so it cannot be a regression this phase introduced.

## Compliance notes against CLAUDE.md

- **Rule 1-2** (native WPF, no Electron/web/cloud/telemetry/runtime
  network): satisfied — every persisted file is a local JSON document under
  `%LOCALAPPDATA%\SceneForge`, written via plain `System.Text.Json` and
  `System.IO`; no network call, no new package dependency anywhere.
- **Rule 3** (FFmpeg/FFprobe/OpenCvSharp basis): not implicated — this phase
  adds no new media processing. The one place persistence touches a real
  media call is `StartupRecoveryRunner` re-probing a fresh, recovered
  source via the existing `IFfprobeService` — the same abstraction every
  other screen already uses, not a new one.
- **Rule 4** (clean architecture): `SceneForge.Infrastructure` now depends
  on `SceneForge.Media` (for its domain types — see Design summary) and
  `SceneForge.Core`, never the other direction; `SceneForge.Media` gained
  zero new references or knowledge of persistence. `SceneForge.App`'s
  `Persistence/` namespace is the only place `WorkflowSession` is read to
  build a `SceneForgeProjectDocument` — no `SceneForge.Infrastructure` type
  leaks into a View or is bound to directly from XAML.
- **Rule 5** (async cancellation/cooperative shutdown): every
  `IProjectPersistenceCoordinator` method accepts and threads a
  `CancellationToken`; `AnalysisProgressViewModel`/`RenderProgressViewModel`
  pass the same token their pipeline stage itself uses (Phase 10's existing
  `CancellationTokenSource`), so a cancelled analysis/render stops both the
  real work *and* never reaches its `CheckpointAsync` call — see Design
  summary's "cancellation retains the last valid checkpoint" and the
  dedicated tests proving it (not merely a token being passed somewhere,
  the same bar Phase 10's report held itself to).
- **Rule 6-7** (bounded memory/concurrency, no full-video buffering):
  `ITempFileRegistry` never grows unbounded (files are cleaned up on
  completion and swept for orphans at startup); `RollingFileLogger` caps
  both individual log file size and the total count of retained rotated
  files; `SourceFingerprint` is a cheap size/mtime pair, never a full-file
  hash of a video. Nothing in this phase reads a video frame into memory at
  all.
- **Rule 8** (test-first): every new `SceneForge.Infrastructure.Persistence`/
  `Logging` component has dedicated tests (43, see Test inventory) written
  and run before this report, and the Self-review section documents one
  real bug (`RationalFrameRate`'s JSON round-trip) a test caught directly —
  exactly the discipline this rule asks for, not an incidental catch.
- **Rule 9** (benchmark every optimization): not applicable this phase —
  new functionality with no prior version to diff against, the same
  precedent every phase since 6 has recorded for itself. No performance-
  sensitive optimization was made that needed before/after evidence; the
  bounded-retention choices (5 MB/10 files for logs) are safety bounds, not
  tuned optimizations.
- **Rule 10** (never claim absolute/opaque correctness): `SourceFreshnessResult`
  reports one of three named, explained states, never a bare "stale"
  boolean; `ProjectCorruptedException`/`ProjectSchemaVersionException` name
  exactly what was wrong (a JSON parse failure, a missing field, a specific
  version mismatch) rather than a generic "load failed."
- **Rule 11-12** (preserve user files, output to new path only):
  `ITempFileRegistry` refuses to register or delete anything outside its
  own app-owned root (enforced by `EnsureWithinRoot`/`DeleteIfWithinRoot`,
  not merely documented — see `TempFileRegistryTests`); every persisted
  path (`ProjectLayout`) is derived from `%LOCALAPPDATA%\SceneForge`, never
  from a user-supplied source/output path, so a project checkpoint can
  never collide with or overwrite an input file or a rendered output. No
  change to the existing render-output rule (`ExportSettingsViewModel`
  still only ever writes to a user-chosen `SaveFileDialog` path).
- **Rule 13** (format/build/tests before ending): `dotnet format SceneForge.sln --verify-no-changes`
  clean (after the one round of `ENDOFLINE` fixes recorded in Self-review
  findings); Debug and Release both build with 0 warnings/errors across all
  ten projects (including the two new ones,
  `SceneForge.Infrastructure.Tests` and this phase's additions to
  `SceneForge.Infrastructure`/`SceneForge.App`); both configurations pass
  every test except the one isolated, non-reproducing, pre-existing
  `SceneForge.Media.Tests` flake recorded above (Release run was fully
  green).
- **Rule 14** (update docs on behavior change): this report is that update.
  `docs/ARCHITECTURE_DECISIONS.md` needed no textual change — Decisions 2
  ("UI stays separate from core logic/processing") and 6 ("preserve user
  files, outputs to a new path") already state exactly the constraints this
  phase's design honors; no new decision was introduced that isn't already
  covered by the existing eight.
- **Rule 15** (don't advance while criteria fail): this phase's own
  criteria — versioned JSON persistence with atomic writes, autosave after
  every completed stage, a crash-safe in-progress marker, stale-source
  detection, bounded log rotation, an app-owned temp-file registry with
  startup sweep, a real startup recovery prompt, cancellation proven to
  retain the last checkpoint, corruption/recovery tests, and this report —
  are all met as of this report, with the "resume lands on Welcome/Import
  rather than mid-pipeline" scope decision named explicitly under
  Outstanding rather than hidden.

## Known limitations / Outstanding for later phases

- **A resumed project re-enters at Welcome/Import, not the exact
  interrupted screen.** See Design summary — the persisted schema
  deliberately does not carry `CleanClipExtractionResult`'s full
  `PerceptualDescriptor`s, a built `TimelinePlan`, or a `RenderPlan`, so a
  deeper jump (straight to Scene Review, say) cannot be backed by what is
  actually on disk without either recomputing those artifacts (which is
  exactly what re-entering at Welcome/Import and letting the pipeline run
  again already does, deterministically) or fabricating a fidelity that
  was never persisted. A future phase wanting a true mid-pipeline resume
  would need to decide whether to widen the persisted schema to include
  those heavier artifacts (a real CLAUDE.md rule 9 tradeoff: bigger project
  files vs. faster resume) or accept re-running the affected stage(s) as
  this phase's design already does.
- **`StartupRecoveryRunner` resumes at most one interrupted project per
  launch.** If a scan finds more than one (unusual — normally there is only
  one active `WorkflowSession` at a time), only the first one the user
  chooses to resume is actually restored into the session this launch; any
  others keep their markers and are offered again on a subsequent launch.
  `WorkflowSession` is a single-project-at-a-time model throughout this
  codebase (Phase 10), so this matches that existing shape rather than
  introducing a new one.
- **No cryptographic integrity check (checksum/hash) over a saved project
  file** — corruption detection is JSON-parse-plus-required-fields
  validation (`ProjectCorruptedException`), which catches truncation,
  malformed syntax, and a missing required field, but not, say, a bit-flip
  that happens to still produce syntactically valid JSON with a plausible-
  looking value in every field. CLAUDE.md rule 10 applies here too: this
  report claims exactly the corruption detection that was built and
  tested, not a stronger guarantee.
- **`RollingFileLogger`'s 5 MB/10-file bounds are untuned heuristic
  defaults**, the same category of caveat this codebase already attaches to
  `CleanClipScoringOptions`/`ThumbnailCacheService`'s cache-size bound
  (Phases 6/10) — not measured against real-world log volume.
- **No packaging/installer** — still out of scope, as every prior phase's
  report has noted for itself.
