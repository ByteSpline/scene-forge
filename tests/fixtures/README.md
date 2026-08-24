# Synthetic fixture matrix

`manifest.json` in this directory is the **compact, committed ground truth**
for the accuracy/benchmark fixture matrix: for each of the 32 fixtures, its
id, its `FixtureGroup`, its source duration, and the exact `[start, end]`
window (in seconds) any expected transition should be detected within. It
contains no file paths and no video data - it is regenerated, in full, by
running the `generate` command below, and is small enough to read and
diff directly in a pull request.

The actual `.mp4` fixture files are **not** committed. They are a few
seconds of ffmpeg-generated test-pattern video each (`testsrc2`, `smptebars`,
`rgbtestsrc`, `pal75bars`, `color`), deterministic to rebuild from the
command line that generated them, and CI never needs to read a committed
copy (it rebuilds them itself - see the `accuracy-regression` CI job). They
are written to the gitignored `tests/fixtures/generated/` directory.

## What is in the matrix

Ground truth for `SceneForge.Media.Detection.TransitionDetector`, built by
`SceneForge.Accuracy.Fixtures.SyntheticFixtureCatalog` (the single source of
truth also used by `tests/SceneForge.Media.Tests/Detection/Fixtures/TransitionDetectorFixtureTests.cs`):

- **8 transition groups** (2 independent-content variants each): HardCut,
  FadeToBlack, FadeFromBlack, Dissolve, Flash, BlurTransition,
  ZoomTransition, DirectionalSwipe.
- **4 distractor groups** (2 variants each, zero expected transitions - any
  detection here is a false positive by construction): BlackHold,
  FrozenFrame, StaticShot, RapidMotion.
- **3 format-robustness groups**, each re-running the hard-cut signature
  under an input-format quirk: VariableFrameRate (2 variants), MixedResolution
  (160x90/320x180/640x360), Rotated (90/180/270 degrees).

## Reproducing it

Requires real `ffmpeg.exe`/`ffprobe.exe` (never committed to this repo -
see `.gitignore`). Copy them, and their dependent DLLs, into
`accuracy/SceneForge.Accuracy/bin/<Debug|Release>/net8.0/tools/ffmpeg/`
(same convention `RealFfmpegAvailability`/the app's own
`FfmpegToolLocator` use), then:

```
dotnet run --project accuracy/SceneForge.Accuracy -- generate --output tests/fixtures/generated --manifest tests/fixtures/manifest.json
```

This is also exactly what `evaluate`/`gate` do internally (into a private
temp directory instead) before analyzing the matrix - see
`docs/ACCURACY_REPORT.md` for the full command reference.

## Limitations

- 160x90-scale, 3-second, `libx264 -preset ultrafast` synthetic clips are a
  controlled, repeatable proxy for real footage, not real footage. See
  `docs/ACCURACY_REPORT.md` for what this does and does not prove.
- The VariableFrameRate fixture is a best-effort construction (two
  differently-declared-rate segments concatenated with `-fps_mode vfr`) -
  it is not a capture from a real variable-frame-rate camera/screen
  recording.
